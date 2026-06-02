# Monitoring

VmManager includes a built-in monitoring system that tracks VM health, host resources, storage, security events, and agent health. It exposes metrics via a Prometheus-compatible endpoint and sends granular email notifications when conditions require attention.

## Enabling Monitoring

Set `Monitoring.Enabled = true` in the agent settings (via the web UI at Agent Settings > Monitoring, or directly in `settings.json`).

## Monitoring Checks

All checks run in the background at configurable intervals. Each check fires alerts only once per condition (no spam). Threshold-based checks use a 5% hysteresis band to prevent flapping.

| Check | Default Interval | What it detects |
|-------|-----------------|-----------------|
| VM State | 30s | VM crashed (unexpected stop, not user/admin initiated). Distinguishes guest OS shutdown, Proxmox/Hyper-V stop, and actual crashes. |
| VM Stuck State | 30s | VM stuck in "Starting" or "Stopping" state for too long. |
| VM Port (RDP) | 60s | RDP port 3389 unreachable on a running VM (after 5-minute boot grace period). |
| VM Port (WinRM) | 60s | WinRM port 5985 unreachable on a running VM (after 5-minute boot grace period). |
| VM No IP | 60s | VM running for 5+ minutes but no IP address assigned. Network adapter or DHCP issue. |
| VM Uptime | 15min | VM running longer than the configured threshold (stale VM detection). |
| Snapshot Depth | 15min | Too many snapshots on a VM (performance degradation risk). |
| Host CPU | 5min | Host CPU usage exceeds warning or critical threshold. |
| Host Memory | 5min | Host memory usage exceeds warning or critical threshold. |
| Storage | 5min | Storage pool free space below warning or critical threshold. |
| Disk Health | 1h | SMART health check failed on a physical disk. |
| Agent Health | 5min | Agent memory usage over 1 GB, or hypervisor API unreachable. |
| Capacity | 15min | Total VM count approaching the configured global limit. |
| Failed Login | 30s | Multiple failed RDP CredSSP login attempts for a user. |
| Brute Force | 30s | Excessive failed login attempts (potential brute force attack). |

### Boot Grace Period

When a VM starts, port checks and IP checks are skipped for 5 minutes to allow Windows to boot and initialize networking. This prevents false positive alerts during normal VM startup.

### Crash Detection

When a VM transitions from Running to Off, the monitoring system determines the cause:

1. **Stopped via VmManager** (user clicked Stop in the UI or API): No alert. The `VmStopTracker` records managed stops.
2. **Stopped via Proxmox/Hyper-V UI**: No alert. The system checks the hypervisor task log for recent `qmstop`/`qmshutdown` tasks.
3. **Guest OS shutdown** (user clicked Shutdown in Windows): Info alert, not critical.
4. **Actual crash** (BSOD, OOM, unexpected failure): Critical alert with email notification.

### Hysteresis

Threshold checks (CPU, memory, storage) use a 5% hysteresis band. Example: if the CPU warning threshold is 85%, the alert fires when CPU reaches 85%. It only clears (and sends a resolution email) when CPU drops below 80%. This prevents rapid on/off flapping when the value hovers around the threshold.

## Email Notifications

Each check can be individually toggled on/off and routed to a specific email address. If no specific email is set for a check, the `DefaultNotificationEmail` is used.

Example configuration:
- VM Crash alerts go to `ops@company.com` (ticketing system)
- Brute Force alerts go to `security@company.com`
- Everything else goes to `admin@me.com`

### Email Format

Emails are plain text monospace format (works in Jira Service Management, Outlook, any email client). Each email includes:
- Alert severity and title
- Detailed message with context
- Full system snapshot: host CPU/memory/uptime, storage usage, all VMs with per-VM CPU and memory

### Resolution Emails

For persistent conditions (CPU, memory, storage, port reachability, agent health, capacity), a resolution email is sent when the condition clears. The resolution email:
- Uses the **same subject line** as the original alert (Jira threads them in the same ticket)
- Includes `In-Reply-To` and `References` email headers for proper threading
- Quotes the original alert below the resolution message
- Includes current system status

One-time events (VM crash, failed login, snapshot depth) do not send resolution emails.

## Thresholds

All thresholds are configurable in the agent settings:

| Threshold | Default | Description |
|-----------|---------|-------------|
| Host CPU Warning | 85% | Fires warning alert |
| Host CPU Critical | 95% | Fires critical alert |
| Host Memory Warning | 85% | Fires warning alert |
| Host Memory Critical | 95% | Fires critical alert |
| Storage Free Warning | 20% | Free space below this triggers warning |
| Storage Free Critical | 10% | Free space below this triggers critical |
| VM Uptime Warning | 30 days | VMs running longer than this get flagged |
| Snapshot Chain Depth | 5 | More snapshots than this triggers warning |
| Failed Login Threshold | 5 | Failed logins in window triggers alert |
| Failed Login Window | 10 min | Time window for counting failed logins |
| Brute Force Threshold | 20 | Failed logins in window triggers critical |
| Brute Force Window | 30 min | Time window for brute force detection |
| Certificate Expiry | 30 days | TLS certificate expiring within this triggers warning |
| Capacity Warning | 80% | VM count percentage of MaxTotalVms |

## Prometheus Metrics

The `/metrics` endpoint (unauthenticated) returns Prometheus text format. Scrape it every 15-30 seconds.

### Host Metrics
```
vmmanager_host_cpu_usage_ratio          # 0-1, host CPU utilization
vmmanager_host_memory_used_bytes        # bytes
vmmanager_host_memory_total_bytes       # bytes
vmmanager_host_uptime_seconds           # seconds
```

### Per-VM Metrics
```
vmmanager_vm_cpu_usage_ratio{vm="Name"}           # 0-1
vmmanager_vm_memory_used_bytes{vm="Name"}         # bytes
vmmanager_vm_memory_assigned_bytes{vm="Name"}     # bytes
vmmanager_vm_disk_read_bytes_total{vm="Name"}     # counter, bytes
vmmanager_vm_disk_write_bytes_total{vm="Name"}    # counter, bytes
vmmanager_vm_net_rx_bytes_total{vm="Name"}        # counter, bytes
vmmanager_vm_net_tx_bytes_total{vm="Name"}        # counter, bytes
vmmanager_vm_state{vm="Name",state="running"}     # 1 if in this state
```

### Storage Metrics
```
vmmanager_storage_used_bytes{pool="Name"}    # bytes
vmmanager_storage_total_bytes{pool="Name"}   # bytes
```

### Alert Metrics
```
vmmanager_alerts_active_total{severity="info"}       # count
vmmanager_alerts_active_total{severity="warning"}    # count
vmmanager_alerts_active_total{severity="critical"}   # count
vmmanager_alerts_active_total{severity="fatal"}      # count
```

## REST API

All endpoints under `/api/monitoring`. Require authentication (Basic Auth or Blazor session).

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| GET | `/api/monitoring/alerts` | monitoring.view | List alerts. Filters: `severity`, `vmName`, `since`, `acknowledged`, `limit`, `offset` |
| GET | `/api/monitoring/alerts/{id}` | monitoring.view | Get single alert |
| POST | `/api/monitoring/alerts/{id}/acknowledge` | monitoring.manage | Acknowledge alert |
| POST | `/api/monitoring/alerts/acknowledge-all` | monitoring.manage | Acknowledge all matching alerts |
| GET | `/api/monitoring/metrics/host` | monitoring.view | Current host CPU, memory, uptime |
| GET | `/api/monitoring/metrics/vms` | monitoring.view | Per-VM metrics |
| GET | `/api/monitoring/metrics/vms/{name}` | monitoring.view | Single VM metrics |
| GET | `/api/monitoring/metrics/storage` | monitoring.view | Storage pool usage |
| GET | `/api/monitoring/metrics/disks` | monitoring.manage | SMART disk health |
| GET | `/api/monitoring/status` | monitoring.view | Check status, last run times, alert counts |
| GET | `/api/monitoring/settings` | monitoring.manage | Get monitoring config |
| PUT | `/api/monitoring/settings` | monitoring.manage | Update monitoring config |

## Grafana Setup

### Option 1: Direct API (Recommended, no Prometheus needed)

Use the Grafana **Infinity** datasource plugin to query the VmManager REST API directly.

1. Install the Infinity plugin: Grafana > Administration > Plugins > Search "Infinity"
2. Add datasource: Type = Infinity, Base URL = `http://agent-ip:18275`, Auth = Basic Auth with VmManager credentials
3. Create panels using JSON endpoints:

| Panel | URL | Type | Key fields |
|-------|-----|------|------------|
| Host CPU gauge | `/api/monitoring/metrics/host` | JSON | `cpuPercent` |
| Host Memory gauge | `/api/monitoring/metrics/host` | JSON | `memoryUsedBytes`, `memoryTotalBytes` |
| Host Uptime | `/api/monitoring/metrics/host` | JSON | `uptimeSeconds` |
| Per-VM CPU | `/api/monitoring/metrics/vms` | JSON | `name`, `cpuPercent` |
| Per-VM Memory | `/api/monitoring/metrics/vms` | JSON | `name`, `memoryUsedBytes`, `memoryAssignedBytes` |
| Per-VM Disk I/O | `/api/monitoring/metrics/vms` | JSON | `name`, `diskReadBytesTotal`, `diskWriteBytesTotal` |
| Per-VM Network | `/api/monitoring/metrics/vms` | JSON | `name`, `networkRxBytesTotal`, `networkTxBytesTotal` |
| Storage usage | `/api/monitoring/metrics/storage` | JSON | `name`, `usedBytes`, `totalBytes` |
| Disk health | `/api/monitoring/metrics/disks` | JSON | `device`, `healthy`, `model` |
| Active alerts | `/api/monitoring/alerts?acknowledged=false&limit=50` | JSON | `severity`, `title`, `vmName`, `timestamp` |
| Alert counts | `/api/monitoring/status` | JSON | `activeAlerts` |

### Option 2: Prometheus

The `/metrics` endpoint returns Prometheus text format. Add as a Prometheus datasource if you already run Prometheus.

Useful PromQL queries:

```promql
# Host memory usage percentage
vmmanager_host_memory_used_bytes / vmmanager_host_memory_total_bytes * 100

# VM CPU usage as percentage
vmmanager_vm_cpu_usage_ratio * 100

# Disk write rate per VM (bytes/sec over 5 minutes)
rate(vmmanager_vm_disk_write_bytes_total[5m])

# Network receive rate per VM
rate(vmmanager_vm_net_rx_bytes_total[5m])

# Storage free percentage
(1 - vmmanager_storage_used_bytes / vmmanager_storage_total_bytes) * 100

# Total active critical alerts
vmmanager_alerts_active_total{severity="critical"}
```

## Proxmox Permissions

The monitoring system queries the Proxmox API for host and VM metrics. The API token needs:
- `PVEVMAdmin` on the VM pool (already required for VM management)
- `PVEAuditor` on the specific node (for host CPU, memory, uptime, disk health)

Grant node-level audit access:
```bash
pveum aclmod /nodes/YOUR_NODE -user vmmanager@pam -role PVEAuditor
```

Without `PVEAuditor`, VM metrics still work but host metrics (CPU, memory, uptime) will be empty.
