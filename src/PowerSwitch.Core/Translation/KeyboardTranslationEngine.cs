using System.Text;
using PowerSwitch.Core.Interop;
using PowerSwitch.Core.Layouts;

namespace PowerSwitch.Core.Translation;

public sealed class KeyboardTranslationEngine
{
    public IReadOnlyList<KeyboardLayoutInfo> FindLayoutsThatCanType(char character)
    {
        if (char.IsSurrogate(character))
        {
            return Array.Empty<KeyboardLayoutInfo>();
        }

        return KeyboardLayoutCatalog.GetInstalledLayouts()
            .Where(layout => TryResolveSourceKey(character, layout.Handle, out _, out _))
            .ToArray();
    }

    public TranslationResult Translate(string text, IntPtr sourceLayout, IntPtr targetLayout)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (sourceLayout == IntPtr.Zero || targetLayout == IntPtr.Zero)
        {
            throw new ArgumentException("Source and target keyboard layout handles must be nonzero.");
        }

        var sourceInfo = KeyboardLayoutInfo.FromHandle(sourceLayout);
        var targetInfo = KeyboardLayoutInfo.FromHandle(targetLayout);
        return Translate(text, sourceInfo, targetInfo);
    }

    public TranslationResult Translate(string text, KeyboardLayoutInfo source, KeyboardLayoutInfo target)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (source.Handle == IntPtr.Zero || target.Handle == IntPtr.Zero)
        {
            throw new ArgumentException("Source and target keyboard layout handles must be nonzero.");
        }

        var output = new StringBuilder(text.Length);
        var unmapped = new List<char>();
        var fallbackOnly = !OperatingSystem.IsWindows();
        var isTargetArabic = target.IsArabic;

        for (var index = 0; index < text.Length;)
        {
            if (source.Kind != KeyboardLayoutKind.Unknown && target.Kind != KeyboardLayoutKind.Unknown &&
                TryFallbackMultiCharacter(text, index, source.Kind, target.Kind, out var fallbackText, out var consumed))
            {
                output.Append(fallbackText);
                index += consumed;
                continue;
            }

            var character = text[index];
            if (!fallbackOnly &&
                TryTranslateCharacter(character, source.Handle, target.Handle, isTargetArabic, out var translated))
            {
                output.Append(translated);
                index++;
                continue;
            }

            if (source.Kind != KeyboardLayoutKind.Unknown && target.Kind != KeyboardLayoutKind.Unknown &&
                FallbackLayoutCatalog.Convert(character.ToString(), source.Kind, target.Kind, out var missed) is { } fallback)
            {
                output.Append(fallback);
                foreach (var missedCharacter in missed)
                {
                    AddUnmapped(unmapped, missedCharacter);
                }
            }
            else
            {
                output.Append(character);
                AddUnmapped(unmapped, character);
            }

            index++;
        }

        var resultText = output.ToString();
        return new TranslationResult(resultText, unmapped)
        {
            WasChanged = !string.Equals(resultText, text, StringComparison.Ordinal)
        };
    }

    public TranslationResult TranslateWithFallback(
        string text,
        KeyboardLayoutKind source,
        KeyboardLayoutKind target)
    {
        ArgumentNullException.ThrowIfNull(text);
        var converted = FallbackLayoutCatalog.Convert(text, source, target, out var unmapped);
        if (converted is null)
        {
            return new TranslationResult(text, unmapped) { WasChanged = false };
        }

        return new TranslationResult(converted, unmapped)
        {
            WasChanged = !string.Equals(converted, text, StringComparison.Ordinal)
        };
    }

    private static bool TryFallbackMultiCharacter(
        string text,
        int start,
        KeyboardLayoutKind source,
        KeyboardLayoutKind target,
        out string translated,
        out int consumed)
    {
        foreach (var candidate in new[] { "لآ", "لأ", "لإ", "لا" })
        {
            if (candidate.Length > text.Length - start ||
                !text.AsSpan(start, candidate.Length).SequenceEqual(candidate.AsSpan()) ||
                !FallbackLayoutCatalog.TryGetKey(candidate, source, out var key, out var shift, out var altGr))
            {
                continue;
            }

            if (FallbackLayoutCatalog.TryGetOutput(key, shift, altGr, target, out translated))
            {
                consumed = candidate.Length;
                return true;
            }
        }

        translated = string.Empty;
        consumed = 0;
        return false;
    }

    private static bool TryTranslateCharacter(
        char character,
        IntPtr sourceLayout,
        IntPtr targetLayout,
        bool isTargetArabic,
        out string translated)
    {
        var lookupChar = (isTargetArabic && char.IsAsciiLetter(character))
            ? char.ToLowerInvariant(character)
            : character;

        if (!TryResolveSourceKey(lookupChar, sourceLayout, out var scanCode, out var modifierState))
        {
            translated = string.Empty;
            return false;
        }

        var targetVirtualKey = NativeMethods.MapVirtualKeyEx(
            scanCode,
            NativeMethods.MapScanCodeToVkEx,
            targetLayout);
        if (targetVirtualKey == 0)
        {
            translated = string.Empty;
            return false;
        }

        if (isTargetArabic && char.IsAsciiLetter(character))
        {
            modifierState = 0;
        }

        var targetState = CreateKeyboardState(modifierState);
        return TryToUnicode(targetVirtualKey, scanCode, targetState, targetLayout, out translated);
    }

    private static bool TryResolveSourceKey(char character, IntPtr sourceLayout, out uint scanCode, out byte modifierState)
    {
        var sourceKey = NativeMethods.VkKeyScanEx(character, sourceLayout);
        if (sourceKey == -1)
        {
            scanCode = 0;
            modifierState = 0;
            return false;
        }

        var virtualKey = unchecked((byte)sourceKey);
        modifierState = unchecked((byte)(sourceKey >> 8));
        scanCode = NativeMethods.MapVirtualKeyEx(virtualKey, NativeMethods.MapVkToScanCodeEx, sourceLayout);
        if (scanCode == 0)
        {
            modifierState = 0;
            return false;
        }

        var sourceState = CreateKeyboardState(modifierState);
        if (TryToUnicode(virtualKey, scanCode, sourceState, sourceLayout, out var sourceText) &&
            sourceText.Contains(character))
        {
            return true;
        }

        scanCode = 0;
        modifierState = 0;
        return false;
    }

    private static byte[] CreateKeyboardState(byte modifierState)
    {
        var state = new byte[256];
        if ((modifierState & 0x01) != 0)
        {
            state[NativeMethods.VkShift] = 0x80;
        }

        if ((modifierState & 0x02) != 0)
        {
            state[NativeMethods.VkControl] = 0x80;
        }

        if ((modifierState & 0x04) != 0)
        {
            state[NativeMethods.VkControl] = 0x80;
            state[NativeMethods.VkMenu] = 0x80;
            state[NativeMethods.VkRightMenu] = 0x80;
        }

        return state;
    }

    private static bool TryToUnicode(
        uint virtualKey,
        uint scanCode,
        byte[] keyboardState,
        IntPtr layout,
        out string translated)
    {
        var buffer = new StringBuilder(8);
        var count = NativeMethods.ToUnicodeEx(
            virtualKey,
            scanCode,
            keyboardState,
            buffer,
            buffer.Capacity,
            NativeMethods.ToUnicodeNoKeyboardStateChange,
            layout);

        translated = buffer.ToString();
        return count > 0 && translated.Length > 0;
    }

    private static void AddUnmapped(List<char> unmapped, char character)
    {
        if (!unmapped.Contains(character))
        {
            unmapped.Add(character);
        }
    }
}
