using Microsoft.Data.Sqlite;

namespace SelectionPreview.Dictionary;

public sealed class OfflineDictionary(string path)
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SelectionPreview", "dictionary-v1.sqlite");

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
