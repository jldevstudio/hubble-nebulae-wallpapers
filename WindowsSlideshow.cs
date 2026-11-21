using System;
using System.Runtime.InteropServices;

namespace HubbleWallpaper
{
    public static class WindowsSlideshow
    {
        private const uint Fill = 4; // DWPOS_FILL

        // The declarations follow the order of IDesktopWallpaper in shobjidl_core.h.
        [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktopWallpaper
        {
            void SetWallpaper(IntPtr monitor, IntPtr path);
            void GetWallpaper(IntPtr monitor, out IntPtr path);
            void GetMonitorDevicePathAt(uint index, out IntPtr monitor);
            void GetMonitorDevicePathCount(out uint count);
            void GetMonitorRECT(IntPtr monitor, IntPtr rect);
            void SetBackgroundColor(uint color);
            void GetBackgroundColor(out uint color);
            void SetPosition(uint position);
            void GetPosition(out uint position);
            void SetSlideshow(IntPtr items);
            void GetSlideshow(out IntPtr items);
            void SetSlideshowOptions(uint options, uint interval);
            void GetSlideshowOptions(out uint options, out uint interval);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHCreateItemFromParsingName(
            string path, IntPtr context, ref Guid iid, out IntPtr item);

        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern int SHCreateShellItemArrayFromShellItem(
            IntPtr item, ref Guid iid, out IntPtr array);

        public static void Enable(string folder, uint milliseconds)
        {
            var itemId = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
            var arrayId = new Guid("B63EA76D-1F85-456F-A19C-48159EFA858B");
            var wallpaperClass = new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");
            IntPtr item = IntPtr.Zero;
            IntPtr array = IntPtr.Zero;
            IDesktopWallpaper wallpaper = null;

            try
            {
                Marshal.ThrowExceptionForHR(
                    SHCreateItemFromParsingName(folder, IntPtr.Zero, ref itemId, out item));
                Marshal.ThrowExceptionForHR(
                    SHCreateShellItemArrayFromShellItem(item, ref arrayId, out array));
                wallpaper = (IDesktopWallpaper)Activator.CreateInstance(
                    Type.GetTypeFromCLSID(wallpaperClass, true));

                wallpaper.SetSlideshow(array);
                wallpaper.SetPosition(Fill);
                wallpaper.SetSlideshowOptions(1, milliseconds); // DSO_SHUFFLEIMAGES
                uint position;
                wallpaper.GetPosition(out position);
                if (position != Fill)
                    throw new InvalidOperationException("Windows did not confirm the Fill wallpaper setting.");

                uint options;
                uint interval;
                wallpaper.GetSlideshowOptions(out options, out interval);
                if ((options & 1) == 0 || interval != milliseconds)
                    throw new InvalidOperationException("Windows did not confirm the slideshow interval and shuffle setting.");
            }
            finally
            {
                if (wallpaper != null) Marshal.ReleaseComObject(wallpaper);
                if (array != IntPtr.Zero) Marshal.Release(array);
                if (item != IntPtr.Zero) Marshal.Release(item);
            }
        }
    }
}
