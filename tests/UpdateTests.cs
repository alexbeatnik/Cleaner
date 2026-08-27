// The self-updater's two pure decisions — when to check, and whether what the
// API reported is actually newer — plus the cancellation contract the duplicate
// finder relies on to stop mid-hash.
using System;
using System.IO;

namespace Cleaner.Tests
{
    static class UpdateScheduleTests
    {
        static readonly DateTime Now = new DateTime(2026, 8, 24, 12, 0, 0);
        const int Period = 24;

        // Every launch gets one check regardless of when the last one happened:
        // a machine that is only ever switched on for an hour a day would
        // otherwise never come due.
        public static void TestFirstCheckOfALaunchAlwaysFires()
        {
            Assert.True(MainForm.AppUpdateDue(false, Now.AddMinutes(-1), Now, Period),
                "the startup check runs even one minute after the last one");
        }

        public static void TestNeverCheckedIsDue()
        {
            Assert.True(MainForm.AppUpdateDue(true, DateTime.MinValue, Now, Period),
                "no recorded check means check now");
        }

        public static void TestDailyBoundary()
        {
            Assert.False(MainForm.AppUpdateDue(true, Now.AddHours(-23), Now, Period), "23 hours is not a day");
            Assert.True(MainForm.AppUpdateDue(true, Now.AddHours(-24), Now, Period), "24 hours is");
        }

        // Same hazard as the clean scheduler: a restored VM snapshot or a
        // timezone change must not park the check until the clock catches up.
        public static void TestFutureTimestampIsDue()
        {
            Assert.True(MainForm.AppUpdateDue(true, Now.AddDays(2), Now, Period),
                "a timestamp from the future means the clock moved, not that we are early");
        }
    }

    static class UpdateVersionTests
    {
        public static void TestNewerVersionWins()
        {
            Assert.True(MainForm.IsNewerVersion("0.1.1", "0.1.0"), "patch bump");
            Assert.True(MainForm.IsNewerVersion("0.2.0", "0.1.9"), "minor bump");
            Assert.True(MainForm.IsNewerVersion("1.0.0", "0.9.9"), "major bump");
        }

        // The check runs on every launch, so "same version" has to be the common
        // case that does nothing — an off-by-one here is an infinite update loop.
        public static void TestSameOrOlderVersionIsIgnored()
        {
            Assert.False(MainForm.IsNewerVersion("0.1.0", "0.1.0"), "the running build is not an update");
            Assert.False(MainForm.IsNewerVersion("0.0.9", "0.1.0"), "nor is an older one");
        }

        public static void TestUnparseableTagsNeverTriggerADownload()
        {
            Assert.False(MainForm.IsNewerVersion("nightly", "0.1.0"), "a named tag");
            Assert.False(MainForm.IsNewerVersion("0.2.0-beta", "0.1.0"), "a pre-release suffix");
            Assert.False(MainForm.IsNewerVersion("", "0.1.0"), "an empty tag");
            Assert.False(MainForm.IsNewerVersion(null, "0.1.0"), "no tag at all");
        }
    }

    static class HashCancelTests
    {
        // Hashing is where a duplicate scan spends its time, so Stop has to reach
        // inside it: before this the flag was only checked between files and a
        // single large one could hold the walk for minutes.
        public static void TestCancelledHashReturnsNull()
        {
            using (var dir = new TempDir())
            {
                string file = dir.WriteFile("big.bin", 512 * 1024);
                var cancel = new CancelFlag();
                cancel.Cancel();
                Assert.Equal(null, Util.HashFile(file, 0, cancel),
                    "a cancelled hash reports nothing rather than a partial digest");
            }
        }

        public static void TestUncancelledHashStillMatchesTheOldBehaviour()
        {
            using (var dir = new TempDir())
            {
                string file = dir.File("a.bin");
                var content = new byte[200000];
                for (int i = 0; i < content.Length; i++) content[i] = (byte)(i % 251);
                File.WriteAllBytes(file, content);

                var live = new CancelFlag();
                Assert.Equal(Util.HashFile(file, 0), Util.HashFile(file, 0, live),
                    "the chunked full hash is the same digest as before");
                // Two files sharing a 64 KB prefix but nothing after it are what
                // the cheap first pass exists to keep apart from the full pass.
                string prefixOnly = Util.HashFile(file, 65536, live);
                Assert.False(prefixOnly == Util.HashFile(file, 0, live),
                    "a prefix hash is not the whole-file hash");
                Assert.Equal(prefixOnly, Util.HashFile(file, 65536, live), "and it is stable");
            }
        }

        // Nothing in the app passes a flag to the two-argument overload, and the
        // rules engine still calls it — a null must mean "cannot be cancelled".
        public static void TestNullFlagIsAllowed()
        {
            using (var dir = new TempDir())
            {
                string file = dir.WriteFile("x.bin", 4096);
                Assert.True(Util.HashFile(file, 0, null) != null, "a null flag hashes normally");
            }
        }
    }
}
