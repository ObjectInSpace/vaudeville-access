using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using UnityEngine.Accessibility;
using Il2CppGeneric = Il2CppSystem.Collections.Generic;

namespace VaudevilleAccess.Native
{
    /// <summary>
    /// Owns the native accessibility hierarchy. Screens are built as fresh hierarchies (<see cref="NewScreen"/>) and
    /// published whole, which lets Unity free the previous screen's native nodes and allocate the new ones in one step.
    ///
    /// The game build stripped Unity's public construction API (constructors, CreateNode, the AssistiveSupport setter,
    /// event accessors). What the engine itself calls survived, so this reproduces the stripped methods following
    /// Unity's 6000.3 reference source: raw-allocate the objects, fill the fields their constructors set, assign the
    /// event delegate fields directly, and hand the hierarchy to AccessibilityHierarchyService under the engine lock,
    /// exactly as AssistiveSupport.activeHierarchy's setter does. <see cref="NativeApiCheck"/> verifies every member
    /// used here at startup.
    ///
    /// Focus rules learned live (2026-11-02):
    /// - Windows ignores a focus request repeating the previous argument (documented by Unity), so re-focusing the same
    ///   node detours through another node for one frame.
    /// - When the game window regains focus, UI Automation focus lands on the window pane, not the last node, so the
    ///   host re-focuses it.
    /// - node.isFocused is unreliable in this build (read true for every node at once); focus is tracked from each
    ///   node's focusChanged callback instead.
    /// </summary>
    public sealed class A11yHost
    {
        // Above anything the game's own (unused) node counter would produce; never reused.
        private static int s_nextId = 900000;

        private readonly MelonLogger.Instance _log;
        private A11yScreen? _published;
        private AccessibilityNode? _focused;
        private AccessibilityNode? _lastRequested;
        private AccessibilityNode? _pendingFocus;
        private bool _wasAppFocused;
        private float _nextHealthCheck;

        public A11yHost(MelonLogger.Instance log)
        {
            _log = log;
            var missing = NativeApiCheck.Run();
            Available = missing.Count == 0;
            if (Available)
                _log.Msg("[A11yHost] Native accessibility API check passed.");
            else
                foreach (string m in missing)
                    _log.Error($"[A11yHost] MISSING native accessibility member: {m}. Screen reader output is disabled.");
        }

        /// <summary>False if the startup check found missing members; every other call is then a no-op.</summary>
        public bool Available { get; }

        public A11yScreen? Published => _published;

        /// <summary>The node the screen reader last reported focusing, or null.</summary>
        public AccessibilityNode? Focused => _focused;

        public A11yScreen NewScreen(string name) => new A11yScreen(this, name);

        /// <summary>Makes <paramref name="screen"/> the active hierarchy and moves screen reader focus into it.</summary>
        public void Publish(A11yScreen screen, AccessibilityNode? focus = null)
        {
            if (!Available)
                throw new InvalidOperationException("Native accessibility is unavailable (startup check failed); see the log.");
            SetServiceHierarchy(screen.Hierarchy);
            _published = screen;
            _focused = null;
            _lastRequested = focus;
            _pendingFocus = null;
            _log.Msg($"[A11yHost] Published '{screen.Name}' ({screen.Nodes.Count} nodes).");
            AssistiveSupport.notificationDispatcher.SendScreenChanged(focus);
        }

        /// <summary>Removes the active hierarchy; the screen reader sees only the bare window.</summary>
        public void Clear()
        {
            if (!Available || _published == null) return;
            SetServiceHierarchy(null);
            _log.Msg($"[A11yHost] Cleared '{_published.Name}'.");
            _published = null;
            _focused = null;
            _lastRequested = null;
            _pendingFocus = null;
        }

        /// <summary>Moves screen reader focus to <paramref name="node"/>, working around Windows' duplicate filter.</summary>
        public void Focus(AccessibilityNode node)
        {
            if (!Available || _published == null) return;
            if (_lastRequested != null && _lastRequested.Pointer == node.Pointer)
            {
                SendLayoutChanged(Detour(node));
                _pendingFocus = node;
                return;
            }
            SendLayoutChanged(node);
        }

        public void SetLabel(AccessibilityNode node, string? text) => NodeText.SetLabel(node, text);
        public void SetValue(AccessibilityNode node, string? text) => NodeText.SetValue(node, text);

        /// <summary>Shows or hides a node from the screen reader without rebuilding the screen (modal dialogs).</summary>
        public void SetActive(AccessibilityNode node, bool active)
        {
            try { node.isActive = active; }
            catch (Exception e) { _log.Warning($"[A11yHost] SetActive({node.id}) threw: {e.Message}"); }
        }

        public void Tick()
        {
            if (!Available || _published == null) return;

            if (_pendingFocus != null)
            {
                var target = _pendingFocus;
                _pendingFocus = null;
                SendLayoutChanged(target);
            }

            bool appFocused = Application.isFocused;
            if (appFocused && !_wasAppFocused)
            {
                var target = _focused ?? _lastRequested;
                if (target != null)
                {
                    SendLayoutChanged(Detour(target));
                    _pendingFocus = target;
                }
            }
            _wasAppFocused = appFocused;

            // Unity drops the active hierarchy when the screen reader is switched off. Put ours back once one is on.
            float now = Time.realtimeSinceStartup;
            if (now < _nextHealthCheck) return;
            _nextHealthCheck = now + 1f;
            var current = AccessibilityHierarchyService.activeHierarchy;
            if ((current == null || current.Pointer != _published.Hierarchy.Pointer) && AccessibilityManager.IsScreenReaderEnabled())
            {
                _log.Msg($"[A11yHost] Active hierarchy was replaced or dropped; republishing '{_published.Name}'.");
                Publish(_published, _focused ?? _lastRequested);
            }
        }

        internal void OnNodeFocusChanged(AccessibilityNode node, bool focused)
        {
            if (focused) _focused = node;
            else if (_focused != null && _focused.Pointer == node.Pointer) _focused = null;
        }

        internal static int NextId() => s_nextId++;

        private AccessibilityNode Detour(AccessibilityNode target)
        {
            var nodes = _published!.Nodes;
            foreach (var n in nodes)
                if (n.Pointer != target.Pointer) return n;
            return target; // single-node screen: nothing to detour through
        }

        private void SendLayoutChanged(AccessibilityNode node)
        {
            _lastRequested = node;
            AssistiveSupport.notificationDispatcher.SendLayoutChanged(node);
        }

        private static void SetServiceHierarchy(AccessibilityHierarchy? hierarchy)
        {
            Il2CppSystem.IDisposable engineLock = AccessibilityManager.GetExclusiveLock();
            try { AccessibilityHierarchyService.activeHierarchy = hierarchy; }
            finally { engineLock.Dispose(); }
        }
    }

    /// <summary>
    /// One screen's hierarchy under construction. Nodes are created by hand (the stripped constructors and CreateNode
    /// are reproduced field by field). Set behaviour before publishing: AllocateNative reads whether `invoked` etc. are
    /// set to tell the platform which actions a node supports.
    /// </summary>
    public sealed class A11yScreen
    {
        private readonly A11yHost _host;
        private readonly List<AccessibilityNode> _nodes = new();
        // The il2cpp delegates reference these managed ones; they must live as long as the screen.
        private readonly List<Delegate> _keepAlive = new();

        internal A11yScreen(A11yHost host, string name)
        {
            _host = host;
            Name = name;
            IntPtr ptr = IL2CPP.il2cpp_object_new(Il2CppClassPointerStore<AccessibilityHierarchy>.NativeClassPtr);
            Hierarchy = new AccessibilityHierarchy(ptr);
            Hierarchy.nodes = new Il2CppGeneric.Dictionary<int, AccessibilityNode>();
            Hierarchy.m_RootNodes = new Il2CppGeneric.List<AccessibilityNode>();
        }

        public string Name { get; }
        public AccessibilityHierarchy Hierarchy { get; }
        public IReadOnlyList<AccessibilityNode> Nodes => _nodes;

        public AccessibilityNode Add(string label, AccessibilityRole role, Rect frame, AccessibilityNode? parent = null,
            string? value = null, string? hint = null, AccessibilityState state = AccessibilityState.None)
        {
            int id = A11yHost.NextId();
            IntPtr ptr = IL2CPP.il2cpp_object_new(Il2CppClassPointerStore<AccessibilityNode>.NativeClassPtr);
            var node = new AccessibilityNode(ptr);
            node.childList = new Il2CppGeneric.List<AccessibilityNode>();
            node._id_k__BackingField = id;
            node.m_Hierarchy = Hierarchy;
            node.m_IsActive = true;
            node.m_Label = label;
            node.m_Role = role;
            node.m_State = state;
            node.m_Frame = frame;
            if (value != null) node.m_Value = value;
            if (hint != null) node.m_Hint = hint;

            Hierarchy.nodes[id] = node;
            if (parent == null)
            {
                Hierarchy.m_RootNodes.Add(node);
            }
            else
            {
                node._parent_k__BackingField = parent;
                parent.childList.Add(node);
            }

            Action<AccessibilityNode, bool> track = (n, focused) => _host.OnNodeFocusChanged(n, focused);
            _keepAlive.Add(track);
            node.focusChanged = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<AccessibilityNode, bool>>(track);

            _nodes.Add(node);
            return node;
        }

        /// <summary>Activation by the screen reader (NVDA+Enter, Narrator double-tap). Return true if handled.</summary>
        public void OnInvoked(AccessibilityNode node, Func<bool> handler)
        {
            _keepAlive.Add(handler);
            node.invoked = DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(handler);
        }

        /// <summary>
        /// Focus gained or lost, in addition to the host's own tracking. Unity's guidance for text fields and sliders on
        /// Windows: focus the real control here so it receives keyboard input.
        /// </summary>
        public void OnFocusChanged(AccessibilityNode node, Action<bool> handler)
        {
            Action<AccessibilityNode, bool> both = (n, focused) =>
            {
                _host.OnNodeFocusChanged(n, focused);
                handler(focused);
            };
            _keepAlive.Add(both);
            node.focusChanged = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<AccessibilityNode, bool>>(both);
        }

        /// <summary>Slider adjustment. On Windows these only fire when the node's value is numeric.</summary>
        public void OnAdjust(AccessibilityNode node, Action increment, Action decrement)
        {
            _keepAlive.Add(increment);
            _keepAlive.Add(decrement);
            node.incremented = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(increment);
            node.decremented = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(decrement);
        }
    }
}
