// Vector glyphs drawn with GDI+ strokes — buttons and tabs carry icons without
// shipping a single image asset.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace WindowsStalker
{
    delegate void IconDraw(Graphics g, RectangleF r, Color c);

    static class Ico
    {
        static Pen P(Color c, RectangleF r, float thicknessFactor)
        {
            var pen = new Pen(c, Math.Max(1.3f, Math.Min(r.Width, r.Height) * thicknessFactor));
            pen.StartCap = pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            return pen;
        }

        // Broom — the cleaner page and the Clean action
        public static void Broom(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            using (var pen = P(c, r, 0.1f))
            {
                // handle, running from top-right down to the head
                g.DrawLine(pen, r.X + w * 0.80f, r.Y + h * 0.10f, r.X + w * 0.45f, r.Y + h * 0.52f);
                // head: a trapezoid of bristles
                using (var p = new GraphicsPath())
                {
                    p.AddLine(r.X + w * 0.30f, r.Y + h * 0.52f, r.X + w * 0.62f, r.Y + h * 0.52f);
                    p.AddLine(r.X + w * 0.62f, r.Y + h * 0.52f, r.X + w * 0.78f, r.Y + h * 0.92f);
                    p.AddLine(r.X + w * 0.78f, r.Y + h * 0.92f, r.X + w * 0.14f, r.Y + h * 0.92f);
                    p.CloseFigure();
                    g.DrawPath(pen, p);
                }
                g.DrawLine(pen, r.X + w * 0.35f, r.Y + h * 0.68f, r.X + w * 0.30f, r.Y + h * 0.92f);
                g.DrawLine(pen, r.X + w * 0.57f, r.Y + h * 0.68f, r.X + w * 0.62f, r.Y + h * 0.92f);
            }
        }

        // The radiation trefoil of the app mark, as a glyph. Deliberately the
        // very same path the icon resource is built from, so the thing on the
        // taskbar and the thing on the dashboard are one mark and not two
        // drawings that merely resemble each other.
        public static void Radiation(Graphics g, RectangleF r, Color c)
        {
            float s = Math.Min(r.Width, r.Height);
            using (var path = Brand.Trefoil(r.X + r.Width / 2f, r.Y + r.Height / 2f, s * 0.5f))
            using (var b = new SolidBrush(c))
                g.FillPath(b, path);
        }

        // Stacked platters — a disk drive; used for the storage readouts
        public static void Disk(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height, eh = h * 0.30f;
            using (var pen = P(c, r, 0.09f))
            {
                g.DrawEllipse(pen, r.X + w * 0.08f, r.Y + h * 0.08f, w * 0.84f, eh);
                g.DrawArc(pen, r.X + w * 0.08f, r.Y + h * 0.35f, w * 0.84f, eh, 0, 180);
                g.DrawArc(pen, r.X + w * 0.08f, r.Y + h * 0.62f, w * 0.84f, eh, 0, 180);
                g.DrawLine(pen, r.X + w * 0.08f, r.Y + h * 0.23f, r.X + w * 0.08f, r.Y + h * 0.77f);
                g.DrawLine(pen, r.X + w * 0.92f, r.Y + h * 0.23f, r.X + w * 0.92f, r.Y + h * 0.77f);
            }
        }

        // Waste basket — delete actions
        public static void Trash(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            using (var pen = P(c, r, 0.1f))
            {
                g.DrawLine(pen, r.X + w * 0.12f, r.Y + h * 0.24f, r.X + w * 0.88f, r.Y + h * 0.24f);
                g.DrawLine(pen, r.X + w * 0.38f, r.Y + h * 0.24f, r.X + w * 0.40f, r.Y + h * 0.10f);
                g.DrawLine(pen, r.X + w * 0.40f, r.Y + h * 0.10f, r.X + w * 0.60f, r.Y + h * 0.10f);
                g.DrawLine(pen, r.X + w * 0.60f, r.Y + h * 0.10f, r.X + w * 0.62f, r.Y + h * 0.24f);
                using (var p = new GraphicsPath())
                {
                    p.AddLine(r.X + w * 0.22f, r.Y + h * 0.24f, r.X + w * 0.28f, r.Y + h * 0.92f);
                    p.AddLine(r.X + w * 0.28f, r.Y + h * 0.92f, r.X + w * 0.72f, r.Y + h * 0.92f);
                    p.AddLine(r.X + w * 0.72f, r.Y + h * 0.92f, r.X + w * 0.78f, r.Y + h * 0.24f);
                    g.DrawPath(pen, p);
                }
            }
        }

        // Rocket — startup / boot items
        public static void Rocket(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            using (var pen = P(c, r, 0.09f))
            {
                using (var p = new GraphicsPath())
                {
                    p.AddBezier(r.X + w * 0.50f, r.Y + h * 0.06f,
                                r.X + w * 0.76f, r.Y + h * 0.28f,
                                r.X + w * 0.74f, r.Y + h * 0.56f,
                                r.X + w * 0.62f, r.Y + h * 0.74f);
                    p.AddLine(r.X + w * 0.62f, r.Y + h * 0.74f, r.X + w * 0.38f, r.Y + h * 0.74f);
                    p.AddBezier(r.X + w * 0.38f, r.Y + h * 0.74f,
                                r.X + w * 0.26f, r.Y + h * 0.56f,
                                r.X + w * 0.24f, r.Y + h * 0.28f,
                                r.X + w * 0.50f, r.Y + h * 0.06f);
                    g.DrawPath(pen, p);
                }
                g.DrawLine(pen, r.X + w * 0.30f, r.Y + h * 0.52f, r.X + w * 0.16f, r.Y + h * 0.74f);
                g.DrawLine(pen, r.X + w * 0.16f, r.Y + h * 0.74f, r.X + w * 0.34f, r.Y + h * 0.70f);
                g.DrawLine(pen, r.X + w * 0.70f, r.Y + h * 0.52f, r.X + w * 0.84f, r.Y + h * 0.74f);
                g.DrawLine(pen, r.X + w * 0.84f, r.Y + h * 0.74f, r.X + w * 0.66f, r.Y + h * 0.70f);
                g.DrawLine(pen, r.X + w * 0.44f, r.Y + h * 0.84f, r.X + w * 0.56f, r.Y + h * 0.84f);
            }
            using (var b = new SolidBrush(c))
                g.FillEllipse(b, r.X + w * 0.42f, r.Y + h * 0.28f, w * 0.16f, h * 0.16f);
        }

        // Shipping box — installed applications
        public static void Box(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            using (var pen = P(c, r, 0.09f))
            {
                using (var p = new GraphicsPath())
                {
                    p.AddLine(r.X + w * 0.50f, r.Y + h * 0.08f, r.X + w * 0.92f, r.Y + h * 0.28f);
                    p.AddLine(r.X + w * 0.92f, r.Y + h * 0.28f, r.X + w * 0.92f, r.Y + h * 0.72f);
                    p.AddLine(r.X + w * 0.92f, r.Y + h * 0.72f, r.X + w * 0.50f, r.Y + h * 0.92f);
                    p.AddLine(r.X + w * 0.50f, r.Y + h * 0.92f, r.X + w * 0.08f, r.Y + h * 0.72f);
                    p.AddLine(r.X + w * 0.08f, r.Y + h * 0.72f, r.X + w * 0.08f, r.Y + h * 0.28f);
                    p.CloseFigure();
                    g.DrawPath(pen, p);
                }
                g.DrawLine(pen, r.X + w * 0.08f, r.Y + h * 0.28f, r.X + w * 0.50f, r.Y + h * 0.48f);
                g.DrawLine(pen, r.X + w * 0.92f, r.Y + h * 0.28f, r.X + w * 0.50f, r.Y + h * 0.48f);
                g.DrawLine(pen, r.X + w * 0.50f, r.Y + h * 0.48f, r.X + w * 0.50f, r.Y + h * 0.92f);
            }
        }

        // Stacked database drums — the registry
        public static void Registry(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            using (var pen = P(c, r, 0.09f))
            {
                for (int i = 0; i < 3; i++)
                {
                    float y = r.Y + h * (0.14f + i * 0.26f);
                    g.DrawRectangle(pen, r.X + w * 0.12f, y, w * 0.76f, h * 0.18f);
                    g.DrawLine(pen, r.X + w * 0.24f, y + h * 0.09f, r.X + w * 0.30f, y + h * 0.09f);
                }
            }
        }

        // Two overlapping sheets — duplicate finder
        public static void Duplicate(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            using (var pen = P(c, r, 0.09f))
            {
                g.DrawRectangle(pen, r.X + w * 0.10f, r.Y + h * 0.10f, w * 0.55f, h * 0.55f);
                g.DrawRectangle(pen, r.X + w * 0.35f, r.Y + h * 0.35f, w * 0.55f, h * 0.55f);
            }
        }

        // Pie slice — the big-files / space breakdown
        public static void Pie(Graphics g, RectangleF r, Color c)
        {
            float s = Math.Min(r.Width, r.Height);
            var box = new RectangleF(r.X + (r.Width - s) / 2f, r.Y + (r.Height - s) / 2f, s, s);
            using (var pen = P(c, r, 0.09f))
            {
                g.DrawEllipse(pen, box.X + s * 0.06f, box.Y + s * 0.06f, s * 0.88f, s * 0.88f);
                g.DrawLine(pen, box.X + s * 0.5f, box.Y + s * 0.5f, box.X + s * 0.5f, box.Y + s * 0.06f);
                g.DrawLine(pen, box.X + s * 0.5f, box.Y + s * 0.5f, box.X + s * 0.94f, box.Y + s * 0.62f);
            }
        }

        // Folder
        public static void FolderIcon(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            using (var pen = P(c, r, 0.1f))
            using (var p = new GraphicsPath())
            {
                p.AddLine(r.X + w * 0.06f, r.Y + h * 0.82f, r.X + w * 0.06f, r.Y + h * 0.20f);
                p.AddLine(r.X + w * 0.06f, r.Y + h * 0.20f, r.X + w * 0.40f, r.Y + h * 0.20f);
                p.AddLine(r.X + w * 0.40f, r.Y + h * 0.20f, r.X + w * 0.50f, r.Y + h * 0.34f);
                p.AddLine(r.X + w * 0.50f, r.Y + h * 0.34f, r.X + w * 0.94f, r.Y + h * 0.34f);
                p.AddLine(r.X + w * 0.94f, r.Y + h * 0.34f, r.X + w * 0.94f, r.Y + h * 0.82f);
                p.CloseFigure();
                g.DrawPath(pen, p);
            }
        }

        // Document
        public static void FileIcon(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height, fold = w * 0.3f;
            using (var pen = P(c, r, 0.1f))
            using (var p = new GraphicsPath())
            {
                p.AddLine(r.X + w * 0.2f, r.Y, r.X + w - fold, r.Y);
                p.AddLine(r.X + w - fold, r.Y, r.X + w * 0.82f, r.Y + fold * 0.75f);
                p.AddLine(r.X + w * 0.82f, r.Y + fold * 0.75f, r.X + w * 0.82f, r.Y + h);
                p.AddLine(r.X + w * 0.82f, r.Y + h, r.X + w * 0.2f, r.Y + h);
                p.CloseFigure();
                g.DrawPath(pen, p);
            }
        }

        // Magnifier — analyze / search
        public static void Search(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height, d = Math.Min(w, h) * 0.62f;
            using (var pen = P(c, r, 0.1f))
            {
                g.DrawEllipse(pen, r.X + w * 0.06f, r.Y + h * 0.06f, d, d);
                g.DrawLine(pen, r.X + w * 0.06f + d * 0.87f, r.Y + h * 0.06f + d * 0.87f,
                                r.X + w * 0.94f, r.Y + h * 0.94f);
            }
        }

        // Gear — settings
        public static void Gear(Graphics g, RectangleF r, Color c)
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            float rad = Math.Min(r.Width, r.Height) / 2f;
            using (var pen = P(c, r, 0.1f))
            {
                for (int i = 0; i < 8; i++)
                {
                    double a = Math.PI * i / 4.0;
                    g.DrawLine(pen,
                        (float)(cx + Math.Cos(a) * rad * 0.62f), (float)(cy + Math.Sin(a) * rad * 0.62f),
                        (float)(cx + Math.Cos(a) * rad * 0.96f), (float)(cy + Math.Sin(a) * rad * 0.96f));
                }
                g.DrawEllipse(pen, cx - rad * 0.60f, cy - rad * 0.60f, rad * 1.2f, rad * 1.2f);
                g.DrawEllipse(pen, cx - rad * 0.22f, cy - rad * 0.22f, rad * 0.44f, rad * 0.44f);
            }
        }

        // Circular arrow — refresh / rescan
        public static void Refresh(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            using (var pen = P(c, r, 0.11f))
            {
                g.DrawArc(pen, r.X + w * 0.12f, r.Y + h * 0.12f, w * 0.76f, h * 0.76f, 40, 280);
                using (var p = new GraphicsPath())
                {
                    p.AddLine(r.X + w * 0.86f, r.Y + h * 0.10f, r.X + w * 0.86f, r.Y + h * 0.40f);
                    p.AddLine(r.X + w * 0.86f, r.Y + h * 0.40f, r.X + w * 0.56f, r.Y + h * 0.40f);
                    g.DrawPath(pen, p);
                }
            }
        }

        public static void Check(Graphics g, RectangleF r, Color c)
        {
            using (var pen = P(c, r, 0.13f))
            {
                g.DrawLine(pen, r.X + r.Width * 0.16f, r.Y + r.Height * 0.54f,
                                r.X + r.Width * 0.40f, r.Y + r.Height * 0.78f);
                g.DrawLine(pen, r.X + r.Width * 0.40f, r.Y + r.Height * 0.78f,
                                r.X + r.Width * 0.84f, r.Y + r.Height * 0.24f);
            }
        }

        public static void Close(Graphics g, RectangleF r, Color c)
        {
            using (var pen = P(c, r, 0.12f))
            {
                g.DrawLine(pen, r.X + r.Width * 0.22f, r.Y + r.Height * 0.22f,
                                r.X + r.Width * 0.78f, r.Y + r.Height * 0.78f);
                g.DrawLine(pen, r.X + r.Width * 0.78f, r.Y + r.Height * 0.22f,
                                r.X + r.Width * 0.22f, r.Y + r.Height * 0.78f);
            }
        }

        public static void StopIcon(Graphics g, RectangleF r, Color c)
        {
            using (var b = new SolidBrush(c))
            using (var p = Theme.Round(new RectangleF(r.X + r.Width * 0.22f, r.Y + r.Height * 0.22f,
                                                      r.Width * 0.56f, r.Height * 0.56f), r.Width * 0.1f))
                g.FillPath(b, p);
        }

        // Exclamation in a triangle — warnings and risky items
        public static void Warning(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            using (var pen = P(c, r, 0.1f))
            using (var p = new GraphicsPath())
            {
                p.AddLine(r.X + w * 0.5f, r.Y + h * 0.10f, r.X + w * 0.94f, r.Y + h * 0.86f);
                p.AddLine(r.X + w * 0.94f, r.Y + h * 0.86f, r.X + w * 0.06f, r.Y + h * 0.86f);
                p.CloseFigure();
                g.DrawPath(pen, p);
                g.DrawLine(pen, r.X + w * 0.5f, r.Y + h * 0.40f, r.X + w * 0.5f, r.Y + h * 0.62f);
            }
            using (var b = new SolidBrush(c))
                g.FillEllipse(b, r.X + w * 0.455f, r.Y + h * 0.70f, w * 0.09f, w * 0.09f);
        }

        public static void Info(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            using (var pen = P(c, r, 0.09f))
            {
                g.DrawEllipse(pen, r.X + w * 0.08f, r.Y + h * 0.08f, w * 0.84f, h * 0.84f);
                g.DrawLine(pen, r.X + w * 0.5f, r.Y + h * 0.46f, r.X + w * 0.5f, r.Y + h * 0.72f);
            }
            using (var b = new SolidBrush(c))
                g.FillEllipse(b, r.X + w * 0.455f, r.Y + h * 0.28f, w * 0.09f, w * 0.09f);
        }

        // Shield with a check — the "system is clean / protected" states
        public static void ShieldIcon(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            using (var p = new GraphicsPath())
            {
                p.AddBezier(r.X + w * .50f, r.Y + h * .04f, r.X + w * .68f, r.Y + h * .10f,
                            r.X + w * .80f, r.Y + h * .12f, r.X + w * .88f, r.Y + h * .12f);
                p.AddLine(r.X + w * .88f, r.Y + h * .12f, r.X + w * .88f, r.Y + h * .48f);
                p.AddBezier(r.X + w * .88f, r.Y + h * .48f, r.X + w * .88f, r.Y + h * .72f,
                            r.X + w * .68f, r.Y + h * .88f, r.X + w * .50f, r.Y + h * .96f);
                p.AddBezier(r.X + w * .50f, r.Y + h * .96f, r.X + w * .32f, r.Y + h * .88f,
                            r.X + w * .12f, r.Y + h * .72f, r.X + w * .12f, r.Y + h * .48f);
                p.AddLine(r.X + w * .12f, r.Y + h * .48f, r.X + w * .12f, r.Y + h * .12f);
                p.AddBezier(r.X + w * .12f, r.Y + h * .12f, r.X + w * .20f, r.Y + h * .12f,
                            r.X + w * .32f, r.Y + h * .10f, r.X + w * .50f, r.Y + h * .04f);
                p.CloseFigure();
                using (var pen = P(c, r, 0.09f)) g.DrawPath(pen, p);
            }
        }

        // Downward arrow into a tray — uninstall / export
        public static void Download(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            using (var pen = P(c, r, 0.1f))
            {
                g.DrawLine(pen, r.X + w * 0.5f, r.Y + h * 0.10f, r.X + w * 0.5f, r.Y + h * 0.62f);
                g.DrawLine(pen, r.X + w * 0.28f, r.Y + h * 0.42f, r.X + w * 0.5f, r.Y + h * 0.64f);
                g.DrawLine(pen, r.X + w * 0.72f, r.Y + h * 0.42f, r.X + w * 0.5f, r.Y + h * 0.64f);
                g.DrawLine(pen, r.X + w * 0.14f, r.Y + h * 0.86f, r.X + w * 0.86f, r.Y + h * 0.86f);
            }
        }

        // Clock — scheduling
        public static void Clock(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            using (var pen = P(c, r, 0.09f))
            {
                g.DrawEllipse(pen, r.X + w * 0.08f, r.Y + h * 0.08f, w * 0.84f, h * 0.84f);
                g.DrawLine(pen, r.X + w * 0.5f, r.Y + h * 0.28f, r.X + w * 0.5f, r.Y + h * 0.52f);
                g.DrawLine(pen, r.X + w * 0.5f, r.Y + h * 0.52f, r.X + w * 0.70f, r.Y + h * 0.64f);
            }
        }

        // Padlock with a raised shackle — items that need elevation
        public static void Unlock(Graphics g, RectangleF r, Color c)
        {
            float w = r.Width, h = r.Height;
            using (var pen = P(c, r, 0.1f))
            {
                g.DrawArc(pen, r.X + w * 0.28f, r.Y + h * 0.06f, w * 0.5f, h * 0.44f, 180, 180);
                g.DrawRectangle(pen, r.X + w * 0.16f, r.Y + h * 0.44f, w * 0.62f, h * 0.48f);
            }
        }
    }
}
