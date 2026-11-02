using System;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using VaudevilleAccess.Native;

namespace VaudevilleAccess.Screens
{
    /// <summary>
    /// Caret review for the game's text fields. Unity's TextField node exposes only a value, no text pattern, so a
    /// screen reader cannot read at the caret (Spike A, 2026-11-02): arrows inside a field were silent. This watches the
    /// active TMP_InputField and announces what a screen reader says in a native edit box:
    /// - caret moved by one character: the character at the caret
    /// - caret moved to another line (multi-line fields): that line
    /// - larger moves on the same line (Home, End, word jumps): the word at the caret
    /// - text removed (Backspace, Delete): what was removed
    /// Typed characters are NOT echoed: that is the screen reader's own setting, which NVDA applies here by itself.
    ///
    /// It also lets Up/Down leave a field: always for single-line fields (Up/Down do nothing there), and from the first
    /// or last line of a multi-line field. Without this the arrows are trapped and Escape leaves the whole conversation.
    /// </summary>
    public sealed class TextFieldReview
    {
        private readonly MelonLogger.Instance _log;
        private TMP_InputField? _field;
        private string _text = "";
        private int _caret = -1;
        private int _line = -1;
        private int _lineCount = 1;

        public TextFieldReview(MelonLogger.Instance log) => _log = log;

        public void Tick(bool gameHasFocus)
        {
            // Sample the arrows every frame, field or not, so a stale "was down" never swallows the first press.
            bool up = HotKeys.Pressed(HotKeys.VK_UP), down = HotKeys.Pressed(HotKeys.VK_DOWN);
            var es = EventSystem.current;
            var selected = es?.currentSelectedGameObject;
            var field = selected != null ? selected.GetComponent<TMP_InputField>() : null;
            if (field == null || !field.isFocused)
            {
                _field = null;
                return;
            }

            if (_field == null || _field.Pointer != field.Pointer)
            {
                _field = field;
                Snapshot(field);
                return;
            }

            // Decide on leaving with the PREVIOUS frame's line: the game may already have moved the caret this frame.
            if (gameHasFocus && TryLeave(es!, field, up, down)) return;

            string text = field.text ?? "";
            int caret = Math.Clamp(field.stringPosition, 0, text.Length);
            if (text != _text)
            {
                if (text.Length < _text.Length) AnnounceRemoval(_text, text);
            }
            else if (caret != _caret)
            {
                AnnounceCaretMove(text, caret, LineOf(field, caret));
            }
            Snapshot(field);
        }

        private bool TryLeave(EventSystem es, TMP_InputField field, bool up, bool down)
        {
            if (!up && !down) return false;
            bool singleLine = field.lineType == TMP_InputField.LineType.SingleLine;
            bool atEdge = singleLine || (up && _line <= 0) || (down && _line >= _lineCount - 1);
            if (!atEdge) return false;
            var next = up ? field.FindSelectableOnUp() : field.FindSelectableOnDown();
            if (next == null) return false;
            field.DeactivateInputField();
            es.SetSelectedGameObject(next.gameObject);
            _field = null;
            _log.Msg($"[TextReview] Left text field with {(up ? "Up" : "Down")} for '{next.gameObject.name}'.");
            return true;
        }

        private void AnnounceCaretMove(string text, int caret, int line)
        {
            if (line != _line && _lineCount > 1)
            {
                NativeAnnouncer.Announce(Speakable(LineText(text, caret)));
                return;
            }
            if (Math.Abs(caret - _caret) == 1)
            {
                NativeAnnouncer.Announce(caret < text.Length ? CharName(text[caret]) : "blank");
                return;
            }
            NativeAnnouncer.Announce(Speakable(WordAt(text, caret)));
        }

        private static void AnnounceRemoval(string before, string after)
        {
            int start = 0;
            while (start < after.Length && before[start] == after[start]) start++;
            string removed = before.Substring(start, before.Length - after.Length);
            NativeAnnouncer.Announce(removed.Length == 1 ? CharName(removed[0]) : Speakable(removed));
        }

        private void Snapshot(TMP_InputField field)
        {
            _text = field.text ?? "";
            _caret = Math.Clamp(field.stringPosition, 0, _text.Length);
            _line = LineOf(field, _caret);
            var info = field.textComponent != null ? field.textComponent.textInfo : null;
            _lineCount = info != null ? Math.Max(1, info.lineCount) : 1;
        }

        // Visual line (wrapping included), as Up/Down move by visual lines.
        private static int LineOf(TMP_InputField field, int caret)
        {
            var info = field.textComponent != null ? field.textComponent.textInfo : null;
            if (info == null || info.characterCount == 0) return 0;
            int index = Math.Clamp(caret, 0, info.characterCount - 1);
            return info.characterInfo[index].lineNumber;
        }

        private static string LineText(string text, int caret)
        {
            int start = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
            if (caret == 0) start = 0;
            int end = text.IndexOf('\n', caret);
            if (end < 0) end = text.Length;
            return text.Substring(start, Math.Max(0, end - start));
        }

        private static string WordAt(string text, int caret)
        {
            if (text.Length == 0) return "";
            int i = Math.Min(caret, text.Length - 1);
            if (char.IsWhiteSpace(text[i]) && i > 0 && caret == text.Length) i--;
            int start = i, end = i;
            while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
            while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
            return text.Substring(start, end - start);
        }

        private static string Speakable(string s) => string.IsNullOrWhiteSpace(s) ? "blank" : s;

        private static string CharName(char c) => c switch
        {
            ' ' => "space",
            '\n' => "new line",
            '\t' => "tab",
            '.' => "dot",
            ',' => "comma",
            '?' => "question",
            '!' => "bang",
            '\'' => "apostrophe",
            '"' => "quote",
            '-' => "dash",
            _ => char.IsUpper(c) ? $"cap {c}" : c.ToString(),
        };
    }
}
