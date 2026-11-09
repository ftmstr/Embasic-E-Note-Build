using System.Globalization;

namespace Embasic.ENote.Storage;

/// <summary>
/// Experimental manifest storage for an existing, protected local directory.
/// Physical power-loss durability and hostile path mutation are outside scope.
/// </summary>
public sealed class VaultGenerationDiskStore : IVaultGenerationStore
{
    private readonly VaultAtomicFileDirectory _files;

    public VaultGenerationDiskStore(string directory) => _files = new VaultAtomicFileDirectory(directory);

    public VaultGenerationManifest? Read(VaultGenerationStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);
        using var file = _files.OpenRead(NameFor(stamp));
        return file is null ? null : VaultGenerationManifest.ReadVerified(file, stamp);
    }

    public void Stage(VaultGenerationManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var stamp = manifest.Stamp;
        _files.Publish(NameFor(stamp), manifest.WriteCanonical, () => Read(stamp) is not null);
    }

    private static string NameFor(VaultGenerationStamp stamp) =>
        $"{stamp.WorkspaceId:N}-{stamp.Epoch.ToString(CultureInfo.InvariantCulture)}-{stamp.ManifestSha256}.egm";
}
