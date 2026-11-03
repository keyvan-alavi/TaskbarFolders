using System.Text.Json;
using Microsoft.Win32;

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

    /// <summary>"auto" (grid of the apps' icons), "emoji" (preset or typed glyph) or "file" (png / ico / jpg).</summary>
    public string IconKind { get; set; } = "auto";

    /// <summary>The emoji/text for "emoji", or the image path for "file".</summary>
    public string IconValue { get; set; } = "";
}

public class Config
{
    public List<Folder> Folders { get; set; } = new();

    public static string Dir => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarFolders");
    public static string IconsDir => System.IO.Path.Combine(Dir, "icons");
    public static string CustomDir => System.IO.Path.Combine(Dir, "custom");
    public static string ShortcutsDir => System.IO.Path.Combine(Dir, "Shortcuts");
    static string FilePath => System.IO.Path.Combine(Dir, "config.json");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static Config Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath), Opts) ?? new();
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
    }
}

/// <summary>Writes unexpected errors to %AppData%\TaskbarFolders\error.log so they can be diagnosed.</summary>
static class ErrorLog
{
    public static string FilePath => System.IO.Path.Combine(Config.Dir, "error.log");

    public static void Write(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Config.Dir);
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }
}

public static class Theme
{
    // Taskbar/system theme (Windows 11: Settings > Personalization > Colors)
    public static bool IsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("SystemUsesLightTheme") is int v && v == 0;
        }
        catch { return true; }
    }

    public static void Apply(Control c, bool dark)
    {
        var bg = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
        var fg = dark ? Color.White : Color.FromArgb(28, 28, 28);
        var panel = dark ? Color.FromArgb(43, 43, 43) : Color.White;

        c.ForeColor = fg;
        c.BackColor = (c is ListBox || c is ListView || c is TextBox || c is ComboBox) ? panel : bg;

        if (c is ComboBox cb) cb.FlatStyle = FlatStyle.Flat;

        if (c is Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(200, 200, 200);
            b.BackColor = panel;
            b.UseVisualStyleBackColor = false;
        }
        foreach (Control ch in c.Controls) Apply(ch, dark);
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Taskbar shortcut: TaskbarFolders.exe --open <folderId>
        bool flyoutMode = args.Length >= 2 && args[0] == "--open";

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

        var cfg = Config.Load();

        if (flyoutMode)
        {
            var f = cfg.Folders.FirstOrDefault(x => x.Id == args[1]);
            if (f != null)
            {
                // Clicking the pinned icon while the flyout is open = toggle (close it)
                if (Flyout.RecentlyClosed(f.Id)) return;
                Application.Run(new Flyout(f));
                return;
            }
        }

        Application.Run(new Manager(cfg));
    }
}
