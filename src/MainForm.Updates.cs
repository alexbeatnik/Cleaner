// Self-update: WindowsStalker ships as one portable exe with no installer to
// hook, so keeping it current is its own job. Once a day, and once on every
// launch, it asks the GitHub Releases API for the latest tag; if that is newer
// than the running build it downloads the release's WindowsStalker.exe into
// %TEMP% and hands the swap to a detached cmd.exe helper, which waits for this
// process to exit (the exe is locked while it runs), moves the new build over
// the old one and starts it again in the same window state.
//
// Everything here fails silently by design: no network, a rate-limited API, a
// release with no exe asset — the app carries on and tries again tomorrow. Only
// a check the user asked for by pressing the button reports what happened.
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace WindowsStalker
{
    public partial class MainForm : Form
    {
        internal const string ProjectUrl = "https://github.com/alexbeatnik/WindowsStalker";
        internal const string AuthorUrl = "https://github.com/alexbeatnik";
        internal const string LicenseUrl = ProjectUrl + "/blob/main/LICENSE";
        internal const string ReleasesUrl = ProjectUrl + "/releases";

        const string UpdateApiUrl =
            "https://api.github.com/repos/alexbeatnik/WindowsStalker/releases/latest";
        // The asset name the release workflow uploads (.github/workflows/release.yml)
        const string UpdateAssetName = "WindowsStalker.exe";
        // One small API request per day: nowhere near the 60/hour GitHub allows
        // an unauthenticated caller, even with several machines behind one address.
        const int AppUpdateCheckHours = 24;
        // A real build is ~240 KB. Anything much smaller is an error page or a
        // redirect stub that WebClient followed into a file.
        const long MinUpdateSize = 50 * 1024;

        bool autoUpdate = true;      // persisted
        DateTime lastAppUpdateCheck; // persisted; MinValue = never checked
        bool checkingAppUpdate;      // a check or download is already in flight
        bool startupAppCheckDone;    // this launch already ran its one free check
        // When the last unattended check was ATTEMPTED, as opposed to when one
        // last succeeded. Not persisted: it only has to survive this session.
        DateTime lastAppUpdateAttempt;

        // Pure, so the boundaries can be pinned by tests: the first check of a
        // launch always fires, after that the daily period applies, and a clock
        // that jumped backwards counts as due rather than parking the check until
        // the calendar catches up.
        internal static bool AppUpdateDue(bool startupChecked, DateTime last, DateTime now, int periodHours)
        {
            if (!startupChecked) return true;
            if (last == DateTime.MinValue) return true;
            if (last > now) return true;
            return (now - last).TotalHours >= periodHours;
        }

        // Also pure: a tag that is not a plain dotted number (a "-beta" suffix, a
        // renamed release) never triggers a download, and neither does one that
        // merely differs from what is running.
        internal static bool IsNewerVersion(string candidate, string current)
        {
            Version a, b;
            if (!Version.TryParse(candidate, out a)) return false;
            if (!Version.TryParse(current, out b)) return false;
            return a > b;
        }

        // Swapping the exe means restarting, which throws away whatever lives only
        // in memory: an analysis nobody has cleaned yet, a half-walked folder, a
        // registry scan. A modal dialog counts too — it disables the owner window
        // at the Win32 level, which is the reliable way to ask.
        bool UpdateBusy
        {
            get
            {
                if (analyzeRunning || cleanRunning || spaceScanRunning || regScanRunning) return true;
                try { if (IsHandleCreated && !NativeMethods.IsWindowEnabled(Handle)) return true; }
                catch { }
                return false;
            }
        }

        void MaybeCheckAppUpdate()
        {
            if (!autoUpdate || checkingAppUpdate || UpdateBusy) return;
            if (!AppUpdateDue(startupAppCheckDone, lastAppUpdateCheck, DateTime.Now, AppUpdateCheckHours)) return;
            // A check that never reached GitHub leaves lastAppUpdateCheck alone —
            // by design, so the status line does not claim a check that did not
            // happen. Without the attempt stamp the line above then says "due"
            // again on the very next timer tick, and an offline machine would ask
            // the API every ten minutes instead of once a day.
            if (startupAppCheckDone
                && !AppUpdateDue(true, lastAppUpdateAttempt, DateTime.Now, AppUpdateCheckHours)) return;
            startupAppCheckDone = true; // set only when a check really starts
            lastAppUpdateAttempt = DateTime.Now;
            StartAppUpdateCheck(false);
        }

        // The Settings button. Runs even with automatic updates switched off —
        // that toggle governs the unattended check, not the user's own.
        void CheckForUpdatesNow()
        {
            if (checkingAppUpdate) return;
            if (UpdateBusy) { SetUpdateStatus(Lang.T("update.busy")); return; }
            startupAppCheckDone = true;
            lastAppUpdateAttempt = DateTime.Now;
            StartAppUpdateCheck(true);
        }

        void StartAppUpdateCheck(bool manual)
        {
            checkingAppUpdate = true;
            if (manual) SetUpdateStatus(Lang.T("update.checking"));
            ThreadPool.QueueUserWorkItem(delegate { AppUpdateWorker(manual); });
        }

        // Off the UI thread from start to finish: the API call, the version
        // comparison and the download all happen here, and only the verdict is
        // marshalled back.
        void AppUpdateWorker(bool manual)
        {
            string downloaded = null, version = null;
            bool reached = false, newer = false;
            try
            {
                // GitHub dropped TLS 1.0/1.1, and .NET Framework 4.8 still takes
                // its default from the machine's SCHANNEL settings — name the
                // protocols rather than inherit them. 12288 is Tls13, which the
                // 4.8 enum has no name for.
                const SecurityProtocolType Tls13 = (SecurityProtocolType)12288;
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | Tls13;

                string json;
                using (var api = new WebClient())
                {
                    api.Headers.Add("User-Agent", AppName); // the API rejects a request without one
                    json = api.DownloadString(UpdateApiUrl);
                }

                Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"[vV]?([\\d.]+)\"");
                Match asset = Regex.Match(json,
                    "\"browser_download_url\"\\s*:\\s*\"([^\"]*" + UpdateAssetName + ")\"");
                if (tag.Success && asset.Success)
                {
                    reached = true;
                    version = tag.Groups[1].Value;
                    if (IsNewerVersion(version, AppVersion))
                    {
                        newer = true;
                        // %TEMP% is writable wherever the app itself lives — a
                        // portable copy dropped in Program Files would not be.
                        string target = Path.Combine(Path.GetTempPath(), AppName + ".update.exe");
                        TryDeleteFile(target); // a leftover from an interrupted attempt
                        using (var wc = new WebClient())
                        {
                            wc.Headers.Add("User-Agent", AppName);
                            wc.DownloadFile(asset.Groups[1].Value, target);
                        }
                        if (File.Exists(target) && new FileInfo(target).Length >= MinUpdateSize)
                            downloaded = target;
                        else
                            TryDeleteFile(target);
                    }
                }
            }
            catch { } // offline, rate-limited, or no release yet — tomorrow, then

            string path = downloaded, found = version;
            bool ok = reached, isNew = newer;
            OnUi(delegate { OnAppUpdateChecked(path, found, ok, isNew, manual); });
        }

        void OnAppUpdateChecked(string updatePath, string version, bool reached, bool newer, bool manual)
        {
            checkingAppUpdate = false;
            if (reached)
            {
                lastAppUpdateCheck = DateTime.Now;
                SaveSettings();
            }

            // Reached but the download failed counts as a failed check: there is
            // a newer build and the user still does not have it.
            if (!reached || (newer && updatePath == null))
            {
                if (manual) SetUpdateStatus(Lang.T("update.failed"));
                else RefreshUpdateStatus();
                return;
            }
            if (!newer)
            {
                if (manual) SetUpdateStatus(string.Format(Lang.T("update.upToDate"), AppVersion));
                else RefreshUpdateStatus();
                return;
            }
            // The user started something between the download beginning and it
            // finishing. Throw the file away rather than restart under them; the
            // next check downloads it again.
            if (UpdateBusy)
            {
                TryDeleteFile(updatePath);
                SetUpdateStatus(Lang.T("update.busy"));
                return;
            }
            ApplyAppUpdate(updatePath, version);
        }

        // The swap itself. cmd.exe outlives this process on purpose: a running exe
        // cannot overwrite itself, so the helper waits three seconds for the file
        // handle to go away, moves the new build into place and relaunches it —
        // back to the tray if that is where the window was.
        void ApplyAppUpdate(string updatePath, string version)
        {
            string message = string.Format(Lang.T("update.installing"), version);
            SetUpdateStatus(message);
            SetStatus(message);
            LogLine(message);
            Notify(AppName, message);

            string exePath = Application.ExecutablePath;
            bool trayNext = WindowState == FormWindowState.Minimized || !ShowInTaskbar;
            var psi = new ProcessStartInfo("cmd.exe",
                "/c timeout /t 3 /nobreak >nul & move /y \"" + updatePath + "\" \"" + exePath + "\""
                + " & start \"\" \"" + exePath + "\"" + (trayNext ? " --tray" : ""));
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            try
            {
                Process.Start(psi);
                ExitApp(); // releases the file lock the helper is waiting on
            }
            catch { TryDeleteFile(updatePath); }
        }

        // ---------- the status line under the toggle ----------

        void SetUpdateStatus(string text)
        {
            if (updateStatus != null && !updateStatus.IsDisposed) updateStatus.Text = text;
        }

        void RefreshUpdateStatus()
        {
            if (updateStatus == null) return;
            if (!autoUpdate) { SetUpdateStatus(Lang.T("update.off")); return; }
            SetUpdateStatus(string.Format(Lang.T("update.lastCheck"),
                lastAppUpdateCheck == DateTime.MinValue
                    ? Lang.T("common.never")
                    : lastAppUpdateCheck.ToString("dd.MM.yyyy HH:mm")));
        }

        internal static void TryDeleteFile(string path)
        {
            try { if (path != null && File.Exists(path)) File.Delete(path); }
            catch { }
        }

        internal static void OpenUrl(string url)
        {
            try { Process.Start(url); }
            catch { }
        }
    }
}
