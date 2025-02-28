namespace FastSearchResolver;

using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Config))]
public partial class ConfigJsonContext : JsonSerializerContext
{
}
