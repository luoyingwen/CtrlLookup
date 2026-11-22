namespace CtrlLookup.Core;

public readonly record struct PixelPoint(int X, int Y);
public readonly record struct PixelSize(int Width, int Height);
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public PixelPoint Center => new(Left + Width / 2, Top + Height / 2);

    public PixelRect Intersect(PixelRect other) => new(Math.Max(Left, other.Left), Math.Max(Top, other.Top),
        Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
}

public static class PopupPlacement
{
    /// <summary>Positions the card in the monitor work area, using physical screen pixels.</summary>
    public static PixelRect Fit(PixelRect anchor, PixelRect work, PixelSize desired, int gap)
    {
        var width = Math.Clamp(desired.Width, 1, work.Width);
        var height = Math.Clamp(desired.Height, 1, work.Height);
        var x = Math.Clamp(anchor.Center.X - width / 2, work.Left, work.Right - width);
        var above = anchor.Top - gap - work.Top;
        var below = work.Bottom - anchor.Bottom - gap;
        var y = height <= above || (height > below && above >= below)
            ? anchor.Top - height - gap : anchor.Bottom + gap;
        y = Math.Clamp(y, work.Top, work.Bottom - height);
        return new(x, y, x + width, y + height);
    }

    public static PixelRect At(PixelPoint point) => new(point.X, point.Y, point.X + 1, point.Y + 1);
}
