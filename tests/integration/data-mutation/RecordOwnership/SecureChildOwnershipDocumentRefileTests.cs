using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Xrm.Sdk;
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

    [Fact]
    public async Task DocumentPut_FilingUnderASecureMatter_ReownsTheDocumentToTheNamedTeam_ReadBack()
    {
        var documentId = DocumentRefileOwnershipTestFixture.OrdinaryDocument;
        using var client = _fixture.CreateClientWithRights("ReadAccess,WriteAccess");

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
        using var client = _fixture.CreateClientWithRights("ReadAccess,WriteAccess");

        var response = await client.PutAsJsonAsync(
            $"/api/v1/documents/{documentId}", new { projectLookup = DocumentRefileOwnershipTestFixture.FlaggedProject });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())?["reasonCode"]?.GetValue<string>()
            .Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        _fixture.UpdatedDataverseDocumentIds.Should().BeEmpty("a refused re-file writes nothing");
    }
}

/// <summary>The document-route host with the REAL owner resolver over an in-memory directory.</summary>
public sealed class DocumentRefileOwnershipTestFixture : DocumentDestroyAuthorizationTestFixture
{
    public static readonly Guid SecureMatter = Guid.Parse("b1460000-0000-4000-8000-000000000001");
    public static readonly Guid FlaggedProject = Guid.Parse("b1460000-0000-4000-8000-000000000002");
    public static readonly Guid OrdinaryDocument = Guid.Parse("b1460000-0000-4000-8000-000000000003");

    internal Directory World { get; private set; } = NewWorld();

    private static Directory NewWorld() => Directory.Standard()
        .WithSecureRoot("sprk_matter", SecureMatter)
        .WithRecord("sprk_project", FlaggedProject, Directory.ChildBu, isSecure: true, owningTeam: Directory.ChildTeam)
        .WithRecord("sprk_document", OrdinaryDocument, Directory.ChildBu, owningTeam: Directory.ChildTeam);

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
        });
    }
}
