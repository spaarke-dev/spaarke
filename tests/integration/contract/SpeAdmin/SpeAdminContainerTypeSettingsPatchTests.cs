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
        graph.StubPatch(ContainerTypesPath, PatchResponse);

        await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: null, isItemVersioningEnabled: true,
            itemMajorVersionLimit: 25, maxStoragePerContainerInBytes: null);

        var body = graph.RequestsFor(ContainerTypesPath).Single().BodyAsJson();

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
        graph.StubPatch(ContainerTypesPath, PatchResponse);

        await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: null, isItemVersioningEnabled: true,
            itemMajorVersionLimit: 25, maxStoragePerContainerInBytes: 10_737_418_240L);

        var settings = graph.RequestsFor(ContainerTypesPath).Single().BodyAsJson()
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
        graph.StubPatch(ContainerTypesPath, PatchResponse);

        await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: "externalUserSharingOnly", isItemVersioningEnabled: true,
            itemMajorVersionLimit: 25, maxStoragePerContainerInBytes: 10_737_418_240L);

        var raw = graph.RequestsFor(ContainerTypesPath).Single().Body ?? string.Empty;

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
        graph.StubPatch(ContainerTypesPath, PatchResponse);

        await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: capability, isItemVersioningEnabled: null,
            itemMajorVersionLimit: null, maxStoragePerContainerInBytes: null);

        graph.RequestsFor(ContainerTypesPath).Single().BodyAsJson()
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
        graph.StubPatch(ContainerTypesPath, PatchResponse);

        var act = async () => await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: "full", isItemVersioningEnabled: null,
            itemMajorVersionLimit: null, maxStoragePerContainerInBytes: null);

        await act.Should().ThrowAsync<ArgumentException>();
        graph.RequestsFor(ContainerTypesPath).Should().BeEmpty(
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
        graph.StubPatch(ContainerTypesPath, PatchResponse);

        await CreateSut().UpdateContainerTypeSettingsAsync(
            graph.CreateGraphClient(), ContainerTypeId,
            sharingCapability: null, isItemVersioningEnabled: null,
            itemMajorVersionLimit: 5, maxStoragePerContainerInBytes: null);

        var settings = graph.RequestsFor(ContainerTypesPath).Single().BodyAsJson()
            .GetProperty("settings");

        settings.GetProperty("itemMajorVersionLimit").GetInt64().Should().Be(5);
        settings.TryGetProperty("maxStoragePerContainerInBytes", out var ceiling)
            .Should().BeFalse("an unset ceiling must not be sent as an explicit null");
        settings.TryGetProperty("sharingCapability", out _).Should().BeFalse();
        _ = ceiling;
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
            dataverseClient: new DataverseWebApiClient(configuration, NullLogger<DataverseWebApiClient>.Instance),
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
