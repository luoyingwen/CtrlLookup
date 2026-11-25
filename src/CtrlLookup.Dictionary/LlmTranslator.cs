using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CtrlLookup.Core;

namespace CtrlLookup.Dictionary;

/// <summary>Optional OpenAI-compatible translation used only after the offline dictionary misses.</summary>
public sealed class LlmTranslator : IDisposable
{
    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CtrlLookup", "llm.json");

    private const int GlossLines = 2;
    private const int GlossLength = 80;
    private const int CacheLimit = 256;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _model;
    private readonly string? _apiKey;
    private readonly Dictionary<(LookupDirection Direction, string Term), string> _cache = new();

    private LlmTranslator(HttpMessageHandler handler, Uri endpoint, string model, string? apiKey, bool ownsHandler)
    {
        _endpoint = endpoint;
        _model = model;
        _apiKey = apiKey;
        _http = new HttpClient(handler, ownsHandler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    public static LlmTranslator? TryCreate() => Load(
        Environment.GetEnvironmentVariable("CTRLLOOKUP_LLM_ENDPOINT"),
        Environment.GetEnvironmentVariable("CTRLLOOKUP_LLM_MODEL"),
        Environment.GetEnvironmentVariable("CTRLLOOKUP_LLM_API_KEY"),
        SettingsPath);

    // Explicit endpoint and model win. Blank values fall through to the settings file.
    public static LlmTranslator? Load(string? endpoint, string? model, string? apiKey, string settingsPath, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
            (endpoint, model, apiKey) = ReadSettings(settingsPath);
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model)) return null;
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        var ownsHandler = handler is null;
        return new LlmTranslator(handler ?? new SocketsHttpHandler(), uri, model.Trim(),
            string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim(), ownsHandler);
    }

    public string? Translate(DictionaryQuery query, CancellationToken cancellation = default)
    {
        if (query.Direction is not (LookupDirection.EnglishToChinese or LookupDirection.ChineseToEnglish)) return null;
        var key = (query.Direction, query.Term);
        if (_cache.TryGetValue(key, out var cached)) return cached;
        if (cancellation.IsCancellationRequested) return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            if (_apiKey is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = new StringContent(RequestJson(query), Encoding.UTF8, "application/json");
            using var response = _http.SendAsync(request, cancellation).ConfigureAwait(false).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return null;
            var body = response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false).GetAwaiter().GetResult();
            var gloss = ReadGloss(body, query.Headword);
            if (gloss is not null && _cache.Count < CacheLimit) _cache.Add(key, gloss);
            return gloss;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or OperationCanceledException)
        {
            return null; // The popup keeps the original text when the service is missing or slow.
        }
    }

    public void Dispose() => _http.Dispose();

    private string RequestJson(DictionaryQuery query)
    {
        var target = query.Direction == LookupDirection.EnglishToChinese ? "Simplified Chinese" : "English";
        var payload = new JsonObject
        {
            ["model"] = _model,
            ["temperature"] = 0,
            ["max_tokens"] = query.Headword ? 80 : 1024,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = query.Headword
                    ? "Reply with at most two short dictionary glosses, one per line. No pronunciation, examples, labels, or explanation."
                    : $"Translate the text into {target}. Return only the translation and preserve line breaks." },
                new JsonObject { ["role"] = "user", ["content"] = query.Headword
                    ? $"Translate this headword into {target}:\n{query.Term}"
                    : query.Term }
            }
        };
        // DeepSeek thinks by default and can spend the whole token budget before writing the gloss.
        if (_endpoint.Host.Equals("deepseek.com", StringComparison.OrdinalIgnoreCase)
            || _endpoint.Host.EndsWith(".deepseek.com", StringComparison.OrdinalIgnoreCase))
            payload["thinking"] = new JsonObject { ["type"] = "disabled" };
        return payload.ToJsonString(Json);
    }

    private static string? ReadGloss(byte[] body, bool headword)
    {
        if (body.Length == 0 || body.Length > 65536) return null;
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array) return null;
        foreach (var choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("message", out var message)) continue;
            if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String) continue;
            return headword ? FormatGloss(content.GetString()) : FormatPassage(content.GetString());
        }
        return null;
    }

    private static string? FormatPassage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim().Replace("\r\n", "\n", StringComparison.Ordinal);
        if (trimmed.Length <= 1500) return trimmed;
        var end = 1500;
        if (char.IsHighSurrogate(trimmed[end - 1])) end--;
        return string.Concat(trimmed.AsSpan(0, end), "…");
    }

    private static string? FormatGloss(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lines = new List<string>(GlossLines);
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = StripLabel(raw.Trim().Trim('`', '"', '\'', '“', '”', '*'));
            if (line.Length == 0) continue;
            if (line.Length > GlossLength)
            {
                var end = GlossLength;
                if (char.IsHighSurrogate(line[end - 1])) end--;
                line = string.Concat(line.AsSpan(0, end), "…");
            }
            lines.Add(line);
            if (lines.Count == GlossLines) break;
        }
        return lines.Count == 0 ? null : string.Join('\n', lines);
    }

    private static string StripLabel(string line)
    {
        var index = 0;
        while (index < line.Length && char.IsDigit(line[index])) index++;
        if (index > 0 && index < line.Length && line[index] is '.' or ')')
        {
            index++;
            if (index < line.Length && line[index] == ' ') index++;
            line = line[index..].Trim();
        }
        if (line.StartsWith("- ", StringComparison.Ordinal)) line = line[2..].Trim();
        return line;
    }

    private static (string? Endpoint, string? Model, string? ApiKey) ReadSettings(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return default;
            var settings = JsonSerializer.Deserialize<LlmSettings>(File.ReadAllText(path), Json);
            return (settings?.Endpoint, settings?.Model, settings?.ApiKey);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return default;
        }
    }

    private sealed class LlmSettings
    {
        public string? Endpoint { get; set; }
        public string? Model { get; set; }
        public string? ApiKey { get; set; }
    }
}

public static class DictionaryTranslation
{
    public static string? Lookup(OfflineDictionaries dictionaries, LlmTranslator? translator, DictionaryQuery query, CancellationToken cancellation = default)
    {
        if (query.Headword && dictionaries.Lookup(query) is { } gloss) return gloss;
        return translator?.Translate(query, cancellation);
    }
}
