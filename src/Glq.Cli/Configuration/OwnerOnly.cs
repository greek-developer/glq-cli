using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Glq.Cli.Configuration;

/// <summary>
/// Owner-only file and folder permissions: <c>0700</c>/<c>0600</c> on macOS and Linux, an ACL
/// that grants only the current user on Windows. <c>config.json</c> holds plaintext tokens, so
/// glq creates it this way and refuses to use it otherwise.
/// </summary>
internal static class OwnerOnly
{
    private const UnixFileMode OthersMask =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    internal static void CreateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>Restricts an existing file to its owner.</summary>
    internal static void Protect(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            ProtectWindows(path);
            return;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>
    /// Returns why <paramref name="path"/> is readable by someone other than its owner, or
    /// <c>null</c> when it is owner-only.
    /// </summary>
    internal static string? FindExposure(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return FindExposureWindows(path);
        }

        var mode = File.GetUnixFileMode(path);
        return (mode & OthersMask) == 0
            ? null
            : $"its mode is {Convert.ToString((int)mode, 8).PadLeft(4, '0')}, not 0600";
    }

    [SupportedOSPlatform("windows")]
    private static void ProtectWindows(string path)
    {
        var user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The current Windows user has no SID.");

        var security = new FileSecurity();
        security.SetOwner(user);
        // Drop inherited entries - SYSTEM, Administrators, Users - and grant the owner alone.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static string? FindExposureWindows(string path)
    {
        // Rights that let a principal read file contents, including the generic forms an
        // inherited entry can carry.
        const FileSystemRights readRights =
            FileSystemRights.ReadData | FileSystemRights.FullControl |
            unchecked((FileSystemRights)0x80000000) | (FileSystemRights)0x10000000;

        var user = WindowsIdentity.GetCurrent().User;
        var rules = new FileInfo(path).GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));

        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType == AccessControlType.Allow &&
                (rule.FileSystemRights & readRights) != 0 &&
                !rule.IdentityReference.Equals(user))
            {
                return $"its access list lets {Describe(rule.IdentityReference)} read it";
            }
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static string Describe(IdentityReference identity)
    {
        try
        {
            return identity.Translate(typeof(NTAccount)).Value;
        }
        catch (IdentityNotMappedException)
        {
            return identity.Value;
        }
    }
}
