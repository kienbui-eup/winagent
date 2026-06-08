using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace Troly.WinAgent.Core;

public sealed record StoredToken(string Token, DateTimeOffset? ExpiresAt, string? UserId);

/// <summary>
/// Persists the Troly app token at rest using Windows DPAPI (CurrentUser scope),
/// so it survives restarts and can be re-pushed to the runtime on cold start.
/// Stored at %LOCALAPPDATA%\Troly\WinAgent\auth.bin. Windows-only (DPAPI).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TokenStore
{
    public TokenStore(string? baseDir = null)
    {
        var dir = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Troly", "WinAgent");
        FilePath = Path.Combine(dir, "auth.bin");
    }

    public string FilePath { get; }

    public void Save(StoredToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(token);
        var encrypted = ProtectedData.Protect(plain, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, encrypted);
    }

    public StoredToken? TryLoad()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var encrypted = File.ReadAllBytes(FilePath);
            var plain = ProtectedData.Unprotect(encrypted, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<StoredToken>(plain);
        }
        catch (CryptographicException) { return null; }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch (IOException) { /* best effort */ }
    }
}
