using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace VaudevilleAccess
{
    /// <summary>
    /// Audible signals for situations where speech itself may be unavailable, such as the startup check failing.
    /// Rising two-note beep = success, single low beep = failure.
    /// </summary>
    internal static class Beeper
    {
        [DllImport("kernel32.dll", EntryPoint = "Beep")]
        private static extern bool NativeBeep(uint frequency, uint durationMs);

        // Off the main thread: kernel32 Beep blocks for its duration.
        public static void Signal(bool success) => Task.Run(() =>
        {
            if (success) { NativeBeep(660, 90); NativeBeep(990, 120); }
            else NativeBeep(220, 450);
        });
    }
}
