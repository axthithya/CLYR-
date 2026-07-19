namespace Clyr.Core;

/// <summary>
/// The one centralized presentation formatter for turning PascalCase enum names and dotted/underscored rule
/// identifiers into human-readable display text. Every caller that previously ran its own ad-hoc regex or
/// string-splitting logic (<c>OverviewPage.Humanize</c>, <c>CleanupCandidates.Humanize</c>) delegates here instead,
/// so a known acronym is spelled correctly in exactly one place rather than drifting between call sites.
/// <para/>
/// This exists because a naive "insert a space before every capital letter" humanizer, when applied to a whole
/// sentence that already contains a correctly-cased acronym (for example a <c>FindingExplanation.SafetyStatus</c>
/// string containing the CLYR brand name), letter-spaces that acronym into a broken, spaced-out rendering of the
/// brand name — the confirmed real-machine defect this type exists to prevent. <see cref="FromPascalCase"/> and
/// <see cref="FromDottedIdentifier"/> are only ever meant
/// to run on a single already-PascalCase token (an enum name) or a single dotted/underscored identifier (a rule
/// id) — never on prose that may already contain a correctly-cased word.
/// </summary>
public static class DisplayNames
{
    /// <summary>Known acronyms and product names whose casing must never be touched by word-boundary splitting.
    /// Keyed by the plain title-cased word a naive splitter would otherwise produce.</summary>
    private static readonly Dictionary<string, string> KnownWords = new(StringComparer.Ordinal)
    {
        ["Wsl"] = "WSL",
        ["Sdk"] = "SDK",
        ["Clyr"] = "CLYR",
        ["Npm"] = "npm",
        ["Pnpm"] = "pnpm",
        ["Nuget"] = "NuGet",
    };

    /// <summary>Splits a single PascalCase token (typically an enum value's own name, e.g. <c>AndroidSdkEmulators</c>
    /// or <c>Wsl</c>) into space-separated words, correcting any known acronym per word. Never intended to run on
    /// a full sentence — see the type-level remarks.</summary>
    public static string FromPascalCase(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var spaced = System.Text.RegularExpressions.Regex.Replace(value, "(?<!^)([A-Z])", " $1");
        return string.Join(' ', spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(FixWord));
    }

    /// <summary>Splits a dotted/underscored identifier (typically a rule id, e.g. <c>developer.npm.cache</c>) into
    /// space-separated, title-cased words, correcting any known acronym per word.</summary>
    public static string FromDottedIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var text = value.Replace('.', ' ').Replace('_', ' ');
        return string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => FixWord(char.ToUpperInvariant(part[0]) + part[1..])));
    }

    private static string FixWord(string word) => KnownWords.TryGetValue(word, out var known) ? known : word;
}
