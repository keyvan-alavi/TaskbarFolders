namespace TaskbarFolders;

public sealed class StartApp
{
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string Group { get; set; } = "";
}

/// <summary>Reads the Start menu shortcuts (all users + current user), the same list Windows shows.</summary>
static class StartMenuScanner
{
    public static List<StartApp> Scan()
    {
        var list = new List<StartApp>();
        var seenNames = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                     Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
                 })
        {
            var dir = System.IO.Path.Combine(root, "Programs");
            if (!Directory.Exists(dir)) continue;

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories); }
            catch { continue; }

            foreach (var file in files)
            {
                try
                {
                    var rel = System.IO.Path.GetRelativePath(dir, file);
                    var name = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (name.Length == 0) continue;
                    if (name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                    if (rel.Contains("Startup", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!seenNames.Add(name)) continue;

                    var group = System.IO.Path.GetDirectoryName(rel) ?? "";
                    list.Add(new StartApp { Name = name, Target = file, Group = group });
                }
                catch { }
            }
        }

        return list.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

/// <summary>Searchable list of Start-menu apps; returns the shortcuts the user picked.</summary>
sealed class StartMenuPicker : Form
{
    readonly List<StartApp> all;
    readonly List<StartApp> shown = new();
    readonly ListView lv;
    readonly ImageList il;
    readonly TextBox search;
    readonly Label countLabel;
    readonly Dictionary<string, Bitmap> icons = new(StringComparer.OrdinalIgnoreCase);

    public List<string> SelectedTargets { get; } = new();

    public StartMenuPicker(List<StartApp> apps)
    {
        all = apps;
        bool dark = ThemeManager.P.Dark;

        Text = "Add from Start menu";
        ClientSize = new Size(700, 520);
        MinimumSize = new Size(560, 420);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;

        var header = new Label
        {
            Text = "Start menu apps",
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(18, 14, 0, 0),
            Font = new Font("Segoe UI Semibold", 13f)
        };
        var sub = new Label
        {
            Text = "Pick one or more apps — they are added straight to this folder.",
            Dock = DockStyle.Top,
            Height = 26,
            Padding = new Padding(19, 0, 0, 0),
            ForeColor = ThemeManager.P.SubText,
            Font = new Font("Segoe UI", 9f)
        };

        search = new TextBox { Dock = DockStyle.Top, Margin = new Padding(18, 0, 18, 8) };
        var searchHost = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(18, 2, 18, 10) };
        searchHost.Controls.Add(search);
        search.PlaceholderText = "Search apps…";

        il = new ImageList { ImageSize = new Size(20, 20), ColorDepth = ColorDepth.Depth32Bit };
        lv = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = true,
            BorderStyle = BorderStyle.None,
            SmallImageList = il,
            OwnerDraw = true,
            HeaderStyle = ColumnHeaderStyle.None,
            Font = new Font("Segoe UI", 10f),
            ShowItemToolTips = true
        };
        lv.Columns.Add("App", 360);
        lv.Columns.Add("Start menu group", 300);
        lv.Resize += (_, _) => lv.Columns[1].Width = Math.Max(160, lv.ClientSize.Width - lv.Columns[0].Width);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 14, 0) };
        var columnHead = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = ThemeManager.P.Card };
        columnHead.Paint += (_, e) =>
        {
            var pp = ThemeManager.P;
            using var f = new Font("Segoe UI Semibold", 8f);
            TextRenderer.DrawText(e.Graphics, "APP", f,
                new Rectangle(8, 0, 340, columnHead.Height), pp.SubText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, "START MENU GROUP", f,
                new Rectangle(368, 0, 320, columnHead.Height), pp.SubText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            using var pen = new Pen(pp.Border);
            e.Graphics.DrawLine(pen, 0, columnHead.Height - 1, columnHead.Width, columnHead.Height - 1);
        };
        body.Controls.Add(lv);
        body.Controls.Add(columnHead);

        countLabel = new Label
        {
            Text = "",
            AutoSize = false,
            Dock = DockStyle.Left,
            Width = 260,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(18, 0, 0, 0),
            ForeColor = ThemeManager.P.SubText
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 300,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 16, 10)
        };
        var add = new FlatButton { Text = "Add selected", Primary = true, Width = 120, Height = 34, Margin = new Padding(0, 0, 8, 0) };
        var cancel = new FlatButton { Text = "Cancel", Width = 90, Height = 34 };
        add.Click += (_, _) => Accept();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(add);
        buttons.Controls.Add(cancel);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 58 };
        footer.Controls.Add(countLabel);
        footer.Controls.Add(buttons);

        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(searchHost);
        Controls.Add(sub);
        Controls.Add(header);

        ThemeManager.Apply(this);
        header.ForeColor = ThemeManager.P.Text;
        sub.ForeColor = ThemeManager.P.SubText;
        countLabel.ForeColor = ThemeManager.P.SubText;
        search.BackColor = ThemeManager.P.Card;
        search.ForeColor = ThemeManager.P.Text;

        search.TextChanged += (_, _) => Fill();
        lv.DoubleClick += (_, _) => Accept();
        lv.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Accept(); e.Handled = true; } };
        lv.OwnerDraw = true;
        lv.DrawItem += (_, _) => { };
        lv.DrawSubItem += DrawSub;

        HandleCreated += (_, _) => Native.SetDarkTitle(Handle, ThemeManager.P.Dark);
        Shown += (_, _) => { search.Focus(); Fill(); };
    }

    void DrawHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        var p = ThemeManager.P;
        using var b = new SolidBrush(p.Card);
        e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(e.Graphics, e.Header!.Text, Font, e.Bounds,
            p.SubText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        using var pen = new Pen(p.Border);
        e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
    }

    void DrawSub(object? sender, DrawListViewSubItemEventArgs e)
    {
        var p = ThemeManager.P;
        var item = e.Item!;
        bool sel = item.Selected;
        var bg = sel ? p.Sel : (e.ItemIndex % 2 == 0 ? p.Panel : p.Bg);
        using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);

        if (e.ColumnIndex == 0)
        {
            int iconSize = 20;
            if (item.ImageIndex >= 0 && item.ImageIndex < il.Images.Count)
                e.Graphics.DrawImage(il.Images[item.ImageIndex], e.Bounds.Left + 8,
                    e.Bounds.Top + (e.Bounds.Height - iconSize) / 2, iconSize, iconSize);
        }
        var text = e.SubItem?.Text ?? "";
        var fg = sel ? p.Text : (e.ColumnIndex == 0 ? p.Text : p.SubText);
        var r = new Rectangle(e.Bounds.Left + (e.ColumnIndex == 0 ? 40 : 8), e.Bounds.Top,
            e.Bounds.Width - (e.ColumnIndex == 0 ? 48 : 12), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, text, Font, r, fg,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    void Fill()
    {
        var q = search.Text.Trim();
        shown.Clear();
        shown.AddRange(q.Length == 0
            ? all
            : all.Where(a => a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                             a.Group.Contains(q, StringComparison.CurrentCultureIgnoreCase)));

        lv.BeginUpdate();
        lv.Items.Clear();
        il.Images.Clear();
        foreach (var app in shown)
        {
            var icon = IconFor(app.Target);
            if (icon != null) il.Images.Add(icon);
            int idx = il.Images.Count - 1;
            var row = new ListViewItem(app.Name) { ImageIndex = idx, ToolTipText = app.Target };
            row.SubItems.Add(app.Group);
            lv.Items.Add(row);
        }
        lv.EndUpdate();
        countLabel.Text = $"{shown.Count} of {all.Count} apps";
    }

    Bitmap? IconFor(string path)
    {
        if (icons.TryGetValue(path, out var bmp)) return bmp;
        try
        {
            bmp = IconUtil.Get(path);
            icons[path] = bmp;
            return bmp;
        }
        catch { return null; }
    }

    void Accept()
    {
        SelectedTargets.Clear();
        foreach (ListViewItem item in lv.SelectedItems)
        {
            int i = item.Index;
            if (i >= 0 && i < shown.Count) SelectedTargets.Add(shown[i].Target);
        }
        if (SelectedTargets.Count == 0 && shown.Count > 0)
            SelectedTargets.Add(shown[lv.FocusedItem?.Index ?? 0].Target);

        if (SelectedTargets.Count == 0) return;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        lv.SmallImageList = null;
        il.Dispose();
        foreach (var b in icons.Values) b.Dispose();
        icons.Clear();
        base.OnFormClosed(e);
    }
}
