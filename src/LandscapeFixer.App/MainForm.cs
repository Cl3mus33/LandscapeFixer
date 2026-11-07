using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using LandscapeCommon.Scanning;
using LandscapeFixer.App.Ui;
using LandscapeFixer.Core.Configuration;
using LandscapeFixer.Core.Pipeline;

namespace LandscapeFixer.App;

// The tool's window: setup, what to touch, and one button. The settings sit in four collapsible cards on the
// left (Blend and Snow unfold when ticked); the right side shows what the run changed and
// the messages of the run.
sealed class MainForm : Form
{
    const int CardWidth = 612;

    readonly SettingsStore _store;
    ExtractSettings _s;
    readonly HelpTip _tip = new();

    // setup
    TextBox _game = null!, _output = null!, _instance = null!;
    DarkSelect _gameType = null!, _manager = null!, _profile = null!;
    Button _instanceBrowse = null!;
    Label _instanceCaption = null!, _profileCaption = null!;
    // landscape
    TextBox _addList = null!;
    RadioButton[] _landscapeColors = null!, _meshColors = null!;
    // blend
    CheckBox _pbr = null!;
    Label _pbrNote = null!;
    TextBox _blendBlacklist = null!, _blendKeywords = null!;
    List<string> _allowlist = new();
    // snow
    RadioButton[] _collision = null!;
    CheckBox _hideDecals = null!, _removeIce = null!;
    TextBox _snowBlacklist = null!, _snowKeywords = null!;

    Card _setupCard = null!, _landscapeCard = null!, _blendCard = null!, _snowCard = null!;
    readonly Panel _cards = new() { AutoScroll = true, BackColor = Theme.Back };
    readonly Label _status = Widgets.Caption("Ready.");
    readonly Button _generate = Widgets.Flat("Generate", true), _open = Widgets.Flat("Open output folder", false);
    readonly ProgressLine _progress = new();
    readonly StatsPanel _stats = new();
    readonly TextBox _log = new();
    readonly System.Windows.Forms.Timer _debounce = new() { Interval = 450 };
    bool _busy, _loading;
    string _lastLogged = "";

    public bool RelaunchRequested { get; private set; }

    public MainForm(SettingsStore store)
    {
        _store = store;
        _s = store.Load();

        Text = "Landscape Fixer";
        ClientSize = Dpi.S(1060, 760);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = Dpi.Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.None;
        try { if (Environment.ProcessPath is { } exe) Icon = Icon.ExtractAssociatedIcon(exe); } catch (ArgumentException) { } catch (IOException) { }

        Place(new Label { Text = "Landscape Fixer", Font = Dpi.Font("Segoe UI Semibold", 18f), ForeColor = Theme.Accent, AutoSize = true }, 20, 12);
        Place(Widgets.Caption(Loc.T("launcher.subtitle", "Snow and blend fixes for the game's landscape meshes")), 24, 52);

        var gear = Widgets.Flat("⚙", false);
        gear.Font = Dpi.Font("Segoe UI Symbol", 13f);
        gear.FlatAppearance.BorderSize = 0;
        gear.BackColor = Theme.Back;
        _tip.SetToolTip(gear, Loc.T("launcher.preferences.tooltip", "Preferences (language)"));
        gear.Click += (_, _) => OpenPreferences();
        Place(gear, 618, 14, 40, 36);

        _cards.Location = Dpi.P(14, 84);
        _cards.Size = Dpi.S(656, 556);
        Controls.Add(_cards);
        Win.DarkScroll(_cards);

        BuildSetup();
        BuildLandscape();
        BuildBlend();
        BuildSnow();
        foreach (var card in new[] { _setupCard, _landscapeCard, _blendCard, _snowCard })
        {
            card.LayoutChanged += LayoutCards;
            _cards.Controls.Add(card);
        }
        LayoutCards();

        _generate.Text = Loc.T("run.generate", "Generate");
        _open.Text = Loc.T("run.openOutput", "Open output folder");
        Place(_generate, 24, 656, 190, 44);
        Place(_open, 224, 656, 160, 44);
        Place(_progress, 24, 712, 636, 6);
        _status.Text = Loc.T("run.ready", "Ready.");
        Place(_status, 24, 726);

        Place(_stats, 684, 24, 352, 170);
        _stats.Font = Font;
        _stats.Show(Array.Empty<StatsPanel.Tile>(), Loc.T("run.hint", "What each run changed appears here."));

        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Theme.Panel;
        _log.ForeColor = Theme.Dim;
        _log.Font = Dpi.Font("Consolas", 9f);
        _log.WordWrap = true;
        Place(_log, 684, 206, 352, 530);
        Win.DarkScroll(_log);

        Populate();
        ActiveControl = _generate;
        _generate.Click += async (_, _) => await RunAsync();
        _open.Click += (_, _) => OpenOutput();
        _open.Enabled = Directory.Exists(_output.Text);
        _debounce.Tick += (_, _) => { _debounce.Stop(); RefreshProfiles(); };
        FormClosing += OnClosing;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Win.DarkTitleBar(Handle);
    }

    void Place(Control control, int x, int y, int width = 0, int height = 0)
    {
        control.Location = Dpi.P(x, y);
        if (width > 0) control.Width = Dpi.P(width);
        if (height > 0) control.Height = Dpi.P(height);
        Controls.Add(control);
    }

    // ---- the cards ----

    void LayoutCards()
    {
        var y = Dpi.P(0);
        foreach (var card in new[] { _setupCard, _landscapeCard, _blendCard, _snowCard })
        {
            card.Location = new Point(Dpi.P(2), y);
            card.Width = Dpi.P(CardWidth);
            y += card.Height + Dpi.P(10);
        }
    }

    static string T(string key, string english) => Loc.T(key, english);

    void BuildSetup()
    {
        _setupCard = new Card(T("card.setup.title", "Setup"), T("card.setup.description", "Game, mod manager and output folder"), false, false, true);
        var st = new Stack(_setupCard.Body, CardWidth, _tip);

        st.Caption(T("launcher.configProfile.label", "Config Profile"));
        var row = st.Add(new Panel { BackColor = Color.Transparent }, 30, CardWidth - 28);
        var load = Widgets.Flat(T("launcher.configProfile.load", "Load Config..."), false);
        var save = Widgets.Flat(T("launcher.configProfile.saveAs", "Save Config As..."), false);
        var loadWidth = Math.Max(Dpi.P(150), TextRenderer.MeasureText(load.Text, load.Font).Width + Dpi.P(30));
        var saveWidth = Math.Max(Dpi.P(160), TextRenderer.MeasureText(save.Text, save.Font).Width + Dpi.P(30));
        load.Bounds = new Rectangle(0, 0, loadWidth, Dpi.P(28));
        save.Bounds = new Rectangle(loadWidth + Dpi.P(10), 0, saveWidth, Dpi.P(28));
        row.Controls.AddRange(new Control[] { load, save });
        load.Click += (_, _) => LoadConfig();
        save.Click += (_, _) => SaveConfig();

        st.Caption(T("launcher.gameLocation.label", "Game Location"));
        (_game, _) = st.PathRow(T("launcher.gameLocation.dialogTitle", "Select Game Location"));

        st.Caption(T("launcher.gameType.label", "Game Type"));
        _gameType = st.Add(Widgets.Combo(T("launcher.gameType.se", "Skyrim Special Edition"), T("launcher.gameType.le", "Skyrim Legendary Edition")), 26, 300);
        _gameType.SelectedIndexChanged += (_, _) => UpdateGameTypeState();

        st.Caption(T("launcher.modManager.label", "Mod Manager"));
        _manager = st.Add(Widgets.Combo(T("launcher.modManager.none", "None / Vortex"), T("launcher.modManager.mo2", "Mod Organizer 2")), 26, 300);
        _manager.SelectedIndexChanged += (_, _) => UpdateMo2State();

        _instanceCaption = st.Caption(T("launcher.mo2InstancePath.label", "MO2 Instance Path"));
        (_instance, _instanceBrowse) = st.PathRow(T("launcher.mo2InstancePath.dialogTitle", "Select MO2 Instance Folder"));
        _instance.TextChanged += (_, _) => { if (!_loading) { _debounce.Stop(); _debounce.Start(); } };

        _profileCaption = st.Caption(T("launcher.mo2Profile.label", "MO2 Profile"));
        _profile = st.Add(Widgets.Combo(), 26, 300);

        st.Caption(T("launcher.outputLocation.label", "Output Location"));
        (_output, _) = st.PathRow(T("launcher.outputLocation.dialogTitle", "Select Output Location"));
        _output.TextChanged += (_, _) => { if (!_busy) _open.Enabled = Directory.Exists(_output.Text); };

        _setupCard.BodyHeight = st.Height;
    }

    void BuildLandscape()
    {
        _landscapeCard = new Card(T("card.landscape.title", "Landscape"), T("card.landscape.description", "Which meshes are touched, and the vertex colors"), false, false, false);
        var st = new Stack(_landscapeCard.Body, CardWidth, _tip);

        st.Caption(T("launcher.meshAddList.label", "Additional Meshes"), T("launcher.meshAddList.help", "Only meshes inside a landscape folder (Meshes\\Landscape, Meshes\\DLC01\\Landscape, Meshes\\_ResourcePack\\Landscape, ...) are processed. List here the meshes outside those folders to process anyway, one pattern per line. Wildcards (*) allowed, e.g. \"*\\whiterun\\wrterrain\\*\"."));
        _addList = st.Add(Widgets.Lines(Array.Empty<string>()), 78, CardWidth - 28);

        st.Caption(T("launcher.landscapeVertexColors.label", "Landscape Vertex Colors"), T("launcher.landscapeVertexColors.help", "Clears baked-in vertex color tinting on terrain (LAND) records, so they render standalone instead of carrying leftover color grading meant for a different look."));
        (_, _landscapeColors) = RadioGroup(st, new[]
        {
            T("launcher.landscapeVertexColors.none", "Don't touch landscape vertex colors"),
            T("launcher.landscapeVertexColors.all", "Clear on all terrain"),
            T("launcher.landscapeVertexColors.snow", "Clear only where a snow-classified texture is used"),
        });

        st.Caption(T("launcher.meshVertexColors.label", "Mesh Vertex Colors"), T("launcher.meshVertexColors.help", "Clears baked-in vertex color tinting on landscape-folder meshes (rocks, cliffs, icebergs, ...), including DLC variants under DLC01\\Landscape\\, DLC02\\Landscape\\, etc."));
        (_, _meshColors) = RadioGroup(st, new[]
        {
            T("launcher.meshVertexColors.none", "Don't touch mesh vertex colors"),
            T("launcher.meshVertexColors.snowOnly", "Only on the generated snow meshes"),
            T("launcher.meshVertexColors.all", "On all landscape meshes"),
        });

        _landscapeCard.BodyHeight = st.Height;
    }

    void BuildBlend()
    {
        _blendCard = new Card(T("card.blend.title", "Blend"), T("card.blend.description", "Alpha test to blend, derived TextureSets, PBR"), true, _s.EnableBlend, false);
        var st = new Stack(_blendCard.Body, CardWidth, _tip);

        _pbr = Widgets.Check(T("launcher.generatePbrSlots.label", "Generate PBR slots"));
        _tip.SetToolTip(_pbr, T("launcher.generatePbrSlots.help", "Needed when a PBR texture pack is installed: generates the PBR version of each blend texture and the json PG Patcher uses to convert it. Left off, the blend areas show the low-resolution vanilla texture."));
        st.Add(_pbr, 22);
        st.Gap(14); // room for the note under the box
        // PBR needs Community Shaders, which only exists for Skyrim SE: say why the box is off instead of just greying it
        _pbrNote = Widgets.Caption(T("launcher.generatePbrSlots.leNote", "Not available on Legendary Edition (PBR needs Skyrim Special Edition and Community Shaders)."));
        _pbrNote.ForeColor = Theme.Orange;
        _pbrNote.Location = Dpi.P(36, 0);
        _blendCard.Body.Controls.Add(_pbrNote);

        st.Caption(T("launcher.autoGenerateAllowlist.label", "Auto-Generate Allowlist"), T("launcher.autoGenerateAllowlist.help", "Only these source diffuse textures (relative to Data, e.g. \"textures\\landscape\\dirt01.dds\") get a missing statics sibling synthesized automatically. Wildcards (*) allowed."));
        var edit = Widgets.Flat(T("launcher.autoGenerateAllowlist.editButton", "Edit Allowlist..."), false);
        st.Add(edit, 28, 170);
        edit.Click += (_, _) =>
        {
            using var dialog = new TextListForm(T("launcher.autoGenerateAllowlist.label", "Auto-Generate Allowlist"), _allowlist);
            if (dialog.ShowDialog(this) == DialogResult.OK) _allowlist = dialog.Lines;
        };

        st.Caption(T("launcher.blendMeshBlacklist.label", "Blend Mesh Blacklist"), T("launcher.blendMeshBlacklist.help", "Meshes matching a rule here are never patched by the blend pass (ice, roads, trees, ...), one rule per line. Wildcards (*) allowed."));
        _blendBlacklist = st.Add(Widgets.Lines(Array.Empty<string>()), 100, CardWidth - 28);

        st.Caption(T("launcher.blendEditorIdKeywords.label", "Blend EditorID Blacklist Keywords"), T("launcher.blendEditorIdKeywords.help", "Records whose EditorID contains one of these words (case-insensitive) are never touched by the blend pass, one per line."));
        _blendKeywords = st.Add(Widgets.Lines(Array.Empty<string>()), 70, CardWidth - 28);

        _blendCard.BodyHeight = st.Height;
    }

    void BuildSnow()
    {
        _snowCard = new Card(T("card.snow.title", "Snow"), T("card.snow.description", "Snow record fixes: collision, decals, ice"), true, _s.EnableSnow, false);
        var st = new Stack(_snowCard.Body, CardWidth, _tip);

        st.Caption(T("launcher.collisionMaterial.label", "Collision Material (Footstep Sounds)"), T("launcher.collisionMaterial.help", "Remaps collision materials on the generated snow meshes toward their snow equivalent (currently just Dirt/Grass -> Snow) so footstep sounds match the snowy visual."));
        (_, _collision) = RadioGroup(st, new[]
        {
            T("launcher.collisionMaterial.none", "Don't touch collision materials"),
            T("launcher.collisionMaterial.snowOnly", "Apply on the generated snow meshes"),
        });

        st.Caption(T("launcher.hideDecalShapes.label", "Hide Decal Shapes"), T("launcher.hideDecalShapes.help", "Avoids z-fighting with Skyrim's own decal-based dynamic snow shaders by hiding any shape textured with \"Rocks01\"/\"SnowRocks01\", on mountain/rock/tundra meshes only. Hides the shape instead of deleting it, so no plugin changes are needed."));
        _hideDecals = st.Add(Widgets.Check(T("launcher.hideDecalShapes.checkbox", "Hide Rocks01/SnowRocks01 decal shapes")), 22);

        st.Caption(T("launcher.removeIceSnowMaterial.label", "Ice Snow Material"), T("launcher.removeIceSnowMaterial.help", "Clears the SnowMaterialGlacier / SnowMaterialGlacierSlab Material Object from every static that uses it, so no snow is projected on glaciers and ice. Only changes LandscapeFixer.esp - no mesh is modified."));
        _removeIce = st.Add(Widgets.Check(T("launcher.removeIceSnowMaterial.checkbox", "Remove the glacier snow material from statics")), 22);

        st.Caption(T("launcher.meshBlacklist.label", "Snow Mesh Blacklist"), T("launcher.meshBlacklist.help", "Meshes matching a rule here are never copied by the snow pass, one rule per line. Wildcards (*) allowed, e.g. \"*\\trees\\*\"."));
        _snowBlacklist = st.Add(Widgets.Lines(Array.Empty<string>()), 100, CardWidth - 28);

        st.Caption(T("launcher.editorIdKeywords.label", "Snow EditorID Blacklist Keywords"), T("launcher.editorIdKeywords.help", "Records whose EditorID contains one of these words (case-insensitive) are skipped by the snow pass, one per line."));
        _snowKeywords = st.Add(Widgets.Lines(Array.Empty<string>()), 70, CardWidth - 28);

        _snowCard.BodyHeight = st.Height;
    }

    // radios of one group live in their own panel, so two groups in a card don't act as one
    static (Panel, RadioButton[]) RadioGroup(Stack st, string[] texts)
    {
        var panel = st.Group(texts.Length * 24 + 2);
        var radios = new RadioButton[texts.Length];
        for (var i = 0; i < texts.Length; i++)
        {
            radios[i] = Widgets.Radio(texts[i]);
            radios[i].Location = Dpi.P(0, i * 24);
            panel.Controls.Add(radios[i]);
        }
        return (panel, radios);
    }

    // ---- settings <-> controls ----

    void Populate()
    {
        _loading = true;
        _game.Text = _s.GameLocation;
        _gameType.SelectedIndex = _s.GameType == GameType.SkyrimLE ? 1 : 0;
        _manager.SelectedIndex = _s.ModManager == ModManagerType.ModOrganizer2 ? 1 : 0;
        _instance.Text = _s.Mo2InstancePath;
        _output.Text = _s.OutputLocation;
        RefreshProfiles(_s.Mo2ProfileName);

        Widgets.SetLines(_addList, _s.MeshAddList);
        _landscapeColors[_s.LandscapeVertexColorMode switch { LandscapeVertexColorMode.All => 1, LandscapeVertexColorMode.SnowOnly => 2, _ => 0 }].Checked = true;
        _meshColors[_s.MeshVertexColorMode switch { MeshVertexColorMode.SnowOnly => 1, MeshVertexColorMode.All => 2, _ => 0 }].Checked = true;

        _blendCard.Toggle!.Checked = _s.EnableBlend;
        _pbr.Checked = _s.GeneratePbrSlots;
        _allowlist = _s.AutoGenerateAllowlist.ToList();
        Widgets.SetLines(_blendBlacklist, _s.BlendMeshBlacklist);
        Widgets.SetLines(_blendKeywords, _s.BlendEditorIdBlacklistKeywords);

        _snowCard.Toggle!.Checked = _s.EnableSnow;
        _collision[_s.CollisionMaterialMode == CollisionMaterialMode.SnowOnly ? 1 : 0].Checked = true;
        _hideDecals.Checked = _s.HideDecalShapes;
        _removeIce.Checked = _s.RemoveIceSnowMaterial;
        Widgets.SetLines(_snowBlacklist, _s.MeshBlacklist);
        Widgets.SetLines(_snowKeywords, _s.EditorIdBlacklistKeywords);

        _loading = false;
        UpdateGameTypeState();
        UpdateMo2State();
    }

    void Collect()
    {
        _s.GameLocation = _game.Text.Trim();
        _s.GameType = _gameType.SelectedIndex == 1 ? GameType.SkyrimLE : GameType.SkyrimSE;
        _s.OutputLocation = _output.Text.Trim();
        _s.ModManager = _manager.SelectedIndex == 1 ? ModManagerType.ModOrganizer2 : ModManagerType.None;
        _s.Mo2InstancePath = _instance.Text.Trim();
        _s.Mo2ProfileName = _profile.SelectedItem ?? "";

        _s.MeshAddList = Widgets.ReadLines(_addList);
        _s.LandscapeVertexColorMode = _landscapeColors[1].Checked ? LandscapeVertexColorMode.All : _landscapeColors[2].Checked ? LandscapeVertexColorMode.SnowOnly : LandscapeVertexColorMode.None;
        _s.MeshVertexColorMode = _meshColors[2].Checked ? MeshVertexColorMode.All : _meshColors[1].Checked ? MeshVertexColorMode.SnowOnly : MeshVertexColorMode.None;

        _s.EnableBlend = _blendCard.Toggle!.Checked;
        _s.GeneratePbrSlots = _pbr.Checked && _pbr.Enabled;
        _s.AutoGenerateAllowlist = _allowlist.ToList();
        _s.BlendMeshBlacklist = Widgets.ReadLines(_blendBlacklist);
        _s.BlendEditorIdBlacklistKeywords = Widgets.ReadLines(_blendKeywords);

        _s.EnableSnow = _snowCard.Toggle!.Checked;
        _s.CollisionMaterialMode = _collision[1].Checked ? CollisionMaterialMode.SnowOnly : CollisionMaterialMode.None;
        _s.HideDecalShapes = _hideDecals.Checked;
        _s.RemoveIceSnowMaterial = _removeIce.Checked;
        _s.MeshBlacklist = Widgets.ReadLines(_snowBlacklist);
        _s.EditorIdBlacklistKeywords = Widgets.ReadLines(_snowKeywords);
        _s.UiLanguage = Loc.Code;
    }

    // PBR slots rely on Community Shaders' PBR, which only exists on Skyrim SE: on Legendary Edition the option
    // is off and disabled, with the reason written under it.
    void UpdateGameTypeState()
    {
        var le = _gameType.SelectedIndex == 1;
        if (le) _pbr.Checked = false;
        _pbr.Enabled = !le;
        _pbrNote.Visible = le;
        // the note sits right under the box; the rest of the card follows it
        _pbrNote.Top = _pbr.Bottom + Dpi.P(1);
    }

    void UpdateMo2State()
    {
        var mo2 = _manager.SelectedIndex == 1;
        foreach (Control c in new Control[] { _instanceCaption, _instance, _instanceBrowse, _profileCaption, _profile }) c.Enabled = mo2;
    }

    void RefreshProfiles(string? prefer = null)
    {
        var wanted = prefer ?? _profile.SelectedItem;
        _profile.SetItems(Array.Empty<string>());
        var path = _instance.Text.Trim();
        if (path == "") return;
        try
        {
            var discovery = Mo2InstanceReader.DiscoverProfiles(path);
            _profile.SetItems(discovery.Profiles);
            var index = wanted != null ? _profile.Items.IndexOf(wanted) : -1;
            if (index < 0 && discovery.SelectedProfile != null) index = _profile.Items.IndexOf(discovery.SelectedProfile);
            _profile.SelectedIndex = index >= 0 ? index : _profile.Items.Count > 0 ? 0 : -1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            // an invalid path leaves the picker empty; Generate reports the actionable error
        }
    }

    void LoadConfig()
    {
        using var dialog = new OpenFileDialog { Title = T("launcher.configProfile.loadDialogTitle", "Load Config"), Filter = T("launcher.configProfile.fileFilter", "JSON files (*.json)|*.json|All files (*.*)|*.*") };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var loaded = SettingsStore.ReadFile(dialog.FileName);
        if (loaded == null)
        {
            Say("That file couldn't be read as a Landscape Fixer config.", true);
            return;
        }
        _s = loaded;
        Populate();
    }

    void SaveConfig()
    {
        using var dialog = new SaveFileDialog { Title = T("launcher.configProfile.saveDialogTitle", "Save Config As"), FileName = "LandscapeFixer_config.json", Filter = T("launcher.configProfile.fileFilter", "JSON files (*.json)|*.json|All files (*.*)|*.*") };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Collect();
        if (!SettingsStore.WriteFile(dialog.FileName, _s)) Say("That config couldn't be saved.", true);
    }

    void OpenPreferences()
    {
        if (_busy) return;
        using var dialog = new PreferencesForm();
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedLanguage == Loc.Code) return;
        Collect();
        _s.UiLanguage = dialog.SelectedLanguage;
        _store.Save(_s);
        Loc.Load(dialog.SelectedLanguage);
        RelaunchRequested = true;
        Close();
    }

    // ---- the run ----

    void Say(string text, bool warn)
    {
        _status.ForeColor = warn ? Theme.Orange : Theme.Dim;
        _status.Text = text;
    }

    async Task RunAsync()
    {
        Collect();
        var problem = Problem();
        if (problem != null)
        {
            Say(problem, true);
            return;
        }

        _store.Save(_s);
        SetBusy(true);
        _log.Clear();
        _lastLogged = "";
        _progress.Reset();
        _stats.Show(Array.Empty<StatsPanel.Tile>(), T("run.running", "Running..."));
        Say(T("progress.starting", "Starting..."), false);
        var clock = Stopwatch.StartNew();

        ExtractResult? result = null;
        Exception? error = null;
        await Task.Run(() =>
        {
            try
            {
                result = new ExtractOrchestrator(_s).Run(p => Post(() => OnProgress(p)));
            }
            catch (Exception e)
            {
                error = e;
            }
        });

        SetBusy(false);
        if (error != null)
        {
            _progress.Reset();
            Say(T("run.error", "Stopped on an error, see the messages on the right."), true);
            Log(error.Message);
            Log("");
            Log(error.ToString());
            _stats.Show(Array.Empty<StatsPanel.Tile>(), T("run.errorHint", "The run stopped on an error."));
            return;
        }

        _progress.Done();
        Say(string.Format(T("run.done", "Done in {0} s. Enable LandscapeFixer.esp in your plugin list if it isn't already."), (int)clock.Elapsed.TotalSeconds), false);
        _status.ForeColor = Theme.Accent;
        ShowResult(result!);
    }

    string? Problem()
    {
        if (_s.GameLocation == "") return T("launcher.missingGameLocation.message", "Please select your game's install location.");
        if (!File.Exists(Path.Combine(_s.GameLocation, "Data", "Skyrim.esm")))
            return "Game Location must be your Skyrim install folder: no Data\\Skyrim.esm in it.";
        if (_s.OutputLocation == "") return T("launcher.missingOutputLocation.message", "Please select an output location.");
        if (_s.ModManager == ModManagerType.ModOrganizer2 && _s.Mo2InstancePath == "") return T("launcher.missingMo2Instance.message", "Please select your Mod Organizer 2 instance folder.");
        if (_s.ModManager == ModManagerType.ModOrganizer2 && _s.Mo2ProfileName == "")
            return T("launcher.missingMo2Profile.message", "No MO2 profile is selected. Make sure the MO2 Instance Path points to your MO2 instance (the folder containing ModOrganizer.ini, or the folder that holds your mods and profiles), then pick a profile.");
        if (!_s.EnableBlend && !_s.EnableSnow && _s.LandscapeVertexColorMode == LandscapeVertexColorMode.None && _s.MeshVertexColorMode == MeshVertexColorMode.None)
            return T("run.nothingToDo", "Nothing is switched on: tick Blend or Snow, or choose a vertex color option.");
        return null;
    }

    void OnProgress(ExtractProgress p)
    {
        if (p.IsIndeterminate) _progress.Indeterminate();
        else _progress.Value = p.Current / (float)p.Total;
        _status.ForeColor = Theme.Dim;
        _status.Text = p.Message;

        // log a line when the kind of message changes, not for every counter tick
        var kind = Regex.Replace(p.Message, @"\d+", "#");
        if (kind != _lastLogged)
        {
            _lastLogged = kind;
            Log(p.Message);
        }
    }

    void ShowResult(ExtractResult r)
    {
        static string N(int value) => value.ToString("N0");
        _stats.Show(new[]
        {
            new StatsPanel.Tile(T("run.tile.blend", "Blend meshes"), N(r.BlendMeshesPatched)),
            new StatsPanel.Tile(T("run.tile.textureSets", "TextureSets"), N(r.BlendTextureSetsCreated)),
            new StatsPanel.Tile(T("run.tile.snowRecords", "Snow records"), N(r.RecordsMatched)),
            new StatsPanel.Tile(T("run.tile.snowMeshes", "Snow meshes"), N(r.MeshesDuplicated)),
            new StatsPanel.Tile(T("run.tile.terrain", "Terrain cleaned"), N(r.LandscapesPatched)),
            new StatsPanel.Tile(T("run.tile.meshColors", "Meshes cleaned"), N(r.VertexColorsNeutralized)),
            new StatsPanel.Tile(T("run.tile.shaders", "Shader fixes"), N(r.ShaderFlagsPatched)),
            new StatsPanel.Tile(T("run.tile.failed", "Meshes failed"), N(r.MeshesFailed), r.MeshesFailed > 0),
        });

        Log("");
        Log(T("progress.done", "Done."));
        if (r.OutputEspPath != null) Log(r.OutputEspPath);
        if (r.Diagnostics.Count > 0)
        {
            Log("");
            Log($"{r.Diagnostics.Count} note(s):");
            foreach (var line in r.Diagnostics.Take(25)) Log(" - " + line);
            if (r.Diagnostics.Count > 25) Log($" ... and {r.Diagnostics.Count - 25} more (see LandscapeFixer-log.txt in the output folder).");
        }
    }

    void Log(string text) => _log.AppendText(text + "\r\n");

    void SetBusy(bool busy)
    {
        _busy = busy;
        _cards.Enabled = !busy;
        _generate.Enabled = !busy;
        _generate.Text = busy ? T("run.generating", "Generating...") : T("run.generate", "Generate");
        _open.Enabled = !busy && Directory.Exists(_output.Text);
    }

    void OpenOutput()
    {
        if (Directory.Exists(_output.Text)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_output.Text}\"") { UseShellExecute = true });
    }

    void Post(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { }
    }

    void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_busy)
        {
            var answer = MessageBox.Show(this, T("run.quitWhileRunning", "A run is in progress. Quit anyway? The output folder may be left incomplete."), "Landscape Fixer", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            Environment.Exit(0);
        }

        if (!RelaunchRequested)
        {
            Collect();
            _store.Save(_s);
        }
    }

    // A thin progress line in the accent colour; a sliding segment while the length of the work isn't known.
    sealed class ProgressLine : Control
    {
        readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
        float _value, _slide;
        bool _indeterminate;

        public ProgressLine()
        {
            DoubleBuffered = true;
            _timer.Tick += (_, _) => { _slide = (_slide + 0.02f) % 1.4f; Invalidate(); };
        }

        public float Value
        {
            set { _indeterminate = false; _timer.Stop(); _value = Math.Clamp(value, 0f, 1f); Invalidate(); }
        }

        public void Indeterminate()
        {
            if (_indeterminate) return;
            _indeterminate = true;
            _timer.Start();
        }

        public void Reset() { _indeterminate = false; _timer.Stop(); _value = 0; Invalidate(); }
        public void Done() { _indeterminate = false; _timer.Stop(); _value = 1; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Field);
            using var brush = new SolidBrush(Theme.Accent);
            if (_indeterminate)
            {
                var width = Width * 0.3f;
                e.Graphics.FillRectangle(brush, (_slide - 0.3f) * Width, 0, width, Height);
            }
            else e.Graphics.FillRectangle(brush, 0, 0, Width * _value, Height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
