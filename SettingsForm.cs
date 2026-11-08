using System.Diagnostics;

namespace TaskbarFolders;

/// <summary>A rounded card panel used by the settings page.</summary>
sealed class CardPanel : Panel
{
    public CardPanel()
    {
        DoubleBuffered = true;
        BackColor = ThemeManager.P.Panel;
        Padding = new Padding(16);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var path = Gfx.Round(new RectangleF(0, 0, Width, Height), 14);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = ThemeManager.P;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var b = new SolidBrush(p.Panel);
        e.Graphics.FillRectangle(b, ClientRectangle);
        using var path = Gfx.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 14);
        using var pen = new Pen(p.Border, 1);
        e.Graphics.DrawPath(pen, path);
    }
}

sealed class ThemeCard : Control
{
    static readonly Font TitleFont = new("Segoe UI Semibold", 10.5f);
    static readonly Font SubFont = new("Segoe UI", 8.25f);

    public string ThemeId = "system";
    public string Title = "";
    public string Sub = "";
    public Palette Preview = ThemeManager.P;

    public ThemeCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Size = new Size(292, 70);
        Margin = new Padding(4);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = ThemeManager.P;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? p.Panel);

        bool sel = ThemeManager.Cfg?.Settings.Theme.Equals(ThemeId, StringComparison.OrdinalIgnoreCase) == true;
        var r = new RectangleF(1, 1, Width - 2, Height - 2);
        using var path = Gfx.Round(r, 10);
        using var fill = new SolidBrush(p.Card);
        g.FillPath(fill, path);
        using var pen = new Pen(sel ? p.Accent : p.Border, sel ? 2f : 1f);
        g.DrawPath(pen, path);

        var mini = new RectangleF(12, 11, 46, 46);
        using (var mp = Gfx.Round(mini, 8))
        using (var mb = new SolidBrush(Preview.Bg))
            g.FillPath(mb, mp);
        using (var mp = Gfx.Round(new RectangleF(mini.X, mini.Y + 24, mini.Width, 22), 8))
        using (var mb = new SolidBrush(Preview.Panel))
            g.FillPath(mb, mp);
        using (var dot = new SolidBrush(Preview.Accent))
            g.FillEllipse(dot, mini.X + 8, mini.Y + 8, 12, 12);
        using (var dot2 = new SolidBrush(Preview.Text.WithAlpha(190)))
            g.FillEllipse(dot2, mini.X + 26, mini.Y + 12, 7, 7);

        TextRenderer.DrawText(g, Title, TitleFont,
            new Rectangle(68, 12, Width - 78, 22), p.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, Sub, SubFont,
            new Rectangle(68, 34, Width - 78, 30), p.SubText,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);

        if (sel)
        {
            using var check = new SolidBrush(p.Accent);
            g.FillEllipse(check, Width - 26, Height - 26, 14, 14);
            using var pen2 = new Pen(p.OnAccent, 2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            g.DrawLines(pen2, new[]
            {
                new PointF(Width - 23, Height - 19),
                new PointF(Width - 20, Height - 16),
                new PointF(Width - 14, Height - 22)
            });
        }
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        var cfg = ThemeManager.Cfg;
        if (cfg == null) return;
        cfg.Settings.Theme = ThemeId;
        cfg.Save();
        ThemeManager.Init(cfg);
        var form = FindForm();
        if (form != null)
        {
            ThemeManager.Apply(form);
            form.Refresh();
        }
    }
}

/// <summary>The settings page: themes, open animation, shell integration and contact links.</summary>
sealed class SettingsForm : Form
{
    readonly Config cfg;

    public SettingsForm(Config config)
    {
        cfg = config;
        var p = ThemeManager.P;

        Text = "Settings";
        ClientSize = new Size(700, 760);
        MinimumSize = new Size(660, 520);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = p.Bg;
        ForeColor = p.Text;
        DoubleBuffered = true;

        var header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = p.Bg };
        var title = new Label
        {
            Text = "Settings", Left = 26, Top = 18, AutoSize = true,
            Font = new Font("Segoe UI Semibold", 19f)
        };
        var sub = new Label
        {
            Text = "Appearance, taskbar behaviour and contact", Left = 29, Top = 56, AutoSize = true,
            ForeColor = p.SubText, Font = new Font("Segoe UI", 9f), Tag = "sub"
        };
        var accentLine = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = p.Border };
        header.Controls.AddRange(new Control[] { title, sub, accentLine });

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = p.Bg };
        var close = new FlatButton { Text = "Done", Width = 100, Height = 34, Primary = true, Margin = new Padding(0) };
        close.Click += (_, _) => Close();
        var footerRight = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, Width = 128, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 14, 20, 0), BackColor = p.Bg
        };
        footerRight.Controls.Add(close);
        var ver = new Label
        {
            Text = $"Taskbar Folders v{Program.Version}  ·  by Keyvan Alavi",
            Left = 26, Top = 22, AutoSize = true, ForeColor = p.SubText, Font = new Font("Segoe UI", 8.5f), Tag = "sub"
        };
        footer.Controls.Add(footerRight);
        footer.Controls.Add(ver);

        var content = new Panel
        {
            Dock = DockStyle.Fill, AutoScroll = true, BackColor = p.Bg,
            Padding = new Padding(10, 4, 10, 10)
        };

        var themeCard = BuildThemeCard(p);
        var animCard = BuildAnimationCard(p);
        var integrationCard = BuildIntegrationCard(p);
        var contactCard = BuildContactCard(p);
        content.Controls.AddRange(new Control[] { themeCard, animCard, integrationCard, contactCard });
        content.Resize += (_, _) => LayoutCards(content, themeCard, animCard, integrationCard, contactCard);
        Shown += (_, _) =>
        {
            LayoutCards(content, themeCard, animCard, integrationCard, contactCard);
        };

        Controls.Add(content);
        Controls.Add(footer);
        Controls.Add(header);

        ThemeManager.Apply(this);
        title.ForeColor = p.Text;
        sub.ForeColor = p.SubText;
        header.BackColor = p.Bg;
        footer.BackColor = p.Bg;
        content.BackColor = p.Bg;

        HandleCreated += (_, _) => Native.SetDarkTitle(Handle, p.Dark);
    }

    static void LayoutCards(Panel host, params Control[] cards)
    {
        int top = 6;
        int width = Math.Max(560, host.ClientSize.Width - host.Padding.Horizontal - 6);
        foreach (var c in cards)
        {
            c.Left = 8;
            c.Top = top;
            c.Width = width;
            top += c.Height + 10;
        }
    }

    static Label SectionTitle(string text, int top, Palette p) => new()
    {
        Text = text, Left = 18, Top = top, AutoSize = true,
        Font = new Font("Segoe UI Semibold", 12.5f), ForeColor = p.Text
    };

    static Label SectionSub(string text, int top, int width, Palette p) => new()
    {
        Text = text, Left = 20, Top = top, Width = width, Height = 24,
        ForeColor = p.SubText, Font = new Font("Segoe UI", 8.75f), AutoEllipsis = true, Tag = "sub"
    };

    CardPanel BuildThemeCard(Palette p)
    {
        var card = new CardPanel { Height = 408 };
        card.Controls.Add(SectionTitle("Theme", 16, p));
        card.Controls.Add(SectionSub("Windows 11 theme with your accent colour, plus light, dark and four hand-made themes.", 44, 620, p));

        var flow = new FlowLayoutPanel { Left = 14, Top = 72, Width = 620, Height = 316, BackColor = p.Panel };
        foreach (var (id, name, desc) in ThemeManager.Themes)
        {
            var preview = ThemeManager.Build(id);
            var tc = new ThemeCard { ThemeId = id, Title = name, Sub = desc, Preview = preview };
            flow.Controls.Add(tc);
        }
        card.Controls.Add(flow);
        return card;
    }

    CardPanel BuildAnimationCard(Palette p)
    {
        var card = new CardPanel { Height = 176 };
        card.Controls.Add(SectionTitle("Open animation", 16, p));
        card.Controls.Add(SectionSub("How the folder menu appears when you click it.", 44, 600, p));

        var ids = new[] { "slidefade", "slide", "fade", "zoom", "none" };
        var combo = new OptionButton
        {
            Left = 20, Top = 86, Width = 270,
            Options = new[] { "Slide + fade (recommended)", "Slide up", "Fade", "Zoom", "None" },
            SelectedIndex = Math.Max(0, Array.IndexOf(ids, cfg.Settings.Animation))
        };
        combo.SelectionChanged += (_, _) =>
        {
            cfg.Settings.Animation = ids[Math.Clamp(combo.SelectedIndex, 0, ids.Length - 1)];
            cfg.Save();
        };

        var speed = new OptionButton
        {
            Left = 344, Top = 86, Width = 170,
            Options = new[] { "Fast · 140 ms", "Normal · 200 ms", "Relaxed · 300 ms" },
            SelectedIndex = cfg.Settings.AnimationMs <= 160 ? 0 : cfg.Settings.AnimationMs <= 240 ? 1 : 2
        };
        speed.SelectionChanged += (_, _) =>
        {
            cfg.Settings.AnimationMs = speed.SelectedIndex switch { 0 => 140, 2 => 300, _ => 200 };
            cfg.Save();
        };

        card.Controls.Add(new Label { Text = "Animation", Left = 20, Top = 66, AutoSize = true, ForeColor = p.SubText, Font = new Font("Segoe UI", 8.5f), Tag = "sub" });
        card.Controls.Add(combo);
        card.Controls.Add(new Label { Text = "Speed", Left = 344, Top = 66, AutoSize = true, ForeColor = p.SubText, Font = new Font("Segoe UI", 8.5f), Tag = "sub" });
        card.Controls.Add(speed);
        card.Controls.Add(new Label
        {
            Text = "Each folder can override this: right-click a folder ▸ Open animation.",
            Left = 20, Top = 136, AutoSize = true, ForeColor = p.SubText, Font = new Font("Segoe UI", 8.5f), Tag = "sub"
        });
        return card;
    }

    CardPanel BuildIntegrationCard(Palette p)
    {
        var card = new CardPanel { Height = 168 };
        card.Controls.Add(SectionTitle("Windows integration", 16, p));

        var shell = new CheckBox
        {
            Text = "Add “Add to Taskbar Folder” to the right-click menu of apps, shortcuts and folders",
            Left = 20, Top = 56, Width = 600, Height = 24,
            Checked = cfg.Settings.ShellMenu,
            ForeColor = p.Text
        };
        shell.CheckedChanged += (_, _) =>
        {
            cfg.Settings.ShellMenu = shell.Checked;
            cfg.Save();
            ShellIntegration.Sync(shell.Checked);
        };

        var openData = new FlatButton { Text = "Open data folder", Left = 20, Top = 92, Width = 160, Height = 32 };
        openData.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(Config.Dir);
                Process.Start("explorer.exe", Config.Dir);
            }
            catch (Exception ex) { ErrorLog.Write(ex); }
        };

        var rebuild = new FlatButton { Text = "Rebuild taskbar icons", Left = 190, Top = 92, Width = 180, Height = 32 };
        rebuild.Click += (_, _) =>
        {
            foreach (var f in cfg.Folders)
            {
                try { Pinning.EnsureShortcut(f); } catch (Exception ex) { ErrorLog.Write(ex); }
            }
            Native.RefreshShellIcons();
            rebuild.Text = "Rebuilt ✓";
        };

        card.Controls.Add(shell);
        card.Controls.Add(openData);
        card.Controls.Add(rebuild);
        card.Controls.Add(new Label
        {
            Text = "This per-user menu entry lets you file an app from the Start menu, desktop or Explorer in one click. No admin rights needed.",
            Left = 20, Top = 132, Width = 620, Height = 26, ForeColor = p.SubText, Font = new Font("Segoe UI", 8.5f), Tag = "sub"
        });
        return card;
    }

    CardPanel BuildContactCard(Palette p)
    {
        var card = new CardPanel { Height = 250 };
        card.Controls.Add(SectionTitle("Contact & links", 16, p));
        card.Controls.Add(new Label
        {
            Text = "برای ارائه پیشنهادات و انتقادات با من در ارتباط باشید:",
            Left = 20, Top = 46, Width = 620, Height = 22, ForeColor = p.SubText, Tag = "sub",
            Font = new Font("Segoe UI", 9f), TextAlign = ContentAlignment.MiddleRight
        });

        var website = ContactButton("Website  ·  technokey.ir", "https://technokey.ir");
        website.Left = 20; website.Top = 78;
        var telegram = ContactButton("Telegram  ·  @Kewin_67", "https://t.me/Kewin_67");
        telegram.Left = 330; telegram.Top = 78;
        var email = ContactButton("Email  ·  key1.alavi@gmail.com", "mailto:key1.alavi@gmail.com");
        email.Left = 20; email.Top = 122;
        var github = ContactButton("GitHub  ·  keyvan-alavi", "https://github.com/keyvan-alavi");
        github.Left = 330; github.Top = 122;

        card.Controls.AddRange(new Control[] { website, telegram, email, github });
        card.Controls.Add(new Label
        {
            Text = "Made with ♥ for Windows 11  ·  Taskbar Folders is free to use.",
            Left = 20, Top = 178, Width = 620, Height = 24, ForeColor = p.SubText, Font = new Font("Segoe UI", 8.5f), Tag = "sub"
        });
        return card;
    }

    static FlatButton ContactButton(string text, string url)
    {
        var b = new FlatButton { Text = text, Width = 294, Height = 36 };
        b.Click += (_, _) => Program.OpenUrl(url);
        return b;
    }
}

