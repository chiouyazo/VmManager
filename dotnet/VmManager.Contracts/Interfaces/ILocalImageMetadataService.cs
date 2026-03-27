using VmManager.Contracts.Models;

namespace VmManager.Contracts.Interfaces;

public interface ILocalImageMetadataService
{
    void SaveMetadata(string extractDir, VmImageVersion version);
    LocalImageMetadata? LoadMetadata(string dir);
}
