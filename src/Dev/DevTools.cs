using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using VaudevilleAccess.Native;
using VaudevilleAccess.Screens;

namespace VaudevilleAccess.Dev
{
    /// <summary>
    /// Developer tooling, active only with the DeveloperMode preference. None of it is needed to play; all of it helps
    /// whoever works on screen reader support next, including the game's own developers.
    /// - UI dumps: every scene's live UI written to UserData\VaudevilleAccess\dumps (2 s and 8 s after each scene load,
    ///   on every screen rebuild, and on Ctrl+F8), plus the Input System bindings per scene.
    /// - Frame-rate log every 5 s (screen reader latency is frame-bound).
    /// - The Phase 0 probe screen: Ctrl+F9 build, Ctrl+F10 next node, Ctrl+F11 change value, Ctrl+F12 clear.
    /// - Flag-file experiments in UserData: VaudevilleAccess.autobuild (build the probe at start),
    ///   .announcetest (announcement latency), .focustest (focus latency), .perftest (vsync experiment).
    /// </summary>
    public sealed class DevTools
    {
        private readonly MelonLogger.Instance _log;
        private readonly GenericUiScreen? _generic;
        private readonly NativeA11yProbe _probe;
        private readonly FrameStats _frameStats;
        private readonly Diagnostics.DumpScheduler _dumps = new();
        private readonly PerfExperiment? _perf;
        private readonly AnnouncementTest? _announceTest;
        private readonly FocusTest? _focusTest;
        private bool _autoBuildPending;

        public DevTools(MelonLogger.Instance log, A11yHost host, GenericUiScreen? generic)
        {
            _log = log;
            _generic = generic;
            Diagnostics.UiDump.Enabled = true;
            _probe = new NativeA11yProbe(log, host);
            _frameStats = new FrameStats(log);
            if (Flag("perftest")) _perf = new PerfExperiment(log);
            if (Flag("announcetest")) _announceTest = new AnnouncementTest(log);
            if (Flag("focustest")) _focusTest = new FocusTest(log, host);
            _autoBuildPending = Flag("autobuild");
            log.Msg("Developer mode: UI dumps on, frame log on. Probe keys Ctrl+F9 build, Ctrl+F10 next node, "
                  + "Ctrl+F11 change value, Ctrl+F12 clear; Ctrl+F8 dumps the current UI.");
        }

        private static bool Flag(string name) =>
            File.Exists(Path.Combine(MelonEnvironment.UserDataDirectory, $"VaudevilleAccess.{name}"));

        public void OnSceneInitialized(string sceneName)
        {
            _dumps.OnSceneInitialized(sceneName);
            _perf?.Start();
            _announceTest?.Start();
            if (!_autoBuildPending) return;
            _autoBuildPending = false;
            _log.Msg("Auto-build requested by flag file.");
            BuildProbe();
        }

        public void Tick()
        {
            _probe.Tick();
            _frameStats.Tick();
            _perf?.Tick();
            _announceTest?.Tick();
            _focusTest?.Tick();
            _dumps.Tick();
            if (!HotKeys.GameHasFocus()) return;
            if (HotKeys.CtrlPressed(HotKeys.VK_F8)) Diagnostics.UiDump.Write("manual");
            if (HotKeys.CtrlPressed(HotKeys.VK_F9)) BuildProbe();
            if (HotKeys.CtrlPressed(HotKeys.VK_F10)) _probe.FocusNext();
            if (HotKeys.CtrlPressed(HotKeys.VK_F11)) _probe.ChangeTextValue();
            if (HotKeys.CtrlPressed(HotKeys.VK_F12)) TearDownProbe();
        }

        public void OnQuit() => _probe.TearDown();

        // The probe and the generic screen would fight over the one active hierarchy.
        private void BuildProbe()
        {
            _generic?.SetEnabled(false);
            _probe.Build();
        }

        private void TearDownProbe()
        {
            _probe.TearDown();
            _generic?.SetEnabled(true);
        }
    }
}
