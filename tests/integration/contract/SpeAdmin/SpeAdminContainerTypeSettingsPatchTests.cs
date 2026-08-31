using System.Text.Json;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.SpeAdmin;

/// <summary>
/// Pins the container-type settings PATCH body: its shape, its property names, and the boundary
/// between a storage <b>ceiling</b> and storage <b>consumption</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these exist.</b> Every settings write was a silent no-op, for three independent reasons
/// (task 023, spec FR-C04 + FR-C05): the values went in as <i>top-level</i> properties when settings
/// are a <b>nested object</b>; two of the four names did not exist on the resource; and one of them
/// named a consumption metric while carrying a quota ceiling. Graph answered <c>200</c> to all of it,
/// so the screen reported success and nothing changed.
/// </para>
/// <para>
/// The production fix uses the SDK's typed settings model, which makes the property names
/// compiler-enforced. These tests guard what the compiler cannot: that the body is still <i>shaped</i>
/// correctly, that the retired names never come back, and that a ceiling is never called usage.
/// </para>
/// <para>Evidence: <c>projects/sdap-SPE-admin-app-r2/notes/task-023-findings.md</c>.</para>
/// </remarks>
public class SpeAdminContainerTypeSettingsPatchTests
{
    private const string ContainerTypesPath = "/storage/fileStorage/containerTypes";
    private const string ContainerTypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06";

    /// <summary>Any successful PATCH response — the assertions here are all about the REQUEST.</summary>
    private const string PatchResponse = """
        {"id":"8a6ce34c-6055-4681-8f87-2f4f9f921c06","name":"Legal Documents"}
        """;

    /// <summary>
    /// The GET that now precedes every PATCH, carrying the <c>etag</c> the Update API requires.
    /// </summary>
    /// <remarks>
    /// Every test here stubs this. That is not boilerplate — it models a real, load-bearing step:
    /// Graph's Update API lists <c>etag</c> as a REQUIRED body property, and without it every write
    /// returns <c>400 invalidRequest</c> with a message naming no cause. That 400 blocked four tasks
    /// for two days and was misdiagnosed as an ownership restriction. A settings write is now a
    /// read-modify-write, and these tests say so.
    /// </remarks>
    private const string GetResponseWithEtag = """
        {"id":"8a6ce34c-6055-4681-8f87-2f4f9f921c06","name":"Legal Documents","etag":"MC4wLjAuMA=="}
        """;

    // ─────────────────────────────────────────────────────────────────────────
    // The etag — why every write returned 400 for two days
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>The load-bearing one for the write path.</summary>
    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task SettingsPatch_SendsTheEtagInTheBody_BecauseTheUpdateApiRequiresIt()
    {
        /*
         * 🔴 Regression guard for the defect that blocked tasks 023, 025, 026 and 029.
         *
         * Graph's Update fileStorageContainerType API lists `etag` as a REQUIRED body property, and
         * its own "Example 2: Update without ETag" documents the response as 400 Bad Request. Every
         * write this product attempted omitted it, so every write 400'd with
         * "One of the provided arguments is not acceptable" — a message that names nothing. It was
         * misdiagnosed for two days as an ownership restriction, which would have cost either a
         * throwaway container type or a change to a production app registration to disprove.
         *
         * Proven live 2026-08-25: the IDENTICAL no-op PATCH returns 400 without the etag and 200
         * with it, on both beta and v1.0.
         *
         * ⚠️ It is a BODY property, NOT the `If-Match` header. An earlier session tried If-Match,
         * saw no change, and moved on — which is exactly how the real cause stayed hidden.
         */
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, GetResponseWithEtag);
        graph.StubPatch(ContainerTypesPath, PatchResponse);

        await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: null, isItemVersioningEnabled: true,
            itemMajorVersionLimit: 25, maxStoragePerContainerInBytes: null);

        var body = graph.PatchRequestsFor(ContainerTypesPath).Single().Body ?? string.Empty;

        body.Should().Contain("MC4wLjAuMA==",
            "without the etag Graph rejects the write as 400 invalidRequest with a message that " +
            "names no cause — the failure mode this whole project exists to remove");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task SettingsPatch_ReadsTheEtagImmediatelyBefore_SoAConcurrentWriteIsRejectedNotOverwritten()
    {
        // The GET is the read half of a read-modify-write, not a convenience. Taking a fresh etag
        // per write is what lets Graph reject a stale one, instead of this app silently
        // last-writer-wins over an administrator who changed the same type moments earlier.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, GetResponseWithEtag);
        graph.StubPatch(ContainerTypesPath, PatchResponse);

        await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: null, isItemVersioningEnabled: true,
            itemMajorVersionLimit: 25, maxStoragePerContainerInBytes: null);

        var all = graph.RequestsFor(ContainerTypesPath);
        all.Should().HaveCount(2);
        all[0].Method.Should().Be("GET", "the etag must be read before the write that carries it");
        all[1].Method.Should().Be("PATCH");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task SettingsPatch_WhenGraphReturnsNoEtag_FailsLoudly_RatherThanSendingADoomedWrite()
    {
        // Sending the PATCH anyway would earn a 400 whose message names nothing, putting an operator
        // back in front of the exact error that cost two days. Fail with a message that says what is
        // missing and why it matters.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, """
            {"id":"8a6ce34c-6055-4681-8f87-2f4f9f921c06","name":"No Etag Returned"}
            """);
        graph.StubPatch(ContainerTypesPath, PatchResponse);

        var act = async () => await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: null, isItemVersioningEnabled: true,
            itemMajorVersionLimit: 25, maxStoragePerContainerInBytes: null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(e => e.Message.Contains("etag"));

        graph.PatchRequestsFor(ContainerTypesPath).Should().BeEmpty(
            "a write that cannot succeed should not be sent");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Shape — the reason names alone were not the bug
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>The load-bearing one.</summary>
    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task SettingsPatch_NestsEverythingUnderSettings_NotAtTheTopLevel()
    {
        // Settings live in a nested `settings` object. Written at the top level they are unknown
        // members on a merge-PATCH, which Graph ignores — returning 200 and changing nothing. That is
        // why correcting the property names alone would not have fixed the no-op.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, GetResponseWithEtag);
        graph.StubPatch(ContainerTypesPath, PatchResponse);

        await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: null, isItemVersioningEnabled: true,
            itemMajorVersionLimit: 25, maxStoragePerContainerInBytes: null);

        var body = graph.PatchRequestsFor(ContainerTypesPath).Single().BodyAsJson();

        body.TryGetProperty("settings", out var settings).Should().BeTrue(
            "settings are a nested object — top-level members are silently ignored by Graph");
        settings.TryGetProperty("itemMajorVersionLimit", out _).Should().BeTrue();
        body.TryGetProperty("itemMajorVersionLimit", out _).Should().BeFalse(
            "a setting at the top level is the no-op this task fixed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Names
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task SettingsPatch_UsesTheRealPropertyNames()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, GetResponseWithEtag);
        graph.StubPatch(ContainerTypesPath, PatchResponse);

        await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: null, isItemVersioningEnabled: true,
            itemMajorVersionLimit: 25, maxStoragePerContainerInBytes: 10_737_418_240L);

        var settings = graph.PatchRequestsFor(ContainerTypesPath).Single().BodyAsJson()
            .GetProperty("settings");

        settings.GetProperty("itemMajorVersionLimit").GetInt64().Should().Be(25);
        settings.GetProperty("maxStoragePerContainerInBytes").GetInt64().Should().Be(10_737_418_240L);
        settings.GetProperty("isItemVersioningEnabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task SettingsPatch_NeverSendsTheRetiredNames()
    {
        // The whole-body check the acceptance criterion asks for as a grep. Asserted against the raw
        // JSON rather than a parsed property so a name reappearing ANYWHERE — nested, top-level, or
        // smuggled through AdditionalData — fails this test.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, GetResponseWithEtag);
        graph.StubPatch(ContainerTypesPath, PatchResponse);

        await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: "externalUserSharingOnly", isItemVersioningEnabled: true,
            itemMajorVersionLimit: 25, maxStoragePerContainerInBytes: 10_737_418_240L);

        var raw = graph.PatchRequestsFor(ContainerTypesPath).Single().Body ?? string.Empty;

        raw.Should().NotContain("storageUsedInBytes",
            "that is the CONSUMPTION metric on a container — a ceiling must never borrow its name");
        raw.Should().NotContain("\"majorVersionLimit\"",
            "the real name is itemMajorVersionLimit");
        raw.Should().NotContain("\"isVersioningEnabled\"",
            "the real name is isItemVersioningEnabled");
        // Phantom names the old doc comment claimed were used. None was ever real.
        raw.Should().NotContain("allowedRoles").And.NotContain("storagePlanInformation");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Sharing capability — the allow-list that rejected the client's own values
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public void ValidSharingCapabilities_AreGraphsValues_NotTheInventedOnes()
    {
        // 🔴 This set is the endpoint's validation allow-list. It used to read
        // { disabled, view, edit, full } — three names Graph has never accepted — so every value the
        // SPE Admin client can actually send except "disabled" was rejected with a 400 by our own
        // validator, before the request ever reached Graph.
        SpeAdminGraphService.ValidSharingCapabilities.Should().BeEquivalentTo(
            new[]
            {
                "Disabled", "ExternalUserSharingOnly",
                "ExternalUserAndGuestSharing", "ExistingExternalUserSharingOnly",
            },
            o => o.Using(StringComparer.OrdinalIgnoreCase),
            "these are the members of Microsoft.Graph.Models.SharingCapabilities");

        SpeAdminGraphService.ValidSharingCapabilities.Should()
            .NotContain("view").And.NotContain("edit").And.NotContain("full");
        SpeAdminGraphService.ValidSharingCapabilities.Should().NotContain("UnknownFutureValue",
            "that is Kiota's forward-compatibility sentinel, not a value anyone may set");
    }

    [Theory]
    [Trait("Category", "SpeAdminGraphContract")]
    [InlineData("externalUserSharingOnly")]
    [InlineData("ExternalUserAndGuestSharing")]
    [InlineData("disabled")]
    public async Task SettingsPatch_AcceptsEveryValueTheClientCanSend(string capability)
    {
        // The client's SharingCapability union (types/spe.ts) sends camelCase; the SDK enum is
        // PascalCase. Matching is case-insensitive, and this proves it for both spellings.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, GetResponseWithEtag);
        graph.StubPatch(ContainerTypesPath, PatchResponse);

        await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: capability, isItemVersioningEnabled: null,
            itemMajorVersionLimit: null, maxStoragePerContainerInBytes: null);

        graph.PatchRequestsFor(ContainerTypesPath).Single().BodyAsJson()
            .GetProperty("settings").TryGetProperty("sharingCapability", out _)
            .Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task SettingsPatch_RejectsAnUnknownSharingCapability_RatherThanForwardingIt()
    {
        // Rejected here rather than sent on: Graph's response to an unparseable enum is not reliably
        // distinguishable from success, which is the failure mode this whole task exists to remove.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, GetResponseWithEtag);
        graph.StubPatch(ContainerTypesPath, PatchResponse);

        var act = async () => await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: "full", isItemVersioningEnabled: null,
            itemMajorVersionLimit: null, maxStoragePerContainerInBytes: null);

        await act.Should().ThrowAsync<ArgumentException>();
        graph.PatchRequestsFor(ContainerTypesPath).Should().BeEmpty(
            "an invalid value must not reach Graph at all");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Partial update semantics
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task SettingsPatch_OmitsUnsetValues_SoAPartialUpdateStaysPartial()
    {
        // Merge-PATCH: a property present with a null value is a request to CLEAR it, which would
        // silently wipe settings the caller never mentioned.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, GetResponseWithEtag);
        graph.StubPatch(ContainerTypesPath, PatchResponse);

        await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: null, isItemVersioningEnabled: null,
            itemMajorVersionLimit: 5, maxStoragePerContainerInBytes: null);

        var settings = graph.PatchRequestsFor(ContainerTypesPath).Single().BodyAsJson()
            .GetProperty("settings");

        settings.GetProperty("itemMajorVersionLimit").GetInt64().Should().Be(5);
        settings.TryGetProperty("maxStoragePerContainerInBytes", out var ceiling)
            .Should().BeFalse("an unset ceiling must not be sent as an explicit null");
        settings.TryGetProperty("sharingCapability", out _).Should().BeFalse();
        _ = ceiling;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Task 025 — the full nine, and the two properties the SDK gets wrong
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task SettingsPatch_CarriesAllNineV1Properties()
    {
        // Nine, verified against Graph's OData metadata rather than documentation prose. FR-C07 listed
        // `agent.chatEmbedAllowedHosts`, which exists in NEITHER api version, and omitted
        // `sharingCapability`, which does. See notes/task-025-schema-verification.md.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, GetResponseWithEtag);
        graph.StubPatch(ContainerTypesPath, PatchResponse);

        await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: "externalUserSharingOnly", isItemVersioningEnabled: true,
            itemMajorVersionLimit: 25, maxStoragePerContainerInBytes: 10_737_418_240L,
            isSearchEnabled: true, isDiscoverabilityEnabled: false, isSharingRestricted: true,
            urlTemplate: "https://example.invalid/{containerId}",
            consumingTenantOverridables: "sharingCapability,itemMajorVersionLimit");

        var settings = graph.PatchRequestsFor(ContainerTypesPath).Single().BodyAsJson()
            .GetProperty("settings");

        foreach (var name in new[]
                 {
                     "sharingCapability", "isItemVersioningEnabled", "itemMajorVersionLimit",
                     "maxStoragePerContainerInBytes", "isSearchEnabled", "isDiscoverabilityEnabled",
                     "isSharingRestricted", "urlTemplate", "consumingTenantOverridables",
                 })
        {
            settings.TryGetProperty(name, out _).Should().BeTrue($"'{name}' is one of the nine");
        }
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task SettingsPatch_SendsOverridablesAsTheRawFlagString_NotTheSdkEnum()
    {
        // 🔴 The SDK's generated FileStorageContainerTypeSettingsOverride declares only
        // UrlTemplate/IsDiscoverabilityEnabled/IsSearchEnabled/IsItemVersioningEnabled/
        // ItemMajorVersionLimit/MaxStoragePerContainerInBytes. The LIVE tenant returns
        // "sharingCapability,itemMajorVersionLimit,isOfficeRestricted" — two flags the enum does not
        // contain. Routing this through the typed enum would drop or reject real values, so the raw
        // string is deliberate. This is the opposite of task 023's typed-over-untyped choice, because
        // here the type is provably narrower than reality.
        const string live = "sharingCapability,itemMajorVersionLimit,isOfficeRestricted";
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, GetResponseWithEtag);
        graph.StubPatch(ContainerTypesPath, PatchResponse);

        await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: null, isItemVersioningEnabled: null,
            itemMajorVersionLimit: null, maxStoragePerContainerInBytes: null,
            consumingTenantOverridables: live);

        graph.PatchRequestsFor(ContainerTypesPath).Single().BodyAsJson()
            .GetProperty("settings").GetProperty("consumingTenantOverridables")
            .GetString().Should().Be(live, "flags outside the SDK enum must survive round-tripping");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task SettingsPatch_NeverSendsTheFictionalAgentProperty()
    {
        // Guards against FR-C07's phantom being "restored" by a future reader of the spec.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, GetResponseWithEtag);
        graph.StubPatch(ContainerTypesPath, PatchResponse);

        await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: null, isItemVersioningEnabled: null,
            itemMajorVersionLimit: 10, maxStoragePerContainerInBytes: null);

        (graph.PatchRequestsFor(ContainerTypesPath).Single().Body ?? "")
            .Should().NotContain("chatEmbedAllowedHosts")
            .And.NotContain("\"agent\"",
                "no such property exists in v1.0 or beta metadata");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The RESPONSE — every test above is about the request
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task SettingsPatch_ReturnsBillingFields_InTheSameCasingTheListDoes()
    {
        // 🔴 Regression guard for a defect task 029 found here. This path stringified the SDK's
        // billing enum directly — `updated.BillingClassification?.ToString()` — while the LIST path
        // ran the same value through the normalizer. So the identical container type came back as
        // "Trial" from a settings save and "trial" from the list, and which spelling a client saw
        // depended on which endpoint it had asked. Casing that varies by endpoint is not cosmetic:
        // every client comparison against Graph's own lowercase value silently fails on one path.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, GetResponseWithEtag);
        graph.StubPatch(ContainerTypesPath, """
            {
              "id":"8a6ce34c-6055-4681-8f87-2f4f9f921c06",
              "name":"Legal Documents",
              "billingClassification":"trial",
              "billingStatus":"invalid"
            }
            """);

        var result = await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: null, isItemVersioningEnabled: true,
            itemMajorVersionLimit: 25, maxStoragePerContainerInBytes: null);

        result.Should().NotBeNull();
        result!.BillingClassification.Should().Be("trial", "the SDK enum stringifies as \"Trial\"");
        result.BillingStatus.Should().Be("invalid", "the SDK enum stringifies as \"Invalid\"");
    }

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
            // auth-v4 (merged from master 2026-08-25) made DataverseWebApiClient select a credential in
            // its ctor: with Managed Identity disabled it now REQUIRES TENANT_ID + API_APP_ID + an
            // IConfidentialClientProvider, and threw before any test body ran. Passing the credential
            // explicitly takes the "selection bypassed" branch — and UnusableCredential throws if
            // anything ever actually asks it for a token, so a test that starts reaching Dataverse
            // fails loudly instead of quietly acquiring one. These contract tests supply the Graph
            // client directly and never touch Dataverse; this dependency exists only to construct.
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
