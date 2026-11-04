using System.Drawing.Drawing2D;

namespace TaskbarFolders;

/// <summary>
/// Compact chooser shown by "Add to Taskbar Folder" in Explorer / Start menu context menus
/// (TaskbarFolders.exe --add "path"). One click files the app into a folder.
/// </summary>
sealed class AddToFolderForm : Form
{
    readonly Config cfg;
    readonly string[] paths;
    readonly BufferedListBox list;
    readonly Label status;
    readonly Dictionary<string, Bitmap> icons = new();
    readonly List<Folder> shown = new();
    readonly System.Windows.Forms.Timer closeTimer = new() { Interval = 650 };
    readonly Font fName = new("Segoe UI Semibold", 10f);
    readonly Font fMeta = new("Segoe UI", 8.25f);
    bool added;

    public AddToFolderForm(Config config, string[] sourcePaths)
    {
        cfg = config;
        paths = sourcePaths;
        var p = ThemeManager.P;

        Text = "Add to Taskbar Folder";
        ClientSize = new Size(520, 560);
        MinimumSize = new Size(460, 420);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        TopMost = true;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = p.Bg;
        ForeColor = p.Text;
        DoubleBuffered = true;

        var header = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = p.Bg };
        var icon = new PictureBox
        {
            Left = 20, Top = 18, Size = new Size(44, 44), SizeMode = PictureBoxSizeMode.Zoom,
            Image = IconUtil.Get(paths[0])
        };
        var title = new Label
        {
            Text = "Add to Taskbar Folder", Left = 76, Top = 16, AutoSize = true,
            Font = new Font("Segoe UI Semibold", 13.5f)
        };
        var file = new Label
        {
            Text = System.IO.Path.GetFileName(paths[0]) + (paths.Length > 1 ? $"  (+{paths.Length - 1} more)" : ""),
            Left = 78, Top = 46, Width = 420, Height = 22, AutoEllipsis = true,
            ForeColor = p.SubText, Font = new Font("Segoe UI", 9f)
        };
        var hint = new Label
        {
            Text = "Choose the folder that should receive this app:",
            Left = 22, Top = 72, AutoSize = true, ForeColor = p.SubText, Font = new Font("Segoe UI", 8.75f)
        };
        header.Controls.AddRange(new Control[] { icon, title, file, hint });

        list = new BufferedListBox
        {
            Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 56,
            BorderStyle = BorderStyle.None, IntegralHeight = false, BackColor = p.Panel
        };
        list.DrawItem += DrawItem;
        list.DoubleClick += (_, _) => Add();

        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 4, 20, 6), BackColor = p.Bg };
        listHost.Controls.Add(list);

        status = new Label
        {
            Dock = DockStyle.Bottom, Height = 28, ForeColor = p.Accent,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(22, 0, 0, 0),
            Font = new Font("Segoe UI", 9f)
        };

        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = p.Bg };
        var add = new FlatButton { Text = "Add", Width = 96, Height = 36, Primary = true };
        add.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        add.Click += (_, _) => Add();
        var cancel = new FlatButton { Text = "Cancel", Width = 96, Height = 36 };
        cancel.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        cancel.Click += (_, _) => Close();
        var newFolder = new FlatButton { Text = "New folder…", Width = 120, Height = 36 };
        newFolder.Click += (_, _) => NewFolder();
        buttons.Controls.AddRange(new Control[] { newFolder, add, cancel });
        buttons.Resize += (_, _) =>
        {
            add.Left = buttons.ClientSize.Width - 220;
            cancel.Left = buttons.ClientSize.Width - 116;
            add.Top = cancel.Top = 12;
            newFolder.Left = 20;
            newFolder.Top = 12;
        };

        Controls.Add(listHost);
        Controls.Add(status);
        Controls.Add(buttons);
        Controls.Add(header);

        ThemeManager.Apply(this);
        file.ForeColor = p.SubText;
        hint.ForeColor = p.SubText;
        status.ForeColor = p.Accent;
        foreach (Control c in header.Controls) c.BackColor = p.Bg;
        AcceptButton = add;
        CancelButton = cancel;

        Load += (_, _) => Reload();
        HandleCreated += (_, _) => Native.SetDarkTitle(Handle, p.Dark);

        closeTimer.Tick += (_, _) => { closeTimer.Stop(); Close(); };
        FormClosed += (_, _) =>
        {
            closeTimer.Dispose();
            foreach (var b in icons.Values) b.Dispose();
            fName.Dispose();
            fMeta.Dispose();
            icon.Image?.Dispose();
        };

        Shown += (_, _) => list.Focus();
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.Enter && list.SelectedIndex >= 0) Add();
        };
    }

    void Reload()
    {
        shown.Clear();
        shown.AddRange(cfg.Folders.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase));
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var f in shown) list.Items.Add(f.Name);
        list.EndUpdate();

        int idx = shown.FindIndex(f => f.Id == cfg.Settings.LastFolderId);
        if (idx >= 0) list.SelectedIndex = idx;
        else if (list.Items.Count > 0) list.SelectedIndex = 0;
        status.Text = cfg.Folders.Count == 0
            ? "No folders yet — create one first."
            : $"{cfg.Folders.Count} folder{(cfg.Folders.Count == 1 ? "" : "s")} available.";
    }

    Folder? SelectedFolder()
    {
        int i = list.SelectedIndex;
        return i >= 0 && i < shown.Count ? shown[i] : null;
    }

    void DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= shown.Count) return;
        var p = ThemeManager.P;
        var f = shown[e.Index];

        bool sel = (e.State & DrawItemState.Selected) != 0;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(sel ? p.Sel : p.Panel))
            g.FillRectangle(b, e.Bounds);

        var row = new RectangleF(4, e.Bounds.Y + 3, e.Bounds.Width - 8, e.Bounds.Height - 6);
        using (var path = Gfx.Round(row, 9))
        using (var b = new SolidBrush(sel ? p.Sel : p.Panel))
            g.FillPath(b, path);

        if (!icons.TryGetValue(f.Id, out var icon))
        {
            icon = IconBuilder.Render(f, 36);
            icons[f.Id] = icon;
        }
        g.DrawImage(icon, row.X + 10, row.Y + (row.Height - 32) / 2, 32, 32);

        TextRenderer.DrawText(g, f.Name, fName,
            new Rectangle((int)row.X + 52, (int)row.Y + 7, (int)row.Width - 60, 22),
            p.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, $"{f.Items.Count} app{(f.Items.Count == 1 ? "" : "s")}", fMeta,
            new Rectangle((int)row.X + 52, (int)row.Y + 29, (int)row.Width - 60, 18),
            p.SubText, TextFormatFlags.Left);
    }

    void Add()
    {
        if (added) return;
        var f = SelectedFolder();
        if (f == null)
        {
            NewFolder();
            f = SelectedFolder();
            if (f == null) return;
        }

        int count = 0;
        foreach (var raw in paths)
        {
            var path = raw.Trim().Trim('"');
            if (path.Length == 0) continue;
            if (f.Items.Any(i => string.Equals(i.Target, path, StringComparison.OrdinalIgnoreCase))) continue;
            string name;
            try
            {
                name = Directory.Exists(path)
                    ? System.IO.Path.GetFileName(path.TrimEnd('\\', '/'))
                    : System.IO.Path.GetFileNameWithoutExtension(path);
            }
            catch { name = path; }
            if (string.IsNullOrWhiteSpace(name)) name = path;
            f.Items.Add(new Item { Name = name, Target = path });
            count++;
        }

        cfg.Settings.LastFolderId = f.Id;
        cfg.Save();
        try
        {
            Pinning.EnsureShortcut(f);
            if (Pinning.IsPinned(f)) Native.RefreshShellIcons();
        }
        catch (Exception ex) { ErrorLog.Write(ex); }

        added = true;
        status.Text = count == 0
            ? $"Already in “{f.Name}”."
            : $"Added to “{f.Name}” ✓";
        status.ForeColor = ThemeManager.P.Accent;
        closeTimer.Start();
    }

    void NewFolder()
    {
        var name = Prompt.Show(this, "New folder", "Folder name:", "New folder");
        if (name == null) return;
        var f = new Folder { Name = name };
        cfg.Folders.Add(f);
        cfg.Save();
        IconBuilder.BuildIcon(f);
        Reload();
        int at = shown.FindIndex(x => x.Id == f.Id);
        if (at >= 0) list.SelectedIndex = at;
        status.Text = $"Created “{f.Name}”. Press Add to file the app.";
    }
}
