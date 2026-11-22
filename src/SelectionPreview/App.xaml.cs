using System;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using SelectionPreview.Core;
using Forms = System.Windows.Forms;

namespace SelectionPreview;

public partial class App : System.Windows.Application
{
    private readonly DispatcherTimer _clock = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(12) };
    private Mutex? _instance;
    private SelectionCycle? _cycle;
    private SelectionReader? _reader;
    private DictionaryLookup? _lookup;
    private DesktopInput? _input;
    private PreviewWindow? _popup;
    private Forms.NotifyIcon? _tray;
    private Forms.ContextMenuStrip? _menu;
    private Forms.ToolStripMenuItem? _toggle;
    private Icon? _icon;
    private bool _active;
    private long _readerGeneration;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new Mutex(false, @"Local\SelectionPreview.7b3ba09a", out var first);
        if (!first)
        {
            Shutdown();
            return;
        }

        // A narrow glyph can be selected with less movement than a Windows drag-and-drop gesture.
        _cycle = new SelectionCycle((int)Native.GetDoubleClickTime(),
            new(Math.Max(1, Native.GetSystemMetrics(36) / 2), Math.Max(1, Native.GetSystemMetrics(37) / 2)),
            new(2, 2), activationHeld: false);
        _popup = new PreviewWindow();
        _reader = new SelectionReader((probe, snapshot) =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Complete(probe, snapshot));
        });
        _lookup = new DictionaryLookup((probe, gloss) =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => CompleteLookup(probe, gloss));
        });
        _input = new DesktopInput();
        _input.ForegroundChanged += hwnd =>
        {
            if (!_active) return;
            _cycle!.SetForeground(External(hwnd));
            Pump();
        };
        _input.PrimaryDown += (hwnd, point, shift) => Signal(cycle => cycle.LeftDown(External(hwnd), point, shift, Now));
        _input.PrimaryUp += point => Signal(cycle => cycle.LeftUp(point, Now));
        _input.Key += key => { if (key != KeyAction.Modifier) Signal(cycle => cycle.KeyDown(key, Now)); };
        _input.Dismissed += () => Signal(cycle => cycle.Dismiss());
        _input.ControlChanged += Pump;
        _clock.Tick += (_, _) => Pump();

        using var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SelectionPreview.Preview.ico")!;
        using var loaded = new Icon(iconStream);
        _icon = (Icon)loaded.Clone();
        _menu = new Forms.ContextMenuStrip();
        _toggle = new Forms.ToolStripMenuItem("恢复划词", null, (_, _) => SetActive(!_active));
        _menu.Items.Add(_toggle);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(new Forms.ToolStripMenuItem("退出", null, (_, _) => Shutdown()));
        _tray = new Forms.NotifyIcon { Icon = _icon, ContextMenuStrip = _menu, Text = "划词预览", Visible = true };
        SetActive(true);
    }

    private void SetActive(bool active)
    {
        if (_active == active) return;
        try
        {
            if (active) _input!.Start();
            else _input!.Stop();
            _active = active;
            _cycle!.Dismiss();
            _reader!.Clear();
            Pump();
        }
        catch (Win32Exception ex)
        {
            _tray!.ShowBalloonTip(4_000, "划词预览未启动", $"无法监听选区手势（Windows 错误 {ex.NativeErrorCode}）。可从托盘重试。",
                Forms.ToolTipIcon.Warning);
        }
        _toggle!.Text = _active ? "暂停划词" : "恢复划词";
        _tray!.Text = _active ? "划词预览（按住 Ctrl）" : "划词预览（已暂停）";
    }

    private static long Now => Environment.TickCount64;
    private static nint External(nint hwnd)
    {
        if (hwnd == 0 || Native.GetWindowThreadProcessId(hwnd, out var pid) == 0 || pid == Environment.ProcessId) return 0;
        return hwnd;
    }

    private void RefreshGestureContext()
    {
        _cycle!.SetActivationHeld(_input!.ControlHeld);
        _cycle.SetForeground(External(Native.GetForegroundWindow()));
    }

    private void Signal(Action<SelectionCycle> apply)
    {
        if (!_active) return;
        RefreshGestureContext();
        apply(_cycle!);
        Pump();
    }

    private void Pump()
    {
        if (!_active)
        {
            _lookup!.Clear();
            _popup!.Clear();
            _clock.Stop();
            return;
        }

        RefreshGestureContext();
        if (_readerGeneration != _cycle!.Generation)
        {
            _reader!.Clear();
            _lookup!.Clear();
            _readerGeneration = _cycle.Generation;
        }
        if (_cycle.Advance(Now) is { } probe) _reader!.Submit(probe);
        Reconcile();
    }

    private void Reconcile()
    {
        if (!_cycle!.Visible)
        {
            _lookup!.Clear();
            _popup!.Clear();
        }
        if (_cycle.HasTimedWork) _clock.Start();
        else _clock.Stop();
    }

    private void Complete(Probe probe, SelectionSnapshot? snapshot)
    {
        if (!_active) return;
        RefreshGestureContext();
        if (_cycle!.Complete(probe, snapshot is not null, Now))
        {
            if (!_popup!.Present(snapshot!, probe)) _cycle.Dismiss();
            else if (EnglishHeadword.FromSelection(snapshot!.Text) is { } word) _lookup!.Submit(probe, word);
        }
        Pump();
    }

    private void CompleteLookup(Probe probe, string gloss)
    {
        if (!_active) return;
        Pump();
        if (_cycle!.CanEnrich(probe, Now)) _popup!.ShowMeaning(gloss);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _active = false;
        _clock.Stop();
        _input?.Dispose();
        _reader?.Dispose();
        _lookup?.Dispose();
        _tray?.Dispose();
        _menu?.Dispose();
        _icon?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
