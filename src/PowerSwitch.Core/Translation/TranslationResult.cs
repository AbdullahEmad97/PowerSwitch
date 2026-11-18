namespace PowerSwitch.Core.Translation;

public sealed record TranslationResult(string Text, IReadOnlyList<char> UnmappedCharacters)
{
    public bool WasChanged { get; init; }
}
