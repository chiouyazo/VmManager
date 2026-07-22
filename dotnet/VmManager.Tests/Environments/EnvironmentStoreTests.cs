using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VmManager.Agent.Services;
using VmManager.Contracts.Models;
using Xunit;

namespace VmManager.Tests.Environments;

public class EnvironmentStoreTests : IDisposable
{
    private readonly TestAppPaths _paths = new TestAppPaths();
    private readonly EnvironmentStore _store;

    public EnvironmentStoreTests()
    {
        _store = new EnvironmentStore(_paths, NullLogger<EnvironmentStore>.Instance);
    }

    public void Dispose() => _paths.Dispose();

    private static EnvironmentMetadata Env(string key, string vmName) =>
        new EnvironmentMetadata
        {
            Key = key,
            VmName = vmName,
            Owner = "owner@x.com",
            Status = EnvironmentStatus.Provisioning,
            CreatedAt = DateTime.UtcNow,
        };

    [Fact]
    public void Upsert_then_Get_roundtrips()
    {
        _store.Upsert(Env("pr-1", "pr-1"));

        EnvironmentMetadata? loaded = _store.Get("pr-1");

        loaded.Should().NotBeNull();
        loaded!.VmName.Should().Be("pr-1");
        loaded.Owner.Should().Be("owner@x.com");
    }

    [Fact]
    public void Get_is_case_insensitive()
    {
        _store.Upsert(Env("PR-2", "pr-2"));
        _store.Get("pr-2").Should().NotBeNull();
    }

    [Fact]
    public void Upsert_same_key_replaces_not_duplicates()
    {
        _store.Upsert(Env("pr-3", "pr-3"));
        EnvironmentMetadata updated = Env("pr-3", "pr-3");
        updated.Status = EnvironmentStatus.Ready;
        _store.Upsert(updated);

        _store
            .GetAll()
            .Count(e => string.Equals(e.Key, "pr-3", StringComparison.OrdinalIgnoreCase))
            .Should()
            .Be(1);
        _store.Get("pr-3")!.Status.Should().Be(EnvironmentStatus.Ready);
    }

    [Fact]
    public void GetByVmName_finds_environment()
    {
        _store.Upsert(Env("pr-4", "pr-4-vm"));
        _store.GetByVmName("pr-4-vm")!.Key.Should().Be("pr-4");
    }

    [Fact]
    public void Remove_deletes_environment()
    {
        _store.Upsert(Env("pr-5", "pr-5"));
        _store.Remove("pr-5");
        _store.Get("pr-5").Should().BeNull();
    }

    [Fact]
    public void GetAll_persists_across_instances()
    {
        _store.Upsert(Env("pr-6", "pr-6"));

        EnvironmentStore reopened = new(_paths, NullLogger<EnvironmentStore>.Instance);
        reopened.Get("pr-6").Should().NotBeNull();
    }
}
