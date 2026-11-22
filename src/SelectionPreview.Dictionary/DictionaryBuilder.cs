using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic.FileIO;
using SelectionPreview.Core;

namespace SelectionPreview.Dictionary;

public static class DictionaryBuilder
{
    public static int Build(string csvPath, string destination, bool overwrite = false)
    {
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!overwrite && File.Exists(destination)) throw new IOException("The dictionary already exists.");

        // Stage next to the destination so a failure never replaces the installed dictionary.
        var staging = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var count = Import(csvPath, staging);
            if (count == 0) throw new InvalidDataException("No usable English–Chinese entries were found.");
            File.Move(staging, destination, overwrite);
            return count;
        }
        finally
        {
            File.Delete(staging);
        }
    }

    private static int Import(string csvPath, string staging)
    {
        using var csv = new TextFieldParser(csvPath, new UTF8Encoding(false, true)) { TrimWhiteSpace = false };
        csv.SetDelimiters(",");
        var header = csv.ReadFields() ?? [];
        var wordColumn = Array.IndexOf(header, "word");
        var translationColumn = Array.IndexOf(header, "translation");
        if (wordColumn < 0 || translationColumn < 0) throw new InvalidDataException("Expected ECDICT word and translation columns.");

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = staging, Pooling = false
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version = 1; CREATE TABLE entry (word TEXT PRIMARY KEY, gloss TEXT NOT NULL) WITHOUT ROWID;";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT OR IGNORE INTO entry (word, gloss) VALUES ($word, $gloss)";
        var word = command.Parameters.Add("$word", SqliteType.Text);
        var gloss = command.Parameters.Add("$gloss", SqliteType.Text);
        command.Prepare();

        var count = 0;
        while (csv.ReadFields() is { } row)
        {
            if (row.Length != header.Length) throw new InvalidDataException($"Unexpected CSV column count at line {csv.LineNumber}.");
            if (EnglishHeadword.Normalize(row[wordColumn].Trim()) is not { } key) continue;
            var lines = row[translationColumn].Replace("\\n", "\n", StringComparison.Ordinal)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(2).ToArray();
            if (lines.Length == 0) continue;
            word.Value = key;
            gloss.Value = string.Join('\n', lines);
            count += command.ExecuteNonQuery();
        }
        transaction.Commit();
        command.Transaction = null;
        command.CommandText = "VACUUM";
        command.Parameters.Clear();
        command.ExecuteNonQuery();
        return count;
    }
}
