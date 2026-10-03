# Builds Lenovo-Logo-Changer.exe with the C# compiler shipped with .NET Framework 4.x (no download needed).
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $PSScriptRoot 'Lenovo-Logo-Changer.exe'
& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$out" "/win32icon:$(Join-Path $PSScriptRoot 'assets\icon.ico')" `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll /reference:System.Security.dll `
    (Join-Path $PSScriptRoot 'src\Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output $out
