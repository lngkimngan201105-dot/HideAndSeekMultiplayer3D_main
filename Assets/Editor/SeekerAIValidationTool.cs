using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class SeekerAIValidationTool
{
    // Root-level request files let the open Editor run the complete idempotency suite.
    private const string RequestFile = "SeekerAISetup.run";
    private const string SmokeKey = "PropHunt.SeekerAI.Smoke";
    private static readonly List<string> SmokeFailures = new List<string>();
    private static double smokeStartedAt;
    private static int smokePhase;

    static SeekerAIValidationTool()
    {
        if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), RequestFile)))
        {
            EditorApplication.update -= TryRunRequestedSetup;
            EditorApplication.update += TryRunRequestedSetup;
        }

        if (SessionState.GetBool(SmokeKey, false))
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        }
    }

    [MenuItem("Tools/Prop Hunt/Setup AI Seeker Twice + Validate")]
    public static void SetupTwiceAndValidate()
    {
        SeekerPresentationSetupTool.SetupTwiceAndValidate();
        Debug.Log("[SeekerAIValidation] PRESENTATION SETUP + VALIDATION PASS.");
        SeekerAISetupTool.Setup();
        Debug.Log("[SeekerAIValidation] SETUP PASS 1.");
        SeekerAISetupTool.Setup();
        Debug.Log("[SeekerAIValidation] SETUP PASS 2.");
        ValidateStatic();
        StartPlaySmoke();
    }

    [MenuItem("Tools/Prop Hunt/Validate AI Seeker Static")]
    public static void ValidateStatic()
    {
        Scene scene = EditorSceneManager.OpenScene(SeekerAISetupTool.ScenePath, OpenSceneMode.Single);
        List<string> failures = new List<string>();
        GameObject[] seekers = FindNamed(scene, "SeekerPlayer");
        Require(seekers.Length == 1, $"Expected one SeekerPlayer, got {seekers.Length}.", failures);
        GameObject seeker = seekers.FirstOrDefault();
        if (seeker != null)
        {
            Require(seeker.GetComponents<SeekerAIController>().Length == 1, "AI Controller count != 1.", failures);
            Require(seeker.GetComponents<SeekerAINavigation>().Length == 1, "AI Navigation count != 1.", failures);
            Require(seeker.GetComponents<SeekerAIPerception>().Length == 1, "AI Perception count != 1.", failures);
            Require(seeker.GetComponents<SeekerAICombat>().Length == 1, "AI Combat count != 1.", failures);
            Require(seeker.GetComponents<SeekerAISuspicionSystem>().Length == 1, "Suspicion count != 1.", failures);
            Require(seeker.GetComponents<PropHuntSinglePlayerBootstrap>().Length == 1, "Bootstrap count != 1.", failures);
            Require(seeker.GetComponents<NavMeshAgent>().Length == 1, "NavMeshAgent count != 1.", failures);
            Require(seeker.GetComponent<CharacterController>() == null ||
                    !seeker.GetComponent<CharacterController>().enabled,
                "Seeker CharacterController must be disabled.", failures);
            Require(seeker.GetComponent<SeekerFirstPersonController>() == null ||
                    !seeker.GetComponent<SeekerFirstPersonController>().enabled,
                "Human Seeker controller must be disabled.", failures);

            NavMeshAgent agent = seeker.GetComponent<NavMeshAgent>();
            Require(agent != null && Approximately(agent.speed, 2.3f), "Agent patrol speed must be 2.3.", failures);
            Require(agent != null && Approximately(agent.angularSpeed, 360f), "Agent angular speed must be 360.", failures);
            Require(agent != null && Approximately(agent.acceleration, 12f), "Agent acceleration must be 12.", failures);

            SeekerAINavigation navigation = seeker.GetComponent<SeekerAINavigation>();
            Require(navigation != null && navigation.PatrolPointCount >= 5,
                "Weighted patrol requires at least five configured regions.", failures);
            Require(navigation != null &&
                    Approximately(navigation.PatrolSpeed, 2.3f) &&
                    Approximately(navigation.ChaseSpeed, 4.2f),
                "Navigation patrol/chase speeds must remain 2.3/4.2.", failures);
            Require(navigation != null &&
                    navigation.StuckTimeout >= 1.5f &&
                    navigation.StuckTimeout <= 2f,
                "Stuck timeout must remain in the 1.5-2.0 second window.", failures);

            SeekerAIPerception perception = seeker.GetComponent<SeekerAIPerception>();
            Require(perception != null && Approximately(perception.ViewDistance, 22f),
                "Perception range must be 22 m.", failures);
            Require(perception != null && Approximately(perception.FieldOfView, 75f),
                "Perception FOV must be 75 degrees.", failures);

            SeekerAIController controller = seeker.GetComponent<SeekerAIController>();
            Require(controller != null && Approximately(controller.ReactionTime, 0.6f),
                "Reaction time must be 0.6 seconds.", failures);
            Require(controller != null && Approximately(controller.LostSightGrace, 0.4f),
                "Lost-sight grace must be 0.4 seconds.", failures);
            Require(controller != null && Approximately(controller.SearchDuration, 8f),
                "Search duration must be 8 seconds.", failures);
            Require(controller != null &&
                    Approximately(controller.PreferredAttackRange.x, 8f) &&
                    Approximately(controller.PreferredAttackRange.y, 18f),
                "Preferred combat range must be 8-18 m.", failures);
            Require(Enum.IsDefined(typeof(SeekerAIState), nameof(SeekerAIState.Observe)) &&
                    Enum.IsDefined(typeof(SeekerAIState), nameof(SeekerAIState.Reloading)),
                "Observe/Reloading states are missing.", failures);

            SeekerAICombat combat = seeker.GetComponent<SeekerAICombat>();
            Require(combat != null &&
                    Approximately(combat.MinimumShotInterval, 0.35f) &&
                    Approximately(combat.MaximumShotInterval, 0.55f),
                "AI firing cadence must be 0.35-0.55 seconds.", failures);
            Require(combat != null && combat.BodyAimTolerance <= 10f &&
                    combat.MuzzleAimTolerance <= 20f,
                "AI aim gates are too permissive.", failures);

            SeekerAISuspicionSystem suspicion =
                seeker.GetComponent<SeekerAISuspicionSystem>();
            Require(suspicion != null &&
                    Approximately(suspicion.SuspicionThreshold, 45f),
                "Prop suspicion threshold must be 45.", failures);

            SeekerRaycastWeapon weapon = seeker.GetComponentInChildren<SeekerRaycastWeapon>(true);
            Require(weapon != null && weapon.Damage == 20, "Weapon damage must remain 20.", failures);
            Require(weapon != null && Approximately(weapon.Range, 50f), "Weapon range must remain 50.", failures);
            Require(weapon != null && Approximately(weapon.Cooldown, 0.35f), "Weapon cooldown must remain 0.35.", failures);

            SeekerWeaponEnergy energy = seeker.GetComponent<SeekerWeaponEnergy>();
            Require(energy != null && energy.MaxCharges == 5, "Energy max must remain 5.", failures);
            Require(energy != null && Approximately(energy.ReloadDuration, 1.8f), "Reload duration must remain 1.8.", failures);

            SeekerWeaponPresentation presentation = seeker.GetComponent<SeekerWeaponPresentation>();
            Require(presentation != null && presentation.MuzzleFlash != null &&
                    IsNamedAncestor(presentation.MuzzleFlash.transform, "MuzzlePoint_World"),
                "Muzzle flash is not bound to MuzzlePoint_World.", failures);
            Require(presentation != null && Approximately(presentation.BaseImpactScale, 0.192f),
                "Base impact scale must remain 0.192.", failures);
            Require(presentation != null && Approximately(presentation.ImpactScale, 0.384f),
                "Final impact scale must remain 200% (0.384).", failures);
            Require(presentation != null && presentation.AudioSource != null &&
                    Approximately(presentation.AudioSource.spatialBlend, 1f),
                "Seeker audio must be 3D.", failures);

            Transform worldGun = FindDescendant(seeker.transform, "SciFiGunLight_World");
            Bounds worldGunBounds = default;
            string worldGunDiagnostic = "World gun root missing.";
            bool worldGunBoundsValid = worldGun != null &&
                                       SeekerPresentationSetupTool.TryCalculateVisualBounds(
                                           worldGun.gameObject,
                                           out worldGunBounds,
                                           out worldGunDiagnostic);
            Require(worldGunBoundsValid,
                "World gun robust bounds validation failed.\n" + worldGunDiagnostic, failures);
            Require(worldGun != null &&
                    (worldGun.GetComponentsInChildren<MeshFilter>(true)
                         .Any(filter => filter.sharedMesh != null &&
                                        filter.sharedMesh.vertexCount > 0) ||
                     worldGun.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                         .Any(renderer => renderer.sharedMesh != null &&
                                          renderer.sharedMesh.vertexCount > 0)),
                "World gun has no real mesh with vertices.", failures);
            float worldGunDimension = Mathf.Max(
                worldGunBounds.size.x, worldGunBounds.size.y, worldGunBounds.size.z);
            Require(worldGunBoundsValid && worldGunDimension >= 0.80f &&
                    worldGunDimension <= 1.00f,
                $"World gun dimension is invalid: {worldGunDimension:F4}m.", failures);

            Require(FindDescendant(seeker.transform, "SeekerAIEye") != null, "SeekerAIEye missing.", failures);
            Transform fps = FindDescendant(seeker.transform, "SciFiGunLight_FPS");
            Transform world = FindDescendant(seeker.transform, "SciFiGunLight_World");
            Camera seekerCamera = seeker.GetComponentInChildren<Camera>(true);
            Require(fps != null && !fps.gameObject.activeInHierarchy, "FPS gun must be inactive.", failures);
            Require(world != null && world.gameObject.activeInHierarchy, "World gun must be active.", failures);
            Require(seekerCamera != null && !seekerCamera.gameObject.activeInHierarchy,
                "SeekerCamera must be inactive.", failures);
        }

        PropHuntTestRoleSelector selector = Object.FindObjectOfType<PropHuntTestRoleSelector>(true);
        Require(selector != null && selector.SinglePlayerHiderMode, "Single-player Hider mode is not enabled.", failures);
        Require(selector != null && (selector.RoleSelectionPanel == null ||
                                     !selector.RoleSelectionPanel.activeSelf),
            "Role selection panel must be hidden.", failures);
        GameObject[] surfaces = FindNamed(scene, "SeekerAINavMeshSurface");
        Require(surfaces.Length == 1, $"NavMeshSurface root count is {surfaces.Length}.", failures);
        NavMeshSurface surface = surfaces.FirstOrDefault()?.GetComponent<NavMeshSurface>();
        Require(surface != null && surface.navMeshData != null, "NavMeshData is missing.", failures);
        Require(AssetDatabase.LoadAssetAtPath<NavMeshData>(SeekerAISetupTool.NavMeshAssetPath) != null,
            "Project-owned NavMesh asset is missing.", failures);
        Require(Object.FindObjectsOfType<Assets.Scripts.RuntimeNavMeshBuilder>(true).Length == 0,
            "RuntimeNavMeshBuilder must not rebuild the map.", failures);
        Require(Object.FindObjectsOfType<HiderPerceptionSignature>(true).Length == 1,
            "HiderPerceptionSignature count != 1.", failures);
        Require(Object.FindObjectsOfType<Camera>(true).Count(camera => camera.enabled && camera.gameObject.activeInHierarchy) == 1,
            "Exactly one Camera must be active.", failures);
        Require(Object.FindObjectsOfType<AudioListener>(true).Count(listener => listener.enabled && listener.gameObject.activeInHierarchy) == 1,
            "Exactly one AudioListener must be active.", failures);
        Require(File.ReadAllText("Packages/manifest.json").Contains("\"com.unity.ai.navigation\": \"1.1.5\""),
            "Existing AI Navigation package 1.1.5 changed or is missing.", failures);
        Require(typeof(HiderAntiCampAlertData).GetProperty("AlertPosition") != null,
            "Anti-Camp event must expose an alert position snapshot.", failures);
        Require(typeof(HiderAntiCampAlertData).GetFields(
                    BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic)
                .All(field => !typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)),
            "Anti-Camp event must not carry a direct Hider object reference.", failures);

        if (failures.Count > 0)
            throw new InvalidOperationException("[SeekerAIValidation] STATIC FAIL\n" + string.Join("\n", failures));
        Debug.Log("[SeekerAIValidation] STATIC PASS — Phase 0 integration and Phase 1 perception, patrol, combat, suspicion, reload and recovery constraints verified.");
    }

    private static void TryRunRequestedSetup()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        EditorApplication.update -= TryRunRequestedSetup;
        string requestPath = Path.Combine(Directory.GetCurrentDirectory(), RequestFile);
        if (File.Exists(requestPath)) File.Delete(requestPath);
        try
        {
            SetupTwiceAndValidate();
        }
        catch (Exception exception)
        {
            Debug.LogError("[SeekerAIValidation] AUTOMATED RUN FAIL\n" + exception);
        }
    }

    private static void StartPlaySmoke()
    {
        SmokeFailures.Clear();
        smokePhase = 0;
        SessionState.SetBool(SmokeKey, true);
        EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        EditorApplication.EnterPlaymode();
    }

    private static void HandlePlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            smokeStartedAt = EditorApplication.timeSinceStartup;
            smokePhase = 0;
            EditorApplication.update -= TickPlaySmoke;
            EditorApplication.update += TickPlaySmoke;
        }
        else if (change == PlayModeStateChange.EnteredEditMode &&
                 SessionState.GetBool(SmokeKey, false))
        {
            SessionState.SetBool(SmokeKey, false);
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
            if (SmokeFailures.Count == 0)
                Debug.Log("[SeekerAIValidation] PLAY MODE SMOKE PASS — preparation lock, 10 weighted patrol selections, no-warp stuck recovery, wall-blocked sight, fair Anti-Camp snapshot, target penalty, 250 shots/50 reloads and Seeker elimination verified.");
            else
                Debug.LogError("[SeekerAIValidation] PLAY MODE SMOKE FAIL\n" + string.Join("\n", SmokeFailures));
        }
    }

    private static void TickPlaySmoke()
    {
        if (!EditorApplication.isPlaying ||
            EditorApplication.timeSinceStartup - smokeStartedAt < 1.2d)
            return;

        if (smokePhase == 0)
        {
            try
            {
                RunSmokeMain();
            }
            catch (Exception exception)
            {
                SmokeFailures.Add(exception.ToString());
            }
            smokePhase = 1;
            smokeStartedAt = EditorApplication.timeSinceStartup;
            return;
        }

        if (EditorApplication.timeSinceStartup - smokeStartedAt < 0.25d) return;
        SeekerAIController controller = Object.FindObjectOfType<SeekerAIController>(true);
        PropHuntRoundManager round = Object.FindObjectOfType<PropHuntRoundManager>(true);
        SmokeRequire(controller != null && controller.CurrentState == SeekerAIState.Eliminated,
            "Seeker did not remain in Eliminated state at 0 HP.");
        SmokeRequire(round != null && round.CurrentWinner == PropHuntRoundWinner.Hiders,
            "Seeker 0 HP did not produce a Hider win.");
        EditorApplication.update -= TickPlaySmoke;
        EditorApplication.ExitPlaymode();
    }

    private static void RunSmokeMain()
    {
        PropHuntRoundManager round = Object.FindObjectOfType<PropHuntRoundManager>(true);
        PropHuntTestRoleSelector selector = Object.FindObjectOfType<PropHuntTestRoleSelector>(true);
        SeekerAIController controller = Object.FindObjectOfType<SeekerAIController>(true);
        SeekerRaycastWeapon weapon = Object.FindObjectOfType<SeekerRaycastWeapon>(true);
        SeekerWeaponEnergy energy = Object.FindObjectOfType<SeekerWeaponEnergy>(true);
        SeekerWeaponPresentation presentation = Object.FindObjectOfType<SeekerWeaponPresentation>(true);
        SeekerHealth seekerHealth = Object.FindObjectOfType<SeekerHealth>(true);
        NavMeshAgent agent = Object.FindObjectOfType<NavMeshAgent>(true);
        SeekerAINavigation navigation = Object.FindObjectOfType<SeekerAINavigation>(true);
        SeekerAIPerception perception = Object.FindObjectOfType<SeekerAIPerception>(true);
        HiderAntiCampSystem antiCamp = Object.FindObjectOfType<HiderAntiCampSystem>(true);
        HiderHealth hiderHealth = Object.FindObjectOfType<HiderHealth>(true);

        SmokeRequire(round != null && selector != null && controller != null &&
                     weapon != null && energy != null && presentation != null &&
                     seekerHealth != null && agent != null && navigation != null &&
                     perception != null && antiCamp != null && hiderHealth != null,
            "A required runtime reference is missing.");
        if (round == null || weapon == null || energy == null || presentation == null ||
            seekerHealth == null || agent == null || navigation == null ||
            perception == null || controller == null || antiCamp == null ||
            hiderHealth == null) return;

        SmokeRequire(controller.CurrentState == SeekerAIState.PreparationWait,
            "AI hunted during the preparation phase.");
        SmokeRequire(!agent.hasPath || agent.isStopped,
            "AI moved during the preparation phase.");
        round.BeginHunting();
        SmokeRequire(selector.CurrentRole == PropHuntTestRole.Hider, "Player is not locked to Hider.");
        SmokeRequire(agent.enabled && agent.isOnNavMesh, "Seeker NavMeshAgent is not on NavMesh.");
        SmokeRequire(Object.FindObjectsOfType<Camera>(true)
                         .Count(camera => camera.enabled && camera.gameObject.activeInHierarchy) == 1,
            "Runtime active camera count is not one.");
        SmokeRequire(Object.FindObjectsOfType<AudioListener>(true)
                         .Count(listener => listener.enabled && listener.gameObject.activeInHierarchy) == 1,
            "Runtime active AudioListener count is not one.");

        int previousPatrolIndex = -2;
        for (int destination = 0; destination < 10; destination++)
        {
            SmokeRequire(navigation.MoveToRandomPatrolPoint(),
                $"Weighted patrol destination {destination} was rejected.");
            SmokeRequire(navigation.CurrentPatrolIndex != previousPatrolIndex,
                $"Patrol repeated region {navigation.CurrentPatrolIndex} consecutively.");
            previousPatrolIndex = navigation.CurrentPatrolIndex;
        }
        SmokeRequire(navigation.PatrolSelectionCount >= 10,
            "Patrol did not record ten destination selections.");

        Vector3 positionBeforeRecovery = agent.transform.position;
        int recoveryCountBefore = navigation.StuckRecoveryCount;
        typeof(SeekerAINavigation).GetField(
                "lastProgressAt",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(navigation, Time.time - navigation.StuckTimeout - 0.1f);
        typeof(SeekerAINavigation).GetField(
                "lastProgressPosition",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(navigation, agent.transform.position);
        SmokeRequire(navigation.TickStuckRecovery(out _),
            "Forced no-progress path did not trigger stuck recovery.");
        SmokeRequire(navigation.StuckRecoveryCount == recoveryCountBefore + 1,
            "Stuck recovery counter did not advance exactly once.");
        SmokeRequire(Vector3.Distance(
                agent.transform.position,
                positionBeforeRecovery) <= 0.05f,
            "Stuck recovery warped the Seeker.");

        Vector3 lastKnownBeforeAlert = controller.LastKnownPosition;
        bool hadLastKnownBeforeAlert = controller.HasLastKnownPosition;
        antiCamp.TriggerAlertForValidation();
        SmokeRequire(controller.CurrentState == SeekerAIState.Investigate,
            "Anti-Camp alert did not enter Investigate.");
        SmokeRequire(Vector3.Distance(
                controller.InvestigationSnapshot,
                hiderHealth.transform.position) >= 0.5f,
            "Anti-Camp investigation used the exact Hider position.");
        SmokeRequire(controller.HasLastKnownPosition == hadLastKnownBeforeAlert &&
                     (!hadLastKnownBeforeAlert ||
                      Vector3.SqrMagnitude(
                          controller.LastKnownPosition - lastKnownBeforeAlert) <
                      0.0001f),
            "Anti-Camp alert overwrote visual last-known memory.");

        GameObject sightTarget = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        GameObject sightBlocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sightTarget.name = "AIPhase1SightTarget";
        sightBlocker.name = "AIPhase1SightBlocker";
        Vector3 sightOrigin = agent.transform.position + Vector3.up * 6f;
        sightTarget.transform.position = sightOrigin + Vector3.up * 4f;
        sightBlocker.transform.position = sightOrigin + Vector3.up * 2f;
        sightBlocker.transform.localScale = new Vector3(2f, 0.5f, 2f);
        Physics.SyncTransforms();
        SmokeRequire(!perception.HasUnblockedLine(
                sightOrigin,
                sightTarget.transform.position,
                sightTarget.transform),
            "Sight ray incorrectly passed through a world blocker.");
        sightBlocker.SetActive(false);
        Physics.SyncTransforms();
        SmokeRequire(perception.HasUnblockedLine(
                sightOrigin,
                sightTarget.transform.position,
                sightTarget.transform),
            "Clear sight ray did not accept the visible target.");
        sightTarget.SetActive(false);
        Object.Destroy(sightBlocker);
        Object.Destroy(sightTarget);

        int shotFeedbackBefore = presentation.ShotFeedbackCount;
        int reloadFeedbackBefore = presentation.ReloadFeedbackCount;
        Vector3 origin = presentation.MuzzleFlash != null
            ? presentation.MuzzleFlash.transform.position
            : agent.transform.position + Vector3.up;
        for (int cycle = 0; cycle < 50; cycle++)
        {
            for (int shot = 0; shot < 5; shot++)
                SmokeRequire(weapon.TryFireRayFromAI(new Ray(origin, Vector3.up), true),
                    $"Stress shot failed at cycle {cycle}, shot {shot}.");
            SmokeRequire(energy.CurrentCharges == 0, $"Energy mismatch after cycle {cycle}.");
            SmokeRequire(energy.TryStartReloadFromAI(), $"Reload failed at cycle {cycle}.");
            energy.AdvanceReloadForValidation(2f);
            SmokeRequire(energy.CurrentCharges == 5 && !energy.IsReloading,
                $"Reload did not complete at cycle {cycle}.");
        }
        SmokeRequire(presentation.ShotFeedbackCount - shotFeedbackBefore == 250,
            "Presentation did not receive exactly 250 shot events.");
        SmokeRequire(presentation.ReloadFeedbackCount - reloadFeedbackBefore == 50,
            "Presentation did not receive exactly 50 reload events.");

        GameObject validProp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        validProp.name = "AIWrongPropSmokeTarget";
        validProp.transform.position = origin + Vector3.forward * 3f;
        PropTarget propTarget = validProp.AddComponent<PropTarget>();
        propTarget.SetGameplayEnabled(true);
        MeshFilter smokeMesh = validProp.GetComponent<MeshFilter>();
        MeshRenderer smokeRenderer = validProp.GetComponent<MeshRenderer>();
        propTarget.visualParts = new[]
        {
            new PropVisualPartData
            {
                mesh = smokeMesh.sharedMesh,
                materials = smokeRenderer.sharedMaterials,
                localScale = Vector3.one
            }
        };
        Physics.SyncTransforms();
        int healthBefore = seekerHealth.CurrentHealth;
        Collider propCollider = validProp.GetComponent<Collider>();
        SmokeRequire(weapon.TryFireRayFromAI(
                new Ray(origin, (propCollider.bounds.center - origin).normalized), true),
            "Wrong-prop shot did not execute.");
        SmokeRequire(seekerHealth.CurrentHealth == healthBefore - 5,
            "Valid wrong prop did not apply exactly 5 Seeker damage.");
        Object.Destroy(validProp);
        seekerHealth.TakeDamage(1000);
    }

    private static GameObject[] FindNamed(Scene scene, string name)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(item => item.name == name)
            .Select(item => item.gameObject)
            .ToArray();
    }

    private static Transform FindDescendant(Transform root, string name)
    {
        return root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item => item.name == name);
    }

    private static bool IsNamedAncestor(Transform item, string name)
    {
        while (item != null)
        {
            if (item.name == name) return true;
            item = item.parent;
        }
        return false;
    }

    private static bool Approximately(float left, float right)
    {
        return Mathf.Abs(left - right) <= 0.001f;
    }

    private static void Require(bool condition, string message, ICollection<string> failures)
    {
        if (!condition) failures.Add(message);
    }

    private static void SmokeRequire(bool condition, string message)
    {
        if (!condition) SmokeFailures.Add(message);
    }
}
