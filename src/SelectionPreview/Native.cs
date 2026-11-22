using System.Runtime.InteropServices;
using SelectionPreview.Core;

namespace SelectionPreview;

internal static class Native
{
    internal const uint ForegroundEvent = 3;
    internal const int WmInput = 0x00FF;
    internal const int ExStyle = -20;
    internal const int NoActivate = 0x08000000, ToolWindow = 0x80, Transparent = 0x20;
    internal const uint SwpNoActivate = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point(int x, int y)
    {
        public int X = x, Y = y;
        public readonly PixelPoint Pixel => new(X, Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly PixelRect Pixel => new(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RawDevice(ushort usage, uint flags, nint target)
    {
        public ushort UsagePage = 1, Usage = usage;
        public uint Flags = flags;
        public nint Target = target;
    }

    // RAWINPUT header is 24 bytes on x64. RAWMOUSE and RAWKEYBOARD share the union.
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    internal struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(28)] public ushort MouseButtons;
        [FieldOffset(24)] public ushort ScanCode;
        [FieldOffset(26)] public ushort KeyFlags;
        [FieldOffset(30)] public ushort VirtualKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Rect CaretRect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    internal delegate void WinEvent(nint hook, uint eventId, nint hwnd, int objectId, int childId, uint thread, uint time);

    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd, uint flags = 2);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] internal static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint MonitorFromPoint(Point point, uint flags = 2);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
    [DllImport("user32.dll")] internal static extern uint GetRawInputData(nint input, uint command, out Input data, ref uint size, uint headerSize);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWinEventHook(uint min, uint max, nint module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint hook);

    internal static bool Down(int vk) => GetAsyncKeyState(vk) < 0;

    internal static PixelRect WorkArea(PixelPoint point)
    {
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(MonitorFromPoint(new(point.X, point.Y)), ref info) ? info.Work.Pixel : default;
    }

    internal static PixelRect MonitorArea(PixelPoint point)
    {
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(MonitorFromPoint(new(point.X, point.Y)), ref info) ? info.Monitor.Pixel : default;
    }

    internal static PixelPoint? CaretPoint(nint foreground)
    {
        var thread = GetWindowThreadProcessId(foreground, out _);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (thread == 0 || !GetGUIThreadInfo(thread, ref info) || info.Caret == 0 || GetAncestor(info.Caret) != foreground)
            return null;
        var point = new Point(info.CaretRect.Left, info.CaretRect.Top);
        return ClientToScreen(info.Caret, ref point) ? point.Pixel : null;
    }
}
