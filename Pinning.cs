using System.Diagnostics;

namespace TaskbarFolders;

/// <summary>Shortcut creation and taskbar-pin helpers.</summary>
static class Pinning
{
    /// <summary>Where Windows keeps copies of the shortcuts pinned to the taskbar.</summary>
    public static string PinnedDir => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    public static string LnkPath(Folder f)
    {
        var bad = System.IO.Path.GetInvalidFileNameChars();
        var name = string.Concat(f.Name.Select(c => bad.Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        if (name.Length == 0) name = f.Id;
        return System.IO.Path.Combine(Config.ShortcutsDir, name + ".lnk");
    }

    /// <summary>(Re)builds the folder icon and the .lnk that points at "TaskbarFolders.exe --open id".</summary>
    public static string EnsureShortcut(Folder f)
    {
        IconBuilder.BuildIcon(f);
        Directory.CreateDirectory(Config.ShortcutsDir);
        var lnk = LnkPath(f);
        ShortcutWriter.Create(lnk, Environment.ProcessPath!, $"--open {f.Id}",
            IconBuilder.IcoPath(f), $"{f.Name} (Taskbar Folder)", "TaskbarFolders." + f.Id);
        return lnk;
    }

    /// <summary>True when Windows holds a pinned copy of this folder's shortcut (matched by file name).</summary>
    public static bool IsPinned(Folder f)
    {
        try
        {
            var name = System.IO.Path.GetFileName(LnkPath(f));
            if (File.Exists(System.IO.Path.Combine(PinnedDir, name))) return true;

            // Windows sometimes renames the pinned copy to "<name> - Shortcut.lnk".
            var alt = System.IO.Path.GetFileNameWithoutExtension(name) + " - Shortcut.lnk";
            return File.Exists(System.IO.Path.Combine(PinnedDir, alt));
        }
        catch { return false; }
    }

    /// <summary>
    /// Best effort: asks the shell to run its "taskbarpin" verb. Recent Windows 11 builds usually
    /// refuse this for third-party apps, so callers must check <see cref="IsPinned"/> afterwards.
    /// </summary>
    public static bool TryVerbPin(string lnk)
    {
        try
        {
            var psi = new ProcessStartInfo(lnk)
            {
                UseShellExecute = true,
                Verb = "taskbarpin",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            return p != null;
        }
        catch { return false; }
    }

    public static void ShowInExplorer(string lnk)
    {
        try { Process.Start("explorer.exe", $"/select,\"{lnk}\""); }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    /// <summary>Removes the stale .lnk copy from the pinned folder (only if Windows no longer pins it).</summary>
    public static void CleanPinnedCopy(Folder f)
    {
        try
        {
            if (IsPinned(f)) return;
            File.Delete(System.IO.Path.Combine(PinnedDir, System.IO.Path.GetFileName(LnkPath(f))));
        }
        catch { }
    }
}
