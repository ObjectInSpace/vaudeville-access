using MelonLoader;
using UnityEngine;

namespace VaudevilleAccess.Native
{
    /// <summary>
    /// Turns vsync off and caps the frame rate instead. Unity answers screen-reader (UI Automation) requests once
    /// per frame, so frame rate IS screen-reader latency. With the game's vSyncCount=1, frames wait on the display;
    /// on a machine whose display is off or idle (common for blind players — here an HDMI AV receiver) that was a
    /// steady 12.9 fps = ~77 ms per UIA read and multi-second announcements, on hardware that renders this game at
    /// ~450 fps. Measured: vsync off + 60 fps target → 16 ms per read; uncapped → 2 ms. Tearing is irrelevant to a
    /// blind player; an explicit cap avoids burning the GPU for nothing. Re-applied if the game turns vsync back on.
    /// </summary>
    public sealed class FrameRateUnlock
    {
        private readonly MelonLogger.Instance _log;
        private readonly MelonPreferences_Entry<bool> _enabled;
        private readonly MelonPreferences_Entry<int> _target;
        private int _reapplied;

        public FrameRateUnlock(MelonLogger.Instance log, MelonPreferences_Category prefs)
        {
            _log = log;
            _enabled = prefs.CreateEntry("UnlockFrameRate", true,
                description: "Turn vsync off and cap the frame rate instead. Screen readers get answers once per frame, "
                           + "so a vsync-throttled game (e.g. with the monitor off) makes every announcement slow.");
            _target = prefs.CreateEntry("TargetFrameRate", 120,
                description: "Frame cap used when UnlockFrameRate is on. Higher = faster screen reader responses.");
        }

        public void Tick()
        {
            if (!_enabled.Value) return;
            if (QualitySettings.vSyncCount == 0 && Application.targetFrameRate == _target.Value) return;

            int oldVSync = QualitySettings.vSyncCount, oldTarget = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = _target.Value;
            if (_reapplied++ < 10) // the game may fight it; say so, but don't flood the log
                _log.Msg($"[FrameRate] vsync off, cap {_target.Value} fps (was vSyncCount={oldVSync}, target={oldTarget}).");
        }
    }
}
