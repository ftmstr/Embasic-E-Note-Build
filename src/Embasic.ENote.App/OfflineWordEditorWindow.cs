using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Embasic.ENote.Storage.Documents;
using Microsoft.Win32;

namespace Embasic.ENote.App;

/// <summary>
/// DOCX/ODT offline extracted-paragraph editor; NOT a paginated visual Word clone.
/// Complex structures are readable only. Edited simple files create new copies.
/// </summary>
public sealed class OfflineWordEditorWindow : Window
{
    private readonly OfflineWordPackageStore _store = new();
    private readonly OfflineWordSnapshot _source;
    private readonly TextBox _text = new()
    {
        AcceptsReturn = true,
        AcceptsTab = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 15,
        Padding = new Thickness(22),
        Background = Brushes.White,
        Foreground = Brushes.Black
    };
    private readonly TextBlock _status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(16, 6, 16, 10),
        Foreground = Brushes.DarkSlateGray
    };
    private bool _dirty;

    public OfflineWordEditorWindow(string path)
    {
        _source = _store.Read(path);
        Title = "Embasic E-Note — yerel DOCX/ODT metin editörü (kopya)";
        Width = 1000;
        Height = 730;
        MinWidth = 650;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var header = new StackPanel { Background = Brushes.WhiteSmoke };
        header.Children.Add(new TextBlock
        {
            Text = "Office gerektirmez · Yalnız tek metin akışlı basit paragraflar değiştirilebilir. " +
                   "Sayfa düzeni, görseller, tablolar ve karmaşık biçimlendirmede düzenleme güvenlik nedeniyle kapalıdır.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 12, 16, 4),
            Foreground = Brushes.DarkRed
        });
        var save = new Button
        {
            Content = "Yeni Kopya Kaydet (Ctrl+S)",
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(16, 6, 16, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = _source.CanEdit
        };
        save.Click += (_, _) => SaveCopy();
        header.Children.Add(save);
        _status.Text = _source.CanEdit
            ? "Basit paragraf yapısı doğrulandı. Paragraf sayısını değiştirmeden metni düzenleyip farklı adla kaydedin."
            : "SALT OKUNUR — " + _source.ReadOnlyReason +
              " Karmaşık belgeye veya orijinale otomatik yazma yapılmaz.";
        header.Children.Add(_status);

        _text.Text = string.Join("\n", _source.Paragraphs);
        _text.IsReadOnly = !_source.CanEdit;
        _text.TextChanged += (_, _) =>
        {
            if (_source.CanEdit)
            {
                _dirty = true;
                Title = "Embasic E-Note — DOCX/ODT düzenleniyor *";
            }
        };
        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(_text);
        Content = layout;
        Closing += OnClosing;
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
            {
                if (_source.CanEdit) SaveCopy();
                e.Handled = true;
            }
        };
    }

    private void SaveCopy()
    {
        if (!_source.CanEdit) return;
        var dialog = new SaveFileDialog
        {
            Title = "Yalnız yeni DOCX/ODT kopyasına kaydet",
            Filter = _source.Format == OfflineWordFormat.Docx
                ? "Word DOCX (.docx)|*.docx"
                : "OpenDocument ODT (.odt)|*.odt",
            AddExtension = true,
            DefaultExt = _source.Format == OfflineWordFormat.Docx ? ".docx" : ".odt",
            FileName = Path.GetFileNameWithoutExtension(_source.FilePath) + "-duzenlendi",
            InitialDirectory = Path.GetDirectoryName(_source.FilePath),
            OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var paragraphs = _text.Text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n');
            _store.SaveEditedCopy(_source, dialog.FileName, paragraphs);
            _dirty = false;
            Title = "Embasic E-Note — yerel DOCX/ODT metin editörü (kopya)";
            _status.Text = "Yeni kopya kaydedildi: " + dialog.FileName +
                " · Kaynak dosya değiştirilmedi. Biçim sadakati tam olarak doğrulanmış değildir.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException or
                                   System.Xml.XmlException)
        {
            MessageBox.Show(this, "Kayıt güvenlik nedeniyle reddedildi: " + ex.Message,
                "Embasic E-Note", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_dirty) return;
        e.Cancel = MessageBox.Show(this,
            "Bu belgede yeni dosyaya kaydedilmeyen değişiklikler var. Kaydetmeden kapatılsın mı?",
            "Embasic E-Note", MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes;
    }
}
