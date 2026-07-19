using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class HiderAntiCampWeaponIsolationValidationTool
{
    private const string RunningKey = "PropHunt.AntiCampWeaponIsolation.Running";
    private const string CommandLineKey = "PropHunt.AntiCampWeaponIsolation.CommandLine";
    private const string ResultKey = "PropHunt.AntiCampWeaponIsolation.Result";
    private const string ScenePath = "Assets/Scenes/Map_v2.unity";
    private static readonly List<string> SmokeFailures = new List<string>();
    private static GameObject smokeBlocker;

    static HiderAntiCampWeaponIsolationValidationTool()
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        if (EditorApplication.isPlaying) EditorApplication.delayCall += RunPlayModeSmoke;
    }

    [MenuItem("Tools/Prop Hunt/Validate Anti-Camp Weapon Isolation")]
    public static void ValidateFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[AntiCampIsolation] Exit Play Mode before validation.");
            return;
        }

        RunSetupAndStaticValidation(false);
    }

    public static void RunCommandLineVerification()
    {
        RunSetupAndStaticValidation(true);
    }

    private static void RunSetupAndStaticValidation(bool commandLine)
    {
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Unity is already entering or running Play Mode.");

            HiderCompleteHUDSetupTool.SetupHiderCompleteHud();
            Debug.Log("[AntiCampIsolation] SETUP PASS 1.");
            HiderCompleteHUDSetupTool.SetupHiderCompleteHud();
            Debug.Log("[AntiCampIsolation] SETUP PASS 2.");

            List<string> failures = ValidateStaticInternal();
            if (failures.Count > 0)
                throw new InvalidOperationException(string.Join("\n", failures));

            Debug.Log("[AntiCampIsolation] STATIC PASS — dedicated alert event/audio, no weapon/input/shared-lock dependency, 5 charges, 1.8s reload and 20/50/0.35 verified.");
            SessionState.SetBool(RunningKey, true);
            SessionState.SetBool(CommandLineKey, commandLine);
            SessionState.SetString(ResultKey, string.Empty);
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
            EditorApplication.EnterPlaymode();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[AntiCampIsolation] STATIC FAIL\n{exception}");
            if (commandLine) EditorApplication.Exit(2);
        }
    }

    private static List<string> ValidateStaticInternal()
    {
        if (SceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        List<string> failures = new List<string>();
        HiderAntiCampSystem antiCamp = Object.FindObjectOfType<HiderAntiCampSystem>(true);
        HiderAntiCampAudioPresentation antiCampAudio =
            Object.FindObjectOfType<HiderAntiCampAudioPresentation>(true);
        SeekerWeaponEnergy energy = Object.FindObjectOfType<SeekerWeaponEnergy>(true);
        SeekerRaycastWeapon weapon = Object.FindObjectOfType<SeekerRaycastWeapon>(true);
        SeekerWeaponPresentation weaponPresentation =
            Object.FindObjectOfType<SeekerWeaponPresentation>(true);

        Require(antiCamp != null, "HiderAntiCampSystem is missing.", failures);
        Require(antiCampAudio != null && antiCampAudio.AntiCampSystem == antiCamp &&
                antiCampAudio.AudioSource != null &&
                antiCampAudio.gameObject.name == HiderAntiCampSystem.DedicatedAudioObjectName &&
                antiCampAudio.transform.parent == antiCamp?.transform,
            "Dedicated HiderAntiCampAudioPresentation hierarchy/references are invalid.", failures);
        Require(antiCampAudio != null && weaponPresentation != null &&
                antiCampAudio.AudioSource != weaponPresentation.AudioSource,
            "Anti-Camp and Seeker weapon share an AudioSource.", failures);
        Require(antiCampAudio != null && antiCampAudio.AudioSource != null &&
                !antiCampAudio.AudioSource.playOnAwake && !antiCampAudio.AudioSource.loop,
            "Anti-Camp AudioSource must be non-looping with Play On Awake disabled.", failures);
        Require(energy != null && energy.MaxCharges == 5 &&
                Approximately(energy.ReloadDuration, 1.8f),
            "Weapon energy must remain 5 charges with a 1.8 second reload.", failures);
        Require(weapon != null && weapon.Damage == 20 && Approximately(weapon.Range, 50f) &&
                Approximately(weapon.Cooldown, 0.35f),
            "Weapon gameplay changed from damage/range/cooldown 20/50/0.35.", failures);

        string antiCampSource = ReadProjectFile("Assets/Scripts/PropHunt/HiderAntiCampSystem.cs");
        string antiCampAudioSource = ReadProjectFile("Assets/Scripts/PropHunt/HiderAntiCampAudioPresentation.cs");
        string weaponSource = ReadProjectFile("Assets/Scripts/PropHunt/SeekerRaycastWeapon.cs");
        string energySource = ReadProjectFile("Assets/Scripts/PropHunt/SeekerWeaponEnergy.cs");
        Require(!Regex.IsMatch(antiCampSource,
                @"SeekerWeaponEnergy|SeekerRaycastWeapon|InputAction|SetWeaponActive|SetGameplayInputLocked|CurrentControlledRole|\bAudioSource\b|StopAllCoroutines|CancelInvoke|Time\.timeScale"),
            "Anti-Camp logic still references weapon/input/audio/shared-lock APIs.", failures);
        Require(!Regex.IsMatch(antiCampAudioSource,
                @"SeekerWeaponEnergy|SeekerRaycastWeapon|InputAction|SetWeaponActive|SetGameplayInputLocked|CurrentControlledRole"),
            "Anti-Camp audio presentation references weapon/input/shared-lock APIs.", failures);
        Require(!Regex.IsMatch(weaponSource + energySource,
                @"HiderAntiCamp|AntiCamp|AudioSource\s*\.\s*isPlaying"),
            "Weapon or energy code depends on Anti-Camp state/audio.", failures);
        Require(!Regex.IsMatch(energySource,
                @"reserveAmmo|totalAmmo|remainingMagazines|maxMagazines|maxReloadCount|remainingReloads|batteryInventory|totalEnergyReserve"),
            "Weapon energy contains a reserve-ammo or magazine-limit field.", failures);
        return failures;
    }

    private static void HandlePlayModeChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            EditorApplication.delayCall += RunPlayModeSmoke;
            return;
        }

        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        SessionState.SetBool(RunningKey, false);
        string result = SessionState.GetString(ResultKey, string.Empty);
        bool commandLine = SessionState.GetBool(CommandLineKey, false);
        SessionState.SetBool(CommandLineKey, false);
        if (string.IsNullOrEmpty(result))
            Debug.Log("[AntiCampIsolation] PLAY MODE PASS — 5/5, 2/5, mid-reload, 20 alerts and 250 shots/50 reloads remained independent.");
        else
            Debug.LogError($"[AntiCampIsolation] PLAY MODE FAIL\n{result}");
        if (commandLine) EditorApplication.Exit(string.IsNullOrEmpty(result) ? 0 : 3);
    }

    private static void RunPlayModeSmoke()
    {
        if (!EditorApplication.isPlaying) return;
        SmokeFailures.Clear();
        HiderAntiCampSystem antiCamp = Object.FindObjectOfType<HiderAntiCampSystem>(true);
        HiderAntiCampAudioPresentation antiCampAudio =
            Object.FindObjectOfType<HiderAntiCampAudioPresentation>(true);
        PropHuntTestRoleSelector selector = Object.FindObjectOfType<PropHuntTestRoleSelector>(true);
        PropHuntRoundManager roundManager = Object.FindObjectOfType<PropHuntRoundManager>(true);
        SeekerRaycastWeapon weapon = Object.FindObjectOfType<SeekerRaycastWeapon>(true);
        SeekerWeaponEnergy energy = Object.FindObjectOfType<SeekerWeaponEnergy>(true);
        SeekerWeaponPresentation presentation = Object.FindObjectOfType<SeekerWeaponPresentation>(true);

        Action<HiderAntiCampAlertData> alertHandler = null;
        try
        {
            Require(antiCamp != null && antiCampAudio != null && selector != null &&
                    roundManager != null && weapon != null && energy != null && presentation != null,
                "Smoke references are incomplete.", SmokeFailures);
            if (SmokeFailures.Count > 0) return;

            selector.SelectInitialSeekerRole();
            roundManager.BeginHunting();
            Require(selector.CurrentControlledRole == PropHuntTestRole.Seeker && weapon.IsWeaponActive,
                "Could not activate Seeker for smoke test.", SmokeFailures);
            Require(antiCampAudio.AudioSource != presentation.AudioSource,
                "Runtime Anti-Camp and weapon AudioSources are shared.", SmokeFailures);

            smokeBlocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            smokeBlocker.name = "AntiCampIsolationWorldBlocker";
            smokeBlocker.transform.SetPositionAndRotation(new Vector3(0f, 10000f, 3f), Quaternion.identity);
            smokeBlocker.transform.localScale = new Vector3(2f, 2f, 0.2f);
            Physics.SyncTransforms();
            Ray shotRay = new Ray(new Vector3(0f, 10000f, 0f), Vector3.forward);

            int receivedAlerts = 0;
            alertHandler = _ => receivedAlerts++;
            antiCamp.AntiCampAlertTriggered += alertHandler;
            int alertStart = antiCamp.AlertTriggerCount;
            int audioStart = antiCampAudio.PlayedAlertCount;

            RunFullWeaponAlertTest(antiCamp, weapon, energy, selector, shotRay);
            RunTwoChargeAlertTest(antiCamp, weapon, energy, selector, shotRay);
            RunReloadAlertTest(antiCamp, weapon, energy, selector, shotRay);
            RunTwentyAlertTest(antiCamp, weapon, energy, selector);
            RunCombinedStressTest(antiCamp, weapon, energy, selector, shotRay);

            int emittedAlerts = antiCamp.AlertTriggerCount - alertStart;
            int playedAlerts = antiCampAudio.PlayedAlertCount - audioStart;
            Require(receivedAlerts == emittedAlerts && playedAlerts == emittedAlerts && emittedAlerts >= 72,
                $"Alert subscription mismatch: emitted={emittedAlerts}, received={receivedAlerts}, audio={playedAlerts}.",
                SmokeFailures);
            Debug.Log($"[AntiCampIsolation] ALERT EVENT PASS — emitted={emittedAlerts}, received={receivedAlerts}, audioCallbacks={playedAlerts}, dedicatedAudio={antiCampAudio.AudioSource.name}.");
        }
        catch (Exception exception)
        {
            SmokeFailures.Add(exception.ToString());
        }
        finally
        {
            if (antiCamp != null && alertHandler != null)
                antiCamp.AntiCampAlertTriggered -= alertHandler;
            if (smokeBlocker != null) Object.Destroy(smokeBlocker);
            SessionState.SetString(ResultKey, string.Join("\n", SmokeFailures));
            EditorApplication.ExitPlaymode();
        }
    }

    private static void RunFullWeaponAlertTest(
        HiderAntiCampSystem antiCamp,
        SeekerRaycastWeapon weapon,
        SeekerWeaponEnergy energy,
        PropHuntTestRoleSelector selector,
        Ray ray)
    {
        energy.ResetForRound();
        WeaponSnapshot before = Capture(weapon, energy, selector);
        antiCamp.TriggerAlertForValidation();
        WeaponSnapshot after = Capture(weapon, energy, selector);
        RequireUnchanged("5/5 alert", before, after);
        Require(FireShots(weapon, energy, ray, 5) && energy.CurrentCharges == 0,
            "5/5 alert test could not fire all five shots.", SmokeFailures);
        Require(energy.TryStartReload(), "5/5 alert test could not start reload.", SmokeFailures);
        energy.AdvanceReloadForValidation(1.8f);
        Require(energy.CurrentCharges == 5 && !energy.IsReloading,
            "5/5 alert test did not finish reload at 5/5.", SmokeFailures);
        LogTransition("5/5", before, after, antiCamp);
    }

    private static void RunTwoChargeAlertTest(
        HiderAntiCampSystem antiCamp,
        SeekerRaycastWeapon weapon,
        SeekerWeaponEnergy energy,
        PropHuntTestRoleSelector selector,
        Ray ray)
    {
        energy.ResetForRound();
        Require(FireShots(weapon, energy, ray, 3) && energy.CurrentCharges == 2,
            "Could not prepare 2/5 alert test.", SmokeFailures);
        WeaponSnapshot before = Capture(weapon, energy, selector);
        antiCamp.TriggerAlertForValidation();
        WeaponSnapshot after = Capture(weapon, energy, selector);
        RequireUnchanged("2/5 alert", before, after);
        Require(energy.CurrentCharges == 2 && FireShots(weapon, energy, ray, 2) && energy.CurrentCharges == 0,
            "2/5 alert changed charges or blocked the final two shots.", SmokeFailures);
        LogTransition("2/5", before, after, antiCamp);
    }

    private static void RunReloadAlertTest(
        HiderAntiCampSystem antiCamp,
        SeekerRaycastWeapon weapon,
        SeekerWeaponEnergy energy,
        PropHuntTestRoleSelector selector,
        Ray ray)
    {
        energy.ResetForRound();
        Require(FireShots(weapon, energy, ray, 5) && energy.TryStartReload(),
            "Could not prepare mid-reload alert test.", SmokeFailures);
        energy.AdvanceReloadForValidation(0.81f);
        WeaponSnapshot before = Capture(weapon, energy, selector);
        antiCamp.TriggerAlertForValidation();
        WeaponSnapshot after = Capture(weapon, energy, selector);
        RequireUnchanged("45% reload alert", before, after);
        energy.AdvanceReloadForValidation(0.99f);
        Require(!energy.IsReloading && energy.CurrentCharges == 5 && FireShots(weapon, energy, ray, 1),
            "Mid-reload alert cancelled/restarted/stuck reload or blocked the next shot.", SmokeFailures);
        LogTransition("reload45", before, after, antiCamp);
    }

    private static void RunTwentyAlertTest(
        HiderAntiCampSystem antiCamp,
        SeekerRaycastWeapon weapon,
        SeekerWeaponEnergy energy,
        PropHuntTestRoleSelector selector)
    {
        energy.ResetForRound();
        int start = antiCamp.AlertTriggerCount;
        for (int i = 0; i < 20; i++)
        {
            WeaponSnapshot before = Capture(weapon, energy, selector);
            antiCamp.TriggerAlertForValidation();
            WeaponSnapshot after = Capture(weapon, energy, selector);
            RequireUnchanged($"repeated alert {i + 1}", before, after);
        }
        Require(antiCamp.AlertTriggerCount == start + 20 && energy.CurrentCharges == 5,
            "Twenty-alert test lost/duplicated alerts or changed weapon energy.", SmokeFailures);
        Debug.Log("[AntiCampIsolation] 20 ALERT PASS — weapon/input/role/energy unchanged.");
    }

    private static void RunCombinedStressTest(
        HiderAntiCampSystem antiCamp,
        SeekerRaycastWeapon weapon,
        SeekerWeaponEnergy energy,
        PropHuntTestRoleSelector selector,
        Ray ray)
    {
        energy.ResetForRound();
        int completedStart = energy.CompletedReloadCount;
        int shots = 0;
        for (int cycle = 0; cycle < 50; cycle++)
        {
            for (int shot = 0; shot < 5; shot++)
            {
                Require(weapon.TryFireRay(ray, true), $"Stress cycle {cycle + 1} shot {shot + 1} was rejected.", SmokeFailures);
                shots++;
                if (shot == 2)
                {
                    WeaponSnapshot before = Capture(weapon, energy, selector);
                    antiCamp.TriggerAlertForValidation();
                    RequireUnchanged($"stress firing cycle {cycle + 1}", before, Capture(weapon, energy, selector));
                }
            }

            Require(energy.CurrentCharges == 0 && energy.TryStartReload(),
                $"Stress cycle {cycle + 1} could not start reload.", SmokeFailures);
            energy.AdvanceReloadForValidation(0.81f);
            WeaponSnapshot reloadBefore = Capture(weapon, energy, selector);
            antiCamp.TriggerAlertForValidation();
            RequireUnchanged($"stress reload cycle {cycle + 1}", reloadBefore, Capture(weapon, energy, selector));
            energy.AdvanceReloadForValidation(0.99f);
            Require(energy.CurrentCharges == 5 && !energy.IsReloading,
                $"Stress cycle {cycle + 1} did not recover to Ready 5/5.", SmokeFailures);
        }

        Require(shots == 250 && energy.CompletedReloadCount == completedStart + 50 &&
                energy.CurrentCharges == 5 && energy.State == SeekerWeaponEnergyState.Ready,
            $"Combined stress mismatch: shots={shots}, reloads={energy.CompletedReloadCount - completedStart}, charges={energy.CurrentCharges}.",
            SmokeFailures);
        Debug.Log("[AntiCampIsolation] STRESS PASS — 250 shots / 50 unlimited reloads with 100 interleaved alerts.");
    }

    private static bool FireShots(SeekerRaycastWeapon weapon, SeekerWeaponEnergy energy, Ray ray, int count)
    {
        int before = energy.CurrentCharges;
        for (int i = 0; i < count; i++)
            if (!weapon.TryFireRay(ray, true)) return false;
        return energy.CurrentCharges == before - count;
    }

    private static WeaponSnapshot Capture(
        SeekerRaycastWeapon weapon,
        SeekerWeaponEnergy energy,
        PropHuntTestRoleSelector selector)
    {
        return new WeaponSnapshot(
            energy.CurrentCharges,
            energy.MaxCharges,
            energy.IsReloading,
            energy.ReloadProgress,
            weapon.enabled,
            weapon.FireInputEnabled,
            energy.ReloadInputEnabled,
            selector.CurrentControlledRole,
            weapon.IsWeaponActive,
            weapon.CooldownRemaining);
    }

    private static void RequireUnchanged(string context, WeaponSnapshot before, WeaponSnapshot after)
    {
        Require(before.EquivalentTo(after),
            $"{context} changed weapon state. BEFORE {before}; AFTER {after}.", SmokeFailures);
    }

    private static void LogTransition(
        string context,
        WeaponSnapshot before,
        WeaponSnapshot after,
        HiderAntiCampSystem antiCamp)
    {
        HiderAntiCampAudioPresentation audioPresentation =
            Object.FindObjectOfType<HiderAntiCampAudioPresentation>(true);
        Debug.Log($"[AntiCampIsolationState] {context} BEFORE {before}; AFTER {after}; " +
                  $"antiCampTimer={antiCamp.CampTime:F3}, warning={antiCamp.IsWarningActive}, " +
                  $"alertCount={antiCamp.AlertTriggerCount}, " +
                  $"audioPlaying={audioPresentation != null && audioPresentation.AudioSource != null && audioPresentation.AudioSource.isPlaying}.");
    }

    private static string ReadProjectFile(string relativePath)
    {
        return File.ReadAllText(Path.GetFullPath(relativePath));
    }

    private static void Require(bool condition, string failure, ICollection<string> failures)
    {
        if (!condition) failures.Add(failure);
    }

    private static bool Approximately(float left, float right, float tolerance = 0.001f)
    {
        return Mathf.Abs(left - right) <= tolerance;
    }

    private readonly struct WeaponSnapshot
    {
        public WeaponSnapshot(
            int currentCharges,
            int maxCharges,
            bool isReloading,
            float reloadProgress,
            bool weaponEnabled,
            bool fireInputEnabled,
            bool reloadInputEnabled,
            PropHuntTestRole role,
            bool weaponActive,
            float cooldownRemaining)
        {
            CurrentCharges = currentCharges;
            MaxCharges = maxCharges;
            IsReloading = isReloading;
            ReloadProgress = reloadProgress;
            WeaponEnabled = weaponEnabled;
            FireInputEnabled = fireInputEnabled;
            ReloadInputEnabled = reloadInputEnabled;
            Role = role;
            WeaponActive = weaponActive;
            CooldownRemaining = cooldownRemaining;
        }

        private int CurrentCharges { get; }
        private int MaxCharges { get; }
        private bool IsReloading { get; }
        private float ReloadProgress { get; }
        private bool WeaponEnabled { get; }
        private bool FireInputEnabled { get; }
        private bool ReloadInputEnabled { get; }
        private PropHuntTestRole Role { get; }
        private bool WeaponActive { get; }
        private float CooldownRemaining { get; }

        public bool EquivalentTo(WeaponSnapshot other)
        {
            return CurrentCharges == other.CurrentCharges &&
                   MaxCharges == other.MaxCharges &&
                   IsReloading == other.IsReloading &&
                   Approximately(ReloadProgress, other.ReloadProgress, 0.001f) &&
                   WeaponEnabled == other.WeaponEnabled &&
                   FireInputEnabled == other.FireInputEnabled &&
                   ReloadInputEnabled == other.ReloadInputEnabled &&
                   Role == other.Role &&
                   WeaponActive == other.WeaponActive &&
                   Approximately(CooldownRemaining, other.CooldownRemaining, 0.01f);
        }

        public override string ToString()
        {
            return $"charges={CurrentCharges}/{MaxCharges}, reloading={IsReloading}, progress={ReloadProgress:F3}, " +
                   $"weaponEnabled={WeaponEnabled}, weaponActive={WeaponActive}, fireInput={FireInputEnabled}, " +
                   $"reloadInput={ReloadInputEnabled}, role={Role}, cooldown={CooldownRemaining:F3}";
        }
    }
}
