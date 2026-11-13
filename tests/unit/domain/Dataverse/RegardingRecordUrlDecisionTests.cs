using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// field-mapping-server-write-path-r1 task 045f: on every pair table outside the invoice family, a regarding record URL a BFF write
/// sends names the record the row's regarding pair names after the write — the pair the same write sends, else the stored one —
/// and is stored in canonical form (<c>RegardingRecordType.BuildRecordUrl</c> of the table and the lower-case "D" id). A URL that
/// names another table or record, or no record at all, is refused; a clear is allowed; a failed read writes nothing. The ONE
/// decision (<see cref="OwnedChildWrite.DecideRegardingRecordUrlAsync"/>) over scripted readers, and its pure part
/// (<see cref="RegardingPairWrite.TryAdmitRecordUrl"/>). The five paths are pinned end to end by
/// <c>InvoiceFamilyGenericUpdatePathTests.RegardingRecordUrl</c>, <c>SecureChildOwnershipAiToolTests.RegardingRecordUrl</c> and
/// <c>RecordCreationServiceMappingSeamTests.RegardingRecordUrl</c>.
/// </summary>
public class RegardingRecordUrlDecisionTests
{
    private static readonly Guid Record = Guid.Parse("a045f000-0000-4000-8000-0000000000a1");
    private static readonly Guid OtherRecord = Guid.Parse("a045f000-0000-4000-8000-0000000000a2");
    private static readonly Guid TypeRow = Guid.Parse("a045f000-0000-4000-8000-0000000000f1");
    private static readonly Guid OtherTypeRow = Guid.Parse("a045f000-0000-4000-8000-0000000000f2");

    private const string PairTable = "sprk_matter";
    private const string OtherPairTable = "sprk_project";

    private readonly List<string> _reads = [];

    /// <summary>Every pair table the rule decides: the 18 pair tables less the six invoice-family tables.</summary>
    public static TheoryData<string> RuleTables() =>
        new(OwnedChildWrite.RegardingPairTables
            .Where(t => !InvoiceFamilyCoreRecordGuard.CoreRecordLookups.ContainsKey(t))
            .OrderBy(t => t, StringComparer.Ordinal));

    public static TheoryData<string> FamilyTables() => new(InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Keys);

    private static string Canonical(Guid id, string table = PairTable) =>
        RegardingRecordType.BuildRecordUrl(table, id.ToString("D").ToLowerInvariant());

    private static RegardingPairWrite Url(string? url, bool setsId = false, Guid? id = null, bool setsType = false, Guid? type = null) =>
        new(setsId, id?.ToString("D"), setsType, type, false) { SetsUrl = true, UrlText = url };

    /// <summary>The stored row: its pair id and type (or none), read with the path's reader.</summary>
    private Func<CancellationToken, Task<OwnedChildWrite.RegardingPairRead<OwnedChildWrite.RegardingPairRow>>> Stored(
        Guid? id, Guid? type, int? failure = null) =>
        _ =>
        {
            _reads.Add("row");
            return Task.FromResult(failure is { } status
                ? new OwnedChildWrite.RegardingPairRead<OwnedChildWrite.RegardingPairRow>(null, status, null)
                : OwnedChildWrite.RegardingPairRead<OwnedChildWrite.RegardingPairRow>.Ok(
                    new OwnedChildWrite.RegardingPairRow(id?.ToString("D"), type, new Dictionary<string, Guid>())));
        };

    /// <summary>The catalog: <see cref="TypeRow"/> names the matter, <see cref="OtherTypeRow"/> the project.</summary>
    private Func<Guid, CancellationToken, Task<OwnedChildWrite.RegardingPairRead<OwnedChildWrite.RegardingPairType>>> Catalog(int? failure = null) =>
        (type, _) =>
        {
            _reads.Add("catalog");
            if (failure is { } status)
                return Task.FromResult(new OwnedChildWrite.RegardingPairRead<OwnedChildWrite.RegardingPairType>(null, status, null));
            var table = type == TypeRow ? PairTable : type == OtherTypeRow ? OtherPairTable : null;
            return Task.FromResult(table is null
                ? new OwnedChildWrite.RegardingPairRead<OwnedChildWrite.RegardingPairType>(null, 404, null)
                : OwnedChildWrite.RegardingPairRead<OwnedChildWrite.RegardingPairType>.Ok(new OwnedChildWrite.RegardingPairType(table, null)));
        };

    private Task<OwnedChildWrite.RegardingRecordUrlDecision> Decide(
        string table, RegardingPairWrite written,
        Func<CancellationToken, Task<OwnedChildWrite.RegardingPairRead<OwnedChildWrite.RegardingPairRow>>>? stored,
        CancellationToken ct = default) =>
        OwnedChildWrite.DecideRegardingRecordUrlAsync(table, written, stored, Catalog(), ct);

    // ── Must fire: a URL naming another table or record, or none, is refused ────────────────────────────────────────

    public static TheoryData<string, string> UnreadableUrls() => new()
    {
        { "no query", "/main.aspx" },
        { "no etn", $"/main.aspx?pagetype=entityrecord&id={Record:D}" },
        { "no id", "/main.aspx?pagetype=entityrecord&etn=sprk_matter" },
        { "an id that is not one", "/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=not-an-id" },
        { "a repeated id", $"/main.aspx?etn=sprk_matter&id={Record:D}&id={Record:D}" },
        { "not a URL", "Acme v. Widget" },
        { "an escaped separator", $"/main.aspx?pagetype=entityrecord&amp;etn=sprk_matter&amp;id={Record:D}" },
    };

    [Theory]
    [MemberData(nameof(RuleTables))]
    public async Task AUrlNamingAnotherRecordThanTheStoredPair_IsRefused(string table)
    {
        var decided = await Decide(table, Url(Canonical(OtherRecord)), Stored(Record, TypeRow));

        decided.Refused.Should().BeTrue();
        decided.CanonicalUrl.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(RuleTables))]
    public async Task AUrlNamingAnotherTableThanTheStoredPair_IsRefused(string table)
    {
        var decided = await Decide(table, Url(Canonical(Record, OtherPairTable)), Stored(Record, TypeRow));

        decided.Refused.Should().BeTrue();
        _reads.Should().Equal(["row", "catalog"], "the type decides the table: the catalog is read");
    }

    [Theory]
    [MemberData(nameof(UnreadableUrls))]
    public async Task AUrlThatNamesNoRecord_IsRefused_WithoutARead(string form, string url)
    {
        var decided = await Decide("sprk_todo", Url(url), Stored(Record, TypeRow));

        decided.Refused.Should().BeTrue(form);
        _reads.Should().BeEmpty("a URL naming no record is refused before any read");
    }

    [Theory]
    [MemberData(nameof(RuleTables))]
    public async Task APairMoveWithAUrlOfTheOldRecord_IsRefused(string table)
    {
        var decided = await Decide(table, Url(Canonical(Record), setsId: true, id: OtherRecord, setsType: true, type: TypeRow),
            Stored(Record, TypeRow));

        decided.Refused.Should().BeTrue("a URL sent with a pair move is decided against the new pair");
        _reads.Should().Equal(["catalog"], "the write sends the whole pair: the stored row is not read");
    }

    [Fact]
    public async Task AnIdOnlyMoveWithAUrlOfTheOldRecord_IsRefused_AgainstTheStoredType()
    {
        var decided = await Decide("sprk_event", Url(Canonical(Record), setsId: true, id: OtherRecord), Stored(Record, TypeRow));

        decided.Refused.Should().BeTrue();
        _reads.Should().Equal(["row", "catalog"]);
    }

    [Fact]
    public async Task ARetypeOverTheSameIdWithAUrlOfTheOldTable_IsRefused()
    {
        var decided = await Decide("sprk_event", Url(Canonical(Record), setsType: true, type: OtherTypeRow), Stored(Record, TypeRow));

        decided.Refused.Should().BeTrue("the pair now names the project of that id");
    }

    [Theory]
    [MemberData(nameof(RuleTables))]
    public async Task AUrlOnARowWithNoPair_IsRefused(string table)
    {
        var decided = await Decide(table, Url(Canonical(Record)), Stored(null, null));

        decided.Refused.Should().BeTrue("the URL names no record of the row");
    }

    [Fact]
    public async Task AUrlOnACreateWithNoPair_IsRefused()
    {
        var decided = await Decide("sprk_todo", Url(Canonical(Record)), stored: null);

        decided.Refused.Should().BeTrue();
        _reads.Should().BeEmpty();
    }

    [Fact]
    public async Task AUrlWhosePairIdIsCleared_IsRefused()
    {
        var written = new RegardingPairWrite(true, null, false, null, false) { SetsUrl = true, UrlText = Canonical(Record) };

        var decided = await Decide("sprk_todo", written, Stored(Record, TypeRow));

        decided.Refused.Should().BeTrue();
    }

    [Fact]
    public async Task AUrlBesideAnUnreadablePairValue_IsRefused()
    {
        var written = new RegardingPairWrite(true, null, false, null, true) { SetsUrl = true, UrlText = Canonical(Record) };

        (await Decide("sprk_todo", written, Stored(Record, TypeRow))).Refused.Should().BeTrue();
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    public async Task AUrlWhosePairTypeNamesNoTableTheReaderCanSee_IsRefused(int status)
    {
        var decided = await OwnedChildWrite.DecideRegardingRecordUrlAsync(
            "sprk_todo", Url(Canonical(Record)), Stored(Record, TypeRow), Catalog(failure: status), CancellationToken.None);

        decided.Refused.Should().BeTrue();
        decided.Failed.Should().BeFalse();
    }

    [Fact]
    public async Task AnIdOnlyPair_WithAUrlOfAnotherId_IsRefused()
    {
        (await Decide("sprk_todo", Url(Canonical(OtherRecord)), Stored(Record, null))).Refused.Should().BeTrue();
    }

    [Fact]
    public async Task AnIdOnlyPair_WithAUrlWhoseTableIsNotALogicalName_IsRefused()
    {
        var url = $"/main.aspx?pagetype=entityrecord&etn={Uri.EscapeDataString("x y")}&id={Record:D}";

        (await Decide("sprk_todo", Url(url), Stored(Record, null))).Refused.Should().BeTrue();
    }

    // ── Canonical: an admitted URL is stored as the server writes it ─────────────────────────────────────────────────

    public static TheoryData<string, string> AdmittedForms() => new()
    {
        { "relative", $"/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={Record:D}" },
        { "absolute on another host", $"https://other.example/main.aspx?appid=0a1b2c3d-0000-4000-8000-000000000001&pagetype=entityrecord&etn=sprk_matter&id={Record:D}" },
        { "another scheme", $"ftp://other.example/main.aspx?etn=sprk_matter&id={Record:D}" },
        { "an upper-case id and table", $"/main.aspx?pagetype=entityrecord&etn=SPRK_MATTER&id={Record.ToString("D").ToUpperInvariant()}" },
        { "a braced id", $"/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={Record:B}" },
        { "escaped, with a fragment", $"/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=%7B{Record:D}%7D#tab" },
    };

    [Theory]
    [MemberData(nameof(AdmittedForms))]
    public async Task AUrlOfThePairsRecord_IsAdmitted_InCanonicalForm(string form, string url)
    {
        var decided = await Decide("sprk_todo", Url(url), Stored(Record, TypeRow));

        decided.Refused.Should().BeFalse(form);
        decided.Failed.Should().BeFalse();
        decided.CanonicalUrl.Should().Be(Canonical(Record), form);
    }

    [Theory]
    [MemberData(nameof(RuleTables))]
    public async Task AUrlOfTheNewPair_SentWithAPairMove_IsAdmitted_InCanonicalForm(string table)
    {
        var url = $"https://other.example/main.aspx?etn=sprk_project&id={OtherRecord:B}";

        var decided = await Decide(table, Url(url, setsId: true, id: OtherRecord, setsType: true, type: OtherTypeRow), Stored(Record, TypeRow));

        decided.CanonicalUrl.Should().Be(Canonical(OtherRecord, OtherPairTable));
    }

    [Fact]
    public async Task AnIdOnlyPair_WithAUrlOfThatId_IsAdmitted_WithItsOwnTable_InCanonicalForm()
    {
        var url = $"https://other.example/main.aspx?etn=SPRK_Matter&id={Record:B}";

        var decided = await Decide("sprk_todo", Url(url), Stored(Record, null));

        decided.CanonicalUrl.Should().Be(Canonical(Record));
        _reads.Should().Equal(["row"], "a pair with no type reads no catalog row");
    }

    // ── Must not fire ──────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AUrlClear_IsUnchanged_AndReadsNothing(string? clear)
    {
        var decided = await Decide("sprk_todo", Url(clear), Stored(Record, TypeRow));

        decided.Should().Be(OwnedChildWrite.RegardingRecordUrlDecision.Unchanged);
        _reads.Should().BeEmpty();
    }

    [Fact]
    public async Task AWriteWithNoUrl_IsUnchanged_AndReadsNothing()
    {
        var decided = await Decide("sprk_todo", new RegardingPairWrite(true, OtherRecord.ToString("D"), false, null, false), Stored(Record, TypeRow));

        decided.Should().Be(OwnedChildWrite.RegardingRecordUrlDecision.Unchanged);
        _reads.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(FamilyTables))]
    public async Task AnInvoiceFamilyTable_IsNotDecidedHere(string table)
    {
        var decided = await Decide(table, Url("not a URL"), Stored(Record, TypeRow));

        decided.Should().Be(OwnedChildWrite.RegardingRecordUrlDecision.Unchanged, "045e decides it against the core record");
        _reads.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_document")]
    public async Task ATableWithNoRegardingPair_IsNotDecided(string table)
    {
        (await Decide(table, Url("not a URL"), Stored(Record, TypeRow))).Should().Be(OwnedChildWrite.RegardingRecordUrlDecision.Unchanged);
        OwnedChildWrite.RegardingRecordUrlRuleApplies(table).Should().BeFalse();
    }

    // ── The communication thread: anchored by typed regarding lookups, not a pair (fix round 1, F1b) ─────────────────────

    private const string Thread = "sprk_communicationthread";

    /// <summary>The thread's stored typed lookups (column → record), read with the path's reader.</summary>
    private Func<CancellationToken, Task<OwnedChildWrite.RegardingPairRead<OwnedChildWrite.RegardingPairRow>>> StoredThread(
        params (string Column, Guid Id)[] lookups) =>
        _ =>
        {
            _reads.Add("row");
            return Task.FromResult(OwnedChildWrite.RegardingPairRead<OwnedChildWrite.RegardingPairRow>.Ok(
                new OwnedChildWrite.RegardingPairRow(null, null, lookups.ToDictionary(l => l.Column, l => l.Id))));
        };

    private Task<OwnedChildWrite.RegardingRecordUrlDecision> DecideThread(
        string? url, Func<CancellationToken, Task<OwnedChildWrite.RegardingPairRead<OwnedChildWrite.RegardingPairRow>>>? stored,
        params (string Column, Guid? Id)[] sent) =>
        OwnedChildWrite.DecideRegardingRecordUrlAsync(Thread, Url(url), stored, Catalog(), default,
            sent.ToDictionary(s => s.Column, s => s.Id));

    [Fact]
    public void TheThread_IsUnderTheRule() => OwnedChildWrite.RegardingRecordUrlRuleApplies(Thread).Should().BeTrue();

    [Fact]
    public async Task TheThread_AUrlOfItsStoredAnchor_IsAdmitted_InCanonicalForm()
    {
        var decided = await DecideThread($"/main.aspx?etn=sprk_matter&id={Record:B}", StoredThread(("sprk_regardingmatter", Record)));

        decided.CanonicalUrl.Should().Be(RegardingRecordType.BuildRecordUrl("sprk_matter", Record.ToString("D")));
        decided.Refused.Should().BeFalse();
    }

    [Theory]
    [InlineData("sprk_matter", true)]   // another record of the anchor's table
    [InlineData("sprk_project", false)] // the anchor's id under another table
    public async Task TheThread_AUrlOfAnotherRecordThanItsAnchor_IsRefused(string urlTable, bool otherId)
    {
        var decided = await DecideThread(RegardingRecordType.BuildRecordUrl(urlTable, (otherId ? OtherRecord : Record).ToString("D")),
            StoredThread(("sprk_regardingmatter", Record)));

        decided.Should().Be(OwnedChildWrite.RegardingRecordUrlDecision.Refuse);
    }

    [Fact]
    public async Task TheThread_TheHighestPriorityAnchorDecides()
    {
        // The matter outranks the project (ThreadResolver's anchor order): a URL of the project is not the thread's anchor.
        var decided = await DecideThread(RegardingRecordType.BuildRecordUrl("sprk_project", OtherRecord.ToString("D")),
            StoredThread(("sprk_regardingproject", OtherRecord), ("sprk_regardingmatter", Record)));

        decided.Should().Be(OwnedChildWrite.RegardingRecordUrlDecision.Refuse);
    }

    [Fact]
    public async Task TheThread_AnAnchorTheSameWriteSets_DecidesWithoutReadingTheRow()
    {
        var decided = await DecideThread(RegardingRecordType.BuildRecordUrl("sprk_matter", OtherRecord.ToString("D")),
            StoredThread(("sprk_regardingmatter", Record)), ("sprk_regardingmatter", OtherRecord));

        decided.CanonicalUrl.Should().Be(RegardingRecordType.BuildRecordUrl("sprk_matter", OtherRecord.ToString("D")));
        _reads.Should().BeEmpty();
    }

    [Fact]
    public async Task TheThread_AnAnchorTheSameWriteClears_FallsToTheNextStoredAnchor()
    {
        var decided = await DecideThread(RegardingRecordType.BuildRecordUrl("sprk_project", OtherRecord.ToString("D")),
            StoredThread(("sprk_regardingmatter", Record), ("sprk_regardingproject", OtherRecord)), ("sprk_regardingmatter", null));

        decided.CanonicalUrl.Should().Be(RegardingRecordType.BuildRecordUrl("sprk_project", OtherRecord.ToString("D")));
    }

    [Fact]
    public async Task TheThread_AnchoredNowhere_IsRefused()
    {
        (await DecideThread(RegardingRecordType.BuildRecordUrl("sprk_matter", Record.ToString("D")), StoredThread()))
            .Should().Be(OwnedChildWrite.RegardingRecordUrlDecision.Refuse);
    }

    [Fact]
    public async Task TheThread_AStoredRowReadFault_FailsClosed()
    {
        var decided = await DecideThread(RegardingRecordType.BuildRecordUrl("sprk_matter", Record.ToString("D")),
            _ => Task.FromResult(new OwnedChildWrite.RegardingPairRead<OwnedChildWrite.RegardingPairRow>(null, 503, null)));

        decided.Failed.Should().BeTrue();
        decided.CanonicalUrl.Should().BeNull();
    }

    [Fact]
    public async Task TheThread_AClear_IsNotDecided()
    {
        (await DecideThread(null, StoredThread(("sprk_regardingmatter", Record)))).Should().Be(OwnedChildWrite.RegardingRecordUrlDecision.Unchanged);
        _reads.Should().BeEmpty();
    }

    // ── Fail closed ────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public async Task AStoredPairReadFault_WritesNothing(int status)
    {
        var decided = await Decide("sprk_todo", Url(Canonical(Record)), Stored(null, null, failure: status));

        decided.Failed.Should().BeTrue();
        decided.FailedStatus.Should().Be(status);
        decided.CanonicalUrl.Should().BeNull();
        decided.Refused.Should().BeFalse("a read that failed is the read's own answer, never the URL's");
    }

    [Fact]
    public async Task ACatalogReadFault_WritesNothing()
    {
        var decided = await OwnedChildWrite.DecideRegardingRecordUrlAsync(
            "sprk_todo", Url(Canonical(Record)), Stored(Record, TypeRow), Catalog(failure: 500), CancellationToken.None);

        decided.Failed.Should().BeTrue();
        decided.CanonicalUrl.Should().BeNull();
    }

    [Fact]
    public async Task Cancellation_IsRethrown()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => OwnedChildWrite.DecideRegardingRecordUrlAsync("sprk_todo", Url(Canonical(Record)),
            ct =>
            {
                ct.ThrowIfCancellationRequested();
                return Stored(Record, TypeRow)(ct);
            },
            Catalog(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── The pure part ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryAdmitRecordUrl_ComparesTheTableAndTheId_AndWritesTheServersForm()
    {
        RegardingPairWrite.TryAdmitRecordUrl($"https://h/main.aspx?etn=sprk_matter&id={Record:B}", PairTable, Record, out var canonical)
            .Should().BeTrue();
        canonical.Should().Be(Canonical(Record));

        RegardingPairWrite.TryAdmitRecordUrl(Canonical(Record), OtherPairTable, Record, out _).Should().BeFalse();
        RegardingPairWrite.TryAdmitRecordUrl(Canonical(Record), PairTable, OtherRecord, out _).Should().BeFalse();
        RegardingPairWrite.TryAdmitRecordUrl(Canonical(Record), PairTable, Guid.Empty, out _).Should().BeFalse();
        RegardingPairWrite.TryAdmitRecordUrl(null, PairTable, Record, out _).Should().BeFalse();
    }

    [Fact]
    public void TheRefusal_HasOneCode_AndFixedTextNamingNoRecord()
    {
        RegardingPairWrite.RecordUrlMismatchCode.Should().Be("regarding_record_url_mismatch");
        RegardingPairWrite.RecordUrlMismatchReason.Should().NotMatchRegex("[0-9a-fA-F]{8}-");
        OwnedChildWrite.RegardingRecordUrlRefusalText.Should().Contain(RegardingPairWrite.RecordUrlMismatchCode);
    }
}
