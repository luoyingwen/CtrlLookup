using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Patterns;
using FlaUI.UIA3;
using SelectionPreview.Core;
using Debug = System.Diagnostics.Debug;

namespace SelectionPreview;

/// <summary>One MTA worker with a latest-only inbox. No UIA objects cross into the UI thread.</summary>
internal sealed class SelectionReader : IDisposable
{
    private readonly object _gate = new();
    private readonly Thread _thread;
    private readonly Action<Probe, SelectionSnapshot?> _completed;
    private bool _stopped;
    private Probe? _pending;
    private long _latest = long.MinValue;

    public SelectionReader(Action<Probe, SelectionSnapshot?> completed)
    {
        _completed = completed;
        _thread = new Thread(Loop) { IsBackground = true, Name = "SelectionPreview.UIAutomation" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void Submit(Probe probe)
    {
        lock (_gate)
        {
            if (_stopped) return;
            Volatile.Write(ref _latest, probe.Generation);
            _pending = probe;
            Monitor.Pulse(_gate);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Volatile.Write(ref _latest, long.MinValue);
            _pending = null;
            Monitor.Pulse(_gate);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stopped = true;
            Volatile.Write(ref _latest, long.MinValue);
            _pending = null;
            Monitor.Pulse(_gate);
        }
    }

    private void Loop()
    {
        UIA3Automation? automation = null;
        // Initialize COM before the first gesture; no target is queried until a probe arrives.
        try { automation = CreateAutomation(); }
        catch (Exception ex) { Debug.WriteLine($"UI Automation not initialized: {ex.GetType().Name}"); }
        try
        {
            while (true)
            {
                Probe probe;
                lock (_gate)
                {
                    while (!_stopped && _pending is null) Monitor.Wait(_gate);
                    if (_stopped) return;
                    probe = _pending!.Value;
                    _pending = null;
                }
                if (Expired(probe)) continue;

                SelectionSnapshot? result = null;
                // Chromium and other providers can publish the selection after the input has settled.
                for (var attempt = 0; attempt < 3 && !Expired(probe); attempt++)
                {
                    if (attempt > 0)
                    {
                        lock (_gate)
                        {
                            if (Expired(probe)) break;
                            Monitor.Wait(_gate, attempt == 1 ? 55 : 110);
                        }
                        if (Expired(probe)) break;
                    }
                    try
                    {
                        automation ??= CreateAutomation();
                        result = Read(automation, probe);
                        if (result is not null) break;
                    }
                    catch (Exception ex) { Debug.WriteLine($"Selection provider unavailable: {ex.GetType().Name}"); }
                }
                if (!Expired(probe)) _completed(probe, result);
            }
        }
        finally { automation?.Dispose(); }
    }

    private static UIA3Automation CreateAutomation()
    {
        var automation = new UIA3Automation();
        try
        {
            automation.ConnectionTimeout = TimeSpan.FromMilliseconds(180);
            automation.TransactionTimeout = TimeSpan.FromMilliseconds(180);
            return automation;
        }
        catch
        {
            automation.Dispose();
            throw;
        }
    }

    private SelectionSnapshot? Read(UIA3Automation automation, Probe probe)
    {
        var cache = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.Full };
        cache.Add(automation.PropertyLibrary.Element.IsPassword);
        cache.Add(automation.PropertyLibrary.Element.NativeWindowHandle);
        cache.Add(automation.PropertyLibrary.Element.RuntimeId);
        cache.Add(automation.PatternLibrary.TextPattern);
        using var cached = cache.Activate();

        var focused = TryFocus(automation);
        if (Sensitive(focused) || Expired(probe)) return null;
        var mouse = probe.Gesture == SelectionGesture.Mouse;
        if (!mouse) return ReadPath(focused, probe, requireBounds: false, new());

        // Virtual web controls may be replaced or move under the start point during a drag.
        // Check both endpoints for protected text before considering any provider.
        var start = Hit(automation, probe, probe.Start);
        var end = probe.Start == probe.End ? start : Hit(automation, probe, probe.End);
        if (Sensitive(start) || Sensitive(end) || Expired(probe)) return null;
        var inspectedPatterns = new HashSet<string>();
        var releasePath = FindPath(end, probe);
        var startPath = probe.Start == probe.End ? [] : FindPath(start, probe);
        if (releasePath is null || startPath is null) return null;

        return ReadProviders(releasePath, probe, requireBounds: false, inspectedPatterns)
            ?? ReadProviders(startPath, probe, requireBounds: false, inspectedPatterns)
            // A focused editor may have its own unrelated selection; require geometric agreement.
            ?? ReadPath(focused, probe, requireBounds: true, inspectedPatterns);
    }

    private static AutomationElement? TryFocus(UIA3Automation automation)
    {
        try { return automation.FocusedElement(); }
        catch (COMException ex) when (IsUnavailable(ex)) { return null; }
    }

    private AutomationElement? Hit(UIA3Automation automation, Probe probe, PixelPoint point)
    {
        if (Expired(probe) || Native.GetAncestor(Native.WindowFromPoint(new(point.X, point.Y))) != probe.Source)
            return null;
        try { return automation.FromPoint(new Point(point.X, point.Y)); }
        catch (COMException ex) when (IsUnavailable(ex)) { return null; }
    }

    // Null means the path hit a protected element; an empty path means no verified providers.
    private List<AutomationElement>? FindPath(AutomationElement? element, Probe probe)
    {
        List<AutomationElement> path = [];
        var attributed = false;
        for (var depth = 0; element is not null && depth < 24 && !Expired(probe); depth++)
        {
            if (Sensitive(element)) return null;
            var handle = element.Properties.NativeWindowHandle.ValueOrDefault;
            if (handle != 0)
            {
                if (Native.GetAncestor(handle) != probe.Source) break;
                attributed = true;
                if (handle == probe.Source) { path.Add(element); return path; }
            }
            path.Add(element);
            try { element = element.Parent; }
            catch (COMException ex) when (IsUnavailable(ex)) { return []; }
        }
        // Never read a foreign ancestor; a verified child HWND is sufficient to anchor its own path.
        return attributed && !Expired(probe) ? path : [];
    }

    private SelectionSnapshot? ReadPath(AutomationElement? element, Probe probe, bool requireBounds,
        HashSet<string> inspectedPatterns) => FindPath(element, probe) is { } path
        ? ReadProviders(path, probe, requireBounds, inspectedPatterns) : null;

    private SelectionSnapshot? ReadProviders(List<AutomationElement> path,
        Probe probe, bool requireBounds, HashSet<string> inspectedPatterns)
    {
        // A leaf can implement TextPattern but expose no selection while its enclosing document does.
        foreach (var element in path)
        {
            if (Expired(probe)) break;
            try
            {
                if (!element.Patterns.Text.TryGetPattern(out var pattern)) continue;
                var id = element.Properties.RuntimeId.ValueOrDefault;
                if (id is { Length: > 0 } && !inspectedPatterns.Add(string.Join(",", id))) continue;
                if (ReadPattern(pattern, probe, requireBounds) is { } snapshot) return snapshot;
            }
            catch (COMException ex) when (IsUnavailable(ex)) { }
            catch (NotSupportedException) { }
        }
        return null;
    }

    private SelectionSnapshot? ReadPattern(ITextPattern pattern, Probe probe, bool requireBounds)
    {
        var ranges = pattern.GetSelection();
        if (ranges is null) return null;
        foreach (var range in ranges.Take(16))
        {
            if (Expired(probe)) return null;
            if (IsCollapsed(range)) continue;
            var bounds = GetBounds(range);
            var mouse = probe.Gesture == SelectionGesture.Mouse;
            if (!SelectionBounds.Match(probe, bounds, requireBounds) &&
                (requireBounds || !SelectionBounds.IsTrailingClickCandidate(probe, bounds) ||
                    !ClickMapsToSelection(pattern, range, probe))) continue;
            PixelPoint? fallback = mouse ? probe.End : Native.CaretPoint(probe.Source);
            if ((bounds.Length == 0 && !fallback.HasValue) || Expired(probe)) continue;
            var text = range.GetText(PreviewText.MaximumLength + 1);
            if (!string.IsNullOrWhiteSpace(text)) return new(PreviewText.Limit(text), bounds, fallback);
        }
        return null;
    }

    private bool ClickMapsToSelection(ITextPattern pattern, ITextRange range, Probe probe)
    {
        if (Expired(probe)) return false;
        try
        {
            // RangeFromPoint can snap a click after the text to the insertion point at the line end.
            var hit = pattern.RangeFromPoint(new Point(probe.End.X, probe.End.Y));
            return hit is not null &&
                range.CompareEndpoints(TextPatternRangeEndpoint.Start, hit, TextPatternRangeEndpoint.Start) <= 0 &&
                range.CompareEndpoints(TextPatternRangeEndpoint.End, hit, TextPatternRangeEndpoint.Start) >= 0;
        }
        catch (NotSupportedException) { return false; }
        catch (COMException ex) when (IsUnavailable(ex)) { return false; }
    }

    private static bool Sensitive(AutomationElement? element) => element?.Properties.IsPassword.ValueOrDefault == true;

    private static bool IsUnavailable(COMException ex) => ex.HResult is unchecked((int)0x80004001) // E_NOTIMPL
        or unchecked((int)0x80040201) // UIA_E_ELEMENTNOTAVAILABLE
        or unchecked((int)0x80040204); // UIA_E_NOTSUPPORTED

    private static bool IsCollapsed(ITextRange range)
    {
        try { return range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) == 0; }
        catch (NotSupportedException) { return false; } // GetText still rejects empty ranges below.
        catch (COMException ex) when (IsOptional(ex)) { return false; }
    }

    private static bool IsOptional(COMException ex) => ex.HResult is unchecked((int)0x80004001) or unchecked((int)0x80040204);

    private static PixelRect[] GetBounds(ITextRange range)
    {
        try
        {
            return range.GetBoundingRectangles().Where(r => r.Width > 0 && r.Height > 0).Take(256)
                .Select(r => new PixelRect(r.Left, r.Top, r.Right, r.Bottom)).ToArray();
        }
        catch (NotSupportedException) { return []; }
        catch (COMException ex) when (IsOptional(ex)) { return []; }
    }

    private bool Expired(Probe probe) => Volatile.Read(ref _latest) != probe.Generation || Environment.TickCount64 >= probe.Deadline
        || Native.GetForegroundWindow() != probe.Source;
}
