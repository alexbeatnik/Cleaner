// The cleaner: building the rule list, the background analysis, and the clean
// itself — including the hand-off to the elevated helper for system folders.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace WindowsStalker
{
    public partial class MainForm : Form
    {
        bool analyzeRunning;

        // A fresh session for every analysis, with one result object per rule.
        // Nothing is shared with the previous session, so a superseded worker's
        // late writes land in an object nobody reads any more.
        CleanScan NewSession()
        {
            var session = new CleanScan();
            foreach (CleanRule rule in catalog)
            {
                var rr = new RuleResult();
                rr.Rule = rule;
                rr.Selected = IsRuleSelected(rule);
                session.Results.Add(rr);
            }
            session.RulesTotal = session.Results.Count;
            return session;
        }

        // The user's tick wins; a rule they never touched follows its own default.
        // That is what lets a new catalog entry arrive switched on for an existing
        // install without a settings migration.
        bool IsRuleSelected(CleanRule rule)
        {
            bool chosen;
            if (ruleChoices.TryGetValue(rule.Id, out chosen)) return chosen;
            return rule.DefaultOn;
        }

        // ---------- the list ----------

        void RefreshCleanList()
        {
            if (cleanList == null || cleanList.IsDisposed) return;
            cleanList.BeginUpdate();
            cleanList.Items.Clear();

            string currentGroup = null;
            foreach (RuleResult rr in scan.Results)
            {
                if (rr.Rule.GroupKey != currentGroup)
                {
                    currentGroup = rr.Rule.GroupKey;
                    var header = new ListViewItem(Lang.T(currentGroup));
                    header.SubItems.Add("");
                    header.SubItems.Add("");
                    header.SubItems.Add("");
                    header.Tag = null; // Tag == null and no check box = a section header
                    cleanList.Items.Add(header);
                }

                var item = new ListViewItem(rr.Rule.DisplayName());
                item.SubItems.Add(scan.Finished ? Util.FormatSize(rr.Bytes) : "");
                item.SubItems.Add(scan.Finished && rr.Files > 0 ? Util.FormatCount(rr.Files) : "");
                item.SubItems.Add(RuleNote(rr));
                item.Tag = rr;
                if (scan.Finished && rr.Empty) item.ForeColor = Theme.Disabled;
                else if (rr.Blocked) item.ForeColor = Theme.Warn;
                cleanList.Items.Add(item);
            }
            cleanList.EndUpdate();
            cleanList.Refit();
            UpdateCleanSummary();
        }

        string RuleNote(RuleResult rr)
        {
            if (rr.Blocked) return string.Format(Lang.T("clean.blocked"), rr.BlockedBy);
            if (rr.Rule.NeedsAdmin) return Lang.T("clean.needsAdmin");
            if (rr.Rule.Risky) return Lang.T("clean.riskyTag");
            return "";
        }

        void UpdateCleanSummary()
        {
            if (cleanSummary == null) return;
            int selected = 0;
            foreach (RuleResult rr in scan.Results) if (rr.Selected) selected++;
            long bytes = scan.Finished ? scan.SelectedBytes : 0;

            // The strip carries the numbers whether or not an analysis has run —
            // before one, the counts are the only thing there is to say.
            FillStrip(cleanStrip,
                new string[]
                {
                    Lang.T("stat.categories"), Lang.T("stat.selected"),
                    Lang.T("stat.junkFound"), Lang.T("stat.ready")
                },
                new string[]
                {
                    scan.Results.Count.ToString(),
                    selected.ToString(),
                    scan.Finished ? Util.FormatSize(scan.TotalBytes) : "—",
                    scan.Finished ? Util.FormatSize(bytes) : "—"
                },
                new Color[]
                {
                    Color.Empty, Color.Empty,
                    Color.Empty, bytes > 0 ? Theme.Warn : Color.Empty
                });

            cleanSummary.Text = !scan.Finished ? Lang.T("clean.notAnalyzed")
                : bytes > 0
                    ? string.Format(Lang.T("clean.found"), Util.FormatSize(bytes), Util.FormatCount(scan.SelectedFiles))
                    : Lang.T("clean.nothing");
            cleanSummary.ForeColor = bytes > 0 ? Theme.Text : Theme.Muted;
            if (btnClean != null) btnClean.Enabled = bytes > 0 && !cleanRunning && !analyzeRunning;
            if (dashClean != null) dashClean.Enabled = btnClean.Enabled;
            UpdateDashboard();
        }

        // mode: 0 = none, 1 = everything, 2 = the recommended set (defaults, and
        // never the risky ones — that is also exactly what the scheduler uses).
        void SelectRules(int mode)
        {
            foreach (RuleResult rr in scan.Results)
            {
                bool on = mode == 1 ? !rr.Rule.Risky
                        : mode == 2 ? (rr.Rule.DefaultOn && !rr.Rule.Risky)
                        : false;
                rr.Selected = on;
                ruleChoices[rr.Rule.Id] = on;
            }
            SaveSettings();
            RefreshCleanList();
        }

        // ---------- analysis ----------

        void StartAnalyze(bool automatic)
        {
            if (analyzeRunning || cleanRunning) return;
            scan.Cancel.Cancel();               // stop whatever the old session was doing
            catalog = Rules.Build();            // software may have been installed since
            CleanScan session = NewSession();
            scan = session;
            analyzeRunning = true;

            RefreshCleanList();
            BeginBusy(string.Format(Lang.T("clean.analyzing"), ""));
            gauge.SetProgress(0, Lang.T("dash.busySub"));
            dashHeadline.Text = Lang.T("dash.busyHead");
            dashSubline.Text = Lang.T("dash.busySub");

            ThreadPool.QueueUserWorkItem(delegate { AnalyzeWorker(session, automatic); });
        }

        void AnalyzeWorker(CleanScan session, bool automatic)
        {
            foreach (RuleResult rr in session.Results)
            {
                if (session.Cancel.Cancelled) break;
                try { AnalyzeRule(rr, session.Cancel); }
                catch { } // one unreadable location must not abort the sweep
                session.RulesDone++;

                RuleResult captured = rr;
                int done = session.RulesDone, total = Math.Max(1, session.RulesTotal);
                OnUi(delegate
                {
                    if (scan != session) return; // superseded by a newer analysis
                    gauge.SetProgress((double)done / total, Lang.T("dash.busySub"));
                    SetStatus(string.Format(Lang.T("clean.analyzing"), captured.Rule.DisplayName()));
                });
            }

            session.Cancelled = session.Cancel.Cancelled;
            session.Finished = !session.Cancelled;
            OnUi(delegate
            {
                if (scan != session) return;
                analyzeRunning = false;
                RefreshCleanList();
                UpdateDashboard();
                EndBusy(session.Cancelled ? Lang.T("common.cancelled") : Lang.T("status.ready"));
                if (session.Finished && automatic) RunAutomaticClean();
            });
        }

        // Fills in one rule's result. Read-only: nothing is deleted here, which is
        // why the analysis can safely cover rules the user has not ticked.
        void AnalyzeRule(RuleResult rr, CancelFlag cancel)
        {
            CleanRule rule = rr.Rule;
            rr.BlockedBy = RunningProcess(rule.Processes);
            rr.Blocked = rr.BlockedBy != null;

            if (rule.Kind == RuleKind.RecycleBin)
            {
                long bytes, items;
                if (Util.QueryRecycleBin(out bytes, out items))
                {
                    rr.Bytes = bytes;
                    rr.Files = (int)Math.Min(int.MaxValue, items);
                }
                return;
            }

            foreach (string root in rule.Roots)
            {
                if (cancel.Cancelled) return;
                if (!Directory.Exists(root)) continue;
                if (rule.Kind == RuleKind.Glob) AnalyzeGlob(rr, root, cancel);
                else AnalyzeFolder(rr, root, cancel);
            }
        }

        // A folder rule empties its root without removing the root itself, so the
        // deletable entries are its immediate children.
        void AnalyzeFolder(RuleResult rr, string root, CancelFlag cancel)
        {
            string[] entries;
            try
            {
                var all = new List<string>();
                all.AddRange(Directory.GetFiles(root));
                all.AddRange(Directory.GetDirectories(root));
                entries = all.ToArray();
            }
            catch { return; }

            DateTime nowUtc = DateTime.UtcNow;
            foreach (string entry in entries)
            {
                if (cancel.Cancelled) return;
                if (Util.IsProtectedPath(entry)) continue;
                if (!Util.IsUnder(entry, root)) continue; // paranoia: never step outside the root
                if (Util.IsRecent(entry, rr.Rule.MinAgeHours, nowUtc)) continue;
                int files = 0;
                long size = Util.EntrySize(entry, ref files, cancel);
                rr.Entries.Add(entry);
                rr.Bytes += size;
                rr.Files += files;
            }
        }

        void AnalyzeGlob(RuleResult rr, string root, CancelFlag cancel)
        {
            foreach (string mask in rr.Rule.Masks)
            {
                if (cancel.Cancelled) return;
                string[] hits;
                try
                {
                    hits = Directory.GetFiles(root, mask,
                        rr.Rule.GlobRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                }
                catch { continue; }
                DateTime nowUtc = DateTime.UtcNow;
                foreach (string hit in hits)
                {
                    if (cancel.Cancelled) return;
                    if (Util.IsProtectedPath(hit)) continue;
                    if (Util.IsRecent(hit, rr.Rule.MinAgeHours, nowUtc)) continue;
                    int files = 0;
                    long size = Util.EntrySize(hit, ref files, cancel);
                    rr.Entries.Add(hit);
                    rr.Bytes += size;
                    rr.Files += files;
                }
            }
        }

        // ---------- cleaning ----------

        void StartClean()
        {
            if (cleanRunning || analyzeRunning || !scan.Finished) return;
            long bytes = scan.SelectedBytes;
            if (bytes <= 0)
            {
                MessageBox.Show(this, Lang.T("clean.nothingSelected"), AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (confirmBeforeClean)
            {
                string question = string.Format(Lang.T("clean.confirm"),
                    Util.FormatSize(bytes), Util.FormatCount(scan.SelectedFiles));
                if (MessageBox.Show(this, question, AppName,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
            }
            RunClean(scan, false);
        }

        void RunClean(CleanScan session, bool automatic)
        {
            cleanRunning = true;
            BeginBusy(Lang.T("clean.running"));
            gauge.SetBusy(Lang.T("clean.running"));

            // Snapshot what to delete on the UI thread: the worker must not read
            // selection state that the user could still be clicking on.
            var jobs = new List<RuleResult>();
            var adminPaths = new List<string>();
            bool emptyRecycleBin = false;
            foreach (RuleResult rr in session.Results)
            {
                if (!rr.Selected || rr.Blocked || rr.Empty) continue;
                if (rr.Rule.Kind == RuleKind.RecycleBin) { emptyRecycleBin = true; continue; }
                if (rr.Rule.NeedsAdmin && !ElevatedJob.IsElevated()) { adminPaths.AddRange(rr.Entries); continue; }
                jobs.Add(rr);
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                long freed = 0;
                int files = 0, failed = 0;
                foreach (RuleResult rr in jobs)
                {
                    if (session.Cancel.Cancelled) break;
                    foreach (string entry in rr.Entries)
                    {
                        if (session.Cancel.Cancelled) break;
                        Util.DeleteEntry(entry, ref freed, ref files, ref failed);
                    }
                }
                if (emptyRecycleBin && !session.Cancel.Cancelled)
                {
                    long binBytes, binItems;
                    bool known = Util.QueryRecycleBin(out binBytes, out binItems);
                    if (Util.EmptyRecycleBin() && known)
                    {
                        freed += binBytes;
                        files += (int)Math.Min(int.MaxValue, binItems);
                    }
                }

                long freedCopy = freed;
                int filesCopy = files, failedCopy = failed;
                OnUi(delegate { FinishClean(session, freedCopy, filesCopy, failedCopy, adminPaths, automatic); });
            });
        }

        void FinishClean(CleanScan session, long freed, int files, int failed,
                         List<string> adminPaths, bool automatic)
        {
            cleanRunning = false;

            // Anything under %SystemRoot% needs the elevated helper. It is offered
            // once, after the ordinary delete, so the user sees what was already
            // freed before deciding about the UAC prompt.
            if (adminPaths.Count > 0 && !automatic)
            {
                if (MessageBox.Show(this, string.Format(Lang.T("admin.offer"), adminPaths.Count),
                        AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    if (!ElevatedJob.RunElevated(ElevatedJob.BuildDeleteJob(adminPaths), backupDir))
                        SetStatus(Lang.T("admin.failed"));
                }
                else SetStatus(Lang.T("admin.declined"));
            }

            totalFreedBytes += freed;
            totalFreedFiles += files;
            totalCleans++;
            lastCleanTime = DateTime.Now;
            SaveSettings();

            string message = failed > 0
                ? string.Format(Lang.T("clean.doneFailed"), Util.FormatSize(freed), failed)
                : string.Format(Lang.T("clean.done"), Util.FormatSize(freed), Util.FormatCount(files));
            LogLine(message);
            EndBusy(message);
            if (automatic) Notify(AppName, message);

            // The results are stale the moment they are deleted — start from a
            // clean session rather than showing sizes that no longer exist.
            scan = NewSession();
            RefreshCleanList();
            RefreshDrives();
            UpdateDashboard();
            gauge.SetIdle("", Lang.T("dash.gaugeIdle"));
        }

        // ---------- the scheduler ----------

        void StartScheduleTimer()
        {
            // Fully qualified: this file also uses System.Threading, whose Timer
            // is not the one that marshals onto the UI thread.
            schedTimer = new System.Windows.Forms.Timer();
            schedTimer.Interval = 10 * 60 * 1000; // ten minutes is plenty for a daily job
            // The same tick drives the update check: both are "once a day, when
            // the app happens to be idle" jobs, and one timer is one thing to
            // reason about when either of them misfires.
            schedTimer.Tick += delegate { ScheduleTick(); MaybeCheckAppUpdate(); };
            schedTimer.Start();
        }

        void ScheduleTick()
        {
            if (schedMode == 0 || analyzeRunning || cleanRunning) return;
            // Never start work behind a modal dialog: a message box disables the
            // owner window at the Win32 level, which is the reliable probe here.
            if (IsHandleCreated && !NativeMethods.IsWindowEnabled(Handle)) return;
            if (!AutoCleanDue(lastScheduledClean, DateTime.Now, schedMode)) return;
            lastScheduledClean = DateTime.Now;
            SaveSettings();
            SelectRules(2); // recommended only — the scheduler never touches risky rules
            StartAnalyze(true);
        }

        // Pure so tests can pin the boundaries: mode 1 = daily, 2 = weekly, and a
        // clock that jumped backwards (a timezone change, a restored VM) counts as
        // due rather than blocking cleaning forever.
        internal static bool AutoCleanDue(DateTime last, DateTime now, int mode)
        {
            if (mode <= 0) return false;
            if (last == DateTime.MinValue) return true;
            if (last > now) return true;
            double days = (now - last).TotalDays;
            return mode == 1 ? days >= 1 : days >= 7;
        }

        void RunAutomaticClean()
        {
            if (scan.SelectedBytes <= 0) return;
            RunClean(scan, true);
        }
    }
}
