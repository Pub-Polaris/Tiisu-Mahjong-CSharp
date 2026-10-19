using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Tiisu.Api.Tests;

internal sealed class ApiTestServer : IAsyncDisposable
{
    private ApiTestServer(string root, WebApplication app, HttpClient client)
    {
        Root = root;
        App = app;
        Client = client;
    }

    public string Root { get; }

    public WebApplication App { get; }

    public HttpClient Client { get; }

    public static async Task<ApiTestServer> StartAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "tiisu_api_tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        await File.WriteAllTextAsync(
            Path.Combine(root, "portal.html"),
            "<!doctype html><title>portal</title><div id=\"portal-marker\">portal</div>",
            new UTF8Encoding(false));
        await File.WriteAllTextAsync(
            Path.Combine(root, "app.js"),
            "window.__test = 1;\n",
            new UTF8Encoding(false));

        var options = new TiisuApiOptions
        {
            Port = 0,
            WebRoot = root,
            DataDirectory = root,
            ContentRoot = root
        };

        var app = TiisuApiApp.Build(Array.Empty<string>(), options);
        await app.StartAsync();

        var addresses = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()!
            .Addresses;
        var address = addresses.First();

        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var client = new HttpClient(handler) { BaseAddress = new Uri(address) };
        return new ApiTestServer(root, app, client);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // test cleanup is best effort
        }
    }
}

public sealed class ApiEndpointsTests
{
    private static StringContent JsonBody(string json)
        => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(text)!;
    }

    [Fact]
    public async Task Settings_get_returns_defaults_and_post_merges()
    {
        await using var server = await ApiTestServer.StartAsync();

        var get = await server.Client.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var defaults = await ReadJsonAsync(get);
        Assert.Equal(30, (int)defaults["thinkSeconds"]!);
        Assert.False((bool)defaults["recordTiles"]!);

        var post = await server.Client.PostAsync(
            "/api/settings",
            JsonBody("""{"thinkSeconds":12,"recordTiles":true}"""));
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var merged = await ReadJsonAsync(post);

        Assert.Equal(12, (int)merged["thinkSeconds"]!);
        Assert.True((bool)merged["recordTiles"]!);
        Assert.True(merged.AsObject().ContainsKey("showMa"));
        Assert.True(merged.AsObject().ContainsKey("showWallViewer"));

        var again = await ReadJsonAsync(await server.Client.GetAsync("/api/settings"));
        Assert.True(JsonNode.DeepEquals(merged, again));

        // server.py treats a POST with an empty object as a GET and does not write.
        var emptyPost = await ReadJsonAsync(await server.Client.PostAsync(
            "/api/settings",
            JsonBody("{}")));
        Assert.Equal(12, (int)emptyPost["thinkSeconds"]!);

        var file = await File.ReadAllTextAsync(Path.Combine(server.Root, "settings.json"));
        Assert.Contains("\"thinkSeconds\": 12", file);
    }

    [Fact]
    public async Task State_roundtrips_and_empty_post_returns_current()
    {
        await using var server = await ApiTestServer.StartAsync();

        var missing = await ReadJsonAsync(await server.Client.GetAsync("/api/state"));
        Assert.Empty(missing.AsObject());

        const string state = """{"inProgress":true,"handNumber":3,"scores":[1,2,3,4],"playerUUID":"u-1"}""";
        var post = await server.Client.PostAsync("/api/state", JsonBody(state));
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(state), await ReadJsonAsync(post)));

        var get = await ReadJsonAsync(await server.Client.GetAsync("/api/state"));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(state), get));

        var emptyPost = await ReadJsonAsync(await server.Client.PostAsync(
            "/api/state",
            JsonBody("{}")));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(state), emptyPost));
    }

    [Fact]
    public async Task Stats_accumulates_normalizes_legacy_and_resets()
    {
        await using var server = await ApiTestServer.StartAsync();
        var statsPath = Path.Combine(server.Root, "stats.json");
        await File.WriteAllTextAsync(
            statsPath,
            """
            {
              "daxingqiCount": 2,
              "wins": 1,
              "rounds": 4,
              "byPlayer": { "你": { "daxingqi": 3, "wins": 2 } }
            }
            """,
            new UTF8Encoding(false));

        var normalized = await ReadJsonAsync(await server.Client.GetAsync("/api/stats"));
        Assert.Equal(2, (int)normalized["tiisuinCount"]!);
        Assert.False(normalized.AsObject().ContainsKey("daxingqiCount"));
        Assert.Equal(3, (int)normalized["byPlayer"]!["你"]!["tiisuin"]!);
        Assert.False(normalized["byPlayer"]!["你"]!.AsObject().ContainsKey("daxingqi"));

        var first = await ReadJsonAsync(await server.Client.PostAsync(
            "/api/stats",
            JsonBody("""{"wins":1,"tiisuin":1,"player":"AI 南"}""")));
        Assert.Equal(3, (int)first["tiisuinCount"]!);
        Assert.Equal(2, (int)first["wins"]!);
        Assert.Equal(1, (int)first["byPlayer"]!["AI 南"]!["wins"]!);
        Assert.Equal(1, (int)first["byPlayer"]!["AI 南"]!["tiisuin"]!);

        var second = await ReadJsonAsync(await server.Client.PostAsync(
            "/api/stats",
            JsonBody("""{"witness":["你","AI 西"],"rounds":1}""")));
        Assert.Equal(2, (int)second["witnessCount"]!);
        Assert.Equal(5, (int)second["rounds"]!);
        Assert.Equal(1, (int)second["byPlayer"]!["你"]!["witness"]!);
        Assert.Equal(1, (int)second["byPlayer"]!["AI 西"]!["witness"]!);

        var reset = await ReadJsonAsync(await server.Client.PostAsync(
            "/api/stats",
            JsonBody("""{"kind":"reset"}""")));
        Assert.Equal(0, (int)reset["wins"]!);
        Assert.Empty(reset["byPlayer"]!.AsObject());
    }

    [Fact]
    public async Task Roundlog_writes_jsonl_without_cjk_and_queries_back()
    {
        await using var server = await ApiTestServer.StartAsync();

        const string payload = """
        {
          "session": "260102_01-02-03",
          "round": 3,
          "player": 1,
          "playerUUID": "uuid-1",
          "events": [
            {"type":"round","round":3,"honba":0,"player":1,"name":"ai_south","wind":"south","dealer":false,"riichi":false,"uuid":"uuid-1","session":"260102_01-02-03","roundWindIdx":0,"dealerCount":0,"extension":false},
            {"type":"step","a":"draw","p":1,"t":"1m","hand":"123m456p789s1122z","melds":[],"discards":["z2"],"wall":79,"rinshan":4},
            {"type":"step","a":"discard","p":1,"t":"1m","hand":"23m456p789s1122z","melds":[{"k":"chi","t":"234m"}],"discards":["z2","1m"],"wall":79,"rinshan":4}
          ]
        }
        """;

        var post = await server.Client.PostAsync("/api/roundlog", JsonBody(payload));
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.Equal("ok", await post.Content.ReadAsStringAsync());

        var file = Path.Combine(
            server.Root,
            "logs",
            "260102_01-02-03",
            "round_003_p1_uuid-1",
            "events.jsonl");
        Assert.True(File.Exists(file));

        var text = await File.ReadAllTextAsync(file);
        Assert.False(Regex.IsMatch(text, "[\u4e00-\u9fff]"), "JSONL must not contain CJK characters");
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.All(lines, line => Assert.NotNull(JsonNode.Parse(line)));
        Assert.Equal("draw", JsonNode.Parse(lines[1])!.AsObject()["a"]!.GetValue<string>());

        var all = await server.Client.GetStringAsync(
            "/api/roundlog?session=260102_01-02-03&player=1&round=3");
        Assert.Equal(3, all.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);

        var bySlug = await server.Client.GetStringAsync(
            "/api/roundlog?session=260102_01-02-03&player=ai_south&round=3");
        Assert.Equal(3, bySlug.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);

        var otherRound = await server.Client.GetStringAsync(
            "/api/roundlog?session=260102_01-02-03&round=2");
        Assert.Equal(string.Empty, otherRound);

        var otherPlayer = await server.Client.GetStringAsync(
            "/api/roundlog?session=260102_01-02-03&player=2");
        Assert.Equal(string.Empty, otherPlayer);
    }

    [Fact]
    public async Task Root_redirects_to_portal_and_static_files_are_served()
    {
        await using var server = await ApiTestServer.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        using var response = await server.Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/portal.html", response.Headers.Location!.OriginalString);

        var portal = await server.Client.GetStringAsync("/portal.html");
        Assert.Contains("portal-marker", portal);

        var js = await server.Client.GetAsync("/app.js");
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
        Assert.Contains("__test", await js.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Log_and_chrlog_append_text_files()
    {
        await using var server = await ApiTestServer.StartAsync();

        var log = await server.Client.PostAsync("/api/log", JsonBody("""{"message":"hello"}"""));
        Assert.Equal("ok", await log.Content.ReadAsStringAsync());

        var chr = await server.Client.PostAsync(
            "/api/chrlog",
            JsonBody("""{"session":"S1","entry":"a\nb"}"""));
        Assert.Equal("ok", await chr.Content.ReadAsStringAsync());

        Assert.Equal(
            "hello\n",
            await File.ReadAllTextAsync(Path.Combine(server.Root, "logs", "output.txt")));
        Assert.Equal(
            "a\nb\n",
            await File.ReadAllTextAsync(Path.Combine(server.Root, "logs", "S1_chr_console.log")));
    }

    [Fact]
    public async Task Round_end_archives_the_session_directory()
    {
        await using var server = await ApiTestServer.StartAsync();
        var sessionDir = Path.Combine(server.Root, "logs", "260102_04-05-06", "round_001_p0_u");
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(
            Path.Combine(sessionDir, "events.jsonl"),
            "{\"type\":\"round\"}\n",
            new UTF8Encoding(false));

        var response = await server.Client.PostAsync(
            "/api/round_end",
            JsonBody("""{"session":"260102_04-05-06"}"""));
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("archived:", body);
        Assert.False(Directory.Exists(Path.Combine(server.Root, "logs", "260102_04-05-06")));

        var archive = body["archived:".Length..];
        Assert.True(File.Exists(archive));
        var magic = new byte[2];
        await using (var stream = File.OpenRead(archive))
        {
            await stream.ReadExactlyAsync(magic);
        }

        Assert.Equal(new byte[] { 0x1F, 0x8B }, magic);
    }
}
