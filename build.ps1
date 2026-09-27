# Builds USB Ports with the C# compiler that ships with Windows (.NET Framework 4.x),
# so no SDK or Visual Studio is needed.
#
#   .\build.ps1            -> dist\USB-Ports-Setup.exe
#   .\build.ps1 -Install   -> also installs it for this user and starts it
#   .\build.ps1 -Release   -> also copies it to download\ (the file the README links to)
param([switch]$Install, [switch]$Release)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$ico = Join-Path $src 'app.ico'
$exe = Join-Path $dist 'USB-Ports-Setup.exe'
$sources = @(Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName })

& $csc /nologo /codepage:65001 /target:winexe /optimize+ /debug- "/win32icon:$ico" "/resource:$ico,app.ico" "/out:$exe" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'compile failed' }
"built $exe ($([math]::Round((Get-Item $exe).Length / 1KB)) KB)"

if ($Release) {
    New-Item -ItemType Directory -Force (Join-Path $root 'download') | Out-Null
    Copy-Item $exe (Join-Path $root 'download\USB-Ports-Setup.exe') -Force
    'copied to download\USB-Ports-Setup.exe'
}
if ($Install) {
    # Wait for the installer only (Start-Process -Wait would also wait for the app it launches).
    $p = Start-Process $exe -ArgumentList '--install' -PassThru
    $p.WaitForExit()
    "installed (exit code $($p.ExitCode))"
}
