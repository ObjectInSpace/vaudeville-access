using System;
using MelonLoader;
using UnityEngine;

namespace VaudevilleAccess.Native
{
    /// <summary>
    /// Spike B: measures native announcement latency. Sends five numbered announcements five seconds apart, logging the
    /// send time of each to the millisecond; NVDA's Input/output log gives the time each was spoken. Enabled by the
    /// file UserData\VaudevilleAccess.announcetest. Starts once the first scene is up.
    /// </summary>
    public sealed class AnnouncementTest
    {
        private const int Count = 5;
        private const float IntervalSeconds = 5f;

        private readonly MelonLogger.Instance _log;
        private int _sent;
        private float _next = -1f;

        public AnnouncementTest(MelonLogger.Instance log) => _log = log;

        public void Start()
        {
            if (_next < 0f) _next = Time.realtimeSinceStartup + IntervalSeconds;
        }

        public void Tick()
        {
            if (_next < 0f || _sent >= Count || Time.realtimeSinceStartup < _next) return;
            _next = Time.realtimeSinceStartup + IntervalSeconds;
            _sent++;
            string text = $"Announcement test {_sent}";
            try
            {
                _log.Msg($"[AnnounceTest] sending '{text}' at {DateTime.Now:HH:mm:ss.fff}, focused={Application.isFocused}");
                NativeAnnouncer.Announce(text);
            }
            catch (Exception e)
            {
                _log.Error($"[AnnounceTest] '{text}' threw: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
