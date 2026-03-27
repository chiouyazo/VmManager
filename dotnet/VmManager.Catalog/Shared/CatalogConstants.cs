namespace VmManager.Catalog.Shared;

public static class CatalogConstants
{
    public const double BytesPerGb = 1024.0 * 1024.0 * 1024.0;
    public const string SnapshotsDirName = "snapshots";
    public const string ManifestFileName = "manifest.json";
    public const string BoxFileExtension = ".box";
    public const string TarGzExtension = ".tar.gz";
    public const string SnapshotArchiveName = "snapshot.tar.gz";
    public const string ReservedSnapshotName = "Base";
}
