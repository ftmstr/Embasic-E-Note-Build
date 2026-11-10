using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

/// <summary>Wraps the role secret to an explicitly provisioned Windows software KSP recipient.</summary>
[SupportedOSPlatform("windows")]
public static class VaultWindowsRoleCustody
{
    public static byte[] Wrap(ReadOnlySpan<byte> recipientPublicKey, ReadOnlySpan<byte> role)
    {
        if (role.Length != 32) throw new ArgumentException("Vault role secrets are 32 bytes.", nameof(role));
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(recipientPublicKey, out int consumed);
        if (consumed != recipientPublicKey.Length || rsa.KeySize is < 2048 or > 4096)
            throw new CryptographicException("Invalid recipient public key.");
        byte[] plaintext = role.ToArray();
        try { return rsa.Encrypt(plaintext, RSAEncryptionPadding.OaepSHA256); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public static byte[] Unwrap(string recipientKeyName, string envelopePath)
    {
        using var key = CngKey.Open(recipientKeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider,
            CngKeyOpenOptions.MachineKey | CngKeyOpenOptions.Silent);
        if (key.AlgorithmGroup != CngAlgorithmGroup.Rsa || key.KeySize is < 2048 or > 4096 ||
            key.ExportPolicy != CngExportPolicies.None || key.KeyUsage != CngKeyUsages.Decryption)
            throw new CryptographicException("The role recipient key is not a non-exportable RSA decryption key.");
        var files = new VaultAtomicFileDirectory(Path.GetDirectoryName(Path.GetFullPath(envelopePath))!);
        using var file = files.OpenRead(Path.GetFileName(envelopePath)) ?? throw new FileNotFoundException("Role envelope is unavailable.");
        byte[] ciphertext = new byte[key.KeySize / 8];
        if (file.Length != ciphertext.Length) throw new CryptographicException("Unexpected role envelope length.");
        file.ReadExactly(ciphertext);
        if (file.ReadByte() != -1) throw new CryptographicException("Role envelope grew during read.");
        using var rsa = new RSACng(key);
        byte[] role = rsa.Decrypt(ciphertext, RSAEncryptionPadding.OaepSHA256);
        if (role.Length == 32) return role;
        CryptographicOperations.ZeroMemory(role);
        throw new CryptographicException("Unexpected role secret length.");
    }
}
