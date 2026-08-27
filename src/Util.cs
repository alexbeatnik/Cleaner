// Pure helpers (sizes, path safety, command-line parsing, hashing) plus the
// small shell interop the cleaner needs. Everything here is deliberately free of
// UI so it can be exercised by tests\*.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Cleaner
{
    static class Util
    {
        // ---------- formatting ----------

        static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB", "PB" };

        // Invariant culture on purpose: the decimal point stays a point in both
        // UI languages, and tests do not have to care about the machine's locale.
        public static string FormatSize(long bytes)
        {
            if (bytes < 0) bytes = 0;
            if (bytes < 1024) return bytes + " B";
            double v = bytes;
            int unit = 0;
            while (v >= 1024 && unit < SizeUnits.Length - 1) { v /= 1024; unit++; }
            string fmt = v >= 100 ? "0" : v >= 10 ? "0.0" : "0.00";
            return v.ToString(fmt, CultureInfo.InvariantCulture) + " " + SizeUnits[unit];
        }

        // Same units, one more digit past 100. FormatSize drops the decimal there
        // so a stat strip stays narrow, but the drive bars are the one place that
        // reads as broken without it: on a 953 GB disk "281 GB free" is what both
        // 281.0 and 281.9 print, so freeing a few gigabytes moved nothing on
        // screen and the clean looked like it had done nothing.
        public static string FormatSizeFine(long bytes)
        {
            if (bytes < 0) bytes = 0;
            if (bytes < 1024) return bytes + " B";
            double v = bytes;
            int unit = 0;
            while (v >= 1024 && unit < SizeUnits.Length - 1) { v /= 1024; unit++; }
            return v.ToString(v >= 10 ? "0.0" : "0.00", CultureInfo.InvariantCulture) + " " + SizeUnits[unit];
        }

        // Takes the fraction, not the percentage: every caller here has a
        // used/total ratio in hand and rounding it twice is how a bar reads 100%
        // while a byte is still free.
        public static string FormatPercent(double fraction)
        {
            if (fraction < 0 || double.IsNaN(fraction)) fraction = 0;
            if (fraction > 1) fraction = 1;
            return (fraction * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        // A dash rather than 01.01.0001 for a file whose timestamp the filesystem
        // would not give us: a sortable date column must not imply that a file is
        // four centuries older than the disk it sits on.
        public static string FormatDate(DateTime when)
        {
            if (when < new DateTime(1980, 1, 1)) return "—";
            return when.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        }

        public static string FormatCount(long n)
        {
            return n.ToString("#,0", CultureInfo.InvariantCulture);
        }

        // Shortens a path for a fixed-width label: keeps the drive and the file
        // name, elides the middle. "C:\a\b\c\d\file.txt" -> "C:\a\...\file.txt"
        public static string ShortenPath(string path, int max)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= max) return path ?? "";
            if (max <= 3) return "...";
            string name = Path.GetFileName(path);
            // Clamped at both ends: a max only a little wider than the ellipsis
            // makes name.Length - (max - 3) point past the end of the name.
            if (name.Length + 6 >= max)
                return "..." + name.Substring(Math.Min(name.Length, Math.Max(0, name.Length - (max - 3))));
            int head = max - name.Length - 4;
            return path.Substring(0, Math.Max(3, head)) + "..." + Path.DirectorySeparatorChar + name;
        }

        // A corrupt settings value must never take down startup: timestamps are
        // range-checked here instead of going straight into new DateTime(ticks).
        public static bool TryParseTicks(string s, out DateTime value)
        {
            value = DateTime.MinValue;
            long ticks;
            if (!long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks)) return false;
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) return false;
            try { value = new DateTime(ticks); return true; }
            catch { return false; }
        }

        // ---------- paths ----------

        // Expands %VAR% references. Returns null when any variable is unset, so a
        // rule for software that is not installed simply drops out of the catalog
        // instead of resolving to a half-expanded path.
        public static string ExpandPath(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string expanded = Environment.ExpandEnvironmentVariables(raw);
            if (expanded.IndexOf('%') >= 0) return null; // an unset variable stayed literal
            try { return Path.GetFullPath(expanded); }
            catch { return null; }
        }

        static string[] protectedRoots;

        // Folders this app must never delete — the well-known shell locations plus
        // the profile root. Cached: Environment.GetFolderPath hits the shell API.
        internal static string[] ProtectedRoots()
        {
            if (protectedRoots != null) return protectedRoots;
            var list = new List<string>();
            Environment.SpecialFolder[] folders =
            {
                Environment.SpecialFolder.Windows,
                Environment.SpecialFolder.System,
                Environment.SpecialFolder.SystemX86,
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.CommonProgramFiles,
                Environment.SpecialFolder.CommonApplicationData,
                Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolder.MyPictures,
                Environment.SpecialFolder.MyMusic,
                Environment.SpecialFolder.MyVideos,
                Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.DesktopDirectory,
                Environment.SpecialFolder.Favorites,
                Environment.SpecialFolder.Startup,
                Environment.SpecialFolder.CommonStartup,
                Environment.SpecialFolder.StartMenu,
                Environment.SpecialFolder.CommonStartMenu,
                Environment.SpecialFolder.Programs,
                Environment.SpecialFolder.CommonPrograms,
            };
            foreach (Environment.SpecialFolder sf in folders)
            {
                string p = null;
                try { p = Environment.GetFolderPath(sf); }
                catch { }
                if (!string.IsNullOrEmpty(p)) AddNormalized(list, p);
            }
            // Not a SpecialFolder, but deleting it would take every profile with it
            string profile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(profile))
            {
                try { AddNormalized(list, Path.GetDirectoryName(profile)); }
                catch { }
            }
            // Downloads has no SpecialFolder entry before .NET Core
            if (!string.IsNullOrEmpty(profile)) AddNormalized(list, Path.Combine(profile, "Downloads"));
            protectedRoots = list.ToArray();
            return protectedRoots;
        }

        static void AddNormalized(List<string> list, string path)
        {
            string n = Normalize(path);
            if (n == null) return;
            foreach (string existing in list)
                if (string.Equals(existing, n, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(n);
        }

        // Full path without a trailing separator (except for a drive root, which
        // keeps its backslash so "C:\" never collapses into the "C:" drive-relative
        // form — a different thing entirely to the Win32 API).
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string full;
            try { full = Path.GetFullPath(path); }
            catch { return null; }
            if (full.Length > 3) full = full.TrimEnd(Path.DirectorySeparatorChar);
            return full;
        }

        public static bool IsDriveRoot(string path)
        {
            string p = Normalize(path);
            if (p == null) return false;
            try { return string.Equals(p, Path.GetPathRoot(p), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        // True when child sits inside root (or is root itself).
        public static bool IsUnder(string child, string root)
        {
            string c = Normalize(child), r = Normalize(root);
            if (c == null || r == null) return false;
            if (string.Equals(c, r, StringComparison.OrdinalIgnoreCase)) return true;
            if (r.Length > 3) r += Path.DirectorySeparatorChar;
            return c.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }

        // The single guard every delete goes through. Refuses drive and share
        // roots, the well-known shell folders, and any ancestor of one of them —
        // deleting C:\Users would take the profile with it even though C:\Users
        // is not itself in the list.
        public static bool IsProtectedPath(string path)
        {
            string p = Normalize(path);
            if (p == null) return true;
            if (IsDriveRoot(p)) return true;
            if (p.StartsWith(@"\\", StringComparison.Ordinal))
            {
                // \\server\share (two segments after the leading slashes) is a root
                string[] parts = p.TrimStart('\\').Split(Path.DirectorySeparatorChar);
                if (parts.Length <= 2) return true;
            }
            foreach (string guard in ProtectedRoots())
            {
                if (string.Equals(p, guard, StringComparison.OrdinalIgnoreCase)) return true;
                if (IsUnder(guard, p)) return true; // p is an ancestor of a protected folder
            }
            return false;
        }

        // ---------- sizing and deleting ----------

        // Recursive size of a directory tree. Inaccessible subtrees are skipped
        // rather than aborting the walk — junk folders routinely contain files
        // locked by the app that owns them.
        public static long DirectorySize(string dir, ref int files, CancelFlag cancel)
        {
            long total = 0;
            var stack = new Stack<string>();
            stack.Push(dir);
            while (stack.Count > 0)
            {
                if (cancel != null && cancel.Cancelled) break;
                string cur = stack.Pop();
                try
                {
                    foreach (string f in Directory.GetFiles(cur))
                    {
                        try { total += new FileInfo(f).Length; files++; }
                        catch { }
                    }
                    foreach (string d in Directory.GetDirectories(cur))
                    {
                        // Reparse points (junctions, OneDrive placeholders) would
                        // walk us out of the tree and double-count — or worse
                        if (IsReparsePoint(d)) continue;
                        stack.Push(d);
                    }
                }
                catch { }
            }
            return total;
        }

        // True when a file or folder was created or written within the last
        // `hours`. Rules with a MinAgeHours floor use this to leave live scratch
        // space alone: %TEMP% is shared by every running program, and deleting a
        // folder an installer or a build is still writing into breaks it.
        // Unreadable timestamps count as recent — the cautious answer.
        public static bool IsRecent(string path, int hours, DateTime nowUtc)
        {
            if (hours <= 0) return false;
            try
            {
                DateTime written = Directory.Exists(path)
                    ? Directory.GetLastWriteTimeUtc(path) : File.GetLastWriteTimeUtc(path);
                DateTime created = Directory.Exists(path)
                    ? Directory.GetCreationTimeUtc(path) : File.GetCreationTimeUtc(path);
                DateTime newest = written > created ? written : created;
                return (nowUtc - newest).TotalHours < hours;
            }
            catch { return true; }
        }

        public static bool IsReparsePoint(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch { return false; }
        }

        public static long EntrySize(string path, ref int files, CancelFlag cancel)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    if (IsReparsePoint(path)) return 0;
                    return DirectorySize(path, ref files, cancel);
                }
                var fi = new FileInfo(path);
                if (!fi.Exists) return 0;
                files++;
                return fi.Length;
            }
            catch { return 0; }
        }

        // Deletes a file or a whole directory tree, counting what actually went
        // away. Locked files (the browser is running, Explorer holds the thumbnail
        // cache) are counted as failures and left alone — never retried harder.
        public static void DeleteEntry(string path, ref long bytes, ref int files, ref int failed)
        {
            if (IsProtectedPath(path)) { failed++; return; }
            try
            {
                if (Directory.Exists(path))
                {
                    if (IsReparsePoint(path))
                    {
                        try { Directory.Delete(path); } catch { failed++; }
                        return;
                    }
                    DeleteTree(path, ref bytes, ref files, ref failed);
                }
                else if (File.Exists(path))
                {
                    DeleteFile(path, ref bytes, ref files, ref failed);
                }
            }
            catch { failed++; }
        }

        static void DeleteFile(string path, ref long bytes, ref int files, ref int failed)
        {
            try
            {
                var fi = new FileInfo(path);
                long len = fi.Length;
                if ((fi.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                    fi.Attributes = FileAttributes.Normal;
                fi.Delete();
                bytes += len;
                files++;
            }
            catch { failed++; }
        }

        static void DeleteTree(string dir, ref long bytes, ref int files, ref int failed)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                    DeleteFile(f, ref bytes, ref files, ref failed);
                foreach (string d in Directory.GetDirectories(dir))
                {
                    if (IsReparsePoint(d)) { try { Directory.Delete(d); } catch { failed++; } continue; }
                    DeleteTree(d, ref bytes, ref files, ref failed);
                }
                Directory.Delete(dir, false); // empty by now unless something was locked
            }
            catch { failed++; }
        }

        // ---------- command lines ----------

        // Pulls the executable out of a registry Run value or an UninstallString.
        // Handles the quoted form, the bare form with arguments, and the common
        // rundll32/msiexec shapes where the first token is what matters.
        public static string ExeFromCommandLine(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            string s = command.Trim();
            if (s.Length > 0 && s[0] == '"')
            {
                int end = s.IndexOf('"', 1);
                if (end > 1) return s.Substring(1, end - 1).Trim();
                return s.Trim('"').Trim();
            }
            // Unquoted: Windows resolves "C:\Program Files\a b\x.exe -flag" by
            // trying successively longer prefixes. Do the same, but stop at the
            // first token that ends in .exe/.com/.bat/.cmd — that is what the
            // overwhelming majority of Run values look like.
            string[] parts = s.Split(' ');
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(parts[i]);
                string candidate = sb.ToString();
                if (HasExeExtension(candidate)) return candidate;
                if (parts[i].StartsWith("/", StringComparison.Ordinal) ||
                    parts[i].StartsWith("-", StringComparison.Ordinal)) break;
            }
            return parts[0];
        }

        static bool HasExeExtension(string s)
        {
            string ext;
            try { ext = Path.GetExtension(s); }
            catch { return false; }
            if (string.IsNullOrEmpty(ext)) return false;
            ext = ext.ToLowerInvariant();
            return ext == ".exe" || ext == ".com" || ext == ".bat" || ext == ".cmd" || ext == ".scr";
        }

        // True when the target of a command line no longer exists on disk — the
        // core test the registry and startup scanners are built on. Bare names
        // ("notepad.exe") are resolved against PATH and System32 before being
        // called missing, so a valid entry is never reported as broken.
        public static bool TargetMissing(string command)
        {
            string exe = ExeFromCommandLine(command);
            if (string.IsNullOrWhiteSpace(exe)) return false; // nothing to judge — leave it alone
            exe = Environment.ExpandEnvironmentVariables(exe).Trim().Trim('"');
            if (exe.Length == 0) return false;
            try
            {
                if (File.Exists(exe) || Directory.Exists(exe)) return false;
                if (!Path.IsPathRooted(exe))
                {
                    string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                    if (File.Exists(Path.Combine(sys, exe))) return false;
                    string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
                    foreach (string dir in pathVar.Split(';'))
                    {
                        if (dir.Length == 0) continue;
                        try { if (File.Exists(Path.Combine(dir, exe))) return false; }
                        catch { }
                    }
                }
                return true;
            }
            catch { return false; } // an unparseable path is not evidence of anything
        }

        // ---------- hashing (duplicate finder) ----------

        // SHA-256 of at most maxBytes from the head of the file. The duplicate
        // finder uses a cheap 64 KB prefix hash to form candidate groups and only
        // then hashes the files in full, so identical multi-GB files are compared
        // once instead of on every pass.
        public static string HashFile(string path, long maxBytes)
        {
            return HashFile(path, maxBytes, null);
        }

        // Chunked all the way through, including the full hash: SHA256's
        // ComputeHash(stream) swallows a multi-gigabyte file in one call that
        // nothing can interrupt, which is what used to make the duplicate finder
        // ignore Stop for minutes at a time. Reading it 64 KB at a time costs
        // nothing measurable and gives the flag somewhere to be seen.
        // A cancelled hash returns null, which every caller already treats as
        // "unreadable, never call it a duplicate".
        public static string HashFile(string path, long maxBytes, CancelFlag cancel)
        {
            try
            {
                using (var sha = SHA256.Create())
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536))
                {
                    var buffer = new byte[65536];
                    long left = maxBytes > 0 ? maxBytes : long.MaxValue;
                    while (left > 0)
                    {
                        if (cancel != null && cancel.Cancelled) return null;
                        int want = (int)Math.Min(buffer.Length, left);
                        int got = fs.Read(buffer, 0, want);
                        if (got <= 0) break;
                        sha.TransformBlock(buffer, 0, got, null, 0);
                        left -= got;
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    return ToHex(sha.Hash);
                }
            }
            catch { return null; }
        }

        public static string ToHex(byte[] bytes)
        {
            if (bytes == null) return null;
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        // ---------- shell interop ----------

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        // Returns false when the shell refuses to answer (a locked-down machine,
        // a drive that was unplugged mid-query) so the caller can show a dash
        // instead of a confident zero.
        public static bool QueryRecycleBin(out long bytes, out long items)
        {
            bytes = 0; items = 0;
            var info = new SHQUERYRBINFO();
            try
            {
                info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
                if (SHQueryRecycleBin(null, ref info) != 0) return false;
                bytes = info.i64Size;
                items = info.i64NumItems;
                return true;
            }
            catch { return false; }
        }

        public static bool EmptyRecycleBin()
        {
            // SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND
            try { return SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4) == 0; }
            catch { return false; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
            public ushort fFlags;
            public int fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        // Sends files to the Recycle Bin instead of deleting them outright. The
        // large-file and duplicate tools use this on purpose: those are the user's
        // own documents, and an undo has to exist.
        public static bool RecycleFiles(IEnumerable<string> paths)
        {
            var sb = new StringBuilder();
            int count = 0;
            foreach (string p in paths)
            {
                if (string.IsNullOrEmpty(p) || IsProtectedPath(p)) continue;
                sb.Append(p).Append('\0');
                count++;
            }
            if (count == 0) return true;
            sb.Append('\0'); // the list is double-null terminated
            var op = new SHFILEOPSTRUCT();
            op.wFunc = 0x0003; // FO_DELETE
            op.pFrom = sb.ToString();
            // FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_NOCONFIRMMKDIR
            op.fFlags = 0x0040 | 0x0010 | 0x0004 | 0x0400 | 0x0200;
            try { return SHFileOperation(ref op) == 0; }
            catch { return false; }
        }
    }

    // A cancel flag that background walkers can watch. A plain bool field on a
    // session object cannot be passed by reference into a helper; this can, and
    // it makes the "whose scan is this?" ownership explicit at every call site.
    sealed class CancelFlag
    {
        volatile bool cancelled;
        public bool Cancelled { get { return cancelled; } }
        public void Cancel() { cancelled = true; }
    }
}
