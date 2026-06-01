using System.Net;

namespace Troly.WinAgent.Core.Tests;

public class RuntimeSupervisorTests
{
    private static RuntimeClient ValidatingClient(string serverId, string token) =>
        new(new HttpClient(new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/health")
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, $"{{\"serverId\":\"{serverId}\"}}");
            var auth = req.Headers.Authorization;
            return auth?.Parameter == token
                ? StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"ok\":true}")
                : StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":\"unauthorized\"}");
        })));

    [Fact]
    public async Task EnsureRunning_launches_runtime_then_resolves_validated_connection()
    {
        var dir = TestState.NewTempDir();
        var launcher = new FakeRuntimeProcessLauncher(_ => TestState.WriteRuntimeFiles(dir, 52000, "srv-x", "tok-x"));
        var options = new RuntimeSupervisorOptions
        {
            StateDir = dir,
            Executable = "node",
            Args = new[] { "runtime-host.mjs" },
            UseJobObject = false,
            StartupTimeout = TimeSpan.FromSeconds(5),
            PollInterval = TimeSpan.FromMilliseconds(50)
        };
        using var supervisor = new RuntimeSupervisor(
            options, launcher, new RuntimeStateReader(dir), ValidatingClient("srv-x", "tok-x"));

        var conn = await supervisor.EnsureRunningAsync();

        Assert.Equal(52000, conn.Port);
        Assert.Equal("srv-x", conn.ServerId);
        Assert.Equal(1, launcher.StartCount);
        Assert.True(supervisor.OwnsProcess);
        Assert.True(launcher.LastSpec!.Headless);
        Assert.Equal(dir, launcher.LastSpec!.StateDir);
    }

    [Fact]
    public async Task EnsureRunning_reuses_already_running_runtime_without_launching()
    {
        var dir = TestState.NewTempDir();
        TestState.WriteRuntimeFiles(dir, 53000, "srv-y", "tok-y");
        var launcher = new FakeRuntimeProcessLauncher(_ => throw new InvalidOperationException("should not launch"));
        var options = new RuntimeSupervisorOptions { StateDir = dir, Executable = "node", UseJobObject = false };
        using var supervisor = new RuntimeSupervisor(
            options, launcher, new RuntimeStateReader(dir), ValidatingClient("srv-y", "tok-y"));

        var conn = await supervisor.EnsureRunningAsync();

        Assert.Equal(53000, conn.Port);
        Assert.Equal(0, launcher.StartCount);
        Assert.False(supervisor.OwnsProcess);
    }

    [Fact]
    public async Task EnsureRunning_throws_when_runtime_never_becomes_healthy()
    {
        var dir = TestState.NewTempDir();
        var launcher = new FakeRuntimeProcessLauncher(_ => { /* never writes discovery files */ });
        var options = new RuntimeSupervisorOptions
        {
            StateDir = dir,
            Executable = "node",
            UseJobObject = false,
            StartupTimeout = TimeSpan.FromMilliseconds(400),
            PollInterval = TimeSpan.FromMilliseconds(50)
        };
        using var supervisor = new RuntimeSupervisor(
            options, launcher, new RuntimeStateReader(dir), ValidatingClient("srv", "tok"));

        await Assert.ThrowsAsync<RuntimeStartException>(() => supervisor.EnsureRunningAsync());
    }
}
