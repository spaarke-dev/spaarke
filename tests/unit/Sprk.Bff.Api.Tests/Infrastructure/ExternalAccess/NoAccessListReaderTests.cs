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
// short-circuits (never query), the defensive subject-size ceiling, ambiguous-object-shape rows
// (never silently expand a deny), and multi-entry provenance accumulation.
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

    [Fact]
    public async Task GetDeniedRecordsAsync_ExcessiveOrganizationIds_ReturnsDenyAllQueriedFailClosedWithoutQuerying()
    {
        var sut = FakeNoAccessListReader.Throwing(new InvalidOperationException("must not be called"));
        var tooManyOrgs = Enumerable.Range(0, NoAccessListReader.MaxSubjectOrganizationIds + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        var candidates = new[] { new NoAccessCandidateRecord("sprk_matter", RecordA, Array.Empty<Guid>()) };

        var result = await sut.GetDeniedRecordsAsync(contactId: null, tooManyOrgs, candidates, CancellationToken.None);

        result.FailedClosed.Should().BeTrue(
            "an implausibly large subject org set cannot be safely bounded-queried -- fail closed rather than truncate (which would silently under-deny)");
        result.DeniedRecordIds.Should().BeEquivalentTo(new[] { RecordA });
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

        filter.Should().Be($"(sprk_subjectcontact eq {Contact} or (sprk_subjectorganization eq {SubjectOrg}))");
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

            var isOrgLoop = objectFilter.Contains("sprk_objectorganization", StringComparison.Ordinal);
            return Task.FromResult<List<NoAccessEntryRow>?>(isOrgLoop ? _orgLoopRows ?? new() : _recordLoopRows ?? new());
        }
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
