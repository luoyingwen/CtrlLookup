using Microsoft.Data.Sqlite;
using SelectionPreview.Core;

namespace SelectionPreview.Dictionary;

public sealed class OfflineDictionary(string path)
{
    public static string DefaultPath => LocalPath("dictionary-v1.sqlite");
    public static string ChinesePath => LocalPath("dictionary-zh-v1.sqlite");

    private static string LocalPath(string filename) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SelectionPreview", filename);

    public string? Lookup(string normalizedWord)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 1
            }.ToString());
            connection.Open();
            using var query = connection.CreateCommand();
            query.CommandText = "SELECT gloss FROM entry WHERE word = $word";
            query.Parameters.AddWithValue("$word", normalizedWord);
            return query.ExecuteScalar() is string gloss && !string.IsNullOrWhiteSpace(gloss) ? gloss : null;
        }
        catch (SqliteException) { return null; } // Optional data: missing schema, corrupt, locked or replaced.
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}

public sealed class OfflineDictionaries(string englishPath, string chinesePath)
{
    private readonly OfflineDictionary _english = new(englishPath);
    private readonly OfflineDictionary _chinese = new(chinesePath);

    public string? Lookup(DictionaryQuery query) => query.Direction switch
    {
        LookupDirection.EnglishToChinese => _english.Lookup(query.Term),
        LookupDirection.ChineseToEnglish => _chinese.Lookup(query.Term),
        _ => null
    };
}
