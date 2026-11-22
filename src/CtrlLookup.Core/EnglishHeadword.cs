namespace CtrlLookup.Core;

public static class EnglishHeadword
{
    private const int MaximumLength = 48;
    private static readonly char[] SurroundingPunctuation = ".,!?;:()[]{}\"'‘’“”，。！？；：".ToCharArray();

    public static string? FromSelection(string text) => Normalize(text.Trim().Trim(SurroundingPunctuation).Trim());

    public static string? Normalize(string token)
    {
        if (token.Length is 0 or > MaximumLength) return null;
        var previousWasLetter = false;
        foreach (var ch in token)
        {
            var isLetter = ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            if (!isLetter && (!previousWasLetter || ch is not ('\'' or '’' or '-'))) return null;
            previousWasLetter = isLetter;
        }
        return previousWasLetter ? token.Replace('’', '\'').ToLowerInvariant() : null;
    }
}
