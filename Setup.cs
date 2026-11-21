using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Setup
{
    private const string Product = "Hubble Nebulae Wallpapers";
    private const string RegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\HubbleNebulaeWallpapers";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--install")
        {
            try { Install(); return 0; }
            catch { return 1; }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using (var window = new Form())
        using (var panel = new TableLayoutPanel())
        using (var message = new Label())
        using (var progress = new ProgressBar())
        using (var button = new Button())
        {
            window.Text = Product + " Setup";
            window.StartPosition = FormStartPosition.CenterScreen;
            window.ClientSize = new Size(480, 230);
            window.FormBorderStyle = FormBorderStyle.FixedDialog;
            window.MaximizeBox = false;

            panel.Dock = DockStyle.Fill;
            panel.Padding = new Padding(20);
            panel.ColumnCount = 1;
            panel.RowCount = 3;
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            window.Controls.Add(panel);

            message.Text = "Download images from NASA's Hubble Nebulae gallery and set Windows to shuffle them every 30 minutes. A daily sync will check for new images.\r\n\r\nThe first download can take a few minutes.";
            message.Dock = DockStyle.Fill;
            message.AutoEllipsis = true;
            panel.Controls.Add(message);
            progress.Dock = DockStyle.Top;
            progress.Style = ProgressBarStyle.Marquee;
            progress.Visible = false;
            panel.Controls.Add(progress);
            button.Text = "Install";
            button.Anchor = AnchorStyles.Right;
            button.AutoSize = true;
            panel.Controls.Add(button);
            window.AcceptButton = button;

            bool running = false;
            bool installed = false;
            window.FormClosing += (sender, e) => { if (running) e.Cancel = true; };
            button.Click += async (sender, e) =>
            {
                if (installed) { window.Close(); return; }
                running = true;
                button.Enabled = false;
                progress.Visible = true;
                message.Text = "Downloading images and configuring Windows. This may take a few minutes.";
                try
                {
                    await Task.Run((Action)Install);
                    message.Text = "Installation complete. Windows will change wallpapers every 30 minutes.\r\n\r\nYou can remove the daily sync from Settings > Apps.";
                    button.Text = "Finish";
                    installed = true;
                }
                catch (Exception ex)
                {
                    message.Text = "Installation failed. You can try again.";
                    MessageBox.Show(window, ex.Message, Product, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    progress.Visible = false;
                    button.Enabled = true;
                    running = false;
                }
            };
            Application.Run(window);
        }
        return 0;
    }

    private static void Install()
    {
        string temp = Path.Combine(Path.GetTempPath(), "HubbleNebulae-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("WallpaperPayload"))
            using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
            {
                foreach (string name in new[] { "HubbleNebulae.ps1", "WindowsSlideshow.cs" })
                {
                    using (var source = archive.GetEntry(name).Open())
                    using (var destination = File.Create(Path.Combine(temp, name)))
                        source.CopyTo(destination);
                }
            }

            string powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            const string options = "-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File ";
            var errors = new System.Text.StringBuilder();
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(powershell,
                    options + Quote(Path.Combine(temp, "HubbleNebulae.ps1")) + " -Action Install");
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardError = true;
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null) errors.AppendLine(e.Data);
                };
                process.Start();
                process.BeginErrorReadLine();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Setup could not activate the wallpaper collection.\r\n\r\n" + errors.ToString().Trim());
            }

            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NebulosasHubble");
            string script = Path.Combine(data, @"App\HubbleNebulae.ps1");
            using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                key.SetValue("DisplayName", Product);
                key.SetValue("DisplayVersion", Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
                key.SetValue("Publisher", "jldevstudio");
                key.SetValue("URLInfoAbout", "https://github.com/jldevstudio/hubble-nebulae-wallpapers");
                key.SetValue("UninstallString", Quote(powershell) + " " + options + Quote(script) + " -Action Uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }
        finally
        {
            foreach (string name in new[] { "HubbleNebulae.ps1", "WindowsSlideshow.cs" })
                File.Delete(Path.Combine(temp, name));
            Directory.Delete(temp);
        }
    }

    private static string Quote(string path)
    {
        return "\"" + path + "\"";
    }
}
