using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>Guards the centralized display-name formatter — the confirmed real-machine defect this replaces was
/// a naive PascalCase-word-splitting regex mangling the CLYR brand name into a spaced-out rendering when it
/// appeared inside an already-composed prose sentence, and separately produced incorrect acronym casing for
/// known technical terms (WSL, SDK).</summary>
public sealed class DisplayNamesTests
{
    [Theory]
    [InlineData("Wsl", "WSL")]
    [InlineData("AndroidSdkEmulators", "Android SDK Emulators")]
    [InlineData("Clyr", "CLYR")]
    [InlineData("BrowserCache", "Browser Cache")]
    [InlineData("Completed", "Completed")]
    public void FromPascalCaseSplitsWordsAndFixesKnownAcronyms(string input, string expected) =>
        Assert.Equal(expected, DisplayNames.FromPascalCase(input));

    [Theory]
    [InlineData("developer.npm.cache", "Developer npm Cache")]
    [InlineData("developer.pnpm.store", "Developer pnpm Store")]
    [InlineData("developer.nuget.packages", "Developer NuGet Packages")]
    [InlineData("browser.chrome.cache", "Browser Chrome Cache")]
    public void FromDottedIdentifierTitleCasesWordsAndFixesKnownAcronyms(string input, string expected) =>
        Assert.Equal(expected, DisplayNames.FromDottedIdentifier(input));

    [Fact]
    public void FromPascalCaseNeverLetterSpacesAKnownAcronym()
    {
        var result = DisplayNames.FromPascalCase("Wsl");
        Assert.DoesNotContain("W s l", result, StringComparison.Ordinal);
        Assert.DoesNotContain("W S L", result, StringComparison.Ordinal);
    }
}
