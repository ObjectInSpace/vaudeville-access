using System;
using System.IO;
using System.Security.Cryptography;
using MelonLoader;
using VaudevilleAccess.Native;

[assembly: MelonInfo(typeof(VaudevilleAccess.VaudevilleAccessMod), "Vaudeville Access", "0.0.1", "objectinspace")]
[assembly: MelonGame("Bumblebee Studios", "Vaudeville")]

namespace VaudevilleAccess
{
    /// <summary>
    /// Mod entry point. <see cref="A11yHost"/> publishes Unity 6.3's native accessibility hierarchy, hand-assembled
    /// because the game build stripped the public API. Screen adapters (Phase 2 on) describe the game's screens through
    /// it; for now <see cref="NativeA11yProbe"/> is the test screen. See docs/PLAN.md.
    /// </summary>
    public sealed class VaudevilleAccessMod : MelonMod
    {
        private A11yHost? _host;
        private NativeA11yProbe? _probe;
        private FrameStats? _frameStats;
        private PerfExperiment? _perf;
        private FrameRateUnlock? _frameRate;
        private readonly Diagnostics.DumpScheduler _dumps = new();

        public override void OnInitializeMelon()
        {
            LogBuildStamp();
            _frameRate = new FrameRateUnlock(LoggerInstance, MelonPreferences.CreateCategory("VaudevilleAccess"));
            _host = new A11yHost(LoggerInstance);
            if (!_host.Available)
            {
                // Silence is the failure a blind player cannot diagnose: make it audible.
                NativeA11yProbe.Beep(false);
                NativeA11yProbe.Beep(false);
            }
            _probe = new NativeA11yProbe(LoggerInstance, _host);
            _frameStats = new FrameStats(LoggerInstance);
            if (File.Exists(Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "VaudevilleAccess.perftest")))
                _perf = new PerfExperiment(LoggerInstance);
            LoggerInstance.Msg("Native accessibility probe ready. Ctrl+F9 build, Ctrl+F10 next node, "
                             + "Ctrl+F11 change text field value, Ctrl+F12 tear down.");
        }

        // Developer self-test: the file UserData\VaudevilleAccess.autobuild builds the probe once the first scene is
        // up, so it can be checked from outside (UI Automation) without sending keystrokes to the game. A file, not an
        // environment variable, because the game must be launched through Steam, which does not pass one on.
        private bool _autoBuildPending = File.Exists(Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory,
                                                                  "VaudevilleAccess.autobuild"));

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            LoggerInstance.Msg($"Scene initialized: {buildIndex} '{sceneName}'.");
            _dumps.OnSceneInitialized(sceneName);
            _perf?.Start();
            if (!_autoBuildPending) return;
            _autoBuildPending = false;
            LoggerInstance.Msg("Auto-build requested by environment.");
            _probe?.Build();
        }

        public override void OnUpdate()
        {
            _frameRate?.Tick();
            _host?.Tick();
            _probe?.Tick();
            _frameStats?.Tick();
            _perf?.Tick();
            _dumps.Tick();
            if (!HotKeys.GameHasFocus()) return;

            if (HotKeys.CtrlPressed(HotKeys.VK_F8)) Diagnostics.UiDump.Write("manual");

            if (HotKeys.CtrlPressed(HotKeys.VK_F9)) _probe?.Build();
            if (HotKeys.CtrlPressed(HotKeys.VK_F10)) _probe?.FocusNext();
            if (HotKeys.CtrlPressed(HotKeys.VK_F11)) _probe?.ChangeTextValue();
            if (HotKeys.CtrlPressed(HotKeys.VK_F12)) _probe?.TearDown();
        }

        public override void OnApplicationQuit() => _probe?.TearDown();

        // Hash of the DLL that actually loaded, so a log can always be matched to the build that produced it.
        private void LogBuildStamp()
        {
            try
            {
                string path = typeof(VaudevilleAccessMod).Assembly.Location;
                using var stream = File.OpenRead(path);
                using var sha = SHA256.Create();
                string hash = Convert.ToHexString(sha.ComputeHash(stream)).Substring(0, 16);
                LoggerInstance.Msg($"Build {hash}, written {File.GetLastWriteTime(path):yyyy-MM-dd HH:mm:ss}.");
            }
            catch (Exception e)
            {
                LoggerInstance.Warning($"Could not stamp build: {e.Message}");
            }
        }
    }
}
