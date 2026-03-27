using FluentAssertions;
using VmManager.Contracts.Models;
using Xunit;

namespace VmManager.Tests.Integration;

public class FeedConfigurationTests
{
    [Fact]
    public void ComputeId_IsDeterministic()
    {
        string id1 = FeedConfiguration.ComputeId(
            FeedType.Nexus,
            "https://nexus.example.com",
            "vm-images"
        );
        string id2 = FeedConfiguration.ComputeId(
            FeedType.Nexus,
            "https://nexus.example.com",
            "vm-images"
        );

        id1.Should().NotBeNullOrEmpty();
        id1.Should().Be(id2);
    }

    [Fact]
    public void ComputeId_DiffersForDifferentFeeds()
    {
        string id1 = FeedConfiguration.ComputeId(
            FeedType.Nexus,
            "https://nexus.example.com",
            "repo-a"
        );
        string id2 = FeedConfiguration.ComputeId(
            FeedType.Nexus,
            "https://nexus.example.com",
            "repo-b"
        );

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void ComputeId_IgnoresTrailingSlash()
    {
        string id1 = FeedConfiguration.ComputeId(
            FeedType.OCI,
            "https://registry.example.com/",
            null
        );
        string id2 = FeedConfiguration.ComputeId(
            FeedType.OCI,
            "https://registry.example.com",
            null
        );

        id1.Should().Be(id2);
    }

    [Fact]
    public void ComputeId_IsCaseInsensitive()
    {
        string id1 = FeedConfiguration.ComputeId(
            FeedType.Nexus,
            "https://Nexus.Example.COM",
            "Repo"
        );
        string id2 = FeedConfiguration.ComputeId(
            FeedType.Nexus,
            "https://nexus.example.com",
            "repo"
        );

        id1.Should().Be(id2);
    }

    [Fact]
    public void EnsureId_SetsIdWhenEmpty()
    {
        FeedConfiguration feed = new FeedConfiguration
        {
            Type = FeedType.Nexus,
            Url = "https://nexus.example.com",
            Repository = "vm-images",
        };

        feed.Id.Should().BeEmpty();
        feed.EnsureId();
        feed.Id.Should().NotBeNullOrEmpty();
        feed.Id.Should()
            .Be(
                FeedConfiguration.ComputeId(
                    FeedType.Nexus,
                    "https://nexus.example.com",
                    "vm-images"
                )
            );
    }

    [Fact]
    public void EnsureId_DoesNotOverwriteExistingId()
    {
        FeedConfiguration feed = new FeedConfiguration
        {
            Id = "custom-id",
            Type = FeedType.Nexus,
            Url = "https://nexus.example.com",
        };

        feed.EnsureId();
        feed.Id.Should().Be("custom-id");
    }

    [Fact]
    public void FeedConfiguration_DefaultTypeIsOci()
    {
        FeedConfiguration feed = new FeedConfiguration();
        feed.Type.Should().Be(FeedType.OCI);
    }

    [Fact]
    public void AppSettings_Feeds_StartsEmpty()
    {
        AppSettings settings = new AppSettings();
        settings.Feeds.Should().BeEmpty();
    }

    [Fact]
    public void AppSettings_CanAddMultipleFeeds()
    {
        AppSettings settings = new AppSettings();
        settings.Feeds.Add(
            new FeedConfiguration
            {
                Name = "OCI",
                Type = FeedType.OCI,
                Url = "https://registry.example.com",
            }
        );
        settings.Feeds.Add(
            new FeedConfiguration
            {
                Name = "Nexus",
                Type = FeedType.Nexus,
                Url = "https://nexus.example.com",
            }
        );
        settings.Feeds.Add(
            new FeedConfiguration
            {
                Name = "Local",
                Type = FeedType.Local,
                Url = @"C:\VMs\catalog",
            }
        );

        settings.Feeds.Should().HaveCount(3);
        settings.Feeds[0].Type.Should().Be(FeedType.OCI);
        settings.Feeds[1].Type.Should().Be(FeedType.Nexus);
        settings.Feeds[2].Type.Should().Be(FeedType.Local);
    }

    [Fact]
    public void FeedConfiguration_StoresCredentials()
    {
        FeedConfiguration feed = new FeedConfiguration
        {
            Name = "Test Nexus",
            Type = FeedType.Nexus,
            Url = "https://nexus.example.com",
            Repository = "vm-images",
            Username = "admin",
            Password = "secret",
        };

        feed.Username.Should().Be("admin");
        feed.Password.Should().Be("secret");
        feed.Repository.Should().Be("vm-images");
    }

    [Fact]
    public void AppSettings_HasCompletedSetup_DefaultsFalse()
    {
        AppSettings settings = new AppSettings();
        settings.HasCompletedSetup.Should().BeFalse();
    }

    [Fact]
    public void AppSettings_DefaultValues()
    {
        AppSettings settings = new AppSettings();
        settings.DefaultMemoryMb.Should().Be(4096);
        settings.DefaultCpuCount.Should().Be(4);
        settings.DefaultVmUsername.Should().Be("Administrator");
        settings.LocalVmPath.Should().Be(@"C:\VMs");
    }
}
