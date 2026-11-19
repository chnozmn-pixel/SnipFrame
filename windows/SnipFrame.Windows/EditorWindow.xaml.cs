using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace SnipFrame;

public partial class EditorWindow : Window
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
    private Color _color;
    private TextBox? _textBox;
    private Point _textOrigin;
    private DispatcherTimer? _statusTimer;

    public EditorWindow(BitmapSource image, EditorTool tool, Color color)
    {
        InitializeComponent();

        _color = color;

        Title = L.T("editor.title", "SnipFrame — Edit");
        CopyButton.Content = L.T("editor.copy", "Copy");
        SaveButton.Content = L.T("editor.save", "Save…");
        UndoButton.ToolTip = L.T("editor.undo", "Undo (Ctrl+Z)");
        RedoButton.ToolTip = L.T("editor.redo", "Redo (Ctrl+Y)");
        WidthSlider.ToolTip = L.T("editor.lineWidth", "Line width");
        ColorSwatch.ToolTip = L.T("editor.color", "Color");

        Surface.BaseImage = image;
        Surface.Tool = tool;
        Surface.StrokeColor = color;
        Surface.LineWidth = WidthSlider.Value;
        Surface.Changed += UpdateHistoryButtons;
        Surface.TextRequested += BeginTextEdit;

        BuildToolButtons(tool);
        UpdateColorSwatch();
        UpdateHistoryButtons();
        SizeToImage(image);

        WidthSlider.ValueChanged += (_, _) =>
        {
            Surface.LineWidth = WidthSlider.Value;
            if (_textBox is not null) _textBox.FontSize = TextFontSize();
        };

        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) => Surface.Focus();
    }

    private double TextFontSize() => 8 + WidthSlider.Value * 2;

    private void SizeToImage(BitmapSource image)
    {
        var work = SystemParameters.WorkArea;
        Width = Math.Min(Math.Max(image.PixelWidth + 40, 780), work.Width * 0.92);
        Height = Math.Min(Math.Max(image.PixelHeight + 110, 340), work.Height * 0.92);
    }

    private void BuildToolButtons(EditorTool selected)
    {
        foreach (var (tool, glyph) in ToolGlyphs)
        {
            var button = new ToggleButton
            {
                Content = glyph,
                ToolTip = ToolNames.Of(tool),
                Tag = tool,
                IsChecked = tool == selected,
                Style = (Style)FindResource("ToolToggle")
            };

            button.Click += OnToolClick;
            _toolButtons.Add(button);
            ToolPanel.Children.Add(button);
        }
    }

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked || clicked.Tag is not EditorTool tool) return;

        foreach (var button in _toolButtons) button.IsChecked = ReferenceEquals(button, clicked);
        Surface.Tool = tool;
        Surface.Focus();
    }

    private void OnColorClick(object sender, MouseButtonEventArgs e)
    {
        using var dialog = new Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(_color.R, _color.G, _color.B)
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            _color = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
            Surface.StrokeColor = _color;
            UpdateColorSwatch();
        }

        Surface.Focus();
    }

    private void UpdateColorSwatch() => ColorSwatch.Background = new SolidColorBrush(_color);

    // ---------- actions ----------

    private void OnUndoClick(object sender, RoutedEventArgs e) => Undo();
    private void OnRedoClick(object sender, RoutedEventArgs e) => Redo();

    private void Undo()
    {
        Surface.Undo();
        UpdateHistoryButtons();
    }

    private void Redo()
    {
        Surface.Redo();
        UpdateHistoryButtons();
    }

    private void UpdateHistoryButtons()
    {
        UndoButton.IsEnabled = Surface.CanUndo;
        RedoButton.IsEnabled = Surface.CanRedo;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyToClipboard();

    private void CopyToClipboard()
    {
        var bitmap = Surface.RenderBitmap();
        if (bitmap is null) return;

        try
        {
            Clipboard.SetImage(bitmap);
            Flash(L.T("editor.copied", "Copied to clipboard ✓"));
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, L.T("editor.saveFailed", "Could not save the image"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => SaveToFile();

    private void SaveToFile()
    {
        var bitmap = Surface.RenderBitmap();
        if (bitmap is null) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = $"PNG (*.png)|*.png|{L.T("save.filter", "PNG image")} (*.png)|*.png",
            DefaultExt = ".png",
            FileName = string.Format(L.T("save.fileName", "Screenshot {0}.png"),
                DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"))
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(dialog.FileName);
            encoder.Save(stream);
            Flash(L.T("editor.saved", "Saved ✓"));
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, L.T("editor.saveFailed", "Could not save the image"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Flash(string text)
    {
        StatusText.Text = text;
        _statusTimer?.Stop();
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += (_, _) =>
        {
            StatusText.Text = string.Empty;
            _statusTimer?.Stop();
        };
        _statusTimer.Start();
    }

    // ---------- text tool ----------

    private void BeginTextEdit(Point origin)
    {
        CommitTextInput();

        _textOrigin = origin;
        var box = new TextBox
        {
            Width = 240,
            FontSize = TextFontSize(),
            Foreground = new SolidColorBrush(_color),
            Background = new SolidColorBrush(Color.FromArgb(210, 20, 20, 24)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(150, 236, 236, 241)),
            Padding = new Thickness(4, 2, 4, 2),
            AcceptsReturn = false,
            Text = string.Empty
        };

        box.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                args.Handled = true;
                CommitTextInput();
            }
            else if (args.Key == Key.Escape)
            {
                args.Handled = true;
                CancelTextInput();
            }
        };

        box.LostFocus += (_, _) => CommitTextInput();

        Canvas.SetLeft(box, origin.X);
        Canvas.SetTop(box, Math.Max(0, origin.Y - TextFontSize()));
        Overlay.Children.Add(box);
        _textBox = box;

        box.Focus();
    }

    private void CommitTextInput()
    {
        if (_textBox is null) return;

        var box = _textBox;
        _textBox = null;
        Overlay.Children.Remove(box);

        Surface.CommitText(box.Text, new Point(Canvas.GetLeft(box), Canvas.GetTop(box) + box.FontSize), _color, box.FontSize);
        Surface.Focus();
    }

    private void CancelTextInput()
    {
        if (_textBox is null) return;
        Overlay.Children.Remove(_textBox);
        _textBox = null;
        Surface.Focus();
    }

    // ---------- keyboard ----------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_textBox is not null) CancelTextInput();
            else Close();
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            e.Handled = true;
            CopyToClipboard();
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            e.Handled = true;
            SaveToFile();
            return;
        }

        if (_textBox is not null) return;

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            e.Handled = true;
            Undo();
            return;
        }

        var redoCombo = (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y) ||
                        (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z);
        if (redoCombo)
        {
            e.Handled = true;
            Redo();
        }
    }
}
