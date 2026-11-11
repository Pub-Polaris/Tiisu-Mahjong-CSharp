using System.Net;
using System.Text;
using Xunit;

namespace Tiisu.Api.Tests;

/// <summary>本机 API 的 Origin 白名单（审计 P1-6）。</summary>
public sealed class ApiOriginTests
{
    private static StringContent JsonBody(string json)
        => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task 外部来源的写请求被拒绝()
    {
        await using var server = await ApiTestServer.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/settings")
        {
            Content = JsonBody("""{"thinkSeconds":1}"""),
        };
        request.Headers.Add("Origin", "https://evil.example");

        var response = await server.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 本机来源的写请求放行并回显Origin()
    {
        await using var server = await ApiTestServer.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/settings")
        {
            Content = JsonBody("""{"thinkSeconds":2}"""),
        };
        request.Headers.Add("Origin", "http://127.0.0.1:5173");

        var response = await server.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("http://127.0.0.1:5173",
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task 无Origin的请求保持可用()
    {
        await using var server = await ApiTestServer.StartAsync();

        var response = await server.Client.GetAsync("/api/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
