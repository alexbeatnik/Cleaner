// Writes .reg files. Every registry fix is backed by one of these first, so the
// change can be undone by double-clicking the backup — which is the only reason
// a registry cleaner is defensible at all.
//
// The formatting is deliberately pure and separated from the scanners so the
// escaping rules (regedit's, not ours) can be tested directly.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace Cleaner
{
    static class RegBackup
    {
        public const string FileHeader = "Windows Registry Editor Version 5.00";

        public static string HiveDisplayName(string shortName)
        {
            if (string.Equals(shortName, "HKLM", StringComparison.OrdinalIgnoreCase)) return "HKEY_LOCAL_MACHINE";
            if (string.Equals(shortName, "HKCU", StringComparison.OrdinalIgnoreCase)) return "HKEY_CURRENT_USER";
            if (string.Equals(shortName, "HKCR", StringComparison.OrdinalIgnoreCase)) return "HKEY_CLASSES_ROOT";
            return shortName;
        }

        // regedit escapes only the backslash and the double quote inside a string
        internal static string EscapeString(string value)
        {
            if (value == null) return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        internal static string HexBytes(byte[] bytes)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        static byte[] Utf16WithNull(string s)
        {
            byte[] body = Encoding.Unicode.GetBytes(s ?? "");
            var result = new byte[body.Length + 2];
            Buffer.BlockCopy(body, 0, result, 0, body.Length);
            return result; // the two trailing zero bytes are the terminator
        }

        static byte[] MultiString(string[] values)
        {
            var all = new List<byte>();
            foreach (string v in values ?? new string[0])
                all.AddRange(Utf16WithNull(v));
            all.Add(0); all.Add(0); // the extra terminator that ends the list
            return all.ToArray();
        }

        // One "name"=value line. The default value is written as @, exactly as
        // regedit does, and unknown kinds fall back to a hex blob rather than
        // being silently dropped from the backup.
        internal static string FormatValue(string name, object value, RegistryValueKind kind)
        {
            string left = string.IsNullOrEmpty(name) ? "@" : "\"" + EscapeString(name) + "\"";
            switch (kind)
            {
                case RegistryValueKind.String:
                    return left + "=\"" + EscapeString(Convert.ToString(value, CultureInfo.InvariantCulture)) + "\"";
                case RegistryValueKind.ExpandString:
                    return left + "=hex(2):" + HexBytes(Utf16WithNull(Convert.ToString(value, CultureInfo.InvariantCulture)));
                case RegistryValueKind.MultiString:
                    if (!(value is string[])) throw new NotSupportedException("Invalid REG_MULTI_SZ value");
                    return left + "=hex(7):" + HexBytes(MultiString((string[])value));
                case RegistryValueKind.DWord:
                    return left + "=dword:" +
                        ((uint)Convert.ToInt64(value, CultureInfo.InvariantCulture)).ToString("x8", CultureInfo.InvariantCulture);
                case RegistryValueKind.QWord:
                    return left + "=hex(b):" + HexBytes(BitConverter.GetBytes(Convert.ToInt64(value, CultureInfo.InvariantCulture)));
                case RegistryValueKind.Binary:
                    if (!(value is byte[])) throw new NotSupportedException("Invalid REG_BINARY value");
                    return left + "=hex:" + HexBytes((byte[])value);
                case RegistryValueKind.None:
                    if (!(value is byte[])) throw new NotSupportedException("Invalid REG_NONE value");
                    return left + "=hex(0):" + HexBytes((byte[])value);
                default:
                    throw new NotSupportedException("Registry value kind cannot be backed up: " + kind);
            }
        }

        // A whole-key delete must never proceed with a partial backup. Return
        // false if even one value or nested key cannot be read or represented.
        public static bool ExportKey(StringBuilder sb, string hiveShort, string subkey, bool recursive)
        {
            RegistryKey hive = ElevatedJob.HiveFor(hiveShort);
            if (hive == null) return false;
            RegistryKey key = null;
            try { key = hive.OpenSubKey(subkey, false); }
            catch { return false; }
            if (key == null) return false;
            using (key)
            {
                try
                {
                    sb.AppendLine();
                    sb.AppendLine("[" + HiveDisplayName(hiveShort) + "\\" + subkey + "]");
                    foreach (string name in key.GetValueNames())
                    {
                        object v = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        if (v == null) return false;
                        sb.AppendLine(FormatValue(name, v, key.GetValueKind(name)));
                    }
                    if (recursive)
                        foreach (string sub in key.GetSubKeyNames())
                            if (!ExportKey(sb, hiveShort, subkey + "\\" + sub, true)) return false;
                    return true;
                }
                catch { return false; }
            }
        }

        // Exports a single value as a one-key file — used when a fix removes one
        // value out of a key that must otherwise stay untouched.
        public static bool ExportValue(StringBuilder sb, string hiveShort, string subkey, string valueName)
        {
            RegistryKey hive = ElevatedJob.HiveFor(hiveShort);
            if (hive == null) return false;
            try
            {
                using (RegistryKey key = hive.OpenSubKey(subkey, false))
                {
                    if (key == null) return false;
                    object v = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (v == null) return false;
                    sb.AppendLine();
                    sb.AppendLine("[" + HiveDisplayName(hiveShort) + "\\" + subkey + "]");
                    sb.AppendLine(FormatValue(valueName, v, key.GetValueKind(valueName)));
                    return true;
                }
            }
            catch { return false; }
        }
    }
}
