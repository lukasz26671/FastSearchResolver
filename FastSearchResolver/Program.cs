using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using BangsMap = System.Collections.Generic.IDictionary<string, FastSearchResolver.BangDefinition>;

namespace FastSearchResolver;
internal static partial class Program
{
    private static string _defaultBangKey = null!;
    private static readonly BangsMap Engines = BangDefinition.FromJsonArray(File.OpenRead("bang.json"))!
        .ToImmutableDictionary(x => x.Tag, x => x, StringComparer.OrdinalIgnoreCase);
    
    private static BangDefinition DefaultBang => Engines[_defaultBangKey];
    private static readonly Regex BangRegex = BangSearchRegex();
    
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, SearchResultContext.Default);
            options.SerializerOptions.TypeInfoResolverChain.Insert(1, ConfigJsonContext.Default);
        });
        
        var config = Config.LoadFromJson("config.json");
        _defaultBangKey = config.DefaultBang;
        Console.WriteLine($"Using default bang key: {_defaultBangKey}");

        if (config.UseCustomBind)
        {
            builder.WebHost.ConfigureKestrel(o =>
            {

                Console.WriteLine($"Using custom http binding: 0.0.0.0:{config.HttpPort!.Value}");

                if (config.UseHttps.GetValueOrDefault())
                {
                    Console.WriteLine($"Using custom https binding: 0.0.0.0:{config.HttpsPort!.Value}");
                    o.ConfigureEndpointDefaults(lo => lo.Protocols = HttpProtocols.Http1AndHttp2AndHttp3);
                }


                // Always enable HTTP
                o.ListenAnyIP(config.HttpPort!.Value);

                // Enable HTTPS if configured
                if (config.UseHttps.GetValueOrDefault() && !string.IsNullOrEmpty(config.CertificatePath))
                {
                    o.ListenAnyIP(config.HttpsPort!.Value,
                        listenOptions =>
                        {
                            listenOptions.UseHttps(config.CertificatePath, config.CertificatePassword);
                        });
                }

            });
        }

        var app = builder.Build();

        app.MapGet("/", (HttpContext context) =>
        {
            var query = context.Request.Query["q"];
            if (!string.IsNullOrEmpty(query))
            {
                // Handle "/?q=" separately
                return HandleSearchResult(query!);
            }

            context.Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
            return GetReadFileResult("index.html");
        });

        app.MapGet("/static/{fileName}", (HttpContext context, string fileName) =>
        {
            context.Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
            return GetReadFileResult(fileName);
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
    private static IResult GetReadFileResult(string? relativePath)
    {
        if (relativePath == null)
            return Results.BadRequest();

        var finalPath = Path.Combine(Directory.GetCurrentDirectory(), "static", relativePath);
        
        try
        {
            var fs = new FileStream(finalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Results.File(fs, contentType: GetContentType(finalPath));
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound();
        }
        catch (PathTooLongException)
        {
            return Results.BadRequest();
        }
        catch (IOException)
        {
            return Results.InternalServerError();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Results.NotFound();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException)
        {
            return Results.Forbid();
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
        var match = BangRegex.Match(query);
        var bangCandidate = match.Success ? match.Groups[1].Value.ToLowerInvariant() : string.Empty;

        // Find matching bang or use default
        var selectedBang = Engines.TryGetValue(bangCandidate, out var bang) ? bang : DefaultBang;

        // Remove the first bang from the query
        string cleanQuery = BangRegex.Replace(query, "", 1).Trim();

        // Encode and clean URL
        string encodedQuery = HttpUtility.UrlEncode(cleanQuery).Replace("%2F", "/");

        // Format URL with encoded query
        return selectedBang.UrlTemplate.Replace("{{{s}}}", encodedQuery, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"!(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, "pl-PL")]
    private static partial Regex BangSearchRegex();
}

