// unified-access-control-r2 Task 038 — NoAccessListReader tests (spec FR-23 deny-list store/reader).
//
// Covers exactly the task's closed acceptance-criteria set (testing.md §2b — an unqualified "write
// tests" is an open instruction inside a closed contract; the criteria below name what's in scope):
//   - contact x organization denies every candidate whose referenced-org set contains the org, ANY
//     reference (the over-match asymmetry, not conferring-only)
//   - contact x record denies exactly that record (per-child revocation); organization-subject
//     variants of BOTH shapes covered
//   - a deactivated entry denies nothing (statecode eq 0 is a SERVER-SIDE filter clause — verified
//     as a pure predicate, since the query seam below is overridden wholesale in tests)
//   - a faulted read denies ALL queried candidates, fail-closed, never an empty result
//   - a subject with no entries yields zero denials (no false walls)
// Plus the structurally-necessary edges the reader's own contract implies: no-subject/no-candidate
// short-circuits (never query), ambiguous-object-shape rows (never silently expand a deny), and
// multi-entry provenance accumulation. Task 142 round 18 replaced the defensive subject-size ceiling
// (more than 25 organizations / 5 contacts failed closed without querying) with subject-side chunking:
// any set is evaluated, the per-chunk matches unioned, and a fault in any chunk fails the whole answer.
//
// Module-boundary substitute only: a subclass overriding NoAccessListReader's internal-virtual
// QueryChunkAsync seam (InternalsVisibleTo, matching the ExternalParticipationService /
// ThrowingFlagParticipationService precedent in AccessibleRecordSetServiceTests.cs) — never
// Mock<HttpMessageHandler> (banned, testing.md B1).

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public class NoAccessListReaderTests
{
    private static readonly Guid Contact = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherContact = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SubjectOrg = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DeniedOrg = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherOrg = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid RecordA = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid RecordB = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid EntryId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid EntryId2 = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid RecordTypeRef = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // ── Ethical wall: contact x organization, ANY reference (over-match asymmetry) ──────────────

    [Fact]
    public async Task GetDeniedRecordsAsync_ContactSubjectOrganizationObject_DeniesEveryRecordReferencingThatOrganization()
    {
        var deniedRow = OrganizationObjectRow(EntryId, subjectContact: Contact, objectOrg: DeniedOrg);
        var sut = FakeNoAccessListReader.ReturningRows(orgLoop: new() { deniedRow }, recordLoop: new());

        var candidates = new[]
        {
            // References the denied org only as a NON-conferring participant (e.g. opposing
            // counsel) -- the reader does not know or care why the org is referenced; ANY
            // reference over-matches by design (spec FR-23 / register B-10).
            new NoAccessCandidateRecord("sprk_matter", RecordA, new[] { DeniedOrg }),
            new NoAccessCandidateRecord("sprk_matter", RecordB, new[] { OtherOrg }),
        };

        var result = await sut.GetDeniedRecordsAsync(Contact, Array.Empty<Guid>(), candidates, CancellationToken.None);

        result.FailedClosed.Should().BeFalse();
        result.DeniedRecordIds.Should().BeEquivalentTo(new[] { RecordA },
            "RecordA references the denied organization; RecordB does not reference it at all");
        result.DenyingEntryIds[RecordA].Should().Equal(EntryId);
    }

    // ── Per-child revocation: contact x record ───────────────────────────────────────────────────

    [Fact]
    public async Task GetDeniedRecordsAsync_ContactSubjectRecordObject_DeniesExactlyThatRecord()
    {
        var deniedRow = RecordObjectRow(EntryId, subjectContact: Contact, objectRecordId: RecordA);
        var sut = FakeNoAccessListReader.ReturningRows(orgLoop: new(), recordLoop: new() { deniedRow });

        var candidates = new[]
        {
            new NoAccessCandidateRecord("sprk_communication", RecordA, Array.Empty<Guid>()),
            new NoAccessCandidateRecord("sprk_communication", RecordB, Array.Empty<Guid>()),
        };

        var result = await sut.GetDeniedRecordsAsync(Contact, Array.Empty<Guid>(), candidates, CancellationToken.None);

        result.DeniedRecordIds.Should().BeEquivalentTo(new[] { RecordA },
            "the revocation names RecordA specifically; the parent/sibling RecordB is unaffected");
    }

    // ── Organization-subject variants of both shapes ─────────────────────────────────────────────

    [Fact]
    public async Task GetDeniedRecordsAsync_OrganizationSubjectOrganizationObject_DeniesReferencingRecords()
    {
        var deniedRow = OrganizationObjectRow(EntryId, subjectOrg: SubjectOrg, objectOrg: DeniedOrg);
        var sut = FakeNoAccessListReader.ReturningRows(orgLoop: new() { deniedRow }, recordLoop: new());

        var candidates = new[] { new NoAccessCandidateRecord("sprk_matter", RecordA, new[] { DeniedOrg }) };

        var result = await sut.GetDeniedRecordsAsync(
            contactId: null, organizationIds: new[] { SubjectOrg }, candidates, CancellationToken.None);

        result.DeniedRecordIds.Should().BeEquivalentTo(new[] { RecordA },
            "every active member of the subject organization is denied on records referencing the object organization");
    }

    [Fact]
    public async Task GetDeniedRecordsAsync_OrganizationSubjectRecordObject_DeniesExactlyThatRecord()
    {
        var deniedRow = RecordObjectRow(EntryId, subjectOrg: SubjectOrg, objectRecordId: RecordA);
        var sut = FakeNoAccessListReader.ReturningRows(orgLoop: new(), recordLoop: new() { deniedRow });

        var candidates = new[]
        {
            new NoAccessCandidateRecord("sprk_matter", RecordA, Array.Empty<Guid>()),
            new NoAccessCandidateRecord("sprk_matter", RecordB, Array.Empty<Guid>()),
        };

        var result = await sut.GetDeniedRecordsAsync(
            contactId: null, organizationIds: new[] { SubjectOrg }, candidates, CancellationToken.None);

        result.DeniedRecordIds.Should().BeEquivalentTo(new[] { RecordA });
    }

    // ── Deactivated entries: statecode eq 0 is a server-side filter clause ──────────────────────

    [Fact]
    public void CombineFilter_AlwaysAndsInActiveStatecodeOnly()
    {
        var filter = NoAccessListReader.CombineFilter("(subject)", "(object)");

        filter.Should().Be("(subject) and (object) and statecode eq 0",
            "a deactivated entry (statecode != 0) must never be returned by the Dataverse query -- " +
            "this is enforced server-side, not by client-side post-filtering");
    }

    // ── Fail-closed: faulted read denies ALL queried candidates, never an empty result ──────────

    [Fact]
    public async Task GetDeniedRecordsAsync_QueryThrows_ReturnsDenyAllQueriedFailClosed()
    {
        var sut = FakeNoAccessListReader.Throwing(new InvalidOperationException("simulated transport fault"));

        var candidates = new[]
        {
            new NoAccessCandidateRecord("sprk_matter", RecordA, Array.Empty<Guid>()),
            new NoAccessCandidateRecord("sprk_matter", RecordB, Array.Empty<Guid>()),
        };

        var result = await sut.GetDeniedRecordsAsync(Contact, Array.Empty<Guid>(), candidates, CancellationToken.None);

        result.FailedClosed.Should().BeTrue("an unreadable deny-list cannot prove 'not denied' (NFR-01)");
        result.DeniedRecordIds.Should().BeEquivalentTo(new[] { RecordA, RecordB },
            "every queried candidate is denied on fault -- never an empty 'nobody denied' result");
        result.DenyingEntryIds[RecordA].Should().BeEmpty("no real entry matched -- the denial is precautionary");
    }

    [Fact]
    public async Task GetDeniedRecordsAsync_QueryReturnsNull_ReturnsDenyAllQueriedFailClosed()
    {
        // Distinct from the throwing case: this exercises the "non-success HTTP status" branch,
        // where the real QueryChunkAsync logs and returns null rather than letting an exception
        // propagate. GetDeniedRecordsAsync must treat both as fail-closed identically.
        var sut = FakeNoAccessListReader.ReturningNull();

        var candidates = new[] { new NoAccessCandidateRecord("sprk_matter", RecordA, Array.Empty<Guid>()) };

        var result = await sut.GetDeniedRecordsAsync(Contact, Array.Empty<Guid>(), candidates, CancellationToken.None);

        result.FailedClosed.Should().BeTrue();
        result.DeniedRecordIds.Should().BeEquivalentTo(new[] { RecordA });
    }

    // ── No false walls: a subject with no matching entries yields zero denials ──────────────────

    [Fact]
    public async Task GetDeniedRecordsAsync_SubjectWithNoMatchingEntries_ReturnsZeroDenials()
    {
        var sut = FakeNoAccessListReader.ReturningRows(orgLoop: new(), recordLoop: new());

        var candidates = new[] { new NoAccessCandidateRecord("sprk_matter", RecordA, new[] { OtherOrg }) };

        var result = await sut.GetDeniedRecordsAsync(Contact, Array.Empty<Guid>(), candidates, CancellationToken.None);

        result.FailedClosed.Should().BeFalse("a real (empty) query result is a considered zero, not a fault");
        result.DeniedRecordIds.Should().BeEmpty();
    }

    // ── Short-circuits: no subject identity / no candidates never issue a query ──────────────────

    [Fact]
    public async Task GetDeniedRecordsAsync_NoSubjectIdentity_ReturnsEmptyWithoutQuerying()
    {
        var sut = FakeNoAccessListReader.Throwing(new InvalidOperationException("must not be called"));

        var result = await sut.GetDeniedRecordsAsync(
            contactId: null,
            organizationIds: Array.Empty<Guid>(),
            candidates: new[] { new NoAccessCandidateRecord("sprk_matter", RecordA, Array.Empty<Guid>()) },
            CancellationToken.None);

        result.Should().BeSameAs(NoAccessListResult.Empty);
    }

    [Fact]
    public async Task GetDeniedRecordsAsync_NoCandidates_ReturnsEmptyWithoutQuerying()
    {
        var sut = FakeNoAccessListReader.Throwing(new InvalidOperationException("must not be called"));

        var result = await sut.GetDeniedRecordsAsync(
            Contact, Array.Empty<Guid>(), Array.Empty<NoAccessCandidateRecord>(), CancellationToken.None);

        result.Should().BeSameAs(NoAccessListResult.Empty);
    }

    // ── Task 142 round 18: a subject set of ANY size is evaluated (chunked), never refused ─────────
    //
    // Before round 18 more than 25 organizations / 5 contacts failed closed WITHOUT querying — a deterministic "could
    // not be checked" that every consumer reported as a transient fault (a grant that "tries again" forever, an
    // Assigned-To job red for as long as the subject stayed assigned). Now the subject side is split into chunks within
    // one query's bound and the matches are unioned; only a genuine read fault (in any chunk) fails closed. These run
    // over TableNoAccessListReader, which answers each query the way Dataverse would — only the entries whose subject
    // AND object the query's filters name — so an entry is found only if its subject actually reached a query.

    private static Guid[] Ids(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();

    [Theory]
    [InlineData(26)] // one over a single query's bound: two subject chunks
    [InlineData(51)] // three chunks, the last holding one organization
    public async Task GetDeniedRecordsAsync_MoreOrganizationsThanOneQueryHolds_AreEvaluated_AnEntryOnTheLastIsFound(int count)
    {
        var organizations = Ids(count);
        var sut = new TableNoAccessListReader();
        sut.Add(RecordObjectRow(EntryId, subjectOrg: organizations[^1], objectRecordId: RecordB));

        var candidates = new[]
        {
            new NoAccessCandidateRecord("sprk_matter", RecordA, Array.Empty<Guid>()),
            new NoAccessCandidateRecord("sprk_matter", RecordB, Array.Empty<Guid>()),
        };

        var result = await sut.GetDeniedRecordsAsync(contactId: null, organizations, candidates, CancellationToken.None);

        result.FailedClosed.Should().BeFalse("a large subject set is evaluated, not refused (round 18)");
        result.DeniedRecordIds.Should().Equal(new[] { RecordB },
            "the entry names the LAST organization, which only the last subject chunk carries");
        result.DenyingEntryIds[RecordB].Should().Equal(EntryId);
        result.DenyingSubjectKinds[RecordB].Should().Be(NoAccessSubjectKinds.Organization);
        sut.SubjectFilters.Distinct().Should().HaveCount((count + NoAccessListReader.MaxSubjectOrganizationIds - 1)
            / NoAccessListReader.MaxSubjectOrganizationIds);
    }

    /// <summary>The twin: the same large set with no entry naming it is a considered zero, never a fail-closed answer.</summary>
    [Fact]
    public async Task GetDeniedRecordsAsync_MoreOrganizationsThanOneQueryHolds_WithNoEntry_IsAConsideredZero()
    {
        var sut = new TableNoAccessListReader();
        sut.Add(RecordObjectRow(EntryId, subjectOrg: Guid.NewGuid(), objectRecordId: RecordA)); // someone else's entry

        var result = await sut.GetDeniedRecordsAsync(contactId: null, Ids(30),
            new[] { new NoAccessCandidateRecord("sprk_matter", RecordA, Array.Empty<Guid>()) }, CancellationToken.None);

        result.FailedClosed.Should().BeFalse();
        result.DeniedRecordIds.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDeniedRecordsAsync_MoreContactsThanOneQueryHolds_AreEvaluated_AnEntryOnTheLastIsFound()
    {
        var contacts = Ids(NoAccessListReader.MaxSubjectContactIds + 2); // 7: two subject chunks
        var sut = new TableNoAccessListReader();
        sut.Add(OrganizationObjectRow(EntryId, subjectContact: contacts[^1], objectOrg: DeniedOrg)); // an ethical wall

        var candidates = new[]
        {
            new NoAccessCandidateRecord("sprk_matter", RecordA, new[] { DeniedOrg }),
            new NoAccessCandidateRecord("sprk_matter", RecordB, new[] { OtherOrg }),
        };

        var result = await sut.GetDeniedRecordsAsync(
            new NoAccessSubjects(contacts, Array.Empty<Guid>(), SystemUserId: null), candidates, CancellationToken.None);

        result.FailedClosed.Should().BeFalse("six or more contacts are evaluated, not refused (round 18)");
        result.DeniedRecordIds.Should().Equal(new[] { RecordA });
        result.DenyingSubjectKinds[RecordA].Should().Be(NoAccessSubjectKinds.Contact);
    }

    /// <summary>
    /// The union: entries found by DIFFERENT subject chunks on one record all count — both entry ids, every subject kind
    /// (the systemuser rides in the first chunk; the 30th organization only in the second).
    /// </summary>
    [Fact]
    public async Task GetDeniedRecordsAsync_EntriesFoundByDifferentSubjectChunks_AreUnioned()
    {
        var organizations = Ids(30);
        var sut = new TableNoAccessListReader();
        var bySystemUser = RecordObjectRow(EntryId, objectRecordId: RecordA);
        bySystemUser._sprk_subjectsystemuser_value = SystemUser;
        sut.Add(bySystemUser);
        sut.Add(RecordObjectRow(EntryId2, subjectOrg: organizations[^1], objectRecordId: RecordA));

        var result = await sut.GetDeniedRecordsAsync(
            new NoAccessSubjects(new[] { Contact }, organizations, SystemUser),
            new[] { new NoAccessCandidateRecord("sprk_project", RecordA, Array.Empty<Guid>()) },
            CancellationToken.None);

        result.FailedClosed.Should().BeFalse();
        result.DenyingEntryIds[RecordA].Should().BeEquivalentTo(new[] { EntryId, EntryId2 });
        result.DenyingSubjectKinds[RecordA].Should().Be(NoAccessSubjectKinds.SystemUser | NoAccessSubjectKinds.Organization);
    }

    /// <summary>
    /// A genuine read fault in ANY chunk still fails the WHOLE answer closed — even when an earlier chunk already found a
    /// real entry, the union so far is not all the denials. Both fault shapes: a non-success status (the seam's null) and
    /// a throw.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetDeniedRecordsAsync_AFaultInOneSubjectChunk_FailsTheWholeAnswerClosed(bool throws)
    {
        var organizations = Ids(30);
        var sut = new TableNoAccessListReader
        {
            // Only the SECOND subject chunk (the one carrying the 26th organization) faults.
            FaultWhenSubjectFilterNames = organizations[NoAccessListReader.MaxSubjectOrganizationIds],
            FaultThrows = throws,
        };
        sut.Add(RecordObjectRow(EntryId, subjectOrg: organizations[0], objectRecordId: RecordA)); // found by chunk 1

        var candidates = new[]
        {
            new NoAccessCandidateRecord("sprk_matter", RecordA, Array.Empty<Guid>()),
            new NoAccessCandidateRecord("sprk_matter", RecordB, Array.Empty<Guid>()),
        };

        var result = await sut.GetDeniedRecordsAsync(contactId: null, organizations, candidates, CancellationToken.None);

        result.FailedClosed.Should().BeTrue("one unreadable chunk means the answer cannot prove 'not denied' (NFR-01)");
        result.DeniedRecordIds.Should().BeEquivalentTo(new[] { RecordA, RecordB });
        result.DenyingEntryIds[RecordA].Should().BeEmpty("a fail-closed answer carries no provenance, even for a real match");
        sut.SubjectFilters.Should().Contain(f => f.Contains(organizations[0].ToString(), StringComparison.Ordinal),
            "the first chunk was evaluated before the second faulted");
    }


    /// <summary>
    /// Task 142 verifier, criterion 19 (batch 4 integration): the ETHICAL-WALL loop (Loop A — the referenced-organization
    /// object chunks) fails the WHOLE answer closed when one of its chunks answers no rows (<c>null</c>, a non-success
    /// status), exactly as the record loop does — even when the record loop answers cleanly and an EARLIER organization
    /// chunk already found a real entry. The record-object queries here succeed, so the only thing that can fail the answer
    /// is the faulted organization chunk: if that chunk were skipped (<c>continue</c>) instead of failing closed, the
    /// answer would read as complete and a wall on an organization in the unread chunk would never bind.
    /// </summary>
    [Fact]
    public async Task GetDeniedRecordsAsync_AnEthicalWallOrganizationChunkThatAnswersNothing_FailsTheWholeAnswerClosed()
    {
        // Two organization-object chunks: the first carries DeniedOrg (a real entry), the second faults.
        var referenced = new[] { DeniedOrg }.Concat(Ids(NoAccessListReader.ObjectIdChunkSize)).ToArray();
        var faultingOrg = referenced[NoAccessListReader.ObjectIdChunkSize]; // the first id of the SECOND chunk
        var sut = new TableNoAccessListReader { NullWhenObjectOrganizationNamed = faultingOrg };
        sut.Add(OrganizationObjectRow(EntryId, subjectContact: Contact, objectOrg: DeniedOrg));   // found by chunk 1
        sut.Add(OrganizationObjectRow(EntryId2, subjectContact: Contact, objectOrg: faultingOrg)); // in the unread chunk

        var candidates = new[]
        {
            new NoAccessCandidateRecord("sprk_matter", RecordA, new[] { DeniedOrg }),
            new NoAccessCandidateRecord("sprk_matter", RecordB, referenced.Skip(1).ToArray()),
        };

        var result = await sut.GetDeniedRecordsAsync(Contact, Array.Empty<Guid>(), candidates, CancellationToken.None);

        result.FailedClosed.Should().BeTrue(
            "an unread ethical-wall chunk means the answer cannot prove 'not denied' (NFR-01)");
        result.DeniedRecordIds.Should().BeEquivalentTo(new[] { RecordA, RecordB },
            "every queried candidate is denied — RecordB's wall sits in the chunk that was never read");
        result.DenyingEntryIds[RecordA].Should().BeEmpty("a fail-closed answer carries no provenance, even for a real match");
    }

    /// <summary>
    /// The bound itself: every query embeds at most <see cref="NoAccessListReader.MaxSubjectContactIds"/> contacts and
    /// <see cref="NoAccessListReader.MaxSubjectOrganizationIds"/> organizations, and across the queries every subject is
    /// asked about — each contact and organization in exactly one chunk, the systemuser once.
    /// </summary>
    [Fact]
    public async Task GetDeniedRecordsAsync_EveryQueryStaysWithinTheBound_AndEverySubjectIsAskedAbout()
    {
        var contacts = Ids(12);
        var organizations = Ids(60);
        var sut = new TableNoAccessListReader();

        await sut.GetDeniedRecordsAsync(
            new NoAccessSubjects(contacts, organizations, SystemUser),
            new[] { new NoAccessCandidateRecord("sprk_matter", RecordA, new[] { DeniedOrg }) },
            CancellationToken.None);

        var distinctFilters = sut.SubjectFilters.Distinct().ToList();
        distinctFilters.Should().HaveCount(3, "12 contacts need three chunks of 5; 60 organizations need three of 25");
        foreach (var filter in distinctFilters)
        {
            TableNoAccessListReader.IdsNamed(filter, "_sprk_subjectcontact_value").Should()
                .HaveCountLessThanOrEqualTo(NoAccessListReader.MaxSubjectContactIds);
            TableNoAccessListReader.IdsNamed(filter, "_sprk_subjectorganization_value").Should()
                .HaveCountLessThanOrEqualTo(NoAccessListReader.MaxSubjectOrganizationIds);
        }

        distinctFilters.SelectMany(f => TableNoAccessListReader.IdsNamed(f, "_sprk_subjectcontact_value"))
            .Should().BeEquivalentTo(contacts, o => o.WithoutStrictOrdering(), "each contact in exactly one chunk");
        distinctFilters.SelectMany(f => TableNoAccessListReader.IdsNamed(f, "_sprk_subjectorganization_value"))
            .Should().BeEquivalentTo(organizations, o => o.WithoutStrictOrdering(), "each organization in exactly one chunk");
        distinctFilters.SelectMany(f => TableNoAccessListReader.IdsNamed(f, "_sprk_subjectsystemuser_value"))
            .Should().Equal(new[] { SystemUser }, "the systemuser is asked about once");
    }

    [Fact]
    public void ChunkSubjects_WithinTheBound_IsOneChunk_TheSingleQueryShapeUnchanged()
    {
        var subjects = new NoAccessSubjects(Ids(NoAccessListReader.MaxSubjectContactIds),
            Ids(NoAccessListReader.MaxSubjectOrganizationIds), SystemUser);

        var chunks = NoAccessListReader.ChunkSubjects(subjects);

        chunks.Should().ContainSingle();
        NoAccessListReader.BuildSubjectFilter(chunks[0]).Should().Be(NoAccessListReader.BuildSubjectFilter(subjects));
    }

    // ── Malformed rows: ambiguous object shape never silently expands a deny ────────────────────

    [Fact]
    public async Task GetDeniedRecordsAsync_AmbiguousObjectShape_ExcludesRowFromMatching()
    {
        var malformedRow = new NoAccessEntryRow
        {
            sprk_noaccessentryid = EntryId,
            _sprk_subjectcontact_value = Contact,
            _sprk_objectorganization_value = DeniedOrg,   // BOTH populated -- ambiguous.
            _sprk_objectrecordtype_value = RecordTypeRef,
            sprk_objectrecordid = RecordA.ToString(),
        };
        var sut = FakeNoAccessListReader.ReturningRows(orgLoop: new() { malformedRow }, recordLoop: new() { malformedRow });

        var candidates = new[] { new NoAccessCandidateRecord("sprk_matter", RecordA, new[] { DeniedOrg }) };

        var result = await sut.GetDeniedRecordsAsync(Contact, Array.Empty<Guid>(), candidates, CancellationToken.None);

        result.DeniedRecordIds.Should().BeEmpty(
            "a row with both object fields populated is malformed and must be excluded, never treated as a match");
        result.FailedClosed.Should().BeFalse("this is a data-quality guard, not a read fault");
    }

    // ── Provenance: more than one matching entry accumulates without duplication ────────────────

    [Fact]
    public async Task GetDeniedRecordsAsync_MultipleMatchingEntries_AccumulatesAllEntryIdsInProvenance()
    {
        var directDeny = OrganizationObjectRow(EntryId, subjectContact: Contact, objectOrg: DeniedOrg);
        var orgDeny = OrganizationObjectRow(EntryId2, subjectOrg: SubjectOrg, objectOrg: DeniedOrg);
        var sut = FakeNoAccessListReader.ReturningRows(orgLoop: new() { directDeny, orgDeny }, recordLoop: new());

        var candidates = new[] { new NoAccessCandidateRecord("sprk_matter", RecordA, new[] { DeniedOrg }) };

        var result = await sut.GetDeniedRecordsAsync(Contact, new[] { SubjectOrg }, candidates, CancellationToken.None);

        result.DeniedRecordIds.Should().BeEquivalentTo(new[] { RecordA });
        result.DenyingEntryIds[RecordA].Should().BeEquivalentTo(new[] { EntryId, EntryId2 },
            "both the direct contact deny and the organization-membership deny matched the same record");
    }

    // ── Pure filter-builder regression pins ─────────────────────────────────────────────────────

    [Fact]
    public void BuildRecordObjectFilter_QuotesGuidsAsStringLiterals()
    {
        // sprk_objectrecordid is a TEXT column (ADR-024 resolver pair) -- an unquoted GUID would
        // be a numeric/lookup-shaped literal against a string column and would not match.
        var filter = NoAccessListReader.BuildRecordObjectFilter(new[] { RecordA });

        filter.Should().Be($"(sprk_objectrecordid eq '{RecordA}')");
    }

    [Fact]
    public void BuildSubjectFilter_ContactAndOrganizations_OrJoinsBothDimensions()
    {
        var filter = NoAccessListReader.BuildSubjectFilter(Contact, new[] { SubjectOrg });

        filter.Should().Be($"(_sprk_subjectcontact_value eq {Contact} or (_sprk_subjectorganization_value eq {SubjectOrg}))");
    }

    // ── Task 143: the systemuser subject, exactly-one-of-THREE, and subject-kind provenance ──────

    private static readonly Guid SystemUser = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void RowSelect_NamesTheSystemUserSubjectColumn()
    {
        // The deploy-order hazard: this column must exist live before this select ships (schema doc + PR order).
        NoAccessListReader.RowSelect.Split(',').Should().Contain("_sprk_subjectsystemuser_value");
    }

    [Fact]
    public void BuildSubjectFilter_AllThreeSubjects_OrJoinsEveryDimension()
    {
        var filter = NoAccessListReader.BuildSubjectFilter(
            new NoAccessSubjects(new[] { Contact }, new[] { SubjectOrg }, SystemUser));

        filter.Should().Be(
            $"(_sprk_subjectcontact_value eq {Contact} or (_sprk_subjectorganization_value eq {SubjectOrg}) or _sprk_subjectsystemuser_value eq {SystemUser})");
    }

    [Fact]
    public async Task GetDeniedRecordsAsync_ASystemUserSubjectRow_DeniesTheRecord_AndSaysASystemUserDeniedIt()
    {
        var row = RecordObjectRow(EntryId, objectRecordId: RecordA);
        row._sprk_subjectsystemuser_value = SystemUser;
        var sut = FakeNoAccessListReader.ReturningRows(orgLoop: new(), recordLoop: new() { row });

        var result = await sut.GetDeniedRecordsAsync(
            new NoAccessSubjects(Array.Empty<Guid>(), Array.Empty<Guid>(), SystemUser),
            new[] { new NoAccessCandidateRecord("sprk_project", RecordA, Array.Empty<Guid>()) },
            CancellationToken.None);

        result.DeniedRecordIds.Should().Equal(RecordA);
        result.DenyingSubjectKinds[RecordA].Should().Be(NoAccessSubjectKinds.SystemUser);
    }

    [Fact]
    public async Task GetDeniedRecordsAsync_AContactAndAnOrganizationEntryOnOneRecord_ReportBothSubjectKinds()
    {
        var direct = OrganizationObjectRow(EntryId, subjectContact: Contact, objectOrg: DeniedOrg);
        var viaFirm = OrganizationObjectRow(EntryId2, subjectOrg: SubjectOrg, objectOrg: DeniedOrg);
        var sut = FakeNoAccessListReader.ReturningRows(orgLoop: new() { direct, viaFirm }, recordLoop: new());

        var result = await sut.GetDeniedRecordsAsync(Contact, new[] { SubjectOrg },
            new[] { new NoAccessCandidateRecord("sprk_matter", RecordA, new[] { DeniedOrg }) }, CancellationToken.None);

        result.DenyingSubjectKinds[RecordA].Should().Be(NoAccessSubjectKinds.Contact | NoAccessSubjectKinds.Organization);
    }

    [Theory]
    [InlineData(true, false, true)]   // contact + systemuser
    [InlineData(false, true, true)]   // organization + systemuser
    [InlineData(true, true, false)]   // contact + organization
    [InlineData(false, false, false)] // no subject at all
    public async Task GetDeniedRecordsAsync_ARowWithNoneOrMoreThanOneSubject_IsMalformed_AndDeniesNothing(
        bool contact, bool organization, bool systemUser)
    {
        var row = RecordObjectRow(EntryId, objectRecordId: RecordA);
        row._sprk_subjectcontact_value = contact ? Contact : null;
        row._sprk_subjectorganization_value = organization ? SubjectOrg : null;
        row._sprk_subjectsystemuser_value = systemUser ? SystemUser : null;
        var sut = FakeNoAccessListReader.ReturningRows(orgLoop: new(), recordLoop: new() { row });

        var result = await sut.GetDeniedRecordsAsync(
            new NoAccessSubjects(new[] { Contact }, new[] { SubjectOrg }, SystemUser),
            new[] { new NoAccessCandidateRecord("sprk_project", RecordA, Array.Empty<Guid>()) },
            CancellationToken.None);

        result.DeniedRecordIds.Should().BeEmpty("exactly one of the three subjects is required (schema Business Rule 1)");
        result.FailedClosed.Should().BeFalse("a malformed row is a data-quality guard, not a read fault");
    }

    [Fact]
    public async Task GetDeniedRecordsAsync_ASystemUserSubjectAlone_IsSomethingToCheck_NotAnEmptyAnswer()
    {
        var sut = FakeNoAccessListReader.Throwing(new InvalidOperationException("the query ran"));

        var result = await sut.GetDeniedRecordsAsync(
            new NoAccessSubjects(Array.Empty<Guid>(), Array.Empty<Guid>(), SystemUser),
            new[] { new NoAccessCandidateRecord("sprk_project", RecordA, Array.Empty<Guid>()) },
            CancellationToken.None);

        result.FailedClosed.Should().BeTrue("a systemuser subject must reach the query — and a fault there denies");
    }

    // ── Task 154: the object record id must be in the form the record filter matches ────────────────

    [Theory]
    [InlineData("66666666-6666-6666-6666-666666666666", true)]   // canonical: what the form and the picker write
    [InlineData("ABCDEF01-2345-6789-ABCD-EF0123456789", true)]   // upper case: Dataverse string equality ignores case
    [InlineData("abcdef01-2345-6789-abcd-ef0123456789  ", true)] // trailing spaces: Dataverse equality ignores them
    [InlineData("{abcdef01-2345-6789-abcd-ef0123456789}", false)] // braces: never matched by the filter
    [InlineData("(abcdef01-2345-6789-abcd-ef0123456789)", false)]
    [InlineData("abcdef0123456789abcdef0123456789", false)]       // 32 digits, no hyphens
    [InlineData(" abcdef01-2345-6789-abcd-ef0123456789", false)]  // leading space: never matched
    [InlineData("00000000-0000-0000-0000-000000000000", false)]   // the empty id names no record
    [InlineData("not a record id", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParseObjectRecordId_AcceptsExactlyTheFormsTheRecordFilterMatches(string? raw, bool expected)
    {
        NoAccessListReader.TryParseObjectRecordId(raw, out var id).Should().Be(expected);
        if (expected)
        {
            id.ToString().Should().Be(raw!.Trim().ToLowerInvariant(), "the parsed id is the record the filter matched");
        }
        else
        {
            id.Should().Be(Guid.Empty);
        }
    }

    [Fact]
    public async Task GetDeniedRecordsAsync_ARecordIdWithBraces_IsMalformed_AndDeniesNothing()
    {
        // The pre-154 fail-open: Guid.TryParse accepted the braces, so the row counted as well-formed while no
        // string-equality filter could ever return it for the record it names. Now the row is malformed, as the
        // enforcer also says (NoAccessShareEnforcer), so the two never disagree about it.
        var row = RecordObjectRow(EntryId, subjectContact: Contact, objectRecordId: RecordA);
        row.sprk_objectrecordid = "{" + RecordA + "}";
        var sut = FakeNoAccessListReader.ReturningRows(orgLoop: new(), recordLoop: new() { row });

        var result = await sut.GetDeniedRecordsAsync(
            Contact, Array.Empty<Guid>(),
            new[] { new NoAccessCandidateRecord("sprk_matter", RecordA, Array.Empty<Guid>()) },
            CancellationToken.None);

        result.DeniedRecordIds.Should().BeEmpty("a braced id is not in the form the record filter matches");
        result.FailedClosed.Should().BeFalse("a malformed row is a data-quality guard, not a read fault");
    }

    [Fact]
    public async Task GetDeniedRecordsAsync_AnUpperCaseRecordId_StillDenies_BecauseDataverseMatchedIt()
    {
        // Dataverse string equality is case-insensitive, so the record filter returns an upper-case row. Rejecting it
        // here would turn a working wall into one that denies nothing.
        var table = new TableNoAccessListReader();
        var row = RecordObjectRow(EntryId, subjectContact: Contact, objectRecordId: RecordB);
        row.sprk_objectrecordid = RecordB.ToString().ToUpperInvariant();
        table.Add(row);

        var result = await table.GetDeniedRecordsAsync(
            Contact, Array.Empty<Guid>(),
            new[] { new NoAccessCandidateRecord("sprk_matter", RecordB, Array.Empty<Guid>()) },
            CancellationToken.None);

        result.DeniedRecordIds.Should().BeEquivalentTo(new[] { RecordB });
    }

    // ── Test double ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Subclasses <see cref="NoAccessListReader"/> and overrides ONLY the internal-virtual
    /// <see cref="NoAccessListReader.QueryChunkAsync"/> wire seam, so the REAL chunking, matching,
    /// malformed-row defense, and fail-closed orchestration in
    /// <see cref="NoAccessListReader.GetDeniedRecordsAsync"/> runs unmocked. Mirrors
    /// <c>ThrowingFlagParticipationService</c> / <c>FakeParticipationService</c> in
    /// <c>AccessibleRecordSetServiceTests.cs</c> (base(new HttpClient(), configuration: null!,
    /// credential: null!, ...) -- safe because the override never reaches the real network code).
    /// </summary>
    private sealed class FakeNoAccessListReader : NoAccessListReader
    {
        private readonly List<NoAccessEntryRow>? _orgLoopRows;
        private readonly List<NoAccessEntryRow>? _recordLoopRows;
        private readonly Exception? _throws;
        private readonly bool _returnsNull;
        private int _callCount;

        private FakeNoAccessListReader(
            List<NoAccessEntryRow>? orgLoopRows, List<NoAccessEntryRow>? recordLoopRows,
            Exception? throws, bool returnsNull)
            : base(new HttpClient(), configuration: null!, credential: null!, logger: NullLogger<NoAccessListReader>.Instance)
        {
            _orgLoopRows = orgLoopRows;
            _recordLoopRows = recordLoopRows;
            _throws = throws;
            _returnsNull = returnsNull;
        }

        public static FakeNoAccessListReader ReturningRows(List<NoAccessEntryRow> orgLoop, List<NoAccessEntryRow> recordLoop)
            => new(orgLoop, recordLoop, throws: null, returnsNull: false);

        public static FakeNoAccessListReader Throwing(Exception ex) => new(null, null, ex, returnsNull: false);

        public static FakeNoAccessListReader ReturningNull() => new(null, null, throws: null, returnsNull: true);

        /// <summary>
        /// Distinguishes the org-object loop from the record-object loop by which filter builder
        /// produced <paramref name="objectFilter"/> -- both loops share the same subject filter, so
        /// only the object fragment identifies which loop is calling.
        /// </summary>
        internal override Task<List<NoAccessEntryRow>?> QueryChunkAsync(string subjectFilter, string objectFilter, CancellationToken ct)
        {
            _callCount++;
            if (_throws is not null)
            {
                throw _throws;
            }

            if (_returnsNull)
            {
                return Task.FromResult<List<NoAccessEntryRow>?>(null);
            }

            var isOrgLoop = objectFilter.Contains("_sprk_objectorganization_value", StringComparison.Ordinal);
            return Task.FromResult<List<NoAccessEntryRow>?>(isOrgLoop ? _orgLoopRows ?? new() : _recordLoopRows ?? new());
        }
    }

    /// <summary>
    /// Task 142 round 18: a deny-list TABLE behind the same <see cref="NoAccessListReader.QueryChunkAsync"/> seam. Each
    /// query is answered the way Dataverse answers the combined <c>$filter</c>: the stored entries whose subject id is
    /// named by the query's SUBJECT fragment (for its own kind) and whose object is named by its OBJECT fragment. So,
    /// unlike <see cref="FakeNoAccessListReader"/>, an entry comes back only when its subject actually reached a query —
    /// a subject dropped by the chunking is never found. Records every subject fragment it was asked, and can fault the
    /// one query whose subject fragment names a given id (a non-success status, or a throw).
    /// </summary>
    private sealed class TableNoAccessListReader : NoAccessListReader
    {
        private const string GuidPattern =
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";

        private readonly List<NoAccessEntryRow> _rows = new();

        public TableNoAccessListReader()
            : base(new HttpClient(), configuration: null!, credential: null!, logger: NullLogger<NoAccessListReader>.Instance)
        {
        }

        /// <summary>The subject fragment of every query, in order (a fragment repeats once per object chunk).</summary>
        public List<string> SubjectFilters { get; } = new();

        /// <summary>When set, the query whose subject fragment names this id faults.</summary>
        public Guid? FaultWhenSubjectFilterNames { get; init; }

        /// <summary>The fault shape: <c>true</c> throws, <c>false</c> answers the seam's fail-closed <c>null</c>.</summary>
        public bool FaultThrows { get; init; }

        /// <summary>
        /// When set, the ethical-wall (Loop A) query whose OBJECT fragment names this referenced organization answers the
        /// seam's fail-closed <c>null</c> — a non-success status on one organization-object chunk. Every other query
        /// (other organization chunks, every record-object chunk) answers from the table.
        /// </summary>
        public Guid? NullWhenObjectOrganizationNamed { get; init; }

        public void Add(NoAccessEntryRow row) => _rows.Add(row);

        /// <summary>Every id the fragment names in a <c>{column} eq {id}</c> (or <c>eq '{id}'</c>) clause.</summary>
        public static List<Guid> IdsNamed(string filter, string column)
            => System.Text.RegularExpressions.Regex.Matches(filter, column + " eq '?(" + GuidPattern + ")'?")
                .Select(m => Guid.Parse(m.Groups[1].Value))
                .ToList();

        internal override Task<List<NoAccessEntryRow>?> QueryChunkAsync(string subjectFilter, string objectFilter, CancellationToken ct)
        {
            SubjectFilters.Add(subjectFilter);
            if (FaultWhenSubjectFilterNames is { } faulting
                && subjectFilter.Contains(faulting.ToString(), StringComparison.Ordinal))
            {
                return FaultThrows
                    ? throw new HttpRequestException("simulated HTTP 503 on one subject chunk")
                    : Task.FromResult<List<NoAccessEntryRow>?>(null);
            }

            if (NullWhenObjectOrganizationNamed is { } nullOrg
                && IdsNamed(objectFilter, "_sprk_objectorganization_value").Contains(nullOrg))
            {
                return Task.FromResult<List<NoAccessEntryRow>?>(null);
            }

            var contacts = IdsNamed(subjectFilter, "_sprk_subjectcontact_value");
            var organizations = IdsNamed(subjectFilter, "_sprk_subjectorganization_value");
            var users = IdsNamed(subjectFilter, "_sprk_subjectsystemuser_value");
            var objectOrganizations = IdsNamed(objectFilter, "_sprk_objectorganization_value");
            var objectRecords = IdsNamed(objectFilter, "sprk_objectrecordid");

            var rows = _rows.Where(r =>
                    ((r._sprk_subjectcontact_value is { } c && contacts.Contains(c))
                     || (r._sprk_subjectorganization_value is { } o && organizations.Contains(o))
                     || (r._sprk_subjectsystemuser_value is { } u && users.Contains(u)))
                    && ((r._sprk_objectorganization_value is { } oo && objectOrganizations.Contains(oo))
                        || objectRecords.Any(id => MatchesLikeDataverse(r.sprk_objectrecordid, id))))
                .ToList();
            return Task.FromResult<List<NoAccessEntryRow>?>(rows);
        }

        /// <summary>
        /// Dataverse's <c>sprk_objectrecordid eq '{id}'</c> on a text column: case-insensitive, trailing spaces ignored
        /// (task 154, verified live). A braced or otherwise non-canonical stored value never matches.
        /// </summary>
        private static bool MatchesLikeDataverse(string? stored, Guid id)
            => stored is not null && string.Equals(stored.TrimEnd(' '), id.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static NoAccessEntryRow OrganizationObjectRow(
        Guid entryId, Guid? subjectContact = null, Guid? subjectOrg = null, Guid objectOrg = default)
        => new()
        {
            sprk_noaccessentryid = entryId,
            _sprk_subjectcontact_value = subjectContact,
            _sprk_subjectorganization_value = subjectOrg,
            _sprk_objectorganization_value = objectOrg,
        };

    private static NoAccessEntryRow RecordObjectRow(
        Guid entryId, Guid? subjectContact = null, Guid? subjectOrg = null, Guid objectRecordId = default)
        => new()
        {
            sprk_noaccessentryid = entryId,
            _sprk_subjectcontact_value = subjectContact,
            _sprk_subjectorganization_value = subjectOrg,
            _sprk_objectrecordtype_value = RecordTypeRef,
            sprk_objectrecordid = objectRecordId.ToString(),
        };
}
