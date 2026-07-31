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
using Sprk.Bff.Api.Services.Identity;
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
    private const string ThreadSet = "sprk_communicationthreads";
    private const int PrivilegeNone = 100000000;
    private const int PrivilegePrivileged = 100000002;
    private const int TypeMessage = 100000004;
    private const int DirectionIncoming = 100000000;
    private const int DirectionOutgoing = 100000001;

    private readonly Mock<IImpersonatedCommunicationQuery> _query = new(MockBehavior.Strict);
    private readonly Mock<ICallerSystemUserResolver> _resolver = new();
    // #675 / ISS-006: the read service now depends on the shared identity resolver for the per-caller
    // internal/external bit. The default fixture models an INTERNAL caller (IsExternalAsync ⇒ false) so the
    // pre-fix behavior (every R1/R3 timeline caller is internal) is preserved; the external-caller drop is a
    // dedicated negative test in the privilege/privacy seam suite.
    private readonly Mock<ISystemUserIdentityResolver> _identity = new();

    private CommunicationThreadReadService Sut()
    {
        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));
        _identity
            .Setup(i => i.IsExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        return new CommunicationThreadReadService(
            _query.Object,
            new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
            _resolver.Object,
            _identity.Object,
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

    /// <summary>Stubs the impersonated sprk_communicationthread name projection (R3 task 002 / FR-18). A null
    /// <paramref name="name"/> models an impersonated miss (caller cannot see the thread record → empty set).</summary>
    private void SetupThreadName(string? name) =>
        _query.Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(name is null
                  ? (IReadOnlyList<Dictionary<string, JsonElement>>)Array.Empty<Dictionary<string, JsonElement>>()
                  : new[] { new Dictionary<string, JsonElement> { ["sprk_name"] = El(name) } });

    // ─────────────────────────── thread-read: projection ───────────────────────────

    [Fact]
    public async Task ReadThreadAsync_ImpersonatedRows_ProjectsBodyChannelSenderAndAttachments()
    {
        var messageId = Guid.NewGuid();
        SetupMessages(MessageRow(messageId, body: "hello", bodyFormat: 1, type: TypeMessage,
            from: "alice@x.com", inReplyTo: "root-1", subject: "Docs for review", to: "bob@x.com; carol@x.com"));
        SetupAttachments(AttachmentRow(Guid.NewGuid(), messageId, docId: Guid.NewGuid(), name: "brief.pdf", type: 1));
        SetupThreadName(null);

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
        // FR-04 email-in-flow enrichment: subject + "; "-split recipient To header, projected from the same
        // access-filtered row (never fabricated, never BCC).
        msg.Subject.Should().Be("Docs for review");
        msg.To.Should().BeEquivalentTo(new[] { "bob@x.com", "carol@x.com" });
        msg.Attachments.Should().ContainSingle().Which.FileName.Should().Be("brief.pdf");
    }

    [Fact]
    public async Task ReadThreadAsync_MessageWithNoRecipientsOrSubject_ProjectsEmptyToAndNullSubject()
    {
        // Message-type rows (chat) carry no sprk_to/sprk_subject — must degrade to empty list + null, never throw.
        SetupMessages(MessageRow(Guid.NewGuid(), body: "just a chat", type: TypeMessage));
        SetupAttachments();
        SetupThreadName(null);

        var result = await Sut().ReadThreadAsync(ThreadId, Caller(), since: null, top: null, CancellationToken.None);

        var msg = result.Messages.Single();
        msg.Subject.Should().BeNull();
        msg.To.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadThreadAsync_OutgoingMessageWithSender_ProjectsDirectionAndSenderIdentity()
    {
        // FR-18 acceptance: a visible OUTGOING message sent by user U projects Direction==100000001, SentBy==U,
        // and SentByName from the row's sprk_sentbyname — read from the SAME already-visible, access-filtered row.
        var messageId = Guid.NewGuid();
        var sender = Guid.NewGuid();
        SetupMessages(MessageRow(messageId, body: "sent from me", type: TypeMessage,
            direction: DirectionOutgoing, sentBy: sender, sentByName: "Alice Attorney"));
        SetupAttachments();
        SetupThreadName("Acme v. Roe");

        var result = await Sut().ReadThreadAsync(ThreadId, Caller(), since: null, top: null, CancellationToken.None);

        var msg = result.Messages.Single();
        msg.Direction.Should().Be(DirectionOutgoing);
        msg.SentBy.Should().Be(sender);
        msg.SentByName.Should().Be("Alice Attorney");
    }

    [Fact]
    public async Task ReadThreadAsync_PrivilegedMessage_IsReturnedWithPrivilegeMetadataNotHidden()
    {
        // Privilege NEVER gates a read (ADR-015 / owner 2026-07-16) — it rides along as metadata.
        var messageId = Guid.NewGuid();
        SetupMessages(MessageRow(messageId, body: "sensitive", privilege: PrivilegePrivileged));
        SetupAttachments(); // no attachments
        SetupThreadName(null);

        var result = await Sut().ReadThreadAsync(ThreadId, Caller(), since: null, top: null, CancellationToken.None);

        result.Messages.Should().ContainSingle();
        result.Messages[0].Privilege.Should().Be(PrivilegePrivileged);
    }

    // ─────────────────────────── thread-read: FR-21 privilege/privacy markers ───────────────────────────

    [Fact]
    public async Task ReadThreadAsync_RowWithMarkers_ProjectsInternalOnlyPrivateAndPrivilege()
    {
        // FR-21: the three markers ride the SAME access-filtered row. An INTERNAL caller permitted to see an
        // internal-only + private + privileged message gets all three markers on the DTO — they are display
        // metadata that never gated the read (ADR-015 / access-model decision 2026-07-16).
        var messageId = Guid.NewGuid();
        SetupMessages(MessageRow(messageId, body: "sensitive", internalOnly: true, isPrivate: true,
            privilege: PrivilegePrivileged));
        SetupAttachments();
        SetupThreadName(null);

        var msg = (await Sut().ReadThreadAsync(ThreadId, Caller(), since: null, top: null, CancellationToken.None))
            .Messages.Single();

        msg.IsInternalOnly.Should().BeTrue();
        msg.IsPrivate.Should().BeTrue();
        msg.Privilege.Should().Be(PrivilegePrivileged);
    }

    [Fact]
    public async Task ReadThreadAsync_RowMissingMarkerColumns_DefaultsMarkersToFalseNeverThrows()
    {
        // A VISIBLE row whose marker columns are absent must default to false (a display default) — never throw,
        // never guess "restricted". The internal-only ACCESS gate is the shared filter's fail-closed job, not this
        // display label.
        var row = new Dictionary<string, JsonElement>
        {
            ["sprk_communicationid"] = El(Guid.NewGuid().ToString()),
            ["createdon"] = El("2026-07-16T12:00:00Z"),
            ["sprk_body"] = El("no marker columns present"),
            // sprk_isinternalonly / sprk_isprivate / sprk_privilegeclassification intentionally absent
        };
        SetupMessages(row);
        SetupAttachments();
        SetupThreadName(null);

        var msg = (await Sut().ReadThreadAsync(ThreadId, Caller(), since: null, top: null, CancellationToken.None))
            .Messages.Single();

        msg.IsInternalOnly.Should().BeFalse();
        msg.IsPrivate.Should().BeFalse();
        msg.Privilege.Should().Be(PrivilegeNone);
    }

    [Fact]
    public async Task ReadThreadAsync_Recipients_AreTheProjectedToSet_AndTheReadNeverSelectsBcc()
    {
        // Recipient display = the permitted row's sprk_to, server-access-bound: the row (and thus its recipients)
        // is only returned because it passed impersonation + the shared filter — not a client-inferred/union list.
        // The read MUST NEVER select sprk_bcc — BCC cannot leak by construction (NFR-01).
        string? select = null;
        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => select = odata)
              .ReturnsAsync(new[] { MessageRow(Guid.NewGuid(), to: "bob@x.com; carol@x.com") });
        SetupAttachments();
        SetupThreadName(null);

        var msg = (await Sut().ReadThreadAsync(ThreadId, Caller(), since: null, top: null, CancellationToken.None))
            .Messages.Single();

        msg.To.Should().BeEquivalentTo(new[] { "bob@x.com", "carol@x.com" });
        select.Should().NotBeNull();
        select!.Should().Contain("sprk_to")
            .And.NotContain("sprk_bcc", "BCC must never be selected or returned (NFR-01 over-disclosure)");
    }

    // ─────────────────────────── thread-read: current DTO shape (task 001 characterization baseline) ───────

    [Fact]
    public async Task ReadThreadAsync_SingleThreadRead_PopulatesNameFromThreadRecord()
    {
        // Post-FR-18 contract (was ReadThreadAsync_SingleThreadRead_NameIsAlwaysNull, task 001 baseline): the R3
        // conversation surface renders the thread label inline, so ReadThreadAsync now populates
        // ThreadReadResult.Name from the thread's sprk_name via a single IMPERSONATED projection. This is the
        // intentional flip the 001 baseline existed to surface as a reviewable diff (plan Phase 1 gate / NFR-08).
        SetupMessages(MessageRow(Guid.NewGuid(), body: "hi"));
        SetupAttachments();
        SetupThreadName("Acme v. Roe");

        var result = await Sut().ReadThreadAsync(ThreadId, Caller(), since: null, top: null, CancellationToken.None);

        result.Name.Should().Be("Acme v. Roe");
    }

    [Fact]
    public async Task ReadThreadAsync_ThreadRecordNotVisibleToCaller_LeavesNameNull()
    {
        // Fail closed: the name projection is IMPERSONATED, so a caller who cannot see the thread record gets an
        // empty set back and Name stays null — no existence leak (NFR-06). Messages can still be present (e.g. a
        // point-forward grant) without the label being disclosed.
        SetupMessages(MessageRow(Guid.NewGuid(), body: "hi"));
        SetupAttachments();
        SetupThreadName(null);

        var result = await Sut().ReadThreadAsync(ThreadId, Caller(), since: null, top: null, CancellationToken.None);

        result.Name.Should().BeNull();
    }

    [Fact]
    public void ThreadMessageDto_PostFr18Shape_ExposesDirectionAndSenderIdentityAlongsideFrom()
    {
        // Post-FR-18 contract (was ThreadMessageDto_CurrentShape_ExposesFromWithNoDirectionOrSenderIdentityField,
        // task 001 baseline): the enriched R3 wire shape carries the raw `From` string AND the new
        // Direction/SentBy/SentByName sender-identity fields the bubble UI keys on. This is the intentional
        // extension the 001 baseline existed to surface as a reviewable diff (plan Phase 1 gate / spec NFR-08).
        var propertyNames = typeof(ThreadMessageDto).GetProperties().Select(p => p.Name).ToList();

        propertyNames.Should().Contain(new[] { "From", "Direction", "SentBy", "SentByName" });
    }

    // ─────────────────────────── thread-read: sender-identity enrichment (FR-18) ───────────────────────────

    [Fact]
    public async Task ReadThreadAsync_MultipleVisibleRows_EachDtoCarriesItsOwnSenderIdentity()
    {
        // Per-row sourcing (FR-18): the enriched Direction/SentBy/SentByName are read from EACH row individually —
        // no cross-row bleed. Two visible rows with distinct senders/directions must map 1:1 to their own DTOs.
        var outgoingId = Guid.NewGuid();
        var incomingId = Guid.NewGuid();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        SetupMessages(
            MessageRow(outgoingId, body: "from me", direction: DirectionOutgoing, sentBy: alice, sentByName: "Alice"),
            MessageRow(incomingId, body: "from them", direction: DirectionIncoming, sentBy: bob, sentByName: "Bob"));
        SetupAttachments();
        SetupThreadName("Mixed thread");

        var result = await Sut().ReadThreadAsync(ThreadId, Caller(), since: null, top: null, CancellationToken.None);

        var outgoing = result.Messages.Single(m => m.MessageId == outgoingId);
        outgoing.Direction.Should().Be(DirectionOutgoing);
        outgoing.SentBy.Should().Be(alice);
        outgoing.SentByName.Should().Be("Alice");

        var incoming = result.Messages.Single(m => m.MessageId == incomingId);
        incoming.Direction.Should().Be(DirectionIncoming);
        incoming.SentBy.Should().Be(bob);
        incoming.SentByName.Should().Be("Bob");
    }

    [Fact]
    public async Task ReadThreadAsync_RowExcludedByAccessLayer_ContributesNoSenderIdentityToOutput()
    {
        // No over-disclosure (NFR-01 / FR-18 criterion 3): the enriched Direction/SentBy/SentByName ride the SAME
        // visible-row projection (BuildDto over the impersonated + access-filtered set) as Body/From. A row the
        // caller may not see is absent from that visible set — excluded by Dataverse impersonation, exactly as the
        // private-thread and point-forward no-leak tests model exclusion (the internal-only filter drop for a
        // non-internal caller is covered exhaustively by CommunicationAccessFilterTests; the R1/R3 timeline caller
        // is internal, so exclusion here is the impersonation seam). The service must therefore never fabricate or
        // backfill a sender/name/direction for a row it did not receive — only the visible sender surfaces.
        var visibleId = Guid.NewGuid();
        var visibleSender = Guid.NewGuid();
        var hiddenSender = Guid.NewGuid();

        // The impersonated query returns ONLY the visible row; the hidden internal-only row (hiddenSender /
        // "Hidden Hank") was already excluded upstream and never reaches the projection.
        SetupMessages(MessageRow(visibleId, body: "visible to caller", direction: DirectionOutgoing,
            sentBy: visibleSender, sentByName: "Visible Vera"));
        SetupAttachments();
        SetupThreadName(null);

        var result = await Sut().ReadThreadAsync(ThreadId, Caller(), since: null, top: null, CancellationToken.None);

        result.Messages.Should().ContainSingle();
        result.Messages.Single().SentBy.Should().Be(visibleSender);
        result.Messages.Single().SentByName.Should().Be("Visible Vera");

        // The excluded row contributes NONE of its identity to the output (no over-disclosure).
        result.Messages.Select(m => m.SentBy).Should().NotContain(hiddenSender);
        result.Messages.Select(m => m.SentByName).Should().NotContain("Hidden Hank");
    }

    // ─────────────────────────── thread-read: no-leak (private via impersonation) ───────────────────────────

    [Fact]
    public async Task ReadThreadAsync_CallerCannotSeePrivateThread_ReturnsEmptyAndMakesNoAttachmentQuery()
    {
        // Impersonation is the private-thread enforcement: Dataverse returns ZERO rows to a caller without a grant.
        // The service must faithfully return empty (no app-only fallback) AND skip the attachment query (NFR-07).
        SetupMessages(/* none */);
        SetupThreadName(null);

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
        SetupThreadName("Post-open thread");

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
            _identity.Object,
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
            _identity.Object,
            Mock.Of<ILogger<CommunicationThreadReadService>>());

        var act = () => sut.GetUnreadCountAsync(ThreadId, Caller(), since: null, CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(403);
    }

    // ─────────────────────────── row builders (OData JSON shape) ───────────────────────────

    private static Dictionary<string, JsonElement> MessageRow(
        Guid id, string? body = null, int? bodyFormat = null, int? type = null,
        string? from = null, string? inReplyTo = null, bool internalOnly = false, int privilege = PrivilegeNone,
        int? direction = null, Guid? sentBy = null, string? sentByName = null,
        string? subject = null, string? to = null, bool isPrivate = false)
    {
        var row = new Dictionary<string, JsonElement>
        {
            ["sprk_communicationid"] = El(id.ToString()),
            ["createdon"] = El("2026-07-16T12:00:00Z"),
            ["sprk_isinternalonly"] = El(internalOnly),
            ["sprk_privilegeclassification"] = El(privilege),
            ["sprk_isprivate"] = El(isPrivate),
        };
        if (body is not null) row["sprk_body"] = El(body);
        if (bodyFormat is not null) row["sprk_bodyformat"] = El(bodyFormat.Value);
        if (type is not null) row["sprk_communicationtype"] = El(type.Value);
        if (from is not null) row["sprk_from"] = El(from);
        if (subject is not null) row["sprk_subject"] = El(subject);
        // `to` is stored on sprk_to as a "; "-joined header (see CommunicationService); pass it in that form.
        if (to is not null) row["sprk_to"] = El(to);
        if (inReplyTo is not null) row["sprk_inreplyto"] = El(inReplyTo);
        if (direction is not null) row["sprk_direction"] = El(direction.Value);
        if (sentBy is not null) row["_sprk_sentby_value"] = El(sentBy.Value.ToString());
        // The sender display name rides the _sprk_sentby_value lookup's FormattedValue annotation (what an
        // impersonated annotated query returns) — NOT the denormalized sprk_sentbyname column, which is
        // broken in the env (IsValidODataAttribute=false) and is therefore neither selected nor read by the
        // service (messaging-r3 2026-07-22). ParseMessageRow reads SentByName from THIS annotation.
        if (sentByName is not null) row["_sprk_sentby_value@OData.Community.Display.V1.FormattedValue"] = El(sentByName);
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
