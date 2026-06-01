using System.Diagnostics;

namespace Troly.WinAgent.Core;

/// <summary>How to start the headless runtime process.</summary>
public sealed record RuntimeLaunchSpec(
    string Executable,
    IReadOnlyList<string> Args,
    string StateDir,
    bool Headless = true,
    IReadOnlyDictionary<string, string?>? ExtraEnv = null);

/// <summary>A handle to a launched runtime process, used for job-object assignment and teardown.</summary>
public interface IRuntimeProcessHandle : IDisposable
{
    bool HasExited { get; }

    /// <summary>The OS process handle, or IntPtr.Zero when not applicable (e.g. fakes).</summary>
    IntPtr ProcessHandle { get; }

    void Kill();
}

/// <summary>Starts the headless runtime. Abstracted so the supervisor is testable without spawning Node.</summary>
public interface IRuntimeProcessLauncher
{
    IRuntimeProcessHandle Start(RuntimeLaunchSpec spec);
}

/// <summary>Default launcher backed by System.Diagnostics.Process (spawns node/electron headless).</summary>
public sealed class ProcessRuntimeProcessLauncher : IRuntimeProcessLauncher
{
    public IRuntimeProcessHandle Start(RuntimeLaunchSpec spec)
    {
        var psi = new ProcessStartInfo
        {
            FileName = spec.Executable,
            // Not redirecting stdout/stderr keeps the child from blocking on a full pipe;
            // the App layer can opt into redirect+drain when it wants log capture.
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = spec.StateDir
        };
        foreach (var arg in spec.Args) psi.ArgumentList.Add(arg);

        psi.Environment[RuntimePaths.StateDirEnvVar] = spec.StateDir;
        if (spec.Headless) psi.Environment["TROLY_HEADLESS"] = "1";
        if (spec.ExtraEnv is not null)
            foreach (var (k, v) in spec.ExtraEnv) psi.Environment[k] = v;

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start runtime process");
        return new DiagnosticsProcessHandle(process);
    }

    private sealed class DiagnosticsProcessHandle(Process process) : IRuntimeProcessHandle
    {
        public bool HasExited
        {
            get { try { return process.HasExited; } catch { return true; } }
        }

        public IntPtr ProcessHandle
        {
            get { try { return process.Handle; } catch { return IntPtr.Zero; } }
        }

        public void Kill()
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* best effort */ }
        }

        public void Dispose() => process.Dispose();
    }
}
