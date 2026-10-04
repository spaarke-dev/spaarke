using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;

/// <summary>
/// unified-access-control-r2 task 156 — the REAL mapped <c>PUT /api/v1/documents/{id}</c> route, end to end: a document
/// re-filed to another matter re-stamps every to-do filed under it IN THE SAME REQUEST, leaves an Office carrier to-do's
/// direct matter alone, and a document update that does not touch its root reads nothing.
/// </summary>
/// <remarks>
/// Only module boundaries are substituted: the caller's rights and the document row (the shared
/// <see cref="DocumentDestroyAuthorizationTestFixture"/>), and the Dataverse rows the cascade reads and PATCHes
/// (<see cref="StampWorld"/>, whose document row already shows the new matter — the state after the document's own
/// update).
/// </remarks>
public class DocumentRefileRestampRouteTests : IClassFixture<DocumentRefileRestampFixture>
{
    private static readonly Guid MatterB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private static readonly Guid Document = Guid.Parse("15600000-0000-0000-0000-000000000d01");
    private static readonly Guid TodoUnderDocument = Guid.Parse("15600000-0000-0000-0000-000000000107");
    private static readonly Guid CarrierTodo = Guid.Parse("15600000-0000-0000-0000-000000000105");

    private readonly DocumentRefileRestampFixture _fixture;

    public DocumentRefileRestampRouteTests(DocumentRefileRestampFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.Reseed();
    }

    [Fact(DisplayName = "Task 156 route: PUT /api/v1/documents/{id} re-filing the document to matter B re-stamps the to-do under it in the same request")]
    public async Task Put_RefilingTheDocument_RestampsItsChildren()
    {
        // Batch 4 integration (task 146): re-filing the document costs AppendTo on the matter it is filed under, asked as
        // the caller (the token-stated rights answer every record alike).
        using var client = _fixture.CreateClientWithRights("ReadAccess,WriteAccess,AppendToAccess");

        var response = await client.PutAsJsonAsync($"/api/v1/documents/{Document}", new { matterLookup = MatterB });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.UpdatedDataverseDocumentIds.Should().ContainSingle().Which.Should().Be(Document.ToString());
        _fixture.World.Lookup("sprk_todo", TodoUnderDocument, "sprk_regardingmatter").Should().Be(MatterB);
        _fixture.World.PatchesTo("sprk_todo", CarrierTodo).Should().BeEmpty(
            "the Office carrier to-do's matter is the user's direct choice, not a copy");
    }

    [Fact(DisplayName = "Task 156 route: a document update that does not touch its root (a rename) re-stamps nothing and reads nothing")]
    public async Task Put_RenamingTheDocument_RestampsNothing()
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess,WriteAccess");

        var response = await client.PutAsJsonAsync($"/api/v1/documents/{Document}", new { name = "renamed.pdf" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.World.Patches.Should().BeEmpty();
    }
}

/// <summary>Test host for <see cref="DocumentRefileRestampRouteTests"/>: the document fixture plus a <see cref="StampWorld"/>.</summary>
public sealed class DocumentRefileRestampFixture : DocumentDestroyAuthorizationTestFixture
{
    internal StampWorld World { get; private set; } = RefilePathRestampTests.DocumentWorld();

    /// <summary>Fresh rows per test (the fixture is shared by the class).</summary>
    internal void Reseed()
    {
        var fresh = RefilePathRestampTests.DocumentWorld();
        World.ReplaceWith(fresh);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<CoreAncestorRestamper>();
            services.AddSingleton(_ => World.Restamper);

            // Batch 4 integration (task 146): the re-file is owned through the one resolver before the write — a module
            // boundary here (the double applies the write); the owner decision is not this test's subject.
            services.RemoveAll<IRecordOwnershipResolver>();
            services.AddSingleton<IRecordOwnershipResolver>(new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble());
        });
    }
}
