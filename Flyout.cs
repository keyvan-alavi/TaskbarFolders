using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TaskbarFolders;

/// <summary>
/// The pop-up menu shown when a folder shortcut on the taskbar is clicked.
/// Layouts: 1 column = rows with names; 2-4 columns = icon grid (names optional).
/// The process lives only while the menu is open, so idle RAM usage is zero.
/// </summary>
public class Flyout : Form
{
    record Row(Item Item, Bitmap Icon);

    readonly Folder folder;
    readonly List<Row> rows = new();
    readonly bool dark;
    readonly float k;
    readonly int cols, cellW, cellH, iconSz, pad, visRows, totalRows, clientW;
    readonly bool showNames;
    readonly Font font, gridFont;
    readonly ToolTip tip = new();
    int hover = -1, scroll = 0;
    bool active;
    Point finalLoc, slide;

    int S(int v) => (int)Math.Round(v * k);

    static string StampFile(string id) => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tbf_" + id);

    public static bool RecentlyClosed(string id)
    {
        try
        {
            var p = StampFile(id);
            if (File.Exists(p) && long.TryParse(File.ReadAllText(p), out var ticks))
                return (DateTime.UtcNow.Ticks - ticks) < TimeSpan.FromMilliseconds(450).Ticks;
        }
        catch { }
        return false;
    }

    public Flyout(Folder f)
    {
        folder = f;
        dark = Theme.IsDark();
        var cur = Cursor.Position;
        k = Native.DpiAt(cur) / 96f;

        AutoScaleMode = AutoScaleMode.None;
        pad = S(8);
        cols = Math.Clamp(f.Columns, 1, 4);
        showNames = cols == 1 || f.ShowNames;

        foreach (var it in f.Items) rows.Add(new Row(it, IconUtil.Get(it.Target)));

        if (cols == 1)
        {
            iconSz = S(24);
            cellW = S(300) - 2 * pad;
            cellH = S(44);
        }
        else
        {
            iconSz = cols == 2 ? S(40) : cols == 3 ? S(36) : S(32);
            cellW = showNames ? (cols == 2 ? S(112) : cols == 3 ? S(96) : S(84)) : iconSz + S(28);
            cellH = showNames ? iconSz + S(48) : iconSz + S(24);
        }

        totalRows = Math.Max(1, (rows.Count + cols - 1) / cols);
        visRows = Math.Clamp(totalRows, 1, cols == 1 ? 12 : showNames ? 6 : 8);
        clientW = rows.Count == 0 ? S(260) : Math.Max(S(160), 2 * pad + cols * cellW);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = Color.Black; // black = transparent over the acrylic backdrop
        font = new Font("Segoe UI", 10f);
        gridFont = new Font("Segoe UI", 9f);
        ClientSize = new Size(clientW, 2 * pad + visRows * cellH);

        Place(cur);
    }

    void Place(Point cur)
    {
        var scr = Screen.FromPoint(cur);
        var b = scr.Bounds; var wa = scr.WorkingArea;
        int gap = S(8), w = Width, h = Height, x, y;
        int slideDist = S(18);

        if (wa.Bottom < b.Bottom || wa == b) // taskbar at bottom (or auto-hidden)
        {
            x = Math.Clamp(cur.X - w / 2, wa.Left + gap, wa.Right - w - gap);
            y = wa.Bottom < b.Bottom ? wa.Bottom - h - gap : b.Bottom - S(56) - h - gap;
            slide = new Point(0, slideDist);
        }
        else if (wa.Top > b.Top)
        {
            x = Math.Clamp(cur.X - w / 2, wa.Left + gap, wa.Right - w - gap);
            y = wa.Top + gap;
            slide = new Point(0, -slideDist);
        }
        else if (wa.Left > b.Left)
        {
            x = wa.Left + gap;
            y = Math.Clamp(cur.Y - h / 2, wa.Top + gap, wa.Bottom - h - gap);
            slide = new Point(-slideDist, 0);
        }
        else
        {
            x = wa.Right - w - gap;
            y = Math.Clamp(cur.Y - h / 2, wa.Top + gap, wa.Bottom - h - gap);
            slide = new Point(slideDist, 0);
        }
        finalLoc = new Point(x, y);
        Location = new Point(x + slide.X, y + slide.Y);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW: hidden from Alt-Tab
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!Native.ApplyBackdrop(Handle, dark))
            BackColor = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243); // older builds: solid fallback
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        Native.SetForegroundWindow(Handle);
        active = true;

        var sw = Stopwatch.StartNew();
        var t = new System.Windows.Forms.Timer { Interval = 8 };
        t.Tick += (_, _) =>
        {
            if (IsDisposed) { t.Stop(); t.Dispose(); return; }
            double p = Math.Min(1, sw.ElapsedMilliseconds / 170.0);
            double ease = 1 - Math.Pow(1 - p, 3);
            Location = new Point(finalLoc.X + (int)(slide.X * (1 - ease)), finalLoc.Y + (int)(slide.Y * (1 - ease)));
            if (p >= 1) { t.Stop(); t.Dispose(); }
        };
        t.Start();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (active) Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try { File.WriteAllText(StampFile(folder.Id), DateTime.UtcNow.Ticks.ToString()); } catch { }
        tip.Dispose();
        foreach (var r in rows) r.Icon.Dispose();
        font.Dispose();
        gridFont.Dispose();
        base.OnFormClosed(e);
    }

    // ---------- layout helpers ----------

    // Left edge of the cell grid; centres narrow grids inside the window.
    int GridX => (clientW - cols * cellW) / 2;

    Rectangle CellRect(int idx)
    {
        int r = idx / cols - scroll, c = idx % cols;
        return new Rectangle(GridX + c * cellW, pad + r * cellH, cellW, cellH);
    }

    int HitTest(Point p)
    {
        if (p.X < GridX || p.Y < pad) return -1;
        int c = (p.X - GridX) / cellW, r = (p.Y - pad) / cellH;
        if (c >= cols || r >= visRows) return -1;
        int idx = (r + scroll) * cols + c;
        return idx < rows.Count ? idx : -1;
    }

    // ---------- painting ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var fg = dark ? Color.White : Color.FromArgb(28, 28, 28);
        using var textBrush = new SolidBrush(fg);
        using var listFmt = new StringFormat
        {
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        using var gridFmt = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisWord,
            FormatFlags = StringFormatFlags.LineLimit
        };

        if (rows.Count == 0)
        {
            var r = new RectangleF(pad + S(12), pad, clientW - 2 * pad - S(24), cellH);
            using var dim = new SolidBrush(Color.FromArgb(170, fg));
            g.DrawString("Empty folder. Click to add apps.", font, dim, r, listFmt);
            return;
        }

        int first = scroll * cols;
        int end = Math.Min(rows.Count, (scroll + visRows) * cols);
        for (int i = first; i < end; i++)
        {
            var cell = CellRect(i);

            if (i == hover)
            {
                using var path = Gfx.Round(Rectangle.Inflate(cell, -S(2), -S(2)), S(6));
                using var hb = new SolidBrush(dark ? Color.FromArgb(34, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0));
                g.FillPath(hb, path);
            }

            if (cols == 1)
            {
                g.DrawImage(rows[i].Icon, new Rectangle(cell.X + S(12), cell.Y + (cell.Height - iconSz) / 2, iconSz, iconSz));
                var tr = new RectangleF(cell.X + S(12) + iconSz + S(12), cell.Y, cell.Width - S(12) - iconSz - S(24), cell.Height);
                g.DrawString(rows[i].Item.Name, font, textBrush, tr, listFmt);
            }
            else
            {
                int ix = cell.X + (cell.Width - iconSz) / 2;
                int iy = showNames ? cell.Y + S(8) : cell.Y + (cell.Height - iconSz) / 2;
                g.DrawImage(rows[i].Icon, new Rectangle(ix, iy, iconSz, iconSz));

                if (showNames)
                {
                    float ty = iy + iconSz + S(4);
                    var tr = new RectangleF(cell.X + S(4), ty, cell.Width - S(8), cell.Bottom - ty - S(4));
                    g.DrawString(rows[i].Item.Name, gridFont, textBrush, tr, gridFmt);
                }
            }
        }
    }

    // ---------- mouse / keyboard ----------

    void UpdateTip()
    {
        // Grids always get a tooltip: it shows the full name when names are hidden or truncated.
        if (cols > 1 && hover >= 0)
        {
            var r = CellRect(hover);
            tip.Show(rows[hover].Item.Name, this, r.X, r.Bottom + S(2), 2500);
        }
        else tip.Hide(this);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitTest(e.Location);
        if (h != hover) { hover = h; Invalidate(); UpdateTip(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hover != -1) { hover = -1; Invalidate(); tip.Hide(this); }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        scroll = Math.Clamp(scroll - Math.Sign(e.Delta), 0, Math.Max(0, totalRows - visRows));
        hover = HitTest(e.Location);
        Invalidate();
        UpdateTip();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (rows.Count == 0) { OpenManager(); return; }
        int i = HitTest(e.Location);
        if (i < 0) return;
        if (e.Button == MouseButtons.Left) Launch(i, false);
        else if (e.Button == MouseButtons.Right) Launch(i, true); // right-click = run as administrator
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) { Close(); return; }
        if (rows.Count == 0) return;

        if (e.KeyCode == Keys.Down) Step(cols);
        else if (e.KeyCode == Keys.Up) Step(-cols);
        else if (e.KeyCode == Keys.Right && cols > 1) Step(1);
        else if (e.KeyCode == Keys.Left && cols > 1) Step(-1);
        else if (e.KeyCode == Keys.Enter && hover >= 0) Launch(hover, e.Shift);
        else return;
        e.Handled = true;
    }

    void Step(int d)
    {
        hover = hover < 0 ? (d > 0 ? 0 : rows.Count - 1) : Math.Clamp(hover + d, 0, rows.Count - 1);
        int row = hover / cols;
        if (row < scroll) scroll = row;
        if (row >= scroll + visRows) scroll = row - visRows + 1;
        Invalidate();
        UpdateTip();
    }

    void OpenManager()
    {
        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true }); } catch { }
        Close();
    }

    void Launch(int i, bool admin)
    {
        var it = rows[i].Item;
        try
        {
            var target = Environment.ExpandEnvironmentVariables(it.Target);
            var psi = new ProcessStartInfo(target) { UseShellExecute = true, Arguments = it.Args };
            if (File.Exists(target)) psi.WorkingDirectory = System.IO.Path.GetDirectoryName(target) ?? "";
            if (admin) psi.Verb = "runas";
            Process.Start(psi);
        }
        catch (Win32Exception) { /* UAC cancelled or target missing */ }
        catch { }
        Close();
    }
}
