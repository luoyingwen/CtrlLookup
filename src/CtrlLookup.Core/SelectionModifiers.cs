namespace CtrlLookup.Core;

[Flags]
public enum ModifierKey : byte
{
    LeftShift = 1, RightShift = 2, LeftControl = 4, RightControl = 8,
    LeftAlt = 16, RightAlt = 32, LeftWindows = 64, RightWindows = 128
}

public sealed class SelectionModifiers(ModifierKey held)
{
    public bool Shift => (held & (ModifierKey.LeftShift | ModifierKey.RightShift)) != 0;
    public bool Control => (held & (ModifierKey.LeftControl | ModifierKey.RightControl)) != 0;

    public KeyAction? OnPacket(ushort key, ushort scanCode, ushort flags)
    {
        var extended = (flags & 2) != 0;
        var released = (flags & 1) != 0;
        ModifierKey? modifier = key switch
        {
            0x10 => scanCode == 0x36 ? ModifierKey.RightShift : ModifierKey.LeftShift,
            0x11 => extended ? ModifierKey.RightControl : ModifierKey.LeftControl,
            0x12 => extended ? ModifierKey.RightAlt : ModifierKey.LeftAlt,
            0xA0 => ModifierKey.LeftShift, 0xA1 => ModifierKey.RightShift,
            0xA2 => ModifierKey.LeftControl, 0xA3 => ModifierKey.RightControl,
            0xA4 => ModifierKey.LeftAlt, 0xA5 => ModifierKey.RightAlt,
            0x5B => ModifierKey.LeftWindows, 0x5C => ModifierKey.RightWindows,
            _ => null
        };
        if (modifier is { } bit)
        {
            held = released ? held & ~bit : held | bit;
            return null;
        }
        if (released) return null;
        var alternate = (held & (ModifierKey.LeftAlt | ModifierKey.RightAlt | ModifierKey.LeftWindows | ModifierKey.RightWindows)) != 0;
        return SelectionKeys.Classify(key, Shift, Control, alternate);
    }
}
