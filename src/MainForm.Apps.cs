// The installed-programs list: the same inventory Settings → Apps shows, read
// straight from the three Uninstall keys, plus a shortcut to each program's own
// uninstaller. Cleaner never deletes another program's files itself.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Cleaner
{
    sealed class InstalledApp
    {
        public string Name = "";
        public string Publisher = "";
        public string Version = "";
        public string InstallLocation;
        public string UninstallString;
        public long SizeKb;
        public DateTime Installed = DateTime.MinValue;

        public string Key { get { return (Name + "|" + Version).ToLowerInvariant(); } }
    }

    public partial class MainForm : Form
    {
        readonly List<InstalledApp> installedApps = new List<InstalledApp>();

        // Column indexes of the apps list, which are also the sort keys.
        const int AppsColName = 0, AppsColPublisher = 1, AppsColVersion = 2,
                  AppsColSize = 3, AppsColInstalled = 4;
        int appsSort = AppsColName;
        bool appsSortDesc;

        void RefreshApps()
        {
            installedApps.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ReadUninstallKey(Registry.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", seen);
            ReadUninstallKey(Registry.LocalMachine,
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", seen);
            ReadUninstallKey(Registry.CurrentUser,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", seen);

            FilterApps();   // which sorts by whatever column the user picked
        }

        void ReadUninstallKey(RegistryKey hive, string path, HashSet<string> seen)
        {
            RegistryKey root = null;
            try { root = hive.OpenSubKey(path, false); }
            catch { }
            if (root == null) return;
            using (root)
            {
                string[] names;
                try { names = root.GetSubKeyNames(); }
                catch { return; }
                foreach (string name in names)
                {
                    try
                    {
                        using (RegistryKey k = root.OpenSubKey(name, false))
                        {
                            if (k == null) continue;
                            string display = k.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(display)) continue;
                            // Updates, hotfixes and component entries are not
                            // programs — they are what makes the raw key unusable.
                            if (ToInt(k.GetValue("SystemComponent")) == 1) continue;
                            if (k.GetValue("ParentKeyName") != null) continue;
                            if (k.GetValue("ReleaseType") as string == "Security Update") continue;

                            var app = new InstalledApp();
                            app.Name = display.Trim();
                            app.Publisher = (k.GetValue("Publisher") as string ?? "").Trim();
                            app.Version = (k.GetValue("DisplayVersion") as string ?? "").Trim();
                            app.InstallLocation = k.GetValue("InstallLocation") as string;
                            // The interactive command wins over the quiet one:
                            // this runs from a button the user pressed, and a
                            // silent uninstall gives them no progress, no vendor
                            // prompt and no way to cancel — it just looks as if
                            // nothing happened. Quiet is only the fallback.
                            app.UninstallString = (k.GetValue("UninstallString") as string)
                                ?? (k.GetValue("QuietUninstallString") as string);
                            app.SizeKb = ToInt(k.GetValue("EstimatedSize"));
                            app.Installed = ParseInstallDate(k.GetValue("InstallDate") as string);
                            if (!seen.Add(app.Key)) continue; // the same app in two hives
                            installedApps.Add(app);
                        }
                    }
                    catch { }
                }
            }
        }

        static long ToInt(object value)
        {
            try { return value == null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        // InstallDate is "YYYYMMDD" — and quite often something else entirely,
        // which is why a failure returns MinValue rather than throwing.
        internal static DateTime ParseInstallDate(string raw)
        {
            if (string.IsNullOrEmpty(raw) || raw.Length != 8) return DateTime.MinValue;
            DateTime parsed;
            if (DateTime.TryParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out parsed))
                return parsed;
            return DateTime.MinValue;
        }

        // Clicking a column header. The first click on a column picks the
        // direction that column is actually useful in — biggest and most recently
        // installed first, everything else A to Z — and each further click flips
        // it. The filter box is left alone: re-sorting is not a new search.
        void SortAppsBy(int column)
        {
            if (column < AppsColName || column > AppsColInstalled) return;
            if (column == appsSort) appsSortDesc = !appsSortDesc;
            else
            {
                appsSort = column;
                appsSortDesc = column == AppsColSize || column == AppsColInstalled;
            }
            FilterApps();
        }

        void SortInstalledApps()
        {
            int column = appsSort;
            bool descending = appsSortDesc;
            installedApps.Sort(delegate(InstalledApp a, InstalledApp b)
            {
                int cmp;
                if (column == AppsColSize) cmp = a.SizeKb.CompareTo(b.SizeKb);
                else if (column == AppsColInstalled) cmp = a.Installed.CompareTo(b.Installed);
                else if (column == AppsColPublisher)
                    cmp = string.Compare(a.Publisher, b.Publisher, StringComparison.CurrentCultureIgnoreCase);
                else if (column == AppsColVersion)
                    cmp = CompareVersions(a.Version, b.Version);
                else cmp = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                if (descending) cmp = -cmp;
                // Name is both the default key and every other key's tie-break:
                // half the entries share a publisher, plenty report no size and
                // no install date at all, and List.Sort is unstable — without
                // this those rows would shuffle on every click. It is deliberately
                // the primary above rather than only here, or a descending sort by
                // name would flip nothing and do exactly what ascending did.
                if (cmp == 0) cmp = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                return cmp;
            });
        }

        // DisplayVersion is free-form text, so "1.10" has to sort above "1.9"
        // without a malformed value throwing: each dotted run of digits is
        // compared numerically and anything else falls back to plain text.
        internal static int CompareVersions(string a, string b)
        {
            string[] left = (a ?? "").Split('.'), right = (b ?? "").Split('.');
            for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
            {
                string l = i < left.Length ? left[i] : "";
                string r = i < right.Length ? right[i] : "";
                long ln, rn;
                int cmp = long.TryParse(l, out ln) && long.TryParse(r, out rn)
                    ? ln.CompareTo(rn)
                    : string.Compare(l, r, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
            }
            return 0;
        }

        void FilterApps()
        {
            if (appsList == null || appsList.IsDisposed) return;
            SortInstalledApps();
            string filter = appsSearch == null ? "" : appsSearch.Text.Trim();
            appsList.BeginUpdate();
            appsList.Items.Clear();
            int shown = 0;
            foreach (InstalledApp app in installedApps)
            {
                if (filter.Length > 0
                    && app.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0
                    && app.Publisher.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0)
                    continue;
                var item = new ListViewItem(app.Name);
                item.SubItems.Add(app.Publisher);
                item.SubItems.Add(app.Version);
                item.SubItems.Add(app.SizeKb > 0 ? Util.FormatSize(app.SizeKb * 1024) : "");
                item.SubItems.Add(app.Installed == DateTime.MinValue ? "" : app.Installed.ToString("dd.MM.yyyy"));
                item.Tag = app;
                if (string.IsNullOrEmpty(app.UninstallString)) item.ForeColor = Theme.Disabled;
                appsList.Items.Add(item);
                shown++;
            }
            appsList.EndUpdate();
            appsList.Refit();
            appsList.SetSort(appsSort, appsSortDesc);

            long totalKb = 0;
            foreach (InstalledApp app in installedApps) totalKb += app.SizeKb;
            FillStrip(appsStrip,
                new string[] { Lang.T("stat.programs"), Lang.T("stat.shown"), Lang.T("stat.totalSize") },
                new string[]
                {
                    installedApps.Count.ToString(), shown.ToString(),
                    // EstimatedSize is missing on plenty of entries, so this is a
                    // floor rather than a true total — never presented as exact.
                    totalKb > 0 ? Util.FormatSize(totalKb * 1024) : "—"
                },
                null);

            if (appsSummary != null)
                appsSummary.Text = string.Format(Lang.T("apps.summary"), installedApps.Count, shown);
        }

        InstalledApp SelectedApp()
        {
            if (appsList == null || appsList.SelectedItems.Count == 0) return null;
            return appsList.SelectedItems[0].Tag as InstalledApp;
        }

        void UninstallSelectedApp()
        {
            InstalledApp app = SelectedApp();
            if (app == null) return;
            if (string.IsNullOrWhiteSpace(app.UninstallString))
            {
                MessageBox.Show(this, Lang.T("apps.noUninstaller"), AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, string.Format(Lang.T("apps.confirm"), app.Name), AppName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            string exe, arguments;
            SplitCommand(app.UninstallString, out exe, out arguments);
            try
            {
                var psi = new ProcessStartInfo(exe, arguments);
                psi.UseShellExecute = true; // the uninstaller asks for elevation itself
                Process.Start(psi);
                LogLine(string.Format(Lang.T("log.uninstallStarted"), app.Name));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Splits an UninstallString into the executable and its arguments. The
        // quoted form is unambiguous; the bare form is resolved the same way
        // ExeFromCommandLine does, so "C:\Program Files\x\u.exe /S" survives.
        internal static void SplitCommand(string command, out string exe, out string arguments)
        {
            exe = command ?? "";
            arguments = "";
            if (string.IsNullOrWhiteSpace(command)) return;
            string s = command.Trim();
            if (s[0] == '"')
            {
                int end = s.IndexOf('"', 1);
                if (end > 1)
                {
                    exe = s.Substring(1, end - 1);
                    arguments = s.Substring(end + 1).Trim();
                    return;
                }
            }
            string candidate = Util.ExeFromCommandLine(s);
            if (!string.IsNullOrEmpty(candidate) && s.Length > candidate.Length)
            {
                exe = candidate;
                arguments = s.Substring(candidate.Length).Trim();
            }
            else exe = candidate ?? s;
        }

        void OpenSelectedAppFolder()
        {
            InstalledApp app = SelectedApp();
            if (app == null) return;
            string folder = app.InstallLocation;
            if (string.IsNullOrWhiteSpace(folder) && !string.IsNullOrWhiteSpace(app.UninstallString))
            {
                string exe, arguments;
                SplitCommand(app.UninstallString, out exe, out arguments);
                try { folder = Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(exe)); }
                catch { folder = null; }
            }
            if (!string.IsNullOrWhiteSpace(folder))
                OpenInExplorer(Environment.ExpandEnvironmentVariables(folder.Trim().Trim('"')));
        }
    }
}
