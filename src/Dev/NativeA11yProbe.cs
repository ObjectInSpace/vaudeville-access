using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MelonLoader;
using UnityEngine;
using UnityEngine.Accessibility;

namespace VaudevilleAccess.Native
{
    /// <summary>
    /// The Phase 0 test screen, now built on <see cref="A11yHost"/>, kept as a regression check for the host: a text
    /// line, a button and a text field. Ctrl+F9 builds it, Ctrl+F10 moves focus to the next node, Ctrl+F11 changes the
    /// text field's value, Ctrl+F12 clears it. Activating the button renames it. A rising beep means a step succeeded,
    /// a low beep that it failed.
    /// </summary>
    public sealed class NativeA11yProbe
    {
        private readonly MelonLogger.Instance _log;
        private readonly A11yHost _host;
        private readonly List<AccessibilityNode> _nodes = new();
        private A11yScreen? _screen;
        private AccessibilityNode? _button;
        private AccessibilityNode? _textField;
        private int _focusIndex;
        private int _textEdits;
        private int _pressCount;

        // Set from the activation callback; consumed in Tick on the main thread.
        private int _pendingPresses;

        public NativeA11yProbe(MelonLogger.Instance log, A11yHost host)
        {
            _log = log;
            _host = host;
        }

        public void Build()
        {
            if (_screen != null) { Msg("Already built. Ctrl+F12 clears it first."); return; }
            Msg("---- Build ----");
            if (!Step("build and publish probe screen", () =>
            {
                float w = Screen.width, h = Screen.height;
                var screen = _host.NewScreen("Probe");
                _nodes.Add(screen.Add("Native accessibility probe. Unity's own screen reader support is working.",
                    AccessibilityRole.StaticText, new Rect(w * 0.25f, h * 0.20f, w * 0.5f, h * 0.10f)));
                _button = screen.Add("Vaudeville Access test button", AccessibilityRole.Button,
                    new Rect(w * 0.25f, h * 0.40f, w * 0.5f, h * 0.10f));
                screen.OnInvoked(_button, OnButtonInvoked);
                _nodes.Add(_button);
                _textField = screen.Add("Question", AccessibilityRole.TextField,
                    new Rect(w * 0.25f, h * 0.60f, w * 0.5f, h * 0.10f), value: "Where were you last night?");
                _nodes.Add(_textField);
                _focusIndex = 1;
                _screen = screen;
                _host.Publish(screen, _button);
            })) { Fail(); return; }
            Beeper.Signal(true);
        }

        public void FocusNext()
        {
            if (_screen == null) { Msg("Nothing built. Ctrl+F9 first."); return; }
            _focusIndex = (_focusIndex + 1) % _nodes.Count;
            var node = _nodes[_focusIndex];
            if (Step($"move focus to node {node.id} '{node.label}'", () => _host.Focus(node))) Beeper.Signal(true);
            else Fail();
        }

        public void ChangeTextValue()
        {
            if (_textField == null) { Msg("Nothing built. Ctrl+F9 first."); return; }
            string[] values = { "Who saw you leave the theatre?", "Did you know the victim?", "Where were you last night?" };
            string next = values[_textEdits++ % values.Length];
            if (Step($"set text field value to '{next}'", () => _host.SetValue(_textField, next))) Beeper.Signal(true);
            else Fail();
        }

        public void TearDown()
        {
            if (_screen == null) return;
            Step("clear probe screen", _host.Clear);
            _screen = null;
            _button = null;
            _textField = null;
            _nodes.Clear();
        }

        public void Tick()
        {
            int presses = Interlocked.Exchange(ref _pendingPresses, 0);
            if (presses == 0 || _button == null) return;
            _pressCount += presses;
            Step("relabel button after activation", () =>
                _host.SetLabel(_button, $"Vaudeville Access test button, pressed {_pressCount} times"));
            Beeper.Signal(true);
        }

        private bool OnButtonInvoked()
        {
            Interlocked.Increment(ref _pendingPresses);
            MelonLogger.Msg("[NativeProbe] Button ACTIVATED by the screen reader.");
            return true;
        }

        private bool Step(string name, Action action)
        {
            try
            {
                action();
                Msg($"OK   {name}");
                return true;
            }
            catch (Exception e)
            {
                _log.Error($"[NativeProbe] FAIL {name}: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        private void Fail()
        {
            Msg("Stopped at the failed step above.");
            Beeper.Signal(false);
        }

        private void Msg(string text) => _log.Msg($"[NativeProbe] {text}");
    }
}
