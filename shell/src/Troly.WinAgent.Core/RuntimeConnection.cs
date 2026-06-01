namespace Troly.WinAgent.Core;

/// <summary>
/// A resolved loopback connection to the Node runtime, discovered from
/// state.json + token.txt. Mirrors mcp-lib.loadConnection's return shape.
/// </summary>
public sealed record RuntimeConnection(int Port, string Token, string? ServerId)
{
    public string BaseUrl => $"http://127.0.0.1:{Port}";
}
