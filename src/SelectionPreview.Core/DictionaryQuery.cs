namespace SelectionPreview.Core;

public enum LookupDirection { EnglishToChinese, ChineseToEnglish }

public readonly record struct DictionaryQuery(LookupDirection Direction, string Term)
{
    public static DictionaryQuery? FromSelection(string text)
    {
        if (EnglishHeadword.FromSelection(text) is { } english) return new(LookupDirection.EnglishToChinese, english);
        if (ChineseHeadword.FromSelection(text) is { } chinese) return new(LookupDirection.ChineseToEnglish, chinese);
        return null;
    }
}

public static class ChineseHeadword
{
    private const int MaximumLength = 32;
    private const string SurroundingPunctuation = ".,!?;:()[]{}\"'‘’“”，。！？；：、（）［］【】「」『』《》〈〉…";

    public static string? FromSelection(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && IsBoundary(text[start])) start++;
        while (start < end && IsBoundary(text[end - 1])) end--;
        var term = text[start..end];
        return IsHeadword(term) ? term : null;
    }

    private static bool IsBoundary(char ch) => char.IsWhiteSpace(ch) || SurroundingPunctuation.Contains(ch);

    public static bool IsHeadword(string term)
    {
        var count = 0;
        foreach (var rune in term.EnumerateRunes())
        {
            if (++count > MaximumLength || rune.Value is not (0x3007
                or >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF
                or >= 0x20000 and <= 0x2EE5F or >= 0x2F800 and <= 0x2FA1F or >= 0x30000 and <= 0x3347F))
                return false;
        }
        return count != 0;
    }
}
