using System.IO;
using System.Windows;
using System.Windows.Input;
using Embasic.ENote.Storage.Workspaces;

namespace Embasic.ENote.App;

public partial class MainWindow
{
    private CancellationTokenSource? _workspaceSearchCancellation;
    private WorkspaceRoot? _searchResultWorkspace;

    private void ShowWorkspaceSearch_Click(object sender, RoutedEventArgs e)
    {
        MainSections.SelectedItem = WorkspaceSearchTab;
        WorkspaceQuery.Focus();
        WorkspaceQuery.SelectAll();
    }

    private async void StartWorkspaceSearch_Click(object sender, RoutedEventArgs e) => await SearchWorkspaceAsync();

    private async void WorkspaceQuery_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _workspaceSearchCancellation is { } cancellation)
        {
            cancellation.Cancel();
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SearchWorkspaceAsync();
    }

    private async Task SearchWorkspaceAsync()
    {
        if (_workspaceSearchCancellation is not null) return;
        var workspace = _viewModel.CurrentWorkspace;
        if (workspace is null)
        {
            WorkspaceSearchStatus.Text = "Arama yapmak için önce bir çalışma alanı seçin.";
            return;
        }

        string query = WorkspaceQuery.Text.Trim();
        if (query.Length == 0)
        {
            WorkspaceSearchStatus.Text = "Bir arama terimi yazın.";
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _workspaceSearchCancellation = cancellation;
        _searchResultWorkspace = null;
        WorkspaceSearchResults.ItemsSource = null;
        StartWorkspaceSearchButton.IsEnabled = false;
        CancelWorkspaceSearchButton.IsEnabled = true;
        WorkspaceQuery.IsReadOnly = true;
        WorkspaceSearchStatus.Text = "Notlar aranıyor…";
        try
        {
            var result = await WorkspaceMarkdownSearch.SearchAsync(workspace, query, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!Equals(workspace, _viewModel.CurrentWorkspace)) return;
            _searchResultWorkspace = workspace;
            WorkspaceSearchResults.ItemsSource = result.Hits;
            string status = result.Truncated ? "İlk 200 sonuç gösteriliyor. Aramayı daraltabilirsiniz."
                : result.Hits.Count == 0 ? "Eşleşme bulunamadı." : $"{result.Hits.Count} not bulundu.";
            if (result.Unreadable > 0) status += $" {result.Unreadable} dosya veya klasör okunamadı.";
            if (result.Oversized > 0) status += $" 2 MiB sınırını aşan {result.Oversized} notun içeriği atlandı.";
            WorkspaceSearchStatus.Text = status;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (Equals(workspace, _viewModel.CurrentWorkspace)) WorkspaceSearchStatus.Text = "Arama iptal edildi.";
        }
        catch (ArgumentException)
        {
            WorkspaceSearchStatus.Text = "Bir arama terimi yazın.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            WorkspaceSearchStatus.Text = "Çalışma alanı taranamadı. Klasörü ve izinleri kontrol edin.";
        }
        finally
        {
            _workspaceSearchCancellation = null;
            WorkspaceQuery.IsReadOnly = false;
            StartWorkspaceSearchButton.IsEnabled = true;
            CancelWorkspaceSearchButton.IsEnabled = false;
        }
    }

    private void CancelWorkspaceSearch_Click(object sender, RoutedEventArgs e) => _workspaceSearchCancellation?.Cancel();

    private void ClearWorkspaceSearch()
    {
        _workspaceSearchCancellation?.Cancel();
        _searchResultWorkspace = null;
        WorkspaceSearchResults.ItemsSource = null;
        WorkspaceSearchStatus.Text = "Arama yapmak için bir terim yazın.";
    }

    private void OpenWorkspaceResult_Click(object sender, RoutedEventArgs e) => OpenWorkspaceSearchResult();

    private void WorkspaceResults_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        OpenWorkspaceSearchResult();
    }

    private void OpenWorkspaceSearchResult()
    {
        if (WorkspaceSearchResults.SelectedItem is not WorkspaceMarkdownHit hit ||
            _searchResultWorkspace is not { } workspace || !Equals(workspace, _viewModel.CurrentWorkspace))
            return;
        try { WorkspaceMarkdownSearch.ValidateResult(workspace, hit.Path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            WorkspaceSearchStatus.Text = "Bu not taşınmış, silinmiş veya erişilemez durumda. Aramayı yenileyin.";
            return;
        }

        OpenMarkdown(hit.Path);
        ActiveEditor?.Focus();
    }
}
