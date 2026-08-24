using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using System.Net.Http.Headers;
using Azure.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Tests.Integration.Workspace;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Test host for the two document DESTROY routes gated by task 022 (findings C2 and C3), with the
/// access data source substituted so the caller's rights can be STATED, and both destroy paths
/// substituted so a test can assert the destroy did not happen.
/// </summary>
/// <remarks>
/// <para><b>Why the rights must be substitutable.</b> Offline, <c>DataverseAccessDataSource</c> fails
/// closed, so every caller is denied before AND after the gate exists — the vacuity trap this project
/// has hit repeatedly. Rights ride on the bearer token (<c>Bearer rights=ReadAccess,DeleteAccess</c>),
/// the same convention as <see cref="OfficeSaveTestFixture"/> and <c>DelegationRuleTestFixture</c>, and
/// for the same reason: the fixture stays immutable across a test class while the double remains a
/// function of the credential, as the production type is.</para>
///
/// <para><b>Why the destroy paths must be substitutable.</b> Task 009's lesson, applied here: a 403
/// assertion alone would pass even if the destroy had already been issued. The load-bearing assertions
/// in this class are <see cref="DeletedDocumentIds"/> and <see cref="DeletedDataverseDocumentIds"/> —
/// the status code is corroboration. Both destroy paths are also genuinely destructive (C2 removes the
/// SPE file as well as the row), so an unsubstituted "allowed" case could not be written at all.</para>
///
/// <para><b>Not sealed</b> — same reason as <c>ExternalCollaborationTestFixture</c>: a later task
/// covering the H2/H3 mutate routes on these same groups will want to extend this rather than fork it.</para>
/// </remarks>
public class DocumentDestroyAuthorizationTestFixture : WorkspaceTestFixture
{
    /// <summary>Every document id that reached <c>DocumentCheckoutService.DeleteAsync</c> (C2's path).</summary>
    public ConcurrentBag<Guid> DeletedDocumentIds { get; } = new();

    /// <summary>Every document id that reached <c>IDocumentDataverseService.DeleteDocumentAsync</c> (C3's path).</summary>
    public ConcurrentBag<string> DeletedDataverseDocumentIds { get; } = new();

    /// <summary>Every document id that reached <c>IDocumentDataverseService.UpdateDocumentAsync</c> (H2's PUT).</summary>
    public ConcurrentBag<string> UpdatedDataverseDocumentIds { get; } = new();

    /// <summary>Every SPE item id whose BYTES were fetched (C1's bulk-download path).</summary>
    public ConcurrentBag<string> DownloadedItemIds { get; } = new();

    /// <summary>
    /// A document id for which the access check THROWS. ADR-003 requires an errored check to deny;
    /// this is the only way a test can reach that catch block.
    /// </summary>
    public const string ThrowingDocumentId = "dead0000-0000-0000-0000-00000000dead";

    /// <summary>
    /// Clears both recorders. A shared <c>IClassFixture</c> gives ONE instance per class, so a
    /// <c>ConcurrentBag</c> accumulates across every test in it — a "destroyed nothing" assertion
    /// would then fail on another test's residue, or worse, pass on it. Call from the test-class
    /// constructor (the trap recorded in <c>ProvisionProjectTestFixture</c>).
    /// </summary>
    public void Reset()
    {
        DeletedDocumentIds.Clear();
        DeletedDataverseDocumentIds.Clear();
        UpdatedDataverseDocumentIds.Clear();
        DownloadedItemIds.Clear();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            // A tenant-bearing auth scheme. WorkspaceTestFixture's FakeAuthHandler emits oid,
            // NameIdentifier, name and roles but NO "tid" — so every route behind a tenant check
            // (BulkDownloadAuthorizationFilter, SemanticSearchAuthorizationFilter) answers 401 under
            // that fixture regardless of the code under test. That is very likely WHY bulk-download
            // had zero test coverage before task 022: it was unreachable from the shared fixture.
            //
            // Overriding the scheme here rather than adding "tid" to the shared FakeAuthHandler is
            // deliberate: that handler backs a large number of tests, and widening the claims it
            // issues could quietly make a route reachable in some other suite that was asserting the
            // 401. Zero blast radius beats one fewer class.
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TenantBearingFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TenantBearingFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TenantBearingFakeAuthHandler>(
                TenantBearingFakeAuthHandler.SchemeName, _ => { });

            // Rights stated by the caller's token. Registered SCOPED to mirror production — the
            // task-008 constraint on task 032 records why that matters for the real type
            // (DataverseAccessDataSource mutates DefaultRequestHeaders, so a singleton would bleed
            // one caller's OBO token into another's request).
            services.RemoveAll<IAccessDataSource>();
            services.AddScoped<IAccessDataSource, TokenStatedAccessDataSource>();

            // C2's destroy path.
            services.RemoveAll<DocumentCheckoutService>();
            services.AddSingleton<DocumentCheckoutService>(sp => new RecordingCheckoutService(
                new HttpClient(),
                sp.GetRequiredService<SpeFileStore>(),
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<TokenCredential>(),
                NullLogger<DocumentCheckoutService>.Instance,
                DeletedDocumentIds));

            // C3's destroy path.
            services.RemoveAll<IDocumentDataverseService>();
            services.AddSingleton<IDocumentDataverseService>(
                new RecordingDocumentDataverseService(
                    DeletedDataverseDocumentIds, UpdatedDataverseDocumentIds));

            // C1's byte source. Substituted so a bulk-download test can assert WHICH documents' bytes
            // reached the zip — the only assertion that actually distinguishes "excluded by
            // authorization" from "included but the download happened to fail offline".
            services.RemoveAll<SpeFileStore>();
            services.AddSingleton<SpeFileStore>(sp => new StubSpeFileStore(sp, DownloadedItemIds));
        });
    }

    /// <summary>
    /// An authenticated caller holding exactly <paramref name="dataverseRights"/> on every document,
    /// stated in Dataverse's own wire vocabulary (e.g. <c>"ReadAccess,DeleteAccess"</c>).
    /// </summary>
    public HttpClient CreateClientWithRights(string dataverseRights)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", $"rights={dataverseRights}");
        return client;
    }

    /// <summary>
    /// Returns deterministic bytes for any SPE pointer and records which item ids were fetched, so a
    /// bulk-download test can assert which documents' bytes actually entered the archive.
    /// </summary>
    private sealed class StubSpeFileStore : SpeFileStore
    {
        private readonly ConcurrentBag<string> _downloaded;

        public StubSpeFileStore(IServiceProvider sp, ConcurrentBag<string> downloaded)
            : base(sp.GetRequiredService<ContainerOperations>(),
                   sp.GetRequiredService<DriveItemOperations>(),
                   sp.GetRequiredService<UploadSessionManager>(),
                   sp.GetRequiredService<UserOperations>())
        {
            _downloaded = downloaded;
        }

        public override Task<Stream?> DownloadFileAsync(
            string driveId, string itemId, CancellationToken ct = default)
        {
            _downloaded.Add(itemId);
            return Task.FromResult<Stream?>(
                new MemoryStream(Encoding.UTF8.GetBytes($"bytes-for-{itemId}")));
        }
    }

    /// <summary>
    /// Same identity as <c>WorkspaceTestFixture</c>'s handler, plus the <c>tid</c> claim that
    /// tenant-checking filters require. No Authorization header still fails authentication, so the
    /// 401 cases stay testable.
    /// </summary>
    internal sealed class TenantBearingFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TenantBearingFakeAuth";
        public const string TenantId = "00000000-0000-0000-0000-0000000000t1";

        public TenantBearingFakeAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization"))
                return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));

            var authHeader = Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(authHeader))
                return Task.FromResult(AuthenticateResult.Fail("Empty Authorization header"));

            var claims = new[]
            {
                new Claim("oid", WorkspaceTestConstants.TestUserId),
                new Claim(ClaimTypes.NameIdentifier, WorkspaceTestConstants.TestUserId),
                new Claim(ClaimTypes.Name, "Test User"),
                new Claim("name", "Test User"),
                new Claim("roles", "SystemAdmin"),
                new Claim("tid", TenantId),
            };

            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    /// <summary>
    /// Reads the rights off the caller's bearer token via the SAME mapper production uses
    /// (<see cref="DataverseAccessRightsMapper"/>), so a transposition in that mapper cannot be
    /// masked by a hand-rolled test parser.
    /// </summary>
    private sealed class TokenStatedAccessDataSource : IAccessDataSource
    {
        public Task<AccessSnapshot> GetUserAccessAsync(
            string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default)
        {
            // An errored access check must DENY, never pass through (ADR-003). Without a document
            // that can actually make the check throw, the fail-closed catch in
            // BulkDownloadAuthorizationFilter is unreachable from any test — and a perturbation that
            // turns it into fail-OPEN then breaks nothing. This sentinel is what makes that
            // perturbation bite.
            if (string.Equals(resourceId, ThrowingDocumentId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Simulated access-check failure for the fail-closed test.");
            }

            var rights = userAccessToken?.StartsWith("rights=", StringComparison.Ordinal) == true
                ? userAccessToken["rights=".Length..]
                : null;

            return Task.FromResult(new AccessSnapshot
            {
                UserId = userId,
                ResourceId = resourceId,
                AccessRights = DataverseAccessRightsMapper.FromAccessRightsString(rights)
            });
        }
    }

    private sealed class RecordingCheckoutService : DocumentCheckoutService
    {
        private readonly ConcurrentBag<Guid> _deleted;

        public RecordingCheckoutService(
            HttpClient httpClient,
            SpeFileStore speFileStore,
            IConfiguration configuration,
            TokenCredential credential,
            ILogger<DocumentCheckoutService> logger,
            ConcurrentBag<Guid> deleted)
            : base(httpClient, speFileStore, configuration, credential, logger)
        {
            _deleted = deleted;
        }

        public override Task<DeleteResult> DeleteAsync(
            Guid documentId, string correlationId, CancellationToken ct = default)
        {
            _deleted.Add(documentId);

            return Task.FromResult(DeleteResult.Success(
                new DeleteDocumentResponse(
                    Success: true,
                    Message: $"Document {documentId} deleted",
                    CorrelationId: correlationId),
                correlationId));
        }
    }

    /// <summary>
    /// Records deletes and answers <c>GetDocumentAsync</c> with a real row, so the C3 handler reaches
    /// its delete rather than short-circuiting on 404 — a 404 would make the allowed case indistinguishable
    /// from a denial.
    /// </summary>
    private sealed class RecordingDocumentDataverseService : IDocumentDataverseService
    {
        private readonly ConcurrentBag<string> _deleted;
        private readonly ConcurrentBag<string> _updated;

        public RecordingDocumentDataverseService(
            ConcurrentBag<string> deleted, ConcurrentBag<string> updated)
        {
            _deleted = deleted;
            _updated = updated;
        }

        public Task DeleteDocumentAsync(string id, CancellationToken ct = default)
        {
            _deleted.Add(id);
            return Task.CompletedTask;
        }

        public Task UpdateDocumentAsync(string id, UpdateDocumentRequest request, CancellationToken ct = default)
        {
            _updated.Add(id);
            return Task.CompletedTask;
        }

        public Task<DocumentEntity?> GetDocumentAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<DocumentEntity?>(new DocumentEntity
            {
                Id = id,
                Name = "Test Document",
                FileName = $"{id}.pdf",
                ContainerId = Guid.NewGuid().ToString(),
                // SPE pointers are REQUIRED for bulk download to resolve a document at all — without
                // them the handler records "no file attached" and the request degrades to a total
                // failure, which looks exactly like a denial. The filename is keyed on the id so a
                // test can tell WHICH documents made it into the zip.
                GraphDriveId = $"drive-{id}",
                GraphItemId = $"item-{id}"
            });

        // Nothing else on this interface is exercised by the destroy routes. These throw rather than
        // returning defaults so a future test that strays onto an unmodelled path fails loudly instead
        // of quietly asserting against a fabricated empty answer.
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
            "DocumentDestroyAuthorizationTestFixture models only the destroy routes' reads and writes. " +
            "If a test needs this member, model it deliberately rather than returning an empty default.";
    }
}
