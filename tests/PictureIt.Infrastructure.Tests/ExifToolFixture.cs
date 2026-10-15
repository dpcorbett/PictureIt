using PictureIt.Infrastructure.ExifTool;

namespace PictureIt.Infrastructure.Tests;

/// <summary>One ExifTool process shared by every test, as in the app.</summary>
public sealed class ExifToolFixture : IAsyncLifetime
{
    public ExifToolProcess Process { get; } = new(ExifToolProcess.DefaultExecutablePath);

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => Process.DisposeAsync();

    /// <summary>SHA-256 of the image data only (not metadata), to prove writes don't touch pixels.</summary>
    public async Task<string> ImageDataHashAsync(string path)
    {
        var result = await Process.ExecuteAsync(["-s3", "-ImageDataHash", "-api", "ImageHashType=SHA256", path]);
        return result.Output.Trim();
    }
}

[CollectionDefinition(Name)]
public sealed class ExifToolCollectionDefinition : ICollectionFixture<ExifToolFixture>
{
    public const string Name = "ExifTool";
}
