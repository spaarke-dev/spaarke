using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication.Access;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Api.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 056b: the contract of <c>POST /api/communications/send</c> with a
/// <c>privateContext</c> (a private message in a record's context). Every refusal is a ProblemDetails with nothing sent (the
/// Message channel sender never called) and nothing created:
/// <list type="bullet">
///   <item>400 (<c>VALIDATION_ERROR</c>): not a Message; an unknown table or an empty id; no thread; any To/Cc/Bcc; an
///   association; an inheritance; a thread that is not the caller's own strict 1:1 Direct thread; a reply into another
///   thread.</item>
///   <item>403 with the owner's exact texts: <see cref="PrivateMessageSendGate.SenderAccessDetail"/> /
///   <see cref="PrivateMessageSendGate.RecipientAccessDetail"/>, codes <see cref="PrivateMessageSendGate.SenderAccessCode"/> /
///   <see cref="PrivateMessageSendGate.RecipientAccessCode"/>.</item>
///   <item>503 <see cref="PrivateMessageSendGate.CheckUnavailableCode"/> for an unanswered check; 409
///   <see cref="PrivateMessageSendGate.ColumnsMissingCode"/> naming the schema script.</item>
/// </list>
/// Host: <see cref="PrivateMessageSendHost"/> (the real route and service; the gate's Dataverse seams scripted).
/// </summary>
public class PrivateMessageSendContractTests : IClassFixture<PrivateMessageSendHost>
{
    private readonly PrivateMessageSendHost _fixture;

    public PrivateMessageSendContractTests(PrivateMessageSendHost fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private void AssertNothingSent()
    {
        _fixture.Host.MessageSender.Sent.Should().BeEmpty("nothing reaches the channel");
        _fixture.Host.EmailSender.Sent.Should().BeEmpty();
        _fixture.Host.Entities.Verify(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static async Task<JsonElement> Problem(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string? Text(JsonElement problem, string name) =>
        problem.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>The stable code, as the BFF's problem writer carries it (<c>extensions.code</c>).</summary>
    private static string? Code(JsonElement problem) =>
        problem.TryGetProperty("extensions", out var ext) ? Text(ext, "code") : null;

    private const string MatterSet = "sprk_matters";
    private const string CommunicationSet = "sprk_communications";

    /// <summary>What the send route's record filter needs so a request reaches the service (each case is the service's to refuse).</summary>
    private void PassTheRecordFilter(string shape, Guid matter, Guid source)
    {
        if (shape == "association")
            _fixture.Host.Probe.Grant(MatterSet, matter, Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.AppendTo);
        if (shape == "inherit")
        {
            // A source filed under nothing, every regarding column returned (an unset lookup as an explicit null).
            var columns = new Dictionary<string, object?>
            {
                ["sprk_communicationid"] = source.ToString(),
                ["sprk_isinternalonly"] = false,
                ["sprk_privilegeclassification"] = 100000000,
            };
            foreach (var (_, field) in Sprk.Bff.Api.Services.Communication.Engine.RegardingFieldMap.All)
                columns[$"_{field}_value"] = null;
            _fixture.Host.Query.Visible(CommunicationSet, source, JsonSerializer.Serialize(columns));
        }
    }

    // ── 400: the request's shape ─────────────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> InvalidShapes() => new()
    {
        "email", "unknown-table", "no-table", "empty-id", "no-thread", "to", "cc", "bcc", "association", "inherit",
    };

    [Theory]
    [MemberData(nameof(InvalidShapes))]
    public async Task Send_WithAPrivateContext_AndAnInvalidShape_Returns400_AndSendsNothing(string shape)
    {
        var thread = _fixture.PairThread();
        var matter = Guid.NewGuid();
        var source = Guid.NewGuid();
        PassTheRecordFilter(shape, matter, source);
        object body = shape switch
        {
            "email" => PrivateMessageSendHost.PrivateBody(null, type: "Email", to: new[] { "someone@contoso.com" }),
            "unknown-table" => PrivateMessageSendHost.PrivateBody(thread, entityType: "account"),
            "no-table" => PrivateMessageSendHost.PrivateBody(thread, entityType: ""),
            "empty-id" => PrivateMessageSendHost.PrivateBody(thread, recordId: Guid.Empty),
            "no-thread" => PrivateMessageSendHost.PrivateBody(null),
            "to" => PrivateMessageSendHost.PrivateBody(thread, to: new[] { "jane@contoso.com" }),
            "cc" => PrivateMessageSendHost.PrivateBody(thread, cc: new[] { "jane@contoso.com" }),
            "bcc" => PrivateMessageSendHost.PrivateBody(thread, bcc: new[] { "jane@contoso.com" }),
            "association" => PrivateMessageSendHost.PrivateBody(thread, association: ("sprk_matter", matter)),
            "inherit" => PrivateMessageSendHost.PrivateBody(thread, inheritFrom: source),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var response = await _fixture.SendAsync(body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        AssertNothingSent();
        _fixture.Script.Calls.Should().BeEmpty("no record check runs for a request of the wrong shape");
    }

    public static TheoryData<string> NotTheCallersPair() => new() { "record-thread", "not-direct", "someone-elses-pair", "default-thread" };

    [Theory]
    [MemberData(nameof(NotTheCallersPair))]
    public async Task Send_WithAPrivateContext_IntoAThreadThatIsNotTheCallersOwnPair_Returns400_AndSendsNothing(string variant)
    {
        var threadMatter = Guid.NewGuid();
        // The route's filter admits a post into a record thread only with AppendTo on its record; granted, so the service decides.
        _fixture.Host.Probe.Grant(MatterSet, threadMatter, Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.AppendTo);
        var thread = variant switch
        {
            "record-thread" => _fixture.RecordThread(threadMatter),
            "not-direct" => _fixture.PairThread(threadType: PrivateMessageSendHost.ThreadTypeRecordAnchored),
            "someone-elses-pair" => _fixture.PairThread(owner: Guid.NewGuid(), other: Guid.NewGuid()),
            "default-thread" => _fixture.PairThread(defaultThread: true),
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };

        var response = await _fixture.SendAsync(PrivateMessageSendHost.PrivateBody(thread));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        Text(await Problem(response), "detail").Should().Contain("one-to-one direct conversation");
        AssertNothingSent();
        _fixture.Script.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Send_WithAPrivateContext_ReplyingToAMessageInAnotherThread_Returns400_TheSameAsForAnUnknownParent()
    {
        var thread = _fixture.PairThread();
        var elsewhere = Guid.NewGuid();
        _fixture.MessageInThread(elsewhere, Guid.NewGuid());

        var other = await _fixture.SendAsync(PrivateMessageSendHost.PrivateBody(thread, inReplyTo: elsewhere.ToString()));
        var unknown = await _fixture.SendAsync(PrivateMessageSendHost.PrivateBody(thread, inReplyTo: Guid.NewGuid().ToString()));

        other.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CommunicationRecordAuthorizationHost.NormalizedBodyAsync(other))
            .Should().Be(await CommunicationRecordAuthorizationHost.NormalizedBodyAsync(unknown));
        AssertNothingSent();
    }

    [Fact]
    public async Task Send_WithAPrivateContext_ReplyingToAMessageInTheSameThread_IsNotRefusedForTheReply()
    {
        var thread = _fixture.PairThread();
        var parent = Guid.NewGuid();
        _fixture.MessageInThread(parent, thread);

        var response = await _fixture.SendAsync(PrivateMessageSendHost.PrivateBody(thread, inReplyTo: parent.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    // ── 403: the owner's two texts ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Send_WhenTheSenderCannotReadTheRecord_Returns403_WithTheSenderText()
    {
        var thread = _fixture.PairThread();
        _fixture.Script.SenderRights = Spaarke.Dataverse.AccessRights.None;

        var response = await _fixture.SendAsync(PrivateMessageSendHost.PrivateBody(thread));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await Problem(response);
        Text(problem, "detail").Should().Be("You do not have required record access");
        Code(problem).Should().Be(PrivateMessageSendGate.SenderAccessCode);
        AssertNothingSent();
    }

    [Fact]
    public async Task Send_WhenTheRecipientCannotReadTheRecord_Returns403_WithTheRecipientText()
    {
        var thread = _fixture.PairThread();
        _fixture.Script.RecipientRights = Spaarke.Dataverse.AccessRights.None;

        var response = await _fixture.SendAsync(PrivateMessageSendHost.PrivateBody(thread));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await Problem(response);
        Text(problem, "detail").Should().Be("Recipient does not have required record access");
        Code(problem).Should().Be(PrivateMessageSendGate.RecipientAccessCode);
        AssertNothingSent();
    }

    [Theory]
    [InlineData("sender")]
    [InlineData("recipient")]
    public async Task Send_EveryRefusalOfOneSide_GetsTheSameBody(string side)
    {
        var user = side == "sender" ? PrivateMessageSendHost.Caller : PrivateMessageSendHost.Recipient;
        var bodies = new List<string>();
        foreach (var cause in new[] { "no-read", "walled", "external-on-restricted" })
        {
            _fixture.Reset();
            var thread = _fixture.PairThread();
            switch (cause)
            {
                case "no-read" when side == "sender":
                    _fixture.Script.SenderRights = Spaarke.Dataverse.AccessRights.None;
                    break;
                case "no-read":
                    _fixture.Script.RecipientRights = Spaarke.Dataverse.AccessRights.None;
                    break;
                case "walled":
                    _fixture.Script.Wall[user] = SecureShareWallOutcome.Walled;
                    break;
                default:
                    _fixture.Script.Restricted = true;
                    _fixture.Script.External[user] = true;
                    break;
            }

            var response = await _fixture.SendAsync(PrivateMessageSendHost.PrivateBody(thread));
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, cause);
            bodies.Add(await CommunicationRecordAuthorizationHost.NormalizedBodyAsync(response));
            AssertNothingSent();
        }

        bodies.Distinct().Should().ContainSingle("the answer never says which check refused");
    }

    // ── 503: a check that cannot be answered ─────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> Unanswered() => new()
    {
        "thread-shape", "sender-rights-fault", "recipient-rights-null", "sender-wall-unverifiable", "recipient-wall-unverifiable",
        "restricted-unread-external", "external-flag-unread", "columns-unread",
    };

    [Theory]
    [MemberData(nameof(Unanswered))]
    public async Task Send_WhenACheckCannotBeAnswered_Returns503_AndSendsNothing(string check)
    {
        var thread = check == "thread-shape" ? _fixture.FaultingThread() : _fixture.PairThread();
        switch (check)
        {
            case "sender-rights-fault":
                _fixture.Script.SenderRightsFault = new HttpRequestException("probe faulted");
                break;
            case "recipient-rights-null":
                _fixture.Script.RecipientRights = null;
                break;
            case "sender-wall-unverifiable":
                _fixture.Script.Wall[PrivateMessageSendHost.Caller] = SecureShareWallOutcome.Unverifiable;
                break;
            case "recipient-wall-unverifiable":
                _fixture.Script.Wall[PrivateMessageSendHost.Recipient] = SecureShareWallOutcome.Unverifiable;
                break;
            case "restricted-unread-external":
                _fixture.Script.Restricted = null;
                _fixture.Script.External[PrivateMessageSendHost.Recipient] = true;
                break;
            case "external-flag-unread":
                _fixture.Script.Restricted = true;
                _fixture.Script.External[PrivateMessageSendHost.Caller] = null;
                break;
            case "columns-unread":
                _fixture.Script.ColumnsPresent = null;
                break;
        }

        var response = await _fixture.SendAsync(PrivateMessageSendHost.PrivateBody(thread));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        var problem = await Problem(response);
        Code(problem).Should().Be(PrivateMessageSendGate.CheckUnavailableCode);
        Text(problem, "detail").Should().Contain("try again");
        AssertNothingSent();
    }

    [Fact]
    public async Task Send_WhenTheRecordIsUnreadableButNoParticipantIsExternal_IsNotRefused()
    {
        // The Restricted state only matters for a participant flagged external; an unread flag alone refuses nobody.
        var thread = _fixture.PairThread();
        _fixture.Script.Restricted = null;

        var response = await _fixture.SendAsync(PrivateMessageSendHost.PrivateBody(thread));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    // ── 409: the reference columns are missing ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Send_WhenTheReferenceColumnsAreMissing_Returns409_NamingTheSchemaScript_AndSendsNothing()
    {
        var thread = _fixture.PairThread();
        _fixture.Script.ColumnsPresent = false;

        var response = await _fixture.SendAsync(PrivateMessageSendHost.PrivateBody(thread));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await Problem(response);
        Code(problem).Should().Be(PrivateMessageSendGate.ColumnsMissingCode);
        Text(problem, "detail").Should().Contain("scripts/Set-PrivateContextSchema.ps1");
        AssertNothingSent();
    }
}
