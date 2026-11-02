using System.Security.Cryptography;

namespace Embasic.ENote.Emd;

/// <summary>
/// Experimental explicit migration/rotation. Always re-encrypts the entire
/// payload with a NEW DEK, salt and nonces. Does not update any disk files.
/// The returned recovery secret replaces the old one; callers must back it up.
/// </summary>
public static partial class PortableEmd
{
    public static PortableEmdResult RotatePassword(
        ReadOnlySpan<byte> originalEmd2, string nextPassword,
        string? currentPassword = null, byte[]? currentRecoverySecret = null,
        IFileKeyProtector? currentWindowsProtector = null,
        IFileKeyProtector? nextWindowsProtector = null)
    {
        string authenticatedMarkdown = Decrypt(originalEmd2,
            password: currentPassword, recoverySecret: currentRecoverySecret,
            windowsKeyProtector: currentWindowsProtector);
        return Encrypt(authenticatedMarkdown, nextPassword,
            createRecoveryKey: true, windowsKeyProtector: nextWindowsProtector);
    }

    /// <summary>
    /// Explicit EMD1-to-EMD2 draft conversion. The original is never mutated.
    /// Existing EMD1 must authenticate before creating a new EMD2 envelope.
    /// </summary>
    public static PortableEmdResult MigrateDraftV1(
        ReadOnlySpan<byte> legacyEmd1, IFileKeyProtector oldWindowsProtector,
        string nextPassword, byte[]? oldRecoverySecret = null,
        IFileKeyProtector? nextWindowsProtector = null)
    {
        ArgumentNullException.ThrowIfNull(oldWindowsProtector);
        string authenticatedMarkdown = EmdDraftCodec.Decrypt(legacyEmd1,
            oldWindowsProtector, recoveryKey: oldRecoverySecret);
        return Encrypt(authenticatedMarkdown, nextPassword,
            createRecoveryKey: true, windowsKeyProtector: nextWindowsProtector);
    }
}
