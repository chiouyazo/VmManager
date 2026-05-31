namespace VmManager.Agent.Services.Rdp;

public sealed class ClientAuthResult
{
    public required string Username { get; init; }
    public required string Domain { get; init; }
    public required byte[] NtProofStr { get; init; }
    public byte[]? EncryptedRandomSessionKey { get; init; }
    public byte[] ExportedSessionKey { get; set; } = Array.Empty<byte>();
    public byte[]? ClientNonce { get; set; }
    public string? SniHostname { get; set; }
}
