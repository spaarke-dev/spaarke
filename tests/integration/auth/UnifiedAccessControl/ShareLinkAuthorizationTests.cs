using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 072 — <c>POST /api/documents/{documentId}/share-link</c>.
/// </summary>
/// <remarks>
/// <para>
/// The route was the ONE route on the <c>/api/documents</c> group with no per-document filter, and the
/// one that mints a credential: <c>linkType: "view", scope: "anonymous", expiration: null</c>. Its
/// authority was the caller's container-scoped OBO access, which is coarser than per-document Dataverse
/// rights.
/// </para>
/// <para>
/// <b>The load-bearing assertion in every denial test here is <see cref="ShareLinkTestFixture.MintedLinks"/>
/// being empty — not the status code.</b> Task 009's lesson, and it matters more here than for a destroy:
/// a 403 returned AFTER Graph has already issued the URL is not a denial at all, and unlike a delete
/// there is nothing to roll back. A minted SPE link is not revocable through Dataverse.
/// </para>
/// </remarks>
public class ShareLinkAuthorizationTests : IClassFixture<ShareLinkTestFixture>
{
    private readonly ShareLinkTestFixture _fixture;

    /// <summary>A document id whose SPE pointers the fixture answers for.</summary>
    private const string DocumentId = "11111111-1111-1111-1111-111111111111";

    private static string Route => $"/api/documents/{DocumentId}/share-link";

    public ShareLinkAuthorizationTests(ShareLinkTestFixture fixture)
    {
        _fixture = fixture;
        // One IClassFixture instance per class, so the recorder accumulates across tests. A
        // "minted nothing" assertion would otherwise pass or fail on another test's residue.
        _fixture.Reset();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The defect: no per-document authorization
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateShareLink_WhenCallerLacksShare_IsDeniedAndMintsNothing()
    {
        // Read is what the eight sibling routes require, and what the composer's caller plausibly holds.
        // It is NOT enough to publish a durable handle to the document.
        var client = _fixture.CreateClientWithRights("ReadAccess");

        var response = await client.PostAsJsonAsync(Route, new { });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.MintedLinks.Should().BeEmpty(
            "the createLink must not be issued for a caller without Share — a minted SPE URL cannot be "
            + "taken back, so a 403 returned after the fact is not a denial");
    }

    [Fact]
    public async Task CreateShareLink_WhenCallerHoldsWriteButNotShare_IsDeniedAndMintsNothing()
    {
        // Write is not Share. Dataverse models them independently, and a caller who may EDIT a document
        // has not thereby been granted permission to publish it outside the platform.
        var client = _fixture.CreateClientWithRights("ReadAccess,WriteAccess");

        var response = await client.PostAsJsonAsync(Route, new { });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.MintedLinks.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateShareLink_WithNoToken_IsRefusedBeforeAnyGraphCall()
    {
        var client = _fixture.CreateClient();

        var response = await client.PostAsJsonAsync(Route, new { });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        _fixture.MintedLinks.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The authorized path still works — otherwise the gate is indistinguishable
    // from having broken the feature
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateShareLink_WhenCallerHoldsShare_MintsAnOrganizationScopedLink()
    {
        var client = _fixture.CreateClientWithRights("ReadAccess,ShareAccess");

        var response = await client.PostAsJsonAsync(Route, new { });

        // Body in the failure message: a bare status-code assertion on the ALLOWED path is painful to
        // diagnose, because every handler-side fault (a DI mistake, a missing option, a pointer
        // validation) presents identically as a non-200.
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "body was: {0}", await response.Content.ReadAsStringAsync());
        _fixture.MintedLinks.Should().HaveCount(1);

        // Organization, NOT anonymous — the caller did not ask for external reach. This is the
        // regression that matters most: the old code passed scope:"anonymous" unconditionally.
        _fixture.MintedLinks.Single().Scope.Should().Be("organization");
    }

    [Fact]
    public async Task CreateShareLink_WhenAuthorized_AlwaysSetsAnExpiry()
    {
        var client = _fixture.CreateClientWithRights("ReadAccess,ShareAccess");

        var response = await client.PostAsJsonAsync(Route, new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.MintedLinks.Single().Expiration.Should().NotBeNull(
            "expiration: null was the pre-072 behaviour and is the reason a revoked user's link kept "
            + "working forever; no code path may reach Graph without a bounded lifetime");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Anonymous is opt-in, and capped harder than organization scope
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateShareLink_WhenExternalRecipientsRequested_MintsAnonymousWithShorterLifetime()
    {
        var client = _fixture.CreateClientWithRights("ReadAccess,ShareAccess");

        var organizationScoped = await client.PostAsJsonAsync(Route, new { });
        var anonymous = await client.PostAsJsonAsync(
            Route, new { allowExternalRecipients = true });

        organizationScoped.StatusCode.Should().Be(HttpStatusCode.OK);
        anonymous.StatusCode.Should().Be(HttpStatusCode.OK);

        var minted = _fixture.MintedLinks.ToList();
        minted.Should().HaveCount(2);

        var orgLink = minted.Single(m => m.Scope == "organization");
        var anonLink = minted.Single(m => m.Scope == "anonymous");

        // The anonymous audience is unbounded and unauthenticated — nobody can enumerate who holds the
        // URL — so time is the only containment, and it must be tighter.
        anonLink.Expiration.Should().BeBefore(orgLink.Expiration!.Value,
            "an anonymous link's lifetime must be capped harder than an organization-scoped one");
    }

    [Fact]
    public async Task CreateShareLink_ForExternalRecipients_StillRequiresShare()
    {
        var client = _fixture.CreateClientWithRights("ReadAccess");

        var response = await client.PostAsJsonAsync(
            Route, new { allowExternalRecipients = true });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.MintedLinks.Should().BeEmpty(
            "opting into external reach must not be a way around the per-document gate");
    }

    /// <summary>
    /// The response carries the granted scope and the expiry so a sender is not left assuming the link
    /// is permanent or tenant-restricted when it is neither.
    /// </summary>
    [Fact]
    public async Task CreateShareLink_WhenAuthorized_ReportsScopeAndExpiryToTheCaller()
    {
        var client = _fixture.CreateClientWithRights("ReadAccess,ShareAccess");

        var response = await client.PostAsJsonAsync(
            Route, new { allowExternalRecipients = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ShareLinkResponseBody>();
        body.Should().NotBeNull();
        body!.Url.Should().NotBeNullOrWhiteSpace();
        body.Scope.Should().Be("anonymous");
        body.ExpiresAt.Should().BeAfter(DateTimeOffset.MinValue);
    }

    private sealed record ShareLinkResponseBody(string Url, DateTimeOffset ExpiresAt, string Scope);
}

/// <summary>
/// The anonymous kill-switch, in its own class because it needs a different configuration and
/// <c>ShareLinkOptions</c> is bound with <c>ValidateOnStart</c> at host build time.
/// </summary>
public class ShareLinkAnonymousDisabledTests : IClassFixture<AnonymousDisabledShareLinkTestFixture>
{
    private readonly AnonymousDisabledShareLinkTestFixture _fixture;

    private const string DocumentId = "11111111-1111-1111-1111-111111111111";

    public ShareLinkAnonymousDisabledTests(AnonymousDisabledShareLinkTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task CreateShareLink_WhenAnonymousDisabled_RefusesRatherThanSilentlyDowngrading()
    {
        var client = _fixture.CreateClientWithRights("ReadAccess,ShareAccess");

        var response = await client.PostAsJsonAsync(
            $"/api/documents/{DocumentId}/share-link", new { allowExternalRecipients = true });

        // Refused, NOT downgraded. A silent downgrade to organization scope yields a link that looks
        // fine to the sender and is dead on arrival for the external recipient — the failure mode
        // hardest to diagnose from a support ticket.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.MintedLinks.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateShareLink_WhenAnonymousDisabled_OrganizationScopedLinksStillWork()
    {
        var client = _fixture.CreateClientWithRights("ReadAccess,ShareAccess");

        var response = await client.PostAsJsonAsync(
            $"/api/documents/{DocumentId}/share-link", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.MintedLinks.Single().Scope.Should().Be("organization",
            "the switch governs the anonymous audience only; it must not disable sharing outright");
    }
}

/// <summary>
/// Records every <c>createLink</c> that reached Graph, with the scope and expiration actually requested.
/// </summary>
/// <remarks>
/// Extends <see cref="DocumentDestroyAuthorizationTestFixture"/> rather than forking it — that fixture
/// is explicitly "not sealed" for this purpose, and it already supplies the two things this route needs:
/// token-stated rights via <c>IAccessDataSource</c>, and an <c>IDocumentDataverseService</c> that answers
/// <c>GetDocumentAsync</c> with SPE pointers (without them the handler 404/409s before authorization is
/// even interesting).
/// </remarks>
public class ShareLinkTestFixture : DocumentDestroyAuthorizationTestFixture
{
    /// <summary>Every createLink that actually reached the Graph layer.</summary>
    public ConcurrentBag<MintedLink> MintedLinks { get; } = new();

    /// <summary>Scope and expiration as requested — the evidence a denial test needs.</summary>
    public sealed record MintedLink(string Scope, DateTimeOffset? Expiration);

    /// <summary><c>false</c> in <see cref="AnonymousDisabledShareLinkTestFixture"/>.</summary>
    protected virtual bool AnonymousLinksEnabled => true;

    public new void Reset()
    {
        base.Reset();
        MintedLinks.Clear();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseSetting(
            "Documents:ShareLinks:AnonymousLinksEnabled",
            AnonymousLinksEnabled ? "true" : "false");

        builder.ConfigureTestServices(services =>
        {
            // The base fixture registers a StubSpeFileStore for the bulk-download path; replace it with
            // one that ALSO records createLink. RemoveAll first — the base registration would otherwise
            // win or lose depending on ordering.
            //
            // SCOPED, not singleton, and that is load-bearing: SpeFileStore's four constructor
            // dependencies (ContainerOperations, DriveItemOperations, UploadSessionManager,
            // UserOperations) are all registered Scoped in DocumentsModule, so a singleton factory
            // receives the ROOT provider and GetRequiredService throws "Cannot resolve scoped service
            // from root provider". That surfaces as a 500 on the AUTHORIZED path only — the denial tests
            // still pass, because endpoint-filter rejection happens before handler parameters are
            // resolved. A green denial suite next to a broken allowed path is exactly the shape that
            // makes a gate look verified when it is not. The recorder is captured from the fixture
            // instance, so it still accumulates across scopes.
            services.RemoveAll<SpeFileStore>();
            services.AddScoped<SpeFileStore>(sp => new RecordingShareLinkSpeFileStore(sp, MintedLinks));

            // The base fixture's document double returns GraphDriveId = "drive-{id}", which
            // ValidateSpePointers rejects — SPE drive ids MUST start with "b!" — so every authorized
            // request would 409 before reaching createLink and the allowed-path tests would be
            // indistinguishable from a denial. Sufficient for bulk download (which never validates the
            // prefix); not sufficient here.
            services.RemoveAll<IDocumentDataverseService>();
            services.AddSingleton<IDocumentDataverseService>(new ShareableDocumentDataverseService());
        });
    }

    /// <summary>
    /// A document with SPE pointers the share-link handler will actually accept: a <c>b!</c>-prefixed
    /// drive id and <c>HasFile = true</c>.
    /// </summary>
    private sealed class ShareableDocumentDataverseService : IDocumentDataverseService
    {
        public Task<DocumentEntity?> GetDocumentAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<DocumentEntity?>(new DocumentEntity
            {
                Id = id,
                Name = "Shareable Document",
                FileName = $"{id}.pdf",
                ContainerId = Guid.NewGuid().ToString(),
                GraphDriveId = $"b!drive-{id}",
                GraphItemId = $"item-{id}",
                HasFile = true
            });

        // Everything else throws rather than returning a default, so a future test that strays onto an
        // unmodelled path fails loudly instead of asserting against a fabricated answer.
        public Task DeleteDocumentAsync(string id, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task UpdateDocumentAsync(string id, UpdateDocumentRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task UpdateDocumentFieldsAsync(string documentId, Dictionary<string, object?> fields, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByContainerAsync(string containerId, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<DocumentAccessLevel> GetUserAccessAsync(string userId, string documentId, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<DocumentEntity?> GetDocumentByEmailLookupAsync(Guid emailId, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<DocumentEntity?> GetEmailArchiveByCommunicationAsync(Guid communicationId, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByParentAsync(Guid parentDocumentId, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByMatterAsync(Guid matterId, Guid? excludeDocumentId = null, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByProjectAsync(Guid projectId, Guid? excludeDocumentId = null, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByInvoiceAsync(Guid invoiceId, Guid? excludeDocumentId = null, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByWorkAssignmentAsync(Guid workAssignmentId, Guid? excludeDocumentId = null, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByConversationIndexAsync(string conversationIndexPrefix, Guid? excludeDocumentId = null, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);

        private const string NotModelled =
            "ShareLinkTestFixture models only the share-link route's read. Model a new member "
            + "deliberately rather than returning an empty default.";
    }

    private sealed class RecordingShareLinkSpeFileStore : SpeFileStore
    {
        private readonly ConcurrentBag<MintedLink> _minted;

        public RecordingShareLinkSpeFileStore(IServiceProvider sp, ConcurrentBag<MintedLink> minted)
            : base(sp.GetRequiredService<ContainerOperations>(),
                   sp.GetRequiredService<DriveItemOperations>(),
                   sp.GetRequiredService<UploadSessionManager>(),
                   sp.GetRequiredService<UserOperations>())
        {
            _minted = minted;
        }

        public override Task<string?> CreateSharingLinkAsUserAsync(
            HttpContext ctx,
            string driveId,
            string itemId,
            string linkType,
            string scope,
            DateTimeOffset? expiration = null,
            CancellationToken ct = default)
        {
            _minted.Add(new MintedLink(scope, expiration));
            return Task.FromResult<string?>($"https://example.invalid/share/{itemId}?scope={scope}");
        }
    }
}

/// <summary>Same host with <c>Documents:ShareLinks:AnonymousLinksEnabled=false</c>.</summary>
public sealed class AnonymousDisabledShareLinkTestFixture : ShareLinkTestFixture
{
    protected override bool AnonymousLinksEnabled => false;
}
