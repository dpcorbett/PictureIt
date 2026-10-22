using PictureIt.Core.History;

namespace PictureIt.Infrastructure.Settings;

/// <summary>Batch history in a JSON file, capped at <see cref="MaxRecords"/> (the oldest are dropped).</summary>
public sealed class JsonHistoryStore(string filePath) : IHistoryStore
{
    public const int MaxRecords = 500;

    public static string DefaultFilePath => Path.Combine(JsonFile.DataDirectory, "history.json");

    public IReadOnlyList<BatchRecord> Load() => JsonFile.Read<List<BatchRecord>>(filePath, []);

    public void Save(IReadOnlyList<BatchRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        JsonFile.Write(filePath, records.Take(MaxRecords).ToList());
    }
}
