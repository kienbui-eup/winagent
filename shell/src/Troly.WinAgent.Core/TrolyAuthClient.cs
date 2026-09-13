using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Troly.WinAgent.Core;

public sealed record TrolyTokenResult(string Token, DateTimeOffset? ExpiresAt, string? UserId);

public sealed record TrolyKeysResult(bool HasAnthropic, bool HasDeepgram, bool HasGemini);

public sealed record TrolyRuntimeStatus(
    string? ServerId,
    string? Version,
    bool Configured,
    bool Authenticated,
    string? UserId,
    DateTimeOffset? TokenExpiresAt,
    bool ExpiringSoon,
    bool HasAnthropic,
    bool HasDeepgram,
    bool HasGemini);

/// <summary>Thrown when a /troly/* call fails; carries the runtime's error code + HTTP status.</summary>
public sealed class TrolyAuthException : Exception
{
    public TrolyAuthException(string code, int httpStatus, string? message = null)
        : base(message ?? code)
    {
        Code = code;
        HttpStatus = httpStatus;
    }

    public string Code { get; }
    public int HttpStatus { get; }
}

/// <summary>
/// Drives the runtime's loopback /troly/* auth + key-sync surface from the shell.
/// The runtime is the single HTTPS egress to the Troly backend (ADR-0002); this
/// client only talks to 127.0.0.1 with the loopback bearer token.
/// </summary>
public sealed class TrolyAuthClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly RuntimeConnection _conn;

    public TrolyAuthClient(RuntimeConnection conn, HttpClient? http = null)
    {
        _conn = conn;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _ownsHttp = http is null;
    }

    public Task<TrolyTokenResult> LoginAsync(string email, string password, CancellationToken ct = default)
        => TokenCallAsync(HttpMethod.Post, "/troly/login", new { email, password }, ct);

    public Task<TrolyTokenResult> ExchangeWebTokenAsync(string webToken, CancellationToken ct = default)
        => TokenCallAsync(HttpMethod.Post, "/troly/exchange-web-token", new { web_token = webToken }, ct);

    public Task<TrolyTokenResult> RefreshAppTokenAsync(CancellationToken ct = default)
        => TokenCallAsync(HttpMethod.Post, "/troly/app-token", new { }, ct);

    /// <summary>Cold-start: push a previously stored app token into the runtime's in-memory session.</summary>
    public async Task PushSessionAsync(string token, DateTimeOffset? expiresAt, string? userId, CancellationToken ct = default)
    {
        var body = new { token, expiresAt = expiresAt?.ToUnixTimeMilliseconds(), userId };
        using var _ = await SendAsync(HttpMethod.Post, "/troly/session", body, ct).ConfigureAwait(false);
    }

    public async Task<TrolyKeysResult> FetchKeysAsync(CancellationToken ct = default)
    {
        using var doc = await SendAsync(HttpMethod.Post, "/troly/keys", new { }, ct).ConfigureAwait(false);
        var r = doc.RootElement;
        return new TrolyKeysResult(
            r.TryGetProperty("hasAnthropic", out var a) && a.ValueKind == JsonValueKind.True,
            r.TryGetProperty("hasDeepgram", out var d) && d.ValueKind == JsonValueKind.True,
            r.TryGetProperty("hasGemini", out var g) && g.ValueKind == JsonValueKind.True);
    }

    public async Task<TrolyRuntimeStatus> GetStatusAsync(CancellationToken ct = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, "/runtime/status", null, ct).ConfigureAwait(false);
        var root = doc.RootElement;
        var troly = root.TryGetProperty("troly", out var tEl) && tEl.ValueKind == JsonValueKind.Object ? tEl : default;
        var keys = troly.ValueKind == JsonValueKind.Object && troly.TryGetProperty("hasKeys", out var kEl) && kEl.ValueKind == JsonValueKind.Object ? kEl : default;
        return new TrolyRuntimeStatus(
            GetString(root, "serverId"),
            GetString(root, "version"),
            GetBool(troly, "configured"),
            GetBool(troly, "authenticated"),
            GetString(troly, "userId"),
            GetEpochMs(troly, "tokenExpiresAt"),
            GetBool(troly, "expiringSoon"),
            GetBool(keys, "anthropic"),
            GetBool(keys, "deepgram"),
            GetBool(keys, "gemini"));
    }

    private async Task<TrolyTokenResult> TokenCallAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var doc = await SendAsync(method, path, body, ct).ConfigureAwait(false);
        var r = doc.RootElement;
        var token = GetString(r, "token") ?? "";
        return new TrolyTokenResult(token, GetEpochMs(r, "expiresAt"), GetString(r, "userId"));
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, $"{_conn.BaseUrl}{path}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _conn.Token);
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new TrolyAuthException("runtime_unreachable", 0, e.Message);
        }

        using (res)
        {
            JsonDocument doc;
            try
            {
                await using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                if (!res.IsSuccessStatusCode)
                    throw new TrolyAuthException("invalid_response", (int)res.StatusCode);
                throw new TrolyAuthException("invalid_response", 200);
            }

            if (!res.IsSuccessStatusCode)
            {
                var code = GetString(doc.RootElement, "error") ?? "http_error";
                doc.Dispose();
                throw new TrolyAuthException(code, (int)res.StatusCode);
            }
            return doc;
        }
    }

    private static string? GetString(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool GetBool(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? GetEpochMs(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
