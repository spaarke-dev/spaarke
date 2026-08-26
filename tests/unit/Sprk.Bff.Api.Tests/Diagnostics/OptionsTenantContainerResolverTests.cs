// -----------------------------------------------------------------------------
// OptionsTenantContainerResolverTests.cs
//
// Unit tests for OptionsTenantContainerResolver (G-8 Batch 6 — customer-
// provisioning-orchestration-r1, fix #18). Verifies the §4D I4 discipline:
//   * happy path — tenant matches Graph:TenantId, container bound → resolution
//     with resolverSource="options", resolvedFromLiteral=false
//   * requested tenantId echoed VERBATIM (ordinal-safe for the L2 probe) even
//     when config casing differs
//   * foreign tenant → TenantNotServed (never another tenant's container)
//   * unpinned tenant scope (blank / "common" / "organizations") → TenantScopeNotPinned
//   * missing StagingContainerId → ContainerNotConfigured (NO fallback default)
//
// ADR-038: hand-rolled options monitors — no Mock<HttpMessageHandler>, no
// DI-registration tests, no ctor null-check tests.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Endpoints.Diagnostics;
using Xunit;

namespace Sprk.Bff.Api.Tests.Diagnostics;

public sealed class OptionsTenantContainerResolverTests
{
    private const string TenantId = "11111111-2222-3333-4444-555555555555";
    private const string ContainerId = "b!AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-abcdef";

    [Fact]
    public async Task ResolveAsync_TenantMatchesAndContainerBound_ReturnsOptionsResolutionNotFromLiteral()
    {
        var resolver = NewResolver(configuredTenantId: TenantId, stagingContainerId: ContainerId);

        var result = await resolver.ResolveAsync(TenantId, CancellationToken.None);

        result.Succeeded.Should().BeTrue(result.Diagnostic);
        var resolution = result.Resolution!;
        resolution.TenantId.Should().Be(TenantId);
        resolution.ContainerId.Should().Be(ContainerId);
        resolution.ResolverSource.Should().Be("options", "the L2 probe's documented enum is kv|options|env");
        resolution.ResolvedFromLiteral.Should().BeFalse(
            "the value derives from boot-time config binding — resolvedFromLiteral=true is CATASTROPHIC to the I4 probe");
    }

    [Fact]
    public async Task ResolveAsync_ConfigCasingDiffers_EchoesRequestedTenantIdVerbatim()
    {
        // The L2 probe compares the echoed tenantId ORDINALLY against its request
        // value. GUID casing is not semantic — a config value stored uppercase
        // must not cause a false CATASTROPHIC mismatch.
        var resolver = NewResolver(
            configuredTenantId: TenantId.ToUpperInvariant(),
            stagingContainerId: ContainerId);

        var result = await resolver.ResolveAsync(TenantId, CancellationToken.None);

        result.Succeeded.Should().BeTrue(result.Diagnostic);
        result.Resolution!.TenantId.Should().Be(
            TenantId, "the requested tenantId must be echoed verbatim (ordinal-safe for the probe)");
    }

    [Fact]
    public async Task ResolveAsync_ForeignTenant_FailsTenantNotServed_NeverReturnsConfiguredContainer()
    {
        // §4D I4: returning the configured container for a tenant this stamp does
        // NOT serve would BE the cross-tenant SPE leak the invariant catches.
        var resolver = NewResolver(configuredTenantId: TenantId, stagingContainerId: ContainerId);

        var result = await resolver.ResolveAsync(
            "99999999-8888-7777-6666-555555555555", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureCode.Should().Be(TenantContainerResolutionFailureCode.TenantNotServed);
        result.Resolution.Should().BeNull("no container id may leak on the failure path");
        result.Diagnostic.Should().NotContain(ContainerId, "the diagnostic must not echo the container id");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("common")]
    [InlineData("COMMON")]
    [InlineData("organizations")]
    [InlineData("consumers")]
    public async Task ResolveAsync_UnpinnedTenantScope_FailsTenantScopeNotPinned(string configuredTenantId)
    {
        var resolver = NewResolver(configuredTenantId, stagingContainerId: ContainerId);

        var result = await resolver.ResolveAsync(TenantId, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureCode.Should().Be(TenantContainerResolutionFailureCode.TenantScopeNotPinned);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveAsync_ContainerNotConfigured_FailsWithoutFallbackDefault(string? stagingContainerId)
    {
        // The load-bearing I4 assertion: a missing binding FAILS — it never
        // substitutes any default container id.
        var resolver = NewResolver(configuredTenantId: TenantId, stagingContainerId: stagingContainerId);

        var result = await resolver.ResolveAsync(TenantId, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureCode.Should().Be(TenantContainerResolutionFailureCode.ContainerNotConfigured);
        result.Resolution.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ContainerIdWithWhitespacePadding_ReturnsTrimmedValue()
    {
        var resolver = NewResolver(configuredTenantId: TenantId, stagingContainerId: $"  {ContainerId}  ");

        var result = await resolver.ResolveAsync(TenantId, CancellationToken.None);

        result.Succeeded.Should().BeTrue(result.Diagnostic);
        result.Resolution!.ContainerId.Should().Be(
            ContainerId, "padding must not break the probe's canonical 'b!…' shape regex");
    }

    // ---------------------------------------------------------------- helpers

    private static OptionsTenantContainerResolver NewResolver(
        string configuredTenantId, string? stagingContainerId)
        => new(
            new StaticOptionsMonitor<GraphOptions>(new GraphOptions { TenantId = configuredTenantId }),
            new StaticOptionsMonitor<SharePointEmbeddedOptions>(
                new SharePointEmbeddedOptions { StagingContainerId = stagingContainerId }),
            NullLogger<OptionsTenantContainerResolver>.Instance);

    /// <summary>Hand-rolled fixed-value IOptionsMonitor (ADR-038 — no mocking framework).</summary>
    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value) => CurrentValue = value;

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
