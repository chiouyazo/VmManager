using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VmManager.Agent.Services;
using VmManager.Catalog.Shared;
using VmManager.Contracts.Models;
using Xunit;

namespace VmManager.Tests.Environments;

public class EnvironmentCleanupTests : IDisposable
{
    private readonly TestAppPaths _paths = new TestAppPaths();
    private readonly EnvironmentStore _store;
    private readonly EnvironmentService _service;

    public EnvironmentCleanupTests()
    {
        FakeVmBackend backend = new FakeVmBackend();
        FakeIpResolver ip = new FakeIpResolver();
        BackgroundTaskManager tasks = new(NullLogger<BackgroundTaskManager>.Instance);
        SettingsService settings = new(_paths);
        VmTrackingService vmTracking = new(_paths, NullLogger<VmTrackingService>.Instance);
        LocalImageMetadataService localImages = new(NullLogger<LocalImageMetadataService>.Instance);
        VmOwnershipService ownership = new(_paths, NullLogger<VmOwnershipService>.Instance);
        _store = new EnvironmentStore(_paths, NullLogger<EnvironmentStore>.Instance);
        UserService users = new(_paths, NullLogger<UserService>.Instance);
        VmSharingService sharing = new(_paths, NullLogger<VmSharingService>.Instance);
        EmailService email = new(settings, NullLogger<EmailService>.Instance);
        EnvironmentAccessService access = new(
            users,
            sharing,
            ownership,
            email,
            NullLogger<EnvironmentAccessService>.Instance
        );
        EnvironmentProvisioner provisioner = new(NullLogger<EnvironmentProvisioner>.Instance);
        QuotaService quota = new(
            users,
            ownership,
            settings,
            email,
            backend,
            NullLogger<QuotaService>.Instance
        );

        _service = new EnvironmentService(
            backend,
            ip,
            tasks,
            settings,
            vmTracking,
            localImages,
            ownership,
            _store,
            access,
            provisioner,
            quota,
            email,
            users,
            NullLogger<EnvironmentService>.Instance
        );
    }

    public void Dispose() => _paths.Dispose();

    private EnvironmentMetadata Add(
        string key,
        string vmName,
        EnvironmentStatus status,
        DateTime? expiresAt
    )
    {
        EnvironmentMetadata env = new EnvironmentMetadata
        {
            Key = key,
            VmName = vmName, // names matching FakeVmBackend are treated as "live"
            Owner = "owner@x.com",
            Status = status,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            ExpiresAt = expiresAt,
        };
        _store.Upsert(env);
        return env;
    }

    [Fact]
    public async Task CleanupAsync_deletes_expired_environment()
    {
        Add("pr-exp", "Test-Server", EnvironmentStatus.Ready, DateTime.UtcNow.AddMinutes(-5));

        int deleted = await _service.CleanupAsync(warnLeadMinutes: 120);

        deleted.Should().Be(1);
        _store.Get("pr-exp").Should().BeNull();
    }

    [Fact]
    public async Task CleanupAsync_flags_soon_to_expire_as_Expiring()
    {
        Add("pr-soon", "Build-Agent", EnvironmentStatus.Ready, DateTime.UtcNow.AddMinutes(30));

        await _service.CleanupAsync(warnLeadMinutes: 120);

        EnvironmentMetadata? env = _store.Get("pr-soon");
        env.Should().NotBeNull();
        env!.Status.Should().Be(EnvironmentStatus.Expiring);
    }

    [Fact]
    public async Task CleanupAsync_leaves_healthy_environment_untouched()
    {
        Add("pr-ok", "Staging", EnvironmentStatus.Ready, DateTime.UtcNow.AddHours(10));

        await _service.CleanupAsync(warnLeadMinutes: 120);

        EnvironmentMetadata? env = _store.Get("pr-ok");
        env.Should().NotBeNull();
        env!.Status.Should().Be(EnvironmentStatus.Ready);
    }

    [Fact]
    public async Task CleanupAsync_reconciles_orphaned_environment()
    {
        // VM name not present in FakeVmBackend -> treated as orphan.
        Add(
            "pr-orphan",
            "vm-that-does-not-exist",
            EnvironmentStatus.Ready,
            DateTime.UtcNow.AddHours(10)
        );

        await _service.CleanupAsync(warnLeadMinutes: 120);

        _store.Get("pr-orphan").Should().BeNull();
    }
}
