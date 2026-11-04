using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarFolders;

static class Native
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref Margins m);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point pt, uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr hm, int type, out uint x, out uint y);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    public static extern int SetWindowTheme(IntPtr h, string? subApp, string? subId);

    public static void RefreshShellIcons()
    {
        try { SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero); } catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Margins { public int L, R, T, B; }

    public const uint SHGFI_ICON = 0x100;
    public const uint SHGFI_LARGEICON = 0x0;
    public const uint SHGFI_SMALLICON = 0x1;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x10;

    public static void SetDarkTitle(IntPtr h, bool dark)
    {
        int d = dark ? 1 : 0;
        DwmSetWindowAttribute(h, 20, ref d, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
    }

    /// <summary>Acrylic backdrop + rounded corners (Windows 11 22H2+). Returns false if unsupported.</summary>
    public static bool ApplyBackdrop(IntPtr h, bool dark)
    {
        SetDarkTitle(h, dark);
        Round(h);
        var m = new Margins { L = -1, R = -1, T = -1, B = -1 };
        DwmExtendFrameIntoClientArea(h, ref m);
        int type = 3; // DWMSBT_TRANSIENTWINDOW (acrylic)
        return DwmSetWindowAttribute(h, 38, ref type, 4) == 0;
    }

    public static void Round(IntPtr h)
    {
        int corner = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(h, 33, ref corner, 4);
    }

    public static int DpiAt(Point p)
    {
        try
        {
            var hm = MonitorFromPoint(p, 2);
            if (GetDpiForMonitor(hm, 0, out var x, out _) == 0) return (int)x;
        }
        catch { }
        return 96;
    }
}

static class IconUtil
{
    public static Bitmap Get(string target)
    {
        var p = "";
        try
        {
            p = Environment.ExpandEnvironmentVariables(target ?? "");
            if (p.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
            {
                using var ico = new Icon(p, 48, 48);
                return ico.ToBitmap();
            }

            if (p.Length > 0)
            {
                var info = new Native.SHFILEINFO();
                var ok = Native.SHGetFileInfo(p, 0, ref info, (uint)Marshal.SizeOf<Native.SHFILEINFO>(),
                    Native.SHGFI_ICON | Native.SHGFI_LARGEICON);
                if (ok != IntPtr.Zero && info.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        using var ic = Icon.FromHandle(info.hIcon);
                        return ic.ToBitmap();
                    }
                    finally { Native.DestroyIcon(info.hIcon); }
                }
            }

            if (File.Exists(p))
            {
                using var ic = Icon.ExtractAssociatedIcon(p);
                if (ic != null) return ic.ToBitmap();
            }
        }
        catch { }

        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                using var ic = Icon.ExtractAssociatedIcon(exe);
                if (ic != null) return ic.ToBitmap();
            }
        }
        catch { }
        return SystemIcons.Application.ToBitmap();
    }
}

static class Gfx
{
    /// <summary>Rounded rectangle that never produces a zero-size arc (GDI+ throws "invalid parameter" for those).</summary>
    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        if (r.Width <= 0 || r.Height <= 0) return p;

        rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2f);
        float d = rad * 2;
        if (d < 0.5f)
        {
            p.AddRectangle(r);
            return p;
        }

        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void DrawShadow(Graphics g, RectangleF r, float radius, int alpha = 40)
    {
        for (int i = 3; i >= 1; i--)
        {
            using var p = Round(RectangleF.Inflate(r, i, i), radius + i);
            using var b = new SolidBrush(Color.FromArgb(alpha / (i + 2), 0, 0, 0));
            g.FillPath(b, p);
        }
    }
}

/// <summary>Builds the folder icon: custom image, emoji/text on a tinted tile, or a 2x2 grid of the folder's apps.</summary>
static class IconBuilder
{
    public static string IcoPath(Folder f) => System.IO.Path.Combine(Config.IconsDir, f.Id + ".ico");

    /// <summary>Loads png/jpg/bmp/ico without keeping the file locked. Returns null if unreadable.</summary>
    public static Bitmap? LoadImage(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                using var ic = new Icon(path, new Size(256, 256));
                return ic.ToBitmap();
            }
            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            using var img = Image.FromStream(ms);
            return new Bitmap(img);
        }
        catch { return null; }
    }

    public static Bitmap Render(Folder f, int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        if (f.IconKind == "file" && DrawImageFile(g, f.IconValue, s)) return bmp;

        DrawTile(g, f, s);
        if (f.IconKind == "emoji" && !string.IsNullOrWhiteSpace(f.IconValue)) DrawGlyph(g, f.IconValue, s);
        else DrawAppGrid(g, f, s);
        return bmp;
    }

    static bool DrawImageFile(Graphics g, string path, int s)
    {
        using var img = LoadImage(path);
        if (img == null || img.Width <= 0 || img.Height <= 0) return false;

        float sc = Math.Min((float)s / img.Width, (float)s / img.Height);
        float w = img.Width * sc, h = img.Height * sc;
        var dest = Rectangle.Round(new RectangleF((s - w) / 2f, (s - h) / 2f, w, h));

        using var ia = new ImageAttributes();
        ia.SetWrapMode(WrapMode.TileFlipXY); // avoids a faint edge halo when scaling
        g.DrawImage(img, dest, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
        return true;
    }

    static void DrawTile(Graphics g, Folder f, int s)
    {
        var c = ThemeManager.FolderColor(f);
        var rect = new RectangleF(s * 0.03f, s * 0.03f, s * 0.94f, s * 0.94f);
        using var path = Gfx.Round(rect, s * 0.23f);

        var top = c.Lighten(0.20);
        var bottom = c.Darken(0.42);
        using (var br = new LinearGradientBrush(rect, top, bottom, 90f))
            g.FillPath(br, path);

        // Soft highlight across the top half, like Windows 11 icon tiles.
        using (var hp = Gfx.Round(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height * 0.52f), s * 0.2f))
        using (var hb = new LinearGradientBrush(rect, Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
            g.FillPath(hb, hp);

        using var pen = new Pen(Color.FromArgb(72, 255, 255, 255), Math.Max(1f, s / 64f));
        g.DrawPath(pen, path);
    }

    // Emoji are drawn as single-colour (white) glyphs: GDI+ cannot render colour emoji,
    // and white-on-tile also matches the other folder icons. Use an image file for full colour.
    static void DrawGlyph(Graphics g, string text, int s)
    {
        text = text.Replace("️", "").Trim();
        if (text.Length == 0) return;

        float px = s * (text.Length <= 2 ? 0.50f : text.Length <= 4 ? 0.36f : 0.26f);
        using var font = new Font("Segoe UI Emoji", px, FontStyle.Regular, GraphicsUnit.Pixel);
        using var sf = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        using var br = new SolidBrush(Color.FromArgb(240, 255, 255, 255));
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.DrawString(text, font, br, new RectangleF(0, s * 0.02f, s, s), sf);
    }

    static void DrawAppGrid(Graphics g, Folder f, int s)
    {
        var icons = f.Items.Take(4).Select(i => IconUtil.Get(i.Target)).ToList();
        if (icons.Count == 0)
        {
            using var white = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
            using var tab = Gfx.Round(new RectangleF(s * 0.22f, s * 0.27f, s * 0.26f, s * 0.14f), s * 0.03f);
            using var body = Gfx.Round(new RectangleF(s * 0.22f, s * 0.34f, s * 0.56f, s * 0.38f), s * 0.05f);
            g.FillPath(white, tab);
            g.FillPath(white, body);
        }
        else if (icons.Count == 1)
        {
            float c = s * 0.52f;
            g.DrawImage(icons[0], (s - c) / 2, (s - c) / 2, c, c);
        }
        else
        {
            float m = s * 0.17f, gap = s * 0.08f, cell = (s - 2 * m - gap) / 2;
            for (int i = 0; i < icons.Count; i++)
                g.DrawImage(icons[i], m + (i % 2) * (cell + gap), m + (i / 2) * (cell + gap), cell, cell);
        }
        foreach (var ic in icons) ic.Dispose();
    }

    public static void BuildIcon(Folder f)
    {
        Directory.CreateDirectory(Config.IconsDir);
        int[] sizes = { 16, 24, 32, 48, 64, 256 };
        var pngs = new List<byte[]>();
        foreach (var s in sizes)
        {
            using var bmp = Render(f, s);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            pngs.Add(ms.ToArray());
        }

        using var fs = new MemoryStream();
        using var bw = new BinaryWriter(fs);
        bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
            bw.Write(dim); bw.Write(dim); bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((short)1); bw.Write((short)32);
            bw.Write(pngs[i].Length); bw.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) bw.Write(p);
        bw.Flush();
        File.WriteAllBytes(IcoPath(f), fs.ToArray());
    }
}

/// <summary>Creates a .lnk with a unique AppUserModelID so every folder gets its own taskbar slot.</summary>
static class ShortcutWriter
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLinkCoClass { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder f, int cch, IntPtr pfd, uint flags);
        void GetIDList(out IntPtr p);
        void SetIDList(IntPtr p);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string s);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string s);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string s);
        void GetHotkey(out short h);
        void SetHotkey(short h);
        void GetShowCmd(out int c);
        void SetShowCmd(int c);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int cch, out int i);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string s, int i);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string s, uint r);
        void Resolve(IntPtr h, uint f);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string s);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010B-0000-0000-C000-000000000046")]
    interface IPersistFile
    {
        void GetClassID(out Guid id);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string f, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string f, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string f);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string f);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct PropertyKey { public Guid Fmtid; public uint Pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant { public ushort Vt, R1, R2, R3; public IntPtr P; public IntPtr P2; }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    interface IPropertyStore
    {
        void GetCount(out uint c);
        void GetAt(uint i, out PropertyKey k);
        void GetValue(ref PropertyKey k, out PropVariant v);
        void SetValue(ref PropertyKey k, ref PropVariant v);
        void Commit();
    }

    public static void Create(string lnkPath, string exe, string args, string icon, string desc, string appId)
    {
        var link = (IShellLinkW)new ShellLinkCoClass();
        link.SetPath(exe);
        link.SetArguments(args);
        link.SetWorkingDirectory(System.IO.Path.GetDirectoryName(exe) ?? "");
        link.SetDescription(desc);
        link.SetIconLocation(icon, 0);

        var store = (IPropertyStore)link;
        var key = new PropertyKey { Fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Pid = 5 }; // System.AppUserModel.ID
        var pv = new PropVariant { Vt = 31, P = Marshal.StringToCoTaskMemUni(appId) }; // VT_LPWSTR
        store.SetValue(ref key, ref pv);
        store.Commit();
        Marshal.FreeCoTaskMem(pv.P);

        ((IPersistFile)link).Save(lnkPath, true);
        Marshal.ReleaseComObject(link);
    }
}
