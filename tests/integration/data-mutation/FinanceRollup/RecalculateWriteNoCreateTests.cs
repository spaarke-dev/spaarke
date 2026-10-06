using System.Net;
using System.Net.Http;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceRollup;

/// <summary>
/// The recalculate write paths must never CREATE a matter or project (unified-access-control-r2 task 130).
/// </summary>
/// <remarks>
/// <para>A Dataverse Web API PATCH without <c>If-Match</c> is an UPSERT: an id that does not exist is created.
/// <c>FinanceRollupService</c> and <c>ScorecardCalculatorService</c> write derived fields onto a parent the
/// caller was authorized against moments earlier; if that parent was deleted in between, the old write would
/// have recreated it as an empty row carrying only rollup fields. These tests pin what
/// <see cref="DataverseWebApiService.UpdateExistingRecordFieldsAsync"/> SENDS — the <c>If-Match: *</c>
/// precondition — and that a refused precondition surfaces as <see cref="KeyNotFoundException"/> (which the
/// recalculate endpoints render as their uniform 404), and that <see cref="DataverseWebApiService.UpdateRecordFieldsAsync"/>
/// is unchanged for every other caller (<c>InvoiceReviewService</c> creates its invoice through that upsert).</para>
/// <para>A recording handler captures each request: a hand-written test double, not <c>Mock&lt;HttpMessageHandler&gt;</c>
/// (ADR-038), asserting on what was sent.</para>
/// </remarks>
public class RecalculateWriteNoCreateTests
{
    [Fact]
    public async Task UpdateExistingRecordFieldsAsync_SendsIfMatchStar_SoThePatchCannotCreate()
    {
        var handler = new RecordingHandler(PatchReturns(HttpStatusCode.NoContent));
        var sut = new OfflineService(handler);
        var id = Guid.NewGuid();

        await sut.UpdateExistingRecordFieldsAsync(
            "sprk_matter", id, new Dictionary<string, object?> { ["sprk_totalspendtodate"] = 10m });

        var patch = handler.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Patch).Subject;
        patch.Target!.ToString().Should().EndWith($"sprk_matters({id})");
        patch.Values("If-Match").Should().ContainSingle().Which.Should().Be("*");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    public async Task UpdateExistingRecordFieldsAsync_RecordAbsent_ThrowsKeyNotFound(HttpStatusCode refusal)
    {
        var handler = new RecordingHandler(PatchReturns(refusal));
        var sut = new OfflineService(handler);

        var act = () => sut.UpdateExistingRecordFieldsAsync(
            "sprk_project", Guid.NewGuid(), new Dictionary<string, object?> { ["sprk_invoicecount"] = 1 });

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateRecordFieldsAsync_IsUnchanged_StillSendsNoIfMatch()
    {
        // InvoiceReviewService creates its sprk_invoice by PATCHing a fresh GUID, which only works because this
        // method upserts. The no-create guarantee is scoped to the two recalculate writes, not applied globally.
        var handler = new RecordingHandler(PatchReturns(HttpStatusCode.NoContent));
        var sut = new OfflineService(handler);

        await sut.UpdateRecordFieldsAsync(
            "sprk_invoice", Guid.NewGuid(), new Dictionary<string, object?> { ["sprk_name"] = "x" });

        handler.Requests.Single(r => r.Method == HttpMethod.Patch).Values("If-Match").Should().BeEmpty();
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> PatchReturns(HttpStatusCode status) =>
        request => request.Method == HttpMethod.Patch
            ? new HttpResponseMessage(status)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.RequestUri!.ToString().Contains("sprk_project", StringComparison.Ordinal)
                        ? "{\"EntitySetName\":\"sprk_projects\"}"
                        : request.RequestUri!.ToString().Contains("sprk_invoice", StringComparison.Ordinal)
                            ? "{\"EntitySetName\":\"sprk_invoices\"}"
                            : "{\"EntitySetName\":\"sprk_matters\"}",
                    Encoding.UTF8, "application/json"),
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
