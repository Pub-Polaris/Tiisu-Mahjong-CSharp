using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.FileProviders;

namespace Tiisu.Api;

public static class TiisuApiApp
{
    /// <summary>
    /// Builds the Kestrel host. Tests call this directly with an explicit
    /// web root / data directory and port 0.
    /// </summary>
    public static WebApplication Build(string[] args, TiisuApiOptions? overrides = null)
    {
        var options = ResolvedTiisuApiOptions.Resolve(args, overrides);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = options.ContentRoot
        });

        builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");

        var store = new DataStore(options.DataDirectory);
        builder.Services.AddSingleton(store);

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            if (HttpMethods.IsOptions(context.Request.Method))
            {
                context.Response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
                context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
                context.Response.StatusCode = StatusCodes.Status200OK;
                return;
            }

            await next();
        });

        if (!Directory.Exists(options.WebRoot))
        {
            throw new DirectoryNotFoundException($"Web root not found: {options.WebRoot}");
        }

        // The Python server redirects "/" to portal.html (not index.html).
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/")
            {
                context.Response.Redirect("/portal.html");
                return;
            }

            await next();
        });

        var fileProvider = new PhysicalFileProvider(options.WebRoot);
        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = fileProvider
        });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider
        });

        app.MapGet("/api/settings", () => Json(store.LoadSettings()));

        app.MapPost("/api/settings", async (HttpRequest request) =>
        {
            var body = await ReadJsonBodyAsync(request);
            if (!DataStore.IsTruthy(body))
            {
                return Json(store.LoadSettings());
            }

            return Json(store.SaveSettings(body!));
        });

        app.MapGet("/api/state", () => Json(store.ReadNode(store.StatePath) ?? new JsonObject()));

        app.MapPost("/api/state", async (HttpRequest request) =>
        {
            var body = await ReadJsonBodyAsync(request);
            if (!DataStore.IsTruthy(body))
            {
                return Json(store.ReadNode(store.StatePath) ?? new JsonObject());
            }

            store.WriteJson(store.StatePath, body);
            return Json(body);
        });

        app.MapGet("/api/stats", () => Json(store.LoadStats()));

        app.MapPost("/api/stats", async (HttpRequest request) =>
        {
            var body = await ReadJsonBodyAsync(request);
            return Json(store.PostStats(body));
        });

        app.MapPost("/api/log", async (HttpRequest request) =>
        {
            var body = await ReadJsonBodyAsync(request);
            var message = body is JsonObject obj ? DataStore.GetString(obj, "message") : string.Empty;
            store.AppendLogMessage(message);
            return Results.Text("ok");
        });

        app.MapPost("/api/chrlog", async (HttpRequest request) =>
        {
            var body = await ReadJsonBodyAsync(request);
            if (body is not JsonObject obj)
            {
                return Results.Text("ok");
            }

            store.AppendChrLog(DataStore.GetString(obj, "session"), DataStore.GetString(obj, "entry"));
            return Results.Text("ok");
        });

        app.MapPost("/api/round_end", async (HttpRequest request) =>
        {
            var body = await ReadJsonBodyAsync(request);
            var session = body is JsonObject obj ? DataStore.GetString(obj, "session") : string.Empty;
            var archived = store.ArchiveSession(session);
            return Results.Text("archived:" + (archived ?? "none"));
        });

        app.MapPost("/api/roundlog", async (HttpRequest request) =>
        {
            var body = await ReadJsonBodyAsync(request);
            if (body is not JsonObject obj)
            {
                return Results.Text("ok");
            }

            var session = DataStore.GetString(obj, "session");
            var round = (int)DataStore.ToInt(obj["round"]);
            var player = (int)DataStore.ToInt(obj["player"]);
            var playerUuid = DataStore.GetString(obj, "playerUUID");
            if (playerUuid.Length == 0)
            {
                playerUuid = DataStore.GetString(obj, "uuid");
            }

            var events = obj["events"] as JsonArray;
            if (events is not null)
            {
                store.AppendRoundLog(session, round, player, playerUuid, events);
            }

            return Results.Text("ok");
        });

        app.MapGet("/api/roundlog", (HttpRequest request) =>
        {
            var session = request.Query["session"].ToString();
            var roundText = request.Query["round"].ToString();
            var playerText = request.Query["player"].ToString();

            int? round = null;
            if (!string.IsNullOrWhiteSpace(roundText))
            {
                if (!int.TryParse(roundText, out var parsedRound))
                {
                    return Results.Text(string.Empty, "application/x-ndjson; charset=utf-8");
                }

                round = parsedRound;
            }

            int? player = null;
            if (!string.IsNullOrWhiteSpace(playerText))
            {
                if (!DataStore.TryParsePlayerFilter(playerText, out var parsedPlayer))
                {
                    return Results.Text(string.Empty, "application/x-ndjson; charset=utf-8");
                }

                player = parsedPlayer;
            }

            var lines = store.QueryRoundLogs(
                string.IsNullOrWhiteSpace(session) ? null : session,
                round,
                player);

            var text = lines.Count == 0 ? string.Empty : string.Join('\n', lines) + "\n";
            return Results.Text(text, "application/x-ndjson; charset=utf-8");
        });

        return app;
    }

    private static IResult Json(JsonNode? node)
        => Results.Text(DataStore.Serialize(node), "application/json; charset=utf-8");

    private static async Task<JsonNode?> ReadJsonBodyAsync(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var text = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
