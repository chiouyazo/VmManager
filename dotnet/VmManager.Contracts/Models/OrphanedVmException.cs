namespace VmManager.Contracts.Models;

public class OrphanedVmException : Exception
{
    public string VmName { get; }
    public int VmId { get; }

    public OrphanedVmException(string vmName, int vmId, string message, Exception innerException)
        : base(message, innerException)
    {
        VmName = vmName;
        VmId = vmId;
    }
}
