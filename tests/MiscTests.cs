// The remaining testable seams: the auto-clean schedule, the startup approval
// blob, the uninstall-string splitter, the scan session's arithmetic, and the
// icon container the build embeds.
using System;
using System.Collections.Generic;

namespace Cleaner.Tests
{
    static class ScheduleTests
    {
        static readonly DateTime Now = new DateTime(2026, 8, 23, 12, 0, 0);

        public static void TestOffIsNeverDue()
        {
            Assert.False(MainForm.AutoCleanDue(DateTime.MinValue, Now, 0), "mode 0 means off, always");
        }

        public static void TestNeverCleanedIsDue()
        {
            Assert.True(MainForm.AutoCleanDue(DateTime.MinValue, Now, 1), "daily, never run");
            Assert.True(MainForm.AutoCleanDue(DateTime.MinValue, Now, 2), "weekly, never run");
        }

        public static void TestDailyBoundary()
        {
            Assert.False(MainForm.AutoCleanDue(Now.AddHours(-23), Now, 1), "23 hours is not a day");
            Assert.True(MainForm.AutoCleanDue(Now.AddHours(-24), Now, 1), "24 hours is");
        }

        public static void TestWeeklyBoundary()
        {
            Assert.False(MainForm.AutoCleanDue(Now.AddDays(-6), Now, 2), "six days is not a week");
            Assert.True(MainForm.AutoCleanDue(Now.AddDays(-7), Now, 2), "seven days is");
        }

        // A clock that moved backwards (a timezone change, a restored VM snapshot)
        // must not park the scheduler forever waiting for a date in the past.
        public static void TestFutureTimestampIsDue()
        {
            Assert.True(MainForm.AutoCleanDue(Now.AddDays(3), Now, 2),
                "a timestamp from the future means the clock changed, not that we are early");
        }
    }

    static class StartupApprovalTests
    {
        public static void TestEnabledBlobShape()
        {
            byte[] blob = MainForm.ApprovedBlob(true);
            Assert.Equal(12, blob.Length, "Task Manager writes twelve bytes");
            Assert.Equal((byte)0x02, blob[0], "0x02 is the enabled state");
            for (int i = 1; i < blob.Length; i++)
                Assert.Equal((byte)0, blob[i], "enabling carries no timestamp, byte " + i);
        }

        public static void TestDisabledBlobShape()
        {
            byte[] blob = MainForm.ApprovedBlob(false);
            Assert.Equal(12, blob.Length, "same length");
            Assert.Equal((byte)0x03, blob[0], "0x03 is the disabled state");
            bool anyStamp = false;
            for (int i = 4; i < 12; i++) if (blob[i] != 0) anyStamp = true;
            Assert.True(anyStamp, "disabling records when it happened, as Task Manager does");
        }

        // The reader treats an odd low bit as disabled, which is the convention
        // both states above have to satisfy.
        public static void TestStateBitsAgreeWithTheReader()
        {
            Assert.Equal(0, MainForm.ApprovedBlob(true)[0] & 1, "enabled is even");
            Assert.Equal(1, MainForm.ApprovedBlob(false)[0] & 1, "disabled is odd");
        }
    }

    static class UninstallCommandTests
    {
        public static void TestRegistryUninstallNeedsTwoMissingTargets()
        {
            string gone = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "CleanerMissing-" + Guid.NewGuid().ToString("N"));
            Assert.False(MainForm.IsStaleUninstallEntry(null, gone + ".exe"),
                "an absent installation path is not evidence");
            Assert.False(MainForm.IsStaleUninstallEntry(gone, null),
                "an absent uninstall command is not evidence");
            Assert.True(MainForm.IsStaleUninstallEntry(gone, gone + ".exe"),
                "both explicit targets are gone");
        }

        public static void TestQuotedCommandSplits()
        {
            string exe, args;
            MainForm.SplitCommand("\"C:\\Program Files\\App\\unins000.exe\" /SILENT", out exe, out args);
            Assert.Equal(@"C:\Program Files\App\unins000.exe", exe, "the executable");
            Assert.Equal("/SILENT", args, "the arguments");
        }

        public static void TestMsiExecCommandSplits()
        {
            string exe, args;
            MainForm.SplitCommand("MsiExec.exe /X{0A1B2C3D-0000-0000-0000-000000000000}", out exe, out args);
            Assert.Equal("MsiExec.exe", exe, "the executable");
            Assert.Equal("/X{0A1B2C3D-0000-0000-0000-000000000000}", args, "the product code");
        }

        public static void TestBareCommandHasNoArguments()
        {
            string exe, args;
            MainForm.SplitCommand(@"C:\App\uninstall.exe", out exe, out args);
            Assert.Equal(@"C:\App\uninstall.exe", exe, "the executable");
            Assert.Equal("", args, "nothing else");
        }

        public static void TestEmptyCommandIsHandled()
        {
            string exe, args;
            MainForm.SplitCommand(null, out exe, out args);
            Assert.Equal("", exe, "no executable");
            Assert.Equal("", args, "no arguments");
        }

        public static void TestInstallDateParsing()
        {
            Assert.Equal(new DateTime(2026, 3, 14), MainForm.ParseInstallDate("20260314"), "the documented form");
            Assert.Equal(DateTime.MinValue, MainForm.ParseInstallDate("14.03.2026"), "some installers write junk");
            Assert.Equal(DateTime.MinValue, MainForm.ParseInstallDate(null), "and some write nothing");
            Assert.Equal(DateTime.MinValue, MainForm.ParseInstallDate("20261340"), "month 13, day 40");
        }

        // The version column is sortable, and DisplayVersion is whatever the
        // installer felt like writing — the comparison has to order the numeric
        // case properly and survive everything else.
        public static void TestVersionOrdering()
        {
            Assert.True(MainForm.CompareVersions("1.9", "1.10") < 0, "1.10 is newer than 1.9, not older");
            Assert.True(MainForm.CompareVersions("2.0", "10.0") < 0, "and 10 is newer than 2");
            Assert.Equal(0, MainForm.CompareVersions("1.2.3", "1.2.3"), "identical versions tie");
            Assert.True(MainForm.CompareVersions("1.2", "1.2.1") < 0, "a missing part counts as lower");
            Assert.True(MainForm.CompareVersions("1.0.0-beta", "1.0.0") > 0,
                "a non-numeric part falls back to text rather than throwing");
            Assert.Equal(0, MainForm.CompareVersions(null, ""), "no version at all is not a crash");
        }
    }

    static class CleanScanTests
    {
        static RuleResult Result(long bytes, int files, bool selected, bool blocked)
        {
            var rule = new CleanRule();
            rule.Id = "test"; rule.NameKey = "rule.userTemp"; rule.GroupKey = "grp.windows";
            var rr = new RuleResult();
            rr.Rule = rule;
            rr.Bytes = bytes; rr.Files = files;
            rr.Selected = selected; rr.Blocked = blocked;
            rr.Entries.Add(@"C:\Windows\Temp\x");
            return rr;
        }

        public static void TestTotalsCountEverythingMeasured()
        {
            var scan = new CleanScan();
            scan.Results.Add(Result(100, 1, true, false));
            scan.Results.Add(Result(200, 2, false, false));
            Assert.Equal(300L, scan.TotalBytes, "the total is what the analysis found");
            Assert.Equal(3, scan.TotalFiles, "including unticked rules");
        }

        // The number on the Clean button has to be a promise, not an estimate:
        // unticked rules and rules blocked by a running process are excluded.
        public static void TestSelectedTotalsExcludeUntickedAndBlocked()
        {
            var scan = new CleanScan();
            scan.Results.Add(Result(100, 1, true, false));
            scan.Results.Add(Result(200, 2, false, false));
            scan.Results.Add(Result(400, 4, true, true));
            Assert.Equal(100L, scan.SelectedBytes, "only the ticked, unblocked rule");
            Assert.Equal(1, scan.SelectedFiles, "same for the file count");
        }

        public static void TestEmptyResultIsRecognised()
        {
            var rr = new RuleResult();
            rr.Rule = new CleanRule();
            Assert.True(rr.Empty, "a result with nothing in it");
            rr.Entries.Add(@"C:\Windows\Temp\x");
            Assert.False(rr.Empty, "one entry is enough to make it real");
        }
    }

    static class IconTests
    {
        public static void TestIcoHeaderDescribesEveryImage()
        {
            int[] sizes = { 16, 32, 256 };
            byte[] ico = Brand.IcoBytes(sizes);
            Assert.Equal((short)0, BitConverter.ToInt16(ico, 0), "reserved field");
            Assert.Equal((short)1, BitConverter.ToInt16(ico, 2), "type 1 = icon");
            Assert.Equal((short)sizes.Length, BitConverter.ToInt16(ico, 4), "one directory entry per size");

            for (int i = 0; i < sizes.Length; i++)
            {
                int entry = 6 + 16 * i;
                byte width = ico[entry];
                Assert.Equal(sizes[i] >= 256 ? (byte)0 : (byte)sizes[i], width,
                    "256 is stored as 0 in the one-byte width field, size " + sizes[i]);
                int length = BitConverter.ToInt32(ico, entry + 8);
                int offset = BitConverter.ToInt32(ico, entry + 12);
                Assert.True(length > 0, "image " + i + " has data");
                Assert.True(offset + length <= ico.Length, "image " + i + " lies inside the file");
                // Each entry is a PNG, which every Windows this runs on accepts
                Assert.Equal((byte)0x89, ico[offset], "PNG signature byte for image " + i);
            }
        }

        public static void TestAppIconIsUsable()
        {
            Assert.True(Brand.AppIcon != null, "the tray and title bar need a real icon");
        }
    }
}
