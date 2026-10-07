using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
              {"principalid":"{{UserId}}","principaltypecode":8,"accessrightsmask":23,"changedon":"2026-09-15T10:00:00Z"},
              {"principalid":"{{TeamId}}","principaltypecode":9,"accessrightsmask":1,"changedon":"2026-09-15T11:00:00Z"},
              {"principalid":"{{Guid.NewGuid()}}","principaltypecode":2,"accessrightsmask":1,"changedon":"2026-09-15T12:00:00Z"}
            ]}
            """);

        var shares = await new OfflineService(handler).GetPrincipalAccessOrThrowAsync("sprk_matter", MatterId);

        shares.Should().Equal(
            new DataversePrincipalAccess(DataversePrincipalRef.User(UserId), 23, new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero)),
            new DataversePrincipalAccess(DataversePrincipalRef.Team(TeamId), 1, new DateTimeOffset(2026, 9, 15, 11, 0, 0, TimeSpan.Zero)));
        handler.Requests.Last().Url.Should().Contain(
            $"objectid eq {MatterId} and objecttypecode eq 'sprk_matter'",
            "the read must be scoped to this record of this table");
    }

    /// <summary>
    /// 🔴 Task 139 regression pin — the LIVE wire shape. Read from spaarkedev1 on 2026-10-02 (the body below is that
    /// response, ids replaced): <c>principaltypecode</c> is an EntityName column and the Web API returns it as the
    /// logical-name STRING, not the object type code. The reader accepted only a JSON number, so the strict read refused
    /// every record with a share ("no readable principal") and the soft read silently answered "no shares". The tests
    /// above feed the numeric form, which is why it went unseen.
    /// </summary>
    [Fact]
    public async Task GetPrincipalAccess_ReadsTheLiveStringPrincipalTypeCode_InBothReads()
    {
        var body = $$"""
            {"@odata.context":"https://test.crm.dynamics.com/api/data/v9.2/$metadata#principalobjectaccessset(objectid,principalid,principaltypecode,accessrightsmask,changedon)",
             "value":[
              {"@odata.etag":"W/\"26686396\"","principalid":"{{UserId}}","objectid":"{{MatterId}}","changedon":"2026-10-02T03:51:06Z","principalobjectaccessid":"{{Guid.NewGuid()}}","accessrightsmask":262167,"principaltypecode":"systemuser"},
              {"principalid":"{{TeamId}}","changedon":"2026-10-02T04:00:00Z","accessrightsmask":1,"principaltypecode":"team"},
              {"principalid":"{{Guid.NewGuid()}}","changedon":"2026-10-02T04:00:00Z","accessrightsmask":1,"principaltypecode":"account"}
            ]}
            """;
        var expected = new[]
        {
            new DataversePrincipalAccess(DataversePrincipalRef.User(UserId), 262167, new DateTimeOffset(2026, 10, 2, 3, 51, 6, TimeSpan.Zero)),
            new DataversePrincipalAccess(DataversePrincipalRef.Team(TeamId), 1, new DateTimeOffset(2026, 10, 2, 4, 0, 0, TimeSpan.Zero)),
        };

        (await new OfflineService(SharesHandler(body)).GetPrincipalAccessOrThrowAsync("sprk_matter", MatterId))
            .Should().Equal(expected, "an unmodelled principal type is skipped, a modelled one is read — never refused");
        (await new OfflineService(SharesHandler(body)).GetPrincipalAccessAsync("sprk_matter", MatterId))
            .Should().Equal(expected, "the soft read must not silently answer 'no shares'");
    }

    /// <summary>The twin: a principal type that is neither a number nor a string is unreadable, and the strict read refuses.</summary>
    [Fact]
    public async Task GetPrincipalAccessOrThrowAsync_WhenThePrincipalTypeIsNeitherNumberNorString_Throws()
    {
        var body = $$"""{"value":[{"principalid":"{{UserId}}","principaltypecode":true,"accessrightsmask":1,"changedon":"2026-09-15T10:00:00Z"}]}""";

        var strict = () => new OfflineService(SharesHandler(body)).GetPrincipalAccessOrThrowAsync("sprk_matter", MatterId);

        await strict.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no readable principal*");
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

    /// <summary>
    /// 🔴 Regression pin for the 2026-09-22 three-fault fix. The previous test here asserted that an unreadable
    /// OBJECT TYPE CODE made the strict read throw — a refusal that no longer exists, because POA's
    /// <c>objecttypecode</c> holds the LOGICAL NAME and the metadata lookup is off this path entirely.
    /// <para>That test passed for months against a query real Dataverse answers <b>400</b> to, because the double
    /// echoed whatever the code asked for. This replacement pins the WIRE SHAPE instead: the three things Dataverse
    /// actually rejected are each asserted, so reintroducing any one of them reddens rather than passing.</para>
    /// </summary>
    [Fact]
    public async Task GetPrincipalAccessOrThrowAsync_IssuesTheWireShapeDataverseAccepts()
    {
        var handler = SharesHandler();
        await new OfflineService(handler).GetPrincipalAccessOrThrowAsync("sprk_matter", MatterId);

        var url = handler.Requests
            .Single(r => r.Url.Contains("principalobjectaccessset", StringComparison.Ordinal)).Url;

        // Fault 1: POA has no `modifiedon` — 400 "Could not find a property named 'modifiedon'".
        url.Should().Contain("changedon").And.NotContain("modifiedon");

        // Faults 2 + 3: `objecttypecode` is Edm.String holding the LOGICAL NAME. An unquoted int gave
        // 400 "incompatible types ... 'Edm.String' and 'Edm.Int32'"; a quoted NUMBER gave
        // 400 "The entity with a name = '10473' ... was not found in the MetadataCache".
        url.Should().Contain("objecttypecode eq 'sprk_matter'");
        url.Should().NotContain($"objecttypecode eq {MatterObjectTypeCode}");

        // The metadata read is off this path — it can no longer fail it.
        handler.Requests.Should().NotContain(
            r => r.Url.Contains("EntityDefinitions", StringComparison.Ordinal),
            "POA's objecttypecode is the logical name, so no object-type-code lookup is issued");
    }

    /// <summary>A second page could hold the very principal a caller is about to change.</summary>
    [Fact]
    public async Task GetPrincipalAccessOrThrowAsync_WhenTheSharesContinueOnAnotherPage_Throws()
    {
        var handler = SharesHandler($$"""
            {"value":[{"principalid":"{{UserId}}","principaltypecode":8,"accessrightsmask":1,"changedon":"2026-09-15T10:00:00Z"}],
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
        var body = $$"""{"value":[{"principalid":"{{UserId}}","principaltypecode":8,"changedon":"2026-09-15T10:00:00Z"}]}""";

        var strict = () => new OfflineService(SharesHandler(body)).GetPrincipalAccessOrThrowAsync("sprk_matter", MatterId);

        await strict.Should().ThrowAsync<InvalidOperationException>().WithMessage("*rights mask*");
        (await new OfflineService(SharesHandler(body)).GetPrincipalAccessAsync("sprk_matter", MatterId))
            .Should().ContainSingle().Which.AccessRightsMask.Should().Be(0);
    }

    [Fact]
    public async Task GetPrincipalAccessOrThrowAsync_WhenARowHasNoReadablePrincipal_Throws()
    {
        var body = """{"value":[{"principaltypecode":8,"accessrightsmask":1,"changedon":"2026-09-15T10:00:00Z"}]}""";

        var strict = () => new OfflineService(SharesHandler(body)).GetPrincipalAccessOrThrowAsync("sprk_matter", MatterId);

        await strict.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no readable principal*");
    }

    /// <summary>
    /// <c>changedon</c> is in the <c>$select</c>, so a value that cannot be read means an anomalous response. The
    /// strict read refuses it for the same reason it refuses an unreadable mask: "incomplete counts as failed" must
    /// not carry an exception that quietly reports a share as changed just now. The soft read keeps its fallback,
    /// because its callers only display the value (Step 9.5 review finding 12).
    /// </summary>
    [Fact]
    public async Task GetPrincipalAccessOrThrowAsync_WhenARowHasNoReadableModifiedOn_Throws_WhileTheSoftReadFallsBack()
    {
        var body = $$"""{"value":[{"principalid":"{{UserId}}","principaltypecode":8,"accessrightsmask":23}]}""";

        var strict = () => new OfflineService(SharesHandler(body)).GetPrincipalAccessOrThrowAsync("sprk_matter", MatterId);

        await strict.Should().ThrowAsync<InvalidOperationException>().WithMessage("*changedon*");
        (await new OfflineService(SharesHandler(body)).GetPrincipalAccessAsync("sprk_matter", MatterId))
            .Should().ContainSingle().Which.AccessRightsMask.Should().Be(23,
                "the soft read still answers, with its fallback timestamp");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Task 149 — the batched strict read (the secure-child share synchronizer)
    // ─────────────────────────────────────────────────────────────────────────────

    private static readonly Guid OtherDocumentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>
    /// The wire shape verified live on spaarkedev1 (2026-10-02): the logical-name <c>objecttypecode</c>, the records OR-ed,
    /// <c>objectid</c> selected; rows grouped by record; a record with no share answered EMPTY, never missing.
    /// </summary>
    [Fact]
    public async Task GetPrincipalAccessForRecordsOrThrowAsync_GroupsTheRowsByRecord_AndAnswersEveryRecordAskedAbout()
    {
        var handler = SharesHandler($$"""
            {"value":[
              {"objectid":"{{MatterId}}","principalid":"{{UserId}}","principaltypecode":"systemuser","accessrightsmask":23,"changedon":"2026-10-02T03:51:06Z"},
              {"objectid":"{{MatterId}}","principalid":"{{TeamId}}","principaltypecode":"team","accessrightsmask":1,"changedon":"2026-10-02T04:00:00Z"}
            ]}
            """);

        var shares = await new OfflineService(handler)
            .GetPrincipalAccessForRecordsOrThrowAsync("sprk_document", new[] { MatterId, OtherDocumentId });

        shares[MatterId].Should().HaveCount(2);
        shares[OtherDocumentId].Should().BeEmpty("a record with no share is answered, not left out");
        var url = handler.Requests.Single().Url;
        url.Should().Contain($"objecttypecode eq 'sprk_document' and (objectid eq {MatterId} or objectid eq {OtherDocumentId})");
        url.Should().Contain("$select=objectid,principalid,principaltypecode,accessrightsmask,changedon");
    }

    [Fact]
    public async Task GetPrincipalAccessForRecordsOrThrowAsync_ARowForARecordNotAskedAbout_Throws()
    {
        var body = $$"""{"value":[{"objectid":"{{Guid.NewGuid()}}","principalid":"{{UserId}}","principaltypecode":8,"accessrightsmask":1,"changedon":"2026-10-02T04:00:00Z"}]}""";

        var read = () => new OfflineService(SharesHandler(body))
            .GetPrincipalAccessForRecordsOrThrowAsync("sprk_document", new[] { MatterId });

        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no record that was asked about*");
    }

    [Fact]
    public async Task GetPrincipalAccessForRecordsOrThrowAsync_ASecondPage_Throws()
    {
        var body = $$"""
            {"value":[{"objectid":"{{MatterId}}","principalid":"{{UserId}}","principaltypecode":8,"accessrightsmask":1,"changedon":"2026-10-02T04:00:00Z"}],
             "@odata.nextLink":"https://test.crm.dynamics.com/api/data/v9.2/principalobjectaccessset?$skiptoken=abc"}
            """;

        var read = () => new OfflineService(SharesHandler(body))
            .GetPrincipalAccessForRecordsOrThrowAsync("sprk_document", new[] { MatterId });

        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*another page*");
    }

    /// <summary>
    /// Task 149 r1 (F6): the batched read is STRICT per row, like the single one. An unreadable mask read softly is 0 —
    /// which the synchronizer treats as "no direct share" — so a principal holding a real share would never be revoked.
    /// </summary>
    [Fact]
    public async Task GetPrincipalAccessForRecordsOrThrowAsync_WhenARowHasNoReadableMask_Throws()
    {
        var body = $$"""{"value":[{"objectid":"{{MatterId}}","principalid":"{{UserId}}","principaltypecode":8,"changedon":"2026-10-02T04:00:00Z"}]}""";

        var read = () => new OfflineService(SharesHandler(body))
            .GetPrincipalAccessForRecordsOrThrowAsync("sprk_document", new[] { MatterId });

        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*rights mask*");
    }

    [Fact]
    public async Task GetPrincipalAccessForRecordsOrThrowAsync_ReadsInBatches()
    {
        var handler = SharesHandler();
        var ids = Enumerable.Range(0, DataverseWebApiService.PrincipalAccessBatchSize + 1).Select(_ => Guid.NewGuid()).ToArray();

        var shares = await new OfflineService(handler).GetPrincipalAccessForRecordsOrThrowAsync("sprk_document", ids);

        shares.Should().HaveCount(ids.Length);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPrincipalAccessForRecordsOrThrowAsync_WhenDataverseRefuses_Throws()
    {
        var read = () => new OfflineService(SharesHandler(status: HttpStatusCode.ServiceUnavailable))
            .GetPrincipalAccessForRecordsOrThrowAsync("sprk_document", new[] { MatterId });

        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*503*");
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

    // ─────────────────────────────────────────────────────────────────────────────
    // Task 171 (adversarial finding 3): the effective-rights read a REVOKING caller uses
    // ─────────────────────────────────────────────────────────────────────────────

    private static HttpResponseMessage Status(HttpStatusCode status, string? errorCode) =>
        new(status)
        {
            Content = errorCode is null
                ? new StringContent(string.Empty)
                : new StringContent("{\"error\":{\"code\":\"" + errorCode + "\",\"message\":\"scripted\"}}", Encoding.UTF8, "application/json"),
        };

    [Theory(DisplayName = "Task 171 (finding 3): the strict rights read answers only what is an ACCESS answer — a request-level 403 is UNKNOWN")]
    [InlineData(HttpStatusCode.Forbidden, "0x80040220", "None")]      // access-check denial — an answer about the user
    [InlineData(HttpStatusCode.Forbidden, "0x80048306", "None")]      // "does not have ReadAccess" — the live unshared-user answer
    [InlineData(HttpStatusCode.NotFound, null, "None")]               // Dataverse's "cannot read this record"
    [InlineData(HttpStatusCode.Forbidden, "0x8004A110", "unknown")]   // CannotActOnBehalfOfAnotherUser — the app's fault
    [InlineData(HttpStatusCode.Forbidden, "0x80040216", "unknown")]   // any other 403 code
    [InlineData(HttpStatusCode.Forbidden, null, "unknown")]           // an unreadable 403
    public async Task RetrievePrincipalRightsOrUnknownAsync_MapsOnlyAccessAnswers(
        HttpStatusCode status, string? errorCode, string expected)
    {
        var handler = new ScriptedHandler(_ => Status(status, errorCode));

        var rights = await new OfflineService(handler).RetrievePrincipalRightsOrUnknownAsync(UserId, "sprk_matters", MatterId);

        if (expected == "unknown")
            rights.Should().BeNull("revoking on a fault of the REQUEST would remove every grant on every pass");
        else
            rights.Should().Be(AccessRights.None);

        var sent = handler.Requests.Should().ContainSingle().Subject;
        sent.Url.Should().Contain($"systemusers({UserId})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess");
    }

    [Theory(DisplayName = "Live regression fix: a 5xx or throttled rights read is never an answer — it THROWS, and the revoking caller keeps the grant")]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task RetrievePrincipalRightsOrUnknownAsync_ServerFault_Throws(HttpStatusCode status)
    {
        var handler = new ScriptedHandler(_ => Status(status, null));

        var act = () => new OfflineService(handler).RetrievePrincipalRightsOrUnknownAsync(UserId, "sprk_matters", MatterId);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Theory(DisplayName = "Live regression fix (F1): the JIT removal pass, end to end over the real rights read — an access-denial answer REMOVES the grant; an impersonation fault or a 5xx KEEPS it")]
    [InlineData(HttpStatusCode.Forbidden, "0x80048306", true)]   // "does not have ReadAccess" — the live unshared user (run d03f01eb)
    [InlineData(HttpStatusCode.Forbidden, "0x80040220", true)]   // access-check denial
    [InlineData(HttpStatusCode.Forbidden, "0x8004A110", false)]  // CannotActOnBehalfOfAnotherUser — the app's fault
    [InlineData(HttpStatusCode.InternalServerError, null, false)] // a server fault
    public async Task JitRemoval_OverTheRealRightsRead_RemovesOnlyOnAnAccessAnswer(HttpStatusCode status, string? code, bool removed)
    {
        var project = Guid.Parse("33333333-3333-3333-3333-333333333333");
        const string container = "b!secure-project-container";
        var markerKey = Sprk.Bff.Api.Infrastructure.ExternalAccess.SpeContainerMembershipService.MarkerKey(
            Sprk.Bff.Api.Infrastructure.ExternalAccess.SpeContainerMembershipService.JitWriterMarkerPrefix, UserId);
        var markers = new Dictionary<string, string> { [markerKey] = "perm-jit" };

        var rows = new Moq.Mock<IGenericEntityService>();
        rows.Setup(r => r.GetEntitySetNameAsync("sprk_project", Moq.It.IsAny<CancellationToken>())).ReturnsAsync("sprk_projects");
        rows.Setup(r => r.RetrieveMultipleAsync(Moq.It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.EntityCollection(
                [new Microsoft.Xrm.Sdk.Entity("sprk_project", project) { ["sprk_containerid"] = container }]) { MoreRecords = false });
        rows.Setup(r => r.RetrieveAsync("sprk_project", project, Moq.It.IsAny<string[]>(), Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("sprk_project", project)
            {
                ["sprk_accesspermission"] = new Microsoft.Xrm.Sdk.OptionSetValue(100000000),
            });
        rows.Setup(r => r.RetrieveAsync("systemuser", UserId, Moq.It.IsAny<string[]>(), Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("systemuser", UserId) { ["isdisabled"] = false });

        var membership = new Moq.Mock<Sprk.Bff.Api.Infrastructure.ExternalAccess.SpeContainerMembershipService>(
            Moq.Mock.Of<Sprk.Bff.Api.Infrastructure.Graph.IGraphClientFactory>(),
            NullLogger<Sprk.Bff.Api.Infrastructure.ExternalAccess.SpeContainerMembershipService>.Instance);
        membership.Setup(m => m.ReadMarkersAsync(container, Moq.It.IsAny<CancellationToken>())).ReturnsAsync(markers);
        membership.Setup(m => m.ReadAccessAsync(container, Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sprk.Bff.Api.Infrastructure.ExternalAccess.SpeContainerMembershipService.ContainerAccess(
                [new("perm-jit", ["writer"], "u@contoso.example", UserId.ToString())], true, markers));
        membership.Setup(m => m.RemoveMarkedGrantAsync(container, markerKey, "perm-jit",
                Moq.It.IsAny<Sprk.Bff.Api.Infrastructure.ExternalAccess.SpeContainerMembershipService.ContainerAccess>(),
                Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sprk.Bff.Api.Infrastructure.ExternalAccess.SpeContainerMembershipService.MarkedRemovalOutcome.Removed);

        var registry = new Moq.Mock<Sprk.Bff.Api.Infrastructure.Dataverse.ISecurableEntityRegistry>();
        registry.Setup(r => r.GetSecurableEntitiesAsync(Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "sprk_project" });

        var rights = new Sprk.Bff.Api.Services.Access.DataverseRecordShareService(
            new OfflineService(new ScriptedHandler(_ => Status(status, code))));
        var sync = new Sprk.Bff.Api.Services.Access.SpeContainerMembershipSync(
            rows.Object, membership.Object, registry.Object, rights,
            NullLogger<Sprk.Bff.Api.Services.Access.SpeContainerMembershipSync>.Instance);

        var result = await sync.RemoveRevokedJitGrantsAsync(CancellationToken.None);

        membership.Verify(m => m.RemoveMarkedGrantAsync(container, markerKey, "perm-jit",
                Moq.It.IsAny<Sprk.Bff.Api.Infrastructure.ExternalAccess.SpeContainerMembershipService.ContainerAccess>(),
                Moq.It.IsAny<CancellationToken>()),
            removed ? Moq.Times.Once() : Moq.Times.Never(),
            removed
                ? "no Read means no Write: a user unshared from the secure record must lose container-wide SPE write"
                : "a fault of the REQUEST is not an answer about the user — revoking on it would strip every grant each pass");
        result.Unknown.Should().Be(removed ? 0 : 1);
    }

    [Fact(DisplayName = "Task 171 (finding 3): a 200 answer is the rights Dataverse states")]
    public async Task RetrievePrincipalRightsOrUnknownAsync_Success_ReturnsTheStatedRights()
    {
        var handler = new ScriptedHandler(_ => Json("""{"AccessRights":"ReadAccess, WriteAccess"}"""));

        var rights = await new OfflineService(handler).RetrievePrincipalRightsOrUnknownAsync(UserId, "sprk_matters", MatterId);

        rights.Should().NotBeNull();
        rights!.Value.HasFlag(AccessRights.Write).Should().BeTrue();
    }

    [Fact(DisplayName = "Task 171 (finding 3): the LENIENT read still maps the impersonation fault to None — which is why a revoking caller must not use it")]
    public async Task RetrievePrincipalRightsAsync_ImpersonationFault_IsNone()
    {
        var handler = new ScriptedHandler(_ => Status(HttpStatusCode.Forbidden, "0x8004A110"));

        var rights = await new OfflineService(handler).RetrievePrincipalRightsAsync(UserId, "sprk_matters", MatterId);

        rights.Should().Be(AccessRights.None, "deny-side callers read every 403 as 'no rights' — safe for them, by design");
    }

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
        new Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator(
            NullLogger<Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator>.Instance),
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
