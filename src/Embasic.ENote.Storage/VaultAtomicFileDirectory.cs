using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Embasic.ENote.Storage;

/// <summary>Same-directory, no-replace publication for the vault's local files.</summary>
internal sealed class VaultAtomicFileDirectory
{
    private readonly string _directory;

    internal VaultAtomicFileDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Path.IsPathFullyQualified(directory))
            throw new ArgumentException("An absolute vault data directory is required.", nameof(directory));
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Vault disk staging supports Windows and Linux.");

        _directory = Path.GetFullPath(directory);
        CheckDirectory();
    }

    internal FileStream? OpenRead(string name)
    {
        CheckDirectory();
        string path = Path.Combine(_directory, name);
        try
        {
            if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("The vault data file must not be a directory or reparse point.");
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    internal void Publish(string name, Action<Stream> write, Func<bool> verifyExisting)
    {
        if (verifyExisting()) return;
        string target = Path.Combine(_directory, name);
        string temporary = Path.Combine(_directory, $".{Guid.NewGuid():N}{Path.GetExtension(name)}-stage");
        bool ownsTemporary = false;
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                ownsTemporary = true;
                write(file);
                file.Flush(flushToDisk: true);
            }

            CheckDirectory();
            try
            {
                PublishName(temporary, target);
                ownsTemporary = !OperatingSystem.IsWindows();
            }
            catch (IOException)
            {
                if (!verifyExisting()) throw;
                return;
            }

            if (!verifyExisting())
                throw new IOException("Published vault data is missing.");
        }
        finally
        {
            if (ownsTemporary) File.Delete(temporary);
        }
    }

    internal FileStream OpenExclusiveLock(string name)
    {
        CheckDirectory();
        string path = Path.Combine(_directory, name);
        RejectLink(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    internal bool DeleteFile(string name)
    {
        CheckDirectory();
        string path = Path.Combine(_directory, name);
        if (!RejectLink(path)) return false;
        File.Delete(path);
        return true;
    }

    private static bool RejectLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("Vault coordination file must be an ordinary file.");
            return true;
        }
        catch (FileNotFoundException) { return false; }
    }

    private void CheckDirectory()
    {
        const FileAttributes relevant = FileAttributes.Directory | FileAttributes.ReparsePoint;
        if ((File.GetAttributes(_directory) & relevant) != FileAttributes.Directory)
            throw new IOException("The generation directory must exist and must not be a reparse point.");
    }

    private static void PublishName(string temporary, string target)
    {
        // Do not request cross-volume copying or replacement on Windows.
        // Linux link(2) either adds a complete file name or fails if it exists.
        bool published = OperatingSystem.IsWindows()
            ? MoveFileExW(WindowsPath(temporary), WindowsPath(target), flags: 0)
            : Link(temporary, target) == 0;
        if (!published)
            throw new IOException("Unable to publish the staged generation.", new Win32Exception(Marshal.GetLastPInvokeError()));
    }

    internal static string WindowsPath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileExW(string existing, string target, uint flags);

    [DllImport("libc", EntryPoint = "link", ExactSpelling = true, SetLastError = true)]
    private static extern int Link([MarshalAs(UnmanagedType.LPUTF8Str)] string existing,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string target);
}
