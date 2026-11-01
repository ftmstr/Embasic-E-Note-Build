using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Embasic.ENote.Storage.Workspaces;

namespace Embasic.ENote.App.ViewModels;

/// <summary>
/// View model for workspace browsing and folders only. No note editor,
/// plaintext .emd reading, hidden auto-save or key access is implemented.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IWorkspaceStorage _storage;
    private readonly Func<string?> _chooseWorkspace;
    private readonly Func<string?> _askFolderName;
    private WorkspaceRoot? _workspace;
    private WorkspaceTreeNode? _selectedNode;
    private string _workspaceLabel = "Çalışma alanı seçilmedi";
    private string _statusText = "TUR 02 · Klasör seçerek başlayın · .emd açma/kaydetme devre dışı";
    private string _selectionDescription = "Henüz dosya veya klasör seçilmedi.";

    public MainViewModel(
        IWorkspaceStorage storage,
        Func<string?> chooseWorkspace,
        Func<string?> askFolderName)
    {
        _storage = storage;
        _chooseWorkspace = chooseWorkspace;
        _askFolderName = askFolderName;
        ChooseWorkspaceCommand = new RelayCommand(ChooseWorkspace);
        CreateFolderCommand = new RelayCommand(() => _ = CreateFolderAsync(), () => _workspace is not null);
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => _workspace is not null);
    }

    public ObservableCollection<WorkspaceTreeNode> Roots { get; } = new();
    public ICommand ChooseWorkspaceCommand { get; }
    public ICommand CreateFolderCommand { get; }
    public ICommand RefreshCommand { get; }
    public string WorkspaceLabel { get => _workspaceLabel; private set => SetField(ref _workspaceLabel, value); }
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public string SelectionDescription
    {
        get => _selectionDescription;
        private set => SetField(ref _selectionDescription, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void ChooseWorkspace()
    {
        var chosen = _chooseWorkspace();
        if (string.IsNullOrWhiteSpace(chosen)) return;
        try
        {
            var workspace = _storage.OpenExisting(chosen);
            _workspace = workspace;
            _selectedNode = null;
            Roots.Clear();
            var name = Path.GetFileName(workspace.FullPath);
            if (string.IsNullOrEmpty(name)) name = workspace.FullPath;
            Roots.Add(new WorkspaceTreeNode(
                name, workspace.FullPath, WorkspaceEntryKind.Folder,
                _storage, workspace, message => StatusText = message));
            WorkspaceLabel = workspace.FullPath;
            SelectionDescription = "Bir klasörü genişletin veya öğe seçin. Not düzenleme henüz etkin değil.";
            StatusText = "Çalışma alanı açıldı. Klasörler genişletildikçe yüklenir.";
            CommandManager.InvalidateRequerySuggested();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            StatusText = $"Çalışma alanı açılamadı: {error.Message}";
        }
    }

    public void SelectNode(WorkspaceTreeNode? node)
    {
        if (node is null || node.IsPlaceholder) return;
        _selectedNode = node;
        SelectionDescription = node.Kind switch
        {
            WorkspaceEntryKind.Folder => $"Klasör: {node.FullPath}",
            WorkspaceEntryKind.PlainMarkdown => $"Standart .md (şifresiz). Dosya: {node.FullPath}. Düzenleme sonraki turda.",
            WorkspaceEntryKind.EncryptedEmd => "Şifreli .emd: güvenli açma henüz uygulanmadı. İçerik okunmuyor.",
            _ => "Seçim yok"
        };
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task RefreshAsync()
    {
        var root = Roots.FirstOrDefault();
        if (root is null) return;
        var folder = _selectedNode?.IsFolder == true ? _selectedNode : root;
        await folder.RefreshAsync();
    }

    private async Task CreateFolderAsync()
    {
        if (_workspace is null) return;
        var folderName = _askFolderName();
        if (folderName is null) return;
        var root = Roots.FirstOrDefault();
        var parent = _selectedNode?.IsFolder == true ? _selectedNode : root;
        if (parent is null) return;
        try
        {
            var createdPath = _storage.CreateFolder(_workspace, parent.FullPath, folderName);
            await parent.RefreshAsync();
            StatusText = $"Klasör oluşturuldu: {createdPath}";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            StatusText = $"Klasör oluşturulamadı: {error.Message}";
        }
    }
}
