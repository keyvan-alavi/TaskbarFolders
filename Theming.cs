using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TaskbarFolders;

/// <summary>A complete colour set used by every window and by custom-painted controls.</summary>
public sealed class Palette
{
    public string Id = "system";
    public string Name = "Windows 11";
    public bool Dark = true;
    public bool Acrylic = true;

    public Color Bg = Color.FromArgb(32, 32, 32);
    public Color BgAlt = Color.FromArgb(26, 26, 26);
    public Color Panel = Color.FromArgb(43, 43, 43);
    public Color Card = Color.FromArgb(50, 50, 50);
    public Color Hover = Color.FromArgb(62, 62, 62);
    public Color Border = Color.FromArgb(62, 62, 62);
    public Color Text = Color.White;
    public Color SubText = Color.FromArgb(165, 165, 165);
    public Color Accent = Color.FromArgb(76, 194, 255);
    public Color OnAccent = Color.Black;
    public Color Sel = Color.FromArgb(50, 76, 194, 255);
    public Color Danger = Color.FromArgb(255, 99, 99);

    public bool IsLight => !Dark;
}

public static class ColorEx
{
    public static Color Blend(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (int)Math.Round(a.R + (b.R - a.R) * t),
            (int)Math.Round(a.G + (b.G - a.G) * t),
            (int)Math.Round(a.B + (b.B - a.B) * t));
    }

    public static Color Lighten(this Color c, double t) => Blend(c, Color.White, t);
    public static Color Darken(this Color c, double t) => Blend(c, Color.Black, t);

    public static Color WithAlpha(this Color c, int a) => Color.FromArgb(Math.Clamp(a, 0, 255), c);

    public static bool IsLightColor(this Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0 > 0.6;

    public static (double H, double S, double V) ToHsv(this Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double h = 0;
        if (d > 0.00001)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        double s = max <= 0 ? 0 : d / max;
        return (h, s, max);
    }

    public static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;
        double r = 0, g = 0, b = 0;
        switch ((int)(h / 60))
        {
            case 0: r = c; g = x; break;
            case 1: r = x; g = c; break;
            case 2: g = c; b = x; break;
            case 3: g = x; b = c; break;
            case 4: r = x; b = c; break;
            default: r = c; b = x; break;
        }
        return Color.FromArgb(
            (int)Math.Round((r + m) * 255),
            (int)Math.Round((g + m) * 255),
            (int)Math.Round((b + m) * 255));
    }

    /// <summary>Keeps the hue but makes very washed-out Windows accents usable as a UI accent.</summary>
    public static Color Vivid(this Color c, double minSat = 0.55, double minVal = 0.65)
    {
        var (h, s, v) = c.ToHsv();
        if (s < 0.12) return Color.FromArgb(0, 120, 212); // pure grey accent -> Windows blue
        return FromHsv(h, Math.Max(s, minSat), Math.Clamp(v, minVal, 0.9));
    }

    public static Color Parse(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            var s = hex.Trim().TrimStart('#');
            if (s.Length == 6) s = "FF" + s;
            if (s.Length != 8 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v))
                return fallback;
            return Color.FromArgb(v);
        }
        catch { return fallback; }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}

public static class ThemeManager
{
    public static Palette P { get; private set; } = BuildFromId("dark");
    public static Config? Cfg { get; private set; }

    public static void Init(Config cfg)
    {
        Cfg = cfg;
        P = Build(cfg.Settings.Theme);
    }

    public static readonly (string Id, string Name, string Sub)[] Themes =
    {
        ("system",  "Windows 11", "Your Windows theme + accent colour"),
        ("light",   "Light",      "Bright, clean Windows 11 look"),
        ("dark",    "Dark",       "Classic Windows 11 dark"),
        ("aurora",  "Aurora",     "Teal and violet, deep sea night"),
        ("sunset",  "Sunset",     "Warm orange and rose"),
        ("forest",  "Forest",     "Mossy green and bark"),
        ("midnight","Midnight",   "Indigo sky and electric blue"),
    };

    public static string ThemeName(string id) =>
        Themes.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).Name ?? "Windows 11";

    public static bool SystemDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("SystemUsesLightTheme") is int v && v == 0;
        }
        catch { return true; }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmGetColorizationColor(out uint color, out bool opaque);

    public static Color SystemAccent()
    {
        try
        {
            if (DwmGetColorizationColor(out var c, out _) == 0)
                return Color.FromArgb(255, (int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF)).Vivid();
        }
        catch { }
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (k?.GetValue("AccentColor") is int v)
            {
                // Stored as 0xAABBGGRR
                return Color.FromArgb(255, v & 0xFF, (v >> 8) & 0xFF, (v >> 16) & 0xFF).Vivid();
            }
        }
        catch { }
        return Color.FromArgb(0, 120, 212);
    }

    public static Palette Build(string? id) => BuildFromId(id ?? "system");

    static Palette BuildFromId(string id)
    {
        switch (id.ToLowerInvariant())
        {
            case "light": return Light(Color.FromArgb(0, 95, 184));
            case "dark": return Dark(Color.FromArgb(76, 194, 255));
            case "aurora":
                return Custom("aurora", "Aurora",
                    Color.FromArgb(13, 22, 28), Color.FromArgb(17, 30, 38), Color.FromArgb(22, 40, 50),
                    Color.FromArgb(30, 52, 62), Color.FromArgb(38, 66, 78), Color.FromArgb(232, 247, 245),
                    Color.FromArgb(148, 180, 178), Color.FromArgb(53, 208, 186));
            case "sunset":
                return Custom("sunset", "Sunset",
                    Color.FromArgb(28, 17, 22), Color.FromArgb(38, 23, 30), Color.FromArgb(48, 29, 38),
                    Color.FromArgb(62, 38, 48), Color.FromArgb(76, 46, 58), Color.FromArgb(255, 237, 227),
                    Color.FromArgb(203, 166, 156), Color.FromArgb(255, 138, 76));
            case "forest":
                return Custom("forest", "Forest",
                    Color.FromArgb(15, 23, 18), Color.FromArgb(20, 31, 24), Color.FromArgb(26, 41, 32),
                    Color.FromArgb(34, 53, 41), Color.FromArgb(44, 68, 52), Color.FromArgb(232, 245, 233),
                    Color.FromArgb(160, 185, 162), Color.FromArgb(123, 201, 111));
            case "midnight":
                return Custom("midnight", "Midnight",
                    Color.FromArgb(11, 16, 32), Color.FromArgb(16, 24, 44), Color.FromArgb(22, 33, 62),
                    Color.FromArgb(30, 44, 80), Color.FromArgb(42, 58, 98), Color.FromArgb(230, 236, 255),
                    Color.FromArgb(150, 164, 200), Color.FromArgb(91, 140, 255));
            default:
            {
                bool dark = SystemDark();
                var accent = SystemAccent();
                return dark ? Dark(accent) : Light(accent);
            }
        }
    }

    static Palette Dark(Color accent)
    {
        var p = new Palette
        {
            Id = "dark", Name = "Dark", Dark = true, Acrylic = true,
            Bg = Color.FromArgb(32, 32, 32),
            BgAlt = Color.FromArgb(26, 26, 26),
            Panel = Color.FromArgb(43, 43, 43),
            Card = Color.FromArgb(50, 50, 50),
            Hover = Color.FromArgb(60, 60, 60),
            Border = Color.FromArgb(64, 64, 64),
            Text = Color.FromArgb(255, 255, 255),
            SubText = Color.FromArgb(166, 166, 166),
            Accent = accent,
            Danger = Color.FromArgb(255, 110, 110),
        };
        p.OnAccent = accent.IsLightColor() ? Color.FromArgb(12, 12, 12) : Color.White;
        p.Sel = accent.WithAlpha(46);
        return p;
    }

    static Palette Light(Color accent)
    {
        var p = new Palette
        {
            Id = "light", Name = "Light", Dark = false, Acrylic = true,
            Bg = Color.FromArgb(243, 243, 243),
            BgAlt = Color.FromArgb(233, 233, 233),
            Panel = Color.FromArgb(255, 255, 255),
            Card = Color.FromArgb(250, 250, 250),
            Hover = Color.FromArgb(238, 238, 238),
            Border = Color.FromArgb(222, 222, 222),
            Text = Color.FromArgb(20, 20, 20),
            SubText = Color.FromArgb(95, 95, 95),
            Accent = accent,
            Danger = Color.FromArgb(196, 43, 28),
        };
        p.OnAccent = accent.IsLightColor() ? Color.FromArgb(12, 12, 12) : Color.White;
        p.Sel = accent.WithAlpha(34);
        return p;
    }

    static Palette Custom(string id, string name, Color bg, Color bgAlt, Color panel, Color card,
        Color border, Color text, Color sub, Color accent)
    {
        var p = new Palette
        {
            Id = id, Name = name, Dark = true, Acrylic = false,
            Bg = bg, BgAlt = bgAlt, Panel = panel, Card = card,
            Hover = card.Lighten(0.09), Border = border,
            Text = text, SubText = sub, Accent = accent,
            Danger = Color.FromArgb(255, 120, 120),
        };
        p.OnAccent = accent.IsLightColor() ? Color.FromArgb(10, 10, 10) : Color.White;
        p.Sel = accent.WithAlpha(52);
        return p;
    }

    // ---------------------------------------------------------------- styling

    /// <summary>Recursively colours stock WinForms controls. Custom controls read the palette at paint time.</summary>
    public static void Apply(Control root)
    {
        Style(root);
    }

    static void Style(Control c)
    {
        var p = P;
        switch (c)
        {
            case FlatButton:
                c.BackColor = Color.Transparent;
                c.ForeColor = p.Text;
                break;
            case Button b:
                b.FlatStyle = FlatStyle.Flat;
                b.UseVisualStyleBackColor = false;
                b.BackColor = p.Card;
                b.ForeColor = p.Text;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = p.Border;
                b.FlatAppearance.MouseOverBackColor = p.Hover;
                b.FlatAppearance.MouseDownBackColor = p.Hover;
                break;
            case TextBox tb:
                tb.BackColor = p.Card;
                tb.ForeColor = p.Text;
                tb.BorderStyle = BorderStyle.FixedSingle;
                DarkenScrollbars(tb);
                break;
            case ComboBox cb:
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = p.Card;
                cb.ForeColor = p.Text;
                break;
            case ListBox lb:
                lb.BackColor = p.Panel;
                lb.ForeColor = p.Text;
                DarkenScrollbars(lb);
                break;
            case ListView lv:
                lv.BackColor = p.Panel;
                lv.ForeColor = p.Text;
                if (lv is not BufferedListView bl || bl.DarkScrollbars) DarkenScrollbars(lv);
                break;
            case TreeView tv:
                tv.BackColor = p.Panel;
                tv.ForeColor = p.Text;
                DarkenScrollbars(tv);
                break;
            case CheckBox:
            case RadioButton:
                c.BackColor = Color.Transparent;
                c.ForeColor = p.Text;
                break;
            case LinkLabel ll:
                ll.LinkColor = p.Accent;
                ll.ActiveLinkColor = p.Accent.Lighten(0.2);
                ll.VisitedLinkColor = p.Accent;
                ll.BackColor = Color.Transparent;
                break;
            case Label l:
                l.BackColor = Color.Transparent;
                l.ForeColor = string.Equals(l.Tag as string, "sub", StringComparison.Ordinal) ? p.SubText : p.Text;
                break;
            case CardPanel:
                c.BackColor = p.Panel;
                c.ForeColor = p.Text;
                break;
            case GroupBox:
            case Panel:
                c.BackColor = p.Bg;
                c.ForeColor = p.Text;
                break;
            default:
                c.BackColor = p.Bg;
                c.ForeColor = p.Text;
                break;
        }

        foreach (Control child in c.Controls) Style(child);

        if (c is Form form && form.IsHandleCreated)
            Native.SetDarkTitle(form.Handle, p.Dark);
    }

    static void DarkenScrollbars(Control c)
    {
        void Apply(Control ctl)
        {
            if (!P.Dark) return;
            try { Native.SetWindowTheme(ctl.Handle, "DarkMode_Explorer", null); } catch { }
        }
        c.HandleCreated += (_, _) => Apply(c);
        if (c.IsHandleCreated) Apply(c);
    }

    public static void PaintCard(Graphics g, RectangleF r, Color fill, Color border, float radius = 10f, float borderWidth = 1f)
    {
        using var path = Gfx.Round(r, radius);
        using var b = new SolidBrush(fill);
        g.FillPath(b, path);
        if (borderWidth > 0)
        {
            using var pen = new Pen(border, borderWidth);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>Fixed folder colours used for "Automatic" icons, chosen by folder id so they never change.</summary>
    public static readonly Color[] AutoTints =
    {
        Color.FromArgb(64, 132, 255),  // blue
        Color.FromArgb(150, 104, 255), // violet
        Color.FromArgb(38, 199, 168),  // teal
        Color.FromArgb(255, 138, 76),  // orange
        Color.FromArgb(232, 93, 117),  // rose
        Color.FromArgb(123, 201, 111), // green
        Color.FromArgb(242, 184, 75),  // amber
        Color.FromArgb(76, 194, 255),  // sky
    };

    public static Color AutoTint(Folder f)
    {
        int h = 17;
        foreach (char ch in f.Id) h = unchecked(h * 31 + ch);
        return AutoTints[Math.Abs(h) % AutoTints.Length];
    }

    public static Color FolderColor(Folder f) =>
        ColorEx.Parse(f.Color, AutoTint(f));
}

/// <summary>ComboBox that actually respects the dark palette (the stock one ignores BackColor).</summary>
public class ThemedCombo : ComboBox
{
    public ThemedCombo()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        DropDownStyle = ComboBoxStyle.DropDownList;
        ItemHeight = 24;
        BackColor = ThemeManager.P.Card;
        ForeColor = ThemeManager.P.Text;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var p = ThemeManager.P;
        using (var bg = new SolidBrush(p.Card)) e.Graphics.FillRectangle(bg, e.Bounds);
        if (e.Index < 0 || e.Index >= Items.Count) return;

        bool selected = (e.State & DrawItemState.Selected) != 0;
        if (selected)
        {
            using var sel = new SolidBrush(p.Sel);
            e.Graphics.FillRectangle(sel, e.Bounds);
        }
        var text = GetItemText(Items[e.Index]);
        TextRenderer.DrawText(e.Graphics, text, Font,
            new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height),
            p.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>A flat button with a drop-down list, themed end to end.</summary>
public class OptionButton : FlatButton
{
    public string[] Options = Array.Empty<string>();
    int selected;
    public event EventHandler? SelectionChanged;

    public OptionButton()
    {
        Width = 170;
        Height = 32;
        Text = "▾";
    }

    public int SelectedIndex
    {
        get => selected;
        set
        {
            selected = Math.Clamp(value, 0, Math.Max(0, Options.Length - 1));
            Text = SelectedLabel + "    ▾";
            Invalidate();
        }
    }

    string SelectedLabel => Options.Length > 0 && selected < Options.Length ? Options[selected] : "";

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (Options.Length == 0) return;
        var p = ThemeManager.P;
        var menu = new ContextMenuStrip
        {
            BackColor = p.Panel, ForeColor = p.Text, ShowImageMargin = false,
            Renderer = new ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges = false }
        };
        for (int i = 0; i < Options.Length; i++)
        {
            int k = i;
            var item = new ToolStripMenuItem(Options[i]) { Checked = i == selected };
            item.Click += (_, _) =>
            {
                SelectedIndex = k;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            };
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(this, new Point(0, Height + 2));
    }
}

/// <summary>A flat, rounded button that follows the active palette.</summary>
public class FlatButton : Button{
    bool hover, down;

    public bool Primary { get; set; }
    public bool Ghost { get; set; }
    public bool Danger { get; set; }
    public int Radius { get; set; } = 8;

    public FlatButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 9.5f);
        Height = 32;
        TabStop = true;
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = ThemeManager.P;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var parentBg = Parent?.BackColor ?? p.Bg;
        if (parentBg.A == 0 || parentBg == Color.Transparent) parentBg = p.Bg;
        g.Clear(parentBg);

        var baseColor = Primary ? p.Accent : Danger ? p.Danger : Ghost ? Color.Transparent : p.Card;
        if (hover)
            baseColor = Primary ? p.Accent.Lighten(0.14) : Danger ? p.Danger.Lighten(0.12)
                : Ghost ? p.Hover : p.Hover;
        if (down)
            baseColor = Primary ? p.Accent.Darken(0.10) : Danger ? p.Danger.Darken(0.10) : p.Hover.Darken(0.06);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using var path = Gfx.Round(rect, Radius);
        if (baseColor.A > 0)
        {
            using var fill = new SolidBrush(Enabled ? baseColor : Color.FromArgb(90, baseColor));
            g.FillPath(fill, path);
        }
        if (!Primary && !Danger && !Ghost)
        {
            using var pen = new Pen(p.Border, 1f);
            g.DrawPath(pen, path);
        }
        if (Ghost && !hover && !down)
        {
            using var pen = new Pen(Color.FromArgb(0, 0, 0, 0), 0f);
            g.DrawPath(pen, path);
        }

        var fg = Primary ? p.OnAccent : Danger ? Color.White : p.Text;
        if (!Enabled) fg = Color.FromArgb(120, fg);
        if (Focused && ShowFocusCues)
        {
            var fr = RectangleF.Inflate(rect, -3f, -3f);
            using var fp = Gfx.Round(fr, Math.Max(2, Radius - 3));
            using var fpen = new Pen(Color.FromArgb(150, fg), 1f) { DashStyle = DashStyle.Dot };
            g.DrawPath(fpen, fp);
        }

        TextRenderer.DrawText(g, Text, Font,
            new Rectangle(0, 0, Width, Height), fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}
