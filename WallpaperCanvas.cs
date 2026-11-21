using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace HubbleWallpaper
{
    public enum WallpaperLayout
    {
        Fill,
        BlurredEdges
    }

    public static class WallpaperCanvas
    {
        public static WallpaperLayout ReadLayout(string data)
        {
            var file = Path.Combine(data, "wallpaper-layout.txt");
            return File.Exists(file) && File.ReadAllText(file).Trim() == "BlurredEdges"
                ? WallpaperLayout.BlurredEdges : WallpaperLayout.Fill;
        }

        public static void SaveLayout(string data, WallpaperLayout layout)
        {
            File.WriteAllText(Path.Combine(data, "wallpaper-layout.txt"), layout.ToString());
        }

        public static string Prepare(string originals, string data, WallpaperLayout layout)
        {
            if (layout == WallpaperLayout.Fill)
                return originals;

            var screen = Screen.PrimaryScreen.Bounds.Size;
            string destination = Path.Combine(data, "BlurredEdges-" + screen.Width + "x" + screen.Height);
            Directory.CreateDirectory(destination);

            var jpeg = Array.Find(ImageCodecInfo.GetImageEncoders(), item => item.FormatID == ImageFormat.Jpeg.Guid);
            using (var quality = new EncoderParameters(1))
            using (var shade = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
            {
                quality.Param[0] = new EncoderParameter(Encoder.Quality, 91L);
                foreach (string source in Directory.GetFiles(originals, "*.jpg"))
                {
                    string target = Path.Combine(destination, Path.GetFileName(source));
                    if (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(source))
                        continue;

                    Render(source, target, screen, jpeg, quality, shade);
                }
            }
            return destination;
        }

        private static void Render(string source, string target, Size screen, ImageCodecInfo jpeg,
            EncoderParameters quality, Brush shade)
        {
            string temporary = Path.Combine(Path.GetDirectoryName(target), Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                int shortSide = 18;
                var blurSize = screen.Width >= screen.Height
                    ? new Size((int)Math.Round(shortSide * (double)screen.Width / screen.Height), shortSide)
                    : new Size(shortSide, (int)Math.Round(shortSide * (double)screen.Height / screen.Width));

                using (var photo = Image.FromFile(source))
                using (var blur = new Bitmap(blurSize.Width, blurSize.Height))
                using (var canvas = new Bitmap(screen.Width, screen.Height))
                using (var background = Graphics.FromImage(blur))
                using (var output = Graphics.FromImage(canvas))
                using (var sampling = new ImageAttributes())
                {
                    sampling.SetWrapMode(WrapMode.TileFlipXY);
                    background.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    output.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    Draw(background, photo, Centered(photo.Size, blurSize, true), sampling);
                    var bounds = new Rectangle(Point.Empty, screen);
                    Draw(output, blur, bounds, sampling);
                    output.FillRectangle(shade, bounds);
                    Draw(output, photo, Centered(photo.Size, screen, false), sampling);
                    canvas.Save(temporary, jpeg, quality);
                }

                if (File.Exists(target))
                    File.Replace(temporary, target, null);
                else
                    File.Move(temporary, target);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        private static void Draw(Graphics graphics, Image image, Rectangle destination, ImageAttributes sampling)
        {
            graphics.DrawImage(image, destination, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, sampling);
        }

        private static Rectangle Centered(Size image, Size canvas, bool cover)
        {
            double horizontal = (double)canvas.Width / image.Width;
            double vertical = (double)canvas.Height / image.Height;
            double scale = cover ? Math.Max(horizontal, vertical) : Math.Min(horizontal, vertical);
            int width = Math.Max(1, (int)Math.Round(image.Width * scale));
            int height = Math.Max(1, (int)Math.Round(image.Height * scale));
            return new Rectangle((canvas.Width - width) / 2, (canvas.Height - height) / 2, width, height);
        }
    }
}
