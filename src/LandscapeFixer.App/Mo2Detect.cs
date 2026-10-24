using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LandscapeFixer.App.Ui;

namespace LandscapeFixer.App;

/// <summary>An MO2 instance: the folder holding its ModOrganizer.ini and what that file says.</summary>
sealed record Mo2Instance(string Name, string Path, string BaseDirectory, string ModsDirectory, string GamePath, string SelectedProfile);

// Works out which Mod Organizer 2 instance this copy of the tool belongs to, so its settings, MO2 profile and
// output folder are the ones of the modlist it was started from. Launched from MO2, the exe really lives in
// <instance>\mods\<mod>; its own module path is the virtual one, hence Win.RealExePath().
static class Mo2Detect
{
    public static Mo2Instance? Detect()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            return FromExeLocation() ?? FromLaunchingOrganizer();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException or System.Security.SecurityException)
        {
            return null;
        }
    }

    // <instance>\mods\<this mod>\LandscapeFixer.exe
    static Mo2Instance? FromExeLocation()
    {
        var exe = Win.RealExePath();
        if (exe == null) return null;
        var modsOfExe = Norm(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(exe)) ?? "");
        if (modsOfExe == "") return null;

        foreach (var instance in AllInstances())
        {
            if (Norm(instance.ModsDirectory) == modsOfExe) return instance;
        }
        return null;
    }

    // The MO2 that started this process, then the instance it has open (a portable one has its ini next to the
    // exe, a global one is named in the registry).
    static Mo2Instance? FromLaunchingOrganizer()
    {
        var organizer = LaunchingOrganizer();
        if (organizer == null) return null;
        var portableIni = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(organizer)!, "ModOrganizer.ini");
        if (File.Exists(portableIni)) return Read(portableIni, System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(portableIni)!), System.IO.Path.GetDirectoryName(portableIni)!);

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Mod Organizer Team\Mod Organizer");
        if (key?.GetValue("CurrentInstance") is not string name || name == "") return null;
        var ini = System.IO.Path.Combine(GlobalInstancesRoot(), name, "ModOrganizer.ini");
        return File.Exists(ini) ? Read(ini, name, System.IO.Path.GetDirectoryName(ini)!) : null;
    }

    static string GlobalInstancesRoot() =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModOrganizer");

    static IEnumerable<Mo2Instance> AllInstances()
    {
        var root = GlobalInstancesRoot();
        if (!Directory.Exists(root)) yield break;
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            var ini = System.IO.Path.Combine(folder, "ModOrganizer.ini");
            if (!File.Exists(ini)) continue;
            yield return Read(ini, System.IO.Path.GetFileName(folder), folder);
        }
    }

    static Mo2Instance Read(string ini, string name, string instancePath)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(ini))
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2 && !values.ContainsKey(parts[0].Trim())) values[parts[0].Trim()] = Unwrap(parts[1]);
        }

        string Get(string key) => values.TryGetValue(key, out var v) ? v : "";
        var baseDir = Get("base_directory");
        if (baseDir == "") baseDir = System.IO.Path.GetDirectoryName(ini)!;
        var mods = Get("mod_directory");
        mods = mods == "" ? System.IO.Path.Combine(baseDir, "mods") : mods.Replace("%BASE_DIR%", baseDir, StringComparison.OrdinalIgnoreCase);
        return new Mo2Instance(name, instancePath, baseDir, mods, Get("gamePath"), Get("selected_profile"));
    }

    // Qt writes values as @ByteArray(...), quoted, with doubled backslashes or forward slashes.
    static string Unwrap(string raw)
    {
        var value = raw.Trim();
        if (value.StartsWith("@ByteArray(", StringComparison.Ordinal) && value.EndsWith(')')) value = value[11..^1];
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
        return value.Replace(@"\\", @"\").Replace('/', '\\');
    }

    static string Norm(string path) => path.Replace('/', '\\').Replace(@"\\", @"\").TrimEnd('\\').ToLowerInvariant();

    // ---- the parent process chain ----

    static string? LaunchingOrganizer()
    {
        var parents = ParentIds();
        var id = Environment.ProcessId;
        for (var depth = 0; depth < 5 && parents.TryGetValue(id, out var parent); depth++)
        {
            id = parent;
            try
            {
                using var process = Process.GetProcessById(id);
                if (process.ProcessName.Equals("ModOrganizer", StringComparison.OrdinalIgnoreCase)) return process.MainModule?.FileName;
            }
            catch (ArgumentException) { return null; }
        }
        return null;
    }

    static Dictionary<int, int> ParentIds()
    {
        var result = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return result;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (var more = Process32First(snapshot, ref entry); more; more = Process32Next(snapshot, ref entry))
                result[(int)entry.ProcessId] = (int)entry.ParentProcessId;
        }
        finally { CloseHandle(snapshot); }
        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode)] static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)] static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
}
