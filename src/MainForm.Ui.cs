// UI construction: window chrome, the seven pages, and language switching.
// The window is fixed-size and the pages are hand-tuned absolute layouts, which
// is what keeps the whole UI in code with no designer and no resize logic.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace WindowsStalker
{
    public partial class MainForm : Form
    {
        // Page area geometry, fixed by the window size below. Named because every
        // page lays itself out against these two numbers.
        const int PageW = 1024, PageH = 591;

        public MainForm(bool startInTray)
        {
            Text = AppName;
            Icon = Brand.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            ClientSize = new Size(1024, 706);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
            Theme.DarkTitleBar(this, true); // the in-window header carries the branding

            ResolvePaths();
            LoadSettings();
            catalog = Rules.Build();
            // A session for the catalog as it stands, so the cleaner page lists
            // every category (with its remembered tick) before anything is
            // analysed — without this the page is blank until ANALYZE is pressed.
            scan = NewSession();
            LoadRecentActivity();

            BuildUi();
            BuildTray();
            ApplyLanguage();
            ShowPage(0);
            RefreshDrives();
            UpdateDashboard();
            EnsureAutostartFirstRun();
            StartScheduleTimer();

            if (startInTray)
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
            }

            Shown += delegate
            {
                if (startInTray) { Hide(); MaybeCheckAppUpdate(); return; }
                // The first normal start decides where the app lives — before any
                // settings or logs are written, so they land in the right place.
                if (!modeAsked && !IsInstalled)
                {
                    modeAsked = true; // one-time question, even if the user says no
                    SaveSettings();
                    if (MessageBox.Show(this,
                            string.Format(Lang.T("msg.firstRunMode"),
                                AppDomain.CurrentDomain.BaseDirectory, InstallDir),
                            AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        LaunchInstaller();
                }
                // Last, not first: the check would otherwise land while the
                // question above still holds the window modal, and a modal dialog
                // is one of the states an update is not allowed to interrupt.
                MaybeCheckAppUpdate();
            };
        }

        // ---------- chrome ----------

        void BuildUi()
        {
            var header = new CrtPanel();
            header.RuleBottom = true; // the dashed rule under the terminal top bar
            headerPanel = header;
            header.Dock = DockStyle.Top;
            header.Height = 78;
            // Docking sets the real width after the children are added, and a
            // high-DPI screen changes it again — re-place the row either way.
            header.SizeChanged += delegate { LayoutNavTabs(); };

            var mark = new Panel();
            mark.SetBounds(24, 17, 44, 44);
            mark.BackColor = Color.Transparent;
            mark.Paint += delegate(object s, PaintEventArgs e)
            {
                Brand.PaintMark(e.Graphics, new RectangleF(0, 0, 44, 44));
            };
            header.Controls.Add(mark);

            headerTitle = new Label();
            // Sized to the wordmark, not padded out: header children added
            // earlier paint on top of ones added later, so an over-wide label
            // here silently covers the first nav tab.
            headerTitle.SetBounds(78, 20, 152, 24);
            // AutoSize, not the fixed 152: the wordmark is wider than that in
            // caps and a Label silently wraps the overflow onto a second line
            // that its 24px height then hides. AutoSize still keeps it exactly
            // as wide as the text, which is what stops it covering the tabs.
            headerTitle.AutoSize = true;
            headerTitle.Font = Theme.UiBold(14f);
            headerTitle.ForeColor = Theme.Text;
            headerTitle.BackColor = Color.Transparent;
            // Set in caps like every other heading here, with the camel hump
            // turned into a space: WINDOWSSTALKER is a wall of letters, and the
            // name itself has to stay one word everywhere else (install folder,
            // registry value, mutex), so the split happens here and only here.
            headerTitle.Text = Regex.Replace(AppName, "(?<=[a-z])(?=[A-Z])", " ").ToUpperInvariant();
            header.Controls.Add(headerTitle);

            var tagline = new Label();
            tagline.Name = "app.tagline";
            tagline.SetBounds(80, 45, 150, 18);
            tagline.Font = Theme.Ui(8.25f);
            tagline.ForeColor = Theme.Muted;
            tagline.BackColor = Color.Transparent;
            header.Controls.Add(tagline);

            // The tabs go straight into the header and are positioned by
            // LayoutNavTabs once they have their text. Neither an auto-sizing
            // FlowLayoutPanel nor an anchored sub-panel works here: both measure
            // themselves before ApplyLanguage has given the tabs a width, and end
            // up either clipping the first tab or pushing the row off the edge.
            IconDraw[] navIcons = { Ico.Pie, Ico.Broom, Ico.Registry, Ico.Rocket, Ico.Box, Ico.Duplicate, Ico.Gear };
            navTabs = new NavTab[navIcons.Length];
            for (int i = 0; i < navIcons.Length; i++)
            {
                var tab = new NavTab("", navIcons[i]);
                int index = i;
                tab.Click += delegate { ShowPage(index); };
                navTabs[i] = tab;
                header.Controls.Add(tab);
            }
            Controls.Add(header);

            var statusBar = new CrtPanel();
            statusBar.RuleTop = true;
            statusBar.Dock = DockStyle.Bottom;
            statusBar.Height = 34;
            statusLabel = new Label();
            statusLabel.SetBounds(24, 8, 900, 20);
            statusLabel.ForeColor = Theme.Muted;
            statusLabel.BackColor = Color.Transparent;
            statusBar.Controls.Add(statusLabel);
            Controls.Add(statusBar);

            progress = new SlimMarquee();
            progress.Dock = DockStyle.Bottom;
            Controls.Add(progress);

            pageHost = new CrtPanel();
            pageHost.Dock = DockStyle.Fill;
            Controls.Add(pageHost);

            pages = new Panel[]
            {
                BuildDashboardPage(), BuildCleanerPage(), BuildRegistryPage(),
                BuildStartupPage(), BuildAppsPage(), BuildSpacePage(), BuildSettingsPage()
            };
            foreach (Panel p in pages)
            {
                p.Dock = DockStyle.Fill;
                p.Visible = false;
                pageHost.Controls.Add(p);
            }

            // Dock layout walks the Controls collection from the highest index
            // down, each control taking a bite out of what is left. The Fill one
            // must therefore sit at index 0 so it is laid out LAST and receives
            // the leftover rectangle — at any other index it swallows the whole
            // client area and the header ends up painted on top of the page.
            Controls.SetChildIndex(pageHost, 0);
        }

        // Right-aligns the tab row against the header's right edge. Called after
        // every language switch, since each label changes the tab widths.
        void LayoutNavTabs()
        {
            if (navTabs == null || headerPanel == null) return;
            // leftLimit reserves the wordmark's column; the Ukrainian labels are
            // the widest set and still fit to its right at this window size.
            const int gap = 4, rightPad = 20, leftLimit = 236;
            int total = -gap;
            foreach (NavTab tab in navTabs) total += tab.Width + gap;
            int x = Math.Max(leftLimit, headerPanel.ClientSize.Width - total - rightPad);
            foreach (NavTab tab in navTabs)
            {
                tab.SetBounds(x, 17, tab.Width, 44);
                x += tab.Width + gap;
            }
        }

        void ShowPage(int index)
        {
            if (pages == null || index < 0 || index >= pages.Length) return;
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].Visible = i == index;
                navTabs[i].SetActive(i == index);
            }
            // Lists are populated the first time their page is opened, not at
            // startup: enumerating installed programs and startup entries costs
            // real time and most sessions never open those pages.
            if (index == 3 && startupList.Items.Count == 0) RefreshStartup();
            if (index == 4 && appsList.Items.Count == 0) RefreshApps();
            if (index == 6) RefreshSettingsStatus();
        }

        // ---------- small builders ----------
        //
        // Every page is built from the same three pieces, the way the AV project
        // does it: a StatStrip of numbers across the top, a CardPanel filling the
        // rest with the list inside it, and a row of buttons along the bottom of
        // that card. Keeping the geometry in named constants here is what makes
        // the five list pages line up with each other pixel for pixel.

        const int Pad = 24;                        // page side margin
        const int ContentW = PageW - Pad * 2;      // 976
        const int StripY = 6, StripH = 66;
        const int CardY = StripY + StripH + 14;    // 86
        const int CardH = 462;                     // the card bottom lands at 548
        const int CardInsetX = 16, CardTop = 44, CardBottomPad = 14;
        const int ButtonRowH = 46;
        const int SummaryY = CardY + CardH + 8;

        ModernButton Btn(IconDraw icon, Color back, Color hover, Color fore,
                         int x, int y, int w, int h, EventHandler onClick)
        {
            var b = new ModernButton("", back, hover, fore);
            b.Icon = icon;
            b.SetBounds(x, y, w, h);
            b.BackColor = Theme.Bg;
            if (onClick != null) b.Click += onClick;
            return b;
        }

        // The same button sitting on a card: the rounded corners have to be cut
        // out of the card colour rather than the page background.
        ModernButton CardBtn(IconDraw icon, Color back, Color hover, Color fore,
                             int x, int y, int w, int h, EventHandler onClick)
        {
            ModernButton b = Btn(icon, back, hover, fore, x, y, w, h, onClick);
            b.BackColor = Theme.Card;
            return b;
        }

        // A big tile: icon above the label, muted caption underneath.
        ModernButton Tile(IconDraw icon, Color back, Color hover, Color fore,
                          int x, int y, int w, int h, EventHandler onClick)
        {
            ModernButton b = Btn(icon, back, hover, fore, x, y, w, h, onClick);
            b.CardStyle = true;
            b.Font = Theme.UiBold(10f);
            return b;
        }

        static Label Lbl(int x, int y, int w, int h, Font font, Color color)
        {
            var l = new Label();
            l.SetBounds(x, y, w, h);
            l.Font = font;
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            return l;
        }

        static CardPanel Card(int x, int y, int w, int h)
        {
            var c = new CardPanel("");
            c.SetBounds(x, y, w, h);
            return c;
        }

        static DarkList List(int x, int y, int w, int h)
        {
            var l = new DarkList();
            l.SetBounds(x, y, w, h);
            return l;
        }

        static Panel Page()
        {
            var p = new CrtPanel();
            // Sized up front so the absolute child coordinates below are laid out
            // against the real page area even before the form docks it.
            p.Size = new Size(PageW, PageH);
            return p;
        }

        // The strip of numbers every list page carries, plus the card under it.
        // Returns the card; the caller drops its list and buttons inside.
        CardPanel BuildListPage(Panel page, string cardKey, out StatStrip strip)
        {
            strip = new StatStrip();
            strip.SetBounds(Pad, StripY, ContentW, StripH);
            page.Controls.Add(strip);

            CardPanel card = Card(Pad, CardY, ContentW, CardH);
            card.Name = cardKey;
            page.Controls.Add(card);
            return card;
        }

        static int ListHeight()
        {
            return CardH - CardTop - CardBottomPad - ButtonRowH - 10;
        }

        // The list fills the card above its button row.
        DarkList CardList(CardPanel card)
        {
            DarkList list = List(CardInsetX, CardTop, ContentW - CardInsetX * 2, ListHeight());
            card.Controls.Add(list);
            return list;
        }

        static int ButtonRowY() { return CardTop + ListHeight() + 10; }

        Label PageSummary(Panel page)
        {
            Label l = Lbl(Pad, SummaryY, ContentW, 26, Theme.Ui(9.5f), Theme.Muted);
            page.Controls.Add(l);
            return l;
        }

        // Drops a control into the right-hand end of a strip (the search box, the
        // folder picker) — the one place AV puts a control inside a StatStrip.
        static void StripRight(StatStrip strip, Control c, int w, int h)
        {
            c.SetBounds(strip.Width - w - 20, (strip.Height - 6 - h) / 2, w, h);
            strip.Controls.Add(c);
        }

        // ---------- dashboard ----------

        Panel BuildDashboardPage()
        {
            var page = Page();

            // Hero row: the one big action on the left, the state of the machine
            // on the right — the shape AV uses for QUICK SCAN plus the shield.
            dashAnalyze = Tile(Ico.Radiation, Theme.Accent, Theme.AccentHot, Theme.OnAccent,
                Pad, 6, 300, 186, delegate { ShowPage(1); StartAnalyze(false); });
            dashAnalyze.Font = Theme.UiBold(12f);
            page.Controls.Add(dashAnalyze);

            // Occupies the same cell as the analyze tile and swaps in while work
            // is running, so the row never reflows.
            dashStop = Tile(Ico.StopIcon, Theme.Danger, Theme.DangerHot, Theme.OnAccent,
                Pad, 6, 300, 186, delegate { CancelWork(); });
            dashStop.Font = Theme.UiBold(12f);
            dashStop.Visible = false;
            page.Controls.Add(dashStop);

            heroBanner = new StatusBanner();
            heroBanner.SetBounds(340, 6, 660, 186);
            page.Controls.Add(heroBanner);

            gauge = new Gauge();
            gauge.SetBounds(24, 30, 126, 126);
            gauge.BackColor = Theme.Card;
            heroBanner.Controls.Add(gauge);

            dashHeadline = Lbl(174, 56, 460, 32, Theme.UiBold(15f), Theme.Text);
            dashHeadline.BackColor = Color.Transparent;
            heroBanner.Controls.Add(dashHeadline);

            dashSubline = Lbl(174, 92, 460, 44, Theme.Ui(9.5f), Theme.Muted);
            dashSubline.BackColor = Color.Transparent;
            heroBanner.Controls.Add(dashSubline);

            // Quick-action tiles: four equal columns across the full width
            const int tileY = 200, tileH = 116, tileW = 235, tileGap = 12;
            dashClean = Tile(Ico.Broom, Theme.Btn, Theme.BtnHot, Theme.BtnText,
                Pad, tileY, tileW, tileH, delegate { StartClean(); });
            dashClean.Enabled = false;
            page.Controls.Add(dashClean);

            tileRegistry = Tile(Ico.Registry, Theme.Card, Theme.CardLine, Theme.Text,
                Pad + (tileW + tileGap), tileY, tileW, tileH,
                delegate { ShowPage(2); StartRegistryScan(); });
            page.Controls.Add(tileRegistry);

            tileBig = Tile(Ico.Pie, Theme.Card, Theme.CardLine, Theme.Text,
                Pad + (tileW + tileGap) * 2, tileY, tileW, tileH,
                delegate { ShowPage(5); StartSpaceScan(false); });
            page.Controls.Add(tileBig);

            tileDupes = Tile(Ico.Duplicate, Theme.Card, Theme.CardLine, Theme.Text,
                Pad + (tileW + tileGap) * 3, tileY, tileW, tileH,
                delegate { ShowPage(5); StartSpaceScan(true); });
            page.Controls.Add(tileDupes);

            dashStats = new StatStrip();
            dashStats.SetBounds(Pad, 324, ContentW, StripH);
            page.Controls.Add(dashStats);

            var storage = Card(Pad, 398, 478, 186);
            storage.Name = "card.storage";
            drivesHost = new Panel();
            drivesHost.SetBounds(18, 44, 442, 128);
            drivesHost.BackColor = Theme.Card;
            drivesHost.AutoScroll = true; // more than two fixed drives simply scrolls
            storage.Controls.Add(drivesHost);
            page.Controls.Add(storage);

            var activity = Card(522, 398, 478, 186);
            activity.Name = "card.activity";
            activityList = List(16, 44, 446, 128);
            activityList.Columns.Add(Lang.T("col.event"), 440);
            activityList.CheckState = delegate(ListViewItem it) { return -1; };
            activityList.BackColor = Theme.Card; // sits on a card, not on the page
            activity.Controls.Add(activityList);
            page.Controls.Add(activity);

            return page;
        }

        void RefreshDrives()
        {
            if (drivesHost == null) return;
            drivesHost.Controls.Clear();
            int y = 0;
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); }
            catch { return; }
            foreach (DriveInfo d in drives)
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    var bar = new DriveBar();
                    bar.Letter = d.Name.TrimEnd('\\');
                    bar.Label = d.VolumeLabel;
                    bar.Total = d.TotalSize;
                    bar.Free = d.AvailableFreeSpace;
                    bar.SetBounds(0, y, drivesHost.ClientSize.Width - 4, 46);
                    drivesHost.Controls.Add(bar);
                    y += 56;
                }
                catch { }
            }
        }

        // Repaints the dashboard from whatever the current session says. Called
        // after an analysis, after a clean, and on every language switch.
        void UpdateDashboard()
        {
            if (gauge == null) return;
            // The headline is what CLEAN would actually free — the ticked, unblocked
            // rules — not everything the analysis happened to measure. The wider
            // total goes in the stat strip instead, where it reads as information
            // rather than as a promise.
            long bytes = scan.Finished ? scan.SelectedBytes : 0;
            int locations = 0;
            foreach (RuleResult r in scan.Results) if (!r.Empty) locations++;

            if (!scan.Finished)
            {
                gauge.SetIdle("", Lang.T("dash.gaugeIdle"));
                dashHeadline.Text = Lang.T("dash.idleHead");
                dashSubline.Text = Lang.T("dash.idleSub");
                heroBanner.SetAccent(Theme.Accent);
            }
            else if (bytes > 0)
            {
                gauge.SetResult(GaugeState.Result, Util.FormatSize(bytes), Lang.T("dash.gaugeFound"));
                dashHeadline.Text = string.Format(Lang.T("dash.foundHead"), Util.FormatSize(bytes));
                dashSubline.Text = string.Format(Lang.T("dash.foundSub"),
                    Util.FormatCount(scan.TotalFiles), locations);
                heroBanner.SetAccent(Theme.Warn);
            }
            else
            {
                gauge.SetResult(GaugeState.Clean, "", Lang.T("dash.gaugeClean"));
                dashHeadline.Text = Lang.T("dash.cleanHead");
                dashSubline.Text = Lang.T("dash.cleanSub");
                heroBanner.SetAccent(Theme.Good);
            }
            dashClean.Enabled = scan.Finished && scan.SelectedBytes > 0 && !cleanRunning;

            dashStats.Captions = new string[]
            {
                Lang.T("stat.lastClean"), Lang.T("stat.freedTotal"),
                Lang.T("stat.junkFound"), Lang.T("stat.startupItems")
            };
            dashStats.Values = new string[]
            {
                lastCleanTime == DateTime.MinValue ? Lang.T("common.never") : lastCleanTime.ToString("dd.MM.yyyy"),
                Util.FormatSize(totalFreedBytes),
                scan.Finished ? Util.FormatSize(scan.TotalBytes) : "—",
                startupCount >= 0 ? startupCount.ToString() : "—"
            };
            dashStats.ValueColors = new Color[]
            {
                Color.Empty, Theme.Good, bytes > 0 ? Theme.Warn : Color.Empty, Color.Empty
            };
            dashStats.Invalidate();
        }

        // ---------- cleaner ----------

        Panel BuildCleanerPage()
        {
            var page = Page();
            CardPanel card = BuildListPage(page, "card.cleaner", out cleanStrip);

            cleanList = CardList(card);
            cleanList.Columns.Add(Lang.T("col.item"), 470);
            cleanList.Columns.Add(Lang.T("col.size"), 130, HorizontalAlignment.Right);
            cleanList.Columns.Add(Lang.T("col.files"), 90, HorizontalAlignment.Right);
            cleanList.Columns.Add(Lang.T("col.status"), 250);
            cleanList.CheckState = delegate(ListViewItem it)
            {
                var rr = it.Tag as RuleResult;
                if (rr == null) return -1;
                return rr.Selected ? 1 : 0;
            };
            cleanList.CheckToggled += delegate(object s, ListViewItemEventArgs e)
            {
                var rr = e.Item.Tag as RuleResult;
                if (rr == null) return;
                rr.Selected = !rr.Selected;
                ruleChoices[rr.Rule.Id] = rr.Selected;
                cleanList.Invalidate(e.Item.Bounds);
                UpdateCleanSummary();
            };

            int y = ButtonRowY();
            btnAnalyze = CardBtn(Ico.Search, Theme.Accent, Theme.AccentHot, Theme.OnAccent,
                CardInsetX, y, 170, ButtonRowH, delegate { StartAnalyze(false); });
            btnAnalyze.Font = Theme.UiBold(10f);
            card.Controls.Add(btnAnalyze);

            btnClean = CardBtn(Ico.Broom, Theme.Btn, Theme.BtnHot, Theme.BtnText,
                CardInsetX + 180, y, 170, ButtonRowH, delegate { StartClean(); });
            btnClean.Font = Theme.UiBold(10f);
            btnClean.Enabled = false;
            card.Controls.Add(btnClean);

            btnStopScan = CardBtn(Ico.StopIcon, Theme.Danger, Theme.DangerHot, Theme.OnAccent,
                CardInsetX + 360, y, 140, ButtonRowH, delegate { CancelWork(); });
            btnStopScan.Visible = false;
            card.Controls.Add(btnStopScan);

            // The three selection helpers sit at the far end, well away from the
            // two buttons that actually delete something.
            int right = ContentW - CardInsetX;
            btnSelectNone = CardBtn(null, Theme.Subtle, Theme.CardLine, Theme.Text,
                right - 150, y + 5, 150, 36, delegate { SelectRules(0); });
            card.Controls.Add(btnSelectNone);
            btnSelectAll = CardBtn(null, Theme.Subtle, Theme.CardLine, Theme.Text,
                right - 310, y + 5, 150, 36, delegate { SelectRules(1); });
            card.Controls.Add(btnSelectAll);
            btnSelectRecommended = CardBtn(Ico.Check, Theme.Subtle, Theme.CardLine, Theme.Text,
                right - 480, y + 5, 160, 36, delegate { SelectRules(2); });
            card.Controls.Add(btnSelectRecommended);

            cleanSummary = PageSummary(page);
            return page;
        }

        // ---------- registry ----------

        Panel BuildRegistryPage()
        {
            var page = Page();
            CardPanel card = BuildListPage(page, "card.registry", out regStrip);

            regList = CardList(card);
            regList.Columns.Add(Lang.T("col.item"), 240);
            regList.Columns.Add(Lang.T("col.detail"), 400);
            regList.Columns.Add(Lang.T("col.location"), 300);
            regList.CheckState = delegate(ListViewItem it)
            {
                var issue = it.Tag as RegIssue;
                if (issue == null) return -1;
                return issue.Selected ? 1 : 0;
            };
            regList.CheckToggled += delegate(object s, ListViewItemEventArgs e)
            {
                var issue = e.Item.Tag as RegIssue;
                if (issue == null) return;
                issue.Selected = !issue.Selected;
                regList.Invalidate(e.Item.Bounds);
                UpdateRegSummary();
            };

            int y = ButtonRowY();
            btnRegScan = CardBtn(Ico.Search, Theme.Accent, Theme.AccentHot, Theme.OnAccent,
                CardInsetX, y, 200, ButtonRowH, delegate { StartRegistryScan(); });
            btnRegScan.Font = Theme.UiBold(10f);
            card.Controls.Add(btnRegScan);

            btnRegFix = CardBtn(Ico.Check, Theme.Btn, Theme.BtnHot, Theme.BtnText,
                CardInsetX + 210, y, 200, ButtonRowH, delegate { FixRegistryIssues(); });
            btnRegFix.Font = Theme.UiBold(10f);
            btnRegFix.Enabled = false;
            card.Controls.Add(btnRegFix);

            int right = ContentW - CardInsetX;
            btnRegBackupFolder = CardBtn(Ico.FolderIcon, Theme.Subtle, Theme.CardLine, Theme.Text,
                right - 190, y + 5, 190, 36, delegate { OpenInExplorer(backupDir); });
            card.Controls.Add(btnRegBackupFolder);
            btnRegSelectAll = CardBtn(null, Theme.Subtle, Theme.CardLine, Theme.Text,
                right - 350, y + 5, 150, 36, delegate { SelectRegIssues(true); });
            card.Controls.Add(btnRegSelectAll);

            regSummary = PageSummary(page);
            return page;
        }

        // ---------- startup ----------

        Panel BuildStartupPage()
        {
            var page = Page();
            CardPanel card = BuildListPage(page, "card.startup", out startupStrip);

            startupList = CardList(card);
            startupList.Columns.Add(Lang.T("col.name"), 250);
            startupList.Columns.Add(Lang.T("col.status"), 110);
            startupList.Columns.Add(Lang.T("col.location"), 220);
            startupList.Columns.Add(Lang.T("col.path"), 360);
            startupList.CheckState = delegate(ListViewItem it) { return -1; };
            startupList.DoubleClick += delegate { ToggleStartupSelected(); };

            int y = ButtonRowY();
            btnStartupToggle = CardBtn(Ico.Rocket, Theme.Accent, Theme.AccentHot, Theme.OnAccent,
                CardInsetX, y, 210, ButtonRowH, delegate { ToggleStartupSelected(); });
            btnStartupToggle.Font = Theme.UiBold(10f);
            card.Controls.Add(btnStartupToggle);
            btnStartupOpen = CardBtn(Ico.FolderIcon, Theme.Subtle, Theme.CardLine, Theme.Text,
                CardInsetX + 220, y + 5, 200, 36, delegate { OpenStartupLocation(); });
            card.Controls.Add(btnStartupOpen);
            btnStartupDelete = CardBtn(Ico.Trash, Theme.Subtle, Theme.CardLine, Theme.DangerHot,
                CardInsetX + 430, y + 5, 200, 36, delegate { DeleteStartupSelected(); });
            card.Controls.Add(btnStartupDelete);

            int right = ContentW - CardInsetX;
            btnStartupRefresh = CardBtn(Ico.Refresh, Theme.Subtle, Theme.CardLine, Theme.Text,
                right - 160, y + 5, 160, 36, delegate { RefreshStartup(); });
            card.Controls.Add(btnStartupRefresh);

            startupSummary = PageSummary(page);
            return page;
        }

        // ---------- apps ----------

        Panel BuildAppsPage()
        {
            var page = Page();
            CardPanel card = BuildListPage(page, "card.apps", out appsStrip);

            // The search box lives in the strip, the way AV puts the quarantine
            // filter there — it belongs with the counts it filters.
            appsSearch = new TextBox();
            appsSearch.BackColor = Theme.LogBg;
            appsSearch.ForeColor = Theme.Text;
            appsSearch.BorderStyle = BorderStyle.FixedSingle;
            appsSearch.Font = Theme.Ui(10f);
            appsSearch.TextChanged += delegate { FilterApps(); };
            appsSearch.HandleCreated += delegate
            {
                try
                {
                    NativeMethods.SendMessage(appsSearch.Handle, NativeMethods.EM_SETCUEBANNER,
                        (IntPtr)1, Lang.T("apps.searchHint"));
                }
                catch { }
            };
            StripRight(appsStrip, appsSearch, 280, 26);

            appsList = CardList(card);
            appsList.Columns.Add(Lang.T("col.name"), 350);
            appsList.Columns.Add(Lang.T("col.publisher"), 230);
            appsList.Columns.Add(Lang.T("col.version"), 120);
            appsList.Columns.Add(Lang.T("col.size"), 110, HorizontalAlignment.Right);
            appsList.Columns.Add(Lang.T("col.installed"), 120);
            appsList.CheckState = delegate(ListViewItem it) { return -1; };
            appsList.DoubleClick += delegate { UninstallSelectedApp(); };

            int y = ButtonRowY();
            btnAppsUninstall = CardBtn(Ico.Trash, Theme.Accent, Theme.AccentHot, Theme.OnAccent,
                CardInsetX, y, 220, ButtonRowH, delegate { UninstallSelectedApp(); });
            btnAppsUninstall.Font = Theme.UiBold(10f);
            card.Controls.Add(btnAppsUninstall);
            btnAppsOpenFolder = CardBtn(Ico.FolderIcon, Theme.Subtle, Theme.CardLine, Theme.Text,
                CardInsetX + 230, y + 5, 200, 36, delegate { OpenSelectedAppFolder(); });
            card.Controls.Add(btnAppsOpenFolder);

            int right = ContentW - CardInsetX;
            btnAppsRefresh = CardBtn(Ico.Refresh, Theme.Subtle, Theme.CardLine, Theme.Text,
                right - 160, y + 5, 160, 36, delegate { RefreshApps(); });
            card.Controls.Add(btnAppsRefresh);

            appsSummary = PageSummary(page);
            return page;
        }

        // ---------- space ----------

        Panel BuildSpacePage()
        {
            var page = Page();
            CardPanel card = BuildListPage(page, "card.space", out spaceStrip);

            btnSpacePickFolder = new ModernButton("", Theme.Subtle, Theme.CardLine, Theme.Text);
            btnSpacePickFolder.Icon = Ico.FolderIcon;
            btnSpacePickFolder.BackColor = Theme.Card;
            btnSpacePickFolder.Click += delegate { PickSpaceFolder(); };
            StripRight(spaceStrip, btnSpacePickFolder, 190, 36);

            spaceList = CardList(card);
            spaceList.Columns.Add(Lang.T("col.name"), 360);
            spaceList.Columns.Add(Lang.T("col.size"), 130, HorizontalAlignment.Right);
            spaceList.Columns.Add(Lang.T("col.path"), 450);
            spaceList.CheckState = delegate(ListViewItem it)
            {
                var entry = it.Tag as SpaceEntry;
                if (entry == null || entry.Keep) return -1;
                return entry.Selected ? 1 : 0;
            };
            spaceList.CheckToggled += delegate(object s, ListViewItemEventArgs e)
            {
                var entry = e.Item.Tag as SpaceEntry;
                if (entry == null || entry.Keep) return;
                entry.Selected = !entry.Selected;
                spaceList.Invalidate(e.Item.Bounds);
                UpdateSpaceSummary();
            };

            int y = ButtonRowY();
            btnSpaceScanBig = CardBtn(Ico.Pie, Theme.Accent, Theme.AccentHot, Theme.OnAccent,
                CardInsetX, y, 200, ButtonRowH, delegate { StartSpaceScan(false); });
            btnSpaceScanBig.Font = Theme.UiBold(10f);
            card.Controls.Add(btnSpaceScanBig);
            btnSpaceScanDupes = CardBtn(Ico.Duplicate, Theme.Subtle, Theme.CardLine, Theme.Text,
                CardInsetX + 210, y, 200, ButtonRowH, delegate { StartSpaceScan(true); });
            btnSpaceScanDupes.Font = Theme.UiBold(10f);
            card.Controls.Add(btnSpaceScanDupes);

            // Covers both scan buttons while a scan runs. A duplicate search over
            // a home folder is the longest job in the app, and this page had no way
            // to call it off: the Stop buttons lived on the dashboard and the
            // cleaner page, neither of which is where the user just pressed
            // DUPLICATES. Spanning the pair rather than sitting beside them keeps
            // it clear of the delete/open buttons at the other end of the row.
            btnSpaceStop = CardBtn(Ico.StopIcon, Theme.Danger, Theme.DangerHot, Theme.OnAccent,
                CardInsetX, y, 410, ButtonRowH, delegate { CancelWork(); });
            btnSpaceStop.Font = Theme.UiBold(10f);
            btnSpaceStop.Visible = false;
            card.Controls.Add(btnSpaceStop);

            int right = ContentW - CardInsetX;
            btnSpaceOpen = CardBtn(Ico.FolderIcon, Theme.Subtle, Theme.CardLine, Theme.Text,
                right - 200, y + 5, 200, 36, delegate { OpenSelectedSpace(); });
            card.Controls.Add(btnSpaceOpen);
            btnSpaceDelete = CardBtn(Ico.Trash, Theme.Subtle, Theme.CardLine, Theme.DangerHot,
                right - 460, y + 5, 250, 36, delegate { RecycleSelectedSpace(); });
            btnSpaceDelete.Enabled = false;
            card.Controls.Add(btnSpaceDelete);

            // The chosen folder is shown as the first cell of the strip rather than
            // as its own label; the field stays so the space code has one place to
            // push the value, and it is hidden off the page.
            spaceFolderLabel = Lbl(0, 0, 0, 0, Theme.Ui(8.5f), Theme.Muted);
            spaceFolderLabel.Visible = false;
            page.Controls.Add(spaceFolderLabel);

            spaceSummary = PageSummary(page);
            return page;
        }

        // ---------- settings ----------

        Panel BuildSettingsPage()
        {
            var page = Page();

            // General
            var general = Card(24, 6, 476, 252);
            general.Name = "card.general";
            chkAutostart = new Toggle("");
            chkAutostart.SetBounds(20, 52, 400, 26);
            chkAutostart.BackColor = Theme.Card;
            chkAutostart.CheckedChanged += delegate { SetAutostart(chkAutostart.Checked); };
            general.Controls.Add(chkAutostart);

            chkConfirm = new Toggle("");
            chkConfirm.SetBounds(20, 88, 400, 26);
            chkConfirm.BackColor = Theme.Card;
            chkConfirm.CheckedChanged += delegate { confirmBeforeClean = chkConfirm.Checked; SaveSettings(); };
            general.Controls.Add(chkConfirm);

            chkTrayClose = new Toggle("");
            chkTrayClose.SetBounds(20, 124, 400, 26);
            chkTrayClose.BackColor = Theme.Card;
            chkTrayClose.CheckedChanged += delegate { closeToTray = chkTrayClose.Checked; SaveSettings(); };
            general.Controls.Add(chkTrayClose);

            var langLabel = Lbl(20, 166, 120, 22, Theme.UiBold(9f), Theme.Muted);
            langLabel.Name = "card.language";
            langLabel.BackColor = Color.Transparent;
            general.Controls.Add(langLabel);

            btnLangEn = Btn(null, Theme.Subtle, Theme.CardLine, Theme.Text, 20, 192, 120, 34,
                delegate { SetLanguage(Lang.Language.English); });
            btnLangEn.Text = "English";
            btnLangEn.BackColor = Theme.Card;
            general.Controls.Add(btnLangEn);
            btnLangUk = Btn(null, Theme.Subtle, Theme.CardLine, Theme.Text, 150, 192, 120, 34,
                delegate { SetLanguage(Lang.Language.Ukrainian); });
            btnLangUk.Text = "Українська";
            btnLangUk.BackColor = Theme.Card;
            general.Controls.Add(btnLangUk);
            page.Controls.Add(general);

            // Automatic clean
            var schedule = Card(524, 6, 476, 252);
            schedule.Name = "card.schedule";
            btnSchedOff = Btn(null, Theme.Subtle, Theme.CardLine, Theme.Text, 20, 52, 138, 34,
                delegate { SetSchedule(0); });
            btnSchedOff.BackColor = Theme.Card;
            schedule.Controls.Add(btnSchedOff);
            btnSchedDaily = Btn(null, Theme.Subtle, Theme.CardLine, Theme.Text, 168, 52, 138, 34,
                delegate { SetSchedule(1); });
            btnSchedDaily.BackColor = Theme.Card;
            schedule.Controls.Add(btnSchedDaily);
            btnSchedWeekly = Btn(null, Theme.Subtle, Theme.CardLine, Theme.Text, 316, 52, 138, 34,
                delegate { SetSchedule(2); });
            btnSchedWeekly.BackColor = Theme.Card;
            schedule.Controls.Add(btnSchedWeekly);

            var schedHint = Lbl(20, 100, 434, 76, Theme.Ui(9f), Theme.Muted);
            schedHint.Name = "sched.hint";
            schedHint.BackColor = Color.Transparent;
            schedule.Controls.Add(schedHint);

            var schedLast = Lbl(20, 186, 434, 40, Theme.Ui(9f), Theme.Muted);
            schedLast.Name = "sched.last";
            schedLast.BackColor = Color.Transparent;
            schedule.Controls.Add(schedLast);
            page.Controls.Add(schedule);

            // Status
            var status = Card(24, 270, 476, 314);
            status.Name = "card.status";
            setStatusCaps = new Label[4];
            setStatusVals = new Label[4];
            for (int i = 0; i < 4; i++)
            {
                setStatusCaps[i] = Lbl(20, 52 + i * 32, 190, 22, Theme.Ui(9f), Theme.Muted);
                setStatusCaps[i].BackColor = Color.Transparent;
                setStatusVals[i] = Lbl(216, 52 + i * 32, 238, 22, Theme.UiBold(9f), Theme.Text);
                setStatusVals[i].BackColor = Color.Transparent;
                status.Controls.Add(setStatusCaps[i]);
                status.Controls.Add(setStatusVals[i]);
            }
            installedBadge = Lbl(20, 184, 200, 20, Theme.UiBold(9f), Theme.Good);
            installedBadge.BackColor = Color.Transparent;
            status.Controls.Add(installedBadge);

            // Install/uninstall and the log belong with the mode readout above
            // them rather than in the About card, which is now the updates card
            // too. Full width, because the Ukrainian captions are half again as
            // long as the English ones and a two-across row clips them.
            btnInstall = Btn(Ico.Download, Theme.Accent, Theme.AccentHot, Theme.OnAccent,
                20, 214, 434, 38, delegate { InstallOrUninstall(); });
            btnInstall.BackColor = Theme.Card;
            status.Controls.Add(btnInstall);

            btnOpenLog = Btn(Ico.FileIcon, Theme.Subtle, Theme.CardLine, Theme.Text,
                20, 260, 434, 38, delegate { OpenLog(); });
            btnOpenLog.BackColor = Theme.Card;
            status.Controls.Add(btnOpenLog);
            page.Controls.Add(status);

            // About and updates
            var about = Card(524, 270, 476, 314);
            about.Name = "card.about";
            var aboutText = Lbl(20, 48, 434, 76, Theme.Ui(8.75f), Theme.Muted);
            aboutText.Name = "about.text";
            aboutText.BackColor = Color.Transparent;
            about.Controls.Add(aboutText);

            chkAutoUpdate = new Toggle("");
            chkAutoUpdate.SetBounds(20, 132, 434, 26);
            chkAutoUpdate.BackColor = Theme.Card;
            chkAutoUpdate.CheckedChanged += delegate
            {
                autoUpdate = chkAutoUpdate.Checked;
                SaveSettings();
                RefreshUpdateStatus();
                // Switching it on should act on it, not wait for the next tick
                if (autoUpdate) MaybeCheckAppUpdate();
            };
            about.Controls.Add(chkAutoUpdate);

            updateStatus = Lbl(20, 164, 434, 34, Theme.Ui(8.5f), Theme.Muted);
            updateStatus.BackColor = Color.Transparent;
            about.Controls.Add(updateStatus);

            btnAbout = Btn(Ico.Info, Theme.Accent, Theme.AccentHot, Theme.OnAccent,
                20, 206, 434, 38, delegate { ShowAboutDialog(); });
            btnAbout.BackColor = Theme.Card;
            about.Controls.Add(btnAbout);

            btnCheckUpdate = Btn(Ico.Download, Theme.Subtle, Theme.CardLine, Theme.Text,
                20, 252, 434, 38, delegate { CheckForUpdatesNow(); });
            btnCheckUpdate.BackColor = Theme.Card;
            about.Controls.Add(btnCheckUpdate);
            page.Controls.Add(about);
            return page;
        }

        void RefreshSettingsStatus()
        {
            if (setStatusCaps == null) return;
            string[] caps = { Lang.T("set.mode"), Lang.T("set.version"), Lang.T("set.rules"), Lang.T("set.autoClean") };
            string[] vals =
            {
                IsInstalled ? Lang.T("set.installed") : Lang.T("set.portable"),
                AppVersion,
                catalog.Count.ToString(),
                schedMode == 0 ? Lang.T("sched.off") : schedMode == 1 ? Lang.T("sched.daily") : Lang.T("sched.weekly")
            };
            for (int i = 0; i < setStatusCaps.Length; i++)
            {
                setStatusCaps[i].Text = caps[i];
                setStatusVals[i].Text = vals[i];
            }
            installedBadge.Text = IsInstalled ? Lang.T("badge.installed") : "";
            chkAutostart.Checked = AutostartEnabled;
            chkConfirm.Checked = confirmBeforeClean;
            chkTrayClose.Checked = closeToTray;
            chkAutoUpdate.Checked = autoUpdate;
            UpdateSchedButtons();
            RefreshUpdateStatus();
            btnInstall.Text = IsInstalled ? Lang.T("btn.uninstallApp") : Lang.T("btn.installApp");
        }

        void UpdateSchedButtons()
        {
            if (btnSchedOff == null) return;
            ModernButton[] all = { btnSchedOff, btnSchedDaily, btnSchedWeekly };
            for (int i = 0; i < all.Length; i++)
            {
                bool active = schedMode == i;
                all[i].Back = active ? Theme.Accent : Theme.Subtle;
                all[i].Hover = active ? Theme.AccentHot : Theme.CardLine;
                all[i].TextColor = active ? Theme.OnAccent : Theme.Text;
                all[i].Invalidate();
            }
        }

        void SetSchedule(int mode)
        {
            schedMode = mode;
            lastScheduledClean = DateTime.Now; // do not fire the moment it is switched on
            SaveSettings();
            UpdateSchedButtons();
            RefreshSettingsStatus();
        }

        void SetLanguage(Lang.Language language)
        {
            Lang.Current = language;
            SaveSettings();
            ApplyLanguage();
        }

        void OpenLog()
        {
            try
            {
                if (logPath != null && File.Exists(logPath))
                    System.Diagnostics.Process.Start("notepad.exe", "\"" + logPath + "\"");
            }
            catch { }
        }

        // ---------- language ----------

        // Every persistent caption is re-read here, so switching languages never
        // needs a restart. Controls whose only text is a Lang key carry that key
        // in Name — one loop then covers all the static labels and card headers.
        void ApplyLanguage()
        {
            string[] navKeys =
            {
                "nav.dashboard", "nav.cleaner", "nav.registry",
                "nav.startup", "nav.apps", "nav.space", "nav.settings"
            };
            for (int i = 0; i < navTabs.Length; i++) navTabs[i].Text = Lang.T(navKeys[i]);
            LayoutNavTabs(); // the Ukrainian labels are wider — re-place them

            RetextByName(this);

            btnAnalyze.Text = Lang.T("btn.analyze");
            btnClean.Text = Lang.T("btn.clean");
            btnStopScan.Text = Lang.T("btn.stop");
            btnSelectAll.Text = Lang.T("btn.selectAll");
            btnSelectNone.Text = Lang.T("btn.selectNone");
            btnSelectRecommended.Text = Lang.T("btn.recommended");

            dashAnalyze.Text = Lang.T("btn.smartScan");
            dashAnalyze.SubText = Lang.T("btn.smartScanSub");
            dashStop.Text = Lang.T("btn.stop");
            dashStop.SubText = Lang.T("btn.stopSub");
            dashClean.Text = Lang.T("tile.clean");
            dashClean.SubText = Lang.T("tile.cleanSub");
            tileRegistry.Text = Lang.T("tile.registry");
            tileRegistry.SubText = Lang.T("tile.registrySub");
            tileBig.Text = Lang.T("tile.big");
            tileBig.SubText = Lang.T("tile.bigSub");
            tileDupes.Text = Lang.T("tile.dupes");
            tileDupes.SubText = Lang.T("tile.dupesSub");

            btnRegScan.Text = Lang.T("btn.regScan");
            btnRegFix.Text = Lang.T("btn.regFix");
            btnRegSelectAll.Text = Lang.T("btn.selectAll");
            btnRegBackupFolder.Text = Lang.T("btn.regBackups");

            btnStartupToggle.Text = Lang.T("btn.toggle");
            btnStartupOpen.Text = Lang.T("btn.openLocation");
            btnStartupDelete.Text = Lang.T("btn.delete");
            btnStartupRefresh.Text = Lang.T("btn.refresh");

            btnAppsUninstall.Text = Lang.T("btn.uninstall");
            btnAppsOpenFolder.Text = Lang.T("btn.openFolder");
            btnAppsRefresh.Text = Lang.T("btn.refresh");

            btnSpaceScanBig.Text = Lang.T("btn.bigFiles");
            btnSpaceScanDupes.Text = Lang.T("btn.duplicates");
            btnSpacePickFolder.Text = Lang.T("btn.pickFolder");
            btnSpaceDelete.Text = Lang.T("btn.deleteSelected");
            btnSpaceOpen.Text = Lang.T("btn.open");
            btnSpaceStop.Text = Lang.T("btn.stop");

            chkAutostart.Text = Lang.T("set.autostart");
            chkConfirm.Text = Lang.T("set.confirm");
            chkTrayClose.Text = Lang.T("set.closeToTray");
            chkAutoUpdate.Text = Lang.T("set.autoUpdate");
            btnAbout.Text = Lang.T("btn.about");
            btnCheckUpdate.Text = Lang.T("btn.checkUpdate");
            btnSchedOff.Text = Lang.T("sched.off");
            btnSchedDaily.Text = Lang.T("sched.daily");
            btnSchedWeekly.Text = Lang.T("sched.weekly");
            btnOpenLog.Text = Lang.T("btn.openLog");

            if (trayOpen != null)
            {
                trayOpen.Text = Lang.T("tray.open");
                trayAnalyze.Text = Lang.T("tray.analyze");
                trayExit.Text = Lang.T("tray.exit");
            }

            RetextColumns(cleanList, "col.item", "col.size", "col.files", "col.status");
            RetextColumns(regList, "col.item", "col.detail", "col.location");
            RetextColumns(startupList, "col.name", "col.status", "col.location", "col.path");
            RetextColumns(appsList, "col.name", "col.publisher", "col.version", "col.size", "col.installed");
            RetextColumns(spaceList, "col.name", "col.size", "col.path");
            RetextColumns(activityList, "col.event");

            SetStatus(Lang.T("status.ready"));
            RefreshCleanList();
            RefreshRegList();
            RefreshStartupTexts();
            FilterApps();
            UpdateSpaceSummary();
            UpdateDashboard();
            RefreshSettingsStatus();
            RefreshActivity();
        }

        // Fills one page strip. Every list page shows its numbers the same way, so
        // the captions and values travel together through here rather than each
        // page reimplementing the StatStrip dance.
        static void FillStrip(StatStrip strip, string[] captions, string[] values, Color[] colors)
        {
            if (strip == null) return;
            strip.Captions = captions;
            strip.Values = values;
            strip.ValueColors = colors;
            strip.Invalidate();
        }

        // Walks the control tree and re-texts anything whose Name is a Lang key.
        // Card headers are a Name too — CardPanel paints HeaderText, so it gets
        // the resolved string rather than the Text property.
        void RetextByName(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (!string.IsNullOrEmpty(c.Name) && c.Name.IndexOf('.') > 0)
                {
                    string text = Lang.T(c.Name);
                    var card = c as CardPanel;
                    if (card != null) { card.HeaderText = text; card.Invalidate(); }
                    else if (c.Name == "about.text") c.Text = string.Format(text, AppVersion);
                    else if (c.Name == "sched.last")
                        c.Text = string.Format(text, lastScheduledClean == DateTime.MinValue
                            ? Lang.T("common.never") : lastScheduledClean.ToString("dd.MM.yyyy HH:mm"));
                    else c.Text = text;
                }
                if (c.Controls.Count > 0) RetextByName(c);
            }
        }

        static void RetextColumns(ListView list, params string[] keys)
        {
            if (list == null) return;
            for (int i = 0; i < keys.Length && i < list.Columns.Count; i++)
                list.Columns[i].Text = Lang.T(keys[i]);
            list.Invalidate();
        }

        // ---------- progress plumbing shared by every page ----------

        // Every Stop button in the app is shown from here, so whichever page the
        // user is looking at when a scan starts has one within reach.
        void BeginBusy(string status)
        {
            SetStatus(status);
            progress.Start();
            btnAnalyze.Enabled = false;
            btnClean.Enabled = false;
            dashClean.Enabled = false;
            btnRegScan.Enabled = false;
            btnSpaceScanBig.Enabled = false;
            btnSpaceScanDupes.Enabled = false;
            btnStopScan.Visible = true;
            // The dashboard and space Stop buttons share their cell with the
            // action they replace, and a control added later sits *under* the one
            // added before it — leaving the tile visible would have hidden Stop
            // behind it. Hiding what it covers is what makes the swap real.
            SwapStop(dashStop, true, dashAnalyze);
            SwapStop(btnSpaceStop, true, btnSpaceScanBig, btnSpaceScanDupes);
        }

        void EndBusy(string status)
        {
            SetStatus(status);
            progress.Stop();
            btnAnalyze.Enabled = true;
            btnRegScan.Enabled = true;
            btnSpaceScanBig.Enabled = true;
            btnSpaceScanDupes.Enabled = true;
            btnStopScan.Visible = false;
            SwapStop(dashStop, false, dashAnalyze);
            SwapStop(btnSpaceStop, false, btnSpaceScanBig, btnSpaceScanDupes);
            btnClean.Enabled = scan.Finished && scan.SelectedBytes > 0;
            dashClean.Enabled = btnClean.Enabled;
        }

        static void SwapStop(Control stop, bool busy, params Control[] covered)
        {
            if (stop == null) return;
            foreach (Control c in covered)
                if (c != null) c.Visible = !busy;
            stop.Visible = busy;
            if (busy) stop.BringToFront();
        }

        void CancelWork()
        {
            scan.Cancel.Cancel();
            if (spaceCancel != null) spaceCancel.Cancel();
            if (regCancel != null) regCancel.Cancel();
            SetStatus(Lang.T("common.cancelled"));
        }
    }
}
