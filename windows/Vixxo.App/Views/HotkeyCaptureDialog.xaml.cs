using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Vixxo.Core.Input;

namespace Vixxo.App.Views;

public partial class HotkeyCaptureDialog : Window
{
    private HotkeyBinding? _capturedBinding;
    private readonly SolidColorBrush _activeBrush = new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#00F2FE"));
    private readonly SolidColorBrush _activeTextBrush = new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#060D1A"));
    private readonly SolidColorBrush _inactiveBrush = new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0F1522"));
    private readonly SolidColorBrush _inactiveTextBrush = new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#64748B"));

    public HotkeyBinding? ResultBinding { get; private set; }

    public HotkeyCaptureDialog(string clipName, string currentShortcut)
    {
        InitializeComponent();
        SoundNameTextBlock.Text = $"Assign Hotkey: {clipName}";

        if (!string.IsNullOrWhiteSpace(currentShortcut))
        {
            _capturedBinding = HotkeyBinding.Parse(currentShortcut);
            if (_capturedBinding != null)
            {
                KeyDisplayTextBlock.Text = _capturedBinding.ToString();
                UpdateModifierBadges(_capturedBinding.Modifiers);
            }
        }

        SaveButton.IsEnabled = _capturedBinding != null && !_capturedBinding.IsEmpty;
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Determine active modifiers
        var wpfMods = Keyboard.Modifiers;
        HotkeyModifiers mods = HotkeyModifiers.None;
        if (wpfMods.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Control;
        if (wpfMods.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (wpfMods.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        if (wpfMods.HasFlag(ModifierKeys.Windows)) mods |= HotkeyModifiers.Windows;

        UpdateModifierBadges(mods);

        bool isModifierOnly = key is Key.LeftCtrl or Key.RightCtrl 
            or Key.LeftAlt or Key.RightAlt 
            or Key.LeftShift or Key.RightShift 
            or Key.LWin or Key.RWin;

        if (isModifierOnly)
        {
            KeyDisplayTextBlock.Text = mods != HotkeyModifiers.None ? $"{mods} + ..." : "Press a key...";
            e.Handled = true;
            return;
        }

        // Map WPF Key to Win32 Virtual Key code
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk > 0)
        {
            _capturedBinding = new HotkeyBinding(vk, mods);
            KeyDisplayTextBlock.Text = _capturedBinding.ToString();
            SaveButton.IsEnabled = true;
            e.Handled = true;
        }
    }

    private void UpdateModifierBadges(HotkeyModifiers mods)
    {
        SetBadge(CtrlBadge, CtrlText, mods.HasFlag(HotkeyModifiers.Control));
        SetBadge(AltBadge, AltText, mods.HasFlag(HotkeyModifiers.Alt));
        SetBadge(ShiftBadge, ShiftText, mods.HasFlag(HotkeyModifiers.Shift));
        SetBadge(WinBadge, WinText, mods.HasFlag(HotkeyModifiers.Windows));
    }

    private void SetBadge(System.Windows.Controls.Border badge, System.Windows.Controls.TextBlock text, bool active)
    {
        badge.Background = active ? _activeBrush : _inactiveBrush;
        text.Foreground = active ? _activeTextBrush : _inactiveTextBrush;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ResultBinding = _capturedBinding;
        DialogResult = true;
        Close();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        ResultBinding = null;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
