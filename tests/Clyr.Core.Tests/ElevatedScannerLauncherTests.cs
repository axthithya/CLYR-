using System.Collections.Immutable;
using System.IO.Pipes;
using System.Runtime.Versioning;
using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

[CollectionDefinition("ElevatedScannerLauncherSequential", DisableParallelization = true)]
public sealed class ElevatedScannerLauncherSequentialMarker;

/// <summary>Phase 7.2.6F2: the app-side launch/IPC orchestration boundary. No test here starts a real process,
/// triggers real UAC, or scans a real drive — every process start is a fake, and every "helper" is an
/// in-process one-shot IPC server started from that fake.</summary>
[Collection("ElevatedScannerLauncherSequential")]
[SupportedOSPlatform("windows")]
public sealed class ElevatedScannerLauncherTests
{
    private static readonly Guid ScanId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private const string DriveIdentity = "drive-fingerprint-launcher";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-17T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private const string TrustedBase = "C:\\CLYR";
    private static readonly ElevatedScanIpcServerTimeouts FastServerTimeouts =
        new(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4));
    private static readonly ElevatedScanIpcClientTimeouts FastClientTimeouts =
        new(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4));

    [Fact]
    public async Task ValidRequestBuildsATrustedPlanAndStartsOnce()
    {
        var starter = new FakeProcessStarter(ElevatedProcessStartOutcome.LaunchFailed);
        await Launcher(starter).RunAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(1, starter.CallCount);
        Assert.Equal("C:\\CLYR\\Clyr.ElevatedScanner.exe", starter.LastPlan!.ExecutablePath);
    }

    [Fact]
    public async Task ExactlyOneBootstrapArgumentIsPassed()
    {
        var starter = new FakeProcessStarter(ElevatedProcessStartOutcome.LaunchFailed);
        await Launcher(starter).RunAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal("--pipe=" + starter.LastPlan!.PipeName, starter.LastPlan.BootstrapArgument);
    }

    [Fact]
    public void HelperPathCannotBeSuppliedByTheCaller()
    {
        var method = typeof(ElevatedScannerLauncher).GetMethod(nameof(ElevatedScannerLauncher.RunAsync));
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(ElevatedScanRetryRequest), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void PipeNameCannotBeSuppliedByTheCaller()
    {
        var method = typeof(ElevatedScannerLauncher).GetMethod(nameof(ElevatedScannerLauncher.RunAsync));
        Assert.All(method!.GetParameters(), parameter => Assert.NotEqual(typeof(string), parameter.ParameterType));
    }

    [Fact]
    public async Task CancellationBeforeLaunchCausesZeroProcessStartCalls()
    {
        var starter = new FakeProcessStarter(ElevatedProcessStartOutcome.Started);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await Launcher(starter).RunAsync(BuildRequest(), cts.Token);

        Assert.Equal(ElevatedScannerLauncherOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, starter.CallCount);
    }

    [Fact]
    public async Task UacCancellationReturnsDenied()
    {
        var starter = new FakeProcessStarter(ElevatedProcessStartOutcome.Denied);
        var result = await Launcher(starter).RunAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(ElevatedScannerLauncherOutcome.Denied, result.Outcome);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task DenialCausesNoIpcConnectionAttempt()
    {
        // A very short client timeout would still surface as Denied (not ConnectionTimedOut) if the launcher
        // never attempted a connection at all after the denial — proving no IPC attempt happened.
        var starter = new FakeProcessStarter(ElevatedProcessStartOutcome.Denied);
        var shortTimeouts = new ElevatedScanIpcClientTimeouts(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));

        var result = await Launcher(starter, shortTimeouts).RunAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(ElevatedScannerLauncherOutcome.Denied, result.Outcome);
    }

    [Fact]
    public async Task OtherWin32ExceptionReturnsLaunchFailed()
    {
        var starter = new FakeProcessStarter(ElevatedProcessStartOutcome.LaunchFailed);
        var result = await Launcher(starter).RunAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(ElevatedScannerLauncherOutcome.LaunchFailed, result.Outcome);
    }

    [Fact]
    public async Task MissingHelperReturnsHelperMissingWithZeroProcessStartCalls()
    {
        var starter = new FakeProcessStarter(ElevatedProcessStartOutcome.Started);
        var launcher = new ElevatedScannerLauncher(Trusted(TrustedBase), Probe(fileExists: false), starter, FastClientTimeouts);

        var result = await launcher.RunAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(ElevatedScannerLauncherOutcome.HelperMissing, result.Outcome);
        Assert.Equal(0, starter.CallCount);
    }

    [Fact]
    public async Task InvalidLaunchPlanIsReturnedForAnUntrustedBaseDirectory()
    {
        var starter = new FakeProcessStarter(ElevatedProcessStartOutcome.Started);
        var launcher = new ElevatedScannerLauncher(Trusted(string.Empty), Probe(), starter, FastClientTimeouts);

        var result = await launcher.RunAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(ElevatedScannerLauncherOutcome.InvalidLaunchPlan, result.Outcome);
        Assert.Equal(0, starter.CallCount);
    }

    [Fact]
    public async Task ValidFakeLaunchPlusValidIpcResponseReturnsCompleted()
    {
        var starter = new FakeProcessStarter(ElevatedProcessStartOutcome.Started, StartFakeHelper);
        var result = await Launcher(starter).RunAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(ElevatedScannerLauncherOutcome.Completed, result.Outcome);
        Assert.NotNull(result.Response);
        Assert.Equal(ElevatedScanRetryOutcome.Completed, result.Response!.Outcome);
    }

    [Fact]
    public async Task ResponseNonceMismatchReturnsInvalidResponse()
    {
        var starter = new FakeProcessStarter(ElevatedProcessStartOutcome.Started, plan =>
            ElevatedScanIpcTransport.RunOneShotAsync(plan.PipeName, FastServerTimeouts, new FixedClock(Now),
                (received, _) => Task.FromResult(CompletedResponse(received) with { Nonce = new string('z', ElevatedScanRetryProtocol.MinNonceLength) }),
                CancellationToken.None));

        var result = await Launcher(starter).RunAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(ElevatedScannerLauncherOutcome.InvalidResponse, result.Outcome);
    }

    [Fact]
    public async Task ConnectionTimeoutReturnsConnectionTimedOut()
    {
        // No server is ever started for this pipe name — the started process is faked but never listens.
        var starter = new FakeProcessStarter(ElevatedProcessStartOutcome.Started);
        var shortTimeouts = new ElevatedScanIpcClientTimeouts(TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4));

        var result = await Launcher(starter, shortTimeouts).RunAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(ElevatedScannerLauncherOutcome.ConnectionTimedOut, result.Outcome);
    }

    [Fact]
    public async Task NoSecondLaunchOrRequestOccurs()
    {
        var starter = new FakeProcessStarter(ElevatedProcessStartOutcome.Started, StartFakeHelper);
        var result = await Launcher(starter).RunAsync(BuildRequest(), CancellationToken.None);

        Assert.Equal(ElevatedScannerLauncherOutcome.Completed, result.Outcome);
        Assert.Equal(1, starter.CallCount);

        // The one-shot helper has already returned exactly once and holds no further listener.
        using var secondClient = new NamedPipeClientStream(".", starter.LastPlan!.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondClient.ConnectAsync(cts.Token));
    }

    private static Task<ElevatedScanRetryResponse> StartFakeHelper(ElevatedScannerLaunchPlan plan) =>
        ElevatedScanIpcTransport.RunOneShotAsync(plan.PipeName, FastServerTimeouts, new FixedClock(Now),
            (received, _) => Task.FromResult(CompletedResponse(received)), CancellationToken.None);

    private static ElevatedScannerLauncher Launcher(FakeProcessStarter starter, ElevatedScanIpcClientTimeouts? timeouts = null) =>
        new(Trusted(TrustedBase), Probe(), starter, timeouts ?? FastClientTimeouts);

    private static FakeTrustedDirectory Trusted(string baseDirectory) => new(baseDirectory);
    private static FakeFileProbe Probe(bool fileExists = true, bool directoryExists = false, bool isReparsePoint = false) =>
        new(fileExists, directoryExists, isReparsePoint);

    private static ElevatedScanRetryRequest BuildRequest()
    {
        var roots = ImmutableArray.Create(new PermissionLimitedRoot("C:\\Data\\Alpha", ScanId, DriveIdentity, "root-1", PermissionLimitedReasonCode.AccessDenied));
        var manifest = ElevatedScanManifestBuilder.Build(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots, ScanId, DriveIdentity, roots);
        return new ElevatedScanRetryRequest(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots,
            new string('a', ElevatedScanRetryProtocol.MinNonceLength), Now, Now.AddMinutes(1), ScanId, DriveIdentity,
            manifest.Value!.Digest, roots, 16);
    }

    private static ElevatedScanRetryResponse CompletedResponse(ElevatedScanRetryRequest request) =>
        new(request.ProtocolVersion, request.Nonce, ElevatedScanRetryOutcome.Completed, Now, Now.AddSeconds(1),
            request.PermissionLimitedRoots.Length, request.PermissionLimitedRoots.Length, 0, 10, 2, 1000, 800, 800, 0, 0, 0, []);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FakeTrustedDirectory(string baseDirectory) : ITrustedApplicationBaseDirectory
    {
        public string BaseDirectory { get; } = baseDirectory;
    }

    private sealed class FakeFileProbe(bool fileExists, bool directoryExists, bool isReparsePoint) : IElevatedScannerFileProbe
    {
        public bool FileExists(string path) => fileExists;
        public bool DirectoryExists(string path) => directoryExists;
        public bool IsReparsePoint(string path) => isReparsePoint;
    }

    private sealed class FakeProcessStarter(ElevatedProcessStartOutcome outcome, Func<ElevatedScannerLaunchPlan, Task>? onStart = null) : IElevatedScannerProcessStarter
    {
        public int CallCount { get; private set; }
        public List<ElevatedScannerLaunchPlan> Plans { get; } = [];
        public ElevatedScannerLaunchPlan? LastPlan => Plans.Count > 0 ? Plans[^1] : null;

        public ElevatedProcessStartResult Start(ElevatedScannerLaunchPlan plan)
        {
            CallCount++;
            Plans.Add(plan);
            if (outcome == ElevatedProcessStartOutcome.Started && onStart is not null)
                _ = Task.Run(() => onStart(plan));
            return new(outcome);
        }
    }
}
