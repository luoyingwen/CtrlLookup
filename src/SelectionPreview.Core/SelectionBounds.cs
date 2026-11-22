namespace SelectionPreview.Core;

public static class SelectionBounds
{
    public static bool Match(Probe probe, IReadOnlyList<PixelRect> bounds, bool requireBounds)
    {
        if (bounds.Count == 0) return !requireBounds;
        if (probe.Gesture != SelectionGesture.Mouse) return true;
        // A resize or a drag outside the document should not show an old selection.
        const int tolerance = 20;
        var swept = new PixelRect(Math.Min(probe.Start.X, probe.End.X) - tolerance,
            Math.Min(probe.Start.Y, probe.End.Y) - tolerance, Math.Max(probe.Start.X, probe.End.X) + tolerance + 1,
            Math.Max(probe.Start.Y, probe.End.Y) + tolerance + 1);
        return bounds.Any(rect => !rect.Intersect(swept).IsEmpty);
    }

    // A line can be selected by clicking in its trailing whitespace, outside the glyph bounds.
    // This only identifies candidates; the text provider must also map the click to the selection.
    public static bool IsTrailingClickCandidate(Probe probe, IReadOnlyList<PixelRect> bounds) =>
        probe.Gesture == SelectionGesture.Mouse && probe.ClickCount >= 3 &&
        bounds.Any(rect => !rect.IsEmpty && probe.End.X >= rect.Right &&
            probe.End.Y >= rect.Top && probe.End.Y < rect.Bottom);
}
