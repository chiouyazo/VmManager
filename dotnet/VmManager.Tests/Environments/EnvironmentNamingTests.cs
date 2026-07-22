using FluentAssertions;
using VmManager.Agent.Services;
using Xunit;

namespace VmManager.Tests.Environments;

public class EnvironmentNamingTests
{
    [Theory]
    [InlineData("pr-123", "pr-123")]
    [InlineData("PR-123", "pr-123")]
    [InlineData("feature/cool_thing", "feature-cool-thing")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("--edges--", "edges")]
    public void SanitizeVmName_produces_safe_names(string input, string expected)
    {
        EnvironmentService.SanitizeVmName(input).Should().Be(expected);
    }

    [Fact]
    public void SanitizeVmName_falls_back_when_empty()
    {
        EnvironmentService.SanitizeVmName("///").Should().Be("env");
    }

    [Fact]
    public void SanitizeVmName_truncates_long_keys()
    {
        string input = new string('a', 100);
        EnvironmentService.SanitizeVmName(input).Length.Should().BeLessThanOrEqualTo(60);
    }
}
