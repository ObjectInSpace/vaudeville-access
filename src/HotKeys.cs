using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VaudevilleAccess
{
    /// <summary>
    /// Edge-detected hotkeys read through Win32, so they work whether the game uses the legacy Input Manager or
    /// the Input System. Only reacts while the game window is in the foreground.
    /// </summary>
    internal static class HotKeys
    {
        public const int VK_CONTROL = 0x11;
        public const int VK_UP = 0x26;
        public const int VK_DOWN = 0x28;
        public const int VK_F8 = 0x77;
        public const int VK_F9 = 0x78;
        public const int VK_F10 = 0x79;
        public const int VK_F11 = 0x7A;
        public const int VK_F12 = 0x7B;

        private static readonly bool[] _wasDown = new bool[256];
        private static readonly uint _pid = (uint)Process.GetCurrentProcess().Id;

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        public static bool GameHasFocus()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            return pid == _pid;
        }

        /// <summary>True on the frame <paramref name="vk"/> goes down. Observes only; the game still gets the key.</summary>
        public static bool Pressed(int vk)
        {
            bool down = (GetAsyncKeyState(vk) & 0x8000) != 0;
            bool pressed = down && !_wasDown[vk];
            _wasDown[vk] = down;
            return pressed;
        }

        /// <summary>True on the frame <paramref name="vk"/> goes down while Ctrl is held.</summary>
        public static bool CtrlPressed(int vk)
        {
            bool down = (GetAsyncKeyState(vk) & 0x8000) != 0;
            bool pressed = down && !_wasDown[vk];
            _wasDown[vk] = down;
            return pressed && (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
        }
    }
}
