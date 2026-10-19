using System.Diagnostics;
using System.IO;

namespace TaskbarAiUsage;

public interface IUsageSource
{
    Task<string> ReadJsonAsync(CancellationToken cancellationToken);
}

public sealed class UsageProcess(string executablePath, TimeSpan timeout, IReadOnlyList<string>? arguments = null) : IUsageSource
{
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
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            if (cancellationToken.IsCancellationRequested)
                throw;
            throw new TimeoutException($"Tokscale did not finish within {timeout.TotalSeconds:0} seconds.");
        }

        var output = await stdout.ConfigureAwait(false);
        _ = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Tokscale exited with code {process.ExitCode}.");
        return output;
    }
}
