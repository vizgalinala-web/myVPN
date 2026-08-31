using System.Diagnostics;

namespace WgPeerSync;

public interface IWgCommandExecutor
{
    Task ExecuteAsync(WgCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs <c>wg</c> with an explicit argument list (no shell). Requires operator opt-in via env.
/// </summary>
public sealed class ProcessWgCommandExecutor : IWgCommandExecutor
{
    private readonly string _wgBinary;

    public ProcessWgCommandExecutor(string? wgBinary = null)
    {
        _wgBinary = string.IsNullOrWhiteSpace(wgBinary)
            ? (Environment.GetEnvironmentVariable("MYVPN_WG_BIN") ?? "wg")
            : wgBinary;
    }

    public async Task ExecuteAsync(WgCommand command, CancellationToken cancellationToken = default)
    {
        if (!PeerSyncPlanner.IsSafeInterfaceName(command.Interface))
        {
            throw new InvalidOperationException($"Refusing unsafe interface: {command.Interface}");
        }

        if (!PeerSyncPlanner.IsValidPublicKey(command.PublicKey))
        {
            throw new InvalidOperationException($"Refusing invalid public key: {command.PublicKey}");
        }

        var psi = new ProcessStartInfo
        {
            FileName = _wgBinary,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in command.ToArgumentList())
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {_wgBinary}");
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{_wgBinary} exited {process.ExitCode}: {stderr.Trim()} {stdout.Trim()}".Trim());
        }
    }
}

public sealed class PrintingWgCommandExecutor : IWgCommandExecutor
{
    private readonly TextWriter _output;

    public PrintingWgCommandExecutor(TextWriter? output = null)
        => _output = output ?? Console.Out;

    public Task ExecuteAsync(WgCommand command, CancellationToken cancellationToken = default)
    {
        _output.WriteLine(command.ToString());
        return Task.CompletedTask;
    }
}
