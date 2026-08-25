using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.SpeAdmin;

/// <summary>
/// Pins the container-type list projection and the mapping of the fields an administrator needs to
/// tell one container type from another.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these exist.</b> Task 030 found that <c>owningAppId</c> and <c>expirationDateTime</c> never
/// reached the client — not because the mapping was wrong, but because the Graph request carried a
/// hand-maintained <c>$select</c> that did not ask for them. A property the projection omits is a
/// property the caller silently never sees, and the symptoms were quiet: the grid's "Owning App"
/// column rendered blank for every row, and the warning that a trial container type expires in 30
/// days could never fire because the date it keyed on was always absent.
/// </para>
/// <para>
/// The fix removed <c>$select</c> entirely rather than extending it. Naming the properties explicitly
/// would have worked today but re-arms the failure this workstream exists to remove: a wrong or
/// version-absent name in <c>$select</c> is a hard 400 that breaks the whole list — precisely what
/// <c>storageUsedInBytes</c> does on v1.0 (see notes/beta-vs-v1-surface-verification.md).
/// </para>
/// <para>
/// So the first test below guards a deliberate <i>absence</i>, which is easy to "tidy up" without
/// these tests to object. The rest pin that absent data stays absent instead of becoming a value.
/// </para>
/// </remarks>
public class SpeAdminContainerTypeMappingTests
{
    private const string ContainerTypesPath = "/storage/fileStorage/containerTypes";
    private const string OwningAppId = "11111111-2222-3333-4444-555555555555";

    // ─────────────────────────────────────────────────────────────────────────
    // The projection
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>The load-bearing one — it guards a deliberate omission.</summary>
    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task ListContainerTypes_SendsNoSelect_SoNewPropertiesArriveWithoutACodeChange()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, """{"value":[]}""");

        await CreateSut().ListContainerTypesAsync(graph.CreateGraphClient());

        var query = Uri.UnescapeDataString(graph.RequestsFor(ContainerTypesPath).Single().RawQuery);

        query.Should().NotContain("$select",
            "a hand-maintained projection is why owningAppId and expirationDateTime never reached " +
            "the client, and a wrong name in $select is a hard 400 that breaks the entire list");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Mapping the fields the lifecycle constraints depend on
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task OwningAppId_ReachesTheSummary()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, $$"""
            {"value":[{
              "id":"8a6ce34c-6055-4681-8f87-2f4f9f921c06",
              "name":"Legal Documents",
              "billingClassification":"standard",
              "owningAppId":"{{OwningAppId}}"
            }]}
            """);

        var result = await CreateSut().ListContainerTypesAsync(graph.CreateGraphClient());

        result.Should().ContainSingle().Which.OwningAppId.Should().Be(OwningAppId,
            "SharePoint Embedded binds one owning app to one container type permanently — it is what " +
            "identifies the type to an administrator");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task TrialExpiry_ReachesTheSummary_AsATypedTimestamp()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, """
            {"value":[{
              "id":"8a6ce34c-6055-4681-8f87-2f4f9f921c06",
              "name":"Dev Trial",
              "billingClassification":"trial",
              "expirationDateTime":"2026-09-22T14:30:00Z"
            }]}
            """);

        var result = await CreateSut().ListContainerTypesAsync(graph.CreateGraphClient());

        result.Should().ContainSingle().Which.ExpirationDateTime
            .Should().Be(new DateTimeOffset(2026, 9, 22, 14, 30, 0, TimeSpan.Zero),
                "a trial container type expires after 30 days and is not renewable — without this " +
                "date the UI cannot warn anyone before it happens");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Absence must stay absence
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task AbsentOwningApp_IsNull_NotEmptyString()
    {
        // "" renders as a blank cell, which reads as "this type has no owning app" — a claim the
        // response never made. Null is the only value that lets the UI say "unknown".
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, """
            {"value":[{"id":"8a6ce34c-6055-4681-8f87-2f4f9f921c06","name":"No Owner Returned"}]}
            """);

        var result = await CreateSut().ListContainerTypesAsync(graph.CreateGraphClient());

        result.Should().ContainSingle().Which.OwningAppId.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task UnreadableExpiry_IsNull_RatherThanADefaultDate()
    {
        // Substituting a default here is how "expires in 30 days" silently becomes "expires today"
        // (or "never"). A date we cannot read is unknown, and the caller must be able to tell.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, """
            {"value":[{
              "id":"8a6ce34c-6055-4681-8f87-2f4f9f921c06",
              "name":"Broken Date",
              "expirationDateTime":"not-a-date"
            }]}
            """);

        var result = await CreateSut().ListContainerTypesAsync(graph.CreateGraphClient());

        result.Should().ContainSingle().Which.ExpirationDateTime.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task BillingClassification_StillMaps_AfterTheSelectWasRemoved()
    {
        // Regression guard: billingClassification is read from AdditionalData, so it is the field
        // most exposed to a change in what the projection returns. Every lifecycle rule in the UI
        // keys off it — which type is deletable, which can be registered, which limit applies.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, """
            {"value":[{
              "id":"8a6ce34c-6055-4681-8f87-2f4f9f921c06",
              "name":"Dev Trial",
              "billingClassification":"trial"
            }]}
            """);

        var result = await CreateSut().ListContainerTypesAsync(graph.CreateGraphClient());

        result.Should().ContainSingle().Which.BillingClassification.Should().Be("trial");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Billing status (task 029 / spec FR-C12)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task BillingStatus_ReachesTheSummary_InGraphsWireCasing()
    {
        // Until task 029 the string "billingStatus" did not appear anywhere in this repository, so a
        // container type whose billing had lapsed looked identical to a healthy one on every screen.
        //
        // The casing half is not cosmetic. The SDK binds this to a typed enum whose ToString() is
        // "Invalid", while Graph, this API's contract, and every client comparison use "invalid".
        // Emitting the C# spelling would make the DTO's value depend on which SDK version happened to
        // be installed — the exact coupling that left billingClassification null for ten days after
        // the Graph 6 upgrade.
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, """
            {"value":[{
              "id":"8a6ce34c-6055-4681-8f87-2f4f9f921c06",
              "name":"Lapsed Billing",
              "billingClassification":"standard",
              "billingStatus":"invalid"
            }]}
            """);

        var result = await CreateSut().ListContainerTypesAsync(graph.CreateGraphClient());

        result.Should().ContainSingle().Which.BillingStatus.Should().Be("invalid");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task AbsentBillingStatus_IsNull_RatherThanValid()
    {
        // NFR-06, and the expensive direction of it. Defaulting an unreported billing status to
        // "valid" would present an unbilled container type as healthy — a fabricated reassurance,
        // which is worse here than an unhelpful blank. Null is what lets the UI say "unknown".
        using var graph = new GraphWireMockFixture();
        graph.StubGet(ContainerTypesPath, """
            {"value":[{
              "id":"8a6ce34c-6055-4681-8f87-2f4f9f921c06",
              "name":"No Billing Status Returned",
              "billingClassification":"standard"
            }]}
            """);

        var result = await CreateSut().ListContainerTypesAsync(graph.CreateGraphClient());

        result.Should().ContainSingle().Which.BillingStatus.Should().BeNull();
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
