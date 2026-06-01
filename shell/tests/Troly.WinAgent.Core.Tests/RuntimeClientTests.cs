using System.Net;

namespace Troly.WinAgent.Core.Tests;

public class RuntimeClientTests
{
    private static RuntimeClient ClientFor(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(new HttpClient(new StubHttpMessageHandler(responder)));

    [Fact]
    public async Task Validate_succeeds_when_health_serverId_matches_and_status_ok()
    {
        var conn = new RuntimeConnection(5000, "tok", "srv-1");
        using var client = ClientFor(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path == "/health")
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"serverId\":\"srv-1\"}");
            // /status requires the bearer token.
            var auth = req.Headers.Authorization;
            if (auth is null || auth.Scheme != "Bearer" || auth.Parameter != "tok")
                return StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":\"unauthorized\"}");
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"ok\":true}");
        });

        var result = await client.ValidateAsync(conn);

        Assert.True(result.Ok);
        Assert.Equal("srv-1", result.ServerId);
    }

    [Fact]
    public async Task Validate_fails_on_server_id_mismatch()
    {
        var conn = new RuntimeConnection(5000, "tok", "expected-srv");
        using var client = ClientFor(req =>
            req.RequestUri!.AbsolutePath == "/health"
                ? StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"serverId\":\"other-srv\"}")
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"ok\":true}"));

        var result = await client.ValidateAsync(conn);

        Assert.False(result.Ok);
        Assert.Equal("server_id_mismatch", result.Reason);
    }

    [Fact]
    public async Task Validate_fails_when_status_unauthorized()
    {
        var conn = new RuntimeConnection(5000, "wrong-token", "srv-1");
        using var client = ClientFor(req =>
            req.RequestUri!.AbsolutePath == "/health"
                ? StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"serverId\":\"srv-1\"}")
                : StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":\"unauthorized\"}"));

        var result = await client.ValidateAsync(conn);

        Assert.False(result.Ok);
        Assert.Equal("status_not_ok", result.Reason);
    }

    [Fact]
    public async Task Validate_fails_when_health_not_ok()
    {
        var conn = new RuntimeConnection(5000, "tok", null);
        using var client = ClientFor(_ => StubHttpMessageHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"));

        var result = await client.ValidateAsync(conn);

        Assert.False(result.Ok);
        Assert.Equal("health_not_ok", result.Reason);
    }
}
