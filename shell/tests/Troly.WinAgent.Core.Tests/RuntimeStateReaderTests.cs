namespace Troly.WinAgent.Core.Tests;

public class RuntimeStateReaderTests
{
    [Fact]
    public async Task TryLoadConnection_returns_null_when_files_missing()
    {
        var dir = TestState.NewTempDir();
        var reader = new RuntimeStateReader(dir);
        Assert.Null(await reader.TryLoadConnectionAsync());
    }

    [Fact]
    public async Task TryLoadConnection_reads_port_serverId_and_token()
    {
        var dir = TestState.NewTempDir();
        TestState.WriteRuntimeFiles(dir, port: 51515, serverId: "srv-abc", token: "tok-xyz");

        var conn = await new RuntimeStateReader(dir).TryLoadConnectionAsync();

        Assert.NotNull(conn);
        Assert.Equal(51515, conn!.Port);
        Assert.Equal("srv-abc", conn.ServerId);
        Assert.Equal("tok-xyz", conn.Token);
        Assert.Equal("http://127.0.0.1:51515", conn.BaseUrl);
    }

    [Fact]
    public async Task TryLoadConnection_returns_null_on_malformed_state_json()
    {
        var dir = TestState.NewTempDir();
        await File.WriteAllTextAsync(RuntimePaths.StatePath(dir), "{ not json");
        await File.WriteAllTextAsync(RuntimePaths.TokenPath(dir), "tok\n");

        Assert.Null(await new RuntimeStateReader(dir).TryLoadConnectionAsync());
    }

    [Fact]
    public async Task TryLoadConnection_returns_null_when_port_missing()
    {
        var dir = TestState.NewTempDir();
        await File.WriteAllTextAsync(RuntimePaths.StatePath(dir), "{ \"serverId\": \"s\" }");
        await File.WriteAllTextAsync(RuntimePaths.TokenPath(dir), "tok\n");

        Assert.Null(await new RuntimeStateReader(dir).TryLoadConnectionAsync());
    }

    [Fact]
    public async Task TryLoadConnection_returns_null_when_token_blank()
    {
        var dir = TestState.NewTempDir();
        await File.WriteAllTextAsync(RuntimePaths.StatePath(dir), "{ \"port\": 5000 }");
        await File.WriteAllTextAsync(RuntimePaths.TokenPath(dir), "   \n");

        Assert.Null(await new RuntimeStateReader(dir).TryLoadConnectionAsync());
    }
}
