using System.Windows;
using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Embasic.ENote.Storage;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using Embasic.ENote.Core.Printing;
using Microsoft.Win32;
using Embasic.ENote.App.ViewModels;
using Embasic.ENote.Storage.Workspaces;

namespace Embasic.ENote.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _darkTheme;
    private bool _syncingPageControls;
    private readonly PlainMarkdownDocumentStore _noteStore = new();
    private readonly DocumentPageSettingsStore _pageStore = new();
    private readonly ObservableCollection<PlainMarkdownEditorTab> _tabs = new();

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(
            new WorkspaceStorage(), PickWorkspace, RequestFolderName);
        DataContext = _viewModel;
        PageSizeComboBox.ItemsSource = PageLayoutCatalog.Presets;
        PageSizeComboBox.SelectedItem = PageLayoutCatalog.Get("A4");
        Loaded += (_, _) => UpdatePagePreview();
        MarkdownTabs.ItemsSource = _tabs;
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

    private void PageLayout_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _syncingPageControls || PageSizeComboBox is null ||
            PageZoomComboBox is null || PageOrientationComboBox is null) return;
        if (MarkdownTabs.SelectedItem is PlainMarkdownEditorTab tab &&
            PageSizeComboBox.SelectedItem is PagePaper paper)
        {
            if (paper.Id == "Custom" && tab.Layout.Paper.Id == "Custom")
                paper = tab.Layout.Paper;
            var zoomItem = PageZoomComboBox.SelectedItem as ComboBoxItem;
            int zoom = int.TryParse(zoomItem?.Tag?.ToString(), out int z) ? z : 100;
            var changed = tab.Layout with
            {
                Paper = paper, Landscape = PageOrientationComboBox.SelectedIndex == 1,
                ZoomPercent = zoom
            };
            try { changed.Validate(); tab.Layout = changed; }
            catch (ArgumentOutOfRangeException error)
            {
                MarkdownStatusText.Text = "Geçersiz sayfa ayarı: " + error.Message;
                return;
            }
        }
        UpdatePagePreview();
    }

    private void UpdatePagePreview()
    {
        if (PageSizeComboBox.SelectedItem is not PagePaper paper) return;
        var landscape = PageOrientationComboBox.SelectedIndex == 1;
        var zoomItem = PageZoomComboBox.SelectedItem as ComboBoxItem;
        int zoom = int.TryParse(zoomItem?.Tag?.ToString(), out int z) ? z : 100;
        var setup = MarkdownTabs.SelectedItem is PlainMarkdownEditorTab active
            ? active.Layout
            : PageLayoutSettings.Default with { Paper = paper, Landscape = landscape, ZoomPercent = zoom };
        setup.Validate();
        double scale = Math.Min(220d / (double)setup.WidthMm,
            300d / (double)setup.HeightMm) * (zoom / 100d);
        PagePreviewBorder.Width = (double)setup.WidthMm * scale;
        PagePreviewBorder.Height = (double)setup.HeightMm * scale;
        PageLayoutSummary.Text =
            $"{paper.Name} · {setup.WidthMm} × {setup.HeightMm} mm · " +
            $"{(landscape ? "Yatay" : "Dikey")} · %{zoom}";
    }

    private PlainMarkdownEditorTab? SelectedTab => MarkdownTabs.SelectedItem as PlainMarkdownEditorTab;

    private void OpenMarkdown_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Düz Markdown notu aç (.md)",
            Filter = "Markdown (.md)|*.md",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        foreach (string file in dialog.FileNames) OpenMarkdown(file);
    }

    private void NewMarkdown_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Yeni düz Markdown notu (.md, şifresiz)",
            Filter = "Markdown (.md)|*.md", AddExtension = true,
            DefaultExt = ".md", OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _noteStore.Save(dialog.FileName, "", expectedSha256: null);
            OpenMarkdown(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            MessageBox.Show(this, "Dosya oluşturulmadı: " + ex.Message, "Embasic E-Note",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenMarkdown(string path)
    {
        try
        {
            string absolute = Path.GetFullPath(path);
            var existing = _tabs.FirstOrDefault(t => string.Equals(t.FilePath,
                absolute, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) { MarkdownTabs.SelectedItem = existing; return; }
            var snap = _noteStore.Read(absolute);
            var layout = _pageStore.Read(absolute);
            var tab = new PlainMarkdownEditorTab(absolute, snap, layout);
            _tabs.Add(tab);
            MarkdownTabs.SelectedItem = tab;
            MarkdownStatusText.Text = "Şifresiz Markdown dosyası açıldı: " + Path.GetFileName(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException or
                                   System.Text.DecoderFallbackException or System.Text.Json.JsonException)
        {
            MessageBox.Show(this, "Not açılamadı: " + ex.Message, "Embasic E-Note",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool SaveTab(PlainMarkdownEditorTab tab)
    {
        try
        {
            tab.Save(_noteStore, _pageStore);
            MarkdownStatusText.Text = "Kaydedildi: " + Path.GetFileName(tab.FilePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            MessageBox.Show(this, "Kaydetme reddedildi. Haricî değişiklik veya erişim sorunu olabilir: "
                + ex.Message, "Embasic E-Note", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void SaveMarkdown_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTab is { } tab) SaveTab(tab);
    }

    private bool ConfirmCloseTab(PlainMarkdownEditorTab tab)
    {
        if (!tab.IsDirty && !tab.IsLayoutDirty) return true;
        var decision = MessageBox.Show(this,
            Path.GetFileName(tab.FilePath) + " kaydedilmemiş değişiklikler içeriyor. Kaydetmek ister misiniz?",
            "Embasic E-Note", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (decision == MessageBoxResult.Cancel) return false;
        if (decision == MessageBoxResult.Yes) return SaveTab(tab);
        return true;
    }

    private void CloseMarkdown_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTab is not { } tab || !ConfirmCloseTab(tab)) return;
        _tabs.Remove(tab);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        foreach (var tab in _tabs)
            if (!ConfirmCloseTab(tab)) { e.Cancel = true; return; }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && e.Key == Key.N)
        { NewMarkdown_Click(sender, e); e.Handled = true; return; }
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        switch (e.Key)
        {
            case Key.S: if (SelectedTab is { } tab) SaveTab(tab); e.Handled = true; break;
            case Key.O: OpenMarkdown_Click(sender, e); e.Handled = true; break;
            case Key.N:
                MarkdownStatusText.Text = "Ctrl+N şifreli .emd notuna ayrıldı; güvenlik kabulüne kadar kapalı. Düz .md: Ctrl+Alt+N.";
                e.Handled = true; break;
        }
    }

    private void MarkdownTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MarkdownTabs)) return;
        if (SelectedTab is not { } tab) return;
        _syncingPageControls = true;
        try
        {
            PageSizeComboBox.SelectedItem = PageLayoutCatalog.Get(tab.Layout.Paper.Id);
            PageOrientationComboBox.SelectedIndex = tab.Layout.Landscape ? 1 : 0;
            foreach (var item in PageZoomComboBox.Items.OfType<ComboBoxItem>())
                if (item.Tag?.ToString() == tab.Layout.ZoomPercent.ToString())
                { PageZoomComboBox.SelectedItem = item; break; }
        }
        finally { _syncingPageControls = false; }
        UpdatePagePreview();
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
