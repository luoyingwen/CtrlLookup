using SelectionPreview.Core;

var cases = new (string Name, Action Run)[]
{
    ("plain click does not query", () =>
    {
        var cycle = Cycle();
        cycle.LeftDown(1, new(20, 20), false, 0);
        cycle.LeftUp(new(22, 22), 30);
        Equal<Probe?>(null, cycle.Advance(90));
        Equal(false, cycle.HasTimedWork);
    }),
    ("drag waits for release and settle", () =>
    {
        var cycle = Cycle();
        cycle.LeftDown(1, new(20, 20), false, 0);
        Equal<Probe?>(null, cycle.Advance(1_000));
        cycle.LeftUp(new(90, 20), 1_010);
        Equal<Probe?>(null, cycle.Advance(1_041));
        var probe = cycle.Advance(1_042)!.Value;
        Equal(SelectionGesture.Mouse, probe.Gesture);
        Equal(new PixelPoint(20, 20), probe.Start);
        Equal(new PixelPoint(90, 20), probe.End);
        Equal<Probe?>(null, cycle.Advance(1_060));
    }),
    ("short drag can select a narrow glyph", () =>
    {
        var cycle = new SelectionCycle(500, new(4, 4), new(2, 2));
        cycle.SetForeground(1);
        cycle.LeftDown(1, new(35, 60), false, 0);
        cycle.LeftUp(new(38, 60), 15);
        Equal(SelectionGesture.Mouse, cycle.Advance(47)!.Value.Gesture);
    }),
    ("mouse retries accept a nearby selection but not unrelated focused text", () =>
    {
        var probe = new Probe(1, 1, SelectionGesture.Mouse, new(160, 50), new(40, 90), 450);
        PixelRect[] matching = [new(49, 83, 108, 93)];
        PixelRect[] unrelated = [new(30, 160, 100, 175)];
        Equal(true, SelectionBounds.Match(probe, matching, requireBounds: true));
        Equal(false, SelectionBounds.Match(probe, unrelated, requireBounds: true));
        Equal(false, SelectionBounds.Match(probe, [], requireBounds: true));
        Equal(true, SelectionBounds.Match(probe, [], requireBounds: false));
    }),
    ("double-click qualifies on second release only", () =>
    {
        var cycle = Cycle();
        Click(cycle, 0, new(20, 20));
        Equal<Probe?>(null, cycle.Advance(80));
        Click(cycle, 150, new(24, 20));
        Equal(SelectionGesture.Mouse, cycle.Advance(210)!.Value.Gesture);
    }),
    ("third click supersedes the second click and queries the new selection", () =>
    {
        var cycle = Cycle();
        var point = new PixelPoint(20, 20);
        Click(cycle, 0, point);
        Equal<Probe?>(null, cycle.Advance(52));
        Click(cycle, 300, point);
        var second = cycle.Advance(352)!.Value;

        cycle.LeftDown(1, point, false, 600);
        Equal(false, cycle.Complete(second, true, 610));
        Equal<Probe?>(null, cycle.Advance(615));
        cycle.LeftUp(point, 620);
        Equal<Probe?>(null, cycle.Advance(651));
        var third = cycle.Advance(652)!.Value;
        Equal(SelectionGesture.Mouse, third.Gesture);
        Equal(point, third.End);
        Equal(true, cycle.Complete(third, true, 685));

        Click(cycle, 900, point);
        Equal(false, cycle.Visible);
        Equal(SelectionGesture.Mouse, cycle.Advance(952)!.Value.Gesture);
    }),
    ("a third click in trailing whitespace can match the line even if the double click selected nothing", () =>
    {
        var cycle = Cycle();
        var point = new PixelPoint(220, 86);
        PixelRect[] line = [new(40, 80, 115, 98)];
        Click(cycle, 0, point);
        Click(cycle, 140, point);
        var second = cycle.Advance(192)!.Value;
        Equal(2, second.ClickCount);
        Equal(false, SelectionBounds.IsTrailingClickCandidate(second, line));
        Equal(false, cycle.Complete(second, false, 200));
        Equal(false, cycle.Visible);

        Click(cycle, 280, point);
        var third = cycle.Advance(332)!.Value;
        Equal(3, third.ClickCount);
        Equal(false, SelectionBounds.Match(third, line, requireBounds: false));
        Equal(true, SelectionBounds.IsTrailingClickCandidate(third, line));
        Equal(false, SelectionBounds.IsTrailingClickCandidate(third, [new(40, 60, 115, 79)]));
        Equal(false, SelectionBounds.IsTrailingClickCandidate(third, [new(40, 98, 115, 116)]));
        Equal(false, SelectionBounds.IsTrailingClickCandidate(third, [new(240, 80, 300, 98)]));
        Equal(false, SelectionBounds.IsTrailingClickCandidate(third, []));
        Equal(false, SelectionBounds.IsTrailingClickCandidate(third with { ClickCount = 0 }, line));
    }),
    ("third click outside the time or distance threshold starts a new sequence", () =>
    {
        foreach (var (when, point) in new[] { (651L, new PixelPoint(24, 20)), (300L, new PixelPoint(28, 20)) })
        {
            var cycle = Cycle();
            Click(cycle, 0, new(20, 20));
            Click(cycle, 150, new(24, 20));
            Equal(SelectionGesture.Mouse, cycle.Advance(202)!.Value.Gesture);
            Click(cycle, when, point);
            Equal<Probe?>(null, cycle.Advance(when + 52));
            Click(cycle, when + 100, point);
            Equal(SelectionGesture.Mouse, cycle.Advance(when + 152)!.Value.Gesture);
        }
    }),
    ("drag, shift-click, keyboard, dismissal and foreground change break the click sequence", () =>
    {
        Action<SelectionCycle>[] interrupts =
        [
            cycle => { cycle.LeftDown(1, new(20, 20), false, 220); cycle.LeftUp(new(50, 20), 240); },
            cycle => { cycle.LeftDown(1, new(20, 20), true, 220); cycle.LeftUp(new(20, 20), 240); },
            cycle => cycle.KeyDown(KeyAction.Selection, 220),
            cycle => cycle.Dismiss(),
            cycle => { cycle.SetForeground(2); cycle.SetForeground(1); }
        ];
        foreach (var interrupt in interrupts)
        {
            var cycle = Cycle();
            Click(cycle, 0, new(20, 20));
            Click(cycle, 150, new(20, 20));
            Equal(SelectionGesture.Mouse, cycle.Advance(202)!.Value.Gesture);
            interrupt(cycle);
            Click(cycle, 300, new(20, 20));
            Equal<Probe?>(null, cycle.Advance(352));
        }
    }),
    ("distant or late second clicks do not qualify", () =>
    {
        var cycle = Cycle();
        Click(cycle, 0, new(20, 20));
        Click(cycle, 250, new(70, 20));
        Equal<Probe?>(null, cycle.Advance(310));
        Click(cycle, 900, new(70, 20));
        Equal<Probe?>(null, cycle.Advance(960));
    }),
    ("shift click qualifies without a drag", () =>
    {
        var cycle = Cycle();
        cycle.LeftDown(1, new(20, 20), true, 0);
        cycle.LeftUp(new(20, 20), 20);
        Equal(SelectionGesture.Mouse, cycle.Advance(60)!.Value.Gesture);
    }),
    ("drag can activate its source before release", () =>
    {
        var cycle = Cycle();
        cycle.LeftDown(2, new(20, 20), false, 0);
        cycle.SetForeground(2);
        cycle.LeftUp(new(80, 20), 20);
        Equal((nint)2, cycle.Advance(60)!.Value.Source);
    }),
    ("unrelated foreground switch cancels drag", () =>
    {
        var cycle = Cycle();
        cycle.LeftDown(1, new(20, 20), false, 0);
        cycle.SetForeground(2);
        cycle.LeftUp(new(80, 20), 20);
        Equal<Probe?>(null, cycle.Advance(60));
    }),
    ("rapid keyboard selection coalesces", () =>
    {
        var cycle = Cycle();
        cycle.KeyDown(KeyAction.Selection, 0);
        cycle.KeyDown(KeyAction.Selection, 35);
        cycle.KeyDown(KeyAction.Selection, 70);
        Equal<Probe?>(null, cycle.Advance(121));
        Equal(SelectionGesture.Keyboard, cycle.Advance(122)!.Value.Gesture);
        Equal<Probe?>(null, cycle.Advance(141));
    }),
    ("modifier packets preserve side and release order", () =>
    {
        var keys = new SelectionModifiers(0);
        Equal<KeyAction?>(null, keys.OnPacket(0x11, 0x1D, 2)); // Right Ctrl down.
        Equal<KeyAction?>(KeyAction.Selection, keys.OnPacket(0x41, 0, 0));
        keys.OnPacket(0x11, 0x1D, 1); // Left Ctrl up must not clear Right Ctrl.
        Equal<KeyAction?>(KeyAction.Selection, keys.OnPacket(0x41, 0, 0));
        Equal<KeyAction?>(null, keys.OnPacket(0x41, 0, 1));
        keys.OnPacket(0x11, 0x1D, 3);
        Equal<KeyAction?>(KeyAction.Dismiss, keys.OnPacket(0x41, 0, 0));
        keys.OnPacket(0x10, 0x36, 0);
        Equal(true, keys.Shift);
        Equal<KeyAction?>(KeyAction.Selection, keys.OnPacket(0x27, 0, 0));
        keys.OnPacket(0x10, 0x2A, 1);
        Equal(true, keys.Shift);
        keys.OnPacket(0x10, 0x36, 1);
        Equal(false, keys.Shift);
    }),
    ("Win or Alt shortcuts do not probe", () =>
    {
        var keys = new SelectionModifiers(ModifierKey.LeftShift);
        keys.OnPacket(0x5B, 0, 2);
        Equal<KeyAction?>(KeyAction.Dismiss, keys.OnPacket(0x25, 0, 0));
        keys.OnPacket(0x5B, 0, 3);
        keys.OnPacket(0x12, 0, 2);
        Equal<KeyAction?>(KeyAction.Dismiss, keys.OnPacket(0x25, 0, 0));
    }),
    ("visibility expires exactly four seconds after completion", () =>
    {
        var cycle = Cycle();
        var probe = KeyProbe(cycle);
        Equal(true, cycle.Complete(probe, true, 100));
        cycle.Advance(4_099);
        Equal(true, cycle.Visible);
        cycle.Advance(4_100);
        Equal(false, cycle.Visible);
        Equal(false, cycle.HasTimedWork);
    }),
    ("late and missing provider results never show", () =>
    {
        var cycle = Cycle();
        var probe = KeyProbe(cycle);
        Equal(false, cycle.Complete(probe, true, 450));
        probe = KeyProbe(cycle, 500);
        Equal(false, cycle.Complete(probe, false, 600));
        Equal(false, cycle.Visible);
    }),
    ("in-flight work expires without a provider callback", () =>
    {
        var cycle = Cycle();
        _ = KeyProbe(cycle);
        Equal(true, cycle.HasTimedWork);
        cycle.Advance(450);
        Equal(false, cycle.HasTimedWork);
    }),
    ("superseded or reused foreground cannot show an old probe", () =>
    {
        var cycle = Cycle();
        var old = KeyProbe(cycle);
        cycle.SetForeground(2);
        cycle.SetForeground(1);
        var current = KeyProbe(cycle, 70);
        Equal(false, cycle.Complete(old, true, 140));
        Equal(true, cycle.Complete(current, true, 150));
    }),
    ("dismissal hides and invalidates pending reads", () =>
    {
        var cycle = Cycle();
        var old = KeyProbe(cycle);
        cycle.Dismiss();
        Equal(false, cycle.Complete(old, true, 100));
        var current = KeyProbe(cycle, 110);
        Equal(true, cycle.Complete(current, true, 200));
        cycle.KeyDown(KeyAction.Dismiss, 220);
        Equal(false, cycle.Visible);
    }),
    ("placement prefers above and falls below at the top edge", () =>
    {
        var work = new PixelRect(0, 0, 1920, 1040);
        Equal(new PixelRect(400, 444, 600, 494), PopupPlacement.Fit(new(450, 500, 550, 520), work, new(200, 50), 6));
        Equal(new PixelRect(400, 38, 600, 88), PopupPlacement.Fit(new(450, 12, 550, 32), work, new(200, 50), 6));
    }),
    ("negative coordinates and scaled dimensions stay within the work area", () =>
    {
        var work = new PixelRect(-1600, -130, 0, 780);
        var card = PopupPlacement.Fit(new(-1600, 750, -1520, 770), work, new(630, 180), 9);
        Equal(new PixelRect(-1600, 561, -970, 741), card);
        Equal(work, PopupPlacement.Fit(new(-1600, 200, -1500, 220), work, new(4000, 4000), 9));
    }),
    ("truncation preserves complete Unicode scalars", () =>
    {
        var prefix = new string('x', PreviewText.MaximumLength - 1);
        Equal(prefix + "…", PreviewText.Limit(prefix + "🙂word"));
        Equal("English\n中文🙂", PreviewText.Limit("English\n中文🙂"));
    })
};

var failures = 0;
foreach (var (name, run) in cases)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"{cases.Length - failures}/{cases.Length} checks passed.");
return failures == 0 ? 0 : 1;

static SelectionCycle Cycle()
{
    var cycle = new SelectionCycle(500, new(4, 4), new(4, 4));
    cycle.SetForeground(1);
    return cycle;
}

static void Click(SelectionCycle cycle, long t, PixelPoint p)
{
    cycle.LeftDown(1, p, false, t);
    cycle.LeftUp(p, t + 20);
}

static Probe KeyProbe(SelectionCycle cycle, long t = 0)
{
    cycle.KeyDown(KeyAction.Selection, t);
    return cycle.Advance(t + 52)!.Value;
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}
