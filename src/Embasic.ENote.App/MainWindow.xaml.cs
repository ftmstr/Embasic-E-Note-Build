using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using Microsoft.Win32;
using Embasic.ENote.App.ViewModels;
using Embasic.ENote.Storage.Workspaces;

namespace Embasic.ENote.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _darkTheme;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(
            new WorkspaceStorage(), PickWorkspace, RequestFolderName);
        DataContext = _viewModel;
    }

    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        _darkTheme = !_darkTheme;
        Resources["WindowSurface"] = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(_darkTheme ? "#0F1724" : "#F7F9FC"));
        Resources["PanelSurface"] = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(_darkTheme ? "#172131" : "#FFFFFF"));
        Resources["PrimaryInk"] = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(_darkTheme ? "#E8EDF5" : "#172033"));
        Resources["SecondaryInk"] = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(_darkTheme ? "#A8B9CE" : "#64748B"));
        BrandLogo.Source = new BitmapImage(new Uri(_darkTheme
            ? "pack://application:,,,/Assets/Logo-Dark.png"
            : "pack://application:,,,/Assets/Logo-Light.png"));
        ThemeToggleButton.Content = _darkTheme ? "Açık tema" : "Koyu tema";
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
