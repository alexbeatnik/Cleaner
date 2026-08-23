// The registry cleaner: read-only scanners that look for entries pointing at
// files and classes which no longer exist, and a fix step that always writes a
// .reg backup before it removes anything.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WindowsStalker
{
    // One problem found in the registry. Either a single value goes, or the whole
    // key does — never anything in between, so the backup and the fix always
    // describe exactly the same thing.
    sealed class RegIssue
    {
        public string HiveName;      // HKCU / HKLM / HKCR
        public string Key;           // subkey path under the hive
        public string ValueName;     // null when the whole key goes
        public bool DeleteWholeKey;
        public string KindKey;       // Lang key naming the kind of problem
        public string Detail;        // already-resolved text, not a Lang key
        public bool NeedsAdmin;
        public bool Selected = true;

        public string Location
        {
            get { return HiveName + "\\" + Key; }
        }
    }

    public partial class MainForm : Form
    {
        CancelFlag regCancel;
        readonly List<RegIssue> regIssues = new List<RegIssue>();
        bool regScanRunning;

        // ---------- scanning ----------

        void StartRegistryScan()
        {
            if (regScanRunning) return;
            regScanRunning = true;
            regCancel = new CancelFlag();
            CancelFlag cancel = regCancel;
            regIssues.Clear();
            RefreshRegList();
            BeginBusy(Lang.T("reg.scanning"));

            ThreadPool.QueueUserWorkItem(delegate
            {
                var found = new List<RegIssue>();
                try
                {
                    ScanAppPaths(found, cancel);
                    ScanSharedDlls(found, cancel);
                    ScanStartupValues(found, cancel);
                    ScanFileExtensions(found, cancel);
                    ScanMuiCache(found, cancel);
                    ScanUninstallEntries(found, cancel);
                }
                catch { }

                OnUi(delegate
                {
                    if (regCancel != cancel) return; // a newer scan took over
                    regScanRunning = false;
                    regIssues.Clear();
                    if (!cancel.Cancelled) regIssues.AddRange(found);
                    RefreshRegList();
                    EndBusy(cancel.Cancelled ? Lang.T("common.cancelled")
                        : string.Format(Lang.T("reg.found"), regIssues.Count));
                });
            });
        }

        // HKLM/HKCU App Paths: each subkey's default value is the executable the
        // shell runs for that name. A missing target makes the whole key dead.
        void ScanAppPaths(List<RegIssue> found, CancelFlag cancel)
        {
            const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
            string[] hives = { "HKCU", "HKLM" };
            foreach (string hiveName in hives)
            {
                RegistryKey hive = ElevatedJob.HiveFor(hiveName);
                if (hive == null) continue;
                RegistryKey root = null;
                try { root = hive.OpenSubKey(path, false); }
                catch { }
                if (root == null) continue;
                using (root)
                {
                    string[] names;
                    try { names = root.GetSubKeyNames(); }
                    catch { continue; }
                    foreach (string name in names)
                    {
                        if (cancel.Cancelled) return;
                        try
                        {
                            using (RegistryKey k = root.OpenSubKey(name, false))
                            {
                                if (k == null) continue;
                                string target = k.GetValue(null) as string;
                                if (string.IsNullOrWhiteSpace(target)) continue;
                                if (!Util.TargetMissing(target)) continue;
                                found.Add(Issue(hiveName, path + "\\" + name, null, true,
                                    "reg.kind.appPaths", string.Format(Lang.T("reg.missingFile"), target)));
                            }
                        }
                        catch { }
                    }
                }
            }
        }

        // SharedDLLs is a reference count keyed by file path — entries for files
        // that are gone are pure residue from uninstalled software.
        void ScanSharedDlls(List<RegIssue> found, CancelFlag cancel)
        {
            const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs";
            RegistryKey key = null;
            try { key = Registry.LocalMachine.OpenSubKey(path, false); }
            catch { }
            if (key == null) return;
            using (key)
            {
                string[] names;
                try { names = key.GetValueNames(); }
                catch { return; }
                foreach (string name in names)
                {
                    if (cancel.Cancelled) return;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    try
                    {
                        if (File.Exists(Environment.ExpandEnvironmentVariables(name))) continue;
                    }
                    catch { continue; }
                    RegIssue issue = Issue("HKLM", path, name, false, "reg.kind.sharedDll",
                        string.Format(Lang.T("reg.missingFile"), name));
                    issue.NeedsAdmin = true;
                    found.Add(issue);
                }
            }
        }

        void ScanStartupValues(List<RegIssue> found, CancelFlag cancel)
        {
            string[][] places =
            {
                new string[] { "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run" },
                new string[] { "HKCU", @"Software\Microsoft\Windows\CurrentVersion\RunOnce" },
                new string[] { "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run" },
                new string[] { "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce" },
            };
            foreach (string[] place in places)
            {
                if (cancel.Cancelled) return;
                RegistryKey hive = ElevatedJob.HiveFor(place[0]);
                if (hive == null) continue;
                RegistryKey key = null;
                try { key = hive.OpenSubKey(place[1], false); }
                catch { }
                if (key == null) continue;
                using (key)
                {
                    string[] names;
                    try { names = key.GetValueNames(); }
                    catch { continue; }
                    foreach (string name in names)
                    {
                        if (cancel.Cancelled) return;
                        try
                        {
                            string command = key.GetValue(name) as string;
                            if (string.IsNullOrWhiteSpace(command)) continue;
                            if (!Util.TargetMissing(command)) continue;
                            RegIssue issue = Issue(place[0], place[1], name, false, "reg.kind.startup",
                                string.Format(Lang.T("reg.missingFile"), Util.ExeFromCommandLine(command)));
                            issue.NeedsAdmin = place[0] == "HKLM";
                            found.Add(issue);
                        }
                        catch { }
                    }
                }
            }
        }

        // Only this user's own class registrations are scanned: HKCR is a merged
        // view whose machine half needs administrator rights, and an extension
        // association is not worth a UAC prompt.
        void ScanFileExtensions(List<RegIssue> found, CancelFlag cancel)
        {
            const string classes = @"Software\Classes";
            RegistryKey root = null;
            try { root = Registry.CurrentUser.OpenSubKey(classes, false); }
            catch { }
            if (root == null) return;
            using (root)
            {
                string[] names;
                try { names = root.GetSubKeyNames(); }
                catch { return; }
                foreach (string name in names)
                {
                    if (cancel.Cancelled) return;
                    if (name.Length < 2 || name[0] != '.') continue;
                    try
                    {
                        using (RegistryKey k = root.OpenSubKey(name, false))
                        {
                            if (k == null) continue;
                            string progId = k.GetValue(null) as string;
                            if (string.IsNullOrWhiteSpace(progId)) continue;
                            if (ClassExists(progId)) continue;
                            found.Add(Issue("HKCU", classes + "\\" + name, null, true, "reg.kind.fileExt",
                                string.Format(Lang.T("reg.missingClass"), progId)));
                        }
                    }
                    catch { }
                }
            }
        }

        static bool ClassExists(string progId)
        {
            foreach (string prefix in new string[] { @"Software\Classes\", "" })
            {
                try
                {
                    RegistryKey hive = prefix.Length > 0 ? Registry.CurrentUser : Registry.ClassesRoot;
                    using (RegistryKey k = hive.OpenSubKey(prefix + progId, false))
                        if (k != null) return true;
                }
                catch { }
            }
            return false;
        }

        // MuiCache remembers the friendly name of every executable ever launched;
        // entries whose exe is gone are the classic registry-cleaner find.
        void ScanMuiCache(List<RegIssue> found, CancelFlag cancel)
        {
            const string path = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
            RegistryKey key = null;
            try { key = Registry.CurrentUser.OpenSubKey(path, false); }
            catch { }
            if (key == null) return;
            using (key)
            {
                string[] names;
                try { names = key.GetValueNames(); }
                catch { return; }
                foreach (string name in names)
                {
                    if (cancel.Cancelled) return;
                    // Names look like "C:\app\x.exe.FriendlyAppName"
                    int dot = name.LastIndexOf('.');
                    if (dot <= 0) continue;
                    string file = name.Substring(0, dot);
                    if (file.IndexOf(":\\", StringComparison.Ordinal) < 0) continue;
                    try { if (File.Exists(file)) continue; }
                    catch { continue; }
                    found.Add(Issue("HKCU", path, name, false, "reg.kind.muiCache",
                        string.Format(Lang.T("reg.missingFile"), file)));
                }
            }
        }

        // An Uninstall entry is only reported when BOTH its install folder and its
        // uninstall command are gone — one missing half is normal for MSI packages
        // and for apps that register a rundll32 command.
        void ScanUninstallEntries(List<RegIssue> found, CancelFlag cancel)
        {
            string[][] places =
            {
                new string[] { "HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" },
                new string[] { "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" },
                new string[] { "HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" },
            };
            foreach (string[] place in places)
            {
                RegistryKey hive = ElevatedJob.HiveFor(place[0]);
                if (hive == null) continue;
                RegistryKey root = null;
                try { root = hive.OpenSubKey(place[1], false); }
                catch { }
                if (root == null) continue;
                using (root)
                {
                    string[] names;
                    try { names = root.GetSubKeyNames(); }
                    catch { continue; }
                    foreach (string name in names)
                    {
                        if (cancel.Cancelled) return;
                        try
                        {
                            using (RegistryKey k = root.OpenSubKey(name, false))
                            {
                                if (k == null) continue;
                                string display = k.GetValue("DisplayName") as string;
                                if (string.IsNullOrWhiteSpace(display)) continue;
                                string location = k.GetValue("InstallLocation") as string;
                                string uninstall = k.GetValue("UninstallString") as string;
                                bool locationGone = string.IsNullOrWhiteSpace(location) || !DirectoryExists(location);
                                bool uninstallGone = string.IsNullOrWhiteSpace(uninstall) || Util.TargetMissing(uninstall);
                                // An MSI uninstall string runs msiexec, which always
                                // exists — those are never reported.
                                if (!locationGone || !uninstallGone) continue;
                                RegIssue issue = Issue(place[0], place[1] + "\\" + name, null, true,
                                    "reg.kind.uninstall", display);
                                issue.NeedsAdmin = place[0] == "HKLM";
                                found.Add(issue);
                            }
                        }
                        catch { }
                    }
                }
            }
        }

        static bool DirectoryExists(string path)
        {
            try { return Directory.Exists(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'))); }
            catch { return false; }
        }

        static RegIssue Issue(string hive, string key, string valueName, bool wholeKey,
                              string kindKey, string detail)
        {
            var issue = new RegIssue();
            issue.HiveName = hive;
            issue.Key = key;
            issue.ValueName = valueName;
            issue.DeleteWholeKey = wholeKey;
            issue.KindKey = kindKey;
            issue.Detail = detail;
            return issue;
        }

        // ---------- list ----------

        void RefreshRegList()
        {
            if (regList == null || regList.IsDisposed) return;
            regList.BeginUpdate();
            regList.Items.Clear();
            string currentKind = null;
            foreach (RegIssue issue in regIssues)
            {
                if (issue.KindKey != currentKind)
                {
                    currentKind = issue.KindKey;
                    var header = new ListViewItem(Lang.T(currentKind));
                    header.SubItems.Add("");
                    header.SubItems.Add("");
                    header.Tag = null;
                    regList.Items.Add(header);
                }
                var item = new ListViewItem(issue.ValueName ?? Path.GetFileName(issue.Key.TrimEnd('\\')));
                item.SubItems.Add(issue.Detail ?? "");
                item.SubItems.Add(issue.Location);
                item.Tag = issue;
                if (issue.NeedsAdmin) item.ForeColor = Theme.Warn;
                regList.Items.Add(item);
            }
            regList.EndUpdate();
            regList.Refit();
            UpdateRegSummary();
        }

        void UpdateRegSummary()
        {
            if (regSummary == null) return;
            int selected = 0, needAdmin = 0;
            foreach (RegIssue issue in regIssues)
            {
                if (issue.Selected) selected++;
                if (issue.NeedsAdmin) needAdmin++;
            }

            FillStrip(regStrip,
                new string[]
                {
                    Lang.T("stat.issues"), Lang.T("stat.selected"),
                    Lang.T("clean.needsAdmin"), Lang.T("stat.backups")
                },
                new string[]
                {
                    regIssues.Count.ToString(), selected.ToString(),
                    needAdmin.ToString(), BackupCount().ToString()
                },
                new Color[]
                {
                    regIssues.Count > 0 ? Theme.Warn : Theme.Good, Color.Empty,
                    needAdmin > 0 ? Theme.Warn : Color.Empty, Color.Empty
                });

            regSummary.Text = regIssues.Count == 0
                ? (regScanRunning ? "" : Lang.T("reg.none"))
                : string.Format(Lang.T("reg.found"), regIssues.Count) + " · " +
                  selected + " " + Lang.T("common.selected");
            if (btnRegFix != null) btnRegFix.Enabled = selected > 0 && !regScanRunning;
        }

        int BackupCount()
        {
            try
            {
                return Directory.Exists(backupDir) ? Directory.GetFiles(backupDir, "*.reg").Length : 0;
            }
            catch { return 0; }
        }

        void SelectRegIssues(bool on)
        {
            foreach (RegIssue issue in regIssues) issue.Selected = on;
            RefreshRegList();
        }

        // ---------- fixing ----------

        void FixRegistryIssues()
        {
            var selected = new List<RegIssue>();
            foreach (RegIssue issue in regIssues) if (issue.Selected) selected.Add(issue);
            if (selected.Count == 0) return;

            if (MessageBox.Show(this, string.Format(Lang.T("reg.confirm"), selected.Count), AppName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            string backupPath = WriteBackup(selected);
            if (backupPath == null)
            {
                // No backup, no deletion. This is the one place the app refuses to
                // continue rather than degrading gracefully.
                MessageBox.Show(this, Lang.T("reg.backupFailed"), AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var admin = new List<RegIssue>();
            int removed = 0;
            foreach (RegIssue issue in selected)
            {
                if (issue.NeedsAdmin && !ElevatedJob.IsElevated()) { admin.Add(issue); continue; }
                if (RemoveIssue(issue)) removed++;
            }

            if (admin.Count > 0)
            {
                if (MessageBox.Show(this, string.Format(Lang.T("admin.offer"), admin.Count), AppName,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    if (ElevatedJob.RunElevated(ElevatedJob.BuildRegistryJob(admin), backupDir)) removed += admin.Count;
                    else SetStatus(Lang.T("admin.failed"));
                }
                else SetStatus(Lang.T("admin.declined"));
            }

            string message = string.Format(Lang.T("reg.done"), removed, Path.GetFileName(backupPath));
            LogLine(message);
            SetStatus(message);

            // Whatever is left is stale — rescan rather than showing entries that
            // may or may not still exist.
            regIssues.Clear();
            RefreshRegList();
        }

        static bool RemoveIssue(RegIssue issue)
        {
            try
            {
                RegistryKey hive = ElevatedJob.HiveFor(issue.HiveName);
                if (hive == null) return false;
                if (issue.DeleteWholeKey)
                {
                    hive.DeleteSubKeyTree(issue.Key, false);
                    return true;
                }
                using (RegistryKey key = hive.OpenSubKey(issue.Key, true))
                {
                    if (key == null) return false;
                    key.DeleteValue(issue.ValueName, false);
                    return true;
                }
            }
            catch { return false; }
        }

        // Returns the backup file path, or null when nothing could be written —
        // which stops the fix entirely.
        string WriteBackup(List<RegIssue> issues)
        {
            try
            {
                if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);
                var sb = new StringBuilder();
                sb.AppendLine(RegBackup.FileHeader);
                sb.AppendLine();
                sb.AppendLine("; WindowsStalker backup — run this file to restore the entries below");
                foreach (RegIssue issue in issues)
                {
                    if (issue.DeleteWholeKey) RegBackup.ExportKey(sb, issue.HiveName, issue.Key, true);
                    else RegBackup.ExportValue(sb, issue.HiveName, issue.Key, issue.ValueName);
                }
                string path = Path.Combine(backupDir,
                    "registry-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".reg");
                // .reg files are UTF-16 LE with a BOM; regedit refuses UTF-8 ones
                File.WriteAllText(path, sb.ToString(), Encoding.Unicode);
                return path;
            }
            catch { return null; }
        }
    }
}
