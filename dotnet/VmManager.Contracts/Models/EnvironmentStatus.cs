using System.Text.Json.Serialization;

namespace VmManager.Contracts.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EnvironmentStatus
{
    Provisioning,
    Ready,
    Failed,
    Expiring,
}
