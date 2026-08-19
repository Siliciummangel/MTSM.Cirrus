using MTSM.Cirrus.Core.Security;

namespace MTSM.Cirrus.API.Security;

public static class CirrusAuthorizationPolicies
{
    public const string Read = "Cirrus.Archive.Read";
    public const string Write = "Cirrus.Archive.Write";
    public const string Delete = "Cirrus.Archive.Delete";
    public const string Verify = "Cirrus.Archive.Verify";

    public static string PermissionFor(string policy) => policy switch
    {
        Read => CirrusPermissionNames.ArchiveRead,
        Write => CirrusPermissionNames.ArchiveWrite,
        Delete => CirrusPermissionNames.ArchiveDelete,
        Verify => CirrusPermissionNames.ArchiveVerify,
        _ => throw new ArgumentOutOfRangeException(nameof(policy))
    };
}
