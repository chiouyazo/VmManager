using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VmManager.Catalog.Nexus;
using VmManager.Contracts.Models;
using Xunit;
using Xunit.Abstractions;

namespace VmManager.Tests.Integration;

public class NexusConnectionTest
{
    private readonly ITestOutputHelper _output;

    public NexusConnectionTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ListRawRepos_ReturnsRepos()
    {
        List<string> repos = await NexusCatalogService.ListRawRepositoriesAsync(
            "nexusfeed",
            "admin",
            "asd"
        );

        _output.WriteLine($"Found {repos.Count} repos:");
        foreach (string repo in repos)
            _output.WriteLine($"  - {repo}");

        repos.Should().NotBeEmpty();
        repos.Should().Contain("Intern_VmImages");
    }

    [Fact]
    public async Task LoadCatalog_FromInternVmImages_FindsCrywin()
    {
        FeedConfiguration feed = new FeedConfiguration
        {
            Name = "Test Nexus",
            Type = FeedType.Nexus,
            Url = "bexzsFeed",
            Repository = "Intern_VmImages",
            Username = "admin",
            Password = "asd",
        };

        NexusCatalogService service = new NexusCatalogService(
            NullLogger<NexusCatalogService>.Instance
        );
        List<VmImage> images = await service.LoadCatalogAsync(feed);

        _output.WriteLine($"Found {images.Count} images:");
        foreach (VmImage img in images)
        {
            _output.WriteLine(
                $"  - {img.Id}: {img.Name} ({img.Versions.Count} versions, {img.UserSnapshots.Count} snapshots)"
            );
            foreach (VmImageVersion ver in img.Versions)
                _output.WriteLine($"    v{ver.Version} - {ver.SizeGb:F1} GB - {ver.FileName}");
        }

        images.Should().NotBeEmpty();
    }
}
