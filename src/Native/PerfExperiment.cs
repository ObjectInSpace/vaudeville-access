using System;
using MelonLoader;
using UnityEngine;

namespace VaudevilleAccess.Native
{
    /// <summary>
    /// Timed frame-rate experiment. Unity services one screen-reader request per frame, and this game measured a
    /// rock-steady ~13 fps (78 ms, worst 80 ms) that did NOT change between 1920x1080 and 640x360 — a cap, not load.
    /// vSyncCount=1 ties frames to the display's presents, and the only display is an HDMI AV receiver whose status
    /// Windows reports as Unknown. So this tests vsync directly: one change per stage, each held long enough for
    /// <see cref="FrameStats"/> to log several windows, then the original settings are restored.
    /// Enabled by the file UserData\VaudevilleAccess.perftest.
    /// </summary>
    public sealed class PerfExperiment
    {
        private const float StageSeconds = 15f;

        private readonly MelonLogger.Instance _log;
        private float _stageStart = -1f;
        private int _stage = -1;
        private int _origVSync, _origTarget;

        public PerfExperiment(MelonLogger.Instance log) => _log = log;

        public void Start()
        {
            if (_stage >= 0) return;
            _origVSync = QualitySettings.vSyncCount;
            _origTarget = Application.targetFrameRate;
            _log.Msg($"[Perf] GPU '{SystemInfo.graphicsDeviceName}'; display {Screen.currentResolution.width}x"
                   + $"{Screen.currentResolution.height} @ {Screen.currentResolution.refreshRateRatio.value:0.##} Hz; "
                   + $"vSyncCount={_origVSync} targetFrameRate={_origTarget}");
            Advance(0);
        }

        public void Tick()
        {
            if (_stage < 0 || _stage > 2) return;
            if (Time.realtimeSinceStartup - _stageStart >= StageSeconds) Advance(_stage + 1);
        }

        private void Advance(int stage)
        {
            _stage = stage;
            _stageStart = Time.realtimeSinceStartup;
            try
            {
                switch (stage)
                {
                    case 0:
                        _log.Msg("[Perf] Stage 0: baseline, no changes.");
                        break;
                    case 1:
                        QualitySettings.vSyncCount = 0;
                        Application.targetFrameRate = 60;
                        _log.Msg($"[Perf] Stage 1: vsync off, target 60 fps (vSyncCount={QualitySettings.vSyncCount}).");
                        break;
                    case 2:
                        Application.targetFrameRate = -1;
                        _log.Msg("[Perf] Stage 2: vsync off, no target (uncapped).");
                        break;
                    default:
                        QualitySettings.vSyncCount = _origVSync;
                        Application.targetFrameRate = _origTarget;
                        _log.Msg($"[Perf] Done. Restored vSyncCount={_origVSync} targetFrameRate={_origTarget}.");
                        break;
                }
            }
            catch (Exception e)
            {
                _log.Error($"[Perf] Stage {stage} failed: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
