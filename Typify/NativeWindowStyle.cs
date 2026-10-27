using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

public static class NativeWindowStyle
{
    private const int GwlStyle = -16;
    private const uint WsPopup = 0x80000000u;
    private const uint WsOverlappedWindow = 0x00CF0000u;
    private const uint WsMaximizeBox = 0x00010000u;
    private const uint WsMinimizeBox = 0x00020000u;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    private const int DwmWindowCornerPreferenceAttribute = 33;
    private const int DwmTransitionsForcedDisabledAttribute = 3;
    private const int DwmWindowCornerDoNotRound = 1;
    private const int DwmWindowCornerRound = 2;
    private const int ShowWindowMinimize = 6;
    private const int ShowWindowMaximize = 3;
    private const int ShowWindowRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr windowHandle, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr windowHandle, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, int x, int y,
        int width, int height, uint flags);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int valueSize);

    public static void ApplyWindowFrame(Window window, bool canMaximize = true, bool canMinimize = true)
    {
        ApplyOverlappedWindowStyle(window, canMaximize, canMinimize);
        UpdateWindowCornerPreference(window);
    }

    public static void Minimize(Window window)
    {
        if (window == null) throw new ArgumentNullException(nameof(window));

        IntPtr windowHandle = new WindowInteropHelper(window).Handle;
        if (windowHandle != IntPtr.Zero)
            ShowWindow(windowHandle, ShowWindowMinimize);
    }

    public static void MaximizeOrRestore(Window window)
    {
        if (window == null) throw new ArgumentNullException(nameof(window));

        IntPtr windowHandle = new WindowInteropHelper(window).Handle;
        if (windowHandle == IntPtr.Zero) return;

        int command = window.WindowState == WindowState.Maximized ? ShowWindowRestore : ShowWindowMaximize;
        ShowWindow(windowHandle, command);
    }

    public static void UpdateWindowCornerPreference(Window window)
    {
        if (window == null) throw new ArgumentNullException(nameof(window));

        IntPtr windowHandle = new WindowInteropHelper(window).Handle;
        if (windowHandle == IntPtr.Zero) return;

        int preference = window.WindowState == WindowState.Maximized
            ? DwmWindowCornerDoNotRound
            : DwmWindowCornerRound;

        try
        {
            int transitionsForcedDisabled = 0;
            DwmSetWindowAttribute(
                windowHandle,
                DwmTransitionsForcedDisabledAttribute,
                ref transitionsForcedDisabled,
                Marshal.SizeOf(typeof(int)));

            DwmSetWindowAttribute(
                windowHandle,
                DwmWindowCornerPreferenceAttribute,
                ref preference,
                Marshal.SizeOf(typeof(int)));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    public static void ApplyOverlappedWindowStyle(Window window, bool canMaximize = true, bool canMinimize = true)
    {
        if (window == null) throw new ArgumentNullException(nameof(window));

        IntPtr windowHandle = new WindowInteropHelper(window).Handle;
        if (windowHandle == IntPtr.Zero) return;

        uint currentStyle = IntPtr.Size == 8
            ? unchecked((uint)GetWindowLongPtr64(windowHandle, GwlStyle).ToInt64())
            : unchecked((uint)GetWindowLong32(windowHandle, GwlStyle));

        uint updatedStyle = (currentStyle & ~WsPopup) | WsOverlappedWindow;
        if (!canMaximize)
            updatedStyle &= ~WsMaximizeBox;
        if (!canMinimize)
            updatedStyle &= ~WsMinimizeBox;

        if (IntPtr.Size == 8)
            SetWindowLongPtr64(windowHandle, GwlStyle, new IntPtr(updatedStyle));
        else
            SetWindowLong32(windowHandle, GwlStyle, unchecked((int)updatedStyle));

        SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }
}
