// Dark theme palette, rounded-corner helpers, Win32 interop for the dark title bar.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Cleaner
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
        // A ListView's header is its own child window, so Invalidate() on the
        // control never reaches it — without this the sort arrow only appeared
        // once something else happened to force the header to repaint.
        public const int LVM_GETHEADER = 0x1000 + 31;
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool InvalidateRect(IntPtr hWnd, IntPtr rect, bool erase);
    }

    // Neutral dark UI, shared with the AV project next door (../AV/src/Theme.cs)
    // so the two tools read as one suite: navy-tinted near-black surfaces, a
    // single blue accent, rounded cards with a soft drop shadow and Segoe UI
    // throughout. This replaced an amber CRT skin - phosphor text, scanlines over
    // every surface, dashed rules, square corners and letter-spaced monospace -
    // which read as a prop rather than as a maintenance tool. Red stays reserved
    // for genuine warnings.
    static class Theme
    {
        public static readonly Color Bg        = Color.FromArgb(16, 18, 24);    // window background
        public static readonly Color Card      = Color.FromArgb(30, 33, 42);    // cards
        public static readonly Color CardLine  = Color.FromArgb(48, 52, 64);    // hairline card border
        // Secondary buttons sitting ON a card: filling them with Card makes them
        // vanish into it and read as plain labels, so they get their own tone.
        public static readonly Color Subtle    = Color.FromArgb(44, 48, 60);
        public static readonly Color LogBg     = Color.FromArgb(12, 13, 18);    // list/log background
        public static readonly Color Text      = Color.FromArgb(232, 234, 240);
        public static readonly Color Muted     = Color.FromArgb(148, 155, 170);
        public static readonly Color Accent    = Color.FromArgb(66, 133, 255);  // blue
        public static readonly Color AccentHot = Color.FromArgb(108, 160, 255);
        public static readonly Color Good      = Color.FromArgb(48, 199, 110);
        public static readonly Color Warn      = Color.FromArgb(232, 197, 71);
        public static readonly Color Danger    = Color.FromArgb(239, 68, 68);
        public static readonly Color DangerHot = Color.FromArgb(248, 113, 113);
        public static readonly Color Disabled  = Color.FromArgb(92, 97, 108);   // clearly gray, not just faded
        public static readonly Color Btn       = Color.FromArgb(216, 219, 226); // light buttons
        public static readonly Color BtnHot    = Color.FromArgb(233, 235, 240);
        public static readonly Color BtnText   = Color.FromArgb(51, 54, 62);
        // Text laid ON a filled accent or danger block: both are saturated enough
        // to carry white, which the amber fills they replaced were not.
        public static readonly Color OnAccent  = Color.FromArgb(255, 255, 255);
        public const int Radius = 12;      // card corner radius
        public const int RadiusSmall = 4;  // buttons, checkboxes, meter cells

        // Segoe UI on any Windows this app runs on; the rest is insurance. The
        // scale knob stays at 1 - the hand-placed 1024x706 layout was tuned
        // against a monospace face at 0.88, and Segoe UI at full size measures to
        // very nearly the same width, so the pages line up without retuning.
        const float FontScale = 1f;
        static readonly string[] UiStack =
            { "Segoe UI", "Segoe UI Variable Text", "Tahoma", "Arial" };
        static string family;

        public static string Family
        {
            get
            {
                if (family == null)
                {
                    family = "Arial"; // on every Windows since 3.1
                    foreach (string name in UiStack)
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

        // Clears a control to a flat colour. Kept as a named call rather than
        // inlined into every OnPaint because this is where the scanline overlay
        // used to be laid over the fill, and it is the seam to reach for if the
        // surfaces ever need texture again.
        public static void Surface(Control c, Graphics g, Color back)
        {
            g.Clear(back);
        }

        // Elevated card: a soft drop shadow under the panel, the fill, a hairline
        // border and a 1px top highlight - depth on a dark background. Drawn
        // inside the control's own bounds, leaving room for the shadow, and the
        // rectangle keeps the inset the hand-placed page layouts were tuned to.
        public static void PaintCard(Graphics g, int w, int h)
        {
            var r = new RectangleF(1.5f, 0.5f, w - 4, h - 6);
            if (r.Width <= 0 || r.Height <= 0) return;
            SmoothingMode was = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 1; i <= 4; i++)
                using (var path = Round(new RectangleF(r.X, r.Y + i, r.Width, r.Height), Radius))
                using (var b = new SolidBrush(Color.FromArgb(13, 0, 0, 0)))
                    g.FillPath(b, path);
            using (var path = Round(r, Radius))
            {
                using (var b = new SolidBrush(Card)) g.FillPath(b, path);
                using (var pen = new Pen(CardLine)) g.DrawPath(pen, path);
            }
            using (var hl = new Pen(Color.FromArgb(16, 255, 255, 255)))
                g.DrawLine(hl, r.X + Radius, r.Y + 1, r.Right - Radius, r.Y + 1);
            g.SmoothingMode = was;
        }

        // Label drawing. These wrapped a hand-rolled letter-spacing routine that
        // stepped by one monospace advance per glyph - correct for the terminal
        // face, nonsense for a proportional one - so they are now thin wrappers
        // that keep the call sites, and the auto-sizing controls that have to
        // agree with what actually gets painted, reading the same.
        const TextFormatFlags Plain = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        public static int MeasureLabel(string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return TextRenderer.MeasureText(s, f, Size.Empty, Plain).Width;
        }

        public static void DrawLabel(Graphics g, string s, Font f, Point at, Color c)
        {
            if (string.IsNullOrEmpty(s)) return;
            TextRenderer.DrawText(g, s, f, at, c, Plain);
        }

        public static void DrawLabelCentered(Graphics g, string s, Font f, Rectangle r, Color c)
        {
            TextRenderer.DrawText(g, s, f, r, c, TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        public static void DrawLabelLeft(Graphics g, string s, Font f, Rectangle r, Color c)
        {
            TextRenderer.DrawText(g, s, f, r, c, TextFormatFlags.Left
                | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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
