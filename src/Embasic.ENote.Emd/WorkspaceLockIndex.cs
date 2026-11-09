using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Embasic.ENote.Emd;

/// <summary>Experimental encrypted, ID-based workspace ancestry index.
/// This is NOT path-handle sandboxing, user setup, or a production key manager.
/// Entries are encrypted with the independently unlocked root folder factor.</summary>
public static class WorkspaceLockIndex
{
    private const int HeaderSize = 4 + 1 + 16 + 16 + 12 + 4;
    private const int MaxCiphertext = 16 * 1024 * 1024;
    internal const int MaximumEncodedBytes = HeaderSize + MaxCiphertext + 16;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] Protect(Guid workspaceId, LayerSecret rootFactor,
        IReadOnlyList<WorkspaceLockEntry> entries)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("Missing workspace ID.");
        if (rootFactor.Kind != LayerKind.Folder) throw new ArgumentException("Root factor required.");
        Validate(entries);
        if (!entries.Any(x => x.Id == rootFactor.Id && x.ParentId == Guid.Empty && x.Kind == LayerKind.Folder))
            throw new InvalidDataException("Workspace root ID must match its unlocked factor.");
        using var plainStream = new MemoryStream();
        using (var writer = new BinaryWriter(plainStream, StrictUtf8, leaveOpen: true))
        {
            writer.Write(entries.Count);
            foreach (var entry in entries)
            {
                writer.Write(entry.Id.ToByteArray());
                writer.Write(entry.ParentId.ToByteArray());
                writer.Write((byte)entry.Kind);
                byte[] name = StrictUtf8.GetBytes(entry.RelativePath);
                writer.Write(name.Length);
                writer.Write(name);
            }
        }
        byte[] plain = plainStream.ToArray();
        byte[] key = rootFactor.CopySecret();
        try
        {
            if (plain.Length > MaxCiphertext) throw new InvalidDataException("Index too large for draft.");
            byte[] header = new byte[HeaderSize];
            "ELH1"u8.CopyTo(header);
            header[4] = 1;
            workspaceId.TryWriteBytes(header.AsSpan(5, 16));
            rootFactor.Id.TryWriteBytes(header.AsSpan(21, 16));
            RandomNumberGenerator.Fill(header.AsSpan(37, 12));
            BitConverter.TryWriteBytes(header.AsSpan(49, 4), plain.Length);
            byte[] encrypted = new byte[plain.Length];
            byte[] tag = new byte[16];
            using (var aead = new AesGcm(key, 16))
                aead.Encrypt(header.AsSpan(37, 12), plain, encrypted, tag, header);
            return [.. header, .. encrypted, .. tag];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static byte[] RekeyRoot(ReadOnlySpan<byte> envelope, Guid workspaceId,
        LayerSecret current, LayerSecret replacement)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(replacement);
        if (current.Id != replacement.Id || current.Kind != LayerKind.Folder ||
            replacement.Kind != LayerKind.Folder || current.HasSameSecret(replacement))
            throw new ArgumentException("A fresh factor with the same root identity is required.", nameof(replacement));

        return Protect(workspaceId, replacement, Open(envelope, workspaceId, current));
    }

    public static IReadOnlyList<WorkspaceLockEntry> Open(ReadOnlySpan<byte> envelope,
        Guid expectedWorkspace, LayerSecret rootFactor)
    {
        if (envelope.Length < HeaderSize + 16 ||
            envelope.Length > MaximumEncodedBytes ||
            !envelope[..4].SequenceEqual("ELH1"u8))
            throw new InvalidDataException("Unknown workspace hierarchy index.");
        if (envelope[4] != 1) throw new NotSupportedException("Unknown hierarchy schema.");
        if (new Guid(envelope.Slice(5, 16)) != expectedWorkspace ||
            new Guid(envelope.Slice(21, 16)) != rootFactor.Id ||
            rootFactor.Kind != LayerKind.Folder)
            throw new CryptographicException("Workspace root identity mismatch.");
        int length = BitConverter.ToInt32(envelope.Slice(49, 4));
        if (length < 0 || length > MaxCiphertext || envelope.Length != HeaderSize + length + 16)
            throw new InvalidDataException("Invalid encrypted index length.");
        byte[] key = rootFactor.CopySecret();
        byte[] clear = new byte[length];
        try
        {
            using (var aead = new AesGcm(key, 16))
                aead.Decrypt(envelope.Slice(37, 12), envelope.Slice(HeaderSize, length),
                    envelope.Slice(HeaderSize + length, 16), clear, envelope[..HeaderSize]);
            using var stream = new MemoryStream(clear, writable: false);
            using var reader = new BinaryReader(stream, StrictUtf8);
            int count = reader.ReadInt32();
            if (count < 0 || count > clear.Length / 37)
                throw new InvalidDataException("Invalid hierarchy entry count.");
            var entries = new List<WorkspaceLockEntry>(count);
            for (int i = 0; i < count; i++)
            {
                Guid id = new(reader.ReadBytes(16));
                Guid parent = new(reader.ReadBytes(16));
                LayerKind kind = (LayerKind)reader.ReadByte();
                int n = reader.ReadInt32();
                if (n <= 0 || n > 4096 || stream.Length - stream.Position < n)
                    throw new InvalidDataException("Invalid hierarchy path length.");
                string path = StrictUtf8.GetString(reader.ReadBytes(n));
                entries.Add(new WorkspaceLockEntry(id, parent, kind, path));
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Trailing hierarchy data.");
            Validate(entries);
            if (!entries.Any(x => x.Id == rootFactor.Id && x.ParentId == Guid.Empty && x.Kind == LayerKind.Folder))
                throw new InvalidDataException("Workspace root ID does not match encrypted metadata.");
            return entries.AsReadOnly();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Obtain the required ordered factor IDs. Callers must separately
    /// unlock each ELV1 vault and pass all factors to LayeredEmd3.Open.</summary>
    public static IReadOnlyList<Guid> RequiredFactors(IReadOnlyList<WorkspaceLockEntry> entries,
        string relativePath)
    {
        Validate(entries);
        string normalized = Normalize(relativePath);
        var target = entries.SingleOrDefault(x => x.RelativePath.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException("No authorized node for path.");
        var byId = entries.ToDictionary(x => x.Id);
        var result = new List<Guid>();
        var visited = new HashSet<Guid>();
        var cursor = target;
        while (true)
        {
            if (!visited.Add(cursor.Id)) throw new InvalidDataException("Cyclic workspace hierarchy.");
            result.Add(cursor.Id);
            if (cursor.ParentId == Guid.Empty) break;
            cursor = byId[cursor.ParentId];
        }
        result.Reverse();
        return result;
    }

    private static void Validate(IReadOnlyList<WorkspaceLockEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<Guid, WorkspaceLockEntry>();
        foreach (var entry in entries)
        {
            if (entry.Id == Guid.Empty || !byId.TryAdd(entry.Id, entry) ||
                !paths.Add(entry.RelativePath) ||
                Normalize(entry.RelativePath) != entry.RelativePath ||
                entry.Kind is not (LayerKind.Folder or LayerKind.Subfolder or LayerKind.NoteAdditional))
                throw new InvalidDataException("Duplicate or invalid locked workspace node.");
            if (entry.Kind == LayerKind.NoteAdditional &&
                !entry.RelativePath.EndsWith(".emd", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Additional note locks require .emd.");
        }
        foreach (var entry in entries)
        {
            if (entry.ParentId == Guid.Empty)
            {
                if (entry.Kind != LayerKind.Folder)
                    throw new InvalidDataException("Hierarchy root must be a folder.");
                continue;
            }
            if (!byId.TryGetValue(entry.ParentId, out var parent) ||
                parent.Kind == LayerKind.NoteAdditional ||
                !entry.RelativePath.StartsWith(parent.RelativePath + "/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Missing parent or path ancestry mismatch.");
            if (entry.Kind == LayerKind.Folder)
                throw new InvalidDataException("Child lock must have subfolder/notelock kind.");
        }
    }

    private static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.EndsWith('/') ||
            normalized.Contains(':') ||
            normalized.Split('/').Any(x => x is "" or "." or ".."))
            throw new InvalidDataException("Absolute or traversing workspace-relative path.");
        return normalized;
    }
}

public sealed record WorkspaceLockEntry(Guid Id, Guid ParentId,
    LayerKind Kind, string RelativePath);
