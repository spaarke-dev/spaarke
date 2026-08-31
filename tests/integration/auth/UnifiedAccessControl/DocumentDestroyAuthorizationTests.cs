using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Findings C2 and C3 (unified-access-control-r2 task 022) — the two document DESTROY routes that
/// carried no per-document authorization at all.
///
/// <para><b>C2</b> — <c>DELETE /api/documents/{documentId}</c>: destroys the Dataverse row AND the SPE
/// file, app-only, and is reachable from a shipped client hook. Any authenticated caller could destroy
/// any document by GUID.</para>
///
/// <para><b>C3</b> — <c>DELETE /api/v1/documents/{id}</c>: a second app-only destroy path, on a group
/// whose <c>/download</c> sibling task 002 HAD gated. The asymmetry is the finding — one route on the
/// group checked the caller, the neighbouring destroy route did not.</para>
///
/// <para><b>The load-bearing assertion is the destroy count, not the status.</b> Task 009's lesson:
/// a 403 assertion alone passes even if the mutation was already issued. Both destroy paths are
/// recorded by <see cref="DocumentDestroyAuthorizationTestFixture"/>, so "denied" means "nothing was
/// destroyed", not merely "the response said no".</para>
///
/// <para><b>Why rights are stated on the token.</b> Offline the real access data source fails closed,
/// so every caller denies before AND after the gate exists and the negative assertions would be
/// vacuous. The fixture substitutes the data source and reads rights off the bearer token, so this
/// class can exercise a caller who genuinely HOLDS Delete — which is what makes the denial cases mean
/// something.</para>
/// </summary>
public class DocumentDestroyAuthorizationTests
    : IClassFixture<DocumentDestroyAuthorizationTestFixture>
{
    private readonly DocumentDestroyAuthorizationTestFixture _fixture;

    private static readonly Guid DocumentId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    public DocumentDestroyAuthorizationTests(DocumentDestroyAuthorizationTestFixture fixture)
    {
        _fixture = fixture;

        // One fixture instance per class — the recorders accumulate across every test in it, so a
        // "destroyed nothing" assertion would otherwise fail on (or pass on) another test's residue.
        _fixture.Reset();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // C2 — DELETE /api/documents/{documentId}
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteDocument_ForCallerWithoutDeleteRight_IsDeniedAndDestroysNothing()
    {
        // Arrange — a caller who can READ the document but holds no Delete right on it. Read is the
        // interesting case: it is the level an ordinary viewer has, and before task 022 it was enough
        // to destroy the document and its SPE file.
        using var client = _fixture.CreateClientWithRights("ReadAccess");

        // Act
        var response = await client.DeleteAsync($"/api/documents/{DocumentId}");

        // Assert — nothing was destroyed. This is the assertion that matters.
        _fixture.DeletedDocumentIds.Should().BeEmpty(
            "C2 is closed: the caller is authorized BEFORE DocumentCheckoutService.DeleteAsync runs. " +
            "That service destroys the SPE file as well as the Dataverse row and takes no caller " +
            "identity of its own, so this filter is the entire boundary");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("sdap.access.deny.insufficient_rights",
            "the denial must be a genuine RIGHTS decision");
        body.Should().NotContain("unknown_operation",
            "an unknown_operation denial would mean the \"delete\" key is missing from " +
            "OperationAccessPolicy — which is not a gate, it is an unconditional 403 for every " +
            "caller, including the ones who legitimately hold Delete");
    }

    [Fact]
    public async Task DeleteDocument_ForCallerWithDeleteRight_IsAllowedAndDestroys()
    {
        // Arrange — the caller genuinely holds Delete.
        using var client = _fixture.CreateClientWithRights("ReadAccess,DeleteAccess");

        // Act
        var response = await client.DeleteAsync($"/api/documents/{DocumentId}");

        // Assert — the gate lets the legitimate caller through. Without this case the gate could be
        // "deny everyone" and every negative test above would still pass.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.DeletedDocumentIds.Should().ContainSingle()
            .Which.Should().Be(DocumentId);
    }

    [Fact]
    public async Task DeleteDocument_ForUnauthenticatedCaller_IsRejectedAndDestroysNothing()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.DeleteAsync($"/api/documents/{DocumentId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _fixture.DeletedDocumentIds.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // C3 — DELETE /api/v1/documents/{id}
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteDataverseDocument_ForCallerWithoutDeleteRight_IsDeniedAndDestroysNothing()
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess");

        var response = await client.DeleteAsync($"/api/v1/documents/{DocumentId}");

        _fixture.DeletedDataverseDocumentIds.Should().BeEmpty(
            "C3 is closed: the second destroy path is authorized too. Gating C2 alone would have left " +
            "the same capability reachable at a different URL — exactly the shape of A-1, where " +
            "/download was gated and /content was not");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("sdap.access.deny.insufficient_rights");
        body.Should().NotContain("unknown_operation");
    }

    [Fact]
    public async Task DeleteDataverseDocument_ForCallerWithDeleteRight_IsAllowedAndDestroys()
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess,DeleteAccess");

        var response = await client.DeleteAsync($"/api/v1/documents/{DocumentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _fixture.DeletedDataverseDocumentIds.Should().ContainSingle()
            .Which.Should().Be(DocumentId.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The two destroy routes must AGREE — the C3 finding was an asymmetry, not a URL
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BothDestroyRoutes_AgreeOnAuthorizationForTheSameCallerAndDocument()
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess");

        var checkoutPath = await client.DeleteAsync($"/api/documents/{DocumentId}");
        var dataversePath = await client.DeleteAsync($"/api/v1/documents/{DocumentId}");

        checkoutPath.StatusCode.Should().Be(dataversePath.StatusCode,
            "two routes that destroy the same document, reached by the same caller in the same " +
            "request context, must reach the same decision. Their DISAGREEMENT was the finding: C3's " +
            "group already gated /download while its neighbouring destroy route was open. This is the " +
            "assertion that catches a third destroy route being added without a filter");

        _fixture.DeletedDocumentIds.Should().BeEmpty();
        _fixture.DeletedDataverseDocumentIds.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // H2 — the mutate/disclose pair on /api/v1/documents/{id}
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PutDataverseDocument_ForCallerWithoutWriteRight_IsDeniedAndWritesNothing()
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess");

        var response = await client.PutAsJsonAsync(
            $"/api/v1/documents/{DocumentId}", new { name = "renamed-by-anyone.pdf" });

        _fixture.UpdatedDataverseDocumentIds.Should().BeEmpty(
            "H2: the PUT was app-only tamper by GUID — any authenticated caller could rewrite any " +
            "document row's fields");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("sdap.access.deny.insufficient_rights");
        body.Should().NotContain("unknown_operation",
            "an unknown_operation denial would mean the \"write\" key is missing — which denies the " +
            "callers who legitimately hold Write too");
    }

    [Fact]
    public async Task PutDataverseDocument_ForCallerWithWriteRight_IsAllowedAndWrites()
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess,WriteAccess");

        var response = await client.PutAsJsonAsync(
            $"/api/v1/documents/{DocumentId}", new { name = "renamed-by-owner.pdf" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.UpdatedDataverseDocumentIds.Should().ContainSingle()
            .Which.Should().Be(DocumentId.ToString());
    }

    /// <summary>
    /// The GET was the discovery step: it returns <c>GraphDriveId</c> and <c>GraphItemId</c> — the exact
    /// SPE pointers the destroy and bulk-download paths consume. Gating the acts while leaving the
    /// pointer disclosure open would have left the reconnaissance half of the surface intact.
    /// </summary>
    [Fact]
    public async Task GetDataverseDocument_ForCallerWithoutReadRight_IsDenied()
    {
        using var client = _fixture.CreateClientWithRights("");

        var response = await client.GetAsync($"/api/v1/documents/{DocumentId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("sdap.access.deny.insufficient_rights");
    }

    [Fact]
    public async Task GetDataverseDocument_ForCallerWithReadRight_IsAllowed()
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess");

        var response = await client.GetAsync($"/api/v1/documents/{DocumentId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // H3 — the checkout family on /api/documents/{documentId}
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// All three mutating checkout routes required Write and had none of it. The replaced comment on
    /// <c>/checkout</c> claimed "PCF controls button visibility based on Dataverse security profile /
    /// actual permissions enforced by Graph API via OBO" — client-side button visibility is not
    /// enforcement, and this path is app-only, so nothing downstream evaluated the caller either.
    /// <c>/checkout</c> is the sharpest of the three: it returns an EDITABLE url.
    /// </summary>
    [Theory]
    [InlineData("checkout")]
    [InlineData("checkin")]
    [InlineData("discard")]
    public async Task CheckoutFamilyRoute_ForCallerWithoutWriteRight_IsDenied(string route)
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess");

        var response = await client.PostAsync($"/api/documents/{DocumentId}/{route}", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("sdap.access.deny.insufficient_rights");
        body.Should().NotContain("unknown_operation");
    }

    /// <summary>
    /// The positive direction for the checkout family. Asserting "not 403" rather than a success
    /// status is deliberate: <c>DocumentCheckoutService</c>'s checkout/checkin/discard paths are not
    /// substituted here (only <c>DeleteAsync</c> is, because only the destroy needed to be provably
    /// prevented), so offline they fail inside the handler for reasons unrelated to authorization.
    /// What this pins is the thing that matters and that the negative cases cannot show: the gate is
    /// not "deny everyone". Without it, deleting the rights check entirely would keep every negative
    /// test above green — the zero-failure perturbation task 009 hit twice.
    /// </summary>
    [Theory]
    [InlineData("checkout")]
    [InlineData("checkin")]
    [InlineData("discard")]
    public async Task CheckoutFamilyRoute_ForCallerWithWriteRight_IsNotDeniedByAuthorization(string route)
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess,WriteAccess");

        var response = await client.PostAsync($"/api/documents/{DocumentId}/{route}", content: null);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "a caller holding Write must pass the filter; whatever the handler then does offline is a " +
            "different question");
    }

    /// <summary>
    /// <c>analyze</c> requires <c>write</c> — an owner decision (2026-08-24), not a default. The case
    /// for <c>read</c> was genuine: the analysis lands on a different entity than the one this filter
    /// authorizes, which is exactly why <c>finance.confirm</c> deliberately does NOT require Create.
    /// <c>write</c> won because the route commits real resources on the caller's say-so — AI spend
    /// plus queued background work — and the profile fields it populates are the document's own.
    /// </summary>
    [Fact]
    public async Task TriggerAnalysis_ForCallerWithReadOnly_IsDenied()
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess");

        var response = await client.PostAsync($"/api/documents/{DocumentId}/analyze", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "queueing analysis spends AI credits and enqueues background work — Read is not enough");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("sdap.access.deny.insufficient_rights");
        body.Should().NotContain("unknown_operation");
    }

    [Fact]
    public async Task TriggerAnalysis_ForCallerWithWriteRight_IsNotDeniedByAuthorization()
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess,WriteAccess");

        var response = await client.PostAsync($"/api/documents/{DocumentId}/analyze", content: null);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "a caller holding Write must pass the filter");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The URL-minting reads — five routes that hand out urls outliving the request
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Unlike <c>/content</c> and <c>/download</c>, these five reach SPE through OBO, so Graph
    /// already enforced the caller's own SPE access. The gate is a SECOND boundary and it narrows
    /// deliberately: SPE permission is container-scoped and coarser than per-document Dataverse
    /// rights, so a caller with container access but no Read on the row is now refused. That caller
    /// seeing another client's document is the disclosure spec FR-01 exists to close.
    /// </summary>
    [Theory]
    [InlineData("preview-url")]
    [InlineData("preview")]
    [InlineData("office")]
    [InlineData("open-links")]
    [InlineData("view-url")]
    public async Task UrlMintingRoute_ForCallerWithoutReadRight_IsDeniedBeforeAnyUrlIsMinted(string route)
    {
        using var client = _fixture.CreateClientWithRights("");

        var response = await client.GetAsync($"/api/documents/{DocumentId}/{route}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a url that outlives the request must not be mintable by a caller with no rights on the " +
            "document");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("sdap.access.deny.insufficient_rights");
        body.Should().NotContain("unknown_operation");
    }

    /// <summary>
    /// The positive direction, so the five gates are not merely "deny everyone". Asserting "not 403"
    /// rather than a success status: these handlers call real SPE/Graph paths that cannot succeed
    /// offline, so what is pinned here is the authorization verdict, not the payload.
    /// </summary>
    [Theory]
    [InlineData("preview-url")]
    [InlineData("preview")]
    [InlineData("office")]
    [InlineData("open-links")]
    [InlineData("view-url")]
    public async Task UrlMintingRoute_ForCallerWithReadRight_IsNotDeniedByAuthorization(string route)
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess");

        var response = await client.GetAsync($"/api/documents/{DocumentId}/{route}");

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetCheckoutStatus_ForCallerWithoutReadRight_IsDenied()
    {
        using var client = _fixture.CreateClientWithRights("");

        var response = await client.GetAsync($"/api/documents/{DocumentId}/checkout-status");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "checkout-status discloses WHO holds the lock and since when, for any document by GUID");
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("sdap.access.deny.insufficient_rights");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The keys these gates depend on — least privilege, pinned
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>DocumentAuthorizationFilterExtensions.AddDocumentAuthorizationFilter</c>'s own
    /// <c>&lt;param&gt;</c> doc has always read <c>'e.g. "read", "write", "delete"'</c>, but only
    /// <c>"read"</c> was ever registered (task 003). Attaching a filter with an unregistered string is
    /// not a no-op — <c>GetRequiredRights</c> throws, the filter's catch returns 500, and every caller
    /// is refused. So these keys had to land BEFORE the gates that use them.
    /// </summary>
    [Theory]
    [InlineData("write", AccessRights.Write)]
    [InlineData("delete", AccessRights.Delete)]
    public void RecordScopedMutationOperation_ResolvesWithLeastPrivilegeRights(
        string operation, AccessRights expected)
    {
        OperationAccessPolicy.IsOperationSupported(operation).Should().BeTrue(
            "\"{0}\" is what the filter's own documented contract tells a caller to pass; if it does " +
            "not resolve, attaching it makes the route an unconditional 403", operation);

        OperationAccessPolicy.GetRequiredRights(operation).Should().Be(expected);
    }

    /// <summary>
    /// Pins the choice of Delete ALONE for "delete". Dataverse models Delete and Write as independent
    /// rights: a principal may hold Delete without Write and may legitimately destroy. Requiring both
    /// would deny that caller for no security gain — the over-restriction direction of the same
    /// mistake as under-granting, and less visible because it looks like caution.
    /// </summary>
    [Fact]
    public void DeleteOperation_DoesNotAlsoRequireWrite()
    {
        var rights = OperationAccessPolicy.GetRequiredRights("delete");

        rights.Should().Be(AccessRights.Delete);
        rights.Should().NotHaveFlag(AccessRights.Write,
            "Delete and Write are independent Dataverse rights; a Delete-without-Write principal " +
            "must not be denied");
        rights.Should().NotHaveFlag(AccessRights.Share);
    }

    /// <summary>
    /// Registering two more keys must not have made the policy permissive. Pairs with the identical
    /// guard task 003 left behind for its four keys.
    /// </summary>
    [Fact]
    public void UnregisteredOperation_StillResolvesToNothing()
    {
        OperationAccessPolicy.IsOperationSupported("document.obliterate").Should().BeFalse();

        var act = () => OperationAccessPolicy.GetRequiredRights("document.obliterate");
        act.Should().Throw<ArgumentException>();
    }
}
