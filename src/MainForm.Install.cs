// Per-user install and uninstall. No administrator rights anywhere: the app
// copies itself into %LocalAppData%\Programs\WindowsStalker, writes per-user
// shortcuts and a per-user Uninstall key, and that is the whole installation.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WindowsStalker
{
    public partial class MainForm : Form
    {
        const string UninstallKeyPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WindowsStalker";

        void InstallOrUninstall()
        {
            if (IsInstalled)
            {
                if (MessageBox.Show(this, Lang.T("uninstall.confirm"), AppName,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                LaunchMode("--uninstall");
            }
            else
            {
                LaunchInstaller();
            }
        }

        void LaunchInstaller() { LaunchMode("--install"); }

        // Both modes run as a separate short-lived instance so the copy can
        // replace the running exe and so uninstall can delete the folder this
        // process is sitting in.
        void LaunchMode(string argument)
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, argument);
                psi.UseShellExecute = true;
                Process.Start(psi);
                ExitApp();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Lang.T("install.failed") + ex.Message, AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------- --install ----------

        static void RunInstallMode()
        {
            var f = new Form();
            f.Text = Lang.T("install.title");
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false; f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.ClientSize = new Size(380, 110);
            f.BackColor = Theme.Bg;
            f.Icon = Brand.AppIcon;
            Theme.DarkTitleBar(f);

            var label = new Label();
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.ForeColor = Theme.Text;
            label.Text = Lang.T("install.installing");
            f.Controls.Add(label);

            f.Shown += delegate
            {
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    string error = null;
                    try { DoInstall(); }
                    catch (Exception ex) { error = ex.Message; }
                    try
                    {
                        f.BeginInvoke((MethodInvoker)delegate
                        {
                            f.Hide();
                            if (error != null)
                                MessageBox.Show(Lang.T("install.failed") + error, AppName,
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                            else
                                try { Process.Start(Path.Combine(InstallDir, "WindowsStalker.exe")); }
                                catch { }
                            Application.ExitThread();
                        });
                    }
                    catch { }
                });
            };
            Application.Run(f);
        }

        static void DoInstall()
        {
            // The instance that launched --install is still shutting down and holds
            // the single-instance mutex — give it a moment, otherwise the installed
            // copy started below would just signal it and exit.
            System.Threading.Thread.Sleep(1500);

            string srcDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string dst = InstallDir;
            Directory.CreateDirectory(dst);

            string dstExe = Path.Combine(dst, "WindowsStalker.exe");
            if (!string.Equals(Application.ExecutablePath, dstExe, StringComparison.OrdinalIgnoreCase))
                File.Copy(Application.ExecutablePath, dstExe, true);

            // Carry the user's own state across, but never overwrite what is
            // already at the destination — a reinstall must not wipe settings.
            if (!string.Equals(srcDir, dst, StringComparison.OrdinalIgnoreCase))
                foreach (string name in new string[] { "settings.ini", "clean.log" })
                    CarryOverFile(Path.Combine(srcDir, name), Path.Combine(dst, name));

            // Shortcuts are non-essential: when Windows Script Host is disabled by
            // policy CreateShortcut throws, and an install whose files are already
            // in place should not fail over a missing .lnk.
            try
            {
                CreateShortcut(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WindowsStalker.lnk"), dstExe, dst);
                CreateShortcut(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WindowsStalker.lnk"), dstExe, dst);
            }
            catch { }

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
            {
                if (k != null)
                {
                    k.SetValue("DisplayName", AppName);
                    k.SetValue("DisplayVersion", AppVersion);
                    k.SetValue("Publisher", "Oleksii Poliakov");
                    k.SetValue("DisplayIcon", dstExe);
                    k.SetValue("InstallLocation", dst);
                    k.SetValue("UninstallString", "\"" + dstExe + "\" --uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", 400, RegistryValueKind.DWord); // KB
                }
            }

            // If autostart was enabled from the old location, repoint it
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                if (k != null && k.GetValue(RunValueName) != null)
                    k.SetValue(RunValueName, AutostartCommand(dstExe));
        }

        static void CarryOverFile(string src, string dst)
        {
            try
            {
                if (File.Exists(src) && !File.Exists(dst)) File.Copy(src, dst);
            }
            catch { }
        }

        // ---------- --uninstall ----------

        static void RunUninstallMode()
        {
            if (MessageBox.Show(Lang.T("uninstall.confirm"), AppName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            System.Threading.Thread.Sleep(1200); // let the main instance close

            string error = null;
            try
            {
                TryDelete(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WindowsStalker.lnk"));
                TryDelete(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WindowsStalker.lnk"));

                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                    if (k != null && k.GetValue(RunValueName) != null) k.DeleteValue(RunValueName, false);
                try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false); }
                catch { }

                // The running exe cannot delete itself; schedule the folder for
                // removal by a detached cmd that waits for this process to exit.
                ScheduleFolderRemoval(InstallDir);
            }
            catch (Exception ex) { error = ex.Message; }

            MessageBox.Show(error == null ? Lang.T("uninstall.done") : Lang.T("uninstall.error") + error,
                AppName, MessageBoxButtons.OK,
                error == null ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        // A detached cmd that waits two seconds and removes the folder. This is
        // the one place a child shell is unavoidable — the exe being deleted is
        // the one running — and it is a plain rmdir with a visible, ordinary
        // command line, not a hidden script host.
        static void ScheduleFolderRemoval(string folder)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe",
                    "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + folder + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch { }
        }

        // .lnk via WScript.Shell (COM, no extra dependencies)
        static void CreateShortcut(string lnkPath, string target, string workDir)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return;
            object shell = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                new object[] { lnkPath });
            Type st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }
    }
}
