using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
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
    private const string Agreement = "sprk_agreement";
    private const string Policy = "sprk_policy";
    private const string PolicyVersion = "sprk_policyversion";
    private const string ServiceRequest = "sprk_servicerequest";
    private const string WorkAssignment = "sprk_workassignment";
    private const string Invoice = "sprk_invoice";

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
        // A readable row whose PRIMARY is the hidden document (sprk_regardingdocument ranks first on an agreement) and which
        // ALSO carries a filing lookup to the matter: it is in the hidden document's family, never listed under the matter.
        var primaryHidden = withBranch.Record(Agreement, ("sprk_regardingdocument", hiddenDoc), ("sprk_regardingmatter", matter));

        // The same family with that branch (the hidden document and everything below it) never created.
        var withoutBranch = new RecordFamilyReaderTests();
        var matter2 = withoutBranch.SecureRecord(Matter, shared: true, ("sprk_mattername", "Secure matter"));
        var readableDoc2 = withoutBranch.SecureRecord(Document, shared: true, ("sprk_matter", matter2), ("sprk_documentname", "Readable"));
        withoutBranch.Record(FileVersion, ("sprk_document", readableDoc2));
        matter2.Should().Be(matter);
        readableDoc2.Should().Be(readableDoc);

        var seen = await withBranch.ReadAsync(Matter, matter);
        var expected = await withoutBranch.ReadAsync(Matter, matter2);

        seen.Members.Select(m => m.Id).Should().NotContain(belowHidden.Append(hiddenDoc).Append(primaryHidden));
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
    public async Task ReadAsync_SignalFiledUnderAMatterAndItsDecisionRecord_IsListedOnceUnderItsPrimary()
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

    [Fact]
    public async Task ReadAsync_RecordReachableAgainDeeperThroughPrimaryEdges_IsKeptOnceAtItsShallowestDepth()
    {
        // Each record has ONE primary parent, so a second, deeper path exists only through a cycle of primary edges (notes,
        // "Dedupe"). Bad data: each document's parent document is the other.
        var a = NewId();
        var b = NewId();
        _dv.Add(Document, a, OrdinaryBu, ("sprk_parentdocument", b));
        _dv.Add(Document, b, OrdinaryBu, ("sprk_parentdocument", a));

        var full = await ReadAsync(Document, a);
        var down = await ReadAsync(Document, a, direction: RecordFamilyDirection.Down);

        // Full: b is the top (depth 0) and is reached again below a, at depth 2; a is at depth 1.
        full.Members.Select(m => (m.Id, m.Depth)).Should().Equal((b, 0), (a, 1));
        // Down: a is the top (depth 0) and is reached again below b, at depth 2; b is at depth 1.
        down.Members.Select(m => (m.Id, m.Depth)).Should().Equal((a, 0), (b, 1));
        down.Members.Single(m => m.Id == b).Parent.Should().Be(new RecordFamilyRecordRef(Document, a));
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

    // ── Fix round 1: a table the caller holds no Read privilege on is no rows; every other refusal still fails ──

    [Fact]
    public async Task ReadAsync_ChildTableTheCallerHoldsNoReadPrivilegeOn_ReturnsTheFamilyWithoutThatTable()
    {
        var matter = Record(Matter);
        var document = Record(Document, ("sprk_matter", matter));
        var policy = Record(Policy, ("sprk_matter", matter));
        var version = Record(PolicyVersion, ("sprk_policy", policy));

        // Seen on dev (Test User 1: Core + Basic + Office Add In User): no prvRead on the policy version table.
        _dv.NoReadPrivilegeOn(PolicyVersion, "sprk_PolicyVersion");
        var withoutVersions = await ReadAsync(Matter, matter);

        // ... and none on the policy table: its versions are not reached at all.
        _dv.NoReadPrivilegeOn(Policy, "sprk_Policy");
        var withoutPolicies = await ReadAsync(Matter, matter);

        withoutVersions.Outcome.Should().Be(RecordFamilyReadOutcome.Read);
        withoutVersions.Members.Select(m => m.Id).Should().BeEquivalentTo(new[] { matter, document, policy });
        withoutVersions.Truncated.Should().BeFalse("a table the caller may not read hides nothing the caller can read");
        withoutPolicies.Members.Select(m => m.Id).Should().BeEquivalentTo(new[] { matter, document });
        withoutPolicies.Members.Select(m => m.Id).Should().NotContain(version);
    }

    [Fact]
    public async Task ReadAsync_RefusedTableQueriedInSeveralChunksOfOneRead_IsAskedOnceNotPerChunk()
    {
        // 51 documents give the file-version edge two chunks of parent ids (50 + 1) in ONE read: only the remembered refusal
        // keeps the second chunk from asking again.
        var matter = Record(Matter);
        var documents = Enumerable.Range(0, RecordFamilyReader.ParentIdsPerQuery + 1)
            .Select(_ => Record(Document, ("sprk_matter", matter))).ToList();
        foreach (var document in documents)
            Record(FileVersion, ("sprk_document", document));
        RecordFamilyEdges.Of(FamilyKind.Filing).Should().Contain(e => e.ChildTable == FileVersion && e.ParentTable == Document);
        var fileVersionSet = SecureChildLineage.Children[FileVersion].EntitySet;

        var whenReadable = await ReadAsync(Matter, matter);
        var readableCalls = _dv.Calls.Count(c => c.EntitySet == fileVersionSet);
        _dv.Calls.Clear();
        _dv.NoReadPrivilegeOn(FileVersion, "sprk_FileVersion");
        var whenRefused = await ReadAsync(Matter, matter);

        readableCalls.Should().Be(2, "the control: without a refusal both chunks are queried");
        whenReadable.Members.Select(m => m.Table).Should().Contain(FileVersion);
        whenRefused.Members.Select(m => m.Table).Should().NotContain(FileVersion);
        whenRefused.Members.Select(m => m.Id).Should().BeEquivalentTo(documents.Append(matter));
        _dv.Calls.Count(c => c.EntitySet == fileVersionSet).Should().Be(1, "the refused table is asked once per read, not per chunk");
    }

    [Fact]
    public async Task ReadAsync_AnchorOrAncestorInATableTheCallerHoldsNoReadPrivilegeOn_IsNotReadable()
    {
        var matter = Record(Matter);
        var policy = Record(Policy, ("sprk_matter", matter));
        var version = Record(PolicyVersion, ("sprk_policy", policy));
        _dv.NoReadPrivilegeOn(Policy, "sprk_Policy");

        var anchoredOnPolicy = await ReadAsync(Policy, policy);
        var fromVersion = await ReadAsync(PolicyVersion, version);

        anchoredOnPolicy.Outcome.Should().Be(RecordFamilyReadOutcome.AnchorNotFound, "an anchor the caller cannot read, one answer");
        anchoredOnPolicy.Members.Should().BeEmpty();
        fromVersion.Outcome.Should().Be(RecordFamilyReadOutcome.Read);
        fromVersion.Top!.Id.Should().Be(version, "the up-walk stops below a parent the caller cannot read");
        fromVersion.Members.Select(m => m.Id).Should().Equal(version);
    }

    [Theory]
    [InlineData("impersonation privilege")]
    [InlineData("impersonation privilege, naming the table too")]
    [InlineData("another table's Read privilege")]
    [InlineData("another error code")]
    [InlineData("no error body")]
    [InlineData("a bare 403")]
    [InlineData("a 500")]
    public async Task ReadAsync_AnyOtherRefusalOrFault_StillThrows(string fault)
    {
        var matter = Record(Matter);
        Record(Policy, ("sprk_matter", matter));
        var who = $"Principal user (Id={Caller.SystemUserId:D}, type=8, roleCount=3, privilegeCount=812, accessMode=0)";
        Exception thrown = fault switch
        {
            "impersonation privilege" => new DataverseForbiddenException(
                DataverseWebApiService.AccessCheckDeniedErrorCode, $"{who}, is missing prvActOnBehalfOfAnotherUser privilege"),
            "impersonation privilege, naming the table too" => new DataverseForbiddenException(
                DataverseWebApiService.AccessCheckDeniedErrorCode,
                $"{who}, is missing prvActOnBehalfOfAnotherUser privilege; prvReadsprk_Policy was not checked"),
            "another table's Read privilege" => new DataverseForbiddenException(
                DataverseWebApiService.AccessCheckDeniedErrorCode, $"{who}, is missing prvReadsprk_PolicyVersion privilege"),
            "another error code" => new DataverseForbiddenException("0x8004A110", $"{who}, is missing prvReadsprk_Policy privilege"),
            "no error body" => new DataverseForbiddenException(null, null),
            "a bare 403" => new HttpRequestException(
                "Response status code does not indicate success: 403 (Forbidden).", null, HttpStatusCode.Forbidden),
            "a 500" => new HttpRequestException(
                "Response status code does not indicate success: 500 (Internal Server Error).", null, HttpStatusCode.InternalServerError),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        _dv.Faults[Policy] = thrown;

        var act = () => ReadAsync(Matter, matter);

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.Should().BeSameAs(thrown);
    }

    // ── Fix round 1: "load more" reaches every member of an edge with more rows than one request returns ──

    [Fact]
    public async Task ReadAsync_EdgeWithMoreRowsThanOneRequestReturns_LoadMoreReturnsEveryMemberExactlyOnce()
    {
        var matter = Record(Matter);
        var documents = Enumerable.Range(0, RecordFamilyReader.MaxRowsPerQuery + 1)
            .Select(_ => Record(Document, ("sprk_matter", matter)))
            .ToList();

        var pages = new List<RecordFamilyRead> { await ReadAsync(Matter, matter) };
        while (pages[^1].Continuation is { } next)
        {
            pages.Count.Should().BeLessThan(20, "the continuation must end");
            pages.Add(await ReadAsync(Matter, matter, continuation: next));
        }

        pages.Should().AllSatisfy(p => p.Outcome.Should().Be(RecordFamilyReadOutcome.Read));
        pages[^1].Truncated.Should().BeFalse("the last page leaves nothing out");
        var all = pages.SelectMany(p => p.Members).Select(m => m.Id).ToList();
        all.Should().OnlyHaveUniqueItems("no member is returned twice");
        all.Should().BeEquivalentTo(documents.Append(matter), "and every one is returned");
        _dv.Calls.Should().Contain(c => c.Query.Contains("sprk_documentid gt ", StringComparison.Ordinal),
            "a full request is followed by the next, keyed by id");
    }

    // ── Fix round 2: only the first request of an edge is sized to what is needed; the rest read to the end of the edge ──

    [Fact]
    public async Task ReadAsync_EdgeWithManyRowsFiledUnderAnotherParent_CostsAFewRequestsNotOnePerSmallPage()
    {
        var matter = Record(Matter);
        // Agreements regarding the matter whose PRIMARY parent is a document outside the family: each is read and skipped.
        var elsewhere = NewId();
        const int skipped = 10 * RecordFamilyReader.MaxMembers;
        for (var i = 0; i < skipped; i++)
            Record(Agreement, ("sprk_regardingdocument", elsewhere), ("sprk_regardingmatter", matter));
        var keptAgreement = Record(Agreement, ("sprk_regardingmatter", matter));

        var read = await ReadAsync(Matter, matter);

        read.Members.Select(m => m.Id).Should().BeEquivalentTo(new[] { matter, keptAgreement });
        read.Truncated.Should().BeFalse();
        var agreementRequests = _dv.Calls.Count(c => c.EntitySet == SecureChildLineage.Children[Agreement].EntitySet
                                                     && c.Query.Contains("_sprk_regardingmatter_value eq", StringComparison.Ordinal));
        agreementRequests.Should().BeLessThanOrEqualTo(3,
            "the rows after the first request are read in full pages, not needed()+1 at a time");
    }

    // ── Fix round 1: the primary parent is UAC 194b's, compared through 194b's own pure members ──

    [Fact]
    public async Task ReadAsync_PrimaryParent_IsTheOneUac194bsOrderingYieldsForTheSameRows()
    {
        var matter = Record(Matter);
        var serviceRequest = Record(ServiceRequest);

        // A Signal with a core record (a project, stamped) and a subject (a document).
        var core = Record(Project);
        var subject = Record(Document, ("sprk_matter", matter));
        var signal = Record(Signal,
            ("sprk_corerecordtype", ProjectType), ("sprk_corerecordid", core.ToString("D")), ("sprk_regardingproject", core),
            ("sprk_regardingdocument", subject), ("sprk_regardingrecordid", subject.ToString("D")));
        var signal194b = Sprk.Bff.Api.Services.Signals.SignalWriter.OwnershipContextOf(
            Project, core, Array.Empty<CoreAncestorStamp>(), Document, subject, requestedBy: null).TargetRecordId;

        // A message in a record thread regarding the matter, also regarding a service request (a column LookupOrder does not
        // name, so it ranks after the thread); and one in a Direct thread regarding the same service request.
        var thread = Record(Thread, ("sprk_threadtype", 100000000), ("sprk_regardingmatter", matter));
        var message = Record(Communication, ("sprk_communicationthread", thread), ("sprk_regardingservicerequest", serviceRequest));
        var direct = Record(Thread, ("sprk_threadtype", RecordThreadFiling.DirectThreadType));
        var inDirect = Record(Communication, ("sprk_communicationthread", direct), ("sprk_regardingservicerequest", serviceRequest));
        Guid? Message194b(Guid id, Guid threadId, int threadType)
        {
            var threadRow = threadType == RecordThreadFiling.DirectThreadType
                ? Row(Thread, threadId)
                : Row(Thread, threadId, ("sprk_regardingmatter", Matter, matter));
            threadRow[RecordThreadFiling.ThreadTypeColumn] = new OptionSetValue(threadType);
            var messageRow = Row(Communication, id,
                ("sprk_communicationthread", Thread, threadId), ("sprk_regardingservicerequest", ServiceRequest, serviceRequest));
            return RecordThreadFiling.QuestionWithThreadFiling(messageRow, RecordThreadFiling.ParentsOf(threadRow)).TargetRecordId;
        }

        // A work assignment filed under an invoice and a matter.
        var invoice = Record(Invoice, ("sprk_matter", matter));
        var assignment = Record(WorkAssignment, ("sprk_regardinginvoice", invoice), ("sprk_regardingmatter", matter));
        // 194b's own ordering since UAC #2451 (owner ruling 2026-10-29: a work assignment's owner follows its matter).
        // Before #2451, OrderedParentsOf ordered a work assignment's lookups by column name and picked the invoice.
        var assignment194b = RecordOwnershipContext.OrderedParentsOf(
            Row(WorkAssignment, assignment, ("sprk_regardinginvoice", Invoice, invoice), ("sprk_regardingmatter", Matter, matter)))
            .First().RecordId;

        // A project, whose one filing column is its regarding pair (RootLookupOrder).
        var project = Record(Project);
        PairUnder(project, MatterType, matter);
        Sprk.Bff.Api.Services.Access.SecureChildLineage.RootLookupOrder[Project].Should().Equal(RecordFamilyEdges.PairIdColumn);

        var expected = new (Guid Row, Guid? Primary194b)[]
        {
            (signal, signal194b),
            (message, Message194b(message, thread, 100000000)),
            (inDirect, Message194b(inDirect, direct, RecordThreadFiling.DirectThreadType)),
            (assignment, assignment194b),
            (project, matter),
        };
        expected.Select(e => e.Primary194b).Should().Equal(new Guid?[] { core, matter, serviceRequest, matter, matter }, "194b's own answers");

        foreach (var (row, primary194b) in expected)
        {
            var read = await ReadAsync(_dv.TableOf(row), row);
            FiledUnder(read, row).Should().Be(primary194b, "the reader's primary for {0} is 194b's", _dv.TableOf(row));
        }
    }

    /// <summary>The record a member is filed under, as 194b names it: a message listed under a thread is filed under the
    /// thread's own primary (RecordThreadFiling).</summary>
    private static Guid? FiledUnder(RecordFamilyRead read, Guid id)
    {
        var parent = read.Members.Single(m => m.Id == id).Parent;
        return parent?.Table == Thread ? read.Members.Single(m => m.Id == parent.Id).Parent?.Id : parent?.Id;
    }

    private static Entity Row(string table, Guid id, params (string Column, string Target, Guid Id)[] lookups)
    {
        var row = new Entity(table, id);
        foreach (var (column, target, value) in lookups)
            row[column] = new EntityReference(target, value);
        return row;
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
    /// field-secured column comes back null, as Dataverse returns it to an impersonated caller without the field profile. A
    /// table the caller holds no Read privilege on answers Dataverse's 403 for the whole query. An entity set that is not a
    /// family table's throws: the fake never answers "no rows" for a set it does not know.
    /// </summary>
    private sealed class FakeCallerScopedDataverse : IImpersonatedCommunicationQuery
    {
        /// <summary>Every family table (the Filing edges' tables and the cores), by its entity set, and the record-type catalog.</summary>
        private static readonly IReadOnlyDictionary<string, string> TableBySet = RecordFamilyEdges.Of(FamilyKind.Filing)
            .SelectMany(e => new[] { e.ChildTable, e.ParentTable })
            .Concat(RecordFamilyEdges.CoreTables)
            .Distinct(StringComparer.Ordinal)
            .Select(t => (Table: t, Set: SecureChildLineage.Children.TryGetValue(t, out var child) ? child.EntitySet : RegardingNameFields.EntitySetName(t)))
            .Where(t => t.Set is not null)
            .Append((Table: "sprk_recordtype_ref", Set: (string?)"sprk_recordtype_refs"))
            .ToDictionary(t => t.Set!, t => t.Table, StringComparer.Ordinal);

        private static readonly Regex AfterTerm = new(@"^(\(.*\)) and (\w+)id gt ([0-9a-f-]{36})$", RegexOptions.IgnoreCase);
        private static readonly Regex LookupTerm = new(@"^_(\w+)_value eq ([0-9a-f-]{36})$", RegexOptions.IgnoreCase);
        private static readonly Regex IdTerm = new(@"^(\w+)id eq ([0-9a-f-]{36})$", RegexOptions.IgnoreCase);
        private static readonly Regex ContainsTerm = new(@"^contains\((\w+),'([0-9a-f]+)'\)$", RegexOptions.IgnoreCase);

        private readonly List<(string Table, Guid Id, Guid Bu, Dictionary<string, object?> Values)> _rows = new();

        public List<(string EntitySet, string Query, Guid CallerSystemUserId)> Calls { get; } = new();
        public HashSet<Guid> SharedWithCaller { get; } = new();
        public HashSet<Guid> Unreadable { get; } = new();
        public HashSet<(string Table, string Column)> FieldSecured { get; } = new();

        /// <summary>What a query of a table throws instead of answering.</summary>
        public Dictionary<string, Exception> Faults { get; } = new(StringComparer.Ordinal);

        /// <summary>The caller holds no Read privilege on <paramref name="table"/>: Dataverse's 403 as seen on dev, naming the
        /// privilege by the table's schema name.</summary>
        public void NoReadPrivilegeOn(string table, string schemaName) =>
            Faults[table] = new DataverseForbiddenException(
                DataverseWebApiService.AccessCheckDeniedErrorCode,
                $"Principal user (Id={Caller.SystemUserId:D}, type=8, roleCount=3, privilegeCount=812, accessMode=0), is missing "
                + $"prvRead{schemaName} privilege (Id=7a1e9d1c-0000-4000-8000-000000000001) on OTC=10234 for entity '{table}'.");

        public string TableOf(Guid id) => _rows.Single(r => r.Id == id).Table;

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
                throw new InvalidOperationException($"The fake knows no family table for the entity set '{entitySetName}'.");
            if (Faults.TryGetValue(table, out var fault))
                throw fault;

            var rows = _rows.Where(r => r.Table == table && Readable(r.Id, r.Bu));
            if (options.TryGetValue("$filter", out var filter))
            {
                if (AfterTerm.Match(filter) is { Success: true } after)
                {
                    after.Groups[2].Value.Should().Be(table);
                    var last = Guid.Parse(after.Groups[3].Value);
                    filter = after.Groups[1].Value;
                    rows = rows.Where(r => r.Id.CompareTo(last) > 0);
                }

                rows = rows.Where(r => Matches(table, r.Id, r.Values, filter));
            }
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
