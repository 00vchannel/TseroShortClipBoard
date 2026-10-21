using System.IO;
using System.Windows.Threading;
using ClipboardCore;
using Velopack;
using Velopack.Sources;

namespace ClipboardApp.Services;

public enum AppUpdatePhase { Unknown, Current, Available, Downloading, Preparing, Failed }
public sealed record AppUpdateState(AppUpdatePhase Phase, string? Version = null, int? Percent = null, string? Error = null);

public interface IAppUpdateService
{
    AppUpdateState State { get; }
    event EventHandler? Changed;
    Task DownloadAsync();
    void Cancel();
}

public sealed class AppUpdateService : IAppUpdateService, IDisposable
{
    private readonly UpdateManager manager;
    private readonly Dispatcher dispatcher;
    private readonly Func<UpdateManager, UpdateInfo, CancellationToken, Task> prepare;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? download;
    private UpdateInfo? available;
    private bool checking;
    private bool started;
    public AppUpdateState State { get; private set; } = new(AppUpdatePhase.Unknown);
    public event EventHandler? Changed;

    public AppUpdateService(Dispatcher dispatcher, Func<UpdateManager, UpdateInfo, CancellationToken, Task> prepare)
        : this(dispatcher, prepare, CreateManager()) { }

    internal AppUpdateService(Dispatcher dispatcher, Func<UpdateManager, UpdateInfo, CancellationToken, Task> prepare, UpdateManager manager)
    {
        this.dispatcher = dispatcher;
        this.prepare = prepare;
        this.manager = manager;
    }

    private static UpdateManager CreateManager()
    {
#if UPDATE_TEST_BUILD
        if (UpdateTestHarness.Feed is { } feed) return new UpdateManager(feed,
            new UpdateOptions { ExplicitChannel = "win-x64", MaximumDeltasBeforeFallback = -1 });
#endif
        return new UpdateManager(new GithubSource("https://github.com/00vchannel/TseroShortClipBoard", null, false),
            new UpdateOptions { ExplicitChannel = "win-x64", AllowVersionDowngrade = false, MaximumDeltasBeforeFallback = -1 });
    }

    public void Start()
    {
        if (started || !manager.IsInstalled) return;
        started = true;
        _ = RunChecksAsync();
    }

    private async Task RunChecksAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), lifetime.Token);
            while (!lifetime.IsCancellationRequested)
            {
                await CheckAsync();
                await Task.Delay(TimeSpan.FromHours(6), lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
    }

    internal async Task CheckAsync()
    {
        if (checking || download is not null) return;
        checking = true;
        try
        {
            var result = await Task.Run(() => manager.CheckForUpdatesAsync());
            if (lifetime.IsCancellationRequested || download is not null) return;
            available = result is { IsDowngrade: false } && !result.TargetFullRelease.Version.IsPrerelease &&
                result.TargetFullRelease.Version > manager.CurrentVersion ? result : null;
            SetState(new(available is null ? AppUpdatePhase.Current : AppUpdatePhase.Available,
                available?.TargetFullRelease.Version.ToString()));
        }
        catch (Exception) when (!lifetime.IsCancellationRequested)
        {
            // A failed request is not proof of being current. Keep an already confirmed update visible.
            if (available is null) SetState(new(AppUpdatePhase.Unknown));
        }
        catch (Exception) when (lifetime.IsCancellationRequested) { }
        finally { checking = false; }
    }

    public async Task DownloadAsync()
    {
        if (download is not null || available is null || lifetime.IsCancellationRequested) return;
        var target = available;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        download = cancellation;
        SetState(new(AppUpdatePhase.Downloading, target.TargetFullRelease.Version.ToString()));
        try
        {
            // Full packages keep the first implementation deterministic and recoverable.
            await Task.Run(() => manager.DownloadUpdatesAsync(target, percent =>
            {
                dispatcher.BeginInvoke(() =>
                {
                    if (download == cancellation && State.Phase == AppUpdatePhase.Downloading && !cancellation.IsCancellationRequested)
                        SetState(State with { Percent = percent is >= 0 and <= 100 ? percent : null });
                });
            }, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            SetState(new(AppUpdatePhase.Preparing, target.TargetFullRelease.Version.ToString()));
            await prepare(manager, target, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            if (!lifetime.IsCancellationRequested) SetState(new(AppUpdatePhase.Available, target.TargetFullRelease.Version.ToString()));
        }
        catch (Exception ex)
        {
            if (!lifetime.IsCancellationRequested)
                SetState(new(AppUpdatePhase.Failed, target.TargetFullRelease.Version.ToString(), Error: FriendlyError(ex)));
        }
        finally { download = null; }
    }

    private static string FriendlyError(Exception error) => error switch
    {
        UnauthorizedAccessException => "無法寫入程式或備份資料夾，請確認存取權限。",
        System.Net.Http.HttpRequestException => "下載中斷，請確認網路後重試。",
        InvalidDataException or InvalidOperationException => error.Message,
        IOException => "無法完成更新，請確認磁碟空間及檔案是否被其他程式占用。",
        _ => "更新未完成，原程式及資料仍保留。請稍後重試。"
    };

    private void SetState(AppUpdateState state) { State = state; Changed?.Invoke(this, EventArgs.Empty); }
    public void Cancel() => download?.Cancel();
    public void Dispose() { lifetime.Cancel(); download?.Cancel(); }
}
