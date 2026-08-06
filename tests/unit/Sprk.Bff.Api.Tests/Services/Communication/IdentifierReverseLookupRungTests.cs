using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Rung 0 (identifier reverse-lookup, FR-01) tests. Covers the closed acceptance set from task 020:
/// a well-formed identifier for EACH of the 7 core types resolves at 0.90; bare-numeric emits sub-threshold
/// (0.65, never auto-files alone); a multi-entity token is capped so none auto-files; same-field duplicates
/// drive the mapper to Ambiguous; a dirty catalog row is read defensively (no wrong-field query, no throw);
/// cross-tenant numbering works with only catalog config; the per-message query count is gated + reported
/// (NFR-08); and a lookup failure degrades to no-match (NFR-04).
///
/// <para><b>Batched seam (task 031, FR-D2 / NFR-08).</b> The rung now issues ONE
/// <see cref="ICommunicationDataverseService.QueryRecordsByNumberFieldValuesAsync"/> In-filter query per
/// distinct number field (≤7) instead of one per (field, value) pair (≈175). These tests exercise that
/// batched seam and assert IDENTICAL RungMatch outcomes to the per-value path (the confidence policy,
/// multi-entity cap, FR-12 demotion, and P1 substring guard are all unchanged) — the differential proof —
/// plus the ≤7 query-count reduction. Returned records carry their number-field attribute so the rung
/// re-associates each match to the token it matched.</para>
/// </summary>
public class IdentifierReverseLookupRungTests
{
    private readonly Mock<ICommunicationDataverseService> _dv = new();

    /// <summary>The 7 core types + their live catalog number field + code regarding write field (task 001).</summary>
    private static readonly (string Entity, string NumberField, string RegardingField)[] CoreSeven =
    {
        ("sprk_matter", "sprk_matternumber", "sprk_regardingmatter"),
        ("sprk_project", "sprk_projectnumber", "sprk_regardingproject"),
        ("sprk_invoice", "sprk_invoicenumber", "sprk_regardinginvoice"),
        ("sprk_workassignment", "sprk_workassignmentnumber", "sprk_regardingworkassignment"),
        ("sprk_budget", "sprk_budgetnumber", "sprk_regardingbudget"),
        ("sprk_servicerequest", "sprk_servicerequestnumber", "sprk_regardingservicerequest"),
        ("sprk_reportcard", "sprk_reportcardnumber", "sprk_regardingreportcard"),
    };

    private IdentifierReverseLookupRung Rung(ILogger<IdentifierReverseLookupRung>? logger = null) =>
        new(_dv.Object, logger ?? NullLogger<IdentifierReverseLookupRung>.Instance);

    private static DataverseEntity RosterRow(string logicalName, string? numberField)
    {
        var e = new DataverseEntity("sprk_recordtype_ref") { Id = Guid.NewGuid() };
        e["sprk_recordlogicalname"] = logicalName;
        e["sprk_regardingrecordnumberfield"] = numberField;
        return e;
    }

    /// <summary>Full clean 7-core roster (each row carries its catalog number field).</summary>
    private static DataverseEntity[] FullRoster() =>
        CoreSeven.Select(t => RosterRow(t.Entity, t.NumberField)).ToArray();

    /// <summary>
    /// A matched record carrying its number-field attribute set to <paramref name="value"/> — the batched
    /// query SELECTs the number field, and the rung reads it to re-associate the record to its token.
    /// </summary>
    private static DataverseEntity Record(string entity, Guid id, string numberField, string value)
    {
        var e = new DataverseEntity(entity) { Id = id };
        e[numberField] = value;
        return e;
    }

    private static NormalizedMessage Envelope(string? subject = null, string? body = null) =>
        new() { Direction = CommunicationDirection.Incoming, Subject = subject, BodyText = body };

    private void SetupRoster(params DataverseEntity[] rows) =>
        _dv.Setup(d => d.QueryAllRecordTypeRefsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rows);

    /// <summary>Default: every batched reverse lookup returns empty; individual tests override the matching one.</summary>
    private void SetupNoRecordMatches() =>
        _dv.Setup(d => d.QueryRecordsByNumberFieldValuesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DataverseEntity>());

    /// <summary>
    /// Stub the batched query for (entity, numberField): when the queried value-set CONTAINS
    /// <paramref name="value"/>, return one record per id, each carrying numberField == value (so the rung
    /// re-associates it to that token). Registered AFTER <see cref="SetupNoRecordMatches"/> so it wins.
    /// </summary>
    private void SetupMatch(string entity, string numberField, string value, params Guid[] ids) =>
        _dv.Setup(d => d.QueryRecordsByNumberFieldValuesAsync(
                entity, numberField,
                It.Is<IReadOnlyCollection<string>>(vs => vs.Contains(value, StringComparer.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids.Select(id => Record(entity, id, numberField, value)).ToArray());

    // ── 1. Well-formed identifier for EACH of the 7 types → correct record + field at 0.90 ──────────

    [Theory]
    [InlineData("sprk_matter", "sprk_matternumber", "sprk_regardingmatter", "MAT-123")]
    [InlineData("sprk_project", "sprk_projectnumber", "sprk_regardingproject", "PRJT.10001.01")]
    [InlineData("sprk_invoice", "sprk_invoicenumber", "sprk_regardinginvoice", "INV-002")]
    [InlineData("sprk_workassignment", "sprk_workassignmentnumber", "sprk_regardingworkassignment", "WRK-55")]
    [InlineData("sprk_budget", "sprk_budgetnumber", "sprk_regardingbudget", "BDGT-9012")]
    [InlineData("sprk_servicerequest", "sprk_servicerequestnumber", "sprk_regardingservicerequest", "SVCR-77")]
    [InlineData("sprk_reportcard", "sprk_reportcardnumber", "sprk_regardingreportcard", "RPTC-3001")]
    public async Task WellFormed_ResolvesCorrectRecord_At090(
        string entity, string numberField, string regardingField, string token)
    {
        SetupRoster(FullRoster());
        SetupNoRecordMatches();
        var recordId = Guid.NewGuid();
        SetupMatch(entity, numberField, token, recordId);

        var matches = await Rung().EvaluateAsync(Envelope(subject: $"Re: {token} update"), new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle().Which.Should().Match<RungMatch>(m =>
            m.RegardingFieldName == regardingField
            && m.Target!.LogicalName == entity
            && m.Target!.Id == recordId
            && m.Confidence == 0.90
            && m.Rung == RungKind.ExplicitReference);
    }

    // ── 2. NEGATIVE: bare-numeric token → 0.65, below the 0.85 auto-file threshold ─────────────────

    [Fact]
    public async Task BareNumeric_EmitsSubThreshold_065_NotAutoFileableAlone()
    {
        SetupRoster(FullRoster());
        SetupNoRecordMatches();
        var matterId = Guid.NewGuid();
        SetupMatch("sprk_matter", "sprk_matternumber", "441482", matterId);

        var matches = await Rung().EvaluateAsync(Envelope(body: "regarding 441482 please advise"), new AssociationContext(), CancellationToken.None);

        var match = matches.Should().ContainSingle().Subject;
        match.Target!.Id.Should().Be(matterId);
        match.Confidence.Should().Be(0.65);
        match.Confidence.Should().BeLessThan(0.85, "a bare-numeric token must never auto-file alone");
    }

    // ── 3a. NEGATIVE: a token matching MULTIPLE entity types → every match capped ≤ 0.65 (none auto-files) ──

    [Fact]
    public async Task MultiEntity_Token_AllMatchesCapped_NoneAutoFileable()
    {
        SetupRoster(FullRoster());
        SetupNoRecordMatches();
        var projectId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        SetupMatch("sprk_project", "sprk_projectnumber", "SHRD-42", projectId);
        SetupMatch("sprk_invoice", "sprk_invoicenumber", "SHRD-42", invoiceId);

        var matches = await Rung().EvaluateAsync(Envelope(subject: "SHRD-42"), new AssociationContext(), CancellationToken.None);

        matches.Should().HaveCount(2);
        matches.Should().OnlyContain(m => m.Confidence <= 0.65);
        matches.Should().NotContain(m => m.Confidence >= 0.85, "a multi-entity token must never produce a guessed auto-file");
        matches.Select(m => m.Target!.Id).Should().BeEquivalentTo(new[] { projectId, invoiceId });
    }

    // ── 3b. NEGATIVE: 2+ records on the SAME field → mapper resolves Ambiguous (never a guess) ──────

    [Fact]
    public async Task SameFieldDuplicates_MapperResolvesAmbiguous()
    {
        SetupRoster(FullRoster());
        SetupNoRecordMatches();
        var matterA = Guid.NewGuid();
        var matterB = Guid.NewGuid();
        SetupMatch("sprk_matter", "sprk_matternumber", "DUP-42", matterA, matterB);

        var matches = await Rung().EvaluateAsync(Envelope(subject: "DUP-42"), new AssociationContext(), CancellationToken.None);

        matches.Should().HaveCount(2);
        matches.Should().OnlyContain(m => m.RegardingFieldName == "sprk_regardingmatter" && m.Confidence == 0.90);

        var decision = AssociationTestSupport.Mapper().Decide(matches, CommunicationDirection.Incoming, null);
        decision.Status.Should().Be(AssociationStatusCodes.Ambiguous);
    }

    // ── 3c. FR-12: a well-formed identifier the email REFERENCES but does not FILE ONTO is capped to 0.65 ──

    [Fact]
    public async Task Fr12_NewRecordFraming_ReferencedIdentifier_CappedSubThreshold_NotAutoFiledAlone()
    {
        // "new litigation matter related to MAT-123" — MAT-123 is referenced, not the record this email is
        // about. It must NOT auto-file onto MAT-123 (misfile guard), so the normally-0.90 well-formed match is
        // demoted to 0.65 (Suggested), while still being surfaced as a review candidate.
        SetupRoster(FullRoster());
        SetupNoRecordMatches();
        var matterId = Guid.NewGuid();
        SetupMatch("sprk_matter", "sprk_matternumber", "MAT-123", matterId);

        var matches = await Rung().EvaluateAsync(
            Envelope(subject: "New matter", body: "This is a new litigation matter related to MAT-123"),
            new AssociationContext(), CancellationToken.None);

        var match = matches.Should().ContainSingle().Subject;
        match.Target!.Id.Should().Be(matterId, "the referenced record is still surfaced for review");
        match.RegardingFieldName.Should().Be("sprk_regardingmatter");
        match.Confidence.Should().Be(0.65);
        match.Confidence.Should().BeLessThan(0.85, "an email presenting a NEW record must never auto-file onto the record it merely references");
        match.Provenance.Should().Contain("new-record-referenced");
    }

    [Fact]
    public async Task Fr12_FileOntoExistingRecord_NoNewRecordFraming_StillAutoFilesAt090()
    {
        // Control: the SAME identifier without new-record framing keeps its 0.90 auto-file confidence — FR-12
        // intent only ever SUPPRESSES auto-file, it never widens or blocks a legitimate file-to-existing.
        SetupRoster(FullRoster());
        SetupNoRecordMatches();
        var matterId = Guid.NewGuid();
        SetupMatch("sprk_matter", "sprk_matternumber", "MAT-123", matterId);

        var matches = await Rung().EvaluateAsync(
            Envelope(subject: "Re: MAT-123", body: "Please file this correspondence onto MAT-123"),
            new AssociationContext(), CancellationToken.None);

        var match = matches.Should().ContainSingle().Subject;
        match.Target!.Id.Should().Be(matterId);
        match.Confidence.Should().Be(0.90, "no new-record framing ⇒ the explicit identifier auto-files normally");
        match.Provenance.Should().NotContain("new-record-referenced");
    }

    // ── 3d. P1 (FR-12 UAT): a bare-numeric segment of a well-formed identifier does NOT collide ──

    [Fact]
    public async Task P1_BareNumericSegmentOfWellFormedIdentifier_NotExtracted_NoCollision()
    {
        // Regression for the UAT misfile: "REAL-2026-123456.02" (a matter number) contains the digit-run
        // "123456", which previously extracted as a standalone bare-numeric and collided with an UNRELATED
        // invoice #123456 — becoming the record's primary Regarding. The inner digit-runs must NOT be
        // extracted as independent tokens.
        SetupRoster(FullRoster());
        SetupNoRecordMatches();
        var matterId = Guid.NewGuid();
        SetupMatch("sprk_matter", "sprk_matternumber", "REAL-2026-123456.02", matterId);
        // A trap: an invoice #123456 exists. If "123456" were still extracted, this would wrongly match.
        var invoiceId = Guid.NewGuid();
        SetupMatch("sprk_invoice", "sprk_invoicenumber", "123456", invoiceId);

        var matches = await Rung().EvaluateAsync(
            Envelope(subject: "Fw: REAL-2026-123456.02 real estate", body: "Regarding REAL-2026-123456.02 please advise"),
            new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle("only the well-formed matter identifier resolves — the inner digit-runs are not independent tokens");
        matches[0].Target!.Id.Should().Be(matterId);
        matches[0].RegardingFieldName.Should().Be("sprk_regardingmatter");
        // The inner digit-runs "123456" and "2026" must never have been queried against any number field.
        _dv.Verify(d => d.QueryRecordsByNumberFieldValuesAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.Is<IReadOnlyCollection<string>>(vs => vs.Contains("123456")), It.IsAny<CancellationToken>()), Times.Never,
            "the '123456' segment of the matter number must not collide with an unrelated invoice");
        _dv.Verify(d => d.QueryRecordsByNumberFieldValuesAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.Is<IReadOnlyCollection<string>>(vs => vs.Contains("2026")), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task P1_StandaloneBareNumeric_StillExtracted_WhenNotInsideAWellFormedToken()
    {
        // Guard against over-suppression: a bare-numeric that is NOT a substring of any well-formed token is
        // still a legitimate (sub-threshold) candidate.
        SetupRoster(FullRoster());
        SetupNoRecordMatches();
        var matterId = Guid.NewGuid();
        SetupMatch("sprk_matter", "sprk_matternumber", "778899", matterId);

        var matches = await Rung().EvaluateAsync(Envelope(body: "please advise on 778899"), new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle().Which.Confidence.Should().Be(0.65, "a genuine standalone bare-numeric still matches at sub-threshold");
    }

    // ── 4. NEGATIVE: a dirty catalog row (null number field / contact anomaly) is read defensively ──

    [Fact]
    public async Task DirtyCatalogRow_NullNumberField_SkippedNoQueryNoThrow()
    {
        // Roster: matter row with a NULL number field + the anomalous contact row (non-core, null number).
        SetupRoster(
            RosterRow("sprk_matter", null),
            RosterRow("contact", null)); // task 001 contact-row anomaly — naturally excluded
        SetupNoRecordMatches();

        var matches = await Rung().EvaluateAsync(Envelope(subject: "Re: MAT-1 hello"), new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
        _dv.Verify(d => d.QueryRecordsByNumberFieldValuesAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 5. Cross-tenant: a tenant-custom number field logical name works with ONLY catalog config ──

    [Fact]
    public async Task CrossTenant_CustomNumberFieldName_WorksWithCatalogConfigOnly()
    {
        // Catalog names a tenant-custom number field for matter — no code change; matching is value-based.
        SetupRoster(RosterRow("sprk_matter", "new_customnumber"));
        SetupNoRecordMatches();
        var matterId = Guid.NewGuid();
        SetupMatch("sprk_matter", "new_customnumber", "CUST-42", matterId);

        var matches = await Rung().EvaluateAsync(Envelope(subject: "CUST-42"), new AssociationContext(), CancellationToken.None);

        matches.Should().ContainSingle().Which.Target!.Id.Should().Be(matterId);
        _dv.Verify(d => d.QueryRecordsByNumberFieldValuesAsync(
                "sprk_matter", "new_customnumber",
                It.Is<IReadOnlyCollection<string>>(vs => vs.Contains("CUST-42")), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── 6. NFR-08 / FR-D2: batched — ≤1 query per distinct number field; per-message query count reported ──

    [Fact]
    public async Task Nfr08_NoCandidateTokens_NoRosterRead_NoLookups()
    {
        SetupRoster(FullRoster());
        SetupNoRecordMatches();

        var matches = await Rung().EvaluateAsync(Envelope(subject: "Hello there", body: "please review when you can"), new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
        _dv.Verify(d => d.QueryAllRecordTypeRefsAsync(It.IsAny<CancellationToken>()), Times.Never);
        _dv.Verify(d => d.QueryRecordsByNumberFieldValuesAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Nfr08_SingleToken_QueriesSevenTypes_AndReportsQueryCount()
    {
        SetupRoster(FullRoster());
        SetupNoRecordMatches();
        var logger = new CapturingLogger<IdentifierReverseLookupRung>();

        await Rung(logger).EvaluateAsync(Envelope(subject: "Re: MAT-123"), new AssociationContext(), CancellationToken.None);

        // Batched: ONE In-filter query per core type = 7 (was 7 single-value queries; unchanged count here,
        // the reduction shows with MULTIPLE tokens — see Fr_D2 test below).
        _dv.Verify(d => d.QueryRecordsByNumberFieldValuesAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Exactly(7));

        var fired = logger.Entries.Should().ContainSingle(e => e.Field("QueryCount") != null).Subject;
        fired.Field("QueryCount").Should().Be(7);
    }

    [Fact]
    public async Task FrD2_ManyDistinctTokens_StillAtMostSevenQueries_OnePerNumberField()
    {
        // FR-D2 core: N distinct tokens across the 7 core types issue ≤7 queries (one In-filter per number
        // field carrying ALL distinct token values), NOT N×7. Three well-formed tokens matching three
        // different types resolve correctly in exactly 7 batched queries (was up to 21 single-value calls).
        SetupRoster(FullRoster());
        SetupNoRecordMatches();
        var matterId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        SetupMatch("sprk_matter", "sprk_matternumber", "MAT-11", matterId);
        SetupMatch("sprk_project", "sprk_projectnumber", "PRJT-22", projectId);
        SetupMatch("sprk_invoice", "sprk_invoicenumber", "INV-33", invoiceId);

        var matches = await Rung().EvaluateAsync(
            Envelope(subject: "MAT-11 PRJT-22 INV-33"), new AssociationContext(), CancellationToken.None);

        matches.Select(m => m.Target!.Id).Should().BeEquivalentTo(new[] { matterId, projectId, invoiceId });
        matches.Should().OnlyContain(m => m.Confidence == 0.90, "each is a well-formed single-type match");

        // Exactly one query per distinct number field (7), regardless of the 3 distinct tokens.
        _dv.Verify(d => d.QueryRecordsByNumberFieldValuesAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Exactly(7));
        // The matter query carries ALL three distinct token values in ONE In-filter (not one query per value).
        _dv.Verify(d => d.QueryRecordsByNumberFieldValuesAsync(
            "sprk_matter", "sprk_matternumber",
            It.Is<IReadOnlyCollection<string>>(vs => vs.Contains("MAT-11") && vs.Contains("PRJT-22") && vs.Contains("INV-33")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── 7. NFR-04: a lookup failure degrades to no-match, never propagates ─────────────────────────

    [Fact]
    public async Task Nfr04_LookupThrows_DegradesToEmpty_NoPropagation()
    {
        SetupRoster(FullRoster());
        _dv.Setup(d => d.QueryRecordsByNumberFieldValuesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));

        var act = async () => await Rung().EvaluateAsync(Envelope(subject: "Re: MAT-123"), new AssociationContext(), CancellationToken.None);

        var matches = await act.Should().NotThrowAsync();
        matches.Which.Should().BeEmpty();
    }

    [Fact]
    public async Task Nfr04_RosterReadThrows_DegradesToEmpty_NoPropagation()
    {
        _dv.Setup(d => d.QueryAllRecordTypeRefsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("catalog unavailable"));

        var matches = await Rung().EvaluateAsync(Envelope(subject: "Re: MAT-123"), new AssociationContext(), CancellationToken.None);

        matches.Should().BeEmpty();
    }
}
