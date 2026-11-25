using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Embasic.ENote.App.ViewModels;
using Embasic.ENote.Storage.Workspaces;

namespace Embasic.ENote.App;

public partial class MainWindow
{
    private bool _revealingWorkspaceNote;

    private async void RevealWorkspaceNote_Click(object sender, RoutedEventArgs e)
    {
        if (_revealingWorkspaceNote || _fileOperationBusy || SelectedTab is not { IsInTrash: false } tab) return;
        if (_viewModel.Roots.FirstOrDefault() is not { } root)
        {
            MarkdownStatusText.Text = "Önce bir çalışma alanı seçin.";
            return;
        }

        string path = tab.FilePath;
        string relative = Path.GetRelativePath(root.FullPath, path);
        string[] segments = relative.Split(Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative) || segments[0] is ".." or ".")
        {
            MarkdownStatusText.Text = "Açık not seçili çalışma alanının dışında.";
            return;
        }

        _revealingWorkspaceNote = true;
        UpdateDocumentActions();
        try
        {
            // Re-read only the ancestors. A cached row may refer to a path removed
            // or renamed on disk before the watcher has reconciled it.
            var branch = new List<WorkspaceTreeNode> { root };
            for (int i = 0; i < segments.Length; i++)
            {
                var parent = branch[^1];
                string? error = await parent.RefreshChildrenAsync(CancellationToken.None);
                if (!IsCurrent()) return;
                if (error is not null)
                {
                    MarkdownStatusText.Text = error;
                    return;
                }

                var kind = i == segments.Length - 1 ? WorkspaceEntryKind.PlainMarkdown : WorkspaceEntryKind.Folder;
                var match = parent.Children.FirstOrDefault(node =>
                    node.Kind == kind && string.Equals(node.Name, segments[i], StringComparison.Ordinal));
                if (match is null)
                {
                    MarkdownStatusText.Text = "Açık not çalışma alanındaki bu konumda bulunamadı. Açık taslak korunuyor.";
                    return;
                }
                branch.Add(match);
            }

            ItemsControl owner = WorkspaceTree;
            foreach (var node in branch)
            {
                if (WorkspaceTreePanel.ContainerFor(owner, node) is not { } container)
                {
                    MarkdownStatusText.Text = "Not ağaçta gösterilemedi. Yenileyip tekrar deneyin.";
                    return;
                }
                owner = container;
                if (node.IsFolder) container.IsExpanded = true;
                else
                {
                    container.IsSelected = true;
                    container.BringIntoView();
                    MarkdownStatusText.Text = $"Not ağaçta seçildi: {relative}";
                }
            }
        }
        finally
        {
            _revealingWorkspaceNote = false;
            UpdateDocumentActions();
        }

        bool IsCurrent() => IsLoaded && !_fileOperationBusy &&
            ReferenceEquals(SelectedTab, tab) && !tab.IsInTrash && tab.FilePath == path &&
            ReferenceEquals(_viewModel.Roots.FirstOrDefault(), root);
    }
}

/// <summary>Realizes a specific virtualized row without expanding its siblings.</summary>
public sealed class WorkspaceTreePanel : VirtualizingStackPanel
{
    internal static TreeViewItem? ContainerFor(ItemsControl owner, WorkspaceTreeNode node)
    {
        int index = owner.Items.IndexOf(node);
        if (index < 0) return null;
        owner.ApplyTemplate();
        owner.UpdateLayout();
        if (owner.ItemContainerGenerator.ContainerFromIndex(index) is TreeViewItem realized) return realized;

        FindItemsPanel(owner)?.BringIndexIntoView(index);
        owner.UpdateLayout();
        return owner.ItemContainerGenerator.ContainerFromIndex(index) as TreeViewItem;
    }

    private static WorkspaceTreePanel? FindItemsPanel(DependencyObject visual)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
        {
            var child = VisualTreeHelper.GetChild(visual, i);
            if (child is WorkspaceTreePanel { IsItemsHost: true } panel) return panel;
            if (child is not ItemsControl && FindItemsPanel(child) is { } nested) return nested;
        }
        return null;
    }
}
