// Cleaner — a junk cleaner / system tune-up tool for Windows in the spirit of
// CCleaner and CleanMyMac. One portable exe, zero dependencies.
// The metadata below is not decoration: a Win32 version resource with empty
// company/description/copyright fields is one of the features Defender's cloud
// model weighs when it scores an unsigned executable, and a disk cleaner that
// deletes files under %LocalAppData% already looks unusual to a heuristic.
// Keep these fields populated.
using System.Reflection;

[assembly: AssemblyTitle("Cleaner")]
[assembly: AssemblyProduct("Cleaner")]
[assembly: AssemblyDescription("Disk cleanup and system tune-up for Windows: junk files, registry, startup, installed apps, large files and duplicates.")]
[assembly: AssemblyCompany("Oleksii Poliakov")]
[assembly: AssemblyCopyright("Copyright 2026 Oleksii Poliakov — Apache License 2.0")]
[assembly: AssemblyVersion("0.4.0.0")]
[assembly: AssemblyFileVersion("0.4.0.0")]
