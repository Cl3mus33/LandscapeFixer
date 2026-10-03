using LandscapeFixer.Core.Configuration;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Skyrim;

namespace LandscapeFixer.Core.Pipeline;

/// <summary>
/// Everything the passes of one run share: the loaded environment, the file probe, the single output
/// plugin all of them write into, and the thread-safe diagnostics/progress sinks. The orchestrator
/// builds it once; the blend pass and the snow pass only read from it and add to the output.
/// </summary>
public sealed class PassContext
{
    private readonly object _diagnosticsLock = new();
    private readonly List<string> _diagnostics;
    private readonly Action<ExtractProgress> _progress;

    public PassContext(
        ExtractSettings settings,
        IGameEnvironment<ISkyrimMod, ISkyrimModGetter> env,
        IGameFileProbe fileProbe,
        Mo2InstanceReader? mo2Reader,
        SkyrimMod outputMod,
        string dataFolder,
        string outputFolder,
        GameRelease gameRelease,
        List<string> diagnostics,
        Action<ExtractProgress>? progress)
    {
        Settings = settings;
        Env = env;
        FileProbe = fileProbe;
        Mo2Reader = mo2Reader;
        OutputMod = outputMod;
        DataFolder = dataFolder;
        OutputFolder = outputFolder;
        GameRelease = gameRelease;
        Scope = new LandscapeScope(settings);
        _diagnostics = diagnostics;
        _progress = progress ?? (_ => { });
    }

    public ExtractSettings Settings { get; }
    public IGameEnvironment<ISkyrimMod, ISkyrimModGetter> Env { get; }
    public IGameFileProbe FileProbe { get; }
    public Mo2InstanceReader? Mo2Reader { get; }
    public SkyrimMod OutputMod { get; }
    public string DataFolder { get; }
    public string OutputFolder { get; }
    public GameRelease GameRelease { get; }

    /// <summary>Which meshes are in the tool's scope at all (landscape folders + the manual add list).</summary>
    public LandscapeScope Scope { get; }

    public void AddDiagnostic(string message)
    {
        lock (_diagnosticsLock)
        {
            _diagnostics.Add(message);
        }
    }

    public void AddDiagnostics(IEnumerable<string> messages)
    {
        lock (_diagnosticsLock)
        {
            _diagnostics.AddRange(messages);
        }
    }

    public void Report(string message, int current = 0, int total = 0) =>
        _progress(new ExtractProgress(message, current, total));
}
