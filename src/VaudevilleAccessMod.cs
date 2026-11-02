using System;
using System.IO;
using System.Security.Cryptography;
using MelonLoader;
using VaudevilleAccess.Native;

[assembly: MelonInfo(typeof(VaudevilleAccess.VaudevilleAccessMod), "Vaudeville Access", "0.1.0", "objectinspace")]
[assembly: MelonGame("Bumblebee Studios", "Vaudeville")]

namespace VaudevilleAccess
{
    /// <summary>
    /// Mod entry point. <see cref="A11yHost"/> publishes Unity 6.3's native accessibility hierarchy, hand-assembled
    /// because the game build stripped the public API. <see cref="Screens.GenericUiScreen"/> describes whatever uGUI
    /// screen is showing through it, and <see cref="Screens.LiveTexts"/> announces texts that change in place.
    /// Developer tooling lives in <see cref="Dev.DevTools"/>, off unless DeveloperMode is set. See docs/PLAN.md.
    /// </summary>
    public sealed class VaudevilleAccessMod : MelonMod
    {
        private A11yHost? _host;
        private FrameRateUnlock? _frameRate;
        private Screens.GenericUiScreen? _generic;
        private Screens.LiveTexts? _liveTexts;
        private Dev.DevTools? _dev;

        public override void OnInitializeMelon()
        {
            LogBuildStamp();
            var prefs = MelonPreferences.CreateCategory("VaudevilleAccess");
            var developerMode = prefs.CreateEntry("DeveloperMode", false,
                description: "Developer tooling: UI dumps to UserData\\VaudevilleAccess\\dumps, a frame-rate log, and the "
                           + "test probe keys (Ctrl+F8 to F12). Not needed to play.");
            _frameRate = new FrameRateUnlock(LoggerInstance, prefs);

            _host = new A11yHost(LoggerInstance);
            if (!_host.Available)
            {
                // Silence is the failure a blind player cannot diagnose: make it audible.
                Beeper.Signal(false);
                Beeper.Signal(false);
            }
            else
            {
                _generic = new Screens.GenericUiScreen(LoggerInstance, _host);
                _liveTexts = new Screens.LiveTexts(LoggerInstance);
            }

            if (developerMode.Value) _dev = new Dev.DevTools(LoggerInstance, _host, _generic);
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            LoggerInstance.Msg($"Scene initialized: {buildIndex} '{sceneName}'.");
            _generic?.OnSceneChanged();
            _liveTexts?.OnSceneChanged();
            _dev?.OnSceneInitialized(sceneName);
        }

        public override void OnUpdate()
        {
            _frameRate?.Tick();
            _host?.Tick();
            _generic?.Tick();
            _liveTexts?.Tick();
            _dev?.Tick();
        }

        public override void OnApplicationQuit() => _dev?.OnQuit();

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
