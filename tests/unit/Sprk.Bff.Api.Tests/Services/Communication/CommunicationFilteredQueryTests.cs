using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of the R2 filtered communication query (tasks 011/051 / FR-02 / NFR-03). Every facet composes onto the
/// SAME impersonation read path + REAL <see cref="CommunicationAccessFilter"/> as the by-regarding read — so, like
/// <see cref="CommunicationThreadReadServiceTests"/>, these tests use the real filter and mock only the Dataverse
/// read + caller resolver. No second/divergent filter, no membership-union (retired 2026-07-16), no text-LIKE
/// participant fallback (the junction join uses typed FK / exact-address equality only). The tests assert (a) each
/// facet builds the right OData clause, (b) graceful degradation on malformed/empty filters, and (c) the
/// <c>participant</c> junction join (task 051) is role-exact/FK-backed, composes as AND, and can never leak a
/// message the caller cannot otherwise see (negative access case).
/// </summary>
public class CommunicationFilteredQueryTests
{
    private static readonly Guid CallerSystemUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ThreadId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid RegardingId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private const string CommunicationSet = "sprk_communications";
    private const string AttachmentSet = "sprk_communicationattachments";
    private const string ParticipantSet = "sprk_communicationparticipants";
    private const int TypeMessage = 100000004;
    private const int PrivilegeNone = 100000000;

    private readonly Mock<IImpersonatedCommunicationQuery> _query = new();
    private readonly Mock<ICallerSystemUserResolver> _resolver = new();
    private string? _messageQuery;
    private string? _participantQuery;

    private CommunicationThreadReadService Sut()
    {
        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));

        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => _messageQuery = odata)
              .ReturnsAsync(new[] { MessageRow(Guid.NewGuid(), ThreadId, body: "m", type: TypeMessage) });
        _query.Setup(q => q.QueryAsync(AttachmentSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<Dictionary<string, JsonElement>>());
        // Default: no participant-junction rows (tests that exercise `participant=` override this explicitly).
        _query.Setup(q => q.QueryAsync(ParticipantSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => _participantQuery = odata)
              .ReturnsAsync(Array.Empty<Dictionary<string, JsonElement>>());

        return new CommunicationThreadReadService(
            _query.Object,
            new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
            _resolver.Object,
            Mock.Of<ILogger<CommunicationThreadReadService>>());
    }

    private static ClaimsPrincipal Caller() =>
        new(new ClaimsIdentity(new[] { new Claim("oid", Guid.NewGuid().ToString()) }, "test"));

    private Task<CommunicationQueryResult> Query(
        string? thread = null, string? regarding = null, string? channel = null,
        string? from = null, string? to = null, string? participant = null) =>
        Sut().QueryCommunicationsAsync(thread, regarding, channel, from, to, participant, Caller(), CancellationToken.None);

    // ─────────────────────────── per-facet coverage ───────────────────────────

    [Fact]
    public async Task QueryCommunicationsAsync_ThreadFacet_FiltersByThreadLookup()
    {
        var result = await Query(thread: ThreadId.ToString());

        result.Count.Should().Be(1);
        _messageQuery.Should().Contain($"_sprk_communicationthread_value eq {ThreadId}");
    }

    [Theory]
    [InlineData("sprk_matter", "_sprk_regardingmatter_value")]
    [InlineData("contact", "_sprk_regardingperson_value")]
    [InlineData("account", "_sprk_regardingaccount_value")]
    public async Task QueryCommunicationsAsync_RegardingFacet_ResolvesTypedLookupPerEntityType(
        string entityType, string expectedLookupValue)
    {
        var result = await Query(regarding: $"{entityType}:{RegardingId}");

        result.Count.Should().Be(1);
        _messageQuery.Should().Contain($"{expectedLookupValue} eq {RegardingId}");
    }

    [Fact]
    public async Task QueryCommunicationsAsync_ChannelFacet_FiltersByCommunicationType()
    {
        await Query(channel: TypeMessage.ToString());

        _messageQuery.Should().Contain($"sprk_communicationtype eq {TypeMessage}");
    }

    [Fact]
    public async Task QueryCommunicationsAsync_DateRangeFacets_FilterBySentAtRange()
    {
        await Query(from: "2026-07-01T00:00:00Z", to: "2026-07-31T23:59:59Z");

        _messageQuery.Should().Contain("sprk_sentat ge 2026-07-01");
        _messageQuery.Should().Contain("sprk_sentat le 2026-07-31");
        _messageQuery.Should().Contain(" and ", "multiple facets are AND-composed");
    }

    // ─────────────────────────── no-leak: access filter applied on the filtered path ───────────────────────────

    [Fact]
    public async Task QueryCommunicationsAsync_AppliesSharedAccessFilter_PrivilegeRidesAsMetadataNeverGates()
    {
        // The filtered path routes rows through the SAME CommunicationAccessFilter — a privileged message is returned
        // (privilege never gates, ADR-015) but carries its classification, proving the filter (not a second
        // mechanism) produced the result set.
        var sut = Sut();
        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { MessageRow(Guid.NewGuid(), ThreadId, body: "sensitive", privilege: 100000002) });

        var result = await sut.QueryCommunicationsAsync(
            thread: ThreadId.ToString(), null, null, null, null, null, Caller(), CancellationToken.None);

        result.Messages.Should().ContainSingle().Which.Privilege.Should().Be(100000002);
    }

    [Fact]
    public void CommunicationAccessFilter_InternalOnlyMessage_HiddenFromNonInternalCaller()
    {
        // The filtered query composes the SAME 2-rule filter; internal-only is hidden from a non-internal caller.
        // Anchored against the REAL filter (external callers are R2/R3). Exhaustive coverage: CommunicationAccessFilterTests.
        var filter = new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>());
        var nonInternal = new CommunicationAccessContext(CallerSystemUserId, IsInternalUser: false);
        var internalOnly = new Entity("sprk_communication", Guid.NewGuid())
        {
            ["sprk_isinternalonly"] = true,
            ["sprk_privilegeclassification"] = new OptionSetValue(PrivilegeNone),
        };

        filter.EvaluateMessage(nonInternal, internalOnly).IsVisible.Should().BeFalse();
    }

    // ─────────────────────────── participant facet (task 051 — joins the sprk_communicationparticipant junction) ───

    [Fact]
    public async Task QueryCommunicationsAsync_ParticipantFacet_ResolvedPersonId_JoinsJunctionByTypedLookup()
    {
        var personId = Guid.NewGuid();
        var matchedMessageId = Guid.NewGuid();
        var sut = Sut();
        _query.Setup(q => q.QueryAsync(ParticipantSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => _participantQuery = odata)
              .ReturnsAsync(new[] { ParticipantRow(matchedMessageId) });
        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => _messageQuery = odata)
              .ReturnsAsync(new[] { MessageRow(matchedMessageId, ThreadId, body: "m", type: TypeMessage) });

        var result = await sut.QueryCommunicationsAsync(null, null, null, null, null, personId.ToString(), Caller(), CancellationToken.None);

        result.Count.Should().Be(1);
        _participantQuery.Should().Contain($"_sprk_systemuser_value eq {personId}")
            .And.Contain($"_sprk_contact_value eq {personId}", "a resolved GUID matches EITHER typed lookup — role-exact, FK-backed, not a text-LIKE scan");
        _messageQuery.Should().Contain($"sprk_communicationid eq {matchedMessageId}");
    }

    [Fact]
    public async Task QueryCommunicationsAsync_ParticipantFacet_UnresolvedAddress_MatchesExactAddressText()
    {
        const string address = "external@example.com";
        var matchedMessageId = Guid.NewGuid();
        var sut = Sut();
        _query.Setup(q => q.QueryAsync(ParticipantSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => _participantQuery = odata)
              .ReturnsAsync(new[] { ParticipantRow(matchedMessageId) });
        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { MessageRow(matchedMessageId, ThreadId, body: "m", type: TypeMessage) });

        var result = await sut.QueryCommunicationsAsync(null, null, null, null, null, address, Caller(), CancellationToken.None);

        result.Count.Should().Be(1);
        _participantQuery.Should().Contain($"sprk_addresstext eq '{address}'",
            "an unresolved external party is matched by EXACT equality on the junction's address column — never a text-LIKE scan");
    }

    [Fact]
    public async Task QueryCommunicationsAsync_ParticipantFacet_ComposesAsAndWithAnotherFacet()
    {
        var sut = Sut();
        _query.Setup(q => q.QueryAsync(ParticipantSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { ParticipantRow(Guid.NewGuid()) });
        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => _messageQuery = odata)
              .ReturnsAsync(new[] { MessageRow(Guid.NewGuid(), ThreadId, body: "m", type: TypeMessage) });

        await sut.QueryCommunicationsAsync(null, null, TypeMessage.ToString(), null, null, Guid.NewGuid().ToString(), Caller(), CancellationToken.None);

        _messageQuery.Should().Contain($"sprk_communicationtype eq {TypeMessage}");
        _messageQuery.Should().Contain(" and ", "participant= composes as AND with the channel facet, same as every other facet pair");
    }

    [Fact]
    public async Task QueryCommunicationsAsync_ParticipantFacet_NoJunctionMatch_DegradesToEmptyResultNotError()
    {
        // Sut()'s default ParticipantSet setup already returns zero rows. The resulting always-false clause is a
        // real OData predicate a live Dataverse would never match — reflect that here by having the (mocked)
        // impersonated sprk_communication read return nothing too, rather than relying on the generic canned row.
        var sut = Sut();
        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => _messageQuery = odata)
              .ReturnsAsync(Array.Empty<Dictionary<string, JsonElement>>());

        var result = await sut.QueryCommunicationsAsync(null, null, null, null, null, Guid.NewGuid().ToString(), Caller(), CancellationToken.None);

        result.Count.Should().Be(0);
        result.Messages.Should().BeEmpty();
        _messageQuery.Should().Contain($"sprk_communicationid eq {Guid.Empty}",
            "no junction match composes to an always-false clause rather than silently ignoring participant=");
    }

    [Fact]
    public async Task QueryCommunicationsAsync_ParticipantFacet_NegativeAccess_CandidateCallerCannotSeeIsNotReturned()
    {
        // The junction says this person participates in `hiddenMessageId`, but the impersonated sprk_communication
        // read (Dataverse's native row-level security, standing in for "the caller has no ownership/sharing/BU
        // grant on this record") returns NOTHING for that id — modeling a message the caller cannot otherwise see.
        // The participant join must not leak it: it only supplies a candidate id into the SAME impersonated read +
        // access filter every other facet uses (no second/divergent access mechanism, NFR-03).
        var personId = Guid.NewGuid();
        var hiddenMessageId = Guid.NewGuid();
        var sut = Sut();
        _query.Setup(q => q.QueryAsync(ParticipantSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { ParticipantRow(hiddenMessageId) });
        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => _messageQuery = odata)
              .ReturnsAsync(Array.Empty<Dictionary<string, JsonElement>>());

        var result = await sut.QueryCommunicationsAsync(null, null, null, null, null, personId.ToString(), Caller(), CancellationToken.None);

        result.Count.Should().Be(0, "a candidate id the impersonated read does not return must never surface via the participant join");
        _messageQuery.Should().Contain($"sprk_communicationid eq {hiddenMessageId}",
            "the candidate id WAS passed to the impersonated read — Dataverse's own scoping excluded it, proving no bypass occurred");
    }

    [Fact]
    public async Task QueryCommunicationsAsync_ParticipantFacet_MalformedValue_Returns400()
    {
        var act = () => Query(participant: new string('x', 401)); // exceeds sprk_addresstext Text(400) and is not a GUID

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(400);
        _query.Verify(q => q.QueryAsync(ParticipantSet, It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "a malformed participant value must fail before issuing the junction query");
    }

    [Fact]
    public async Task QueryCommunicationsAsync_ParticipantFacet_BlankValue_DegradesGracefullyAsNoFilter()
    {
        // A blank/whitespace participant= alongside a real facet behaves as if participant were absent entirely —
        // never an error, never a junction query.
        var result = await Query(thread: ThreadId.ToString(), participant: "   ");

        result.Count.Should().Be(1);
        _query.Verify(q => q.QueryAsync(ParticipantSet, It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────── graceful degradation (ADR-019, never 500 / never an unfiltered dump) ───────────

    [Fact]
    public async Task QueryCommunicationsAsync_NoFacet_Returns400AndNeverQueries()
    {
        var act = () => Query();

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(400);
        _query.Verify(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "no facet must never dump every communication the caller can see");
    }

    [Theory]
    [InlineData("not-a-guid", null, null, null)]                  // bad thread
    [InlineData(null, "sprk_matter", null, null)]                 // regarding missing ':id'
    [InlineData(null, "sprk_notathing:55555555-5555-5555-5555-555555555555", null, null)] // unknown regarding entity
    [InlineData(null, null, "not-an-int", null)]                  // bad channel
    [InlineData(null, null, null, "not-a-date")]                  // bad from
    public async Task QueryCommunicationsAsync_MalformedFacet_Returns400(
        string? thread, string? regarding, string? channel, string? from)
    {
        var act = () => Query(thread: thread, regarding: regarding, channel: channel, from: from);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(400);
    }

    // ─────────────────────────── row builder ───────────────────────────

    private static Dictionary<string, JsonElement> MessageRow(
        Guid id, Guid threadId, string? body = null, int? type = null,
        bool internalOnly = false, int privilege = PrivilegeNone) => new()
        {
            ["sprk_communicationid"] = El(id.ToString()),
            ["_sprk_communicationthread_value"] = El(threadId.ToString()),
            ["createdon"] = El("2026-07-19T12:00:00Z"),
            ["sprk_isinternalonly"] = El(internalOnly),
            ["sprk_privilegeclassification"] = El(privilege),
            ["sprk_body"] = El(body ?? ""),
            ["sprk_communicationtype"] = El(type ?? TypeMessage),
        };

    private static Dictionary<string, JsonElement> ParticipantRow(Guid communicationId) => new()
    {
        ["_sprk_communication_value"] = El(communicationId.ToString()),
    };

    private static JsonElement El<T>(T value) => JsonSerializer.SerializeToElement(value);
}
