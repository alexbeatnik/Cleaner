# Builds Cleaner with the compiler built into Windows (.NET Framework 4.8).
# Nothing to install: csc.exe is already present on the system.
#
# Two passes on purpose: the app draws its own icon (src/Branding.cs), so pass 1
# produces an exe that can write app.ico via --write-icon, and pass 2 embeds that
# file as the Win32 icon resource. Result: no binary assets in the repository and
# the taskbar/Explorer icon is always in sync with the mark drawn in the UI.
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:windir 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

$outExe = Join-Path $PSScriptRoot 'Cleaner.exe'
$icoPath = Join-Path $PSScriptRoot 'app.ico'
# All sources live in src\ — csc stitches them into the same single portable exe
$sources = Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter *.cs |
    Sort-Object Name | ForEach-Object { $_.FullName }

# Each argument is built as its own complete string and passed via an array
# (splat) rather than as one long backtick-continued line: PowerShell 7/pwsh
# (used by GitHub Actions) passes embedded quotes in mixed quoted/unquoted
# tokens through to the native exe literally instead of resolving them, unlike
# Windows PowerShell 5.1. Splatting a pre-built array works identically on both.
function Build-Exe([string[]] $extra) {
    $cscArgs = @(
        '/nologo'
        '/target:winexe'
        '/platform:anycpu'
        '/codepage:65001'
        "/out:$outExe"
        '/r:System.dll'
        '/r:System.Core.dll'
        '/r:System.Drawing.dll'
        '/r:System.Windows.Forms.dll'
        '/r:System.IO.Compression.dll'
        '/r:System.IO.Compression.FileSystem.dll'
        # Win32_LogicalDisk / Win32_OperatingSystem for the dashboard readouts
        '/r:System.Management.dll'
    ) + $extra + $sources
    & $csc @cscArgs
    if ($LASTEXITCODE -ne 0) { Write-Host 'Build FAILED' -ForegroundColor Red; exit 1 }
}

Build-Exe @()   # pass 1: no icon resource yet
# Start-Process -Wait, not the call operator: PowerShell does not wait for a
# Windows-subsystem executable, so "& $outExe" would return before app.ico exists.
Start-Process -FilePath $outExe -ArgumentList '--write-icon', $icoPath -Wait -WindowStyle Hidden
if (-not (Test-Path $icoPath)) { Write-Host 'Icon generation FAILED' -ForegroundColor Red; exit 1 }
Build-Exe @("/win32icon:$icoPath")  # pass 2: same sources, now with the icon

$size = [math]::Round((Get-Item $outExe).Length / 1KB, 1)
Write-Host "OK: Cleaner.exe ($size KB)"
