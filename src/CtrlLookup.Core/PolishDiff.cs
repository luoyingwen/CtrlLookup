using System.Globalization;
using System.Text;

namespace CtrlLookup.Core;

public readonly record struct TextHighlight(int Start, int Length);

public static class PolishDiff
{
    /// <summary>Finds added and rewritten text in the result, ignoring whitespace-only edits.</summary>
    public static IReadOnlyList<TextHighlight> FindHighlights(string original, string revised)
    {
        // Live requests and replies are already capped. Keep the quadratic alignment bounded here too.
        if (original == revised || original.Length > PreviewText.MaximumLength || revised.Length > PreviewText.MaximumLength)
            return [];

        var before = Tokenize(original);
        var after = Tokenize(revised);
        var width = after.Count + 1;
        var lengths = new ushort[(before.Count + 1) * width];
        for (var i = before.Count - 1; i >= 0; i--)
        {
            for (var j = after.Count - 1; j >= 0; j--)
            {
                var cell = i * width + j;
                lengths[cell] = before[i].Text == after[j].Text
                    ? (ushort)(lengths[cell + width + 1] + 1)
                    : Math.Max(lengths[cell + width], lengths[cell + 1]);
            }
        }

        var highlights = new List<TextHighlight>();
        var oldIndex = 0;
        var newIndex = 0;
        var start = -1;
        var end = 0;
        while (newIndex < after.Count)
        {
            if (oldIndex < before.Count && before[oldIndex].Text == after[newIndex].Text)
            {
                Flush();
                oldIndex++;
                newIndex++;
            }
            else if (oldIndex < before.Count &&
                lengths[(oldIndex + 1) * width + newIndex] >= lengths[oldIndex * width + newIndex + 1])
            {
                oldIndex++;
            }
            else
            {
                var token = after[newIndex++];
                // Merge neighboring edits, but leave line breaks and indentation unstyled.
                if (start >= 0 && revised.AsSpan(end, token.Start - end).ContainsAny('\r', '\n')) Flush();
                if (start < 0) start = token.Start;
                end = token.Start + token.Text.Length;
            }
        }
        Flush();
        return highlights;

        void Flush()
        {
            if (start < 0) return;
            highlights.Add(new(start, end - start));
            start = -1;
        }
    }

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        for (var index = 0; index < text.Length;)
        {
            var end = index + StringInfo.GetNextTextElementLength(text.AsSpan(index));
            if (char.IsWhiteSpace(text[index])) { index = end; continue; }
            if (IsWordLetter(text, index))
            {
                // Keep words (including contractions and hyphenation) whole when matching edits.
                while (end < text.Length)
                {
                    if (text[end] is '\'' or '’' or '-' && end + 1 < text.Length && IsWordLetter(text, end + 1)) end++;
                    if (!IsWordLetter(text, end)) break;
                    end += StringInfo.GetNextTextElementLength(text.AsSpan(end));
                }
            }
            tokens.Add(new(text[index..end], index));
            index = end;
        }
        return tokens;
    }

    // In scripts commonly written without spaces, compare graphemes rather than whole clauses.
    private static bool IsWordLetter(string text, int index) => Rune.TryGetRuneAt(text, index, out var rune)
        && Rune.IsLetterOrDigit(rune) && !ChineseHeadword.IsHanRune(rune)
        && rune.Value is not (>= 0x3040 and <= 0x30FF or >= 0x0E00 and <= 0x0EFF or >= 0x1780 and <= 0x17FF);

    private readonly record struct Token(string Text, int Start);
}
