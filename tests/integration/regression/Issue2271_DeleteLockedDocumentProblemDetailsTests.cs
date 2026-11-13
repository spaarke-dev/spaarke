using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// #2271 residual (spaarke-SPA-external-access-platform-r4 task 002, FR-09): <c>DELETE /api/documents/{id}</c> on a
/// checked-out document answered 409 with the bare <see cref="DocumentLockedError"/> — no <c>status</c>, no <c>title</c> —
/// so a client's RFC 7807 parser read it as nothing and could only say "something went wrong". The checkout route's 409
/// already carried the ProblemDetails members; the delete 409 now answers the same shape, from the same builder.
/// </summary>
/// <remarks>
/// Driven through the REAL route, filters and handler on <see cref="DocumentDestroyAuthorizationTestFixture"/>; the only
/// double is the checkout service's delete outcome (a checked-out document, refused before anything is deleted).
/// Must-fire: the locked delete is a 409 that parses as ProblemDetails and names the lock holder. Must-not-fire: a document
/// that is not checked out is deleted (200), and a destroy-authorization denial is untouched (403, not this 409).
/// </remarks>
public sealed class Issue2271_DeleteLockedDocumentProblemDetailsTests : IClassFixture<DocumentDestroyAuthorizationTestFixture>
{
    private static readonly Guid DocumentId = Guid.Parse("22710000-0000-4000-8000-000000002271");
    private static readonly Guid LockHolder = Guid.Parse("22710000-0000-4000-8000-0000000000aa");
    private static readonly DateTime LockedAt = new(2026, 11, 10, 15, 30, 0, DateTimeKind.Utc);

    private readonly DocumentDestroyAuthorizationTestFixture _fixture;

    public Issue2271_DeleteLockedDocumentProblemDetailsTests(DocumentDestroyAuthorizationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private void CheckOut(Guid documentId) =>
        _fixture.CheckedOutLocks[documentId] = new DocumentLockedError(
            Error: "document_locked",
            Detail: "This document is checked out by Pat Lawyer.",
            CheckedOutBy: new CheckoutUserInfo(LockHolder, "Pat Lawyer", null),
            CheckedOutAt: LockedAt);

    [Fact]
    public async Task DeleteDocument_WhenCheckedOut_Is409ProblemDetails_NamingTheLockHolder_AndDeletesNothing()
    {
        CheckOut(DocumentId);
        using var client = _fixture.CreateClientWithRights("ReadAccess,DeleteAccess");

        using var response = await client.DeleteAsync($"/api/documents/{DocumentId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();

        // The RFC 7807 members a client parser keys on.
        var problem = JsonSerializer.Deserialize<ProblemDetails>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        problem!.Status.Should().Be(409);
        problem.Title.Should().Be("Document Locked");
        problem.Detail.Should().Be("This document is checked out by Pat Lawyer.");

        // The DocumentLockedError members keep their names (every current reader is unaffected), plus the correlation id.
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        root.GetProperty("error").GetString().Should().Be("document_locked");
        root.GetProperty("checkedOutBy").GetProperty("name").GetString().Should().Be("Pat Lawyer");
        root.GetProperty("checkedOutBy").GetProperty("id").GetGuid().Should().Be(LockHolder);
        root.GetProperty("checkedOutAt").GetDateTime().Should().Be(LockedAt);
        root.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();

        _fixture.DeletedDocumentIds.Should().BeEmpty("a checked-out document is refused before anything is deleted");
    }

    [Fact]
    public async Task DeleteDocument_WhenCheckedOut_AnswersExactlyTheLockedMemberSet()
    {
        // The member set the checkout route's 409 has sent since compose-r8 task 016 (both now come from one builder,
        // DocumentOperationsEndpoints.DocumentLocked). Pinned on the delete route, the one #2271 broke; the checkout route
        // is not driven here (this fixture doubles only the delete outcome).
        CheckOut(DocumentId);
        using var client = _fixture.CreateClientWithRights("ReadAccess,DeleteAccess");

        using var response = await client.DeleteAsync($"/api/documents/{DocumentId}");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "status", "title", "error", "detail", "checkedOutBy", "checkedOutAt", "correlationId");
    }

    [Fact]
    public async Task DeleteDocument_WhenNotCheckedOut_IsDeleted_NotA409()
    {
        CheckOut(Guid.NewGuid()); // another document is locked; this one is not
        using var client = _fixture.CreateClientWithRights("ReadAccess,DeleteAccess");

        using var response = await client.DeleteAsync($"/api/documents/{DocumentId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.DeletedDocumentIds.Should().ContainSingle().Which.Should().Be(DocumentId);
    }

    [Fact]
    public async Task DeleteDocument_WhenCheckedOut_ButTheCallerLacksDelete_Is403_NotTheLockedAnswer()
    {
        // The lock is never disclosed to a caller the destroy gate refuses: authorization runs first.
        CheckOut(DocumentId);
        using var client = _fixture.CreateClientWithRights("ReadAccess");

        using var response = await client.DeleteAsync($"/api/documents/{DocumentId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Pat Lawyer");
        _fixture.DeletedDocumentIds.Should().BeEmpty();
    }
}
