using Vixxo.Core.Input;
using Xunit;

namespace Vixxo.Tests;

public class HotkeyBindingTests
{
    [Theory]
    [InlineData("1", 0x31, HotkeyModifiers.None, "1")]
    [InlineData("Space", 0x20, HotkeyModifiers.None, "Space")]
    [InlineData("F5", 0x74, HotkeyModifiers.None, "F5")]
    [InlineData("NumPad 3", 0x63, HotkeyModifiers.None, "NumPad 3")]
    [InlineData("NumPad3", 0x63, HotkeyModifiers.None, "NumPad 3")]
    [InlineData("A", 0x41, HotkeyModifiers.None, "A")]
    public void Parse_SingleKey_ParsesCorrectly(string input, int expectedVk, HotkeyModifiers expectedMods, string expectedText)
    {
        var binding = HotkeyBinding.Parse(input);
        Assert.NotNull(binding);
        Assert.Equal(expectedVk, binding.VirtualKey);
        Assert.Equal(expectedMods, binding.Modifiers);
        Assert.Equal(expectedText, binding.ToString());
    }

    [Theory]
    [InlineData("Ctrl + 1", 0x31, HotkeyModifiers.Control, "Ctrl + 1")]
    [InlineData("Ctrl+Alt+NumPad 5", 0x65, HotkeyModifiers.Control | HotkeyModifiers.Alt, "Ctrl + Alt + NumPad 5")]
    [InlineData("Ctrl + Shift + F8", 0x77, HotkeyModifiers.Control | HotkeyModifiers.Shift, "Ctrl + Shift + F8")]
    [InlineData("Alt + Space", 0x20, HotkeyModifiers.Alt, "Alt + Space")]
    [InlineData("Win + Shift + Z", 0x5A, HotkeyModifiers.Windows | HotkeyModifiers.Shift, "Shift + Win + Z")]
    public void Parse_CombinationKeys_ParsesCorrectly(string input, int expectedVk, HotkeyModifiers expectedMods, string expectedText)
    {
        var binding = HotkeyBinding.Parse(input);
        Assert.NotNull(binding);
        Assert.Equal(expectedVk, binding.VirtualKey);
        Assert.Equal(expectedMods, binding.Modifiers);
        Assert.Equal(expectedText, binding.ToString());
    }

    [Fact]
    public void Parse_InvalidKey_ReturnsNull()
    {
        Assert.Null(HotkeyBinding.Parse(""));
        Assert.Null(HotkeyBinding.Parse("   "));
        Assert.Null(HotkeyBinding.Parse("Ctrl + "));
        Assert.Null(HotkeyBinding.Parse("UnknownCrazyKey"));
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var b1 = new HotkeyBinding(0x31, HotkeyModifiers.Control | HotkeyModifiers.Alt);
        var b2 = HotkeyBinding.Parse("Ctrl + Alt + 1");
        Assert.Equal(b1, b2);
        Assert.True(b1 == b2);
        Assert.False(b1 != b2);
        Assert.Equal(b1.GetHashCode(), b2?.GetHashCode());
    }
}
