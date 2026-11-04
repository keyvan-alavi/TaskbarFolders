using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace TaskbarFolders;

static class Prompt
{
    public static string? Show(IWin32Window owner, string title, string label, string def = "")
    {
        using var f = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, ClientSize = new Size(400, 132),
            Font = new Font("Segoe UI", 9.5f), AutoScaleMode = AutoScaleMode.Dpi,
            BackColor = ThemeManager.P.Bg, ForeColor = ThemeManager.P.Text
        };
        var l = new Label { Text = label, Left = 16, Top = 16, Width = 368, AutoSize = false, Height = 20 };
        var tb = new TextBox { Left = 16, Top = 44, Width = 368, Text = def };
        var ok = new FlatButton { Text = "OK", Left = 216, Top = 86, Width = 80, Height = 32, Primary = true, DialogResult = DialogResult.OK };
        var cancel = new FlatButton { Text = "Cancel", Left = 304, Top = 86, Width = 80, Height = 32, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { l, tb, ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        ThemeManager.Apply(f);
        l.ForeColor = ThemeManager.P.Text;
        tb.BackColor = ThemeManager.P.Card;
        tb.ForeColor = ThemeManager.P.Text;
        f.HandleCreated += (_, _) => Native.SetDarkTitle(f.Handle, ThemeManager.P.Dark);
        tb.SelectAll();
        return f.ShowDialog(owner) == DialogResult.OK && tb.Text.Trim().Length > 0 ? tb.Text.Trim() : null;
    }
}

/// <summary>Dialog for choosing a folder's icon: preset emoji, typed emoji/letters, or an image file.</summary>
static class IconPicker
{
    // Presets as \U escapes so the file encoding never matters.
    static readonly string[] Presets =
    {
        "\U0001F4C1", // folder
        "\U0001F4BB", // laptop
        "\U0001F6E0", // tools
        "\U0001F3AE", // game controller
        "\U0001F3B5", // music
        "\U0001F3AC", // clapper
        "\U0001F310", // globe
        "\U0001F4F7", // camera
        "\U0001F4DD", // memo
        "⚙",     // gear
        "\U0001F680", // rocket
        "\U0001F512", // lock
        "⭐",     // star
        "\U0001F4AC", // speech bubble
        "\U0001F4CA", // chart
        "\U0001F9EA", // test tube
    };

    /// <summary>Returns true (and updates the folder's IconKind/IconValue) when the user pressed OK.</summary>
    public static bool Show(IWin32Window owner, Folder f)
    {
        var p = ThemeManager.P;
        string kind = f.IconKind, value = f.IconValue;

        using var form = new Form
        {
            Text = "Folder icon", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, ClientSize = new Size(470, 420),
            Font = new Font("Segoe UI", 9.5f), AutoScaleMode = AutoScaleMode.Dpi,
            BackColor = p.Bg, ForeColor = p.Text
        };

        var lblPresets = new Label { Text = "Presets", Left = 16, Top = 12, Width = 200 };
        var flow = new FlowLayoutPanel { Left = 16, Top = 36, Width = 438, Height = 112 };

        var btnAuto = new FlatButton { Text = "Automatic (grid of the folder's apps)", Left = 16, Top = 160, Width = 310, Height = 34 };

        var lblCustom = new Label { Text = "Custom emoji or letters:", Left = 16, Top = 206, Width = 300 };
        var tb = new TextBox { Left = 16, Top = 230, Width = 120, MaxLength = 8 };
        var lblTip = new Label { Text = "Tip: press Win + . for the emoji panel", Left = 148, Top = 234, Width = 190, Height = 40, ForeColor = p.SubText };

        var btnFile = new FlatButton { Text = "Choose image file…  (PNG, ICO, JPG)", Left = 16, Top = 280, Width = 310, Height = 34 };
        var lblNote = new Label
        {
            Text = "Emoji are drawn as single-colour glyphs. Use an image file if you want full colour.",
            Left = 16, Top = 322, Width = 310, Height = 44, ForeColor = p.SubText
        };

        var preview = new PictureBox { Left = 344, Top = 160, Size = new Size(100, 100), SizeMode = PictureBoxSizeMode.Zoom };
        var lblPreview = new Label { Text = "Preview", Left = 344, Top = 264, Width = 100, ForeColor = p.SubText };

        var ok = new FlatButton { Text = "OK", Left = 286, Top = 376, Width = 80, Height = 32, Primary = true, DialogResult = DialogResult.OK };
        var cancel = new FlatButton { Text = "Cancel", Left = 374, Top = 376, Width = 80, Height = 32, DialogResult = DialogResult.Cancel };

        void UpdatePreview()
        {
            var tmp = new Folder
            {
                Id = f.Id, Name = f.Name, Items = f.Items,
                IconKind = kind, IconValue = value, Color = f.Color
            };
            var old = preview.Image;
            preview.Image = IconBuilder.Render(tmp, 100);
            old?.Dispose();
        }

        foreach (var glyph in Presets)
        {
            var g = glyph;
            var b = new FlatButton
            {
                Text = g, Width = 46, Height = 46, Margin = new Padding(3),
                Font = new Font("Segoe UI Emoji", 15f), Radius = 8
            };
            b.Click += (_, _) => { tb.Text = g; };
            flow.Controls.Add(b);
        }

        tb.TextChanged += (_, _) =>
        {
            var t = tb.Text.Trim();
            if (t.Length == 0) return;
            kind = "emoji";
            value = t;
            UpdatePreview();
        };

        btnAuto.Click += (_, _) =>
        {
            tb.Text = "";
            kind = "auto";
            value = "";
            UpdatePreview();
        };

        btnFile.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Choose an icon image",
                Filter = "Images and icons|*.png;*.ico;*.jpg;*.jpeg;*.bmp|All files|*.*"
            };
            if (dlg.ShowDialog(form) != DialogResult.OK) return;

            using var test = IconBuilder.LoadImage(dlg.FileName);
            if (test == null)
            {
                MessageBox.Show(form, "That file couldn't be read as an image.", "Folder icon",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            tb.Text = "";
            kind = "file";
            value = dlg.FileName;
            UpdatePreview();
        };

        form.Controls.AddRange(new Control[]
        {
            lblPresets, flow, btnAuto, lblCustom, tb, lblTip, btnFile, lblNote, preview, lblPreview, ok, cancel
        });
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        ThemeManager.Apply(form);
        form.HandleCreated += (_, _) => Native.SetDarkTitle(form.Handle, p.Dark);
        UpdatePreview();

        bool accepted = form.ShowDialog(owner) == DialogResult.OK;
        preview.Image?.Dispose();
        if (!accepted) return false;

        if (kind == "file")
        {
            try { value = Persist(f, value); }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
                MessageBox.Show(owner, "Couldn't copy the image:\n" + ex.Message, "Folder icon",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }
        else CleanupCustom(f, null);

        f.IconKind = kind;
        f.IconValue = value;
        return true;
    }

    /// <summary>Copies the chosen image into %AppData%\TaskbarFolders\custom so it survives the original being moved.</summary>
    static string Persist(Folder f, string src)
    {
        Directory.CreateDirectory(Config.CustomDir);
        var dest = System.IO.Path.Combine(Config.CustomDir, f.Id + System.IO.Path.GetExtension(src).ToLowerInvariant());
        if (!string.Equals(System.IO.Path.GetFullPath(src), System.IO.Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            File.Copy(src, dest, true);
        CleanupCustom(f, dest);
        return dest;
    }

    internal static void CleanupCustom(Folder f, string? keep)
    {
        try
        {
            if (!Directory.Exists(Config.CustomDir)) return;
            foreach (var p in Directory.GetFiles(Config.CustomDir, f.Id + ".*"))
                if (keep == null || !string.Equals(p, keep, StringComparison.OrdinalIgnoreCase))
                    File.Delete(p);
        }
        catch { }
    }
}

/// <summary>Colour swatch used by the folder-colour dialog.</summary>
sealed class ColorSwatch : Control
{
    public Color Swatch = Color.White;
    public bool Checked;

    public ColorSwatch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(40, 40);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? ThemeManager.P.Bg);

        var r = new RectangleF(4, 4, Width - 9, Height - 9);
        using var path = Gfx.Round(r, 9);
        using var b = new SolidBrush(Swatch);
        g.FillPath(b, path);

        using var border = new Pen(Checked ? ThemeManager.P.Text : Color.FromArgb(60, 255, 255, 255), Checked ? 2.5f : 1f);
        g.DrawPath(border, path);

        if (Checked)
        {
            using var pen = new Pen(Swatch.IsLightColor() ? Color.Black : Color.White, 2.2f)
            {
                StartCap = LineCap.Round, EndCap = LineCap.Round
            };
            g.DrawLines(pen, new[]
            {
                new PointF(Width * 0.30f, Height * 0.52f),
                new PointF(Width * 0.45f, Height * 0.66f),
                new PointF(Width * 0.70f, Height * 0.36f)
            });
        }
    }
}

/// <summary>Choose a folder colour: theme automatic, a preset, or any custom colour.</summary>
static class ColorPicker
{
    static readonly Color[] Presets =
    {
        Color.FromArgb(232, 93, 117), Color.FromArgb(255, 138, 76), Color.FromArgb(242, 184, 75),
        Color.FromArgb(123, 201, 111), Color.FromArgb(38, 199, 168), Color.FromArgb(76, 194, 255),
        Color.FromArgb(64, 132, 255), Color.FromArgb(150, 104, 255), Color.FromArgb(228, 92, 208),
        Color.FromArgb(169, 116, 82), Color.FromArgb(124, 135, 152), Color.FromArgb(255, 90, 90),
    };

    public static bool Show(IWin32Window? owner, Folder f)
    {
        var p = ThemeManager.P;
        string selected = f.Color ?? "";

        using var form = new Form
        {
            Text = "Folder colour", FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false, ClientSize = new Size(500, 440), Font = new Font("Segoe UI", 9.5f),
            AutoScaleMode = AutoScaleMode.Dpi, BackColor = p.Bg, ForeColor = p.Text
        };

        var title = new Label
        {
            Text = "Choose a colour", Left = 18, Top = 14, Width = 300,
            Font = new Font("Segoe UI Semibold", 12f)
        };
        var sub = new Label
        {
            Text = "The colour is used for the folder icon and its taskbar button.",
            Left = 18, Top = 42, Width = 320, Height = 34, ForeColor = p.SubText
        };

        var flow = new FlowLayoutPanel { Left = 16, Top = 82, Width = 340, Height = 176 };
        var swatches = new List<ColorSwatch>();

        var preview = new PictureBox { Left = 368, Top = 78, Size = new Size(110, 110), SizeMode = PictureBoxSizeMode.Zoom };
        var previewLabel = new Label { Left = 368, Top = 196, Width = 110, Text = "Preview", ForeColor = p.SubText };

        var auto = new FlatButton { Text = "Automatic (theme colour)", Left = 18, Top = 268, Width = 220, Height = 34 };
        var custom = new FlatButton { Text = "Custom colour…", Left = 248, Top = 268, Width = 152, Height = 34 };
        var hex = new Label { Left = 18, Top = 312, Width = 340, Height = 22, ForeColor = p.SubText };

        var ok = new FlatButton { Text = "OK", Left = 306, Top = 388, Width = 80, Height = 34, Primary = true, DialogResult = DialogResult.OK };
        var cancel = new FlatButton { Text = "Cancel", Left = 396, Top = 388, Width = 80, Height = 34, DialogResult = DialogResult.Cancel };

        void Refresh()
        {
            foreach (var s in swatches) s.Checked = selected.Length > 0 && ColorEx.ToHex(s.Swatch).Equals(selected, StringComparison.OrdinalIgnoreCase);
            foreach (var s in swatches) s.Invalidate();

            var tmp = new Folder
            {
                Id = f.Id, Name = f.Name, Items = f.Items, IconKind = f.IconKind, IconValue = f.IconValue,
                Color = selected
            };
            var old = preview.Image;
            preview.Image = IconBuilder.Render(tmp, 110);
            old?.Dispose();
            hex.Text = selected.Length == 0 ? "Automatic — follows the active theme" : selected.ToUpperInvariant();
            auto.Primary = selected.Length == 0;
            auto.Invalidate();
        }

        foreach (var c in Presets)
        {
            var s = new ColorSwatch { Swatch = c, Margin = new Padding(3) };
            s.Click += (_, _) => { selected = ColorEx.ToHex(c); Refresh(); };
            swatches.Add(s);
            flow.Controls.Add(s);
        }

        auto.Click += (_, _) => { selected = ""; Refresh(); };
        custom.Click += (_, _) =>
        {
            using var dlg = new ColorDialog
            {
                FullOpen = true,
                Color = ColorEx.Parse(selected, ThemeManager.AutoTint(f)),
                AnyColor = true
            };
            if (dlg.ShowDialog(form) != DialogResult.OK) return;
            selected = ColorEx.ToHex(dlg.Color);
            Refresh();
        };

        form.Controls.AddRange(new Control[] { title, sub, flow, preview, previewLabel, auto, custom, hex, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        ThemeManager.Apply(form);
        form.HandleCreated += (_, _) => Native.SetDarkTitle(form.Handle, p.Dark);
        Refresh();

        bool accepted = form.ShowDialog(owner) == DialogResult.OK;
        preview.Image?.Dispose();
        if (!accepted) return false;
        f.Color = selected;
        return true;
    }
}

/// <summary>
/// Shown when Windows doesn't pin silently: a draggable icon plus instructions.
/// It watches the pinned-shortcuts folder and closes itself once the pin appears.
/// </summary>
class PinHelper : Form
{
    readonly Folder folder;
    readonly string lnk;
    readonly System.Windows.Forms.Timer watch = new() { Interval = 600 };
    readonly Label status;
    readonly PictureBox pic;
    Point down;

    public PinHelper(Folder f, string lnkPath)
    {
        folder = f;
        lnk = lnkPath;
        var p = ThemeManager.P;

        Text = "Pin to taskbar";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(560, 342);
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = p.Bg;
        ForeColor = p.Text;

        pic = new PictureBox
        {
            Left = 24, Top = 28, Size = new Size(92, 92), SizeMode = PictureBoxSizeMode.Zoom,
            Image = IconBuilder.Render(f, 92), Cursor = Cursors.Hand
        };
        var title = new Label
        {
            Left = 136, Top = 22, Width = 396, Height = 28,
            Text = "Drag this icon onto the taskbar",
            Font = new Font("Segoe UI Semibold", 12.5f)
        };
        var msg = new Label
        {
            Left = 136, Top = 56, Width = 396, Height = 64,
            Text = "Windows 11 doesn't let apps pin themselves, but one gesture is enough: " +
                   "drag the icon onto an empty spot on the taskbar.",
            ForeColor = p.SubText
        };
        var alt = new Label
        {
            Left = 136, Top = 124, Width = 396, Height = 24,
            Text = "Or: right-click the shortcut › Show more options › Pin to taskbar.",
            ForeColor = p.SubText, Font = new Font("Segoe UI", 8.5f)
        };
        status = new Label
        {
            Left = 24, Top = 168, Width = 512, Height = 26,
            Text = "Waiting for the pin to appear…",
            ForeColor = p.Accent
        };

        var btnShow = new FlatButton { Text = "Show shortcut", Left = 24, Top = 208, Width = 160, Height = 34 };
        var btnRetry = new FlatButton { Text = "Check again", Left = 192, Top = 208, Width = 120, Height = 34 };
        var btnClose = new FlatButton { Text = "Close", Left = 412, Top = 208, Width = 120, Height = 34 };

        btnShow.Click += (_, _) => Pinning.ShowInExplorer(lnk);
        btnRetry.Click += (_, _) => Check();
        btnClose.Click += (_, _) => Close();

        var hint = new Label
        {
            Left = 24, Top = 258, Width = 508, Height = 40,
            Text = "Tip: you can also drag a folder from the main window straight onto the taskbar.",
            ForeColor = p.SubText
        };

        pic.MouseDown += (_, e) => down = e.Location;
        pic.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (Math.Abs(e.X - down.X) < SystemInformation.DragSize.Width &&
                Math.Abs(e.Y - down.Y) < SystemInformation.DragSize.Height) return;
            pic.DoDragDrop(new DataObject(DataFormats.FileDrop, new[] { lnk }), DragDropEffects.Copy | DragDropEffects.Link);
        };

        Controls.AddRange(new Control[] { pic, title, msg, alt, status, btnShow, btnRetry, btnClose, hint });
        ThemeManager.Apply(this);
        title.ForeColor = p.Text;
        msg.ForeColor = p.SubText;
        alt.ForeColor = p.SubText;
        status.ForeColor = p.Accent;
        hint.ForeColor = p.SubText;
        FormClosed += (_, _) => { watch.Stop(); watch.Dispose(); pic.Image?.Dispose(); };
        HandleCreated += (_, _) => Native.SetDarkTitle(Handle, p.Dark);

        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(wa.Right - Width - 24, wa.Bottom - Height - 24);

        watch.Tick += (_, _) => Check();
        watch.Start();
    }

    void Check()
    {
        if (!Pinning.IsPinned(folder)) return;
        watch.Stop();
        status.Text = "Pinned! The folder is now on your taskbar.";
        var closer = new System.Windows.Forms.Timer { Interval = 2500 };
        closer.Tick += (_, _) => { closer.Stop(); closer.Dispose(); Close(); };
        closer.Start();
    }
}
