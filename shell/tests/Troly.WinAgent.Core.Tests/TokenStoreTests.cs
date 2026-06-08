namespace Troly.WinAgent.Core.Tests;

public class TokenStoreTests
{
    [Fact]
    public void Save_load_clear_roundtrips_via_dpapi()
    {
        // DPAPI is Windows-only; skip elsewhere (guard also satisfies the platform analyzer).
        if (!OperatingSystem.IsWindows()) return;

        var dir = TestState.NewTempDir();
        var store = new TokenStore(dir);

        Assert.Null(store.TryLoad());

        var token = new StoredToken("tok-abc", DateTimeOffset.FromUnixTimeSeconds(1893456000), "u1");
        store.Save(token);

        Assert.True(File.Exists(store.FilePath));
        var loaded = store.TryLoad();
        Assert.NotNull(loaded);
        Assert.Equal("tok-abc", loaded!.Token);
        Assert.Equal("u1", loaded.UserId);
        Assert.Equal(1893456000, loaded.ExpiresAt!.Value.ToUnixTimeSeconds());

        store.Clear();
        Assert.False(File.Exists(store.FilePath));
        Assert.Null(store.TryLoad());
    }

    [Fact]
    public void TryLoad_returns_null_on_corrupt_file()
    {
        if (!OperatingSystem.IsWindows()) return;

        var dir = TestState.NewTempDir();
        var store = new TokenStore(dir);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(store.FilePath, new byte[] { 1, 2, 3, 4, 5 }); // not DPAPI-protected

        Assert.Null(store.TryLoad());
    }
}
