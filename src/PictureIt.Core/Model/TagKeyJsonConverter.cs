using System.Text.Json;
using System.Text.Json.Serialization;

namespace PictureIt.Core.Model;

/// <summary>Stores a <see cref="TagKey"/> as its readable "Group:Name" form in templates and history files.</summary>
public sealed class TagKeyJsonConverter : JsonConverter<TagKey>
{
    public override TagKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        TagKey.TryParse(reader.GetString(), out var key) ? key : throw new JsonException($"'{reader.GetString()}' is not a tag key.");

    public override void Write(Utf8JsonWriter writer, TagKey value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(value.ToString());
    }
}
