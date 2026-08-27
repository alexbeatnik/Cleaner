// Table-wide property tests over the string table: every key is checked, so a
// new string cannot ship half-translated or with mismatched placeholders.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Cleaner.Tests
{
    static class LangTests
    {
        public static void TestBothLanguagesHaveTheSameKeys()
        {
            Assert.Equal(Lang.CountFor(Lang.Language.English), Lang.CountFor(Lang.Language.Ukrainian),
                "the two tables have drifted apart");
            foreach (string key in Lang.AllKeys())
                Assert.True(Lang.Raw(Lang.Language.Ukrainian, key) != null,
                    "no Ukrainian string for key: " + key);
        }

        public static void TestNoEmptyStrings()
        {
            foreach (string key in Lang.AllKeys())
            {
                Assert.False(string.IsNullOrWhiteSpace(Lang.Raw(Lang.Language.English, key)),
                    "empty English string for key: " + key);
                Assert.False(string.IsNullOrWhiteSpace(Lang.Raw(Lang.Language.Ukrainian, key)),
                    "empty Ukrainian string for key: " + key);
            }
        }

        // A translation that drops a {0} turns string.Format into an exception at
        // the worst possible moment — mid-clean, in the other language.
        public static void TestPlaceholdersMatchAcrossLanguages()
        {
            foreach (string key in Lang.AllKeys())
            {
                HashSet<string> en = Placeholders(Lang.Raw(Lang.Language.English, key));
                HashSet<string> uk = Placeholders(Lang.Raw(Lang.Language.Ukrainian, key));
                Assert.True(en.SetEquals(uk),
                    "placeholders differ for key " + key + ": {" + string.Join(",", ToArray(en))
                    + "} vs {" + string.Join(",", ToArray(uk)) + "}");
            }
        }

        // Placeholders must be a contiguous 0..n-1 run, or string.Format throws
        // on the missing index no matter which language is active.
        public static void TestPlaceholderIndexesAreContiguous()
        {
            foreach (string key in Lang.AllKeys())
            {
                HashSet<string> used = Placeholders(Lang.Raw(Lang.Language.English, key));
                for (int i = 0; i < used.Count; i++)
                    Assert.True(used.Contains(i.ToString()),
                        "key " + key + " skips placeholder {" + i + "}");
            }
        }

        public static void TestFallsBackToEnglishThenToTheKey()
        {
            Lang.Language original = Lang.Current;
            try
            {
                Lang.Current = Lang.Language.Ukrainian;
                Assert.Equal("no.such.key.anywhere", Lang.T("no.such.key.anywhere"),
                    "an unknown key resolves to itself rather than to empty text");
                Assert.False(Lang.T("nav.cleaner") == "nav.cleaner", "a known key resolves");
            }
            finally { Lang.Current = original; }
        }

        public static void TestUkrainianIsActuallyDifferent()
        {
            // Some strings are legitimately identical in both languages (product
            // names, "Windows"), but the bulk must not be copy-pasted English.
            int same = 0, total = 0;
            foreach (string key in Lang.AllKeys())
            {
                total++;
                if (Lang.Raw(Lang.Language.English, key) == Lang.Raw(Lang.Language.Ukrainian, key)) same++;
            }
            Assert.True(same * 4 < total, same + " of " + total + " strings are untranslated");
        }

        static HashSet<string> Placeholders(string text)
        {
            var found = new HashSet<string>();
            if (text == null) return found;
            foreach (Match m in Regex.Matches(text, @"\{(\d+)\}")) found.Add(m.Groups[1].Value);
            return found;
        }

        static string[] ToArray(HashSet<string> set)
        {
            var list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }
    }
}
