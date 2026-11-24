using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using HubbleWallpaper;

internal sealed class DesktopApp : Form
{
    private const string Product = "Hubble Nebulae Wallpapers";
    private const uint HalfHour = 30 * 60 * 1000;
    private readonly Gallery gallery;
    private readonly string dataFolder;
    private readonly string statePath;
    private readonly Button syncButton;
    private readonly Button applyButton;
    private readonly ComboBox layoutPicker;
    private readonly Label status;
    private readonly Label progressText;
    private readonly ProgressBar downloadProgress;
    private readonly CheckBox autoApply;
    private readonly string autoApplyPath;
    private readonly Icon appIcon;
    private Bitmap thumbnail;

    private static readonly Color Ink = Color.FromArgb(244, 242, 252);
    private static readonly Color Muted = Color.FromArgb(180, 176, 200);
    private static readonly Color Background = Color.FromArgb(19, 18, 30);
    private static readonly Color PanelColor = Color.FromArgb(32, 29, 48);
    private static readonly Color Border = Color.FromArgb(65, 59, 88);
    private static readonly Color Accent = Color.FromArgb(188, 169, 255);
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer;
    private DateTime nextSync;
    private bool working;
    private bool quitting;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFamilyName(ref uint length, StringBuilder familyName);

    [STAThread]
    private static int Main(string[] args)
    {
        System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
#if PREVIEW
        if (args.Length == 2 && args[0] == "--test-sync")
        {
            try
            {
                string result = new Gallery(args[1]).SyncAsync().GetAwaiter().GetResult().ToString();
                File.WriteAllText(Path.Combine(args[1], "test-result.txt"), result);
                return 0;
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(args[1], "test-result.txt"), error.ToString());
                return 1;
            }
        }

#endif
        bool first;
        using (var instance = new Mutex(true, "Local\\jldevstudio.HubbleNebulae.Store", out first))
        {
            if (!first)
            {
                MessageBox.Show("Hubble Nebulae Wallpapers is already running. Look for its icon in the notification area.", Product);
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                using (var window = new DesktopApp(DataPath()))
                    Application.Run(window);
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, Product, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
        return 0;
    }

    private static string DataPath()
    {
        uint length = 0;
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        int first = GetCurrentPackageFamilyName(ref length, null);
        if (first == 15700) // APPMODEL_ERROR_NO_PACKAGE
            return Path.Combine(local, "HubbleNebulaeStorePreview");
        if (first != 122) // ERROR_INSUFFICIENT_BUFFER
            throw new System.ComponentModel.Win32Exception(first);
        var name = new StringBuilder((int)length);
        int result = GetCurrentPackageFamilyName(ref length, name);
        if (result != 0)
            throw new System.ComponentModel.Win32Exception(result);
        return Path.Combine(local, "Packages", name.ToString(), "LocalState");
    }

    private DesktopApp(string data)
    {
        dataFolder = data;
        gallery = new Gallery(Path.Combine(data, "Wallpapers"));
        statePath = Path.Combine(data, "last-sync.txt");

        autoApplyPath = Path.Combine(data, "auto-apply.txt");
        appIcon = Icon.ExtractAssociatedIcon(typeof(DesktopApp).Assembly.Location);
        Text = Product;
        Icon = appIcon;
        Font = new Font("Segoe UI", 10);
        BackColor = Background;
        ForeColor = Ink;
        ClientSize = new Size(610, 440);
        MinimumSize = new Size(565, 470);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(layout);

        var heading = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = PanelColor };
        heading.Paint += (sender, e) => PaintHeader(heading, e);
        layout.Controls.Add(heading, 0, 0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Margin = Padding.Empty, Padding = new Padding(24, 15, 24, 0) };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(body, 0, 1);

        var layoutLabel = new Label { Text = "Wallpaper layout", AutoSize = true, Margin = Padding.Empty };
        body.Controls.Add(layoutLabel, 0, 0);
        layoutPicker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330,
            AccessibleName = "Wallpaper layout", Margin = Padding.Empty, BackColor = PanelColor, ForeColor = Ink,
            FlatStyle = FlatStyle.Flat };
        layoutPicker.Items.Add("Fill screen (crop edges as needed)");
        layoutPicker.Items.Add("Blurred edges (show the whole photo)");
        layoutPicker.SelectedIndex = WallpaperCanvas.ReadLayout(dataFolder) == WallpaperLayout.BlurredEdges ? 1 : 0;
        body.Controls.Add(layoutPicker, 0, 1);

        autoApply = new CheckBox { Text = "Apply wallpaper automatically after downloading", AutoSize = true,
            Margin = Padding.Empty, Checked = !File.Exists(autoApplyPath) || File.ReadAllText(autoApplyPath).Trim() != "off" };
        autoApply.CheckedChanged += (sender, e) =>
        {
            try
            {
                Directory.CreateDirectory(dataFolder);
                File.WriteAllText(autoApplyPath, autoApply.Checked ? "on" : "off");
            }
            catch (IOException error) { status.Text = error.Message; }
            catch (UnauthorizedAccessException error) { status.Text = error.Message; }
        };
        body.Controls.Add(autoApply, 0, 2);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        syncButton = ActionButton("Download / Sync", true);
        applyButton = ActionButton("Apply Wallpapers", false);
        actions.Controls.Add(syncButton);
        actions.Controls.Add(applyButton);
        body.Controls.Add(actions, 0, 3);

        var progressPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        progressPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        progressPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 13));
        progressPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        progressText = new Label { Dock = DockStyle.Fill, Text = "Download progress", Margin = Padding.Empty, ForeColor = Muted };
        downloadProgress = new ProgressBar { Dock = DockStyle.Fill, AccessibleName = "Download progress",
            Style = ProgressBarStyle.Continuous, Margin = Padding.Empty };
        status = new Label { Dock = DockStyle.Fill, Padding = new Padding(0, 7, 0, 0), Margin = Padding.Empty,
            Text = "Ready. You can minimize the app while it downloads.", ForeColor = Muted, AutoEllipsis = true };
        progressPanel.Controls.Add(progressText, 0, 0);
        progressPanel.Controls.Add(downloadProgress, 0, 1);
        progressPanel.Controls.Add(status, 0, 2);
        body.Controls.Add(progressPanel, 0, 4);

        var footer = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(24, 12, 24, 0) };
        var startup = new LinkLabel { Text = "Windows startup settings", AutoSize = true, Dock = DockStyle.Left,
            LinkColor = Accent, ActiveLinkColor = Ink, VisitedLinkColor = Accent };
        startup.LinkClicked += (sender, e) =>
        {
            try { System.Diagnostics.Process.Start("ms-settings:startupapps"); }
            catch (Exception error) { status.Text = error.Message; }
        };
        footer.Controls.Add(startup);
        layout.Controls.Add(footer, 0, 2);
        RefreshThumbnail();

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Open Hubble Nebulae Wallpapers", null, (sender, e) => ShowWindow());
        trayMenu.Items.Add("Exit", null, (sender, e) => { quitting = true; Close(); });
        tray = new NotifyIcon { Icon = appIcon ?? SystemIcons.Application, Text = Product, ContextMenuStrip = trayMenu, Visible = true };
        tray.DoubleClick += (sender, e) => ShowWindow();

        nextSync = ReadNextSync();
        timer = new System.Windows.Forms.Timer { Interval = 10 * 60 * 1000 };
        timer.Tick += async (sender, e) => await SyncIfDueAsync();
        timer.Start();
        Shown += async (sender, e) => await SyncIfDueAsync();
        syncButton.Click += async (sender, e) => await SyncAsync(false, false);
        applyButton.Click += async (sender, e) => await SyncAsync(true, false);
    }

    private DateTime ReadNextSync()
    {
        DateTime last;
        return File.Exists(statePath) && DateTime.TryParse(File.ReadAllText(statePath), null,
            System.Globalization.DateTimeStyles.RoundtripKind, out last)
            ? last.AddDays(1) : DateTime.MaxValue;
    }

    private Task SyncIfDueAsync()
    {
        return DateTime.UtcNow >= nextSync ? SyncAsync(false, true) : Task.FromResult(false);
    }

    private async Task SyncAsync(bool apply, bool background)
    {
        if (working)
            return;

        working = true;
        syncButton.Enabled = applyButton.Enabled = layoutPicker.Enabled = autoApply.Enabled = false;
        var layout = background ? WallpaperCanvas.ReadLayout(dataFolder)
            : (layoutPicker.SelectedIndex == 1 ? WallpaperLayout.BlurredEdges : WallpaperLayout.Fill);
        bool shouldAutoApply = autoApply.Checked;
        SetWorking("Checking NASA gallery", "You can minimize this window and keep downloading.");

        try
        {
            bool sync = !apply || !Directory.Exists(gallery.Folder) || Directory.GetFiles(gallery.Folder, "*.jpg").Length == 0;
            Gallery.Result result = null;
            if (sync)
            {
                var progress = new Progress<Gallery.Progress>(ShowDownloadProgress);
                result = await Task.Run(() => gallery.SyncAsync(progress));
                DateTime finished = DateTime.UtcNow;
                File.WriteAllText(statePath, finished.ToString("O"));
                nextSync = result.Failed == 0 ? finished.AddDays(1) : finished.AddHours(1);
            }

            bool activate = apply || (shouldAutoApply && result != null && result.Saved > 0);
            if (activate || result != null)
            {
                SetWorking("Preparing wallpapers", "Preparing the selected layout.");
                string folder = await Task.Run(() => WallpaperCanvas.Prepare(gallery.Folder, dataFolder, layout));
                if (activate)
                {
                    WindowsSlideshow.Enable(folder, HalfHour);
                    WallpaperCanvas.SaveLayout(dataFolder, layout);
                }
            }

            FinishProgress(activate ? "Wallpaper slideshow active" : "Download complete", true);
            status.Text = result == null ? "Using previously downloaded wallpapers." : result.ToString();
            RefreshThumbnail();
        }
        catch (Exception error)
        {
            if (nextSync != DateTime.MaxValue)
                nextSync = DateTime.UtcNow.AddHours(1);
            FinishProgress("Could not finish", false);
            status.Text = error.Message;
        }
        finally
        {
            syncButton.Enabled = applyButton.Enabled = layoutPicker.Enabled = autoApply.Enabled = true;
            working = false;
        }
    }

    private void SetWorking(string title, string detail)
    {
        progressText.Text = title;
        status.Text = detail;
        downloadProgress.Style = ProgressBarStyle.Marquee;
        downloadProgress.MarqueeAnimationSpeed = 28;
        Text = title + " · " + Product;
    }

    private void ShowDownloadProgress(Gallery.Progress value)
    {
        downloadProgress.Style = ProgressBarStyle.Continuous;
        downloadProgress.Maximum = value.Total;
        downloadProgress.Value = value.Processed;
        progressText.Text = string.Format("Downloading wallpaper images  ·  {0} / {1}", value.Processed, value.Total);
        status.Text = value.Failed == 0 ? "You can minimize this window and keep downloading."
            : string.Format("{0} failed so far. The download will continue.", value.Failed);
        Text = string.Format("{0}% · {1}", (100 * value.Processed) / value.Total, Product);
    }

    private void FinishProgress(string title, bool complete)
    {
        downloadProgress.Style = ProgressBarStyle.Continuous;
        downloadProgress.Value = complete ? downloadProgress.Maximum : 0;
        progressText.Text = title;
        Text = Product;
    }

    private static Button ActionButton(string text, bool primary)
    {
        var button = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat,
            Padding = new Padding(13, 5, 13, 5), Margin = new Padding(0, 0, 10, 0),
            BackColor = primary ? Accent : PanelColor, ForeColor = primary ? Background : Ink };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(216, 203, 255) : Border;
        return button;
    }

    private void RefreshThumbnail()
    {
        if (thumbnail != null || !Directory.Exists(gallery.Folder))
            return;

        foreach (string path in Directory.EnumerateFiles(gallery.Folder, "*.jpg"))
        {
            try
            {
                using (var source = Image.FromFile(path))
                    thumbnail = new Bitmap(source, 520, Math.Max(1, 520 * source.Height / source.Width));
                Invalidate(true);
                return;
            }
            catch (ArgumentException) { }
            catch (IOException) { }
        }
    }

    private void PaintHeader(Panel panel, PaintEventArgs e)
    {
        var bounds = panel.ClientRectangle;
        if (thumbnail != null)
        {
            int x = bounds.Width / 3;
            var imageArea = new Rectangle(x, 0, bounds.Width - x, bounds.Height);
            double scale = Math.Max((double)imageArea.Width / thumbnail.Width, (double)imageArea.Height / thumbnail.Height);
            int width = (int)Math.Ceiling(scale * thumbnail.Width);
            int height = (int)Math.Ceiling(scale * thumbnail.Height);
            var clipped = e.Graphics.Save();
            e.Graphics.SetClip(imageArea);
            e.Graphics.DrawImage(thumbnail, x + (imageArea.Width - width) / 2, (imageArea.Height - height) / 2, width, height);
            using (var fade = new System.Drawing.Drawing2D.LinearGradientBrush(Rectangle.Inflate(imageArea, 2, 0), PanelColor,
                Color.FromArgb(78, Background), 0f))
                e.Graphics.FillRectangle(fade, imageArea);
            e.Graphics.Restore(clipped);
        }

        else
        {
            var transform = e.Graphics.Save();
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TranslateTransform(bounds.Width * .89f, bounds.Height * .53f);
            e.Graphics.RotateTransform(-27);
            using (var orbit = new Pen(Color.FromArgb(65, Accent), 1.5f))
            using (var star = new SolidBrush(Color.FromArgb(140, Accent)))
            {
                e.Graphics.DrawEllipse(orbit, -97, -34, 194, 68);
                e.Graphics.DrawEllipse(orbit, -66, -23, 132, 46);
                e.Graphics.FillEllipse(star, -4, -4, 8, 8);
                e.Graphics.FillEllipse(star, 66, 19, 6, 6);
            }
            e.Graphics.Restore(transform);
        }

        int iconSize = bounds.Height * 36 / 126;
        if (appIcon != null)
            e.Graphics.DrawIcon(appIcon, new Rectangle(23, 19, iconSize, iconSize));
        using (var label = new Font("Segoe UI", 9, FontStyle.Bold))
        using (var title = new Font("Segoe UI Semibold", 20))
        {
            TextRenderer.DrawText(e.Graphics, "NASA SCIENCE GALLERY", label, new Point(70, 25), Accent);
            TextRenderer.DrawText(e.Graphics, Product, title, new Point(22, bounds.Height * 61 / 126), Ink);
        }
        using (var line = new Pen(Border))
            e.Graphics.DrawLine(line, 0, bounds.Height - 1, bounds.Width, bounds.Height - 1);
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!quitting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (timer != null) timer.Dispose();
            if (tray != null)
            {
                tray.Visible = false;
                if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose();
                tray.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}
