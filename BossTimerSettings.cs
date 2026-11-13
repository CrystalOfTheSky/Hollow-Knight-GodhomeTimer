namespace GodhomeBossTimerMod
{
    /// <summary>
    /// Persistent settings for the standalone Godhome boss timer.
    /// </summary>
    public class BossTimerSettings
    {
        public BossTimerSettings()
        {
            TimerPersonalBests =
                new System.Collections.Generic.Dictionary<string, long>();
            TimerPreviousRuns =
                new System.Collections.Generic.Dictionary<string, long>();
            TimerPersonalWorsts =
                new System.Collections.Generic.Dictionary<string, long>();
        }

        /// <summary>Whether the timer HUD is shown in Godhome.</summary>
        public bool TimerEnabled { get; set; } = true;

        /// <summary>
        /// Use Attuned (Pantheon) boss health during Ascended Hall of Gods
        /// challenges. This is ignored while GodSeeker+ P5 Health is active.
        /// </summary>
        public bool PantheonHealth { get; set; } = false;

        /// <summary>
        /// Make a Hall of Gods Vengefly King challenge use the native Pantheon
        /// first-boss arrival animation and movement lock.
        /// </summary>
        public bool PantheonVengeflyIntro { get; set; } = false;

        /// <summary>
        /// Key used to pause/resume the active Hall of Gods run. Set to "None"
        /// to disable. Any UnityEngine.KeyCode name is accepted in the JSON.
        /// </summary>
        public string TimerPauseKey { get; set; } = "F8";

        /// <summary>Key used to restart or clear the current Hall of Gods run.</summary>
        public string TimerResetKey { get; set; } = "F9";

        /// <summary>
        /// Key used to enter/leave timer HUD layout mode. In layout mode, use
        /// the arrow keys to move the timer and PageUp/PageDown to change size.
        /// </summary>
        public string TimerLayoutKey { get; set; } = "F10";

        /// <summary>Main timer text size. The PB and status rows scale with it.</summary>
        public int TimerFontSize { get; set; } = 40;

        /// <summary>
        /// Built-in font resource used by the timer HUD. Supported values are
        /// TrajanPro-Bold, TrajanPro-Regular, Perpetua and
        /// NotoSerifCJKsc-Regular.
        /// </summary>
        public string TimerFontFamily { get; set; } = "TrajanPro-Bold";

        /// <summary>
        /// Draw a dark outline around timer text. Turning this off avoids the
        /// multi-directional outline looking like a ghost on bright backgrounds.
        /// </summary>
        public bool TimerOutline { get; set; } = false;

        /// <summary>Horizontal anchor of the timer HUD (0 = left, 1 = right).</summary>
        public float TimerAnchorX { get; set; } = 0f;

        /// <summary>Vertical anchor of the timer HUD (0 = bottom, 1 = top).</summary>
        public float TimerAnchorY { get; set; } = 0f;

        /// <summary>Pixel offset from the timer anchor.</summary>
        public float TimerOffsetX { get; set; } = 20f;

        /// <summary>Pixel offset from the timer anchor.</summary>
        public float TimerOffsetY { get; set; } = 20f;

        /// <summary>
        /// Personal bests for Hall of Gods boss challenges, in milliseconds.
        /// Keys use the form "GG_Boss_Scene|level" where level is
        /// 0 = Attuned, 1 = Ascended, and 2 = Radiant.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, long> TimerPersonalBests { get; set; }

        /// <summary>Most recent completed valid time for each boss/difficulty.</summary>
        public System.Collections.Generic.Dictionary<string, long> TimerPreviousRuns { get; set; }

        /// <summary>Slowest completed valid time for each boss/difficulty.</summary>
        public System.Collections.Generic.Dictionary<string, long> TimerPersonalWorsts { get; set; }
    }
}
