using System.IO;

namespace Embasic.ENote.Core.Notes;

/// <summary>Recognizes note extensions, without treating encrypted content as plaintext.</summary>
public static class NoteFilePolicy
{
    public static NoteFileFormat Detect(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return NoteFileFormat.Unsupported;
        }

        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".md" => NoteFileFormat.PlainMarkdown,
            ".emd" => NoteFileFormat.EncryptedEmd,
            _ => NoteFileFormat.Unsupported
        };
    }

    /// <summary>Fail closed: .emd cannot enter the plaintext Markdown reader.</summary>
    public static bool CanReadAsPlaintext(string? filePath) =>
        Detect(filePath) == NoteFileFormat.PlainMarkdown;
}

public enum NoteFileFormat
{
    Unsupported = 0,
    PlainMarkdown = 1,
    EncryptedEmd = 2
}
