namespace VmManager.Contracts.Models;

public enum VmShutdownReason
{
    GuestInitiated,
    HostInitiated,
    Crashed,
    Unknown,
}
