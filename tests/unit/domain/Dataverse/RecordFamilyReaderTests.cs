using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using EdgeKind = Sprk.Bff.Api.Services.Dataverse.RecordFamilyEdges.EdgeKind;
using FamilyKind = Sprk.Bff.Api.Services.Dataverse.RecordFamilyEdges.FamilyKind;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// field-mapping-server-write-path-r1 task 071 (FR-30; design §12.3 B) — <see cref="RecordFamilyReader"/> walks a record's Filing
/// family AS THE CALLER through the caller-scoped seam (<see cref="IImpersonatedCommunicationQuery"/>, the /push pattern), faked
/// here by an in-memory Dataverse that answers only what the caller may read. Isolation in the fixture is UAC's one predicate
/// (<see cref="SecureRecordOwnerTeam.IsInSecureBusinessUnit"/> over the row's owning business unit), never a team id.
/// KEEP path: tests/unit/domain/** (ADR-038 §2 — handler-internal orchestration over an in-memory seam; no I/O).
/// </summary>
public class RecordFamilyReaderTests
{
    private const string Matter = "sprk_matter";
    private const string Project = "sprk_project";
    private const string Document = "sprk_document";
    private const string FileVersion = "sprk_fileversion";
    private const string Signal = "sprk_signal";
    private const string DecisionRecord = "sprk_decisionrecord";
    private const string Communication = "sprk_communication";
    private const string Thread = "sprk_communicationthread";

    private static readonly Guid OrdinaryBu = new("0b000000-0000-4000-8000-000000000001");
    private static readonly Guid SecureBu = new("0b000000-0000-4000-8000-000000000002");
    private static readonly Guid MatterType = new("0c000000-0000-4000-8000-000000000001");
    private static readonly Guid ProjectType = new("0c000000-0000-4000-8000-000000000002");
    private static readonly RecordFamilyCaller Caller =
        new(new Guid("0a000000-0000-4000-8000-000000000001"), new Guid("0a000000-0000-4000-8000-000000000002"));

    private readonly FakeCallerScopedDataverse _dv = new();
    private readonly EphemeralDataProtectionProvider _keys = new();
    private int _next;

    public RecordFamilyReaderTests()
    {
        _dv.Add("sprk_recordtype_ref", MatterType, OrdinaryBu, ("sprk_recordlogicalname", Matter));
        _dv.Add("sprk_recordtype_ref", ProjectType, OrdinaryBu, ("sprk_recordlogicalname", Project));
    }

    // ── Pruning (UAC F2): a readable record below an unreadable one is absent, and nothing reveals it ──

    [Fact]
    public async Task ReadAsync_ReadableRecordsBelowAnUnreadableSecureRecord_AreAbsentAndTheViewEqualsAFamilyWithoutThatBranch()
    {
        // A Secure matter shared with the caller; one Secure document the caller may read, one they may not. Below the
        // unreadable one: 600 readable file versions (more than the cap, so a leak would also show as truncation).
        var withBranch = new RecordFamilyReaderTests();
        var matter = withBranch.SecureRecord(Matter, shared: true, ("sprk_mattername", "Secure matter"));
        var readableDoc = withBranch.SecureRecord(Document, shared: true, ("sprk_matter", matter), ("sprk_documentname", "Readable"));
        withBranch.Record(FileVersion, ("sprk_document", readableDoc));
        var hiddenDoc = withBranch.SecureRecord(Document, shared: false, ("sprk_matter", matter), ("sprk_documentname", "Hidden"));
        var belowHidden = Enumerable.Range(0, 600).Select(_ => withBranch.Record(FileVersion, ("sprk_document", hiddenDoc))).ToList();

        // The same family with that branch (the hidden document and everything below it) never created.
        var withoutBranch = new RecordFamilyReaderTests();
        var matter2 = withoutBranch.SecureRecord(Matter, shared: true, ("sprk_mattername", "Secure matter"));
        var readableDoc2 = withoutBranch.SecureRecord(Document, shared: true, ("sprk_matter", matter2), ("sprk_documentname", "Readable"));
        withoutBranch.Record(FileVersion, ("sprk_document", readableDoc2));
        matter2.Should().Be(matter);
        readableDoc2.Should().Be(readableDoc);

        var seen = await withBranch.ReadAsync(Matter, matter);
        var expected = await withoutBranch.ReadAsync(Matter, matter2);

        seen.Members.Select(m => m.Id).Should().NotContain(belowHidden.Append(hiddenDoc));
        seen.Members.Should().HaveCount(3);
        seen.Should().BeEquivalentTo(expected, o => o.WithStrictOrdering(),
            "result, per-table counts and the truncation flags must be those of a family where the branch does not exist");
    }

    // ── Up-walk (UAC F2, T1) ──

    [Fact]
    public async Task ReadAsync_AncestorTheCallerCannotRead_StopsTheUpWalkAndRootsTheViewAtTheHighestReadableAncestor()
    {
        var matter = SecureRecord(Matter, shared: false);
        var project = Record(Project);
        PairUnder(project, MatterType, matter);
        var sibling = Record(Project);
        PairUnder(sibling, MatterType, matter);
        var anchor = Record(Document, ("sprk_project", project));

        var read = await ReadAsync(Document, anchor);

        read.Outcome.Should().Be(RecordFamilyReadOutcome.Read);
        read.Top!.Id.Should().Be(project, "the project is the highest ancestor the caller can read");
        read.Top.Depth.Should().Be(0);
        read.Top.Parent.Should().BeNull("nothing above the first unreadable ancestor is returned or hinted at");
        read.Top.EdgeKind.Should().BeNull();
        read.Members.Select(m => m.Id).Should().Equal(project, anchor);
        read.Members.Single(m => m.Id == anchor).Depth.Should().Be(1);
        read.Members.Select(m => m.Id).Should().NotContain(new[] { matter, sibling });
    }

    [Fact]
    public async Task ReadAsync_RootTheCallerCanRead_IsTheTopOfTheFullView()
    {
        var matter = Record(Matter);
        var project = Record(Project);
        PairUnder(project, MatterType, matter);
        var sibling = Record(Project);
        PairUnder(sibling, MatterType, matter);
        var anchor = Record(Document, ("sprk_project", project));

        var read = await ReadAsync(Document, anchor);

        read.Top!.Id.Should().Be(matter);
        read.Members.Select(m => (m.Id, m.Depth)).Should().BeEquivalentTo(new[] { (matter, 0), (project, 1), (anchor, 2), (sibling, 1) });
        read.Members.Single(m => m.Id == project).EdgeKind.Should().Be(EdgeKind.Pair);
        read.Members.Single(m => m.Id == project).Parent.Should().Be(new RecordFamilyRecordRef(Matter, matter));
    }

    // ── One family per record (owner via UAC-r2): a child is listed only under its PRIMARY parent ──

    [Fact]
    public async Task ReadAsync_RecordFiledUnderAMatterAndAProject_IsInTheMattersFamilyOnly()
    {
        var matterA = Record(Matter);
        var projectB = Record(Project);
        var filed = Record(Document, ("sprk_matter", matterA), ("sprk_project", projectB));

        var underB = await ReadAsync(Project, projectB);
        var underA = await ReadAsync(Matter, matterA);

        underB.Members.Select(m => m.Id).Should().NotContain(filed, "the project is the document's secondary filing");
        var member = underA.Members.Single(m => m.Id == filed);
        member.Parent.Should().Be(new RecordFamilyRecordRef(Matter, matterA));
        member.Depth.Should().Be(1);
    }

    // ── De-duplication (ontology): unique by (table, id), shallowest kept ──

    [Fact]
    public async Task ReadAsync_SignalReachableAtDepthOneAndTwo_AppearsOnceAtDepthOne()
    {
        var matter = Record(Matter);
        var decision = Record(DecisionRecord,
            ("sprk_corerecordtype", MatterType), ("sprk_corerecordid", matter.ToString("D")),
            ("sprk_matter", matter), ("sprk_regardingmatter", matter));
        var signal = Record(Signal,
            ("sprk_corerecordtype", MatterType), ("sprk_corerecordid", matter.ToString("D")),
            ("sprk_matter", matter), ("sprk_regardingmatter", matter), ("sprk_decisionrecord", decision));

        var read = await ReadAsync(Matter, matter);

        read.Members.Where(m => m.Id == signal).Should().ContainSingle()
            .Which.Should().Match<RecordFamilyMember>(m => m.Depth == 1 && m.Parent == new RecordFamilyRecordRef(Matter, matter));
        read.Members.Single(m => m.Id == decision).Depth.Should().Be(1);
        read.CountsByTable[Signal].Should().Be(1);
    }

    // ── Caps (FR-30, UAC C4): truncation keeps the anchor's chain and subtree; "load more" returns the rest ──

    [Fact]
    public async Task ReadAsync_FamilyOverTheCap_KeepsTheAnchorsChainAndSubtreeAndLoadMoreReturnsTheRestOnce()
    {
        var matter = Record(Matter);
        var siblings = Enumerable.Range(0, 520).Select(_ => Record(Document, ("sprk_matter", matter))).ToList();
        var anchor = Record(Document, ("sprk_matter", matter));
        var versions = Enumerable.Range(0, 3).Select(_ => Record(FileVersion, ("sprk_document", anchor))).ToList();

        var first = await ReadAsync(Document, anchor);

        first.Truncated.Should().BeTrue();
        first.Continuation.Should().NotBeNullOrEmpty();
        first.Members.Should().HaveCount(500);
        first.Members.Select(m => m.Id).Should().StartWith(new[] { matter, anchor }.Concat(versions),
            "the anchor's chain, then the anchor's own subtree, before any sibling's branch");

        var second = await ReadAsync(Document, anchor, continuation: first.Continuation);

        second.Outcome.Should().Be(RecordFamilyReadOutcome.Read);
        second.Truncated.Should().BeFalse();
        second.Continuation.Should().BeNull();
        var all = first.Members.Concat(second.Members).Select(m => m.Id).ToList();
        all.Should().OnlyHaveUniqueItems("no member is returned twice");
        all.Should().BeEquivalentTo(siblings.Append(matter).Append(anchor).Concat(versions), "and none is dropped");
    }

    // ── Continuation (UAC F1, C1) ──

    [Fact]
    public async Task ReadAsync_ContinuationReplayedUnderAnotherBindingOrTampered_IsRefusedExactlyLikeAnInvalidOne()
    {
        var matter = Record(Matter);
        var siblings = Enumerable.Range(0, 510).Select(_ => Record(Document, ("sprk_matter", matter))).ToList();
        var anchor = Record(Document, ("sprk_matter", matter));
        var token = (await ReadAsync(Document, anchor)).Continuation!;
        var invalid = await ReadAsync(Document, anchor, continuation: "not-a-continuation");
        var otherKind = RecordFamilyReader.ProtectContinuation(
            _keys.CreateProtector(RecordFamilyReader.ContinuationPurpose),
            new RecordFamilyReader.ContinuationState(1, Caller.ObjectId, Document, anchor, Kind: 2, (int)RecordFamilyDirection.Full,
                Matter, matter, 500));
        var tampered = token[..(token.Length / 2)] + (token[token.Length / 2] == 'A' ? 'B' : 'A') + token[(token.Length / 2 + 1)..];

        var replays = new[]
        {
            await ReadAsync(Document, anchor, continuation: token, caller: Caller with { ObjectId = Guid.NewGuid() }),
            await ReadAsync(Document, siblings[0], continuation: token),
            await ReadAsync(Document, anchor, continuation: token, direction: RecordFamilyDirection.Down),
            await ReadAsync(Document, anchor, continuation: otherKind),
            await ReadAsync(Document, anchor, continuation: tampered),
        };

        invalid.Outcome.Should().Be(RecordFamilyReadOutcome.ContinuationInvalid);
        invalid.Members.Should().BeEmpty();
        replays.Should().AllSatisfy(r => r.Should().BeEquivalentTo(invalid));
    }

    [Fact]
    public async Task ReadAsync_ContinuationWhoseCarriedTopBecameUnreadable_DoesNotTraverseIt()
    {
        var matter = Record(Matter);
        foreach (var _ in Enumerable.Range(0, 510))
            Record(Document, ("sprk_matter", matter));
        var anchor = Record(Document, ("sprk_matter", matter));
        var token = (await ReadAsync(Document, anchor)).Continuation!;
        _dv.Unreadable.Add(matter);
        var callsBefore = _dv.Calls.Count;

        var resumed = await ReadAsync(Document, anchor, continuation: token);

        resumed.Outcome.Should().Be(RecordFamilyReadOutcome.ContinuationInvalid);
        resumed.Members.Should().BeEmpty();
        _dv.Calls.Skip(callsBefore).Where(c => c.Query.Contains(matter.ToString("D"), StringComparison.OrdinalIgnoreCase))
            .Should().OnlyContain(c => c.Query.Contains($"sprk_matterid eq {matter:D}"),
                "the matter may be re-checked as the caller, but no child query is ever filtered on it");
    }

    // ── One kind per walk (ontology 3a); the visited set ──

    [Fact]
    public async Task ReadAsync_KindOtherThanFiling_IsRejectedBeforeAnyDataverseCall()
    {
        var matter = Record(Matter);

        var read = await Reader().ReadAsync(Caller, (FamilyKind)2, Matter, matter, RecordFamilyDirection.Full, null, CancellationToken.None);

        read.Outcome.Should().Be(RecordFamilyReadOutcome.KindNotSupported);
        read.Members.Should().BeEmpty();
        _dv.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_BadDataLoop_TerminatesWithEachRecordOnce()
    {
        var first = NewId();
        var second = NewId();
        _dv.Add(Document, first, OrdinaryBu, ("sprk_parentdocument", second));
        _dv.Add(Document, second, OrdinaryBu, ("sprk_parentdocument", first));

        var read = await ReadAsync(Document, first);

        read.Outcome.Should().Be(RecordFamilyReadOutcome.Read);
        read.Members.Select(m => m.Id).Should().BeEquivalentTo(new[] { first, second });
        read.Members.Select(m => m.Id).Should().OnlyHaveUniqueItems();
        read.Truncated.Should().BeFalse();
    }

    // ── Display columns: as the caller only ──

    [Fact]
    public async Task ReadAsync_FieldSecuredDisplayColumn_ComesBackNull()
    {
        var matter = Record(Matter, ("sprk_mattername", "Acme v Contoso"), ("sprk_matternumber", "MAT-0001"));
        var document = Record(Document, ("sprk_matter", matter), ("sprk_documentname", "Brief.docx"), ("sprk_document_number", "DOC-7"));
        _dv.FieldSecured.Add((Document, "sprk_document_number"));

        var read = await ReadAsync(Matter, matter);

        var member = read.Members.Single(m => m.Id == document);
        member.RecordNumber.Should().BeNull("a field-secured value the caller cannot read comes back null, never read app-only");
        member.Name.Should().Be("Brief.docx");
        member.Status.Should().Be("Active");
        read.Members.Single(m => m.Id == matter).Should().Match<RecordFamilyMember>(m =>
            m.Name == "Acme v Contoso" && m.RecordNumber == "MAT-0001");
    }

    [Fact]
    public async Task ReadAsync_EveryDataverseRead_IsMadeAsTheCallerThroughTheCallerScopedSeam()
    {
        var matter = Record(Matter);
        var project = Record(Project);
        PairUnder(project, MatterType, matter);
        var document = Record(Document, ("sprk_project", project));
        Record(FileVersion, ("sprk_document", document));

        await ReadAsync(Document, document);

        _dv.Calls.Should().NotBeEmpty();
        _dv.Calls.Should().OnlyContain(c => c.CallerSystemUserId == Caller.SystemUserId,
            "every member query and display-column read impersonates the caller; the reader holds no app-only client");
    }

    // ── Beyond the criteria, one line each in notes/task-071-notes.md ──

    [Fact]
    public async Task ReadAsync_FamilyDeeperThanTheDepthCap_SaysItIsTruncatedAndStillListsTheOtherBranches()
    {
        // matter (0) → chain[0] (1) → … → chain[4] (5): one level deeper than the cap, below the opened document.
        var matter = Record(Matter);
        var parent = Record(Document, ("sprk_matter", matter));
        var chain = new List<Guid> { parent };
        for (var i = 0; i < 4; i++)
            chain.Add(parent = Record(Document, ("sprk_parentdocument", parent)));
        var sibling = Record(Document, ("sprk_matter", matter));

        var fromRoot = await ReadAsync(Matter, matter);
        var fromAnchor = await ReadAsync(Document, chain[0]);
        var fromDeepest = await ReadAsync(Document, chain[^1]);

        foreach (var read in new[] { fromRoot, fromAnchor })
        {
            read.Members.Max(m => m.Depth).Should().Be(4);
            read.Members.Select(m => m.Id).Should().NotContain(chain[^1]).And.Contain(sibling,
                "the cap below the anchor's subtree ends that branch, not the walk of the others");
            read.Truncated.Should().BeTrue("a member left out by the depth cap is never dropped silently");
            read.Continuation.Should().BeNull("paging cannot reach below the depth cap");
        }

        fromDeepest.Top!.Id.Should().Be(chain[0], "the up-walk follows at most the depth cap's hops");
        fromDeepest.Members.Select(m => m.Id).Should().NotContain(new[] { matter, sibling });
        fromDeepest.Truncated.Should().BeTrue("the readable matter above the capped view is left out, and that is said");

        _dv.Unreadable.Add(matter);
        var belowHidden = await ReadAsync(Document, chain[^1]);

        belowHidden.Should().BeEquivalentTo(fromDeepest with { Truncated = false }, o => o.WithStrictOrdering(),
            "an ancestor the caller cannot read past the cap is never hinted at, not even by the truncation flag");
    }

    [Fact]
    public async Task ReadAsync_SignalWhoseSubjectRanksFirstInLookupOrder_IsListedUnderItsCoreRecord()
    {
        var matter = Record(Matter);
        var subject = Record(Document, ("sprk_matter", matter));
        var core = Record(Project);
        var signal = Record(Signal,
            ("sprk_corerecordtype", ProjectType), ("sprk_corerecordid", core.ToString("D")), ("sprk_regardingproject", core),
            ("sprk_regardingdocument", subject), ("sprk_regardingrecordid", subject.ToString("D")));

        var underCore = await ReadAsync(Project, core);
        var underSubject = await ReadAsync(Matter, matter);

        underCore.Members.Single(m => m.Id == signal).Should().Match<RecordFamilyMember>(m =>
            m.Parent == new RecordFamilyRecordRef(Project, core) && m.EdgeKind == EdgeKind.Stamp);
        underSubject.Members.Select(m => m.Id).Should().NotContain(signal);
    }

    [Fact]
    public async Task ReadAsync_MessagesInThreads_RankTheThreadAfterTheirOwnParentsAndADirectThreadFilesNothing()
    {
        var matter = Record(Matter);
        var recordThread = Record(Thread, ("sprk_threadtype", 100000000), ("sprk_regardingmatter", matter));
        var directThread = Record(Thread, ("sprk_threadtype", 100000001), ("sprk_regardingmatter", matter));
        var ownParent = Record(Communication, ("sprk_regardingmatter", matter), ("sprk_communicationthread", recordThread));
        var threadOnly = Record(Communication, ("sprk_communicationthread", recordThread));
        var inDirect = Record(Communication, ("sprk_communicationthread", directThread));

        var read = await ReadAsync(Matter, matter);

        read.Members.Single(m => m.Id == ownParent).Parent.Should().Be(new RecordFamilyRecordRef(Matter, matter));
        read.Members.Single(m => m.Id == threadOnly).Parent.Should().Be(new RecordFamilyRecordRef(Thread, recordThread));
        read.Members.Select(m => m.Id).Should().Contain(directThread).And.NotContain(inDirect);
    }

    // ── Fixture ──

    private RecordFamilyReader Reader() =>
        new(_dv, _keys, NullLogger<RecordFamilyReader>.Instance);

    private Task<RecordFamilyRead> ReadAsync(
        string table, Guid id, string? continuation = null, RecordFamilyCaller? caller = null,
        RecordFamilyDirection direction = RecordFamilyDirection.Full) =>
        Reader().ReadAsync(caller ?? Caller, FamilyKind.Filing, table, id, direction, continuation, CancellationToken.None);

    private Guid NewId()
    {
        var n = ++_next;
        return new Guid($"{n:x8}-0000-4000-8000-{n:x12}");
    }

    private Guid Record(string table, params (string Column, object? Value)[] values)
    {
        var id = NewId();
        _dv.Add(table, id, OrdinaryBu, values);
        return id;
    }

    private Guid SecureRecord(string table, bool shared, params (string Column, object? Value)[] values)
    {
        var id = NewId();
        _dv.Add(table, id, SecureBu, values);
        if (shared)
            _dv.SharedWithCaller.Add(id);
        return id;
    }

    private void PairUnder(Guid project, Guid type, Guid parent)
    {
        _dv.Set(project, "sprk_regardingrecordid", parent.ToString("D"));
        _dv.Set(project, "sprk_regardingrecordtype", type);
    }

    /// <summary>
    /// The caller-scoped seam, in memory: answers the OData the reader sends with only the rows the caller may read — an
    /// ordinary row, or a row in the Secure Record business unit (UAC's predicate) that is shared with the caller. A
    /// field-secured column comes back null, as Dataverse returns it to an impersonated caller without the field profile.
    /// </summary>
    private sealed class FakeCallerScopedDataverse : IImpersonatedCommunicationQuery
    {
        private static readonly IReadOnlyDictionary<string, string> TableBySet = new Dictionary<string, string>
        {
            ["sprk_matters"] = Matter, ["sprk_projects"] = Project, ["sprk_documents"] = Document,
            ["sprk_fileversions"] = FileVersion, ["sprk_signals"] = Signal, ["sprk_decisionrecords"] = DecisionRecord,
            ["sprk_communications"] = Communication, ["sprk_communicationthreads"] = Thread,
            ["sprk_recordtype_refs"] = "sprk_recordtype_ref",
        };

        private static readonly Regex LookupTerm = new(@"^_(\w+)_value eq ([0-9a-f-]{36})$", RegexOptions.IgnoreCase);
        private static readonly Regex IdTerm = new(@"^(\w+)id eq ([0-9a-f-]{36})$", RegexOptions.IgnoreCase);
        private static readonly Regex ContainsTerm = new(@"^contains\((\w+),'([0-9a-f]+)'\)$", RegexOptions.IgnoreCase);

        private readonly List<(string Table, Guid Id, Guid Bu, Dictionary<string, object?> Values)> _rows = new();

        public List<(string EntitySet, string Query, Guid CallerSystemUserId)> Calls { get; } = new();
        public HashSet<Guid> SharedWithCaller { get; } = new();
        public HashSet<Guid> Unreadable { get; } = new();
        public HashSet<(string Table, string Column)> FieldSecured { get; } = new();

        public void Add(string table, Guid id, Guid bu, params (string Column, object? Value)[] values)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["statuscode"] = 1 };
            foreach (var (column, value) in values)
                row[column] = value;
            _rows.Add((table, id, bu, row));
        }

        public void Set(Guid id, string column, object? value) => _rows.Single(r => r.Id == id).Values[column] = value;

        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
            string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct)
        {
            if (callerSystemUserId == Guid.Empty)
                throw new ArgumentException("an impersonated read needs the caller", nameof(callerSystemUserId));
            Calls.Add((entitySetName, odataQuery ?? string.Empty, callerSystemUserId));

            var options = (odataQuery ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : string.Empty);
            if (!TableBySet.TryGetValue(entitySetName, out var table))
                return Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>(Array.Empty<Dictionary<string, JsonElement>>());

            var rows = _rows.Where(r => r.Table == table && Readable(r.Id, r.Bu));
            if (options.TryGetValue("$filter", out var filter))
                rows = rows.Where(r => Matches(table, r.Id, r.Values, filter));
            if (options.TryGetValue("$orderby", out var order))
            {
                order.Should().Be($"{table}id asc");
                rows = rows.OrderBy(r => r.Id);
            }

            if (options.TryGetValue("$top", out var top))
                rows = rows.Take(int.Parse(top, System.Globalization.CultureInfo.InvariantCulture));

            var select = options["$select"].Split(',');
            IReadOnlyList<Dictionary<string, JsonElement>> result = rows.Select(r => ProjectRow(table, r.Id, r.Values, select)).ToList();
            return Task.FromResult(result);
        }

        private bool Readable(Guid id, Guid bu) =>
            !Unreadable.Contains(id)
            && SecureRecordOwnerTeam.IsInSecureBusinessUnit(bu, SecureBu) switch
            {
                false => true,
                true => SharedWithCaller.Contains(id),
                null => false,
            };

        private static bool Matches(string table, Guid id, Dictionary<string, object?> values, string filter)
        {
            var body = filter.StartsWith('(') && filter.EndsWith(')') ? filter[1..^1] : filter;
            return body.Split(" or ").Any(term =>
            {
                if (IdTerm.Match(term) is { Success: true } idTerm)
                {
                    idTerm.Groups[1].Value.Should().Be(table);
                    return id == Guid.Parse(idTerm.Groups[2].Value);
                }

                if (LookupTerm.Match(term) is { Success: true } lookup)
                    return values.TryGetValue(lookup.Groups[1].Value, out var v) && v is Guid g && g == Guid.Parse(lookup.Groups[2].Value);
                if (ContainsTerm.Match(term) is { Success: true } contains)
                    return values.TryGetValue(contains.Groups[1].Value, out var v) && v is string s
                           && s.Contains(contains.Groups[2].Value, StringComparison.OrdinalIgnoreCase);
                throw new InvalidOperationException($"The fake does not understand the filter term '{term}'.");
            });
        }

        private Dictionary<string, JsonElement> ProjectRow(string table, Guid id, Dictionary<string, object?> values, string[] select)
        {
            var row = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in select)
            {
                if (string.Equals(column, table + "id", StringComparison.OrdinalIgnoreCase))
                {
                    row[column] = JsonSerializer.SerializeToElement(id.ToString("D"));
                    continue;
                }

                var logical = column.StartsWith('_') && column.EndsWith("_value", StringComparison.Ordinal)
                    ? column[1..^"_value".Length]
                    : column;
                var value = FieldSecured.Contains((table, logical)) ? null : values.GetValueOrDefault(logical);
                row[column] = value switch
                {
                    Guid g => JsonSerializer.SerializeToElement(g.ToString("D")),
                    null => JsonSerializer.SerializeToElement<object?>(null),
                    _ => JsonSerializer.SerializeToElement(value),
                };
                if (logical == "statuscode" && value is not null)
                    row[column + "@OData.Community.Display.V1.FormattedValue"] = JsonSerializer.SerializeToElement("Active");
            }

            return row;
        }
    }
}
