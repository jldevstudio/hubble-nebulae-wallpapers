#Requires -Version 5.1
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.1.0',

    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'
$null = New-Item -ItemType Directory -Force $OutputDirectory
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET'
$framework = Join-Path $frameworkRoot 'Framework64\v4.0.30319'
if (!(Test-Path (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $frameworkRoot 'Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('HubbleBuild-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $temp
$zip = Join-Path $OutputDirectory "HubbleNebulaeWallpapers-v$Version.zip"
$exe = Join-Path $OutputDirectory "HubbleNebulaeWallpapers-Setup-v$Version.exe"
$checksum = Join-Path $OutputDirectory "HubbleNebulaeWallpapers-v$Version.sha256"

Push-Location -LiteralPath $PSScriptRoot
try {
    $payload = Join-Path $temp 'payload.zip'
    $info = Join-Path $temp 'AssemblyInfo.cs'
    Compress-Archive -Path HubbleNebulae.ps1,WindowsSlideshow.cs -DestinationPath $payload
    @"
using System.Reflection;
[assembly: AssemblyTitle("Hubble Nebulae Wallpapers Setup")]
[assembly: AssemblyCompany("jldevstudio")]
[assembly: AssemblyProduct("Hubble Nebulae Wallpapers")]
[assembly: AssemblyVersion("$Version.0")]
"@ | Set-Content -LiteralPath $info -Encoding ASCII

    & $compiler /nologo /target:winexe /platform:anycpu /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll "/resource:$payload,WallpaperPayload" "/out:$exe" Setup.cs $info
    if ($LASTEXITCODE -ne 0) { throw 'Setup compilation failed.' }

    Compress-Archive -Path Install.cmd,Disable.cmd,HubbleNebulae.ps1,WindowsSlideshow.cs,Setup.cs,Build-Release.ps1,LICENSE,README.md -DestinationPath $zip -Force
    Get-FileHash -LiteralPath $exe,$zip -Algorithm SHA256 | ForEach-Object {
        '{0}  {1}' -f $_.Hash.ToLowerInvariant(), (Split-Path $_.Path -Leaf)
    } | Set-Content -LiteralPath $checksum -Encoding ASCII
} finally {
    Pop-Location
    foreach ($name in 'payload.zip','AssemblyInfo.cs') {
        $path = Join-Path $temp $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    Remove-Item -LiteralPath $temp
}
