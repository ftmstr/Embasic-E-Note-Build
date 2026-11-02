using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Embasic.ENote.Core.Printing;

namespace Embasic.ENote.Storage;

/// <summary>
/// Explicitly PLAINTEXT .md-only editor persistence. Optimistic digest
/// checks prevent ordinary accidental overwrite but are NOT cross-process CAS.
/// File name/path are never made available to an advertising integration.
/// </summary>
public sealed class PlainMarkdownDocumentStore
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly ConcurrentDictionary<string, object> Gates = new(StringComparer.OrdinalIgnoreCase);

    public PlainMarkdownSnapshot Read(string path)
    {
        string full = ValidatePath(path);
        byte[] data = File.ReadAllBytes(full);
        string text = StrictUtf8.GetString(data.AsSpan(HasBom(data) ? 3 : 0));
        return new PlainMarkdownSnapshot(text, Convert.ToHexString(SHA256.HashData(data)), HasBom(data));
    }

    public string Save(string path, string content, string? expectedSha256, bool writeBom = false)
    {
        string full = ValidatePath(path);
        ArgumentNullException.ThrowIfNull(content);
        // Strict UTF-8, no .emd fallback; preserve original BOM preference.
        byte[] text = StrictUtf8.GetBytes(content);
        byte[] data = writeBom ? [0xEF, 0xBB, 0xBF, .. text] : text;
        try
        {
            lock (Gates.GetOrAdd(full, _ => new object()))
            {
                CheckSnapshot(full, expectedSha256);
                return WriteAtomically(full, data, expectedSha256, () => CheckSnapshot(full, expectedSha256));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
            CryptographicOperations.ZeroMemory(text);
        }
    }

    internal static string WriteAtomically(string path, byte[] bytes, string? expected,
        Action verifyAgain)
    {
        string temp = Path.Combine(Path.GetDirectoryName(path)!,
            "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".enote-tmp");
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            verifyAgain();
            if (expected is null) File.Move(temp, path, overwrite: false);
            else File.Replace(temp, path, null, ignoreMetadataErrors: false);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static void CheckSnapshot(string full, string? expected)
    {
        if (expected is null)
        {
            if (File.Exists(full)) throw new IOException("Document already exists.");
            return;
        }
        if (expected.Length != 64 || !expected.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected SHA-256 must contain 64 hex characters.");
        if (!File.Exists(full)) throw new IOException("Previously opened document disappeared.");
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Linked document path rejected.");
        byte[] now = File.ReadAllBytes(full);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected), SHA256.HashData(now)))
                throw new IOException("Document externally modified; overwrite refused.");
        }
        finally { CryptographicOperations.ZeroMemory(now); }
    }

    internal static string ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        if (!Path.GetExtension(full).Equals(".md", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Plain editor accepts .md only, never .emd.");
        string directory = Path.GetDirectoryName(full)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
            (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0))
            throw new UnauthorizedAccessException("Reparse-point file or parent rejected.");
        return full;
    }

    private static bool HasBom(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
}

public sealed record PlainMarkdownSnapshot(string Text, string Sha256, bool HasUtf8Bom);

/// <summary>
/// Local, unencrypted sidecar: plain .md page geometry is NOT part of Markdown.
/// Sidecar may reveal the filename; never use for .emd or private note titles.
/// </summary>
public sealed class DocumentPageSettingsStore
{
    private const string Suffix = ".enote-layout.json";
    private static readonly ConcurrentDictionary<string, object> Gates = new(StringComparer.OrdinalIgnoreCase);
    public DocumentPageSnapshot Read(string markdownPath)
    {
        string file = Sidecar(markdownPath);
        if (!File.Exists(file)) return new DocumentPageSnapshot(PageLayoutSettings.Default, null);
        byte[] raw = File.ReadAllBytes(file);
        try
        {
            var payload = JsonSerializer.Deserialize<PagePayload>(raw)
                ?? throw new InvalidDataException("Missing page metadata.");
            if (payload.Version != 1) throw new NotSupportedException("Unknown page metadata version.");
            var paper = payload.PaperId == "Custom"
                ? PageLayoutCatalog.Custom(payload.WidthMm, payload.HeightMm)
                : PageLayoutCatalog.Get(payload.PaperId);
            var settings = PageLayoutSettings.Default with
            {
                Paper = paper,
                Landscape = payload.Landscape,
                ZoomPercent = payload.ZoomPercent
            };
            settings.Validate();
            return new DocumentPageSnapshot(settings, Convert.ToHexString(SHA256.HashData(raw)));
        }
        catch (JsonException ex) { throw new InvalidDataException("Malformed page metadata.", ex); }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }

    public string Save(string markdownPath, PageLayoutSettings settings, string? expectedSha256)
    {
        string sidecar = Sidecar(markdownPath);
        settings.Validate();
        var p = new PagePayload(1, settings.Paper.Id, settings.Paper.WidthMm,
            settings.Paper.HeightMm, settings.Landscape, settings.ZoomPercent);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(p);
        try
        {
            lock (Gates.GetOrAdd(sidecar, _ => new object()))
            {
                PlainMarkdownDocumentStore.CheckSnapshot(sidecar, expectedSha256);
                return PlainMarkdownDocumentStore.WriteAtomically(sidecar, data,
                    expectedSha256, () => PlainMarkdownDocumentStore.CheckSnapshot(sidecar, expectedSha256));
            }
        }
        finally { CryptographicOperations.ZeroMemory(data); }
    }

    private static string Sidecar(string markdownPath) =>
        PlainMarkdownDocumentStore.ValidatePath(markdownPath) + Suffix;

    private sealed record PagePayload(int Version, string PaperId, decimal WidthMm,
        decimal HeightMm, bool Landscape, int ZoomPercent);
}
public sealed record DocumentPageSnapshot(PageLayoutSettings Settings, string? Sha256);
