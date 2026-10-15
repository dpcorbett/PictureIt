namespace PictureIt.Infrastructure.Tests;

/// <summary>
/// A per-test temporary folder holding copies of the sample images, so tests can write freely.
/// </summary>
public sealed class TestFiles : IDisposable
{
    public TestFiles()
    {
        Directory = Path.Combine(Path.GetTempPath(), "PictureIt.Tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
    }

    /// <summary>
    /// The sample JPEG: Canon EOS R5, RF50mm, 2024:06:15 14:32:10, 1/250 f/2.8 ISO 400 50mm, GPS near Big Ben,
    /// keywords London + Big Ben (XMP-dc:Subject and IPTC:Keywords), Artist, Copyright, serial number.
    /// </summary>
    public const string SampleJpeg = "sample.jpg";

    /// <summary>The sample PNG: no metadata.</summary>
    public const string SamplePng = "sample.png";

    public string Directory { get; }

    /// <summary>Copies a sample into the temp folder, optionally under a different name.</summary>
    public string Copy(string sample, string? name = null)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", sample);
        var destination = Path.Combine(Directory, name ?? sample);
        File.Copy(source, destination);
        return destination;
    }

    public string PathFor(string name) => Path.Combine(Directory, name);

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
