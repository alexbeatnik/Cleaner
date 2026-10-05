// The app mark, drawn with GDI+ — and the ICO writer that turns it into the
// executable's Win32 icon resource at build time (build.ps1 pass 1 runs
// Cleaner.exe --write-icon app.ico, pass 2 embeds the result).
// Keeping the icon generated rather than committed means the repository holds
// no binary assets and the taskbar icon can never drift from the in-app mark.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Cleaner
{
    static class Brand
    {
        // Sizes baked into the icon resource. 256 is what Explorer's extra-large
        // view wants; 16/20/24 are the tray, taskbar and title bar.
        internal static readonly int[] IconSizes = new int[] { 16, 20, 24, 32, 48, 64, 128, 256 };

        // A mop, filled rather than stroked: at 16 px an outline of this shape
        // closes up into a grey blob, and the badge needs a silhouette. Built
        // upright around the origin in units of r, then tilted — a mop standing
        // straight up reads as a lollipop, and the lean is what makes it a tool
        // someone is holding.
        //
        // r is the half-height of the whole drawing, so the handle tip sits at
        // -r and the strand tips at +r before the tilt.
        internal static GraphicsPath Mop(float cx, float cy, float r)
        {
            var path = new GraphicsPath();

            // Handle: a slim bar with a rounded cap, running down into the collar.
            // Any thinner and it is a single pixel at tray size, which reads as a
            // stray mark rather than as the thing the head is attached to.
            path.AddPath(Theme.Round(new RectangleF(-0.14f, -1.00f, 0.28f, 1.02f), 0.14f), false);
            // Collar: the ferrule that clamps the head on. Without it the handle
            // and the head read as two unrelated shapes at small sizes.
            path.AddPath(Theme.Round(new RectangleF(-0.32f, -0.14f, 0.64f, 0.21f), 0.06f), false);

            // Head: a trapezoid flaring out to the floor, its bottom edge cut by
            // two notches. Three strands is what survives 16 px — four turns the
            // bottom edge into noise, and none of them reads as a plain wedge. The
            // notches stop well short of the collar: cut any deeper and the
            // silhouette stops being a mop head and starts being a pair of legs.
            var head = new GraphicsPath();
            head.AddLine(-0.46f, 0.02f, 0.46f, 0.02f);
            head.AddLine(0.46f, 0.02f, 0.82f, 1.00f);
            head.AddLine(0.82f, 1.00f, 0.44f, 1.00f);
            head.AddLine(0.44f, 1.00f, 0.34f, 0.74f);   // notch up
            head.AddLine(0.34f, 0.74f, 0.24f, 1.00f);   // and back down
            head.AddLine(0.24f, 1.00f, -0.24f, 1.00f);
            head.AddLine(-0.24f, 1.00f, -0.34f, 0.74f);
            head.AddLine(-0.34f, 0.74f, -0.44f, 1.00f);
            head.AddLine(-0.44f, 1.00f, -0.82f, 1.00f);
            head.CloseFigure();
            path.AddPath(head, false);
            head.Dispose();

            using (var m = new Matrix())
            {
                m.Translate(cx, cy);
                m.Scale(r, r);
                m.Rotate(-24f);   // GDI+ rotates clockwise, so this leans the handle right
                path.Transform(m);
            }
            return path;
        }

        // The app mark: the mop, with the whole drawing nudged so the tilt does
        // not leave it sitting off-centre in its badge. r is the half-height.
        internal static GraphicsPath AppMark(float cx, float cy, float r)
        {
            return Mop(cx - r * 0.04f, cy, r);
        }

        // The full mark: a rounded blue badge carrying the mop in white, the flat
        // treatment the AV project next door uses for its own badge so the two sit
        // together on a taskbar. Drawn into an arbitrary rectangle, so the same
        // code serves the icon resource, the tray icon and the in-window header.
        public static void PaintMark(Graphics g, RectangleF r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(r.Width, r.Height);
            var box = new RectangleF(r.X + (r.Width - s) / 2f, r.Y + (r.Height - s) / 2f, s, s);
            float cx = box.X + s * 0.5f, cy = box.Y + s * 0.5f;

            using (var path = Theme.Round(box, s * 0.22f))
            using (var brush = new LinearGradientBrush(box,
                       Color.FromArgb(96, 156, 255), Color.FromArgb(38, 96, 214), 55f))
                g.FillPath(brush, path);

            // Rim, inset by half its own width: a pen centred on the box edge
            // would put half the stroke outside the bitmap and lose it. It is what
            // keeps the badge from dissolving into a dark taskbar.
            float bw = Math.Max(1f, s * 0.028f);
            var rim = new RectangleF(box.X + bw / 2f, box.Y + bw / 2f, s - bw, s - bw);
            using (var path = Theme.Round(rim, s * 0.22f - bw / 2f))
            using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), bw))
                g.DrawPath(pen, path);

            using (var mark = AppMark(cx, cy, s * 0.33f))
            using (var b = new SolidBrush(Color.White))
                g.FillPath(b, mark);

            // A small glint gives the mark a clear cleaning cue without adding
            // noise to the 16/20 px tray versions of the icon.
            if (s >= 24f)
            {
                float gx = box.X + s * 0.75f, gy = box.Y + s * 0.26f;
                float arm = s * 0.075f, waist = s * 0.018f;
                PointF[] glint =
                {
                    new PointF(gx, gy - arm), new PointF(gx + waist, gy - waist),
                    new PointF(gx + arm, gy), new PointF(gx + waist, gy + waist),
                    new PointF(gx, gy + arm), new PointF(gx - waist, gy + waist),
                    new PointF(gx - arm, gy), new PointF(gx - waist, gy - waist)
                };
                using (var b = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                    g.FillPolygon(b, glint);
            }
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
