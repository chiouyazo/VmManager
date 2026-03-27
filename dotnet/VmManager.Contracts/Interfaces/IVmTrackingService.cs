using VmManager.Contracts.Models;

namespace VmManager.Contracts.Interfaces;

public interface IVmTrackingService
{
    void TrackVm(string vmName, VmOrigin? origin);
    void UntrackVm(string vmName);
    VmOrigin? GetOrigin(string vmName);
    Dictionary<string, VmOrigin?> LoadAll();
    void PruneStaleEntries(IReadOnlySet<string> existingVmNames);
    Dictionary<string, string> LoadNotes();
    void SaveNote(string vmName, string note);
    void RemoveNote(string vmName);
}
