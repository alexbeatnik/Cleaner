// MainForm: app-lifetime state, the entry point, single-instance plumbing, the
// tray icon, activity logging and autostart.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Cleaner
{
    public partial class MainForm : Form
    {
        const string AppName = "Cleaner";
        static readonly string AppVersion =
            Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValueName = "Cleaner";

        // ---------- paths and persisted state ----------

        string settingsPath;   // settings.ini next to the exe
        string logPath;        // clean.log next to the exe
        string backupDir;      // registry .reg backups, next to the exe

        long totalFreedBytes;      // cumulative, across every clean
        long totalFreedFiles;
        int totalCleans;
        DateTime lastCleanTime;    // DateTime.MinValue = never

        // Which rule ids the user turned on or off; anything absent falls back to
        // the rule's own DefaultOn, so a catalog addition arrives switched on for
        // existing installs without a settings migration.
        readonly Dictionary<string, bool> ruleChoices =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        int schedMode;             // auto clean: 0 = off, 1 = daily, 2 = weekly
        DateTime lastScheduledClean;
        Timer schedTimer;
        Timer driveTimer;          // keeps the dashboard storage card off stale numbers
        bool confirmBeforeClean = true;
        bool autostartInitialized;
        bool modeAsked;            // the first-run portable-vs-installed question
        bool reallyClose;          // true = exit, false = minimize to tray

        // ---------- live state ----------

        List<CleanRule> catalog = new List<CleanRule>();
        CleanScan scan = new CleanScan();   // current or last finished analysis
        bool cleanRunning;
        readonly List<string> recentActivity = new List<string>();

        // ---------- UI ----------

        Panel pageHost;
        Panel headerPanel;
        Panel[] pages;
        NavTab[] navTabs;
        Label headerTitle;
        SlimMarquee progress;
        Label statusLabel;

        // dashboard
        Gauge gauge;
        StatusBanner heroBanner;
        ModernButton dashAnalyze, dashClean, dashStop, tileRegistry, tileBig, tileDupes;
        Panel drivesHost;
        StatStrip dashStats;
        DarkList activityList;
        Label dashHeadline, dashSubline;

        // cleaner
        DarkList cleanList;
        StatStrip cleanStrip;
        ModernButton btnAnalyze, btnClean, btnStopScan, btnSelectAll, btnSelectNone, btnSelectRecommended;
        Label cleanSummary;

        // registry
        DarkList regList;
        StatStrip regStrip;
        ModernButton btnRegScan, btnRegFix, btnRegSelectAll, btnRegBackupFolder;
        Label regSummary;

        // startup
        DarkList startupList;
        StatStrip startupStrip;
        ModernButton btnStartupRefresh, btnStartupToggle, btnStartupOpen, btnStartupDelete;
        Label startupSummary;

        // apps
        DarkList appsList;
        StatStrip appsStrip;
        ModernButton btnAppsRefresh, btnAppsUninstall, btnAppsOpenFolder;
        TextBox appsSearch;
        Label appsSummary;

        // space (large files + duplicates)
        DarkList spaceList;
        StatStrip spaceStrip;
        ModernButton btnSpaceScanBig, btnSpaceScanDupes, btnSpacePickFolder, btnSpaceDelete, btnSpaceOpen;
        ModernButton btnSpaceStop;
        Label spaceSummary, spaceFolderLabel;

        // settings
        ModernButton btnLangEn, btnLangUk, btnInstall, btnOpenLog;
        ModernButton btnSchedOff, btnSchedDaily, btnSchedWeekly;
        ModernButton btnAbout, btnCheckUpdate;
        Toggle chkAutostart, chkConfirm, chkTrayClose, chkAutoUpdate;
        Label installedBadge, updateStatus;
        Label[] setStatusCaps, setStatusVals;

        NotifyIcon tray;
        ContextMenuStrip trayMenu;
        ToolStripMenuItem trayOpen, trayAnalyze, trayExit;
        bool closeToTray = true;

        // One instance per user session + a "show window" message to it
        static System.Threading.Mutex singleInstanceMutex;
        static readonly int WmShow = NativeMethods.RegisterWindowMessage("Cleaner_Show_v1");

        // ---------- entry point ----------

        [STAThread]
        static void Main(string[] args)
        {
            // --write-icon runs before any UI: build.ps1's first pass uses the
            // freshly compiled exe to render app.ico for its second pass.
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--write-icon")
                    Environment.Exit(Brand.WriteIconFile(i + 1 < args.Length ? args[i + 1] : "app.ico"));

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool startInTray = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--tray") startInTray = true;
                if (args[i] == "--install") { RunInstallMode(); return; }
                if (args[i] == "--uninstall") { RunUninstallMode(); return; }
                // The elevated helper: performs a job file written by the normal
                // instance and exits with the number of failures.
                if (args[i] == "--run-job")
                {
                    Environment.Exit(ElevatedJob.Run(i + 1 < args.Length ? args[i + 1] : null));
                    return;
                }
            }

            bool createdNew;
            singleInstanceMutex = new System.Threading.Mutex(true, "Local\\Cleaner_SingleInstance_v1", out createdNew);
            if (!createdNew)
            {
                // The broadcast reaches the instance while its window is visible or
                // plainly minimized, but a tray-hidden window is OWNED (that is how
                // ShowInTaskbar=false hides it) and HWND_BROADCAST skips owned
                // windows — so the running instance must also get the message
                // posted straight to its windows (unknown windows just ignore it).
                NativeMethods.PostMessage((IntPtr)NativeMethods.HWND_BROADCAST, WmShow, IntPtr.Zero, IntPtr.Zero);
                PostShowToRunningInstance();
                return;
            }

            try { Application.Run(new MainForm(startInTray)); }
            finally { GC.KeepAlive(singleInstanceMutex); }
        }

        // Posts WmShow directly to every top-level window of the already-running
        // instance (same process name, other PID). EnumWindows, unlike
        // HWND_BROADCAST, also lists owned windows — including the tray-hidden
        // main form. Extra hits (a message box, the IME window) ignore the
        // registered message, so precision beyond the PID does not matter.
        static void PostShowToRunningInstance()
        {
            var pids = new HashSet<uint>();
            try
            {
                using (var self = Process.GetCurrentProcess())
                    foreach (Process p in Process.GetProcessesByName(self.ProcessName))
                    {
                        try { if (p.Id != self.Id) pids.Add((uint)p.Id); }
                        catch { }
                        finally { try { p.Dispose(); } catch { } }
                    }
            }
            catch { }
            if (pids.Count == 0) return;
            try
            {
                NativeMethods.EnumWindows(delegate(IntPtr h, IntPtr lp)
                {
                    uint pid;
                    NativeMethods.GetWindowThreadProcessId(h, out pid);
                    if (pids.Contains(pid))
                        NativeMethods.PostMessage(h, WmShow, IntPtr.Zero, IntPtr.Zero);
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmShow && WmShow != 0) RestoreFromTray();
            base.WndProc(ref m);
        }

        // ---------- window lifetime ----------

        void RestoreFromTray()
        {
            try
            {
                ShowInTaskbar = true;
                Show();
                // ShowInTaskbar recreates the handle, after which WindowState can
                // report Normal while the window is still iconic at -32000 — ask
                // Win32 instead of trusting the managed property.
                if (NativeMethods.IsIconic(Handle)) NativeMethods.ShowWindow(Handle, NativeMethods.SW_RESTORE);
                WindowState = FormWindowState.Normal;
                Activate();
                BringToFront();
                // The drive timer skips ticks while the window is hidden, so a
                // window coming back from the tray would show whatever the card
                // said when it went away until the next tick.
                RefreshDrives();
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyClose && closeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                return;
            }
            scan.Cancel.Cancel();
            SaveSettings();
            try { if (tray != null) tray.Visible = false; }
            catch { }
            base.OnFormClosing(e);
        }

        void ExitApp()
        {
            reallyClose = true;
            Close();
            Application.Exit();
        }

        // ---------- tray ----------

        void BuildTray()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors());
            trayMenu.BackColor = Theme.LogBg;
            trayMenu.ForeColor = Theme.Text;

            trayOpen = new ToolStripMenuItem(Lang.T("tray.open"));
            trayOpen.Click += delegate { RestoreFromTray(); };
            trayAnalyze = new ToolStripMenuItem(Lang.T("tray.analyze"));
            trayAnalyze.Click += delegate { RestoreFromTray(); ShowPage(1); StartAnalyze(false); };
            trayExit = new ToolStripMenuItem(Lang.T("tray.exit"));
            trayExit.Click += delegate { ExitApp(); };
            trayMenu.Items.Add(trayOpen);
            trayMenu.Items.Add(trayAnalyze);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(trayExit);

            tray = new NotifyIcon();
            tray.Icon = Brand.AppIcon;
            tray.Text = AppName;
            tray.Visible = true;
            tray.ContextMenuStrip = trayMenu;
            tray.DoubleClick += delegate { RestoreFromTray(); };
        }

        void Notify(string title, string text)
        {
            try
            {
                if (tray == null) return;
                tray.BalloonTipTitle = title;
                tray.BalloonTipText = text;
                tray.ShowBalloonTip(5000);
            }
            catch { }
        }

        // ---------- activity log ----------

        // One line per meaningful action, kept both on disk (clean.log, for the
        // user) and in memory (the dashboard's activity card). Never throws: a
        // read-only folder must not break cleaning.
        internal void LogLine(string text)
        {
            string stamped = DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "  " + text;
            recentActivity.Insert(0, stamped);
            while (recentActivity.Count > 60) recentActivity.RemoveAt(recentActivity.Count - 1);
            try
            {
                if (logPath != null)
                    File.AppendAllText(logPath, stamped + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
            RefreshActivity();
        }

        void LoadRecentActivity()
        {
            recentActivity.Clear();
            try
            {
                if (logPath == null || !File.Exists(logPath)) return;
                string[] lines = File.ReadAllLines(logPath, Encoding.UTF8);
                for (int i = lines.Length - 1; i >= 0 && recentActivity.Count < 60; i--)
                    if (lines[i].Trim().Length > 0) recentActivity.Add(lines[i]);
            }
            catch { }
        }

        void RefreshActivity()
        {
            if (activityList == null || activityList.IsDisposed) return;
            activityList.BeginUpdate();
            activityList.Items.Clear();
            foreach (string line in recentActivity)
            {
                // "2026-08-23 10:14  Freed 1.2 GB" — the date is redundant on a
                // list that only ever shows the last few entries, so the row keeps
                // the time and the message in one column and stays readable in the
                // narrow half of the dashboard's bottom row.
                string text = line;
                if (line.Length > 17)
                {
                    string stamp = line.Substring(0, 16).Trim();
                    int space = stamp.IndexOf(' ');
                    if (space > 0) stamp = stamp.Substring(space + 1);
                    text = stamp + " · " + line.Substring(17).Trim();
                }
                var item = new ListViewItem(text);
                item.Tag = line;
                item.ForeColor = Theme.Muted;
                activityList.Items.Add(item);
                if (activityList.Items.Count >= 40) break;
            }
            activityList.EndUpdate();
            activityList.Refit();
        }

        // ---------- shared UI helpers ----------

        void SetStatus(string text)
        {
            if (statusLabel != null && !statusLabel.IsDisposed) statusLabel.Text = text;
        }

        // Marshals a callback onto the UI thread, swallowing the race where the
        // form closed while a worker was still running.
        void OnUi(MethodInvoker action)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(action);
            }
            catch { }
        }

        internal static void OpenInExplorer(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
                else if (File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch { }
        }

        // True when any process with one of these names is running. Cleaning a
        // browser's cache while it is open mostly fails and can corrupt its
        // profile database, so the analyzer marks those rules blocked instead.
        internal static string RunningProcess(string[] names)
        {
            if (names == null) return null;
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                try
                {
                    Process[] found = Process.GetProcessesByName(name);
                    try { if (found.Length > 0) return name; }
                    finally { foreach (Process p in found) try { p.Dispose(); } catch { } }
                }
                catch { }
            }
            return null;
        }

        // ---------- autostart ----------

        internal static string AutostartCommand(string exePath)
        {
            return "\"" + exePath + "\" --tray";
        }

        bool AutostartEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                        return k != null && k.GetValue(RunValueName) != null;
                }
                catch { return false; }
            }
        }

        void SetAutostart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (k == null) return;
                    if (on) k.SetValue(RunValueName, AutostartCommand(Application.ExecutablePath));
                    else if (k.GetValue(RunValueName) != null) k.DeleteValue(RunValueName, false);
                }
            }
            catch { }
        }

        // Autostart is only switched on by itself once, on an installed copy: a
        // portable exe the user dropped in Downloads has no business writing a Run
        // value, and re-enabling it after the user turned it off would be rude.
        void EnsureAutostartFirstRun()
        {
            if (autostartInitialized) return;
            autostartInitialized = true;
            if (IsInstalled) SetAutostart(true);
            SaveSettings();
        }
    }
}
