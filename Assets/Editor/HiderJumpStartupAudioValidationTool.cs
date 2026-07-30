using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using StarterAssets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class HiderJumpStartupAudioValidationTool
{
    private const string ScenePath = "Assets/Scenes/Map_v2.unity";
    private const string RunningKey = "PropHunt.HiderJumpAudioValidation.Running";
    private const string CompletedKey = "PropHunt.HiderJumpAudioValidation.Completed";
    private const string SetupSummaryKey = "PropHunt.HiderJumpAudioValidation.Setup";
    private const string MarkerName = "HiderJumpStartupAudioValidation.run";
    private const string ResultName = "HiderJumpStartupAudioValidation.log";

    private static readonly List<string> Failures = new List<string>();
    private static readonly List<string> RuntimeNotes = new List<string>();
    private static int phase;
    private static double phaseStartedAt;
    private static PropTransformSystem hider;
    private static FirstPersonController movement;
    private static StarterAssetsInputs input;
    private static CharacterController characterController;
    private static float groundY;
    private static float startY;
    private static float maximumY;
    private static float maximumVerticalVelocity;
    private static float minimumVerticalVelocity;
    private static bool sawAirborne;
    private static bool sawLanding;
    private static bool jumpRequestConsumed;
    private static bool airJumpInjected;
    private static float airJumpVelocityBefore;
    private static Vector3 visualLocalStart;
    private static Vector3 visualRootOffsetStart;
    private static int takeoffCount;
    private static bool wasGrounded;

    static HiderJumpStartupAudioValidationTool()
    {
        EditorApplication.update -= TryAutoStart;
        EditorApplication.update += TryAutoStart;
        EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        if (SessionState.GetBool(RunningKey, false))
        {
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        }
    }

    [MenuItem("Tools/Prop Hunt/Validate Hider Jump And Startup Audio")]
    public static void RunAll()
    {
        if (Application.isPlaying || EditorApplication.isCompiling ||
            EditorApplication.isUpdating)
        {
            Debug.LogWarning(
                "HiderJumpAudioValidation: wait for Edit Mode and compilation to finish.");
            return;
        }

        RunSetupTwiceAndValidateStatic();
        Failures.Clear();
        RuntimeNotes.Clear();
        phase = 0;
        SessionState.SetBool(RunningKey, true);
        SessionState.SetBool(CompletedKey, false);
        EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        EditorApplication.EnterPlaymode();
    }

    private static void TryAutoStart()
    {
        if (Application.isPlaying || EditorApplication.isCompiling ||
            EditorApplication.isUpdating)
        {
            return;
        }

        string markerPath = Path.Combine(ProjectRoot, MarkerName);
        if (!File.Exists(markerPath))
        {
            return;
        }

        File.Delete(markerPath);
        RunAll();
    }

    private static void RunSetupTwiceAndValidateStatic()
    {
        HiderCompleteHUDSetupTool.SetupHiderJumpAndStartupAudioOnly();
        string firstHash = HashFile(ScenePath);
        HiderCompleteHUDSetupTool.SetupHiderJumpAndStartupAudioOnly();
        string secondHash = HashFile(ScenePath);

        List<string> failures = new List<string>();
        Require(firstHash == secondHash,
            "Setup pass 2 changed Map_v2; setup is not idempotent.", failures);

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        PropTransformSystem sceneHider = Object.FindObjectsOfType<PropTransformSystem>(true)
            .FirstOrDefault(item => item.playerRole == PlayerRole.Hider);
        Require(sceneHider != null, "Hider PropTransformSystem is missing.", failures);
        if (sceneHider != null)
        {
            FirstPersonController[] firstPerson =
                sceneHider.GetComponents<FirstPersonController>();
            Require(firstPerson.Count(item => item.enabled) == 1,
                "Hider must have exactly one enabled FirstPersonController.", failures);
            Require(sceneHider.GetComponents<ThirdPersonController>()
                        .All(item => !item.enabled),
                "A ThirdPersonController is also enabled on Hider.", failures);
            Require(sceneHider.GetComponents<SeekerFirstPersonController>()
                        .All(item => !item.enabled),
                "A SeekerFirstPersonController is also enabled on Hider.", failures);
            Require(sceneHider.GetComponent<CharacterController>() != null &&
                    sceneHider.GetComponent<CharacterController>().enabled,
                "Hider root CharacterController is missing or disabled.", failures);
            Require(sceneHider.GetComponents<Rigidbody>().Length == 0,
                "Hider root contains a Rigidbody.", failures);

            FirstPersonController controller = firstPerson.FirstOrDefault();
            Require(controller != null && controller.Gravity < 0f,
                "Hider gravity must be negative.", failures);

            Transform visualRoot = sceneHider.propVisualRoot;
            Require(visualRoot != null, "PropVisualRoot is missing.", failures);
            if (visualRoot != null)
            {
                Require(visualRoot.GetComponentsInChildren<Collider>(true).Length == 0,
                    "PropVisualRoot contains a Collider.", failures);
                Require(visualRoot.GetComponentsInChildren<Rigidbody>(true).Length == 0 &&
                        visualRoot.GetComponentsInChildren<Rigidbody2D>(true).Length == 0,
                    "PropVisualRoot contains a Rigidbody.", failures);
            }
        }

        string movementSource = File.ReadAllText(
            Path.Combine(ProjectRoot,
                "Assets/StarterAssets/FirstPersonController/Scripts/FirstPersonController.cs"));
        string propSource = File.ReadAllText(
            Path.Combine(ProjectRoot,
                "Assets/Scripts/PropHunt/PropTransformSystem.cs"));
        string bootstrapSource = File.ReadAllText(
            Path.Combine(ProjectRoot,
                "Assets/Scripts/PropHunt/Map2RuntimeBootstrap.cs"));
        string weaponAudioSource = File.ReadAllText(
            Path.Combine(ProjectRoot,
                "Assets/Scripts/PropHunt/SeekerWeaponPresentation.cs"));
        string antiCampAudioSource = File.ReadAllText(
            Path.Combine(ProjectRoot,
                "Assets/Scripts/PropHunt/HiderAntiCampAudioPresentation.cs"));

        Require(Count(movementSource, "_controller.Move(") == 1,
            "FirstPersonController must be the only direct CharacterController.Move owner.",
            failures);
        Require(!propSource.Contains("_characterController.Move("),
            "PropTransformSystem still calls CharacterController.Move directly.", failures);
        Require(movementSource.Contains("bool jumpRequested = _input.jump;") &&
                movementSource.Contains("_input.jump = false;") &&
                movementSource.Contains("if (jumpRequested &&"),
            "Jump request is not consumed as a one-shot.", failures);
        Require(movementSource.Contains("Physics.OverlapSphereNonAlloc") &&
                movementSource.Contains("hit.transform.IsChildOf(transform)"),
            "Ground probe does not exclude Hider-owned colliders.", failures);
        Require(movementSource.Contains("_verticalVelocity + Gravity * Time.deltaTime") &&
                movementSource.Contains("-_terminalVelocity"),
            "Gravity integration/terminal fall speed is not configured safely.", failures);
        Require(!propSource.Contains("AddBlockingColliderToPropVisual"),
            "Runtime disguise still adds a blocking collider to copied visuals.", failures);
        Require(!bootstrapSource.Contains("GenerateMusicClip") &&
                !bootstrapSource.Contains("_musicSource.Play()"),
            "Map2RuntimeBootstrap still generates or auto-plays placeholder music.",
            failures);
        Require(weaponAudioSource.Contains("PlayShotFeedback") &&
                weaponAudioSource.Contains("PlayOneShot") &&
                antiCampAudioSource.Contains("PlayOneShot"),
            "Event-driven weapon or Anti-Camp audio API was removed.", failures);

        AudioSource[] sceneAudio = Object.FindObjectsOfType<AudioSource>(true);
        Require(sceneAudio.All(source => !source.playOnAwake),
            "A scene AudioSource still has Play On Awake enabled.", failures);
        Require(sceneAudio.All(source => !source.loop),
            "A scene AudioSource still loops at startup.", failures);
        Require(CountEnabledListeners() == 1,
            $"Expected one active AudioListener, got {CountEnabledListeners()}.",
            failures);

        if (failures.Count > 0)
        {
            string failureText = string.Join(Environment.NewLine, failures);
            WriteResult("STATIC FAIL" + Environment.NewLine + failureText);
            throw new InvalidOperationException(
                "HiderJumpAudioValidation STATIC FAIL\n" + failureText);
        }

        string summary =
            $"Setup pass 1 hash={firstHash}{Environment.NewLine}" +
            $"Setup pass 2 hash={secondHash}{Environment.NewLine}" +
            "Static validation PASS: one movement owner, one-shot jump, negative " +
            "gravity, visual physics absent, placeholder startup music absent.";
        SessionState.SetString(SetupSummaryKey, summary);
        Debug.Log("[HiderJumpAudioValidation] " + summary);
    }

    private static void HandlePlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            Failures.Clear();
            RuntimeNotes.Clear();
            phase = 0;
            phaseStartedAt = EditorApplication.timeSinceStartup;
            Application.logMessageReceived -= CaptureRuntimeException;
            Application.logMessageReceived += CaptureRuntimeException;
            EditorApplication.update -= TickPlayValidation;
            EditorApplication.update += TickPlayValidation;
        }
        else if (change == PlayModeStateChange.EnteredEditMode &&
                 SessionState.GetBool(RunningKey, false))
        {
            EditorApplication.update -= TickPlayValidation;
            Application.logMessageReceived -= CaptureRuntimeException;
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
            SessionState.SetBool(RunningKey, false);

            string status = Failures.Count == 0 &&
                            SessionState.GetBool(CompletedKey, false)
                ? "PASS"
                : "FAIL";
            string result =
                "Hider jump/startup audio validation: " + status +
                Environment.NewLine +
                SessionState.GetString(SetupSummaryKey, string.Empty) +
                Environment.NewLine +
                string.Join(Environment.NewLine, RuntimeNotes) +
                (Failures.Count > 0
                    ? Environment.NewLine + "Failures:" + Environment.NewLine +
                      string.Join(Environment.NewLine, Failures)
                    : string.Empty);
            WriteResult(result);

            if (status == "PASS")
            {
                Debug.Log("[HiderJumpAudioValidation] PLAY MODE PASS\n" + result);
            }
            else
            {
                Debug.LogError("[HiderJumpAudioValidation] PLAY MODE FAIL\n" + result);
            }
        }
    }

    private static void TickPlayValidation()
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }

        double elapsed = EditorApplication.timeSinceStartup - phaseStartedAt;
        switch (phase)
        {
            case 0:
                if (elapsed < 1d) return;
                RunStartupAudioInventory();
                PrepareRuntimeActors();
                BeginSettlePhase(1);
                break;

            case 1:
                if (elapsed < 0.75d) return;
                BeginJump("Human");
                SetPhase(2);
                break;

            case 2:
                MonitorJump();
                if (elapsed < 3.2d) return;
                ValidateJump("Human", false);
                RuntimeNotes.Add(
                    $"Human jump PASS: startY={startY:F3}, maxY={maximumY:F3}, " +
                    $"finalY={hider.transform.position.y:F3}, " +
                    $"velocityRange=[{minimumVerticalVelocity:F3}, " +
                    $"{maximumVerticalVelocity:F3}], takeoffs={takeoffCount}, " +
                    $"requestConsumed={jumpRequestConsumed}.");
                BeginDisguisePhase();
                break;

            case 3:
                if (elapsed < 0.8d) return;
                BeginJump("Disguised");
                visualLocalStart = hider.CurrentPropVisualTransform.localPosition;
                visualRootOffsetStart =
                    hider.CurrentPropVisualTransform.position - hider.transform.position;
                SetPhase(4);
                break;

            case 4:
                MonitorJump();
                MonitorVisualAttachment();
                if (!airJumpInjected && sawAirborne && elapsed > 0.75d)
                {
                    airJumpVelocityBefore = movement.VerticalVelocity;
                    input.jump = true;
                    airJumpInjected = true;
                    RuntimeNotes.Add(
                        $"Air Space injected once at velocity " +
                        $"{airJumpVelocityBefore:F3} m/s.");
                }

                if (elapsed < 3.2d) return;
                ValidateJump("Disguised", true);
                Require(movement.VerticalVelocity <= 0.1f,
                    "Air Space added a second upward jump.", Failures);
                RuntimeNotes.Add(
                    $"Disguised jump/hold/air-Space PASS: startY={startY:F3}, " +
                    $"maxY={maximumY:F3}, finalY={hider.transform.position.y:F3}, " +
                    $"visualLocal={hider.CurrentPropVisualTransform.localPosition}, " +
                    $"takeoffs={takeoffCount}.");
                RunPropSwitchChecks();
                BeginWallDetachPhase();
                break;

            case 5:
                MonitorJump();
                MonitorVisualAttachment();
                if (elapsed < 3.2d) return;
                Require(!hider.IsWallAttached,
                    "Wall traversal remained attached after detach.", Failures);
                Require(minimumVerticalVelocity < -0.5f,
                    "Gravity did not resume after leaving wall traversal.", Failures);
                Require(Mathf.Abs(hider.transform.position.y - groundY) < 0.2f,
                    "Hider did not land after wall detach.", Failures);
                RuntimeNotes.Add(
                    $"Wall detach PASS: attached={hider.IsWallAttached}, " +
                    $"velocityRange=[{minimumVerticalVelocity:F3}, " +
                    $"{maximumVerticalVelocity:F3}], finalY=" +
                    $"{hider.transform.position.y:F3}.");
                Finish();
                break;
        }
    }

    private static void RunStartupAudioInventory()
    {
        AudioSource[] sources = Resources.FindObjectsOfTypeAll<AudioSource>()
            .Where(source => source != null && source.gameObject.scene.IsValid())
            .OrderBy(source => HierarchyPath(source.transform))
            .ToArray();
        StringBuilder inventory = new StringBuilder();
        inventory.AppendLine(
            $"Startup AudioSource inventory ({sources.Length} sources):");
        foreach (AudioSource source in sources)
        {
            string clipPath = source.clip != null
                ? AssetDatabase.GetAssetPath(source.clip)
                : "<none>";
            inventory.AppendLine(
                $"- {HierarchyPath(source.transform)} | " +
                $"clip={(source.clip != null ? source.clip.name : "<none>")} | " +
                $"asset={clipPath} | playOnAwake={source.playOnAwake} | " +
                $"loop={source.loop} | isPlaying={source.isPlaying} | " +
                $"volume={source.volume:F2} | pitch={source.pitch:F2} | " +
                $"spatialBlend={source.spatialBlend:F2} | " +
                $"active={source.gameObject.activeInHierarchy} | " +
                $"enabled={source.enabled} | caller={ResolveAudioCaller(source)}");
        }

        RuntimeNotes.Add(inventory.ToString().TrimEnd());
        Require(sources.All(source => !source.isPlaying ||
                                      source.GetComponent<
                                          PersistentMusicManager>() != null),
            "A non-music AudioSource is already playing at scene startup.",
            Failures);

        AudioSource music = sources.FirstOrDefault(
            source => source.gameObject.name == "Map2MusicPlayer");
        Require(music != null, "Runtime Map2MusicPlayer was not created.", Failures);
        Require(music != null && music.clip == null && !music.playOnAwake &&
                !music.loop && !music.isPlaying,
            "Map2MusicPlayer still has a clip, loop, Play On Awake, or playback.",
            Failures);
        Require(CountEnabledListeners() == 1,
            $"Runtime expected one AudioListener, got {CountEnabledListeners()}.",
            Failures);
    }

    private static void PrepareRuntimeActors()
    {
        Map2RuntimeBootstrap.CloseRuntimeMenusForGameplay();
        Time.timeScale = 1f;
        foreach (SeekerAIController seeker in
                 Object.FindObjectsOfType<SeekerAIController>(true))
        {
            seeker.enabled = false;
        }

        hider = Object.FindObjectsOfType<PropTransformSystem>(true)
            .FirstOrDefault(item => item.playerRole == PlayerRole.Hider);
        Require(hider != null, "Runtime Hider was not found.", Failures);
        if (hider == null)
        {
            Finish();
            return;
        }

        movement = hider.GetComponent<FirstPersonController>();
        input = hider.GetComponent<StarterAssetsInputs>();
        characterController = hider.GetComponent<CharacterController>();
        Require(movement != null && input != null && characterController != null,
            "Runtime movement/input/CharacterController component is missing.",
            Failures);
        if (movement == null || input == null || characterController == null)
        {
            Finish();
            return;
        }

        hider.SetGameplayInputLocked(false);
        hider.ResetToHumanForRoleSelection();
        PlaceHiderOnGround();
    }

    private static void PlaceHiderOnGround()
    {
        movement.SetControlLocked(true);
        bool controllerEnabled = characterController.enabled;
        characterController.enabled = false;

        Vector3 origin = hider.transform.position + Vector3.up * 10f;
        RaycastHit groundHit = Physics.RaycastAll(
                origin, Vector3.down, 100f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore)
            .Where(hit => hit.collider != null &&
                          !hit.collider.transform.IsChildOf(hider.transform) &&
                          Vector3.Dot(hit.normal, Vector3.up) > 0.6f)
            .OrderBy(hit => hit.distance)
            .FirstOrDefault();

        float bottomLocal = characterController.center.y -
                            characterController.height * 0.5f;
        if (groundHit.collider != null)
        {
            Vector3 position = hider.transform.position;
            position.y = groundHit.point.y - bottomLocal + 0.02f;
            hider.transform.position = position;
        }

        characterController.enabled = controllerEnabled;
        Physics.SyncTransforms();
        movement.SetControlLocked(false);
        input.move = Vector2.zero;
        input.look = Vector2.zero;
        input.jump = false;
        input.sprint = false;
        groundY = hider.transform.position.y;
    }

    private static void BeginJump(string label)
    {
        PlaceHiderOnGround();
        startY = hider.transform.position.y;
        maximumY = startY;
        maximumVerticalVelocity = float.MinValue;
        minimumVerticalVelocity = float.MaxValue;
        sawAirborne = false;
        sawLanding = false;
        jumpRequestConsumed = false;
        airJumpInjected = false;
        takeoffCount = 0;
        wasGrounded = movement.Grounded;
        input.jump = true;
        RuntimeNotes.Add(
            $"{label} Space request: grounded={movement.Grounded}, " +
            $"isGrounded={characterController.isGrounded}, " +
            $"verticalVelocity={movement.VerticalVelocity:F3}, " +
            $"rootY={hider.transform.position.y:F3}, wall={hider.IsWallAttached}, " +
            $"controllerEnabled={characterController.enabled}, " +
            $"rigidbodyCount={hider.GetComponentsInChildren<Rigidbody>(true).Length}.");
    }

    private static void MonitorJump()
    {
        if (hider == null || movement == null || input == null)
        {
            return;
        }

        float y = hider.transform.position.y;
        maximumY = Mathf.Max(maximumY, y);
        maximumVerticalVelocity =
            Mathf.Max(maximumVerticalVelocity, movement.VerticalVelocity);
        minimumVerticalVelocity =
            Mathf.Min(minimumVerticalVelocity, movement.VerticalVelocity);
        jumpRequestConsumed |= !input.jump;

        if (wasGrounded && !movement.Grounded)
        {
            takeoffCount++;
            sawAirborne = true;
        }
        else if (sawAirborne && !wasGrounded && movement.Grounded)
        {
            sawLanding = true;
        }

        wasGrounded = movement.Grounded;
    }

    private static void ValidateJump(string label, bool requireVisual)
    {
        Require(maximumY - startY > 0.3f,
            $"{label} did not rise enough.", Failures);
        Require(maximumVerticalVelocity > 1f,
            $"{label} never received an upward velocity.", Failures);
        Require(minimumVerticalVelocity < -0.5f,
            $"{label} gravity never produced downward velocity.", Failures);
        Require(sawAirborne && sawLanding,
            $"{label} did not complete takeoff and landing.", Failures);
        Require(takeoffCount == 1,
            $"{label} produced {takeoffCount} takeoffs from one held request.",
            Failures);
        Require(jumpRequestConsumed,
            $"{label} jump request was not consumed.", Failures);
        Require(Mathf.Abs(hider.transform.position.y - groundY) < 0.2f,
            $"{label} did not return to its ground height.", Failures);

        if (requireVisual)
        {
            Require(hider.CurrentPropVisualTransform != null,
                "Disguised visual disappeared during jump.", Failures);
            if (hider.CurrentPropVisualTransform != null)
            {
                Require(Vector3.Distance(
                            hider.CurrentPropVisualTransform.localPosition,
                            visualLocalStart) < 0.001f,
                    "Disguised visual local offset drifted during jump.", Failures);
                Require(Vector3.Distance(
                            hider.CurrentPropVisualTransform.position -
                            hider.transform.position,
                            visualRootOffsetStart) < 0.001f,
                    "Disguised visual separated from Hider root.", Failures);
            }
        }
    }

    private static void BeginDisguisePhase()
    {
        PropTarget prop = FindValidProps().FirstOrDefault();
        Require(prop != null, "No valid PropTarget exists for disguise test.",
            Failures);
        bool disguised = prop != null && hider.TryBecomePropForTesting(prop);
        Require(disguised, "Hider could not enter disguised state.", Failures);
        ValidateVisualPhysics();
        ValidateRootRaycastHitbox();
        BeginSettlePhase(3);
    }

    private static void ValidateRootRaycastHitbox()
    {
        Physics.SyncTransforms();
        Vector3 target = hider.transform.TransformPoint(characterController.center);
        Vector3 origin = target + hider.transform.forward * 2f;
        bool hitSomething = Physics.Raycast(
            origin,
            (target - origin).normalized,
            out RaycastHit hit,
            3f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        Require(hitSomething &&
                hit.collider != null &&
                hit.collider.GetComponentInParent<PropTransformSystem>() == hider,
            "Root CharacterController is not raycast-hittable after visual " +
            "colliders are removed.",
            Failures);
        RuntimeNotes.Add(
            $"Disguised raycast hitbox: hit={hitSomething}, collider=" +
            $"{(hit.collider != null ? hit.collider.GetType().Name : "<none>")}.");
    }

    private static void RunPropSwitchChecks()
    {
        PropTarget[] props = FindValidProps().Take(4).ToArray();
        Require(props.Length >= 2,
            "Not enough valid props for repeated E/O model switch test.", Failures);
        int applied = 0;
        foreach (PropTarget prop in props.Skip(1))
        {
            if (!hider.ApplyPropDefinition(prop, true))
            {
                continue;
            }

            applied++;
            ValidateVisualPhysics();
            Require(hider.CurrentPropVisualTransform != null &&
                    hider.CurrentPropVisualTransform.localPosition.sqrMagnitude <
                    0.000001f,
                $"Switched prop '{prop.name}' has a non-zero pivot offset.",
                Failures);
        }

        Require(applied > 0, "No alternate prop definition could be applied.",
            Failures);
        RuntimeNotes.Add(
            $"Prop switch PASS: {applied} alternate visual(s), final rootY=" +
            $"{hider.transform.position.y:F3}, visual physics count=0.");
    }

    private static void ValidateVisualPhysics()
    {
        Transform visual = hider.CurrentPropVisualTransform;
        Require(visual != null, "Current prop visual is missing.", Failures);
        if (visual == null) return;
        Require(visual.GetComponentsInChildren<Collider>(true).Length == 0,
            "Copied prop visual contains a Collider.", Failures);
        Require(visual.GetComponentsInChildren<Rigidbody>(true).Length == 0 &&
                visual.GetComponentsInChildren<Rigidbody2D>(true).Length == 0,
            "Copied prop visual contains a Rigidbody.", Failures);
    }

    private static void MonitorVisualAttachment()
    {
        if (hider == null || hider.CurrentPropVisualTransform == null)
        {
            return;
        }

        if (Vector3.Distance(
                hider.CurrentPropVisualTransform.position - hider.transform.position,
                visualRootOffsetStart) > 0.01f)
        {
            Require(false, "Visual offset changed relative to Hider root.", Failures);
        }
    }

    private static void BeginWallDetachPhase()
    {
        PlaceHiderOnGround();
        movement.SetControlLocked(true);
        SetPrivateProperty(hider, "IsWallAttached", true);
        SetPrivateProperty(hider, "WallNormal", Vector3.forward);
        hider.DetachFromWall(true);

        startY = hider.transform.position.y;
        maximumY = startY;
        maximumVerticalVelocity = float.MinValue;
        minimumVerticalVelocity = float.MaxValue;
        sawAirborne = false;
        sawLanding = false;
        jumpRequestConsumed = true;
        takeoffCount = 0;
        wasGrounded = movement.Grounded;
        if (hider.CurrentPropVisualTransform != null)
        {
            visualLocalStart = hider.CurrentPropVisualTransform.localPosition;
            visualRootOffsetStart =
                hider.CurrentPropVisualTransform.position - hider.transform.position;
        }
        SetPhase(5);
    }

    private static IEnumerable<PropTarget> FindValidProps()
    {
        return Object.FindObjectsOfType<PropTarget>(true)
            .Where(prop => prop != null && prop.GameplayEnabled &&
                           prop.visualParts != null &&
                           prop.visualParts.Length > 0 &&
                           prop.visualParts.All(part =>
                               part != null && part.mesh != null &&
                               part.materials != null &&
                               part.materials.Any(material => material != null)))
            .OrderBy(prop => prop.name);
    }

    private static void BeginSettlePhase(int nextPhase)
    {
        SetPhase(nextPhase);
    }

    private static void SetPhase(int nextPhase)
    {
        phase = nextPhase;
        phaseStartedAt = EditorApplication.timeSinceStartup;
    }

    private static void Finish()
    {
        SessionState.SetBool(CompletedKey, true);
        EditorApplication.update -= TickPlayValidation;
        EditorApplication.ExitPlaymode();
    }

    private static void CaptureRuntimeException(
        string condition, string stackTrace, LogType type)
    {
        if (condition.Contains("NullReferenceException") ||
            condition.Contains("MissingReferenceException"))
        {
            Require(false, condition + Environment.NewLine + stackTrace, Failures);
        }
    }

    private static void SetPrivateProperty<T>(
        object target, string propertyName, T value)
    {
        PropertyInfo property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null)
        {
            Require(false, $"Property '{propertyName}' was not found.", Failures);
            return;
        }

        property.SetValue(target, value);
    }

    private static string ResolveAudioCaller(AudioSource source)
    {
        if (source.GetComponent<SeekerWeaponPresentation>() != null)
            return "SeekerWeaponPresentation.PlayShotFeedback/PlayReloadFeedback";
        if (source.GetComponent<HiderAntiCampAudioPresentation>() != null)
            return "HiderAntiCampAudioPresentation.HandleAlertTriggered";
        if (source.GetComponent<PersistentMusicManager>() != null)
            return "PersistentMusicManager.Awake";
        if (source.gameObject.name == "Map2MusicPlayer")
            return "Map2RuntimeBootstrap (no Play caller)";
        return "<none found>";
    }

    private static int CountEnabledListeners()
    {
        return Resources.FindObjectsOfTypeAll<AudioListener>()
            .Count(listener => listener != null &&
                               listener.gameObject.scene.IsValid() &&
                               listener.enabled &&
                               listener.gameObject.activeInHierarchy);
    }

    private static string HierarchyPath(Transform transform)
    {
        List<string> names = new List<string>();
        while (transform != null)
        {
            names.Add(transform.name);
            transform = transform.parent;
        }
        names.Reverse();
        return string.Join("/", names);
    }

    private static int Count(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(
                   value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string HashFile(string assetPath)
    {
        string fullPath = Path.Combine(ProjectRoot,
            assetPath.Replace('/', Path.DirectorySeparatorChar));
        using (SHA256 sha = SHA256.Create())
        {
            return BitConverter.ToString(
                    sha.ComputeHash(File.ReadAllBytes(fullPath)))
                .Replace("-", string.Empty);
        }
    }

    private static void Require(
        bool condition, string message, ICollection<string> failures)
    {
        if (!condition && !failures.Contains(message))
        {
            failures.Add(message);
        }
    }

    private static void WriteResult(string text)
    {
        File.WriteAllText(
            Path.Combine(ProjectRoot, ResultName),
            text,
            new UTF8Encoding(false));
    }

    private static string ProjectRoot =>
        Directory.GetParent(Application.dataPath).FullName;
}
