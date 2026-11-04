using System.Diagnostics;
using System.Text.Json;

namespace TaskbarFolders;

public class Item
{
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string Args { get; set; } = "";
}

public class Folder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "New folder";
    public List<Item> Items { get; set; } = new();

    /// <summary>Menu layout: 1 = rows with names, 2..4 = icon grid.</summary>
    public int Columns { get; set; } = 1;

    /// <summary>Grid layouts only: show the app names under the icons.</summary>
    public bool ShowNames { get; set; } = true;

    /// <summary>"auto" (theme colour), "emoji" (preset or typed glyph) or "file" (png / ico / jpg).</summary>
    public string IconKind { get; set; } = "auto";

    /// <summary>The emoji/text for "emoji", or the image path for "file".</summary>
    public string IconValue { get; set; } = "";

    /// <summary>Custom accent colour as #RRGGBB. Empty = automatic colour.</summary>
    public string Color { get; set; } = "";

    /// <summary>Per-folder open animation. Empty = use the global setting.</summary>
    public string Animation { get; set; } = "";

    /// <summary>Runtime-only: last known pin state (kept out of config.json).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool PinnedCache { get; set; }
}

public class AppSettings
{
    /// <summary>system | light | dark | aurora | sunset | forest | midnight</summary>
    public string Theme { get; set; } = "system";

    /// <summary>slide | fade | zoom | slidefade | none</summary>
    public string Animation { get; set; } = "slidefade";

    /// <summary>Animation duration in milliseconds.</summary>
    public int AnimationMs { get; set; } = 200;

    /// <summary>Add "Add to Taskbar Folder" to Explorer / Start-menu file context menus.</summary>
    public bool ShellMenu { get; set; } = true;

    public string LastFolderId { get; set; } = "";

    /// <summary>Show a one-time hint about dragging folders onto the taskbar.</summary>
    public bool ShowPinHint { get; set; } = true;
}

public class Config
{
    public List<Folder> Folders { get; set; } = new();
    public AppSettings Settings { get; set; } = new();

    public static string Dir
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("TBF_DATA");
            return string.IsNullOrWhiteSpace(custom)
                ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarFolders")
                : custom;
        }
    }

    public static string IconsDir => System.IO.Path.Combine(Dir, "icons");
    public static string CustomDir => System.IO.Path.Combine(Dir, "custom");
    public static string ShortcutsDir => System.IO.Path.Combine(Dir, "Shortcuts");
    public static string FilePath => System.IO.Path.Combine(Dir, "config.json");

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static Config Load()
    {
        var cfg = new Config();
        try
        {
            if (File.Exists(FilePath))
                cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath), Opts) ?? new Config();
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
        cfg.Normalize();
        return cfg;
    }

    /// <summary>Repairs configs written by older versions and guarantees unique folder ids.</summary>
    public void Normalize()
    {
        Folders ??= new List<Folder>();
        Settings ??= new AppSettings();
        Settings.Theme = string.IsNullOrWhiteSpace(Settings.Theme) ? "system" : Settings.Theme;
        Settings.Animation = string.IsNullOrWhiteSpace(Settings.Animation) ? "slidefade" : Settings.Animation;
        Settings.AnimationMs = Math.Clamp(Settings.AnimationMs, 80, 600);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Folders)
        {
            f.Items ??= new List<Item>();
            f.Name = string.IsNullOrWhiteSpace(f.Name) ? "Folder" : f.Name;
            f.Color ??= "";
            f.Animation ??= "";
            f.IconKind ??= "auto";
            f.IconValue ??= "";
            if (string.IsNullOrWhiteSpace(f.Id) || !seen.Add(f.Id))
            {
                f.Id = Guid.NewGuid().ToString("N")[..8];
                seen.Add(f.Id);
            }
            foreach (var it in f.Items)
            {
                it.Name ??= "";
                it.Target ??= "";
                it.Args ??= "";
            }
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opts));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    public Folder? Find(string? id) => Folders.FirstOrDefault(f => f.Id == id);
}

/// <summary>Writes unexpected errors to %AppData%\TaskbarFolders\error.log so they can be diagnosed.</summary>
static class ErrorLog
{
    public static string FilePath => System.IO.Path.Combine(Config.Dir, "error.log");

    public static void Write(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            Directory.CreateDirectory(Config.Dir);
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }
}

static class Program
{
    public static string Version
    {
        get
        {
            var v = typeof(Program).Assembly.GetName().Version;
            return v == null ? "1.3" : $"{v.Major}.{v.Minor}" + (v.Build > 0 ? $".{v.Build}" : "");
        }
    }

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var cfg = Config.Load();
        ThemeManager.Init(cfg);

        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        bool flyoutMode = mode is "--open" or "--flyout" && args.Length >= 2;

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            ErrorLog.Write(e.Exception);
            if (flyoutMode) Application.Exit(); // never leave a broken menu on screen
            else
                MessageBox.Show(
                    "Something went wrong.\n\n" + e.Exception.Message +
                    "\n\nDetails were saved to:\n" + ErrorLog.FilePath,
                    "Taskbar Folders", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ErrorLog.Write(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));

        try
        {
            switch (mode)
            {
                case "--open":
                case "--flyout":
                {
                    var f = cfg.Find(args[1]);
                    if (f == null) return;
                    if (Flyout.RecentlyClosed(f.Id)) return;
                    Application.Run(new Flyout(cfg, f));
                    return;
                }

                case "--add":
                {
                    var paths = args.Skip(1).Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
                    if (paths.Length == 0) return;
                    Application.Run(new AddToFolderForm(cfg, paths));
                    return;
                }

                case "--settings":
                    Application.Run(new Manager(cfg, openSettings: true));
                    return;


                default:
                    Application.Run(new Manager(cfg));
                    return;
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            MessageBox.Show("Taskbar Folders could not start.\n\n" + ex.Message +
                            "\n\nDetails: " + ErrorLog.FilePath,
                "Taskbar Folders", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

}
