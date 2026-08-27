// settings.ini load/save and the paths everything else hangs off.
// A corrupt or truncated file must never take down startup: every value is
// parsed defensively and a missing key falls back to the built-in default.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Cleaner
{
    public partial class MainForm : Form
    {
        void ResolvePaths()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            settingsPath = Path.Combine(dir, "settings.ini");
            logPath = Path.Combine(dir, "clean.log");
            backupDir = Path.Combine(dir, "backups");
        }

        internal static string InstallDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\Cleaner");
            }
        }

        internal static bool IsInstalled
        {
            get
            {
                try { return Util.IsUnder(Application.ExecutablePath, InstallDir); }
                catch { return false; }
            }
        }

        void LoadSettings()
        {
            if (settingsPath == null || !File.Exists(settingsPath)) return;
            string[] lines;
            try { lines = File.ReadAllLines(settingsPath, Encoding.UTF8); }
            catch { return; }

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();

                try
                {
                    if (key == "lang")
                        Lang.Current = value == "uk" ? Lang.Language.Ukrainian : Lang.Language.English;
                    else if (key == "schedmode") schedMode = ClampInt(value, 0, 2, schedMode);
                    else if (key == "confirm") confirmBeforeClean = value != "0";
                    else if (key == "traycl") closeToTray = value != "0";
                    else if (key == "autoupdate") autoUpdate = value != "0";
                    else if (key == "lastupdate") Util.TryParseTicks(value, out lastAppUpdateCheck);
                    else if (key == "autostartinit") autostartInitialized = value == "1";
                    else if (key == "modeasked") modeAsked = value == "1";
                    else if (key == "freedbytes") totalFreedBytes = ParseLong(value, 0);
                    else if (key == "freedfiles") totalFreedFiles = ParseLong(value, 0);
                    else if (key == "cleans") totalCleans = (int)ParseLong(value, 0);
                    else if (key == "lastclean") Util.TryParseTicks(value, out lastCleanTime);
                    else if (key == "lastsched") Util.TryParseTicks(value, out lastScheduledClean);
                    else if (key.StartsWith("rule.", StringComparison.Ordinal))
                        ruleChoices[key.Substring(5)] = value != "0";
                }
                catch { } // one bad line never costs the rest of the file
            }
        }

        static int ClampInt(string text, int min, int max, int fallback)
        {
            int v;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return fallback;
            return v < min ? min : v > max ? max : v;
        }

        static long ParseLong(string text, long fallback)
        {
            long v;
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v >= 0
                ? v : fallback;
        }

        void SaveSettings()
        {
            if (settingsPath == null) return;
            var sb = new StringBuilder();
            sb.AppendLine("# Cleaner settings");
            sb.AppendLine("lang=" + (Lang.Current == Lang.Language.Ukrainian ? "uk" : "en"));
            sb.AppendLine("schedmode=" + schedMode.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("confirm=" + (confirmBeforeClean ? "1" : "0"));
            sb.AppendLine("traycl=" + (closeToTray ? "1" : "0"));
            sb.AppendLine("autoupdate=" + (autoUpdate ? "1" : "0"));
            sb.AppendLine("lastupdate=" + lastAppUpdateCheck.Ticks.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("autostartinit=" + (autostartInitialized ? "1" : "0"));
            sb.AppendLine("modeasked=" + (modeAsked ? "1" : "0"));
            sb.AppendLine("freedbytes=" + totalFreedBytes.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("freedfiles=" + totalFreedFiles.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("cleans=" + totalCleans.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("lastclean=" + lastCleanTime.Ticks.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("lastsched=" + lastScheduledClean.Ticks.ToString(CultureInfo.InvariantCulture));
            foreach (KeyValuePair<string, bool> choice in ruleChoices)
                sb.AppendLine("rule." + choice.Key + "=" + (choice.Value ? "1" : "0"));
            try { File.WriteAllText(settingsPath, sb.ToString(), Encoding.UTF8); }
            catch { } // a read-only folder must not break the app
        }
    }
}
