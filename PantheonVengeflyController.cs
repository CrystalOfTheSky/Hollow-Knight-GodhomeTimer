using System;
using System.Reflection;
using MonoMod.RuntimeDetour;

namespace GodhomeBossTimerMod
{
    /// <summary>
    /// Makes the Hall of Gods Vengefly King scene take the native first-boss
    /// Pantheon path without pretending that a real BossSequence exists.
    /// </summary>
    internal sealed class PantheonVengeflyController : IDisposable
    {
        private const string VengeflyScene = "GG_Vengefly_V";

        private delegate bool orig_IsTrue(GGCheckIfBossSequence self);
        private delegate bool hook_IsTrue(
            orig_IsTrue orig,
            GGCheckIfBossSequence self
        );
        private delegate bool orig_FirstBossIsTrue(
            GGCheckIfFirstBossScene self
        );
        private delegate bool hook_FirstBossIsTrue(
            orig_FirstBossIsTrue orig,
            GGCheckIfFirstBossScene self
        );

        private readonly BossTimerSettings _settings;
        private readonly Action<string> _log;

        private Hook _bossSequenceCheckHook;
        private Hook _firstBossCheckHook;
        private bool _active;

        public PantheonVengeflyController(
            BossTimerSettings settings,
            Action<string> log)
        {
            _settings = settings;
            _log = log;
        }

        public void Initialize()
        {
            try
            {
                MethodInfo getter = typeof(GGCheckIfBossSequence)
                    .GetProperty(nameof(GGCheckIfBossSequence.IsTrue))
                    .GetGetMethod();
                _bossSequenceCheckHook = new Hook(
                    getter,
                    new hook_IsTrue(OverrideBossSequenceCheck)
                );
                MethodInfo firstBossGetter = typeof(GGCheckIfFirstBossScene)
                    .GetProperty(nameof(GGCheckIfFirstBossScene.IsTrue))
                    .GetGetMethod();
                _firstBossCheckHook = new Hook(
                    firstBossGetter,
                    new hook_FirstBossIsTrue(OverrideFirstBossCheck)
                );
            }
            catch (Exception ex)
            {
                _log($"Failed to install Pantheon Vengefly intro hook: {ex}");
                Dispose();
            }
        }

        public void PrepareForAttempt(string sceneName)
        {
            _active =
                _settings.PantheonVengeflyIntro &&
                sceneName == VengeflyScene &&
                !BossSequenceController.IsInSequence;

            if (_active)
            {
                _log(
                    "Pantheon Vengefly form enabled: using the native " +
                    "first-boss arrival animation and movement lock."
                );
            }
        }

        public void EndAttempt()
        {
            _active = false;
        }

        public void Dispose()
        {
            EndAttempt();
            if (_bossSequenceCheckHook != null)
            {
                _bossSequenceCheckHook.Dispose();
                _bossSequenceCheckHook = null;
            }
            if (_firstBossCheckHook != null)
            {
                _firstBossCheckHook.Dispose();
                _firstBossCheckHook = null;
            }
        }

        private bool OverrideBossSequenceCheck(
            orig_IsTrue orig,
            GGCheckIfBossSequence self)
        {
            bool original = orig(self);
            if (original ||
                !_active ||
                !_settings.PantheonVengeflyIntro ||
                BossSequenceController.IsInSequence ||
                BossSceneController.Instance == null ||
                BossSceneController.Instance.gameObject.scene.name != VengeflyScene)
            {
                return original;
            }

            return true;
        }

        private bool OverrideFirstBossCheck(
            orig_FirstBossIsTrue orig,
            GGCheckIfFirstBossScene self)
        {
            bool original = orig(self);
            if (original ||
                !_active ||
                !_settings.PantheonVengeflyIntro ||
                BossSequenceController.IsInSequence ||
                BossSceneController.Instance == null ||
                BossSceneController.Instance.gameObject.scene.name != VengeflyScene)
            {
                return original;
            }

            return true;
        }
    }
}
