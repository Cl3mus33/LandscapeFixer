using System.Text.Json;
using LandscapeFixer.Core.Configuration;

namespace LandscapeFixer.App;

// Settings are the ExtractSettings of the Core, written as the same JSON the native launcher used
// (%APPDATA%\LandscapeFixer\settings.json), so an existing file keeps working. When the tool is installed in
// an MO2 instance, each instance has its own file (instances\<name>.json): the modlists don't share an MO2
// path, a profile or an output folder any more.
sealed class SettingsStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LandscapeFixer");
    static string SharedFile => Path.Combine(Root, "settings.json");

    public Mo2Instance? Instance { get; private set; }
    public string FilePath { get; }

    public SettingsStore(Mo2Instance? instance)
    {
        Instance = instance;
        FilePath = instance == null ? SharedFile : Path.Combine(Root, "instances", Sanitize(instance.Name) + ".json");
        // LANDSCAPEFIXER_CONFIG names a settings file to use instead of the usual one (tests, or several setups side by side)
        if (Environment.GetEnvironmentVariable("LANDSCAPEFIXER_CONFIG") is { Length: > 0 } custom)
        {
            Instance = null;
            FilePath = custom;
        }
    }

    public ExtractSettings Load()
    {
        var firstTimeForInstance = Instance != null && !File.Exists(FilePath);
        var settings = ReadFile(firstTimeForInstance ? SharedFile : FilePath) ?? new ExtractSettings();
        if (Instance == null) return settings;

        if (firstTimeForInstance)
        {
            // Starts from the shared file (lists and options carry over) but with this instance's own paths.
            settings.ModManager = ModManagerType.ModOrganizer2;
            settings.Mo2InstancePath = Instance.Path;
            if (Instance.SelectedProfile != "") settings.Mo2ProfileName = Instance.SelectedProfile;
            if (Instance.GamePath != "") settings.GameLocation = Instance.GamePath;
            if (Instance.BaseDirectory != "" && !IsUnder(settings.OutputLocation, Instance.BaseDirectory))
            {
                settings.OutputLocation = Path.Combine(Instance.ModsDirectory, "LandscapeFixer Output");
            }
        }
        else if (settings.Mo2InstancePath == "" || !Directory.Exists(settings.Mo2InstancePath))
        {
            settings.ModManager = ModManagerType.ModOrganizer2;
            settings.Mo2InstancePath = Instance.Path;
        }
        return settings;
    }

    public void Save(ExtractSettings settings) => WriteFile(FilePath, settings);

    // Config profiles (Load Config / Save Config As) use the very same JSON.
    public static ExtractSettings? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<ExtractSettings>(File.ReadAllText(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // unreadable settings are the same as none
        }
    }

    public static bool WriteFile(string path, ExtractSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, Json));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    static bool IsUnder(string path, string folder)
    {
        if (path == "" || folder == "") return false;
        static string N(string p) => p.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
        return N(path).StartsWith(N(folder) + "\\", StringComparison.Ordinal);
    }

    static string Sanitize(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
