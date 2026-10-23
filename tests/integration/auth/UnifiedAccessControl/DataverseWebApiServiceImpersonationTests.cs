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

    // ── ebilling-r1 #2081 round 3: the update-only write (UpdateExistingRecordFieldsAsync, impersonation overload) ─
    //
    // The invoice-family guard stops a generic write from CREATING a row (a PATCH without If-Match is an upsert) and
    // keeps the write running as the caller. Every other test of that path mocks the service interface, so these pin
    // what actually goes on the wire: dropping the If-Match line or the impersonation argument must fail here.

    private static readonly Dictionary<string, object?> RenamedField = new() { ["sprk_name"] = "renamed" };

    private static SentRequest PatchOf(RecordingHandler handler) =>
        handler.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Patch).Subject;

    /// <summary>1. PATCH, aimed at the entity set and the row id (not a POST, not a collection URL).</summary>
    [Fact]
    public async Task UpdateExistingRecordFieldsAsync_SendsAPatchToTheEntitySetAndId()
    {
        var handler = new RecordingHandler(PatchOrEntitySetName);
        var sut = new OfflineService(handler);
        var recordId = Guid.NewGuid();

        await sut.UpdateExistingRecordFieldsAsync("sprk_matter", recordId, RenamedField, CallerSystemUserId);

        handler.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
        var patch = PatchOf(handler);
        patch.Target!.ToString().Should().EndWith($"sprk_matters({recordId})");
    }

    /// <summary>2. The update-only header: without it the Web API creates the row when it is missing.</summary>
    [Fact]
    public async Task UpdateExistingRecordFieldsAsync_SendsIfMatchStar()
    {
        var handler = new RecordingHandler(PatchOrEntitySetName);
        var sut = new OfflineService(handler);

        await sut.UpdateExistingRecordFieldsAsync("sprk_matter", Guid.NewGuid(), RenamedField, CallerSystemUserId);

        PatchOf(handler).Values("If-Match").Should().ContainSingle().Which.Should().Be("*");
    }

    /// <summary>3a. The write runs as the caller: exactly one MSCRMCallerID, equal to the id.</summary>
    [Fact]
    public async Task UpdateExistingRecordFieldsAsync_WithCallerId_ImpersonatesTheWrite()
    {
        var handler = new RecordingHandler(PatchOrEntitySetName);
        var sut = new OfflineService(handler);

        await sut.UpdateExistingRecordFieldsAsync("sprk_matter", Guid.NewGuid(), RenamedField, CallerSystemUserId);

        var patch = PatchOf(handler);
        patch.Values("MSCRMCallerID").Should().ContainSingle().Which.Should().Be(CallerSystemUserId.ToString());
        patch.Headers.Keys.Should().NotContain("CallerObjectId");
        handler.Requests.Where(r => r.Method != HttpMethod.Patch)
            .Should().OnlyContain(r => !r.Headers.ContainsKey("MSCRMCallerID"), "the metadata lookup stays app-only");
    }

    /// <summary>3b. The app-only call (null caller): no header names a user, and the write is still update-only.</summary>
    [Fact]
    public async Task UpdateExistingRecordFieldsAsync_WithNullCaller_IsAppOnlyButStillIfMatchStar()
    {
        var handler = new RecordingHandler(PatchOrEntitySetName);
        var sut = new OfflineService(handler);

        await sut.UpdateExistingRecordFieldsAsync("sprk_matter", Guid.NewGuid(), RenamedField, impersonateSystemUserId: null);

        var patch = PatchOf(handler);
        patch.Headers.Keys.Should().NotContain("MSCRMCallerID").And.NotContain("CallerObjectId");
        patch.Values("If-Match").Should().ContainSingle().Which.Should().Be("*");
    }

    /// <summary>The no-caller convenience overload is the same app-only update-only write.</summary>
    [Fact]
    public async Task UpdateExistingRecordFieldsAsync_WithoutACallerOverload_IsAppOnlyAndIfMatchStar()
    {
        var handler = new RecordingHandler(PatchOrEntitySetName);
        var sut = new OfflineService(handler);

        await sut.UpdateExistingRecordFieldsAsync("sprk_matter", Guid.NewGuid(), RenamedField);

        var patch = PatchOf(handler);
        patch.Headers.Keys.Should().NotContain("MSCRMCallerID");
        patch.Values("If-Match").Should().ContainSingle().Which.Should().Be("*");
    }

    /// <summary>
    /// 4. An empty caller id is refused before the write is sent. Only the app-only metadata lookup runs first: the
    /// builder is reached after it, so the assertion is "no PATCH, and no request names a user".
    /// </summary>
    [Fact]
    public async Task UpdateExistingRecordFieldsAsync_WithEmptyCallerId_ThrowsAndSendsNoWrite()
    {
        var handler = new RecordingHandler(PatchOrEntitySetName);
        var sut = new OfflineService(handler);

        var act = () => sut.UpdateExistingRecordFieldsAsync("sprk_matter", Guid.NewGuid(), RenamedField, Guid.Empty);

        (await act.Should().ThrowAsync<ArgumentException>().WithMessage("*fail closed*"))
            .Which.ParamName.Should().Be("systemUserId");
        handler.Requests.Should().NotContain(r => r.Method == HttpMethod.Patch);
        handler.Requests.Should().OnlyContain(r => !r.Headers.ContainsKey("MSCRMCallerID"));
    }

    /// <summary>5. A missing row (404 under If-Match: *) is KeyNotFound, not a generic HTTP failure.</summary>
    [Fact]
    public async Task UpdateExistingRecordFieldsAsync_On404_ThrowsKeyNotFound()
    {
        var handler = new RecordingHandler(r => r.Method == HttpMethod.Patch
            ? new HttpResponseMessage(HttpStatusCode.NotFound) : EntitySetNameResponse(r));
        var sut = new OfflineService(handler);

        var act = () => sut.UpdateExistingRecordFieldsAsync("sprk_matter", Guid.NewGuid(), RenamedField, CallerSystemUserId);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    /// <summary>6. A precondition failure (412) is treated the same: never a silent success or a retry that creates.</summary>
    [Fact]
    public async Task UpdateExistingRecordFieldsAsync_On412_ThrowsKeyNotFound()
    {
        var handler = new RecordingHandler(r => r.Method == HttpMethod.Patch
            ? new HttpResponseMessage(HttpStatusCode.PreconditionFailed) : EntitySetNameResponse(r));
        var sut = new OfflineService(handler);

        var act = () => sut.UpdateExistingRecordFieldsAsync("sprk_matter", Guid.NewGuid(), RenamedField, CallerSystemUserId);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    /// <summary>7. A 204 succeeds.</summary>
    [Fact]
    public async Task UpdateExistingRecordFieldsAsync_On204_Succeeds()
    {
        var handler = new RecordingHandler(PatchOrEntitySetName);
        var sut = new OfflineService(handler);

        var act = () => sut.UpdateExistingRecordFieldsAsync("sprk_matter", Guid.NewGuid(), RenamedField, CallerSystemUserId);

        await act.Should().NotThrowAsync();
    }

    /// <summary>Anything but 404/412 is still a failure the caller sees (a 400 or 403 is not "not found").</summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task UpdateExistingRecordFieldsAsync_OnOtherFailures_ThrowsHttpRequestException(HttpStatusCode status)
    {
        var handler = new RecordingHandler(r => r.Method == HttpMethod.Patch
            ? new HttpResponseMessage(status) : EntitySetNameResponse(r));
        var sut = new OfflineService(handler);

        var act = () => sut.UpdateExistingRecordFieldsAsync("sprk_matter", Guid.NewGuid(), RenamedField, CallerSystemUserId);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── unified-access-control-r2 task 159 (#1098): the trimmed GET /api/v1/events ──────────────────────────────

    /// <summary>The caller-scoped event query runs AS the caller: exactly one MSCRMCallerID, and it still counts.</summary>
    [Fact]
    public async Task QueryEventsAsCallerAsync_SendsExactlyOneMscrmCallerIdHeader_AndCountsTheTrimmedSet()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":[],\"@odata.count\":0}"));
        var sut = new OfflineService(handler);

        await sut.QueryEventsAsCallerAsync(CallerSystemUserId, mine: new EventOwnershipScope(CallerSystemUserId, null, null));

        var sent = handler.Requests.Should().ContainSingle().Subject;
        sent.Values("MSCRMCallerID").Should().ContainSingle().Which.Should().Be(CallerSystemUserId.ToString());
        sent.Headers.Keys.Should().NotContain("CallerObjectId");
        Uri.UnescapeDataString(sent.Target!.ToString()).Should().Contain("$count=true");
    }

    /// <summary>The background (TodoGenerationService) event query stays app-only: no header names a user.</summary>
    [Fact]
    public async Task QueryEventsAsync_AppOnly_SendsNoImpersonationHeader()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":[],\"@odata.count\":0}"));
        var sut = new OfflineService(handler);

        await sut.QueryEventsAsync(top: 100);

        var sent = handler.Requests.Should().ContainSingle().Subject;
        sent.Headers.Keys.Should().NotContain("MSCRMCallerID").And.NotContain("CallerObjectId");
    }

    /// <summary>An empty caller is refused before the URL is built or anything is sent — never an app-only list.</summary>
    [Fact]
    public async Task QueryEventsAsCallerAsync_WithEmptyCallerId_ThrowsAndSendsNothing()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":[],\"@odata.count\":0}"));
        var sut = new OfflineService(handler);

        var act = () => sut.QueryEventsAsCallerAsync(Guid.Empty);

        (await act.Should().ThrowAsync<ArgumentException>().WithMessage("*fail closed*"))
            .Which.ParamName.Should().Be("callerSystemUserId");
        handler.Requests.Should().BeEmpty();
    }

    // ── unified-access-control-r2 (live on dev 2026-10-06): the record owner's own share ───────────────────────────
    //
    // Dataverse refuses an app-only RevokeAccess of the share held by the record's CURRENT owning user (400 0x80040223
    // "Only owner can revoke access to the owner") and accepts it sent as that user. The owner-aware revoke stamps
    // MSCRMCallerID for exactly that case; every other revoke is the app-only request it always was.

    private static readonly Guid OtherUserId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    /// <summary>The owner's own share is revoked AS the owner: one MSCRMCallerID, naming the revokee, and no CallerObjectId.</summary>
    [Fact]
    public async Task RevokeAccessAsync_OfTheOwningUsersOwnShare_RunsAsThatUser()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = new OfflineService(handler);
        var owner = DataversePrincipalRef.User(CallerSystemUserId);

        await sut.RevokeAccessAsync("sprk_matters", Guid.NewGuid(), owner, recordOwner: owner);

        var sent = handler.Requests.Should().ContainSingle().Subject;
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Target!.ToString().Should().EndWith("RevokeAccess");
        sent.Values("MSCRMCallerID").Should().ContainSingle().Which.Should().Be(CallerSystemUserId.ToString());
        sent.Headers.Keys.Should().NotContain("CallerObjectId");
    }

    /// <summary>
    /// Every other principal / owner pairing stays app-only — exactly the headers an app-only request carries. A user who
    /// does not own the record, a team that owns it (Dataverse's rule is about the owning USER), a user principal whose id
    /// equals the owning TEAM's, and a team principal whose id equals the owning user's: none is ever impersonated.
    /// </summary>
    [Theory]
    [InlineData("other-user-on-user-owned")]
    [InlineData("team-on-own-team")]
    [InlineData("user-id-of-owning-team")]
    [InlineData("team-id-of-owning-user")]
    public async Task RevokeAccessAsync_OfAnyShareButTheOwningUsersOwn_IsAppOnly(string pairing)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = new OfflineService(handler);
        var (principal, owner) = pairing switch
        {
            "other-user-on-user-owned" => (DataversePrincipalRef.User(OtherUserId), DataversePrincipalRef.User(CallerSystemUserId)),
            "team-on-own-team" => (DataversePrincipalRef.Team(OtherUserId), DataversePrincipalRef.Team(OtherUserId)),
            "user-id-of-owning-team" => (DataversePrincipalRef.User(OtherUserId), DataversePrincipalRef.Team(OtherUserId)),
            "team-id-of-owning-user" => (DataversePrincipalRef.Team(OtherUserId), DataversePrincipalRef.User(OtherUserId)),
            _ => throw new ArgumentOutOfRangeException(nameof(pairing), pairing, null),
        };

        await sut.RevokeAccessAsync("sprk_matters", Guid.NewGuid(), principal, recordOwner: owner);

        var sent = handler.Requests.Should().ContainSingle().Subject;
        sent.Headers.Keys.Should().BeEquivalentTo(AppOnlyHeaders, "only the owning user's own share is sent as a user");
    }

    /// <summary>The revoke that names no owner is unchanged: app-only even for the owner's share (Dataverse then refuses it).</summary>
    [Fact]
    public async Task RevokeAccessAsync_WithoutAnOwner_IsAppOnly()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = new OfflineService(handler);

        await sut.RevokeAccessAsync("sprk_matters", Guid.NewGuid(), DataversePrincipalRef.User(CallerSystemUserId));

        var sent = handler.Requests.Should().ContainSingle().Subject;
        sent.Headers.Keys.Should().BeEquivalentTo(AppOnlyHeaders);
    }

    /// <summary>
    /// The owner-aware revoke is still a share write that fails loud: Dataverse's refusal (here, as if the impersonated
    /// owner were refused) surfaces as an exception, so a caller's read-back / completeness report never counts it.
    /// </summary>
    [Fact]
    public async Task RevokeAccessAsync_AsTheOwner_WhenDataverseRefuses_Throws()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var sut = new OfflineService(handler);
        var owner = DataversePrincipalRef.User(CallerSystemUserId);

        var act = () => sut.RevokeAccessAsync("sprk_matters", Guid.NewGuid(), owner, recordOwner: owner);

        await act.Should().ThrowAsync<HttpRequestException>();
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
        new Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator(
            NullLogger<Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator>.Instance),
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
