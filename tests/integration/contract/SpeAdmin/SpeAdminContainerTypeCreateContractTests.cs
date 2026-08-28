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
/// Pins the container-type CREATE body: that it carries <c>owningAppId</c>, and that an unrecognised
/// billing classification stops the call instead of quietly changing it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these exist.</b> Creating a container type failed in UAT 2026-08-28 with
/// <c>invalidRequest: One of the provided arguments is not acceptable</c> — an error that does not
/// say WHICH argument. The body carried only <c>name</c> and <c>billingClassification</c>;
/// <c>owningAppId</c> was never sent, although Graph's beta CSDL marks
/// <c>fileStorageContainerType.owningAppId</c> <c>Nullable="false"</c> and Microsoft's documented
/// create body includes it. Every container type is owned by exactly one Entra app registration,
/// fixed at creation.
/// </para>
/// <para>
/// ⚠️ <b>Scope of proof.</b> Unlike the custom-property defect, this one could NOT be confirmed
/// against live Graph: container-type create is delegated-only and an app-only token receives
/// <c>403 accessDenied</c> (consistent with task 010's finding on the sibling LIST endpoint), so the
/// argument error is unreachable from a probe. These tests prove the request we now SEND; whether
/// Graph accepts it is settled by UAT with a delegated token.
/// </para>
/// <para>
/// The second test guards a separate, quieter defect found while reading this path: an unparseable
/// classification fell through to <c>null</c>, so a request for a <b>trial</b> type would have
/// created a <b>standard</b> one and reported success. Billing classification is permanent — a trial
/// type can never be converted — so a silent substitution there is unrecoverable.
/// </para>
/// <para>Per <c>tests/CLAUDE.md</c> this lives under <c>tests/integration/contract/**</c> — a KEEP path.</para>
/// </remarks>
public class SpeAdminContainerTypeCreateContractTests
{
    private const string ContainerTypesPath = "/storage/fileStorage/containerTypes";
    private const string OwningAppId = "170c98e1-d486-4355-bcbe-170454e0207c";

    private const string CreatedResponse = """
        {"id":"9c1e2f3a-0000-4000-8000-abcdefabcdef","name":"Spaarke Model 1 Trial PAYGO",
         "billingClassification":"trial","owningAppId":"170c98e1-d486-4355-bcbe-170454e0207c"}
        """;

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task CreateContainerType_SendsOwningAppId_BecauseGraphRequiresIt()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPost(ContainerTypesPath, CreatedResponse);

        await CreateSut().CreateContainerTypeAsync(
            graph.CreateGraphClient(),
            displayName: "Spaarke Model 1 Trial PAYGO",
            billingClassification: "trial",
            owningAppId: OwningAppId);

        var post = graph.RequestsFor(ContainerTypesPath).Should().ContainSingle().Subject;
        using var body = JsonDocument.Parse(post.Body!);

        // 🔴 THE ONE THAT MATTERS. Omitting this is the UAT failure, and Graph's rejection names no
        // field, so its absence is invisible from the response alone.
        body.RootElement.TryGetProperty("owningAppId", out var owner).Should().BeTrue(
            because: "Graph marks owningAppId Nullable=\"false\" on fileStorageContainerType");
        owner.GetString().Should().Be(OwningAppId);
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task CreateContainerType_WithTrialClassification_SendsTrial_NotStandard()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPost(ContainerTypesPath, CreatedResponse);

        await CreateSut().CreateContainerTypeAsync(
            graph.CreateGraphClient(),
            displayName: "Spaarke Model 1 Trial PAYGO",
            billingClassification: "trial",
            owningAppId: OwningAppId);

        var post = graph.RequestsFor(ContainerTypesPath).Should().ContainSingle().Subject;
        using var body = JsonDocument.Parse(post.Body!);

        // "trial" is a real Graph value. The endpoint's allow-list used to reject it while permitting
        // "premium", which Graph has never had — so the documented path for a new environment was
        // closed and the undocumented one was open.
        body.RootElement.GetProperty("billingClassification").GetString()
            .Should().Be("trial", because: "a trial type must not be created as standard");
        body.RootElement.GetProperty("name").GetString().Should().Be("Spaarke Model 1 Trial PAYGO");
    }

    [Theory]
    [Trait("Category", "SpeAdminGraphContract")]
    [InlineData("premium")]      // never existed in Graph; the retired allow-list permitted it
    [InlineData("payg")]
    [InlineData("passthrough")]  // the human name for directToCustomer — not the wire value
    public async Task CreateContainerType_WithUnrecognisedClassification_ThrowsRatherThanDefaulting(
        string classification)
    {
        using var graph = new GraphWireMockFixture();
        graph.StubPost(ContainerTypesPath, CreatedResponse);
        var sut = CreateSut();
        var client = graph.CreateGraphClient();

        var act = async () => await sut.CreateContainerTypeAsync(
            client,
            displayName: "Spaarke Model 1 Trial PAYGO",
            billingClassification: classification,
            owningAppId: OwningAppId);

        // 🔴 Falling through to null here would create a STANDARD container type while the operator
        // believed they had asked for something else — and the classification can never be changed
        // afterwards. An unrecoverable silent substitution is strictly worse than a failed create.
        await act.Should().ThrowAsync<ArgumentException>(
            because: "a classification we cannot map must stop the call, not pick one");

        graph.RequestsFor(ContainerTypesPath).Should().BeEmpty(
            because: "nothing may reach Graph once the classification is known to be unmappable");
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
