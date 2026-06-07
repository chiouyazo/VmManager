# VmManager

A cross-platform VM management platform for Hyper-V, KVM, and Proxmox VE. Includes an Avalonia desktop client, a Blazor web frontend, an RDP CredSSP proxy for transparent VM access, comprehensive monitoring with Prometheus integration, and full user/permission management.

## Why VmManager?

| | VmManager | OpenNebula | CloudStack | Ravada VDI | Xen Orchestra |
|---|---|---|---|---|---|
| **Single binary, no dependencies** | Yes | No (DB, services) | No (DB, MQ, services) | No (DB, services) | No (Node.js server) |
| **Hyper-V + KVM + Proxmox** | Yes | KVM, VMware | KVM, VMware, Xen | KVM only | Xen only |
| **RDP proxy with credential injection** | Yes | No | No | No | No |
| **Feed-based image distribution** | OCI, Nexus, Local | Marketplace | Templates | Base images | Templates |
| **Self-service VM creation** | Yes | Yes | Yes | Yes | Yes |
| **Snapshot push/pull to registries** | Yes | No | No | No | No |
| **Built-in monitoring + alerting** | Yes | Partial | Partial | No | Partial |
| **Setup time** | Minutes | Hours | Hours | Hours | ~1 hour |

VmManager is designed for teams that need on-demand Windows VMs with minimal infrastructure. One agent binary handles VM provisioning, RDP access, image distribution, monitoring, and user management. No database servers, no message queues, no container orchestration required.

## Features

### VM Management
- **Multi-backend support** - Hyper-V (Windows), KVM/libvirt (Linux), Proxmox VE (API-based)
- **Full VM lifecycle** - Create, start, stop, rename, reset, delete
- **Template-based instant creation** - First import creates a Proxmox template; subsequent VMs are linked clones (seconds instead of minutes)
- **Disk Passthrough import** - For agents running inside a VM: hot-plugs disks via Proxmox API, no CLI access needed
- **Snapshot management** - Create, restore, delete, clone from snapshots
- **Snapshot sharing** - Push snapshots to OCI registries or Nexus repositories for team sharing
- **Image catalog** - Browse and import VM images from OCI, Nexus, or local sources
- **VLAN support** - Configure bridge, VLAN tag, and VM subnet for network isolation
- **Locale configuration** - Apply language, keyboard layout, timezone on VM creation via WinRM or PowerShell Direct
- **Post-creation scripts** - Run custom scripts after VM creation or startup
- **VM name validation** - Cross-platform safe naming (letters, numbers, hyphens, dots, max 63 chars)

### RDP CredSSP Proxy
- **Transparent RDP access** - Users connect with their VmManager credentials, proxy authenticates to VMs
- **Per-user VM credentials** - 4-level hierarchy: per-user-per-VM > per-user global > per-VM default > global default
- **Dual-mode routing** - DNS wildcard (e.g. `myVm.lab.domain`) or username-prefix (`vmName:user@email`)
- **Username or email login** - Users can log in with email or optional short username (e.g. AD account name)
- **No tokens or .rdp files** - Standard mstsc login dialog, credentials validated against VmManager user database
- **Session tracking** - Active RDP sessions visible in the web UI and API
- **Permission-based access** - Users can only connect to VMs they own or have been shared

### Web Frontend (Blazor Server)
- **Embedded in the agent** - No separate hosting, served from the same port as the API
- **MudBlazor UI** - Material Design components with responsive layout
- **Full feature parity** - VMs, images, users, sessions, settings, all from the browser
- **Background task tracking** - Long-running operations with progress, per-user visibility
- **Login with VmManager credentials** - Persisted sessions via encrypted browser storage

### Desktop Client (Avalonia)
- **Cross-platform** - Windows, macOS, Linux
- **Native RDP launch** - One-click connect through the CredSSP proxy
- **Shadow sessions** - Admin can shadow active RDP sessions
- **SignalR progress** - Real-time progress for imports, VM creation, snapshot push

### Monitoring
- **Single monitoring API** - External tools (Grafana, Zabbix) query VmManager instead of the hypervisor directly
- **Prometheus /metrics endpoint** - Per-VM CPU, memory, disk I/O, network; host stats; storage; alert counts
- **10 monitoring checks** - VM crash detection, stuck states, port reachability, host resources, storage, SMART, agent health, capacity, login security
- **Smart crash detection** - Distinguishes guest shutdown vs unexpected crash vs managed stop
- **Granular email notifications** - Per-check toggle with per-check email routing (e.g. critical alerts to ops, security alerts to security team)
- **Alert history** - Persistent, queryable via API, acknowledgeable by admins
- **Brute force detection** - Failed RDP login tracking with configurable thresholds

### User Management
- **Email + username accounts** - Users identified by email, with optional short username for login
- **PBKDF2-SHA256 passwords** - 100K iterations, salted
- **23 granular permissions** - VM, snapshot, catalog, settings, RDP, monitoring, user management
- **VM sharing** - Share VMs with specific users, grant per-VM permissions
- **VM ownership** - Track who created each VM, transfer ownership
- **Quotas** - Per-user and global VM limits
- **Email notifications** - SMTP for invites, stale VM reminders, monitoring alerts
- **NT hash storage** - For CredSSP proxy NTLM authentication

### RD Web Feed
- **Workspace feed** - Standard MS-TSWP XML feed at `/RDWeb/Feed/webfeed.aspx`
- **Per-user VM list** - Only shows VMs the authenticated user can access
- **Compatible with RemoteApp and Desktop Connections** (RADC) on Windows

## Architecture

```
                          +-------------------+
                          |   Avalonia Client  |
                          |   (Desktop App)    |
                          +--------+----------+
                                   |
                                   | REST API + SignalR
                                   |
+----------------+        +--------v----------+        +-----------+
|   Web Browser  |------->|   VmManager Agent  |<------>| Hypervisor|
|  (Blazor UI)   |        |   (ASP.NET Core)   |        | Hyper-V / |
+----------------+        |                    |        | KVM /     |
                          | - REST API         |        | Proxmox   |
+----------------+        | - Blazor Server    |        +-----------+
| mstsc / RDP    |------->| - CredSSP Proxy    |
| Client         |  RDP   | - Monitoring       |        +-----------+
+----------------+        | - RD Web Feed      |------->|   VMs     |
                          | - Prometheus       |  RDP   | :3389     |
+----------------+        +--------+-----------+        +-----------+
| Grafana /      |------->|
| Prometheus     | /metrics
+----------------+
```

## Quick Start

### Windows (Desktop + Agent)
```bash
dotnet run --project dotnet/VmManager
```
Requires Hyper-V enabled, Administrator privileges.

### Linux Agent (Proxmox)
```bash
apt install gss-ntlmssp  # Required for RDP CredSSP proxy
dotnet publish dotnet/VmManager.Agent -c Release -r linux-x64 --self-contained -o /opt/vmmanager-agent
```
Configure `settings.json` with `"VmBackend": "Proxmox"` and Proxmox API credentials.

### Linux Agent (KVM)
```bash
apt install qemu-kvm libvirt-daemon-system virt-install qemu-utils gss-ntlmssp
```

### Web UI
Open `http://agent-ip:18275` in any browser. Login with VmManager credentials.

### RDP Connection
- **With DNS**: Set `RdpDomainSuffix` in settings, configure wildcard DNS, type `vmName.lab.domain` in mstsc
- **Without DNS**: Connect to `agent-ip:13389`, enter `vmName:user@email` as username

### Monitoring
- Enable in agent settings: `Monitoring.Enabled = true`
- Prometheus scrape: `http://agent-ip:18275/metrics`
- Grafana dashboard: Import from `docs/` or auto-provision
- Email alerts: Configure SMTP + per-check notification emails

## API

Full REST API with Swagger documentation at `http://agent-ip:18275/swagger`.

Key endpoint groups:
- `/api/vms` - VM lifecycle, snapshots, sharing
- `/api/catalog` - Image catalog, import, VM creation
- `/api/users` - User management, permissions
- `/api/auth` - Authentication, password changes
- `/api/settings` - Agent configuration
- `/api/monitoring` - Alerts, metrics, monitoring settings
- `/api/rdp-sessions` - Active RDP session listing
- `/metrics` - Prometheus text format
- `/RDWeb/Feed/webfeed.aspx` - RD Web Feed (XML)
- `/health` - Health check

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| `VmManager:HttpPort` | `18275` | API + Web UI + multiplexed RDP port |
| `VmManager:RdpProxyPort` | `13389` | Standalone RDP CredSSP proxy port |
| `VmBackend` | `HyperV` | Backend: `HyperV`, `KVM`, `Proxmox`, `Fake` |
| `DefaultVmUsername` | `Administrator` | VM guest credentials |
| `DefaultVmPassword` | `Admin123!` | VM guest credentials |
| `RdpDomainSuffix` | `""` | DNS wildcard domain for RDP (e.g. `vms.company.com`) |
| `SmtpEnabled` | `false` | Enable email notifications |
| `Monitoring.Enabled` | `false` | Enable monitoring system |

See [Architecture](docs/architecture.md), [Install Guide](docs/install-guide.md), and [Troubleshooting](docs/troubleshooting.md) for full documentation.

## Build

```bash
dotnet build dotnet/VmManager.sln

# Desktop client (Windows)
dotnet publish dotnet/VmManager/VmManager.csproj -r win-x64 --self-contained -c Release -o publish/

# Agent (Linux)
dotnet publish dotnet/VmManager.Agent/VmManager.Agent.csproj -r linux-x64 --self-contained -c Release -o publish-agent/
```

## License

This project is licensed under the [Apache 2.0](https://www.apache.org/licenses/LICENSE-2.0.txt) license.
