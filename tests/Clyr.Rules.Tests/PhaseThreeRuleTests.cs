using Clyr.Contracts;
using Clyr.Core;
using Clyr.Rules;

namespace Clyr.Rules.Tests;

public sealed class PhaseThreeRuleTests
{
    [Fact]
    public void BuiltInPackIsDigestVerifiedAndActive()
    {
        var result = Load();
        Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(item => item.Code)));
        Assert.True(result.Pack!.Summary.IsVerified);
        Assert.True(result.Pack.Summary.IsActive);
        Assert.Equal(RulePackTrust.BuiltIn, result.Pack.Summary.Trust);
        Assert.True(result.Pack.Rules.Count >= 30);
    }

    [Fact]
    public void EveryBuiltInRuleHasPositiveAndNegativeFixtureCoverage()
    {
        var pack = Load().Pack!;
        foreach (var rule in pack.Rules)
        {
            Assert.True(rule.IsMatch(rule.SampleMatch), "Positive fixture did not match rule: " + rule.Id);
            Assert.False(rule.IsMatch(rule.SampleNonMatch), "Negative fixture matched rule: " + rule.Id);
        }
    }

    [Fact]
    public void ProtectedAndMoreSpecificRuleWinsDeterministically()
    {
        var original = Load().Pack!;
        var reversed = new BuiltInRulePack(original.Summary, original.Rules.Reverse().ToArray());
        var path = Path.Combine(Root, "Windows", "System32", "kernel32.dll");
        Assert.Equal("windows.system32", Classify(original, path).Findings.Single().RuleId);
        Assert.Equal("windows.system32", Classify(reversed, path).Findings.Single().RuleId);
    }

    [Fact]
    public void UnknownObservedAndCoverageGapsRemainSeparate()
    {
        var session = Load().Pack!.Start(new(Root, ScanMode.Quick), Drive());
        session.Observe(new(Path.Combine(Root, "Unrecognized", "opaque.xyz"), 90, EntryTraits.None));
        var result = session.Complete(new(1, 0, 2, 3, 0, 4, 5, false, false, false), 700);
        Assert.Equal(90, result.Coverage.UnknownBytes);
        Assert.Equal(2, result.Coverage.InaccessibleEntries);
        Assert.Equal(12, result.Coverage.SkippedEntries);
        Assert.Equal(700, result.Coverage.UnaccountedDriveBytes);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void FindingsAreStableAndPrivacySafe()
    {
        var pack = Load().Pack!;
        var first = Classify(pack, Path.Combine(Root, "Users", "Alice", "Downloads", "secret.zip")).Findings;
        var second = Classify(pack, Path.Combine(Root, "Users", "Bob", "Downloads", "other.zip")).Findings;
        var downloadOne = Assert.Single(first, item => item.RuleId == "user.downloads");
        var downloadTwo = Assert.Single(second, item => item.RuleId == "user.downloads");
        Assert.Equal(downloadOne.Id, downloadTwo.Id);
        var serialized = System.Text.Json.JsonSerializer.Serialize(first);
        Assert.DoesNotContain("Alice", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TamperedCatalogIsRejectedAtomically()
    {
        var source = BuiltInDirectory();
        var temporary = Path.Combine(Path.GetTempPath(), "clyr-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            File.Copy(Path.Combine(source, "manifest.yaml"), Path.Combine(temporary, "manifest.yaml"));
            File.WriteAllText(Path.Combine(temporary, "rules.yaml"), File.ReadAllText(Path.Combine(source, "rules.yaml")) + Environment.NewLine);
            var result = BuiltInRulePackLoader.Load(temporary);
            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics, item => item.Code == "rule.digest-mismatch");
        }
        finally { Directory.Delete(temporary, true); }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void MillionObservationsUseOneBoundedClassificationSession()
    {
        var session = Load().Pack!.Start(new(Root, ScanMode.Deep), Drive());
        for (var index = 0; index < 1_000_000; index++)
            session.Observe(new(Path.Combine(Root, "src", "app", "node_modules", $"pkg{index % 32}", "file.js"), 10, EntryTraits.None));
        var result = session.Complete(new(1_000_000, 32, 0, 0, 0, 0, 0, false, false, false), 0);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(1_000_000, finding.FileCount);
        Assert.Equal(10_000_000, finding.LogicalBytes);
    }

    private static ClassificationResult Classify(BuiltInRulePack pack, string path)
    {
        var session = pack.Start(new(Root, ScanMode.Quick), Drive());
        session.Observe(new(path, 100, EntryTraits.None));
        return session.Complete(new(1, 0, 0, 0, 0, 0, 0, false, false, false), 0);
    }
    private static DriveSummary Drive() => new(Root, string.Empty, "NTFS", DriveKind.Fixed, true, true, true, "fixture", 1000, 1000, 0);
    private static string Root => Path.GetPathRoot(Environment.SystemDirectory)!;
    private static RulePackLoadResult Load() => BuiltInRulePackLoader.Load(BuiltInDirectory());
    private static string BuiltInDirectory() => Path.Combine(RepositoryRoot(), "rules", "builtin");
    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
