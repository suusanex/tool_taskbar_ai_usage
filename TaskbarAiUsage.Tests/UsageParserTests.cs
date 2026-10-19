using System.IO;
using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using TaskbarAiUsage;

namespace TaskbarAiUsage.Tests;

public sealed class UsageParserTests
{
    private static string Fixture => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tokscale-sanitized.json"));

    [Fact]
    public void ParsesOnlyRequiredProvidersAndPeriods()
    {
        var parsed = UsageParser.Parse(Fixture);
        Assert.Equal(83, parsed.Codex5Hour?.RemainingPercent);
        Assert.Equal(23, parsed.CodexWeekly?.RemainingPercent);
        Assert.Equal(53, parsed.GrokWeekly?.RemainingPercent);
        Assert.Equal(TimeSpan.FromHours(9), parsed.Codex5Hour?.ResetsAt?.Offset);
        Assert.Null(parsed.CodexError);
        Assert.Null(parsed.GrokError);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(7.99, 7)]
    [InlineData(73.8, 73)]
    [InlineData(100.0, 100)]
    public void TruncatesPercentagesWithinRange(double input, int expected)
    {
        var parsed = UsageParser.Parse(BuildJson(input, 10, 20));
        Assert.Equal(expected, parsed.Codex5Hour?.RemainingPercent);
    }

    [Fact]
    public void GeneratedJsonUsesInvariantNumberFormattingUnderGermanCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var json = BuildJson(73.8, 45.5, 9.9);
            Assert.Contains("73.8", json);
            Assert.Equal(73, UsageParser.Parse(json).Codex5Hour?.RemainingPercent);
            Assert.Equal(45, UsageParser.Parse(json).CodexWeekly?.RemainingPercent);
            Assert.Equal(9, UsageParser.Parse(json).GrokWeekly?.RemainingPercent);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(100.1)]
    public void RejectsOutOfRangeMetricWithoutFabricatingValue(double input)
    {
        var parsed = UsageParser.Parse(BuildJson(input, 10, 20));
        Assert.Null(parsed.Codex5Hour);
        Assert.Contains("out-of-range", parsed.CodexError);
    }

    [Fact]
    public void MissingAndMalformedPeriodsAreIndependentlyUnavailable()
    {
        var parsed = UsageParser.Parse("""
            [{"provider":"Codex","metrics":[{"label":"5h","remaining_percent":"73"}]},
             {"provider":"Grok","metrics":[{"label":"Monthly","remaining_percent":4}]}]
            """);
        Assert.Null(parsed.Codex5Hour);
        Assert.Null(parsed.CodexWeekly);
        Assert.Null(parsed.GrokWeekly);
        Assert.NotNull(parsed.CodexError);
        Assert.NotNull(parsed.GrokError);
    }

    [Fact]
    public void SelectsOnlyUniquelyActiveCodexAccount()
    {
        var json = """
            [{"provider":"Codex","account":{"is_active":false},"metrics":[{"label":"5h","remaining_percent":10}]},
             {"provider":"Codex","account":{"is_active":true},"metrics":[{"label":"5h","remaining_percent":73},{"label":"Weekly","remaining_percent":33}]},
             {"provider":"Grok","metrics":[{"label":"Weekly","remaining_percent":40}]}]
            """;
        Assert.Equal(73, UsageParser.Parse(json).Codex5Hour?.RemainingPercent);
        Assert.Null(UsageParser.Parse(json.Replace("\"is_active\":false", "\"is_active\":true")).Codex5Hour);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[{\"provider\":12},{\"provider\":\"Grok\",\"metrics\":[]}]")]
    public void HandlesInvalidOrMissingProviderShapes(string json)
    {
        if (json == "{}" || json == "null")
            Assert.Throws<FormatException>(() => UsageParser.Parse(json));
        else
            Assert.Null(UsageParser.Parse(json).Codex5Hour);
    }

    private static string BuildJson(double fiveHour, double weekly, double grokWeekly) => FormattableString.Invariant($$"""
        [{"provider":"Codex","metrics":[{"label":"5h","remaining_percent":{{fiveHour}}},{"label":"Weekly","remaining_percent":{{weekly}}}]},
         {"provider":"Grok","metrics":[{"label":"Weekly","remaining_percent":{{grokWeekly}}}]}]
        """);
}

public sealed class UsageProcessTests
{
    [Fact]
    public async Task TimedOutOutputReadAfterParentExitReleasesUpdateGate()
    {
        var childPidFile = Path.Combine(Path.GetTempPath(), $"taskbar-child-{Guid.NewGuid():N}.pid");
        var fixture = Path.Combine(AppContext.BaseDirectory, "TaskbarAiUsage.ProcessFixture.exe");
        var source = new RecoveringProcessSource(
            new UsageProcess(fixture, TimeSpan.FromMilliseconds(500), ["--exit-with-child", childPidFile]));
        var updater = new UsageUpdater(source);
        try
        {
            Assert.True(await updater.RefreshAsync());
            Assert.True(File.Exists(childPidFile));
            Assert.Contains("did not finish", updater.State.FailureSummary);
            Assert.True(await updater.RefreshAsync());
            Assert.Null(updater.State.FailureSummary);
            Assert.Equal(61, updater.State.Values.Codex5Hour?.RemainingPercent);
        }
        finally
        {
            StopChild(childPidFile);
        }
    }

    [Fact]
    public async Task ShutdownStopKillsActiveProcessAndWaitsWithinBound()
    {
        var childPidFile = Path.Combine(Path.GetTempPath(), $"taskbar-shutdown-child-{Guid.NewGuid():N}.pid");
        var fixture = Path.Combine(AppContext.BaseDirectory, "TaskbarAiUsage.ProcessFixture.exe");
        var source = new UsageProcess(fixture, TimeSpan.FromSeconds(30), ["--spawn-child-and-wait", childPidFile]);
        using var callerCancellation = new CancellationTokenSource();
        try
        {
            var read = source.ReadJsonAsync(callerCancellation.Token);
            for (var attempt = 0; attempt < 40 && !File.Exists(childPidFile); attempt++)
                await Task.Delay(50);
            Assert.True(File.Exists(childPidFile));
            var stopwatch = Stopwatch.StartNew();
            source.StopForShutdown(TimeSpan.FromSeconds(2));
            stopwatch.Stop();
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
            Assert.False(IsProcessRunning(ReadChildProcessId(childPidFile)));
        }
        finally
        {
            StopChild(childPidFile);
        }
    }

    [Fact]
    public async Task NonZeroExitDoesNotReturnOutput()
    {
        var source = new UsageProcess(Environment.GetEnvironmentVariable("ComSpec")!, TimeSpan.FromSeconds(5), ["/c", "echo sensitive-output & exit /b 17"]);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadJsonAsync(CancellationToken.None));
        Assert.Contains("code 17", exception.Message);
        Assert.DoesNotContain("sensitive-output", exception.Message);
    }

    [Fact]
    public async Task TimeoutTerminatesChild()
    {
        var source = new UsageProcess(Environment.GetEnvironmentVariable("ComSpec")!, TimeSpan.FromMilliseconds(250), ["/c", "ping -n 10 127.0.0.1 > nul"]);
        await Assert.ThrowsAsync<TimeoutException>(() => source.ReadJsonAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MissingExecutableIsReported()
    {
        var source = new UsageProcess(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"), TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<FileNotFoundException>(() => source.ReadJsonAsync(CancellationToken.None));
    }

    private static void StopChild(string pidFile)
    {
        try
        {
            if (!File.Exists(pidFile) || !int.TryParse(File.ReadAllText(pidFile), out var processId))
                return;
            using var child = Process.GetProcessById(processId);
            if (!child.HasExited)
                child.Kill(entireProcessTree: true);
            child.WaitForExit(3000);
        }
        catch (ArgumentException) { }
        finally
        {
            if (File.Exists(pidFile))
                File.Delete(pidFile);
        }
    }

    private static int ReadChildProcessId(string pidFile) => int.Parse(File.ReadAllText(pidFile), CultureInfo.InvariantCulture);

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

public sealed class UsageUpdaterTests
{
    [Fact]
    public async Task SkipsOverlappingUpdates()
    {
        var source = new BlockingSource();
        var updater = new UsageUpdater(source);
        var first = updater.RefreshAsync();
        await source.Started.Task;
        Assert.False(await updater.RefreshAsync());
        source.Release.TrySetResult();
        Assert.True(await first);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task FailureClearsValuesInsteadOfKeepingStaleData()
    {
        var updater = new UsageUpdater(new SequenceSource(FixtureJson, new InvalidOperationException("Tokscale exited with code 3.")));
        await updater.RefreshAsync();
        Assert.Equal(93, updater.State.Values.Codex5Hour?.RemainingPercent);
        await updater.RefreshAsync();
        Assert.Null(updater.State.Values.Codex5Hour);
        Assert.NotNull(updater.State.FailureSummary);
    }

    private const string FixtureJson = "[{\"provider\":\"Codex\",\"metrics\":[{\"label\":\"5h\",\"remaining_percent\":93},{\"label\":\"Weekly\",\"remaining_percent\":25}]},{\"provider\":\"Grok\",\"metrics\":[{\"label\":\"Weekly\",\"remaining_percent\":53}]}]";
}

public sealed class RendererAndLifecycleTests
{
    [Fact]
    public void RendersRepresentativeValuesAndUnavailableMarkersAtDpiScales()
    {
        RunSta(() =>
        {
            Assert.Equal(32, TaskbarIconRenderer.Render(0, 7).PixelWidth);
            Assert.Equal(48, TaskbarIconRenderer.Render(73, 100, scale: 1.5).PixelWidth);
            Assert.Equal(32, TaskbarIconRenderer.Render(null, 53).PixelWidth);
            Assert.Equal(32, TaskbarIconRenderer.Render(73, null).PixelWidth);
            Assert.Equal(64, TaskbarIconRenderer.Render(null, null, scale: 2).PixelWidth);
            Assert.Equal(144, TaskbarIconRenderer.Render(45, 67, scale: 4.5).PixelWidth);
            Assert.Equal(160, TaskbarIconRenderer.Render(45, 67, scale: 5).PixelWidth);
            Assert.Throws<ArgumentOutOfRangeException>(() => TaskbarIconRenderer.Render(45, 67, scale: double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => TaskbarIconRenderer.Render(45, 67, scale: double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => TaskbarIconRenderer.Render(45, 67, scale: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => TaskbarIconRenderer.Render(101, 2));
            var preview = TaskbarIconRenderer.Render(73, 53, scale: 4);
            DirectoryInfo? testProject = new(AppContext.BaseDirectory);
            while (testProject is not null && !File.Exists(Path.Combine(testProject.FullName, "TaskbarAiUsage.Tests.csproj")))
                testProject = testProject.Parent;
            Assert.NotNull(testProject);
            var directory = Path.Combine(Directory.GetParent(testProject!.FullName)!.FullName, "artifacts");
            Directory.CreateDirectory(directory);
            using var stream = File.Create(Path.Combine(directory, "taskbar-icon-preview.png"));
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(preview));
            encoder.Save(stream);
        });
    }

    [Fact]
    public void CloseMinimizesUntilExplicitExitOrWindowsShutdown()
    {
        var lifecycle = new WindowLifetimePolicy();
        Assert.True(lifecycle.ShouldMinimizeOnClose);
        lifecycle.AllowShutdown();
        Assert.False(lifecycle.ShouldMinimizeOnClose);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}

public sealed class AppSettingsTests
{
    [Fact]
    public void MissingSettingsUseDocumentedDefaults()
    {
        WithSettings("{}", path =>
        {
            var settings = AppSettings.Load(path);
            Assert.Equal(300, settings.UpdateIntervalSeconds);
            Assert.Equal(60, settings.CliTimeoutSeconds);
            Assert.Null(settings.TokscalePath);
        });
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"updateIntervalSeconds\":0}")]
    [InlineData("{\"cliTimeoutSeconds\":9999}")]
    [InlineData("{\"unknownSetting\":true}")]
    [InlineData("{ invalid")]
    public void InvalidSettingsGiveAnActionableConfigurationError(string json)
    {
        WithSettings(json, path => Assert.Throws<InvalidDataException>(() => AppSettings.Load(path)));
    }

    private static void WithSettings(string json, Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), $"taskbar-usage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            action(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

internal sealed class BlockingSource : IUsageSource
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Calls { get; private set; }
    public void StopForShutdown(TimeSpan waitLimit) => Release.TrySetCanceled();
    public async Task<string> ReadJsonAsync(CancellationToken cancellationToken)
    {
        Calls++;
        Started.TrySetResult();
        await Release.Task.WaitAsync(cancellationToken);
        return "[]";
    }
}

internal sealed class SequenceSource(string success, Exception failure) : IUsageSource
{
    private int calls;
    public void StopForShutdown(TimeSpan waitLimit) { }
    public Task<string> ReadJsonAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref calls) == 1) return Task.FromResult(success);
        return Task.FromException<string>(failure);
    }
}

internal sealed class RecoveringProcessSource(UsageProcess process) : IUsageSource
{
    private int calls;
    public void StopForShutdown(TimeSpan waitLimit) => process.StopForShutdown(waitLimit);
    public Task<string> ReadJsonAsync(CancellationToken cancellationToken) =>
        Interlocked.Increment(ref calls) == 1
            ? process.ReadJsonAsync(cancellationToken)
            : Task.FromResult("[{\"provider\":\"Codex\",\"metrics\":[{\"label\":\"5h\",\"remaining_percent\":61},{\"label\":\"Weekly\",\"remaining_percent\":31}]},{\"provider\":\"Grok\",\"metrics\":[{\"label\":\"Weekly\",\"remaining_percent\":51}]}]");
}
