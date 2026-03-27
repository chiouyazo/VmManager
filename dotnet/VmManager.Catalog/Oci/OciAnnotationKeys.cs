namespace VmManager.Catalog.Oci;

/// <summary>
/// Constants for OCI manifest annotation keys used when reading and writing image metadata.
/// </summary>
public static class OciAnnotationKeys
{
    public const string Title = "org.opencontainers.image.title";
    public const string Description = "org.opencontainers.image.description";
    public const string Created = "org.opencontainers.image.created";
    public const string Version = "org.opencontainers.image.version";
    public const string Features = "dev.vmmanager.features";
    public const string ImageType = "dev.vmmanager.imagetype";
    public const string Snapshot = "dev.vmmanager.snapshot";
    public const string SnapshotName = "dev.vmmanager.snapshot-name";
    public const string PushedBy = "dev.vmmanager.pushed-by";
    public const string ParentImageId = "dev.vmmanager.parent-image-id";
    public const string ParentImageName = "dev.vmmanager.parent-image-name";
    public const string ParentVersion = "dev.vmmanager.parent-version";
}
