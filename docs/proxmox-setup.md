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

# Create API token with privilege separation DISABLED
pveum user token add vmmanager@pam vmm-token --privsep=0
# Save the output token value for settings.json
```

> **Important:** The `--privsep=0` flag is required. With privilege separation enabled (the default), the API token gets its own separate permissions and will NOT inherit the user's permissions, even if the user has the correct roles. This causes 403 errors on all API calls. Always use `--privsep=0` so the token inherits the user's permissions.
>
> If you created the token through the Proxmox web UI, uncheck "Privilege Separation" in the token creation dialog.

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

## 6. Grant Monitoring Permissions (Optional)

To enable host-level monitoring (CPU, memory, uptime, disk health) via the VmManager monitoring system:

```bash
pveum aclmod /nodes/<your-node> -user vmmanager@pam -role PVEAuditor
```

Without this, VM metrics still work but host metrics will be empty.

## 7. Install Agent Prerequisites

```bash
apt install qemu-utils ntfs-3g gss-ntlmssp
```

The `gss-ntlmssp` package is required for the RDP CredSSP proxy (NTLM authentication to Windows VMs).

## 8. Install the Agent

Download the latest Linux agent release, or build from source:

```bash
# Option A: Download release
wget https://github.com/chiouyazo/VmManager/releases/latest/download/VmManager-Agent-linux-x64.tar.gz

# Option B: Build from source (requires .NET 10 SDK)
git clone https://github.com/chiouyazo/VmManager.git
cd VmManager
dotnet publish dotnet/VmManager.Agent -c Release -r linux-x64 --self-contained -o /tmp/vmmanager-agent-publish
cd /tmp/vmmanager-agent-publish && tar czf /tmp/VmManager-Agent-linux-x64.tar.gz .
```

Install:

```bash
mkdir -p /opt/vmmanager-agent
tar xzf VmManager-Agent-linux-x64.tar.gz -C /opt/vmmanager-agent/
chmod +x /opt/vmmanager-agent/VmManager.Agent
```

Create the systemd service file at `/etc/systemd/system/vmmanager-agent.service`:

```ini
[Unit]
Description=VmManager Agent
After=network.target

[Service]
Type=simple
ExecStart=/opt/vmmanager-agent/VmManager.Agent
WorkingDirectory=/opt/vmmanager-agent
Restart=always
RestartSec=5
Environment=DOTNET_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

Enable the service:

```bash
systemctl daemon-reload
systemctl enable vmmanager-agent
```

## 9. Configure Settings

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
    "MaxPoolCpuCores": 0,
    "VmIdRangeStart": 400,
    "VmIdRangeEnd": 499,
    "DefaultBridge": "vmbr1",
    "DefaultVlanTag": 0,
    "VmSubnet": "192.168.10"
  },
  "DefaultVmUsername": "Administrator",
  "DefaultVmPassword": "Admin123!",
  "RdpDomainSuffix": "",
  "ApplyLocaleOnCreate": true,
  "DefaultLocale": "de-DE",
  "DefaultKeyboardLayout": "00000407",
  "DefaultTimezone": "W. Europe Standard Time",
  "Feeds": [],
  "Monitoring": {
    "Enabled": true,
    "DefaultNotificationEmail": "admin@company.com"
  }
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
| `VmIdRangeStart` | First VMID in the allowed range (0 = use Proxmox default) |
| `VmIdRangeEnd` | Last VMID in the allowed range |
| `DefaultBridge` | Network bridge for VM NICs (default: `vmbr0`) |
| `DefaultVlanTag` | VLAN tag for VM NICs (0 = no VLAN) |
| `VmSubnet` | VM subnet prefix for IP resolution (e.g. `192.168.10`) |
| `ImportMethod` | `Standard` (agent on Proxmox host) or `DiskPassthrough` (agent in a VM) |
| `AgentVmId` | VMID of the VM running the agent (required for DiskPassthrough) |
| `DefaultVmUsername` | Windows login for VMs (default: `Administrator`). Users can override per-VM or per-user. |
| `DefaultVmPassword` | Password for VM guest OS (used by CredSSP proxy) |
| `RdpDomainSuffix` | DNS wildcard domain for RDP (e.g. `vms.company.com`), leave empty for username-prefix mode |
| `Monitoring.Enabled` | Enable monitoring checks and alerts |
| `Monitoring.DefaultNotificationEmail` | Fallback email for monitoring alerts |

## 10. Start the Agent

```bash
systemctl start vmmanager-agent
journalctl -u vmmanager-agent -f
```

The agent listens on port 18275 (HTTP + Web UI + RDP multiplexed) and port 13389 (standalone RDP proxy).

Check the generated admin credentials:
```bash
cat /root/.config/VmManager/api-credentials.txt
```

## 11. Access the Web UI

Open `http://<proxmox-ip>:18275` in a browser. Login with the admin credentials from the previous step.

## 12. Connect from Desktop Client

In VmManager desktop client, add a new agent connection pointing to `http://<proxmox-ip>:18275` with the admin credentials.

## 13. Connect to VMs via RDP

**With DNS wildcard (recommended):**
1. Set `RdpDomainSuffix` in settings (e.g. `vms.company.com`)
2. Configure wildcard DNS: `*.vms.company.com` pointing to the Proxmox IP
3. Type `myVm.vms.company.com` in mstsc, enter VmManager credentials

**Without DNS:**
1. Connect mstsc to `<proxmox-ip>:13389`
2. Enter `vmName:user@email` as the username (e.g. `myVm:admin`)
3. Enter VmManager password

## 14. Set Up Monitoring (Optional)

### Grafana with Direct API Connection

No Prometheus needed. Use the Grafana **Infinity** plugin to query the VmManager API directly.

1. Install the Infinity datasource plugin in Grafana
2. Add a new Infinity datasource:
   - Base URL: `http://<proxmox-ip>:18275`
   - Authentication: Basic Auth with VmManager admin credentials
3. Create panels using these endpoints:

| Panel | URL | Type | Fields |
|-------|-----|------|--------|
| Host CPU | `/api/monitoring/metrics/host` | JSON | `cpuPercent` |
| Host Memory | `/api/monitoring/metrics/host` | JSON | `memoryUsedBytes`, `memoryTotalBytes` |
| Per-VM CPU | `/api/monitoring/metrics/vms` | JSON | `name`, `cpuPercent` |
| Per-VM Memory | `/api/monitoring/metrics/vms` | JSON | `name`, `memoryUsedBytes` |
| Storage | `/api/monitoring/metrics/storage` | JSON | `name`, `usedBytes`, `totalBytes` |
| Active Alerts | `/api/monitoring/alerts?acknowledged=false` | JSON | `severity`, `title`, `timestamp` |

Alternatively, the `/metrics` endpoint (Prometheus text format) works with the standard Prometheus datasource if you prefer that setup.

### Email Notifications

Configure SMTP in Agent Settings (web UI or settings.json). Each monitoring check can be toggled individually and routed to a specific email address. See [Monitoring Documentation](monitoring.md) for the full list of checks and thresholds.

## Firewall

If the Proxmox firewall is enabled, open these ports:

| Port | Protocol | Purpose |
|------|----------|---------|
| 18275 | TCP | HTTP API + Web UI + multiplexed RDP |
| 13389 | TCP | Standalone RDP CredSSP proxy |

```bash
# Proxmox firewall rules (if enabled)
# Add in Datacenter > Firewall > Rules or via CLI:
pve-firewall add -action ACCEPT -type in -dport 18275 -proto tcp -comment "VmManager API"
pve-firewall add -action ACCEPT -type in -dport 13389 -proto tcp -comment "VmManager RDP Proxy"
```

Or if using iptables directly:
```bash
iptables -A INPUT -p tcp --dport 18275 -j ACCEPT
iptables -A INPUT -p tcp --dport 13389 -j ACCEPT
```

## Running the Agent on a Separate VM (Disk Passthrough)

When the agent runs inside a Proxmox VM rather than directly on the Proxmox host, the standard import method (`qm importdisk`) is unavailable. The Disk Passthrough method works around this by hot-plugging a temporary SCSI disk to the agent VM, writing the image data locally, then moving the disk to the target VM via the API.

### Agent VM Requirements

The agent VM must have:
- `scsihw=virtio-scsi-single` (SCSI controller that supports hot-plug)
- `hotplug=disk,network,usb` (enable disk hot-plug)

These can be set in the Proxmox web UI under VM > Hardware > Options, or via API.

### Configuration

Set in Agent Settings (web UI) or `settings.json`:

```json
{
  "Proxmox": {
    "ImportMethod": "DiskPassthrough",
    "AgentVmId": 403
  }
}
```

- `ImportMethod`: Set to `DiskPassthrough`
- `AgentVmId`: The VMID of the VM running the VmManager agent (visible in the Proxmox web UI)

### How It Works

1. Agent creates the target VM via API (empty, with EFI disk)
2. Agent creates a temporary SCSI disk on its own VM (hot-plugged, same storage as target)
3. Agent writes the QCOW2 image to the hot-plugged disk using `qemu-img convert`
4. Agent detaches the disk from itself
5. Agent moves the disk to the target VM via the `move_disk` API
6. Target VM has a bootable disk with the imported image

### Permissions

The API token needs VM management permissions on both the agent VM and the target pool. No special storage permissions beyond `AllocateSpace` are required.

## Known Limitations

- **No per-pool resource limits in Proxmox**: CPU/RAM limits are enforced in software via `MaxPoolMemoryMb` and `MaxPoolCpuCores`
- **No TPM support on directory storage**: Snapshots require qcow2 format; TPM uses raw format which blocks this
- **QEMU guest agent not in HyperV images**: IP resolution uses ARP table scanning by MAC address instead
- **Network bridges must be pre-configured**: VmManager lists existing bridges but cannot create new ones on Proxmox
- **VM hardware profile**: Uses SATA + e1000e (not virtio) for Windows compatibility without driver injection
