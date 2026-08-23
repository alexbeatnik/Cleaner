// The app mark, drawn with GDI+ — and the ICO writer that turns it into the
// executable's Win32 icon resource at build time (build.ps1 pass 1 runs
// WindowsStalker.exe --write-icon app.ico, pass 2 embeds the result).
// Keeping the icon generated rather than committed means the repository holds
// no binary assets and the taskbar icon can never drift from the in-app mark.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace WindowsStalker
{
    static class Brand
    {
        // Sizes baked into the icon resource. 256 is what Explorer's extra-large
        // view wants; 16/20/24 are the tray, taskbar and title bar.
        internal static readonly int[] IconSizes = new int[] { 16, 20, 24, 32, 48, 64, 128, 256 };

        // Radiation trefoil, ISO 361 geometry: a centre disc, then three
        // 60°-wide blades running from 1.5x to 5x the disc radius with 60° gaps
        // between them. r is the outer radius. GDI+ angles run clockwise from 3
        // o'clock, so -120..-60 is the blade pointing straight up and the gap
        // between the other two lands at the bottom — the orientation the real
        // sign has.
        internal static GraphicsPath Trefoil(float cx, float cy, float r)
        {
            float disc = r * 0.21f;   // centre dot
            float inner = r * 0.33f;  // where the blades begin
            var outer = new RectangleF(cx - r, cy - r, r * 2, r * 2);
            var hole = new RectangleF(cx - inner, cy - inner, inner * 2, inner * 2);
            var p = new GraphicsPath();
            p.AddEllipse(cx - disc, cy - disc, disc * 2, disc * 2);
            for (int i = 0; i < 3; i++)
            {
                float a = -120f + i * 120f;
                p.StartFigure();
                p.AddArc(outer, a, 60f);       // along the outer edge
                p.AddArc(hole, a + 60f, -60f); // and back along the inner one
                p.CloseFigure();
            }
            return p;
        }

        // The full mark: a scorched-dark badge carrying the radiation trefoil in
        // hazard amber, with a faint radioactive haze behind it — the Zone
        // warning-sign look. Drawn into an arbitrary rectangle so the same code
        // serves the icon resource, the tray icon and the in-window header.
        public static void PaintMark(Graphics g, RectangleF r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(r.Width, r.Height);
            var box = new RectangleF(r.X + (r.Width - s) / 2f, r.Y + (r.Height - s) / 2f, s, s);
            float cx = box.X + s * 0.5f, cy = box.Y + s * 0.5f;

            using (var path = Theme.Round(box, s * 0.24f))
            using (var brush = new LinearGradientBrush(box,
                       Color.FromArgb(52, 48, 38), Color.FromArgb(13, 14, 12), 55f))
                g.FillPath(brush, path);

            // The haze lifts the trefoil off the near-black field and keeps the
            // badge from dissolving into the app's own dark header. Radius stays
            // under half the box, so it never spills past the rounded corners.
            float gr = s * 0.46f;
            using (var glow = new GraphicsPath())
            {
                glow.AddEllipse(cx - gr, cy - gr, gr * 2, gr * 2);
                using (var haze = new PathGradientBrush(glow))
                {
                    haze.CenterColor = Color.FromArgb(70, 250, 204, 74);
                    haze.SurroundColors = new Color[] { Color.FromArgb(0, 250, 204, 74) };
                    g.FillPath(haze, glow);
                }
            }

            // Rim, inset by half its own width: a pen centred on the box edge
            // would put half the stroke outside the bitmap and lose it.
            float bw = Math.Max(1f, s * 0.028f);
            var rim = new RectangleF(box.X + bw / 2f, box.Y + bw / 2f, s - bw, s - bw);
            using (var path = Theme.Round(rim, s * 0.24f - bw / 2f))
            using (var pen = new Pen(Color.FromArgb(105, 240, 196, 84), bw))
                g.DrawPath(pen, path);

            using (var trefoil = Trefoil(cx, cy, s * 0.355f))
            using (var b = new LinearGradientBrush(box,
                       Color.FromArgb(255, 226, 88), Color.FromArgb(224, 142, 18), 90f))
                g.FillPath(b, trefoil);
        }

        public static Bitmap MarkBitmap(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                PaintMark(g, new RectangleF(0, 0, size, size));
            }
            return bmp;
        }

        // ICO container with one PNG-compressed entry per size. PNG entries are
        // understood by every Windows the .NET Framework 4.8 runtime installs on
        // (Vista+), and they keep 256x256 from bloating the file to a megabyte.
        internal static byte[] IcoBytes(int[] sizes)
        {
            var images = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = MarkBitmap(s))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    images.Add(ms.ToArray());
                }

            using (var outMs = new MemoryStream())
            using (var w = new BinaryWriter(outMs))
            {
                w.Write((short)0);              // reserved
                w.Write((short)1);              // type: 1 = icon
                w.Write((short)images.Count);
                int offset = 6 + 16 * images.Count;
                for (int i = 0; i < images.Count; i++)
                {
                    // 256 is stored as 0 in the single-byte width/height fields
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0);           // palette size (0 = truecolour)
                    w.Write((byte)0);           // reserved
                    w.Write((short)1);          // colour planes
                    w.Write((short)32);         // bits per pixel
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] img in images) w.Write(img);
                w.Flush();
                return outMs.ToArray();
            }
        }

        static Icon appIcon;

        // Multi-resolution icon built in memory — no file, no GetHicon handle to
        // leak, and Windows picks the right size for the tray, taskbar and Alt+Tab.
        public static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    try
                    {
                        using (var ms = new MemoryStream(IcoBytes(IconSizes)))
                            appIcon = new Icon(ms);
                    }
                    catch { appIcon = SystemIcons.Application; }
                }
                return appIcon;
            }
        }

        // --write-icon <path>: the build's first pass calls this so the second
        // pass has something to hand /win32icon.
        public static int WriteIconFile(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, IcoBytes(IconSizes));
                return 0;
            }
            catch { return 1; }
        }
    }
}
