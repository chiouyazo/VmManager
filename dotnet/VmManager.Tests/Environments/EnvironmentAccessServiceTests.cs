using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VmManager.Agent.Services;
using VmManager.Catalog.Shared;
using VmManager.Contracts.Models;
using Xunit;

namespace VmManager.Tests.Environments;

public class EnvironmentAccessServiceTests : IDisposable
{
    private readonly TestAppPaths _paths = new TestAppPaths();
    private readonly UserService _users;
    private readonly VmSharingService _sharing;
    private readonly VmOwnershipService _ownership;
    private readonly EnvironmentAccessService _access;

    public EnvironmentAccessServiceTests()
    {
        _users = new UserService(_paths, NullLogger<UserService>.Instance);
        _sharing = new VmSharingService(_paths, NullLogger<VmSharingService>.Instance);
        _ownership = new VmOwnershipService(_paths, NullLogger<VmOwnershipService>.Instance);
        EmailService email = new(new SettingsService(_paths), NullLogger<EmailService>.Instance);
        _access = new EnvironmentAccessService(
            _users,
            _sharing,
            _ownership,
            email,
            NullLogger<EnvironmentAccessService>.Instance
        );
    }

    public void Dispose() => _paths.Dispose();

    [Fact]
    public async Task GrantAccess_sets_owner_and_shares_with_non_owner_emails()
    {
        _users.CreateUser(
            "existing@x.com",
            "pw-Existing9!",
            [.. Permission.DefaultUser],
            isAdmin: false
        );

        await _access.GrantAccessAsync(
            "pr-7",
            "owner@x.com",
            ["owner@x.com", "existing@x.com", "new@x.com"]
        );

        _ownership.GetOwner("pr-7").Should().Be("owner@x.com");

        List<VmShareEntry> shares = _sharing.GetSharesForVm("pr-7");
        shares
            .Select(s => s.SharedWithUsername)
            .Should()
            .BeEquivalentTo(["existing@x.com", "new@x.com"]); // owner is not self-shared

        shares.Should().OnlyContain(s => s.GrantedPermissions.Contains(Permission.RdpConnect));
    }

    [Fact]
    public async Task GrantAccess_auto_creates_unknown_users()
    {
        _users.GetByUsername("new@x.com").Should().BeNull();

        await _access.GrantAccessAsync("pr-8", "owner@x.com", ["new@x.com"]);

        UserModelExistsCheck("new@x.com");
        _users.GetByUsername("new@x.com")!.MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task GrantAccess_ignores_invalid_emails()
    {
        await _access.GrantAccessAsync("pr-9", "owner@x.com", ["not-an-email", ""]);
        _sharing.GetSharesForVm("pr-9").Should().BeEmpty();
    }

    [Fact]
    public async Task RevokeAccess_removes_all_shares()
    {
        await _access.GrantAccessAsync("pr-10", "owner@x.com", ["a@x.com", "b@x.com"]);
        _sharing.GetSharesForVm("pr-10").Should().HaveCount(2);

        _access.RevokeAccess("pr-10");

        _sharing.GetSharesForVm("pr-10").Should().BeEmpty();
    }

    private void UserModelExistsCheck(string email) =>
        _users.GetByUsername(email).Should().NotBeNull();
}
