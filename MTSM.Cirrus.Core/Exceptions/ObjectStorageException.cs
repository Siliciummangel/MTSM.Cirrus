namespace MTSM.Cirrus.Core.Exceptions;

/// <summary>
/// Represents a technical failure while communicating with object storage.
/// The public message deliberately excludes storage locations and provider details.
/// </summary>
public sealed class ObjectStorageException : Exception
{
    public ObjectStorageException(string message)
        : base(message)
    {
    }

    public ObjectStorageException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
