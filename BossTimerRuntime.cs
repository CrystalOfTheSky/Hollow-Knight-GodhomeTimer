using System;
using System.Diagnostics;
using GlobalEnums;
using Modding;
using UnityEngine;
using UnityEngine.UI;

namespace GodhomeBossTimerMod
{
    /// <summary>
    /// Times individual Hall of Gods boss challenges. A run starts when the boss
    /// scene loads and is validated when BossSceneController completes its exit
    /// sequence. Only actual death or a Dreamgate departure invalidates a run;
    /// non-lethal environmental hazard respawns keep the timer running.
    /// </summary>
    internal sealed class BossTimerRuntime : IDisposable
    {
        private const int MinFontSize = 12;
        private const int MaxFontSize = 120;

        private readonly BossTimerSettings _settings;
        private readonly Action<string> _log;
        private readonly Action _saveSettings;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private readonly PantheonHealthController _pantheonHealth;
        private readonly PantheonVengeflyController _pantheonVengefly;

        private TimerRunState _state = TimerRunState.Idle;
        private string _currentScene = string.Empty;
        private string _bossScene = string.Empty;
        private string _bossKey = string.Empty;
        private string _bossDetail = "Enter a Hall of Gods fight";
        private string _status = "READY";
        private long _pbDeltaMilliseconds;
        private bool _newPersonalBest;
        private bool _fasterThanPrevious;
        private bool _slowerThanPrevious;

        private BossSceneController _bossController;
        private HeroController _hero;
        private bool _hasPauseKey;
        private KeyCode _pauseKey;
        private bool _hasResetKey;
        private KeyCode _resetKey;
        private bool _hasLayoutKey;
        private KeyCode _layoutKey;
        private bool _layoutMode;
        private bool _layoutRelinquishedControl;
        private bool _autoPausedByGame;
        private bool _waitingForLiveSplitStart;
        private bool _sceneAttempted;
        private bool _pendingHallChallenge;
        private int _pendingHallChallengeLevel;
        private float _pendingHallChallengeTime;
        private bool _pantheonHealthApplied;
        private bool _hazardRespawnLogged;

        private GameObject _canvas;
        private RectTransform _container;
        private Text _timeText;
        private Text _previousText;
        private Text _pbText;
        private Text _worstText;
        private Text _statusText;
        private Outline _timeOutline;
        private Outline _previousOutline;
        private Outline _pbOutline;
        private Outline _worstOutline;
        private Outline _statusOutline;

        private int _appliedFontSize = -1;
        private float _appliedAnchorX = float.NaN;
        private float _appliedAnchorY = float.NaN;
        private float _appliedOffsetX = float.NaN;
        private float _appliedOffsetY = float.NaN;
        private string _appliedFontFamily = string.Empty;
        private bool _appliedOutline;

        public BossTimerRuntime(
            BossTimerSettings settings,
            Action<string> log,
            Action saveSettings)
        {
            _settings = settings;
            _log = log;
            _saveSettings = saveSettings;
            _pantheonHealth = new PantheonHealthController(settings, log);
            _pantheonVengefly =
                new PantheonVengeflyController(settings, log);
        }

        public void Initialize()
        {
            NormalizeSettings();
            RefreshHotkeys();
            AttachHeroDeathHook();
            _pantheonHealth.Initialize();
            _pantheonVengefly.Initialize();
        }

        public void RefreshHotkeys()
        {
            _hasPauseKey = TryParseKey(_settings.TimerPauseKey, out _pauseKey);
            _hasResetKey = TryParseKey(_settings.TimerResetKey, out _resetKey);
            _hasLayoutKey = TryParseKey(_settings.TimerLayoutKey, out _layoutKey);
        }

        public void OnSceneChanged(string targetScene)
        {
            targetScene = targetScene ?? string.Empty;
            HandleSceneChanged(targetScene);
        }

        public void MarkHallChallengeSelected(int level)
        {
            _pendingHallChallenge = true;
            _pendingHallChallengeLevel = Mathf.Clamp(level, 0, 2);
            _pendingHallChallengeTime = Time.realtimeSinceStartup;
            _pantheonHealthApplied =
                _pantheonHealth.PrepareForChallenge(_pendingHallChallengeLevel);
            _log($"Hall of Gods challenge selected at level {_pendingHallChallengeLevel}.");
        }

        private void HandleSceneChanged(string targetScene)
        {
            if (targetScene == _currentScene)
            {
                TryStartCurrentSceneRun();
                UpdateHud();
                return;
            }

            if (IsLive &&
                !string.IsNullOrEmpty(_bossScene) &&
                targetScene != _bossScene)
            {
                string entryGate = GameManager.instance == null
                    ? string.Empty
                    : GameManager.instance.GetEntryGateName();
                _log(
                    $"[Timer detect] leaving boss scene: target={targetScene}, " +
                    $"entryGate={entryGate ?? "null"}."
                );

                bool dreamGateTransition =
                    string.Equals(
                        entryGate,
                        "dreamGate",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        entryGate,
                        "door_dreamReturn",
                        StringComparison.OrdinalIgnoreCase);
                InvalidateRun(dreamGateTransition ? "DREAMGATE" : "LEFT ARENA");
            }

            CancelLayoutMode(regainControl: false);
            DetachBossController();
            _pantheonHealth.EndAttempt();
            _pantheonHealthApplied = false;
            _currentScene = targetScene;
            _sceneAttempted = false;
            AttachHeroDeathHook();

            if (targetScene.StartsWith("GG_", StringComparison.OrdinalIgnoreCase))
            {
                BossSceneController controller = BossSceneController.Instance;
                bool hallIntent =
                    _pendingHallChallenge &&
                    Time.realtimeSinceStartup - _pendingHallChallengeTime < 30f;
                _log(
                    $"[Timer detect] scene={targetScene}, " +
                    $"bossController={(controller == null ? "null" : "present")}, " +
                    $"inSequence={BossSequenceController.IsInSequence}, " +
                    $"hallSelection={hallIntent}."
                );
            }

            if (targetScene == Constants.MENU_SCENE)
            {
                ClearForMainMenu();
            }
            else
            {
                TryStartCurrentSceneRun();
            }

            _pantheonHealth.ApplySceneSpecialCase(targetScene);
            UpdateHud();
        }

        public void OnHeroUpdate()
        {
            SyncActiveScene();
            TryStartCurrentSceneRun();
            TryStartStopwatchAtLiveSplitBoundary();
            UpdateAutomaticPause();
            HandleInput();
            MonitorHeroDeath();
            UpdateHud();
        }

        public void ApplyHudCorner(int index)
        {
            switch (index)
            {
                case 1:
                    _settings.TimerAnchorX = 0f;
                    _settings.TimerAnchorY = 1f;
                    _settings.TimerOffsetX = 20f;
                    _settings.TimerOffsetY = -20f;
                    break;

                case 2:
                    _settings.TimerAnchorX = 1f;
                    _settings.TimerAnchorY = 0f;
                    _settings.TimerOffsetX = -20f;
                    _settings.TimerOffsetY = 20f;
                    break;

                case 3:
                    _settings.TimerAnchorX = 1f;
                    _settings.TimerAnchorY = 1f;
                    _settings.TimerOffsetX = -20f;
                    _settings.TimerOffsetY = -20f;
                    break;

                default:
                    _settings.TimerAnchorX = 0f;
                    _settings.TimerAnchorY = 0f;
                    _settings.TimerOffsetX = 20f;
                    _settings.TimerOffsetY = 20f;
                    break;
            }

            ApplyHudLayout();
        }

        public int GetHudCornerIndex()
        {
            if (_settings.TimerAnchorX < 0.5f && _settings.TimerAnchorY > 0.5f)
            {
                return 1;
            }

            if (_settings.TimerAnchorX > 0.5f && _settings.TimerAnchorY < 0.5f)
            {
                return 2;
            }

            if (_settings.TimerAnchorX > 0.5f && _settings.TimerAnchorY > 0.5f)
            {
                return 3;
            }

            return 0;
        }

        public void ClearCurrentPersonalBest()
        {
            NormalizeSettings();

            if (string.IsNullOrEmpty(_bossKey))
            {
                _log("Timer PB reset skipped: no current Hall of Gods boss is selected.");
                return;
            }

            if (_settings.TimerPersonalBests.Remove(_bossKey))
            {
                _log($"Timer PB cleared for {_bossKey}.");
                _saveSettings();
            }

            UpdateHud();
        }

        public void ClearAllPersonalBests()
        {
            NormalizeSettings();
            _settings.TimerPersonalBests.Clear();
            _settings.TimerPreviousRuns.Clear();
            _settings.TimerPersonalWorsts.Clear();
            _saveSettings();
            _log("All Hall of Gods timer records cleared.");
            UpdateHud();
        }

        public void Dispose()
        {
            CancelLayoutMode(regainControl: true);
            DetachBossController();
            DetachHeroDeathHook();
            _pantheonHealth.Dispose();
            _pantheonVengefly.Dispose();
            DestroyHud();
        }

        private void ClearForMainMenu()
        {
            _stopwatch.Reset();
            _state = TimerRunState.Idle;
            _bossScene = string.Empty;
            _bossKey = string.Empty;
            _bossDetail = "Enter a Hall of Gods fight";
            _status = "READY";
            _newPersonalBest = false;
            _fasterThanPrevious = false;
            _slowerThanPrevious = false;
            _pbDeltaMilliseconds = 0;
            _autoPausedByGame = false;
            _waitingForLiveSplitStart = false;
            _sceneAttempted = false;
            _pendingHallChallenge = false;
            _pantheonHealth.EndAttempt();
            _pantheonHealthApplied = false;
        }

        private void NormalizeSettings()
        {
            if (_settings.TimerPersonalBests == null)
            {
                _settings.TimerPersonalBests =
                    new System.Collections.Generic.Dictionary<string, long>();
            }
            if (_settings.TimerPreviousRuns == null)
            {
                _settings.TimerPreviousRuns =
                    new System.Collections.Generic.Dictionary<string, long>();
            }
            if (_settings.TimerPersonalWorsts == null)
            {
                _settings.TimerPersonalWorsts =
                    new System.Collections.Generic.Dictionary<string, long>();
            }

            _settings.TimerFontSize = Mathf.Clamp(
                _settings.TimerFontSize,
                MinFontSize,
                MaxFontSize
            );
            _settings.TimerFontFamily =
                TimerFontCatalog.Normalize(_settings.TimerFontFamily);
            _settings.TimerAnchorX = Mathf.Clamp01(_settings.TimerAnchorX);
            _settings.TimerAnchorY = Mathf.Clamp01(_settings.TimerAnchorY);
            _settings.TimerOffsetX = Mathf.Clamp(_settings.TimerOffsetX, -4000f, 4000f);
            _settings.TimerOffsetY = Mathf.Clamp(_settings.TimerOffsetY, -4000f, 4000f);
        }

        private static bool TryParseKey(string value, out KeyCode key)
        {
            key = KeyCode.None;
            return !string.IsNullOrEmpty(value) &&
                   value != "None" &&
                   Enum.TryParse(value, out key);
        }

        private void SyncActiveScene()
        {
            string activeScene =
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (!string.IsNullOrEmpty(activeScene) && activeScene != _currentScene)
            {
                HandleSceneChanged(activeScene);
            }
        }

        private void TryStartCurrentSceneRun()
        {
            if (IsLive || _sceneAttempted || string.IsNullOrEmpty(_currentScene))
            {
                return;
            }

            BossSceneController controller;
            if (CanStartRun(_currentScene, out controller))
            {
                StartRun(_currentScene, controller);
            }
        }

        private void TryStartStopwatchAtLiveSplitBoundary()
        {
            if (!_waitingForLiveSplitStart ||
                _state != TimerRunState.Running ||
                !IsLiveSplitStartBoundaryReached())
            {
                return;
            }

            _waitingForLiveSplitStart = false;
            _stopwatch.Start();
            _status = "RUNNING";
            _log(
                $"Hall of Gods timer started at LiveSplit-style boundary: " +
                $"{_bossDetail} ({_bossKey})."
            );
        }

        private bool IsLiveSplitStartBoundaryReached()
        {
            GameManager gameManager = GameManager.instance;
            if (gameManager == null ||
                _bossController == null ||
                !_bossController.HasTransitionedIn ||
                gameManager.gameState != GameState.PLAYING ||
                gameManager.ui == null ||
                gameManager.ui.uiState != UIState.PLAYING ||
                gameManager.inputHandler == null ||
                !gameManager.inputHandler.acceptingInput)
            {
                return false;
            }

            HeroController hero = HeroController.SilentInstance;
            return hero != null &&
                   !hero.cState.transitioning &&
                   hero.transitionState != HeroTransitionState.WAITING_TO_ENTER_LEVEL;
        }

        private bool CanStartRun(string targetScene, out BossSceneController controller)
        {
            controller = null;
            if (string.IsNullOrEmpty(targetScene) ||
                !targetScene.StartsWith("GG_", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            bool hallIntent =
                _pendingHallChallenge &&
                Time.realtimeSinceStartup - _pendingHallChallengeTime < 30f;

            // Pantheons also use BossSceneController. The BossChallengeUI hook
            // is the authoritative marker for a Hall of Gods statue selection;
            // IsInSequence can remain stale in a save that previously entered a
            // Pantheon, so it is only a fallback for direct scene loads.
            if (BossSequenceController.IsInSequence && !hallIntent)
            {
                return false;
            }

            controller = BossSceneController.Instance;
            if (controller == null || controller.gameObject.scene.name != targetScene)
            {
                controller = UnityEngine.Object.FindObjectOfType<BossSceneController>();
            }

            if (controller == null || controller.gameObject.scene.name != targetScene)
            {
                controller = null;
                return false;
            }

            return true;
        }

        private void StartRun(string targetScene, BossSceneController controller)
        {
            if (controller == null)
            {
                return;
            }

            int level = _pendingHallChallenge
                ? _pendingHallChallengeLevel
                : Mathf.Clamp(controller.BossLevel, 0, 2);
            _pantheonHealth.BeginAttempt(level);
            _pantheonHealthApplied = _pantheonHealth.IsActive;
            _pantheonVengefly.PrepareForAttempt(targetScene);
            _pendingHallChallenge = false;
            _sceneAttempted = true;
            _bossController = controller;
            _bossController.OnBossSceneComplete += OnBossSceneComplete;

            _bossScene = targetScene;
            _bossKey = $"{targetScene}|{level}";
            _bossDetail = $"{FormatSceneName(targetScene)} · {FormatTier(level)}";
            _newPersonalBest = false;
            _fasterThanPrevious = false;
            _slowerThanPrevious = false;
            _pbDeltaMilliseconds = 0;
            _autoPausedByGame = false;
            _state = TimerRunState.Running;
            _waitingForLiveSplitStart = true;
            _status = "WAITING FOR START";

            _stopwatch.Reset();

            AttachHeroDeathHook();
            _log(
                $"Hall of Gods attempt prepared; waiting for LiveSplit-style " +
                $"start boundary: {_bossDetail} ({_bossKey})."
            );
        }

        private void OnBossSceneComplete()
        {
            if (!IsLive)
            {
                return;
            }

            _stopwatch.Stop();
            _waitingForLiveSplitStart = false;
            _state = TimerRunState.Completed;
            _autoPausedByGame = false;
            _pantheonHealth.EndAttempt();
            _pantheonVengefly.EndAttempt();

            long elapsedMilliseconds = (long)Math.Round(_stopwatch.Elapsed.TotalMilliseconds);
            long previousMilliseconds;
            bool hasPrevious =
                _settings.TimerPreviousRuns.TryGetValue(
                    _bossKey,
                    out previousMilliseconds) &&
                previousMilliseconds > 0;
            long oldBest;
            if (!_settings.TimerPersonalBests.TryGetValue(_bossKey, out oldBest) ||
                elapsedMilliseconds < oldBest)
            {
                _settings.TimerPersonalBests[_bossKey] = elapsedMilliseconds;
                _newPersonalBest = true;
                _pbDeltaMilliseconds = oldBest > 0
                    ? elapsedMilliseconds - oldBest
                    : 0;
                _status = "NEW PB";
            }
            else
            {
                _newPersonalBest = false;
                _pbDeltaMilliseconds = elapsedMilliseconds - oldBest;
                _status = FormatDelta(_pbDeltaMilliseconds);
            }

            _fasterThanPrevious =
                !_newPersonalBest &&
                hasPrevious &&
                elapsedMilliseconds < previousMilliseconds;
            _slowerThanPrevious =
                hasPrevious &&
                elapsedMilliseconds > previousMilliseconds;

            _settings.TimerPreviousRuns[_bossKey] = elapsedMilliseconds;

            long oldWorst;
            if (!_settings.TimerPersonalWorsts.TryGetValue(_bossKey, out oldWorst) ||
                elapsedMilliseconds > oldWorst)
            {
                _settings.TimerPersonalWorsts[_bossKey] = elapsedMilliseconds;
            }

            // Ensure new settings fields and the PB are persisted together.
            _saveSettings();

            _log(
                $"Hall of Gods timer finished: {_bossDetail}, " +
                $"{FormatTime(_stopwatch.Elapsed)}, status={_status}."
            );
            UpdateHud();
        }

        private void OnHeroDeath()
        {
            InvalidateRun("DEATH");
        }

        public void OnDreamGateUsed()
        {
            InvalidateRun("DREAMGATE");
        }

        private void MonitorHeroDeath()
        {
            if (!IsLive)
            {
                return;
            }

            HeroController hero = HeroController.SilentInstance;
            if (hero == null)
            {
                return;
            }

            if (hero.cState.dead)
            {
                InvalidateRun("DEATH");
                return;
            }

            if (hero.cState.hazardDeath)
            {
                if (!_hazardRespawnLogged)
                {
                    _hazardRespawnLogged = true;
                    _log(
                        $"Environmental hazard respawn ignored; timer remains active: " +
                        $"{FormatTime(_stopwatch.Elapsed)}."
                    );
                }
            }
            else
            {
                _hazardRespawnLogged = false;
            }
        }

        private void InvalidateRun(string reason)
        {
            if (!IsLive)
            {
                return;
            }

            CancelLayoutMode(regainControl: false);
            _pantheonHealth.EndAttempt();
            _pantheonVengefly.EndAttempt();
            _stopwatch.Stop();
            _waitingForLiveSplitStart = false;
            _state = TimerRunState.Invalid;
            _autoPausedByGame = false;
            _status = $"INVALID · {reason}";
            _newPersonalBest = false;
            _fasterThanPrevious = false;
            _slowerThanPrevious = false;
            _log(
                $"Hall of Gods timer invalidated: {reason}, " +
                $"{FormatTime(_stopwatch.Elapsed)}, boss={_bossKey}."
            );
            UpdateHud();
        }

        private void HandleInput()
        {
            if (_hasLayoutKey && Input.GetKeyDown(_layoutKey))
            {
                ToggleLayoutMode();
                return;
            }

            if (_layoutMode)
            {
                HandleLayoutInput();
                return;
            }

            if (_hasPauseKey && Input.GetKeyDown(_pauseKey))
            {
                TogglePause();
            }

            if (_hasResetKey && Input.GetKeyDown(_resetKey))
            {
                ResetRun();
            }
        }

        private void TogglePause()
        {
            if (_autoPausedByGame)
            {
                return;
            }

            if (_state == TimerRunState.Running)
            {
                _stopwatch.Stop();
                _state = TimerRunState.Paused;
                _status = "PAUSED";
                _log($"Hall of Gods timer paused at {FormatTime(_stopwatch.Elapsed)}.");
            }
            else if (_state == TimerRunState.Paused)
            {
                if (!_waitingForLiveSplitStart)
                {
                    _stopwatch.Start();
                }
                _state = TimerRunState.Running;
                _status = _waitingForLiveSplitStart
                    ? "WAITING FOR START"
                    : "RUNNING";
                _log("Hall of Gods timer resumed.");
            }
        }

        private void ResetRun()
        {
            if (_state == TimerRunState.Running || _state == TimerRunState.Paused)
            {
                _stopwatch.Reset();
                if (!_waitingForLiveSplitStart)
                {
                    _stopwatch.Start();
                }
                _autoPausedByGame = false;
                _state = TimerRunState.Running;
                _status = _waitingForLiveSplitStart
                    ? "WAITING FOR START"
                    : "RESTARTED";
                _newPersonalBest = false;
                _fasterThanPrevious = false;
                _slowerThanPrevious = false;
                _pbDeltaMilliseconds = 0;
                _log($"Hall of Gods attempt restarted: {_bossKey}.");
                return;
            }

            _stopwatch.Reset();
            _state = TimerRunState.Idle;
            _status = "READY";
            _newPersonalBest = false;
            _fasterThanPrevious = false;
            _slowerThanPrevious = false;
            _pbDeltaMilliseconds = 0;
            _autoPausedByGame = false;
            _waitingForLiveSplitStart = false;
            _log("Hall of Gods timer cleared.");
        }

        private void UpdateAutomaticPause()
        {
            bool gamePaused = GameManager.instance != null && GameManager.instance.isPaused;

            if (gamePaused)
            {
                if (_state == TimerRunState.Running && !_autoPausedByGame)
                {
                    _stopwatch.Stop();
                    _state = TimerRunState.Paused;
                    _autoPausedByGame = true;
                    _status = "PAUSED · GAME MENU";
                }

                return;
            }

            if (_autoPausedByGame && _state == TimerRunState.Paused)
            {
                if (!_waitingForLiveSplitStart)
                {
                    _stopwatch.Start();
                }
                _state = TimerRunState.Running;
                _autoPausedByGame = false;
                _status = _waitingForLiveSplitStart
                    ? "WAITING FOR START"
                    : "RUNNING";
            }
        }

        private void ToggleLayoutMode()
        {
            if (_layoutMode)
            {
                CancelLayoutMode(regainControl: true);
                _saveSettings();
                return;
            }

            if (IsLive)
            {
                _log("Timer layout adjustment is disabled during an active boss run.");
                return;
            }

            _layoutMode = true;
            EnsureHud();
            ApplyHudLayout();

            HeroController hero = HeroController.SilentInstance;
            if (hero != null &&
                GameManager.instance != null &&
                !GameManager.instance.isPaused &&
                !hero.cState.dead &&
                !hero.cState.hazardDeath)
            {
                hero.RelinquishControl();
                _layoutRelinquishedControl = true;
            }

            _log(
                "Timer layout mode enabled. Arrow keys move the HUD, " +
                "PageUp/PageDown change font size, and the layout key finishes."
            );
        }

        private void CancelLayoutMode(bool regainControl)
        {
            if (!_layoutMode)
            {
                return;
            }

            _layoutMode = false;
            if (regainControl && _layoutRelinquishedControl)
            {
                HeroController hero = HeroController.SilentInstance;
                if (hero != null && !hero.cState.dead && !hero.cState.hazardDeath)
                {
                    hero.RegainControl();
                }
            }

            _layoutRelinquishedControl = false;
            ApplyHudLayout();
        }

        private void HandleLayoutInput()
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            int step = shift ? 1 : 10;
            bool changed = false;

            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                _settings.TimerOffsetX -= step;
                changed = true;
            }

            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                _settings.TimerOffsetX += step;
                changed = true;
            }

            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                _settings.TimerOffsetY += step;
                changed = true;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                _settings.TimerOffsetY -= step;
                changed = true;
            }

            if (Input.GetKeyDown(KeyCode.PageUp))
            {
                _settings.TimerFontSize += shift ? 1 : 2;
                changed = true;
            }

            if (Input.GetKeyDown(KeyCode.PageDown))
            {
                _settings.TimerFontSize -= shift ? 1 : 2;
                changed = true;
            }

            if (changed)
            {
                NormalizeSettings();
                ApplyHudLayout();
            }
        }

        private bool IsLive =>
            _state == TimerRunState.Running || _state == TimerRunState.Paused;

        private void AttachHeroDeathHook()
        {
            HeroController hero = HeroController.SilentInstance;
            if (hero == null || hero == _hero)
            {
                return;
            }

            DetachHeroDeathHook();
            _hero = hero;
            _hero.OnDeath += OnHeroDeath;
        }

        private void DetachHeroDeathHook()
        {
            if (_hero != null)
            {
                _hero.OnDeath -= OnHeroDeath;
            }

            _hero = null;
        }

        private void DetachBossController()
        {
            if (_bossController != null)
            {
                _bossController.OnBossSceneComplete -= OnBossSceneComplete;
            }

            _bossController = null;
        }

        private bool ShouldShowHud()
        {
            if (_layoutMode)
            {
                return true;
            }

            if (!_settings.TimerEnabled ||
                string.IsNullOrEmpty(_currentScene) ||
                !_currentScene.StartsWith("GG_", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Once a run or result exists, keep the HUD visible even if stale
            // sequence state would otherwise hide an idle Godhome HUD.
            return _state != TimerRunState.Idle ||
                   _bossController != null ||
                   !BossSequenceController.IsInSequence;
        }

        private void UpdateHud()
        {
            if (!ShouldShowHud())
            {
                if (_canvas != null && _canvas.activeSelf)
                {
                    _canvas.SetActive(false);
                }

                return;
            }

            EnsureHud();
            if (_canvas == null)
            {
                return;
            }

            _canvas.SetActive(true);
            ApplyHudLayout();
            _timeText.text = FormatTime(_stopwatch.Elapsed);
            _previousText.text = BuildPreviousText();
            _pbText.text = BuildPersonalBestText();
            _worstText.text = BuildPersonalWorstText();
            _statusText.text = BuildStatusText();
            ApplyStatusColors();
        }

        private void EnsureHud()
        {
            if (_canvas != null)
            {
                return;
            }

            if (CanvasUtil.TrajanBold == null)
            {
                return;
            }

            try
            {
                _canvas = CanvasUtil.CreateCanvas(RenderMode.ScreenSpaceOverlay, 100);
                UnityEngine.Object.DontDestroyOnLoad(_canvas);

                GraphicRaycaster raycaster = _canvas.GetComponent<GraphicRaycaster>();
                if (raycaster != null)
                {
                    UnityEngine.Object.Destroy(raycaster);
                }

                GameObject containerObject = CanvasUtil.CreateBasePanel(
                    _canvas,
                    new CanvasUtil.RectData(
                        new Vector2(440f, 160f),
                        Vector2.zero,
                        Vector2.zero,
                        Vector2.zero,
                        Vector2.zero)
                );
                _container = containerObject.GetComponent<RectTransform>();
                Font timerFont = TimerFontCatalog.Resolve(_settings.TimerFontFamily);

                GameObject timeObject = CanvasUtil.CreateTextPanel(
                    containerObject,
                    "0:00.000",
                    40,
                    TextAnchor.MiddleLeft,
                    new CanvasUtil.RectData(
                        Vector2.zero,
                        Vector2.zero,
                        new Vector2(0f, 0.55f),
                        new Vector2(1f, 1f),
                        new Vector2(0.5f, 0.5f)),
                    timerFont
                );
                _timeText = timeObject.GetComponent<Text>();
                ConfigureText(_timeText, Color.white);
                _timeOutline = AddOutline(timeObject);

                GameObject previousObject = CanvasUtil.CreateTextPanel(
                    containerObject,
                    string.Empty,
                    18,
                    TextAnchor.MiddleLeft,
                    new CanvasUtil.RectData(
                        Vector2.zero,
                        Vector2.zero,
                        new Vector2(0f, 0.43f),
                        new Vector2(1f, 0.57f),
                        new Vector2(0.5f, 0.5f)),
                    timerFont
                );
                _previousText = previousObject.GetComponent<Text>();
                ConfigureText(_previousText, new Color(0.75f, 0.75f, 0.75f, 1f));
                _previousOutline = AddOutline(previousObject);

                GameObject pbObject = CanvasUtil.CreateTextPanel(
                    containerObject,
                    string.Empty,
                    18,
                    TextAnchor.MiddleLeft,
                    new CanvasUtil.RectData(
                        Vector2.zero,
                        Vector2.zero,
                        new Vector2(0f, 0.31f),
                        new Vector2(1f, 0.45f),
                        new Vector2(0.5f, 0.5f)),
                    timerFont
                );
                _pbText = pbObject.GetComponent<Text>();
                ConfigureText(_pbText, new Color(0.9f, 0.9f, 0.9f, 1f));
                _pbOutline = AddOutline(pbObject);

                GameObject worstObject = CanvasUtil.CreateTextPanel(
                    containerObject,
                    string.Empty,
                    18,
                    TextAnchor.MiddleLeft,
                    new CanvasUtil.RectData(
                        Vector2.zero,
                        Vector2.zero,
                        new Vector2(0f, 0.19f),
                        new Vector2(1f, 0.33f),
                        new Vector2(0.5f, 0.5f)),
                    timerFont
                );
                _worstText = worstObject.GetComponent<Text>();
                ConfigureText(_worstText, new Color(0.75f, 0.75f, 0.75f, 1f));
                _worstOutline = AddOutline(worstObject);

                GameObject statusObject = CanvasUtil.CreateTextPanel(
                    containerObject,
                    string.Empty,
                    16,
                    TextAnchor.MiddleLeft,
                    new CanvasUtil.RectData(
                        Vector2.zero,
                        Vector2.zero,
                        new Vector2(0f, 0f),
                        new Vector2(1f, 0.21f),
                        new Vector2(0.5f, 0.5f)),
                    timerFont
                );
                _statusText = statusObject.GetComponent<Text>();
                ConfigureText(_statusText, new Color(0.8f, 0.8f, 0.8f, 1f));
                _statusOutline = AddOutline(statusObject);

                _appliedFontSize = -1;
                _appliedAnchorX = float.NaN;
                _appliedAnchorY = float.NaN;
                _appliedOffsetX = float.NaN;
                _appliedOffsetY = float.NaN;
                _appliedFontFamily = string.Empty;
                _appliedOutline = !_settings.TimerOutline;
                ApplyHudLayout();
            }
            catch (Exception ex)
            {
                _log($"Failed to create Hall of Gods timer HUD: {ex}");
                DestroyHud();
            }
        }

        private static void ConfigureText(Text text, Color color)
        {
            text.color = color;
            text.supportRichText = true;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private static Outline AddOutline(GameObject textObject)
        {
            Outline outline = textObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(1f, -1f);
            return outline;
        }

        private void ApplyHudLayout()
        {
            if (_container == null ||
                _timeText == null ||
                _previousText == null ||
                _pbText == null ||
                _worstText == null ||
                _statusText == null)
            {
                return;
            }

            NormalizeSettings();

            if (_appliedFontSize == _settings.TimerFontSize &&
                string.Equals(
                    _appliedFontFamily,
                    TimerFontCatalog.Normalize(_settings.TimerFontFamily),
                    StringComparison.Ordinal
                ) &&
                _appliedOutline == _settings.TimerOutline &&
                Mathf.Approximately(_appliedAnchorX, _settings.TimerAnchorX) &&
                Mathf.Approximately(_appliedAnchorY, _settings.TimerAnchorY) &&
                Mathf.Approximately(_appliedOffsetX, _settings.TimerOffsetX) &&
                Mathf.Approximately(_appliedOffsetY, _settings.TimerOffsetY))
            {
                return;
            }

            bool rightAligned = _settings.TimerAnchorX >= 0.5f;
            bool topAligned = _settings.TimerAnchorY >= 0.5f;
            TextAnchor alignment = rightAligned
                ? TextAnchor.MiddleRight
                : TextAnchor.MiddleLeft;
            Vector2 anchor = new Vector2(
                _settings.TimerAnchorX,
                _settings.TimerAnchorY
            );
            Vector2 pivot = new Vector2(
                rightAligned ? 1f : 0f,
                topAligned ? 1f : 0f
            );

            _container.anchorMin = anchor;
            _container.anchorMax = anchor;
            _container.pivot = pivot;
            _container.anchoredPosition = new Vector2(
                _settings.TimerOffsetX,
                _settings.TimerOffsetY
            );

            int mainSize = Mathf.Clamp(_settings.TimerFontSize, MinFontSize, MaxFontSize);
            int infoSize = Mathf.Max(12, Mathf.RoundToInt(mainSize * 0.46f));
            int statusSize = Mathf.Max(11, Mathf.RoundToInt(mainSize * 0.42f));
            float width = Mathf.Max(360f, mainSize * 9f);
            float height = mainSize * 1.55f + infoSize * 5.2f;

            _container.sizeDelta = new Vector2(width, height);
            _timeText.fontSize = mainSize;
            _previousText.fontSize = infoSize;
            _pbText.fontSize = infoSize;
            _worstText.fontSize = infoSize;
            _statusText.fontSize = statusSize;
            Font timerFont = TimerFontCatalog.Resolve(_settings.TimerFontFamily);
            _timeText.font = timerFont;
            _previousText.font = timerFont;
            _pbText.font = timerFont;
            _worstText.font = timerFont;
            _statusText.font = timerFont;
            ApplyOutline(_timeOutline, mainSize);
            ApplyOutline(_previousOutline, infoSize);
            ApplyOutline(_pbOutline, infoSize);
            ApplyOutline(_worstOutline, infoSize);
            ApplyOutline(_statusOutline, statusSize);
            _timeText.alignment = alignment;
            _previousText.alignment = alignment;
            _pbText.alignment = alignment;
            _worstText.alignment = alignment;
            _statusText.alignment = alignment;

            _appliedFontSize = _settings.TimerFontSize;
            _appliedAnchorX = _settings.TimerAnchorX;
            _appliedAnchorY = _settings.TimerAnchorY;
            _appliedOffsetX = _settings.TimerOffsetX;
            _appliedOffsetY = _settings.TimerOffsetY;
            _appliedFontFamily = TimerFontCatalog.Normalize(_settings.TimerFontFamily);
            _appliedOutline = _settings.TimerOutline;
        }

        private void ApplyOutline(Outline outline, int fontSize)
        {
            if (outline == null)
            {
                return;
            }

            outline.enabled = _settings.TimerOutline;
            float distance = Mathf.Max(0.55f, fontSize * 0.045f);
            outline.effectDistance = new Vector2(distance, -distance);
        }

        private string BuildPreviousText()
        {
            if (string.IsNullOrEmpty(_bossKey))
            {
                return "Previous  --:--";
            }

            long previous;
            return _settings.TimerPreviousRuns.TryGetValue(_bossKey, out previous) &&
                   previous > 0
                ? $"Previous  {FormatMilliseconds(previous)}"
                : "Previous  --:--";
        }

        private string BuildPersonalBestText()
        {
            if (string.IsNullOrEmpty(_bossKey))
            {
                return "PB  --:--";
            }

            long best;
            if (_settings.TimerPersonalBests.TryGetValue(_bossKey, out best) && best > 0)
            {
                return $"PB  {FormatMilliseconds(best)}";
            }

            return "PB  --:--";
        }

        private string BuildPersonalWorstText()
        {
            if (string.IsNullOrEmpty(_bossKey))
            {
                return "Worst  --:--";
            }

            long worst;
            if (_settings.TimerPersonalWorsts.TryGetValue(_bossKey, out worst) && worst > 0)
            {
                return $"Worst  {FormatMilliseconds(worst)}";
            }

            return "Worst  --:--";
        }

        private string BuildStatusText()
        {
            if (_layoutMode)
            {
                return BuildLayoutStatusText();
            }

            string healthMarker = _pantheonHealthApplied ? "  ·  P5 HP" : string.Empty;
            string prefix = string.IsNullOrEmpty(_bossKey)
                ? _bossDetail
                : _bossDetail + healthMarker;
            return $"{prefix}  ·  {_status}";
        }

        private string BuildLayoutStatusText()
        {
            return $"MOVE Arrows  ·  FONT PgUp/PgDn  ·  {KeyName(_layoutKey)} DONE";
        }

        private void ApplyStatusColors()
        {
            if (_layoutMode)
            {
                _timeText.color = new Color(0.55f, 0.9f, 1f, 1f);
                _statusText.color = new Color(0.55f, 0.9f, 1f, 1f);
                return;
            }

            switch (_state)
            {
                case TimerRunState.Paused:
                    _timeText.color = new Color(1f, 0.82f, 0.35f, 1f);
                    _statusText.color = new Color(1f, 0.82f, 0.35f, 1f);
                    break;

                case TimerRunState.Completed:
                    Color completedColor;
                    if (_newPersonalBest)
                    {
                        completedColor = new Color(1f, 0.92f, 0.30f, 1f);
                    }
                    else if (_fasterThanPrevious)
                    {
                        completedColor = new Color(0.55f, 1f, 0.62f, 1f);
                    }
                    else if (_slowerThanPrevious)
                    {
                        completedColor = new Color(1f, 0.70f, 0.66f, 1f);
                    }
                    else
                    {
                        completedColor = Color.white;
                    }

                    _timeText.color = completedColor;
                    _statusText.color = completedColor;
                    break;

                case TimerRunState.Invalid:
                    _timeText.color = new Color(0.72f, 0.045f, 0.065f, 1f);
                    _statusText.color = new Color(0.72f, 0.045f, 0.065f, 1f);
                    break;

                default:
                    _timeText.color = Color.white;
                    _statusText.color = new Color(0.8f, 0.8f, 0.8f, 1f);
                    break;
            }
        }

        private static string FormatSceneName(string sceneName)
        {
            string result = sceneName ?? string.Empty;
            if (result.StartsWith("GG_", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Substring(3);
            }

            return result.Replace('_', ' ');
        }

        private static string FormatTier(int level)
        {
            switch (level)
            {
                case 1:
                    return "Ascended";
                case 2:
                    return "Radiant";
                default:
                    return "Attuned";
            }
        }

        private static string FormatTime(TimeSpan time)
        {
            long totalMilliseconds = (long)Math.Floor(time.TotalMilliseconds);
            return FormatMilliseconds(totalMilliseconds);
        }

        private static string FormatMilliseconds(long milliseconds)
        {
            milliseconds = Math.Max(0L, milliseconds);
            long minutes = milliseconds / 60000L;
            long seconds = milliseconds / 1000L % 60L;
            long millis = milliseconds % 1000L;
            return string.Format("{0}:{1:00}.{2:000}", minutes, seconds, millis);
        }

        private static string FormatDelta(long milliseconds)
        {
            string sign = milliseconds >= 0 ? "+" : "-";
            return $"FINISHED  {sign}{FormatMilliseconds(Math.Abs(milliseconds))}";
        }

        private static string KeyName(KeyCode key)
        {
            return key == KeyCode.None ? "Layout key" : key.ToString();
        }

        private void DestroyHud()
        {
            if (_canvas != null)
            {
                UnityEngine.Object.Destroy(_canvas);
            }

            _canvas = null;
            _container = null;
            _timeText = null;
            _previousText = null;
            _pbText = null;
            _worstText = null;
            _statusText = null;
            _timeOutline = null;
            _previousOutline = null;
            _pbOutline = null;
            _worstOutline = null;
            _statusOutline = null;
            _appliedFontSize = -1;
            _appliedFontFamily = string.Empty;
        }

        private enum TimerRunState
        {
            Idle,
            Running,
            Paused,
            Completed,
            Invalid
        }
    }
}
