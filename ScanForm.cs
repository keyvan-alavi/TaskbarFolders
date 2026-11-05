using System.Drawing.Drawing2D;

namespace TaskbarFolders;

/// <summary>One Start-menu app found by the scanner.</summary>
public sealed class ScannedApp
{
    public string Name = "";
    public string Target = "";
    public string Category = "Other";
    public Bitmap? Icon;
    public bool AlreadyInFolder;
    public override string ToString() => Name;
}

/// <summary>Heuristic Start-menu app classifier used by the scanner.</summary>
public static class AppClassifier
{
    public static readonly string[] Categories =
    {
        "AI", "Development", "Browsers", "Media", "Office", "Games",
        "System Tools", "Network & VPN", "Hardware", "Utilities", "Other"
    };

    static readonly (string Cat, string[] Words)[] Rules =
    {
        ("AI", new[]
        {
            "chatgpt", "gemini", "copilot", "claude", "deepseek", "grok", "perplexity", "qwen",
            "kimi", "opencode", "autoclaw", "cursor", "midjourney", "ollama", "lm studio",
            "stable diffusion", "notebooklm", "poe ", "character.ai", "hugging"
        }),
        ("Development", new[]
        {
            "visual studio", "vs code", "vscode", "pycharm", "webstorm", "intellij", "android studio",
            "jetbrains", "rider", "goland", "phpstorm", "clion", "eclipse", "netbeans", "godot",
            "unity", "unreal", "git", "github", "docker", "postman", "insomnia", "powershell",
            "terminal", "command prompt", "python", "node.js", "npm", "xampp", "wamp", "mysql",
            "sqlite", "dbeaver", "arduino", "platformio", "dev c++", "code::blocks", "sublime",
            "notepad++", "vim", "neovim", "jupyter", "anaconda", "conda", "visual studio installer",
            "java", "adb", "scrcpy", "apk", "inno setup", "virtualbox", "android"
        }),
        ("Browsers", new[]
        {
            "chrome", "firefox", "edge", "brave", "opera", "vivaldi", "safari", "browser"
        }),
        ("Media", new[]
        {
            "vlc", "spotify", "media player", "photos", "paint", "premiere", "after effects",
            "obs", "audacity", "capcut", "filmora", "itunes", "music", "video", "camera",
            "recorder", "netflix", "blender", "canva", "gimp", "photoshop", "illustrator",
            "lightroom", "figma", "shotcut", "handbrake", "kdenlive", "player", "codec",
            "madvr", "lav audio", "lav splitter", "x264", "xvid", "vfw", "graphstudio",
            "mediainfo", "cad"
        }),
        ("Office", new[]
        {
            "word", "excel", "powerpoint", "outlook", "onenote", "teams", "zoom", "notion",
            "obsidian", "evernote", "acrobat", "adobe reader", "libreoffice", "wps", "slack",
            "discord", "telegram", "whatsapp", "clickup", "trello", "todoist", "calendar",
            "office", "pdf"
        }),
        ("Games", new[]
        {
            "steam", "epic games", "riot", "minecraft", "game", "battle.net", "ubisoft",
            "xbox", "playnite", "roblox", "gog galaxy", "ea app", "origin",
            "chess", "freecell", "hearts", "mahjong", "minesweeper", "purble", "solitaire"
        }),
        ("Network & VPN", new[]
        {
            "anydesk", "vnc", "teamviewer", "hiddify", "lantern", "psiphon", "v2ray",
            "vpn", "openvpn", "wireguard", "wintun", "tap-windows", "dco-win",
            "virtual network adapter", "network adapter", "ssh", "putty"
        }),
        ("Hardware", new[]
        {
            "driver", "nvidia", "amd", "realtek", "core temp", "crystaldiskinfo",
            "keyboard", "mouse", "audio", "display", "monitor", "printer", "scanner",
            "canon", "epson", "hp "
        }),
        ("System Tools", new[]
        {
            "administrative tools", "component services", "computer management", "event viewer",
            "performance monitor", "resource monitor", "task scheduler", "system configuration",
            "system information", "registry editor", "group policy", "odbc", "iscsi",
            "print management", "recoverydrive", "memory diagnostics", "character map", "magnify",
            "on-screen keyboard", "narrator", "remote desktop", "livecaptions", "voiceaccess",
            "firewall", "spooler", "update service", "security configuration", "services",
            "task manager", "file explorer", "control panel", "settings", "defender"
        }),
        ("Utilities", new[]
        {
            "7-zip", "winrar", "zip", "calculator", "notepad", "snipping", "cleaner",
            "ccleaner", "backup", "utility", "disk cleanup", "disk management", "partition",
            "virtual clone", "winaero", "download manager", "internet download", "idm"
        }),
    };

    static readonly string[] Noise =
    {
        "readme", "release notes", "revision history", "documentation", "manual", "license",
        "tutorial", "faq", "what is new", "example script", "about ", "check for update",
        "visit ", "telemetry", "user guide", "help"
    };

    /// <summary>Menu entries that are docs/scripts rather than apps.</summary>
    public static bool IsNoise(string name)
    {
        var n = name.ToLowerInvariant();
        foreach (var word in Noise)
            if (n.Contains(word)) return true;
        return false;
    }

    public static string Classify(StartApp app)
    {
        var name = " " + app.Name.ToLowerInvariant() + " ";
        foreach (var (cat, words) in Rules)
            foreach (var w in words)
                if (name.Contains(w)) return cat;
        return "Other";
    }

    public static Color CategoryColor(string cat) => cat switch
    {
        "AI" => Color.FromArgb(150, 104, 255),
        "Development" => Color.FromArgb(64, 132, 255),
        "Browsers" => Color.FromArgb(38, 199, 168),
        "Media" => Color.FromArgb(232, 93, 117),
        "Office" => Color.FromArgb(242, 184, 75),
        "Games" => Color.FromArgb(255, 138, 76),
        "System Tools" => Color.FromArgb(124, 135, 152),
        "Network & VPN" => Color.FromArgb(56, 189, 248),
        "Hardware" => Color.FromArgb(245, 158, 11),
        "Utilities" => Color.FromArgb(148, 163, 184),
        _ => Color.FromArgb(150, 150, 150)
    };

    /// <summary>Folder name that should receive a category (reuses existing "AI"/"Dev tools"/"browser" folders).</summary>
    public static string? FolderHint(string cat) => cat switch
    {
        "AI" => "ai",
        "Development" => "dev",
        "Browsers" => "browser",
        "Media" => "media",
        "Office" => "office",
        "Games" => "game",
        "System Tools" => "system",
        "Network & VPN" => "network",
        "Hardware" => "hardware",
        "Utilities" => "utilit",
        _ => null
    };
}

/// <summary>
/// Scans the Start menu, shows every app with an automatic category, and lets the user add selected
/// apps to a folder or auto-create one folder per category in a single step.
/// </summary>
sealed class ScanForm : Form
{
    readonly Config cfg;
    readonly string? preferredFolderId;
    readonly List<ScannedApp> all = new();
    readonly List<ScannedApp> shown = new();
    readonly HashSet<ScannedApp> checkedApps = new();
    readonly ListBox list;
    readonly TextBox search;
    readonly OptionButton categoryFilter;
    readonly OptionButton targetFolder;
    readonly CheckBox showAdded;
    readonly Label status;
    readonly Label counts;
    readonly List<Folder> targetChoices = new();
    readonly Font rowFont = new("Segoe UI", 9.5f);
    readonly Font pillFont = new("Segoe UI Semibold", 8f);
    readonly System.Windows.Forms.Timer debounce = new() { Interval = 180 };

    bool building;
    bool scanned;
    public bool Changed { get; private set; }

    int S(int v) => (int)Math.Round(v * DeviceDpi / 96f);

    public ScanForm(Config config, string? selectedFolderId = null)
    {
        cfg = config;
        preferredFolderId = selectedFolderId;
        var p = ThemeManager.P;

        Text = "Scan this PC";
        ClientSize = new Size(900, 690);
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = p.Bg;
        ForeColor = p.Text;
        DoubleBuffered = true;

        var header = new Panel { Dock = DockStyle.Top, Height = 88, BackColor = p.Bg };
        var title = new Label
        {
            Text = "Scan this PC", Left = 24, Top = 16, AutoSize = true,
            Font = new Font("Segoe UI Semibold", 17f)
        };
        var sub = new Label
        {
            Text = "Every app in your Start menu, grouped automatically. Pick the ones you want, then add them to a folder or let the scanner organise everything.",
            Left = 27, Top = 52, Width = 830, Height = 26, ForeColor = p.SubText,
            Font = new Font("Segoe UI", 8.75f), Tag = "sub"
        };
        header.Controls.AddRange(new Control[] { title, sub });

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = p.Bg };
        search = new TextBox { Left = 24, Top = 10, Width = 280 };
        search.PlaceholderText = "Search apps…";
        categoryFilter = new OptionButton
        {
            Left = 316, Top = 8, Width = 190,
            Options = new[] { "All categories" }.Concat(AppClassifier.Categories).ToArray(),
            SelectedIndex = 0
        };
        showAdded = new CheckBox
        {
            Text = "Show apps already in a folder", Left = 518, Top = 12, AutoSize = true, ForeColor = p.Text
        };
        var selectAll = new FlatButton { Text = "Select all", Left = 726, Top = 8, Width = 90, Height = 32 };
        var clear = new FlatButton { Text = "Clear", Left = 822, Top = 8, Width = 60, Height = 32 };
        toolbar.Controls.AddRange(new Control[] { search, categoryFilter, showAdded, selectAll, clear });

        list = new ListBox
        {
            Dock = DockStyle.Fill,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 40,
            SelectionMode = SelectionMode.MultiSimple,
            BorderStyle = BorderStyle.None,
            BackColor = p.Panel,
            ForeColor = p.Text,
            Font = rowFont,
            IntegralHeight = false,
            HorizontalScrollbar = false
        };
        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 0, 20, 0), BackColor = p.Bg };
        listHost.Controls.Add(list);

        status = new Label
        {
            Dock = DockStyle.Bottom, Height = 26, ForeColor = p.SubText,
            Padding = new Padding(24, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft, Tag = "sub"
        };

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 66, BackColor = p.Bg };
        counts = new Label
        {
            Left = 24, Top = 22, Width = 230, AutoSize = false, Height = 22,
            ForeColor = p.SubText, Tag = "sub"
        };
        var targetLabel = new Label
        {
            Text = "Add to:", Left = 266, Top = 24, AutoSize = true, ForeColor = p.SubText, Font = new Font("Segoe UI", 8.5f), Tag = "sub"
        };
        targetFolder = new OptionButton { Left = 322, Top = 16, Width = 210, Height = 34 };
        var add = new FlatButton { Text = "Add checked", Left = 542, Top = 16, Width = 118, Height = 34, Primary = true };
        var auto = new FlatButton { Text = "Auto-organise", Left = 668, Top = 16, Width = 132, Height = 34 };
        var close = new FlatButton { Text = "Close", Left = 808, Top = 16, Width = 72, Height = 34 };
        footer.Controls.AddRange(new Control[] { counts, targetLabel, targetFolder, add, auto, close });

        Controls.Add(listHost);
        Controls.Add(status);
        Controls.Add(footer);
        Controls.Add(toolbar);
        Controls.Add(header);

        ThemeManager.Apply(this);
        title.ForeColor = p.Text;
        sub.ForeColor = p.SubText;
        status.ForeColor = p.SubText;
        counts.ForeColor = p.SubText;
        search.TextChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
        categoryFilter.SelectionChanged += (_, _) => Fill();
        showAdded.CheckedChanged += (_, _) => Fill();
        selectAll.Click += (_, _) => SetAll(true);
        clear.Click += (_, _) => SetAll(false);
        add.Click += (_, _) => AddChecked();
        auto.Click += (_, _) => AutoOrganize();
        close.Click += (_, _) => Close();
        list.SelectedIndexChanged += ListSelectionChanged;
        list.DrawItem += DrawRow;
        list.Resize += (_, _) => list.ItemHeight = S(40);
        debounce.Tick += (_, _) => { debounce.Stop(); Fill(); };
        Shown += (_, _) =>
        {
            // Some CheckedListBox builds reset DrawMode while creating their handle.
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = S(40);
            list.Invalidate();
            BeginInvoke(new Action(Scan));
        };
        FormClosed += (_, _) =>
        {
            debounce.Dispose();
            foreach (var a in all) a.Icon?.Dispose();
            rowFont.Dispose();
            pillFont.Dispose();
        };
        HandleCreated += (_, _) => Native.SetDarkTitle(Handle, p.Dark);
    }

    // ---------------------------------------------------------------- scanning

    void Scan()
    {
        if (scanned) return;
        scanned = true;
        Cursor = Cursors.WaitCursor;
        status.Text = "Scanning the Start menu…";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var already = new HashSet<string>(
                cfg.Folders.SelectMany(f => f.Items).Select(i => i.Target),
                StringComparer.OrdinalIgnoreCase);
            var alreadyNames = new HashSet<string>(
                cfg.Folders.SelectMany(f => f.Items).Select(i => i.Name),
                StringComparer.OrdinalIgnoreCase);

            var found = StartMenuScanner.Scan();
            foreach (var app in found)
            {
                if (AppClassifier.IsNoise(app.Name)) continue;
                var item = new ScannedApp
                {
                    Name = app.Name,
                    Target = app.Target,
                    Category = AppClassifier.Classify(app),
                    AlreadyInFolder = already.Contains(app.Target) || alreadyNames.Contains(app.Name)
                };
                item.Icon = IconUtil.Get(app.Target);
                all.Add(item);
            }
            all.Sort((a, b) =>
            {
                int ca = Array.IndexOf(AppClassifier.Categories, a.Category);
                int cb = Array.IndexOf(AppClassifier.Categories, b.Category);
                return ca != cb ? ca.CompareTo(cb) : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
        finally { Cursor = Cursors.Default; }

        status.Text = $"Scanned {all.Count} apps in {sw.Elapsed.TotalSeconds:0.0}s — {all.Count(a => a.AlreadyInFolder)} are already in a folder.";
        RefreshTargets();
        Fill();
    }

    void RefreshTargets()
    {
        targetChoices.Clear();
        targetChoices.AddRange(cfg.Folders.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase));
        targetFolder.Options = targetChoices.Count == 0
            ? new[] { "New folder “Scanned apps”" }
            : targetChoices.Select(f => f.Name).ToArray();

        int idx = targetChoices.FindIndex(f => f.Id == preferredFolderId);
        if (idx < 0) idx = Math.Max(0, targetChoices.FindIndex(f => f.Id == cfg.Settings.LastFolderId));
        targetFolder.SelectedIndex = idx;
    }

    // ---------------------------------------------------------------- list

    void Fill()
    {
        if (!scanned) return;
        var q = search.Text.Trim();
        int cat = categoryFilter.SelectedIndex - 1;
        shown.Clear();
        foreach (var a in all)
        {
            if (!showAdded.Checked && a.AlreadyInFolder) continue;
            if (cat >= 0 && !a.Category.Equals(AppClassifier.Categories[cat], StringComparison.OrdinalIgnoreCase)) continue;
            if (q.Length > 0 &&
                !a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) &&
                !a.Category.Contains(q, StringComparison.CurrentCultureIgnoreCase)) continue;
            shown.Add(a);
        }

        building = true;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var a in shown) list.Items.Add(a);
        for (int i = 0; i < shown.Count; i++)
            list.SetSelected(i, checkedApps.Contains(shown[i]));
        list.EndUpdate();
        building = false;

        counts.Text = $"{shown.Count} shown  ·  {checkedApps.Count} selected";
    }

    void ListSelectionChanged(object? sender, EventArgs e)
    {
        if (building) return;
        checkedApps.Clear();
        foreach (int i in list.SelectedIndices)
            if (i >= 0 && i < shown.Count) checkedApps.Add(shown[i]);
        counts.Text = $"{shown.Count} shown  ·  {checkedApps.Count} selected";
        list.Invalidate();
    }

    void SetAll(bool on)
    {
        building = true;
        for (int i = 0; i < shown.Count; i++)
        {
            list.SetSelected(i, on);
            if (on) checkedApps.Add(shown[i]);
            else checkedApps.Remove(shown[i]);
        }
        building = false;
        list.Invalidate();
        counts.Text = $"{shown.Count} shown  ·  {checkedApps.Count} selected";
    }

    void DrawRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= shown.Count) return;
        var p = ThemeManager.P;
        var app = shown[e.Index];
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        bool selected = (e.State & DrawItemState.Selected) != 0;
        bool check = list.GetSelected(e.Index);
        var bg = selected ? p.Sel : p.Panel;
        using (var b = new SolidBrush(bg)) g.FillRectangle(b, e.Bounds);

        int top = e.Bounds.Top + (e.Bounds.Height - S(18)) / 2;
        var box = new Rectangle(e.Bounds.Left + S(8), top, S(18), S(18));
        using (var path = Gfx.Round(box, S(4)))
        using (var fill = new SolidBrush(check ? p.Accent : p.Card))
        using (var pen = new Pen(check ? p.Accent : p.Border, 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
        if (check)
        {
            using var pen = new Pen(p.OnAccent, Math.Max(1.6f, S(2)))
            {
                StartCap = LineCap.Round, EndCap = LineCap.Round
            };
            g.DrawLines(pen, new[]
            {
                new PointF(box.X + box.Width * 0.25f, box.Y + box.Height * 0.52f),
                new PointF(box.X + box.Width * 0.44f, box.Y + box.Height * 0.72f),
                new PointF(box.X + box.Width * 0.78f, box.Y + box.Height * 0.30f)
            });
        }

        int iconX = box.Right + S(10);
        int iconSz = S(24);
        if (app.Icon != null)
            g.DrawImage(app.Icon, iconX, e.Bounds.Top + (e.Bounds.Height - iconSz) / 2, iconSz, iconSz);
        else
            g.FillRectangle(Brushes.Gray, iconX, e.Bounds.Top + (e.Bounds.Height - iconSz) / 2, iconSz, iconSz);

        // category pill on the right
        var color = AppClassifier.CategoryColor(app.Category);
        var pillText = app.Category;
        int pillW = TextRenderer.MeasureText(pillText, pillFont).Width + S(20);
        var pill = new Rectangle(e.Bounds.Right - pillW - S(10),
            e.Bounds.Top + (e.Bounds.Height - S(22)) / 2, pillW, S(22));
        using (var path = Gfx.Round(pill, S(10)))
        using (var b = new SolidBrush(color.WithAlpha(p.Dark ? 60 : 38)))
            g.FillPath(b, path);
        TextRenderer.DrawText(g, pillText, pillFont, pill,
            p.Dark ? color.Lighten(0.28) : color.Darken(0.28),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        int textX = iconX + iconSz + S(12);
        int textW = Math.Max(S(80), pill.Left - textX - S(10));
        var nameColor = app.AlreadyInFolder ? p.SubText : p.Text;
        TextRenderer.DrawText(g, app.Name, rowFont,
            new Rectangle(textX, e.Bounds.Top, textW, e.Bounds.Height), nameColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    // ---------------------------------------------------------------- actions

    void AddChecked()
    {
        var picked = checkedApps.Count > 0 ? checkedApps.ToList() : new List<ScannedApp>();
        if (picked.Count == 0)
        {
            status.Text = "Tick a few apps first (or press Select all).";
            return;
        }

        Folder target;
        if (targetChoices.Count == 0 || targetFolder.SelectedIndex < 0 || targetFolder.SelectedIndex >= targetChoices.Count)
        {
            target = new Folder { Name = UniqueFolderName("Scanned apps"), Columns = 4 };
            cfg.Folders.Add(target);
        }
        else target = targetChoices[targetFolder.SelectedIndex];

        int added = AddTo(target, picked);
        Finish($"Added {added} app{(added == 1 ? "" : "s")} to “{target.Name}”.");
    }

    int AddTo(Folder target, IEnumerable<ScannedApp> apps)
    {
        int added = 0;
        foreach (var app in apps)
        {
            if (target.Items.Any(i =>
                    string.Equals(i.Target, app.Target, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(i.Name, app.Name, StringComparison.OrdinalIgnoreCase))) continue;
            target.Items.Add(new Item { Name = app.Name, Target = app.Target });
            app.AlreadyInFolder = true;
            added++;
        }
        if (added > 0) Persist(target);
        return added;
    }

    void AutoOrganize()
    {
        bool explicitPick = checkedApps.Count > 0;
        var picked = explicitPick
            ? checkedApps.Where(a => !a.AlreadyInFolder).ToList()
            : shown.Where(a => !a.AlreadyInFolder && a.Category != "Other").ToList();
        if (picked.Count == 0)
        {
            status.Text = "Nothing new to organise — check some apps or clear the search filter.";
            return;
        }
        int uncategorised = explicitPick ? 0 : shown.Count(a => !a.AlreadyInFolder && a.Category == "Other");

        var groups = picked.GroupBy(a => a.Category)
            .OrderBy(g => Array.IndexOf(AppClassifier.Categories, g.Key))
            .Where(g => g.Any())
            .ToList();
        var summary = string.Join("\n", groups.Select(g => $"• {g.Key}: {g.Count()} app{(g.Count() == 1 ? "" : "s")}"));
        if (MessageBox.Show(this,
                $"Create/update folders for these apps?\n\n{summary}\n\nExisting folders such as “AI” or “Dev tools” are reused when their name matches.",
                "Auto-organise", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        int created = 0, added = 0;
        foreach (var group in groups)
        {
            var target = FindFolderForCategory(group.Key);
            if (target == null)
            {
                target = new Folder
                {
                    Name = UniqueFolderName(group.Key),
                    Columns = group.Key == "AI" ? 4 : 3,
                    ShowNames = true
                };
                cfg.Folders.Add(target);
                created++;
            }
            added += AddTo(target, group);
        }
        Finish($"Auto-organised {added} apps into {groups.Count} folder{(groups.Count == 1 ? "" : "s")}" +
               (created > 0 ? $" ({created} new)." : ".") +
               (uncategorised > 0 ? $" {uncategorised} uncategorised apps were left for manual sorting." : ""));
    }

    Folder? FindFolderForCategory(string category)
    {
        var exact = cfg.Folders.FirstOrDefault(f => f.Name.Equals(category, StringComparison.CurrentCultureIgnoreCase));
        if (exact != null) return exact;
        var hint = AppClassifier.FolderHint(category);
        if (hint == null) return null;
        return cfg.Folders.FirstOrDefault(f => f.Name.Contains(hint, StringComparison.CurrentCultureIgnoreCase));
    }

    string UniqueFolderName(string name)
    {
        var candidate = name;
        int i = 2;
        while (cfg.Folders.Any(f => f.Name.Equals(candidate, StringComparison.CurrentCultureIgnoreCase)))
            candidate = $"{name} ({i++})";
        return candidate;
    }

    void Persist(Folder f)
    {
        try
        {
            cfg.Save();
            IconBuilder.BuildIcon(f);
            Changed = true;
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    void Finish(string message)
    {
        var targets = new HashSet<string>(cfg.Folders.SelectMany(f => f.Items).Select(i => i.Target),
            StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(cfg.Folders.SelectMany(f => f.Items).Select(i => i.Name),
            StringComparer.OrdinalIgnoreCase);
        foreach (var a in all)
            a.AlreadyInFolder = targets.Contains(a.Target) || names.Contains(a.Name);

        cfg.Save();
        RefreshTargets();
        Fill();
        status.Text = message;
    }
}

