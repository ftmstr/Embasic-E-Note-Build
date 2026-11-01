using System.Text;
using Embasic.ENote.Core.Notes;

namespace Embasic.ENote.Storage;

/// <summary>
/// Strictly limited to .md files. Encrypted .emd reading requires the future
/// authenticated decryption and key-policy services; never fall back to text.
/// </summary>
public sealed class PlainMarkdownReader
{
    public async Task<string> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!NoteFilePolicy.CanReadAsPlaintext(filePath))
        {
            throw new NotSupportedException("Only explicitly unencrypted .md files may be read as text.");
        }

        return await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
    }
}
