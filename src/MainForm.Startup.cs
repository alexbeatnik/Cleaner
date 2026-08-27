// The startup manager: what runs at sign-in, and turning it off without
// uninstalling anything.
//
// Enabling and disabling goes through the same StartupApproved values Task
// Manager writes, so the two agree with each other — deleting the Run value
// outright (what most cleaners do) loses the entry permanently and cannot be
// undone from Windows' own UI.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Cleaner
{
    enum StartupSource { HkcuRun, HklmRun, HklmRun32, FolderUser, FolderCommon }

    sealed class StartupEntry
    {
        public string Name;          // Run value name, or the shortcut's file name
        public string Command;       // command line or shortcut target
        public string FilePath;      // the .lnk itself, for folder entries
        public StartupSource Source;
        public bool Enabled = true;
        public bool Missing;         // the program it points at is gone
        public bool NeedsAdmin;      // machine-wide entry: cannot be changed as a user

        public string LocationKey
        {
            get
            {
                switch (Source)
                {
                    case StartupSource.FolderUser: return "startup.locFolderUser";
                    case StartupSource.FolderCommon: return "startup.locFolderCommon";
                    case StartupSource.HkcuRun: return "startup.locHkcu";
                    default: return "startup.locHklm";
                }
            }
        }
    }

    public partial class MainForm : Form
    {
        int startupCount = -1;
        readonly List<StartupEntry> startupEntries = new List<StartupEntry>();

        const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string ApprovedRun32 = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
        const string ApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

        // ---------- reading ----------

        void RefreshStartup()
        {
            startupEntries.Clear();
            ReadRunKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run",
                StartupSource.HkcuRun);
            ReadRunKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                StartupSource.HklmRun);
            ReadRunKey(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
                StartupSource.HklmRun32);
            ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                StartupSource.FolderUser);
            ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                StartupSource.FolderCommon);

            startupCount = startupEntries.Count;
            RefreshStartupTexts();
            UpdateDashboard();
        }

        void ReadRunKey(RegistryKey hive, string path, StartupSource source)
        {
            RegistryKey key = null;
            try { key = hive.OpenSubKey(path, false); }
            catch { }
            if (key == null) return;
            using (key)
            {
                string[] names;
                try { names = key.GetValueNames(); }
                catch { return; }
                foreach (string name in names)
                {
                    try
                    {
                        string command = key.GetValue(name) as string;
                        if (string.IsNullOrWhiteSpace(command)) continue;
                        var entry = new StartupEntry();
                        entry.Name = name;
                        entry.Command = command;
                        entry.Source = source;
                        entry.NeedsAdmin = source != StartupSource.HkcuRun;
                        entry.Missing = Util.TargetMissing(command);
                        entry.Enabled = ReadApproved(source, ApprovedKeyFor(source), name);
                        startupEntries.Add(entry);
                    }
                    catch { }
                }
            }
        }

        void ReadStartupFolder(string folder, StartupSource source)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            string[] files;
            try { files = Directory.GetFiles(folder); }
            catch { return; }
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                var entry = new StartupEntry();
                entry.Name = Path.GetFileNameWithoutExtension(file);
                entry.FilePath = file;
                entry.Command = ResolveShortcut(file) ?? file;
                entry.Source = source;
                entry.NeedsAdmin = source == StartupSource.FolderCommon;
                entry.Missing = Util.TargetMissing(entry.Command);
                entry.Enabled = ReadApproved(source, ApprovedFolder, name);
                startupEntries.Add(entry);
            }
        }

        static string ApprovedKeyFor(StartupSource source)
        {
            return source == StartupSource.HklmRun32 ? ApprovedRun32 : ApprovedRun;
        }

        // The StartupApproved value is a binary blob whose first byte carries the
        // state: an odd low bit means disabled. No entry at all means enabled,
        // which is the state of everything that has never been touched.
        static bool ReadApproved(StartupSource source, string approvedPath, string valueName)
        {
            RegistryKey hive = source == StartupSource.HkcuRun || source == StartupSource.FolderUser
                ? Registry.CurrentUser : Registry.LocalMachine;
            // Folder entries are approved per user even when the shortcut is
            // machine-wide, so both folder sources look in HKCU first.
            if (source == StartupSource.FolderCommon) hive = Registry.CurrentUser;
            try
            {
                using (RegistryKey key = hive.OpenSubKey(approvedPath, false))
                {
                    if (key == null) return true;
                    var data = key.GetValue(valueName) as byte[];
                    if (data == null || data.Length == 0) return true;
                    return (data[0] & 1) == 0;
                }
            }
            catch { return true; }
        }

        // Task Manager writes 12 bytes: the state byte, three pad bytes, and the
        // FILETIME of the change (zero when enabling).
        internal static byte[] ApprovedBlob(bool enabled)
        {
            var data = new byte[12];
            data[0] = (byte)(enabled ? 0x02 : 0x03);
            if (!enabled)
            {
                byte[] stamp = BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc());
                Buffer.BlockCopy(stamp, 0, data, 4, 8);
            }
            return data;
        }

        static string ResolveShortcut(string lnkPath)
        {
            if (!lnkPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return null;
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                    new object[] { lnkPath });
                return sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null) as string;
            }
            catch { return null; }
        }

        // ---------- list ----------

        void RefreshStartupTexts()
        {
            if (startupList == null || startupList.IsDisposed) return;
            startupList.BeginUpdate();
            startupList.Items.Clear();
            int enabled = 0;
            foreach (StartupEntry entry in startupEntries)
            {
                if (entry.Enabled) enabled++;
                var item = new ListViewItem(entry.Name);
                item.SubItems.Add(entry.Enabled ? Lang.T("startup.enabled") : Lang.T("startup.disabled"));
                item.SubItems.Add(Lang.T(entry.LocationKey));
                item.SubItems.Add(entry.Missing
                    ? Lang.T("startup.broken") + " — " + entry.Command
                    : entry.Command);
                item.Tag = entry;
                item.ForeColor = entry.Missing ? Theme.Danger
                    : entry.Enabled ? Theme.Text : Theme.Disabled;
                startupList.Items.Add(item);
            }
            startupList.EndUpdate();
            startupList.Refit();

            int broken = 0;
            foreach (StartupEntry entry in startupEntries) if (entry.Missing) broken++;
            FillStrip(startupStrip,
                new string[]
                {
                    Lang.T("stat.entries"), Lang.T("stat.enabled"),
                    Lang.T("stat.disabled"), Lang.T("stat.broken")
                },
                new string[]
                {
                    startupEntries.Count.ToString(), enabled.ToString(),
                    (startupEntries.Count - enabled).ToString(), broken.ToString()
                },
                new Color[]
                {
                    Color.Empty, Theme.Good, Color.Empty,
                    broken > 0 ? Theme.Danger : Color.Empty
                });

            if (startupSummary != null)
                startupSummary.Text = string.Format(Lang.T("startup.summary"), startupEntries.Count, enabled);
        }

        StartupEntry SelectedStartup()
        {
            if (startupList == null || startupList.SelectedItems.Count == 0) return null;
            return startupList.SelectedItems[0].Tag as StartupEntry;
        }

        // ---------- actions ----------

        void ToggleStartupSelected()
        {
            StartupEntry entry = SelectedStartup();
            if (entry == null) return;
            if (entry.NeedsAdmin && entry.Source != StartupSource.FolderCommon)
            {
                // The machine-wide approval list lives in HKLM. Rather than asking
                // for elevation to flip a checkbox, say so plainly.
                MessageBox.Show(this, Lang.T("startup.adminOnly"), AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            bool target = !entry.Enabled;
            string approvedPath = entry.Source == StartupSource.FolderUser
                || entry.Source == StartupSource.FolderCommon
                ? ApprovedFolder : ApprovedKeyFor(entry.Source);
            string valueName = entry.FilePath != null ? Path.GetFileName(entry.FilePath) : entry.Name;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(approvedPath))
                {
                    if (key == null) return;
                    key.SetValue(valueName, ApprovedBlob(target), RegistryValueKind.Binary);
                }
                entry.Enabled = target;
                LogLine(string.Format(
                    Lang.T(target ? "log.startupEnabled" : "log.startupDisabled"), entry.Name));
                RefreshStartupTexts();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void OpenStartupLocation()
        {
            StartupEntry entry = SelectedStartup();
            if (entry == null) return;
            if (entry.FilePath != null) { OpenInExplorer(entry.FilePath); return; }
            string exe = Util.ExeFromCommandLine(entry.Command);
            if (!string.IsNullOrEmpty(exe))
                OpenInExplorer(Environment.ExpandEnvironmentVariables(exe.Trim('"')));
        }

        void DeleteStartupSelected()
        {
            StartupEntry entry = SelectedStartup();
            if (entry == null) return;
            if (entry.NeedsAdmin)
            {
                MessageBox.Show(this, Lang.T("startup.adminOnly"), AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, string.Format(Lang.T("startup.confirmDelete"), entry.Name), AppName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                if (entry.FilePath != null)
                {
                    Util.RecycleFiles(new string[] { entry.FilePath }); // recoverable on purpose
                }
                else
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                        if (key != null) key.DeleteValue(entry.Name, false);
                }
                LogLine(string.Format(Lang.T("log.startupRemoved"), entry.Name));
                RefreshStartup();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
