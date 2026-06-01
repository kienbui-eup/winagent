namespace Troly.WinAgent.Core.Tests;

/// <summary>An HttpMessageHandler that answers requests by URL path, for testing RuntimeClient without sockets.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_responder(request));

    public static HttpResponseMessage Json(System.Net.HttpStatusCode code, string json)
        => new(code) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}

/// <summary>A fake launcher that simulates the runtime coming up by writing state.json + token.txt.</summary>
internal sealed class FakeRuntimeProcessLauncher : IRuntimeProcessLauncher
{
    private readonly Action<RuntimeLaunchSpec> _onStart;
    public int StartCount { get; private set; }
    public RuntimeLaunchSpec? LastSpec { get; private set; }

    public FakeRuntimeProcessLauncher(Action<RuntimeLaunchSpec> onStart) => _onStart = onStart;

    public IRuntimeProcessHandle Start(RuntimeLaunchSpec spec)
    {
        StartCount++;
        LastSpec = spec;
        _onStart(spec);
        return new FakeHandle();
    }

    private sealed class FakeHandle : IRuntimeProcessHandle
    {
        public bool HasExited => false;
        public IntPtr ProcessHandle => IntPtr.Zero;
        public void Kill() { }
        public void Dispose() { }
    }
}

internal static class TestState
{
    public static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "trolywin-shell-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void WriteRuntimeFiles(string dir, int port, string serverId, string token)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(RuntimePaths.StatePath(dir), $"{{\"port\":{port},\"serverId\":\"{serverId}\"}}");
        File.WriteAllText(RuntimePaths.TokenPath(dir), token + "\n");
    }
}
