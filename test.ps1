# Builds and runs the tests (see .claude/rules/testing.md). No SDK needed: it uses
# the same C# compiler and options as build.ps1.
#
#   .\test.ps1                 unit tests + the 500-line check
#   .\test.ps1 -Integration    also the integration tests (real Windows, read-only)
#   .\test.ps1 -Hardware       also the hardware tests (only after the owner confirms in chat)
#   .\test.ps1 -Perf           the performance check against the installed, running app
#   .\test.ps1 -Filter <text>  only tests whose name contains the text
#
# The exit code is non-zero if anything fails.
param([switch]$Integration, [switch]$Hardware, [switch]$Perf, [string]$Filter)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null

# 1. No file may exceed 500 lines (.claude/rules/file-size.md). Build output doesn't count.
$limit = 500
$over = @()
Get-ChildItem $root -Recurse -File -Include *.cs, *.ps1, *.md |
    Where-Object { $_.FullName -notmatch '\\(dist|download|\.git)\\' } |
    ForEach-Object {
        $lines = [IO.File]::ReadAllLines($_.FullName).Count
        if ($lines -gt $limit) { $over += ('{0} ({1} lines)' -f $_.FullName.Substring($root.Length + 1), $lines) }
    }
if ($over.Count -gt 0) {
    "FAIL  File size: $($over.Count) file(s) over $limit lines:"
    $over | ForEach-Object { "        $_" }
} else {
    "PASS  File size: every file is at most $limit lines."
}

# 2. Compile the app's sources with the tests into a console exe.
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$ico = Join-Path $root 'src\app.ico'
$exe = Join-Path $dist 'UsbPorts.Tests.exe'
$sources = @(Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'tests') -Filter *.cs | ForEach-Object { $_.FullName })
& $csc /nologo /codepage:65001 /target:exe /main:TestRunner /optimize+ /debug- "/resource:$ico,app.ico" "/out:$exe" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'compile failed' }

# 3. Run them.
$runArgs = @()
if ($Perf) { $runArgs += '--perf' }
if ($Integration) { $runArgs += '--integration' }
if ($Hardware) { $runArgs += '--hardware' }
if ($Filter) { $runArgs += '--filter', $Filter }
& $exe @runArgs
$failures = $LASTEXITCODE
if ($over.Count -gt 0) { $failures++ }
if ($failures -ne 0) { "test.ps1: FAILED ($failures)"; exit $failures }
'test.ps1: all passed'
