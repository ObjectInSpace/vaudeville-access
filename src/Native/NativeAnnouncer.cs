using System;
using UnityEngine.Accessibility;
using UnityEngine.Bindings;

namespace VaudevilleAccess.Native
{
    /// <summary>
    /// Screen reader announcements through Unity's own channel (a UI Automation notification on Windows), for events
    /// outside the player's focus. The interop wrapper AccessibilityManager.SendAnnouncementNotification pins the text
    /// through the missing Il2CppSystem.ReadOnlySpan method (same breakage as the node setters, see
    /// <see cref="NodeText"/>), so this builds the {pointer, length} view itself and calls the engine function directly.
    /// </summary>
    public static class NativeAnnouncer
    {
        public static unsafe void Announce(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            fixed (char* chars = text)
            {
                var span = new ManagedSpanWrapper(chars, text.Length);
                AccessibilityManager.SendAnnouncementNotification_Injected(ref span);
            }
        }
    }
}
