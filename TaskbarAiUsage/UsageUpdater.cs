using System.IO;
using System.Text.Json;

namespace TaskbarAiUsage;

public sealed record UpdateState(UsageValues Values, DateTimeOffset LastAttemptAt, DateTimeOffset? LastSuccessAt, string? FailureSummary);

public sealed class UsageUpdater(IUsageSource process)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private UpdateState state = new(UsageValues.Empty("Not updated yet.", "Not updated yet."), DateTimeOffset.MinValue, null, "Not updated yet.");

    public UpdateState State => state;
    public bool IsUpdating => gate.CurrentCount == 0;
    public event EventHandler<UpdateState>? StateChanged;

    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await gate.WaitAsync(0, cancellationToken))
            return false;
        var attempted = DateTimeOffset.Now;
        try
        {
            try
            {
                var output = await process.ReadJsonAsync(cancellationToken);
                var parsed = UsageParser.Parse(output);
                var errors = JoinErrors(parsed.CodexError, parsed.GrokError);
                state = new(parsed, attempted, errors is null ? attempted : state.LastSuccessAt, errors);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var summary = SafeSummary(ex);
                SafeLog.Write("update-failed", summary);
                state = new(UsageValues.Empty(summary, summary), attempted, state.LastSuccessAt, summary);
            }
            if (state.FailureSummary is null)
                SafeLog.Write("update-succeeded", "Usage data updated successfully.");
            else
            {
                var hasAnyValue = state.Values.Codex5Hour is not null || state.Values.CodexWeekly is not null || state.Values.GrokWeekly is not null;
                SafeLog.Write(hasAnyValue ? "update-partial" : "update-failed", state.FailureSummary);
            }
            StateChanged?.Invoke(this, state);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string? JoinErrors(string? codex, string? grok) =>
        codex is null && grok is null ? null : string.Join(" ", new[] { codex, grok }.Where(value => value is not null));

    private static string SafeSummary(Exception exception) => exception switch
    {
        FileNotFoundException => "Tokscale executable was not found. Configure its path in appsettings.json.",
        TimeoutException => exception.Message,
        InvalidOperationException => exception.Message,
        JsonException => "Tokscale returned invalid JSON.",
        FormatException => exception.Message,
        _ => $"Update failed ({exception.GetType().Name})."
    };
}

public sealed record AppSettings(int UpdateIntervalSeconds, int CliTimeoutSeconds, string? TokscalePath)
{
    public static AppSettings Load(string filePath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(filePath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("appsettings.json must contain a JSON object.");
            var allowed = new HashSet<string>(["updateIntervalSeconds", "cliTimeoutSeconds", "tokscalePath"], StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!allowed.Contains(property.Name))
                    throw new InvalidDataException($"Unknown appsettings.json setting: '{property.Name}'.");
            var interval = ReadInt(root, "updateIntervalSeconds", 300);
            var timeout = ReadInt(root, "cliTimeoutSeconds", 60);
            string? path = null;
            if (root.TryGetProperty("tokscalePath", out var pathValue) && pathValue.ValueKind != JsonValueKind.Null)
            {
                if (pathValue.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("Setting 'tokscalePath' must be a string or null.");
                path = pathValue.GetString();
            }
            if (interval < 30 || interval > 86400)
                throw new InvalidDataException("Setting 'updateIntervalSeconds' must be from 30 to 86400.");
            if (timeout < 1 || timeout > 600)
                throw new InvalidDataException("Setting 'cliTimeoutSeconds' must be from 1 to 600.");
            return new(interval, timeout, string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("appsettings.json is invalid. Check its JSON syntax and setting names.", ex);
        }
    }

    private static int ReadInt(JsonElement root, string name, int defaultValue)
    {
        if (!root.TryGetProperty(name, out var value))
            return defaultValue;
        if (!value.TryGetInt32(out var result))
            throw new InvalidDataException($"Setting '{name}' must be an integer.");
        return result;
    }
}

public static class TokscalePathResolver
{
    public static string Resolve(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return configuredPath;

        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var nativePath = Path.Combine(roaming, "npm", "node_modules", "@tokscale", "cli", "node_modules", "@tokscale", "cli-win32-x64-msvc", "bin", "tokscale.exe");
        if (File.Exists(nativePath))
            return nativePath;

        var pathMatch = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim('"'), "tokscale.exe"))
            .FirstOrDefault(File.Exists);
        return pathMatch ?? nativePath;
    }
}

internal static class SafeLog
{
    public static void Write(string eventName, string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarAiUsage");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "app.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                File.WriteAllText(path, string.Empty);
            var safeMessage = message.Replace('\r', ' ').Replace('\n', ' ');
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {eventName}: {safeMessage}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}

public sealed class WindowLifetimePolicy
{
    private bool shutdownAllowed;
    public bool ShouldMinimizeOnClose => !shutdownAllowed;
    public void AllowShutdown() => shutdownAllowed = true;
}
