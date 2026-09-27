using System.Runtime.InteropServices;

namespace OlhoNoChat
{
    public static class WindowHelper
    {
        // Use SetWindowLongPtr for 64-bit compatibility
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        // Use GetWindowLongPtr for 64-bit compatibility
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        internal const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;

        /// <summary>
        /// Makes the window click-through.
        /// </summary>
        public static void SetWindowExTransparent(IntPtr hwnd)
        {
            var extendedStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(extendedStyle.ToInt64() | WS_EX_TRANSPARENT));
        }

        /// <summary>
        /// Makes the window interactable.
        /// </summary>
        public static void SetWindowExDefault(IntPtr hwnd)
        {
            var extendedStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(extendedStyle.ToInt64() & ~WS_EX_TRANSPARENT));
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        // Without activate, only the z-order changes: the focus is never taken away from a game
        public static void SetWindowPosTopMost(IntPtr hwnd, bool activate = false)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW | (activate ? 0 : SWP_NOACTIVATE));
        }
    }
}
