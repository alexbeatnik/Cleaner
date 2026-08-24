// Tests for the pure helpers in src/Util.cs — the formatting, the path guard
// that stands between this app and someone's Documents folder, and the
// command-line parsing the registry and startup scanners are built on.
using System;
using System.IO;

namespace WindowsStalker.Tests
{
    static class UtilTests
    {
        public static void TestFormatSizeUnits()
        {
            Assert.Equal("0 B", Util.FormatSize(0), "zero");
            Assert.Equal("512 B", Util.FormatSize(512), "bytes stay bytes");
            Assert.Equal("1.00 KB", Util.FormatSize(1024), "one kilobyte");
            Assert.Equal("1.50 KB", Util.FormatSize(1536), "one and a half kilobytes");
            Assert.Equal("1.00 MB", Util.FormatSize(1024 * 1024), "one megabyte");
            Assert.Equal("1.00 GB", Util.FormatSize(1024L * 1024 * 1024), "one gigabyte");
        }

        // Three significant figures throughout: 999 MB must not render as 999.00
        public static void TestFormatSizePrecisionTiers()
        {
            Assert.Equal("10.0 MB", Util.FormatSize(10L * 1024 * 1024), "two-digit values get one decimal");
            Assert.Equal("100 MB", Util.FormatSize(100L * 1024 * 1024), "three-digit values get none");
        }

        public static void TestFormatSizeNegativeIsZero()
        {
            Assert.Equal("0 B", Util.FormatSize(-5), "a negative size is a bug elsewhere, not a crash here");
        }

        public static void TestTryParseTicksRejectsGarbage()
        {
            DateTime value;
            Assert.False(Util.TryParseTicks("not-a-number", out value), "text is not a timestamp");
            Assert.False(Util.TryParseTicks("-1", out value), "before DateTime.MinValue");
            Assert.False(Util.TryParseTicks("999999999999999999999", out value), "overflows long");
            Assert.True(Util.TryParseTicks(new DateTime(2026, 1, 2).Ticks.ToString(), out value), "a real timestamp");
            Assert.Equal(new DateTime(2026, 1, 2), value, "round-trips");
        }

        public static void TestExpandPathDropsUnsetVariables()
        {
            Assert.True(Util.ExpandPath(@"%TEMP%\x") != null, "TEMP is always set");
            Assert.Equal(null, Util.ExpandPath(@"%WINCLEANER_NOT_SET_ANYWHERE%\x"),
                "an unset variable makes the whole rule drop out");
            Assert.Equal(null, Util.ExpandPath(""), "empty input");
        }

        public static void TestNormalizeKeepsDriveRootSlash()
        {
            Assert.Equal(@"C:\", Util.Normalize(@"C:\"), "a drive root keeps its backslash");
            Assert.Equal(@"C:\Windows", Util.Normalize(@"C:\Windows\"), "everything else loses it");
            Assert.Equal(null, Util.Normalize("   "), "whitespace is not a path");
        }

        public static void TestIsUnder()
        {
            Assert.True(Util.IsUnder(@"C:\a\b\c", @"C:\a"), "a descendant");
            Assert.True(Util.IsUnder(@"C:\a", @"C:\a"), "the folder itself");
            Assert.True(Util.IsUnder(@"C:\A\B", @"c:\a"), "case does not matter on Windows");
            Assert.False(Util.IsUnder(@"C:\ab", @"C:\a"), "a name prefix is not containment");
            Assert.False(Util.IsUnder(@"D:\a\b", @"C:\a"), "different drives");
        }

        public static void TestIsDriveRoot()
        {
            Assert.True(Util.IsDriveRoot(@"C:\"), "C:\\ is a root");
            Assert.False(Util.IsDriveRoot(@"C:\Windows"), "a folder is not");
        }

        // The single most important test in the project: these are the paths a
        // bug in a rule could otherwise hand to the delete routine.
        public static void TestProtectedPathsRefuseTheObviousDisasters()
        {
            Assert.True(Util.IsProtectedPath(@"C:\"), "drive root");
            Assert.True(Util.IsProtectedPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows)),
                "the Windows folder");
            Assert.True(Util.IsProtectedPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
                "the profile root");
            Assert.True(Util.IsProtectedPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                "Documents");
            Assert.True(Util.IsProtectedPath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
                "the Desktop");
            Assert.True(Util.IsProtectedPath(@"C:\Users"), "an ancestor of the profile");
            Assert.True(Util.IsProtectedPath(""), "empty means refuse");
            Assert.True(Util.IsProtectedPath(@"\\server\share"), "a UNC share root");
        }

        public static void TestProtectedPathAllowsRealJunkFolders()
        {
            string temp = Path.Combine(Path.GetTempPath(), "wincleaner-probe");
            Assert.False(Util.IsProtectedPath(temp), "a folder inside TEMP is fair game");
            Assert.False(Util.IsProtectedPath(@"C:\Windows\Temp\abc"), "so is one inside Windows\\Temp");
        }

        public static void TestExeFromCommandLine()
        {
            Assert.Equal(@"C:\Program Files\App\app.exe",
                Util.ExeFromCommandLine("\"C:\\Program Files\\App\\app.exe\" --flag"), "quoted with arguments");
            Assert.Equal(@"C:\Windows\notepad.exe",
                Util.ExeFromCommandLine(@"C:\Windows\notepad.exe /x"), "unquoted with arguments");
            Assert.Equal(@"C:\Program Files\a b\x.exe",
                Util.ExeFromCommandLine(@"C:\Program Files\a b\x.exe -q"), "unquoted with spaces in the path");
            Assert.Equal(null, Util.ExeFromCommandLine("   "), "nothing to parse");
        }

        public static void TestTargetMissingResolvesBareSystemNames()
        {
            Assert.False(Util.TargetMissing("notepad.exe"), "a System32 name is not missing");
            Assert.False(Util.TargetMissing("\"" + Environment.GetFolderPath(Environment.SpecialFolder.System)
                + "\\notepad.exe\" /a"), "a real quoted path with arguments");
            Assert.True(Util.TargetMissing(@"C:\definitely\not\here\nope.exe"), "a genuinely dead path");
            Assert.False(Util.TargetMissing(""), "nothing to judge is not evidence of a problem");
        }

        public static void TestDirectorySizeCountsTheWholeTree()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile("a.bin", 1000);
                dir.WriteFile(Path.Combine("sub", "b.bin"), 2000);
                dir.WriteFile(Path.Combine("sub", "deep", "c.bin"), 3000);
                int files = 0;
                long size = Util.DirectorySize(dir.Path, ref files, new CancelFlag());
                Assert.Equal(6000L, size, "every file in the tree");
                Assert.Equal(3, files, "counted once each");
            }
        }

        public static void TestDirectorySizeStopsWhenCancelled()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile("a.bin", 1000);
                var cancel = new CancelFlag();
                cancel.Cancel();
                int files = 0;
                Assert.Equal(0L, Util.DirectorySize(dir.Path, ref files, cancel),
                    "an already-cancelled walk does nothing");
            }
        }

        public static void TestDeleteEntryRemovesTreesAndReportsBytes()
        {
            using (var dir = new TempDir())
            {
                string victim = Path.Combine(dir.Path, "victim");
                Directory.CreateDirectory(victim);
                File.WriteAllBytes(Path.Combine(victim, "a.bin"), new byte[1500]);
                Directory.CreateDirectory(Path.Combine(victim, "nested"));
                File.WriteAllBytes(Path.Combine(victim, "nested", "b.bin"), new byte[500]);

                long bytes = 0; int files = 0, failed = 0;
                Util.DeleteEntry(victim, ref bytes, ref files, ref failed);
                Assert.Equal(2000L, bytes, "freed bytes are counted as files go");
                Assert.Equal(2, files, "both files");
                Assert.Equal(0, failed, "nothing was locked");
                Assert.False(Directory.Exists(victim), "the tree is gone");
            }
        }

        public static void TestDeleteEntryClearsReadOnly()
        {
            using (var dir = new TempDir())
            {
                string file = dir.WriteFile("locked.bin", 100);
                File.SetAttributes(file, FileAttributes.ReadOnly);
                long bytes = 0; int files = 0, failed = 0;
                Util.DeleteEntry(file, ref bytes, ref files, ref failed);
                Assert.Equal(0, failed, "a read-only attribute is not a lock");
                Assert.False(File.Exists(file), "deleted anyway");
            }
        }

        // The guard is enforced inside DeleteEntry too, not only by its callers —
        // that is what makes it a guard rather than a convention.
        public static void TestDeleteEntryRefusesProtectedPaths()
        {
            long bytes = 0; int files = 0, failed = 0;
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            Util.DeleteEntry(documents, ref bytes, ref files, ref failed);
            Assert.Equal(1, failed, "refused");
            Assert.Equal(0L, bytes, "and nothing was touched");
            Assert.True(Directory.Exists(documents), "Documents is still there");
        }

        public static void TestHashFileIsStableAndPrefixSensitive()
        {
            using (var dir = new TempDir())
            {
                string a = dir.File("a.bin"), b = dir.File("b.bin");
                var content = new byte[1000];
                for (int i = 0; i < content.Length; i++) content[i] = (byte)(i % 251);
                File.WriteAllBytes(a, content);
                File.WriteAllBytes(b, content);
                Assert.Equal(Util.HashFile(a, 0), Util.HashFile(b, 0), "identical files hash the same");

                content[10] = 0xFF;
                File.WriteAllBytes(b, content);
                Assert.False(Util.HashFile(a, 0) == Util.HashFile(b, 0), "one changed byte changes the hash");
            }
        }

        public static void TestHashFileMissingReturnsNull()
        {
            Assert.Equal(null, Util.HashFile(@"C:\definitely\not\here.bin", 0),
                "an unreadable file must never be called a duplicate");
        }

        // The age floor is what keeps the temp rules from deleting scratch space
        // a running program is still writing into.
        public static void TestIsRecentHonoursTheAgeFloor()
        {
            using (var dir = new TempDir())
            {
                string file = dir.WriteFile("fresh.bin", 10);
                DateTime now = DateTime.UtcNow;
                Assert.True(Util.IsRecent(file, 24, now), "a file written just now is recent");
                Assert.False(Util.IsRecent(file, 0, now), "a rule with no floor never skips anything");

                File.SetLastWriteTimeUtc(file, now.AddDays(-3));
                File.SetCreationTimeUtc(file, now.AddDays(-3));
                Assert.False(Util.IsRecent(file, 24, now), "three days old is past a one-day floor");
                Assert.True(Util.IsRecent(file, 24 * 7, now), "but not past a one-week floor");
            }
        }

        public static void TestIsRecentTreatsUnreadableAsRecent()
        {
            Assert.True(Util.IsRecent(@"C:\definitely
ot\here.bin", 24, DateTime.UtcNow),
                "when the timestamp cannot be read, the cautious answer is to leave it alone");
        }

        public static void TestShortenPathKeepsTheFileName()
        {
            string shortened = Util.ShortenPath(@"C:\a\b\c\d\e\f\report.txt", 20);
            Assert.True(shortened.EndsWith("report.txt"), "the file name survives: " + shortened);
            Assert.True(shortened.Length <= 24, "and it actually got shorter: " + shortened);
            Assert.Equal(@"C:\x.txt", Util.ShortenPath(@"C:\x.txt", 20), "a short path is left alone");
        }

        // A width narrower than the ellipsis itself used to run Substring off the
        // end of the file name and throw — which is not something a fixed-width
        // label should be able to do to a running scan's status line.
        public static void TestShortenPathSurvivesAbsurdWidths()
        {
            for (int max = -2; max <= 14; max++)
                Assert.True(Util.ShortenPath(@"C:\a\b\c\d\e\f\report.txt", max) != null,
                    "never throws and never returns null, width " + max);
        }
    }
}
