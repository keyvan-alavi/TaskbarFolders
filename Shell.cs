using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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

    [StructLayout(LayoutKind.Sequential)]
    struct Margins { public int L, R, T, B; }

    public static void SetDarkTitle(IntPtr h, bool dark)
    {
        int d = dark ? 1 : 0;
        DwmSetWindowAttribute(h, 20, ref d, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
    }

    /// <summary>Acrylic backdrop + rounded corners (Windows 11 22H2+). Returns false if unsupported.</summary>
    public static bool ApplyBackdrop(IntPtr h, bool dark)
    {
        SetDarkTitle(h, dark);
        int corner = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(h, 33, ref corner, 4);
        var m = new Margins { L = -1, R = -1, T = -1, B = -1 };
        DwmExtendFrameIntoClientArea(h, ref m);
        int type = 3; // DWMSBT_TRANSIENTWINDOW (acrylic)
        return DwmSetWindowAttribute(h, 38, ref type, 4) == 0;
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
        try
        {
            var p = Environment.ExpandEnvironmentVariables(target);
            if (File.Exists(p))
            {
                using var ic = Icon.ExtractAssociatedIcon(p);
                if (ic != null) return ic.ToBitmap();
            }
        }
        catch { }
        return SystemIcons.Application.ToBitmap();
    }
}

static class Gfx
{
    public static GraphicsPath Round(RectangleF r, float rad)
    {
        float d = rad * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

/// <summary>Builds the .ico used by a folder's taskbar shortcut: a tinted tile with a 2x2 grid of its apps.</summary>
static class IconBuilder
{
    public static string IcoPath(Folder f) => System.IO.Path.Combine(Config.IconsDir, f.Id + ".ico");

    static Color Hsv(double h, double s, double v)
    {
        int hi = (int)(h / 60) % 6;
        double f = h / 60 - Math.Floor(h / 60);
        double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        var (r, g, b) = hi switch
        {
            0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t),
            3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q)
        };
        return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }

    public static Bitmap Render(Folder f, int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        int hue = 210;
        try { hue = Convert.ToInt32(f.Id[..Math.Min(4, f.Id.Length)], 16) % 360; } catch { }

        var rect = new RectangleF(s * 0.03f, s * 0.03f, s * 0.94f, s * 0.94f);
        using (var path = Gfx.Round(rect, s * 0.22f))
        {
            using (var br = new LinearGradientBrush(rect, Hsv(hue, 0.55, 0.60), Hsv(hue, 0.75, 0.26), 90f))
                g.FillPath(br, path);
            using var pen = new Pen(Color.FromArgb(70, 255, 255, 255), Math.Max(1f, s / 64f));
            g.DrawPath(pen, path);
        }

        var icons = f.Items.Take(4).Select(i => IconUtil.Get(i.Target)).ToList();
        if (icons.Count == 0)
        {
            using var white = new SolidBrush(Color.FromArgb(225, 255, 255, 255));
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
        return bmp;
    }

    public static void BuildIcon(Folder f)
    {
        Directory.CreateDirectory(Config.IconsDir);
        int[] sizes = { 16, 32, 48, 256 };
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
