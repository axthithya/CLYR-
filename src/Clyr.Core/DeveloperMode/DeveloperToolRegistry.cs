using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Clyr.Contracts;

namespace Clyr.Core.DeveloperMode;

/// <summary>
/// The closed, reviewed list of every Phase 7 developer tool and the orchestrator that turns classification
/// output plus (for Docker/WSL only) a narrow read-only status probe into <see cref="DeveloperToolReport"/>s.
/// This is a fixed, compiled list — there is no rule-pack-declared or dynamically registered tool.
/// </summary>
public static class DeveloperToolRegistry
{
    public static readonly ImmutableArray<DeveloperToolDescriptor> Descriptors =
    [
        new(DeveloperToolId.Docker, "Docker Desktop", ["windows"], ["docker.exe"], true, TimeSpan.FromSeconds(5), 4096,
            "Docker Desktop stores images, containers, build cache, and volumes in product-managed data. CLYR only asks Docker for its own installed version; it never runs prune, removal, or reset commands."),
        new(DeveloperToolId.Wsl, "Windows Subsystem for Linux", ["windows"], ["wsl.exe"], true, TimeSpan.FromSeconds(5), 4096,
            "WSL stores each distribution's filesystem in a virtual disk. CLYR only asks WSL for its own status; it never unregisters a distribution or deletes a virtual disk."),
        new(DeveloperToolId.NodeNpm, "Node.js / npm", ["windows"], [], false, TimeSpan.Zero, 0,
            "npm caches downloaded packages and installs project dependencies into node_modules folders."),
        new(DeveloperToolId.Pnpm, "pnpm", ["windows"], [], false, TimeSpan.Zero, 0,
            "pnpm keeps a shared content-addressed package store."),
        new(DeveloperToolId.Yarn, "Yarn", ["windows"], [], false, TimeSpan.Zero, 0,
            "Yarn caches downloaded packages to speed later installs."),
        new(DeveloperToolId.DotNetNuGet, ".NET / NuGet", ["windows"], [], false, TimeSpan.Zero, 0,
            "NuGet caches downloaded packages; .NET build output lives under bin/obj."),
        new(DeveloperToolId.Gradle, "Gradle", ["windows"], [], false, TimeSpan.Zero, 0,
            "Gradle caches dependencies and build metadata."),
        new(DeveloperToolId.Maven, "Maven", ["windows"], [], false, TimeSpan.Zero, 0,
            "Maven caches downloaded artifacts in a local repository."),
        new(DeveloperToolId.PythonPip, "Python / pip", ["windows"], [], false, TimeSpan.Zero, 0,
            "pip caches downloaded package wheels and sources."),
        new(DeveloperToolId.RustCargo, "Rust / Cargo", ["windows"], [], false, TimeSpan.Zero, 0,
            "Cargo caches crate sources and writes compiled build output to a target directory."),
        new(DeveloperToolId.FlutterDart, "Flutter / Dart", ["windows"], [], false, TimeSpan.Zero, 0,
            "Dart/Flutter pub caches downloaded package versions."),
        new(DeveloperToolId.AndroidSdk, "Android SDK & emulators", ["windows"], [], false, TimeSpan.Zero, 0,
            "Android tooling stores SDK components and emulator images; emulator data is protected."),
        new(DeveloperToolId.Playwright, "Playwright", ["windows"], [], false, TimeSpan.Zero, 0,
            "Playwright downloads browser binaries for automated testing."),
        new(DeveloperToolId.BuildOutput, "Generic build output", ["windows"], [], false, TimeSpan.Zero, 0,
            "Many build tools emit compiled artifacts into generic bin/obj/target/build/dist folders."),
    ];

    public static DeveloperToolDescriptor Descriptor(DeveloperToolId id) => Descriptors.First(item => item.Id == id);

    public static async Task<ImmutableArray<DeveloperToolReport>> DetectAllAsync(
        ImmutableArray<DeveloperToolReport> classificationReports, IDeveloperToolExecutableLocator locator,
        IDeveloperToolProbeRunner probeRunner, CancellationToken cancellationToken)
    {
        var byTool = classificationReports.ToDictionary(report => report.ToolId);
        var results = ImmutableArray.CreateBuilder<DeveloperToolReport>();
        foreach (var descriptor in Descriptors)
        {
            if (descriptor.RequiresProbe)
            {
                results.Add(await ProbeAsync(descriptor, byTool.GetValueOrDefault(descriptor.Id), locator, probeRunner, cancellationToken).ConfigureAwait(false));
            }
            else if (byTool.TryGetValue(descriptor.Id, out var report))
            {
                results.Add(report);
            }
            else
            {
                results.Add(new(descriptor.Id, DeveloperToolStatus.Unavailable, null, null, [],
                    [new("developer.no-evidence", "No scan evidence is available for this tool yet. Run or refresh a storage analysis.")], 0, null));
            }
        }
        return results.ToImmutable();
    }

    private static async Task<DeveloperToolReport> ProbeAsync(DeveloperToolDescriptor descriptor, DeveloperToolReport? classification,
        IDeveloperToolExecutableLocator locator, IDeveloperToolProbeRunner probeRunner, CancellationToken cancellationToken)
    {
        var candidates = classification?.Candidates ?? [];
        var observedBytes = classification?.TotalObservedLogicalBytes ?? 0;
        var located = locator.Locate(descriptor);
        if (located is null)
        {
            return new(descriptor.Id, candidates.Length > 0 ? DeveloperToolStatus.PartiallyDetected : DeveloperToolStatus.NotInstalled,
                null, null, candidates,
                [new("developer.not-installed", descriptor.DisplayName + " was not found through any trusted discovery path.")], observedBytes, null);
        }

        var arguments = descriptor.Id switch
        {
            DeveloperToolId.Docker => ImmutableArray.Create("--version"),
            DeveloperToolId.Wsl => ImmutableArray.Create("--status"),
            _ => ImmutableArray<string>.Empty
        };
        if (arguments.IsEmpty)
        {
            return new(descriptor.Id, DeveloperToolStatus.ProbeFailed, null, located.DiscoverySource, candidates,
                [new("developer.probe-unsupported", "No safe read-only probe is defined for this tool yet.")], observedBytes, null);
        }

        var request = new DeveloperToolProbeRequest(descriptor.Id, located.NormalizedFullPath, arguments, descriptor.ProbeTimeout, descriptor.MaxProbeOutputBytes);
        DeveloperToolProbeResult result;
        try { result = await probeRunner.RunAsync(request, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            return new(descriptor.Id, DeveloperToolStatus.ProbeFailed, null, located.DiscoverySource, candidates,
                [new("developer.probe-failed", "The read-only status probe did not complete.")], observedBytes, null);
        }

        if (result.TimedOut)
        {
            return new(descriptor.Id, DeveloperToolStatus.ProbeFailed, null, located.DiscoverySource, candidates,
                [new("developer.probe-timeout", "The read-only status probe timed out.")], observedBytes, null);
        }
        if (!result.Succeeded)
        {
            return new(descriptor.Id, candidates.Length > 0 ? DeveloperToolStatus.PartiallyDetected : DeveloperToolStatus.ProbeFailed,
                null, located.DiscoverySource, candidates,
                [new("developer.probe-nonzero-exit", "The tool reported a non-zero exit status.")], observedBytes, null);
        }

        var version = ExtractVersion(result.StandardOutput);
        var status = candidates.Length > 0 ? DeveloperToolStatus.FullyDetected : DeveloperToolStatus.InstalledNoData;
        var diagnostics = result.OutputTruncated
            ? ImmutableArray.Create(new DeveloperToolDiagnostic("developer.probe-output-truncated", "Probe output exceeded the bounded limit and was truncated."))
            : ImmutableArray<DeveloperToolDiagnostic>.Empty;
        return new(descriptor.Id, status, version, located.DiscoverySource, candidates, diagnostics, observedBytes, null);
    }

    private static string? ExtractVersion(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var match = Regex.Match(output, @"\d+\.\d+(\.\d+)?", RegexOptions.None, TimeSpan.FromMilliseconds(200));
        return match.Success ? match.Value : null;
    }
}
