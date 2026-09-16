using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// What <see cref="DataverseWebApiService"/> sends and accepts on the POA share surface task 063 builds on: the
/// ModifyAccess payload, and the strict share read that answers completely or throws.
/// </summary>
/// <remarks>
/// <para><b>Why the strict read is pinned here and not only through the endpoints.</b> The endpoints are tested
/// against an in-memory share table (<see cref="FakeRecordShareTable"/>) that throws when told to. These tests pin that
/// the REAL read throws in each case the table stands in for — a refused read, an unreadable table code, a second page,
/// an unreadable row — while the soft read keeps its long-standing empty answer.</para>
/// <para><b>ADR-038 ban B1 — a path-A exception (root CLAUDE.md §6.5), stated rather than argued around.</b> B1 bans
/// <c>Mock&lt;HttpMessageHandler&gt;</c> because a transport-level double encodes the wire format and breaks on
/// refactors. <see cref="ScriptedHandler"/> is hand-written rather than a mock, but it has the banned PROPERTY: it
/// asserts the exact JSON body. The exception is narrow and deliberate on both halves. The POA payload shape IS the
/// contract this task has to get right — a wrong <c>@odata.id</c> or a renamed <c>AccessMask</c> fails at runtime and
/// nothing offline would notice. And the strict read's refusal branches (a non-success status, <c>@odata.nextLink</c>,
/// an unreadable row) are unreachable through <c>WebApplicationFactory</c>, because nothing offline can make
/// Dataverse answer those shapes. Task 104 set the same precedent in this folder
/// (<c>DataverseWebApiServiceImpersonationTests</c>) for the same reason. A static credential stands in for the token
/// through the service's protected test constructor.</para>
/// </remarks>
public class DataverseRecordShareWireTests
{
    private const int MatterObjectTypeCode = 10042;

    private static readonly Guid MatterId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeamId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // ─────────────────────────────────────────────────────────────────────────────
    // Payloads
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ModifyAccessAsync_PostsModifyAccessWithTheTargetAndPrincipalAccessBody()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await new OfflineService(handler).ModifyAccessAsync(
            "sprk_matters", MatterId, DataversePrincipalRef.User(UserId), "ReadAccess");

        var sent = handler.Requests.Should().ContainSingle().Subject;
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Path.Should().EndWith("/ModifyAccess");
        AssertPrincipalAccessBody(sent.Body, $"sprk_matters({MatterId})", $"systemusers({UserId})", "ReadAccess");
    }

    /// <summary>Regression: GrantAccess sends the same body it always did, now built by the shared payload helper.</summary>
    [Fact]
    public async Task GrantAccessAsync_StillPostsGrantAccessWithTheSameBody()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await new OfflineService(handler).GrantAccessAsync(
            "sprk_projects", MatterId, DataversePrincipalRef.Team(TeamId), "ReadAccess,WriteAccess");

        var sent = handler.Requests.Should().ContainSingle().Subject;
        sent.Path.Should().EndWith("/GrantAccess");
        AssertPrincipalAccessBody(sent.Body, $"sprk_projects({MatterId})", $"teams({TeamId})", "ReadAccess,WriteAccess");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The strict read
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPrincipalAccessOrThrowAsync_ReturnsTheUserAndTeamShares_AndSkipsUnmodelledPrincipals()
    {
        var handler = SharesHandler($$"""
            {"value":[
              {"principalid":"{{UserId}}","principaltypecode":8,"accessrightsmask":23,"modifiedon":"2026-09-15T10:00:00Z"},
              {"principalid":"{{TeamId}}","principaltypecode":9,"accessrightsmask":1,"modifiedon":"2026-09-15T11:00:00Z"},
              {"principalid":"{{Guid.NewGuid()}}","principaltypecode":2,"accessrightsmask":1,"modifiedon":"2026-09-15T12:00:00Z"}
            ]}
            """);

        var shares = await new OfflineService(handler).GetPrincipalAccessOrThrowAsync("sprk_matter", MatterId);

        shares.Should().Equal(
            new DataversePrincipalAccess(DataversePrincipalRef.User(UserId), 23, new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero)),
            new DataversePrincipalAccess(DataversePrincipalRef.Team(TeamId), 1, new DateTimeOffset(2026, 9, 15, 11, 0, 0, TimeSpan.Zero)));
        handler.Requests.Last().Url.Should().Contain(
            $"objectid eq {MatterId} and objecttypecode eq {MatterObjectTypeCode}",
            "the read must be scoped to this record of this table");
    }

    /// <summary>The pair that matters: the same refused read throws from the strict read and is empty from the soft one.</summary>
    [Fact]
    public async Task GetPrincipalAccessOrThrowAsync_WhenDataverseRefusesTheRead_Throws_WhileTheSoftReadStillAnswersEmpty()
    {
        var strict = () => new OfflineService(SharesHandler(status: HttpStatusCode.ServiceUnavailable))
            .GetPrincipalAccessOrThrowAsync("sprk_matter", MatterId);

        await strict.Should().ThrowAsync<InvalidOperationException>().WithMessage("*503*");
        (await new OfflineService(SharesHandler(status: HttpStatusCode.ServiceUnavailable))
            .GetPrincipalAccessAsync("sprk_matter", MatterId))
            .Should().BeEmpty("the soft read's contract is unchanged for its existing callers");
    }

    [Fact]
    public async Task GetPrincipalAccessOrThrowAsync_WhenTheTablesObjectTypeCodeCannotBeRead_Throws()
    {
        var strict = () => new OfflineService(SharesHandler(typeCodeStatus: HttpStatusCode.NotFound))
            .GetPrincipalAccessOrThrowAsync("sprk_matter", MatterId);

        await strict.Should().ThrowAsync<InvalidOperationException>().WithMessage("*object type code*");
    }

    /// <summary>A second page could hold the very principal a caller is about to change.</summary>
    [Fact]
    public async Task GetPrincipalAccessOrThrowAsync_WhenTheSharesContinueOnAnotherPage_Throws()
    {
        var handler = SharesHandler($$"""
            {"value":[{"principalid":"{{UserId}}","principaltypecode":8,"accessrightsmask":1,"modifiedon":"2026-09-15T10:00:00Z"}],
             "@odata.nextLink":"https://test.crm.dynamics.com/api/data/v9.2/principalobjectaccessset?$skiptoken=abc"}
            """);

        var strict = () => new OfflineService(handler).GetPrincipalAccessOrThrowAsync("sprk_matter", MatterId);

        await strict.Should().ThrowAsync<InvalidOperationException>().WithMessage("*another page*");
    }

    /// <summary>
    /// An unreadable mask would otherwise read as 0 — "no share" — and send a GrantAccess for a user who holds one.
    /// The soft read keeps its old behaviour (mask 0) for its existing callers.
    /// </summary>
    [Fact]
    public async Task GetPrincipalAccessOrThrowAsync_WhenARowHasNoReadableMask_Throws_WhileTheSoftReadReadsZero()
    {
        var body = $$"""{"value":[{"principalid":"{{UserId}}","principaltypecode":8,"modifiedon":"2026-09-15T10:00:00Z"}]}""";

        var strict = () => new OfflineService(SharesHandler(body)).GetPrincipalAccessOrThrowAsync("sprk_matter", MatterId);

        await strict.Should().ThrowAsync<InvalidOperationException>().WithMessage("*rights mask*");
        (await new OfflineService(SharesHandler(body)).GetPrincipalAccessAsync("sprk_matter", MatterId))
            .Should().ContainSingle().Which.AccessRightsMask.Should().Be(0);
    }

    [Fact]
    public async Task GetPrincipalAccessOrThrowAsync_WhenARowHasNoReadablePrincipal_Throws()
    {
        var body = """{"value":[{"principaltypecode":8,"accessrightsmask":1,"modifiedon":"2026-09-15T10:00:00Z"}]}""";

        var strict = () => new OfflineService(SharesHandler(body)).GetPrincipalAccessOrThrowAsync("sprk_matter", MatterId);

        await strict.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no readable principal*");
    }

    /// <summary>
    /// <c>modifiedon</c> is in the <c>$select</c>, so a value that cannot be read means an anomalous response. The
    /// strict read refuses it for the same reason it refuses an unreadable mask: "incomplete counts as failed" must
    /// not carry an exception that quietly reports a share as changed just now. The soft read keeps its fallback,
    /// because its callers only display the value (Step 9.5 review finding 12).
    /// </summary>
    [Fact]
    public async Task GetPrincipalAccessOrThrowAsync_WhenARowHasNoReadableModifiedOn_Throws_WhileTheSoftReadFallsBack()
    {
        var body = $$"""{"value":[{"principalid":"{{UserId}}","principaltypecode":8,"accessrightsmask":23}]}""";

        var strict = () => new OfflineService(SharesHandler(body)).GetPrincipalAccessOrThrowAsync("sprk_matter", MatterId);

        await strict.Should().ThrowAsync<InvalidOperationException>().WithMessage("*modifiedon*");
        (await new OfflineService(SharesHandler(body)).GetPrincipalAccessAsync("sprk_matter", MatterId))
            .Should().ContainSingle().Which.AccessRightsMask.Should().Be(23,
                "the soft read still answers, with its fallback timestamp");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static void AssertPrincipalAccessBody(string? body, string target, string principal, string accessMask)
    {
        body.Should().NotBeNull();
        using var document = JsonDocument.Parse(body!);
        var root = document.RootElement;

        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("Target", "PrincipalAccess");
        root.GetProperty("Target").GetProperty("@odata.id").GetString().Should().Be(target);

        var access = root.GetProperty("PrincipalAccess");
        access.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("Principal", "AccessMask");
        access.GetProperty("Principal").GetProperty("@odata.id").GetString().Should().Be(principal);
        access.GetProperty("AccessMask").GetString().Should().Be(accessMask);
    }

    /// <summary>Answers the object-type-code lookup and the POA query; anything else is a test failure.</summary>
    private static ScriptedHandler SharesHandler(
        string sharesBody = """{"value":[]}""",
        HttpStatusCode status = HttpStatusCode.OK,
        HttpStatusCode typeCodeStatus = HttpStatusCode.OK)
        => new(request =>
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.ToString());

            if (url.Contains("EntityDefinitions", StringComparison.Ordinal))
                return typeCodeStatus == HttpStatusCode.OK
                    ? Json($$"""{"ObjectTypeCode":{{MatterObjectTypeCode}}}""")
                    : new HttpResponseMessage(typeCodeStatus);

            if (url.Contains("principalobjectaccessset", StringComparison.Ordinal))
                return status == HttpStatusCode.OK ? Json(sharesBody) : new HttpResponseMessage(status);

            throw new InvalidOperationException($"Unexpected request: {request.Method} {url}");
        });

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>The production service, built through its protected test constructor with a static token.</summary>
    private sealed class OfflineService(HttpMessageHandler handler) : DataverseWebApiService(
        new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
        }).Build(),
        NullLogger<DataverseWebApiService>.Instance,
        confidentialClients: null,
        credential: new StaticTokenCredential());

    /// <summary>A request as it left the client, body included, copied before disposal.</summary>
    private sealed record SentRequest(HttpMethod Method, string Url, string Path, string? Body);

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SentRequest(
                request.Method,
                Uri.UnescapeDataString(request.RequestUri!.ToString()),
                request.RequestUri!.AbsolutePath,
                body));
            return respond(request);
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
