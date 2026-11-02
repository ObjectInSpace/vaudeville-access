using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.Accessibility;

namespace VaudevilleAccess.Native
{
    /// <summary>
    /// Spike C: times focus moves. Phase 0 measured 0.7 to 1.2 s from SendLayoutChanged to the first UI Automation
    /// event, while announcements take ~0.17 s. This moves focus through the probe screen's nodes every few seconds,
    /// alternating Unity's two focus requests (layout changed, then screen changed), logging each send to the
    /// millisecond. Compare against tools/Watch-UiaEvents.ps1 (Unity's event) and NVDA's log (speech).
    /// Enabled by the file UserData\VaudevilleAccess.focustest; needs the probe screen published (autobuild).
    /// </summary>
    public sealed class FocusTest
    {
        private const int MovesPerMethod = 4;
        private const float IntervalSeconds = 4f;

        private readonly MelonLogger.Instance _log;
        private readonly A11yHost _host;
        private int _moves;
        private float _next = -1f;

        public FocusTest(MelonLogger.Instance log, A11yHost host)
        {
            _log = log;
            _host = host;
        }

        public void Tick()
        {
            var screen = _host.Published;
            if (screen == null || _moves >= MovesPerMethod * 2) return;
            float now = Time.realtimeSinceStartup;
            if (_next < 0f) { _next = now + 6f; return; } // let the initial publish settle
            if (now < _next) return;
            _next = now + IntervalSeconds;

            // Each move targets a different node than the last, so Windows' duplicate filter never applies.
            var node = screen.Nodes[_moves % screen.Nodes.Count];
            bool screenChanged = _moves >= MovesPerMethod;
            _moves++;
            string method = screenChanged ? "SendScreenChanged" : "SendLayoutChanged";
            _log.Msg($"[FocusTest] {method} -> '{node.label}' at {DateTime.Now:HH:mm:ss.fff}, focused={Application.isFocused}");
            try
            {
                if (screenChanged) AssistiveSupport.notificationDispatcher.SendScreenChanged(node);
                else AssistiveSupport.notificationDispatcher.SendLayoutChanged(node);
            }
            catch (Exception e)
            {
                _log.Error($"[FocusTest] {method} threw: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
