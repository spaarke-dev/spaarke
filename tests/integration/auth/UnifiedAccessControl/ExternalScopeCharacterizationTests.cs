using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.FetchXml;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Characterization suite for the external-module FetchXML entity guard.
///
/// ORIGINALLY pinned finding A-17 (unified-access-control-r2 spec NFR-07), rated High: the guard at
/// <c>ExternalModuleDataEndpoints.ExecuteScopedFetchAsync</c> rejected a fetch iff
/// <c>referenced.Count == 0 || referenced.Any(e =&gt; e != module.RecordEntity)</c>. A
/// <c>&lt;link-entity&gt;</c> whose name equals the module's own record entity — a SELF-JOIN — adds
/// only that same name to the referenced set, so the guard passed it.
///
/// Why that mattered: Tier-2 scoping filters only PRIMARY rows
/// (<c>Tier2ScopeFilterInjector</c> / <c>ExternalModuleDescriptor.ScopeRows</c>). Aliased columns
/// pulled through a self-joined link-entity are extra attributes ON an in-scope primary row and are
/// never scope-checked, and <c>FetchService.ProjectEntity</c> serializes <c>AliasedValue</c> straight
/// to the client. The result was cross-matter / cross-client field disclosure on a caller-controlled
/// FetchXML surface.
///
/// STATUS: **A-17 is CLOSED** by task 011 (spec FR-10). The guard now applies a second, structural
/// check — any <c>&lt;link-entity&gt;</c> at any depth is refused — so the self-join is REJECTED
/// rather than scoped. The three characterization facts below have been FLIPPED to assert the fixed
/// behavior; they remain here as the regression anchor for the finding. Full coverage of the fix
/// (evasion variants, fail-closed paths, per-check perturbation) lives in
/// <c>FetchXmlGuardSelfJoinTests</c>.
///
/// Task 011 also removed this file's hand-transcribed copy of the guard predicate — see
/// <see cref="GuardRejects"/> for why that mattered.
/// </summary>
public class ExternalScopeCharacterizationTests
{
    private const string ModuleRecordEntity = "sprk_document";

    private static IReadOnlySet<string> Extract(string fetchXml) =>
        new FetchXmlEntityExtractor().ExtractEntities(fetchXml);

    /// <summary>
    /// Invokes the PRODUCTION guard. Returns true when the fetch is REJECTED.
    ///
    /// Until task 011 this was a hand-TRANSCRIBED copy of the guard's predicate. A transcription can pin
    /// a snapshot, but it structurally cannot verify a fix: it does not change when production changes,
    /// so after the FR-10 fix it would have kept answering for the OLD code and every assertion below
    /// would have passed VACUOUSLY — green, fast, correctly named, and indistinguishable from a real
    /// pass. It now calls the real guard, so these facts fail if the fix regresses.
    /// </summary>
    private static bool GuardRejects(string fetchXml, string moduleRecordEntity) =>
        !ExternalModuleDataEndpoints
            .EvaluateFetchXmlGuard(fetchXml, moduleRecordEntity, new FetchXmlEntityExtractor())
            .IsAllowed;

    // ─────────────────────────────────────────────────────────────────────────────
    // NEGATIVE — must already hold. The guard DOES stop cross-entity joins.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractEntities_ForCrossEntityJoin_ReturnsBothEntitiesAndGuardRejects()
    {
        // Arrange — a link-entity naming a DIFFERENT entity.
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

        // Act
        var referenced = Extract(fetchXml);

        // Assert — both entities surface, so the guard rejects. This is the protection that works.
        referenced.Should().BeEquivalentTo(new[] { "sprk_document", "sprk_matter" });
        GuardRejects(fetchXml, ModuleRecordEntity).Should().BeTrue();
    }

    [Fact]
    public void ExtractEntities_ForNestedCrossEntityJoin_ReturnsEveryEntityAtAnyDepth()
    {
        // Arrange — depth-2 nesting; the extractor must not stop at depth 1.
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

        var referenced = Extract(fetchXml);

        referenced.Should().BeEquivalentTo(new[] { "sprk_document", "sprk_matter", "contact" });
        GuardRejects(fetchXml, ModuleRecordEntity).Should().BeTrue();
    }

    [Fact]
    public void ExtractEntities_ForMalformedFetchXml_ThrowsParseException()
    {
        // Fail-closed on unparseable input — the endpoint maps this to 400 DV_FETCHXML_MALFORMED.
        var act = () => Extract("<fetch><entity name='sprk_document'>");

        act.Should().Throw<FetchXmlParseException>();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // A-17 — FLIPPED by task 011 (FR-10). These now assert the FIXED behavior and
    // stand as the regression anchor for the finding.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A-17, first half — STILL TRUE and deliberately so: the entity-name analysis remains blind to a
    /// self-join, because a self-join contributes only the module's own name. Task 011 did NOT change
    /// <see cref="FetchXmlEntityExtractor"/> (it is shared with <c>DataverseAuthorizationFilter</c>);
    /// it added a SECOND, structural check to the guard instead. This test therefore documents the
    /// blind spot, and the flipped assertion at the end shows the guard now refuses anyway — which is
    /// exactly the separation of concerns FR-10 called for.
    /// </summary>
    [Fact]
    public void Characterization_ExtractEntities_ForSelfJoin_IsIndistinguishableFromSingleEntityQuery()
    {
        // Arrange — a broad self-join: every ACTIVE sprk_document joined on statecode, aliasing
        // columns sourced from rows the caller has no scope over.
        const string selfJoinFetchXml = """
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

        const string plainFetchXml = """
            <fetch>
              <entity name='sprk_document'>
                <attribute name='sprk_name' />
              </entity>
            </fetch>
            """;

        // Act
        var selfJoinReferenced = Extract(selfJoinFetchXml);
        var plainReferenced = Extract(plainFetchXml);

        // Assert — the entity-name sets ARE identical; that blind spot is unchanged by design.
        selfJoinReferenced.Should().BeEquivalentTo(plainReferenced,
            "a self-join adds only the module's own entity name, so entity-identity analysis alone sees " +
            "exactly what a plain query produces — this is WHY the guard needs a structural join check");
        selfJoinReferenced.Should().ContainSingle().Which.Should().Be(ModuleRecordEntity);

        // FLIPPED (task 011 / FR-10): identical inputs to check (1), opposite security outcomes.
        GuardRejects(selfJoinFetchXml, ModuleRecordEntity).Should().BeTrue(
            "FR-10: the self-join is refused by structural join detection despite the identical name set");
        GuardRejects(plainFetchXml, ModuleRecordEntity).Should().BeFalse(
            "a benign single-entity read must still pass — the fix must not break the module grids");
    }

    /// <summary>
    /// A-17 — the decision itself, FLIPPED by task 011 (FR-10). States the security outcome rather than
    /// the intermediate set: the exfiltrating self-join is now REJECTED.
    /// </summary>
    [Fact]
    public void Characterization_Guard_RejectsSelfJoinThatCouldExfiltrateOutOfScopeRows()
    {
        const string selfJoinFetchXml = """
            <fetch>
              <entity name='sprk_document'>
                <link-entity name='sprk_document' from='statecode' to='statecode' alias='leak'>
                  <attribute name='sprk_documenturl' />
                </link-entity>
              </entity>
            </fetch>
            """;

        GuardRejects(selfJoinFetchXml, ModuleRecordEntity).Should().BeTrue(
            "A-17 is CLOSED: Tier-2 scoping filters PRIMARY rows only, so a self-join's aliased columns " +
            "would carry out-of-scope rows to the client. FR-10 refuses the fetch instead of scoping it.");
    }

    /// <summary>
    /// A-17 — the same shape with a differently-cased entity name. Case normalization does not close the
    /// hole on its own (the referenced set is still a single name), so this confirms the refusal comes
    /// from join detection. FLIPPED by task 011 (FR-10).
    /// </summary>
    [Fact]
    public void Characterization_Guard_RejectsSelfJoinRegardlessOfCasing()
    {
        const string selfJoinFetchXml = """
            <fetch>
              <entity name='sprk_document'>
                <link-entity name='SPRK_Document' from='statecode' to='statecode' alias='leak'>
                  <attribute name='sprk_documenturl' />
                </link-entity>
              </entity>
            </fetch>
            """;

        var referenced = Extract(selfJoinFetchXml);

        referenced.Should().ContainSingle("entity names are normalized to lower case by the extractor");
        GuardRejects(selfJoinFetchXml, ModuleRecordEntity).Should().BeTrue();
    }
}
