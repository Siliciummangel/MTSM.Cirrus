using System;
using System.Collections.Generic;
using System.Text;

namespace MTSM.Cirrus.Core.Exceptions;

public sealed class ArchiveObjectNotFoundException : ArchiveException
{
    public ArchiveObjectNotFoundException(long archiveObjectId)
        : base($"Archive object {archiveObjectId} does not exist.")
    {
        ArchiveObjectId = archiveObjectId;
    }

    public long ArchiveObjectId { get; }
}