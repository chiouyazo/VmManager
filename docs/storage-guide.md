# VM Manager - Storage & Deployment Guide

## What this is about

VM Manager can pull VM images from multiple sources and uses Hyper-V's differencing disks and snapshots to keep local storage usage low. This doc goes over the different backends it support, when to use what, how the storage stuff works under the hood, and what a good setup looks like for our use case.

## Image Source Backends

### 1. Local / Network Path (SMB/UNC)

You point the app at a folder (local drive or network share) that has a `catalog.json` and the `.box` archives. The app copies the box file locally and extracts it.

|              |                                                 |
| ------------ | ----------------------------------------------- |
| **Setup**    | Drop files on a share, write a `catalog.json`   |
| **Auth**     | Windows/AD, handled automatically               |
| **Speed**    | Very fast on LAN, limited by network throughput |
| **Cost**     | Free if you have an existing file server or NAS |
| **Offline**  | Works if files are on a local drive             |
| **Best for** | Office teams with existing file infrastructure  |

**Pros:**

- No extra infrastructure needed, any SMB share works (NAS, file server, even a USB drive)
- Fastest option on LAN since its just a direct file copy with no protocol overhead
- Uses existing Windows/AD permissions
- Simple to set up and maintain

**Cons:**

- Not reachable outside the office (wihtout a VPN)
- No versioning or deduplication at the storage level
- No access control beyond what Windows file permissions give you
- You have to manage the `catalog.json` manually (or automate it via CI)

**Example `catalog.json`:**

```json
{
  "images": [
    {
      "id": "windows-mySoft",
      "name": "Windows + MySoftware",
      "description": "Windows Server with MySoftware pre-installed",
      "imageType": "Windows",
      "features": ["MySoftware", "Git"],
      "versions": [
        {
          "version": "1.0.0",
          "fileName": "win-mySoft-1.0.0.box",
          "sizeGb": 25.0,
          "notes": "Initial release"
        }
      ]
    }
  ]
}
```

---

### 2. OCI Registry (Zot, Azure Container Registry, GitHub Packages)

Images get pushed as OCI artifacts (using [ORAS](https://oras.land/)) and pulled via the standard OCI Distribution API. The registry uses content-addressable storage, so if you push the same blob twice (e.g. re-tag or re-push the same version) it only stores it once. Note that this is not the same as Docker-style layer dedup though. Since VM images are pushed as single archive blobs, two different VM versions are stored as separate blobs even if most of the content is the same.

|              |                                                              |
| ------------ | ------------------------------------------------------------ |
| **Setup**    | Deploy Zot (self-hosted) or use a cloud registry             |
| **Auth**     | Basic Auth (username/password)                               |
| **Speed**    | Good, HTTP streaming with resumable downloads                |
| **Cost**     | Free with Zot, pay-per-use with cloud options                |
| **Offline**  | No, needs network access                                     |
| **Best for** | Teams that need versioning, remote access, or CI integration |

**Self-hosted with Zot (good for on-prem)**

- Lightweight Go binary, runs on any server or in Docker
- OCI-native with content-addressable storage (identical blobs are stored once regardless of how many tags reference them)
- Supports deduplication, garbage collection, and storage optimization out of the box
- Supports image signing and verification (cosign/notation)
- Can do sub-path routing so you can run it behind a reverse proxy alongside other services
- Has a built-in web UI for browsing (with the search extension enabled)
- Free and open source
- Can run on-prem or on a cloud VM

**Important note on dedup:** Zot deduplicates at the blob level, meaning if two tags point to the exact same blob (same SHA256), its only stored once. However, since we currently push VM images as single large archive files, two different versions of a VM are two different blobs even if most of the content overlaps. To get cross-version dedup, we would need to split images into multiple OCI layers (e.g. base OS as one layer, application as another). That's not how it works today but could be added in the future.

**Cloud option: Azure Container Registry (ACR)**

- Fully managed, supports geo-replication
- Integrates with Azure AD
- Priced per day (Basic: 0.142€ Standard: 0.565€ Premium: 1.413€) but basic with additional storage should fill your needs at €0.00283/day per GB
- Makes sense for remote/hybrid teams that are already on Azure

**Pushing images from CI:**

```bash
# Package the VM as a .box archive
tar -czf my-image-1.0.0.box disk.vhdx metadata.json

# Push to registry using ORAS
oras push registry.example.com/vms/my-image:1.0.0 \
  my-image-1.0.0.box:application/vnd.vagrant.box \
  --annotation "org.opencontainers.image.title=My Image" \
  --annotation "org.opencontainers.image.description=Description here" \
  --annotation "org.opencontainers.image.version=1.0.0"
```

---

### 3. Nexus Repository (Raw)

Images get uploaded to a Nexus raw repository. VM Manager uses the Nexus REST API to discover whats available and downloads directly.

|              |                                                             |
| ------------ | ----------------------------------------------------------- |
| **Setup**    | Create a raw repo in your existing Nexus instance           |
| **Auth**     | Basic Auth with Nexus credentials                           |
| **Speed**    | Good, standard HTTP download                                |
| **Cost**     | Free if you already run Nexus                               |
| **Offline**  | No                                                          |
| **Best for** | Teams already using Nexus for other stuff (Java, npm, etc.) |

**Pros:**

- Nexus can not only be used for this specific product
- Supports metadata via `manifest.json` per version
- Access control through Nexus roles and privileges
- Has a web UI for browsing

**Cons:**

- Raw repositories dont deduplicate content like OCI does
- No content-addressable storage
- Some features like cleanup policies and replication need Nexus Pro

**Repository structure:**

```
nexus-repo/
  manifest.json                          # Top-level catalog
  versions/
    1.0.0/
      manifest.json                      # Version metadata
      win-mysoft-1.0.0.box         # VM archive
    1.1.0/
      manifest.json
      win-mysoft-1.1.0.box
```

---

## When to use what

| Scenario                                 | Backend                       | Why                                 |
| ---------------------------------------- | ----------------------------- | ----------------------------------- |
| Small team, same office, simple setup    | **Local/Network Path**        | Zero overhead, fastest on LAN       |
| Remote workers or hybrid setup           | **OCI Registry (Cloud)**      | Accessible from anywhere            |
| Already running Nexus                    | **Nexus Raw Repository**      | Reuse existing infra, no new tools  |
| Need versioning + dedup at storage level | **OCI Registry (Zot)**        | Content-addressable blob storage    |
| CI/CD should publish VM images           | **OCI Registry** or **Nexus** | Both have APIs for pushing          |
| Air-gapped or offline environment        | **Local/Network Path**        | No internet dependency              |
| Large team across multiple offices       | **OCI Registry (ACR/Zot)**    | Geo-replication, central management |

You can also combine backends. VM Manager merges catalogs from all configured sources. So for example you could have:

- **Nexus** for Linux container images (Docker-based)
- **OCI Registry** for Windows VM images (Hyper-V)
- **Local path** as a fast cache for the images people use most often

---

## Local Storage: Differencing Disks & Snapshots

### How Differencing Disks Work

VM Manager uses Hyper-V differencing disks (copy-on-write) so you dont have to duplicate the entire base image for every VM you create:

```
Base Image (read-only, shared)          <- 20 GB, stored once in extracted/
    |
    +-- VM-1 differencing disk          <- ~500 MB (only the changes)
    +-- VM-2 differencing disk          <- ~800 MB (only the changes)
    +-- VM-3 differencing disk          <- ~300 MB (only the changes)
```

**What this saves you:** If you create 5 VMs from the same 20 GB base image:

- Without differencing: 5 x 20 GB = **100 GB**
- With differencing: 20 GB + 5 x ~500 MB = **~22.5 GB**

The base VHDX in the `extracted/` folder is the parent. You should never modify it directly. Each VM gets a lightweight differencing disk that only records blocks that actually changed.

### How Snapshots Work

Hyper-V snapshots (also called checkpoints) extend the differencing chain further:

```
Base VHDX (parent, read-only)
    +-- VM.vhdx (differencing disk, changes since creation)
            +-- VM_snapshot1.avhdx (changes since snapshot 1)
                    +-- VM_snapshot2.avhdx (changes since snapshot 2)
```

Each snapshot is a delta. It only stores the blocks that changed since the previous point. So:

- Creating a snapshot takes seconds and is small (only changed blocks)
- You can have a bunch of snapshots without using proportional storage
- Restoring a snapshot is fast because it just repoints the disk chain

### Reset to Base Image

VM Manager also supports resetting a VM all the way back to its original state:

1. It deletes the differencing VHDX
2. Creates a fresh one pointing to the same parent

This is basically a factory reset. The VM goes back to the exact state it had when you first imported it, and you dont have to re-download anything.

### Snapshot Tips

| When                       | What to do                                                     |
| -------------------------- | -------------------------------------------------------------- |
| Before testing something   | Create a named snapshot like "Pre-test clean"                  |
| After installing software  | Snapshot so you can preserve the configured state              |
| Long-running VMs           | Clean up old snapshots periodically to free space              |
| Want to share your state   | Click "Clone" on a snapshot to create a new VM from it         |
| Want to distribute a state | Click "Push" on a snapshot to upload it to the feed it came from |

### Local Image Metadata (vmmanager.json)

When VM Manager extracts a base image, it writes a `vmmanager.json` file alongside the extracted VHDX. This file tracks where the image came from so that snapshot push can route to the correct feed:

```json
{
  "Name": "win-mySoft",
  "ParentImageId": "windows-mySoft",
  "ParentImageName": "Windows + MySoftware",
  "Version": "1.0.0",
  "FeedId": "a1b2c3...",
  "FeedUrl": "https://registry.example.com",
  "FeedRepository": "vms/windows-mySoft"
}
```

The `FeedId`, `FeedUrl`, and `FeedRepository` fields link the extracted image back to the feed it was downloaded from. These are used to determine the push target when sharing snapshots.

### Cleaning Up Storage

Snapshots grow over time as more blocks change. To keep things tidy:

- **Delete old snapshots** - Hyper-V merges the delta back into the parent disk
- **Reset to base** - Throws away all changes and starts fresh (~0 bytes delta)
- **Delete extracted images** - Remove base images you dont need anymore from the Marketplace page

---

## Deployment Setup for Our Use Case

### Architecture

```
                    +-------------------------+
                    |   Nexus Repository       |
                    |   (Linux images,         |
                    |    already in use)       |
                    +-----------+-------------+
                                |
    +---------------------------+---------------------------+
    |                           |                           |
    v                           v                           v
+---------+            +-------------+            +-------------+
| Dev PC 1 |           |  Dev PC 2   |            |  Dev PC 3   |
| (Office) |           |  (Remote)   |            |  (Office)   |
|          |           |             |            |             |
| Hyper-V  |           |  Hyper-V    |            |  Hyper-V    |
| VMs      |           |  VMs        |            |  VMs        |
+---------+            +-------------+            +-------------+
    |                                                  |
    +------------ LAN / Network Share -----------------+
                  (fast local cache for
                   office workers)
```

### VM Images setup example

For the Win + MySoftware + Git use case:

| Image        | Whats in it                                                                                    | Approx. Size |
| ------------ | ---------------------------------------------------------------------------------------------- | ------------ |
| `win-base`   | Windows Server + sample data                                                                   | ~15-20 GB    |
| `win-mySoft` | Everything above + MySoftware                                                                  | ~20-25 GB    |
| `win-dev`    | Everything above + VS Code + Claude extension + Git credentials (read-only) + Bitbucket access | ~22-28 GB    |

Each dev then creates a differencing disk from one of these bases. Their local changes only use about 2-5 GB per VM on top of the shared base.

### Git Credentials & Security

For VMs that come with Git access pre-configured:

- Use read-only deploy keys or app passwords scoped to read-only
- Store them in Windows Credential Manager inside the VM image
- Credentials are part of the base image so every VM gets them automatically

---

## Cost Overview

| Solution                            | Monthly Cost                                                                      | Storage                 | Remote Access         |
| ----------------------------------- | --------------------------------------------------------------------------------- | ----------------------- | --------------------- |
| Network share (existing NAS)        | $0                                                                                | Limited by NAS capacity | VPN only              |
| Zot on-prem (Docker)                | $0                                                                                | Limited by server       | VPN only              |
| Azure Container Registry (Basic)    | [See here](https://azure.microsoft.com/en-us/pricing/details/container-registry/) | Scalable                | Yes                   |
| Azure Container Registry (Standard) | [See here](https://azure.microsoft.com/en-us/pricing/details/container-registry/) | Geo-replication         | Yes                   |
| Nexus                               | $0                                                                                | Limited by server       | Depends on your setup |

### What makes sense when

- If everyone is in the same office and you just want something that works: **Network Share**. Free, fast, no setup beyond dropping files on a share.
- If you already run Nexus for other packages: **Nexus**. No new infrastructure, just create a raw repo and youre good.
- If you need remote access (via VPN) and dont want the overhead of Nexus: **Zot**
- If you need remote access or have people working from different locations: **OCI Registry in the cloud** (ACR or Zot on an Azure VM). Costs a bit but accessible from anywhere.
- If you want the best of both worlds: combine a **local share for speed** with a **cloud registry for availability**. VM Manager merges all configured sources automatically.

### What VM Manager supports today

- OCI registries (Zot, ACR, GHCR, or any OCI-compliant registry)
- Nexus raw repositories
- Local/network paths with `catalog.json`
- All three can be active at the same time

### What could be added in the future

- Azure Blob Storage with SAS token auth (cheap bulk storage, good for large images)
- S3-compatible storage (MinIO, AWS S3)
- Automatic image caching (download from cloud once, serve from local cache after that)
- Image compression/deduplication at the app level (on top of what the registry already does)
- Scheduled cleanup of old local images that havent been used in a while
