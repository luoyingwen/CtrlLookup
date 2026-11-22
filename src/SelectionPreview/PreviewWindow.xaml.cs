using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SelectionPreview.Core;

namespace SelectionPreview;

public partial class PreviewWindow : Window
{
    private static readonly SolidColorBrush Paper = Brush(0xF9, 0xFD, 0xFD);
    private static readonly SolidColorBrush Ink = Brush(0x12, 0x32, 0x43);
    private static readonly SolidColorBrush Edge = Brush(0x6F, 0x94, 0x9C);
    private readonly nint _hwnd;

    public PreviewWindow()
    {
        InitializeComponent();
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        var styles = Native.GetWindowLongPtr(_hwnd, Native.ExStyle);
        Native.SetWindowLongPtr(_hwnd, Native.ExStyle, styles | Native.NoActivate | Native.ToolWindow | Native.Transparent);
    }

    internal bool Present(SelectionSnapshot snapshot, Probe probe)
    {
        var nearest = probe.Gesture == SelectionGesture.Mouse ? probe.End : snapshot.Fallback ?? snapshot.Bounds.FirstOrDefault().Center;
        var monitor = Native.MonitorArea(nearest);
        var lines = snapshot.Bounds.Select(rect => rect.Intersect(monitor)).Where(rect => !rect.IsEmpty).ToArray();
        if (lines.Length == 0 && snapshot.Bounds.Count > 0)
        {
            nearest = snapshot.Bounds[0].Center;
            monitor = Native.MonitorArea(nearest);
            lines = snapshot.Bounds.Select(rect => rect.Intersect(monitor)).Where(rect => !rect.IsEmpty).ToArray();
        }

        var work = Native.WorkArea(nearest);
        if (work.IsEmpty || (lines.Length == 0 && !snapshot.Fallback.HasValue)) return false;
        var anchor = lines.Length == 0 ? PopupPlacement.At(snapshot.Fallback!.Value) : MakeAnchor(lines, nearest);

        // Move the hidden HWND to the target monitor before measuring DIPs. Keep all placement in physical pixels.
        var topmost = new nint(-1);
        if (!Native.SetWindowPos(_hwnd, topmost, work.Left, work.Top, 1, 1, Native.SwpNoActivate)) return false;
        var dpi = Native.GetDpiForWindow(_hwnd);
        var scale = dpi == 0 ? 1 : dpi / 96.0;
        var highContrast = SystemParameters.HighContrast;
        Card.Background = highContrast ? SystemColors.WindowBrush : Paper;
        Card.BorderBrush = highContrast ? SystemColors.WindowTextBrush : Edge;
        Body.Foreground = highContrast ? SystemColors.WindowTextBrush : Ink;
        Body.Text = snapshot.Text;
        var width = Math.Min(420, work.Width / scale);
        var height = Math.Min(120, work.Height / scale);
        Body.MaxHeight = Math.Max(1, height - 20);
        Card.Measure(new Size(width, height));
        var desired = new PixelSize((int)Math.Ceiling(Card.DesiredSize.Width * scale),
            (int)Math.Ceiling(Card.DesiredSize.Height * scale));
        var placement = PopupPlacement.Fit(anchor, work, desired, (int)Math.Ceiling(6 * scale));
        if (!Move(placement)) return false;
        Show();
        return Move(placement);
    }

    internal void Clear()
    {
        if (IsVisible) Hide();
        if (Body.Text.Length > 0) Body.Text = string.Empty;
    }

    private bool Move(PixelRect rect) => Native.SetWindowPos(_hwnd, new nint(-1), rect.Left, rect.Top,
        rect.Width, rect.Height, Native.SwpNoActivate);

    private static PixelRect MakeAnchor(PixelRect[] lines, PixelPoint nearest)
    {
        var column = lines.OrderBy(r => Math.Abs((long)r.Center.Y - nearest.Y)).First();
        return new(column.Left, lines.Min(r => r.Top), column.Right, lines.Max(r => r.Bottom));
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
