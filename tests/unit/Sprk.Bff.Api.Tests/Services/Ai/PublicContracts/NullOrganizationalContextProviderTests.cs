using System.Reflection;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PublicContracts;

/// <summary>
/// spaarke-ai-architecture-redesign-r2 task AIR2-060 (FR-B-11): the read-only INBOUND
/// Organizational-scope provider seam. Covers the ADR-032 Null-Object default behavior AND the
/// structural NEGATIVE — the interface must expose no outbound/push path (no Spaarke-as-MCP-server
/// surface).
/// </summary>
public sealed class NullOrganizationalContextProviderTests
{
    [Fact]
    public async Task GetContextAsync_WhenNoProviderRegistered_ReturnsEmptyResultNotAFailure()
    {
        var sut = new NullOrganizationalContextProvider();

        var result = await sut.GetContextAsync(
            new OrganizationalContextRequest { CallerContactId = "contact-123" },
            CancellationToken.None);

        // ADR-032: absence of a registered provider is the everyday default, never a thrown failure.
        result.ProviderImplemented.Should().BeFalse();
        result.ReferenceCount.Should().Be(0);
        result.Should().BeEquivalentTo(OrganizationalContextResult.Empty);
    }

    [Fact]
    public async Task GetContextAsync_WithNullCallerContactId_StillReturnsEmptyResultNotAFailure()
    {
        var sut = new NullOrganizationalContextProvider();

        var result = await sut.GetContextAsync(
            new OrganizationalContextRequest { CallerContactId = null },
            CancellationToken.None);

        result.ProviderImplemented.Should().BeFalse();
        result.ReferenceCount.Should().Be(0);
    }

    /// <summary>
    /// NEGATIVE (acceptance criterion): the interface is read-only INBOUND — Spaarke receives
    /// organizational context, it never pushes it out. This asserts the interface's public method
    /// surface is exactly the one inbound read, and that no member name suggests an outbound/push/
    /// publish/notify/send capability (which would make this an accidental Spaarke-as-MCP-server
    /// surface — explicitly out of scope per the task's background).
    /// </summary>
    [Fact]
    public void IOrganizationalContextProvider_ExposesOnlyTheInboundReadNoOutboundPushMethod()
    {
        var interfaceType = typeof(IOrganizationalContextProvider);
        var methods = interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

        methods.Should().HaveCount(1, "the interface must expose exactly one inbound read method");
        methods.Single().Name.Should().Be(nameof(IOrganizationalContextProvider.GetContextAsync));

        var outboundVerbs = new[] { "Send", "Push", "Publish", "Notify", "Emit", "Post", "Dispatch", "Broadcast" };
        foreach (var method in methods)
        {
            foreach (var verb in outboundVerbs)
            {
                method.Name.Should().NotContain(
                    verb,
                    $"method '{method.Name}' must not carry an outbound-shaped verb ('{verb}') — " +
                    "this seam is read-only INBOUND; no Spaarke-as-MCP-server surface.");
            }
        }
    }
}
