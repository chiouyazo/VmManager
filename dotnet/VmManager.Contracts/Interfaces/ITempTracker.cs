namespace VmManager.Contracts.Interfaces;

public interface ITempTracker
{
    string CreateTrackedTempDir(string prefix);
    void Register(string path);
    void Unregister(string path);
}
