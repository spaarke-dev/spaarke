// unified-access-control-r2 task 011 — spec FR-10, finding A-17 (High).
//
// KEEP-path classification (ADR-038 §2 path #1 security-auth / tests/CLAUDE.md line 120 "every new auth
// path → ≥1 integration test"): this asserts an AUTHORIZATION decision on the external module read seam,
// so it lives under tests/integration/auth/** and NOT under tests/unit/**. (Task 011's POML named
// tests/unit/Sprk.Bff.Api.Tests/AccessControl/ — not a KEEP path; re-scoped per CLAUDE.md §6.5 path C.
// The file is compiled into the SAME assembly either way by the csproj auth glob, so InternalsVisibleTo
// still reaches the guard.)
//
// ─────────────────────────────────────────────────────────────────────────────────────────────────────
// WHY THESE TESTS CALL PRODUCTION CODE DIRECTLY
// ─────────────────────────────────────────────────────────────────────────────────────────────────────
// The task-001 characterization suite pinned A-17 by TRANSCRIBING the guard predicate into the test
// (`private static bool GuardRejects(...)`). That is sound for pinning a snapshot, but it is structurally
// incapable of verifying a FIX: a transcription does not change when production changes, so after the fix
// the transcribed copy would keep answering for the old code and the test would pass VACUOUSLY — green,
// fast, correctly named, and indistinguishable from a real pass. This project has already paid for that
// failure mode. So every test below calls the real
// `ExternalModuleDataEndpoints.EvaluateFetchXmlGuard(...)` with the real `FetchXmlEntityExtractor`.
//
// ─────────────────────────────────────────────────────────────────────────────────────────────────────
// WHY ASSERTIONS PIN A SPECIFIC VERDICT RATHER THAN "REJECTED"
// ─────────────────────────────────────────────────────────────────────────────────────────────────────
// The guard has TWO independent refusal checks: (1) entity identity, (2) structural join detection. A
// cross-entity join trips BOTH. So a test asserting only "cross-entity join is rejected" would still pass
// if check (1) were deleted entirely — check (2) would catch it and the test could not tell. Asserting
// the exact verdict (`EntityMismatch` vs `LinkEntityNotPermitted`) is what makes each check individually
// load-bearing, and the `ScriptedEntityExtractor` cases below perturb each check in isolation.

using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.FetchXml;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

using Verdict = ExternalModuleDataEndpoints.FetchXmlGuardVerdict;

public class FetchXmlGuardSelfJoinTests
{
    private const string ModuleRecordEntity = "sprk_document";

    /// <summary>Evaluates the REAL guard with the REAL extractor — no doubles, no transcription.</summary>
    private static ExternalModuleDataEndpoints.FetchXmlGuardResult Evaluate(string? fetchXml) =>
        ExternalModuleDataEndpoints.EvaluateFetchXmlGuard(
            fetchXml, ModuleRecordEntity, new FetchXmlEntityExtractor());

    /// <summary>
    /// The A-17 exploit verbatim: every ACTIVE sprk_document self-joined on statecode, aliasing columns
    /// sourced from rows the caller has no scope over. Tier-2 scoping filters PRIMARY rows only, so these
    /// aliased values would ride out on in-scope rows via FetchService.ProjectEntity.
    /// </summary>
    private const string A17ExploitFetchXml = """
        <fetch>
          <entity name='sprk_document'>
            <attribute name='sprk_name' />
            <link-entity name='sprk_document' from='statecode' to='statecode' alias='leak'>
              <attribute name='sprk_name' />
              <attribute name='sprk_documenturl' />
            </link-entity>
          </entity>
        </fetch>
        """;

    // ─────────────────────────────────────────────────────────────────────────────
    // FR-10 ACCEPTANCE — the exploit is REJECTED, not scoped.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateFetchXmlGuard_ForA17SelfJoinExploit_RejectsAsLinkEntityNotPermitted()
    {
        var result = Evaluate(A17ExploitFetchXml);

        result.IsAllowed.Should().BeFalse(
            "FR-10: a self-join projecting aliased columns of out-of-scope rows must be REJECTED, not scoped");
        result.Verdict.Should().Be(Verdict.LinkEntityNotPermitted,
            "the refusal must come from structural join detection — the entity-name set alone admits this fetch");
    }

    /// <summary>
    /// Proves the A-17 blind spot still exists in the entity-name analysis and that check (2) is what
    /// closes it: the referenced set for the exploit is IDENTICAL to a benign single-entity read, yet the
    /// verdicts differ. If check (2) were removed, the exploit would be Allowed and this test would fail.
    /// </summary>
    [Fact]
    public void EvaluateFetchXmlGuard_SelfJoinAndPlainRead_ShareAReferencedSetButNotAVerdict()
    {
        const string plainFetchXml = """
            <fetch>
              <entity name='sprk_document'>
                <attribute name='sprk_name' />
              </entity>
            </fetch>
            """;

        var exploit = Evaluate(A17ExploitFetchXml);
        var plain = Evaluate(plainFetchXml);

        exploit.ReferencedEntities.Should().BeEquivalentTo(plain.ReferencedEntities,
            "A-17: a self-join contributes only the module's own entity name, so the name set cannot tell " +
            "the two apart — this is precisely why the guard needs a second, structural check");
        exploit.ReferencedEntities.Should().ContainSingle().Which.Should().Be(ModuleRecordEntity);

        plain.Verdict.Should().Be(Verdict.Allowed);
        exploit.Verdict.Should().Be(Verdict.LinkEntityNotPermitted);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // EVASION VARIANTS — a self-join must not slip past on spelling.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateFetchXmlGuard_ForSelfJoinWithDifferentlyCasedEntityName_Rejects()
    {
        // The extractor lower-cases entity names, so check (1) still admits. Check (2) must reject.
        const string fetchXml = """
            <fetch>
              <entity name='sprk_document'>
                <link-entity name='SPRK_Document' from='statecode' to='statecode' alias='leak'>
                  <attribute name='sprk_documenturl' />
                </link-entity>
              </entity>
            </fetch>
            """;

        var result = Evaluate(fetchXml);

        result.ReferencedEntities.Should().ContainSingle(
            "entity names are normalized to lower case, so casing does not incidentally close the hole");
        result.Verdict.Should().Be(Verdict.LinkEntityNotPermitted);
    }

    [Fact]
    public void EvaluateFetchXmlGuard_ForSelfJoinWithUppercasedElementName_Rejects()
    {
        // FetchXmlEntityExtractor matches Descendants("link-entity") EXACTLY, so it does not see
        // <LINK-ENTITY> at all and reports a clean single-entity set. Only the guard's case-insensitive
        // local-name match can refuse this. Load-bearing for that specific choice.
        const string fetchXml = """
            <fetch>
              <entity name='sprk_document'>
                <LINK-ENTITY name='sprk_document' from='statecode' to='statecode' alias='leak'>
                  <attribute name='sprk_documenturl' />
                </LINK-ENTITY>
              </entity>
            </fetch>
            """;

        var result = Evaluate(fetchXml);

        result.Verdict.Should().Be(Verdict.LinkEntityNotPermitted,
            "the guard matches the join element case-insensitively; a guard that matched only the exact " +
            "lower-case spelling would admit this");
    }

    [Fact]
    public void EvaluateFetchXmlGuard_ForNamespaceQualifiedSelfJoin_Rejects()
    {
        // Same argument for XML namespaces: the extractor's XName lookup misses a qualified element.
        const string fetchXml = """
            <fetch xmlns:x='urn:spaarke:test'>
              <entity name='sprk_document'>
                <x:link-entity name='sprk_document' from='statecode' to='statecode' alias='leak'>
                  <attribute name='sprk_documenturl' />
                </x:link-entity>
              </entity>
            </fetch>
            """;

        var result = Evaluate(fetchXml);

        result.Verdict.Should().Be(Verdict.LinkEntityNotPermitted,
            "the guard matches on LOCAL name, ignoring namespace — a namespace-qualified join is still a join");
    }

    [Fact]
    public void EvaluateFetchXmlGuard_ForDeeplyNestedSelfJoin_Rejects()
    {
        const string fetchXml = """
            <fetch>
              <entity name='sprk_document'>
                <link-entity name='sprk_document' from='statecode' to='statecode' alias='a'>
                  <link-entity name='sprk_document' from='statecode' to='statecode' alias='b'>
                    <attribute name='sprk_documenturl' />
                  </link-entity>
                </link-entity>
              </entity>
            </fetch>
            """;

        Evaluate(fetchXml).Verdict.Should().Be(Verdict.LinkEntityNotPermitted,
            "join detection walks the whole subtree, so depth does not evade it");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // NO REGRESSION — cross-entity refs still rejected, and still as EntityMismatch.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateFetchXmlGuard_ForCrossEntityJoin_StillRejectsAsEntityMismatch()
    {
        const string fetchXml = """
            <fetch>
              <entity name='sprk_document'>
                <attribute name='sprk_name' />
                <link-entity name='sprk_matter' from='sprk_matterid' to='sprk_matter' alias='m'>
                  <attribute name='sprk_confidentialnotes' />
                </link-entity>
              </entity>
            </fetch>
            """;

        var result = Evaluate(fetchXml);

        result.ReferencedEntities.Should().BeEquivalentTo(new[] { "sprk_document", "sprk_matter" });
        result.Verdict.Should().Be(Verdict.EntityMismatch,
            "the pre-existing entity-identity check must still be the one that fires here — asserting only " +
            "'rejected' would pass even if that check were deleted, because join detection also catches it");
    }

    [Fact]
    public void EvaluateFetchXmlGuard_ForNestedCrossEntityJoin_StillRejectsAsEntityMismatch()
    {
        const string fetchXml = """
            <fetch>
              <entity name='sprk_document'>
                <link-entity name='sprk_matter' from='sprk_matterid' to='sprk_matter' alias='m'>
                  <link-entity name='contact' from='contactid' to='sprk_assignedattorney1' alias='c'>
                    <attribute name='emailaddress1' />
                  </link-entity>
                </link-entity>
              </entity>
            </fetch>
            """;

        var result = Evaluate(fetchXml);

        result.ReferencedEntities.Should().BeEquivalentTo(new[] { "sprk_document", "sprk_matter", "contact" });
        result.Verdict.Should().Be(Verdict.EntityMismatch);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // LEGITIMATE READS STILL PASS — the fix must not break the module grids.
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("<fetch><entity name='sprk_document'><attribute name='sprk_name' /></entity></fetch>")]
    [InlineData("<fetch><entity name='sprk_document'><all-attributes /></entity></fetch>")]
    [InlineData("<fetch top='50'><entity name='sprk_document'><attribute name='sprk_name' />" +
                "<order attribute='sprk_name' descending='true' /></entity></fetch>")]
    [InlineData("<fetch page='2' count='50'><entity name='sprk_document'><attribute name='sprk_name' />" +
                "<filter type='and'><condition attribute='statecode' operator='eq' value='0' /></filter>" +
                "</entity></fetch>")]
    [InlineData("<fetch distinct='true'><entity name='SPRK_DOCUMENT'><attribute name='sprk_name' /></entity></fetch>")]
    public void EvaluateFetchXmlGuard_ForSingleEntityRead_Allows(string fetchXml)
    {
        var result = Evaluate(fetchXml);

        result.IsAllowed.Should().BeTrue(
            "single-entity reads of the module's own entity are the supported shape of this seam; " +
            "rejecting them would break every module DataGrid");
        result.Verdict.Should().Be(Verdict.Allowed);
    }

    [Fact]
    public void EvaluateFetchXmlGuard_ForCommentMentioningLinkEntity_Allows()
    {
        // Comments and text nodes are not elements — the guard must not false-positive on the words.
        const string fetchXml = """
            <fetch>
              <!-- link-entity joins are not permitted here; see FR-10 -->
              <entity name='sprk_document'>
                <attribute name='sprk_name' />
                <filter><condition attribute='sprk_name' operator='eq' value='link-entity' /></filter>
              </entity>
            </fetch>
            """;

        Evaluate(fetchXml).Verdict.Should().Be(Verdict.Allowed,
            "join detection inspects ELEMENTS, so the literal text in a comment or a filter value is not a join");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // FAIL CLOSED (ADR-003) — anything unprovable is refused, never passed through.
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<fetch><entity name='sprk_document'>")]                      // truncated
    [InlineData("not xml at all")]
    [InlineData("<fetch><entity /></fetch>")]                                 // entity without a name
    [InlineData("<fetch><entity name='sprk_document'><link-entity /></entity></fetch>")] // join without a name
    // XXE / DTD: .NET prohibits DTD processing by default, so this must surface as a parse failure and be
    // refused — never parsed, and never admitted. Asserted rather than assumed.
    [InlineData("<?xml version='1.0'?><!DOCTYPE fetch [<!ENTITY xxe SYSTEM 'file:///etc/passwd'>]>" +
                "<fetch><entity name='sprk_document'><attribute name='&xxe;' /></entity></fetch>")]
    public void EvaluateFetchXmlGuard_ForUnprovableFetchXml_RejectsAsMalformed(string? fetchXml)
    {
        var result = Evaluate(fetchXml);

        result.IsAllowed.Should().BeFalse("ADR-003: a fetch the guard cannot prove safe is refused");
        result.Verdict.Should().Be(Verdict.Malformed);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // INDIVIDUAL PERTURBATION — prove each check carries weight ON ITS OWN.
    //
    // The double below models ONE exact input and THROWS on anything else. A double that fell back to a
    // default answer could let a test pass without the guard ever doing the work under test — the exact
    // vacuous-pass mode this project has hit repeatedly.
    // ─────────────────────────────────────────────────────────────────────────────

    private sealed class ScriptedEntityExtractor : IFetchXmlEntityExtractor
    {
        private readonly string _expectedFetchXml;
        private readonly IReadOnlySet<string> _entities;

        public ScriptedEntityExtractor(string expectedFetchXml, params string[] entities)
        {
            _expectedFetchXml = expectedFetchXml;
            _entities = new HashSet<string>(entities, StringComparer.OrdinalIgnoreCase);
        }

        public int CallCount { get; private set; }

        public IReadOnlySet<string> ExtractEntities(string fetchXml)
        {
            if (!string.Equals(fetchXml, _expectedFetchXml, StringComparison.Ordinal))
            {
                // Refuse to guess. A permissive fallback here would silently make the caller's assertion
                // meaningless — see the header note on vacuous passes.
                throw new InvalidOperationException(
                    "ScriptedEntityExtractor received FetchXML it was not scripted for. It fails loudly " +
                    "instead of returning a default, so a guard change cannot be masked by the double.");
            }

            CallCount++;
            return _entities;
        }
    }

    /// <summary>
    /// Perturbs check (1) ALONE. The XML is join-free, so check (2) cannot be the refuser; only the
    /// entity-identity check can reject. Deleting check (1) turns this Allowed and fails the test.
    /// </summary>
    [Fact]
    public void EvaluateFetchXmlGuard_WhenExtractorReportsAForeignEntityOnJoinFreeXml_RejectsAsEntityMismatch()
    {
        const string joinFreeFetchXml =
            "<fetch><entity name='sprk_document'><attribute name='sprk_name' /></entity></fetch>";
        var extractor = new ScriptedEntityExtractor(joinFreeFetchXml, "sprk_document", "sprk_matter");

        var result = ExternalModuleDataEndpoints.EvaluateFetchXmlGuard(
            joinFreeFetchXml, ModuleRecordEntity, extractor);

        result.Verdict.Should().Be(Verdict.EntityMismatch,
            "with no join present, ONLY the entity-identity check can produce a refusal here");
        extractor.CallCount.Should().Be(1, "the guard must consult the extractor with the unmodified payload");
    }

    /// <summary>
    /// Perturbs the <c>Count == 0</c> clause ALONE — an extractor that reports nothing must not be read
    /// as "nothing foreign, therefore fine".
    /// </summary>
    [Fact]
    public void EvaluateFetchXmlGuard_WhenExtractorReportsNoEntitiesOnJoinFreeXml_RejectsAsEntityMismatch()
    {
        const string joinFreeFetchXml =
            "<fetch><entity name='sprk_document'><attribute name='sprk_name' /></entity></fetch>";
        var extractor = new ScriptedEntityExtractor(joinFreeFetchXml);

        var result = ExternalModuleDataEndpoints.EvaluateFetchXmlGuard(
            joinFreeFetchXml, ModuleRecordEntity, extractor);

        result.Verdict.Should().Be(Verdict.EntityMismatch, "an empty referenced set is refused, not admitted");
    }

    /// <summary>
    /// Perturbs check (2) ALONE. The extractor is scripted to return exactly what the REAL one returns for
    /// a self-join — the module entity by itself — so check (1) admits. Deleting check (2) turns this
    /// Allowed and fails the test. This is the assertion that pins the A-17 fix.
    /// </summary>
    [Fact]
    public void EvaluateFetchXmlGuard_WhenEntityCheckAdmitsSelfJoin_JoinDetectionStillRejects()
    {
        var extractor = new ScriptedEntityExtractor(A17ExploitFetchXml, ModuleRecordEntity);

        var result = ExternalModuleDataEndpoints.EvaluateFetchXmlGuard(
            A17ExploitFetchXml, ModuleRecordEntity, extractor);

        result.Verdict.Should().Be(Verdict.LinkEntityNotPermitted,
            "the entity-identity check ADMITS a self-join (that is A-17); structural join detection is the " +
            "only thing standing between this fetch and cross-matter field disclosure");
    }

    /// <summary>
    /// The guard must fail closed when the extractor's own parse succeeds but the guard's does not. Scripted
    /// so the extractor reports a clean set for XML that cannot be re-parsed — the guard cannot then prove
    /// the fetch join-free and must refuse.
    /// </summary>
    [Fact]
    public void EvaluateFetchXmlGuard_WhenXmlIsUnparseableButExtractorReportsCleanSet_RejectsAsMalformed()
    {
        const string unparseableFetchXml = "<fetch><entity name='sprk_document'>";
        var extractor = new ScriptedEntityExtractor(unparseableFetchXml, ModuleRecordEntity);

        var result = ExternalModuleDataEndpoints.EvaluateFetchXmlGuard(
            unparseableFetchXml, ModuleRecordEntity, extractor);

        result.Verdict.Should().Be(Verdict.Malformed,
            "ADR-003: unable to prove the absence of a join ⇒ refuse; never admit on a parse failure");
    }
}
