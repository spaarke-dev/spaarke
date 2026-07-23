using FluentAssertions;
using Sprk.Bff.Api.Configuration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Resolution tests for the ADR-018 per-tenant auto-file kill-switch (R4 FR-11). Protects the
/// global-vs-tenant precedence contract that governs whether the engine auto-files.
/// </summary>
public class AutoFileGateTests
{
    [Fact]
    public void Resolve_NoTenantKey_ReturnsGlobalDefaults()
    {
        var gate = AssociationTestSupport.Gate(enabled: true, threshold: 0.85);

        var settings = gate.Resolve(tenantKey: null);

        settings.Enabled.Should().BeTrue();
        settings.Threshold.Should().Be(0.85);
    }

    [Fact]
    public void Resolve_UnknownTenantKey_FallsBackToGlobal()
    {
        var gate = AssociationTestSupport.Gate(enabled: false, threshold: 0.70);

        var settings = gate.Resolve(tenantKey: "no-such-tenant");

        settings.Enabled.Should().BeFalse();
        settings.Threshold.Should().Be(0.70);
    }

    [Fact]
    public void Resolve_TenantEnabledOverride_ReplacesGlobalFlagOnly()
    {
        var gate = AssociationTestSupport.Gate(
            enabled: true, threshold: 0.85,
            tenants: new Dictionary<string, AutoFileTenantOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["tenant-a"] = new AutoFileTenantOverride { Enabled = false },
            });

        var settings = gate.Resolve("tenant-a");

        settings.Enabled.Should().BeFalse();
        settings.Threshold.Should().Be(0.85); // threshold inherits global
    }

    [Fact]
    public void Resolve_TenantThresholdOverride_ReplacesGlobalThresholdOnly()
    {
        var gate = AssociationTestSupport.Gate(
            enabled: true, threshold: 0.85,
            tenants: new Dictionary<string, AutoFileTenantOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["tenant-a"] = new AutoFileTenantOverride { Threshold = 0.95 },
            });

        var settings = gate.Resolve("tenant-a");

        settings.Enabled.Should().BeTrue(); // flag inherits global
        settings.Threshold.Should().Be(0.95);
    }
}
