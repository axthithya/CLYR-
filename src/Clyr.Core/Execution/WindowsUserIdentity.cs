using System.Runtime.Versioning;
using System.Security.Principal;

namespace Clyr.Core.Execution;

[SupportedOSPlatform("windows")]
public static class WindowsUserIdentity
{
    public static string CurrentSid() => WindowsIdentity.GetCurrent().User?.Value
        ?? throw new InvalidOperationException("The current Windows user identity is unavailable.");
}
