using System.Collections.Immutable;
using Clyr.Contracts;

namespace Clyr.Core.DeveloperMode;

/// <summary>
/// The closed mapping from a built-in classification rule ID to the Phase 7 developer-tool taxonomy. Nothing
/// here is rule-pack-declared or user-editable — adding a tool means adding a case here, in
/// <see cref="DeveloperToolRegistry"/>, and in the rule pack together, all reviewed as one change.
/// </summary>
public static class DeveloperToolTaxonomy
{
    private static readonly ImmutableDictionary<string, DeveloperToolId> RuleToTool = new Dictionary<string, DeveloperToolId>(StringComparer.Ordinal)
    {
        ["developer.node.modules"] = DeveloperToolId.NodeNpm,
        ["developer.npm.cache"] = DeveloperToolId.NodeNpm,
        ["developer.yarn.cache"] = DeveloperToolId.Yarn,
        ["developer.pnpm.store"] = DeveloperToolId.Pnpm,
        ["developer.nuget.packages"] = DeveloperToolId.DotNetNuGet,
        ["developer.dotnet.bin"] = DeveloperToolId.DotNetNuGet,
        ["developer.dotnet.obj"] = DeveloperToolId.DotNetNuGet,
        ["developer.gradle.cache"] = DeveloperToolId.Gradle,
        ["developer.maven.cache"] = DeveloperToolId.Maven,
        ["developer.pip.cache"] = DeveloperToolId.PythonPip,
        ["developer.cargo.registry"] = DeveloperToolId.RustCargo,
        ["developer.rust.target"] = DeveloperToolId.RustCargo,
        ["developer.flutter.pubcache"] = DeveloperToolId.FlutterDart,
        ["developer.playwright.cache"] = DeveloperToolId.Playwright,
        ["android.emulator"] = DeveloperToolId.AndroidSdk,
        ["containers.docker"] = DeveloperToolId.Docker,
        ["virtualization.wsl"] = DeveloperToolId.Wsl,
        ["virtualization.vhdx"] = DeveloperToolId.Wsl,
        ["developer.buildoutput.generic"] = DeveloperToolId.BuildOutput,
    }.ToImmutableDictionary();

    private static readonly ImmutableDictionary<string, DeveloperStorageCategory> RuleToCategory = new Dictionary<string, DeveloperStorageCategory>(StringComparer.Ordinal)
    {
        ["developer.node.modules"] = DeveloperStorageCategory.DependencyDirectory,
        ["developer.npm.cache"] = DeveloperStorageCategory.PackageCache,
        ["developer.yarn.cache"] = DeveloperStorageCategory.PackageCache,
        ["developer.pnpm.store"] = DeveloperStorageCategory.PackageStore,
        ["developer.nuget.packages"] = DeveloperStorageCategory.PackageCache,
        ["developer.dotnet.bin"] = DeveloperStorageCategory.BuildOutput,
        ["developer.dotnet.obj"] = DeveloperStorageCategory.BuildOutput,
        ["developer.gradle.cache"] = DeveloperStorageCategory.PackageCache,
        ["developer.maven.cache"] = DeveloperStorageCategory.PackageCache,
        ["developer.pip.cache"] = DeveloperStorageCategory.PackageCache,
        ["developer.cargo.registry"] = DeveloperStorageCategory.PackageCache,
        ["developer.rust.target"] = DeveloperStorageCategory.BuildOutput,
        ["developer.flutter.pubcache"] = DeveloperStorageCategory.PackageCache,
        ["developer.playwright.cache"] = DeveloperStorageCategory.DownloadCache,
        ["android.emulator"] = DeveloperStorageCategory.EmulatorImage,
        ["containers.docker"] = DeveloperStorageCategory.ContainerImage,
        ["virtualization.wsl"] = DeveloperStorageCategory.WslVirtualDisk,
        ["virtualization.vhdx"] = DeveloperStorageCategory.WslVirtualDisk,
        ["developer.buildoutput.generic"] = DeveloperStorageCategory.BuildOutput,
    }.ToImmutableDictionary();

    public static DeveloperToolId? ToolFor(string ruleId) => RuleToTool.TryGetValue(ruleId, out var tool) ? tool : null;
    public static DeveloperStorageCategory? CategoryFor(string ruleId) => RuleToCategory.TryGetValue(ruleId, out var category) ? category : null;
    public static IEnumerable<string> RuleIdsFor(DeveloperToolId tool) => RuleToTool.Where(pair => pair.Value == tool).Select(pair => pair.Key);
}
