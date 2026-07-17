using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Clyr.Core.DeveloperMode;

/// <summary>
/// Pure, side-effect-free validation of a single candidate file path. This never searches, never picks between
/// candidates, and never decides what counts as a trusted location — it only answers "is this exact file safe
/// to even consider." <see cref="TrustedExecutableLocator"/> is the only caller that decides which locations
/// are trusted in the first place.
/// </summary>
public static class TrustedExecutableValidation
{
    /// <summary>
    /// True only for an absolute path to a real, non-reparse-point <c>.exe</c> file. Rejects relative paths
    /// (a relative path can be redirected by an unexpected current directory), any extension other than
    /// <c>.exe</c> (batch files, PowerShell scripts, and other interpreted content are never trusted here even
    /// if renamed), and reparse points (symlinks/junctions can silently redirect to an attacker-controlled
    /// target after this check runs).
    /// </summary>
    public static bool IsTrustedExecutableFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (!Path.IsPathRooted(path)) return false;
        if (!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (!File.Exists(path)) return false;
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        return true;
    }

    /// <summary>
    /// Best-effort Authenticode signer subject, extracted directly from the file's embedded signature — this
    /// does not validate the certificate chain, trust root, or revocation status, and it deliberately never
    /// throws or blocks discovery: many legitimate Windows system files are catalog-signed rather than
    /// embedded-signed, so an absent result here must never by itself be treated as proof of tampering. The
    /// canonical-location restriction in <see cref="TrustedExecutableLocator"/> remains the actual trust
    /// boundary; this is supplementary, surfaced metadata only.
    /// </summary>
    public static string? TryGetPublisher(string path)
    {
        try
        {
            // X509Certificate.CreateFromSignedFile extracts the Authenticode signer embedded in a signed PE
            // (an .exe), which is a different operation from loading a standalone certificate file — there is
            // no non-obsolete replacement for this specific PE-signature-extraction use case as of this SDK.
#pragma warning disable SYSLIB0057
            using var certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return certificate.Subject;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Best-effort product version from file metadata, independent of any later probe output parsing.</summary>
    public static string? TryGetFileVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return string.IsNullOrWhiteSpace(info.ProductVersion) ? null : info.ProductVersion.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
