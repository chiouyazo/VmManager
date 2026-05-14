# Install Guide

## Windows (Full -- Desktop + Agent)

- Download `VmManager-Setup-Full-{version}.exe` from GitHub releases
- Run the installer
- Requires: Windows 10/11, Hyper-V enabled, Administrator privileges
- The installer creates a Start Menu shortcut and optionally a desktop shortcut
- The embedded agent starts automatically

## Windows (Client Only -- Remote Agent)

- Download `VmManager-Setup-Client-{version}.exe` from GitHub releases
- Run the installer
- No Hyper-V or admin required -- connects to remote agents only

## Windows (Agent Only -- Windows Service)

- Download `VmManager-Setup-Agent-{version}.exe` from GitHub releases
- Run the installer
- Optionally installs as Windows service (auto-start)
- Optionally creates firewall rules for ports 18275 (API) and 13389 (RDP proxy)

## Linux (Agent -- KVM)

### Prerequisites

```bash
sudo apt install qemu-kvm libvirt-daemon-system virt-install qemu-utils
```

### Install

```bash
sudo mkdir -p /opt/vmmanager-agent
sudo tar xzf VmManager-Agent-{version}-linux-x64.tar.gz -C /opt/vmmanager-agent/
sudo cp /opt/vmmanager-agent/vmmanager-agent.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable vmmanager-agent
sudo systemctl start vmmanager-agent
```

### Verify

```bash
sudo systemctl status vmmanager-agent
curl http://localhost:18275/health
```

## Linux (Agent -- Proxmox VE)

See [Proxmox Setup Guide](proxmox-setup.md) for the full walkthrough including user creation, API token, resource pool, and storage isolation.

### Quick Install

```bash
# Prerequisites
apt install qemu-utils ntfs-3g

# Agent
mkdir -p /opt/vmmanager-agent
tar xzf VmManager-Agent-{version}-linux-x64.tar.gz -C /opt/vmmanager-agent/
cp /opt/vmmanager-agent/vmmanager-agent.service /etc/systemd/system/
systemctl daemon-reload
systemctl enable vmmanager-agent
```

Configure `/root/.config/VmManager/settings.json` with `"VmBackend": "Proxmox"` and Proxmox API credentials, then start the service.

```bash
systemctl start vmmanager-agent
```

## VM Image Requirements

All VM images managed by VmManager must share the same local administrator credentials. Configure these in Settings > VM Credentials (default: `Administrator` / `Admin123!`).

This is required because VmManager connects to VMs after creation to apply locale, keyboard, and timezone settings. On Hyper-V this uses PowerShell Direct, on KVM and Proxmox it uses WinRM. All methods require valid guest credentials.

If your images use different credentials, either standardize them before packaging, or disable "Apply locale on create" in settings and configure locale manually after creation.

## macOS (Client Only)

- Download `VmManager-Client-{version}-osx-arm64.dmg` (Apple Silicon) or `osx-x64.dmg` (Intel)
- Open DMG, drag VmManager to Applications
- First run: `xattr -cr /Applications/VmManager.app` (unsigned app)
- Connects to remote agents only (no local VM management)
