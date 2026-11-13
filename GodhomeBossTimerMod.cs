using System;
using System.Collections.Generic;
using System.Reflection;
using Modding;

namespace GodhomeBossTimerMod
{
    /// <summary>
    /// Standalone Hollow Knight mod for timing Hall of Gods boss challenges.
    /// </summary>
    public class GodhomeBossTimer : Mod, IGlobalSettings<BossTimerSettings>, ITogglableMod, IMenuMod
    {
        public static GodhomeBossTimer Instance { get; private set; }

        public BossTimerSettings Settings { get; private set; } = new BossTimerSettings();

        private static readonly string[] OnOffValues = { "Off", "On" };
        private static readonly string[] KeyOptions =
        {
            "None",
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8",
            "F9", "F10", "F11", "F12",
            "Keypad0", "Keypad1", "Keypad2", "Keypad3", "Keypad4",
            "Keypad5", "Keypad6", "Keypad7", "Keypad8", "Keypad9"
        };
        private static readonly string[] HudCornerOptions =
        {
            "Bottom-Left", "Top-Left", "Bottom-Right", "Top-Right"
        };
        private static readonly string[] TimerFontOptions =
        {
            "24", "28", "32", "36", "40", "48", "56", "64", "72"
        };
        private static readonly int[] TimerFontSizes =
        {
            24, 28, 32, 36, 40, 48, 56, 64, 72
        };
        private static readonly string[] ResetActionOptions = { "No", "Reset" };

        private BossTimerRuntime _runtime;
        private bool _hooked;

        public GodhomeBossTimer() : base("Godhome Boss Timer")
        {
        }

        public override string GetVersion() =>
            Assembly.GetExecutingAssembly().GetName().Version.ToString();

        public bool ToggleButtonInsideMenu => true;

        public void OnLoadGlobal(BossTimerSettings settings)
        {
            if (settings != null)
            {
                Settings = settings;
            }

            RefreshHotkeys();
        }

        public BossTimerSettings OnSaveGlobal() => Settings;

        public override void Initialize()
        {
            if (Instance != null)
            {
                return;
            }

            Instance = this;
            _runtime = new BossTimerRuntime(Settings, Log, SaveGlobalSettings);
            _runtime.Initialize();

            On.BossChallengeUI.LoadBoss_int_bool += OnBossChallengeLoadBoss;
            On.GameManager.WarpToDreamGate += OnWarpToDreamGate;
            On.HeroController.EnterSceneDreamGate += OnEnterSceneDreamGate;
            ModHooks.HeroUpdateHook += OnHeroUpdate;
            ModHooks.SceneChanged += OnSceneChanged;
            _hooked = true;
            RefreshHotkeys();

            Log($"Godhome Boss Timer v{GetVersion()} initialized.");
        }

        public void Unload()
        {
            if (_hooked)
            {
                On.BossChallengeUI.LoadBoss_int_bool -= OnBossChallengeLoadBoss;
                On.GameManager.WarpToDreamGate -= OnWarpToDreamGate;
                On.HeroController.EnterSceneDreamGate -= OnEnterSceneDreamGate;
                ModHooks.HeroUpdateHook -= OnHeroUpdate;
                ModHooks.SceneChanged -= OnSceneChanged;
                _hooked = false;
            }

            if (_runtime != null)
            {
                _runtime.Dispose();
                _runtime = null;
            }

            Instance = null;
        }

        public List<IMenuMod.MenuEntry> GetMenuData(IMenuMod.MenuEntry? toggleButtonEntry)
        {
            var entries = new List<IMenuMod.MenuEntry>();

            if (toggleButtonEntry.HasValue)
            {
                entries.Add(toggleButtonEntry.Value);
            }

            entries.Add(
                new IMenuMod.MenuEntry(
                    "Show Timer",
                    OnOffValues,
                    "Show the Hall of Gods single-boss timer in Godhome.",
                    index => Settings.TimerEnabled = index == 1,
                    () => Settings.TimerEnabled ? 1 : 0
                )
            );

            entries.Add(
                new IMenuMod.MenuEntry(
                    "P5 Health (Ascended)",
                    OnOffValues,
                    "Use Pantheon/Attuned boss HP in Ascended challenges. Ignored while GodSeeker+ P5 Health is enabled.",
                    index => Settings.PantheonHealth = index == 1,
                    () => Settings.PantheonHealth ? 1 : 0
                )
            );

            entries.Add(
                new IMenuMod.MenuEntry(
                    "Pantheon Vengefly Form",
                    OnOffValues,
                    "Use Pantheon 1/5's native first-boss arrival animation and movement lock in Hall of Gods Vengefly King.",
                    index => Settings.PantheonVengeflyIntro = index == 1,
                    () => Settings.PantheonVengeflyIntro ? 1 : 0
                )
            );

            entries.Add(
                new IMenuMod.MenuEntry(
                    "Pause Key",
                    KeyOptions,
                    "Key pressed during a boss run to pause or resume the timer.",
                    index =>
                    {
                        Settings.TimerPauseKey = KeyOptions[index];
                        RefreshHotkeys();
                    },
                    () => GetKeyIndex(Settings.TimerPauseKey, "F8")
                )
            );

            entries.Add(
                new IMenuMod.MenuEntry(
                    "Reset Key",
                    KeyOptions,
                    "Key pressed during a boss run to restart it from zero.",
                    index =>
                    {
                        Settings.TimerResetKey = KeyOptions[index];
                        RefreshHotkeys();
                    },
                    () => GetKeyIndex(Settings.TimerResetKey, "F9")
                )
            );

            entries.Add(
                new IMenuMod.MenuEntry(
                    "Layout Key",
                    KeyOptions,
                    "Key that opens layout mode. Arrow keys move the timer and PageUp/PageDown resize its font.",
                    index =>
                    {
                        Settings.TimerLayoutKey = KeyOptions[index];
                        RefreshHotkeys();
                    },
                    () => GetKeyIndex(Settings.TimerLayoutKey, "F10")
                )
            );

            entries.Add(
                new IMenuMod.MenuEntry(
                    "Font Type",
                    TimerFontCatalog.Labels,
                    "Built-in game font used by the timer, PB, and status rows.",
                    index => Settings.TimerFontFamily =
                        TimerFontCatalog.ResourceNames[index],
                    () => TimerFontCatalog.GetIndex(Settings.TimerFontFamily)
                )
            );

            entries.Add(
                new IMenuMod.MenuEntry(
                    "Font Size",
                    TimerFontOptions,
                    "Main timer text size; the PB and status rows scale with it.",
                    index => Settings.TimerFontSize = TimerFontSizes[index],
                    GetTimerFontIndex
                )
            );

            entries.Add(
                new IMenuMod.MenuEntry(
                    "Text Outline",
                    OnOffValues,
                    "Draw a dark outline around all timer rows. Turn off on bright backgrounds to remove the shadow/ghost effect.",
                    index => Settings.TimerOutline = index == 1,
                    () => Settings.TimerOutline ? 1 : 0
                )
            );

            entries.Add(
                new IMenuMod.MenuEntry(
                    "HUD Corner",
                    HudCornerOptions,
                    "Initial screen corner for the timer. Use layout mode for pixel-level placement.",
                    index =>
                    {
                        if (_runtime != null)
                        {
                            _runtime.ApplyHudCorner(index);
                        }
                    },
                    () => _runtime == null ? 0 : _runtime.GetHudCornerIndex()
                )
            );

            entries.Add(
                new IMenuMod.MenuEntry(
                    "Reset Current PB",
                    ResetActionOptions,
                    "Clear the PB for the last Hall of Gods boss and difficulty entered.",
                    index =>
                    {
                        if (index == 1 && _runtime != null)
                        {
                            _runtime.ClearCurrentPersonalBest();
                        }
                    },
                    () => 0
                )
            );

            entries.Add(
                new IMenuMod.MenuEntry(
                    "Reset All Timer Records",
                    ResetActionOptions,
                    "Clear all PB, previous-run, and personal-worst records.",
                    index =>
                    {
                        if (index == 1 && _runtime != null)
                        {
                            _runtime.ClearAllPersonalBests();
                        }
                    },
                    () => 0
                )
            );

            return entries;
        }

        private void OnHeroUpdate()
        {
            if (_runtime != null)
            {
                _runtime.OnHeroUpdate();
            }
        }

        private void OnBossChallengeLoadBoss(
            On.BossChallengeUI.orig_LoadBoss_int_bool orig,
            BossChallengeUI self,
            int level,
            bool doHideAnim)
        {
            if (_runtime != null)
            {
                _runtime.MarkHallChallengeSelected(level);
            }

            orig(self, level, doHideAnim);
        }

        private void OnWarpToDreamGate(
            On.GameManager.orig_WarpToDreamGate orig,
            GameManager self)
        {
            if (_runtime != null)
            {
                _runtime.OnDreamGateUsed();
            }

            orig(self);
        }

        private void OnEnterSceneDreamGate(
            On.HeroController.orig_EnterSceneDreamGate orig,
            HeroController self)
        {
            if (_runtime != null)
            {
                _runtime.OnDreamGateUsed();
            }

            orig(self);
        }

        private void OnSceneChanged(string targetScene)
        {
            if (_runtime != null)
            {
                _runtime.OnSceneChanged(targetScene);
            }
        }

        private void RefreshHotkeys()
        {
            if (_runtime != null)
            {
                _runtime.RefreshHotkeys();
            }
        }

        private static int GetKeyIndex(string configuredKey, string fallbackKey)
        {
            int index = Array.IndexOf(KeyOptions, configuredKey);
            if (index >= 0)
            {
                return index;
            }

            index = Array.IndexOf(KeyOptions, fallbackKey);
            return index < 0 ? 0 : index;
        }

        private int GetTimerFontIndex()
        {
            int bestIndex = 0;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < TimerFontSizes.Length; i++)
            {
                int distance = Math.Abs(TimerFontSizes[i] - Settings.TimerFontSize);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }
    }
}
