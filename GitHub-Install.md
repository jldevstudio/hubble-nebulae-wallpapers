# GitHub installer and ZIP

Turn images from NASA’s Hubble nebulae gallery into a rotating wallpaper collection on Windows. The tool downloads the images and Windows displays them in random order. It works on Windows 10 and 11 without an account, API key or administrator access.

## Install or update

1. Download the Windows installer from [GitHub Releases](https://github.com/jldevstudio/hubble-nebulae-wallpapers/releases/latest).
2. Open the installer, choose a wallpaper layout, and click **Install**. This release is unsigned, so Windows may show a SmartScreen prompt. The release includes the SHA-256 checksums and the installer source.
3. Let the first download finish. It can take a few minutes and use about 100 MB, depending on the size of the gallery.

The installer sets Windows to shuffle wallpapers every **30 minutes** and schedules a gallery sync every day at **noon local time**. It downloads only missing images. Downloaded images remain available offline, and updating from an earlier version reuses the same collection. To change the interval, go to **Settings → Personalization → Background**.

To use the script package instead, download the ZIP from the same release, extract it and run **Install.cmd**. If Windows blocks the scripts because they came from the internet, review the source first. If you trust the archive, open its **Properties**, select **Unblock**, and extract it again.

## Wallpaper layouts

- **Fill screen** covers the screen and crops the edges of photos as needed.
- **Blurred edges** shows the entire photo and fills the extra space with a soft, darker copy of the image. It prepares the canvas for the primary display. If your displays have different aspect ratios, Windows may crop the blurred background on the other displays. Reapply the layout after changing your primary display resolution.

The app remembers your choice and prepares newly downloaded wallpapers during the daily sync. To change the layout, run the installer again or run the following from the extracted ZIP (use Fill to switch back):

    powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\HubbleNebulae.ps1 -Action Enable -Layout BlurredEdges

## Sync now or disable

To check for new images immediately, open a terminal in the extracted folder and run:

    powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\HubbleNebulae.ps1 -Action Sync

For the EXE installation, uninstall **Hubble Nebulae Wallpapers** from **Settings > Apps** to remove the daily sync and installed scripts. For the ZIP installation, run **Disable.cmd** to remove the daily sync. To stop the slideshow, choose a different background in **Settings → Personalization → Background**. You can then delete the data folder to free up space.

## Data and troubleshooting

The data folder is **%LOCALAPPDATA%\NebulosasHubble**. Downloaded images are in **Fotos**, the scheduled task runs the scripts in **App**, and **ultima-sincronizacion.txt** contains the most recent sync summary. A failed sync leaves downloaded images available. If NASA changes the layout of the gallery, the downloader may need updating.

The tool uses Windows PowerShell 5.1 and the built-in Windows wallpaper slideshow. The command files and setup source are included in the ZIP. To build the release packages, run Build-Release.ps1 with Windows PowerShell 5.1.

## License and credits

The software is released under the [MIT license](LICENSE). Images are downloaded directly from [NASA Science’s Hubble Nebulae gallery](https://science.nasa.gov/gallery/hubbles-nebulae/) and are not bundled with the software. For image credits and usage terms, see the individual gallery pages and [NASA’s media usage guidelines](https://www.nasa.gov/nasa-brand-center/images-and-media/). This is an independent project.
