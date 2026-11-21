#Requires -Version 5.1
param(
    [ValidateSet('Install','Sync','Enable','Stop','Uninstall')]
    [string]$Action = 'Sync',

    [ValidateRange(1, 1440)]
    [int]$Minutes = 30,

    [string]$DataDirectory = (Join-Path $env:LOCALAPPDATA 'NebulosasHubble')
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run this script with powershell.exe.' }
$DataDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DataDirectory)
$gallery = [uri]'https://science.nasa.gov/gallery/hubbles-nebulae/'
$images = Join-Path $DataDirectory 'Fotos'
$taskName = 'Nebulosas Hubble - sincronizar galeria'

function Sync-Gallery {
    $null = New-Item -ItemType Directory -Force -Path $images
    Add-Type -AssemblyName PresentationCore
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $headers = @{ Accept = 'image/jpeg,image/png,image/*;q=0.8' }
    $page = $gallery
    $visited = [Collections.Generic.HashSet[string]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new()
    $hash = [Security.Cryptography.SHA256]::Create()
    $downloaded = 0
    $failed = 0
    try {
        while ($page -and $visited.Add($page.AbsoluteUri)) {
            $html = (Invoke-WebRequest -Uri $page -UseBasicParsing -TimeoutSec 25).Content
            $cards = [regex]::Matches($html, '<a\b(?=[^>]*\bclass="[^"]*\bhds-gallery-image-link\b)[^>]*>(?:(?!</a>).)*?<img\b[^>]*\bsrc="([^"]+)"', 'Singleline')
            if (!$cards.Count) { throw "No images found at $page" }

            foreach ($card in $cards) {
                $url = [uri][Net.WebUtility]::HtmlDecode($card.Groups[1].Value)
                if ($url.Scheme -ne 'https' -or $url.Host -notin 'science.nasa.gov','assets.science.nasa.gov') { continue }
                if (!$seen.Add($url.AbsolutePath)) { continue }
                $digest = $hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($url.AbsolutePath))
                $name = ([BitConverter]::ToString($digest)).Replace('-', '').Substring(0, 24)
                $target = Join-Path $images "$name.jpg"
                if (Test-Path -LiteralPath $target) { continue }

                $builder = [UriBuilder]::new($url)
                $builder.Query = if ($url.Host -eq 'assets.science.nasa.gov') { 'w=2560&h=2560&fit=clip' } else { 'w=2560' }
                $temp = Join-Path $DataDirectory ([guid]::NewGuid().ToString() + '.tmp')
                try {
                    try {
                        Invoke-WebRequest -Uri $builder.Uri -Headers $headers -UseBasicParsing -TimeoutSec 25 -OutFile $temp
                    } catch {
                        Invoke-WebRequest -Uri $url -Headers $headers -UseBasicParsing -TimeoutSec 25 -OutFile $temp
                    }
                    $stream = [IO.File]::OpenRead($temp)
                    try {
                        $bitmap = [Windows.Media.Imaging.BitmapFrame]::Create($stream, 'PreservePixelFormat', 'OnLoad')
                    } finally { $stream.Dispose() }

                    $encoder = [Windows.Media.Imaging.JpegBitmapEncoder]::new()
                    $encoder.QualityLevel = 94
                    $encoder.Frames.Add($bitmap)
                    $stream = [IO.File]::Create($temp)
                    try { $encoder.Save($stream) } finally { $stream.Dispose() }
                    if (!(Test-Path -LiteralPath $target)) {
                        Move-Item -LiteralPath $temp -Destination $target
                        $downloaded++
                    }
                } catch {
                    Write-Warning "$url - $_"
                    $failed++
                } finally {
                    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp }
                }
            }

            $next = [regex]::Match($html, '<a\b(?=[^>]*\bclass="next page-numbers")[^>]*\bhref="([^"]+)"')
            $page = if ($next.Success) { [uri]::new($gallery, $next.Groups[1].Value) }
            if ($page -and ($page.Host -ne $gallery.Host -or $page.Scheme -ne 'https' -or
                $page.AbsolutePath -notmatch '^/gallery/hubbles-nebulae/page/\d+/$')) {
                throw "The next page is outside the gallery: $page"
            }
        }
    } finally { $hash.Dispose() }
    $summary = "Pages: $($visited.Count). Images found: $($seen.Count). New: $downloaded. Failed: $failed."
    Set-Content -LiteralPath (Join-Path $DataDirectory 'ultima-sincronizacion.txt') -Value @((Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'), $summary)
    Write-Output $summary
    if (!(Get-ChildItem -LiteralPath $images -Filter '*.jpg' -File | Select-Object -First 1)) {
        throw 'No images are available for the wallpaper.'
    }
    if ($failed) { throw 'Some downloads failed. Previously downloaded images are still available.' }
}

function Enable-Slideshow {
    if (!(Test-Path -LiteralPath $images -PathType Container)) { throw 'Run Sync first.' }
    Add-Type -Path (Join-Path $PSScriptRoot 'WindowsSlideshow.cs')
    [HubbleWallpaper.WindowsSlideshow]::Enable($images, [uint32]($Minutes * 60000))
}

switch ($Action) {
    'Sync' { Sync-Gallery }
    'Enable' { Enable-Slideshow }
    'Install' {
        Sync-Gallery
        $engine = Join-Path $PSHOME 'powershell.exe'
        $runtime = Join-Path $DataDirectory 'App'
        $null = New-Item -ItemType Directory -Path $runtime -Force
        if ([IO.Path]::GetFullPath($runtime) -ne $PSScriptRoot) {
            Copy-Item -LiteralPath $PSCommandPath, (Join-Path $PSScriptRoot 'WindowsSlideshow.cs') -Destination $runtime -Force
        }
        $script = Join-Path $runtime 'HubbleNebulae.ps1'
        $taskAction = New-ScheduledTaskAction -Execute $engine -Argument (
            '-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -WindowStyle Hidden -File "{0}" -Action Sync -DataDirectory "{1}"' -f $script, $DataDirectory
        )
        $trigger = New-ScheduledTaskTrigger -Daily -At '12:00'
        $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew
        $user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        Register-ScheduledTask -TaskName $taskName -Action $taskAction -Trigger $trigger -Settings $settings -User $user -Force | Out-Null
        Enable-Slideshow
    }
    { $_ -in 'Stop','Uninstall' } {
        if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
            Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        }
        if ($Action -eq 'Uninstall') {
            $entry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HubbleNebulaeWallpapers'
            if (Test-Path -LiteralPath $entry) { Remove-Item -LiteralPath $entry }
            $runtime = Join-Path $DataDirectory 'App'
            foreach ($name in 'HubbleNebulae.ps1','NebulosasHubble.ps1','WindowsSlideshow.cs') {
                $file = Join-Path $runtime $name
                if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
            }
        }
    }
}
