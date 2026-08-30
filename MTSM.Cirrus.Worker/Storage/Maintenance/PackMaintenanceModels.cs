using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Worker.StorageV2;

namespace MTSM.Cirrus.Worker.Maintenance;

internal sealed record PackMaintenanceWork(long StoragePackId);
internal sealed record MovedPackLocation(StorageLocation Source, long NewPackId, PackEntry Entry);
