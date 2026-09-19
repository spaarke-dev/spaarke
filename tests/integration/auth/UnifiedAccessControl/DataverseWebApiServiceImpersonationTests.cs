using System.Net;
using System.Net.Http;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// What <see cref="DataverseWebApiService"/> actually SENDS on its app-only and impersonated paths
/// (unified-access-control-r2 task 104, #990).
/// </summary>
/// <remarks>
/// <para><see cref="DataverseImpersonationHelperTests"/> pins the helper's header contract. These tests pin that
/// the production request builder uses it correctly:
/// <list type="bullet">
///   <item>an app-only request carries exactly the headers it always did, and none that names a user;</item>
///   <item>an impersonated one carries exactly one impersonation header;</item>
///   <item>an empty caller id is refused by the request builder itself, before the write is sent. That builder
///     is the one place every impersonated request passes through, so it is tested directly rather than behind a
///     method-level guard that would mask it.</item>
/// </list></para>
/// <para>A recording handler captures each request. It is a hand-written test double, not
/// <c>Mock&lt;HttpMessageHandler&gt;</c>, and it asserts on what was sent rather than on how; the headers are the
/// contract under test. A static credential stands in for the network token, supplied through the service's
/// protected test constructor (which dependency injection cannot reach).</para>
/// </remarks>
public class DataverseWebApiServiceImpersonationTests
{
    private static readonly Guid CallerSystemUserId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly string[] AppOnlyHeaders = { "Authorization", "Accept", "OData-MaxVersion", "OData-Version" };

    /// <summary>
    /// The regression guard for every existing app-only read: exactly the headers it sent before task 104
    /// (token + OData defaults), nothing that names a user.
    /// </summary>
    [Fact]
    public async Task GetEntitySetNameAsync_AppOnly_SendsExactlyTheTokenAndODataHeaders()
    {
        var handler = new RecordingHandler(EntitySetNameResponse);
        var sut = new OfflineService(handler);

        await sut.GetEntitySetNameAsync("sprk_matter");

        var sent = handler.Requests.Should().ContainSingle().Subject;
        sent.Method.Should().Be(HttpMethod.Get);
        sent.Headers.Keys.Should().BeEquivalentTo(AppOnlyHeaders);
        sent.Values("Authorization").Should().ContainSingle().Which.Should().Be("Bearer test-token");
    }

    /// <summary>The same guard for an app-only write: a null caller id leaves the PATCH app-only and unchanged.</summary>
    [Fact]
    public async Task UpdateRecordFieldsAsync_AppOnly_PatchSendsExactlyTheTokenAndODataHeaders()
    {
        var handler = new RecordingHandler(PatchOrEntitySetName);
        var sut = new OfflineService(handler);

        await sut.UpdateRecordFieldsAsync(
            "sprk_matter", Guid.NewGuid(), new Dictionary<string, object?> { ["sprk_name"] = "renamed" },
            CancellationToken.None, impersonateSystemUserId: null);

        var patch = handler.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Patch).Subject;
        patch.Headers.Keys.Should().BeEquivalentTo(AppOnlyHeaders);
    }

    [Fact]
    public async Task RetrieveMultipleImpersonatedAsync_WithCallerId_SendsExactlyOneMscrmCallerIdHeader()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":[]}"));
        var sut = new OfflineService(handler);

        await sut.RetrieveMultipleImpersonatedAsync("sprk_matters", "$select=sprk_matterid", CallerSystemUserId);

        var sent = handler.Requests.Should().ContainSingle().Subject;
        sent.Values("MSCRMCallerID").Should().ContainSingle().Which.Should().Be(CallerSystemUserId.ToString());
        sent.Headers.Keys.Should().NotContain("CallerObjectId");
    }

    /// <summary>
    /// The Job B apply path (task 031): the PATCH runs as the confirming user. The EntitySetName lookup before it
    /// is environment metadata and stays app-only.
    /// </summary>
    [Fact]
    public async Task UpdateRecordFieldsAsync_WithCallerId_ImpersonatesOnlyThePatch()
    {
        var handler = new RecordingHandler(PatchOrEntitySetName);
        var sut = new OfflineService(handler);

        await sut.UpdateRecordFieldsAsync(
            "sprk_matter", Guid.NewGuid(), new Dictionary<string, object?> { ["sprk_name"] = "renamed" },
            CancellationToken.None, CallerSystemUserId);

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].Headers.Keys.Should().NotContain("MSCRMCallerID");
        handler.Requests[1].Method.Should().Be(HttpMethod.Patch);
        handler.Requests[1].Values("MSCRMCallerID")
            .Should().ContainSingle().Which.Should().Be(CallerSystemUserId.ToString());
    }

    /// <summary>
    /// Before task 104 an empty id sent this PATCH app-only: the write succeeded with the application's rights,
    /// not the caller's. The request builder now refuses it (the helper's fail-closed error), so the write is
    /// never sent. Only the app-only metadata lookup, which names no user, may precede the refusal.
    /// </summary>
    [Fact]
    public async Task UpdateRecordFieldsAsync_WithEmptyCallerId_RequestBuilderRefusesAndNoWriteIsSent()
    {
        var handler = new RecordingHandler(PatchOrEntitySetName);
        var sut = new OfflineService(handler);

        var act = () => sut.UpdateRecordFieldsAsync(
            "sprk_matter", Guid.NewGuid(), new Dictionary<string, object?> { ["sprk_name"] = "renamed" },
            CancellationToken.None, Guid.Empty);

        (await act.Should().ThrowAsync<ArgumentException>().WithMessage("*fail closed*"))
            .Which.ParamName.Should().Be("systemUserId");
        handler.Requests.Should().NotContain(r => r.Method == HttpMethod.Patch);
        handler.Requests.Should().OnlyContain(r => !r.Headers.ContainsKey("MSCRMCallerID"));
    }

    private static HttpResponseMessage PatchOrEntitySetName(HttpRequestMessage request) =>
        request.Method == HttpMethod.Patch
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : EntitySetNameResponse(request);

    private static HttpResponseMessage EntitySetNameResponse(HttpRequestMessage _) =>
        Json("{\"EntitySetName\":\"sprk_matters\"}");

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>The production service, built through its protected test constructor with a static token.</summary>
    private sealed class OfflineService(RecordingHandler handler) : DataverseWebApiService(
        new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
        }).Build(),
        NullLogger<DataverseWebApiService>.Instance,
        confidentialClients: null,
        credential: new StaticTokenCredential());

    /// <summary>A request as it left the client: method, target and every header, copied before disposal.</summary>
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
