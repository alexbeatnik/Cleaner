// Catalog invariants. These are property tests over whatever src/CleanRules.cs
// resolved on the machine running them, so a new rule entry is checked the
// moment it is added rather than needing a test of its own.
using System;
using System.Collections.Generic;
using System.IO;

namespace WindowsStalker.Tests
{
    static class RulesTests
    {
        static List<CleanRule> Catalog() { return Rules.Build(); }

        public static void TestCatalogIsNotEmpty()
        {
            // %TEMP% and the Recycle Bin exist on every Windows machine, so the
            // catalog can never legitimately come back empty.
            Assert.True(Catalog().Count >= 2, "the catalog resolved to nothing at all");
        }

        public static void TestRuleIdsAreUnique()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CleanRule rule in Catalog())
                Assert.True(seen.Add(rule.Id), "duplicate rule id: " + rule.Id
                    + " — ids are settings keys, so a clash silently ties two rules together");
        }

        public static void TestEveryRuleHasTranslatedStrings()
        {
            foreach (CleanRule rule in Catalog())
            {
                Assert.False(string.IsNullOrEmpty(rule.Id), "a rule without an id");
                Assert.True(Lang.Raw(Lang.Language.English, rule.NameKey) != null,
                    "rule " + rule.Id + " has no English name for key " + rule.NameKey);
                Assert.True(Lang.Raw(Lang.Language.Ukrainian, rule.NameKey) != null,
                    "rule " + rule.Id + " has no Ukrainian name for key " + rule.NameKey);
                Assert.True(Lang.Raw(Lang.Language.English, rule.GroupKey) != null,
                    "rule " + rule.Id + " sits in an unnamed group " + rule.GroupKey);
            }
        }

        // The guard in Util is the last line of defence; this makes sure the
        // catalog never even points at something it would have to refuse.
        public static void TestNoRuleRootIsProtected()
        {
            foreach (CleanRule rule in Catalog())
                foreach (string root in rule.Roots)
                    Assert.False(Util.IsProtectedPath(root),
                        "rule " + rule.Id + " points at a protected folder: " + root);
        }

        public static void TestEveryResolvedRootExists()
        {
            foreach (CleanRule rule in Catalog())
                foreach (string root in rule.Roots)
                    Assert.True(Directory.Exists(root),
                        "rule " + rule.Id + " kept a root that is not on this machine: " + root);
        }

        public static void TestOnlyGlobRulesCarryMasks()
        {
            foreach (CleanRule rule in Catalog())
            {
                if (rule.Kind == RuleKind.Glob)
                    Assert.True(rule.Masks != null && rule.Masks.Length > 0,
                        "glob rule " + rule.Id + " has no masks, so it would match nothing");
                else
                    Assert.True(rule.Masks == null,
                        "rule " + rule.Id + " carries masks its kind never reads");
            }
        }

        public static void TestRiskyRulesAreNeverOnByDefault()
        {
            foreach (CleanRule rule in Catalog())
                if (rule.Risky)
                    Assert.False(rule.DefaultOn,
                        "rule " + rule.Id + " loses user data and is ticked by default");
        }

        public static void TestNonRecycleBinRulesAlwaysHaveRoots()
        {
            foreach (CleanRule rule in Catalog())
                if (rule.Kind != RuleKind.RecycleBin)
                    Assert.True(rule.Roots.Count > 0,
                        "rule " + rule.Id + " was kept in the catalog with nowhere to look");
        }

        public static void TestRecycleBinRuleIsPresent()
        {
            bool found = false;
            foreach (CleanRule rule in Catalog())
                if (rule.Kind == RuleKind.RecycleBin) found = true;
            Assert.True(found, "the Recycle Bin rule must survive catalog building — it has no folder to verify");
        }

        public static void TestChromiumProfilesIgnoresMissingFolders()
        {
            Assert.Equal(0, Rules.ChromiumProfiles(@"%LOCALAPPDATA%\WindowsStalkerNoSuchBrowser\User Data").Count,
                "a browser that is not installed contributes nothing");
        }

        // The auto-clean path only ever ticks the recommended set, so that set has
        // to be free of anything the user would be upset to lose.
        public static void TestRecommendedSetIsSafe()
        {
            foreach (CleanRule rule in Catalog())
                if (rule.DefaultOn && !rule.Risky && rule.Kind == RuleKind.Folder)
                    foreach (string root in rule.Roots)
                        Assert.False(Util.IsUnder(
                            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), root),
                            "recommended rule " + rule.Id + " would sweep Documents");
        }
    }
}
