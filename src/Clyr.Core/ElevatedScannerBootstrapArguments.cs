namespace Clyr.Core;

/// <summary>Every way the elevated scanner's single bootstrap argument can be rejected, named specifically
/// enough that a caller never has to parse a message string to know which check failed.</summary>
public enum ElevatedScannerBootstrapOutcome
{
    Valid, MissingArgument, TooManyArguments, InvalidSwitch, EmptyPipeName, InvalidPipeName
}

/// <summary>A typed parse outcome rather than a thrown exception — a malformed bootstrap command line is an
/// expected, routine input to reject, not an exceptional program state.</summary>
public sealed record ElevatedScannerBootstrapArguments(ElevatedScannerBootstrapOutcome Outcome, string? PipeName)
{
    public bool IsValid => Outcome == ElevatedScannerBootstrapOutcome.Valid;

    /// <summary>The exact, case-sensitive, and only switch this executable's command line ever accepts.
    /// Nothing else is supported: no positional fallback, no other switch, no repeated <c>--pipe</c>, no
    /// response-file (<c>@file</c>) syntax, no environment-variable or configuration-file fallback.</summary>
    private const string PipeSwitchPrefix = "--pipe=";

    /// <summary>Pure, side-effect-free parsing — no filesystem access, no environment lookup, no process
    /// launch, never throws for an expected malformed input. Exactly one argument, with the exact prefix
    /// <c>--pipe=</c>, whose value passes <see cref="ElevatedScanPipeName.IsValid"/>, is accepted.</summary>
    public static ElevatedScannerBootstrapArguments TryParse(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return new(ElevatedScannerBootstrapOutcome.MissingArgument, null);
        if (args.Count > 1) return new(ElevatedScannerBootstrapOutcome.TooManyArguments, null);

        var argument = args[0];
        if (!argument.StartsWith(PipeSwitchPrefix, StringComparison.Ordinal))
            return new(ElevatedScannerBootstrapOutcome.InvalidSwitch, null);

        var pipeName = argument[PipeSwitchPrefix.Length..];
        if (pipeName.Length == 0) return new(ElevatedScannerBootstrapOutcome.EmptyPipeName, null);
        if (!ElevatedScanPipeName.IsValid(pipeName)) return new(ElevatedScannerBootstrapOutcome.InvalidPipeName, null);

        return new(ElevatedScannerBootstrapOutcome.Valid, pipeName);
    }
}
