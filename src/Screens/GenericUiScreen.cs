using System;
using System.Collections.Generic;
using System.Text;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Accessibility;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VaudevilleAccess.Native;

namespace VaudevilleAccess.Screens
{
    /// <summary>
    /// Publishes whatever uGUI screen is showing as native accessibility nodes, without per-screen code: every visible
    /// interactable control (button, toggle, slider, dropdown, text field) plus visible static text, in reading order.
    ///
    /// The game's menus use uGUI selection with keyboard navigation (InputSystemUIInputModule), so focus is kept in
    /// sync both ways: when the game's selection moves (arrow keys), screen reader focus follows; when the screen
    /// reader moves focus, the game's selection follows. Activation calls the control's own behaviour. Text fields get
    /// Unity's documented Windows treatment: on focus, activate the real input field so typing reaches it, and mirror
    /// its text into the node's value (Spike A).
    ///
    /// The screen is rebuilt when the set of visible controls or their labels change (a panel opens or closes); value
    /// changes (slider moved, text typed) update nodes in place.
    /// </summary>
    public sealed class GenericUiScreen
    {
        private const float ScanInterval = 0.25f;

        private readonly MelonLogger.Instance _log;
        private readonly A11yHost _host;
        private readonly Dictionary<int, Entry> _byObject = new();
        private readonly Dictionary<IntPtr, Entry> _byNode = new();
        private readonly TextFieldReview _textReview;
        private string _signature = "";
        private float _nextScan;
        private int _lastSelectedId;
        private bool _enabled = true;
        private float _lastRebuildDump = -10f;
        // Entries in reading order, for moving to a neighbouring control.
        private readonly List<Entry> _ordered = new();

        public GenericUiScreen(MelonLogger.Instance log, A11yHost host)
        {
            _log = log;
            _host = host;
            _textReview = new TextFieldReview(log);
        }

        /// <summary>Stops publishing (another screen owns the hierarchy) or resumes.</summary>
        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            _signature = "";
        }

        public void OnSceneChanged()
        {
            _signature = "";
            _nextScan = 0f;
        }

        public void Tick()
        {
            if (!_enabled || !_host.Available) return;
            try
            {
                float now = Time.realtimeSinceStartup;
                if (now >= _nextScan)
                {
                    _nextScan = now + ScanInterval;
                    Scan();
                }
                _textReview.Tick(HotKeys.GameHasFocus());
                FollowGameSelection();
            }
            catch (Exception e)
            {
                _log.Warning($"[GenericUi] Tick threw: {e.GetType().Name}: {e.Message}");
                _nextScan = Time.realtimeSinceStartup + 2f; // back off rather than spam
            }
        }

        private sealed class Entry
        {
            public GameObject Go = null!;
            public AccessibilityNode Node = null!;
            public Selectable? Control;
            public string Label = "";
            public string Value = "";
            public Func<string>? ReadValue;
        }

        private sealed class Candidate
        {
            public GameObject Go = null!;
            public Selectable? Control;
            public string Label = "";
            public AccessibilityRole Role;
            public Rect Frame;
            public List<TMP_Text> ValueTexts = new();
            public bool Disabled;
        }

        private void Scan()
        {
            var candidates = Collect();
            string signature = Signature(candidates);
            if (signature != _signature)
            {
                _signature = signature;
                Rebuild(candidates);
                return;
            }

            // Same controls: update labels and values in place. Unity raises no UI Automation event for a changed
            // name or value, so a change on the focused control is announced, the way a screen reader reports a
            // native slider moving. (Rebuilding instead re-announced it by accident and lost the screen's state.)
            var focused = _host.Focused;
            foreach (var c in candidates)
            {
                if (!_byObject.TryGetValue(c.Go.GetInstanceID(), out var entry)) continue;
                bool isFocused = focused != null && focused.Pointer == entry.Node.Pointer;
                if (c.Label != entry.Label)
                {
                    entry.Label = c.Label;
                    _host.SetLabel(entry.Node, c.Label);
                    if (isFocused) NativeAnnouncer.Announce(c.Label);
                }
                if (entry.ReadValue == null) continue;
                string value = entry.ReadValue();
                if (value == entry.Value) continue;
                entry.Value = value;
                _host.SetValue(entry.Node, value);
                // Text fields are reviewed by TextFieldReview; announcing their whole value per keystroke is noise.
                if (isFocused && value.Length > 0 && entry.Node.role != AccessibilityRole.TextField)
                    NativeAnnouncer.Announce(value);
            }
            EnsureGameSelection();
        }

        // Some screens (the Story Vault) start with nothing selected, which leaves the game's own keyboard navigation
        // dead: arrows and Enter have no control to act on. Give the game the control the screen reader is on.
        // Also covers a selection left on a control that has since been hidden: leaving the story editor kept the
        // selection on the editor's hidden Exit button, from which the arrows reach nothing, so nothing changed and
        // nothing was said.
        private void EnsureGameSelection()
        {
            var es = EventSystem.current;
            if (es == null) return;
            var current = es.currentSelectedGameObject;
            if (current != null && (_byObject.ContainsKey(current.GetInstanceID()) || IsLiveControl(current))) return;
            var target = _host.Focused is { } f && _byNode.TryGetValue(f.Pointer, out var fe) && fe.Control != null ? fe : FirstControl();
            if (target == null) return;
            _lastSelectedId = target.Go.GetInstanceID();
            es.SetSelectedGameObject(target.Go);
            _log.Msg(current == null
                ? $"[GenericUi] Game had no selection; selected '{target.Label}'."
                : $"[GenericUi] Game selection '{current.name}' is hidden; selected '{target.Label}'.");
        }

        private static bool IsLiveControl(GameObject go)
        {
            var selectable = go.GetComponent<Selectable>();
            return selectable != null && selectable.IsActive() && selectable.IsInteractable() && IsVisible(go);
        }

        private List<Candidate> Collect()
        {
            var list = new List<Candidate>();
            var labelTexts = new HashSet<int>();

            foreach (var control in UnityEngine.Object.FindObjectsByType<Selectable>(FindObjectsSortMode.None))
            {
                if (control == null || !control.IsActive()) continue;
                var go = control.gameObject;
                // Studio map post-its are 3D objects carrying a uGUI Selectable. Locked locations are non-interactable
                // and the game's keyboard navigation never reaches them, so they are left out like any other
                // non-interactable control: the mod mirrors the game rather than adding to it (decision 2026-11-02).
                var map = go.GetComponent<Il2Cpp.MapLocation>();
                bool interactable = control.IsInteractable();
                if (!interactable) continue;
                if (!IsVisible(go) || IsIgnored(go)) continue;
                if (control.TryCast<Scrollbar>() != null) continue;

                string label;
                if (map != null)
                {
                    label = Clean(map.LocationName);
                    if (label.Length == 0) label = Humanize(go.name);
                }
                else
                {
                    var texts = LabelTexts(go);
                    foreach (var t in texts) labelTexts.Add(t.GetInstanceID());
                    label = texts.Count > 0 ? JoinTexts(texts) : FallbackLabel(go);
                    string context = ActionContext(go);
                    if (context.Length > 0 && context != label) label = $"{label}, {context}";
                }

                var valueTexts = SideValueTexts(go);
                foreach (var t in valueTexts) labelTexts.Add(t.GetInstanceID());

                var rt = go.GetComponent<RectTransform>();
                list.Add(new Candidate
                {
                    Go = go,
                    Control = control,
                    Label = label,
                    Role = RoleOf(go),
                    Frame = rt != null ? Frames.Of(rt) : WorldFrame(go),
                    ValueTexts = valueTexts,
                    Disabled = !interactable,
                });
            }

            foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
            {
                if (text == null || !text.isActiveAndEnabled || labelTexts.Contains(text.GetInstanceID())) continue;
                var go = text.gameObject;
                if (!IsVisible(go) || IsIgnored(go)) continue;
                if (go.GetComponentInParent<Selectable>() is { } owner && owner.IsInteractable()) continue; // part of a control
                string s = Clean(text.text);
                if (s.Length == 0) continue;
                var rt = go.GetComponent<RectTransform>();
                list.Add(new Candidate
                {
                    Go = go,
                    Label = s,
                    Role = AccessibilityRole.StaticText,
                    Frame = rt != null ? Frames.Of(rt) : default,
                });
            }

            // Reading order: top to bottom, then left to right, with a tolerance so a row stays a row.
            list.Sort((a, b) =>
            {
                float dy = a.Frame.y - b.Frame.y;
                if (Math.Abs(dy) > 20f) return dy < 0 ? -1 : 1;
                return a.Frame.x.CompareTo(b.Frame.x);
            });
            return list;
        }

        // Which objects are on screen, not what they say: label changes are updated in place.
        private static string Signature(List<Candidate> candidates)
        {
            var sb = new StringBuilder();
            foreach (var c in candidates) sb.Append(c.Go.GetInstanceID()).Append('|');
            return sb.ToString();
        }

        private void Rebuild(List<Candidate> candidates)
        {
            _byObject.Clear();
            _ordered.Clear();
            _byNode.Clear();
            if (candidates.Count == 0)
            {
                _host.Clear();
                return;
            }

            var screen = _host.NewScreen($"Generic ({candidates.Count})");
            // Open dropdowns: Unity's documented shape is the dropdown node marked Expanded with its options as child
            // nodes. The options are uGUI Toggles inside the dropdown, which would otherwise read as check boxes.
            // Parents go first so the options can attach to them.
            var options = new List<Candidate>();
            foreach (var c in candidates)
            {
                if (DropdownOf(c) != null) { options.Add(c); continue; }
                AddEntry(screen, c, null);
            }
            foreach (var c in options)
            {
                var dropdown = DropdownOf(c)!;
                AccessibilityNode? parent = _byObject.TryGetValue(dropdown.gameObject.GetInstanceID(), out var owner) ? owner.Node : null;
                AddEntry(screen, c, parent);
            }

            var selected = EventSystem.current?.currentSelectedGameObject;
            Entry? focus = selected != null && _byObject.TryGetValue(selected.GetInstanceID(), out var e) ? e : null;
            focus ??= FirstControl();
            _lastSelectedId = selected != null ? selected.GetInstanceID() : 0;
            _host.Publish(screen, focus?.Node);
            _log.Msg($"[GenericUi] Built {candidates.Count} nodes; focus '{focus?.Label}'.");

            // Capture each new screen for later analysis, without the tester pressing anything.
            float now = Time.realtimeSinceStartup;
            if (now - _lastRebuildDump > 3f)
            {
                _lastRebuildDump = now;
                Diagnostics.UiDump.Write($"rebuild{candidates.Count}");
            }
            EnsureGameSelection();
        }

        private void AddEntry(A11yScreen screen, Candidate c, AccessibilityNode? parent)
        {
            var entry = new Entry { Go = c.Go, Control = c.Control, Label = c.Label };
            entry.ReadValue = ValueReader(c.Go, c.ValueTexts);
            entry.Value = entry.ReadValue?.Invoke() ?? "";
            bool option = parent != null;
            var role = option ? AccessibilityRole.Button : c.Role;
            entry.Node = screen.Add(c.Label, role, c.Frame, parent,
                value: entry.Value.Length > 0 ? entry.Value : null,
                state: c.Disabled ? AccessibilityState.Disabled : StateOf(c.Go));
            if (c.Control != null) Wire(screen, entry, role);
            _byObject[c.Go.GetInstanceID()] = entry;
            _ordered.Add(entry);
            _byNode[entry.Node.Pointer] = entry;
        }

        // The open dropdown a candidate is an option of, if any.
        private static TMP_Dropdown? DropdownOf(Candidate c)
        {
            if (c.Control == null || c.Go.GetComponent<Toggle>() == null) return null;
            var dropdown = c.Go.GetComponentInParent<TMP_Dropdown>();
            return dropdown != null && dropdown.IsExpanded ? dropdown : null;
        }

        private Entry? FirstControl()
        {
            foreach (var entry in _byObject.Values)
                if (entry.Control != null) return entry;
            return null;
        }

        private void Wire(A11yScreen screen, Entry entry, AccessibilityRole role)
        {
            var go = entry.Go;
            screen.OnInvoked(entry.Node, () =>
            {
                MelonLogger.Msg($"[GenericUi] Activated '{entry.Node.label}'.");
                Activate(go);
                return true;
            });

            screen.OnFocusChanged(entry.Node, focused =>
            {
                if (!focused) return;
                // Screen reader moved focus: make the game's selection follow, so keys act on the same control.
                var es = EventSystem.current;
                if (es != null && es.currentSelectedGameObject != go)
                {
                    _lastSelectedId = go.GetInstanceID();
                    es.SetSelectedGameObject(go);
                }
                // Unity's guidance for text fields on Windows: select the real field so it receives typing.
                var input = go.GetComponent<TMP_InputField>();
                if (input != null && !input.isFocused) input.ActivateInputField();
            });

            var slider = go.GetComponent<Slider>();
            if (slider != null)
            {
                float step = slider.wholeNumbers ? 1f : (slider.maxValue - slider.minValue) / 10f;
                screen.OnAdjust(entry.Node,
                    () => slider.value = Math.Min(slider.maxValue, slider.value + step),
                    () => slider.value = Math.Max(slider.minValue, slider.value - step));
            }
        }

        private static void Activate(GameObject go)
        {
            // The Studio map: the game travels through the post-it's own raycast-target method.
            var map = go.GetComponent<Il2Cpp.MapLocation>();
            if (map != null) { map.activatePrimary(); return; }
            var input = go.GetComponent<TMP_InputField>();
            if (input != null) { input.ActivateInputField(); return; }
            var toggle = go.GetComponent<Toggle>();
            if (toggle != null)
            {
                // A dropdown option is chosen, not flipped (choosing it also closes the list).
                bool option = go.GetComponentInParent<TMP_Dropdown>() is { IsExpanded: true };
                toggle.isOn = option || !toggle.isOn;
                return;
            }
            var dropdown = go.GetComponent<TMP_Dropdown>();
            if (dropdown != null) { dropdown.Show(); return; }
            var button = go.GetComponent<Button>();
            if (button != null) { button.onClick.Invoke(); return; }
        }

        private void FollowGameSelection()
        {
            var es = EventSystem.current;
            var selected = es?.currentSelectedGameObject;
            int id = selected != null ? selected.GetInstanceID() : 0;
            if (id == _lastSelectedId) return;
            int previousId = _lastSelectedId;
            _lastSelectedId = id;
            if (id == 0) return;
            if (!_byObject.TryGetValue(id, out var entry))
            {
                GuardSelection(es!, selected!, previousId);
                return;
            }
            var focused = _host.Focused;
            if (focused != null && focused.Pointer == entry.Node.Pointer) return; // already there (screen reader moved it)
            _host.Focus(entry.Node);
        }

        // The game's automatic navigation can move selection onto controls of a hidden panel (seen in the story editor:
        // Down on the Characters tab selected the General tab's invisible text fields). A sighted player sees the
        // highlight vanish; a screen reader user gets silence. If the newly selected control is visible it just
        // appeared, so rescan; if it is hidden, move the selection on to the next visible control in the direction
        // of the arrow pressed.
        private void GuardSelection(EventSystem es, GameObject selected, int previousId)
        {
            if (IsLiveControl(selected))
            {
                _nextScan = 0f;
                return;
            }
            if (!_byObject.TryGetValue(previousId, out var previous)) return;
            int index = _ordered.IndexOf(previous);
            if (index < 0) return;
            bool backward = HotKeys.IsDown(HotKeys.VK_UP) || HotKeys.IsDown(HotKeys.VK_LEFT);
            Entry? target = null;
            for (int i = index + (backward ? -1 : 1); i >= 0 && i < _ordered.Count; i += backward ? -1 : 1)
                if (_ordered[i].Control != null) { target = _ordered[i]; break; }
            target ??= previous;

            var input = selected.GetComponent<TMP_InputField>();
            if (input != null && input.isFocused) input.DeactivateInputField();
            _lastSelectedId = target.Go.GetInstanceID();
            es.SetSelectedGameObject(target.Go);
            _host.Focus(target.Node);
            _log.Msg($"[GenericUi] Selection moved to hidden '{selected.name}'; redirected to '{target.Label}'.");
        }

        private static AccessibilityRole RoleOf(GameObject go)
        {
            if (go.GetComponent<TMP_InputField>() != null) return AccessibilityRole.TextField;
            if (go.GetComponent<TMP_Dropdown>() != null) return AccessibilityRole.Dropdown;
            if (go.GetComponent<Slider>() != null) return AccessibilityRole.Slider;
            if (go.GetComponent<Toggle>() != null) return AccessibilityRole.Toggle;
            return AccessibilityRole.Button;
        }

        private static AccessibilityState StateOf(GameObject go)
        {
            var toggle = go.GetComponent<Toggle>();
            if (toggle != null && toggle.isOn) return AccessibilityState.Selected;
            var dropdown = go.GetComponent<TMP_Dropdown>();
            if (dropdown != null && dropdown.IsExpanded) return AccessibilityState.Expanded;
            return AccessibilityState.None;
        }

        private static Func<string>? ValueReader(GameObject go, List<TMP_Text> valueTexts)
        {
            // The typed text. The placeholder is the field's label (see FallbackLabel), as a sighted player reads it.
            var input = go.GetComponent<TMP_InputField>();
            if (input != null) return () => input.text;
            var slider = go.GetComponent<Slider>();
            if (slider != null)
            {
                // Windows only raises increment/decrement for numeric values, so keep this a plain number.
                return () => slider.wholeNumbers
                    ? slider.value.ToString("0")
                    : Math.Round((slider.value - slider.minValue) / Math.Max(0.0001f, slider.maxValue - slider.minValue) * 100f).ToString("0");
            }
            var dropdown = go.GetComponent<TMP_Dropdown>();
            if (dropdown != null) return () => dropdown.captionText != null ? Clean(dropdown.captionText.text) : "";
            if (valueTexts.Count > 0)
            {
                return () =>
                {
                    var sb = new StringBuilder();
                    foreach (var t in valueTexts)
                    {
                        string s = t != null ? Clean(t.text) : "";
                        if (s.Length > 0) sb.Append(sb.Length > 0 ? " " : "").Append(s);
                    }
                    return sb.ToString();
                };
            }
            return null;
        }

        // A button alone in a layout row whose other texts show its current setting, such as Options' "SCREEN
        // RESOLUTION" button with "1080p" beside it. Those texts are the button's value, so a change is announced.
        private static List<TMP_Text> SideValueTexts(GameObject go)
        {
            var result = new List<TMP_Text>();
            if (go.GetComponent<Button>() == null) return result;
            var parent = go.transform.parent;
            if (parent == null || parent.GetComponent<HorizontalLayoutGroup>() == null) return result;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == go.transform) continue;
                if (child.GetComponent<Selectable>() != null) return new List<TMP_Text>(); // a row of controls, not a setting
                foreach (var text in child.GetComponentsInChildren<TMP_Text>())
                    result.Add(text);
            }
            return result;
        }

        // Text-less controls: a text field is described by its placeholder; anything else by its object name.
        private static string FallbackLabel(GameObject go)
        {
            var input = go.GetComponent<TMP_InputField>();
            if (input != null && input.placeholder != null)
            {
                string placeholder = Clean(input.placeholder.TryCast<TMP_Text>()?.text);
                if (placeholder.Length > 0) return placeholder;
            }
            return Humanize(go.name);
        }

        // The control's own text, else (sliders, fields) the first text among its parent's children.
        // All of the control's own texts (a save card's story, progress and date), excluding texts that belong to a
        // nested control; failing that (sliders, fields), the first text among its parent's children.
        private static List<TMP_Text> LabelTexts(GameObject go)
        {
            var result = new List<TMP_Text>();
            if (go.GetComponent<TMP_InputField>() == null && go.GetComponent<TMP_Dropdown>() == null)
            {
                var self = go.GetComponent<Selectable>();
                foreach (var text in go.GetComponentsInChildren<TMP_Text>())
                {
                    if (Clean(text.text).Length == 0) continue;
                    var owner = text.GetComponentInParent<Selectable>();
                    if (owner == null || owner.Pointer != self.Pointer) continue;
                    result.Add(text);
                }
                if (result.Count > 0) return result;
            }
            var parent = go.transform.parent;
            if (parent == null) return result;
            for (int i = 0; i < parent.childCount; i++)
            {
                var sibling = parent.GetChild(i);
                if (sibling == go.transform) continue;
                var text = sibling.GetComponent<TMP_Text>();
                if (text != null && Clean(text.text).Length > 0) { result.Add(text); break; }
            }
            return result;
        }

        private static string JoinTexts(List<TMP_Text> texts)
        {
            var sb = new StringBuilder();
            foreach (var t in texts)
            {
                string s = Clean(t.text);
                if (s.Length > 0) sb.Append(sb.Length > 0 ? ", " : "").Append(s);
            }
            return sb.ToString();
        }

        // A button whose click acts on another control names that control: each save's DELETE calls that save card's
        // Delete, so it reads "DELETE, Story, PROGRESS 58%, ..." instead of a row of identical DELETE buttons.
        private static string ActionContext(GameObject go)
        {
            var button = go.GetComponent<Button>();
            if (button == null) return "";
            int n = button.onClick.GetPersistentEventCount();
            for (int i = 0; i < n; i++)
            {
                var component = button.onClick.GetPersistentTarget(i)?.TryCast<Component>();
                if (component == null || component.gameObject.Pointer == go.Pointer) continue;
                if (component.GetComponent<Selectable>() == null) continue;
                var texts = LabelTexts(component.gameObject);
                if (texts.Count > 0) return JoinTexts(texts);
            }
            return "";
        }

        // 3D objects (map post-its) have no RectTransform: project their position to the screen.
        private static Rect WorldFrame(GameObject go)
        {
            var cam = Camera.main;
            if (cam == null) return default;
            Vector3 p = cam.WorldToScreenPoint(go.transform.position);
            if (p.z < 0) return default;
            return new Rect(p.x - 30f, Screen.height - p.y - 30f, 60f, 60f);
        }

        // Hidden panels in this game often stay active with a CanvasGroup at alpha 0.
        private static bool IsVisible(GameObject go)
        {
            foreach (var group in go.GetComponentsInParent<CanvasGroup>())
                if (group.alpha < 0.01f) return false;
            return true;
        }

        // SRDebugger's corner trigger is a developer console, not game UI.
        private static bool IsIgnored(GameObject go)
        {
            for (var t = go.transform; t != null; t = t.parent)
                if (t.name.StartsWith("SR_") || t.name == "Trigger(Clone)") return true;
            return false;
        }

        private static string Clean(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            bool inTag = false;
            foreach (char ch in s)
            {
                if (ch == '<') { inTag = true; continue; }
                if (ch == '>' && inTag) { inTag = false; continue; }
                if (!inTag) sb.Append(ch == '\n' || ch == '\r' ? ' ' : ch);
            }
            return sb.ToString().Trim();
        }

        // Object names of text-less buttons: "SteamSubscribe" -> "Steam Subscribe".
        private static string Humanize(string name)
        {
            string s = name.Replace("(Clone)", "").Replace("_", " ").Trim();
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i]) && char.IsLower(s[i - 1])) sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
