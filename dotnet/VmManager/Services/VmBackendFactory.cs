namespace VmManager.Services;

/// <summary>
/// Resolves the correct IVmBackend implementation based on image type or backend name.
/// "Windows" images use HyperVService, "Linux" images use DockerService.
/// </summary>
public class VmBackendFactory
{
    private readonly HyperVService _hyperVService;
    private readonly DockerService _dockerService;

    public VmBackendFactory(HyperVService hyperVService, DockerService dockerService)
    {
        _hyperVService = hyperVService;
        _dockerService = dockerService;
    }

    /// <summary>Returns the backend for the given image type ("Windows" or "Linux").</summary>
    public IVmBackend GetBackend(string imageType) =>
        imageType.Equals("Linux", StringComparison.OrdinalIgnoreCase)
            ? _dockerService
            : _hyperVService;

    /// <summary>Returns the backend by backend name ("HyperV" or "Docker").</summary>
    public IVmBackend GetBackendByName(string backendName) =>
        backendName.Equals("Docker", StringComparison.OrdinalIgnoreCase)
            ? _dockerService
            : _hyperVService;

    public HyperVService HyperV => _hyperVService;
    public DockerService Docker => _dockerService;
}
