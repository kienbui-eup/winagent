using System.Net.Http.Headers;
using System.Text.Json;

namespace Troly.WinAgent.Core;

/// <summary>Result of the runtime health/auth handshake.</summary>
public sealed record RuntimeValidation(bool Ok, string? Reason = null, string? ServerId = null);

/// <summary>
/// Typed client for the runtime's loopback HTTP API. Ports the two-step
/// handshake from mcp-lib.validateConn: (1) GET /health (liveness + serverId
/// match), (2) authenticated GET /status (token match + ok:true payload).
/// </summary>
public sealed class RuntimeClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public RuntimeClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _ownsHttp = http is null;
    }

    public async Task<RuntimeValidation> ValidateAsync(RuntimeConnection conn, CancellationToken ct = default)
    {
        // 1) Health: something is listening, and serverId matches when both sides know it.
        string? healthServerId;
        try
        {
            using var healthRes = await _http.GetAsync($"{conn.BaseUrl}/health", ct).ConfigureAwait(false);
            if (!healthRes.IsSuccessStatusCode) return new RuntimeValidation(false, "health_not_ok");
            using var healthDoc = await ReadJsonAsync(healthRes, ct).ConfigureAwait(false);
            healthServerId = TryGetString(healthDoc, "serverId");
        }
        catch (HttpRequestException) { return new RuntimeValidation(false, "health_unreachable"); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new RuntimeValidation(false, "health_timeout"); }

        if (conn.ServerId is not null && healthServerId is not null && conn.ServerId != healthServerId)
            return new RuntimeValidation(false, "server_id_mismatch");

        // 2) Authenticated status: token matches, payload is ours.
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{conn.BaseUrl}/status");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", conn.Token);
            using var statusRes = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!statusRes.IsSuccessStatusCode) return new RuntimeValidation(false, "status_not_ok");
            using var statusDoc = await ReadJsonAsync(statusRes, ct).ConfigureAwait(false);
            if (statusDoc is null || statusDoc.RootElement.ValueKind != JsonValueKind.Object)
                return new RuntimeValidation(false, "unexpected_status_payload");
            if (TryGetString(statusDoc, "error") == "unauthorized")
                return new RuntimeValidation(false, "unauthorized");
            if (!(statusDoc.RootElement.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True))
                return new RuntimeValidation(false, "unexpected_status_payload");
        }
        catch (HttpRequestException) { return new RuntimeValidation(false, "status_unreachable"); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new RuntimeValidation(false, "status_timeout"); }

        return new RuntimeValidation(true, ServerId: healthServerId);
    }

    private static async Task<JsonDocument?> ReadJsonAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            await using var s = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(s, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (JsonException) { return null; }
    }

    private static string? TryGetString(JsonDocument? doc, string prop)
        => doc is not null
           && doc.RootElement.ValueKind == JsonValueKind.Object
           && doc.RootElement.TryGetProperty(prop, out var el)
           && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
