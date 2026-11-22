namespace SelectionPreview.Core;

public enum SelectionGesture { Mouse, Keyboard }
public readonly record struct Probe(long Generation, nint Source, SelectionGesture Gesture,
    PixelPoint Start, PixelPoint End, long Deadline, int ClickCount = 0);
public sealed record SelectionSnapshot(string Text, IReadOnlyList<PixelRect> Bounds, PixelPoint? Fallback);

public enum KeyAction { Modifier, Selection, Dismiss }

public static class SelectionKeys
{
    public static KeyAction Classify(ushort key, bool shift, bool control, bool alt) => key switch
    {
        0x10 or 0x11 or 0x12 or >= 0xA0 and <= 0xA5 => KeyAction.Modifier,
        >= 0x21 and <= 0x28 when shift && !alt => KeyAction.Selection, // PgUp through Down
        0x41 when control && !alt && !shift => KeyAction.Selection,
        _ => KeyAction.Dismiss
    };
}

/// <summary>Owns the timing and identity of gestures, probes and visibility. All times are monotonic milliseconds.</summary>
public sealed class SelectionCycle(int doubleClickMs, PixelSize doubleClickRadius, PixelSize dragRadius, bool activationHeld = true)
{
    private long _generation;
    private long _due;
    private Probe? _pending;
    private Probe? _inFlight;
    private MousePress? _press;
    private ClickSequence? _clickSequence;
    private nint _foreground;
    private bool _activationHeld = activationHeld;

    public long Generation => _generation;
    public bool Visible { get; private set; }
    // Keep polling while visible so a missed Ctrl key-up cannot strand a card.
    public bool NeedsPolling => Visible || _pending.HasValue || _inFlight.HasValue;

    public void SetActivationHeld(bool held)
    {
        if (_activationHeld == held) return;
        _activationHeld = held;
        if (!held) Dismiss();
    }

    public void SetForeground(nint foreground)
    {
        if (foreground == _foreground) return;
        _foreground = foreground;
        Invalidate();
        if (_press?.Window != foreground) _press = null;
        _clickSequence = null;
    }

    public void LeftDown(nint source, PixelPoint position, bool shift, long now)
    {
        if (!_activationHeld) return;
        Invalidate();
        if (_clickSequence?.Window != source) _clickSequence = null;
        _press = source == 0 ? null : new(source, position, shift, now);
    }

    public void LeftUp(PixelPoint position, long now)
    {
        var press = _press;
        _press = null;
        if (press is not { } down || down.Window != _foreground) return;

        var dragged = !Near(down.Position, position, dragRadius);
        if (dragged || down.Shift)
        {
            _clickSequence = null;
            Schedule(SelectionGesture.Mouse, down.Position, position, now, 32);
            return;
        }

        // Consecutive clicks can change the selection again (e.g. word, then line).
        // Refresh the time per click, but keep the original position so the sequence cannot drift.
        if (_clickSequence is { } sequence && down.When - sequence.LastWhen <= doubleClickMs
            && Near(sequence.Origin, down.Position, doubleClickRadius))
        {
            var count = sequence.Count + 1;
            _clickSequence = sequence with { LastWhen = down.When, Count = count };
            Schedule(SelectionGesture.Mouse, down.Position, position, now, 32, clickCount: count);
        }
        else
        {
            _clickSequence = new(down.Window, down.Position, down.When, Count: 1);
        }
    }

    public void KeyDown(KeyAction action, long now)
    {
        if (!_activationHeld) return;
        switch (action)
        {
            case KeyAction.Selection:
                _clickSequence = null;
                _press = null;
                Schedule(SelectionGesture.Keyboard, default, default, now, 52);
                break;
            case KeyAction.Dismiss:
                Dismiss();
                break;
        }
    }

    public void Dismiss()
    {
        _clickSequence = null;
        _press = null;
        Invalidate();
    }

    public Probe? Advance(long now)
    {
        if (_inFlight is { } running && now >= running.Deadline) _inFlight = null;
        if (_pending is not { } pending || now < _due) return null;
        _pending = null;
        if (now >= pending.Deadline) return null;
        return _inFlight = pending;
    }

    public bool Complete(Probe probe, bool usable, long now)
    {
        if (_inFlight != probe || probe.Generation != _generation || probe.Source != _foreground) return false;
        _inFlight = null;
        if (!usable || now >= probe.Deadline) return false;
        Visible = true;
        return true;
    }

    public bool CanEnrich(Probe probe) => _activationHeld && Visible
        && probe.Generation == _generation && probe.Source == _foreground;

    private void Schedule(SelectionGesture gesture, PixelPoint start, PixelPoint end, long now, int delay, int clickCount = 0)
    {
        Invalidate();
        if (_foreground == 0) return;
        _due = now + delay;
        _pending = new(_generation, _foreground, gesture, start, end, now + 450, clickCount);
    }

    private void Invalidate()
    {
        _generation++;
        _pending = _inFlight = null;
        Visible = false;
    }

    private static bool Near(PixelPoint a, PixelPoint b, PixelSize radius) =>
        Math.Abs(a.X - b.X) <= radius.Width && Math.Abs(a.Y - b.Y) <= radius.Height;

    private readonly record struct MousePress(nint Window, PixelPoint Position, bool Shift, long When);
    private readonly record struct ClickSequence(nint Window, PixelPoint Origin, long LastWhen, int Count);
}
