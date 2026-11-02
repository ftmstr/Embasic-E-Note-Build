namespace Embasic.ENote.Emd;

public interface IFileKeyProtector
{
    byte[] Protect(ReadOnlySpan<byte> dataKey);
    byte[] Unprotect(ReadOnlySpan<byte> wrappedDataKey);
}
