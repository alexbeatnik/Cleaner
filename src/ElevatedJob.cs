// The elevated helper. The app always runs non-elevated; the handful of actions
// that genuinely cannot (emptying C:\Windows\Temp, removing an HKLM key) are
// written to a job file and performed by a short-lived second instance started
// with Verb = "runas". That instance does nothing but execute the file and exit
// with the number of failures, so there is no long-running elevated UI.
//
// Deliberately NOT a hidden "powershell -Command" or a script host: a cleaner
// that spawns hidden script children to delete system files is indistinguishable
// from the real thing to a heuristic scanner, and this app cannot afford that.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Cleaner
{
    enum JobOpKind { Delete, RegDeleteValue, RegDeleteKey }

    sealed class JobOp
    {
        public JobOpKind Kind;
        public string A;   // path, or hive name
        public string B;   // registry key path
        public string C;   // registry value name
    }

    static class ElevatedJob
    {
        internal const string Header = "# Cleaner job v1";

        public static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        // ---------- writing ----------

        public static string BuildDeleteJob(IEnumerable<string> paths)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Header);
            foreach (string p in paths)
            {
                if (string.IsNullOrEmpty(p) || p.IndexOf('\n') >= 0) continue;
                sb.AppendLine("DEL|" + p);
            }
            return sb.ToString();
        }

        public static string BuildRegistryJob(IEnumerable<RegIssue> issues)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Header);
            foreach (RegIssue issue in issues)
            {
                if (issue == null || issue.Key == null) continue;
                sb.AppendLine((issue.DeleteWholeKey ? "REGKEY|" : "REGVAL|")
                    + issue.HiveName + "|" + issue.Key + "|" + (issue.ValueName ?? ""));
            }
            return sb.ToString();
        }

        // ---------- parsing ----------

        // Kept pure and separate from execution so the format has real tests: a
        // malformed line must be skipped, never guessed at, because the process
        // reading this file is running as administrator.
        internal static List<JobOp> Parse(string content)
        {
            int skipped;
            return Parse(content, out skipped);
        }

        // skipped counts the instruction lines that were refused. Run adds it to
        // the failure count, because a job whose lines were thrown away has NOT
        // been carried out — and the caller reads exit code 0 as "everything done".
        internal static List<JobOp> Parse(string content, out int skipped)
        {
            skipped = 0;
            var ops = new List<JobOp>();
            if (string.IsNullOrEmpty(content)) return ops;
            string[] lines = content.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != Header)
            {
                foreach (string other in lines)
                {
                    string t = other.Trim();
                    if (t.Length > 0 && t[0] != '#') skipped++;
                }
                return ops; // wrong file, do nothing
            }
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] parts = line.Split('|');
                if (parts.Length < 2) { skipped++; continue; }
                var op = new JobOp();
                if (parts[0] == "DEL")
                {
                    op.Kind = JobOpKind.Delete;
                    op.A = line.Substring(4); // paths may contain '|'? they cannot on Windows, but be exact
                    if (op.A.Length == 0) { skipped++; continue; }
                }
                else if (parts[0] == "REGVAL" && parts.Length >= 4)
                {
                    op.Kind = JobOpKind.RegDeleteValue;
                    op.A = parts[1]; op.B = parts[2]; op.C = parts[3];
                    if (op.C.Length == 0) { skipped++; continue; } // a value job with no value is malformed
                }
                else if (parts[0] == "REGKEY" && parts.Length >= 3)
                {
                    op.Kind = JobOpKind.RegDeleteKey;
                    op.A = parts[1]; op.B = parts[2];
                }
                else { skipped++; continue; }
                if (!Validate(op)) { skipped++; continue; }
                ops.Add(op);
            }
            return ops;
        }

        // Every op is re-checked inside the elevated process. The file lives in a
        // user-writable folder, so its contents are treated as a request, not as
        // an instruction: paths go through the same guard the normal delete uses,
        // and only the two hives the scanners actually touch are accepted.
        internal static bool Validate(JobOp op)
        {
            if (op == null) return false;
            if (op.Kind == JobOpKind.Delete)
            {
                if (string.IsNullOrWhiteSpace(op.A)) return false;
                if (!Path.IsPathRooted(op.A)) return false;
                return !Util.IsProtectedPath(op.A);
            }
            if (HiveFor(op.A) == null) return false;
            if (string.IsNullOrWhiteSpace(op.B)) return false;
            // A bare hive root, or anything trying to walk upwards, is refused
            if (op.B.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            return op.B.IndexOf('\\') > 0;
        }

        internal static RegistryKey HiveFor(string name)
        {
            if (string.Equals(name, "HKLM", StringComparison.OrdinalIgnoreCase)) return Registry.LocalMachine;
            if (string.Equals(name, "HKCU", StringComparison.OrdinalIgnoreCase)) return Registry.CurrentUser;
            if (string.Equals(name, "HKCR", StringComparison.OrdinalIgnoreCase)) return Registry.ClassesRoot;
            return null;
        }

        // ---------- execution ----------

        // Runs the job file named on the command line and returns the number of
        // operations that failed — which becomes the process exit code, so the
        // normal instance can tell "declined or blocked" from "done".
        public static int Run(string jobPath)
        {
            if (string.IsNullOrEmpty(jobPath)) return 1;
            string content;
            try { content = File.ReadAllText(jobPath, Encoding.UTF8); }
            catch { return 1; }
            try { File.Delete(jobPath); } catch { } // single use, whatever happens next

            int skipped;
            List<JobOp> ops = Parse(content, out skipped);
            int failed = skipped; // a refused line is a failure, not a silent success
            foreach (JobOp op in ops)
            {
                try
                {
                    if (op.Kind == JobOpKind.Delete)
                    {
                        long bytes = 0; int files = 0, entryFailed = 0;
                        Util.DeleteEntry(op.A, ref bytes, ref files, ref entryFailed);
                        if (entryFailed > 0) failed++;
                    }
                    else if (op.Kind == JobOpKind.RegDeleteValue)
                    {
                        using (RegistryKey key = HiveFor(op.A).OpenSubKey(op.B, true))
                        {
                            if (key == null) { failed++; continue; }
                            key.DeleteValue(op.C, false);
                        }
                    }
                    else
                    {
                        HiveFor(op.A).DeleteSubKeyTree(op.B, false);
                    }
                }
                catch { failed++; }
            }
            return failed;
        }

        // Writes the job next to the app's own data, launches the elevated
        // instance and waits for it. Returns false when UAC was declined, the
        // helper could not start, or it reported failures — the caller must not
        // claim success it did not get.
        public static bool RunElevated(string jobContent, string folder)
        {
            string jobPath = null;
            try
            {
                string dir = string.IsNullOrEmpty(folder) ? Path.GetTempPath() : folder;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                jobPath = Path.Combine(dir, "job-" + Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllText(jobPath, jobContent, Encoding.UTF8);

                var psi = new ProcessStartInfo(Application.ExecutablePath,
                    "--run-job \"" + jobPath + "\"");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return false;
                    p.WaitForExit(120000);
                    return p.HasExited && p.ExitCode == 0;
                }
            }
            catch { return false; } // declined UAC throws Win32Exception
            finally
            {
                // The helper deletes the file itself; this covers the case where
                // it never ran at all.
                try { if (jobPath != null && File.Exists(jobPath)) File.Delete(jobPath); }
                catch { }
            }
        }
    }
}
