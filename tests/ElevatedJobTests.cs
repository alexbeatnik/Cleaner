// The job-file format. This file is written by a normal-privilege process and
// executed by an elevated one, so every rule about what it may contain is
// load-bearing rather than cosmetic.
using System;
using System.Collections.Generic;
using System.IO;

namespace WindowsStalker.Tests
{
    static class ElevatedJobTests
    {
        static string Job(params string[] lines)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(ElevatedJob.Header);
            foreach (string line in lines) sb.AppendLine(line);
            return sb.ToString();
        }

        public static void TestWrongHeaderExecutesNothing()
        {
            List<JobOp> ops = ElevatedJob.Parse("# something else\r\nDEL|C:\\Windows\\Temp\\x");
            Assert.Equal(0, ops.Count,
                "a file that is not ours must produce no operations at all, not a best-effort guess");
        }

        public static void TestEmptyInputIsHarmless()
        {
            Assert.Equal(0, ElevatedJob.Parse(null).Count, "null");
            Assert.Equal(0, ElevatedJob.Parse("").Count, "empty");
        }

        public static void TestDeleteLineParses()
        {
            List<JobOp> ops = ElevatedJob.Parse(Job(@"DEL|C:\Windows\Temp\somefile.tmp"));
            Assert.Equal(1, ops.Count, "one operation");
            Assert.Equal(JobOpKind.Delete, ops[0].Kind, "a delete");
            Assert.Equal(@"C:\Windows\Temp\somefile.tmp", ops[0].A, "the full path, verbatim");
        }

        public static void TestPathsWithSpacesSurvive()
        {
            List<JobOp> ops = ElevatedJob.Parse(Job(@"DEL|C:\Windows\Temp\a folder with spaces\x.tmp"));
            Assert.Equal(1, ops.Count, "spaces are not a delimiter");
            Assert.Equal(@"C:\Windows\Temp\a folder with spaces\x.tmp", ops[0].A, "kept intact");
        }

        // The elevated process re-runs the same guard the normal delete uses, so a
        // tampered job file cannot talk it into deleting the profile.
        public static void TestProtectedPathsAreRejected()
        {
            Assert.Equal(0, ElevatedJob.Parse(Job(@"DEL|C:\")).Count, "a drive root");
            Assert.Equal(0, ElevatedJob.Parse(Job(@"DEL|" +
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))).Count, "Documents");
            Assert.Equal(0, ElevatedJob.Parse(Job(@"DEL|relative\path")).Count, "a relative path");
        }

        public static void TestRegistryValueLineParses()
        {
            List<JobOp> ops = ElevatedJob.Parse(Job(@"REGVAL|HKLM|SOFTWARE\Vendor\Product|Stale"));
            Assert.Equal(1, ops.Count, "one operation");
            Assert.Equal(JobOpKind.RegDeleteValue, ops[0].Kind, "a value delete");
            Assert.Equal("HKLM", ops[0].A, "hive");
            Assert.Equal(@"SOFTWARE\Vendor\Product", ops[0].B, "key");
            Assert.Equal("Stale", ops[0].C, "value name");
        }

        public static void TestRegistryKeyLineParses()
        {
            List<JobOp> ops = ElevatedJob.Parse(Job(@"REGKEY|HKCU|Software\Vendor\Dead"));
            Assert.Equal(1, ops.Count, "one operation");
            Assert.Equal(JobOpKind.RegDeleteKey, ops[0].Kind, "a key delete");
        }

        public static void TestUnknownHiveIsRejected()
        {
            Assert.Equal(0, ElevatedJob.Parse(Job(@"REGKEY|HKEY_USERS|Software\X\Y")).Count,
                "only the hives the scanners use are accepted");
        }

        public static void TestHiveRootCannotBeDeleted()
        {
            Assert.Equal(0, ElevatedJob.Parse(Job("REGKEY|HKLM|SOFTWARE")).Count,
                "a top-level key is refused: the format only ever removes leaf entries");
            Assert.Equal(0, ElevatedJob.Parse(Job("REGKEY|HKLM|")).Count, "an empty key path");
        }

        public static void TestTraversalIsRejected()
        {
            Assert.Equal(0, ElevatedJob.Parse(Job(@"REGKEY|HKCU|Software\..\..\X")).Count,
                "relative segments have no business in a registry path");
        }

        public static void TestMalformedLinesAreSkippedNotGuessed()
        {
            List<JobOp> ops = ElevatedJob.Parse(Job(
                "nonsense",
                "DEL",
                @"REGVAL|HKCU|Software\Vendor\Product",       // no value name
                @"DEL|C:\Windows\Temp\good.tmp"));
            Assert.Equal(1, ops.Count, "only the well-formed line survives");
            Assert.Equal(@"C:\Windows\Temp\good.tmp", ops[0].A, "and it is the right one");
        }

        public static void TestCommentsAndBlanksAreIgnored()
        {
            List<JobOp> ops = ElevatedJob.Parse(Job("", "# a note", @"DEL|C:\Windows\Temp\x.tmp", ""));
            Assert.Equal(1, ops.Count, "blank lines and comments do not become operations");
        }

        public static void TestBuildDeleteJobRoundTrips()
        {
            var paths = new List<string> { @"C:\Windows\Temp\a", @"C:\Windows\Temp\b" };
            List<JobOp> ops = ElevatedJob.Parse(ElevatedJob.BuildDeleteJob(paths));
            Assert.Equal(2, ops.Count, "what is written is what is read back");
            Assert.Equal(@"C:\Windows\Temp\a", ops[0].A, "first");
            Assert.Equal(@"C:\Windows\Temp\b", ops[1].A, "second");
        }

        public static void TestBuildRegistryJobRoundTrips()
        {
            var issue = new RegIssue();
            issue.HiveName = "HKLM";
            issue.Key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs";
            issue.ValueName = @"C:\gone\x.dll";
            issue.DeleteWholeKey = false;
            List<JobOp> ops = ElevatedJob.Parse(ElevatedJob.BuildRegistryJob(new List<RegIssue> { issue }));
            Assert.Equal(1, ops.Count, "one operation");
            Assert.Equal(JobOpKind.RegDeleteValue, ops[0].Kind, "a value delete");
            Assert.Equal(@"C:\gone\x.dll", ops[0].C, "the value name survives the path characters in it");
        }

        public static void TestRunOnMissingFileIsAFailureNotACrash()
        {
            Assert.Equal(1, ElevatedJob.Run(Path.Combine(Path.GetTempPath(), "no-such-job-file.txt")),
                "a missing job file reports failure through the exit code");
            Assert.Equal(1, ElevatedJob.Run(null), "so does no file at all");
        }

        // The job file is single-use: it is deleted before the first operation
        // runs, so a crash mid-job cannot leave a replayable elevated script.
        public static void TestRunConsumesTheJobFile()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("job.txt");
                File.WriteAllText(path, Job(@"DEL|C:\Windows\Temp\wincleaner-no-such-file.tmp"));
                ElevatedJob.Run(path);
                Assert.False(File.Exists(path), "the job file is removed as it is read");
            }
        }
    }
}
