using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using SelectionPreview.Core;

namespace SelectionPreview.Dictionary;

public static class ChineseDictionaryBuilder
{
    public const string SourceUrl = "https://www.mdbg.net/chinese/export/cedict/cedict_1_0_ts_utf-8_mdbg.txt.gz";
    private const string LicenseUrl = "https://creativecommons.org/licenses/by-sa/4.0/";

    public static int Build(string gzipPath, string destination, bool overwrite = false) =>
        DictionaryBuilder.BuildStaged(destination, overwrite, staging => Import(gzipPath, staging));

    private static int Import(string gzipPath, string staging)
    {
        using var input = File.OpenRead(gzipPath);
        var hash = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        input.Position = 0;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, new UTF8Encoding(false, true));

        var entries = new Dictionary<string, GlossPair>(StringComparer.Ordinal);
        var hasTitle = false;
        var hasLicense = false;
        var lineNumber = 0;
        while (reader.ReadLine() is { } raw)
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line[0] == '#')
            {
                hasTitle |= line.Equals("# CC-CEDICT", StringComparison.Ordinal);
                hasLicense |= line.Equals("# " + LicenseUrl, StringComparison.Ordinal);
                continue;
            }

            // Official v1 text format: traditional simplified [pinyin] /sense/sense/.
            var pinyin = line.IndexOf(" [", StringComparison.Ordinal);
            var definitions = pinyin < 0 ? -1 : line.IndexOf("] /", pinyin, StringComparison.Ordinal);
            if (pinyin <= 0 || definitions < 0 || line.Length <= definitions + 4 || line[^1] != '/')
                throw new InvalidDataException($"Invalid CC-CEDICT entry at line {lineNumber}.");
            var forms = line[..pinyin].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (forms.Length != 2) throw new InvalidDataException($"Invalid CC-CEDICT headwords at line {lineNumber}.");
            var (traditional, simplified) = (forms[0], forms[1]);
            var useTraditional = ChineseHeadword.IsHeadword(traditional);
            var useSimplified = simplified != traditional && ChineseHeadword.IsHeadword(simplified);
            if (!useTraditional && !useSimplified) continue;
            foreach (var sense in line[(definitions + 3)..^1].Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (sense.StartsWith("CL:", StringComparison.OrdinalIgnoreCase)) continue;
                if (useTraditional) AddSense(entries, traditional, sense);
                if (useSimplified) AddSense(entries, simplified, sense);
            }
        }
        if (!hasTitle || !hasLicense) throw new InvalidDataException("Expected the CC-CEDICT title and CC BY-SA 4.0 notice.");
        if (entries.Count == 0) return 0;
        WriteDatabase(staging, entries, hash);
        return entries.Count;
    }

    private static void AddSense(Dictionary<string, GlossPair> entries, string headword, string sense)
    {
        if (!entries.TryGetValue(headword, out var existing)) entries.Add(headword, new(sense));
        else if (existing.Second is null && existing.First != sense) entries[headword] = existing with { Second = sense };
    }

    private static void WriteDatabase(string staging, Dictionary<string, GlossPair> entries, string hash)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = staging, Pooling = false
        }.ToString());
        connection.Open();
        using (var transaction = connection.BeginTransaction())
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = DictionaryBuilder.EntrySchema + """
                 CREATE TABLE metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL) WITHOUT ROWID;
                 INSERT INTO metadata (key, value) VALUES
                   ('source_url', $source), ('source_sha256', $sha256), ('license_url', $license), ('attribution', $attribution);
                """;
            command.Parameters.AddWithValue("$source", SourceUrl);
            command.Parameters.AddWithValue("$sha256", hash);
            command.Parameters.AddWithValue("$license", LicenseUrl);
            command.Parameters.AddWithValue("$attribution", "CC-CEDICT contributors, published by MDBG; based on CEDICT by Paul Andrew Denisowski.");
            command.ExecuteNonQuery();
            command.Parameters.Clear();
            command.CommandText = "INSERT INTO entry (word, gloss) VALUES ($word, $gloss)";
            var word = command.Parameters.Add("$word", SqliteType.Text);
            var gloss = command.Parameters.Add("$gloss", SqliteType.Text);
            command.Prepare();
            foreach (var (headword, pair) in entries)
            {
                word.Value = headword;
                gloss.Value = pair.Second is null ? pair.First : pair.First + "\n" + pair.Second;
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        using var compact = connection.CreateCommand();
        compact.CommandText = "VACUUM";
        compact.ExecuteNonQuery();
    }

    private readonly record struct GlossPair(string First, string? Second = null);
}
