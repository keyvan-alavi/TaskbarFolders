using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TaskbarFolders;

/// <summary>
/// The pop-up menu shown when a folder shortcut on the taskbar is clicked.
/// The process lives only while the menu is open, so idle RAM usage is zero.
/// </summary>
public class Flyout : Form
{
    record Row(Item Item, Bitmap Icon);

    readonly Folder folder;
    readonly List<Row> rows = new();
    readonly bool dark;
    readonly float k;
    readonly int W, RowH, Pad, visible;
    readonly Font font;
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
        W = S(300); RowH = S(44); Pad = S(8);

        foreach (var it in f.Items) rows.Add(new Row(it, IconUtil.Get(it.Target)));
        visible = Math.Clamp(rows.Count, 1, 12);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = Color.Black; // black = transparent over the acrylic backdrop
        font = new Font("Segoe UI", 10f);
        ClientSize = new Size(W, Pad * 2 + RowH * visible);

        Place(cur);
    }

    void Place(Point cur)
    {
        var scr = Screen.FromPoint(cur);
        var b = scr.Bounds; var wa = scr.WorkingArea;
        int gap = S(8), H = Height, x, y;
        int slideDist = S(18);

        if (wa.Bottom < b.Bottom || wa == b) // taskbar at bottom (or auto-hidden)
        {
            x = Math.Clamp(cur.X - W / 2, wa.Left + gap, wa.Right - W - gap);
            y = wa.Bottom < b.Bottom ? wa.Bottom - H - gap : b.Bottom - S(56) - H - gap;
            slide = new Point(0, slideDist);
        }
        else if (wa.Top > b.Top)
        {
            x = Math.Clamp(cur.X - W / 2, wa.Left + gap, wa.Right - W - gap);
            y = wa.Top + gap;
            slide = new Point(0, -slideDist);
        }
        else if (wa.Left > b.Left)
        {
            x = wa.Left + gap;
            y = Math.Clamp(cur.Y - H / 2, wa.Top + gap, wa.Bottom - H - gap);
            slide = new Point(-slideDist, 0);
        }
        else
        {
            x = wa.Right - W - gap;
            y = Math.Clamp(cur.Y - H / 2, wa.Top + gap, wa.Bottom - H - gap);
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
        foreach (var r in rows) r.Icon.Dispose();
        font.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var fg = dark ? Color.White : Color.FromArgb(28, 28, 28);
        using var textBrush = new SolidBrush(fg);
        using var sf = new StringFormat
        {
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        if (rows.Count == 0)
        {
            var r = new RectangleF(Pad + S(12), Pad, W - 2 * Pad - S(24), RowH);
            using var dim = new SolidBrush(Color.FromArgb(170, fg));
            g.DrawString("Empty folder. Click to add apps.", font, dim, r, sf);
            return;
        }

        int end = Math.Min(rows.Count, scroll + visible);
        for (int i = scroll; i < end; i++)
        {
            int y = Pad + (i - scroll) * RowH;
            var rect = new Rectangle(Pad, y, W - 2 * Pad, RowH);

            if (i == hover)
            {
                using var path = Gfx.Round(rect, S(6));
                using var hb = new SolidBrush(dark ? Color.FromArgb(34, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0));
                g.FillPath(hb, path);
            }

            int isz = S(24);
            g.DrawImage(rows[i].Icon, new Rectangle(rect.X + S(12), y + (RowH - isz) / 2, isz, isz));
            var tr = new RectangleF(rect.X + S(12) + isz + S(12), y, rect.Width - S(12) - isz - S(24), RowH);
            g.DrawString(rows[i].Item.Name, font, textBrush, tr, sf);
        }
    }

    int HitTest(int y)
    {
        if (y < Pad) return -1;
        int idx = (y - Pad) / RowH + scroll;
        return (y - Pad) / RowH < visible && idx < rows.Count ? idx : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitTest(e.Y);
        if (h != hover) { hover = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hover != -1) { hover = -1; Invalidate(); }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        scroll = Math.Clamp(scroll - Math.Sign(e.Delta), 0, Math.Max(0, rows.Count - visible));
        hover = HitTest(e.Y);
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (rows.Count == 0) { OpenManager(); return; }
        int i = HitTest(e.Y);
        if (i < 0) return;
        if (e.Button == MouseButtons.Left) Launch(i, false);
        else if (e.Button == MouseButtons.Right) Launch(i, true); // right-click = run as administrator
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) { Close(); return; }
        if (rows.Count == 0) return;

        if (e.KeyCode == Keys.Down) Step(1);
        else if (e.KeyCode == Keys.Up) Step(-1);
        else if (e.KeyCode == Keys.Enter && hover >= 0) Launch(hover, e.Shift);
        else return;
        e.Handled = true;
    }

    void Step(int d)
    {
        hover = hover < 0 ? (d > 0 ? 0 : rows.Count - 1) : Math.Clamp(hover + d, 0, rows.Count - 1);
        if (hover < scroll) scroll = hover;
        if (hover >= scroll + visible) scroll = hover - visible + 1;
        Invalidate();
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
