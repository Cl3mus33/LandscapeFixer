using LandscapeFixer.App.Ui;

namespace LandscapeFixer.App;

static class Program
{
    [STAThread]
    static void Main()
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Dpi.Init();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (_, e) => Crash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash(e.ExceptionObject as Exception);

            // the MO2 instance this copy belongs to, if any, decides which settings file is used
            var store = new SettingsStore(Mo2Detect.Detect());
            Loc.Load(store.Load().UiLanguage);

            // a language change rebuilds the window in place
            bool again;
            do
            {
                using var form = new MainForm(store);
                Application.Run(form);
                again = form.RelaunchRequested;
            } while (again);
        }
        catch (Exception e)
        {
            Crash(e);
        }
    }

    static void Crash(Exception? e)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LandscapeFixer");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "app-crash.log"), $"{DateTime.Now:s} {e}\r\n\r\n");
        }
        catch (Exception log) when (log is IOException or UnauthorizedAccessException) { }

        MessageBox.Show($"Landscape Fixer stopped on an error:\r\n\r\n{e?.Message}", "Landscape Fixer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Environment.Exit(1);
    }
}
