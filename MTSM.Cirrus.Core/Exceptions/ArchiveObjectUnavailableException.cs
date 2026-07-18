using System;
using System.Collections.Generic;
using System.Text;

using MTSM.Cirrus.Core.Enums;

namespace MTSM.Cirrus.Core.Exceptions;

public sealed class ArchiveObjectUnavailableException : ArchiveException
{
    public ArchiveObjectUnavailableException(
        long archiveObjectId,
        ArchiveStatus archiveStatus)
        : base(
            $"Archive object {archiveObjectId} cannot be accessed " +
            $"because its status is '{archiveStatus}'.")
    {
        ArchiveObjectId = archiveObjectId;
        ArchiveStatus = archiveStatus;
    }

    public long ArchiveObjectId { get; }

    public ArchiveStatus ArchiveStatus { get; }
}