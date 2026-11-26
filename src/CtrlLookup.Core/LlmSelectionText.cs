namespace CtrlLookup.Core;

internal static class LlmSelectionText
{
    public const int MaximumLength = 1200;

    public static string? Limit(string text)
    {
        if (text.Length == 0 || IsAddress(text)) return null;
        if (text.Length <= MaximumLength) return text;
        var end = char.IsHighSurrogate(text[MaximumLength - 1]) ? MaximumLength - 1 : MaximumLength;
        return text[..end];
    }

    private static bool IsAddress(string text) =>
        (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) && !text.Any(char.IsWhiteSpace);
}
