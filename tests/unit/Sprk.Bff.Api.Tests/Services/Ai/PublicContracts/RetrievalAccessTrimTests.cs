using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PublicContracts;

/// <summary>
/// The access trim's own contract (unified-access-control-r2 task 176, #1511): the rules the owner set for the batch
/// read: ids canonicalized (ADR-044), at most 20 ids per read, rows without an id dropped, an answer naming an id that
/// was not asked never widens the result, and a caller who is not the request principal gets nothing. The callers'
/// behaviour is in <c>tests/integration/regression/Ai/Issue1511_AiRetrievalAccessTrimTests.cs</c>.
/// Task 188 round 3 (F1-A): an external caller's document keys also pass THE external document narrowing, decided over a
/// seeded APP-ONLY world (<see cref="AppOnlyDocumentWorld"/>: the real predicate behind the real app-only seam).
/// </summary>
public sealed class RetrievalAccessTrimTests
{
    private const string CallerOid = "11111111-2222-3333-4444-555555555555";

    private sealed record Row(string Name, string? DocumentId);

    private static (RetrievalAccessTrim Trim, ReadableDocumentsUserClient Client) Create(
        string? requestOid = CallerOid, params Guid[] readable)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = requestOid is null ? null : TestHttpContexts.Authenticated(requestOid),
        };
        var client = new ReadableDocumentsUserClient(readable);
        var internalCaller = new NarrowingWorld(external: false);
        return (new RetrievalAccessTrim(
            accessor, client, internalCaller.Caller.Object, internalCaller.Identity.Object, DocumentNarrowingReads.NeverCalled,
            NullLogger<RetrievalAccessTrim>.Instance), client);
    }

    [Fact]
    public async Task Ids_AreCanonicalized_SoBracedUppercaseRowsMatchTheLowercaseAnswer()
    {
        var doc = Guid.NewGuid();
        var (trim, client) = Create(readable: doc);
        var rows = new[] { new Row("a", "{" + doc.ToString().ToUpperInvariant() + "}") };

        var result = await trim.TrimAsync(rows, r => r.DocumentId, CallerOid);

        result.Rows.Should().ContainSingle().Which.Name.Should().Be("a");
        client.Reads.Single().Should().ContainSingle().Which.Should().Be(doc.ToString("D"),
            because: "ADR-044: the filter carries the bare lowercase form, whatever the index row spelled");
    }

    [Fact]
    public async Task MoreThanTwentyDistinctIds_AreReadInChunksOfTwenty_AndOrderIsKept()
    {
        var docs = Enumerable.Range(0, 45).Select(_ => Guid.NewGuid()).ToArray();
        var readable = docs.Where((_, i) => i % 2 == 0).ToArray();
        var (trim, client) = Create(readable: readable);
        var rows = docs.Select((d, i) => new Row($"r{i}", d.ToString())).ToList();

        var result = await trim.TrimAsync(rows, r => r.DocumentId, CallerOid);

        client.Reads.Select(r => r.Count).Should().Equal(20, 20, 5);
        result.Rows.Select(r => r.Name).Should().Equal(rows.Where((_, i) => i % 2 == 0).Select(r => r.Name));
        result.DroppedCount.Should().Be(22);
    }

    [Fact]
    public async Task RowsWithoutAUsableDocumentId_AreDropped()
    {
        var doc = Guid.NewGuid();
        var (trim, _) = Create(readable: doc);
        var rows = new[]
        {
            new Row("kept", doc.ToString()),
            new Row("null", null),
            new Row("not-a-guid", "chunk-17"),
            new Row("empty-guid", Guid.Empty.ToString()),
        };

        var result = await trim.TrimAsync(rows, r => r.DocumentId, CallerOid);

        result.Rows.Select(r => r.Name).Should().Equal("kept");
    }

    [Fact]
    public async Task AnAnswerNamingAnIdThatWasNotAsked_DoesNotWidenTheResult()
    {
        var asked = Guid.NewGuid();
        var other = Guid.NewGuid();
        var (trim, client) = Create();
        client.RawBody = $"{{\"value\":[{{\"sprk_documentid\":\"{other}\"}}]}}";

        var result = await trim.TrimAsync(new[] { new Row("asked", asked.ToString()) }, r => r.DocumentId, CallerOid);

        result.Rows.Should().BeEmpty();
        result.Outcome.Should().Be(RetrievalTrimOutcome.Evaluated);
    }

    [Fact]
    public async Task ADeclaredCallerWhoIsNotTheRequestPrincipal_GetsNothing_AndNoReadIsMade()
    {
        var doc = Guid.NewGuid();
        var (trim, client) = Create(requestOid: "99999999-9999-9999-9999-999999999999", readable: doc);

        var result = await trim.TrimAsync(new[] { new Row("a", doc.ToString()) }, r => r.DocumentId, CallerOid);

        result.Rows.Should().BeEmpty();
        result.Outcome.Should().Be(RetrievalTrimOutcome.NoVerifiedCaller);
        client.Reads.Should().BeEmpty();
        trim.CanEvaluate(CallerOid).Should().BeFalse();
    }

    [Fact]
    public async Task AMalformedAnswer_WithholdsEverything()
    {
        var doc = Guid.NewGuid();
        var (trim, client) = Create(readable: doc);
        client.RawBody = "{\"unexpected\":true}";

        var result = await trim.TrimAsync(new[] { new Row("a", doc.ToString()) }, r => r.DocumentId, CallerOid);

        result.Rows.Should().BeEmpty();
        result.Outcome.Should().Be(RetrievalTrimOutcome.CheckFailed);
    }

    // ── Task 188 round 3 (F1-A): an external caller's document keys pass THE external document narrowing ─────────────

    private static readonly Guid CallerSystemUserId = Guid.Parse("0a0a0a0a-0000-0000-0000-0000000001a8");
    private const int Restricted = 100000002;
    private const int OpenThread = 100000000;

    public enum HiddenBy { InternalOnlyCommunication, OwnRestricted, RestrictedParent }

    public enum KeyKind { DocumentId, DriveItem }

    public enum LookupFault { ResolverLookupFailed, ExternalFlagReadThrows }

    private sealed record Chunk(string Name, RetrievalRecordKey Key);

    /// <summary>
    /// The caller's own (OBO) read, the externality lookup, and the APP-ONLY world the narrowing reads. Every document id
    /// is a fresh GUID, so a row's fate depends only on what the test seeds.
    /// </summary>
    private sealed class NarrowingWorld
    {
        public ReadableDocumentsUserClient User { get; } = new();
        public AppOnlyDocumentWorld AppOnly { get; } = new();
        public Mock<ICallerSystemUserResolver> Caller { get; } = new();
        public Mock<ISystemUserIdentityResolver> Identity { get; } = new();
        public List<string> Logs { get; } = new();

        public NarrowingWorld(bool external)
        {
            Caller.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));
            Identity.Setup(r => r.IsExternalAsync(CallerSystemUserId, It.IsAny<CancellationToken>())).ReturnsAsync(external);
        }

        public RetrievalAccessTrim Trim() => new(
            new HttpContextAccessor { HttpContext = TestHttpContexts.Authenticated(CallerOid) },
            User, Caller.Object, Identity.Object, AppOnly.Narrowing, new ListLogger(Logs));

        /// <summary>A document the app-only world hides for <paramref name="hiddenBy"/>.</summary>
        public Guid HiddenDocument(HiddenBy hiddenBy)
        {
            var id = Guid.NewGuid();
            switch (hiddenBy)
            {
                case HiddenBy.InternalOnlyCommunication:
                    var thread = Guid.NewGuid();
                    var communication = Guid.NewGuid();
                    AppOnly.Thread(thread, OpenThread);
                    AppOnly.Communication(communication, internalOnly: true, thread);
                    AppOnly.Document(id, communication: communication);
                    break;
                case HiddenBy.OwnRestricted:
                    AppOnly.Document(id, permission: Restricted);
                    break;
                case HiddenBy.RestrictedParent:
                    var parent = AppOnly.Document(Guid.NewGuid(), permission: Restricted);
                    AppOnly.Document(id, parent: parent);
                    break;
            }

            return id;
        }

        /// <summary>A document the app-only world shows: Standard, under a shareable communication on an Open thread.</summary>
        public Guid VisibleDocument()
        {
            var thread = Guid.NewGuid();
            var communication = Guid.NewGuid();
            AppOnly.Thread(thread, OpenThread);
            AppOnly.Communication(communication, internalOnly: false, thread);
            return AppOnly.Document(Guid.NewGuid(), communication: communication);
        }

        /// <summary>The chunk's key for <paramref name="kind"/>, with the caller holding Read on <paramref name="document"/>.</summary>
        public RetrievalRecordKey Readable(Guid document, KeyKind kind)
        {
            if (kind == KeyKind.DocumentId)
            {
                User.Allow("sprk_documents", document.ToString("D"));
                return RetrievalRecordKey.Document(document.ToString())!;
            }

            var item = $"01ITEM{document:N}";
            User.AllowDocumentOnDriveItem(item, document);
            return RetrievalRecordKey.DocumentByDriveItem(item)!;
        }
    }

    private sealed class ListLogger(List<string> sink) : ILogger<RetrievalAccessTrim>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Add(formatter(state, exception));
    }

    private static Task<RetrievalTrimResult<Chunk>> TrimAsync(NarrowingWorld world, params Chunk[] chunks) =>
        world.Trim().TrimByRecordAsync(chunks, c => c.Key, CallerOid);

    [Theory]
    [InlineData(HiddenBy.InternalOnlyCommunication, KeyKind.DocumentId)]
    [InlineData(HiddenBy.InternalOnlyCommunication, KeyKind.DriveItem)]
    [InlineData(HiddenBy.OwnRestricted, KeyKind.DocumentId)]
    [InlineData(HiddenBy.OwnRestricted, KeyKind.DriveItem)]
    [InlineData(HiddenBy.RestrictedParent, KeyKind.DocumentId)]
    [InlineData(HiddenBy.RestrictedParent, KeyKind.DriveItem)]
    public async Task AnExternalCaller_WithReadOnAHiddenDocument_GetsNoChunkOfIt(HiddenBy hiddenBy, KeyKind kind)
    {
        var world = new NarrowingWorld(external: true);
        var hidden = world.HiddenDocument(hiddenBy);
        var visible = world.VisibleDocument();

        var result = await TrimAsync(world,
            new Chunk("hidden", world.Readable(hidden, kind)), new Chunk("visible", world.Readable(visible, kind)));

        result.Rows.Select(r => r.Name).Should().Equal(new[] { "visible" },
            because: "Dataverse Read is not enough for an external caller: THE external document narrowing hides it");
        result.Outcome.Should().Be(RetrievalTrimOutcome.Evaluated);
        result.DroppedCount.Should().Be(1);
        world.AppOnly.Calls.Should().Be(1, because: "one narrowing call per page");
        world.Logs.Should().NotContain(l => l.Contains(hidden.ToString("D")) || l.Contains(visible.ToString("D")),
            because: "ADR-015: counts and reason codes only");
        world.Logs.Should().Contain(l => l.Contains("withheld by the external document narrowing"));
    }

    [Theory]
    [InlineData(LookupFault.ResolverLookupFailed, KeyKind.DocumentId)]
    [InlineData(LookupFault.ResolverLookupFailed, KeyKind.DriveItem)]
    [InlineData(LookupFault.ExternalFlagReadThrows, KeyKind.DocumentId)]
    [InlineData(LookupFault.ExternalFlagReadThrows, KeyKind.DriveItem)]
    public async Task AFailedExternalityLookup_AppliesTheNarrowing_ItDoesNotRefuseEverything(LookupFault fault, KeyKind kind)
    {
        // The caller is internal; only the lookup that would say so fails.
        var world = new NarrowingWorld(external: false);
        if (fault == LookupFault.ResolverLookupFailed)
        {
            world.Caller.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CallerSystemUserResolution.Unresolved(ExternalCallerCommunicationGuard.LookupFailedReason));
        }
        else
        {
            world.Identity.Setup(r => r.IsExternalAsync(CallerSystemUserId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("simulated Dataverse fault"));
        }

        var hidden = world.HiddenDocument(HiddenBy.InternalOnlyCommunication);
        var visible = world.VisibleDocument();

        var result = await TrimAsync(world,
            new Chunk("hidden", world.Readable(hidden, kind)), new Chunk("visible", world.Readable(visible, kind)));

        result.Rows.Select(r => r.Name).Should().Equal(new[] { "visible" },
            because: "a lookup fault counts as external (fail closed), and costs only the documents the narrowing hides");
        result.Outcome.Should().Be(RetrievalTrimOutcome.Evaluated, because: "a lookup fault is not a refusal");
        world.Logs.Should().Contain(l => l.Contains("external flag could not be read"));
    }

    [Theory]
    [InlineData(KeyKind.DocumentId)]
    [InlineData(KeyKind.DriveItem)]
    public async Task AnInternalUser_ResultsAreUnchanged_AndNothingIsNarrowed(KeyKind kind)
    {
        var world = new NarrowingWorld(external: false);
        var hidden = world.HiddenDocument(HiddenBy.InternalOnlyCommunication);
        var visible = world.VisibleDocument();

        var result = await TrimAsync(world,
            new Chunk("hidden", world.Readable(hidden, kind)), new Chunk("visible", world.Readable(visible, kind)));

        result.Rows.Select(r => r.Name).Should().Equal("hidden", "visible");
        world.AppOnly.Calls.Should().Be(0, because: "an internal systemuser is not narrowed");
        world.AppOnly.AllReads.Should().BeEmpty();
    }

    [Theory]
    [InlineData(KeyKind.DocumentId)]
    [InlineData(KeyKind.DriveItem)]
    public async Task AnExternalCaller_OnAVisibleDocument_KeepsIt(KeyKind kind)
    {
        var world = new NarrowingWorld(external: true);
        var visible = world.VisibleDocument();
        var plain = world.AppOnly.Document(Guid.NewGuid());

        var result = await TrimAsync(world,
            new Chunk("visible", world.Readable(visible, kind)), new Chunk("plain", world.Readable(plain, kind)));

        result.Rows.Select(r => r.Name).Should().Equal("visible", "plain");
        world.AppOnly.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ADriveItem_WithOneHiddenAndOneVisibleReadableRow_Passes()
    {
        var world = new NarrowingWorld(external: true);
        var hidden = world.HiddenDocument(HiddenBy.OwnRestricted);
        var visible = world.VisibleDocument();
        world.User.AllowDocumentOnDriveItem("01ITEM-SHARED", hidden).AllowDocumentOnDriveItem("01ITEM-SHARED", visible);

        var result = await TrimAsync(world, new Chunk("item", RetrievalRecordKey.DocumentByDriveItem("01ITEM-SHARED")!));

        result.Rows.Select(r => r.Name).Should().Equal(new[] { "item" },
            because: "the file the quote came from is reachable through a row the caller can read and the narrowing shows");
        world.User.Selects.Should().ContainSingle().Which.Should().Be("sprk_driveitemid,sprk_documentid",
            because: "the caller's own read names the document rows on the item");
    }

    [Fact]
    public async Task ADriveItem_WhoseOnlyVisibleRowTheCallerCannotRead_IsWithheld()
    {
        var world = new NarrowingWorld(external: true);
        var hidden = world.HiddenDocument(HiddenBy.RestrictedParent);
        var visibleButUnreadable = world.VisibleDocument();
        world.User.AllowDocumentOnDriveItem("01ITEM-MIXED", hidden);

        var result = await TrimAsync(world, new Chunk("item", RetrievalRecordKey.DocumentByDriveItem("01ITEM-MIXED")!));

        result.Rows.Should().BeEmpty(because: "only rows the caller's own read returned can vouch for the item");
        world.AppOnly.Reads("sprk_documents").Should().NotContain(r => r.Contains(visibleButUnreadable.ToString("D")));
    }

    [Fact]
    public async Task ADriveItemRowWithoutADocumentId_IsWithheld_ForAnExternalCaller()
    {
        var world = new NarrowingWorld(external: true);
        world.User.Allow("sprk_documents", "01ITEM-NO-ID");

        var result = await TrimAsync(world, new Chunk("item", RetrievalRecordKey.DocumentByDriveItem("01ITEM-NO-ID")!));

        result.Rows.Should().BeEmpty(because: "a row the narrowing cannot name cannot be shown (fail closed)");
        world.AppOnly.Calls.Should().Be(0);
    }

    [Fact(DisplayName = "Task 188 r4b pin: a drive-item row with no document id stays withheld on a page that DOES make a narrowing call")]
    public async Task ADriveItemRowWithoutADocumentId_IsWithheld_EvenWhenThePageNarrowsAVisibleDocument()
    {
        var world = new NarrowingWorld(external: true);
        world.User.Allow("sprk_documents", "01ITEM-NO-ID");
        var visible = world.VisibleDocument();

        var result = await TrimAsync(world,
            new Chunk("visible", world.Readable(visible, KeyKind.DocumentId)),
            new Chunk("item", RetrievalRecordKey.DocumentByDriveItem("01ITEM-NO-ID")!));

        result.Rows.Should().ContainSingle(because: "the item's row names no document, so the narrowing cannot show it");
        world.AppOnly.Calls.Should().Be(1, "the visible document makes the page's one narrowing call");
    }

    [Theory]
    [InlineData(KeyKind.DocumentId)]
    [InlineData(KeyKind.DriveItem)]
    public async Task AFailedNarrowing_WithholdsTheDocumentKeys_ParentRecordsStay(KeyKind kind)
    {
        var world = new NarrowingWorld(external: true);
        var visible = world.VisibleDocument();
        var matter = Guid.NewGuid();
        world.User.Allow("sprk_matters", matter.ToString("D"));
        world.AppOnly.Faults["sprk_documents"] = HttpStatusCode.InternalServerError;

        var result = await TrimAsync(world,
            new Chunk("document", world.Readable(visible, kind)),
            new Chunk("matter", RetrievalRecordKey.ParentRecord("matter", matter.ToString())!));

        result.Rows.Select(r => r.Name).Should().Equal(new[] { "matter" },
            because: "a narrowing that cannot answer shows no document (fail closed); a parent record is not document content");
        result.Outcome.Should().Be(RetrievalTrimOutcome.Evaluated);
        world.Logs.Should().Contain(l => l.Contains("external document narrowing failed"));
    }

    [Fact]
    public async Task AnExternalCaller_ParentRecordKeys_AreNotNarrowed_AndNothingIsLookedUp()
    {
        var world = new NarrowingWorld(external: true);
        var matter = Guid.NewGuid();
        world.User.Allow("sprk_matters", matter.ToString("D"));

        var result = await TrimAsync(world, new Chunk("matter", RetrievalRecordKey.ParentRecord("matter", matter.ToString())!));

        result.Rows.Select(r => r.Name).Should().Equal("matter");
        world.Caller.Verify(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()), Times.Never);
        world.AppOnly.Calls.Should().Be(0);
    }
}
