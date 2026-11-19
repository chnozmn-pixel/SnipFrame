using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace SnipFrame.Setup;

public partial class InstallWindow : Window
{
    private readonly string _version =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.0";

    private string _installedPath = string.Empty;

    public InstallWindow()
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        Localize();

        LocationBox.Text = Installer.DefaultDirectory;
    }

    private void Localize()
    {
        Title = Text.T("SnipFrame Setup", "SnipFrame Kurulum");
        TitleText.Text = Text.T("Install SnipFrame", "SnipFrame'i kur");
        SubtitleText.Text = Text.T($"Version {_version} for Windows", $"Windows için sürüm {_version}");

        IntroText.Text = Text.T(
            "Setup installs SnipFrame for this account, adds a Start menu shortcut and registers an " +
            "uninstall entry in Windows Settings. No administrator rights are required.",
            "Kurulum SnipFrame'i bu hesap için kurar, Başlat menüsüne kısayol ekler ve Windows " +
            "Ayarları'na kaldırma kaydı yazar. Yönetici izni gerekmez.");

        LocationLabel.Text = Text.T("Install location", "Kurulum konumu");
        BrowseButton.Content = Text.T("Browse…", "Gözat…");
        DesktopShortcut.Content = Text.T("Create a desktop shortcut", "Masaüstü kısayolu oluştur");

        HintText.Text = Text.T("SnipFrame runs in the notification area.",
            "SnipFrame bildirim alanında çalışır.");
        CancelButton.Content = Text.T("Cancel", "İptal");
        InstallButton.Content = Text.T("Install", "Kur");

        DoneTitle.Text = Text.T("You're all set", "Kurulum tamam");
        LaunchButton.Content = Text.T("Launch SnipFrame", "SnipFrame'i aç");
        CloseButton.Content = Text.T("Close", "Kapat");
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Text.T("Choose an install location", "Kurulum konumu seç"),
            InitialDirectory = Directory.Exists(LocationBox.Text)
                ? LocationBox.Text
                : Installer.DefaultDirectory
        };

        if (dialog.ShowDialog(this) == true) LocationBox.Text = dialog.FolderName;
    }

    private async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        var directory = LocationBox.Text.Trim();
        if (directory.Length == 0) directory = Installer.DefaultDirectory;
        LocationBox.Text = directory;

        SetBusy(true);
        _installedPath = directory;

        var progress = new Progress<InstallProgress>(state =>
        {
            Progress.Value = Math.Clamp(state.Fraction, 0, 1) * 100;
            ProgressText.Text = state.Text;
        });

        try
        {
            await Task.Run(() => Installer.Install(directory, DesktopShortcut.IsChecked == true, _version, progress));
            ShowDone();
        }
        catch (Exception error)
        {
            SetBusy(false);
            MessageBox.Show(this, error.Message,
                Text.T("Installation failed", "Kurulum başarısız oldu"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetBusy(bool busy)
    {
        InstallButton.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
        LocationBox.IsEnabled = !busy;
        DesktopShortcut.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
        ProgressPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowDone()
    {
        ActionRow.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;
        DonePanel.Visibility = Visibility.Visible;

        DoneBody.Text = Text.T(
            $"SnipFrame {_version} is installed. Press {HotkeyHint} anywhere, or start it from the Start menu.",
            $"SnipFrame {_version} kuruldu. Her yerde {HotkeyHint} tuşlarına basabilir veya Başlat menüsünden açabilirsiniz.");

        InstallButton.Visibility = Visibility.Collapsed;
    }

    private string HotkeyHint => "Ctrl+Shift+X";

    private void OnLaunchClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var executable = Path.Combine(_installedPath, "SnipFrame.exe");
            if (File.Exists(executable))
            {
                Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
            }
        }
        catch
        {
            // Launching is a convenience; the app is already installed.
        }

        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
