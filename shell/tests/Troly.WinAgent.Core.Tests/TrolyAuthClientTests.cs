using System.Net;

namespace Troly.WinAgent.Core.Tests;

public class TrolyAuthClientTests
{
    private static TrolyAuthClient ClientFor(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(new RuntimeConnection(5000, "loopback-tok", "sid"), new HttpClient(new StubHttpMessageHandler(responder)));

    [Fact]
    public async Task LoginAsync_parses_token_expiry_and_userId()
    {
        long expMs = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds();
        using var client = ClientFor(req =>
        {
            Assert.Equal("/troly/login", req.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("loopback-tok", req.Headers.Authorization!.Parameter);
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, $"{{\"ok\":true,\"token\":\"jwt-abc\",\"expiresAt\":{expMs},\"userId\":\"u1\"}}");
        });

        var result = await client.LoginAsync("a@b.c", "pw");

        Assert.Equal("jwt-abc", result.Token);
        Assert.Equal("u1", result.UserId);
        Assert.Equal(expMs, result.ExpiresAt!.Value.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task LoginAsync_maps_error_body_to_TrolyAuthException()
    {
        using var client = ClientFor(_ =>
            StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":\"troly_unauthorized\"}"));

        var ex = await Assert.ThrowsAsync<TrolyAuthException>(() => client.LoginAsync("a@b.c", "bad"));
        Assert.Equal("troly_unauthorized", ex.Code);
        Assert.Equal(401, ex.HttpStatus);
    }

    [Fact]
    public async Task FetchKeysAsync_parses_key_presence()
    {
        using var client = ClientFor(req =>
        {
            Assert.Equal("/troly/keys", req.RequestUri!.AbsolutePath);
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"ok\":true,\"hasAnthropic\":true,\"hasDeepgram\":false,\"hasGemini\":false}");
        });

        var keys = await client.FetchKeysAsync();

        Assert.True(keys.HasAnthropic);
        Assert.False(keys.HasDeepgram);
        Assert.False(keys.HasGemini);
    }

    [Fact]
    public async Task GetStatusAsync_parses_runtime_and_troly_state()
    {
        long expMs = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        using var client = ClientFor(req =>
        {
            Assert.Equal("/runtime/status", req.RequestUri!.AbsolutePath);
            return StubHttpMessageHandler.Json(HttpStatusCode.OK,
                $"{{\"ok\":true,\"serverId\":\"sid\",\"version\":\"0.1.2\",\"troly\":{{\"configured\":true,\"authenticated\":true,\"userId\":\"u1\",\"tokenExpiresAt\":{expMs},\"expiringSoon\":false,\"hasKeys\":{{\"anthropic\":true,\"deepgram\":false,\"gemini\":false}}}}}}");
        });

        var s = await client.GetStatusAsync();

        Assert.Equal("sid", s.ServerId);
        Assert.Equal("0.1.2", s.Version);
        Assert.True(s.Configured);
        Assert.True(s.Authenticated);
        Assert.Equal("u1", s.UserId);
        Assert.True(s.HasAnthropic);
        Assert.False(s.HasDeepgram);
        Assert.Equal(expMs, s.TokenExpiresAt!.Value.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task RefreshAppTokenAsync_uses_GET()
    {
        using var client = ClientFor(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("/troly/app-token", req.RequestUri!.AbsolutePath);
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"ok\":true,\"token\":\"jwt-new\",\"userId\":\"u1\"}");
        });

        var result = await client.RefreshAppTokenAsync();
        Assert.Equal("jwt-new", result.Token);
    }
}
