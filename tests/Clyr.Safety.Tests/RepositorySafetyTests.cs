using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Clyr.Safety.Tests;

public sealed class RepositorySafetyTests
{
    private static readonly string Root = RepositoryRoot();
    private static readonly string[] CoreForbiddenReferences = { "Microsoft.Data.Sqlite", "Microsoft.WindowsAppSDK", "Clyr.Windows" };

    [Fact]
    public void PackageVersionsExistOnlyInCentralFile()
    {
        foreach (var project in Directory.EnumerateFiles(Root, "*.csproj", SearchOption.AllDirectories))
        {
            if (project.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            var document = XDocument.Load(project);
            var versioned = document.Descendants("PackageReference").Where(item => item.Attribute("Version") is not null || item.Element("Version") is not null);
            Assert.Empty(versioned);
        }
    }

    [Fact]
    public void ApplicationVersionHasExactlyOneAuthoritativeSource()
    {
        var centralProps = XDocument.Load(Path.Combine(Root, "Directory.Build.props"));
        var version = centralProps.Descendants("Version").Select(item => item.Value).SingleOrDefault();
        Assert.Equal("0.7.0-phase7", version);

        // No individual project may declare its own Version/AssemblyVersion/FileVersion/InformationalVersion —
        // Directory.Build.props is the only place this is ever set.
        foreach (var project in Directory.EnumerateFiles(Root, "*.csproj", SearchOption.AllDirectories))
        {
            if (project.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            var document = XDocument.Load(project);
            foreach (var tag in new[] { "Version", "AssemblyVersion", "FileVersion", "InformationalVersion" })
                Assert.Empty(document.Descendants(tag));
        }

        // Program.cs (CLI) and App.xaml.cs (WinUI) must read the shared ApplicationVersion.Current rather than
        // duplicating the literal version string themselves. The regex guards every past and present phase
        // literal ("0.5.0", "0.6.0-phase6", "0.7.0-phase7", ...) generically, so this does not need editing
        // again the next time the version bumps.
        var cliProgram = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Cli", "Program.cs"));
        var appXamlCs = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "App.xaml.cs"));
        // Deliberately narrow to the app's own "N.N.N-phaseN" scheme (not a bare semver like a rule-pack
        // version "1.0.0", which legitimately appears as an unrelated fallback literal in this same file).
        var versionLiteral = new Regex("\"\\d+\\.\\d+\\.\\d+-phase\\d+\"", RegexOptions.None, TimeSpan.FromSeconds(1));
        foreach (var source in new[] { cliProgram, appXamlCs })
        {
            Assert.Contains("ApplicationVersion.Current", source, StringComparison.Ordinal);
            Assert.False(versionLiteral.IsMatch(source), "A hard-coded version literal was found outside Directory.Build.props.");
        }

        // AboutViewModel (the WinUI About page's source of truth) must read the injected IApplicationVersion
        // instance rather than a literal — App.xaml.cs registers that instance as ApplicationVersion.Current, so
        // this, together with the CLI check above, proves CLI and WinUI display exactly the same value.
        var appSessionViewModel = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "ViewModels", "AppSessionViewModel.cs"));
        Assert.Contains("AboutViewModel(AppSessionViewModel session, IApplicationVersion version", appSessionViewModel, StringComparison.Ordinal);
        Assert.Contains("public string Version { get; } = version.Value;", appSessionViewModel, StringComparison.Ordinal);
        Assert.False(versionLiteral.IsMatch(appSessionViewModel), "A hard-coded version literal was found in AppSessionViewModel.cs.");
    }

    [Fact]
    public void DoctorCommandDescribesOnlyTheApprovedBoundaryWithNoPhase7Claim()
    {
        var cliApplication = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Cli", "CliApplication.cs"));
        Assert.Contains("guarded low-risk execution is enabled only for approved CLYR-owned temporary artifacts", cliApplication, StringComparison.Ordinal);
        Assert.Contains("no developer-tool action is currently enabled for execution", cliApplication, StringComparison.OrdinalIgnoreCase);
        foreach (var overclaim in new[] { "read-only scanner available", "general cleanup available", "general cleanup support", "cleanup any file", "tool adapter", "npm install", "docker run" })
            Assert.DoesNotContain(overclaim, cliApplication, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CentralPackageManagementIsEnabledOnceWithoutOverridesOrAdvisorySuppression()
    {
        var central = XDocument.Load(Path.Combine(Root, "Directory.Packages.props"));
        Assert.Single(central.Descendants("ManagePackageVersionsCentrally"));
        Assert.Equal("true", central.Descendants("ManagePackageVersionsCentrally").Single().Value, ignoreCase: true);
        var packageVersions = central.Descendants("PackageVersion").ToArray();
        Assert.Equal(packageVersions.Length, packageVersions.Select(item => item.Attribute("Include")?.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var project in RepositoryFiles("*.csproj"))
        {
            var text = File.ReadAllText(project);
            Assert.DoesNotContain("VersionOverride", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("NU190", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("NU1008", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void CentralVersionsAreExactStableVersions()
    {
        var document = XDocument.Load(Path.Combine(Root, "Directory.Packages.props"));
        var versions = document.Descendants("PackageVersion").Select(item => item.Attribute("Version")?.Value ?? string.Empty);
        Assert.All(versions, version =>
        {
            Assert.DoesNotContain("*", version, StringComparison.Ordinal);
            Assert.DoesNotContain("-preview", version, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("-rc", version, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ArchitectureReferencesRespectPhaseOneBoundaries()
    {
        var contracts = Project("Clyr.Contracts");
        var core = Project("Clyr.Core");
        var app = Project("Clyr.App");
        Assert.Empty(contracts.Descendants("ProjectReference"));
        Assert.All(CoreForbiddenReferences, reference => Assert.DoesNotContain(reference, core.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(app.Descendants("PackageReference"), item => string.Equals(item.Attribute("Include")?.Value, "Microsoft.Data.Sqlite.Core", StringComparison.Ordinal));
    }

    [Fact]
    public void ProductionSourceContainsNoProcessExecutorOrElevationOutsideTheReviewedExecutionBoundary()
    {
        // These must never appear anywhere in production source, including the Phase 6 execution boundary itself.
        var alwaysForbidden = new[] { "RecycleOption", "powershell.exe", "cmd.exe" };
        // File/Directory mutation APIs may exist only inside the reviewed, narrow Phase 6 execution boundary.
        var mutationBoundaryOnly = new[] { "File.Delete", "File.Move", "Directory.Delete" };
        // A process launch may exist only in the one file that performs the controlled, known-binary UAC launch.
        var processLaunchBoundaryOnly = new[] { "Process.Start", "System.Diagnostics.Process", "ProcessStartInfo" };
        var executionBoundary = Path.Combine(Root, "src", "Clyr.Core", "Execution") + Path.DirectorySeparatorChar;
        var launcherFile = Path.Combine(executionBoundary, "ElevatedHelperLauncher.cs");
        // Phase 7.1 adds exactly one more reviewed mutation surface: the Quick Analysis checkpoint store. It
        // reads and writes only its own small JSON file inside CLYR's application-data checkpoints directory
        // (never a scanned drive or user path), and File.Move/File.Delete here manage only that CLYR-owned
        // cache file — the explicit "CLYR-owned persistence inside CLYR's application-data directory" carve-out
        // from the scanning safety boundary, not a general filesystem-mutation capability.
        var checkpointStoreFile = Path.Combine(Root, "src", "Clyr.Persistence", "ScanCheckpointStore.cs");
        // Phase 7 adds exactly one more reviewed process-launch surface: the narrow, non-elevated, read-only
        // developer-tool status probe. It never mutates anything and never accepts a caller-supplied command.
        var developerProbeFile = Path.Combine(Root, "src", "Clyr.Core", "DeveloperMode", "DeveloperToolProbeRunner.cs");
        // requireAdministrator may exist only in the elevated helper's own application manifest, scanned separately below.
        var helperManifestGuard = Path.Combine(Root, "src", "Clyr.ElevatedHelper", "app.manifest");
        Assert.Contains("requireAdministrator", File.ReadAllText(helperManifestGuard), StringComparison.Ordinal);

        foreach (var source in Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (source.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            var text = File.ReadAllText(source);
            foreach (var token in alwaysForbidden) Assert.DoesNotContain(token, text, StringComparison.Ordinal);
            Assert.DoesNotContain("requireAdministrator", text, StringComparison.Ordinal);
            if (!source.StartsWith(executionBoundary, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(source, checkpointStoreFile, StringComparison.OrdinalIgnoreCase))
                foreach (var token in mutationBoundaryOnly) Assert.DoesNotContain(token, text, StringComparison.Ordinal);
            if (!string.Equals(source, launcherFile, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(source, developerProbeFile, StringComparison.OrdinalIgnoreCase))
                foreach (var token in processLaunchBoundaryOnly) Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ExecutionBoundaryContainsNoShellPackageManagerOrContainerCommands()
    {
        var forbidden = new[]
        {
            "npm ", "npm.exe", "pnpm", "yarn", "pip ", "pip.exe", "nuget.exe", "gradle", "mvn ", "flutter", "cargo ",
            "docker", "wsl", "dism.exe", "reg.exe", "sc.exe", "takeown", "icacls", "attrib +", "vssadmin"
        };
        var executionBoundary = Path.Combine(Root, "src", "Clyr.Core", "Execution");
        foreach (var source in Directory.EnumerateFiles(executionBoundary, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(source);
            foreach (var token in forbidden) Assert.DoesNotContain(token, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DeveloperModeBoundaryNeverMutatesToolsAndOnlyEverAsksForStatus()
    {
        // The Docker/WSL executable names and their read-only --version/--status flags are legitimate and
        // expected here; what must never appear is any mutating subcommand, install/update/uninstall action,
        // shell invocation, or generic script runner.
        var forbidden = new[]
        {
            "system prune", "volume rm", "volume prune", "container rm", "image rm", "builder prune",
            "docker rmi", "docker kill", "docker stop", "--unregister", "--terminate", "--shutdown",
            "compact", ".wslconfig", "npm install", "npm uninstall", "npm update", "pip install", "pip uninstall",
            "cargo install", "gradle clean", "mvn clean", "powershell.exe", "cmd.exe", "cmd /c",
            "Process.Start(\"", "Process.Start('"
        };
        var developerModeBoundary = Path.Combine(Root, "src", "Clyr.Core", "DeveloperMode");
        foreach (var source in Directory.EnumerateFiles(developerModeBoundary, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(source);
            foreach (var token in forbidden) Assert.DoesNotContain(token, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ScanningProductionCodeContainsNoMutationPhase6OrShellInvocation()
    {
        // Scanning must remain strictly metadata-only: no scan code path may ever write, delete, move, rename,
        // create, or otherwise mutate anything on the scanned drive, nor call into Phase 6 cleanup/execution,
        // nor launch a shell or arbitrary process, nor reference Phase 8 movement behavior. Scoped to exactly
        // the scan engine and its Windows-specific adapter — the one narrow surface a future elevated read-only
        // scan helper would also need to satisfy.
        var forbiddenMutationApis = new[]
        {
            "File.Delete", "Directory.Delete", "File.Move", "Directory.Move", "File.WriteAllText",
            "File.WriteAllBytes", "File.AppendAllText", "File.Create(", "File.OpenWrite", "File.SetAttributes",
            "File.Replace", "File.Encrypt", "File.Decrypt", "FileSecurity", "DirectorySecurity",
            "SetAccessControl", "FileSystemAclExtensions", "TakeOwnership", "Ownership.Set",
        };
        var forbiddenExecutionApis = new[]
        {
            "Process.Start", "ProcessStartInfo", "System.Diagnostics.Process", "powershell.exe", "cmd.exe",
            "cmd /c", "runas", "requireAdministrator",
        };
        var forbiddenPhaseReferences = new[]
        {
            "NonElevatedCleanupExecutor", "CleanupPlanBuilder", "ExecutionTokenService", "ElevatedHelperLauncher",
            "CleanupCandidateFactory", "BuiltInExecutionActions", "MoveKnownFolder", "MoveToAnotherDrive",
        };
        var scanFiles = new[]
        {
            Path.Combine(Root, "src", "Clyr.Core", "Scanning.cs"),
            Path.Combine(Root, "src", "Clyr.Core", "ScanUx.cs"),
            Path.Combine(Root, "src", "Clyr.Core", "ScanAccounting.cs"),
            Path.Combine(Root, "src", "Clyr.Windows", "WindowsScanning.cs"),
        };
        foreach (var file in scanFiles)
        {
            Assert.True(File.Exists(file), $"Expected scan file not found: {file}");
            var text = File.ReadAllText(file);
            foreach (var token in forbiddenMutationApis) Assert.DoesNotContain(token, text, StringComparison.Ordinal);
            foreach (var token in forbiddenExecutionApis) Assert.DoesNotContain(token, text, StringComparison.Ordinal);
            foreach (var token in forbiddenPhaseReferences) Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ManifestIsNonElevatedAndCapabilityFree()
    {
        var manifest = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "app.manifest"));
        Assert.Contains("asInvoker", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("requireAdministrator", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("Capability", manifest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlanningAndExecutionVocabularyExistOnlyWithinReviewedFiles()
    {
        var names = Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories).Select(Path.GetFileName);
        Assert.Contains(names, name => name is not null && name.Contains("Scanning", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, name => name is not null && name.Contains("BuiltInRulePack", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, name => name is not null && name.Contains("CleanupPlan", StringComparison.OrdinalIgnoreCase));
        var planning = string.Join(Environment.NewLine, Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs",
            SearchOption.AllDirectories).Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)).Select(File.ReadAllText));
        Assert.Contains("ExecutionNotAvailableInPhase5", planning, StringComparison.Ordinal);

        // 'plan execute' is a legitimate Phase 6 command, but only inside the reviewed CLI/Core execution surface —
        // never inside WinUI, which has no execution flow implemented yet.
        var executionCommandFiles = new[] { "PlanCliCommands.cs", "ExecutionCliCommands.cs", "CliApplication.cs" };
        foreach (var source in Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (source.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            if (executionCommandFiles.Contains(Path.GetFileName(source), StringComparer.Ordinal)) continue;
            Assert.DoesNotContain("plan execute", File.ReadAllText(source), StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("plan execute", File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "App.xaml.cs")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplicationCompositionAndNavigationUseDistinctReadOnlyPages()
    {
        var appSource = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "App.xaml.cs"));
        var navigation = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "MainWindow.xaml"));
        Assert.Contains("ServiceCollection", appSource, StringComparison.Ordinal);
        Assert.Contains("StartupErrorWindow", appSource, StringComparison.Ordinal);
        foreach (var destination in new[] { "Overview", "Scan", "Results", "Review Plan", "History", "Developer Mode", "Privacy", "Licenses", "About" })
            Assert.Contains($"Content=\"{destination}\"", navigation, StringComparison.Ordinal);
        Assert.Contains("IsSettingsVisible=\"True\"", navigation, StringComparison.Ordinal);
        Assert.Contains("ContentControl", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("DriveSelector", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("Start Analysis", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Data.Sqlite", appSource + navigation, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryContainsNoMachineSpecificPathOrCredentialMaterial()
    {
        var machinePaths = new[] { Root, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .SelectMany(path => new[] { path, path.Replace('\\', '/') })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var credentialPattern = new Regex("-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|AKIA[0-9A-Z]{16}|gh[pousr]_[A-Za-z0-9]{30,}", RegexOptions.CultureInvariant);
        foreach (var file in RepositoryFiles("*").Where(IsTextConfigurationFile))
        {
            var text = File.ReadAllText(file);
            foreach (var path in machinePaths) Assert.DoesNotContain(path, text, StringComparison.OrdinalIgnoreCase);
            Assert.False(credentialPattern.IsMatch(text), $"Credential-shaped material found in {file}.");
        }
    }

    [Fact]
    public void ElevatedScanRetryContractsAndValidatorContainNoExecutionOrMutationCapability()
    {
        // Phase 7.2.6A adds only typed contracts and a pure validator for the future elevated scan retry — this
        // proves that closure narrowly: neither file may reference process launch, shell execution, an
        // elevation manifest concept, Phase 6 cleanup/execution, or any filesystem-mutation or ACL/ownership API.
        var forbidden = new[]
        {
            "Process.Start", "ProcessStartInfo", "System.Diagnostics.Process", "powershell.exe", "cmd.exe",
            "cmd /c", "runas", "requireAdministrator", "NamedPipe",
            "File.Delete", "File.Move", "File.WriteAllText", "File.WriteAllBytes", "File.AppendAllText",
            "File.Create(", "File.OpenWrite", "File.SetAttributes", "File.Replace", "File.Encrypt", "File.Decrypt",
            "Directory.Delete", "Directory.Move", "Directory.CreateDirectory",
            "FileSecurity", "DirectorySecurity", "SetAccessControl", "FileSystemAclExtensions",
            "TakeOwnership", "Ownership.Set",
            "Clyr.ElevatedHelper", "ElevatedHelperLauncher", "ElevatedHelperRequestHandler",
            "NonElevatedCleanupExecutor", "CleanupPlanBuilder", "ExecutionTokenService", "CleanupCandidateFactory",
            "BuiltInExecutionActions", "MoveKnownFolder", "MoveToAnotherDrive",
        };
        var files = new[]
        {
            Path.Combine(Root, "src", "Clyr.Contracts", "ElevatedScanRetry.cs"),
            Path.Combine(Root, "src", "Clyr.Core", "ElevatedScanRetryValidation.cs"),
        };
        foreach (var file in files)
        {
            Assert.True(File.Exists(file), $"Expected file not found: {file}");
            var text = File.ReadAllText(file);
            foreach (var token in forbidden) Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ElevatedScanIpcTransportContainsNoMutationExecutionOrNetworkCapability()
    {
        // Phase 7.2.6B adds the bounded named-pipe IPC transport for the future elevated scan retry. Named-pipe
        // writes are the whole point of this file, so unlike the narrower 7.2.6A check above, "NamedPipe" itself
        // is expected here — everything else on the forbidden list (mutation APIs, ACL/ownership mutation,
        // process launch, shell invocation, real network sockets, and any Phase 6/Phase 8 reference) is not.
        var forbidden = new[]
        {
            "Process.Start", "ProcessStartInfo", "System.Diagnostics.Process", "powershell.exe", "cmd.exe",
            "cmd /c", "runas", "requireAdministrator",
            "File.Delete", "File.Move", "File.WriteAllText", "File.WriteAllBytes", "File.AppendAllText",
            "File.Create(", "File.OpenWrite", "File.SetAttributes", "File.Replace", "File.Encrypt", "File.Decrypt",
            "Directory.Delete", "Directory.Move", "Directory.CreateDirectory",
            "FileSystemAclExtensions", "TakeOwnership", "Ownership.Set",
            "System.Net.Sockets", "TcpClient", "TcpListener", "UdpClient", "HttpClient", "WebRequest",
            "Clyr.ElevatedHelper", "ElevatedHelperLauncher", "ElevatedHelperRequestHandler",
            "NonElevatedCleanupExecutor", "CleanupPlanBuilder", "ExecutionTokenService", "CleanupCandidateFactory",
            "BuiltInExecutionActions", "MoveKnownFolder", "MoveToAnotherDrive", "BinaryFormatter",
        };
        var file = Path.Combine(Root, "src", "Clyr.Core", "ElevatedScanIpc.cs");
        Assert.True(File.Exists(file), $"Expected file not found: {file}");
        var text = File.ReadAllText(file);
        foreach (var token in forbidden) Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        // PipeSecurity/PipeAccessRule/SetAccessRule here configure the current-user-only ACL on CLYR's own
        // ephemeral IPC pipe — a fundamentally different concept from mutating a scanned file's or directory's
        // ACL/ownership on disk (the FileSecurity/DirectorySecurity/SetAccessControl tokens forbidden above).
        Assert.Contains("PipeSecurity", text, StringComparison.Ordinal);
    }

    private static XDocument Project(string name) => XDocument.Load(Path.Combine(Root, "src", name, name + ".csproj"));
    private static IEnumerable<string> RepositoryFiles(string pattern) => Directory.EnumerateFiles(Root, pattern, SearchOption.AllDirectories)
        .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        .Where(path => !path.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        .Where(path => !path.Contains(Path.DirectorySeparatorChar + ".tools" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    private static bool IsTextConfigurationFile(string path) => Path.GetExtension(path) is ".cs" or ".csproj" or ".props" or ".targets" or ".json" or ".yaml" or ".yml" or ".md" or ".ps1" or ".xaml";
    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
