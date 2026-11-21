# Microsoft Store edition

The MSIX edition is a Windows desktop app. Open it, choose **Fill screen** or **Blurred edges**, and select **Set Wallpaper Slideshow** to download images and enable the native Windows slideshow. After the first sync it checks the gallery about once a day while running. Closing the window keeps the app in the notification area, where **Exit** closes it completely. Startup is disabled by default. The app links to Windows Startup Apps settings.

Blurred edges shows the whole photo and fills the spare space with a softened copy. The canvas uses the primary display resolution. Reapply the slideshow after changing that resolution.

This edition stores its own downloads in the package's LocalState folder and uses neither the GitHub release's scheduled task nor its PowerShell installer. If switching from the EXE edition, uninstall that edition to stop its scheduled task. When uninstalling the MSIX edition, choose a different background in Windows Personalization if the slideshow still points to its former image folder.

## Build a preview

From the repository root, use Windows PowerShell 5.1:

    .\store\Build-Msix.ps1 -MakeAppx 'C:\path\to\x64\makeappx.exe'

MakeAppx is available through the Windows SDK or the Microsoft.Windows.SDK.BuildTools NuGet package. The preview has a development identity, supports x64, and is unsigned. It is for testing, not Store submission or public installation. For a packaged installation test, use a test signing certificate trusted by a development machine or VM. An administrator may be required to trust a test signing certificate.

## Build for submission

1. Reserve **Hubble Nebulae Wallpapers** as an MSIX app in Microsoft Partner Center.
2. Open **Product management > Product identity**. Use the exact values shown for package identity name, publisher and publisher display name:

       .\store\Build-Msix.ps1 -MakeAppx 'C:\path\to\x64\makeappx.exe' -PackageName 'NAME_FROM_PARTNER_CENTER' -Publisher 'PUBLISHER_FROM_PARTNER_CENTER' -PublisherDisplayName 'DISPLAY_NAME_FROM_PARTNER_CENTER'

3. Test launch, gallery sync, wallpaper selection, startup opt-in and uninstall with the packaged build on a test machine. Run the Windows App Certification Kit.
4. Create the English Store listing and upload the MSIX, screenshots, support link and public privacy policy URL. Use the [privacy policy draft](PRIVACY.md) when creating the public page.
5. In certification notes, explain that the package declares runFullTrust so a per-user Windows desktop app can use the Windows wallpaper slideshow COM API and a notification area icon. The app does not request elevation. Reviewers can select **Set Wallpaper Slideshow**, wait for the sync, and confirm that the slideshow is active under Windows Personalization.

Store listing short description:

> Set your Windows wallpaper to a rotating collection of nebula images from NASA Science’s Hubble gallery. Download once to rotate offline, and sync for new images whenever the app is running.

Microsoft signs the MSIX package if it passes Store certification. The public EXE/ZIP release is maintained separately.
