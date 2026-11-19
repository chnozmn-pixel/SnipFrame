using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SnipFrame;

public partial class App : Application
{
    private static readonly HotkeyBinding DefaultBinding =
        new(ModifierKeys.Control | ModifierKeys.Shift, Key.X);

    private static readonly Color DefaultStroke = Color.FromRgb(0x1C, 0x1C, 0x21);

    private readonly List<Window> _windows = new();
    private Mutex? _mutex;
    private MainWindow? _main;
    private HotkeyManager? _hotkeys;
    private TrayIcon? _tray;
    private CaptureOverlayWindow? _overlay;

    public AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _mutex = new Mutex(true, @"Local\SnipFrame.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        Settings = AppSettings.Load();
        L.Apply(Settings.Language);

        var main = new MainWindow(Settings);
        _main = main;
        main.RegionRequested += () => BeginCapture(CaptureMode.Region);
        main.WindowRequested += () => BeginCapture(CaptureMode.Window);
        main.FullScreenRequested += CaptureFullScreen;
        main.HotkeyChanged += RegisterHotkey;
        main.LanguageChanged += OnLanguageChanged;
        main.Show();

        _hotkeys = new HotkeyManager(main);
        _hotkeys.Pressed += () => BeginCapture(CaptureMode.Region);
        RegisterHotkey(HotkeyManager.Parse(Settings.Hotkey, DefaultBinding));

        _tray = new TrayIcon(Settings.DelaySeconds);
        _tray.OpenRequested += ShowMainWindow;
        _tray.RegionRequested += () => BeginCapture(CaptureMode.Region);
        _tray.WindowRequested += () => BeginCapture(CaptureMode.Window);
        _tray.FullScreenRequested += CaptureFullScreen;
        _tray.DelayChanged += OnTrayDelayChanged;
        _tray.QuitRequested += Quit;
    }

    // ---------- hotkey ----------

    private void RegisterHotkey(HotkeyBinding binding)
    {
        if (_hotkeys is null) return;

        if (_hotkeys.TryRegister(binding)) return;

        if (binding == DefaultBinding) return;

        // The requested combination is taken by another app: fall back to the default.
        if (_hotkeys.TryRegister(DefaultBinding))
        {
            Settings.Hotkey = AppSettings.DefaultHotkey;
            Settings.Save();
            _main?.ApplyLocalization();
        }
    }

    private void OnLanguageChanged(string language)
    {
        L.Apply(language);
        _main?.ApplyLocalization();
        _tray?.ApplyLocalization(Settings.DelaySeconds);
    }

    private void OnTrayDelayChanged(int seconds)
    {
        Settings.DelaySeconds = seconds;
        Settings.Save();
        _main?.RefreshDelaySelection();
        _tray?.ApplyLocalization(seconds);
    }

    // ---------- capture ----------

    private void BeginCapture(CaptureMode mode)
    {
        HideMainWindow();
        RunWithDelay(() => ShowOverlay(mode));
    }

    private void CaptureFullScreen()
    {
        HideMainWindow();
        RunWithDelay(() =>
        {
            var monitor = CaptureService.GetMonitorAtCursor();
            var image = CaptureService.Capture(monitor.Bounds);
            if (image is null)
            {
                ShowCaptureError();
                return;
            }

            ShowEditor(image);
        });
    }

    private void ShowOverlay(CaptureMode mode)
    {
        try
        {
            var overlay = new CaptureOverlayWindow(mode, CaptureService.GetMonitorAtCursor());
            _overlay = overlay;

            overlay.Captured += image =>
            {
                _overlay = null;
                ShowEditor(image);
            };

            overlay.Cancelled += () => _overlay = null;
            overlay.Closed += (_, _) =>
            {
                if (ReferenceEquals(_overlay, overlay)) _overlay = null;
            };

            overlay.Show();
            overlay.Activate();
        }
        catch
        {
            ShowCaptureError();
        }
    }

    private void RunWithDelay(Action action)
    {
        var seconds = Math.Max(0, Settings.DelaySeconds);
        if (seconds == 0)
        {
            action();
            return;
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    private void ShowEditor(BitmapSource image)
    {
        var editor = new EditorWindow(image, Settings.Tool, DefaultStroke);
        _windows.Add(editor);
        editor.Closed += (_, _) => _windows.Remove(editor);
        editor.Show();
        editor.Activate();
    }

    private void ShowCaptureError()
    {
        MessageBox.Show(L.T("capture.failed", "Could not capture the screen"), L.T("app.title", "SnipFrame"),
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ---------- window / lifecycle ----------

    private void ShowMainWindow()
    {
        if (_main is null) return;

        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    private void HideMainWindow()
    {
        if (_main is { IsVisible: true }) _main.Hide();
    }

    private void Quit()
    {
        _tray?.Dispose();
        _tray = null;

        _hotkeys?.Dispose();
        _hotkeys = null;

        if (_main is not null) _main.AllowClose = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
