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

/// <summary>Dark colours for the folder context menu.</summary>
sealed class MenuColors : ProfessionalColorTable
{
    static readonly Color Bg = Color.FromArgb(43, 43, 43);
    static readonly Color Line = Color.FromArgb(70, 70, 70);
    static readonly Color Sel = Color.FromArgb(62, 62, 62);

    public override Color ToolStripDropDownBackground => Bg;
    public override Color ImageMarginGradientBegin => Bg;
    public override Color ImageMarginGradientMiddle => Bg;
    public override Color ImageMarginGradientEnd => Bg;
    public override Color MenuBorder => Line;
    public override Color MenuItemBorder => Line;
    public override Color MenuItemSelected => Sel;
    public override Color MenuItemSelectedGradientBegin => Sel;
    public override Color MenuItemSelectedGradientEnd => Sel;
    public override Color SeparatorDark => Line;
    public override Color SeparatorLight => Line;
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
    readonly Font tinyFont = new("Segoe UI", 8.5f);

    // per-folder menu settings
    readonly ComboBox cbView = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    readonly CheckBox chkNames = new() { Text = "Show names", AutoSize = true };
    readonly Button btnIcon = new() { Text = "Folder icon…", Width = 120, Height = 30 };
    readonly ContextMenuStrip folderMenu = new();

    bool syncing;
    Point dragOrigin;
    int dragIndex = -1;

    public Manager(Config config)
    {
        cfg = config;
        Text = "Taskbar Folders";
        ClientSize = new Size(880, 560);
        MinimumSize = new Size(760, 460);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }

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

        // ---- right: menu settings + apps ----
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 12, 14, 12) };
        rightTitle.Text = "Apps (drag && drop .exe / .lnk / folders here)";

        cbView.Items.AddRange(new object[] { "Rows (with names)", "2 columns", "3 columns", "4 columns" });
        var viewLabel = new Label { Text = "Menu view:", AutoSize = true, Margin = new Padding(0, 9, 6, 0) };
        cbView.Margin = new Padding(0, 4, 0, 0);
        chkNames.Margin = new Padding(14, 7, 0, 0);
        btnIcon.Margin = new Padding(14, 2, 0, 0);
        var settings = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(0, 4, 0, 0), WrapContents = false };
        settings.Controls.AddRange(new Control[] { viewLabel, cbView, chkNames, btnIcon });

        var rightBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(0, 8, 0, 0) };
        rightBtns.Controls.AddRange(new Control[]
        {
            Btn("Add…", (_, _) => AddDialog(), 80),
            Btn("Edit", (_, _) => EditSelected(), 70),
            Btn("Remove", (_, _) => RemoveSelected(), 80),
            Btn("▲", (_, _) => Move(-1), 44),
            Btn("▼", (_, _) => Move(1), 44)
        });
        var btnPin = new Button { Text = "Pin to taskbar", Dock = DockStyle.Bottom, Height = 44 };
        btnPin.Click += (_, _) => PinCurrent();

        right.Controls.Add(lv);
        right.Controls.Add(rightBtns);
        right.Controls.Add(btnPin);
        right.Controls.Add(settings);
        right.Controls.Add(rightTitle);

        Controls.Add(right);
        Controls.Add(left);

        lv.Columns.Add("Name", 200);
        lv.Columns.Add("Target", 330);
        lv.Columns.Add("Arguments", 120);
        lv.SmallImageList = il;

        // ---- folder context menu ----
        folderMenu.Items.Add("Pin to taskbar", null, (_, _) => PinCurrent());
        folderMenu.Items.Add("Show shortcut in Explorer", null, (_, _) => ShowShortcut());
        folderMenu.Items.Add(new ToolStripSeparator());
        folderMenu.Items.Add("Change icon…", null, (_, _) => ChangeIcon());
        folderMenu.Items.Add("Rename", null, (_, _) => RenameFolder());
        folderMenu.Items.Add("Delete", null, (_, _) => DeleteFolder());
        folderMenu.Opening += (_, e) => e.Cancel = Current == null;
        if (dark)
        {
            folderMenu.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
            folderMenu.BackColor = Color.FromArgb(43, 43, 43);
            folderMenu.ForeColor = Color.White;
        }
        lbFolders.ContextMenuStrip = folderMenu;

        // ---- events ----
        lbFolders.DrawItem += DrawFolder;
        lbFolders.SelectedIndexChanged += (_, _) => { FillItems(); SyncSettings(); };
        lbFolders.MouseDown += (_, e) =>
        {
            int i = lbFolders.IndexFromPoint(e.Location);
            if (e.Button == MouseButtons.Right) { if (i >= 0) lbFolders.SelectedIndex = i; }
            else if (e.Button == MouseButtons.Left) { dragIndex = i; dragOrigin = e.Location; }
        };
        lbFolders.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || dragIndex < 0 || dragIndex >= cfg.Folders.Count) return;
            if (Math.Abs(e.X - dragOrigin.X) < SystemInformation.DragSize.Width &&
                Math.Abs(e.Y - dragOrigin.Y) < SystemInformation.DragSize.Height) return;
            var fld = cfg.Folders[dragIndex];
            dragIndex = -1;
            StartDrag(fld);
        };
        lbFolders.MouseUp += (_, _) => dragIndex = -1;

        cbView.SelectedIndexChanged += (_, _) =>
        {
            if (syncing || Current is not { } fld) return;
            fld.Columns = cbView.SelectedIndex + 1;
            chkNames.Enabled = fld.Columns > 1;
            cfg.Save();
        };
        chkNames.CheckedChanged += (_, _) =>
        {
            if (syncing || Current is not { } fld) return;
            fld.ShowNames = chkNames.Checked;
            cfg.Save();
        };
        btnIcon.Click += (_, _) => ChangeIcon();

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
        else { FillItems(); SyncSettings(); }
    }

    void SyncSettings()
    {
        var f = Current;
        syncing = true;
        cbView.Enabled = btnIcon.Enabled = f != null;
        if (f != null)
        {
            cbView.SelectedIndex = Math.Clamp(f.Columns, 1, 4) - 1;
            chkNames.Checked = f.ShowNames;
            chkNames.Enabled = f.Columns > 1;
        }
        else
        {
            cbView.SelectedIndex = -1;
            chkNames.Enabled = false;
        }
        syncing = false;
    }

    void DrawFolder(object? s, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= cfg.Folders.Count) return;
        var f = cfg.Folders[e.Index];
        e.DrawBackground();
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        if (previews.TryGetValue(f.Id, out var bmp))
            e.Graphics.DrawImage(bmp, e.Bounds.X + 6, e.Bounds.Y + 6, 32, 32);

        bool pinned = Pinning.IsPinned(f);
        int reserve = pinned ? 62 : 4;
        TextRenderer.DrawText(e.Graphics, f.Name, Font,
            new Rectangle(e.Bounds.X + 46, e.Bounds.Y, e.Bounds.Width - 46 - reserve, e.Bounds.Height),
            e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (pinned)
        {
            bool sel = (e.State & DrawItemState.Selected) != 0;
            TextRenderer.DrawText(e.Graphics, "Pinned", tinyFont,
                new Rectangle(e.Bounds.Right - 60, e.Bounds.Y, 56, e.Bounds.Height),
                sel ? e.ForeColor : Color.FromArgb(76, 160, 255),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
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
        try { IconBuilder.BuildIcon(f); } catch (Exception ex) { ErrorLog.Write(ex); }
        var fresh = IconBuilder.Render(f, 64);
        if (previews.TryGetValue(f.Id, out var old)) old.Dispose();
        previews[f.Id] = fresh;
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
        var oldLnk = Pinning.LnkPath(f);
        f.Name = name;
        Save(f);
        try { File.Delete(oldLnk); } catch { }
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
        try { File.Delete(Pinning.LnkPath(f)); } catch { }
        try { foreach (var p in Directory.GetFiles(Config.CustomDir, f.Id + ".*")) File.Delete(p); } catch { }
        if (previews.Remove(f.Id, out var bmp)) bmp.Dispose();
        RefreshFolders(Math.Min(idx, cfg.Folders.Count - 1));
    }

    void ChangeIcon()
    {
        var f = Current; if (f == null) return;
        if (!IconPicker.Show(this, f)) return;
        Save(f);
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

    // ---------- taskbar ----------

    /// <summary>Drag a folder out of the list and drop it on the taskbar (or Explorer / desktop).</summary>
    void StartDrag(Folder f)
    {
        string lnk;
        try { lnk = Pinning.EnsureShortcut(f); }
        catch (Exception ex) { ErrorLog.Write(ex); return; }
        lbFolders.DoDragDrop(new DataObject(DataFormats.FileDrop, new[] { lnk }), DragDropEffects.Copy | DragDropEffects.Link);
    }

    async void PinCurrent()
    {
        var f = Current; if (f == null) return;
        try
        {
            if (Pinning.IsPinned(f))
            {
                MessageBox.Show(this, "This folder is already pinned to the taskbar.", "Taskbar Folders",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var lnk = Pinning.EnsureShortcut(f);
            Pinning.TryVerbPin(lnk);

            // If Windows accepted the pin it shows up within a moment.
            for (int i = 0; i < 8 && !Pinning.IsPinned(f); i++) await Task.Delay(150);
            lbFolders.Invalidate();
            if (Pinning.IsPinned(f)) return; // the list now shows "Pinned"

            new PinHelper(f, lnk).Show(this);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            MessageBox.Show(this, "Couldn't create the shortcut:\n" + ex.Message, "Taskbar Folders",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void ShowShortcut()
    {
        var f = Current; if (f == null) return;
        try
        {
            var lnk = Pinning.EnsureShortcut(f);
            Process.Start("explorer.exe", $"/select,\"{lnk}\"");
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }
}
