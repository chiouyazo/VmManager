# VM Manager

A WPF desktop application for managing Hyper-V virtual machines and Docker containers on Windows. Browse VM images from OCI registries or local catalogs, create VMs with one click, and manage snapshots - all from a clean, modern UI.

## Features

- **Hyper-V management** - Create, start, stop, rename, reset and delete VMs via fast WMI calls
- **Docker support** - Manage Docker containers alongside Hyper-V VMs with a unified interface
- **OCI registry integration** - Browse and pull VM box images from any OCI-compliant registry (Zot, ORAS)
- **Nexus repository support** - Fetch Linux images from Nexus raw repositories
- **Local catalog** - Import VMs from local or network folders with a catalog.json manifest
- **Snapshot management** - Create, restore, delete and push checkpoints to the registry
- **One-click VM creation** - Downloads, extracts and registers VMs with differencing disks
- **Auto locale configuration** - Optionally applies DE locale and QWERTZ keyboard via PowerShell Direct

## Requirements

- Windows 10 or later (Windows 11 recommended)
- Hyper-V enabled (via Windows Features)
- Administrator privileges (required for Hyper-V and WMI access)
- .NET 10 SDK (for building from source)

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

## License

This project is licensed under the [GPL-3.0](https://www.gnu.org/licenses/gpl-3.0.html) license.
