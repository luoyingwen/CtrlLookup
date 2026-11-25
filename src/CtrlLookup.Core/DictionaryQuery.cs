namespace CtrlLookup.Core;

public enum LookupDirection { EnglishToChinese, ChineseToEnglish }

public readonly record struct DictionaryQuery(LookupDirection Direction, string Term, bool Headword = true)
{
    public const int PassageLimit = 1200;

    public static DictionaryQuery? FromSelection(string text)
    {
        if (EnglishHeadword.FromSelection(text) is { } english) return new(LookupDirection.EnglishToChinese, english);
        if (ChineseHeadword.FromSelection(text) is { } chinese) return new(LookupDirection.ChineseToEnglish, chinese);
        return Passage(text);
    }

    // Phrases and sentences skip the offline dictionary and are translated only when an LLM is configured.
    private static DictionaryQuery? Passage(string text)
    {
        var term = text.Trim();
        if (term.Length == 0 || IsAddress(term)) return null;
        if (term.Length > PassageLimit)
        {
            var end = PassageLimit;
            if (char.IsHighSurrogate(term[end - 1])) end--;
            term = term[..end];
        }
        var han = 0;
        var latin = 0;
        try
        {
            foreach (var rune in term.EnumerateRunes())
            {
                if (ChineseHeadword.IsHanRune(rune)) han++;
                else if (rune.Value is >= 'A' and <= 'Z' or >= 'a' and <= 'z') latin++;
            }
        }
        catch (ArgumentException) { return null; }
        if (han == 0 && latin == 0) return null;
        var direction = han >= latin ? LookupDirection.ChineseToEnglish : LookupDirection.EnglishToChinese;
        return new(direction, term, false);
    }

    private static bool IsAddress(string text) =>
        !text.Contains(' ') && !text.Contains('\n') && !text.Contains('\t') &&
        (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("www.", StringComparison.OrdinalIgnoreCase));
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
            if (++count > MaximumLength || !IsHanRune(rune)) return false;
        }
        return count != 0;
    }

    public static bool IsHanRune(System.Text.Rune rune) => rune.Value is 0x3007
        or >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF
        or >= 0x20000 and <= 0x2EE5F or >= 0x2F800 and <= 0x2FA1F or >= 0x30000 and <= 0x3347F;
}
