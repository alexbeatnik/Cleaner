// The junk catalog: what Cleaner knows how to clean, and where it lives.
// Rules are data, not code paths — the analyzer in MainForm.Cleaner.cs walks
// this list, so adding a location means adding one entry here (plus its two
// Lang strings) and nothing else.
using System;
using System.Collections.Generic;
using System.IO;

namespace Cleaner
{
    enum RuleKind
    {
        Folder,      // delete the contents of Roots (the root folders themselves stay)
        Glob,        // delete files in Roots matching Masks
        RecycleBin   // the shell Recycle Bin, sized and emptied through shell32
    }

    sealed class CleanRule
    {
        public string Id;            // stable identifier, persisted in settings.ini
        public string GroupKey;      // Lang key for the group header
        public string NameKey;       // Lang key for the item name
        public string NameArg;       // optional prefix (a browser or app name)
        public bool DefaultOn;       // ticked on a fresh install
        public bool Risky;           // logs you out / loses history — never on by default
        public bool NeedsAdmin;      // the elevated helper has to do this one
        public RuleKind Kind = RuleKind.Folder;
        public readonly List<string> Roots = new List<string>();
        public string[] Masks;       // Glob only
        public bool GlobRecursive;   // Glob only
        // Leave anything touched within this many hours alone. The temp folders
        // are shared scratch space for every running program, and a file written
        // minutes ago usually belongs to something that is still using it.
        public int MinAgeHours;
        public string[] Processes;   // executables that must not be running (no extension)

        public string DisplayName()
        {
            string s = Lang.T(NameKey);
            return string.IsNullOrEmpty(NameArg) ? s : NameArg + " — " + s;
        }
    }

    static class Rules
    {
        // Chromium-family browsers all keep the same cache layout under a
        // "User Data" folder; Opera is the odd one out and points straight at a
        // profile directory. The table drives both the cache and the risky
        // cookie/history rules so a new browser is one line.
        sealed class Chromium
        {
            public string Id, Name, Dir, Process;
            public Chromium(string id, string name, string dir, string process)
            {
                Id = id; Name = name; Dir = dir; Process = process;
            }
        }

        static readonly Chromium[] ChromiumBrowsers =
        {
            new Chromium("chrome",  "Google Chrome", @"%LOCALAPPDATA%\Google\Chrome\User Data",              "chrome"),
            new Chromium("edge",    "Microsoft Edge",@"%LOCALAPPDATA%\Microsoft\Edge\User Data",             "msedge"),
            new Chromium("brave",   "Brave",         @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data","brave"),
            new Chromium("vivaldi", "Vivaldi",       @"%LOCALAPPDATA%\Vivaldi\User Data",                    "vivaldi"),
            new Chromium("yandex",  "Yandex",        @"%LOCALAPPDATA%\Yandex\YandexBrowser\User Data",       "browser"),
            new Chromium("opera",   "Opera",         @"%APPDATA%\Opera Software\Opera Stable",               "opera"),
            new Chromium("operagx", "Opera GX",      @"%APPDATA%\Opera Software\Opera GX Stable",            "opera"),
        };

        // Cache folders inside a Chromium profile. Deleting them costs nothing but
        // a slower first page load; none of them hold credentials or settings.
        static readonly string[] ChromiumCacheDirs =
        {
            "Cache", "Code Cache", "GPUCache", "DawnCache", "GrShaderCache", "ShaderCache",
            "Media Cache", @"Service Worker\CacheStorage", @"Service Worker\ScriptCache",
        };

        // Builds the catalog for THIS machine: a rule whose folders do not exist
        // is dropped, so the cleaner list never offers to clean software that is
        // not installed. Called again after an install/uninstall changes things.
        public static List<CleanRule> Build()
        {
            var rules = new List<CleanRule>();
            AddWindows(rules);
            AddBrowsers(rules);
            AddApps(rules);
            return rules;
        }

        // ---------- Windows ----------

        static void AddWindows(List<CleanRule> rules)
        {
            const string G = "grp.windows";

            // Both temp rules leave the last day alone: %TEMP% is live scratch
            // space for every running program, and deleting a folder an installer
            // or a build is still writing into breaks it.
            CleanRule userTemp = Folder("win.temp", G, "rule.userTemp", true, false, false, @"%TEMP%");
            userTemp.MinAgeHours = 24;
            Add(rules, userTemp);
            CleanRule winTemp = Folder("win.wintemp", G, "rule.winTemp", true, false, true, @"%SystemRoot%\Temp");
            winTemp.MinAgeHours = 24;
            Add(rules, winTemp);

            var bin = new CleanRule();
            bin.Id = "win.recycle"; bin.GroupKey = G; bin.NameKey = "rule.recycleBin";
            bin.DefaultOn = true; bin.Kind = RuleKind.RecycleBin;
            rules.Add(bin); // no root to verify — the shell always has a bin

            Add(rules, Folder("win.crashdumps", G, "rule.crashDumps", true, false, false,
                @"%LOCALAPPDATA%\CrashDumps"));
            Add(rules, Folder("win.wer", G, "rule.errorReports", true, false, false,
                @"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportArchive",
                @"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportQueue",
                @"%LOCALAPPDATA%\Microsoft\Windows\WER\Temp"));
            Add(rules, Folder("win.werSystem", G, "rule.errorReportsSystem", false, false, true,
                @"%ProgramData%\Microsoft\Windows\WER\ReportArchive",
                @"%ProgramData%\Microsoft\Windows\WER\ReportQueue"));

            // Explorer holds these open; the delete usually fails until a restart,
            // which is why the rule is off by default rather than noisily failing.
            var thumbs = Glob("win.thumbcache", G, "rule.thumbnailCache", false, false, false,
                new string[] { "thumbcache_*.db", "iconcache_*.db" },
                @"%LOCALAPPDATA%\Microsoft\Windows\Explorer");
            thumbs.Processes = new string[] { "explorer" };
            Add(rules, thumbs);

            Add(rules, Folder("win.recent", G, "rule.recentDocs", false, true, false,
                @"%APPDATA%\Microsoft\Windows\Recent\AutomaticDestinations",
                @"%APPDATA%\Microsoft\Windows\Recent\CustomDestinations"));
            Add(rules, Folder("win.inetcache", G, "rule.inetCache", true, false, false,
                @"%LOCALAPPDATA%\Microsoft\Windows\INetCache\IE"));
            Add(rules, Folder("win.d3dcache", G, "rule.shaderCache", true, false, false,
                @"%LOCALAPPDATA%\D3DSCache"));
            Add(rules, Folder("win.fontcache", G, "rule.fontCache", false, false, true,
                @"%SystemRoot%\ServiceProfiles\LocalService\AppData\Local\FontCache"));

            // Elevated territory. These are the biggest single wins on a machine
            // that has been through a few feature updates.
            Add(rules, Folder("win.update", G, "rule.updateCache", false, false, true,
                @"%SystemRoot%\SoftwareDistribution\Download"));
            Add(rules, Folder("win.delivery", G, "rule.deliveryOptimization", false, false, true,
                @"%SystemRoot%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"));
            Add(rules, Folder("win.prefetch", G, "rule.prefetch", false, false, true,
                @"%SystemRoot%\Prefetch"));
            Add(rules, Glob("win.memdumps", G, "rule.memoryDumps", false, false, true,
                new string[] { "*.dmp" }, @"%SystemRoot%\Minidump"));
            // Windows\Logs nests one level deep per component, so this glob is the
            // one that has to recurse — everywhere else the junk sits at the top.
            CleanRule logs = Glob("win.logs", G, "rule.windowsLogs", false, false, true,
                new string[] { "*.log", "*.etl" }, @"%SystemRoot%\Logs");
            logs.GlobRecursive = true;
            Add(rules, logs);
        }

        // ---------- browsers ----------

        static void AddBrowsers(List<CleanRule> rules)
        {
            const string G = "grp.browsers";

            foreach (Chromium b in ChromiumBrowsers)
            {
                List<string> profiles = ChromiumProfiles(b.Dir);
                if (profiles.Count == 0) continue;

                var cache = new CleanRule();
                cache.Id = "br." + b.Id + ".cache";
                cache.GroupKey = G; cache.NameKey = "rule.browserCache"; cache.NameArg = b.Name;
                cache.DefaultOn = true;
                cache.Processes = new string[] { b.Process };
                foreach (string p in profiles)
                    foreach (string sub in ChromiumCacheDirs)
                        AddRootIfExists(cache, Path.Combine(p, sub));
                // Root-level GPU caches live beside the profiles, not inside them
                foreach (string sub in new string[] { "ShaderCache", "GrShaderCache", "GraphiteDawnCache" })
                    AddRootIfExists(cache, Path.Combine(Util.ExpandPath(b.Dir) ?? "", sub));
                Add(rules, cache);

                var cookies = new CleanRule();
                cookies.Id = "br." + b.Id + ".cookies";
                cookies.GroupKey = G; cookies.NameKey = "rule.browserCookies"; cookies.NameArg = b.Name;
                cookies.Risky = true; cookies.Kind = RuleKind.Glob;
                cookies.Masks = new string[] { "Cookies", "Cookies-journal" };
                cookies.Processes = new string[] { b.Process };
                foreach (string p in profiles)
                {
                    AddRootIfExists(cookies, Path.Combine(p, "Network"));
                    AddRootIfExists(cookies, p); // pre-2020 layouts kept Cookies in the profile root
                }
                Add(rules, cookies);

                var history = new CleanRule();
                history.Id = "br." + b.Id + ".history";
                history.GroupKey = G; history.NameKey = "rule.browserHistory"; history.NameArg = b.Name;
                history.Risky = true; history.Kind = RuleKind.Glob;
                history.Masks = new string[] { "History", "History-journal", "Visited Links", "Top Sites", "Top Sites-journal" };
                history.Processes = new string[] { b.Process };
                foreach (string p in profiles) AddRootIfExists(history, p);
                Add(rules, history);
            }

            AddFirefox(rules);
        }

        // Firefox keeps the cache under %LOCALAPPDATA% and everything else under
        // %APPDATA%, with profile folders named "<random>.<name>" in both.
        static void AddFirefox(List<CleanRule> rules)
        {
            const string G = "grp.browsers";
            string localProfiles = Util.ExpandPath(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles");
            string roamProfiles = Util.ExpandPath(@"%APPDATA%\Mozilla\Firefox\Profiles");

            var cache = new CleanRule();
            cache.Id = "br.firefox.cache";
            cache.GroupKey = G; cache.NameKey = "rule.browserCache"; cache.NameArg = "Firefox";
            cache.DefaultOn = true;
            cache.Processes = new string[] { "firefox" };
            foreach (string p in SubDirectories(localProfiles))
            {
                AddRootIfExists(cache, Path.Combine(p, "cache2"));
                AddRootIfExists(cache, Path.Combine(p, "startupCache"));
                AddRootIfExists(cache, Path.Combine(p, "shader-cache"));
            }
            Add(rules, cache);

            var cookies = new CleanRule();
            cookies.Id = "br.firefox.cookies";
            cookies.GroupKey = G; cookies.NameKey = "rule.browserCookies"; cookies.NameArg = "Firefox";
            cookies.Risky = true; cookies.Kind = RuleKind.Glob;
            cookies.Masks = new string[] { "cookies.sqlite", "cookies.sqlite-wal", "cookies.sqlite-shm" };
            cookies.Processes = new string[] { "firefox" };
            foreach (string p in SubDirectories(roamProfiles)) AddRootIfExists(cookies, p);
            Add(rules, cookies);

            var history = new CleanRule();
            history.Id = "br.firefox.history";
            history.GroupKey = G; history.NameKey = "rule.browserHistory"; history.NameArg = "Firefox";
            history.Risky = true; history.Kind = RuleKind.Glob;
            // Files only: a Glob rule runs Directory.GetFiles, so a folder name
            // among the masks (sessionstore-backups sat here) never matched
            // anything and only made the rule look as if it did more than it does.
            history.Masks = new string[] { "places.sqlite-wal", "places.sqlite-shm" };
            history.Processes = new string[] { "firefox" };
            foreach (string p in SubDirectories(roamProfiles)) AddRootIfExists(history, p);
            Add(rules, history);
        }

        // ---------- applications ----------

        static void AddApps(List<CleanRule> rules)
        {
            const string G = "grp.apps";

            AddElectronCache(rules, G, "discord", "Discord", @"%APPDATA%\discord", "discord");
            AddElectronCache(rules, G, "slack", "Slack", @"%APPDATA%\Slack", "slack");
            AddElectronCache(rules, G, "teams", "Microsoft Teams",
                @"%LOCALAPPDATA%\Packages\MSTeams_8wekyb3d8bbwe\LocalCache\Microsoft\MSTeams", "ms-teams");
            AddElectronCache(rules, G, "vscode", "Visual Studio Code", @"%APPDATA%\Code", "Code");
            AddElectronCache(rules, G, "spotify", "Spotify", @"%LOCALAPPDATA%\Spotify", "Spotify");

            Add(rules, Folder("app.steam", G, "rule.appCache", true, false, false,
                @"%ProgramFiles(x86)%\Steam\appcache\httpcache")).NameArg = "Steam";
            Add(rules, Folder("app.nvidia", G, "rule.shaderCache", true, false, false,
                @"%LOCALAPPDATA%\NVIDIA\DXCache",
                @"%LOCALAPPDATA%\NVIDIA\GLCache",
                @"%ProgramData%\NVIDIA Corporation\NV_Cache")).NameArg = "NVIDIA";
            Add(rules, Folder("app.adobe", G, "rule.mediaCache", false, false, false,
                @"%APPDATA%\Adobe\Common\Media Cache Files",
                @"%APPDATA%\Adobe\Common\Media Cache")).NameArg = "Adobe";
            Add(rules, Folder("app.java", G, "rule.appCache", true, false, false,
                @"%LOCALAPPDATA%\Sun\Java\Deployment\cache")).NameArg = "Java";
            Add(rules, Folder("app.office", G, "rule.documentCache", false, true, false,
                @"%LOCALAPPDATA%\Microsoft\Office\16.0\OfficeFileCache")).NameArg = "Microsoft Office";

            // Package-manager caches. Big, safe to lose, and painful to find by
            // hand — the single most useful group on a developer's machine.
            const string D = "grp.dev";
            Add(rules, Folder("dev.nuget", D, "rule.packageCache", false, false, false,
                @"%LOCALAPPDATA%\NuGet\v3-cache",
                @"%USERPROFILE%\.nuget\packages\.tools")).NameArg = "NuGet";
            Add(rules, Folder("dev.npm", D, "rule.packageCache", false, false, false,
                @"%LOCALAPPDATA%\npm-cache\_cacache",
                @"%APPDATA%\npm-cache\_cacache")).NameArg = "npm";
            Add(rules, Folder("dev.pip", D, "rule.packageCache", false, false, false,
                @"%LOCALAPPDATA%\pip\cache")).NameArg = "pip";
            Add(rules, Folder("dev.yarn", D, "rule.packageCache", false, false, false,
                @"%LOCALAPPDATA%\Yarn\Cache")).NameArg = "Yarn";
            Add(rules, Folder("dev.gradle", D, "rule.buildCache", false, false, false,
                @"%USERPROFILE%\.gradle\caches\build-cache-1")).NameArg = "Gradle";
            Add(rules, Folder("dev.vsTelemetry", D, "rule.appCache", false, false, false,
                @"%LOCALAPPDATA%\Microsoft\VSApplicationInsights",
                @"%LOCALAPPDATA%\Microsoft\VisualStudio\Packages\_Instances")).NameArg = "Visual Studio";
        }

        // Every Electron app keeps the same four Chromium cache folders; naming
        // them once here is what keeps the app group from becoming boilerplate.
        static void AddElectronCache(List<CleanRule> rules, string group, string id, string name,
                                     string baseDir, string process)
        {
            var r = new CleanRule();
            r.Id = "app." + id;
            r.GroupKey = group; r.NameKey = "rule.appCache"; r.NameArg = name;
            r.DefaultOn = true;
            r.Processes = new string[] { process };
            string root = Util.ExpandPath(baseDir);
            if (root == null) return;
            foreach (string sub in new string[] { "Cache", "Code Cache", "GPUCache", "Crashpad\\reports", "logs" })
                AddRootIfExists(r, Path.Combine(root, sub));
            Add(rules, r);
        }

        // ---------- helpers ----------

        static CleanRule Folder(string id, string group, string nameKey, bool on, bool risky,
                                bool admin, params string[] roots)
        {
            var r = new CleanRule();
            r.Id = id; r.GroupKey = group; r.NameKey = nameKey;
            r.DefaultOn = on; r.Risky = risky; r.NeedsAdmin = admin;
            r.Kind = RuleKind.Folder;
            foreach (string root in roots) AddRootIfExists(r, Util.ExpandPath(root));
            return r;
        }

        static CleanRule Glob(string id, string group, string nameKey, bool on, bool risky,
                              bool admin, string[] masks, params string[] roots)
        {
            CleanRule r = Folder(id, group, nameKey, on, risky, admin, roots);
            r.Kind = RuleKind.Glob;
            r.Masks = masks;
            return r;
        }

        static void AddRootIfExists(CleanRule r, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string full = Util.Normalize(path);
            if (full == null || !Directory.Exists(full)) return;
            // Never let a rule point at something the delete guard would refuse
            // anyway — better to drop it here than to fail silently per entry.
            if (Util.IsProtectedPath(full)) return;
            foreach (string existing in r.Roots)
                if (string.Equals(existing, full, StringComparison.OrdinalIgnoreCase)) return;
            r.Roots.Add(full);
        }

        // Adds the rule only when it has somewhere to look. Returns the rule so
        // callers can keep chaining (.NameArg = ...) on one line.
        static CleanRule Add(List<CleanRule> rules, CleanRule r)
        {
            if (r != null && r.Roots.Count > 0) rules.Add(r);
            return r ?? new CleanRule();
        }

        internal static List<string> ChromiumProfiles(string userDataRaw)
        {
            var result = new List<string>();
            string userData = Util.ExpandPath(userDataRaw);
            if (userData == null || !Directory.Exists(userData)) return result;
            // Opera points straight at a profile; Chrome/Edge point at a container
            // of them. Detect by looking for the marker folders a profile has.
            if (LooksLikeProfile(userData)) result.Add(userData);
            foreach (string d in SubDirectories(userData))
            {
                string name = Path.GetFileName(d);
                bool named = string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Guest Profile", StringComparison.OrdinalIgnoreCase);
                if (named || LooksLikeProfile(d)) result.Add(d);
            }
            return result;
        }

        static bool LooksLikeProfile(string dir)
        {
            try
            {
                return Directory.Exists(Path.Combine(dir, "Network"))
                    || Directory.Exists(Path.Combine(dir, "Cache"))
                    || File.Exists(Path.Combine(dir, "Preferences"));
            }
            catch { return false; }
        }

        static string[] SubDirectories(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return new string[0];
            try { return Directory.GetDirectories(dir); }
            catch { return new string[0]; }
        }
    }
}
