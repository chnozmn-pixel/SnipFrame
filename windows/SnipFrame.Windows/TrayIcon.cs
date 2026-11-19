using System.Drawing;
using Forms = System.Windows.Forms;

namespace SnipFrame;

/// <summary>Notification-area icon; keeps SnipFrame reachable when the main window is hidden.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly Icon _appIcon;

    private readonly Forms.ToolStripMenuItem _open;
    private readonly Forms.ToolStripMenuItem _region;
    private readonly Forms.ToolStripMenuItem _window;
    private readonly Forms.ToolStripMenuItem _full;
    private readonly Forms.ToolStripMenuItem _delay;
    private readonly Forms.ToolStripMenuItem _quit;

    public event Action? OpenRequested;
    public event Action? RegionRequested;
    public event Action? WindowRequested;
    public event Action? FullScreenRequested;
    public event Action? QuitRequested;
    public event Action<int>? DelayChanged;

    public TrayIcon(int delaySeconds)
    {
        _open = new Forms.ToolStripMenuItem("Open SnipFrame", null, (_, _) => OpenRequested?.Invoke());
        _region = new Forms.ToolStripMenuItem("Capture region", null, (_, _) => RegionRequested?.Invoke());
        _window = new Forms.ToolStripMenuItem("Capture window", null, (_, _) => WindowRequested?.Invoke());
        _full = new Forms.ToolStripMenuItem("Capture full screen", null, (_, _) => FullScreenRequested?.Invoke());
        _quit = new Forms.ToolStripMenuItem("Quit", null, (_, _) => QuitRequested?.Invoke());

        _delay = new Forms.ToolStripMenuItem("Delay");
        _delay.DropDownItems.Add(new Forms.ToolStripMenuItem("None", null, (_, _) => DelayChanged?.Invoke(0)));
        _delay.DropDownItems.Add(new Forms.ToolStripMenuItem("3 s", null, (_, _) => DelayChanged?.Invoke(3)));
        _delay.DropDownItems.Add(new Forms.ToolStripMenuItem("5 s", null, (_, _) => DelayChanged?.Invoke(5)));

        _menu.Items.Add(_open);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(_region);
        _menu.Items.Add(_window);
        _menu.Items.Add(_full);
        _menu.Items.Add(_delay);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(_quit);

        _appIcon = LoadAppIcon();

        _icon = new Forms.NotifyIcon
        {
            Icon = _appIcon,
            Visible = true,
            Text = "SnipFrame",
            ContextMenuStrip = _menu
        };

        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();

        ApplyLocalization(delaySeconds);
    }

    public void ApplyLocalization(int delaySeconds)
    {
        _icon.Text = L.T("app.title", "SnipFrame");
        _open.Text = L.T("tray.open", "Open SnipFrame");
        _region.Text = L.T("tray.region", "Capture region");
        _window.Text = L.T("tray.window", "Capture window");
        _full.Text = L.T("tray.fullScreen", "Capture full screen");
        _quit.Text = L.T("tray.quit", "Quit");
        _delay.Text = L.T("tray.delay", "Delay");

        var labels = new[]
        {
            L.T("delay.none", "None"),
            L.T("delay.seconds", "{0} s", 3),
            L.T("delay.seconds", "{0} s", 5)
        };

        for (var i = 0; i < _delay.DropDownItems.Count && i < labels.Length; i++)
        {
            _delay.DropDownItems[i].Text = labels[i];
        }

        var active = delaySeconds switch { 3 => 1, 5 => 2, _ => 0 };
        for (var i = 0; i < _delay.DropDownItems.Count; i++)
        {
            if (_delay.DropDownItems[i] is Forms.ToolStripMenuItem item) item.Checked = i == active;
        }
    }

    /// <summary>Uses the original macOS app icon so both builds share one identity.</summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/AppIcon.ico", UriKind.Absolute));

            if (resource is not null)
            {
                using var stream = resource.Stream;
                return new Icon(stream, 16, 16);
            }
        }
        catch
        {
            // Fall through to the generic icon rather than failing startup.
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _appIcon.Dispose();
    }
}
