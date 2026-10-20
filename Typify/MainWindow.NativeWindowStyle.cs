using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Typify
{
    public partial class MainWindow
    {
        // These constants identify the native window style field, the style bits used for
        // popup and overlapped windows, and the flags for refreshing the frame without moving it.
        private const int GwlStyle = -16;
        private const uint WsPopup = 0x80000000u;
        private const uint WsOverlappedWindow = 0x00CF0000u;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;

        // GetWindowLongPtr is available on 64-bit Windows; GetWindowLongW is its 32-bit counterpart.
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr windowHandle, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr windowHandle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr windowHandle, int index, int value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr windowHandle, int index, IntPtr value);

        // SWP_FRAMECHANGED makes Windows recalculate the non-client frame after changing style bits.
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, int x, int y,
            int width, int height, uint flags);

        // WindowStyle=None gives WPF a popup-style HWND. Replace that native style with an
        // overlapped-window style so Windows can handle standard maximize and restore transitions,
        // while WindowChrome continues to provide the custom WPF title bar and resize behavior.
        private void ApplyOverlappedWindowStyle()
        {
            IntPtr windowHandle = new WindowInteropHelper(this).Handle;
            if (windowHandle == IntPtr.Zero) return;

            // Read the current style using the API matching the process architecture.
            uint currentStyle = IntPtr.Size == 8
                ? unchecked((uint)GetWindowLongPtr64(windowHandle, GwlStyle).ToInt64())
                : unchecked((uint)GetWindowLong32(windowHandle, GwlStyle));

            // Remove WS_POPUP and add the standard overlapped-window style bits.
            uint updatedStyle = (currentStyle & ~WsPopup) | WsOverlappedWindow;

            if (IntPtr.Size == 8)
                SetWindowLongPtr64(windowHandle, GwlStyle, new IntPtr(updatedStyle));
            else
                SetWindowLong32(windowHandle, GwlStyle, unchecked((int)updatedStyle));

            // Notify Windows that the frame styles changed without changing position, size,
            // stacking order, or activation state.
            SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }
    }
}
