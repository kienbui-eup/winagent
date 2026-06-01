using System.Text.Json;

namespace Troly.WinAgent.Core;

/// <summary>
/// Reads the runtime discovery files (state.json + token.txt). Returns null
/// until both exist and are valid, mirroring mcp-lib.loadConnection so the
/// shell uses the exact same handshake the MCP server already relies on.
/// </summary>
public sealed class RuntimeStateReader
{
    public RuntimeStateReader(string? stateDir = null)
        => StateDir = stateDir ?? RuntimePaths.DefaultStateDir();

    public string StateDir { get; }

    public async Task<RuntimeConnection?> TryLoadConnectionAsync(CancellationToken ct = default)
    {
        var statePath = RuntimePaths.StatePath(StateDir);
        var tokenPath = RuntimePaths.TokenPath(StateDir);
        if (!File.Exists(statePath) || !File.Exists(tokenPath)) return null;

        int port;
        string? serverId;
        try
        {
            await using var stream = File.OpenRead(statePath);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("port", out var portEl) || !portEl.TryGetInt32(out port) || port <= 0)
                return null;
            serverId = root.TryGetProperty("serverId", out var sidEl) && sidEl.ValueKind == JsonValueKind.String
                ? sidEl.GetString()
                : null;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }

        string token;
        try
        {
            token = (await File.ReadAllTextAsync(tokenPath, ct).ConfigureAwait(false)).Trim();
        }
        catch (IOException) { return null; }

        return string.IsNullOrEmpty(token) ? null : new RuntimeConnection(port, token, serverId);
    }
}
