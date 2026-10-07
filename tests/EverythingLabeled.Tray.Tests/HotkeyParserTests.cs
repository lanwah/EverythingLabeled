using EverythingLabeled.Tray;

namespace EverythingLabeled.Tray.Tests;

public sealed class HotkeyParserTests
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkL = (uint)'L';
    private const uint VkF12 = 0x7B;
    private const uint VkSpace = 0x20;
    private static readonly HotkeySpec AltL = new(ModAlt, VkL);

    [Theory]
    [InlineData("Alt+L")]
    [InlineData("alt + l")]
    public void Parse_alt_l_variants_register(string raw)
    {
        var kind = HotkeyParser.Parse(raw, out var spec);
        Assert.Equal(HotkeyParseKind.Register, kind);
        Assert.Equal(AltL, spec);
    }

    [Fact]
    public void Parse_ctrl_shift_f12_registers()
    {
        var kind = HotkeyParser.Parse("Ctrl+Shift+F12", out var spec);
        Assert.Equal(HotkeyParseKind.Register, kind);
        Assert.Equal(new HotkeySpec(ModControl | ModShift, VkF12), spec);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("none")]
    [InlineData(" None ")]
    [InlineData("NONE")]
    public void Parse_empty_or_none_disables(string? raw)
    {
        var kind = HotkeyParser.Parse(raw, out var spec);
        Assert.Equal(HotkeyParseKind.Disabled, kind);
        Assert.Equal(default, spec);
    }

    [Theory]
    [InlineData("Alt+Left")]
    [InlineData("Ctrl+Oem1")]
    [InlineData("Alt")]
    [InlineData("Space")]
    public void Parse_illegal_falls_back_to_alt_l(string raw)
    {
        var kind = HotkeyParser.Parse(raw, out var spec);
        Assert.Equal(HotkeyParseKind.FallbackDefault, kind);
        Assert.Equal(AltL, spec);
    }

    [Fact]
    public void Parse_ctrl_space_registers()
    {
        var kind = HotkeyParser.Parse("Ctrl+Space", out var spec);
        Assert.Equal(HotkeyParseKind.Register, kind);
        Assert.Equal(new HotkeySpec(ModControl, VkSpace), spec);
    }

    [Fact]
    public void DefaultChord_is_alt_l()
    {
        Assert.Equal("Alt+L", HotkeyParser.DefaultChord);
    }
}
