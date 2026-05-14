# Proxmox VE Backend Setup

This guide covers setting up VmManager Agent on a Proxmox VE host.

## Prerequisites

- Proxmox VE 8.x
- SSH access to the Proxmox node
- `qemu-utils` and `ntfs-3g` installed

## 1. Create Proxmox User and API Token

```bash
# Create Linux system user (required for PAM auth)
useradd -m vmmanager
passwd vmmanager

# Create Proxmox user
pveum useradd vmmanager@pam -comment "VmManager Agent"

# Create API token (privsep=0 = token inherits user permissions)
pveum user token add vmmanager@pam vmm-token --privsep=0
# Save the output token value for settings.json
```

## 2. Create Resource Pool

```bash
pveum pool add vmmanager-pool
```

All VMs created by VmManager will be assigned to this pool. The agent only sees VMs in this pool.

## 3. Assign Permissions

```bash
pveum aclmod / -user vmmanager@pam -role PVEVMAdmin
pveum aclmod /pool/vmmanager-pool -user vmmanager@pam -role PVEPoolAdmin
pveum aclmod /pool/vmmanager-pool -user vmmanager@pam -role PVEVMAdmin
pveum aclmod /sdn -user vmmanager@pam -role PVESDNUser
```

| Role | Path | Purpose |
|------|------|---------|
| PVEVMAdmin | `/` | Create and manage VMs |
| PVEPoolAdmin | `/pool/vmmanager-pool` | Manage pool membership |
| PVEVMAdmin | `/pool/vmmanager-pool` | VM operations within pool |
| PVESDNUser | `/sdn` | Attach VMs to network bridges |

## 4. Create Dedicated Storage (Recommended)

Isolate VmManager disk usage with a dedicated storage volume:

```bash
# Create LVM volume (adjust VG name and size)
lvcreate -L 100G -n vmmanager-store <your-vg-name>
mkfs.ext4 /dev/<your-vg-name>/vmmanager-store
mkdir -p /mnt/vmmanager-storage
mount /dev/<your-vg-name>/vmmanager-store /mnt/vmmanager-storage
echo "/dev/<your-vg-name>/vmmanager-store /mnt/vmmanager-storage ext4 defaults 0 2" >> /etc/fstab

# Register as Proxmox storage
pvesm add dir vmmanager-storage --path /mnt/vmmanager-storage --content images,iso,snippets

# Lock vmmanager to ONLY this storage
pveum aclmod /storage/local -user vmmanager@pam -role NoAccess
pveum aclmod /storage/vmmanager-storage -user vmmanager@pam -role PVEDatastoreAdmin
```

When the volume fills up, no more VMs can be created. This provides hard disk space limits.

## 5. Ensure Network Bridge Exists

VmManager assigns VMs to `vmbr0` by default. Verify it exists:

```bash
cat /etc/network/interfaces | grep vmbr0
```

If not present, create it bridging your physical NIC (e.g., `enp13s0`):

```
auto vmbr0
iface vmbr0 inet static
    address 192.168.5.151/24
    gateway 192.168.5.1
    bridge-ports enp13s0
    bridge-stp off
    bridge-fd 0
```

Then `systemctl restart networking`.

## 6. Install Agent Prerequisites

```bash
apt install qemu-utils ntfs-3g
```

## 7. Install the Agent

Use the same Linux agent binary as KVM:

```bash
mkdir -p /opt/vmmanager-agent
tar xzf VmManager-Agent-<version>-linux-x64.tar.gz -C /opt/vmmanager-agent/
cp /opt/vmmanager-agent/vmmanager-agent.service /etc/systemd/system/
systemctl daemon-reload
systemctl enable vmmanager-agent
```

## 8. Configure Settings

Create `/root/.config/VmManager/settings.json`:

```json
{
  "VmBackend": "Proxmox",
  "Proxmox": {
    "ApiUrl": "https://192.168.5.151:8006",
    "ApiTokenId": "vmmanager@pam!vmm-token",
    "ApiTokenSecret": "your-token-uuid-here",
    "Node": "pve01",
    "StorageId": "vmmanager-storage",
    "PoolId": "vmmanager-pool",
    "VerifySsl": false,
    "MaxPoolMemoryMb": 0,
    "MaxPoolCpuCores": 0
  },
  "SecureApi": false,
  "Feeds": []
}
```

| Setting | Description |
|---------|-------------|
| `ApiUrl` | Proxmox web UI URL (port 8006) |
| `ApiTokenId` | Format: `user@realm!token-name` |
| `ApiTokenSecret` | UUID from token creation |
| `Node` | Proxmox node name (shown in web UI) |
| `StorageId` | Target storage for VM disks |
| `PoolId` | Resource pool for VM isolation |
| `VerifySsl` | Set `false` for self-signed certs |
| `MaxPoolMemoryMb` | Soft limit on total pool RAM (0 = unlimited) |
| `MaxPoolCpuCores` | Soft limit on total pool CPU cores (0 = unlimited) |

## 9. Start the Agent

```bash
systemctl start vmmanager-agent
journalctl -u vmmanager-agent -f
```

The agent listens on port 18275 (HTTP + RDP multiplexed).

## 10. Connect from Client

In VmManager client, add a new agent connection pointing to `http://<proxmox-ip>:18275`.

## Known Limitations

- **No per-pool resource limits in Proxmox**: CPU/RAM limits are enforced in software via `MaxPoolMemoryMb` and `MaxPoolCpuCores`
- **No TPM support on directory storage**: Snapshots require qcow2 format; TPM uses raw format which blocks this
- **QEMU guest agent not in HyperV images**: IP resolution uses ARP table scanning by MAC address instead
- **Network bridges must be pre-configured**: VmManager lists existing bridges but cannot create new ones on Proxmox
- **VM hardware profile**: Uses SATA + e1000e (not virtio) for Windows compatibility without driver injection
