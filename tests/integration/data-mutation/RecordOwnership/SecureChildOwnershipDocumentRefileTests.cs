using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// unified-access-control-r2 task 146 r1 (verifier item 5) — the document RE-FILE through the REAL
/// <c>PUT /api/v1/documents/{id}</c> (its write gate, its reparent) and the REAL <see cref="RecordOwnershipResolver"/>
/// over <see cref="Directory"/>: filing a document under a secure matter re-owns it to the named Secure team (read
/// back); a flagged-but-not-isolated target is a 409 with the stable code and the update is not written.
/// </summary>
[Trait("status", "new")]
public sealed class SecureChildOwnershipDocumentRefileTests : IClassFixture<DocumentRefileOwnershipTestFixture>
{
    private readonly DocumentRefileOwnershipTestFixture _fixture;

    public SecureChildOwnershipDocumentRefileTests(DocumentRefileOwnershipTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    // The caller's rights are stated per token for every record: Write on the document (the route's filter) and — r2,
    // verifier items 7/8 — AppendTo on the record it is filed under.
    private const string RefileRights = "ReadAccess,WriteAccess,AppendToAccess";

    [Fact]
    public async Task DocumentPut_FilingUnderASecureMatter_ReownsTheDocumentToTheNamedTeam_ReadBack()
    {
        var documentId = DocumentRefileOwnershipTestFixture.OrdinaryDocument;
        using var client = _fixture.CreateClientWithRights(RefileRights);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/documents/{documentId}", new { matterLookup = DocumentRefileOwnershipTestFixture.SecureMatter });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.UpdatedDataverseDocumentIds.Should().Contain(documentId.ToString());
        _fixture.World.Assignments.Should().Contain(("sprk_document", documentId, Directory.SecureNamedTeam));
        _fixture.World.Row("sprk_document", documentId).GetAttributeValue<EntityReference>("owningteam").Id
            .Should().Be(Directory.SecureNamedTeam);
    }

    [Fact]
    public async Task DocumentPut_FilingUnderAFlaggedButNotIsolatedProject_Is409WithTheStableCode_AndWritesNothing()
    {
        var documentId = DocumentRefileOwnershipTestFixture.OrdinaryDocument;
        using var client = _fixture.CreateClientWithRights(RefileRights);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/documents/{documentId}", new { projectLookup = DocumentRefileOwnershipTestFixture.FlaggedProject });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (JsonNode.Parse(await response.Content.ReadAsStringAsync())?["reasonCode"]?.GetValue<string>())
            .Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        _fixture.UpdatedDataverseDocumentIds.Should().BeEmpty("a refused re-file writes nothing");
    }

    // r2 (verifier item 8): Write on the document is not enough to pull it under a record — AppendTo on that record is
    // asked AS THE CALLER first. Without it: 403, nothing written, no owner reassigned.
    [Fact]
    public async Task DocumentPut_FilingUnderASecureMatterWithoutAppendToOnIt_Is403_AndWritesAndReassignsNothing()
    {
        var documentId = DocumentRefileOwnershipTestFixture.OrdinaryDocument;
        using var client = _fixture.CreateClientWithRights("ReadAccess,WriteAccess");

        var response = await client.PutAsJsonAsync(
            $"/api/v1/documents/{documentId}", new { matterLookup = DocumentRefileOwnershipTestFixture.SecureMatter });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.UpdatedDataverseDocumentIds.Should().BeEmpty();
        _fixture.World.Assignments.Should().BeEmpty();
    }

    // r2 (verifier item 7): the 409's detail names the target and why it is refused. A caller without AppendTo on the
    // target must not reach it — they get the uniform 403, which names neither the record nor its state.
    [Fact]
    public async Task DocumentPut_OntoAFlaggedProjectWithoutAppendToOnIt_Is403_NotTheRefusalOracle()
    {
        var documentId = DocumentRefileOwnershipTestFixture.OrdinaryDocument;
        using var client = _fixture.CreateClientWithRights("ReadAccess,WriteAccess");

        var response = await client.PutAsJsonAsync(
            $"/api/v1/documents/{documentId}", new { projectLookup = DocumentRefileOwnershipTestFixture.FlaggedProject });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(DocumentRefileOwnershipTestFixture.FlaggedProject.ToString());
        body.Should().NotContain(RecordOwnerRefusal.SecureParentNotIsolated);
        body.Should().NotContainEquivalentOf("secure");
        _fixture.UpdatedDataverseDocumentIds.Should().BeEmpty();
    }

    // ---- c1, owner round 10 item 7: moving a document OUT of a secure root is an un-secure (F3) ----

    [Fact]
    public async Task DocumentPut_MovingOutOfASecureMatter_ByAFullAccessHolderOnIt_IsReownedByTheNewMattersTeam()
    {
        var documentId = DocumentRefileOwnershipTestFixture.SecureDocument;
        using var client = _fixture.CreateClientWithRights(RefileRights + ",DeleteAccess");

        var response = await client.PutAsJsonAsync(
            $"/api/v1/documents/{documentId}", new { matterLookup = DocumentRefileOwnershipTestFixture.OrdinaryMatter });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.UpdatedDataverseDocumentIds.Should().Contain(documentId.ToString());
        _fixture.World.Assignments.Should().Equal(("sprk_document", documentId, Directory.ChildTeam));
    }

    [Fact]
    public async Task DocumentPut_MovingOutOfASecureMatter_ByTheDocumentsCreator_IsAllowed_WithoutFullAccess()
    {
        var documentId = DocumentRefileOwnershipTestFixture.CallersSecureDocument;
        using var client = _fixture.CreateClientWithRights(RefileRights);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/documents/{documentId}", new { matterLookup = DocumentRefileOwnershipTestFixture.OrdinaryMatter });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.World.Assignments.Should().Equal(("sprk_document", documentId, Directory.ChildTeam));
    }

    [Fact]
    public async Task DocumentPut_MovingOutOfASecureMatter_ByAWriteOnlyHolder_Is403_InTheUnsecureEndpointsShape_AndWritesNothing()
    {
        var documentId = DocumentRefileOwnershipTestFixture.SecureDocument;
        using var client = _fixture.CreateClientWithRights(RefileRights); // Write and AppendTo, not Delete: not Full Access

        var response = await client.PutAsJsonAsync(
            $"/api/v1/documents/{documentId}", new { matterLookup = DocumentRefileOwnershipTestFixture.OrdinaryMatter });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        problem["title"]!.GetValue<string>().Should().Be("Forbidden");
        problem["reasonCode"]!.GetValue<string>().Should().Be("sdap.unsecure.not_permitted");
        problem["traceId"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        problem["detail"]!.GetValue<string>().Should().Contain("Full Access").And.Contain("document");
        _fixture.UpdatedDataverseDocumentIds.Should().BeEmpty("refused before any write");
        _fixture.World.Assignments.Should().BeEmpty();
    }
}

/// <summary>The document-route host with the REAL owner resolver over an in-memory directory.</summary>
public sealed class DocumentRefileOwnershipTestFixture : DocumentDestroyAuthorizationTestFixture
{
    public static readonly Guid SecureMatter = Guid.Parse("b1460000-0000-4000-8000-000000000001");
    public static readonly Guid FlaggedProject = Guid.Parse("b1460000-0000-4000-8000-000000000002");
    public static readonly Guid OrdinaryDocument = Guid.Parse("b1460000-0000-4000-8000-000000000003");

    // c1 (owner round 10 item 7): documents filed under the secure matter, and an ordinary matter to move them to.
    public static readonly Guid OrdinaryMatter = Guid.Parse("b1460000-0000-4000-8000-000000000004");
    public static readonly Guid SecureDocument = Guid.Parse("b1460000-0000-4000-8000-000000000005");
    public static readonly Guid CallersSecureDocument = Guid.Parse("b1460000-0000-4000-8000-000000000006");

    /// <summary>Who WhoAmI answers for every authenticated test caller (the F3 probe below).</summary>
    public static readonly Guid ProbeCaller = Guid.Parse("b1460000-0000-4000-8000-0000000000ca");

    internal Directory World { get; private set; } = NewWorld();

    private static Directory NewWorld() => Directory.Standard()
        .WithSecureRoot("sprk_matter", SecureMatter)
        .WithOrdinaryRoot("sprk_matter", OrdinaryMatter)
        .WithRecord("sprk_project", FlaggedProject, Directory.ChildBu, isSecure: true, owningTeam: Directory.ChildTeam)
        .WithRecord("sprk_document", OrdinaryDocument, Directory.ChildBu, owningTeam: Directory.ChildTeam)
        .WithRecord("sprk_document", SecureDocument, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam,
            extra: new()
            {
                ["sprk_matter"] = new EntityReference("sprk_matter", SecureMatter),
                ["createdby"] = new EntityReference("systemuser", Guid.Parse("b1460000-0000-4000-8000-0000000000ee")),
            })
        .WithRecord("sprk_document", CallersSecureDocument, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam,
            extra: new()
            {
                ["sprk_matter"] = new EntityReference("sprk_matter", SecureMatter),
                ["createdby"] = new EntityReference("systemuser", ProbeCaller),
            });

    public new void Reset()
    {
        base.Reset();
        World = NewWorld();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Resolved per request from the CURRENT world, so Reset() gives each test a fresh directory.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRecordOwnershipResolver>();
            services.AddScoped<IRecordOwnershipResolver>(_ => World.Resolver());

            // c1: the caller-scoped probe F3 asks — rights from the same "rights=" bearer-token convention as the access
            // seam above, for every record; WhoAmI = ProbeCaller.
            services.RemoveAll<CallerRecordAccessProbe>();
            services.AddSingleton<CallerRecordAccessProbe>(new TokenRightsProbe());
        });
    }

    /// <summary>The F3 probe double: the caller's rights on any record are the ones its bearer token states.</summary>
    private sealed class TokenRightsProbe : CallerRecordAccessProbe
    {
        public TokenRightsProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        public override Task<Guid?> GetCallerSystemUserIdAsync(string? callerBearerToken, CancellationToken ct = default) =>
            Task.FromResult<Guid?>(string.IsNullOrWhiteSpace(callerBearerToken) ? null : ProbeCaller);

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default) =>
            Task.FromResult(callerBearerToken?.StartsWith("rights=", StringComparison.Ordinal) == true
                ? DataverseAccessRightsMapper.FromAccessRightsString(callerBearerToken["rights=".Length..])
                : AccessRights.None);
    }
}
