using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Embasic.ENote.Storage;

/// <summary>
/// Experimental manifest storage for an existing, protected local directory.
/// Publication uses a same-directory, no-replace operation. Physical power-loss
/// durability and hostile path mutation are outside the accepted scope.
/// </summary>
public sealed class VaultGenerationDiskStore : IVaultGenerationStore
{
    private readonly string _directory;

    public VaultGenerationDiskStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Path.IsPathFullyQualified(directory))
            throw new ArgumentException("An absolute generation directory is required.", nameof(directory));
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Generation disk staging supports Windows and Linux.");

        _directory = Path.GetFullPath(directory);
        CheckDirectory();
    }

    public VaultGenerationManifest? Read(VaultGenerationStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);
        CheckDirectory();
        string path = PathFor(stamp);
        FileStream file;
        try
        {
            if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("The generation path must not be a directory or reparse point.");
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        using (file) return VaultGenerationManifest.ReadVerified(file, stamp);
    }

    public void Stage(VaultGenerationManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var stamp = manifest.Stamp;
        if (Read(stamp) is not null) return;

        string target = PathFor(stamp);
        string temporary = Path.Combine(_directory, $".{Guid.NewGuid():N}.egm-stage");
        bool ownsTemporary = false;
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                ownsTemporary = true;
                manifest.WriteCanonical(file);
                file.Flush(flushToDisk: true);
            }

            CheckDirectory();
            try
            {
                Publish(temporary, target);
                // Windows renames the source, Linux publishes a hard link.
                ownsTemporary = !OperatingSystem.IsWindows();
            }
            catch (IOException)
            {
                if (Read(stamp) is null) throw;
                return; // A concurrent writer already published the exact generation.
            }

            if (Read(stamp) is null)
                throw new IOException("Published generation is missing.");
        }
        finally
        {
            if (ownsTemporary) File.Delete(temporary);
        }
    }

    private string PathFor(VaultGenerationStamp stamp) => Path.Combine(_directory,
        $"{stamp.WorkspaceId:N}-{stamp.Epoch.ToString(CultureInfo.InvariantCulture)}-{stamp.ManifestSha256}.egm");

    private void CheckDirectory()
    {
        const FileAttributes relevant = FileAttributes.Directory | FileAttributes.ReparsePoint;
        if ((File.GetAttributes(_directory) & relevant) != FileAttributes.Directory)
            throw new IOException("The generation directory must exist and must not be a reparse point.");
    }

    private static void Publish(string temporary, string target)
    {
        // Do not request cross-volume copying or replacement on Windows.
        // Linux link(2) either adds a complete file name or fails if it exists.
        bool published = OperatingSystem.IsWindows()
            ? MoveFileExW(WindowsPath(temporary), WindowsPath(target), flags: 0)
            : Link(temporary, target) == 0;
        if (!published)
            throw new IOException("Unable to publish the staged generation.", new Win32Exception(Marshal.GetLastPInvokeError()));
    }

    private static string WindowsPath(string path)
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
