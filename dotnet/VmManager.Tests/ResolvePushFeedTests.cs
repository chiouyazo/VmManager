using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VmManager.Agent.Services;
using VmManager.Contracts.Models;
using Xunit;

namespace VmManager.Tests;

public class ResolvePushFeedTests
{
    private readonly FeedResolutionService _service;

    public ResolvePushFeedTests()
    {
        _service = new FeedResolutionService(NullLogger<FeedResolutionService>.Instance);
    }

    [Fact]
    public void NullOrigin_ReturnsNull()
    {
        AppSettings settings = new AppSettings();
        settings.Feeds.Add(MakeFeed(FeedType.Nexus, "https://nexus.example.com", "repo"));

        FeedConfiguration? result = _service.ResolvePushFeed(null, settings);

        result.Should().BeNull();
    }

    [Fact]
    public void EmptyFeedId_ReturnsNull()
    {
        AppSettings settings = new AppSettings();
        settings.Feeds.Add(MakeFeed(FeedType.Nexus, "https://nexus.example.com", "repo"));

        VmOrigin origin = new VmOrigin { FeedId = "" };
        FeedConfiguration? result = _service.ResolvePushFeed(origin, settings);

        result.Should().BeNull();
    }

    [Fact]
    public void MatchesByFeedId()
    {
        string id = FeedConfiguration.ComputeId(
            FeedType.Nexus,
            "https://nexus.example.com",
            "repo"
        );
        AppSettings settings = new AppSettings();
        settings.Feeds.Add(MakeFeed(FeedType.Nexus, "https://nexus.example.com", "repo"));

        VmOrigin origin = new VmOrigin { FeedId = id };
        FeedConfiguration? result = _service.ResolvePushFeed(origin, settings);

        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.Repository.Should().Be("repo");
    }

    [Fact]
    public void FallsBackToUrlMatch_ForAutoDiscoveredRepo()
    {
        string parentId = FeedConfiguration.ComputeId(
            FeedType.Nexus,
            "https://nexus.example.com",
            ""
        );
        string repoId = FeedConfiguration.ComputeId(
            FeedType.Nexus,
            "https://nexus.example.com",
            "vm-images"
        );

        AppSettings settings = new AppSettings();
        FeedConfiguration parentFeed = MakeFeed(FeedType.Nexus, "https://nexus.example.com", "");
        parentFeed.Username = "admin";
        parentFeed.Password = "secret";
        settings.Feeds.Add(parentFeed);

        VmOrigin origin = new VmOrigin
        {
            FeedId = repoId,
            FeedUrl = "https://nexus.example.com",
            Repository = "vm-images",
        };

        parentId.Should().NotBe(repoId);

        FeedConfiguration? result = _service.ResolvePushFeed(origin, settings);

        result.Should().NotBeNull();
        result!.Repository.Should().Be("vm-images");
        result.Username.Should().Be("admin");
        result.Password.Should().Be("secret");
        result.Url.Should().Be("https://nexus.example.com");
    }

    [Fact]
    public void UrlMatchPreservesOriginRepository_NotParentEmpty()
    {
        AppSettings settings = new AppSettings();
        settings.Feeds.Add(MakeFeed(FeedType.Nexus, "https://nexus.example.com", ""));

        string repoId = FeedConfiguration.ComputeId(
            FeedType.Nexus,
            "https://nexus.example.com",
            "specific-repo"
        );
        VmOrigin origin = new VmOrigin
        {
            FeedId = repoId,
            FeedUrl = "https://nexus.example.com",
            Repository = "specific-repo",
        };

        FeedConfiguration? result = _service.ResolvePushFeed(origin, settings);

        result.Should().NotBeNull();
        result!.Repository.Should().Be("specific-repo");
    }

    [Fact]
    public void NoMatch_ReturnsNull()
    {
        AppSettings settings = new AppSettings();
        settings.Feeds.Add(MakeFeed(FeedType.Nexus, "https://other-server.com", "repo"));

        VmOrigin origin = new VmOrigin
        {
            FeedId = "nonexistent",
            FeedUrl = "https://nexus.example.com",
            Repository = "vm-images",
        };

        FeedConfiguration? result = _service.ResolvePushFeed(origin, settings);

        result.Should().BeNull();
    }

    [Fact]
    public void ComputeId_EmptyRepoAndNamedRepo_AreDifferent()
    {
        string emptyRepoId = FeedConfiguration.ComputeId(
            FeedType.Nexus,
            "https://nexus.example.com",
            ""
        );
        string namedRepoId = FeedConfiguration.ComputeId(
            FeedType.Nexus,
            "https://nexus.example.com",
            "vm-images"
        );

        emptyRepoId
            .Should()
            .NotBe(
                namedRepoId,
                "auto-discovered repos must have different IDs than the parent feed with empty repo"
            );
    }

    private static FeedConfiguration MakeFeed(FeedType type, string url, string? repo)
    {
        return new FeedConfiguration
        {
            Id = FeedConfiguration.ComputeId(type, url, repo),
            Name = "Test Feed",
            Type = type,
            Url = url,
            Repository = repo,
        };
    }
}
