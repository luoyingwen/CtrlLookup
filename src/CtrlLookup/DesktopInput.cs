using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using CtrlLookup.Core;

namespace CtrlLookup;

/// <summary>Signals gestures only. Never consumes input or translates key presses to text.</summary>
internal sealed class DesktopInput : IDisposable
{
    private static readonly uint RawDeviceSize = (uint)Marshal.SizeOf<Native.RawDevice>();
    private readonly HwndSource _window = new(new HwndSourceParameters("CtrlLookup.Input") { ParentWindow = new nint(-3) });
    private readonly Native.WinEvent _foregroundCallback;
    private nint _eventHook;
    private bool _registered;
    private SelectionModifiers _modifiers = new(0);

    public event Action<nint>? ForegroundChanged;
    public event Action<nint, PixelPoint, bool>? PrimaryDown;
    public event Action<PixelPoint>? PrimaryUp;
    public event Action? ActivationChanged;
    public event Action<KeyAction>? Key;
    public event Action? Dismissed;

    // Also check the live state so a missed key-up (e.g. when leaving the desktop) cannot leave the gate open.
    public SelectionMode Mode => !_registered ? SelectionMode.None : _modifiers.ActiveMode(
        Native.Down(0xA2) || Native.Down(0xA3), Native.Down(0xA4) || Native.Down(0xA5));

    public DesktopInput()
    {
        _foregroundCallback = (_, _, hwnd, _, _, _, _) => ForegroundChanged?.Invoke(hwnd);
        _window.AddHook(OnMessage);
    }

    public void Start()
    {
        if (_registered) return;
        _modifiers = new(CurrentModifiers());
        SetRegistration(true);
        _eventHook = Native.SetWinEventHook(Native.ForegroundEvent, Native.ForegroundEvent, 0, _foregroundCallback, 0, 0, 0);
        if (_eventHook != 0) return;
        var failure = new System.ComponentModel.Win32Exception();
        Stop();
        throw failure;
    }

    public void Stop()
    {
        if (_eventHook != 0)
        {
            Native.UnhookWinEvent(_eventHook);
            _eventHook = 0;
        }
        if (!_registered) return;
        _registered = false; // Ignore already queued packets even if deregistration fails.
        try { SetRegistration(false); }
        catch (System.ComponentModel.Win32Exception) { Debug.WriteLine("Raw Input deregistration failed."); }
    }

    private void SetRegistration(bool enable)
    {
        var flags = enable ? 0x100u : 0x01u; // RIDEV_INPUTSINK or RIDEV_REMOVE; keep legacy messages intact.
        var target = enable ? _window.Handle : 0;
        Native.RawDevice[] devices = [new(2, flags, target), new(6, flags, target)];
        if (!Native.RegisterRawInputDevices(devices, (uint)devices.Length, RawDeviceSize))
            throw new System.ComponentModel.Win32Exception();
        _registered = enable;
    }

    private nint OnMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != Native.WmInput || !_registered) return 0;
        uint bytes = 48;
        var read = Native.GetRawInputData(lParam, 0x10000003u, out var data, ref bytes, 24);
        if (read == uint.MaxValue || read < 40) return 0;
        if (data.Type == 0 && read >= 48) OnMouse(data.MouseButtons);
        if (data.Type == 1 && data.VirtualKey is > 0 and < 255)
        {
            var before = (_modifiers.Control, _modifiers.Alt);
            var action = _modifiers.OnPacket(data.VirtualKey, data.ScanCode, data.KeyFlags);
            if (before != (_modifiers.Control, _modifiers.Alt)) ActivationChanged?.Invoke();
            if (action is { } key) Key?.Invoke(key);
        }
        return 0; // Leave normal WM_INPUT cleanup to the window procedure.
    }

    private void OnMouse(ushort buttons)
    {
        if (buttons == 0) return;
        var swapped = Native.GetSystemMetrics(23) != 0;
        var down = swapped ? 4 : 1;
        var up = swapped ? 8 : 2;
        if ((buttons & 0x0FFF & ~(down | up)) != 0)
        {
            Dismissed?.Invoke();
            return;
        }
        if (!Native.GetCursorPos(out var point)) return;
        if ((buttons & down) != 0)
            PrimaryDown?.Invoke(Native.GetAncestor(Native.WindowFromPoint(point)), point.Pixel, _modifiers.Shift);
        if ((buttons & up) != 0) PrimaryUp?.Invoke(point.Pixel);
    }

    private static ModifierKey CurrentModifiers()
    {
        ModifierKey state = 0;
        for (var i = 0; i < 6; i++)
            if (Native.Down(0xA0 + i)) state |= (ModifierKey)(1 << i);
        if (Native.Down(0x5B)) state |= ModifierKey.LeftWindows;
        if (Native.Down(0x5C)) state |= ModifierKey.RightWindows;
        return state;
    }

    public void Dispose()
    {
        Stop();
        _window.Dispose();
    }
}
