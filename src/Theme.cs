// Dark theme palette, rounded-corner helpers, Win32 interop for the dark title bar.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowsStalker
{
    static class NativeMethods
    {
        public const int HWND_BROADCAST = 0xffff;
        [DllImport("user32.dll")]
        public static extern int RegisterWindowMessage(string message);
        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        // EM_SETCUEBANNER: gray placeholder text inside an empty TextBox
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        public const int EM_SETCUEBANNER = 0x1501;
        // A modal dialog owned by the window disables it at the Win32 level — the
        // reliable "is some dialog open?" probe (Form.Modal etc. do not see
        // MessageBox), used to postpone timer-triggered work like the auto clean
        [DllImport("user32.dll")]
        public static extern bool IsWindowEnabled(IntPtr hWnd);
        // Ground truth for "is the window really minimized": ShowInTaskbar
        // recreates the handle, after which Form.WindowState can report Normal
        // while the real window is still iconic at -32000 (see RestoreFromTray)
        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        public const int SW_RESTORE = 9;
        // Used by the second-instance handshake: HWND_BROADCAST only reaches
        // UNOWNED top-level windows, and a tray-hidden form is an owned window
        // (that is how ShowInTaskbar=false hides it) — the show-yourself message
        // must be posted to the running instance's windows directly.
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        // The one part of a ListView that cannot be owner-drawn is its scrollbar,
        // which stays system-light however dark the rest is painted. Handing the
        // control the shell dark-mode theme is what darkens it (Win10 1809+);
        // on anything older the call simply does nothing.
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hWnd, string app, string idList);
    }

    // Amber CRT — the wasteland terminal look, carried over from WastelandNext's
    // renderer palette (src/renderer/styles.css :root) so the two apps read as
    // the same machine: near-black glass, phosphor amber, dashed rules, square
    // corners and scanlines. Red stays reserved for genuine warnings.
    static class Theme
    {
        public static readonly Color Bg        = Color.FromArgb(5, 5, 5);       // --bg
        public static readonly Color Card      = Color.FromArgb(10, 10, 10);    // --bg-panel
        public static readonly Color CardLine  = Color.FromArgb(90, 61, 0);     // --amber-faint
        // Secondary buttons sitting ON a card: filling them with Card makes them
        // vanish into it and read as plain labels, so they get their own tone.
        public static readonly Color Subtle    = Color.FromArgb(26, 22, 14);    // --summary-bg, warmed
        public static readonly Color LogBg     = Color.FromArgb(0, 0, 0);       // --bg-deep
        public static readonly Color Text      = Color.FromArgb(255, 176, 0);   // --amber
        public static readonly Color Muted     = Color.FromArgb(153, 106, 0);   // --amber-dim
        public static readonly Color Accent    = Color.FromArgb(255, 176, 0);   // --amber
        public static readonly Color AccentHot = Color.FromArgb(255, 208, 96);  // --amber-bright
        public static readonly Color Good      = Color.FromArgb(102, 204, 102); // --ok
        public static readonly Color Warn      = Color.FromArgb(255, 96, 32);   // --warn
        public static readonly Color Danger    = Color.FromArgb(216, 68, 47);   // --bar-life
        public static readonly Color DangerHot = Color.FromArgb(240, 96, 74);
        public static readonly Color Disabled  = Color.FromArgb(112, 78, 8);
        public static readonly Color Btn       = Color.FromArgb(51, 51, 51);    // --btn-bg
        public static readonly Color BtnHot    = Color.FromArgb(68, 68, 68);    // --btn-bg-hover
        public static readonly Color BtnText   = Color.FromArgb(255, 176, 0);
        // Text laid ON a filled amber block. White on amber is unreadable; the
        // terminal cuts its bright fills out of the background colour instead.
        public static readonly Color OnAccent  = Color.FromArgb(8, 6, 2);
        public const int Radius = 0; // --radius: 0 — nothing on a terminal is rounded
        public const float Track = 1.4f; // letter-spacing, the CSS 0.12em in pixels

        // The terminal monospace stack, first one installed wins. Mono glyphs run
        // wider than Segoe UI at the same point size and this window is a fixed
        // 1024x706 of hand-placed controls, so the compensation is one knob here
        // rather than a fudged number at each of the ~40 call sites.
        const float FontScale = 0.88f;
        static readonly string[] MonoStack =
            { "Cascadia Mono", "Consolas", "DejaVu Sans Mono", "Courier New" };
        static string family;

        public static string Family
        {
            get
            {
                if (family == null)
                {
                    family = "Courier New"; // on every Windows since 3.1
                    foreach (string name in MonoStack)
                    {
                        try
                        {
                            // The FontFamily ctor is the probe: it throws when the
                            // family is missing rather than silently substituting,
                            // which is what the Font ctor would do.
                            using (var probe = new FontFamily(name)) { family = probe.Name; break; }
                        }
                        catch (ArgumentException) { }
                    }
                }
                return family;
            }
        }

        public static Font Ui(float size) { return new Font(Family, size * FontScale); }
        public static Font UiBold(float size) { return new Font(Family, size * FontScale, FontStyle.Bold); }
        // Pixel-unit variants: the gauge scales its type with the control
        public static Font UiPx(float px) { return new Font(Family, px * FontScale, GraphicsUnit.Pixel); }
        public static Font UiBoldPx(float px)
        {
            return new Font(Family, px * FontScale, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            if (d <= 0 || r.Width <= d || r.Height <= d)
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

        static TextureBrush scanlines;

        // CRT scanlines: one darkened row in every three, from a cached 1x3
        // texture — cheap enough to lay over every surface on every paint.
        // phase is the surface screen Y, so the lines stay in step across control
        // boundaries instead of restarting at each control origin.
        public static void Scanlines(Graphics g, Rectangle r, int phase)
        {
            if (scanlines == null)
            {
                var tile = new Bitmap(1, 3);
                tile.SetPixel(0, 2, Color.FromArgb(48, 0, 0, 0));
                scanlines = new TextureBrush(tile);
            }
            scanlines.ResetTransform();
            scanlines.TranslateTransform(0, -(phase % 3));
            g.FillRectangle(scanlines, r);
        }

        // Clear a control to a flat colour and lay the scanlines over it. The
        // handle exists by paint time, so PointToScreen is safe here.
        public static void Surface(Control c, Graphics g, Color back)
        {
            g.Clear(back);
            Scanlines(g, c.ClientRectangle, Phase(c));
        }

        public static int Phase(Control c)
        {
            try { return c.PointToScreen(Point.Empty).Y; }
            catch { return 0; }
        }

        public static void PaintCard(Graphics g, int w, int h) { PaintCard(g, w, h, 0); }

        // Panel surface: a flat slab, scanlines, a dashed amber rule around it and
        // bright corner brackets. No drop shadow and no rounding — the terminal
        // has no depth, it has phosphor and hard edges. The rectangle keeps the
        // old shadow inset so the hand-placed page layouts do not shift.
        public static void PaintCard(Graphics g, int w, int h, int phase)
        {
            var r = new Rectangle(1, 0, w - 4, h - 6);
            if (r.Width <= 0 || r.Height <= 0) return;
            SmoothingMode was = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None; // 1px rules go blurry under AA
            using (var b = new SolidBrush(Card)) g.FillRectangle(b, r);
            Region clip = g.Clip;
            g.SetClip(r);
            Scanlines(g, r, phase);
            g.Clip = clip;
            using (var pen = new Pen(CardLine))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
            }
            Brackets(g, r, Muted, 12);
            g.SmoothingMode = was;
        }

        // The HUD framing every panel in the wasteland UI wears: an L at each
        // corner, solid where the border itself is dashed.
        public static void Brackets(Graphics g, Rectangle r, Color c, int leg)
        {
            int x0 = r.X, y0 = r.Y, x1 = r.Right - 1, y1 = r.Bottom - 1;
            if (r.Width < leg * 3 || r.Height < leg * 2) return;
            using (var pen = new Pen(c))
            {
                g.DrawLines(pen, new Point[] { new Point(x0, y0 + leg), new Point(x0, y0), new Point(x0 + leg, y0) });
                g.DrawLines(pen, new Point[] { new Point(x1 - leg, y0), new Point(x1, y0), new Point(x1, y0 + leg) });
                g.DrawLines(pen, new Point[] { new Point(x1, y1 - leg), new Point(x1, y1), new Point(x1 - leg, y1) });
                g.DrawLines(pen, new Point[] { new Point(x0 + leg, y1), new Point(x0, y1), new Point(x0, y1 - leg) });
            }
        }

        // Letter-spaced text, the 0.12em tracking of the terminal headings. GDI+
        // has no such setting, so the run is drawn a character at a time;
        // MeasureTracked is the matching width, so auto-sized controls agree with
        // what actually gets painted.
        const TextFormatFlags Plain = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        // One cell of the grid. Every glyph in a monospace face has the same
        // advance, so ten of them measured together and divided gives it without
        // the padding TextRenderer adds once per measuring call — charging that
        // padding per character is what would otherwise triple the tracking.
        const string Ruler = "MMMMMMMMMM";

        static float Advance(Font f)
        {
            return TextRenderer.MeasureText(Ruler, f, Size.Empty, Plain).Width / 10f;
        }

        public static void DrawTracked(Graphics g, string s, Font f, Point at, Color c, float track)
        {
            if (string.IsNullOrEmpty(s)) return;
            float step = Advance(f) + track;
            float x = at.X;
            for (int i = 0; i < s.Length; i++, x += step)
                TextRenderer.DrawText(g, s.Substring(i, 1), f, new Point((int)Math.Round(x), at.Y), c, Plain);
        }

        public static int MeasureTracked(string s, Font f, float track)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return (int)Math.Ceiling(s.Length * (Advance(f) + track) - track);
        }

        // Tracked text centred in a box, and the same left-aligned — for the
        // buttons and tabs whose labels are set in caps. A label too wide to be
        // tracked falls back to the plain renderer, which can at least ellipsise
        // it: DrawTracked would happily spill outside the control.
        public static void DrawTrackedCentered(Graphics g, string s, Font f, Rectangle r, Color c, float track)
        {
            int w = MeasureTracked(s, f, track);
            if (w > r.Width)
            {
                TextRenderer.DrawText(g, s, f, r, c, TextFormatFlags.HorizontalCenter
                    | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                return;
            }
            DrawTracked(g, s, f, new Point(r.X + (r.Width - w) / 2, r.Y + (r.Height - LineHeight(g, f)) / 2), c, track);
        }

        public static void DrawTrackedLeft(Graphics g, string s, Font f, Rectangle r, Color c, float track)
        {
            if (MeasureTracked(s, f, track) > r.Width)
            {
                TextRenderer.DrawText(g, s, f, r, c, TextFormatFlags.Left
                    | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                return;
            }
            DrawTracked(g, s, f, new Point(r.X, r.Y + (r.Height - LineHeight(g, f)) / 2), c, track);
        }

        static int LineHeight(Graphics g, Font f)
        {
            return TextRenderer.MeasureText(g, "X", f, Size.Empty, Plain).Height;
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // GDI COLORREF is 0x00BBGGRR — not the ARGB order Color uses
        static int ToColorRef(Color c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }

        public static void DarkTitleBar(Form f) { DarkTitleBar(f, false); }

        // Dark window title bar (Win10 1903+); without it the frame stays white.
        // On Windows 11 the caption is additionally painted in the app's own
        // background colour so the title bar does not read as a foreign gray strip;
        // those attributes simply fail on Windows 10 and the dark caption stays.
        // hideCaptionText paints the caption text in the caption colour, making it
        // invisible while Form.Text keeps naming the window for the taskbar,
        // Alt+Tab and screen readers — used by the main window, whose in-window
        // header already carries the branding. Dialogs keep their visible titles.
        public static void DarkTitleBar(Form f, bool hideCaptionText)
        {
            EventHandler apply = delegate
            {
                try
                {
                    int on = 1;
                    if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0)
                        DwmSetWindowAttribute(f.Handle, 19, ref on, 4); // older Win10 builds
                    int caption = ToColorRef(Bg);
                    DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);  // DWMWA_CAPTION_COLOR (Win11 22000+)
                    int text = ToColorRef(hideCaptionText ? Bg : Text);
                    DwmSetWindowAttribute(f.Handle, 36, ref text, 4);     // DWMWA_TEXT_COLOR
                    int border = ToColorRef(CardLine);
                    DwmSetWindowAttribute(f.Handle, 34, ref border, 4);   // DWMWA_BORDER_COLOR
                }
                catch { }
            };
            if (f.IsHandleCreated) apply(null, EventArgs.Empty);
            else f.HandleCreated += apply;
        }
    }
}
