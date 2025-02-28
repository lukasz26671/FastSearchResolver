namespace FastSearchResolver;

using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<BangDefinition>))]
public partial class SearchResultContext : JsonSerializerContext;