using PowerSwitch.Core.Layouts;
using PowerSwitch.Core.Translation;
using Xunit;

namespace PowerSwitch.Core.Tests;

public sealed class KeyboardTranslationEngineTests
{
    private readonly KeyboardTranslationEngine engine = new();

    [Theory]
    [InlineData("ضصثقفغ", KeyboardLayoutKind.Arabic101, KeyboardLayoutKind.EnglishQwerty, "qwerty")]
    [InlineData("qwerty", KeyboardLayoutKind.EnglishQwerty, KeyboardLayoutKind.Arabic101, "ضصثقفغ")]
    [InlineData("QWERTY", KeyboardLayoutKind.EnglishQwerty, KeyboardLayoutKind.Arabic101, "ضصثقفغ")]
    [InlineData("HELLO", KeyboardLayoutKind.EnglishQwerty, KeyboardLayoutKind.Arabic101, "اثممخ")]
    [InlineData("Hello", KeyboardLayoutKind.EnglishQwerty, KeyboardLayoutKind.Arabic101, "اثممخ")]
    [InlineData("HELLO!", KeyboardLayoutKind.EnglishQwerty, KeyboardLayoutKind.Arabic101, "اثممخ!")]
    [InlineData("TEST?", KeyboardLayoutKind.EnglishQwerty, KeyboardLayoutKind.Arabic101, "فثسف؟")]
    [InlineData("-+=_:", KeyboardLayoutKind.EnglishQwerty, KeyboardLayoutKind.Arabic101, "-+=_:")]
    [InlineData("ل", KeyboardLayoutKind.Arabic101, KeyboardLayoutKind.EnglishQwerty, "g")]
    [InlineData("لا", KeyboardLayoutKind.Arabic101, KeyboardLayoutKind.EnglishQwerty, "b")]
    [InlineData("لأ", KeyboardLayoutKind.Arabic101, KeyboardLayoutKind.EnglishQwerty, "G")]
    [InlineData("لإ", KeyboardLayoutKind.Arabic101, KeyboardLayoutKind.EnglishQwerty, "T")]
    [InlineData("لآ", KeyboardLayoutKind.Arabic101, KeyboardLayoutKind.EnglishQwerty, "B")]
    [InlineData("b", KeyboardLayoutKind.EnglishQwerty, KeyboardLayoutKind.Arabic101, "لا")]
    [InlineData("B", KeyboardLayoutKind.EnglishQwerty, KeyboardLayoutKind.Arabic101, "لا")]
    [InlineData("g", KeyboardLayoutKind.EnglishQwerty, KeyboardLayoutKind.Arabic101, "ل")]
    [InlineData("G", KeyboardLayoutKind.EnglishQwerty, KeyboardLayoutKind.Arabic101, "ل")]
    [InlineData("t", KeyboardLayoutKind.EnglishQwerty, KeyboardLayoutKind.Arabic101, "ف")]
    [InlineData("T", KeyboardLayoutKind.EnglishQwerty, KeyboardLayoutKind.Arabic101, "ف")]
    public void ConvertsArabicAndEnglish(string input, KeyboardLayoutKind source, KeyboardLayoutKind target, string expected)
    {
        Assert.Equal(expected, engine.TranslateWithFallback(input, source, target).Text);
    }

    [Theory]
    [InlineData("qwerty", KeyboardLayoutKind.EnglishQwerty, KeyboardLayoutKind.FrenchAzerty, "azerty")]
    [InlineData("azerty", KeyboardLayoutKind.FrenchAzerty, KeyboardLayoutKind.EnglishQwerty, "qwerty")]
    [InlineData("ضصثقفغ", KeyboardLayoutKind.Arabic101, KeyboardLayoutKind.FrenchAzerty, "azerty")]
    [InlineData("1234567890", KeyboardLayoutKind.EnglishQwerty, KeyboardLayoutKind.FrenchAzerty, "&é\"'(-è_çà")]
    [InlineData("&é\"'(-è_çà", KeyboardLayoutKind.FrenchAzerty, KeyboardLayoutKind.EnglishQwerty, "1234567890")]
    [InlineData(";", KeyboardLayoutKind.FrenchAzerty, KeyboardLayoutKind.EnglishQwerty, ",")]
    [InlineData("£", KeyboardLayoutKind.EnglishQwertyUk, KeyboardLayoutKind.EnglishQwerty, "#")]
    public void ConvertsEnglishAndFrench(string input, KeyboardLayoutKind source, KeyboardLayoutKind target, string expected)
    {
        Assert.Equal(expected, engine.TranslateWithFallback(input, source, target).Text);
    }

    [Fact]
    public void PreservesSpanishEnyeAcrossSpanishRoundTrip()
    {
        Assert.Equal("ñ", engine.TranslateWithFallback("ñ", KeyboardLayoutKind.SpanishQwerty, KeyboardLayoutKind.SpanishQwerty).Text);
        Assert.Equal("ñ", engine.TranslateWithFallback(";", KeyboardLayoutKind.EnglishQwerty, KeyboardLayoutKind.SpanishQwerty).Text);
        Assert.Equal(";", engine.TranslateWithFallback("ñ", KeyboardLayoutKind.SpanishQwerty, KeyboardLayoutKind.EnglishQwerty).Text);
    }

    [Theory]
    [InlineData("azerty", KeyboardLayoutKind.FrenchAzerty, KeyboardLayoutKind.SpanishQwerty, "qwerty")]
    [InlineData("qwerty", KeyboardLayoutKind.SpanishQwerty, KeyboardLayoutKind.Arabic101, "ضصثقفغ")]
    public void ConvertsAcrossArabicFrenchAndSpanish(string input, KeyboardLayoutKind source, KeyboardLayoutKind target, string expected)
    {
        Assert.Equal(expected, engine.TranslateWithFallback(input, source, target).Text);
    }

    [Fact]
    public void ConvertsArabicLigatureAndSpanishIsoKeysByPhysicalPosition()
    {
        Assert.Equal("b", engine.TranslateWithFallback("لا", KeyboardLayoutKind.Arabic101, KeyboardLayoutKind.SpanishQwerty).Text);
        Assert.Equal("B", engine.TranslateWithFallback("لآ", KeyboardLayoutKind.Arabic101, KeyboardLayoutKind.SpanishQwerty).Text);
        Assert.Equal("<", engine.TranslateWithFallback("<", KeyboardLayoutKind.SpanishQwerty, KeyboardLayoutKind.EnglishQwerty).Text);
        Assert.Equal("<", engine.TranslateWithFallback("ذ", KeyboardLayoutKind.Arabic102, KeyboardLayoutKind.EnglishQwerty).Text);
    }

    [Fact]
    public void PreservesUnknownCharactersAndReportsThem()
    {
        var result = engine.TranslateWithFallback("€", KeyboardLayoutKind.EnglishQwerty, KeyboardLayoutKind.FrenchAzerty);

        Assert.Equal("€", result.Text);
        Assert.Contains('€', result.UnmappedCharacters);
    }

    [Fact]
    public void DetectsWindowsKeyboardLanguageFromHklLowWord()
    {
        Assert.Equal(KeyboardLayoutKind.Arabic101, KeyboardLayoutInfo.FromHandle(new IntPtr(0x0401)).Kind);
        Assert.Equal(KeyboardLayoutKind.EnglishQwertyUk, KeyboardLayoutInfo.FromHandle(new IntPtr(0x0809)).Kind);
        Assert.Equal(KeyboardLayoutKind.FrenchAzerty, KeyboardLayoutInfo.FromHandle(new IntPtr(0x040C)).Kind);
        Assert.Equal(KeyboardLayoutKind.SpanishQwerty, KeyboardLayoutInfo.FromHandle(new IntPtr(0x0C0A)).Kind);
    }

    [Fact]
    public void TranslatesUppercaseEnglishLettersToCorrespondingArabicLettersWithoutSymbols()
    {
        if (!OperatingSystem.IsWindows()) return;
        var en = KeyboardLayoutCatalog.TryLoadLayout("00000409");
        var ar = KeyboardLayoutCatalog.TryLoadLayout("00000401");
        if (en is null || ar is null) return;

        Assert.Equal("اثممخ!", engine.Translate("HELLO!", en, ar).Text);
        Assert.Equal("اثممخ!", engine.Translate("hello!", en, ar).Text);
        Assert.Equal("ضصثقفغ", engine.Translate("QWERTY", en, ar).Text);
        Assert.Equal("فثسف؟", engine.Translate("TEST?", en, ar).Text);
        Assert.Equal("ش + لا = ؤ", engine.Translate("A + B = C", en, ar).Text);
        Assert.Equal("كتاب", engine.Translate(";JHF", en, ar).Text);
        Assert.Equal("كتاب", engine.Translate(";jhf", en, ar).Text);
    }
}
