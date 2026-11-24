#Requires -Version 5.1
param(
    [Parameter(Mandatory)]
    [string]$MakeAppx,
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot) 'dist'),
    [version]$Version = '1.3.0.0',
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
    function New-NebulaArtwork([int]$Size) {
        $bitmap = [Drawing.Bitmap]::new($Size, $Size)
        $canvas = [Drawing.Graphics]::FromImage($bitmap)
        $disc = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(40, 33, 68))
        $orbit = [Drawing.Pen]::new([Drawing.Color]::FromArgb(197, 178, 255), [single]7)
        try {
            $canvas.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $canvas.ScaleTransform($Size / 100, $Size / 100)
            $canvas.FillEllipse($disc, 2, 2, 96, 96)
            $canvas.TranslateTransform(50, 50)
            $canvas.RotateTransform(-34)
            $canvas.DrawEllipse($orbit, -37, -16, 74, 32)
            $canvas.FillEllipse([Drawing.Brushes]::White, -10, -10, 20, 20)
            $canvas.FillEllipse([Drawing.Brushes]::White, 20, -29, 9, 9)
            $stream = [IO.MemoryStream]::new()
            try {
                $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
                return ,($stream.ToArray())
            } finally { $stream.Dispose() }
        } finally {
            $orbit.Dispose()
            $disc.Dispose()
            $canvas.Dispose()
            $bitmap.Dispose()
        }
    }

    foreach ($logo in @{ StoreLogo = 50; Square150x150Logo = 150; Square44x44Logo = 44 }.GetEnumerator()) {
        [IO.File]::WriteAllBytes((Join-Path $assets ($logo.Key + '.png')), (New-NebulaArtwork $logo.Value))
    }

    $iconFile = Join-Path $stage 'HubbleNebulae.ico'
    $sizes = @(16, 24, 32, 48, 64, 256)
    $frames = foreach ($size in $sizes) { New-NebulaArtwork $size }
    $writer = [IO.BinaryWriter]::new([IO.File]::Create($iconFile))
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $dimension = [byte]($sizes[$i] % 256)
            $writer.Write($dimension)
            $writer.Write($dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$i].Length)
            $writer.Write([uint32]$offset)
            $offset += $frames[$i].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    } finally { $writer.Dispose() }

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
        "/reference:$wpf\WindowsBase.dll", "/reference:$wpf\PresentationCore.dll", "/out:$app", "/win32icon:$iconFile",
        (Join-Path $PSScriptRoot 'DesktopApp.cs'), (Join-Path $PSScriptRoot 'Gallery.cs'),
        (Join-Path (Split-Path $PSScriptRoot) 'WindowsSlideshow.cs'), (Join-Path (Split-Path $PSScriptRoot) 'WallpaperCanvas.cs')
    )
    if ($sample) { $compilerArgs += '/define:PREVIEW' }
    & $compiler @compilerArgs
    if ($LASTEXITCODE -ne 0) { throw 'App compilation failed.' }
    Remove-Item -LiteralPath $iconFile

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
