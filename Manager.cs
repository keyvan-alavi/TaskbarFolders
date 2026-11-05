using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO.Pipes;
using System.Threading;

namespace TaskbarFolders;

sealed class BufferedListBox : ListBox
{
    public BufferedListBox() { DoubleBuffered = true; }
}

sealed class BufferedListView : ListView
{
    public bool DarkScrollbars = true;
    public BufferedListView() { DoubleBuffered = true; }
}

/// <summary>Palette for the folder / app context menus.</summary>
sealed class MenuColors : ProfessionalColorTable
{
    static Palette P => ThemeManager.P;
    public override Color ToolStripDropDownBackground => P.Panel;
    public override Color ImageMarginGradientBegin => P.Panel;
    public override Color ImageMarginGradientMiddle => P.Panel;
    public override Color ImageMarginGradientEnd => P.Panel;
    public override Color MenuBorder => P.Border;
    public override Color MenuItemBorder => P.Accent;
    public override Color MenuItemSelected => P.Hover;
    public override Color MenuItemSelectedGradientBegin => P.Hover;
    public override Color MenuItemSelectedGradientEnd => P.Hover;
    public override Color SeparatorDark => P.Border;
    public override Color SeparatorLight => P.Border;
    public override Color ButtonSelectedHighlight => P.Hover;
}

public class Manager : Form
{
    const string ItemDragFormat = "TBF.Items";

    readonly Config cfg;
    readonly BufferedListBox lbFolders;
    readonly BufferedListView lv;
    readonly ImageList il;
    readonly List<Bitmap> rowIcons = new();
    readonly Dictionary<string, Bitmap> previews = new();
    readonly Label lblName = new();
    readonly Label lblMeta = new();
    readonly Label lblStatus = new();
    readonly PictureBox picFolder = new();
    readonly OptionButton cbView = new();
    readonly CheckBox chkNames = new();
    readonly TextBox txtSearch = new();
    readonly FlatButton btnPin;
    readonly FlatButton btnAdd;
    readonly FlatButton btnStart;
    readonly FlatButton btnScan;
    readonly FlatButton btnColor;
    readonly FlatButton btnIcon;
    readonly FlatButton btnSettings;
    readonly Font fFolderName = new("Segoe UI Semibold", 10f);
    readonly Font fMeta = new("Segoe UI", 8.25f);
    readonly Font fHead = new("Segoe UI", 8.5f);
    readonly Font fRowName = new("Segoe UI", 9.5f);
    readonly Font fRowSub = new("Segoe UI", 8.75f);
    readonly ContextMenuStrip folderMenu = new();
    readonly ContextMenuStrip itemMenu = new();
    readonly System.Windows.Forms.Timer pinWatch = new() { Interval = 1600 };

    bool syncing;
    int hoverIndex = -1;
    int dropIndex = -1;
    Point dragOrigin;
    int dragIndex = -1;
    bool dragBtnDown;
    Point dragBtnOrigin;
    NamedPipeServerStream? pipeServer;
    Thread? pipeThread;
    volatile bool pipeRunning;
    PinHelper? pinHelper;

    int S(int v) => (int)Math.Round(v * DeviceDpi / 96f);

    public Manager(Config config, bool openSettings = false, string? selectFolderId = null)
    {
        cfg = config;
        var p = ThemeManager.P;

        Text = "Taskbar Folders";
        ClientSize = new Size(1040, 690);
        MinimumSize = new Size(920, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = p.Bg;
        ForeColor = p.Text;
        DoubleBuffered = true;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }

        // ---------------- top bar ----------------
        var top = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = p.Bg };
        var logo = new PictureBox { Left = 20, Top = 16, Size = new Size(44, 44), SizeMode = PictureBoxSizeMode.Zoom };
        try { logo.Image = Icon?.ToBitmap(); } catch { }
        var title = new Label
        {
            Text = "Taskbar Folders", Left = 76, Top = 12, AutoSize = true,
            Font = new Font("Segoe UI Semibold", 16f), ForeColor = p.Text
        };
        var subtitle = new Label
        {
            Text = "Group apps into folders and keep them one click away on the Windows 11 taskbar.",
            Left = 78, Top = 44, AutoSize = true, ForeColor = p.SubText, Font = new Font("Segoe UI", 8.75f), Tag = "sub"
        };
        var btnSettingsHost = new Panel { Dock = DockStyle.Right, Width = 124, BackColor = p.Bg };
        btnSettings = new FlatButton { Text = "Settings", Width = 100, Height = 34, Left = 4, Top = 21 };
        btnSettings.Click += (_, _) => OpenSettings();
        btnSettingsHost.Controls.Add(btnSettings);
        var divider = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = p.Border };
        top.Controls.Add(logo);
        top.Controls.Add(title);
        top.Controls.Add(subtitle);
        top.Controls.Add(btnSettingsHost);
        top.Controls.Add(divider);
        top.Paint += (_, e) =>
        {
            using var brush = new SolidBrush(ThemeManager.P.Accent);
            e.Graphics.FillRectangle(brush, 0, 0, top.Width, 3);
        };

        // ---------------- left: folders ----------------
        var left = new Panel { Dock = DockStyle.Left, Width = 300, Padding = new Padding(16, 14, 8, 14), BackColor = p.Bg };
        var leftTitle = new Label
        {
            Text = "FOLDERS", Dock = DockStyle.Top, Height = 26, ForeColor = p.SubText,
            Font = new Font("Segoe UI Semibold", 8f)
        };
        lbFolders = new BufferedListBox
        {
            Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed,
            BorderStyle = BorderStyle.None, IntegralHeight = false, ItemHeight = 58,
            AllowDrop = true, BackColor = p.Panel
        };
        lbFolders.DrawItem += DrawFolder;
        lbFolders.SelectedIndexChanged += (_, _) => { FillItems(); SyncSettings(); };
        lbFolders.MouseMove += FolderMouseMove;
        lbFolders.MouseLeave += (_, _) => { if (hoverIndex != -1) { hoverIndex = -1; lbFolders.Invalidate(); } };
        lbFolders.MouseDown += FolderMouseDown;
        lbFolders.MouseUp += (_, _) => dragIndex = -1;
        lbFolders.DragEnter += FolderDragEnter;
        lbFolders.DragOver += FolderDragOver;
        lbFolders.DragLeave += (_, _) => { dropIndex = -1; lbFolders.Invalidate(); };
        lbFolders.DragDrop += FolderDragDrop;

        var folderButtons = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = p.Bg };
        var newBtn = new FlatButton { Text = "New folder", Left = 0, Top = 8, Width = 100, Height = 32, Primary = true };
        var renameBtn = new FlatButton { Text = "Rename", Left = 106, Top = 8, Width = 82, Height = 32 };
        var delBtn = new FlatButton { Text = "Delete", Left = 194, Top = 8, Width = 78, Height = 32 };
        newBtn.Click += (_, _) => NewFolder();
        renameBtn.Click += (_, _) => RenameFolder();
        delBtn.Click += (_, _) => DeleteFolder();
        var dragHint = new Label
        {
            Text = "Drag a folder onto the taskbar to pin it.",
            Left = 2, Top = 46, Width = 268, Height = 22, ForeColor = p.SubText, Font = new Font("Segoe UI", 8.25f), Tag = "sub"
        };
        folderButtons.Controls.AddRange(new Control[] { newBtn, renameBtn, delBtn, dragHint });
        left.Controls.Add(lbFolders);
        left.Controls.Add(folderButtons);
        left.Controls.Add(leftTitle);

        // ---------------- right ----------------
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 10, 16, 14), BackColor = p.Bg };

        var head = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = p.Bg };
        picFolder = new PictureBox
        {
            Dock = DockStyle.Left, Width = 64, SizeMode = PictureBoxSizeMode.Zoom,
            Padding = new Padding(0, 6, 10, 6), BackColor = p.Bg
        };
        lblName = new Label
        {
            Text = "Choose a folder", Dock = DockStyle.Top, Height = 34, AutoSize = false, AutoEllipsis = true,
            Font = new Font("Segoe UI Semibold", 14.5f), ForeColor = p.Text, TextAlign = ContentAlignment.BottomLeft
        };
        lblMeta = new Label
        {
            Text = "Folders keep your taskbar tidy.", Dock = DockStyle.Top, Height = 24, AutoSize = false,
            AutoEllipsis = true, ForeColor = p.SubText, Font = new Font("Segoe UI", 8.75f), Tag = "sub"
        };
        var info = new Panel { Dock = DockStyle.Fill, BackColor = p.Bg, Padding = new Padding(0, 6, 10, 0) };
        info.Controls.Add(lblMeta);
        info.Controls.Add(lblName);

        var pinHost = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, Width = 300, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = p.Bg, Padding = new Padding(0, 8, 0, 0)
        };
        btnPin = new FlatButton { Text = "Pin to taskbar", Width = 150, Height = 38, Primary = true, Margin = new Padding(0, 0, 8, 0) };
        btnPin.Click += (_, _) => PinCurrent();
        var btnDrag = new FlatButton { Text = "Drag to taskbar", Width = 136, Height = 38, Ghost = true, Margin = new Padding(0), Cursor = Cursors.SizeAll };
        btnDrag.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) { dragBtnDown = true; dragBtnOrigin = e.Location; }
        };
        btnDrag.MouseMove += (_, e) =>
        {
            if (!dragBtnDown || e.Button != MouseButtons.Left) return;
            if (Math.Abs(e.X - dragBtnOrigin.X) < SystemInformation.DragSize.Width &&
                Math.Abs(e.Y - dragBtnOrigin.Y) < SystemInformation.DragSize.Height) return;
            dragBtnDown = false;
            if (Current is { } fld) StartDrag(fld, btnDrag);
        };
        btnDrag.MouseUp += (_, _) => dragBtnDown = false;
        pinHost.Controls.Add(btnPin);
        pinHost.Controls.Add(btnDrag);

        head.Controls.Add(info);
        head.Controls.Add(pinHost);
        head.Controls.Add(picFolder);

        var actions = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = p.Bg };
        btnAdd = new FlatButton { Text = "+  Add apps", Left = 0, Top = 6, Width = 116, Height = 34 };
        btnStart = new FlatButton { Text = "Start menu…", Left = 122, Top = 6, Width = 118, Height = 34 };
        btnScan = new FlatButton { Text = "Scan PC…", Left = 246, Top = 6, Width = 92, Height = 34 };
        btnColor = new FlatButton { Text = "Colour…", Left = 344, Top = 6, Width = 92, Height = 34 };
        btnIcon = new FlatButton { Text = "Icon…", Left = 442, Top = 6, Width = 80, Height = 34 };
        btnAdd.Click += (_, _) => AddDialog();
        btnStart.Click += (_, _) => AddFromStartMenu();
        btnScan.Click += (_, _) => ScanPc();
        btnColor.Click += (_, _) => ChangeColor();
        btnIcon.Click += (_, _) => ChangeIcon();
        actions.Controls.AddRange(new Control[] { btnAdd, btnStart, btnScan, btnColor, btnIcon });

        var settingsRow = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = p.Bg };
        var viewLabel = new Label { Text = "Menu view", Left = 0, Top = 16, AutoSize = true, ForeColor = p.SubText, Font = new Font("Segoe UI", 8.5f), Tag = "sub" };
        cbView.Left = 64;
        cbView.Top = 10;
        cbView.Width = 170;
        cbView.Options = new[] { "Rows (with names)", "2 columns", "3 columns", "4 columns" };
        cbView.SelectedIndex = 0;
        chkNames.Text = "Show names";
        chkNames.Left = 248;
        chkNames.Top = 13;
        chkNames.AutoSize = true;
        chkNames.ForeColor = p.Text;
        txtSearch.PlaceholderText = "Search apps…";
        txtSearch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        txtSearch.Top = 10;
        txtSearch.Width = 220;
        txtSearch.TextChanged += (_, _) => FillItems();
        txtSearch.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { txtSearch.Text = ""; } };
        settingsRow.Controls.AddRange(new Control[] { viewLabel, cbView, chkNames, txtSearch });
        settingsRow.Resize += (_, _) => txtSearch.Left = settingsRow.ClientSize.Width - txtSearch.Width - 8;

        il = new ImageList { ImageSize = new Size(28, 28), ColorDepth = ColorDepth.Depth32Bit };
        lv = new BufferedListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
            BorderStyle = BorderStyle.None, SmallImageList = il, OwnerDraw = true, AllowDrop = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable, ShowItemToolTips = false,
            DarkScrollbars = false // the dark ListView theme paints artifacts under the last row
        };
        lv.Columns.Add("App", 220);
        lv.Columns.Add("Target", 320);
        lv.Columns.Add("Arguments", 110);
        lv.DrawColumnHeader += DrawItemHeader;
        lv.DrawItem += DrawItemRow;
        lv.DrawSubItem += DrawItemSub;
        lv.DoubleClick += (_, _) => EditSelected();
        lv.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete) RemoveSelected();
            else if (e.KeyCode == Keys.Enter) EditSelected();
        };
        lv.SelectedIndexChanged += (_, _) => UpdateButtons();
        lv.MouseMove += ItemMouseMove;
        lv.MouseLeave += (_, _) => { if (hoverRow != -1) { hoverRow = -1; lv.Invalidate(); } };
        lv.ItemDrag += ItemDrag;
        lv.DragEnter += (_, e) => { if (AllowedDrop(e)) e.Effect = DragDropEffects.Copy; };
        lv.DragOver += (_, e) => { if (AllowedDrop(e)) e.Effect = DragDropEffects.Copy; };
        lv.DragDrop += ListDragDrop;

        lblStatus = new Label
        {
            Dock = DockStyle.Bottom, Height = 26, ForeColor = p.SubText,
            Font = new Font("Segoe UI", 8.5f), TextAlign = ContentAlignment.MiddleLeft, Tag = "sub",
            Padding = new Padding(2, 0, 0, 0)
        };

        right.Controls.Add(lv);
        right.Controls.Add(lblStatus);
        right.Controls.Add(settingsRow);
        right.Controls.Add(actions);
        right.Controls.Add(head);

        Controls.Add(right);
        Controls.Add(left);
        Controls.Add(top);

        // ---------------- menus ----------------
        BuildFolderMenu();
        BuildItemMenu();

        // ---------------- events ----------------
        cbView.SelectionChanged += (_, _) =>
        {
            if (syncing || Current is not { } fld) return;
            fld.Columns = cbView.SelectedIndex + 1;
            chkNames.Enabled = fld.Columns > 1;
            SaveFolder(fld);
            SyncSettings();
        };
        chkNames.CheckedChanged += (_, _) =>
        {
            if (syncing || Current is not { } fld) return;
            fld.ShowNames = chkNames.Checked;
            SaveFolder(fld);
        };

        pinWatch.Tick += (_, _) =>
        {
            if (!Visible) return;
            bool changed = false;
            foreach (var f in cfg.Folders)
            {
                bool now = Pinning.IsPinned(f);
                if (now != f.PinnedCache)
                {
                    f.PinnedCache = now;
                    changed = true;
                }
            }
            if (changed)
            {
                lbFolders.Invalidate();
                UpdateHeader();
            }
        };

        ThemeManager.Apply(this);
        ApplyOwnColors();
        foreach (var f in cfg.Folders)
        {
            try
            {
                if (!File.Exists(IconBuilder.IcoPath(f))) IconBuilder.BuildIcon(f);
            }
            catch (Exception ex) { ErrorLog.Write(ex); }
            previews[f.Id] = IconBuilder.Render(f, S(64));
        }
        RefreshFolders(cfg.Folders.Count > 0 ? 0 : -1);
        if (!string.IsNullOrEmpty(selectFolderId))
        {
            int idx = cfg.Folders.FindIndex(x => x.Id == selectFolderId);
            if (idx >= 0) lbFolders.SelectedIndex = idx;
        }
        UpdateButtons();
        SetStatus("Tip: drag apps from Explorer or the Start menu onto a folder on the left.");

        Shown += (_, _) =>
        {
            ScaleColumns();
            pinWatch.Start();
            if (openSettings) OpenSettings();
            ShellIntegration.Sync(cfg.Settings.ShellMenu);
        };
        FormClosing += (_, _) => pinWatch.Stop();
        FormClosed += (_, _) =>
        {
            pipeRunning = false;
            try { pipeServer?.Dispose(); } catch { }
            pinWatch.Dispose();
            foreach (var b in previews.Values) b.Dispose();
            foreach (var b in rowIcons) b.Dispose();
            fFolderName.Dispose();
            fMeta.Dispose();
            fHead.Dispose();
            fRowName.Dispose();
            fRowSub.Dispose();
        };
    }

    float Dpi => DeviceDpi / 96f;

    void ScaleColumns()
    {
        lbFolders.ItemHeight = S(58);
        il.ImageSize = new Size(S(28), S(28));
        lv.Columns[0].Width = S(220);
        lv.Columns[1].Width = Math.Max(S(160), lv.ClientSize.Width - S(220) - S(110));
        lv.Columns[2].Width = S(110);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetDarkTitle(Handle, ThemeManager.P.Dark);
        StartPipeServer();
    }

    void StartPipeServer()
    {
        if (pipeThread != null) return;
        pipeRunning = true;
        pipeThread = new Thread(PipeLoop) { IsBackground = true, Name = "TaskbarFolders.ManagerPipe" };
        pipeThread.Start();
    }

    void PipeLoop()
    {
        while (pipeRunning)
        {
            try
            {
                var server = new NamedPipeServerStream(Program.ManagerPipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                pipeServer = server;
                server.WaitForConnection();

                string request;
                using (var reader = new StreamReader(server)) request = reader.ReadToEnd().Trim();
                server.Dispose();
                pipeServer = null;

                if (!pipeRunning) break;
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(new Action(() => ApplyExternalFocus(request)));
            }
            catch
            {
                if (!pipeRunning) break;
                try { pipeServer?.Dispose(); } catch { }
                pipeServer = null;
                Thread.Sleep(120);
            }
        }
    }

    /// <summary>Another instance (or the folder menu's "Manage") asked us to come to the front.</summary>
    void ApplyExternalFocus(string request)
    {
        if (IsDisposed || Disposing) return;
        if (request == "@settings") { OpenSettings(); return; }

        if (!string.IsNullOrEmpty(request))
        {
            int idx = cfg.Folders.FindIndex(x => x.Id == request);
            if (idx >= 0) lbFolders.SelectedIndex = idx;
        }
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        Activate();
        Native.SetForegroundWindow(Handle);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ScaleColumns();
        lbFolders.Invalidate();
    }

    void ApplyOwnColors()
    {
        var p = ThemeManager.P;
        BackColor = p.Bg;
        foreach (Control c in Controls) c.BackColor = p.Bg;
        lblStatus.ForeColor = p.SubText;
        lbFolders.BackColor = p.Panel;
    }

    Folder? Current => lbFolders.SelectedIndex >= 0 && lbFolders.SelectedIndex < cfg.Folders.Count
        ? cfg.Folders[lbFolders.SelectedIndex] : null;

    void SetStatus(string text) => lblStatus.Text = text;

    // ---------------- folder list ----------------

    void RefreshFolders(int select)
    {
        int prev = lbFolders.SelectedIndex;
        lbFolders.BeginUpdate();
        lbFolders.Items.Clear();
        foreach (var f in cfg.Folders) lbFolders.Items.Add(f.Name);
        lbFolders.EndUpdate();

        if (select >= 0 && select < lbFolders.Items.Count) lbFolders.SelectedIndex = select;
        else if (prev >= 0 && prev < lbFolders.Items.Count) lbFolders.SelectedIndex = prev;
        else { FillItems(); SyncSettings(); }
        UpdateButtons();
    }

    void FolderMouseDown(object? sender, MouseEventArgs e)
    {
        int i = lbFolders.IndexFromPoint(e.Location);
        if (e.Button == MouseButtons.Right)
        {
            if (i >= 0) lbFolders.SelectedIndex = i;
            return;
        }
        if (e.Button == MouseButtons.Left)
        {
            dragIndex = i;
            dragOrigin = e.Location;
        }
    }

    void FolderMouseMove(object? sender, MouseEventArgs e)
    {
        int i = lbFolders.IndexFromPoint(e.Location);
        if (i != hoverIndex)
        {
            hoverIndex = i;
            lbFolders.Invalidate();
        }
        if (e.Button != MouseButtons.Left || dragIndex < 0 || dragIndex != i) return;
        if (Math.Abs(e.X - dragOrigin.X) < SystemInformation.DragSize.Width &&
            Math.Abs(e.Y - dragOrigin.Y) < SystemInformation.DragSize.Height) return;
        if (dragIndex >= cfg.Folders.Count) return;

        var f = cfg.Folders[dragIndex];
        dragIndex = -1;
        StartDrag(f, lbFolders);
    }

    void DrawFolder(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= cfg.Folders.Count) return;
        var p = ThemeManager.P;
        var f = cfg.Folders[e.Index];
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        bool sel = (e.State & DrawItemState.Selected) != 0;
        bool hot = e.Index == hoverIndex;

        var row = new RectangleF(2, e.Bounds.Y + 2, e.Bounds.Width - 5, e.Bounds.Height - 5);
        using (var path = Gfx.Round(row, 10))
        {
            using var b = new SolidBrush(sel ? p.Sel : hot ? p.Hover : p.Panel);
            g.FillPath(b, path);
        }

        if (e.Index == dropIndex)
        {
            using var path = Gfx.Round(RectangleF.Inflate(row, -1, -1), 9);
            using var pen = new Pen(p.Accent, 2) { DashStyle = DashStyle.Dash };
            g.DrawPath(pen, path);
        }

        var c = ThemeManager.FolderColor(f);
        using (var path = Gfx.Round(new RectangleF(row.X + 6, row.Y + 11, 4, row.Height - 22), 2))
        using (var b = new SolidBrush(c))
            g.FillPath(b, path);

        if (previews.TryGetValue(f.Id, out var bmp))
        {
            int sz = S(36);
            g.DrawImage(bmp, row.X + 16, row.Y + (row.Height - sz) / 2f, sz, sz);
        }

        bool pinned = Pinning.IsPinned(f);
        int textX = (int)row.X + S(62);
        int textW = (int)row.Width - S(70);
        TextRenderer.DrawText(g, f.Name, fFolderName,
            new Rectangle(textX, (int)row.Y + 7, textW, 22), p.Text,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var meta = $"{f.Items.Count} app{(f.Items.Count == 1 ? "" : "s")} · {LayoutName(f)}";
        if (pinned) meta += "  ·  Pinned";
        TextRenderer.DrawText(g, meta, fMeta,
            new Rectangle(textX, (int)row.Y + 29, textW, 18),
            pinned ? p.Accent : p.SubText, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    static string LayoutName(Folder f) => f.Columns <= 1 ? "list" : $"{f.Columns} columns";

    // ---------------- drag & drop onto folders ----------------

    static bool HasFiles(DragEventArgs e) => e.Data?.GetDataPresent(DataFormats.FileDrop) == true;
    static bool HasItems(DragEventArgs e) => e.Data?.GetDataPresent(ItemDragFormat) == true;
    static bool AllowedDrop(DragEventArgs e) => HasFiles(e) || HasItems(e);

    void FolderDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = !AllowedDrop(e) ? DragDropEffects.None : HasItems(e) ? DragDropEffects.Move : DragDropEffects.Copy;
    }

    void FolderDragOver(object? sender, DragEventArgs e)
    {
        if (!AllowedDrop(e)) { e.Effect = DragDropEffects.None; return; }
        var pt = lbFolders.PointToClient(new Point(e.X, e.Y));
        int idx = lbFolders.IndexFromPoint(pt);
        if (idx != dropIndex)
        {
            dropIndex = idx;
            lbFolders.Invalidate();
        }
        e.Effect = HasItems(e) ? DragDropEffects.Move : DragDropEffects.Copy;
    }

    void FolderDragDrop(object? sender, DragEventArgs e)
    {
        int idx = lbFolders.IndexFromPoint(lbFolders.PointToClient(new Point(e.X, e.Y)));
        dropIndex = -1;
        lbFolders.Invalidate();
        if (idx < 0 || idx >= cfg.Folders.Count) return;
        var target = cfg.Folders[idx];

        if (HasFiles(e) && e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
        {
            AddPaths(target, paths);
            lbFolders.SelectedIndex = idx;
            SetStatus($"Added {paths.Length} item{(paths.Length == 1 ? "" : "s")} to “{target.Name}”.");
        }
        else if (HasItems(e) && e.Data?.GetData(ItemDragFormat) is string payload)
        {
            bool copy = (e.KeyState & 8) != 0; // Ctrl
            if (MoveItems(payload, target, copy))
                SetStatus(copy ? $"Copied apps to “{target.Name}”." : $"Moved apps to “{target.Name}”.");
        }
    }

    // ---------------- item list ----------------

    void FillItems()
    {
        if (lv == null) return;
        var f = Current;
        var filter = txtSearch?.Text?.Trim() ?? "";
        lv.BeginUpdate();
        lv.Items.Clear();
        il.Images.Clear();
        foreach (var oldIcon in rowIcons) oldIcon.Dispose();
        rowIcons.Clear();
        if (f != null)
        {
            int imageIndex = 0;
            for (int i = 0; i < f.Items.Count; i++)
            {
                var it = f.Items[i];
                if (filter.Length > 0 &&
                    !it.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) &&
                    !it.Target.Contains(filter, StringComparison.CurrentCultureIgnoreCase)) continue;

                var ic = IconUtil.Get(it.Target);
                rowIcons.Add(ic);
                il.Images.Add(ic);
                var row = new ListViewItem(it.Name, imageIndex++) { Tag = i };
                row.SubItems.Add(it.Target);
                row.SubItems.Add(it.Args);
                lv.Items.Add(row);
            }
        }
        lv.EndUpdate();
        UpdateButtons();
        UpdateHeader();
    }

    void UpdateHeader()
    {
        var f = Current;
        if (f == null)
        {
            lblName.Text = "Choose a folder";
            lblMeta.Text = "Create a folder, then add apps from your Start menu or drag them in.";
            picFolder.Image = null;
            return;
        }
        lblName.Text = f.Name;
        bool pinned = Pinning.IsPinned(f);
        lblMeta.Text = $"{f.Items.Count} app{(f.Items.Count == 1 ? "" : "s")}  ·  {LayoutName(f)}" +
                       (pinned ? "  ·  Pinned to taskbar" : "  ·  Not pinned");
        var old = picFolder.Image;
        picFolder.Image = IconBuilder.Render(f, S(52));
        old?.Dispose();
        btnPin.Text = pinned ? "Pinned ✓" : "Pin to taskbar";
        btnPin.Primary = !pinned;
        btnPin.Invalidate();
    }

    void UpdateButtons()
    {
        var f = Current;
        bool on = f != null;
        btnAdd.Enabled = btnStart.Enabled = btnColor.Enabled = btnIcon.Enabled = btnPin.Enabled = on;
        btnScan.Enabled = true;
        cbView.Enabled = chkNames.Enabled = on;
    }

    int hoverRow = -1;

    void ItemMouseMove(object? sender, MouseEventArgs e)
    {
        int row = lv.GetItemAt(e.X, e.Y)?.Index ?? -1;
        if (row != hoverRow) { hoverRow = row; lv.Invalidate(); }
    }

    void DrawItemHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        var p = ThemeManager.P;
        using var b = new SolidBrush(p.Card);
        e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(e.Graphics, e.Header!.Text, fHead, e.Bounds,
            p.SubText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                       TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        using var pen = new Pen(p.Border);
        e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
    }

    void DrawItemRow(object? sender, DrawListViewItemEventArgs e)
    {
        var p = ThemeManager.P;
        var bg = e.Item!.Selected ? p.Sel : e.ItemIndex == hoverRow ? p.Hover : p.Panel;
        using var b = new SolidBrush(bg);
        e.Graphics.FillRectangle(b, new Rectangle(0, e.Bounds.Top, lv.ClientSize.Width, e.Bounds.Height));
    }

    void DrawItemSub(object? sender, DrawListViewSubItemEventArgs e)
    {
        var p = ThemeManager.P;
        var item = e.Item!;

        if (e.ColumnIndex == 0)
        {
            if (item.ImageIndex >= 0 && item.ImageIndex < il.Images.Count)
            {
                int sz = S(28);
                e.Graphics.DrawImage(il.Images[item.ImageIndex], e.Bounds.Left + S(8),
                    e.Bounds.Top + (e.Bounds.Height - sz) / 2, sz, sz);
            }
        }

        var text = e.SubItem?.Text ?? "";
        var fg = e.ColumnIndex == 0 ? p.Text : p.SubText;
        var r = new Rectangle(e.Bounds.Left + (e.ColumnIndex == 0 ? S(44) : S(8)), e.Bounds.Top,
            e.Bounds.Width - (e.ColumnIndex == 0 ? S(52) : S(12)), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, text, e.ColumnIndex == 0 ? fRowName : fRowSub,
            r, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    // ---------------- folder actions ----------------

    void NewFolder()
    {
        var name = Prompt.Show(this, "New folder", "Folder name:", "New folder");
        if (name == null) return;
        var f = new Folder { Name = UniqueName(name) };
        cfg.Folders.Add(f);
        SaveFolder(f);
        RefreshFolders(cfg.Folders.Count - 1);
        SetStatus($"Created “{f.Name}”. Add apps with the buttons above.");
    }

    /// <summary>Folder names must be unique: the pinned shortcut is named after the folder.</summary>
    string UniqueName(string name, Folder? except = null)
    {
        var candidate = name;
        int i = 2;
        while (cfg.Folders.Any(x => !ReferenceEquals(x, except) &&
                                    string.Equals(x.Name, candidate, StringComparison.CurrentCultureIgnoreCase)))
            candidate = $"{name} ({i++})";
        return candidate;
    }

    void RenameFolder()
    {
        var f = Current;
        if (f == null) return;
        var name = Prompt.Show(this, "Rename folder", "Folder name:", f.Name);
        if (name == null || name == f.Name) return;
        int idx = lbFolders.SelectedIndex;
        bool wasPinned = Pinning.IsPinned(f);
        try { File.Delete(Pinning.LnkPath(f)); } catch { }
        f.Name = UniqueName(name, f);
        SaveFolder(f);
        RefreshFolders(idx);
        if (wasPinned)
            SetStatus("Folder renamed. The old pinned button still exists — unpin it from the taskbar and pin again.");
    }

    void DeleteFolder()
    {
        var f = Current;
        if (f == null) return;
        bool pinned = Pinning.IsPinned(f);
        var msg = $"Delete folder “{f.Name}”?" +
                  (pinned ? "\n\nThe pinned taskbar button stays behind — right-click it and unpin." : "");
        if (MessageBox.Show(this, msg, "Delete folder", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        int idx = lbFolders.SelectedIndex;
        cfg.Folders.Remove(f);
        cfg.Save();
        try { File.Delete(IconBuilder.IcoPath(f)); } catch { }
        try { File.Delete(Pinning.LnkPath(f)); } catch { }
        try { foreach (var p in Directory.GetFiles(Config.CustomDir, f.Id + ".*")) File.Delete(p); } catch { }
        if (previews.Remove(f.Id, out var bmp)) bmp.Dispose();
        FillItems();
        RefreshFolders(Math.Min(idx, cfg.Folders.Count - 1));
        SetStatus($"Deleted “{f.Name}”.");
    }

    void ChangeIcon()
    {
        var f = Current;
        if (f == null) return;
        if (!IconPicker.Show(this, f)) return;
        SaveFolder(f);
        UpdateHeader();
    }

    void ChangeColor()
    {
        var f = Current;
        if (f == null) return;
        if (!ColorPicker.Show(this, f)) return;
        SaveFolder(f);
        UpdateHeader();
        SetStatus(f.Color.Length == 0 ? "Colour reset to automatic." : "Folder colour updated.");
    }

    // ---------------- item actions ----------------

    void ScanPc()
    {
        int before = cfg.Folders.Count;
        using var scan = new ScanForm(cfg, Current?.Id);
        scan.ShowDialog(this);
        if (!scan.Changed) return;

        foreach (var b in previews.Values) b.Dispose();
        previews.Clear();
        foreach (var f in cfg.Folders)
        {
            try
            {
                if (!File.Exists(IconBuilder.IcoPath(f))) IconBuilder.BuildIcon(f);
            }
            catch (Exception ex) { ErrorLog.Write(ex); }
            previews[f.Id] = IconBuilder.Render(f, S(64));
        }

        int idx = Math.Clamp(lbFolders.SelectedIndex, 0, Math.Max(0, cfg.Folders.Count - 1));
        RefreshFolders(cfg.Folders.Count > 0 ? idx : -1);
        FillItems();
        UpdateHeader();
        SetStatus($"Scan finished — {Math.Max(0, cfg.Folders.Count - before)} new folder(s), {cfg.Folders.Sum(f => f.Items.Count)} apps in total.");
    }

    void AddDialog()
    {
        var f = Current;
        if (f == null)
        {
            MessageBox.Show(this, "Create or select a folder first.", "Taskbar Folders");
            return;
        }
        using var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = $"Add apps to “{f.Name}”",
            Filter = "Programs and shortcuts|*.exe;*.lnk;*.bat;*.cmd;*.url|All files|*.*"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) AddPaths(f, dlg.FileNames);
    }

    void AddFromStartMenu()
    {
        var f = Current;
        if (f == null) return;
        using var picker = new StartMenuPicker(StartMenuScanner.Scan());
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        AddPaths(f, picker.SelectedTargets);
        SetStatus($"Added {picker.SelectedTargets.Count} app{(picker.SelectedTargets.Count == 1 ? "" : "s")} from the Start menu.");
    }

    void AddPaths(Folder f, IEnumerable<string> paths)
    {
        int added = 0;
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var path = raw.Trim().Trim('"');
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
            added++;
        }
        if (added == 0)
        {
            SetStatus("Nothing new was added (already in this folder?).");
            return;
        }
        SaveFolder(f);
        FillItems();
        UpdateHeader();
    }

    void EditSelected()
    {
        var f = Current;
        if (f == null || lv.SelectedItems.Count == 0) return;
        int index = (int)(lv.SelectedItems[0].Tag ?? 0);
        if (index < 0 || index >= f.Items.Count) return;
        var it = f.Items[index];
        var name = Prompt.Show(this, "Edit app", "Display name:", it.Name);
        if (name == null) return;
        it.Name = name;
        var args = Prompt.Show(this, "Edit app", "Arguments (optional — cancel to keep):", it.Args);
        if (args != null) it.Args = args;
        SaveFolder(f);
        FillItems();
    }

    void RemoveSelected()
    {
        var f = Current;
        if (f == null) return;
        var indices = lv.SelectedItems.Cast<ListViewItem>()
            .Select(x => (int)(x.Tag ?? 0)).Distinct().OrderByDescending(x => x).ToList();
        if (indices.Count == 0) return;
        foreach (var i in indices)
            if (i >= 0 && i < f.Items.Count) f.Items.RemoveAt(i);
        SaveFolder(f);
        FillItems();
    }

    void MoveSelected(int delta)
    {
        var f = Current;
        if (f == null || lv.SelectedIndices.Count != 1) return;
        int i = (int)(lv.SelectedItems[0].Tag ?? 0);
        int j = i + delta;
        if (j < 0 || j >= f.Items.Count) return;
        (f.Items[i], f.Items[j]) = (f.Items[j], f.Items[i]);
        SaveFolder(f);
        FillItems();
        if (j < lv.Items.Count) lv.Items[j].Selected = true;
    }

    void ItemDrag(object? sender, ItemDragEventArgs e)
    {
        var f = Current;
        if (f == null || lv.SelectedItems.Count == 0) return;
        var indices = lv.SelectedItems.Cast<ListViewItem>()
            .Select(x => (int)(x.Tag ?? 0)).OrderBy(x => x).ToArray();
        var payload = f.Id + "|" + string.Join(",", indices);
        lv.DoDragDrop(new DataObject(ItemDragFormat, payload), DragDropEffects.Move | DragDropEffects.Copy);
    }

    bool MoveItems(string payload, Folder target, bool copy)
    {
        var parts = payload.Split('|', 2);
        if (parts.Length != 2) return false;
        var src = cfg.Find(parts[0]);
        if (src == null) return false;
        var indices = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var v) ? v : -1)
            .Where(v => v >= 0 && v < src.Items.Count)
            .OrderBy(v => v)
            .ToList();
        if (indices.Count == 0) return false;
        if (src == target && copy) return false;

        var moving = indices.Select(i => src.Items[i]).ToList();
        if (copy)
        {
            foreach (var it in moving)
                target.Items.Add(new Item { Name = it.Name, Target = it.Target, Args = it.Args });
        }
        else
        {
            foreach (var i in indices.OrderByDescending(v => v)) src.Items.RemoveAt(i);
            foreach (var it in moving) target.Items.Add(it);
        }

        SaveFolder(src);
        if (target != src) SaveFolder(target);
        FillItems();
        lbFolders.Invalidate();
        return true;
    }

    void ListDragDrop(object? sender, DragEventArgs e)
    {
        var f = Current;
        if (f == null) return;
        if (HasFiles(e) && e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
        {
            AddPaths(f, paths);
        }
        else if (HasItems(e) && e.Data?.GetData(ItemDragFormat) is string payload)
        {
            MoveItems(payload, f, (e.KeyState & 8) != 0);
        }
    }

    // ---------------- taskbar ----------------

    async void StartDrag(Folder f, Control source)
    {
        string lnk;
        try { lnk = Pinning.EnsureShortcut(f); }
        catch (Exception ex) { ErrorLog.Write(ex); return; }

        SetStatus($"Drop “{f.Name}” on empty taskbar space to pin it.");
        source.DoDragDrop(new DataObject(DataFormats.FileDrop, new[] { lnk }),
            DragDropEffects.Copy | DragDropEffects.Link);

        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(250);
            if (Pinning.IsPinned(f)) break;
        }
        lbFolders.Invalidate();
        UpdateHeader();
        if (Pinning.IsPinned(f)) SetStatus($"“{f.Name}” is pinned to the taskbar.");
    }

    void PinCurrent()
    {
        var f = Current;
        if (f == null) return;

        if (Pinning.IsPinned(f))
        {
            MessageBox.Show(this,
                "This folder is already pinned.\n\nTo remove it, right-click its taskbar icon and choose “Unpin from taskbar”.",
                "Taskbar Folders", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var lnk = Pinning.EnsureShortcut(f);

            // Windows 11 refuses programmatic pinning: open the shortcut in Explorer, selected,
            // and show the one-step instruction. (The old "taskbarpin" verb just launched the folder.)
            Pinning.ShowInExplorer(lnk);
            SetStatus("In Explorer: right-click the selected shortcut › Show more options › Pin to taskbar.");

            if (pinHelper is { IsDisposed: false })
            {
                pinHelper.Activate();
                return;
            }
            pinHelper = new PinHelper(f, lnk);
            pinHelper.FormClosed += (_, _) =>
            {
                pinHelper = null;
                lbFolders.Invalidate();
                UpdateHeader();
            };
            pinHelper.Show(this);
            SetStatus("Right-click the selected shortcut in Explorer › Show more options › Pin to taskbar (or drag the icon).");
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            MessageBox.Show(this, "Couldn't create the shortcut:\n" + ex.Message, "Taskbar Folders",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ---------------- persistence ----------------

    void SaveFolder(Folder f)
    {
        cfg.Save();
        try { IconBuilder.BuildIcon(f); } catch (Exception ex) { ErrorLog.Write(ex); }
        if (previews.TryGetValue(f.Id, out var old)) old.Dispose();
        previews[f.Id] = IconBuilder.Render(f, S(64));
        lbFolders.Invalidate();
        UpdateHeader();
    }

    void SyncSettings()
    {
        var f = Current;
        syncing = true;
        cbView.Enabled = chkNames.Enabled = f != null;
        if (f != null)
        {
            cbView.SelectedIndex = Math.Clamp(f.Columns, 1, 4) - 1;
            chkNames.Checked = f.ShowNames;
            chkNames.Enabled = f.Columns > 1;
        }
        else
        {
            cbView.SelectedIndex = 0;
        }
        syncing = false;
    }

    void OpenSettings()
    {
        using var dlg = new SettingsForm(cfg);
        dlg.ShowDialog(this);
        ThemeManager.Init(cfg);
        ThemeManager.Apply(this);
        ApplyOwnColors();
        lbFolders.BackColor = ThemeManager.P.Panel;
        lv.Invalidate();
        lbFolders.Invalidate();
        UpdateHeader();
        SyncSettings();
        Refresh();
    }

    // ---------------- menus ----------------

    void StyleMenu(ContextMenuStrip menu)
    {
        var p = ThemeManager.P;
        menu.BackColor = p.Panel;
        menu.ForeColor = p.Text;
        menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges = false };
        menu.ShowImageMargin = false;
    }

    void BuildFolderMenu()
    {
        folderMenu.Items.Clear();
        folderMenu.Items.Add("Preview menu", null, (_, _) => PreviewMenu());
        folderMenu.Items.Add("Pin to taskbar", null, (_, _) => PinCurrent());
        folderMenu.Items.Add("Show shortcut in Explorer", null, (_, _) => ShowShortcut());
        folderMenu.Items.Add(new ToolStripSeparator());
        folderMenu.Items.Add("Change colour…", null, (_, _) => ChangeColor());
        folderMenu.Items.Add("Change icon…", null, (_, _) => ChangeIcon());

        var anim = new ToolStripMenuItem("Open animation");
        foreach (var (id, name) in new[]
                 {
                     ("", "Default (from Settings)"), ("slidefade", "Slide + fade"), ("slide", "Slide up"),
                     ("fade", "Fade"), ("zoom", "Zoom"), ("none", "None")
                 })
        {
            var item = new ToolStripMenuItem(name) { Tag = id };
            item.Click += (_, _) =>
            {
                if (Current is not { } f) return;
                f.Animation = (string)item.Tag!;
                cfg.Save();
                SetStatus("Animation saved for this folder.");
            };
            anim.DropDownItems.Add(item);
        }
        folderMenu.Items.Add(anim);

        folderMenu.Items.Add(new ToolStripSeparator());
        folderMenu.Items.Add("Rename", null, (_, _) => RenameFolder());
        folderMenu.Items.Add("Delete", null, (_, _) => DeleteFolder());

        folderMenu.Opening += (_, e) =>
        {
            e.Cancel = Current == null;
            var f = Current;
            if (f == null) return;
            anim.Enabled = true;
            foreach (ToolStripMenuItem mi in anim.DropDownItems)
                mi.Checked = string.Equals((string?)mi.Tag, f.Animation, StringComparison.OrdinalIgnoreCase);
        };
        StyleMenu(folderMenu);
        lbFolders.ContextMenuStrip = folderMenu;
    }

    void BuildItemMenu()
    {
        itemMenu.Items.Clear();
        itemMenu.Items.Add("Launch", null, (_, _) => LaunchSelected(false));
        itemMenu.Items.Add("Run as administrator", null, (_, _) => LaunchSelected(true));
        itemMenu.Items.Add("Open file location", null, (_, _) => OpenSelectedLocation());
        itemMenu.Items.Add(new ToolStripSeparator());
        itemMenu.Items.Add("Edit name / arguments…", null, (_, _) => EditSelected());
        itemMenu.Items.Add("Move up", null, (_, _) => MoveSelected(-1));
        itemMenu.Items.Add("Move down", null, (_, _) => MoveSelected(1));
        itemMenu.Items.Add("Remove", null, (_, _) => RemoveSelected());
        itemMenu.Items.Add(new ToolStripSeparator());

        var move = new ToolStripMenuItem("Move to folder");
        itemMenu.Items.Add(move);
        move.DropDownOpening += (_, _) =>
        {
            move.DropDownItems.Clear();
            var cur = Current;
            foreach (var f in cfg.Folders)
            {
                var target = f;
                var mi = new ToolStripMenuItem(target.Name) { Enabled = target != cur };
                mi.Click += (_, _) =>
                {
                    var src = Current;
                    if (src == null || src == target) return;
                    var items = lv.SelectedItems.Cast<ListViewItem>()
                        .Select(x => (int)(x.Tag ?? 0)).Where(i => i >= 0 && i < src.Items.Count)
                        .OrderByDescending(i => i).ToList();
                    foreach (var i in items)
                    {
                        var it = src.Items[i];
                        src.Items.RemoveAt(i);
                        target.Items.Add(it);
                    }
                    SaveFolder(src);
                    SaveFolder(target);
                    FillItems();
                };
                move.DropDownItems.Add(mi);
            }
        };

        itemMenu.Opening += (_, e) =>
        {
            bool any = lv.SelectedItems.Count > 0 && Current != null;
            foreach (ToolStripItem i in itemMenu.Items) i.Enabled = any || i is ToolStripSeparator;
            move.Enabled = any;
        };
        StyleMenu(itemMenu);
        lv.ContextMenuStrip = itemMenu;
        lv.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var hit = lv.GetItemAt(e.X, e.Y);
            if (hit == null) return;
            if (!hit.Selected)
            {
                lv.SelectedItems.Clear();
                hit.Selected = true;
            }
        };
    }

    void LaunchSelected(bool admin)
    {
        var f = Current;
        if (f == null || lv.SelectedItems.Count == 0) return;
        int idx = (int)(lv.SelectedItems[0].Tag ?? 0);
        if (idx < 0 || idx >= f.Items.Count) return;
        var it = f.Items[idx];
        try
        {
            var target = Environment.ExpandEnvironmentVariables(it.Target);
            var psi = new ProcessStartInfo(target) { UseShellExecute = true, Arguments = it.Args };
            if (File.Exists(target)) psi.WorkingDirectory = System.IO.Path.GetDirectoryName(target) ?? "";
            if (admin) psi.Verb = "runas";
            Process.Start(psi);
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    void OpenSelectedLocation()
    {
        var f = Current;
        if (f == null || lv.SelectedItems.Count == 0) return;
        int idx = (int)(lv.SelectedItems[0].Tag ?? 0);
        if (idx < 0 || idx >= f.Items.Count) return;
        var path = Environment.ExpandEnvironmentVariables(f.Items[idx].Target);
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    void PreviewMenu()
    {
        var f = Current;
        if (f == null) return;
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--open {f.Id}") { UseShellExecute = true });
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    void ShowShortcut()
    {
        var f = Current;
        if (f == null) return;
        try { Pinning.ShowInExplorer(Pinning.EnsureShortcut(f)); }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }
}


