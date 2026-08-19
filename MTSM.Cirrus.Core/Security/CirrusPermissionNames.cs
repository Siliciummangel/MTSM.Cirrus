namespace MTSM.Cirrus.Core.Security;

public static class CirrusPermissionNames
{
    public const string ArchiveRead = "archive.read";
    public const string ArchiveWrite = "archive.write";
    public const string ArchiveDelete = "archive.delete";
    public const string ArchiveVerify = "archive.verify";

    public static string ToExternalName(this CirrusPermission permission) =>
        permission switch
        {
            CirrusPermission.ArchiveRead => ArchiveRead,
            CirrusPermission.ArchiveWrite => ArchiveWrite,
            CirrusPermission.ArchiveDelete => ArchiveDelete,
            CirrusPermission.ArchiveVerify => ArchiveVerify,
            _ => throw new ArgumentOutOfRangeException(nameof(permission))
        };
}
