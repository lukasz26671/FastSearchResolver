using System.Text.Json;

namespace FastSearchResolver;

using System.Text.Json.Serialization;

public record BangDefinition
{
    [JsonPropertyName("t")]
    [JsonInclude]
    public string Tag { get; init; } = null!;

    [JsonPropertyName("u")]
    [JsonInclude]
    public string UrlTemplate { get; init; } = null!;
    public static List<BangDefinition>? FromJsonArray(Stream jsonStream) => JsonSerializer.Deserialize(jsonStream, SearchResultContext.Default.ListBangDefinition);
}

