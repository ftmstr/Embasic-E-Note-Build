using System.IO;
using System.Windows.Threading;

namespace Embasic.ENote.App;

public partial class MainWindow
{
    private readonly DispatcherTimer _workspaceWatchTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private WorkspaceWatch? _workspaceWatch;
    private bool _workspaceWatchRefreshing;

    private void StartWorkspaceWatch()
    {
        StopWorkspaceWatch();
        if (_viewModel.CurrentWorkspace is not { } root) return;
        try
        {
            _workspaceWatch = new WorkspaceWatch(root.FullPath);
            WorkspaceWatchStatus.Text = "Klasör ağacı otomatik izleniyor.";
            _workspaceWatchTimer.Start();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            WorkspaceWatchStatus.Text = "Otomatik izleme başlatılamadı. Yenile düğmesini kullanın.";
        }
    }

    private void StopWorkspaceWatch()
    {
        _workspaceWatchTimer.Stop();
        var watch = _workspaceWatch;
        _workspaceWatch = null;
        watch?.Dispose();
    }

    private async Task RefreshWorkspaceWatchAsync()
    {
        if (_workspaceWatchRefreshing || _fileOperationBusy || _workspaceWatch is not { } watch ||
            _viewModel.Roots.FirstOrDefault() is not { } root || !watch.TakeChanges(out var watcherError)) return;

        _workspaceWatchRefreshing = true;
        try
        {
            var error = await root.RefreshLoadedBranchesAsync(watch.Token);
            if (!ReferenceEquals(_workspaceWatch, watch)) return;
            WorkspaceWatchStatus.Text = error ?? (watcherError
                ? "İzleyici bildirim kaybetti. Gerekirse Yenile düğmesini kullanın."
                : "Klasör ağacı otomatik izleniyor.");
        }
        catch (OperationCanceledException) when (watch.Token.IsCancellationRequested) { }
        finally { _workspaceWatchRefreshing = false; }
    }

    private sealed class WorkspaceWatch : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly CancellationTokenSource _lifetime = new();
        private int _dirty;
        private int _error;

        public CancellationToken Token { get; }

        public WorkspaceWatch(string path)
        {
            Token = _lifetime.Token;
            _watcher = new FileSystemWatcher();
            try
            {
                _watcher.Path = path;
                _watcher.IncludeSubdirectories = true;
                _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName;
                _watcher.Created += OnChange;
                _watcher.Deleted += OnChange;
                _watcher.Renamed += OnChange;
                _watcher.Error += OnError;
                _watcher.EnableRaisingEvents = true;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void OnChange(object sender, FileSystemEventArgs e) => Interlocked.Exchange(ref _dirty, 1);

        private void OnError(object sender, ErrorEventArgs e)
        {
            Interlocked.Exchange(ref _error, 1);
            Interlocked.Exchange(ref _dirty, 1);
        }

        public bool TakeChanges(out bool watcherError)
        {
            bool changed = Interlocked.Exchange(ref _dirty, 0) != 0;
            watcherError = changed && Interlocked.Exchange(ref _error, 0) != 0;
            return changed;
        }

        public void Dispose()
        {
            _lifetime.Cancel();
            _watcher.Dispose();
            _lifetime.Dispose();
        }
    }
}
