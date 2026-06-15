using Microsoft.UI.Xaml;
using Troly.WinAgent.Core;

namespace Troly.WinAgent.App;

public sealed partial class MainWindow : Window
{
    private readonly TokenStore _tokenStore = new();
    private RuntimeSupervisor? _supervisor;
    private TrolyAuthClient? _auth;

    public MainWindow()
    {
        InitializeComponent();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        SetStatus("Starting runtime…");
        try
        {
            _supervisor = new RuntimeSupervisor(ResolveRuntimeOptions());
            var conn = await _supervisor.EnsureRunningAsync();
            _auth = new TrolyAuthClient(conn);
        }
        catch (Exception ex)
        {
            SetStatus($"Runtime failed to start: {ex.Message}");
            return;
        }

        // Cold start: re-hydrate a previously stored token into the runtime session.
        if (OperatingSystem.IsWindows())
        {
            var saved = _tokenStore.TryLoad();
            if (saved is not null)
            {
                try
                {
                    await _auth.PushSessionAsync(saved.Token, saved.ExpiresAt, saved.UserId);
                    await TryFetchKeysAsync();
                }
                catch { /* stale token; user can sign in again */ }
            }
        }

        await RefreshStatusAsync();
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        if (_auth is null) return;
        SignInButton.IsEnabled = false;
        SetMessage("Signing in…");
        try
        {
            var token = await _auth.LoginAsync(EmailBox.Text.Trim(), PasswordBox.Password);
            if (OperatingSystem.IsWindows())
                _tokenStore.Save(new StoredToken(token.Token, token.ExpiresAt, token.UserId));
            await TryFetchKeysAsync();
            await RefreshStatusAsync();
            SetMessage("Signed in.");
            PasswordBox.Password = string.Empty;
        }
        catch (TrolyAuthException ex)
        {
            SetMessage($"Login failed: {ex.Code} (HTTP {ex.HttpStatus}).");
        }
        catch (Exception ex)
        {
            SetMessage($"Error: {ex.Message}");
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    private async Task TryFetchKeysAsync()
    {
        if (_auth is null) return;
        try { await _auth.FetchKeysAsync(); }
        catch (TrolyAuthException) { /* keys optional at this stage */ }
    }

    private async Task RefreshStatusAsync()
    {
        if (_auth is null) return;
        try
        {
            var s = await _auth.GetStatusAsync();
            var who = s.Authenticated ? $"signed in{(s.UserId is null ? "" : $" as {s.UserId}")}" : "signed out";
            var keys = $"A:{(s.HasAnthropic ? "✓" : "–")} D:{(s.HasDeepgram ? "✓" : "–")} G:{(s.HasGemini ? "✓" : "–")}";
            SetStatus($"Runtime healthy (sid {s.ServerId}, v{s.Version}). Troly: {who}. Keys {keys}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Runtime status unavailable: {ex.Message}");
        }
    }

    private void SetStatus(string text) => StatusText.Text = text;
    private void SetMessage(string text) => MessageText.Text = text;

    private static RuntimeSupervisorOptions ResolveRuntimeOptions()
    {
        var baseDir = AppContext.BaseDirectory;
        var exe = Environment.GetEnvironmentVariable("TROLY_RUNTIME_EXE");
        string executable;
        IReadOnlyList<string> args;
        if (!string.IsNullOrWhiteSpace(exe))
        {
            executable = exe;
            var argEnv = Environment.GetEnvironmentVariable("TROLY_RUNTIME_ARGS") ?? string.Empty;
            args = argEnv.Length > 0 ? argEnv.Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
        }
        else
        {
            // Bundled layout (Phase G): \runtime\node.exe + \sidecar\runtime-host.mjs
            // (main.mjs until the pure-Node host lands in Phase F).
            executable = Path.Combine(baseDir, "runtime", "node.exe");
            args = new[] { Path.Combine(baseDir, "sidecar", "main.mjs") };
        }

        return new RuntimeSupervisorOptions
        {
            Executable = executable,
            Args = args,
            AppVersion = "0.1.2"
        };
    }
}
