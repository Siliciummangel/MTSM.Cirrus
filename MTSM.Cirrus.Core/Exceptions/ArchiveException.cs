namespace MTSM.Cirrus.Core.Exceptions;

public class ArchiveException : Exception
{
    public ArchiveException(string message)
        : base(message)
    {
    }

    public ArchiveException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}