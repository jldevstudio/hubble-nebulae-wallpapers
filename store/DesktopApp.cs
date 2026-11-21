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
                string result = new Gallery(args[1]).SyncAsync().GetAwaiter().GetResult();
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

        Text = Product;
        ClientSize = new Size(525, 302);
        MinimumSize = new Size(510, 325);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Use images from NASA’s Hubble Nebulae gallery as your Windows wallpaper. Windows shuffles the downloaded collection every 30 minutes."
        });

        layoutPicker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 315, AccessibleName = "Wallpaper layout" };
        layoutPicker.Items.Add("Fill screen (crop edges as needed)");
        layoutPicker.Items.Add("Blurred edges (show the whole photo)");
        layoutPicker.SelectedIndex = WallpaperCanvas.ReadLayout(dataFolder) == WallpaperLayout.BlurredEdges ? 1 : 0;
        layout.Controls.Add(layoutPicker);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        syncButton = new Button { Text = "Sync Gallery", AutoSize = true };
        applyButton = new Button { Text = "Set Wallpaper Slideshow", AutoSize = true };
        actions.Controls.Add(syncButton);
        actions.Controls.Add(applyButton);
        layout.Controls.Add(actions);

        status = new Label { Dock = DockStyle.Fill, Padding = new Padding(0, 7, 0, 0),
            Text = "The app syncs daily once you have synced or applied wallpapers. Closing the window keeps the app in the notification area. To stop it, choose Exit from its tray icon." };
        layout.Controls.Add(status);
        var startup = new LinkLabel { Text = "Open Windows Startup Apps settings", AutoSize = true };
        startup.LinkClicked += (sender, e) =>
        {
            try { System.Diagnostics.Process.Start("ms-settings:startupapps"); }
            catch (Exception error) { status.Text = error.Message; }
        };
        layout.Controls.Add(startup);

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Open Hubble Nebulae Wallpapers", null, (sender, e) => ShowWindow());
        trayMenu.Items.Add("Exit", null, (sender, e) => { quitting = true; Close(); });
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = Product, ContextMenuStrip = trayMenu, Visible = true };
        tray.DoubleClick += (sender, e) => ShowWindow();

        nextSync = ReadNextSync();
        timer = new System.Windows.Forms.Timer { Interval = 10 * 60 * 1000 };
        timer.Tick += async (sender, e) => await SyncIfDueAsync();
        timer.Start();
        Shown += async (sender, e) => await SyncIfDueAsync();
        syncButton.Click += async (sender, e) => await SyncAsync(false);
        applyButton.Click += async (sender, e) => await SyncAsync(true);
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
        return DateTime.UtcNow >= nextSync ? SyncAsync(false) : Task.FromResult(false);
    }

    private async Task SyncAsync(bool apply)
    {
        if (working)
            return;
        working = true;
        syncButton.Enabled = applyButton.Enabled = layoutPicker.Enabled = false;
        status.Text = "Checking the NASA gallery. The first download can take a few minutes.";
        try
        {
            var layout = apply
                ? (layoutPicker.SelectedIndex == 1 ? WallpaperLayout.BlurredEdges : WallpaperLayout.Fill)
                : WallpaperCanvas.ReadLayout(dataFolder);
            bool sync = !apply || !Directory.Exists(gallery.Folder) || Directory.GetFiles(gallery.Folder, "*.jpg").Length == 0;
            string summary = sync ? await Task.Run(() => gallery.SyncAsync()) : "Using images already downloaded.";
            string folder = await Task.Run(() => WallpaperCanvas.Prepare(gallery.Folder, dataFolder, layout));
            if (sync)
            {
                DateTime finished = DateTime.UtcNow;
                File.WriteAllText(statePath, finished.ToString("O"));
                nextSync = finished.AddDays(1);
            }

            status.Text = summary;
            if (apply)
            {
                WindowsSlideshow.Enable(folder, HalfHour);
                WallpaperCanvas.SaveLayout(dataFolder, layout);
                status.Text += Environment.NewLine + "Windows wallpaper slideshow enabled.";
            }
        }
        catch (Exception error)
        {
            if (nextSync != DateTime.MaxValue)
                nextSync = DateTime.UtcNow.AddHours(1);
            status.Text = error.Message;
        }
        finally
        {
            syncButton.Enabled = applyButton.Enabled = layoutPicker.Enabled = true;
            working = false;
        }
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
