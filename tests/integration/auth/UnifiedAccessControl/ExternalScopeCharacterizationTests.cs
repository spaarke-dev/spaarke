using FluentAssertions;
using Sprk.Bff.Api.Services.Dataverse.FetchXml;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Characterization suite for the external-module FetchXML entity guard.
///
/// Pins finding A-17 (unified-access-control-r2 spec NFR-07), rated High: the guard at
/// <c>ExternalModuleDataEndpoints.ExecuteScopedFetchAsync:160-172</c> rejects a fetch iff
/// <c>referenced.Count == 0 || referenced.Any(e =&gt; e != module.RecordEntity)</c>. A
/// <c>&lt;link-entity&gt;</c> whose name equals the module's own record entity — a SELF-JOIN — adds
/// only that same name to the referenced set, so the guard passes it.
///
/// Why that matters: Tier-2 scoping filters only PRIMARY rows
/// (<c>Tier2ScopeFilterInjector</c> / <c>ExternalModuleDescriptor.ScopeRows</c>). Aliased columns
/// pulled through a self-joined link-entity are extra attributes ON an in-scope primary row and are
/// never scope-checked, and <c>FetchService.ProjectEntity</c> serializes <c>AliasedValue</c> straight
/// to the client. The result is cross-matter / cross-client field disclosure on a caller-controlled
/// FetchXML surface.
///
/// These tests exercise <see cref="FetchXmlEntityExtractor"/> — the guard's load-bearing input — and
/// then replay the guard's own boolean against that input, so the pinned behavior is the real
/// decision, not a paraphrase of it. The extractor is <c>internal</c>; the BFF already declares
/// <c>InternalsVisibleTo("Sprk.Bff.Api.Tests")</c>, so no production change is needed to reach it.
/// </summary>
public class ExternalScopeCharacterizationTests
{
    private const string ModuleRecordEntity = "sprk_document";

    private static IReadOnlySet<string> Extract(string fetchXml) =>
        new FetchXmlEntityExtractor().ExtractEntities(fetchXml);

    /// <summary>
    /// The guard's exact predicate, transcribed from
    /// <c>ExternalModuleDataEndpoints.ExecuteScopedFetchAsync:160-161</c>. Returns true when the
    /// fetch is REJECTED.
    /// </summary>
    private static bool GuardRejects(IReadOnlySet<string> referenced, string moduleRecordEntity) =>
        referenced.Count == 0 ||
        referenced.Any(e => !string.Equals(e, moduleRecordEntity, StringComparison.OrdinalIgnoreCase));

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
        GuardRejects(referenced, ModuleRecordEntity).Should().BeTrue();
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
        GuardRejects(referenced, ModuleRecordEntity).Should().BeTrue();
    }

    [Fact]
    public void ExtractEntities_ForMalformedFetchXml_ThrowsParseException()
    {
        // Fail-closed on unparseable input — the endpoint maps this to 400 DV_FETCHXML_MALFORMED.
        var act = () => Extract("<fetch><entity name='sprk_document'>");

        act.Should().Throw<FetchXmlParseException>();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // CHARACTERIZATION — A-17. Flipped by task 011 (FR-10).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A-17 — CURRENT (BROKEN) BEHAVIOR. A SELF-JOIN link-entity contributes only the module's own
    /// entity name, so the referenced set is indistinguishable from a plain single-entity query and
    /// the guard cannot tell the two apart.
    ///
    /// FLIPPED BY: task 011 (FR-10) — the self-join MUST then be rejected. After that task this test
    /// asserts the fetch is rejected (or that the extractor surfaces link-entity presence separately
    /// from entity identity).
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

        // Assert — CURRENT behavior: identical referenced sets. The guard's only input cannot
        // distinguish an exfiltrating self-join from a benign single-entity read.
        selfJoinReferenced.Should().BeEquivalentTo(plainReferenced,
            "A-17 pins the CURRENT broken state: a self-join adds only the module's own entity name, " +
            "so the guard at ExecuteScopedFetchAsync:160-161 sees exactly what a plain query produces");
        selfJoinReferenced.Should().ContainSingle().Which.Should().Be(ModuleRecordEntity);
    }

    /// <summary>
    /// A-17 — the decision itself: the guard ADMITS the self-join. This is the assertion task 011
    /// must invert; it states the security outcome rather than the intermediate set.
    ///
    /// FLIPPED BY: task 011 (FR-10) — MUST become BeTrue (rejected).
    /// </summary>
    [Fact]
    public void Characterization_Guard_AdmitsSelfJoinThatCanExfiltrateOutOfScopeRows()
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

        var referenced = Extract(selfJoinFetchXml);

        GuardRejects(referenced, ModuleRecordEntity).Should().BeFalse(
            "A-17 pins the CURRENT broken state: the self-join passes the entity guard, and Tier-2 " +
            "scoping only filters PRIMARY rows — aliased columns from out-of-scope rows ride out to " +
            "the client. Task 011 rejects the self-join and flips this to BeTrue.");
    }

    /// <summary>
    /// A-17 — the same hole with a differently-cased entity name, confirming case-insensitivity does
    /// not incidentally close it. Documents the full shape task 011 must cover.
    ///
    /// FLIPPED BY: task 011 (FR-10).
    /// </summary>
    [Fact]
    public void Characterization_Guard_AdmitsSelfJoinRegardlessOfCasing()
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
        GuardRejects(referenced, ModuleRecordEntity).Should().BeFalse();
    }
}
