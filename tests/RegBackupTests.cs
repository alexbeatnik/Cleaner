// The .reg writer. A registry cleaner is only defensible because every change
// is backed up first, so the format regedit will actually accept is pinned here.
using System;
using System.Text;
using Microsoft.Win32;

namespace WindowsStalker.Tests
{
    static class RegBackupTests
    {
        public static void TestHiveDisplayNames()
        {
            Assert.Equal("HKEY_CURRENT_USER", RegBackup.HiveDisplayName("HKCU"), "HKCU");
            Assert.Equal("HKEY_LOCAL_MACHINE", RegBackup.HiveDisplayName("HKLM"), "HKLM");
            Assert.Equal("HKEY_CLASSES_ROOT", RegBackup.HiveDisplayName("hkcr"), "case insensitive");
        }

        public static void TestEscapeString()
        {
            Assert.Equal(@"C:\\Windows\\notepad.exe", RegBackup.EscapeString(@"C:\Windows\notepad.exe"),
                "backslashes are doubled");
            Assert.Equal("say \\\"hi\\\"", RegBackup.EscapeString("say \"hi\""), "quotes are escaped");
            Assert.Equal("", RegBackup.EscapeString(null), "null becomes empty, never a crash");
        }

        public static void TestStringValueLine()
        {
            Assert.Equal("\"Path\"=\"C:\\\\App\\\\x.exe\"",
                RegBackup.FormatValue("Path", @"C:\App\x.exe", RegistryValueKind.String),
                "a named string value");
            Assert.Equal("@=\"default\"",
                RegBackup.FormatValue(null, "default", RegistryValueKind.String),
                "the default value is written as @, exactly as regedit does");
        }

        public static void TestDwordIsEightHexDigits()
        {
            Assert.Equal("\"Count\"=dword:00000001",
                RegBackup.FormatValue("Count", 1, RegistryValueKind.DWord), "one");
            Assert.Equal("\"Count\"=dword:ffffffff",
                RegBackup.FormatValue("Count", -1, RegistryValueKind.DWord),
                "negative DWORDs round-trip through the unsigned form");
        }

        public static void TestBinaryIsCommaSeparatedHex()
        {
            Assert.Equal("\"Blob\"=hex:00,0a,ff",
                RegBackup.FormatValue("Blob", new byte[] { 0, 10, 255 }, RegistryValueKind.Binary),
                "lowercase, two digits, comma separated");
        }

        // REG_EXPAND_SZ is stored as hex(2) holding UTF-16LE plus a terminator —
        // regedit rejects it written as a plain string.
        public static void TestExpandStringIsHex2WithTerminator()
        {
            string line = RegBackup.FormatValue("P", "A", RegistryValueKind.ExpandString);
            Assert.Equal("\"P\"=hex(2):41,00,00,00", line, "one character plus the two-byte terminator");
        }

        public static void TestMultiStringEndsWithDoubleTerminator()
        {
            string line = RegBackup.FormatValue("M", new string[] { "A", "B" }, RegistryValueKind.MultiString);
            Assert.Equal("\"M\"=hex(7):41,00,00,00,42,00,00,00,00,00", line,
                "each entry terminated, then the list terminator");
        }

        public static void TestQwordIsLittleEndianHexB()
        {
            Assert.Equal("\"Q\"=hex(b):01,00,00,00,00,00,00,00",
                RegBackup.FormatValue("Q", 1L, RegistryValueKind.QWord), "little endian, eight bytes");
        }

        public static void TestHexBytesFormatting()
        {
            Assert.Equal("", RegBackup.HexBytes(new byte[0]), "empty stays empty");
            Assert.Equal("de,ad,be,ef", RegBackup.HexBytes(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }), "four bytes");
        }

        // A real round trip against the registry: write a value, export it, and
        // check the export names the key and the value it will remove.
        public static void TestExportValueWritesTheKeyHeader()
        {
            const string path = @"Software\WindowsStalkerSelfTest";
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(path))
                    k.SetValue("Probe", "hello");
                var sb = new StringBuilder();
                RegBackup.ExportValue(sb, "HKCU", path, "Probe");
                string text = sb.ToString();
                Assert.True(text.Contains("[HKEY_CURRENT_USER\\" + path + "]"), "the key header: " + text);
                Assert.True(text.Contains("\"Probe\"=\"hello\""), "the value line: " + text);
            }
            finally
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(path, false); } catch { }
            }
        }

        public static void TestExportMissingKeyWritesNothing()
        {
            var sb = new StringBuilder();
            RegBackup.ExportKey(sb, "HKCU", @"Software\WindowsStalkerNoSuchKeyAnywhere", true);
            Assert.Equal("", sb.ToString(), "a key that is already gone contributes no backup lines");
        }
    }
}
