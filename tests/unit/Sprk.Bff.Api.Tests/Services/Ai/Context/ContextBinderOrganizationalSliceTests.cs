using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Context;

/// <summary>
/// spaarke-ai-architecture-redesign-r2 task AIR2-060 (FR-B-11): the Context Binder's Organizational
/// slice reads through <see cref="IOrganizationalContextProvider"/> — with no provider registered it is
/// an ADR-032 empty Null-Object default (never a failure), and with a real provider registered it
/// genuinely reflects that provider's result (proving the read is not hardcoded).
/// </summary>
public sealed class ContextBinderOrganizationalSliceTests
{
    private static ChatSessionManager BuildSessionManager() =>
        new(
            new InMemoryTenantCache(),
            Mock.Of<IChatDataverseRepository>(),
            Mock.Of<ILogger<ChatSessionManager>>());

    [Fact]
    public async Task BindAsync_WithNoOrganizationalProviderSupplied_ProducesEmptyNullObjectSlice()
    {
        // No IOrganizationalContextProvider passed — ContextBinder falls back to its internal
        // NullOrganizationalContextProvider default (ADR-032).
        var sut = new ContextBinder(BuildSessionManager(), Mock.Of<ILogger<ContextBinder>>());

        var bound = await sut.BindAsync(
            new ContextBindingRequest { CallerContactId = "contact-1" },
            CancellationToken.None);

        bound.Context.Organizational.Should().NotBeNull();
        bound.Context.Organizational!.ProviderImplemented.Should().BeFalse();
        bound.Context.Organizational.Meta.Present.Should().BeFalse();
        bound.Context.Organizational.Meta.ReferenceCount.Should().Be(0);
    }

    [Fact]
    public async Task BindAsync_WithExplicitNullObjectProvider_ProducesEmptyNullObjectSlice()
    {
        var sut = new ContextBinder(
            BuildSessionManager(),
            Mock.Of<ILogger<ContextBinder>>(),
            new NullOrganizationalContextProvider());

        var bound = await sut.BindAsync(
            new ContextBindingRequest { CallerContactId = "contact-1" },
            CancellationToken.None);

        bound.Context.Organizational!.ProviderImplemented.Should().BeFalse();
        bound.Context.Organizational.Meta.Present.Should().BeFalse();
    }

    [Fact]
    public async Task BindAsync_WithRegisteredProvider_ReadsOrganizationalSliceThroughTheInterface()
    {
        // A fake (non-Null-Object) provider — proves the Binder genuinely reads THROUGH the interface
        // rather than hardcoding an empty Organizational slice inline (spec FR-B-11 acceptance criterion 1).
        var fakeProvider = new FakeOrganizationalContextProvider(
            new OrganizationalContextResult { ProviderImplemented = true, ReferenceCount = 3 });

        var sut = new ContextBinder(BuildSessionManager(), Mock.Of<ILogger<ContextBinder>>(), fakeProvider);

        var bound = await sut.BindAsync(
            new ContextBindingRequest { CallerContactId = "contact-42" },
            CancellationToken.None);

        bound.Context.Organizational!.ProviderImplemented.Should().BeTrue();
        bound.Context.Organizational.Meta.Present.Should().BeTrue();
        bound.Context.Organizational.Meta.ReferenceCount.Should().Be(3);

        // The request the Binder forwarded carries the caller contact id it was given.
        fakeProvider.LastRequest.Should().NotBeNull();
        fakeProvider.LastRequest!.CallerContactId.Should().Be("contact-42");
    }

    private sealed class FakeOrganizationalContextProvider : IOrganizationalContextProvider
    {
        private readonly OrganizationalContextResult _result;

        public FakeOrganizationalContextProvider(OrganizationalContextResult result) => _result = result;

        public OrganizationalContextRequest? LastRequest { get; private set; }

        public Task<OrganizationalContextResult> GetContextAsync(
            OrganizationalContextRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }
}
