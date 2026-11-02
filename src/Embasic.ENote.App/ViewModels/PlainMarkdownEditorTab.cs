using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Embasic.ENote.Core.Printing;
using Embasic.ENote.Storage;

namespace Embasic.ENote.App.ViewModels;

/// <summary>Temporary plaintext-only WPF tab model. This does not unlock .emd.</summary>
public sealed class PlainMarkdownEditorTab : INotifyPropertyChanged
{
    private string _text;
    private string _savedText;
    private PageLayoutSettings _layout;
    private PageLayoutSettings _savedLayout;
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
