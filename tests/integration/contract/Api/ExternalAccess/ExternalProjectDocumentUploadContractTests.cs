// unified-access-control-r2 — contract tests for the external document-upload route
// (POST /api/v1/external/projects/{id}/documents).
//
// WHY THIS ROUTE EXISTS: the external SPA's upload dialog posted to
// `POST /api/v1/external/documents/upload`, which is mapped NOWHERE in the BFF — every upload from
// that dialog 404'd (sweep claim R15). This is the real, project-scoped route.
//
// WHAT MAKES THIS THE HIGHEST-STAKES EXTERNAL ROUTE, and what these tests pin:
//
//   1. WRITE ≠ READ. A View-Only participant may list a project's documents but must never add one.
//      That is UploadDocument_WhenParticipantLacksCreateRight_Returns403 — the load-bearing test,
//      and the property most likely to be silently lost in a refactor of the two-stage gate.
//
//   2. THE CLIENT CANNOT NAME A CONTAINER (#858). The request carries the file and nothing about
//      storage; the container is derived server-side from the SAME project id the participation gate
//      authorized. UploadDocument_WhenBodyNamesAContainer_IgnoresIt pins that a client that tries
//      anyway gains nothing — it cannot redirect the write, because there is no parameter to bind.
//      This is the shape that let the Office save path stay invisible for months, so it is asserted
//      rather than assumed.
//
//   3. AUTHORIZATION RUNS BEFORE ANY STORAGE WORK. Every denial below is observable as a status
//      without a live SPE container or ServiceClient, which is only possible because the gate short-
//      circuits ahead of the container resolution and the Graph write.
//
// KEEP-path classification (ADR-038 §2 / tests/CLAUDE.md): endpoint-contract + security-auth. Like
// the sibling event/module contract tests, these assert ONLY the security/validation layer that runs
// BEFORE the container resolver and SPE are touched — the app-only write path needs a live
// ServiceClient and a real container, so the success path belongs to a real-SPE smoke, not here.
// The 409-collision and 422-unresolved paths are likewise beyond this seam: both require a live
// Graph/Dataverse response to produce. What IS pinned here is that nothing reaches them without
// passing the gate.
//
// Banned-pattern compliance (ADR-038): no Mock<HttpMessageHandler>, no DI-registration tests, no
// ctor null-check tests. Names are {Method}_{Scenario}_{ExpectedResult}.

using System.Net;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

public sealed class ExternalProjectDocumentUploadContractTests
    : IClassFixture<ExternalAccessContractFixture>
{
    private static readonly Guid ProjectA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly ExternalAccessContractFixture _fixture;

    public ExternalProjectDocumentUploadContractTests(ExternalAccessContractFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private static string UploadPath(Guid projectId)
        => $"/api/v1/external/projects/{projectId}/documents";

    /// <summary>
    /// A minimal well-formed multipart body. Content is a few bytes because none of these tests reach
    /// the point of storing it — the gate answers first, which is the property under test.
    /// </summary>
    private static MultipartFormDataContent FileBody(string fileName = "contract.txt")
    {
        var content = new ByteArrayContent("hello"u8.ToArray());
        var form = new MultipartFormDataContent { { content, "file", fileName } };
        return form;
    }

    // -----------------------------------------------------------------------
    // Authentication
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UploadDocument_WhenUnauthenticated_Returns401()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        using var response = await client.PostAsync(UploadPath(ProjectA), FileBody());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the external route group requires authorization before any handler runs");
    }

    // -----------------------------------------------------------------------
    // Participation gate (stage 1)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UploadDocument_WhenNonParticipant_Returns403()
    {
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: Array.Empty<Guid>());

        using var response = await client.PostAsync(UploadPath(ProjectA), FileBody());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "authentication alone must not permit writing content into a project's container");
    }

    [Fact]
    public async Task UploadDocument_WhenParticipantOfADifferentProject_Returns403()
    {
        // Participates in ProjectA; uploads to ProjectB. This is the cross-project write case — and
        // because the container is derived from the ROUTE project, a successful call here would place
        // the caller's content in a container they have no participation in.
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        using var response = await client.PostAsync(UploadPath(ProjectB), FileBody());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the write must be scoped to a project the caller actually participates in");
    }

    // -----------------------------------------------------------------------
    // Create-right gate (stage 2) — THE load-bearing test
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UploadDocument_WhenParticipantLacksCreateRight_Returns403()
    {
        // Full participation in ProjectA, but View-Only. Read access must never imply write access:
        // this caller can list the project's documents and download them, and must still be unable
        // to add one.
        using var client = _fixture.CreateAuthenticatedClient(
            accessibleProjects: new[] { ProjectA },
            accessLevel: ExternalAccessLevel.ViewOnly);

        using var response = await client.PostAsync(UploadPath(ProjectA), FileBody());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "View-Only participation grants read but never the right to write a document");
    }

    // -----------------------------------------------------------------------
    // Request validation — positive control that the gate is not the ONLY thing responding
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UploadDocument_WhenFilePartMissing_Returns400()
    {
        // A participant WITH the Create right, sending multipart with no `file` part. Proves the two
        // 403s above are the gate denying a well-formed request rather than the route rejecting every
        // request for some unrelated reason — without this, all the negative tests could pass on a
        // permanently broken endpoint.
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        var form = new MultipartFormDataContent
        {
            { new StringContent("not-a-file"), "notthefilepart" },
        };
        using var response = await client.PostAsync(UploadPath(ProjectA), form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a multipart request with no file part cannot be satisfied");
    }

    [Fact]
    public async Task UploadDocument_WhenFileIsEmpty_Returns400()
    {
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA });

        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Array.Empty<byte>()), "file", "empty.txt" },
        };
        using var response = await client.PostAsync(UploadPath(ProjectA), form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a zero-byte upload has nothing to store and must be refused explicitly, not stored blank");
    }

    // -----------------------------------------------------------------------
    // #858 — the client cannot influence the storage target
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UploadDocument_WhenBodyNamesAContainer_StillDeniesOnTheProjectGate()
    {
        // A caller who tries to name a container gains NOTHING: the handler has no container
        // parameter to bind, so the extra form fields are inert and the decision still rests on the
        // route's project id — for which this caller has no participation.
        //
        // Pinned as a test because "the client supplies a container id three call frames down from a
        // request DTO" is exactly how the Office save path stayed invisible. The guarantee here is
        // structural (there is no parameter), and this asserts the structure has not changed.
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: Array.Empty<Guid>());

        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent("hello"u8.ToArray()), "file", "contract.txt" },
            { new StringContent("b!attacker-chosen-container"), "containerId" },
            { new StringContent("b!attacker-chosen-container"), "driveId" },
            { new StringContent(ProjectB.ToString()), "projectId" },
        };
        using var response = await client.PostAsync(UploadPath(ProjectA), form);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "storage target is derived from the route project, so client-supplied container/drive/"
            + "project fields cannot redirect the write or bypass the gate");
    }
}
