using System.Diagnostics;

namespace TaskbarFolders;

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
        bool dark = Theme.IsDark();
        string kind = f.IconKind, value = f.IconValue;

        using var form = new Form
        {
            Text = "Folder icon", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, ClientSize = new Size(440, 396),
            Font = new Font("Segoe UI", 9.5f)
        };

        var lblPresets = new Label { Text = "Presets", Left = 14, Top = 12, Width = 200 };
        var flow = new FlowLayoutPanel { Left = 14, Top = 36, Width = 412, Height = 108 };

        var btnAuto = new Button { Text = "Automatic (grid of the folder's apps)", Left = 14, Top = 156, Width = 300, Height = 32 };

        var lblCustom = new Label { Text = "Custom emoji or letters:", Left = 14, Top = 200, Width = 300 };
        var tb = new TextBox { Left = 14, Top = 224, Width = 120, MaxLength = 8 };
        var lblTip = new Label { Text = "Tip: press Win + . for the emoji panel", Left = 144, Top = 228, Width = 180, Height = 36 };

        var btnFile = new Button { Text = "Choose image file…  (PNG, ICO, JPG)", Left = 14, Top = 268, Width = 300, Height = 32 };
        var lblNote = new Label
        {
            Text = "Emoji are drawn as single-colour glyphs. Use an image file if you want full colour.",
            Left = 14, Top = 308, Width = 300, Height = 36
        };

        var preview = new PictureBox { Left = 330, Top = 156, Size = new Size(96, 96), SizeMode = PictureBoxSizeMode.Zoom };
        var lblPreview = new Label { Text = "Preview", Left = 330, Top = 256, Width = 96 };

        var ok = new Button { Text = "OK", Left = 264, Top = 352, Width = 76, Height = 32, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 348, Top = 352, Width = 76, Height = 32, DialogResult = DialogResult.Cancel };

        void UpdatePreview()
        {
            var tmp = new Folder { Id = f.Id, Name = f.Name, Items = f.Items, IconKind = kind, IconValue = value };
            var old = preview.Image;
            preview.Image = IconBuilder.Render(tmp, 96);
            old?.Dispose();
        }

        foreach (var p in Presets)
        {
            var glyph = p;
            var b = new Button { Text = glyph, Width = 44, Height = 44, Margin = new Padding(3), Font = new Font("Segoe UI Emoji", 15f) };
            b.Click += (_, _) => tb.Text = glyph; // TextChanged applies it
            flow.Controls.Add(b);
        }

        tb.TextChanged += (_, _) =>
        {
            var t = tb.Text.Trim();
            if (t.Length == 0) return;
            kind = "emoji"; value = t;
            UpdatePreview();
        };

        btnAuto.Click += (_, _) =>
        {
            tb.Text = "";
            kind = "auto"; value = "";
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
                MessageBox.Show(form, "That file couldn't be read as an image.", "Folder icon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            tb.Text = "";
            kind = "file"; value = dlg.FileName;
            UpdatePreview();
        };

        form.Controls.AddRange(new Control[]
        {
            lblPresets, flow, btnAuto, lblCustom, tb, lblTip, btnFile, lblNote, preview, lblPreview, ok, cancel
        });
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        Theme.Apply(form, dark);
        form.HandleCreated += (_, _) => Native.SetDarkTitle(form.Handle, dark);
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
                MessageBox.Show(owner, "Couldn't copy the image:\n" + ex.Message, "Folder icon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    static void CleanupCustom(Folder f, string? keep)
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

/// <summary>
/// Shown when Windows doesn't pin silently: a draggable icon plus instructions.
/// It watches the pinned-shortcuts folder and closes itself once the pin appears.
/// </summary>
class PinHelper : Form
{
    readonly Folder folder;
    readonly string lnk;
    readonly System.Windows.Forms.Timer watch = new() { Interval = 700 };
    readonly Label status;
    readonly PictureBox pic;
    Point down;

    public PinHelper(Folder f, string lnkPath)
    {
        folder = f;
        lnk = lnkPath;
        bool dark = Theme.IsDark();

        Text = "Pin to taskbar";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(440, 214);
        Font = new Font("Segoe UI", 9.5f);

        pic = new PictureBox
        {
            Left = 20, Top = 20, Size = new Size(72, 72), SizeMode = PictureBoxSizeMode.Zoom,
            Image = IconBuilder.Render(f, 72), Cursor = Cursors.Hand
        };
        var msg = new Label
        {
            Left = 108, Top = 18, Width = 316, Height = 100,
            Text = "Windows 11 doesn't let apps pin silently, so:\n\n" +
                   "•  Drag this icon onto the taskbar, or\n" +
                   "•  Show the shortcut in Explorer, right-click it > Show more options > Pin to taskbar."
        };
        status = new Label { Left = 20, Top = 130, Width = 400, Height = 24, Text = "Waiting for the pin to appear on the taskbar..." };

        var btnShow = new Button { Text = "Show in Explorer", Left = 20, Top = 166, Width = 150, Height = 32 };
        var btnClose = new Button { Text = "Close", Left = 344, Top = 166, Width = 80, Height = 32 };
        btnShow.Click += (_, _) => { try { Process.Start("explorer.exe", $"/select,\"{lnk}\""); } catch { } };
        btnClose.Click += (_, _) => Close();

        pic.MouseDown += (_, e) => down = e.Location;
        pic.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (Math.Abs(e.X - down.X) < SystemInformation.DragSize.Width &&
                Math.Abs(e.Y - down.Y) < SystemInformation.DragSize.Height) return;
            pic.DoDragDrop(new DataObject(DataFormats.FileDrop, new[] { lnk }), DragDropEffects.Copy | DragDropEffects.Link);
        };

        Controls.AddRange(new Control[] { pic, msg, status, btnShow, btnClose });
        Theme.Apply(this, dark);
        HandleCreated += (_, _) => Native.SetDarkTitle(Handle, dark);

        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(wa.Right - Width - 24, wa.Bottom - Height - 24);

        watch.Tick += (_, _) =>
        {
            if (!Pinning.IsPinned(folder)) return;
            watch.Stop();
            status.Text = "Pinned!";
            var closer = new System.Windows.Forms.Timer { Interval = 1200 };
            closer.Tick += (_, _) => { closer.Stop(); closer.Dispose(); Close(); };
            closer.Start();
        };
        watch.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        watch.Stop();
        watch.Dispose();
        pic.Image?.Dispose();
        base.OnFormClosed(e);
    }
}
