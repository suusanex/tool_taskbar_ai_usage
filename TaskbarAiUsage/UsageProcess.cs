using System.Diagnostics;
using System.IO;

namespace TaskbarAiUsage;

public interface IUsageSource
{
    Task<string> ReadJsonAsync(CancellationToken cancellationToken);
    void StopForShutdown(TimeSpan waitLimit);
}

public sealed class UsageProcess(string executablePath, TimeSpan timeout, IReadOnlyList<string>? arguments = null) : IUsageSource
{
    private readonly object stateLock = new();
    private Process? activeProcess;
    private CancellationTokenSource? activeCancellation;
    private bool shutdownRequested;

    public async Task<string> ReadJsonAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Tokscale executable was not found. Set its path in appsettings.json.", executablePath);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var argument in arguments ?? ["usage", "--json"])
            process.StartInfo.ArgumentList.Add(argument);

        using var deadline = new CancellationTokenSource(timeout);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        Task<string>? stdout = null;
        Task<string>? stderr = null;
        try
        {
            lock (stateLock)
            {
                if (activeProcess is not null)
                    throw new InvalidOperationException("Tokscale is already running.");
                shutdownRequested = false;
                process.Start();
                activeProcess = process;
                activeCancellation = operation;
            }

            stdout = process.StandardOutput.ReadToEndAsync(operation.Token);
            stderr = process.StandardError.ReadToEndAsync(operation.Token);
            var exit = process.WaitForExitAsync(operation.Token);
            await Task.WhenAll(stdout, stderr, exit).WaitAsync(operation.Token).ConfigureAwait(false);

            var output = await stdout.ConfigureAwait(false);
            _ = await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Tokscale exited with code {process.ExitCode}.");
            return output;
        }
        catch (OperationCanceledException)
        {
            var stoppedForShutdown = IsShutdownRequested(process);
            await TerminateAndWaitAsync(process, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested || stoppedForShutdown)
                throw new OperationCanceledException(cancellationToken);
            throw new TimeoutException($"Tokscale did not finish within {timeout.TotalSeconds:0} seconds.");
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested || IsShutdownRequested(process))
        {
            await TerminateAndWaitAsync(process, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            throw new OperationCanceledException(cancellationToken);
        }
        finally
        {
            lock (stateLock)
            {
                if (ReferenceEquals(activeProcess, process))
                {
                    activeProcess = null;
                    activeCancellation = null;
                    shutdownRequested = false;
                }
            }
        }
    }

    public void StopForShutdown(TimeSpan waitLimit)
    {
        Process? process;
        CancellationTokenSource? cancellation;
        lock (stateLock)
        {
            shutdownRequested = true;
            process = activeProcess;
            cancellation = activeCancellation;
        }

        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        if (process is not null)
            TerminateAndWait(process, waitLimit);
    }

    private bool IsShutdownRequested(Process process)
    {
        lock (stateLock)
            return ReferenceEquals(activeProcess, process) && shutdownRequested;
    }

    private static async Task TerminateAndWaitAsync(Process process, TimeSpan waitLimit)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }

        try
        {
            await process.WaitForExitAsync().WaitAsync(waitLimit).ConfigureAwait(false);
        }
        catch (InvalidOperationException) { }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { SafeLog.Write("cli-cleanup-timeout", "Tokscale process did not exit within the cleanup wait limit."); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static void TerminateAndWait(Process process, TimeSpan waitLimit)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }

        try
        {
            var milliseconds = (int)Math.Clamp(waitLimit.TotalMilliseconds, 0, int.MaxValue);
            process.WaitForExit(milliseconds);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
