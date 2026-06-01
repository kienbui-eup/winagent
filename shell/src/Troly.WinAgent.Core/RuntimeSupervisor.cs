namespace Troly.WinAgent.Core;

public sealed class RuntimeSupervisorOptions
{
    /// <summary>State directory shared with the runtime (state.json/token.txt live here).</summary>
    public string StateDir { get; init; } = RuntimePaths.DefaultStateDir();

    /// <summary>Executable to launch (e.g. bundled node.exe, or "node"/"electron").</summary>
    public required string Executable { get; init; }

    /// <summary>Args to the executable (e.g. ["runtime-host.mjs"] or ["main.mjs"]).</summary>
    public IReadOnlyList<string> Args { get; init; } = Array.Empty<string>();

    /// <summary>Agent version, surfaced to the runtime as TROLY_APP_VERSION (X-App-Version).</summary>
    public string? AppVersion { get; init; }

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Reap the runtime (and its Chrome) when the shell exits, via a Win32 Job Object.</summary>
    public bool UseJobObject { get; init; } = true;
}

/// <summary>
/// Boots and supervises the headless Node runtime, then exposes a validated
/// loopback <see cref="RuntimeConnection"/>. Mirrors mcp-lib.ensureDesktopRunning:
/// reuse an already-running, validated runtime if present; otherwise launch it
/// headless and poll state.json + token.txt + /health + /status until ready.
/// On Windows the child is bound to a KILL_ON_JOB_CLOSE job so it cannot
/// outlive the shell.
/// </summary>
public sealed class RuntimeSupervisor : IDisposable
{
    private readonly RuntimeSupervisorOptions _options;
    private readonly RuntimeStateReader _reader;
    private readonly RuntimeClient _client;
    private readonly IRuntimeProcessLauncher _launcher;

    private IRuntimeProcessHandle? _process;
    private Win32JobObject? _job;
    private bool _disposed;

    public RuntimeSupervisor(
        RuntimeSupervisorOptions options,
        IRuntimeProcessLauncher? launcher = null,
        RuntimeStateReader? reader = null,
        RuntimeClient? client = null)
    {
        _options = options;
        _reader = reader ?? new RuntimeStateReader(options.StateDir);
        _client = client ?? new RuntimeClient();
        _launcher = launcher ?? new ProcessRuntimeProcessLauncher();
    }

    /// <summary>The last validated connection, available after EnsureRunningAsync succeeds.</summary>
    public RuntimeConnection? Connection { get; private set; }

    /// <summary>True if this supervisor started the runtime process (vs. reusing a running one).</summary>
    public bool OwnsProcess => _process is not null;

    public async Task<RuntimeConnection> EnsureRunningAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 1) Reuse an already-running, validated runtime (e.g. started by the MCP server).
        var existing = await _reader.TryLoadConnectionAsync(ct).ConfigureAwait(false);
        if (existing is not null && (await _client.ValidateAsync(existing, ct).ConfigureAwait(false)).Ok)
            return Connection = existing;

        // 2) Launch headless and bind to a job object so it dies with the shell.
        LaunchProcess();

        // 3) Poll for the discovery files + a passing handshake.
        var deadline = DateTime.UtcNow + _options.StartupTimeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (_process is { HasExited: true })
                throw new RuntimeStartException("The runtime process exited during startup.");

            var conn = await _reader.TryLoadConnectionAsync(ct).ConfigureAwait(false);
            if (conn is not null && (await _client.ValidateAsync(conn, ct).ConfigureAwait(false)).Ok)
                return Connection = conn;

            await Task.Delay(_options.PollInterval, ct).ConfigureAwait(false);
        }

        throw new RuntimeStartException(
            $"Runtime did not become healthy within {_options.StartupTimeout.TotalSeconds:n0}s.");
    }

    private void LaunchProcess()
    {
        var extraEnv = _options.AppVersion is { Length: > 0 } v
            ? new Dictionary<string, string?> { ["TROLY_APP_VERSION"] = v }
            : null;

        _process = _launcher.Start(new RuntimeLaunchSpec(
            _options.Executable, _options.Args, _options.StateDir, Headless: true, ExtraEnv: extraEnv));

        if (_options.UseJobObject && OperatingSystem.IsWindows())
            AssignToJobObject(_process.ProcessHandle);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void AssignToJobObject(IntPtr processHandle)
    {
        try
        {
            _job = new Win32JobObject();
            _job.AssignProcess(processHandle);
        }
        catch
        {
            // If the job object can't be set up we still run; teardown falls back to Kill().
            _job?.Dispose();
            _job = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Closing the job handle kills the runtime (and its Chrome) on Windows.
        if (_job is not null)
        {
            if (OperatingSystem.IsWindows()) _job.Dispose();
        }
        else
        {
            _process?.Kill();
        }
        _process?.Dispose();
        _client.Dispose();
    }
}

public sealed class RuntimeStartException(string message) : Exception(message);
