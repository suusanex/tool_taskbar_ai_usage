using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TaskbarAiUsage;

public partial class MainWindow : Window
{
    private readonly UsageUpdater updater;
    private readonly System.Windows.Threading.DispatcherTimer timer;
    private readonly CancellationTokenSource lifetime = new();
    private Task activeRefresh = Task.CompletedTask;
    public MainWindow()
    {
        InitializeComponent();
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var settings = AppSettings.Load(configPath);
        var cliPath = TokscalePathResolver.Resolve(settings.TokscalePath);
        updater = new UsageUpdater(new UsageProcess(cliPath, TimeSpan.FromSeconds(settings.CliTimeoutSeconds)));
        updater.StateChanged += (_, state) =>
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;
            try { Dispatcher.BeginInvoke(new Action(() => Render(state))); }
            catch (InvalidOperationException) { }
        };
        timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(settings.UpdateIntervalSeconds) };
        timer.Tick += (_, _) => _ = RefreshInBackgroundAsync();
        timer.Start();
        Render(updater.State);
        Loaded += (_, _) =>
        {
            var source = (HwndSource?)PresentationSource.FromVisual(this);
            source?.AddHook(WindowMessageHook);
            _ = RefreshInBackgroundAsync();
        };
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        try { await RefreshInBackgroundAsync(); }
        finally { RefreshButton.IsEnabled = true; }
    }

    private async void Exit_Click(object sender, RoutedEventArgs e)
    {
        await BeginShutdownAsync();
    }

    public async Task BeginShutdownAsync()
    {
        timer.Stop();
        lifetime.Cancel();
        SafeLog.Write("application-exit", "Application is shutting down.");
        await activeRefresh;
        Close();
    }

    public void BeginSessionShutdown()
    {
        timer.Stop();
        lifetime.Cancel();
        SafeLog.Write("windows-session-ending", "Windows is ending the session.");
        updater.StopForShutdown(TimeSpan.FromSeconds(2));
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        timer.Stop();
        lifetime.Cancel();
        updater.StopForShutdown(TimeSpan.FromSeconds(2));
        base.OnClosing(e);
    }

    private void Render(UpdateState state)
    {
        SetMetric(Codex5h, Codex5hReset, state.Values.Codex5Hour);
        SetMetric(CodexWeekly, CodexWeeklyReset, state.Values.CodexWeekly);
        SetMetric(GrokWeekly, GrokWeeklyReset, state.Values.GrokWeekly);
        AttemptText.Text = state.LastAttemptAt == DateTimeOffset.MinValue ? "最終試行: --" : $"最終試行: {FormatLocal(state.LastAttemptAt)}";
        SuccessText.Text = state.LastSuccessAt is null ? "最終成功: --" : $"最終成功: {FormatLocal(state.LastSuccessAt.Value)}";
        FailureText.Text = state.FailureSummary is null ? "" : $"状態: {state.FailureSummary}";
        UpdateIcon(state.Values.Codex5Hour?.RemainingPercent, state.Values.GrokWeekly?.RemainingPercent);
    }

    private Task RefreshInBackgroundAsync()
    {
        if (activeRefresh.IsCompleted)
            activeRefresh = ExecuteRefreshAsync();
        return activeRefresh;
    }

    private async Task ExecuteRefreshAsync()
    {
        try { await updater.RefreshAsync(lifetime.Token); }
        catch (OperationCanceledException) { }
    }

    private void UpdateIcon(int? upper, int? lower)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        Icon = TaskbarIconRenderer.Render(upper, lower, scale: dpi.DpiScaleX);
    }

    private static IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x02E0 && Application.Current.MainWindow is MainWindow window)
            window.Dispatcher.BeginInvoke(new Action(() => window.UpdateIcon(window.updater.State.Values.Codex5Hour?.RemainingPercent, window.updater.State.Values.GrokWeekly?.RemainingPercent)));
        return IntPtr.Zero;
    }

    private static void SetMetric(System.Windows.Controls.TextBlock valueText, System.Windows.Controls.TextBlock resetText, UsageMetric? metric)
    {
        valueText.Text = metric is null ? "--" : $"{metric.RemainingPercent}%";
        resetText.Text = metric?.ResetsAt is null ? "リセット: --" : $"リセット: {FormatLocal(metric.ResetsAt.Value)}";
    }

    private static string FormatLocal(DateTimeOffset value) => value.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.CurrentCulture);
}
