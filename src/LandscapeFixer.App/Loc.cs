using System.Text.Json;

namespace LandscapeFixer.App;

// The translations: LandscapeFixer_translations/<code>.json next to the exe, nested keys read with a dotted
// name ("launcher.gameLocation.label"). English text is always given in the code, so a missing file or key
// just shows English.
static class Loc
{
    public sealed record Language(string Code, string Name);

    static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "LandscapeFixer_translations");
    static JsonElement? _current;

    public static string Code { get; private set; } = "en";

    public static void Load(string code)
    {
        Code = string.IsNullOrWhiteSpace(code) ? "en" : code;
        _current = null;
        try
        {
            var file = Path.Combine(Folder, Code + ".json");
            if (Code != "en" && File.Exists(file))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
                _current = document.RootElement.Clone();
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            _current = null;
        }
    }

    public static string T(string key, string english)
    {
        if (_current is not { } root) return english;
        var node = root;
        foreach (var part in key.Split('.'))
        {
            if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(part, out node)) return english;
        }
        return node.ValueKind == JsonValueKind.String && node.GetString() is { Length: > 0 } text ? text : english;
    }

    public static List<Language> Available()
    {
        var result = new List<Language> { new("en", "English") };
        try
        {
            foreach (var file in Directory.EnumerateFiles(Folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var code = Path.GetFileNameWithoutExtension(file);
                if (code == "en") continue;
                var name = code;
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file));
                    if (document.RootElement.TryGetProperty("_language", out var language) && language.GetString() is { Length: > 0 } text) name = text;
                }
                catch (Exception e) when (e is IOException or JsonException) { }
                result.Add(new Language(code, name));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return result;
    }
}
