using System.Text;

namespace Vixxo.Core.Input;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 1,        // Win32 MOD_ALT
    Control = 2,    // Win32 MOD_CONTROL
    Shift = 4,      // Win32 MOD_SHIFT
    Windows = 8     // Win32 MOD_WIN
}

public sealed class HotkeyBinding : IEquatable<HotkeyBinding>
{
    public HotkeyModifiers Modifiers { get; init; }
    public int VirtualKey { get; init; }

    public HotkeyBinding() { }

    public HotkeyBinding(int virtualKey, HotkeyModifiers modifiers = HotkeyModifiers.None)
    {
        VirtualKey = virtualKey;
        Modifiers = modifiers;
    }

    public bool IsEmpty => VirtualKey == 0;

    public string DisplayText => ToString();

    public override string ToString()
    {
        if (VirtualKey == 0) return string.Empty;

        var sb = new StringBuilder();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) sb.Append("Ctrl + ");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) sb.Append("Alt + ");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) sb.Append("Shift + ");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) sb.Append("Win + ");

        sb.Append(GetKeyName(VirtualKey));
        return sb.ToString();
    }

    public static string GetKeyName(int vk)
    {
        // Letters A-Z
        if (vk >= 0x41 && vk <= 0x5A)
            return ((char)vk).ToString();

        // Numbers 0-9
        if (vk >= 0x30 && vk <= 0x39)
            return ((char)vk).ToString();

        // Numpad 0-9
        if (vk >= 0x60 && vk <= 0x69)
            return $"NumPad {vk - 0x60}";

        // Function keys F1-F24
        if (vk >= 0x70 && vk <= 0x87)
            return $"F{vk - 0x70 + 1}";

        return vk switch
        {
            0x20 => "Space",
            0x0D => "Enter",
            0x1B => "Esc",
            0x09 => "Tab",
            0x08 => "Backspace",
            0x2E => "Delete",
            0x2D => "Insert",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x26 => "Up",
            0x28 => "Down",
            0x25 => "Left",
            0x27 => "Right",
            0x6A => "NumPad *",
            0x6B => "NumPad +",
            0x6D => "NumPad -",
            0x6E => "NumPad .",
            0x6F => "NumPad /",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => $"Key(0x{vk:X2})"
        };
    }

    public static int GetVirtualKeyFromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        string clean = name.Trim();

        // Single alphanumeric char
        if (clean.Length == 1)
        {
            char c = char.ToUpperInvariant(clean[0]);
            if (c >= 'A' && c <= 'Z') return c;
            if (c >= '0' && c <= '9') return c;
        }

        // Numpad e.g. "NumPad 1" or "NumPad1"
        if (clean.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase))
        {
            string numPart = clean[6..].Trim();
            if (int.TryParse(numPart, out int num) && num >= 0 && num <= 9)
                return 0x60 + num;
            if (numPart == "*") return 0x6A;
            if (numPart == "+") return 0x6B;
            if (numPart == "-") return 0x6D;
            if (numPart == ".") return 0x6E;
            if (numPart == "/") return 0x6F;
        }

        // F-keys e.g. "F1" - "F24"
        if (clean.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(clean[1..], out int fNum) && fNum >= 1 && fNum <= 24)
        {
            return 0x70 + (fNum - 1);
        }

        return clean.ToLowerInvariant() switch
        {
            "space" => 0x20,
            "enter" or "return" => 0x0D,
            "esc" or "escape" => 0x1B,
            "tab" => 0x09,
            "backspace" or "back" => 0x08,
            "del" or "delete" => 0x2E,
            "ins" or "insert" => 0x2D,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            _ => 0
        };
    }

    public static HotkeyBinding? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var parts = text.Split(new[] { '+', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;

        HotkeyModifiers mods = HotkeyModifiers.None;
        int vk = 0;

        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i];
            string lower = p.ToLowerInvariant();

            if (lower is "ctrl" or "control") mods |= HotkeyModifiers.Control;
            else if (lower is "alt") mods |= HotkeyModifiers.Alt;
            else if (lower is "shift") mods |= HotkeyModifiers.Shift;
            else if (lower is "win" or "windows") mods |= HotkeyModifiers.Windows;
            else
            {
                // This is the primary key
                vk = GetVirtualKeyFromName(p);
            }
        }

        if (vk == 0) return null;
        return new HotkeyBinding(vk, mods);
    }

    public bool Equals(HotkeyBinding? other)
    {
        if (other is null) return false;
        return VirtualKey == other.VirtualKey && Modifiers == other.Modifiers;
    }

    public override bool Equals(object? obj) => Equals(obj as HotkeyBinding);

    public override int GetHashCode() => HashCode.Combine(VirtualKey, (uint)Modifiers);

    public static bool operator ==(HotkeyBinding? left, HotkeyBinding? right) => Equals(left, right);
    public static bool operator !=(HotkeyBinding? left, HotkeyBinding? right) => !Equals(left, right);
}
