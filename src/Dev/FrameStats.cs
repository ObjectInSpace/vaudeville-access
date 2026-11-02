using MelonLoader;
using UnityEngine;

namespace VaudevilleAccess.Native
{
    /// <summary>
    /// Logs the game's real frame rate every few seconds. Unity answers screen-reader (UI Automation) queries on the
    /// main thread, measured at ~77 ms per property read and ~5 s from a focus move to its event, so whether that
    /// cost is "one answer per frame" depends on how fast frames actually are. Also logs the settings that cap or
    /// throttle frames (target rate, vsync, run-in-background) once at start.
    /// </summary>
    public sealed class FrameStats
    {
        private const float WindowSeconds = 5f;

        private readonly MelonLogger.Instance _log;
        private float _windowStart = -1f;
        private int _frames;
        private float _worstFrame;
        private bool _settingsLogged;

        public FrameStats(MelonLogger.Instance log) => _log = log;

        public void Tick()
        {
            float now = Time.realtimeSinceStartup;
            float dt = Time.unscaledDeltaTime;

            if (!_settingsLogged)
            {
                _settingsLogged = true;
                _log.Msg($"[Frames] targetFrameRate={Application.targetFrameRate} vSyncCount={QualitySettings.vSyncCount} "
                       + $"runInBackground={Application.runInBackground}");
            }

            if (_windowStart < 0f) { _windowStart = now; return; }
            _frames++;
            if (dt > _worstFrame) _worstFrame = dt;

            float elapsed = now - _windowStart;
            if (elapsed < WindowSeconds) return;

            _log.Msg($"[Frames] {_frames / elapsed:0.0} fps over {elapsed:0.0}s, average {1000f * elapsed / _frames:0} ms, "
                   + $"worst {_worstFrame * 1000f:0} ms, focused={Application.isFocused}");
            _windowStart = now;
            _frames = 0;
            _worstFrame = 0f;
        }
    }
}
