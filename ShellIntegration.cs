using Microsoft.Win32;

namespace TaskbarFolders;

/// <summary>
/// Optional per-user shell integration: adds "Add to Taskbar Folder" to the right-click menu of
/// shortcuts, programs, folders and files, so apps can be filed straight from Start menu or Explorer.
/// Everything is written under HKCU\Software\Classes and can be removed again from Settings.
/// </summary>
static class ShellIntegration
{
    const string MenuKey = "TaskbarFolders.Add";
    static readonly string[] Classes = { "lnkfile", "exefile", "*", "Directory" };

    public static bool IsEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\lnkfile\shell\{MenuKey}");
            return k != null;
        }
        catch { return false; }
    }

    public static void Sync(bool enable)
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;

            foreach (var cls in Classes)
            {
                var basePath = $@"Software\Classes\{cls}\shell\{MenuKey}";
                if (!enable)
                {
                    try { Registry.CurrentUser.DeleteSubKeyTree(basePath, false); } catch { }
                    continue;
                }

                using var key = Registry.CurrentUser.CreateSubKey(basePath);
                if (key == null) continue;
                key.SetValue(null, "Add to Taskbar Folder");
                key.SetValue("Icon", $"\"{exe}\",0");
                using var cmd = key.CreateSubKey("command");
                cmd?.SetValue(null, $"\"{exe}\" --add \"%1\"");
            }
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }
}
