using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Embasic.ENote.App.ViewModels;
using Embasic.ENote.Storage.Workspaces;

namespace Embasic.ENote.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(
            new WorkspaceStorage(), PickWorkspace, RequestFolderName);
        DataContext = _viewModel;
    }

    private string? PickWorkspace()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Embasic E-Note — Çalışma alanı klasörünü seçin"
        };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    private string? RequestFolderName()
    {
        var dialog = new FolderNameDialog { Owner = this };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private void WorkspaceTree_SelectedItemChanged(
        object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _viewModel.SelectNode(e.NewValue as WorkspaceTreeNode);
    }

    private async void WorkspaceTree_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item &&
            item.DataContext is WorkspaceTreeNode node)
        {
            await node.ExpandAsync();
        }
    }
}
