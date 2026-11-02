using MelonLoader;
using UnityEngine;

namespace VaudevilleAccess.Native
{
    /// <summary>
    /// Turns vsync off. Unity answers screen-reader (UI Automation) requests once per frame, so frame rate IS
    /// screen-reader latency. With the game's vSyncCount=1, frames wait on the display; on a machine whose display is
    /// off or idle (common for blind players, here an HDMI AV receiver) that was a steady 12.9 fps = ~77 ms per UIA
    /// read and multi-second announcements, on hardware that renders this game at ~450 fps.
    ///
    /// Only vsync is changed. The game's own frame-rate target (none: -1) is left alone, by decision (2026-11-02): a
    /// frame cap is not a setting the game exposes, so changing it would be untested territory. Measured uncapped:
    /// about 2 ms per UIA read. Re-applied if the game turns vsync back on.
    /// </summary>
    public sealed class FrameRateUnlock
    {
        private readonly MelonLogger.Instance _log;
        private readonly MelonPreferences_Entry<bool> _enabled;
        private int _reapplied;

        public FrameRateUnlock(MelonLogger.Instance log, MelonPreferences_Category prefs)
        {
            _log = log;
            _enabled = prefs.CreateEntry("DisableVsync", true,
                description: "Turn vsync off. Screen readers get answers once per frame, so a vsync-throttled game "
                           + "(for example with the monitor off) makes every announcement slow.");
        }

        public void Tick()
        {
            if (!_enabled.Value || QualitySettings.vSyncCount == 0) return;
            int old = QualitySettings.vSyncCount;
            QualitySettings.vSyncCount = 0;
            if (_reapplied++ < 10) // the game may fight it; say so, but don't flood the log
                _log.Msg($"[FrameRate] vsync off (was vSyncCount={old}); frame-rate target left at {Application.targetFrameRate}.");
        }
    }
}
