using Clyr.Core;
using Clyr.Windows;

namespace Clyr.App;

/// <summary>
/// The one production composition boundary for the elevated permission-limited-root retry feature. Wires the
/// already-completed <see cref="ElevatedScanRetryRequestFactory"/>, <see cref="ElevatedScannerLauncher"/>,
/// <see cref="ElevatedScanResultReconcilerAdapter"/>, and <see cref="ElevatedScanRetryCoordinator"/> together
/// using the current real production implementations — no new dependency-injection framework, no rewrite of
/// application startup, and no second place in the codebase that constructs any of these types. The approved
/// process-start boundary remains exactly <see cref="WindowsElevatedScannerProcessStarter"/>
/// (<c>src/Clyr.Windows/ElevatedScannerProcessStarter.cs</c>) — this factory only ever references that existing,
/// already-reviewed type; it never starts a process itself.
/// </summary>
public static class ElevatedScanRetryServiceFactory
{
    /// <summary><paramref name="drives"/> and <paramref name="driveIdentity"/> are accepted rather than
    /// constructed here because the application already owns one shared instance of each (see
    /// <c>App.ConfigureServices</c>) — reusing them keeps drive discovery and drive-identity derivation consistent
    /// with the rest of the app instead of standing up a second, independent copy.</summary>
    public static IElevatedScanRetryService Create(IDriveDiscovery drives, IDriveIdentityProvider driveIdentity)
    {
        var clock = new SystemClock();
        var nonceGenerator = new CryptographicNonceGenerator();
        var requestFactory = new ElevatedScanRetryRequestFactory(drives, driveIdentity, clock, nonceGenerator);

        var trustedDirectory = new ProcessTrustedApplicationBaseDirectory();
        var fileProbe = new FileSystemElevatedScannerFileProbe();
        var processStarter = new WindowsElevatedScannerProcessStarter();
        var launcher = new ElevatedScannerLauncher(trustedDirectory, fileProbe, processStarter);

        var reconciler = new ElevatedScanResultReconcilerAdapter(clock);
        var coordinator = new ElevatedScanRetryCoordinator(requestFactory, launcher, reconciler);

        return new ElevatedScanRetryService(requestFactory, coordinator);
    }
}
