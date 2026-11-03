using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace TaskbarFolders;

static class Prompt
{
    public static string? Show(IWin32Window owner, string title, string label, string def = "")
    {
        bool dark = Theme.IsDark();
        using var f = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, ClientSize = new Size(380, 120),
            Font = new Font("Segoe UI", 9.5f)
        };
        var l = new Label { Text = label, Left = 14, Top = 14, Width = 350 };
        var tb = new TextBox { Left = 14, Top = 40, Width = 350, Text = def };
        var ok = new Button { Text = "OK", Left = 204, Top = 78, Width = 76, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 288, Top = 78, Width = 76, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { l, tb, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        Theme.Apply(f, dark);
        f.HandleCreated += (_, _) => Native.SetDarkTitle(f.Handle, dark);
        tb.SelectAll();
        return f.ShowDialog(owner) == DialogResult.OK && tb.Text.Trim().Length > 0 ? tb.Text.Trim() : null;
    }
}

public class Manager : Form
{
    readonly Config cfg;
    readonly bool dark = Theme.IsDark();
    readonly ListBox lbFolders = new()
    {
        Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 44,
        BorderStyle = BorderStyle.None, IntegralHeight = false
    };
    readonly ListView lv = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
        AllowDrop = true, BorderStyle = BorderStyle.None
    };
    readonly ImageList il = new() { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit };
    readonly Dictionary<string, Bitmap> previews = new();
    readonly Label rightTitle = new() { Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI Semibold", 10.5f) };

    public Manager(Config config)
    {
        cfg = config;
        Text = "Taskbar Folders";
        ClientSize = new Size(880, 540);
        MinimumSize = new Size(760, 420);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        // ---- left: folders ----
        var left = new Panel { Dock = DockStyle.Left, Width = 260, Padding = new Padding(14, 12, 6, 12) };
        var leftTitle = new Label { Text = "Folders", Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI Semibold", 10.5f) };
        var leftBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(0, 8, 0, 0) };
        leftBtns.Controls.AddRange(new Control[]
        {
            Btn("New", (_, _) => NewFolder(), 70),
            Btn("Rename", (_, _) => RenameFolder(), 80),
            Btn("Delete", (_, _) => DeleteFolder(), 70)
        });
        left.Controls.Add(lbFolders);
        left.Controls.Add(leftBtns);
        left.Controls.Add(leftTitle);

        // ---- right: apps ----
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 12, 14, 12) };
        rightTitle.Text = "Apps (drag & drop .exe / .lnk / folders here)";
        var rightBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(0, 8, 0, 0) };
        rightBtns.Controls.AddRange(new Control[]
        {
            Btn("Add…", (_, _) => AddDialog(), 80),
            Btn("Edit", (_, _) => EditSelected(), 70),
            Btn("Remove", (_, _) => RemoveSelected(), 80),
            Btn("▲", (_, _) => Move(-1), 44),
            Btn("▼", (_, _) => Move(1), 44)
        });
        var btnPin = new Button { Text = "Create taskbar shortcut for this folder", Dock = DockStyle.Bottom, Height = 44 };
        btnPin.Click += (_, _) => CreateShortcut();
        right.Controls.Add(lv);
        right.Controls.Add(rightBtns);
        right.Controls.Add(btnPin);
        right.Controls.Add(rightTitle);

        Controls.Add(right);
        Controls.Add(left);

        lv.Columns.Add("Name", 200);
        lv.Columns.Add("Target", 330);
        lv.Columns.Add("Arguments", 120);
        lv.SmallImageList = il;

        lbFolders.DrawItem += DrawFolder;
        lbFolders.SelectedIndexChanged += (_, _) => FillItems();
        lv.DoubleClick += (_, _) => EditSelected();
        lv.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
        lv.DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        lv.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
        };

        Theme.Apply(this, dark);
        btnPin.BackColor = Color.FromArgb(0, 103, 192);
        btnPin.ForeColor = Color.White;
        btnPin.FlatAppearance.BorderSize = 0;
        lbFolders.BackColor = dark ? Color.FromArgb(43, 43, 43) : Color.White;

        foreach (var f in cfg.Folders) previews[f.Id] = IconBuilder.Render(f, 64);
        RefreshFolders(cfg.Folders.Count > 0 ? 0 : -1);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetDarkTitle(Handle, dark);
    }

    // ---------- helpers ----------
    static Button Btn(string text, EventHandler h, int w)
    {
        var b = new Button { Text = text, Width = w, Height = 30 };
        b.Click += h;
        return b;
    }

    Folder? Current => lbFolders.SelectedIndex >= 0 && lbFolders.SelectedIndex < cfg.Folders.Count
        ? cfg.Folders[lbFolders.SelectedIndex] : null;

    void RefreshFolders(int select)
    {
        lbFolders.Items.Clear();
        foreach (var f in cfg.Folders) lbFolders.Items.Add(f.Name);
        if (select >= 0 && select < lbFolders.Items.Count) lbFolders.SelectedIndex = select;
        else FillItems();
    }

    void DrawFolder(object? s, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= cfg.Folders.Count) return;
        var f = cfg.Folders[e.Index];
        e.DrawBackground();
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        if (previews.TryGetValue(f.Id, out var bmp))
            e.Graphics.DrawImage(bmp, e.Bounds.X + 6, e.Bounds.Y + 6, 32, 32);
        TextRenderer.DrawText(e.Graphics, f.Name, Font,
            new Rectangle(e.Bounds.X + 46, e.Bounds.Y, e.Bounds.Width - 50, e.Bounds.Height),
            e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    void FillItems()
    {
        lv.Items.Clear();
        il.Images.Clear();
        var f = Current;
        if (f == null) return;
        for (int i = 0; i < f.Items.Count; i++)
        {
            var it = f.Items[i];
            using var ic = IconUtil.Get(it.Target);
            il.Images.Add(ic);
            var row = new ListViewItem(it.Name, i);
            row.SubItems.Add(it.Target);
            row.SubItems.Add(it.Args);
            lv.Items.Add(row);
        }
    }

    void Save(Folder f)
    {
        cfg.Save();
        IconBuilder.BuildIcon(f);
        if (previews.TryGetValue(f.Id, out var old)) old.Dispose();
        previews[f.Id] = IconBuilder.Render(f, 64);
        lbFolders.Invalidate();
    }

    // ---------- folder actions ----------
    void NewFolder()
    {
        var name = Prompt.Show(this, "New folder", "Folder name:", "Dev tools");
        if (name == null) return;
        var f = new Folder { Name = name };
        cfg.Folders.Add(f);
        Save(f);
        RefreshFolders(cfg.Folders.Count - 1);
    }

    void RenameFolder()
    {
        var f = Current; if (f == null) return;
        var name = Prompt.Show(this, "Rename folder", "Folder name:", f.Name);
        if (name == null) return;
        int idx = lbFolders.SelectedIndex;
        f.Name = name;
        Save(f);
        RefreshFolders(idx);
    }

    void DeleteFolder()
    {
        var f = Current; if (f == null) return;
        if (MessageBox.Show(this, $"Delete folder \"{f.Name}\"?\n(Unpin its shortcut from the taskbar manually.)",
                "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        int idx = lbFolders.SelectedIndex;
        cfg.Folders.Remove(f);
        cfg.Save();
        try { File.Delete(IconBuilder.IcoPath(f)); } catch { }
        try { File.Delete(LnkPath(f)); } catch { }
        if (previews.Remove(f.Id, out var bmp)) bmp.Dispose();
        RefreshFolders(Math.Min(idx, cfg.Folders.Count - 1));
    }

    // ---------- item actions ----------
    void AddDialog()
    {
        if (Current == null) return;
        using var dlg = new OpenFileDialog
        {
            Multiselect = true, Title = "Add apps",
            Filter = "Programs and shortcuts|*.exe;*.lnk|All files|*.*"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) AddPaths(dlg.FileNames);
    }

    void AddPaths(IEnumerable<string> paths)
    {
        var f = Current;
        if (f == null) { MessageBox.Show(this, "Create or select a folder first.", "Taskbar Folders"); return; }
        foreach (var p in paths)
        {
            var name = Directory.Exists(p) ? System.IO.Path.GetFileName(p.TrimEnd('\\')) : System.IO.Path.GetFileNameWithoutExtension(p);
            f.Items.Add(new Item { Name = name, Target = p });
        }
        Save(f);
        FillItems();
    }

    void EditSelected()
    {
        var f = Current;
        if (f == null || lv.SelectedIndices.Count == 0) return;
        var it = f.Items[lv.SelectedIndices[0]];
        var name = Prompt.Show(this, "Edit app", "Display name:", it.Name);
        if (name == null) return;
        it.Name = name;
        var args = Prompt.Show(this, "Edit app", "Arguments (optional, cancel to keep):", it.Args);
        if (args != null) it.Args = args;
        Save(f);
        FillItems();
    }

    void RemoveSelected()
    {
        var f = Current; if (f == null) return;
        foreach (int i in lv.SelectedIndices.Cast<int>().OrderByDescending(x => x)) f.Items.RemoveAt(i);
        Save(f);
        FillItems();
    }

    void Move(int d)
    {
        var f = Current;
        if (f == null || lv.SelectedIndices.Count != 1) return;
        int i = lv.SelectedIndices[0], j = i + d;
        if (j < 0 || j >= f.Items.Count) return;
        (f.Items[i], f.Items[j]) = (f.Items[j], f.Items[i]);
        Save(f);
        FillItems();
        lv.Items[j].Selected = true;
    }

    // ---------- taskbar shortcut ----------
    static string LnkPath(Folder f)
    {
        var bad = System.IO.Path.GetInvalidFileNameChars();
        var name = string.Concat(f.Name.Select(c => bad.Contains(c) ? '_' : c));
        return System.IO.Path.Combine(Config.ShortcutsDir, name + ".lnk");
    }

    void CreateShortcut()
    {
        var f = Current; if (f == null) return;
        IconBuilder.BuildIcon(f);
        Directory.CreateDirectory(Config.ShortcutsDir);
        var lnk = LnkPath(f);
        ShortcutWriter.Create(lnk, Environment.ProcessPath!, $"--open {f.Id}",
            IconBuilder.IcoPath(f), $"{f.Name} (Taskbar Folder)", "TaskbarFolders." + f.Id);

        try { Process.Start("explorer.exe", $"/select,\"{lnk}\""); } catch { }
        MessageBox.Show(this,
            "Shortcut created and shown in Explorer.\n\n" +
            "Pin it once: right-click it > Show more options > Pin to taskbar\n" +
            "(or drag it onto the taskbar).\n\n" +
            "After that, clicking the icon opens this folder's menu. " +
            "Changing the apps inside later needs no re-pinning.",
            "Taskbar Folders", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
