namespace Embasic.ENote.App.ViewModels;

/// <summary>
/// Presentation model only. No plaintext file access or cryptographic state
/// is exposed to the UI layer at this bootstrap stage.
/// </summary>
public sealed class MainViewModel
{
    public string StatusText =>
        "TUR 01 · WPF iskeleti · .emd işlemleri devre dışı · Windows GUI kabulü bekleniyor";
}
