using PowerSwitch.Core.Layouts;

namespace PowerSwitch.Core.Translation;

public static class FallbackLayoutCatalog
{
    private readonly record struct KeyStroke(string Key, bool Shift = false, bool AltGr = false);

    private sealed class LayoutMap
    {
        private readonly Dictionary<KeyStroke, string> outputByKey = new();
        private readonly Dictionary<string, KeyStroke> keyByOutput = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<KeyStroke, string> OutputByKey => outputByKey;
        public IReadOnlyDictionary<string, KeyStroke> KeyByOutput => keyByOutput;

        public void Add(string key, string normal, string? shifted = null, string? altGr = null)
        {
            Add(new KeyStroke(key), normal);
            if (shifted is not null)
            {
                Add(new KeyStroke(key, Shift: true), shifted);
            }

            if (altGr is not null)
            {
                Add(new KeyStroke(key, AltGr: true), altGr);
            }
        }

        private void Add(KeyStroke stroke, string output)
        {
            outputByKey[stroke] = output;
            keyByOutput.TryAdd(output, stroke);
        }
    }

    private static readonly IReadOnlyDictionary<KeyboardLayoutKind, LayoutMap> Layouts = CreateLayouts();

    public static string? Convert(
        string text,
        KeyboardLayoutKind source,
        KeyboardLayoutKind target,
        out IReadOnlyList<char> unmappedCharacters)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!Layouts.TryGetValue(source, out var sourceMap) || !Layouts.TryGetValue(target, out var targetMap))
        {
            unmappedCharacters = text.Distinct().ToArray();
            return null;
        }

        var isTargetArabic = target is KeyboardLayoutKind.Arabic101 or KeyboardLayoutKind.Arabic102;
        var output = new System.Text.StringBuilder(text.Length);
        var unmapped = new List<char>();
        for (var index = 0; index < text.Length;)
        {
            var sourceKey = FindLongestSourceKey(text, index, sourceMap);
            if (sourceKey.HasValue)
            {
                if (targetMap.OutputByKey.TryGetValue(sourceKey.Value, out var translated))
                {
                    output.Append(translated);
                }
                else
                {
                    var sourceText = sourceMap.OutputByKey[sourceKey.Value];
                    output.Append(sourceText);
                    AddUnmapped(unmapped, sourceText);
                }

                index += sourceMap.OutputByKey[sourceKey.Value].Length;
                continue;
            }

            var character = text[index++];
            var value = character.ToString();

            if (isTargetArabic && char.IsAsciiLetter(character))
            {
                var lowerVal = char.ToLowerInvariant(character).ToString();
                if (sourceMap.KeyByOutput.TryGetValue(lowerVal, out var letterKey))
                {
                    var unshiftedKey = new KeyStroke(letterKey.Key, Shift: false, AltGr: false);
                    if (targetMap.OutputByKey.TryGetValue(unshiftedKey, out var targetLetter))
                    {
                        output.Append(targetLetter);
                        continue;
                    }
                }
            }

            if (sourceMap.KeyByOutput.TryGetValue(value, out var key) &&
                targetMap.OutputByKey.TryGetValue(key, out var targetText))
            {
                output.Append(targetText);
            }
            else
            {
                output.Append(character);
                AddUnmapped(unmapped, value);
            }
        }

        unmappedCharacters = unmapped;
        return output.ToString();
    }

    internal static bool TryGetKey(string output, KeyboardLayoutKind layout, out string key, out bool shift, out bool altGr)
    {
        if (Layouts.TryGetValue(layout, out var map) && map.KeyByOutput.TryGetValue(output, out var stroke))
        {
            key = stroke.Key;
            shift = stroke.Shift;
            altGr = stroke.AltGr;
            return true;
        }

        key = string.Empty;
        shift = false;
        altGr = false;
        return false;
    }

    internal static bool TryGetOutput(string key, bool shift, bool altGr, KeyboardLayoutKind layout, out string output)
    {
        if (Layouts.TryGetValue(layout, out var map) &&
            map.OutputByKey.TryGetValue(new KeyStroke(key, shift, altGr), out output!))
        {
            return true;
        }

        output = string.Empty;
        return false;
    }

    private static KeyStroke? FindLongestSourceKey(string text, int start, LayoutMap sourceMap)
    {
        KeyStroke? longest = null;
        var longestLength = 1;
        foreach (var (key, value) in sourceMap.OutputByKey)
        {
            if (value.Length <= longestLength || start + value.Length > text.Length)
            {
                continue;
            }

            if (text.AsSpan(start, value.Length).SequenceEqual(value.AsSpan()))
            {
                longest = key;
                longestLength = value.Length;
            }
        }

        return longest;
    }

    private static void AddUnmapped(List<char> unmapped, string value)
    {
        foreach (var character in value)
        {
            if (!unmapped.Contains(character))
            {
                unmapped.Add(character);
            }
        }
    }

    private static IReadOnlyDictionary<KeyboardLayoutKind, LayoutMap> CreateLayouts()
    {
        var english = CreateEnglish();
        return new Dictionary<KeyboardLayoutKind, LayoutMap>
        {
            [KeyboardLayoutKind.EnglishQwerty] = english,
            [KeyboardLayoutKind.EnglishQwertyUk] = CreateEnglishUk(),
            [KeyboardLayoutKind.FrenchAzerty] = CreateFrench(),
            [KeyboardLayoutKind.SpanishQwerty] = CreateSpanish(),
            [KeyboardLayoutKind.Arabic101] = CreateArabic101(),
            [KeyboardLayoutKind.Arabic102] = CreateArabic102()
        };
    }

    private static LayoutMap CreateEnglish()
    {
        var map = new LayoutMap();
        AddLetterRows(map, "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM");
        AddNumberRow(map,
            ("1", "!"), ("2", "@"), ("3", "#"), ("4", "$"), ("5", "%"),
            ("6", "^"), ("7", "&"), ("8", "*"), ("9", "("), ("0", ")"));
        AddPunctuation(map);
        map.Add("OEM102", "<", ">");
        return map;
    }

    private static LayoutMap CreateEnglishUk()
    {
        var map = new LayoutMap();
        AddLetterRows(map, "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM");
        AddNumberRow(map,
            ("1", "!"), ("2", "\""), ("3", "£"), ("4", "$"), ("5", "%"),
            ("6", "^"), ("7", "&"), ("8", "*"), ("9", "("), ("0", ")"));
        map.Add("OEM_MINUS", "-", "_");
        map.Add("OEM_PLUS", "=", "+");
        map.Add("OEM4", "[", "{");
        map.Add("OEM6", "]", "}");
        map.Add("OEM5", "#", "~");
        map.Add("OEM3", "`", "¬");
        map.Add("OEM1", ";", ":");
        map.Add("OEM7", "'", "@");
        map.Add("OEM_COMMA", ",", "<");
        map.Add("OEM_PERIOD", ".", ">");
        map.Add("OEM2", "/", "?");
        map.Add("OEM102", "\\", "|");
        return map;
    }

    private static LayoutMap CreateFrench()
    {
        var map = new LayoutMap();
        AddPhysicalLetters(map,
            ("Q", "a"), ("W", "z"), ("E", "e"), ("R", "r"), ("T", "t"),
            ("Y", "y"), ("U", "u"), ("I", "i"), ("O", "o"), ("P", "p"),
            ("A", "q"), ("S", "s"), ("D", "d"), ("F", "f"), ("G", "g"),
            ("H", "h"), ("J", "j"), ("K", "k"), ("L", "l"),
            ("Z", "w"), ("X", "x"), ("C", "c"), ("V", "v"), ("B", "b"),
            ("N", "n"));
        map.Add("OEM1", "m", "M");
        map.Add("M", ",", "?");

        AddNumberRow(map,
            ("&", "1"), ("é", "2"), ("\"", "3"), ("'", "4"), ("(", "5"),
            ("-", "6"), ("è", "7"), ("_", "8"), ("ç", "9"), ("à", "0"));
        map.Add("OEM4", "^", "¨");
        map.Add("OEM6", "$", "£");
        map.Add("OEM5", "*", "µ");
        map.Add("OEM_PLUS", "=", "+");
        map.Add("OEM_COMMA", ";", ".");
        map.Add("OEM_PERIOD", ":", "/");
        map.Add("OEM2", "!", "§");
        map.Add("OEM_MINUS", ")", "°");
        map.Add("OEM3", "²");
        map.Add("OEM102", "<", ">");
        map.Add("SPACE", " ");
        return map;
    }

    private static LayoutMap CreateSpanish()
    {
        var map = new LayoutMap();
        AddLetterRows(map, "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM");
        AddNumberRow(map,
            ("1", "!"), ("2", "\""), ("3", "·"), ("4", "$"), ("5", "%"),
            ("6", "&"), ("7", "/"), ("8", "("), ("9", ")"), ("0", "="));
        map.Add("OEM1", "ñ", "Ñ");
        map.Add("OEM7", "´", "¨");
        map.Add("OEM4", "º", "ª");
        map.Add("OEM6", "+", "*");
        map.Add("OEM5", "ç", "Ç");
        map.Add("OEM_PLUS", "¡", "¿");
        map.Add("OEM_MINUS", "'", "?");
        map.Add("OEM_COMMA", ",", ";");
        map.Add("OEM_PERIOD", ".", ":");
        map.Add("OEM2", "-", "_");
        map.Add("OEM102", "<", ">");
        return map;
    }

    private static LayoutMap CreateArabic()
    {
        var map = new LayoutMap();
        map.Add("Q", "ض", "َ");
        map.Add("W", "ص", "ً");
        map.Add("E", "ث", "ُ");
        map.Add("R", "ق", "ٌ");
        map.Add("T", "ف", "لإ");
        map.Add("Y", "غ", "إ");
        map.Add("U", "ع", "‘");
        map.Add("I", "ه", "÷");
        map.Add("O", "خ", "×");
        map.Add("P", "ح", "؛");
        map.Add("OEM4", "ج", "<");
        map.Add("OEM6", "د", ">");
        map.Add("A", "ش", "ِ");
        map.Add("S", "س", "ٍ");
        map.Add("D", "ي", "]");
        map.Add("F", "ب", "[");
        map.Add("G", "ل", "لأ");
        map.Add("H", "ا", "أ");
        map.Add("J", "ت", "ـ");
        map.Add("K", "ن", "،");
        map.Add("L", "م", "/");
        map.Add("OEM1", "ك", ":");
        map.Add("Z", "ئ", "~");
        map.Add("X", "ء", "ْ");
        map.Add("C", "ؤ", "}");
        map.Add("V", "ر", "{");
        map.Add("B", "لا", "لآ");
        map.Add("N", "ى", "آ");
        map.Add("M", "ة", "’");
        map.Add("OEM_COMMA", "و", ",");
        map.Add("OEM_PERIOD", "ز", ".");
        map.Add("OEM2", "ظ", "؟");
        map.Add("OEM_MINUS", "-", "_");
        map.Add("OEM_PLUS", "=", "+");
        map.Add("OEM5", "\\", "|");
        map.Add("OEM7", "ط", "\"");
        AddNumberRow(map,
            ("1", "!"), ("2", "@"), ("3", "#"), ("4", "$"), ("5", "%"),
            ("6", "^"), ("7", "&"), ("8", "*"), ("9", "("), ("0", ")"));
        map.Add("SPACE", " ");
        return map;
    }

    private static LayoutMap CreateArabic101()
    {
        var map = CreateArabic();
        map.Add("OEM3", "ذ", "ّ");
        return map;
    }

    private static LayoutMap CreateArabic102()
    {
        var map = CreateArabic();
        map.Add("OEM102", "ذ", "<");
        return map;
    }

    private static void AddLetterRows(LayoutMap map, string top, string home, string bottom)
    {
        foreach (var letter in top)
        {
            map.Add(letter.ToString(), letter.ToString().ToLowerInvariant(), letter.ToString());
        }

        foreach (var letter in home)
        {
            map.Add(letter.ToString(), letter.ToString().ToLowerInvariant(), letter.ToString());
        }

        foreach (var letter in bottom)
        {
            map.Add(letter.ToString(), letter.ToString().ToLowerInvariant(), letter.ToString());
        }

        map.Add("SPACE", " ");
    }

    private static void AddPhysicalLetters(LayoutMap map, params (string Key, string Output)[] letters)
    {
        foreach (var (key, output) in letters)
        {
            map.Add(key, output, output.ToUpperInvariant());
        }

        map.Add("SPACE", " ");
    }

    private static void AddNumberRow(LayoutMap map, params (string Normal, string Shifted)[] values)
    {
        var keys = new[] { "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D0" };
        for (var index = 0; index < values.Length; index++)
        {
            map.Add(keys[index], values[index].Normal, values[index].Shifted);
        }
    }

    private static void AddPunctuation(LayoutMap map)
    {
        map.Add("OEM_MINUS", "-", "_");
        map.Add("OEM_PLUS", "=", "+");
        map.Add("OEM4", "[", "{");
        map.Add("OEM6", "]", "}");
        map.Add("OEM5", "\\", "|");
        map.Add("OEM1", ";", ":");
        map.Add("OEM7", "'", "\"");
        map.Add("OEM_COMMA", ",", "<");
        map.Add("OEM_PERIOD", ".", ">");
        map.Add("OEM2", "/", "?");
        map.Add("OEM3", "`", "~");
    }
}
