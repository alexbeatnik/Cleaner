// Custom-drawn controls: buttons, toggle, nav tabs, cards, the hero gauge,
// drive bars and the thin progress bar.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WindowsStalker
{
    // Rounded button with hover/pressed states instead of the system Button.
    // Implements IButtonControl so it can act as AcceptButton/CancelButton.
    class ModernButton : Control, IButtonControl
    {
        public Color Back, Hover, TextColor;
        public IconDraw Icon;   // optional glyph; null = text-only button
        public bool CardStyle;  // icon centered above the text, for the big dashboard actions
        public string SubText;  // muted one-line caption under the label (card-style tiles only)
        bool over, down;
        DialogResult dialogResult = DialogResult.None;

        public ModernButton(string text, Color back, Color hover, Color fore)
        {
            Text = text;
            Back = back; Hover = hover; TextColor = fore;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 36;
            Width = 150;
            Font = Theme.UiBold(9f);
            Cursor = Cursors.Hand;
            Margin = new Padding(0, 4, 8, 4);
            MouseEnter += delegate { over = true; Invalidate(); };
            MouseLeave += delegate { over = false; down = false; Invalidate(); };
            MouseDown += delegate { down = true; Invalidate(); };
            MouseUp += delegate { down = false; Invalidate(); };
        }

        public DialogResult DialogResult
        {
            get { return dialogResult; }
            set { dialogResult = value; }
        }
        public void NotifyDefault(bool value) { }
        public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

        protected override void OnClick(EventArgs e)
        {
            var f = FindForm();
            if (dialogResult != DialogResult.None && f != null && f.Modal) f.DialogResult = dialogResult;
            base.OnClick(e);
        }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.Clear(BackColor); // background behind the cell (card colour when on a card)
            // disabled = dark surface with muted text (not a bright gray slab)
            Color c = !Enabled ? Theme.Subtle : (down ? Back : (over ? Hover : Back));
            var box = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(c)) g.FillRectangle(b, box);
            Theme.Scanlines(g, box, Theme.Phase(this));
            // Every button is a framed cell: this terminal has no fill without an
            // edge, and the frame is what carries the hover state.
            Color edge = !Enabled ? Theme.CardLine : (over ? Theme.AccentHot : Theme.Muted);
            using (var pen = new Pen(edge)) g.DrawRectangle(pen, box);
            if (CardStyle) Theme.Brackets(g, box, edge, 10);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color fg = Enabled ? TextColor : Theme.Muted;
            // NoPrefix: button labels may contain a literal "&"
            const TextFormatFlags NoPre = TextFormatFlags.NoPrefix;
            if (Icon == null)
            {
                Theme.DrawTrackedCentered(g, Text.ToUpperInvariant(), Font, ClientRectangle, fg, Theme.Track);
                return;
            }
            if (CardStyle)
            {
                // icon scales with the tile so big dashboard tiles get big glyphs
                float iconSize = Math.Min(Width * 0.30f, Height * 0.36f);
                var iconRect = new RectangleF((Width - iconSize) / 2f, Height * 0.16f, iconSize, iconSize);
                Icon(g, iconRect, fg);
                int textTop = (int)(iconRect.Bottom + 10);
                if (string.IsNullOrEmpty(SubText))
                {
                    var textRect = new Rectangle(4, textTop, Width - 8, Height - textTop - 6);
                    TextRenderer.DrawText(g, Text.ToUpperInvariant(), Font, textRect, fg,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | NoPre);
                }
                else
                {
                    int labelH = Font.Height + 2;
                    Theme.DrawTrackedCentered(g, Text.ToUpperInvariant(), Font,
                        new Rectangle(4, textTop - 2, Width - 8, labelH), fg, Theme.Track);
                    // caption colour follows the painted background: amber-dim on
                    // dark cells, near-black on a lit amber fill, where dim amber
                    // would sink into it
                    Color subFg = !Enabled ? Theme.Disabled
                        : c.GetBrightness() > 0.35f ? Color.FromArgb(210, Theme.OnAccent)
                        : Theme.Muted;
                    using (var sf = Theme.Ui(7.75f))
                        TextRenderer.DrawText(g, SubText, sf, new Rectangle(4, textTop - 2 + labelH, Width - 8, 15),
                            subFg,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | NoPre);
                }
            }
            else
            {
                const int iconBox = 16, gap = 7;
                string label = Text.ToUpperInvariant();
                int totalW = iconBox + gap + Theme.MeasureTracked(label, Font, Theme.Track);
                int startX = Math.Max(6, (Width - totalW) / 2);
                var iconRect = new RectangleF(startX, (Height - iconBox) / 2f, iconBox, iconBox);
                Icon(g, iconRect, fg);
                var textRect = new Rectangle(startX + iconBox + gap, 0, Width - (startX + iconBox + gap) - 4, Height);
                Theme.DrawTrackedLeft(g, label, Font, textRect, fg, Theme.Track);
            }
        }
    }

    // Animated toggle switch, used instead of the system CheckBox
    class Toggle : Control
    {
        public event EventHandler CheckedChanged;
        bool isOn;
        float knob; // 0..1 — animated knob position
        readonly Timer anim = new Timer();

        public Toggle(string text)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 26;
            Cursor = Cursors.Hand;
            Text = text;
            anim.Interval = 15;
            anim.Tick += delegate
            {
                float target = isOn ? 1f : 0f;
                knob += (target - knob) * 0.35f;
                if (Math.Abs(target - knob) < 0.03f) { knob = target; anim.Stop(); }
                Invalidate();
            };
            Click += delegate { Checked = !Checked; };
        }

        public bool Checked
        {
            get { return isOn; }
            set
            {
                if (isOn == value) return;
                isOn = value;
                if (IsHandleCreated && Visible) anim.Start();
                else { knob = isOn ? 1f : 0f; Invalidate(); }
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        void FitWidth()
        {
            Width = 64 + TextRenderer.MeasureText(Text, Font).Width;
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); FitWidth(); }
        // the form's font arrives after the constructor runs — re-measure width then
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitWidth(); }
        protected override void OnParentChanged(EventArgs e) { base.OnParentChanged(e); FitWidth(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.SmoothingMode = SmoothingMode.None;
            const int tw = 40, th = 20; // track
            int ty = (Height - th) / 2;
            var track = new Rectangle(0, ty, tw - 1, th - 1);
            using (var b = new SolidBrush(isOn ? Theme.Accent : Theme.Subtle))
                g.FillRectangle(b, track);
            using (var p = new Pen(isOn ? Theme.AccentHot : Theme.CardLine))
                g.DrawRectangle(p, track);
            // A square block, not a knob: this switch is a lit cell that slides,
            // the way a console would draw it
            float kx = 3 + knob * (tw - th); // ranges 3..23
            using (var b = new SolidBrush(isOn ? Theme.OnAccent : Theme.Muted))
                g.FillRectangle(b, kx, ty + 4, th - 8, th - 8);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(tw + 10, 0, Width - tw - 10, Height),
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    // Dark colours for the tray context menu (the system default is stark white)
    class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.LogBg; } }
        public override Color ImageMarginGradientBegin { get { return Theme.LogBg; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.LogBg; } }
        public override Color ImageMarginGradientEnd { get { return Theme.LogBg; } }
        public override Color MenuItemSelected { get { return Theme.Card; } }
        public override Color MenuItemBorder { get { return Theme.Card; } }
        public override Color MenuBorder { get { return Theme.CardLine; } }
    }

    // Horizontal top-bar nav tab: icon + label, active state = filled accent pill
    class NavTab : Control
    {
        public IconDraw Icon;
        public bool Active;
        bool hover;

        public NavTab(string text, IconDraw icon)
        {
            Text = text;
            Icon = icon;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.UiBold(9.5f);
            Height = 44;
            Cursor = Cursors.Hand;
            Margin = new Padding(1, 0, 1, 0);
            MouseEnter += delegate { hover = true; Invalidate(); };
            MouseLeave += delegate { hover = false; Invalidate(); };
        }

        public void SetActive(bool a) { Active = a; Invalidate(); }

        void FitWidth() { Width = 42 + Theme.MeasureTracked(Text.ToUpperInvariant(), Font, Theme.Track); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); FitWidth(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitWidth(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            Theme.Surface(this, g, Theme.Bg);
            // The live tab is a lit cell underlined in amber rather than a pill:
            // this is a console, and an underline is how a console says which
            // channel is selected.
            var cell = new Rectangle(1, 6, Width - 3, Height - 15);
            if (Active || hover)
                using (var b = new SolidBrush(Color.FromArgb(Active ? 34 : 18, Theme.Accent)))
                    g.FillRectangle(b, cell);
            if (Active)
                using (var b = new SolidBrush(Theme.Accent))
                    g.FillRectangle(b, cell.X, cell.Bottom - 1, cell.Width, 2);
            Color c = Active ? Theme.AccentHot : (hover ? Theme.Text : Theme.Muted);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var iconRect = new RectangleF(13, (Height - 8 - 17) / 2f, 17, 17);
            if (Icon != null) Icon(g, iconRect, c);
            var textRect = new Rectangle((int)iconRect.Right + 6, 0, Width - (int)iconRect.Right - 12, Height - 8);
            Theme.DrawTrackedLeft(g, Text.ToUpperInvariant(), Font, textRect, c, Theme.Track);
        }
    }

    enum GaugeState { Idle, Busy, Result, Clean }

    // The dashboard hero: a donut ring with a headline value in the middle.
    // Idle shows the sparkle mark, Busy shows the analysis percentage, Result
    // shows how much can be reclaimed, Clean shows the all-tidy state.
    class Gauge : Control
    {
        public GaugeState State = GaugeState.Idle;
        public string Big = "", Sub = "";
        float progress = -1f;   // 0..1 while analysing; -1 = unknown (spinner)
        float spin;             // rotating arc for the unknown-progress case
        readonly Timer anim = new Timer();

        public Gauge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(210, 210);
            BackColor = Theme.Bg;
            anim.Interval = 16;
            anim.Tick += delegate { spin = (spin + 4.5f) % 360f; Invalidate(); };
        }

        public void SetIdle(string big, string sub)
        {
            anim.Stop();
            State = GaugeState.Idle; Big = big; Sub = sub; progress = -1f;
            Invalidate();
        }

        public void SetBusy(string sub)
        {
            State = GaugeState.Busy; Sub = sub; Big = ""; progress = -1f;
            if (!anim.Enabled) anim.Start();
            Invalidate();
        }

        // Once the analysis knows its total, the spinner turns into a real arc
        public void SetProgress(double f, string sub)
        {
            State = GaugeState.Busy;
            Sub = sub;
            float v = (float)Math.Max(0, Math.Min(1, f));
            Big = ((int)Math.Round(v * 100)) + "%";
            bool repaint = Math.Abs(v - progress) >= 0.005f;
            progress = v;
            anim.Stop();
            if (repaint) Invalidate();
        }

        public void SetResult(GaugeState state, string big, string sub)
        {
            anim.Stop();
            State = state; Big = big; Sub = sub; progress = -1f;
            Invalidate();
        }

        Color StateColor()
        {
            switch (State)
            {
                case GaugeState.Result: return Theme.Warn;
                case GaugeState.Clean: return Theme.Good;
                case GaugeState.Busy: return Theme.Accent;
                default: return Theme.Accent;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Surface(this, g, BackColor);
            float s = Math.Min(Width, Height);
            float thick = s * 0.075f;
            var box = new RectangleF((Width - s) / 2f + thick, (Height - s) / 2f + thick,
                                     s - thick * 2, s - thick * 2);
            Color c = StateColor();

            // Soft glow behind the ring — the status lighting these dashboards use
            using (var gp = new GraphicsPath())
            {
                gp.AddEllipse((Width - s) / 2f, (Height - s) / 2f, s, s);
                using (var pgb = new PathGradientBrush(gp))
                {
                    pgb.CenterColor = Color.FromArgb(42, c);
                    pgb.SurroundColors = new Color[] { Color.FromArgb(0, c) };
                    g.FillPath(pgb, gp);
                }
            }

            // The unlit part of the ring is dashed, so the dial reads as segments
            // on a panel instead of a solid donut
            using (var track = new Pen(Color.FromArgb(70, 47, 0), thick))
            {
                track.DashStyle = DashStyle.Dash;
                g.DrawEllipse(track, box);
            }
            using (var pen = new Pen(c, thick))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                if (State == GaugeState.Busy && progress < 0) g.DrawArc(pen, box, spin, 90);
                else if (State == GaugeState.Busy) g.DrawArc(pen, box, -90, Math.Max(2f, progress * 360f));
                else if (State != GaugeState.Idle) g.DrawArc(pen, box, -90, 360);
                else g.DrawArc(pen, box, -90, 120);
            }

            if (State == GaugeState.Idle && string.IsNullOrEmpty(Big))
            {
                Ico.Radiation(g, new RectangleF(Width / 2f - s * 0.17f, Height / 2f - s * 0.19f, s * 0.34f, s * 0.34f), c);
            }
            else
            {
                using (var bf = Theme.UiBoldPx(s * (Big.Length > 7 ? 0.10f : 0.145f)))
                using (var b = new SolidBrush(Theme.Text))
                using (var sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(Big, bf, b, new RectangleF(0, Height * 0.36f, Width, Height * 0.20f), sf);
                }
            }
            // The caption is only legible on a large gauge. In the dashboard hero
            // the ring is 126 px and the headline beside it already says the same
            // thing, so it is dropped rather than rendered at six pixels.
            if (s < 170) return;
            using (var sfont = Theme.UiPx(s * 0.052f))
            using (var b = new SolidBrush(Theme.Muted))
            using (var sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Near;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(Sub, sfont, b, new RectangleF(Width * 0.12f, Height * 0.57f, Width * 0.76f, Height * 0.14f), sf);
            }
        }
    }

    // One row per fixed drive: letter + label, a used/free bar, and the numbers
    class DriveBar : Control
    {
        public string Letter = "C:", Label = "";
        public long Total, Free;

        public DriveBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Height = 46;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            Theme.Surface(this, g, BackColor);
            double used = Total > 0 ? (double)(Total - Free) / Total : 0;
            // A nearly full disk is the one thing this app exists to fix — colour it
            Color c = used > 0.92 ? Theme.Danger : used > 0.82 ? Theme.Warn : Theme.Accent;

            using (var f = Theme.UiBold(9.5f))
                TextRenderer.DrawText(g, Letter + (string.IsNullOrEmpty(Label) ? "" : "  " + Label), f,
                    new Rectangle(0, 2, Width - 150, 18), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            using (var f = Theme.Ui(8.5f))
                TextRenderer.DrawText(g, Util.FormatSize(Free) + " " + Lang.T("drive.freeOf") + " " + Util.FormatSize(Total), f,
                    new Rectangle(Width - 190, 3, 190, 18), Theme.Muted,
                    TextFormatFlags.Right | TextFormatFlags.NoPadding);

            // A cell meter rather than a bar: forty segments, lit up to the used
            // fraction, the way a console reports a tank
            const int cells = 40;
            float cw = Width / (float)cells;
            int lit = (int)Math.Round(used * cells);
            for (int i = 0; i < cells; i++)
                using (var b = new SolidBrush(i < lit ? c : Theme.Subtle))
                    g.FillRectangle(b, i * cw, 26, Math.Max(1f, cw - 1.5f), 8);
        }
    }

    // Wide, short status card with a colour-coded bar down its left edge — the
    // dashboard hero. Deliberately not another square card in a grid: the bar is
    // what carries the state (teal idle, amber when there is junk, green clean)
    // without needing a second colour anywhere else on the page.
    class StatusBanner : Panel
    {
        public Color AccentColor = Theme.Accent;

        public StatusBanner()
        {
            BackColor = Theme.Bg;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void SetAccent(Color c)
        {
            if (AccentColor == c) return;
            AccentColor = c;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.PaintCard(g, Width, Height, Theme.Phase(this));
            // The mark again, huge and nearly burnt out, sitting in the empty
            // right-hand end of the banner: the same trefoil as the taskbar icon,
            // used here as the panel stencil the way a rig has one stamped on it.
            var slab = new Rectangle(1, 0, Width - 4, Height - 6);
            Region clip = g.Clip;
            g.SetClip(slab);
            float wr = Height * 0.40f;
            using (var mark = Brand.Trefoil(slab.Right - wr * 1.35f, slab.Y + Height * 0.48f, wr))
            using (var b = new SolidBrush(Color.FromArgb(24, Theme.Accent)))
                g.FillPath(b, mark);
            g.Clip = clip;
            // A slim bar: anything wider reads as a stuck progress indicator
            using (var b = new SolidBrush(AccentColor))
                g.FillRectangle(b, 1, Height * 0.20f, 4, Height * 0.60f);
        }
    }

    // Compact one-row statistics strip: small muted captions with the value below
    class StatStrip : Panel
    {
        public string[] Captions = new string[0];
        public string[] Values = new string[0];
        public Color[] ValueColors; // optional per-value override (Color.Empty = default text)

        public StatStrip()
        {
            BackColor = Theme.Bg;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.PaintCard(g, Width, Height, Theme.Phase(this));
            using (var capF = Theme.UiBold(8f))
            using (var valF = Theme.UiBold(13.5f))
            {
                float x = 20;
                for (int i = 0; i < Captions.Length; i++)
                {
                    string cap = Captions[i].ToUpperInvariant();
                    string val = i < Values.Length ? Values[i] : "";
                    int cell = Math.Max(Theme.MeasureTracked(cap, capF, Theme.Track),
                                        TextRenderer.MeasureText(g, val, valF).Width);
                    Theme.DrawTracked(g, cap, capF, new Point((int)x, 11), Theme.Muted, Theme.Track);
                    Color vc = ValueColors != null && i < ValueColors.Length && !ValueColors[i].IsEmpty
                        ? ValueColors[i] : Theme.Text;
                    TextRenderer.DrawText(g, val, valF, new Rectangle((int)x, 28, cell + 4, 28),
                        vc, TextFormatFlags.Left | TextFormatFlags.NoPadding);
                    x += cell + 28;
                }
            }
        }
    }

    // Friendly placeholder shown instead of an empty list
    class EmptyState : Control
    {
        public string Title = "", Sub = "";

        public EmptyState()
        {
            BackColor = Theme.LogBg;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Surface(this, g, BackColor);
            float cx = Width / 2f, cy = Height / 2f;
            Ico.Radiation(g, new RectangleF(cx - 30, cy - 84, 60, 60), Theme.CardLine);
            using (var tf = Theme.UiBold(12f))
                Theme.DrawTrackedCentered(g, Title.ToUpperInvariant(), tf,
                    new Rectangle(0, (int)cy - 12, Width, 30), Theme.Text, Theme.Track);
            TextRenderer.DrawText(g, Sub, Font, new Rectangle(0, (int)cy + 20, Width, 24),
                Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
        }
    }

    // Card with rounded corners, a thin border, and an UPPERCASE header
    class CardPanel : Panel
    {
        public string HeaderText = "";

        public CardPanel(string header)
        {
            HeaderText = header;
            BackColor = Theme.Bg; // corners show the page background through them
            Padding = new Padding(16, 44, 16, 14);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.PaintCard(g, Width, Height, Theme.Phase(this));
            using (var f = Theme.UiBold(9.5f))
                Theme.DrawTracked(g, HeaderText.ToUpperInvariant(), f, new Point(16, 15),
                    Theme.AccentHot, Theme.Track);
        }
    }

    // Owner-drawn dark ListView. WinForms cannot theme the stock control's header
    // or its group bands, and its checkbox comes from the system image list — so
    // every pixel is painted here instead: rows, header, and a drawn tick box.
    // Rows carry their own state through Tag; CheckState decides per row whether a
    // box appears at all (-1 turns the row into a section header).
    class DarkList : ListView
    {
        public Func<ListViewItem, int> CheckState;   // -1 none / 0 unchecked / 1 checked
        public event EventHandler<ListViewItemEventArgs> CheckToggled;

        const int CheckBox = 15, CheckLeft = 6;

        public DarkList()
        {
            View = View.Details;
            FullRowSelect = true;
            HideSelection = false;
            MultiSelect = true;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.LogBg;
            ForeColor = Theme.Text;
            OwnerDraw = true;
            DoubleBuffered = true;
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            DrawColumnHeader += OnDrawHeader;
            DrawItem += OnDrawItem;
            DrawSubItem += OnDrawSubItem;
            MouseDown += OnMouseDownCheck;
            KeyDown += OnKeyToggle;
            ClientSizeChanged += delegate { StretchLastColumn(); };
        }

        // Call after filling the list: adding rows makes the vertical scrollbar
        // appear, which changes ClientSize without raising a resize event, so the
        // last column would otherwise overhang and produce a horizontal scrollbar.
        public void Refit() { StretchLastColumn(); }

        // The strip of header to the right of the last column is drawn by the
        // control itself and stays system-white however the rest is owner-drawn.
        // Stretching the last column to the client edge is what removes it — and
        // it has to be redone whenever a scrollbar appears and steals 17 pixels.
        void StretchLastColumn()
        {
            if (Columns.Count == 0 || ClientSize.Width <= 0) return;
            int used = 0;
            for (int i = 0; i < Columns.Count - 1; i++) used += Columns[i].Width;
            int last = ClientSize.Width - used;
            if (last < 40) return; // too narrow to be worth fighting over
            if (Columns[Columns.Count - 1].Width != last) Columns[Columns.Count - 1].Width = last;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { NativeMethods.SetWindowTheme(Handle, "DarkMode_Explorer", null); }
            catch { } // pre-1809 Windows: the scrollbar stays light, nothing else breaks
            StretchLastColumn();
        }

        public void Toggle(ListViewItem item)
        {
            if (item == null || CheckState == null || CheckState(item) < 0) return;
            if (CheckToggled != null) CheckToggled(this, new ListViewItemEventArgs(item));
        }

        void OnKeyToggle(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Space) return;
            foreach (ListViewItem it in SelectedItems) Toggle(it);
            e.Handled = true;
        }

        void OnMouseDownCheck(object sender, MouseEventArgs e)
        {
            var hit = GetItemAt(e.X, e.Y);
            if (hit == null) return;
            // Only the drawn box toggles; clicking the label selects, as usual
            if (e.X <= CheckLeft + CheckBox + 4) Toggle(hit);
        }

        void OnDrawHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var b = new SolidBrush(BackColor)) e.Graphics.FillRectangle(b, e.Bounds);
            using (var f = Theme.UiBold(8.25f))
                TextRenderer.DrawText(e.Graphics, e.Header.Text.ToUpperInvariant(), f,
                    Rectangle.Inflate(e.Bounds, -8, 0), Theme.Muted,
                    (e.Header.TextAlign == HorizontalAlignment.Right
                        ? TextFormatFlags.Right : TextFormatFlags.Left)
                    | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (var p = new Pen(Theme.CardLine))
            {
                p.DashStyle = DashStyle.Dash;
                e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
        }

        // Deliberately empty. Moving the mouse across the control makes Windows
        // invalidate just the hot row, and only DrawItem is raised for it — so a
        // background fill here wipes every sub-item that DrawSubItem is not asked
        // to redraw, and the list loses all its columns but the first. Each cell
        // paints its own background in DrawSubItem instead.
        void OnDrawItem(object sender, DrawListViewItemEventArgs e) { }

        // On the page background a selected row lifts to the card colour; on a
        // card it has to go the other way to stay visible.
        Color SelectedBack { get { return BackColor == Theme.Card ? Theme.LogBg : Theme.Card; } }

        void OnDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            var g = e.Graphics;
            int state = CheckState != null ? CheckState(e.Item) : -1;
            bool header = state < 0 && e.Item.Tag == null;

            bool selected = e.Item.Selected;
            using (var b = new SolidBrush(selected ? SelectedBack : BackColor))
                g.FillRectangle(b, e.Bounds);
            // A bar down the left edge of the first cell marks the selection —
            // a full rectangle would leave a seam between every pair of cells.
            if (selected && e.ColumnIndex == 0)
                using (var b = new SolidBrush(Theme.Accent))
                    g.FillRectangle(b, e.Bounds.X, e.Bounds.Y + 1, 2, e.Bounds.Height - 2);

            var text = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);

            if (e.ColumnIndex == 0 && state >= 0)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var box = new RectangleF(e.Bounds.X + CheckLeft, e.Bounds.Y + (e.Bounds.Height - CheckBox) / 2f,
                                         CheckBox, CheckBox);
                using (var path = Theme.Round(box, Theme.Radius))
                {
                    if (state == 1)
                    {
                        using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
                        Ico.Check(g, RectangleF.Inflate(box, -2.5f, -2.5f), Theme.OnAccent);
                    }
                    else
                    {
                        using (var p = new Pen(Theme.Muted, 1.2f)) g.DrawPath(p, path);
                    }
                }
                g.SmoothingMode = SmoothingMode.Default;
                text = new Rectangle(e.Bounds.X + CheckLeft + CheckBox + 8, e.Bounds.Y,
                                     e.Bounds.Width - (CheckLeft + CheckBox + 12), e.Bounds.Height);
            }

            Font font = header ? Theme.UiBold(8.5f) : Font;
            Color fore = header ? Theme.AccentHot
                : (e.Item.ForeColor.IsEmpty || e.Item.ForeColor == Color.Empty ? Theme.Text : e.Item.ForeColor);
            string s = e.ColumnIndex == 0 ? e.Item.Text : e.SubItem.Text;
            if (header && e.ColumnIndex == 0) s = s.ToUpperInvariant();
            TextFormatFlags align = Columns[e.ColumnIndex].TextAlign == HorizontalAlignment.Right
                ? TextFormatFlags.Right : TextFormatFlags.Left;
            TextRenderer.DrawText(g, s, font, text, fore,
                align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (header) font.Dispose();
        }
    }

    sealed class ListViewItemEventArgs : EventArgs
    {
        public readonly ListViewItem Item;
        public ListViewItemEventArgs(ListViewItem item) { Item = item; }
    }

    // A flat CRT surface: a page or bar background with the scanlines carried
    // over it, and the dashed rule the terminal draws under its top bar. Plain
    // Panels would paint a clean slab and break the scanlines mid-window.
    class CrtPanel : Panel
    {
        public bool RuleBottom, RuleTop;

        public CrtPanel()
        {
            BackColor = Theme.Bg;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            Theme.Surface(this, g, BackColor);
            if (!RuleBottom && !RuleTop) return;
            using (var p = new Pen(Theme.CardLine))
            {
                p.DashStyle = DashStyle.Dash;
                if (RuleBottom) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
                if (RuleTop) g.DrawLine(p, 0, 0, Width, 0);
            }
        }
    }

    // Thin progress bar: marquee (while the total is unknown) or a real percentage
    class SlimMarquee : Control
    {
        readonly Timer timer = new Timer();
        float pos;
        double fraction = -1; // -1 = indeterminate mode (marquee)

        public SlimMarquee()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer, true);
            Height = 3;
            timer.Interval = 16;
            timer.Tick += delegate { pos = (pos + 0.012f) % 1.3f; Invalidate(); };
            Visible = false;
        }

        public void Start() { fraction = -1; Visible = true; timer.Start(); }
        public void Stop() { timer.Stop(); Visible = false; fraction = -1; }

        public void SetFraction(double f)
        {
            fraction = Math.Max(0, Math.Min(1, f));
            timer.Stop();
            if (!Visible) Visible = true;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Bg);
            using (var b = new SolidBrush(Theme.Accent))
            {
                if (fraction >= 0)
                {
                    e.Graphics.FillRectangle(b, 0, 0, (int)(Width * fraction), Height);
                }
                else
                {
                    int w = (int)(Width * 0.3f);
                    int x = (int)(Width * pos) - w;
                    e.Graphics.FillRectangle(b, x, 0, w, Height);
                }
            }
        }
    }
}
