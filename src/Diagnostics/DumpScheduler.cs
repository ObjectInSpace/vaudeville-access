using System.Collections.Generic;
using UnityEngine;

namespace VaudevilleAccess.Diagnostics
{
    /// <summary>
    /// Runs <see cref="UiDump"/> a few seconds after each scene loads (menus are often built a moment after the scene
    /// itself, so two passes catch both states) and <see cref="BindingDump"/> once per distinct scene name.
    /// </summary>
    public sealed class DumpScheduler
    {
        private static readonly float[] DelaysSeconds = { 2f, 8f };

        private readonly List<(float due, string reason)> _pending = new();
        private readonly HashSet<string> _bindingsDumpedFor = new();

        public void OnSceneInitialized(string sceneName)
        {
            float now = Time.realtimeSinceStartup;
            foreach (float delay in DelaysSeconds)
                _pending.Add((now + delay, $"{sceneName}+{delay:0}s"));
            if (_bindingsDumpedFor.Add(sceneName))
                BindingDump.Write(sceneName);
        }

        public void Tick()
        {
            float now = Time.realtimeSinceStartup;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (now < _pending[i].due) continue;
                string reason = _pending[i].reason;
                _pending.RemoveAt(i);
                UiDump.Write(reason);
            }
        }
    }
}
