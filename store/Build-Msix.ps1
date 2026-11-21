#Requires -Version 5.1
param(
    [Parameter(Mandatory)]
    [string]$MakeAppx,
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot) 'dist'),
    [version]$Version = '1.2.0.0',
    [string]$PackageName,
    [string]$Publisher,
    [string]$PublisherDisplayName,
    [switch]$UnsignedPreview
)

$ErrorActionPreference = 'Stop'
$sample = !$PackageName -and !$Publisher -and !$PublisherDisplayName
if (!$sample -and (!$PackageName -or !$Publisher -or !$PublisherDisplayName)) {
    throw 'Provide the package name, publisher and publisher display name from Partner Center together.'
}
if (!$sample -and $UnsignedPreview) { throw 'Use the unsigned preview only with the sample identity.' }
if ($Version.Revision -ne 0) { throw 'The four-part Store package version must end in .0.' }

$MakeAppx = (Resolve-Path -LiteralPath $MakeAppx).Path
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$stage = Join-Path ([IO.Path]::GetTempPath()) ('HubbleMsix-' + [guid]::NewGuid().ToString('N'))
$assets = Join-Path $stage 'Assets'
$null = New-Item -ItemType Directory -Path $assets -Force
try {
    Add-Type -AssemblyName System.Drawing
    foreach ($logo in @{ StoreLogo = 50; Square150x150Logo = 150; Square44x44Logo = 44 }.GetEnumerator()) {
        $size = [int]$logo.Value
        $bitmap = [Drawing.Bitmap]::new($size, $size)
        $canvas = [Drawing.Graphics]::FromImage($bitmap)
        $pen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(145, 195, 255), [single]5)
        try {
            $canvas.Clear([Drawing.Color]::FromArgb(20, 28, 59))
            $canvas.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $canvas.ScaleTransform($size / 100, $size / 100)
            $canvas.TranslateTransform(50, 50)
            $canvas.RotateTransform(-35)
            $canvas.DrawEllipse($pen, -32, -15, 64, 30)
            $canvas.FillEllipse([Drawing.Brushes]::White, -9, -9, 18, 18)
            $canvas.FillEllipse([Drawing.Brushes]::White, 13, 22, 7, 7)
            $bitmap.Save((Join-Path $assets ($logo.Key + '.png')), [Drawing.Imaging.ImageFormat]::Png)
        } finally {
            $pen.Dispose()
            $canvas.Dispose()
            $bitmap.Dispose()
        }
    }

    [xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw
    $manifest.Package.Identity.Version = $Version.ToString()
    if (!$sample) {
        $manifest.Package.Identity.Name = $PackageName
        $manifest.Package.Identity.Publisher = $Publisher
        $manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
    }
    if ($UnsignedPreview) {
        $manifest.Package.Identity.Publisher += ', OID.2.25.311729368913984317654407730594956997722=1'
    }
    $manifest.Save((Join-Path $stage 'AppxManifest.xml'))

    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    $compiler = Join-Path $framework 'csc.exe'
    $wpf = Join-Path $framework 'WPF'
    $app = Join-Path $stage 'HubbleNebulae.exe'
    $compilerArgs = @(
        '/nologo', '/target:winexe', '/platform:x64', '/optimize+',
        '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Net.Http.dll', '/reference:System.Xaml.dll',
        "/reference:$wpf\WindowsBase.dll", "/reference:$wpf\PresentationCore.dll", "/out:$app",
        (Join-Path $PSScriptRoot 'DesktopApp.cs'), (Join-Path $PSScriptRoot 'Gallery.cs'),
        (Join-Path (Split-Path $PSScriptRoot) 'WindowsSlideshow.cs')
    )
    if ($sample) { $compilerArgs += '/define:PREVIEW' }
    & $compiler @compilerArgs
    if ($LASTEXITCODE -ne 0) { throw 'App compilation failed.' }

    $name = if ($sample) { "HubbleNebulae-Preview-$Version" } else { "HubbleNebulae-Store-$Version" }
    $result = Join-Path $OutputDirectory "$name-x64.msix"
    & $MakeAppx pack /d $stage /p $result /o
    if ($LASTEXITCODE -ne 0) { throw 'MSIX packaging failed.' }
    Get-Item -LiteralPath $result
} finally {
    $stagePath = [IO.Path]::GetFullPath($stage)
    $allowed = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($stagePath.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path $stagePath -Leaf) -match '^HubbleMsix-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $stagePath -Recurse
    }
}
