using System.Collections.Concurrent;
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
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
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
    /// Reads the rights off the caller's bearer token via the SAME mapper production uses
    /// (<see cref="DataverseAccessRightsMapper"/>), so a transposition in that mapper cannot be
    /// masked by a hand-rolled test parser.
    /// </summary>
    private sealed class TokenStatedAccessDataSource : IAccessDataSource
    {
        public Task<AccessSnapshot> GetUserAccessAsync(
            string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default)
        {
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
                FileName = "test.pdf",
                ContainerId = Guid.NewGuid().ToString()
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
