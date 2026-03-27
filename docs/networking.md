# Network Management

VmManager can automatically create and manage Hyper-V virtual switches based on network definitions stored in the Nexus feed. Images reference networks by ID, and the agent ensures the correct switches exist before VM creation.

## Overview

- Network definitions live in `networks.json` at the Nexus repo root (single source of truth)
- Image manifests reference networks by ID and specify per-VM adapter settings
- The agent creates switches with the `VmMgr-` prefix to distinguish them from user-created switches
- Reference counting tracks which VMs use which switches
- Unused switches are automatically cleaned up (configurable)

## networks.json Schema

Place this file at the root of your Nexus raw repository:

```json
{
  "version": 1,
  "networks": [
    {
      "id": "internal-nat",
      "name": "Internal NAT Network",
      "switchType": "NAT",
      "natSubnet": "192.168.100.0/24",
      "natGateway": "192.168.100.1",
      "dhcpRangeStart": "192.168.100.100",
      "dhcpRangeEnd": "192.168.100.200"
    },
    {
      "id": "corp-lan",
      "name": "Corporate LAN",
      "switchType": "External",
      "physicalAdapter": "auto",
      "allowManagementOs": true
    },
    {
      "id": "isolated",
      "name": "Isolated Network",
      "switchType": "Private"
    },
    {
      "id": "dev-internal",
      "name": "Dev Internal",
      "switchType": "Internal"
    }
  ]
}
```

### Network Definition Fields

| Field | Required | Description |
|---|---|---|
| `id` | Yes | Unique identifier referenced by image manifests |
| `name` | Yes | Human-readable display name |
| `switchType` | Yes | `Internal`, `External`, `Private`, or `NAT` |
| `physicalAdapter` | External only | Adapter selection (see below) |
| `allowManagementOs` | External only | Allow host OS to share the adapter (default: true) |
| `natSubnet` | NAT only | Subnet in CIDR notation (e.g. `192.168.100.0/24`) |
| `natGateway` | NAT only | Gateway IP address |
| `dhcpRangeStart` | NAT only | Start of DHCP range (optional) |
| `dhcpRangeEnd` | NAT only | End of DHCP range (optional) |
| `vlanId` | Optional | VLAN ID for the switch |
| `minimumBandwidthAbsolute` | Optional | Minimum bandwidth in bits/sec |
| `maximumBandwidth` | Optional | Maximum bandwidth in bits/sec |

### Switch Types

- **Internal** — VMs can communicate with each other and the host. No external network access.
- **External** — VMs share a physical network adapter with the host. Full network access.
- **Private** — VMs can only communicate with each other. No host or external access.
- **NAT** — VMs get a private subnet with NAT to the host's network. Internet access via NAT.

## Physical Adapter Selection (External Switches)

The `physicalAdapter` field controls which host network card is used:

| Value | Behavior |
|---|---|
| `auto` | First wired adapter that's Up, sorted by speed (fastest first) |
| `auto-wireless` | Same as auto but includes Wi-Fi adapters |
| `name:Ethernet 2` | Exact match on adapter name |
| `description:Intel*` | Wildcard match on adapter description |

If the selected adapter is already bound to another `VmMgr-*` switch, the next candidate is tried. If no suitable adapter is found, VM creation fails with a list of available adapters.

## Image Manifest — Network References

Add a `networks` array to your image's `manifest.json`:

```json
{
  "title": "Windows 11 Dev Workstation",
  "description": "Development environment with SQL Server",
  "version": "2.0",
  "networks": [
    {
      "networkId": "internal-nat",
      "staticIp": "192.168.100.50/24",
      "gateway": "192.168.100.1",
      "dnsServers": "8.8.8.8,8.8.4.4"
    },
    {
      "networkId": "corp-lan"
    }
  ]
}
```

### Per-Adapter Fields

| Field | Required | Description |
|---|---|---|
| `networkId` | Yes | References a network ID from `networks.json` |
| `staticIp` | No | Static IP in CIDR notation (e.g. `192.168.100.50/24`). Omit for DHCP. |
| `gateway` | No | Default gateway (for static IP) |
| `dnsServers` | No | Comma-separated DNS servers (for static IP) |
| `macAddress` | No | Static MAC address. Omit for auto-assign. |
| `vlanId` | No | Per-adapter VLAN override |

When `networks` is absent or empty in the manifest, VMs get a single adapter connected to the Default Switch (legacy behavior).

## Switch Lifecycle

### Creation
When a VM requires a network that doesn't have a corresponding Hyper-V switch:
1. The agent creates a switch named `VmMgr-{networkId}` (e.g. `VmMgr-internal-nat`)
2. Configures it according to the network definition
3. Records the switch in `managed-networks.json` with a config hash

### Drift Detection
When a VM requires a network that already has a managed switch:
1. The agent compares the current network definition's hash against the stored hash
2. If they differ, the switch settings are updated **in-place** (the switch is never removed while VMs are connected)
3. The config hash is updated in tracking

### Cleanup
When a VM is deleted:
1. Reference count is decremented for each network the VM used
2. If a network's reference count reaches 0 and `AutoCleanupUnusedNetworks` is enabled (default: true), the switch is removed
3. NAT switches also remove their associated `NetNat` and IP configuration

### Reconciliation
On agent startup, the tracking file is reconciled with actual Hyper-V state:
- Tracked switches that no longer exist in Hyper-V are removed from tracking
- Orphan `VmMgr-*` switches not in tracking are removed
- VM reference counts are adjusted to match actual VMs

## Configuration

### Agent Settings (`appsettings.json`)

```json
{
  "VmManager": {
    "AutoCleanupUnusedNetworks": true
  }
}
```

### Tracking File

The agent stores network state in `%APPDATA%/VmManager/managed-networks.json`:

```json
[
  {
    "networkId": "internal-nat",
    "switchName": "VmMgr-internal-nat",
    "configHash": "a1b2c3d4...",
    "referenceCount": 2,
    "vmNames": ["Win11-Dev", "Ubuntu-Test"],
    "createdAt": "2026-04-21T10:00:00Z",
    "lastUsedAt": "2026-04-21T14:30:00Z"
  }
]
```

## Troubleshooting

### "Physical adapter not available"
The External switch's adapter selector couldn't find a matching adapter. Check:
- `Get-NetAdapter -Physical | Where Status -eq 'Up'` — is an adapter available?
- Is the adapter already bound to another `VmMgr-*` switch?
- Try `"auto-wireless"` if only Wi-Fi is available

### "Switch already exists but is not managed by VmManager"
A Hyper-V switch with the expected name exists but doesn't have the `VmMgr-` prefix. VmManager never modifies user-created switches. Either rename the existing switch or change the network ID in `networks.json`.

### Networks not loading from feed
- Check that `networks.json` is at the repository root (not inside an image directory)
- Verify the file is valid JSON with the `"version": 1` field
- Check agent logs for "No networks.json found" messages

### Switch settings out of date
Network definitions are cached per-feed during catalog load. Restart the agent or reload the catalog to pick up changes. The agent will detect the config hash difference and update the switch in-place.

### VM has no network after creation
- Check that the image manifest includes a `networks` array
- Verify the `networkId` references match IDs in `networks.json`
- Check agent logs for provisioning errors during VM creation
