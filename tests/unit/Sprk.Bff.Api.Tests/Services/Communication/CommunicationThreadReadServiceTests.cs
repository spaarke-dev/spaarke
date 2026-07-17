using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of the messaging read model (task 050 / FR-11 / NFR-06/07). The service routes both reads through
/// Dataverse IMPERSONATION (the <see cref="IImpersonatedCommunicationQuery"/> seam simulates "the rows Dataverse
/// returned to this caller") and applies the SHARED, REAL <see cref="CommunicationAccessFilter"/> on top — so these
/// tests use the real filter (pure, cheap; ADR-038 prefers a real collaborator over a mock) and mock only the two
/// genuine module boundaries: the Dataverse read and the caller resolver.
///
/// <para><b>No-leak coverage.</b> Record-level access (private threads, ownership, role depth, BU, sharing) is
/// enforced by impersonation, i.e. by WHICH rows the query returns — proven here by the private-thread negative
/// (empty impersonated set → empty result, no app-only fallback) and the unread-count-excludes-unreadable case.
/// The internal-only (non-internal caller) + privilege enforcement is the filter's job and is covered exhaustively
/// by <see cref="CommunicationAccessFilterTests"/>; R1 callers are internal (external = R2), so the service composes
/// <c>IsInternalUser = true</c> and these tests assert the R1 behaviors that actually exist.</para>
/// </summary>
public class CommunicationThreadReadServiceTests
{
    private static readonly Guid ThreadId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CallerSystemUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const string CommunicationSet = "sprk_communications";
    private const string AttachmentSet = "sprk_communicationattachments";
    private const int PrivilegeNone = 100000000;
    private const int PrivilegePrivileged = 100000002;
    private const int TypeMessage = 100000004;

    private readonly Mock<IImpersonatedCommunicationQuery> _query = new(MockBehavior.Strict);
    private readonly Mock<ICallerSystemUserResolver> _resolver = new();

    private CommunicationThreadReadService Sut()
    {
        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));

        return new CommunicationThreadReadService(
            _query.Object,
            new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
            _resolver.Object,
            Mock.Of<ILogger<CommunicationThreadReadService>>());
    }

    private static ClaimsPrincipal Caller() =>
        new(new ClaimsIdentity(new[] { new Claim("oid", Guid.NewGuid().ToString()) }, "test"));

    private void SetupMessages(params Dictionary<string, JsonElement>[] rows) =>
        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(rows);

    private void SetupAttachments(params Dictionary<string, JsonElement>[] rows) =>
        _query.Setup(q => q.QueryAsync(AttachmentSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(rows);

    // ─────────────────────────── thread-read: projection ───────────────────────────

    [Fact]
    public async Task ReadThreadAsync_ImpersonatedRows_ProjectsBodyChannelSenderAndAttachments()
    {
        var messageId = Guid.NewGuid();
        SetupMessages(MessageRow(messageId, body: "hello", bodyFormat: 1, type: TypeMessage,
            from: "alice@x.com", inReplyTo: "root-1"));
        SetupAttachments(AttachmentRow(Guid.NewGuid(), messageId, docId: Guid.NewGuid(), name: "brief.pdf", type: 1));

        var result = await Sut().ReadThreadAsync(ThreadId, Caller(), since: null, top: null, CancellationToken.None);

        result.ThreadId.Should().Be(ThreadId);
        result.Count.Should().Be(1);
        var msg = result.Messages.Single();
        msg.MessageId.Should().Be(messageId);
        msg.Body.Should().Be("hello");
        msg.BodyFormat.Should().Be(1);
        msg.CommunicationType.Should().Be(TypeMessage);
        msg.From.Should().Be("alice@x.com");
        msg.InReplyTo.Should().Be("root-1");
        msg.Attachments.Should().ContainSingle().Which.FileName.Should().Be("brief.pdf");
    }

    [Fact]
    public async Task ReadThreadAsync_PrivilegedMessage_IsReturnedWithPrivilegeMetadataNotHidden()
    {
        // Privilege NEVER gates a read (ADR-015 / owner 2026-07-16) — it rides along as metadata.
        var messageId = Guid.NewGuid();
        SetupMessages(MessageRow(messageId, body: "sensitive", privilege: PrivilegePrivileged));
        SetupAttachments(); // no attachments

        var result = await Sut().ReadThreadAsync(ThreadId, Caller(), since: null, top: null, CancellationToken.None);

        result.Messages.Should().ContainSingle();
        result.Messages[0].Privilege.Should().Be(PrivilegePrivileged);
    }

    // ─────────────────────────── thread-read: no-leak (private via impersonation) ───────────────────────────

    [Fact]
    public async Task ReadThreadAsync_CallerCannotSeePrivateThread_ReturnsEmptyAndMakesNoAttachmentQuery()
    {
        // Impersonation is the private-thread enforcement: Dataverse returns ZERO rows to a caller without a grant.
        // The service must faithfully return empty (no app-only fallback) AND skip the attachment query (NFR-07).
        SetupMessages(/* none */);

        var result = await Sut().ReadThreadAsync(ThreadId, Caller(), since: null, top: null, CancellationToken.None);

        result.Messages.Should().BeEmpty();
        result.Count.Should().Be(0);
        _query.Verify(q => q.QueryAsync(AttachmentSet, It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "no visible messages ⇒ no per-row/attachment fan-out (NFR-07)");
    }

    [Fact]
    public async Task ReadThreadAsync_PrivateThreadOpenedPointForward_ReturnsOnlyPostBoundaryMessages()
    {
        // Point-forward-open (task 080 / NFR-06): flipping a private thread to Open must expose ONLY messages
        // from that point forward. The grant boundary is enforced upstream by Dataverse impersonation — the
        // impersonated query below returns ONLY the post-boundary rows; the pre-boundary (pre-open) messages
        // are never present in the impersonated set, mirroring how the private-thread no-leak test above
        // proves total exclusion (empty set) and CommunicationAccessFilterTests proves the internal-only/
        // privilege axis. CommunicationAccessFilter itself does not do date-based filtering — point-forward is
        // a WHICH-ROWS-COME-BACK contract, not a filter-predicate one, so this models it at the query seam.
        var preBoundaryId1 = Guid.NewGuid(); // pre-open messages — must never surface, even implicitly
        var preBoundaryId2 = Guid.NewGuid();
        var postBoundaryId1 = Guid.NewGuid();
        var postBoundaryId2 = Guid.NewGuid();

        SetupMessages(
            MessageRow(postBoundaryId1, body: "opened — welcome to the thread"),
            MessageRow(postBoundaryId2, body: "follow-up after open"));
        SetupAttachments();

        var result = await Sut().ReadThreadAsync(ThreadId, Caller(), since: null, top: null, CancellationToken.None);

        result.Messages.Should().HaveCount(2);
        result.Messages.Select(m => m.MessageId).Should().BeEquivalentTo(new[] { postBoundaryId1, postBoundaryId2 });
        result.Messages.Select(m => m.MessageId).Should().NotContain(new[] { preBoundaryId1, preBoundaryId2 });
    }

    [Fact]
    public async Task ReadThreadAsync_UnresolvedCaller_ThrowsForbiddenAndNeverQueries()
    {
        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("no-matching-systemuser"));

        var sut = new CommunicationThreadReadService(
            _query.Object,
            new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
            _resolver.Object,
            Mock.Of<ILogger<CommunicationThreadReadService>>());

        var act = () => sut.ReadThreadAsync(ThreadId, Caller(), since: null, top: null, CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(403);
        _query.Verify(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "an unresolved caller must never issue a query (fail closed, no app-only fallback)");
    }

    // ─────────────────────────── unread-count ───────────────────────────

    [Fact]
    public async Task GetUnreadCountAsync_CountsReadableMessagesFromImpersonatedSet()
    {
        SetupMessages(
            MessageRow(Guid.NewGuid(), body: "m1"),
            MessageRow(Guid.NewGuid(), body: "m2", privilege: PrivilegePrivileged),
            MessageRow(Guid.NewGuid(), body: "m3"));

        var result = await Sut().GetUnreadCountAsync(ThreadId, Caller(),
            since: DateTimeOffset.Parse("2026-07-16T00:00:00Z"), CancellationToken.None);

        // All three were returned to this (internal) caller by impersonation; privilege doesn't gate.
        result.UnreadCount.Should().Be(3);
        result.Since.Should().Be(DateTimeOffset.Parse("2026-07-16T00:00:00Z"));
    }

    [Fact]
    public async Task GetUnreadCountAsync_MessagesOutsideImpersonatedSet_AreNotCounted()
    {
        // A private message the caller cannot read is simply absent from the impersonated set → never counted.
        SetupMessages(MessageRow(Guid.NewGuid(), body: "only-readable-one"));

        var result = await Sut().GetUnreadCountAsync(ThreadId, Caller(), since: null, CancellationToken.None);

        result.UnreadCount.Should().Be(1);
    }

    [Fact]
    public async Task GetUnreadCountAsync_UnresolvedCaller_ThrowsForbidden()
    {
        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("no-oid-claim"));

        var sut = new CommunicationThreadReadService(
            _query.Object,
            new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
            _resolver.Object,
            Mock.Of<ILogger<CommunicationThreadReadService>>());

        var act = () => sut.GetUnreadCountAsync(ThreadId, Caller(), since: null, CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(403);
    }

    // ─────────────────────────── row builders (OData JSON shape) ───────────────────────────

    private static Dictionary<string, JsonElement> MessageRow(
        Guid id, string? body = null, int? bodyFormat = null, int? type = null,
        string? from = null, string? inReplyTo = null, bool internalOnly = false, int privilege = PrivilegeNone)
    {
        var row = new Dictionary<string, JsonElement>
        {
            ["sprk_communicationid"] = El(id.ToString()),
            ["createdon"] = El("2026-07-16T12:00:00Z"),
            ["sprk_isinternalonly"] = El(internalOnly),
            ["sprk_privilegeclassification"] = El(privilege),
        };
        if (body is not null) row["sprk_body"] = El(body);
        if (bodyFormat is not null) row["sprk_bodyformat"] = El(bodyFormat.Value);
        if (type is not null) row["sprk_communicationtype"] = El(type.Value);
        if (from is not null) row["sprk_from"] = El(from);
        if (inReplyTo is not null) row["sprk_inreplyto"] = El(inReplyTo);
        return row;
    }

    private static Dictionary<string, JsonElement> AttachmentRow(
        Guid attachmentId, Guid messageId, Guid docId, string name, int type) => new()
    {
        ["sprk_communicationattachmentid"] = El(attachmentId.ToString()),
        ["_sprk_communication_value"] = El(messageId.ToString()),
        ["_sprk_document_value"] = El(docId.ToString()),
        ["sprk_name"] = El(name),
        ["sprk_attachmenttype"] = El(type),
    };

    private static JsonElement El<T>(T value) => JsonSerializer.SerializeToElement(value);
}
