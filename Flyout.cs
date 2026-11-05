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

    readonly Config cfg;
    readonly Folder folder;
    readonly Palette pal;
    readonly List<Row> rows = new();
    readonly float k;
    readonly int cols, stripH, pad, footerH, cellW, cellH, iconSz;
    readonly bool showNames;
    readonly Font font, gridFont, footFont;
    readonly Point anchorPoint;
    readonly ToolTip tip = new();

    int visRows, totalRows, clientW, clientH;
    int hover = -1, scroll = 0, footerHover;
    bool active, busy, closing, dragging, animating;
    Point finalLoc, slide;
    Rectangle addRect, manageRect;

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

    public Flyout(Config config, Folder f)
    {
        cfg = config;
        folder = f;
        pal = ThemeManager.P;
        anchorPoint = Cursor.Position;
        k = Native.DpiAt(anchorPoint) / 96f;

        AutoScaleMode = AutoScaleMode.None;
        pad = S(8);
        stripH = S(3);
        footerH = S(40);
        cols = Math.Clamp(f.Columns, 1, 4);
        showNames = cols == 1 || f.ShowNames;

        font = new Font("Segoe UI", 10f);
        gridFont = new Font("Segoe UI", 9f);
        footFont = new Font("Segoe UI Semibold", 9f);

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

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        Text = "Taskbar Folders — " + f.Name;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = pal.Panel;
        Font = font;
        AllowDrop = true;
        tip.InitialDelay = 350;

        foreach (var it in f.Items) rows.Add(new Row(it, IconUtil.Get(it.Target)));
        Relayout(f.Items.Count == 0 ? S(250) : 0);
        Place();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80;              // WS_EX_TOOLWINDOW: hidden from Alt-Tab
            cp.ClassStyle |= 0x20000;        // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.Round(Handle);
        Native.SetDarkTitle(Handle, pal.Dark);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var path = Gfx.Round(new RectangleF(0, 0, Width, Height), S(12));
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    // ---------------------------------------------------------------- layout

    void Relayout(int minWidth)
    {
        totalRows = Math.Max(1, (rows.Count + cols - 1) / cols);
        int maxRows = cols == 1 ? 12 : showNames ? 6 : 8;
        int screenLimit = Math.Max(3, (int)((Screen.FromPoint(anchorPoint).WorkingArea.Height * 0.82 - footerH - 2 * pad) / (double)cellH));
        maxRows = Math.Min(maxRows, screenLimit);
        visRows = Math.Clamp(totalRows, 1, Math.Max(1, maxRows));
        scroll = 0;

        int contentW = rows.Count == 0 ? S(250) : Math.Max(S(200), 2 * pad + cols * cellW);
        clientW = Math.Max(minWidth, contentW);
        clientH = stripH + pad + visRows * cellH + S(6) + footerH;

        ClientSize = new Size(clientW, clientH);
        ComputeFooter();
    }

    void Place()
    {
        var scr = Screen.FromPoint(anchorPoint);
        var b = scr.Bounds;
        var wa = scr.WorkingArea;
        int gap = S(8), w = Width, h = Height, x, y;
        int slideDist = S(16);
        slide = Point.Empty;

        if (wa.Bottom < b.Bottom || wa == b) // taskbar at bottom (or auto-hidden)
        {
            x = Math.Clamp(anchorPoint.X - w / 2, wa.Left + gap, wa.Right - w - gap);
            y = wa.Bottom < b.Bottom ? wa.Bottom - h - gap : b.Bottom - S(56) - h - gap;
            slide = new Point(0, slideDist);
        }
        else if (wa.Top > b.Top)
        {
            x = Math.Clamp(anchorPoint.X - w / 2, wa.Left + gap, wa.Right - w - gap);
            y = wa.Top + gap;
            slide = new Point(0, -slideDist);
        }
        else if (wa.Left > b.Left)
        {
            x = wa.Left + gap;
            y = Math.Clamp(anchorPoint.Y - h / 2, wa.Top + gap, wa.Bottom - h - gap);
            slide = new Point(-slideDist, 0);
        }
        else
        {
            x = wa.Right - w - gap;
            y = Math.Clamp(anchorPoint.Y - h / 2, wa.Top + gap, wa.Bottom - h - gap);
            slide = new Point(slideDist, 0);
        }

        finalLoc = new Point(x, y);
        Location = slide.IsEmpty ? finalLoc : new Point(x + slide.X, y + slide.Y);
    }

    void ComputeFooter()
    {
        int footerTop = clientH - footerH;
        int btnH = S(26);
        int y = footerTop + (footerH - btnH) / 2;

        var addText = "+  Add apps";
        var manageText = "Manage";
        int addW = TextRenderer.MeasureText(addText, footFont).Width + S(18);
        int manW = TextRenderer.MeasureText(manageText, footFont).Width + S(18);
        addRect = new Rectangle(pad + S(2), y, addW, btnH);
        manageRect = new Rectangle(clientW - pad - manW - S(2), y, manW, btnH);
    }

    // ---------------------------------------------------------------- animation

    string AnimKind
    {
        get
        {
            var id = string.IsNullOrWhiteSpace(folder.Animation) ? cfg.Settings.Animation : folder.Animation;
            return string.IsNullOrWhiteSpace(id) ? "slidefade" : id;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        Native.SetForegroundWindow(Handle);
        active = true;
        ComputeFooter();

        string kind = AnimKind;
        int ms = Math.Clamp(cfg.Settings.AnimationMs, 80, 600);
        if (kind == "none" || ms <= 0)
        {
            Invalidate();
            return;
        }

        animating = true;
        var sw = Stopwatch.StartNew();
        var startSize = new Size((int)(clientW * 0.90), (int)(clientH * 0.90));
        var startLoc = new Point(finalLoc.X + (clientW - startSize.Width) / 2,
            finalLoc.Y + (clientH - startSize.Height) / 2);

        if (kind is "zoom")
        {
            Size = startSize;
            Location = startLoc;
            Opacity = 0.0;
        }
        else if (kind is "fade")
        {
            Opacity = 0.0;
        }

        var timer = new System.Windows.Forms.Timer { Interval = 8 };
        timer.Tick += (_, _) =>
        {
            if (IsDisposed) { timer.Stop(); timer.Dispose(); return; }
            double t = Math.Min(1, sw.Elapsed.TotalMilliseconds / ms);
            double e = 1 - Math.Pow(1 - t, 3);

            if (kind is "slide" or "slidefade")
            {
                Location = new Point(finalLoc.X + (int)(slide.X * (1 - e)),
                    finalLoc.Y + (int)(slide.Y * (1 - e)));
                if (kind == "slidefade") Opacity = e;
            }
            else if (kind == "fade")
            {
                Opacity = e;
            }
            else if (kind == "zoom")
            {
                int w = (int)(startSize.Width + (clientW - startSize.Width) * e);
                int h = (int)(startSize.Height + (clientH - startSize.Height) * e);
                Size = new Size(w, h);
                Location = new Point(finalLoc.X + (clientW - w) / 2, finalLoc.Y + (clientH - h) / 2);
                Opacity = e;
            }

            if (t >= 1)
            {
                timer.Stop();
                timer.Dispose();
                Opacity = 1.0;
                Size = new Size(clientW, clientH);
                Location = finalLoc;
                animating = false;
                Invalidate();
            }
        };
        timer.Start();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (active && !busy && !closing)
        {
            closing = true;
            Close();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try { File.WriteAllText(StampFile(folder.Id), DateTime.UtcNow.Ticks.ToString()); } catch { }
        tip.Dispose();
        foreach (var r in rows) r.Icon.Dispose();
        font.Dispose();
        gridFont.Dispose();
        footFont.Dispose();
        base.OnFormClosed(e);
    }

    // ---------------------------------------------------------------- painting

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Painted in OnPaint so the rounded background and gradient stay in one pass.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        float scale = clientW <= 0 ? 1f : ClientSize.Width / (float)clientW;
        g.ScaleTransform(scale, scale);

        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, clientW, Math.Max(1, clientH)),
                   pal.Dark ? ColorEx.Blend(pal.Panel, Color.Black, 0.08) : ColorEx.Blend(pal.Panel, Color.White, 0.35),
                   pal.Panel, 90f))
            g.FillRectangle(bg, 0, 0, clientW, clientH);

        // Folder colour strip makes every menu instantly recognisable.
        using (var strip = new SolidBrush(ThemeManager.FolderColor(folder)))
            g.FillRectangle(strip, 0, 0, clientW, stripH);

        var fg = pal.Text;
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

        int rowsTop = stripH + pad;
        if (rows.Count == 0)
        {
            var r = new RectangleF(pad + S(10), rowsTop + S(4), clientW - 2 * pad - S(20), cellH);
            using var dim = new SolidBrush(Color.FromArgb(190, fg));
            g.DrawString("Empty folder", font, dim, r, listFmt);
            using var hintFont = new Font("Segoe UI", 9f);
            using var hintBrush = new SolidBrush(pal.SubText);
            g.DrawString("Use “+ Add apps” or drop shortcuts here.", hintFont, hintBrush,
                new RectangleF(r.X, r.Y + S(28), r.Width, cellH), listFmt);
        }
        else
        {
            int first = scroll * cols;
            int end = Math.Min(rows.Count, (scroll + visRows) * cols);
            var hoverTint = ColorEx.Blend(pal.Panel, ThemeManager.FolderColor(folder), pal.Dark ? 0.26 : 0.18);

            for (int i = first; i < end; i++)
            {
                var cell = CellRect(i);

                if (i == hover)
                {
                    using var path = Gfx.Round(Rectangle.Inflate(cell, -S(2), -S(2)), S(7));
                    using var hb = new SolidBrush(hoverTint);
                    g.FillPath(hb, path);
                }

                if (cols == 1)
                {
                    g.DrawImage(rows[i].Icon, new Rectangle(cell.X + S(12), cell.Y + (cell.Height - iconSz) / 2, iconSz, iconSz));
                    var tr = new RectangleF(cell.X + S(12) + iconSz + S(12), cell.Y,
                        cell.Width - S(12) - iconSz - S(24), cell.Height);
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
                        using var nameBr = new SolidBrush(fg);
                        g.DrawString(rows[i].Item.Name, gridFont, nameBr, tr, gridFmt);
                    }
                }
            }
        }

        // ---------- footer ----------
        int footerTop = clientH - footerH;
        using (var line = new Pen(Color.FromArgb(70, pal.Border), 1f))
            g.DrawLine(line, pad, footerTop, clientW - pad, footerTop);

        DrawFooterButton(g, addRect, "+  Add apps", footerHover == 1);
        DrawFooterButton(g, manageRect, "Manage", footerHover == 2);

        if (dragging)
        {
            using var border = new Pen(pal.Accent, Math.Max(2f, S(2))) { DashStyle = DashStyle.Dash };
            var r = RectangleF.Inflate(new RectangleF(1, 1, clientW - 2, clientH - 2), -2, -2);
            using var path = Gfx.Round(r, S(11));
            g.DrawPath(border, path);

            var msg = "Drop to add apps";
            var size = TextRenderer.MeasureText(msg, footFont);
            var box = new Rectangle((clientW - size.Width - S(28)) / 2, footerTop - S(38), size.Width + S(28), S(28));
            using var fill = new SolidBrush(pal.Accent);
            using var path2 = Gfx.Round(box, S(8));
            g.FillPath(fill, path2);
            TextRenderer.DrawText(g, msg, footFont, box, pal.OnAccent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    void DrawFooterButton(Graphics g, Rectangle r, string text, bool hot)
    {
        if (hot)
        {
            using var path = Gfx.Round(r, S(6));
            using var b = new SolidBrush(pal.Hover);
            g.FillPath(b, path);
        }
        TextRenderer.DrawText(g, text, footFont, r,
            hot ? pal.Text : pal.SubText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    // ---------------------------------------------------------------- helpers

    int GridX => (clientW - cols * cellW) / 2;
    int RowsTop => stripH + pad;

    Rectangle CellRect(int idx)
    {
        int r = idx / cols - scroll, c = idx % cols;
        return new Rectangle(GridX + c * cellW, RowsTop + r * cellH, cellW, cellH);
    }

    int HitTest(Point p)
    {
        if (p.X < GridX || p.Y < RowsTop) return -1;
        int c = (p.X - GridX) / cellW, r = (p.Y - RowsTop) / cellH;
        if (c >= cols || r >= visRows) return -1;
        int idx = (r + scroll) * cols + c;
        return idx < rows.Count ? idx : -1;
    }

    void UpdateTip()
    {
        if (cols > 1 && hover >= 0)
        {
            var r = CellRect(hover);
            tip.Show(rows[hover].Item.Name, this, r.X, r.Bottom + S(2), 2500);
        }
        else tip.Hide(this);
    }

    // ---------------------------------------------------------------- mouse / keyboard

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (animating) return;

        int fh = addRect.Contains(e.Location) ? 1 : manageRect.Contains(e.Location) ? 2 : 0;
        if (fh != footerHover) { footerHover = fh; Invalidate(); }

        int h = fh != 0 ? -1 : HitTest(e.Location);
        if (h != hover) { hover = h; Invalidate(); UpdateTip(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hover != -1 || footerHover != 0)
        {
            hover = -1;
            footerHover = 0;
            Invalidate();
            tip.Hide(this);
        }
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
        if (animating) return;

        if (addRect.Contains(e.Location)) { AddFromStartMenu(); return; }
        if (manageRect.Contains(e.Location)) { OpenManager(); return; }

        if (rows.Count == 0) { AddFromStartMenu(); return; }
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
        try
        {
            // One manager only: if it is already running, bring it forward with this folder selected.
            if (!Program.TryFocusManager(folder.Id))
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--manage {folder.Id}")
                {
                    UseShellExecute = true
                });
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
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

    // ---------------------------------------------------------------- add apps

    void AddFromStartMenu()
    {
        busy = true;
        try
        {
            using var picker = new StartMenuPicker(StartMenuScanner.Scan());
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            if (AddItems(picker.SelectedTargets))
            {
                Relayout(S(250));
                Place();
                Invalidate();
            }
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
        finally
        {
            busy = false;
            Activate();
            Native.SetForegroundWindow(Handle);
        }
    }

    bool AddItems(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (var raw in paths)
        {
            var path = raw.Trim().Trim('"');
            if (path.Length == 0) continue;
            if (folder.Items.Any(i => string.Equals(i.Target, path, StringComparison.OrdinalIgnoreCase))) continue;

            string name;
            try
            {
                name = Directory.Exists(path)
                    ? System.IO.Path.GetFileName(path.TrimEnd('\\', '/'))
                    : System.IO.Path.GetFileNameWithoutExtension(path);
            }
            catch { name = path; }
            if (string.IsNullOrWhiteSpace(name)) name = path;
            folder.Items.Add(new Item { Name = name, Target = path });
            added++;
        }

        if (added == 0) return false;

        cfg.Save();
        try
        {
            IconBuilder.BuildIcon(folder);
            if (Pinning.IsPinned(folder)) Pinning.EnsureShortcut(folder);
        }
        catch (Exception ex) { ErrorLog.Write(ex); }

        foreach (var r in rows) r.Icon.Dispose();
        rows.Clear();
        foreach (var it in folder.Items) rows.Add(new Row(it, IconUtil.Get(it.Target)));
        UpdateTip();
        Invalidate();
        return true;
    }

    // ---------------------------------------------------------------- drag & drop

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect = DragDropEffects.Copy;
            dragging = true;
            Invalidate();
        }
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        dragging = false;
        Invalidate();
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        dragging = false;
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
        {
            if (AddItems(paths))
            {
                Relayout(S(250));
                Place();
            }
        }
        Invalidate();
    }
}

