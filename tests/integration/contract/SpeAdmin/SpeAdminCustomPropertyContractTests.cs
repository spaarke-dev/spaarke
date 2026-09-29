using System.Text.Json;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.SpeAdmin;

/// <summary>
/// Pins the custom-properties WRITE to the right URL and the right body shape.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these exist.</b> The "Add Property" path had NEVER worked. It PATCHed the CONTAINER with a
/// <c>{ "customProperties": { ... } }</c> wrapper, and Graph rejects that outright:
/// </para>
/// <code>400 invalidRequest: Unsupported request body property: customProperties.</code>
/// <para>
/// Found 2026-08-28 by live probe on a throwaway container, answering the UAT question "can we
/// confirm the + Add functions work?". The answer for this one was no — and no test existed to say
/// so. <c>customProperties</c> is its own sub-resource: the PATCH goes to
/// <c>/containers/{id}/customProperties</c> and the property map IS the body root, unwrapped.
/// </para>
/// <para>
/// <b>Why it stayed invisible.</b> READS use <c>GET ?$select=customProperties</c> on the container —
/// a different, valid shape that works. A working read sitting beside a broken write is exactly the
/// arrangement that survives inspection: the screen lists properties correctly, so the surface looks
/// healthy right up until someone adds one.
/// </para>
/// <para>
/// The probe also established the two semantics the endpoint depends on, both asserted below in
/// spirit: partial writes MERGE (an untouched property survives), and a null value REMOVES a
/// property. The merge behaviour is what makes the BFF's PUT-shaped endpoint non-destructive.
/// </para>
/// <para>Per <c>tests/CLAUDE.md</c> this lives under <c>tests/integration/contract/**</c> — a KEEP path.</para>
/// </remarks>
public class SpeAdminCustomPropertyContractTests
{
    private const string ContainersPath = "/storage/fileStorage/containers";
    private const string ContainerId = "b!probe-container-id";

    /// <summary>The PATCH reply. Every assertion here is about the REQUEST we sent.</summary>
    private const string PatchResponse = """{"ProbeAlpha":{"value":"a","isSearchable":false}}""";

    /// <summary>The re-read the method performs after a successful write.</summary>
    private const string ReadBackResponse = """
        {"id":"b!probe-container-id","customProperties":{"ProbeAlpha":{"value":"a","isSearchable":false}}}
        """;

    // ─────────────────────────────────────────────────────────────────────────
    // The URL — this is the defect
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task UpdateCustomProperties_PatchesTheSubResource_NotTheContainerItself()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPatch(ContainersPath, PatchResponse);
        graph.StubGet(ContainersPath, ReadBackResponse);

        await CreateSut().UpdateCustomPropertiesAsync(
            graph.CreateGraphClient(),
            ContainerId,
            new[] { new CustomPropertyDto("ProbeAlpha", "a", false) });

        var patch = graph.PatchRequestsFor(ContainersPath).Should().ContainSingle().Subject;

        // 🔴 THE ONE THAT MATTERS. Targeting the container instead of the sub-resource is a
        // guaranteed 400 — Graph calls customProperties an "Unsupported request body property"
        // there. The old code did exactly this, so no property was ever written.
        patch.Path.Should().EndWith("/customProperties",
            because: "customProperties is its own sub-resource; PATCHing the container is rejected 400");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The body — the map is the root, not a wrapped field
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task UpdateCustomProperties_SendsThePropertyMapAsTheBodyRoot_Unwrapped()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPatch(ContainersPath, PatchResponse);
        graph.StubGet(ContainersPath, ReadBackResponse);

        await CreateSut().UpdateCustomPropertiesAsync(
            graph.CreateGraphClient(),
            ContainerId,
            new[] { new CustomPropertyDto("ProbeAlpha", "a", true) });

        var patch = graph.PatchRequestsFor(ContainersPath).Should().ContainSingle().Subject;
        using var body = JsonDocument.Parse(patch.Body!);

        // The property name is a ROOT key. A "customProperties" key at the root is the retired
        // wrapper coming back — the exact shape Graph refuses.
        body.RootElement.TryGetProperty("customProperties", out _).Should().BeFalse(
            because: "the wrapper is what Graph rejected; the map itself is the body");
        body.RootElement.TryGetProperty("ProbeAlpha", out var prop).Should().BeTrue(
            because: "each property name is a top-level key of the request body");

        prop.GetProperty("value").GetString().Should().Be("a");
        prop.GetProperty("isSearchable").GetBoolean().Should().BeTrue(
            because: "isSearchable is carried per-property and decides whether the value is indexed");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task UpdateCustomProperties_WithSeveralProperties_SendsThemAllInOneWrite()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPatch(ContainersPath, PatchResponse);
        graph.StubGet(ContainersPath, ReadBackResponse);

        await CreateSut().UpdateCustomPropertiesAsync(
            graph.CreateGraphClient(),
            ContainerId,
            new[]
            {
                new CustomPropertyDto("Alpha", "1", false),
                new CustomPropertyDto("Beta", "2", true),
            });

        // One request, not one per property. Graph merges partial writes, so a per-property loop
        // would still converge — but it would multiply the failure surface and the latency for no
        // gain, and a partial failure mid-loop would leave the set half-written with no way to tell.
        var writes = graph.PatchRequestsFor(ContainersPath);
        writes.Should().ContainSingle(because: "the whole set goes in a single PATCH");

        using var body = JsonDocument.Parse(writes[0].Body!);
        body.RootElement.TryGetProperty("Alpha", out _).Should().BeTrue();
        body.RootElement.TryGetProperty("Beta", out _).Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Construction
    // ─────────────────────────────────────────────────────────────────────────

    private static SpeAdminGraphService CreateSut()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://unused.invalid",
            })
            .Build();

        return new SpeAdminGraphService(
            httpClientFactory: new UnusedHttpClientFactory(),
            secretClient: new SecretClient(new Uri("https://unused.invalid/"), new UnusableCredential()),
            dataverseClient: new DataverseWebApiClient(
                configuration, NullLogger<DataverseWebApiClient>.Instance, new UnusableCredential()),
            configuration: configuration,
            logger: NullLogger<SpeAdminGraphService>.Instance,
            tokenProvider: null);
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException(
            $"A method under test requested the '{name}' HttpClient. These tests supply the Graph " +
            "client directly, so building one means the code took an unexpected path.");
    }

    private sealed class UnusableCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext r, CancellationToken c)
            => throw new InvalidOperationException("Key Vault must not be reached from a contract test.");

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext r, CancellationToken c)
            => throw new InvalidOperationException("Key Vault must not be reached from a contract test.");
    }
}
