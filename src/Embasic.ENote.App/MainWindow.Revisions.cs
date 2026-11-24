using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Threading;
using Embasic.ENote.App.ViewModels;
using Embasic.ENote.Storage.Documents;
using Microsoft.Win32;

namespace Embasic.ENote.App;

public partial class MainWindow
{
    private readonly DispatcherTimer _revisionTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _checkingRevision;
    private bool _resolvingRevision;

    private async Task CheckSelectedRevisionAsync()
    {
        if (_checkingRevision || _resolvingRevision || _fileOperationBusy ||
            MainSections.SelectedItem != MarkdownSectionTab || SelectedTab is not { IsInTrash: false } tab)
            return;

        string path = tab.FilePath, digest = tab.Digest;
        string? layout = tab.LayoutDigest;
        _checkingRevision = true;
        try
        {
            var result = await Task.Run(() => PlainMarkdownRevision.Check(path, digest, layout));
            if (!_resolvingRevision && !_fileOperationBusy && ReferenceEquals(SelectedTab, tab) &&
                !tab.IsInTrash && tab.FilePath == path && tab.Digest == digest && tab.LayoutDigest == layout)
                ShowRevisionState(result);
        }
        finally { _checkingRevision = false; }
    }

    private void ShowRevisionState(PlainMarkdownRevisionState state)
    {
        if (RevisionNotice is null) return;
        RevisionNotice.Visibility = state == PlainMarkdownRevisionState.Current ? Visibility.Collapsed : Visibility.Visible;
        ReloadRevisionButton.IsEnabled = state == PlainMarkdownRevisionState.Changed;
        string message = state switch
        {
            PlainMarkdownRevisionState.Changed => "Diskteki not veya sayfa düzeni değişti. Açık taslak korunuyor.",
            PlainMarkdownRevisionState.Missing => "Not artık bu konumda bulunmuyor. Açık taslağı ayrı kaydedebilirsiniz.",
            PlainMarkdownRevisionState.Unavailable => "Not veya sayfa düzeni okunamadı. Açık taslağı ayrı kaydedebilirsiniz.",
            _ => ""
        };
        if (RevisionMessage.Text == message) return;
        RevisionMessage.Text = message;
        UIElementAutomationPeer.FromElement(RevisionMessage)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void ReloadRevision_Click(object sender, RoutedEventArgs e)
    {
        if (_resolvingRevision || _fileOperationBusy || SelectedTab is not { IsInTrash: false } tab) return;
        _resolvingRevision = true;
        try
        {
            bool dirty = tab.IsDirty || tab.IsLayoutDirty;
            if (dirty)
            {
                var answer = MessageBox.Show(this,
                    "Açık taslak ve sayfa düzeni önce ayrı bir Markdown kopyasına kaydedilecek. Ardından bu sekme diskten yeniden yüklenecek.",
                    "Diskten yeniden yükle", MessageBoxButton.OKCancel, MessageBoxImage.Information);
                if (answer != MessageBoxResult.OK || !SaveRevisionCopy(tab)) return;
            }

            var note = _noteStore.Read(tab.FilePath);
            var layout = _pageStore.Read(tab.FilePath);
            if (PlainMarkdownRevision.Check(tab.FilePath, note.Sha256, layout.Sha256) != PlainMarkdownRevisionState.Current)
                throw new IOException("Dosya okunurken yeniden değişti. Lütfen tekrar deneyin.");

            tab.LoadDiskRevision(note, layout);
            // Undo belongs to the previous revision, which is available in the saved copy.
            if (ActiveEditor is { } editor)
            {
                editor.IsUndoEnabled = false;
                editor.IsUndoEnabled = true;
            }
            tab.RefreshPreview();
            SyncPageControls(tab);
            ShowRevisionState(PlainMarkdownRevisionState.Current);
            MarkdownStatusText.Text = dirty ? "Taslak ayrı kaydedildi ve not diskten yeniden yüklendi." : "Not diskten yeniden yüklendi.";
        }
        catch (Exception error) when (IsRevisionError(error)) { ShowRevisionError(error); }
        finally { _resolvingRevision = false; }
    }

    private void SaveRevisionCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_resolvingRevision || _fileOperationBusy || SelectedTab is not { IsInTrash: false } tab) return;
        _resolvingRevision = true;
        try
        {
            if (SaveRevisionCopy(tab))
                MarkdownStatusText.Text = "Taslak ve sayfa düzeni ayrı kaydedildi. Bu sekme açık kaldı.";
        }
        catch (Exception error) when (IsRevisionError(error)) { ShowRevisionError(error); }
        finally { _resolvingRevision = false; }
    }

    private bool SaveRevisionCopy(PlainMarkdownEditorTab tab)
    {
        string? sourceDirectory = Path.GetDirectoryName(tab.FilePath);
        var dialog = new SaveFileDialog
        {
            Title = "Taslağı ayrı Markdown dosyasına kaydet",
            Filter = "Markdown (.md)|*.md",
            FileName = Path.GetFileNameWithoutExtension(tab.FilePath) + "-taslak",
            InitialDirectory = Directory.Exists(sourceDirectory) ? sourceDirectory! : "",
            DefaultExt = ".md",
            AddExtension = true,
            OverwritePrompt = false,
            CheckPathExists = true
        };
        if (dialog.ShowDialog(this) != true) return false;
        string destination = Path.GetFullPath(dialog.FileName);
        if (_tabs.Any(open => string.Equals(open.FilePath, destination, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Bu hedef açık bir sekmeye ait. Farklı bir dosya adı seçin.");

        PlainMarkdownRevision.WriteDraftCopy(tab.FilePath, destination, tab.Text, tab.Layout, tab.HasUtf8Bom);
        return true;
    }

    private static bool IsRevisionError(Exception error) =>
        error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private void ShowRevisionError(Exception error) =>
        MarkdownStatusText.Text = "Açık taslak korundu. " + error.Message;
}
