namespace SelectionPreview.Core;

public static class PreviewText
{
    public const int MaximumLength = 2048;

    public static string Limit(string text)
    {
        if (text.Length <= MaximumLength) return text;
        var end = char.IsHighSurrogate(text[MaximumLength - 1]) ? MaximumLength - 1 : MaximumLength;
        return string.Concat(text.AsSpan(0, end), "…");
    }
}
