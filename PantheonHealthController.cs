using System;
using System.Reflection;
using Modding;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace GodhomeBossTimerMod
{
    /// <summary>
    /// Replicates GodSeeker+ P5 Health for Ascended Hall of Gods runs while
    /// preserving the normal Ascended damage multiplier.
    /// </summary>
    internal sealed class PantheonHealthController : IDisposable
    {
        private const string GodSeekerPlusAssemblyName = "GodSeekerPlus";
        private const string GodSeekerPlusP5ModuleName = "P5Health";

        private delegate int orig_BossLevel(BossSceneController self);
        private delegate int hook_BossLevel(
            orig_BossLevel orig,
            BossSceneController self
        );

        private readonly BossTimerSettings _settings;
        private readonly Action<string> _log;

        private Hook _bossLevelHook;
        private bool _active;
        private bool _loggedActive;
        private float _expiresAt;

        public PantheonHealthController(
            BossTimerSettings settings,
            Action<string> log)
        {
            _settings = settings;
            _log = log;
        }

        public bool IsActive
        {
            get
            {
                RefreshExpiry();
                return _active;
            }
        }

        public void Initialize()
        {
            try
            {
                MethodInfo getter = typeof(BossSceneController)
                    .GetProperty(nameof(BossSceneController.BossLevel))
                    .GetGetMethod();
                _bossLevelHook = new Hook(
                    getter,
                    new hook_BossLevel(OverrideBossLevel)
                );
                ModHooks.TakeDamageHook += OnTakeDamage;
            }
            catch (Exception ex)
            {
                _log($"Failed to install P5 Health hook: {ex}");
                Dispose();
            }
        }

        public bool PrepareForChallenge(int level)
        {
            if (level != 1)
            {
                Deactivate();
                return false;
            }

            if (!_settings.TimerEnabled || !_settings.PantheonHealth)
            {
                Deactivate();
                return false;
            }

            if (IsGodSeekerPlusP5HealthActive())
            {
                Deactivate();
                _log(
                    "P5 Health skipped: GodSeeker+ P5 Health is active. " +
                    "Disable it in GodSeeker+ before enabling this mod's option."
                );
                return false;
            }

            _active = true;
            _expiresAt = UnityEngine.Time.realtimeSinceStartup + 30f;
            if (!_loggedActive)
            {
                _loggedActive = true;
                _log(
                    "P5 Health prepared for Ascended challenge: " +
                    "boss HP will use Attuned/Pantheon values; " +
                    "Ascended player damage remains unchanged."
                );
            }

            return true;
        }

        public void BeginAttempt(int level)
        {
            if (PrepareForChallenge(level))
            {
                _expiresAt = float.PositiveInfinity;
            }
        }

        public void ApplySceneSpecialCase(string sceneName)
        {
            if (!IsActive || sceneName != "GG_Markoth_V")
            {
                return;
            }

            UnityEngine.SceneManagement.Scene scene =
                UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            }

            GameObject[] roots = scene.GetRootGameObjects();
            GameObject warrior = null;
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null && roots[i].name == "Warrior")
                {
                    warrior = roots[i];
                    break;
                }
            }

            Transform markoth = warrior == null
                ? null
                : warrior.transform.Find("Ghost Warrior Markoth");
            HealthManager health = markoth == null
                ? null
                : markoth.GetComponent<HealthManager>();
            if (health == null)
            {
                _log("P5 Health: ascended Markoth health override target was not found.");
                return;
            }

            health.hp = 650;
            BossSceneController.ReportHealth(health, 650, 650, forceAdd: true);
            _log("P5 Health: applied ascended Markoth's 650 HP exception.");
        }

        public void EndAttempt()
        {
            Deactivate();
        }

        public void Dispose()
        {
            Deactivate();
            ModHooks.TakeDamageHook -= OnTakeDamage;

            if (_bossLevelHook != null)
            {
                _bossLevelHook.Dispose();
                _bossLevelHook = null;
            }
        }

        private void Deactivate()
        {
            _active = false;
            _loggedActive = false;
            _expiresAt = 0f;
        }

        private void RefreshExpiry()
        {
            if (!_active ||
                float.IsPositiveInfinity(_expiresAt) ||
                UnityEngine.Time.realtimeSinceStartup <= _expiresAt)
            {
                return;
            }

            Deactivate();
        }

        private int OverrideBossLevel(
            orig_BossLevel orig,
            BossSceneController self)
        {
            int level = orig(self);
            return IsActive && level == 1 ? 0 : level;
        }

        private int OnTakeDamage(ref int hazardType, int damage)
        {
            if (!IsActive || damage <= 0)
            {
                return damage;
            }

            return damage * 2;
        }

        private static bool IsGodSeekerPlusP5HealthActive()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly.GetName().Name != GodSeekerPlusAssemblyName)
                {
                    continue;
                }

                Type managerType = assembly.GetType(
                    "GodSeekerPlus.ModuleManager",
                    throwOnError: false
                );
                MethodInfo method = managerType == null
                    ? null
                    : managerType.GetMethod(
                        "IsModuleLoaded",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(string) },
                        null
                    );
                if (method == null)
                {
                    // GodSeeker+ is present but its state cannot be inspected.
                    // Fail closed so both mods never scale HP at the same time.
                    return true;
                }

                try
                {
                    object result = method.Invoke(
                        null,
                        new object[] { GodSeekerPlusP5ModuleName }
                    );
                    return result is bool && (bool)result;
                }
                catch
                {
                    return true;
                }
            }

            return false;
        }
    }
}
