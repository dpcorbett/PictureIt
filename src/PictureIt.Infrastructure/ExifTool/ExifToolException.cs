namespace PictureIt.Infrastructure.ExifTool;

public sealed class ExifToolException : Exception
{
    public ExifToolException()
    {
    }

    public ExifToolException(string message)
        : base(message)
    {
    }

    public ExifToolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
