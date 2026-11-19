using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace SnipFrame;

public readonly record struct HotkeyBinding(ModifierKeys Modifiers, Key Key)
{
    public override string ToString() => HotkeyManager.Format(this);
}

/// <summary>Registers one system-wide hotkey through RegisterHotKey.</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x5A17;

    private readonly HwndSource _source;
    public event Action? Pressed;

    public HotkeyManager(Window window)
    {
        var helper = new WindowInteropHelper(window);
        var handle = helper.EnsureHandle();
        _source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("No window handle for the hotkey.");
        _source.AddHook(Hook);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }

        return IntPtr.Zero;
    }

    public bool TryRegister(HotkeyBinding binding)
    {
        Unregister();
        if (!IsValid(binding)) return false;
        return RegisterHotKey(_source.Handle, HotkeyId, ToWin32(binding.Modifiers), (uint)KeyInterop.VirtualKeyFromKey(binding.Key));
    }

    public void Unregister() => UnregisterHotKey(_source.Handle, HotkeyId);

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(Hook);
    }

    public static bool IsValid(HotkeyBinding binding)
    {
        if (binding.Key == Key.None) return false;
        if (binding.Modifiers != ModifierKeys.None) return true;
        return binding.Key >= Key.F1 && binding.Key <= Key.F24;
    }

    public static string Format(HotkeyBinding binding)
    {
        var parts = new List<string>();
        if (binding.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (binding.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (binding.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (binding.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(binding.Key));
        return string.Join("+", parts);
    }

    public static HotkeyBinding Parse(string text, HotkeyBinding fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;

        var modifiers = ModifierKeys.None;
        Key key = Key.None;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    key = ParseKey(raw);
                    break;
            }
        }

        var binding = new HotkeyBinding(modifiers, key);
        return IsValid(binding) ? binding : fallback;
    }

    public static HotkeyBinding FromKeyEvent(Key key, ModifierKeys modifiers)
    {
        var normalized = key == Key.System ? Key.None : key;
        return new HotkeyBinding(modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows), normalized);
    }

    private static Key ParseKey(string token)
    {
        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z') return Key.A + (c - 'A');
            if (c is >= '0' and <= '9') return Key.D0 + (c - '0');
        }

        return Enum.TryParse<Key>(token, true, out var key) ? key : Key.None;
    }

    private static string KeyName(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return ((char)('A' + (key - Key.A))).ToString();
        if (key is >= Key.D0 and <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
        return key.ToString();
    }

    private static uint ToWin32(ModifierKeys modifiers)
    {
        uint value = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) value |= 0x0001;
        if (modifiers.HasFlag(ModifierKeys.Control)) value |= 0x0002;
        if (modifiers.HasFlag(ModifierKeys.Shift)) value |= 0x0004;
        if (modifiers.HasFlag(ModifierKeys.Windows)) value |= 0x0008;
        return value;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
