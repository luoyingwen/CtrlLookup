using CtrlLookup.Core;

namespace CtrlLookup.Dictionary;

public sealed class LookupHistory(string path)
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CtrlLookup", "lookup-history.txt");

    public void Record(DictionaryQuery query, string? gloss)
    {
        if (!query.Headword || string.IsNullOrWhiteSpace(gloss)) return;

        var entry = query.Direction switch
        {
            LookupDirection.EnglishToChinese => EnglishHeadword.Normalize(query.Term),
            LookupDirection.ChineseToEnglish => string.Join(Environment.NewLine,
                gloss.Split('\n', StringSplitOptions.TrimEntries).Where(IsEnglishGloss)),
            _ => null
        };
        if (string.IsNullOrEmpty(entry)) return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.AppendAllText(path, entry + Environment.NewLine);
        }
        catch (IOException) { } // History is best-effort and must not prevent lookup results.
        catch (UnauthorizedAccessException) { }
    }

    // Some CC-CEDICT senses are cross-references or contain Han annotations. Skip those
    // lines instead of recording the source word or extracting fragments from a mixed gloss.
    private static bool IsEnglishGloss(string line) => line.Any(IsLatinLetter) && line.All(ch =>
        ch is >= ' ' and <= '~' or '‘' or '’' or '“' or '”' or '–' or '—' or '…' || IsLatinLetter(ch));

    private static bool IsLatinLetter(char ch) => char.IsAsciiLetter(ch) ||
        (ch is >= '\u00C0' and <= '\u024F' or >= '\u1E00' and <= '\u1EFF') && char.IsLetter(ch);
}
