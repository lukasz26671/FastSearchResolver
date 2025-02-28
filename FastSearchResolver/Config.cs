using System.Text.Json;
namespace FastSearchResolver;

public record Config
{
    public required string DefaultBang { get; init; }
    public required bool UseCustomBind { get; init; }
    public required string MainBangs { get; init; }
    public string? CustomBangs { get; init; }
    public int? HttpPort { get; init; }
    public int? HttpsPort { get; init; }
    public bool? UseHttps { get; init; }
    public string? CertificatePath { get; init; } = string.Empty;
    public string? CertificatePassword { get; init; } = string.Empty;

    /// <summary>
    /// Loads the configuration from a JSON file.
    /// </summary>
    /// <param name="filePath">Path to the JSON configuration file.</param>
    /// <returns>A Config object populated with values from the JSON file.</returns>
    public static Config LoadFromJson(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Configuration file not found: {filePath}");
        }

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<Config>(json, ConfigJsonContext.Default.Config) 
               ?? throw new InvalidOperationException("Failed to deserialize configuration.");
    }
}