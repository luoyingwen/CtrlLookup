using SelectionPreview.Core;
using SelectionPreview.Dictionary;

var cases = new (string Name, Action Run)[]
{
    ("dictionary eligibility accepts one word and preserves internal punctuation", () =>
    {
        Equal("week", EnglishHeadword.FromSelection("  (WEEK.)\r\n"));
        Equal("don't", EnglishHeadword.FromSelection("“Don’t”"));
        Equal("well-known", EnglishHeadword.FromSelection("well-known"));
        Equal("i", EnglishHeadword.FromSelection("I"));
        foreach (var value in new[] { "next week", "week\nend", "中文", "http://example.com", "example.com", "foo_bar",
            "week2", "--word", "well--known", "a-'b", "", new string('a', 49) })
            Equal<string?>(null, EnglishHeadword.FromSelection(value));
    }),
    ("offline dictionary import and lookup retain two glosses and preserve an installed database on failure", () =>
    {
        var temp = Path.Combine(Path.GetTempPath(), $"SelectionPreview-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        var source = Path.Combine(temp, "words.csv");
        var database = Path.Combine(temp, "words.sqlite");
        var dictionary = new OfflineDictionary(database);
        try
        {
            Equal<string?>(null, dictionary.Lookup("week"));
            Equal(false, File.Exists(database));
            File.WriteAllText(source, """
                word,translation,definition
                Week,"n. 周, 星期\n一周\n不会显示",definition
                Don't,不要,definition
                well-known,著名的,definition
                two words,不导入,definition
                week,重复,definition
                """);
            Equal(3, DictionaryBuilder.Build(source, database));
            Equal("n. 周, 星期\n一周", dictionary.Lookup(EnglishHeadword.FromSelection("WEEK.")!));
            Equal("不要", dictionary.Lookup(EnglishHeadword.FromSelection("Don’t")!));
            Equal("著名的", dictionary.Lookup("well-known"));
            Equal<string?>(null, dictionary.Lookup("missing"));
            Equal<string?>(null, dictionary.Lookup("' OR 1=1 --"));
            File.WriteAllText(source, "word,translation\nnew,新的\nbad,unexpected,field\n");
            try
            {
                DictionaryBuilder.Build(source, database, overwrite: true);
                throw new Exception("Malformed CSV should fail.");
            }
            catch (InvalidDataException) { }
            Equal("n. 周, 星期\n一周", dictionary.Lookup("week"));
            File.WriteAllText(database, "not a database");
            Equal<string?>(null, dictionary.Lookup("week"));
        }
        finally
        {
            File.Delete(source);
            File.Delete(database);
            Directory.Delete(temp);
        }
    }),
    ("lookup results can enrich only the still-visible, current selection", () =>
    {
        Action<SelectionCycle>[] interruptions =
        [
            cycle => cycle.LeftDown(1, new(30, 30), false, 200),
            cycle => { cycle.SetActivationHeld(false); cycle.SetActivationHeld(true); },
            cycle => { cycle.SetForeground(2); cycle.SetForeground(1); },
            cycle => cycle.Dismiss()
        ];
        foreach (var interrupt in interruptions)
        {
            var cycle = Cycle();
            var previous = KeyProbe(cycle);
            Equal(false, cycle.CanEnrich(previous, 70));
            Equal(true, cycle.Complete(previous, true, 100));
            Equal(true, cycle.CanEnrich(previous, 150));
            interrupt(cycle);
            Equal(false, cycle.CanEnrich(previous, 260));
            var current = KeyProbe(cycle, 300);
            Equal(true, cycle.Complete(current, true, 400));
            Equal(false, cycle.CanEnrich(previous, 500));
            Equal(true, cycle.CanEnrich(current, 4_399));
            Equal(false, cycle.CanEnrich(current, 4_400)); // Even before the timer hides the card.
            cycle.Advance(4_400);
            Equal(false, cycle.Visible);
        }
    }),
    ("gestures do not query until activation is held from the start", () =>
    {
        var cycle = Cycle(activationHeld: false);
        var start = new PixelPoint(20, 20);
        var end = new PixelPoint(80, 20);
        cycle.LeftDown(1, start, false, 10);
        cycle.LeftUp(end, 30);
        Click(cycle, 100, start);
        Click(cycle, 180, start);
        cycle.KeyDown(KeyAction.Selection, 260);
        Equal<Probe?>(null, cycle.Advance(320));
        Equal(false, cycle.HasTimedWork);

        cycle.LeftDown(1, start, false, 350);
        cycle.SetActivationHeld(true);
        cycle.LeftUp(end, 380);
        Equal<Probe?>(null, cycle.Advance(430));
        Click(cycle, 450, start);
        Equal<Probe?>(null, cycle.Advance(502));
        cycle.LeftDown(1, start, false, 550);
        cycle.LeftUp(end, 580);
        Equal(SelectionGesture.Mouse, cycle.Advance(612)!.Value.Gesture);
    }),
    ("activation release cancels pending or in-flight selection reads", () =>
    {
        var cycle = Cycle();
        cycle.KeyDown(KeyAction.Selection, 0);
        cycle.SetActivationHeld(false);
        cycle.SetActivationHeld(true);
        Equal<Probe?>(null, cycle.Advance(52));

        var inFlight = KeyProbe(cycle, 100);
        cycle.SetActivationHeld(false);
        cycle.SetActivationHeld(true);
        Equal(false, cycle.Complete(inFlight, true, 200));
        Equal(false, cycle.HasTimedWork);
    }),
    ("activation release hides the preview and resets consecutive clicks", () =>
    {
        var cycle = Cycle();
        var point = new PixelPoint(20, 20);
        Click(cycle, 0, point);
        Click(cycle, 100, point);
        var probe = cycle.Advance(152)!.Value;
        Equal(true, cycle.Complete(probe, true, 175));
        cycle.SetActivationHeld(false);
        Equal(false, cycle.Visible);
        Equal(false, cycle.HasTimedWork);
        cycle.SetActivationHeld(true);
        Click(cycle, 240, point);
        Equal<Probe?>(null, cycle.Advance(292));
    }),
    ("either Ctrl keeps the activation held until both are released", () =>
    {
        var keys = new SelectionModifiers(0);
        Equal(false, keys.Control);
        keys.OnPacket(0x11, 0x1D, 0); // Left Ctrl down.
        Equal(true, keys.Control);
        keys.OnPacket(0x11, 0x1D, 2); // Right Ctrl down.
        keys.OnPacket(0x11, 0x1D, 1); // Left Ctrl up.
        Equal(true, keys.Control);
        keys.OnPacket(0x11, 0x1D, 3); // Right Ctrl up.
        Equal(false, keys.Control);
    }),
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

static SelectionCycle Cycle(bool activationHeld = true)
{
    var cycle = new SelectionCycle(500, new(4, 4), new(4, 4), activationHeld);
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
