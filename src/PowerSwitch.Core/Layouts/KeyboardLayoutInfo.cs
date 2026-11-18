namespace PowerSwitch.Core.Layouts;

public enum KeyboardLayoutKind
{
    Unknown,
    Arabic101,
    Arabic102,
    EnglishQwerty,
    EnglishQwertyUk,
    FrenchAzerty,
    SpanishQwerty
}

public sealed record KeyboardLayoutInfo(IntPtr Handle, ushort LanguageId, KeyboardLayoutKind Kind)
{
    public bool IsArabic => Kind is KeyboardLayoutKind.Arabic101 or KeyboardLayoutKind.Arabic102 ||
                            (LanguageId & 0x03FF) == 0x0001;

    public bool IsEnglish => Kind is KeyboardLayoutKind.EnglishQwerty or KeyboardLayoutKind.EnglishQwertyUk ||
                             (LanguageId & 0x03FF) == 0x0009;

    public string LanguageTag => Kind switch
    {
        KeyboardLayoutKind.Arabic101 or KeyboardLayoutKind.Arabic102 => "ar",
        KeyboardLayoutKind.EnglishQwerty => "en-US",
        KeyboardLayoutKind.EnglishQwertyUk => "en-GB",
        KeyboardLayoutKind.FrenchAzerty => "fr-FR",
        KeyboardLayoutKind.SpanishQwerty => "es",
        _ => $"und-{LanguageId:X4}"
    };

    public static KeyboardLayoutInfo FromHandle(IntPtr handle)
    {
        var languageId = unchecked((ushort)handle.ToInt64());
        var primaryLanguageId = (ushort)(languageId & 0x03FF);
        var kind = primaryLanguageId switch
        {
            0x0001 => KeyboardLayoutKind.Arabic101,
            0x0009 when languageId == 0x0809 => KeyboardLayoutKind.EnglishQwertyUk,
            0x0009 => KeyboardLayoutKind.EnglishQwerty,
            0x000C => KeyboardLayoutKind.FrenchAzerty,
            0x000A => KeyboardLayoutKind.SpanishQwerty,
            _ => KeyboardLayoutKind.Unknown
        };

        return new KeyboardLayoutInfo(handle, languageId, kind);
    }
}
