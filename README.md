# VM Manager

A WPF desktop application for managing Hyper-V virtual machines and Docker containers on Windows. Browse VM images from OCI registries or local catalogs, create VMs with one click, and manage snapshots - all from a clean, modern UI.

## Features

- **Hyper-V management** - Create, start, stop, rename, reset and delete VMs via fast WMI calls
- **Docker support** - Manage Docker containers alongside Hyper-V VMs with a unified interface
- **OCI registry integration** - Browse and pull VM box images from any OCI-compliant registry (Zot, ORAS)
- **Nexus repository support** - Fetch Linux images from Nexus raw repositories
- **Local catalog** - Import VMs from local or network folders with a catalog.json manifest
- **Inline snapshot management** - Create, restore, delete and clone from checkpoints directly on each VM card
- **Snapshot sharing** - Push snapshots to OCI, Nexus, or network shares. They show up under the parent image for colleagues to pull
- **One-click VM creation** - Downloads, extracts and registers VMs with differencing disks
- **Auto locale configuration** - Optionally applies DE locale and QWERTZ keyboard via PowerShell Direct

## Requirements

- Windows 10 or later (Windows 11 recommended)
- Hyper-V enabled (via Windows Features)
- Administrator privileges (required for Hyper-V and WMI access)
- .NET 10 SDK (for building from source)

## Storage & Deployment

For details on the different image source backends (network paths, OCI registries, Nexus), how differencing disks and snapshots keep local storage low, and what setup makes sense for your team, see the [Storage & Deployment Guide](docs/storage-guide.md).

## Snapshot Sharing

VM Manager tracks which marketplace image each VM was created from. This makes it possible to share customized VM states with your team:

1. Create a VM from the marketplace
2. Customize it, install what you need, then expand the "Snapshots" section on the VM card and create a snapshot
3. Click "Push" on the snapshot. It automatically goes to the feed the VM came from (OCI, Nexus, or network share)
4. Your colleagues refresh their marketplace and see the snapshot under "Shared Snapshots" on the same image card
5. They can import it and create a new VM from your snapshot, just like any other version

Push includes connectivity checking, progress reporting with transfer speed and ETA. The snapshot metadata (who pushed it, which parent image, timestamp) is stored alongside the artifact so it automatically shows up in the right place.

## Build

```bash
dotnet tool install --global csharpier --version 1.2.1

csharpier format ./dotnet

# Build
dotnet build VmManager.sln --configuration Release

# Publish a self-contained single-file executable
dotnet publish dotnet/VmManager/VmManager.csproj \
  -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  --configuration Release -o ./publish
```

To build the installer, install [Inno Setup](https://jrsoftware.org/isinfo.php) and run:

```bash
iscc installer\setup.iss
```

## Roadmap

- **Display language packs** - Currently keyboard layout, system locale, and timezone are applied on VM creation, but the Windows display language (UI text) requires a language pack download from Windows Update or Features on Demand ISO. This can take 20+ minutes and needs internet access in the VM. Planned: pre-bake language packs into base images during the Packer build, or download them asynchronously after VM creation.
- **Parallel chunk downloads** - Split large .box downloads into parallel HTTP range requests for faster throughput on high-latency connections.

## License

This project is licensed under the [Apache2.0](https://www.apache.org/licenses/LICENSE-2.0.txt) license.
