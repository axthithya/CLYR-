using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using Clyr.Contracts;
using Clyr.Core.Execution;

namespace Clyr.Tools.Phase6UacSmoke;

/// <summary>
/// Real, interactive, fixture-only UAC smoke test for the Phase 6 elevated helper. Invoked only by
/// scripts/run-phase6-uac-smoke.ps1 — never part of the shipped product, never referenced by Clyr.sln.
/// Creates a synthetic temporary fixture root and one synthetic stale file, launches the real
/// Clyr.ElevatedHelper.exe through a real Windows UAC prompt, sends a real IPC request naming only that
/// fixture root and file, and verifies the real response and real file-system outcome. Never touches a real
/// user, system, browser, Docker, WSL, package-cache, or project path.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var timeoutSeconds = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 60;
        string? fixtureRoot = null;
        try
        {
            if (!Environment.UserInteractive)
                throw new InvalidOperationException("This is not an interactive session. A real desktop session is required to approve the UAC prompt.");
            if (string.Equals(Environment.GetEnvironmentVariable("SESSIONNAME"), "Services", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Running in a non-interactive service session. Run this from an interactive desktop logon.");

            var repoRoot = FindRepoRoot();
            var helperPath = Path.Combine(repoRoot, "src", "Clyr.ElevatedHelper", "bin", "Release",
                "net10.0-windows10.0.26100.0", "Clyr.ElevatedHelper.exe");
            if (!File.Exists(helperPath))
                throw new FileNotFoundException("Required Release build output is missing. Run 'dotnet build Clyr.sln --configuration Release' first.", helperPath);

            fixtureRoot = Path.Combine(Path.GetTempPath(), "clyr-uac-smoke-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixtureRoot);
            var fixtureFile = Path.Combine(fixtureRoot, "uac-smoke-fixture.tmp");
            File.WriteAllText(fixtureFile, "synthetic fixture data written only for this smoke test");
            var old = DateTime.UtcNow.AddDays(-30);
            File.SetLastWriteTimeUtc(fixtureFile, old);
            File.SetCreationTimeUtc(fixtureFile, old);
            Console.WriteLine($"Synthetic fixture root: {fixtureRoot}");

            var capability = BuiltInExecutionActions.ClyrOwnedTempArtifacts;
            var info = new FileInfo(fixtureFile);
            var target = new HelperTargetManifestItem("smoke-item-1", "smoke-target-1", fixtureFile, info.Length, info.LastWriteTimeUtc, false);
            var nowUtc = DateTimeOffset.UtcNow;
            var requestId = Guid.NewGuid();
            var request = new HelperRequest(HelperProtocol.Version, requestId, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                Guid.NewGuid(), WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Current user SID unavailable."),
                "uac-smoke-drive", capability.ActionId, capability.TrustedRootIdentity, fixtureRoot,
                Guid.NewGuid().ToString("D"), new string('0', 64), nowUtc, nowUtc.AddMinutes(2), [target]);

            var pipeName = ElevatedHelperIpc.NewPipeName();
            Console.WriteLine("Launching the elevated helper through a real UAC prompt. Please approve it to continue...");
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = helperPath,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    ArgumentList = { pipeName }
                }
            };
            if (!process.Start())
                throw new InvalidOperationException("The elevated helper process failed to start (UAC prompt may have been declined).");

            var response = ElevatedHelperIpc.SendRequestAsync(pipeName, request, TimeSpan.FromSeconds(timeoutSeconds), CancellationToken.None)
                .GetAwaiter().GetResult();
            if (!process.WaitForExit(5000))
                throw new InvalidOperationException("The elevated helper process did not exit after responding.");

            if (response.ProtocolVersion != HelperProtocol.Version) throw new InvalidOperationException("Protocol version mismatch in response.");
            if (response.RequestId != requestId) throw new InvalidOperationException("Response request ID did not match.");
            if (response.Status != HelperResponseStatus.Completed) throw new InvalidOperationException($"Expected Completed, got {response.Status}.");
            if (response.Items.Length != 1 || response.Items[0].Outcome != ExecutionItemOutcome.Removed)
                throw new InvalidOperationException("Expected exactly one Removed item result.");
            if (File.Exists(fixtureFile)) throw new InvalidOperationException("The synthetic fixture file was not removed.");
            if (!Directory.Exists(fixtureRoot)) throw new InvalidOperationException("The fixture root itself was incorrectly removed.");

            Console.WriteLine($"Helper version: {response.HelperVersion}");
            Console.WriteLine($"Response status: {response.Status}; items: {response.Items.Length}");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("PHASE 6 UAC SMOKE TEST: PASS");
            Console.WriteLine("The elevated helper was launched through a real UAC prompt, independently validated and removed exactly " +
                "one synthetic fixture file under a synthetic fixture root, and exited. No real user, system, browser, Docker, WSL, " +
                "package-cache, or project data was touched.");
            Console.ResetColor();
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"FAILURE DETAIL: {ex.Message}");
            Console.WriteLine("PHASE 6 UAC SMOKE TEST: FAIL");
            Console.WriteLine("Phase 6 is not complete because the required fixture-only UAC smoke test has not passed.");
            Console.ResetColor();
            return 1;
        }
        finally
        {
            if (fixtureRoot is not null && Directory.Exists(fixtureRoot))
            {
                try { Directory.Delete(fixtureRoot, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Clyr.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (Clyr.sln not found).");
    }
}
