using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

internal sealed class Gallery
{
    private static readonly Uri FirstPage = new Uri("https://science.nasa.gov/gallery/hubbles-nebulae/");
    private static readonly Regex Cards = new Regex(
        @"<a\b(?=[^>]*\bclass=""[^""]*\bhds-gallery-image-link\b)[^>]*>(?:(?!</a>).)*?<img\b[^>]*\bsrc=""([^""]+)""",
        RegexOptions.Singleline, TimeSpan.FromSeconds(5));
    private static readonly Regex NextPage = new Regex(
        @"<a\b(?=[^>]*\bclass=""next page-numbers"")[^>]*\bhref=""([^""]+)""",
        RegexOptions.None, TimeSpan.FromSeconds(5));
    private static readonly HttpClient Client = new HttpClient(
        new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
        { Timeout = TimeSpan.FromSeconds(25), MaxResponseContentBufferSize = 32 * 1024 * 1024 };

    internal string Folder { get; private set; }

    internal Gallery(string folder)
    {
        Folder = folder;
    }

    internal async Task<string> SyncAsync()
    {
        Directory.CreateDirectory(Folder);
        var pages = new HashSet<Uri>();
        var images = new HashSet<string>(StringComparer.Ordinal);
        int saved = 0;
        int failed = 0;

        using (var hash = SHA256.Create())
        {
            Uri page = FirstPage;
            while (page != null && pages.Add(page))
            {
                string html = await Client.GetStringAsync(page).ConfigureAwait(false);
                MatchCollection cards = Cards.Matches(html);
                if (cards.Count == 0)
                    throw new InvalidOperationException("No images found on the NASA gallery page.");

                foreach (Match card in cards)
                {
                    Uri image;
                    if (!Uri.TryCreate(WebUtility.HtmlDecode(card.Groups[1].Value), UriKind.Absolute, out image) ||
                        image.Scheme != Uri.UriSchemeHttps ||
                        (image.Host != FirstPage.Host && image.Host != "assets.science.nasa.gov") ||
                        !images.Add(image.AbsolutePath))
                        continue;

                    string key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(image.AbsolutePath)))
                        .Replace("-", "").Substring(0, 24);
                    string target = Path.Combine(Folder, key + ".jpg");
                    if (File.Exists(target))
                        continue;

                    try
                    {
                        await DownloadJpegAsync(image, target).ConfigureAwait(false);
                        saved++;
                    }
                    catch (Exception error)
                    {
                        if (!(error is HttpRequestException || error is TaskCanceledException ||
                              error is IOException || error is NotSupportedException ||
                              error is System.Runtime.InteropServices.COMException))
                            throw;
                        failed++;
                    }
                }

                Match next = NextPage.Match(html);
                page = next.Success ? new Uri(FirstPage, WebUtility.HtmlDecode(next.Groups[1].Value)) : null;
                if (page != null && (page.Host != FirstPage.Host || page.Scheme != Uri.UriSchemeHttps ||
                    !Regex.IsMatch(page.AbsolutePath, @"^/gallery/hubbles-nebulae/page/\d+/$")))
                    throw new InvalidOperationException("The next page is outside the NASA gallery.");
            }
        }

        if (Directory.GetFiles(Folder, "*.jpg").Length == 0)
            throw new InvalidOperationException("No images could be downloaded. Try syncing again later.");

        return string.Format("{0} images in gallery. {1} new. {2} failed.", images.Count, saved, failed);
    }

    private static async Task DownloadJpegAsync(Uri original, string target)
    {
        var highResolution = new UriBuilder(original);
        highResolution.Query = original.Host == "assets.science.nasa.gov" ? "w=2560&h=2560&fit=clip" : "w=2560";

        byte[] bytes = null;
        try
        {
            bytes = await FetchImageAsync(highResolution.Uri).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            if (!(error is HttpRequestException || error is TaskCanceledException))
                throw;
        }
        if (bytes == null)
            bytes = await FetchImageAsync(original).ConfigureAwait(false);

        string temp = target + ".tmp";
        try
        {
            using (var input = new MemoryStream(bytes))
            {
                BitmapFrame bitmap = BitmapFrame.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var encoder = new JpegBitmapEncoder { QualityLevel = 94 };
                encoder.Frames.Add(bitmap);
                using (var output = File.Create(temp))
                    encoder.Save(output);
            }
            File.Move(temp, target);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static async Task<byte[]> FetchImageAsync(Uri image)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Get, image))
        {
            request.Headers.TryAddWithoutValidation("Accept", "image/jpeg,image/png,image/*;q=0.8");
            using (HttpResponseMessage response = await Client.SendAsync(request).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }
    }
}
