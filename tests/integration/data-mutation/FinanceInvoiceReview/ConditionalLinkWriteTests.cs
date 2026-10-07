using System.Data;
using System.Net;
using System.Net.Http;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceInvoiceReview;

/// <summary>
/// The conditional write behind invoice confirm's document link (unified-access-control-r2 task 130 fix round,
/// item 1): <see cref="DataverseWebApiService.UpdateRecordFieldsIfUnchangedAsync"/> must send the document's version
/// as a weak ETag, and must turn a refused precondition into <see cref="DBConcurrencyException"/> — not into success,
/// and not into "not found" — so a second concurrent confirm cannot overwrite the first one's link.
/// </summary>
/// <remarks>
/// A Dataverse row's ETag is <c>W/"&lt;versionnumber&gt;"</c> (verified read-only on spaarkedev1, 2026-10-01:
/// <c>"@odata.etag":"W/\"10039914\"","versionnumber":10039914</c>). A recording handler captures each request: a
/// hand-written test double, not <c>Mock&lt;HttpMessageHandler&gt;</c> (ADR-038), asserting on what was sent.
/// </remarks>
public class ConditionalLinkWriteTests
{
    [Fact]
    public async Task SendsTheVersionAsAWeakETag()
    {
        var handler = new RecordingHandler(PatchReturns(HttpStatusCode.NoContent));
        var sut = new OfflineService(handler);
        var id = Guid.NewGuid();

        await sut.UpdateRecordFieldsIfUnchangedAsync(
            "sprk_document", id, new Dictionary<string, object?> { ["sprk_Invoice@odata.bind"] = "/sprk_invoices(x)" }, 10039914);

        var patch = handler.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Patch).Subject;
        patch.Target!.ToString().Should().EndWith($"sprk_documents({id})");
        patch.Values("If-Match").Should().ContainSingle().Which.Should().Be("W/\"10039914\"");
    }

    [Fact]
    public async Task ChangedSinceRead_412_ThrowsConcurrency_NotNotFound()
    {
        var sut = new OfflineService(new RecordingHandler(PatchReturns(HttpStatusCode.PreconditionFailed)));

        var act = () => sut.UpdateRecordFieldsIfUnchangedAsync(
            "sprk_document", Guid.NewGuid(), new Dictionary<string, object?> { ["x"] = 1 }, 7);

        await act.Should().ThrowAsync<DBConcurrencyException>();
    }

    [Fact]
    public async Task RecordAbsent_404_ThrowsKeyNotFound()
    {
        var sut = new OfflineService(new RecordingHandler(PatchReturns(HttpStatusCode.NotFound)));

        var act = () => sut.UpdateRecordFieldsIfUnchangedAsync(
            "sprk_document", Guid.NewGuid(), new Dictionary<string, object?> { ["x"] = 1 }, 7);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> PatchReturns(HttpStatusCode status) =>
        request => request.Method == HttpMethod.Patch
            ? new HttpResponseMessage(status)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"EntitySetName\":\"sprk_documents\"}", Encoding.UTF8, "application/json"),
            };

    /// <summary>The production service, built through its protected test constructor with a static token.</summary>
    private sealed class OfflineService(RecordingHandler handler) : DataverseWebApiService(
        new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
        }).Build(),
        NullLogger<DataverseWebApiService>.Instance,
        new Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator(
            NullLogger<Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator>.Instance),
        confidentialClients: null,
        credential: new StaticTokenCredential());

    private sealed record SentRequest(HttpMethod Method, Uri? Target, IReadOnlyDictionary<string, string[]> Headers)
    {
        public IReadOnlyCollection<string> Values(string header) =>
            Headers.TryGetValue(header, out var values) ? values : Array.Empty<string>();
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(
                request.Method,
                request.RequestUri,
                request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase)));
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static readonly AccessToken Token = new("test-token", DateTimeOffset.MaxValue);

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => Token;

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }
}
