namespace EverythingLabeled.Tray;

public enum HotkeyParseKind
{
    Register,
    Disabled,
    FallbackDefault,
}

public readonly record struct HotkeySpec(uint Modifiers, uint VirtualKey);

public static class HotkeyParser
{
    public const string DefaultChord = "Alt+L";

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint VkSpace = 0x20;
    private const uint VkF1 = 0x70;

    private static readonly HotkeySpec DefaultSpec = new(ModAlt, (uint)'L');

    /// <summary>
    /// 空/空白或 none → Disabled。
    /// 合法 → Register + Spec。
    /// 非法非空 → FallbackDefault + Alt+L 的 Spec。
    /// </summary>
    public static HotkeyParseKind Parse(string? raw, out HotkeySpec spec)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            spec = default;
            return HotkeyParseKind.Disabled;
        }

        var trimmed = raw.Trim();
        if (string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase))
        {
            spec = default;
            return HotkeyParseKind.Disabled;
        }

        if (!TryParseChord(trimmed, out spec))
        {
            spec = DefaultSpec;
            return HotkeyParseKind.FallbackDefault;
        }

        return HotkeyParseKind.Register;
    }

    private static bool TryParseChord(string raw, out HotkeySpec spec)
    {
        spec = default;
        var parts = raw.Split('+');
        if (parts.Length < 2)
            return false;

        uint modifiers = 0;
        uint? virtualKey = null;

        foreach (var part in parts)
        {
            var token = part.Trim();
            if (token.Length == 0)
                return false;

            if (TryParseModifier(token, out var mod))
            {
                modifiers |= mod;
                continue;
            }

            if (TryParseKey(token, out var vk))
            {
                if (virtualKey is not null)
                    return false;
                virtualKey = vk;
                continue;
            }

            return false;
        }

        if (modifiers == 0 || virtualKey is null)
            return false;

        spec = new HotkeySpec(modifiers, virtualKey.Value);
        return true;
    }

    private static bool TryParseModifier(string token, out uint modifier)
    {
        if (string.Equals(token, "Ctrl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "Control", StringComparison.OrdinalIgnoreCase))
        {
            modifier = ModControl;
            return true;
        }

        if (string.Equals(token, "Alt", StringComparison.OrdinalIgnoreCase))
        {
            modifier = ModAlt;
            return true;
        }

        if (string.Equals(token, "Shift", StringComparison.OrdinalIgnoreCase))
        {
            modifier = ModShift;
            return true;
        }

        if (string.Equals(token, "Win", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "Windows", StringComparison.OrdinalIgnoreCase))
        {
            modifier = ModWin;
            return true;
        }

        modifier = 0;
        return false;
    }

    private static bool TryParseKey(string token, out uint virtualKey)
    {
        if (string.Equals(token, "Space", StringComparison.OrdinalIgnoreCase))
        {
            virtualKey = VkSpace;
            return true;
        }

        if (token.Length >= 2
            && (token[0] is 'F' or 'f')
            && int.TryParse(token.AsSpan(1), out var fNum)
            && fNum is >= 1 and <= 12)
        {
            virtualKey = VkF1 + (uint)(fNum - 1);
            return true;
        }

        if (token.Length == 1)
        {
            var ch = char.ToUpperInvariant(token[0]);
            if (ch is >= 'A' and <= 'Z')
            {
                virtualKey = ch;
                return true;
            }

            if (ch is >= '0' and <= '9')
            {
                virtualKey = ch;
                return true;
            }
        }

        virtualKey = 0;
        return false;
    }
}
