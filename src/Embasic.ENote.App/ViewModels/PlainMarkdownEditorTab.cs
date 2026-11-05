using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Embasic.ENote.Core.Printing;
using Embasic.ENote.Storage;
using Embasic.ENote.App.Preview;
using System.Windows;
using System.Windows.Documents;

namespace Embasic.ENote.App.ViewModels;

/// <summary>Temporary plaintext-only WPF tab model. This does not unlock .emd.</summary>
public sealed class PlainMarkdownEditorTab : INotifyPropertyChanged
{
    private string _text;
    private string _savedText;
    private PageLayoutSettings _layout;
    private PageLayoutSettings _savedLayout;
    private MarkdownEditorViewMode _viewMode = MarkdownEditorViewMode.Split;
    public FlowDocument PreviewDocument { get; private set; }
    public MarkdownEditorViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (_viewMode == value) return;
            _viewMode = value;
            Changed(); Changed(nameof(SourceColumnWidth)); Changed(nameof(PreviewColumnWidth));
            Changed(nameof(SourceVisibility)); Changed(nameof(PreviewVisibility));
        }
    }
    public GridLength SourceColumnWidth => new(ViewMode == MarkdownEditorViewMode.Preview ? 0 : 1, GridUnitType.Star);
    public GridLength PreviewColumnWidth => new(ViewMode == MarkdownEditorViewMode.Write ? 0 : 1, GridUnitType.Star);
    public Visibility SourceVisibility => ViewMode == MarkdownEditorViewMode.Preview ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PreviewVisibility => ViewMode == MarkdownEditorViewMode.Write ? Visibility.Collapsed : Visibility.Visible;
    public void RefreshPreview()
    {
        PreviewDocument = MarkdownPreviewDocumentFactory.Create(Text);
        Changed(nameof(PreviewDocument));
    }
    public string FilePath { get; }
    public string Digest { get; private set; }
    public bool HasUtf8Bom { get; }
    public string? LayoutDigest { get; private set; }
    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            Changed();
            Changed(nameof(IsDirty));
            Changed(nameof(DisplayTitle));
        }
    }
    public PageLayoutSettings Layout
    {
        get => _layout;
        set
        {
            _layout = value;
            Changed();
            Changed(nameof(IsLayoutDirty));
            Changed(nameof(DisplayTitle));
        }
    }
    public bool IsDirty => _text != _savedText;
    public bool IsLayoutDirty => !_layout.Equals(_savedLayout);
    public string DisplayTitle => Path.GetFileName(FilePath) + (IsDirty || IsLayoutDirty ? " *" : "");

    public PlainMarkdownEditorTab(string path, PlainMarkdownSnapshot note, DocumentPageSnapshot page)
    {
        FilePath = Path.GetFullPath(path);
        _text = _savedText = note.Text;
        Digest = note.Sha256;
        HasUtf8Bom = note.HasUtf8Bom;
        _layout = _savedLayout = page.Settings;
        LayoutDigest = page.Sha256;
        PreviewDocument = MarkdownPreviewDocumentFactory.Create(_text);
    }
    public void Save(PlainMarkdownDocumentStore notes, DocumentPageSettingsStore pages)
    {
        if (IsDirty)
        {
            Digest = notes.Save(FilePath, Text, Digest, HasUtf8Bom);
            _savedText = _text;
        }
        if (IsLayoutDirty)
        {
            // Metadata is an independent write. Its failure must not be disguised
            // as an all-or-nothing transaction with the text save.
            LayoutDigest = pages.Save(FilePath, Layout, LayoutDigest);
            _savedLayout = _layout;
        }
        Changed(nameof(IsDirty));
        Changed(nameof(IsLayoutDirty));
        Changed(nameof(DisplayTitle));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name=null) =>
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
}

public enum MarkdownEditorViewMode { Write, Preview, Split }
