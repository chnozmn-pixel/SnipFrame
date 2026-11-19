using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace SnipFrame;

public partial class MainWindow : Window
{
    private static readonly (EditorTool Tool, string Glyph)[] ToolGlyphs =
    {
        (EditorTool.Pen, "✎"),
        (EditorTool.Highlighter, "▍"),
        (EditorTool.Arrow, "↗"),
        (EditorTool.Rectangle, "▭"),
        (EditorTool.Ellipse, "◯"),
        (EditorTool.Mosaic, "▦"),
        (EditorTool.Text, "A"),
        (EditorTool.Eraser, "⌫")
    };

    private readonly List<ToggleButton> _toolButtons = new();
    private bool _loading = true;
    private bool _recordingHotkey;

    public AppSettings Settings { get; }
    public bool AllowClose { get; set; }

    public event Action? RegionRequested;
    public event Action? WindowRequested;
    public event Action? FullScreenRequested;
    public event Action<HotkeyBinding>? HotkeyChanged;
    public event Action<string>? LanguageChanged;

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();

        Settings = settings;
        ApplyLocalization();
        BuildToolButtons();
        BuildDelayCombo();
        BuildLanguageCombo();

        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) => StopHotkeyRecording(silent: true);

        _loading = false;
    }

    /// <summary>Re-applies every localized string (after a language switch).</summary>
    public void ApplyLocalization()
    {
        Title = L.T("app.title", "SnipFrame");
        HeaderTitle.Text = Title;
        HeaderSubtitle.Text = L.T("app.subtitle", "Capture, mark up, share.");
        NewSnipLabel.Text = L.T("action.newSnip", "New Snip");
        NewSnipHotkey.Text = HotkeyManager.Format(HotkeyManager.Parse(Settings.Hotkey, new HotkeyBinding(ModifierKeys.Control | ModifierKeys.Shift, Key.X)));

        WindowCardTitle.Text = L.T("action.window", "Window");
        WindowCardHint.Text = L.T("action.window.hint", "Click a window");
        FullCardTitle.Text = L.T("action.fullScreen", "Full Screen");
        FullCardHint.Text = L.T("action.fullScreen.hint", "Capture the whole display");

        ToolLabel.Text = L.T("label.tool", "Tool");
        DelayLabel.Text = L.T("label.delay", "Delay");
        ShortcutLabel.Text = L.T("label.shortcut", "Shortcut");
        LanguageLabel.Text = L.T("label.language", "Language");
        ResetButton.Content = L.T("action.reset", "Reset");

        ShortcutText.Text = HotkeyManager.Format(HotkeyManager.Parse(Settings.Hotkey, new HotkeyBinding(ModifierKeys.Control | ModifierKeys.Shift, Key.X)));

        BuildToolButtons();
    }

    private void BuildToolButtons()
    {
        ToolPanel.Children.Clear();
        _toolButtons.Clear();

        foreach (var (tool, glyph) in ToolGlyphs)
        {
            var button = new ToggleButton
            {
                Content = glyph,
                ToolTip = ToolNames.Of(tool),
                Tag = tool,
                IsChecked = tool == Settings.Tool,
                Style = (Style)FindResource("ToolToggle")
            };

            button.Click += OnToolClick;
            _toolButtons.Add(button);
            ToolPanel.Children.Add(button);
        }
    }

    private void BuildDelayCombo()
    {
        var previous = _loading;
        _loading = true;

        DelayCombo.Items.Clear();
        DelayCombo.Items.Add(new ComboBoxItem { Content = L.T("delay.none", "None"), Tag = 0 });
        DelayCombo.Items.Add(new ComboBoxItem { Content = L.T("delay.seconds", "{0} s", 3), Tag = 3 });
        DelayCombo.Items.Add(new ComboBoxItem { Content = L.T("delay.seconds", "{0} s", 5), Tag = 5 });
        DelayCombo.SelectedIndex = Settings.DelaySeconds switch { 3 => 1, 5 => 2, _ => 0 };

        _loading = previous;
    }

    private void BuildLanguageCombo()
    {
        var previous = _loading;
        _loading = true;

        LanguageCombo.Items.Clear();
        LanguageCombo.Items.Add(new ComboBoxItem { Content = L.T("language.system", "System language"), Tag = "system" });
        LanguageCombo.Items.Add(new ComboBoxItem { Content = L.T("language.en", "English"), Tag = "en" });
        LanguageCombo.Items.Add(new ComboBoxItem { Content = L.T("language.tr", "Türkçe"), Tag = "tr" });
        LanguageCombo.SelectedIndex = Settings.Language switch { "en" => 1, "tr" => 2, _ => 0 };

        _loading = previous;
    }

    // ---------- capture ----------

    private void OnNewSnipClick(object sender, RoutedEventArgs e) => RegionRequested?.Invoke();

    private void OnWindowCardClick(object sender, MouseButtonEventArgs e) => WindowRequested?.Invoke();

    private void OnFullCardClick(object sender, MouseButtonEventArgs e) => FullScreenRequested?.Invoke();

    // ---------- settings ----------

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked || clicked.Tag is not EditorTool tool) return;

        foreach (var button in _toolButtons) button.IsChecked = ReferenceEquals(button, clicked);
        Settings.Tool = tool;
        Settings.Save();
    }

    private void OnDelayChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (DelayCombo.SelectedItem is not ComboBoxItem item || item.Tag is not int seconds) return;

        Settings.DelaySeconds = seconds;
        Settings.Save();
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (LanguageCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string language) return;

        Settings.Language = language;
        Settings.Save();
        LanguageChanged?.Invoke(language);
    }

    // ---------- shortcut ----------

    private void OnShortcutClick(object sender, MouseButtonEventArgs e)
    {
        _recordingHotkey = true;
        ShortcutText.Text = "Press keys…";
        ShortcutBox.BorderBrush = (System.Windows.Media.Brush)FindResource("TextBrush");
        Focus();
        Keyboard.Focus(this);
    }

    private void StopHotkeyRecording(bool silent)
    {
        if (!_recordingHotkey) return;
        _recordingHotkey = false;
        ShortcutBox.BorderBrush = (System.Windows.Media.Brush)FindResource("StrokeBrush");

        if (silent)
            ShortcutText.Text = HotkeyManager.Format(HotkeyManager.Parse(Settings.Hotkey,
                new HotkeyBinding(ModifierKeys.Control | ModifierKeys.Shift, Key.X)));
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recordingHotkey) return;

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            StopHotkeyRecording(silent: false);
            return;
        }

        var binding = HotkeyManager.FromKeyEvent(e.Key, Keyboard.Modifiers);
        if (!HotkeyManager.IsValid(binding))
        {
            ShortcutText.Text = "Ctrl/Alt/Shift + key";
            return;
        }

        Settings.Hotkey = HotkeyManager.Format(binding);
        Settings.Save();
        StopHotkeyRecording(silent: false);
        ShortcutText.Text = Settings.Hotkey;
        HotkeyChanged?.Invoke(binding);
    }

    private void OnResetShortcutClick(object sender, RoutedEventArgs e)
    {
        var binding = HotkeyManager.Parse(AppSettings.DefaultHotkey,
            new HotkeyBinding(ModifierKeys.Control | ModifierKeys.Shift, Key.X));

        Settings.Hotkey = HotkeyManager.Format(binding);
        Settings.Save();
        ShortcutText.Text = Settings.Hotkey;
        NewSnipHotkey.Text = Settings.Hotkey;
        HotkeyChanged?.Invoke(binding);
    }

    /// <summary>Re-selects the delay combo after the value changed elsewhere (tray menu).</summary>
    public void RefreshDelaySelection()
    {
        _loading = true;
        DelayCombo.SelectedIndex = Settings.DelaySeconds switch { 3 => 1, 5 => 2, _ => 0 };
        _loading = false;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}
