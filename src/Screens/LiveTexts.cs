using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using VaudevilleAccess.Native;

namespace VaudevilleAccess.Screens
{
    /// <summary>
    /// Announces game texts that change in place, outside the player's focus: what a sighted player notices appearing
    /// on screen. Without this the screen reader only hears them if the player happens to review the screen.
    /// - The AI loading overlay (OffWorldInit): its status text when it appears or changes, including failure hints.
    /// - MessageLabel: transient messages in the Studio and conversations.
    /// - UnlockUI: the popup when a conversation unlocks a location.
    /// - MessageUI: message dialogs (title and body).
    /// - CharacterLabel and LocationLabel: who and where, on arriving at a location.
    /// Each source is announced when it becomes visible with text, or its text changes while visible.
    /// </summary>
    public sealed class LiveTexts
    {
        private const float ScanInterval = 0.25f;

        private readonly MelonLogger.Instance _log;
        private readonly Dictionary<IntPtr, string> _lastSpoken = new();
        private float _nextScan;

        public LiveTexts(MelonLogger.Instance log) => _log = log;

        public void OnSceneChanged() => _lastSpoken.Clear();

        public void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextScan) return;
            _nextScan = now + ScanInterval;
            try
            {
                foreach (var init in UnityEngine.Object.FindObjectsByType<OffWorldInit>(FindObjectsSortMode.None))
                    Watch(init.Pointer, Shown(init.loadingGroup) && init.loading != null && init.loading.activeInHierarchy,
                        Text(init.loadingLabel));

                foreach (var label in UnityEngine.Object.FindObjectsByType<MessageLabel>(FindObjectsSortMode.None))
                    Watch(label.Pointer, label.isActiveAndEnabled, Text(label.GetComponent<TMP_Text>()));

                foreach (var unlock in UnityEngine.Object.FindObjectsByType<UnlockUI>(FindObjectsSortMode.None))
                    Watch(unlock.Pointer, Shown(unlock.canvasGroup), Text(unlock.text));

                foreach (var message in UnityEngine.Object.FindObjectsByType<MessageUI>(FindObjectsSortMode.None))
                    Watch(message.Pointer, Shown(message.canvasGroup), Join(Text(message.messageTitle), Text(message.messageBody)));

                // Arriving at a location: the character's name and the location animate onto the screen.
                foreach (var character in UnityEngine.Object.FindObjectsByType<CharacterLabel>(FindObjectsSortMode.None))
                    Watch(character.Pointer, character.isActiveAndEnabled, Text(character.GetComponent<TMP_Text>()));

                // In the Studio the same component shows the map location under selection, which focus already speaks.
                bool studio = UnityEngine.Object.FindObjectsByType<MapLocation>(FindObjectsSortMode.None).Length > 0;
                if (!studio)
                    foreach (var location in UnityEngine.Object.FindObjectsByType<LocationLabel>(FindObjectsSortMode.None))
                        Watch(location.Pointer, location.isActiveAndEnabled, Text(location.label));
            }
            catch (Exception e)
            {
                _log.Warning($"[LiveTexts] scan threw: {e.GetType().Name}: {e.Message}");
                _nextScan = now + 2f;
            }
        }

        private void Watch(IntPtr source, bool visible, string text)
        {
            string current = visible ? text : "";
            _lastSpoken.TryGetValue(source, out var last);
            if (current == (last ?? "")) return;
            _lastSpoken[source] = current;
            if (current.Length == 0) return; // disappeared: the screen rebuild re-announces focus
            _log.Msg($"[LiveTexts] {current}");
            NativeAnnouncer.Announce(current);
        }

        private static bool Shown(CanvasGroup? group) =>
            group != null && group.gameObject.activeInHierarchy && group.alpha > 0.01f;

        private static string Text(TMP_Text? t) => t != null ? Clean(t.text) : "";

        private static string Join(string a, string b) =>
            a.Length == 0 ? b : b.Length == 0 ? a : $"{a}. {b}";

        private static string Clean(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            bool inTag = false;
            foreach (char ch in s)
            {
                if (ch == '<') { inTag = true; continue; }
                if (ch == '>' && inTag) { inTag = false; continue; }
                if (!inTag && ch != '​') sb.Append(ch == '\n' || ch == '\r' ? ' ' : ch);
            }
            return sb.ToString().Trim();
        }
    }
}
