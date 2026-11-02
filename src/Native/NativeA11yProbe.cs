using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using MelonLoader;
using UnityEngine;
using UnityEngine.Accessibility;
using Il2CppGeneric = Il2CppSystem.Collections.Generic;

namespace VaudevilleAccess.Native
{
    /// <summary>
    /// Phase-0 test: can a mod publish a Unity 6.3 AccessibilityHierarchy that the OS screen reader sees?
    ///
    /// The game build stripped the public construction API — <c>new AccessibilityHierarchy()</c>,
    /// <c>CreateNode</c>, <c>AssistiveSupport.activeHierarchy</c>'s setter and every node event accessor are
    /// "Method unstripping failed" stubs in the interop. What survived is everything the ENGINE calls
    /// ([RequiredByNativeCode]): <c>AccessibilityHierarchyService.activeHierarchy</c>, node
    /// <c>AllocateNative</c>, <c>InvokeNodeInvoked</c>, the field storage, the exclusive lock and the notification
    /// dispatcher. So this reproduces the stripped methods by hand, following Unity's 6000.3 reference source:
    /// allocate raw objects, fill the fields the constructors would have set, then hand the hierarchy to the
    /// service under the same lock <c>AssistiveSupport.activeHierarchy</c> takes. The service calls
    /// <c>AllocateNative</c>, which creates the native nodes from those fields.
    ///
    /// Every step logs its result. A rising beep means a step succeeded, a low beep means it failed.
    /// </summary>
    public sealed class NativeA11yProbe
    {
        // Far above anything the game's own (unused) id counter would hand out.
        private const int FirstNodeId = 900000;

        private readonly MelonLogger.Instance _log;
        private readonly List<AccessibilityNode> _nodes = new();
        private readonly Dictionary<int, bool> _lastFocused = new();
        private AccessibilityHierarchy? _hierarchy;
        private AccessibilityNode? _button;
        private AccessibilityNode? _textField;
        private int _focusIndex;
        private int _textEdits;
        private int _pressCount;
        private bool _focusPollBroken;
        // When the window regains focus, UIA focus lands on the window pane, not on the node that had it (seen live:
        // Alt+Tab in → FOCUS 'Vaudeville' pane), so the screen reader has nothing to announce or activate.
        private bool _wasAppFocused;
        // Re-focusing the node that already holds Unity's internal focus raises NO focus event (seen live: only
        // "FOCUS -> pane"), so restore detours through another node first; the real target follows next frame.
        private AccessibilityNode? _pendingRefocus;

        // Set from the activation callback, which the engine may call off the main thread; consumed in Tick.
        private int _pendingPresses;

        // The il2cpp delegate holds a reference to this managed one; keep it alive for the hierarchy's lifetime.
        private Func<bool>? _onInvokedManaged;

        public NativeA11yProbe(MelonLogger.Instance log) => _log = log;

        public void Build()
        {
            if (_hierarchy != null) { Msg("Already built. Ctrl+F12 tears it down first."); return; }

            Msg("---- Build ----");
            if (!Step("read platform and screen reader status", ReportStatus)) { Fail(); return; }
            if (!Step("create hierarchy object", CreateHierarchy)) { Fail(); return; }

            if (!Step("create nodes", () =>
            {
                float w = Screen.width, h = Screen.height;
                CreateNode("Native accessibility probe. Unity's own screen reader support is working.",
                    AccessibilityRole.StaticText, new Rect(w * 0.25f, h * 0.20f, w * 0.5f, h * 0.10f));
                _button = CreateNode("Vaudeville Access test button", AccessibilityRole.Button,
                    new Rect(w * 0.25f, h * 0.40f, w * 0.5f, h * 0.10f), invokable: true);
                _textField = CreateNode("Question", AccessibilityRole.TextField,
                    new Rect(w * 0.25f, h * 0.60f, w * 0.5f, h * 0.10f), value: "Where were you last night?");
            })) { Fail(); return; }

            if (!Step("set as active hierarchy", () =>
            {
                Il2CppSystem.IDisposable engineLock = AccessibilityManager.GetExclusiveLock();
                try { AccessibilityHierarchyService.activeHierarchy = _hierarchy; }
                finally { engineLock.Dispose(); }
                bool active = AccessibilityHierarchyService.activeHierarchy?.Pointer == _hierarchy!.Pointer;
                Msg($"  service.activeHierarchy is ours: {active}");
                if (!active) throw new InvalidOperationException("service did not keep the hierarchy");
            })) { Fail(); return; }

            Step("report node state", () =>
            {
                foreach (var node in _nodes)
                    Msg($"  node {node.id} '{node.label}' role={node.role} inActiveHierarchy={node.IsInActiveHierarchy()}");
            });

            _focusIndex = 1; // the button
            Step("move screen reader focus to the button", () =>
                AssistiveSupport.notificationDispatcher.SendScreenChanged(_button));

            Msg("Build finished. Listen for the button to be announced; Ctrl+F10 moves to the next node.");
            Beep(true);
        }

        public void FocusNext()
        {
            if (_hierarchy == null || _nodes.Count == 0) { Msg("Nothing built. Ctrl+F9 first."); return; }
            _focusIndex = (_focusIndex + 1) % _nodes.Count;
            var node = _nodes[_focusIndex];
            if (Step($"move focus to node {node.id} '{node.label}'", () =>
                    AssistiveSupport.notificationDispatcher.SendLayoutChanged(node)))
                Beep(true);
            else
                Fail();
        }

        public void ChangeTextValue()
        {
            if (_textField == null) { Msg("Nothing built. Ctrl+F9 first."); return; }
            string[] values = { "Who saw you leave the theatre?", "Did you know the victim?", "Where were you last night?" };
            string next = values[_textEdits++ % values.Length];
            if (Step($"set text field value to '{next}'", () => NodeText.SetValue(_textField, next)))
                Beep(true);
            else
                Fail();
        }

        public void TearDown()
        {
            if (_hierarchy == null) return;
            Msg("---- Tear down ----");
            Step("clear active hierarchy", () =>
            {
                Il2CppSystem.IDisposable engineLock = AccessibilityManager.GetExclusiveLock();
                try { AccessibilityHierarchyService.activeHierarchy = null; }
                finally { engineLock.Dispose(); }
            });
            _hierarchy = null;
            _button = null;
            _textField = null;
            _nodes.Clear();
            _lastFocused.Clear();
            _onInvokedManaged = null;
        }

        /// <summary>Per-frame: apply activations and log focus changes the screen reader makes.</summary>
        public void Tick()
        {
            int presses = Interlocked.Exchange(ref _pendingPresses, 0);
            if (presses > 0 && _button != null)
            {
                _pressCount += presses;
                Step("relabel button after activation", () =>
                    NodeText.SetLabel(_button, $"Vaudeville Access test button, pressed {_pressCount} times"));
                Beep(true);
            }

            if (_pendingRefocus != null)
            {
                var target = _pendingRefocus;
                _pendingRefocus = null;
                Step($"restore focus to node {target.id} '{target.label}'", () =>
                    AssistiveSupport.notificationDispatcher.SendLayoutChanged(target));
            }

            bool appFocused = Application.isFocused;
            if (appFocused && !_wasAppFocused && _hierarchy != null && _nodes.Count > 1)
            {
                var detour = _nodes[(_focusIndex + 1) % _nodes.Count];
                _pendingRefocus = _nodes[_focusIndex];
                Step($"window regained focus; detour via node {detour.id}", () =>
                    AssistiveSupport.notificationDispatcher.SendLayoutChanged(detour));
            }
            _wasAppFocused = appFocused;

            if (_hierarchy == null || _focusPollBroken) return;
            try
            {
                foreach (var node in _nodes)
                {
                    bool focused = node.isFocused;
                    if (_lastFocused.TryGetValue(node.id, out bool was) && was == focused) continue;
                    _lastFocused[node.id] = focused;
                    if (focused) Msg($"Screen reader focus is now on node {node.id} '{node.label}'.");
                }
            }
            catch (Exception e)
            {
                _focusPollBroken = true;
                _log.Warning($"[NativeProbe] Focus polling disabled, isFocused threw: {e.GetType().Name}: {e.Message}");
            }
        }

        private void ReportStatus()
        {
            Msg($"  isSupportedPlatform={AccessibilityManager.isSupportedPlatform}");
            Msg($"  AccessibilityManager.IsScreenReaderEnabled()={AccessibilityManager.IsScreenReaderEnabled()}");
            Msg($"  AssistiveSupport.isScreenReaderEnabled={AssistiveSupport.isScreenReaderEnabled}");
            Msg($"  existing active hierarchy: {(AccessibilityHierarchyService.activeHierarchy == null ? "none" : "PRESENT (the game uses one)")}");
            if (!AccessibilityManager.isSupportedPlatform)
                throw new PlatformNotSupportedException("Unity reports this platform as unsupported");
        }

        // Mirrors the stripped `new AccessibilityHierarchy()` plus its field initializers.
        private void CreateHierarchy()
        {
            IntPtr ptr = IL2CPP.il2cpp_object_new(Il2CppClassPointerStore<AccessibilityHierarchy>.NativeClassPtr);
            var hierarchy = new AccessibilityHierarchy(ptr);
            hierarchy.nodes = new Il2CppGeneric.Dictionary<int, AccessibilityNode>();
            hierarchy.m_RootNodes = new Il2CppGeneric.List<AccessibilityNode>();
            _hierarchy = hierarchy;
        }

        // Mirrors the stripped CreateNode + `new AccessibilityNode(id, hierarchy)` + CreateNodeAndSetParent for a
        // root node. The hierarchy is not active yet, so no native node is created here; that happens in
        // AllocateNative when the hierarchy is activated, using the fields set below.
        private AccessibilityNode CreateNode(string label, AccessibilityRole role, Rect frame,
            bool invokable = false, string? value = null)
        {
            int id = FirstNodeId + _nodes.Count;
            IntPtr ptr = IL2CPP.il2cpp_object_new(Il2CppClassPointerStore<AccessibilityNode>.NativeClassPtr);
            var node = new AccessibilityNode(ptr);
            node.childList = new Il2CppGeneric.List<AccessibilityNode>();
            node._id_k__BackingField = id;
            node.m_Hierarchy = _hierarchy;
            node.m_IsActive = true;
            node.m_Label = label;
            node.m_Role = role;
            node.m_Frame = frame;
            if (value != null) node.m_Value = value;
            if (invokable)
            {
                // add_invoked is stripped; assign the backing field. AllocateNative reads `invoked != null` to tell
                // the native side this node can be activated.
                _onInvokedManaged = OnButtonInvoked;
                node.invoked = DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(_onInvokedManaged);
            }

            _hierarchy!.nodes[id] = node;
            _hierarchy.m_RootNodes.Add(node);
            _nodes.Add(node);
            Msg($"  created node {id} '{label}' role={role} frame={frame}");
            return node;
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
            Beep(false);
        }

        private void Msg(string text) => _log.Msg($"[NativeProbe] {text}");

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool Beep(uint frequency, uint durationMs);

        // Off the main thread: kernel32 Beep blocks for its duration.
        private static void Beep(bool success) => Task.Run(() =>
        {
            if (success) { Beep(660, 90); Beep(990, 120); }
            else Beep(220, 450);
        });
    }
}
