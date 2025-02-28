using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using BangsMap = System.Collections.Generic.IDictionary<string, FastSearchResolver.BangDefinition>;

namespace FastSearchResolver;
internal static partial class Program
{
    private static readonly string DefaultBangKey;
    private static BangsMap _engines = null!;
    
    private static BangDefinition DefaultBang => _engines[DefaultBangKey];
    private static readonly Regex BangPrefixRegex = BangPrefixSearchRegex();
    private static readonly Regex BangSuffixRegex = BangSuffixSearchRegex();
    private static readonly Config Config = Config.LoadFromJson("config.json");

    private static void LoadBangDefinitions()
    {
        List<BangDefinition> bangDefinitions = [];

        if (File.Exists(Config.MainBangs))
        {
            using var fs = File.OpenRead(Config.MainBangs);
            bangDefinitions.AddRange(BangDefinition.FromJsonArray(fs)!);
        }
        if (File.Exists(Config.CustomBangs))
        {
            using var fs = File.OpenRead(Config.CustomBangs);
            bangDefinitions.AddRange(BangDefinition.FromJsonArray(fs)!);
        }
        
        if(bangDefinitions.Count == 0)
            throw new FileNotFoundException("Could not find any Bang definitions file.");
        
        _engines = bangDefinitions
            .ToImmutableDictionary(x => x.Tag, x => x, StringComparer.OrdinalIgnoreCase);

        Console.WriteLine($"Loaded {_engines.Count} usable bangs");
    }
    static Program()
    {
        DefaultBangKey = Config.DefaultBang;
        LoadBangDefinitions();
        Console.WriteLine($"Using default bang key: {DefaultBangKey}");
    }
    
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, SearchResultContext.Default);
            options.SerializerOptions.TypeInfoResolverChain.Insert(1, ConfigJsonContext.Default);
        });
        
        if (Config.UseCustomBind)
        {
            builder.WebHost.ConfigureKestrel(o =>
            {
                Console.WriteLine($"Using custom http binding: 0.0.0.0:{Config.HttpPort!.Value}");

                if (Config.UseHttps.GetValueOrDefault())
                {
                    Console.WriteLine($"Using custom https binding: 0.0.0.0:{Config.HttpsPort!.Value}");
                    o.ConfigureEndpointDefaults(lo => lo.Protocols = HttpProtocols.Http1AndHttp2AndHttp3);
                }

                // Always enable HTTP
                o.ListenAnyIP(Config.HttpPort!.Value);

                // Enable HTTPS if configured
                if (Config.UseHttps.GetValueOrDefault() && !string.IsNullOrEmpty(Config.CertificatePath))
                {
                    o.ListenAnyIP(Config.HttpsPort!.Value,
                        listenOptions =>
                        {
                            listenOptions.UseHttps(Config.CertificatePath, Config.CertificatePassword);
                        });
                }

            });
        }

        var app = builder.Build();

        app.MapGet("/", async (HttpContext context) =>
        {
            var query = context.Request.Query["q"];
            if (!string.IsNullOrEmpty(query))
            {
                // Handle "/?q=" separately
                return HandleSearchResult(query!);
            }

            context.Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
            return await GetReadFileResult(context, "index.html");
        });

        if (Config.UseReloadEndpoint)
        {
            app.MapGet("/reload", () =>
            {
                LoadBangDefinitions();
                return Results.Redirect("/");
            });
        }

        app.MapGet("/static/{fileName}", async (HttpContext context, string fileName) =>
        {
            context.Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
            return await GetReadFileResult(context, fileName);
        });

        app.Run();
    }
    
    private static IResult HandleSearchResult(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return Results.BadRequest();
        }

        var redirectUrl = GetBangRedirectUrl(query);
        return redirectUrl == null ? Results.UnprocessableEntity() : Results.Redirect(redirectUrl, permanent: true);
    }
    private static async Task<IResult> GetReadFileResult(HttpContext context, string? relativePath)
    {
        if (relativePath == null)
            return Results.BadRequest();

        var finalPath = Path.Combine(Directory.GetCurrentDirectory(), "static", relativePath);
        try
        {
            var contentType = GetContentType(finalPath);
            if (!relativePath.EndsWith("opensearch.xml"))
            {
                await using var fs = new FileStream(finalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Results.File(fs, contentType);
            }

            var content = (await File.ReadAllTextAsync(finalPath)).Replace("%{PUBLIC_URL}%", $"{context.Request.Scheme}://{context.Request.Host}");
            return Results.Content(content, contentType);
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound();
        }
        catch (PathTooLongException)
        {
            return Results.BadRequest();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Results.NotFound();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (IOException)
        {
            return Results.InternalServerError();
        }
    }
    
    private static string GetContentType(string fileName)
    {
        return fileName switch
        {
            _ when fileName.EndsWith(".html") => "text/html",
            _ when fileName.EndsWith(".css") => "text/css",
            _ when fileName.EndsWith(".js") => "application/javascript",
            _ when fileName.EndsWith(".png") => "image/png",
            _ when fileName.EndsWith(".jpg") || fileName.EndsWith(".jpeg") => "image/jpeg",
            _ when fileName.EndsWith(".gif") => "image/gif",
            _ when fileName.EndsWith(".svg") => "image/svg+xml",
            _ when fileName.EndsWith(".xml") => "application/xml",
            _ => "application/octet-stream",
        };
    }
    
    private static string? GetBangRedirectUrl(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        // Match first "!" bang in query
        bool prefix = true;
        var match = BangPrefixRegex.Match(query);
        if (!match.Success)
        {
            match = BangSuffixRegex.Match(query);
            prefix = false;
        }
        var bangCandidate = match.Success ? match.Groups[1].Value.ToLowerInvariant() : string.Empty;

        // Find matching bang or use default
        var selectedBang = _engines.TryGetValue(bangCandidate, out var bang) ? bang : DefaultBang;

        // Remove the first bang from the query
        string cleanQuery = (prefix ? BangPrefixRegex : BangSuffixRegex)
            .Replace(query, "", 1).Trim();

        // Encode and clean URL
        string encodedQuery = HttpUtility.UrlEncode(cleanQuery).Replace("%2F", "/");

        // Format URL with encoded query
        return selectedBang.UrlTemplate.Replace("{{{s}}}", encodedQuery, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"!(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, "pl-PL")]
    private static partial Regex BangPrefixSearchRegex();
    
    [GeneratedRegex(@"(\S+)!", RegexOptions.IgnoreCase | RegexOptions.Compiled, "pl-PL")]
    private static partial Regex BangSuffixSearchRegex();
}

