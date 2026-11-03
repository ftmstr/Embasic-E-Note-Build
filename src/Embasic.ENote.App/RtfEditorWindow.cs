using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Embasic.ENote.Storage;
using Microsoft.Win32;

namespace Embasic.ENote.App;

/// <summary>
/// Native, offline RTF visual editor. Always exports a NEW RTF copy.
/// RichTextBox is NOT a promise of Word/LibreOffice layout equivalence.
/// </summary>
public sealed class RtfEditorWindow : Window
{
    private readonly SafeRtfCopyStore _store = new();
    private readonly RtfSourceSnapshot _source;
    private readonly RichTextBox _editor = new()
    {
        AcceptsTab = true,
        IsUndoEnabled = true,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = Brushes.White,
        Foreground = Brushes.Black,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 14,
        Padding = new Thickness(24)
    };
    private readonly TextBlock _status = new()
    {
        Foreground = Brushes.DarkSlateGray,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(12, 6, 12, 8)
    };
    private bool _dirty;

    public RtfEditorWindow(string sourcePath)
    {
        _source = _store.Read(sourcePath);
        Title = "Embasic E-Note — RTF görsel editörü (kopya kaydı)";
        Width = 1024;
        Height = 760;
        MinWidth = 700;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var toolbar = new WrapPanel { Margin = new Thickness(12, 8, 12, 5) };
        toolbar.Children.Add(ActionButton("Kalın", () => Run(EditingCommands.ToggleBold)));
        toolbar.Children.Add(ActionButton("İtalik", () => Run(EditingCommands.ToggleItalic)));
        toolbar.Children.Add(ActionButton("Altı çizili", () => Run(EditingCommands.ToggleUnderline)));
        toolbar.Children.Add(ActionButton("Madde işareti", () => Run(EditingCommands.ToggleBullets)));
        toolbar.Children.Add(ActionButton("Sola", () => Run(EditingCommands.AlignLeft)));
        toolbar.Children.Add(ActionButton("Ortala", () => Run(EditingCommands.AlignCenter)));
        toolbar.Children.Add(ActionButton("Sağa", () => Run(EditingCommands.AlignRight)));
        toolbar.Children.Add(ActionButton("Başlık", () =>
            _editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 20.0)));
        toolbar.Children.Add(ActionButton("Normal", () =>
            _editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 14.0)));
        toolbar.Children.Add(ActionButton("Farklı Kaydet (.rtf)", SaveEditedCopy));

        var header = new StackPanel { Background = Brushes.WhiteSmoke };
        header.Children.Add(toolbar);
        header.Children.Add(new TextBlock
        {
            Text = "Yerleşik RTF editörü · Kaydetme yalnız YENİ kopyaya yapılır; özgün dosya değişmez. " +
                   "Karmaşık resim, tablo ve sayfa yerleşiminde eşdeğerlik doğrulanmadı.",
            Margin = new Thickness(12, 0, 12, 6),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DarkRed
        });
        header.Children.Add(_status);
        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(_editor);
        Content = layout;

        using (var input = new MemoryStream(_source.Bytes, writable: false))
        {
            new TextRange(_editor.Document.ContentStart, _editor.Document.ContentEnd)
                .Load(input, DataFormats.Rtf);
        }
        _status.Text = Path.GetFileName(_source.FilePath) +
            " açıldı. Orijinal salt okunur; düzenlenmiş kopya için Farklı Kaydet.";
        _editor.TextChanged += (_, _) =>
        {
            _dirty = true;
            Title = "Embasic E-Note — RTF düzenleniyor *";
        };
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private Button ActionButton(string label, Action action)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(9, 5, 9, 5),
            Margin = new Thickness(0, 0, 6, 6),
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void Run(RoutedUICommand command)
    {
        if (command.CanExecute(null, _editor))
            command.Execute(null, _editor);
        _editor.Focus();
    }

    private void SaveEditedCopy()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Düzenlenen RTF'yi YENİ dosyaya kaydet",
            Filter = "Rich Text Format (.rtf)|*.rtf",
            DefaultExt = ".rtf",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = Path.GetFileNameWithoutExtension(_source.FilePath) + "-duzenlendi.rtf",
            InitialDirectory = Path.GetDirectoryName(_source.FilePath)
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            using var output = new MemoryStream();
            new TextRange(_editor.Document.ContentStart, _editor.Document.ContentEnd)
                .Save(output, DataFormats.Rtf);
            _store.SaveEditedCopy(_source, dialog.FileName, output.ToArray());
            _dirty = false;
            Title = "Embasic E-Note — RTF görsel editörü (kopya kaydı)";
            _status.Text = "Yeni RTF kopyası kaydedildi: " + dialog.FileName +
                ". Özgün dosya korunmuştur.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            MessageBox.Show(this, "Kayıt reddedildi; özgün belgeye dokunulmadı: " +
                error.Message, "Embasic E-Note", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            SaveEditedCopy();
            e.Handled = true;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_dirty) return;
        var result = MessageBox.Show(this,
            "Bu RTF üzerinde yeni bir dosyaya kaydedilmemiş değişiklikler var. " +
            "Kaydetmeden kapatmak istediğinize emin misiniz?",
            "Embasic E-Note", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        e.Cancel = result != MessageBoxResult.Yes;
    }
}
