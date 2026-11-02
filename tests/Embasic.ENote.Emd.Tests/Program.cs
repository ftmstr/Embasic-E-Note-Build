using System.IO;
using System.Security.Cryptography;
using System.Text;
using Embasic.ENote.Emd;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("DPAPI tests must run on Windows.");
    return 2;
}

var dpapi = new WindowsDpapiKeyProtector();
const string secret = "# Şifreli not 🌿\nTürkçe, 日本語 ve Unicode";
int passed = 0;
int failed = 0;

void Check(string name, Action assertion)
{
    try { assertion(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {error.GetType().Name} {error.Message}");
    }
}

static void Require(bool condition)
{
    if (!condition) throw new Exception("Assertion failed.");
}
static void Expect<T>(Action attempt) where T : Exception
{
    try { attempt(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}

var initial = EmdDraftCodec.Encrypt(secret, dpapi, createRecoveryKey: true);
byte[] bytes = initial.Envelope;
byte[] recovery = initial.RecoveryKey!;
try
{
    Check("Windows CurrentUser DPAPI roundtrip",
        () => Require(EmdDraftCodec.Decrypt(bytes, dpapi) == secret));
    Check("disk envelope does not contain UTF-8 plaintext",
        () => Require(!Convert.ToHexString(bytes).Contains(
            Convert.ToHexString(Encoding.UTF8.GetBytes(secret)))));
    Check("recovery works with unavailable Windows user key",
        () => Require(EmdDraftCodec.Decrypt(bytes, new DeniedProtector(), recovery) == secret));
    Check("wrong recovery key fails authentication", () =>
    {
        var wrong = RandomNumberGenerator.GetBytes(32);
        try { Expect<CryptographicException>(() => EmdDraftCodec.Decrypt(bytes, dpapi, wrong)); }
        finally { CryptographicOperations.ZeroMemory(wrong); }
    });
    Check("missing DPAPI key cannot open envelope",
        () => Expect<UnauthorizedAccessException>(() => EmdDraftCodec.Decrypt(bytes, new DeniedProtector())));
    Check("ciphertext tamper detected", () =>
    {
        var changed = bytes.ToArray();
        changed[^17] ^= 1;
        Expect<CryptographicException>(() => EmdDraftCodec.Decrypt(changed, dpapi));
    });
    Check("authentication tag tamper detected", () =>
    {
        var changed = bytes.ToArray();
        changed[^1] ^= 1;
        Expect<CryptographicException>(() => EmdDraftCodec.Decrypt(changed, dpapi));
    });
    Check("authenticated header tamper detected", () =>
    {
        var changed = bytes.ToArray();
        changed[7] ^= 1;
        Expect<CryptographicException>(() => EmdDraftCodec.Decrypt(changed, dpapi));
    });
    Check("truncated container rejected", () =>
        Expect<InvalidDataException>(() => EmdDraftCodec.Decrypt(bytes[..^3], dpapi)));
    Check("unsupported version rejected", () =>
    {
        var changed = bytes.ToArray();
        changed[4] = 127;
        Expect<NotSupportedException>(() => EmdDraftCodec.Decrypt(changed, dpapi));
    });
    Check("fresh encryption yields distinct randomized envelopes", () =>
    {
        var other = EmdDraftCodec.Encrypt(secret, dpapi);
        Require(!bytes.SequenceEqual(other.Envelope));
        Require(EmdDraftCodec.Decrypt(other.Envelope, dpapi) == secret);
    });
    Check("absent recovery slot cannot be bypassed", () =>
    {
        var noRecovery = EmdDraftCodec.Encrypt(secret, dpapi);
        Expect<CryptographicException>(() => EmdDraftCodec.Decrypt(noRecovery.Envelope, dpapi, recovery));
    });
    Check("empty document roundtrip",
        () => Require(EmdDraftCodec.Decrypt(EmdDraftCodec.Encrypt("", dpapi).Envelope, dpapi) == ""));
    Check("short recovery key rejected",
        () => Expect<CryptographicException>(() => EmdDraftCodec.Decrypt(bytes, dpapi, new byte[8])));
    Check("recovery secret is optional",
        () => Require(EmdDraftCodec.Encrypt(secret, dpapi).RecoveryKey is null));
}
finally { CryptographicOperations.ZeroMemory(recovery); }
Console.WriteLine($"{passed}/{passed + failed} EMD draft checks passed.");
return failed == 0 ? 0 : 1;

sealed class DeniedProtector : IFileKeyProtector
{
    public byte[] Protect(ReadOnlySpan<byte> key) => throw new UnauthorizedAccessException();
    public byte[] Unprotect(ReadOnlySpan<byte> wrapped) => throw new UnauthorizedAccessException();
}
