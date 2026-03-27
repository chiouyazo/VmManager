using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VmManager.Contracts.Interfaces;
using VmManager.Contracts.Models;
using VmManager.Services;
using Xunit;

namespace VmManager.Tests;

public class OriginTrackingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ILocalImageMetadataService _metadataService;
    private readonly IVmTrackingService _trackingService;

    public OriginTrackingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vmm_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _metadataService = new LocalImageMetadataService(
            NullLogger<LocalImageMetadataService>.Instance
        );
        _trackingService = new VmTrackingService(
            new AppPaths(),
            NullLogger<VmTrackingService>.Instance
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void SaveLocalImageMetadata_IncludesFeedFields()
    {
        VmImageVersion version = new VmImageVersion
        {
            Version = "1.0",
            ParentImageId = "nexus:crywin",
            ParentImageName = "Crywin",
            FeedId = "abc123",
            FeedUrl = "https://nexus.example.com:8002",
            FeedRepository = "Intern_VmImages",
        };

        _metadataService.SaveMetadata(_tempDir, version);

        string metadataFile = Path.Combine(_tempDir, "vmmanager.json");
        File.Exists(metadataFile).Should().BeTrue();

        JsonDocument doc = JsonDocument.Parse(File.ReadAllText(metadataFile));
        JsonElement root = doc.RootElement;

        root.GetProperty("feedId").GetString().Should().Be("abc123");
        root.GetProperty("feedUrl").GetString().Should().Be("https://nexus.example.com:8002");
        root.GetProperty("feedRepository").GetString().Should().Be("Intern_VmImages");
        root.GetProperty("parentImageId").GetString().Should().Be("nexus:crywin");
        root.GetProperty("parentImageName").GetString().Should().Be("Crywin");
        root.GetProperty("version").GetString().Should().Be("1.0");
    }

    [Fact]
    public void SaveLocalImageMetadata_HandlesEmptyFeedFields()
    {
        VmImageVersion version = new VmImageVersion
        {
            Version = "2.0",
            ParentImageId = "local:myvm",
            ParentImageName = "My VM",
        };

        _metadataService.SaveMetadata(_tempDir, version);

        string metadataFile = Path.Combine(_tempDir, "vmmanager.json");
        JsonDocument doc = JsonDocument.Parse(File.ReadAllText(metadataFile));
        JsonElement root = doc.RootElement;

        // Feed fields should be present but empty
        root.GetProperty("feedId").GetString().Should().BeEmpty();
        root.GetProperty("feedUrl").GetString().Should().BeEmpty();
    }

    [Fact]
    public void TrackManagedVm_PersistsAndLoadsOrigin()
    {
        string vmName = $"test-vm-{Guid.NewGuid():N}";
        VmOrigin origin = new VmOrigin
        {
            ImageId = "nexus:testimage",
            ImageName = "Test Image",
            Version = "1.0",
            FeedId = "feedhash123",
            FeedUrl = "https://nexus.example.com",
            Repository = "vm-images",
        };

        try
        {
            _trackingService.TrackVm(vmName, origin);
            VmOrigin? loaded = _trackingService.GetOrigin(vmName);

            loaded.Should().NotBeNull();
            loaded!.ImageId.Should().Be("nexus:testimage");
            loaded.ImageName.Should().Be("Test Image");
            loaded.Version.Should().Be("1.0");
            loaded.FeedId.Should().Be("feedhash123");
            loaded.FeedUrl.Should().Be("https://nexus.example.com");
            loaded.Repository.Should().Be("vm-images");
        }
        finally
        {
            // Clean up: remove our test entry
            _trackingService.TrackVm(vmName, null);
        }
    }

    [Fact]
    public void TrackManagedVm_NullOriginStoresNull()
    {
        string vmName = $"test-vm-null-{Guid.NewGuid():N}";

        try
        {
            _trackingService.TrackVm(vmName, null);
            VmOrigin? loaded = _trackingService.GetOrigin(vmName);

            loaded.Should().BeNull();
        }
        finally
        {
            _trackingService.TrackVm(vmName, null);
        }
    }
}
