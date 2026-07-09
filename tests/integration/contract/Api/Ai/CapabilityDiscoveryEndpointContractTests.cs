using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Contract tests for the capability-discovery READ endpoint
/// <c>GET /api/ai/capabilities</c> (spec FR-A1-12, R1 — the gate-038 deferral,
/// <c>spaarke-ai-architecture-redesign-r2</c> task 041).
/// </summary>
/// <remarks>
/// <b>Hosting approach</b>: mirrors <see cref="DispatchSessionEndpointContractTests"/> —
/// a minimal in-process <see cref="WebApplication"/> mapping ONLY
/// <see cref="CapabilityDiscoveryEndpoints.MapCapabilityDiscoveryEndpoints"/> against a
/// mocked <see cref="IConsumerRoutingService"/> (module-boundary double per ADR-038),
/// reusing the sibling fixture's <see cref="SummarizeFakeAuthHandler"/>/<see cref="SummarizeFakeAuthOptions"/>.
/// </remarks>
public class CapabilityDiscoveryEndpointContractTests : IClassFixture<CapabilityDiscoveryEndpointTestFixture>
{
    private readonly CapabilityDiscoveryEndpointTestFixture _fx;

    public CapabilityDiscoveryEndpointContractTests(CapabilityDiscoveryEndpointTestFixture fx)
    {
        _fx = fx;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DETERMINISM — the endpoint returns the catalog projection verbatim, in
    // the SAME order the catalog read supplied (no re-sort, no invention).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_WithCatalogEntries_ReturnsCapabilitiesInCatalogOrder()
    {
        _fx.Reset();
        var first = _fx.BuildBinding(consumerType: "chat-summarize", toolDescription: "Summarize this document.");
        var second = _fx.BuildBinding(consumerType: "chat-draft", toolDescription: "Draft a response.");
        _fx.ConsumerRoutingMock
            .Setup(c => c.ListTextProjectableBindingsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { first, second });

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/ai/capabilities");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CapabilityDiscoveryResponse>();
        body.Should().NotBeNull();
        body!.Capabilities.Should().HaveCount(2);
        body.Capabilities[0].BindingId.Should().Be(first.BindingId,
            "the endpoint must preserve the catalog read's ordering (NFR-04) rather than re-sorting");
        body.Capabilities[1].BindingId.Should().Be(second.BindingId);
    }

    [Fact]
    public async Task Get_MultipleRequests_ReturnsSameOrder()
    {
        _fx.Reset();
        var a = _fx.BuildBinding(consumerType: "chat-summarize");
        var b = _fx.BuildBinding(consumerType: "chat-analyze");
        _fx.ConsumerRoutingMock
            .Setup(c => c.ListTextProjectableBindingsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { a, b });

        var client = _fx.CreateAuthenticatedClient();
        var response1 = await client.GetAsync("/api/ai/capabilities");
        var response2 = await client.GetAsync("/api/ai/capabilities");

        var body1 = await response1.Content.ReadFromJsonAsync<CapabilityDiscoveryResponse>();
        var body2 = await response2.Content.ReadFromJsonAsync<CapabilityDiscoveryResponse>();

        body1!.Capabilities.Select(c => c.BindingId).Should().Equal(
            body2!.Capabilities.Select(c => c.BindingId),
            "stable ordering must be reproducible across requests");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NEGATIVE — a capability NOT returned by the catalog read must NOT appear
    // (ADR-039: the endpoint has exactly one query path; it never invents or
    // supplements the closed-catalog projection).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_CapabilityNotInCatalog_DoesNotAppearInResponse()
    {
        _fx.Reset();
        var cataloged = _fx.BuildBinding(consumerType: "chat-summarize");
        var notCataloged = _fx.BuildBinding(consumerType: "invented-capability");
        // The routing service only returns `cataloged` — `notCataloged` was never
        // supplied by the catalog read and must not surface via any other path.
        _fx.ConsumerRoutingMock
            .Setup(c => c.ListTextProjectableBindingsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { cataloged });

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/ai/capabilities");

        var body = await response.Content.ReadFromJsonAsync<CapabilityDiscoveryResponse>();
        body!.Capabilities.Should().ContainSingle(c => c.BindingId == cataloged.BindingId);
        body.Capabilities.Should().NotContain(c => c.BindingId == notCataloged.BindingId,
            "the endpoint must never surface a capability the catalog read did not return");
        body.Capabilities.Should().NotContain(c => c.ConsumerType == "invented-capability");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SURFACE FILTER — "capabilities the caller may launch": a Binding scoped
    // to a surface other than the requested one is excluded; an unscoped
    // Binding (empty Surfaces) is offered on every surface.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_DefaultSurface_ExcludesBindingScopedToDifferentSurface()
    {
        _fx.Reset();
        var assistantScoped = _fx.BuildBinding(consumerType: "chat-summarize", surfaces: new[] { "assistant" });
        var recordFormOnly = _fx.BuildBinding(consumerType: "record-form-only", surfaces: new[] { "record-form" });
        _fx.ConsumerRoutingMock
            .Setup(c => c.ListTextProjectableBindingsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { assistantScoped, recordFormOnly });

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/ai/capabilities"); // default surface = "assistant"

        var body = await response.Content.ReadFromJsonAsync<CapabilityDiscoveryResponse>();
        body!.Capabilities.Should().ContainSingle(c => c.BindingId == assistantScoped.BindingId);
        body.Capabilities.Should().NotContain(c => c.BindingId == recordFormOnly.BindingId,
            "a Binding scoped ONLY to record-form is not launchable from the assistant soft-slash surface");
    }

    [Fact]
    public async Task Get_UnscopedBinding_OfferedOnAllSurfaces()
    {
        _fx.Reset();
        var unscoped = _fx.BuildBinding(consumerType: "chat-summarize", surfaces: Array.Empty<string>());
        _fx.ConsumerRoutingMock
            .Setup(c => c.ListTextProjectableBindingsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { unscoped });

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/ai/capabilities?surface=office");

        var body = await response.Content.ReadFromJsonAsync<CapabilityDiscoveryResponse>();
        body!.Capabilities.Should().ContainSingle(c => c.BindingId == unscoped.BindingId,
            "empty Surfaces means offered on ALL surfaces per the sprk_surfaces column dictionary");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NO CATALOG INTERNALS / NO VOLATILE PER-REQUEST FIELDS — the projection is
    // a purpose-built launcher shape, not the raw Binding contract.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ResponseShape_ContainsOnlyLauncherFields_NoCatalogInternalsOrTimestamps()
    {
        _fx.Reset();
        var binding = _fx.BuildBinding(consumerType: "chat-summarize");
        _fx.ConsumerRoutingMock
            .Setup(c => c.ListTextProjectableBindingsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { binding });

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/ai/capabilities");

        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        var entry = doc.RootElement.GetProperty("capabilities")[0];

        var propertyNames = entry.EnumerateObject().Select(p => p.Name).ToArray();
        propertyNames.Should().BeEquivalentTo(new[]
        {
            "bindingId", "consumerType", "consumerCode", "displayLabel", "surfaces", "launchArgsSchemaJson",
        }, "the launcher projection must expose ONLY launch metadata — no per-request timestamps/correlation " +
           "ids and no catalog internals (match conditions, workflow class, chip transitions, risk, priority)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AUTH — requires authentication (root CLAUDE.md §9: every endpoint requires
    // auth except /healthz, /ping).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        _fx.Reset();
        var client = _fx.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/api/ai/capabilities");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_EmptyCatalog_ReturnsEmptyArray()
    {
        _fx.Reset();
        _fx.ConsumerRoutingMock
            .Setup(c => c.ListTextProjectableBindingsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Binding>());

        var client = _fx.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/ai/capabilities");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CapabilityDiscoveryResponse>();
        body!.Capabilities.Should().BeEmpty();
    }
}

/// <summary>
/// Test fixture for <see cref="CapabilityDiscoveryEndpointContractTests"/>. Hosts a
/// minimal <see cref="WebApplication"/> mapping ONLY
/// <see cref="CapabilityDiscoveryEndpoints.MapCapabilityDiscoveryEndpoints"/> against a
/// mocked <see cref="IConsumerRoutingService"/> (module-boundary double per ADR-038).
/// Reuses <see cref="SummarizeFakeAuthHandler"/>/<see cref="SummarizeFakeAuthOptions"/>
/// from the sibling summarize-endpoint fixture (same test-project namespace).
/// </summary>
public sealed class CapabilityDiscoveryEndpointTestFixture : IAsyncLifetime, IDisposable
{
    public Mock<IConsumerRoutingService> ConsumerRoutingMock { get; } = new();

    private WebApplication? _app;
    private int _bindingCounter;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
        });

        builder.Logging.ClearProviders();

        builder.Services
            .AddSingleton(new SummarizeFakeAuthOptions(includeTid: true))
            .AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = SummarizeFakeAuthHandler.SchemeName;
                o.DefaultChallengeScheme = SummarizeFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, SummarizeFakeAuthHandler>(
                SummarizeFakeAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();

        builder.Services.AddSingleton(ConsumerRoutingMock.Object);

        builder.WebHost.UseTestServer();

        _app = builder.Build();

        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapCapabilityDiscoveryEndpoints();

        await _app.StartAsync();
    }

    public Task DisposeAsync() => _app?.StopAsync() ?? Task.CompletedTask;

    public void Dispose() => _app?.DisposeAsync().AsTask().GetAwaiter().GetResult();

    public void Reset()
    {
        ConsumerRoutingMock.Reset();
        _bindingCounter = 0;
    }

    /// <summary>Builds a deterministic, uniquely-identified Binding for test setup.</summary>
    public Binding BuildBinding(
        string consumerType,
        string? toolDescription = "Do the thing.",
        string[]? surfaces = null) => new()
        {
            BindingId = new Guid(++_bindingCounter, 0, 0, new byte[8]),
            ConsumerType = consumerType,
            ConsumerCode = "default",
            Environment = "*",
            ToolDescription = toolDescription,
            Surfaces = surfaces ?? Array.Empty<string>(),
        };

    public HttpClient CreateAuthenticatedClient()
    {
        var client = _app!.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fake-token");
        return client;
    }

    public HttpClient CreateUnauthenticatedClient() => _app!.GetTestClient();
}
