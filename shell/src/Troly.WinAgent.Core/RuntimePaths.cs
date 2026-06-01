namespace Troly.WinAgent.Core;

/// <summary>
/// Resolves the runtime state directory and the discovery files (state.json,
/// token.txt) written by the Node runtime. Mirrors winagent/state.mjs so the
/// shell and the runtime agree on where to find the loopback port + token.
/// </summary>
public static class RuntimePaths
{
    /// <summary>Env var the runtime honors to override its state directory.</summary>
    public const string StateDirEnvVar = "AGENTIFY_DESKTOP_STATE_DIR";

    public static string DefaultStateDir()
    {
        var fromEnv = Environment.GetEnvironmentVariable(StateDirEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".agentify-desktop");
    }

    public static string StatePath(string stateDir) => Path.Combine(stateDir, "state.json");

    public static string TokenPath(string stateDir) => Path.Combine(stateDir, "token.txt");
}
