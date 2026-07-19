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
/// Behavior of the R2 by-regarding read (task 010 / FR-01 / NFR-03). Mirrors
/// <see cref="CommunicationThreadReadServiceTests"/>: the service routes BOTH the thread query and the message
/// query through Dataverse IMPERSONATION (the <see cref="IImpersonatedCommunicationQuery"/> seam simulates "the rows
/// Dataverse returned to this caller") and applies the SHARED, REAL <see cref="CommunicationAccessFilter"/> on top —
/// so these tests use the real filter (ADR-038 prefers a real collaborator over a mock) and mock only the two genuine
/// module boundaries: the Dataverse read and the caller resolver. There is NO membership-union / no second access
/// mechanism (retired 2026-07-16) — the SUT structurally depends only on the impersonation seam + the shared filter.
///
/// <para><b>No-leak coverage.</b> Record-level access (private threads, ownership, role depth, BU, sharing) is
/// enforced by impersonation, i.e. by WHICH threads/rows the query returns — proven here by the private-thread
/// negative (a private thread the caller has no grant for is absent from the impersonated thread set, so its messages
/// are never fetched). The internal-only (non-internal caller) axis is the shared filter's job and is asserted here
/// against the REAL filter (the by-regarding service composes it with <c>IsInternalUser = true</c> for R1/R2 internal
/// callers per notes/access-model-decision.md; when external callers arrive in R2/R3 the SAME filter hides
/// internal-only — proven by the filter test below and exhaustively by <see cref="CommunicationAccessFilterTests"/>).</para>
/// </summary>
public class CommunicationByRegardingReadTests
{
    private static readonly Guid RecordId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid CallerSystemUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const string ThreadSet = "sprk_communicationthreads";
    private const string CommunicationSet = "sprk_communications";
    private const string AttachmentSet = "sprk_communicationattachments";
    private const int TypeMessage = 100000004;
    private const int PrivilegeNone = 100000000;

    private readonly Mock<IImpersonatedCommunicationQuery> _query = new();
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

    private void SetupThreads(params Dictionary<string, JsonElement>[] rows) =>
        _query.Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(rows);

    private void SetupMessages(params Dictionary<string, JsonElement>[] rows) =>
        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(rows);

    private void SetupAttachments(params Dictionary<string, JsonElement>[] rows) =>
        _query.Setup(q => q.QueryAsync(AttachmentSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(rows);

    // ─────────────────────────── projection / DTO shape ───────────────────────────

    [Fact]
    public async Task ReadByRegardingAsync_ReturnsThreadsAndMessages_InR1DtoShape()
    {
        var threadId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        SetupThreads(ThreadRow(threadId, "Matter — Acme"));
        SetupMessages(MessageRow(messageId, threadId, body: "hello", bodyFormat: 1, type: TypeMessage,
            from: "alice@x.com", inReplyTo: "root-1"));
        SetupAttachments(AttachmentRow(Guid.NewGuid(), messageId, docId: Guid.NewGuid(), name: "brief.pdf", type: 1));

        var result = await Sut().ReadByRegardingAsync("sprk_matter", RecordId, Caller(), CancellationToken.None);

        result.EntityType.Should().Be("sprk_matter");
        result.RecordId.Should().Be(RecordId);
        result.ThreadCount.Should().Be(1);
        result.MessageCount.Should().Be(1);
        var thread = result.Threads.Single();
        thread.ThreadId.Should().Be(threadId);
        thread.Count.Should().Be(1);
        var msg = thread.Messages.Single();
        msg.MessageId.Should().Be(messageId);
        msg.Body.Should().Be("hello");
        msg.CommunicationType.Should().Be(TypeMessage);
        msg.From.Should().Be("alice@x.com");
        msg.InReplyTo.Should().Be("root-1");
        msg.Attachments.Should().ContainSingle().Which.FileName.Should().Be("brief.pdf");
    }

    [Fact]
    public async Task ReadByRegardingAsync_MultipleThreads_GroupsMessagesByThread()
    {
        var threadA = Guid.NewGuid();
        var threadB = Guid.NewGuid();
        SetupThreads(ThreadRow(threadA, "A"), ThreadRow(threadB, "B"));
        SetupMessages(
            MessageRow(Guid.NewGuid(), threadA, body: "a1"),
            MessageRow(Guid.NewGuid(), threadB, body: "b1"),
            MessageRow(Guid.NewGuid(), threadA, body: "a2"));
        SetupAttachments();

        var result = await Sut().ReadByRegardingAsync("sprk_matter", RecordId, Caller(), CancellationToken.None);

        result.ThreadCount.Should().Be(2);
        result.MessageCount.Should().Be(3);
        result.Threads.Single(t => t.ThreadId == threadA).Count.Should().Be(2);
        result.Threads.Single(t => t.ThreadId == threadB).Count.Should().Be(1);
    }

    // ─────────────────────────── entity-set-agnostic (≥3 of the 11 families) ───────────────────────────

    [Theory]
    [InlineData("sprk_matter", "_sprk_regardingmatter_value")]
    [InlineData("contact", "_sprk_regardingperson_value")]     // non-sprk_-prefixed; lookup is regardingperson
    [InlineData("account", "_sprk_regardingaccount_value")]    // non-sprk_-prefixed; lookup is regardingaccount
    [InlineData("sprk_project", "_sprk_regardingproject_value")]
    public async Task ReadByRegardingAsync_ResolvesTypedRegardingLookupPerEntityType_AndBehavesIdentically(
        string entityType, string expectedLookupValue)
    {
        var threadId = Guid.NewGuid();
        string? threadQuery = null;
        _query.Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => threadQuery = odata)
              .ReturnsAsync(new[] { ThreadRow(threadId, "t") });
        SetupMessages(MessageRow(Guid.NewGuid(), threadId, body: "m"));
        SetupAttachments();

        var result = await Sut().ReadByRegardingAsync(entityType, RecordId, Caller(), CancellationToken.None);

        // The ONLY per-family difference is which typed thread-regarding lookup is filtered on.
        threadQuery.Should().Contain($"{expectedLookupValue} eq {RecordId}");
        // Behavior is identical across families: same DTO shape, one thread, one message.
        result.ThreadCount.Should().Be(1);
        result.MessageCount.Should().Be(1);
    }

    [Fact]
    public async Task ReadByRegardingAsync_UnknownEntityType_ThrowsBadRequestAndNeverQueries()
    {
        var act = () => Sut().ReadByRegardingAsync("sprk_notathing", RecordId, Caller(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(400);
        _query.Verify(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "a bad entity type is rejected before any impersonated query is issued");
    }

    // ─────────────────────────── no-leak: private thread hidden (via impersonation) ───────────────────────────

    [Fact]
    public async Task ReadByRegardingAsync_PrivateThreadWithoutGrant_IsAbsentAndItsMessagesNeverFetched()
    {
        // Impersonation is the private-thread enforcement: Dataverse returns to the caller ONLY the threads they may
        // see. The private thread the caller has no grant for is simply absent from the impersonated thread set, so
        // the OR-filter that fetches messages never even references it — its messages can never surface (NFR-03).
        var visibleThread = Guid.NewGuid();
        var privateThread = Guid.NewGuid(); // NOT returned by the impersonated thread query
        string? messageQuery = null;

        SetupThreads(ThreadRow(visibleThread, "open"));
        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => messageQuery = odata)
              .ReturnsAsync(new[] { MessageRow(Guid.NewGuid(), visibleThread, body: "only-visible") });
        SetupAttachments();

        var result = await Sut().ReadByRegardingAsync("sprk_matter", RecordId, Caller(), CancellationToken.None);

        result.Threads.Select(t => t.ThreadId).Should().ContainSingle().Which.Should().Be(visibleThread);
        result.Threads.Select(t => t.ThreadId).Should().NotContain(privateThread);
        messageQuery.Should().NotContain(privateThread.ToString(), "a hidden thread is never referenced by the message fetch");
    }

    [Fact]
    public async Task ReadByRegardingAsync_NoVisibleThreads_ReturnsEmptyAndNeverQueriesMessages()
    {
        SetupThreads(/* none */);

        var result = await Sut().ReadByRegardingAsync("sprk_matter", RecordId, Caller(), CancellationToken.None);

        result.ThreadCount.Should().Be(0);
        result.MessageCount.Should().Be(0);
        _query.Verify(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "no visible threads ⇒ no message fan-out");
    }

    [Fact]
    public async Task ReadByRegardingAsync_UnresolvedCaller_ThrowsForbiddenAndNeverQueries()
    {
        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("no-matching-systemuser"));

        var sut = new CommunicationThreadReadService(
            _query.Object,
            new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
            _resolver.Object,
            Mock.Of<ILogger<CommunicationThreadReadService>>());

        var act = () => sut.ReadByRegardingAsync("sprk_matter", RecordId, Caller(), CancellationToken.None);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(403);
        _query.Verify(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "an unresolved caller must never issue a query (fail closed, no app-only fallback)");
    }

    // ─────────────────────────── no-leak: internal-only (the shared 2-rule filter the by-regarding path composes) ───

    [Fact]
    public void CommunicationAccessFilter_InternalOnlyMessage_HiddenFromNonInternalCaller()
    {
        // The by-regarding read composes the SAME CommunicationAccessFilter the R1 thread-read composes. For an
        // internal R1/R2 caller nothing is hidden; the security-critical rule is that an internal-only message is
        // hidden from a NON-internal caller. Proven here directly against the REAL filter the by-regarding path uses
        // (external callers arrive in R2/R3) so the no-leak contract is anchored where it lives — not duplicated as a
        // service mirror. Exhaustive filter coverage is in CommunicationAccessFilterTests.
        var filter = new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>());
        var nonInternal = new CommunicationAccessContext(CallerSystemUserId, IsInternalUser: false);

        var internalOnly = new Entity("sprk_communication", Guid.NewGuid())
        {
            ["sprk_isinternalonly"] = true,
            ["sprk_privilegeclassification"] = new OptionSetValue(PrivilegeNone),
        };

        filter.EvaluateMessage(nonInternal, internalOnly).IsVisible.Should().BeFalse();
    }

    // ─────────────────────────── row builders (OData JSON shape) ───────────────────────────

    private static Dictionary<string, JsonElement> ThreadRow(Guid id, string name) => new()
    {
        ["sprk_communicationthreadid"] = El(id.ToString()),
        ["sprk_name"] = El(name),
        ["createdon"] = El("2026-07-19T12:00:00Z"),
    };

    private static Dictionary<string, JsonElement> MessageRow(
        Guid id, Guid threadId, string? body = null, int? bodyFormat = null, int? type = null,
        string? from = null, string? inReplyTo = null, bool internalOnly = false, int privilege = PrivilegeNone)
    {
        var row = new Dictionary<string, JsonElement>
        {
            ["sprk_communicationid"] = El(id.ToString()),
            ["_sprk_communicationthread_value"] = El(threadId.ToString()),
            ["createdon"] = El("2026-07-19T12:00:00Z"),
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
