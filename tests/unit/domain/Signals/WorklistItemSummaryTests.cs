using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.DecisionCapabilityFixtures;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// spaarkeai-mcp-bff-api-r1 task 015 (B-5; spec FR-11, FR-05): the slim Decide-lane row and the paging cursor's contract, with no
/// Dataverse. The paging itself, over the real worklist read and 200 seeded items, is
/// <c>tests/integration/regression/Signals/DecideLanePagingTests.cs</c>.
/// </summary>
/// <remarks>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>): the 600-byte bound is a contract of the MCP tools, not an implementation detail.</remarks>
public sealed class WorklistItemSummaryTests
{
    private static readonly Guid Id = Guid.Parse("11111111-2222-4333-8444-555555555555");

    /// <summary>The longest each stored text can be (sprk_signal: name 200, regarding name 400; sprk_policy short name, 100 assumed).</summary>
    private const int TitleMax = 200;
    private const int SubjectMax = 400;
    private const int ShortNameMax = 100;

    /// <summary>A Decide-lane row with every member filled at its longest: a billing item (both invoice ids), the longest work type and
    /// status labels, a flagged Signal, evidence and sentence present on the full row.</summary>
    private static WorklistItemRow FullRow(string filler = "x") =>
        new(Id, "Decide", "Acknowledged", Repeat(filler, TitleMax), Repeat(filler, SubjectMax), Repeat(filler, ShortNameMax),
            "approveOrRebudget", "overdue", DateTimeOffset.Parse("2026-11-07T12:34:56.1234567+00:00"), "2026-11-07",
            EvidenceRefs: Repeat("e", 4000), Guid.NewGuid(), Sentence: Repeat("s", 2000),
            InvoiceReview: new WorklistInvoiceReview(Guid.NewGuid(), Guid.NewGuid()));

    private static string Repeat(string piece, int length) => string.Concat(Enumerable.Repeat(piece, length));

    /// <summary>The group's core record, its name and number at their longest (sprk_matter: name 400, number 100 assumed).</summary>
    private static WorklistCoreRecord FullCore(string filler = "x") =>
        new("sprk_matter", Guid.NewGuid(), "Matter", Repeat(filler, 400), Repeat(filler, 100));

    private static int JsonBytes(object value, JsonSerializerOptions options) =>
        Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(value, options));

    private static readonly JsonSerializerOptions[] HostOptions =
    [
        new JsonSerializerOptions(JsonSerializerDefaults.Web),
        new JsonSerializerOptions(),
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never },
    ];

    // The three serializer settings a host may use; the bound holds for all (nulls are never written, names are the same length).
    public static TheoryData<string> Fillers => new() { "x", "\"", "€", "\U0001F4C4" };

    [Theory]
    [MemberData(nameof(Fillers))]
    public void ASummaryAtTheLongestStoredText_WithItsCoreRecordId_IsAtMost600Bytes_AsJson_InEveryHostSerializer(string filler)
    {
        var core = FullCore(filler);
        var summary = WorklistItemSummary.From(FullRow(filler), core, privilegeFlagged: true);

        summary.CoreRecordId.Should().Be(core.RecordId, "the worst case carries the core record id");
        foreach (var options in HostOptions)
        {
            JsonBytes(summary, options).Should().BeLessThanOrEqualTo(WorklistItemSummary.MaxJsonBytes);
        }
    }

    [Fact]
    public void ASummary_KeepsInvoiceReviewWithBothIds_CarriesItsCoreRecordId_AndThePrivilegeFlag()
    {
        var row = FullRow();
        var core = FullCore();
        var json = JsonSerializer.SerializeToElement(WorklistItemSummary.From(row, core, privilegeFlagged: true), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.GetProperty("invoiceReview").GetProperty("invoiceId").GetGuid().Should().Be(row.InvoiceReview!.InvoiceId);
        json.GetProperty("invoiceReview").GetProperty("lineId").GetGuid().Should().Be(row.InvoiceReview.LineId!.Value);
        json.GetProperty("coreRecordId").GetGuid().Should().Be(core.RecordId);
        json.GetProperty("privilegeFlagged").GetBoolean().Should().BeTrue();
        json.TryGetProperty("coreName", out _).Should().BeFalse("the name is on the page once per group, not on every row");
        WorklistItemSummary.From(row, core, privilegeFlagged: false).PrivilegeFlagged.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Fillers))]
    public void APageAtItsLongest_50WorstRows_50WorstCoreEntries_AndAFullCursor_IsUnder64KiB(string filler)
    {
        // NFR-02: every page result fits Cowork's 64 KiB inline cap. Worst case: 50 rows of 50 different core records, every text at its
        // longest, and a cursor remembering the most groups it can.
        var cores = Enumerable.Range(0, DecideLanePaging.PageSize).Select(_ => FullCore(filler)).ToList();
        var items = cores.Select(c => WorklistItemSummary.From(FullRow(filler), c, privilegeFlagged: true)).ToList();
        var map = cores.ToDictionary(c => c.RecordId, WorklistCoreSummary.From);
        var cursor = (SomeCursor() with { Returned = Enumerable.Range(0, DecideCursor.MaxReturnedGroups).Select(_ => Guid.NewGuid()).ToList() }).Encode();
        var page = DecidePageResult.Ok(items, map, cursor);

        foreach (var options in HostOptions)
        {
            JsonBytes(new { page.Items, page.Cores, page.NextCursor }, options).Should().BeLessThan(64 * 1024);
            foreach (var entry in map.Values)
            {
                JsonEncodedBytes(entry.Name!).Should().BeLessThanOrEqualTo(WorklistCoreSummary.NameBytes);
                JsonEncodedBytes(entry.Number!).Should().BeLessThanOrEqualTo(WorklistCoreSummary.NumberBytes);
            }
        }
    }

    [Fact]
    public void ASummary_HasNoEvidenceSentenceOrDecisionRecord_AndNothingBeyondTheRowsDecideLaneMembers()
    {
        // FR-11: the row without EvidenceRefs (and Steps, which live in the Decision Record). The members a Decide-lane row never carries are left out too.
        var summaryMembers = typeof(WorklistItemSummary).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType == typeof(WorklistItemSummary) && p.Name != "EqualityContract").Select(p => p.Name).ToList();
        var rowMembers = typeof(WorklistItemRow).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType == typeof(WorklistItemRow) && p.Name != "EqualityContract").Select(p => p.Name).ToList();

        summaryMembers.Should().NotContain(["EvidenceRefs", "Sentence", "Decision", "Steps"]);
        summaryMembers.Except(rowMembers).Should().BeEquivalentTo(["CoreRecordId", "PrivilegeFlagged"],
            "the only members the row does not have are its group's core record id and the privilege flag");
        rowMembers.Should().NotContain("CoreRecordId", "the Console's REST row gains no member");
        rowMembers.Should().NotContain("PrivilegeFlagged", "the Console's REST response gains no member");
    }

    [Fact]
    public void ClippedText_EndsInAnEllipsis_AndShortTextIsUnchanged()
    {
        var summary = WorklistItemSummary.From(FullRow(), FullCore(), false);
        summary.Title.Should().EndWith("…").And.HaveLength(WorklistItemSummary.TitleBytes - 5, "an ellipsis escapes to six bytes");

        var short1 = FullRow() with { Title = "Fee change", SubjectName = "RE: revised fees", RuleShortName = "Budget gap" };
        var kept = WorklistItemSummary.From(short1, FullCore(), false);
        (kept.Title, kept.SubjectName, kept.RuleShortName).Should().Be(("Fee change", "RE: revised fees", "Budget gap"));
        WorklistItemSummary.From(FullRow() with { Title = null }, FullCore(), false).Title.Should().BeNull();
    }

    [Fact]
    public void ACoreEntry_ClipsTheNameAndNumber_AndKeepsShortOnesUnchanged()
    {
        var clipped = WorklistCoreSummary.From(FullCore());
        clipped.Name.Should().EndWith("…").And.HaveLength(WorklistCoreSummary.NameBytes - 5);
        clipped.Number.Should().EndWith("…").And.HaveLength(WorklistCoreSummary.NumberBytes - 5);

        WorklistCoreSummary.From(new WorklistCoreRecord("sprk_matter", Guid.NewGuid(), "Matter", "Acme v. Widget", "MAT-0042"))
            .Should().Be(new WorklistCoreSummary("Acme v. Widget", "MAT-0042"));
    }

    [Fact]
    public void Clip_NeverSplitsASurrogatePair()
    {
        var clipped = WorklistItemSummary.Clip(Repeat("\U0001F4C4", 50), 40)!;

        clipped.Should().EndWith("…");
        for (var i = 0; i < clipped.Length; i++)
        {
            if (char.IsHighSurrogate(clipped[i])) char.IsLowSurrogate(clipped[i + 1]).Should().BeTrue();
        }

        JsonEncodedBytes(clipped).Should().BeLessThanOrEqualTo(40);
    }

    private static int JsonEncodedBytes(string text) => JsonEncodedText.Encode(text).EncodedUtf8Bytes.Length;

    // ── The cursor's wire contract ─────────────────────────────────────────────────────────────────────────────────

    private static DecideCursor SomeCursor() => new(
        new RankKey(1, true, DateTimeOffset.Parse("2026-10-01T09:00:00Z")),
        Guid.NewGuid(),
        new RankKey(2, false, null),
        Guid.NewGuid(),
        [Guid.NewGuid(), Guid.NewGuid()]);

    [Fact]
    public void ACursor_RoundTrips_AndIsOpaqueBase64Url()
    {
        var cursor = SomeCursor();
        var wire = cursor.Encode();

        wire.Should().MatchRegex("^[A-Za-z0-9_-]+$");
        DecideCursor.TryDecode(wire, out var decoded).Should().BeTrue();
        decoded.Should().BeEquivalentTo(cursor);
    }

    [Fact]
    public void ACursorForAGroupWithNoCore_RoundTrips()
    {
        var cursor = SomeCursor() with { GroupCoreId = null, Returned = [] };

        DecideCursor.TryDecode(cursor.Encode(), out var decoded).Should().BeTrue();
        decoded.GroupCoreId.Should().BeNull();
    }

    public static TheoryData<string?> BadCursors => new()
    {
        "not base64url !!",
        "AAAA",
        "e30",                                  // {}
        Base64("""{"v":1,"g":{},"i":{},"r":""}"""),
        Base64("""{"v":2,"g":{"s":0,"h":true,"f":null,"c":"11111111-2222-4333-8444-555555555555"},"i":{"s":0,"h":true,"f":null,"id":"11111111-2222-4333-8444-555555555555"},"r":""}"""),
        Base64("""{"v":1,"g":{"s":9,"h":true,"f":null,"c":"11111111-2222-4333-8444-555555555555"},"i":{"s":0,"h":true,"f":null,"id":"11111111-2222-4333-8444-555555555555"},"r":""}"""),
        Base64("""{"v":1,"g":{"s":0,"h":true,"f":null,"c":"not-a-guid"},"i":{"s":0,"h":true,"f":null,"id":"11111111-2222-4333-8444-555555555555"},"r":""}"""),
        Base64("""{"v":1,"g":{"s":0,"h":true,"f":null,"c":"00000000-0000-0000-0000-000000000000"},"i":{"s":0,"h":true,"f":null,"id":"11111111-2222-4333-8444-555555555555"},"r":""}"""),
        Base64("""{"v":1,"g":{"s":0,"h":true,"f":null,"c":"11111111-2222-4333-8444-555555555555"},"i":{"s":0,"h":true,"f":null,"id":"00000000-0000-0000-0000-000000000000"},"r":""}"""),
        Base64("""{"v":1,"g":{"s":0,"h":true,"f":"x","c":"11111111-2222-4333-8444-555555555555"},"i":{"s":0,"h":true,"f":null,"id":"11111111-2222-4333-8444-555555555555"},"r":""}"""),
        Base64("""{"v":1,"extra":1,"g":{"s":0,"h":true,"f":null,"c":"11111111-2222-4333-8444-555555555555"},"i":{"s":0,"h":true,"f":null,"id":"11111111-2222-4333-8444-555555555555"},"r":""}"""),
        // A well-formed cursor except for its returned-groups member: not base64url, or not a whole number of 16-byte ids.
        Base64("""{"v":1,"g":{"s":0,"h":true,"f":null,"c":"11111111-2222-4333-8444-555555555555"},"i":{"s":0,"h":true,"f":null,"id":"11111111-2222-4333-8444-555555555555"},"r":"AAAA"}"""),
        Base64("""{"v":1,"g":{"s":0,"h":true,"f":null,"c":"11111111-2222-4333-8444-555555555555"},"i":{"s":0,"h":true,"f":null,"id":"11111111-2222-4333-8444-555555555555"},"r":"!!!"}"""),
        Base64("""{"v":1,"g":{"s":0,"h":true,"f":null,"c":"11111111-2222-4333-8444-555555555555"},"i":{"s":0,"h":true,"f":null,"id":"11111111-2222-4333-8444-555555555555"},"r":5}"""),
        new string('A', 9000),
    };

    private static string Base64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Theory]
    [MemberData(nameof(BadCursors))]
    public void AMalformedOrTamperedCursor_DoesNotDecode_AndNeverThrows(string? wire)
    {
        DecideCursor.TryDecode(wire, out _).Should().BeFalse();
    }

    [Fact]
    public void ACursorWithAByteFlipped_IsInvalidOrADifferentPositionNeverAnException()
    {
        var wire = SomeCursor().Encode();
        for (var i = 0; i < wire.Length; i++)
        {
            var flipped = wire.ToCharArray();
            flipped[i] = flipped[i] == 'A' ? 'B' : 'A';
            var act = () => DecideCursor.TryDecode(new string(flipped), out _);
            act.Should().NotThrow();
        }
    }

    // ── The capability: caller guard and cursor refusal come before any read ────────────────────────────────────────

    [Fact]
    public async Task ListDecidePageAsync_WithNoSignedInCaller_IsUserContextRequired_AndReadsNothing()
    {
        // SignalReadService is Unused<T>(): any call into it fails the test.
        var result = await Capability().ListDecidePageAsync(null, null, CancellationToken.None);

        result.Outcome.Should().Be(DecidePageOutcome.UserContextRequired);
        DecisionResultMapper.ToToolError(result)!.ReasonCode.Should().Be(Sprk.Bff.Api.Infrastructure.Dataverse.DataverseUserClientErrorCodes.UserContextRequired);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("e30")]
    public async Task ListDecidePageAsync_WithAMalformedCursor_IsCursorInvalid_AndReadsNothing(string cursor)
    {
        var result = await Capability().ListDecidePageAsync(SignedIn().User, cursor, CancellationToken.None);

        result.Outcome.Should().Be(DecidePageOutcome.CursorInvalid);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public void ToToolError_ForACursorThatDoesNotDecode_IsAnIsErrorWithItsOwnCodeStatusAndMessage()
    {
        var error = DecisionResultMapper.ToToolError(DecidePageResult.CursorInvalid);

        error.Should().NotBeNull();
        error!.ReasonCode.Should().Be("ontology.worklist.cursor_invalid");
        error.Status.Should().Be(400);
        error.Message.Should().NotBeNullOrWhiteSpace().And.Be(
            DecisionResultMapper.ToToolError("ontology.worklist.cursor_invalid", 400).Message,
            "the table has the code's own message");
        error.Message.Should().NotBe(DecisionResultMapper.ToToolError("test.unknown.code", 400).Message, "it is not the unknown-code message");
    }

    [Fact]
    public void ToToolError_ForAPageThatCouldNotBeRead_IsTheWorklistsOwnAnswer_AndAPageIsNoError()
    {
        DecisionResultMapper.ToToolError(DecidePageResult.Ok([], new Dictionary<Guid, WorklistCoreSummary>(), null)).Should().BeNull();
        DecisionResultMapper.ToToolError(DecidePageResult.ReadFailed(SignalReadResult.Unavailable("too_many_signals")))!
            .ReasonCode.Should().Be("ontology.signal.worklist_unavailable");
        DecisionResultMapper.ToToolError(DecidePageResult.ReadFailed(SignalReadResult.CallerUnresolved))!
            .ReasonCode.Should().Be("sdap.access.deny.caller_unresolved");
    }
}
