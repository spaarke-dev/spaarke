using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Insights.LiveFacts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Insights.LiveFacts;

/// <summary>
/// Unit tests for <see cref="DataverseLiveFactResolver"/> (task 071, Wave 8.5 pre-deploy
/// gap fix, 2026-05-29). Verifies each of the 4 predicates the D-P14 predict-matter-cost
/// synthesis playbook needs resolves to a deterministic <see cref="FactArtifact"/> with
/// confidence 1.0 and proper evidence reference shape per design.md §2.1 + SPEC §3.4.1.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope</b>:
/// <list type="bullet">
///   <item>Each of 4 predicates (attorney, client, matterType, opposingCounsel) →
///   returns FactArtifact with correct value/predicate/confidence</item>
///   <item>Composite predicate (currentMatterFacts) → returns single FactArtifact with
///   all 4 sub-values composed into <see cref="Value.Raw"/></item>
///   <item>Unsupported predicate → throws <see cref="LiveFactNotSupportedException"/></item>
///   <item>Invalid subject format → throws <see cref="LiveFactNotSupportedException"/></item>
///   <item>Empty/missing lookup → returns null (Subject not found semantics)</item>
///   <item>Matter row not found → returns null</item>
/// </list>
/// </para>
/// <para>
/// <b>Zone B placement</b> per SPEC §3.5 — these tests verify
/// DataverseLiveFactResolver depends ONLY on IGenericEntityService. No AI internals.
/// </para>
/// </remarks>
public class DataverseLiveFactResolverTests
{
    private const string TenantId = "tenant-acme";
    private static readonly Guid MatterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly string MatterSubject = $"matter:{MatterId}";

    private static readonly Guid AttorneyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid MatterTypeId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OpposingCounselId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly Mock<IGenericEntityService> _entityServiceMock = new(MockBehavior.Strict);

    private DataverseLiveFactResolver CreateSut()
        => new(_entityServiceMock.Object, NullLogger<DataverseLiveFactResolver>.Instance);

    /// <summary>
    /// Build a fully-populated sprk_matter Entity for the happy-path tests.
    /// All 4 lookup fields populated with sensible Names so the FactArtifacts surface
    /// proper display values.
    /// </summary>
    private static Entity BuildMatter()
    {
        var matter = new Entity("sprk_matter", MatterId);
        matter["sprk_matterid"] = MatterId;
        matter["sprk_matternumber"] = "M-2026-0042";
        matter["sprk_assignedattorney1"] = new EntityReference("contact", AttorneyId) { Name = "Jane Smith" };
        matter["sprk_externalaccount"] = new EntityReference("account", ClientId) { Name = "Acme Corp" };
        matter["sprk_mattertype"] = new EntityReference("sprk_mattertype_ref", MatterTypeId) { Name = "IP licensing" };
        matter["sprk_assignedlawfirm2"] = new EntityReference("sprk_organization", OpposingCounselId) { Name = "BigFirm LLP" };
        return matter;
    }

    private void SetupEntityServiceReturnsMatter(Entity matter)
    {
        _entityServiceMock
            .Setup(s => s.RetrieveAsync(
                "sprk_matter",
                MatterId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(matter);
    }

    // ─── Per-predicate happy path tests ──────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_AttorneyPredicate_ReturnsFactWithEntityReferenceValue()
    {
        SetupEntityServiceReturnsMatter(BuildMatter());
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(MatterSubject, "attorney", TenantId, CancellationToken.None);

        fact.Should().NotBeNull();
        fact!.Subject.Should().Be(MatterSubject);
        fact.Predicate.Should().Be("attorney");
        fact.Confidence.Should().Be(1.0, "Facts are always certain per design.md §2.1");
        fact.TenantId.Should().Be(TenantId);
        fact.Value.DisplayHint.Should().Be("entity-reference");

        // Value.Raw is { id, name } JSON object
        fact.Value.Raw.GetProperty("id").GetString().Should().Be(AttorneyId.ToString());
        fact.Value.Raw.GetProperty("name").GetString().Should().Be("Jane Smith");

        // Evidence is fact-source pointing at the Dataverse field per SPEC §3.4.1
        fact.Evidence.Should().HaveCount(1);
        fact.Evidence[0].RefType.Should().Be("fact-source");
        fact.Evidence[0].Ref.Should().Be($"dataverse://sprk_matter/{MatterId}#attorney");

        // ProducedBy carries the system-of-record identity per design.md §2.2
        fact.ProducedBy.Kind.Should().Be("query");
        fact.ProducedBy.Id.Should().Be("dataverse://sprk_matter");
        fact.ProducedBy.Version.Should().Be("v1");
    }

    [Fact]
    public async Task ResolveAsync_ClientPredicate_ReturnsFactWithAccountReference()
    {
        SetupEntityServiceReturnsMatter(BuildMatter());
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(MatterSubject, "client", TenantId, CancellationToken.None);

        fact.Should().NotBeNull();
        fact!.Predicate.Should().Be("client");
        fact.Confidence.Should().Be(1.0);
        fact.Value.Raw.GetProperty("id").GetString().Should().Be(ClientId.ToString());
        fact.Value.Raw.GetProperty("name").GetString().Should().Be("Acme Corp");
        fact.Evidence[0].Ref.Should().Be($"dataverse://sprk_matter/{MatterId}#client");
    }

    [Fact]
    public async Task ResolveAsync_MatterTypePredicate_ReturnsFactWithPlainStringValue()
    {
        // matterType returns a plain string (the reference table's display name) per
        // the synthesis prompt's expectation. Not the {id, name} object the lookup
        // predicates use — the prompt template wants a bare type name like "IP licensing".
        SetupEntityServiceReturnsMatter(BuildMatter());
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(MatterSubject, "matterType", TenantId, CancellationToken.None);

        fact.Should().NotBeNull();
        fact!.Predicate.Should().Be("matterType");
        fact.Confidence.Should().Be(1.0);
        fact.Value.DisplayHint.Should().Be("text",
            "matterType is a plain text string for the synthesis prompt");
        fact.Value.Raw.GetString().Should().Be("IP licensing");
        fact.Evidence[0].Ref.Should().Be($"dataverse://sprk_matter/{MatterId}#matterType");
    }

    [Fact]
    public async Task ResolveAsync_OpposingCounselPredicate_ReturnsFactWithOrganizationReference()
    {
        SetupEntityServiceReturnsMatter(BuildMatter());
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(MatterSubject, "opposingCounsel", TenantId, CancellationToken.None);

        fact.Should().NotBeNull();
        fact!.Predicate.Should().Be("opposingCounsel");
        fact.Confidence.Should().Be(1.0);
        fact.Value.Raw.GetProperty("id").GetString().Should().Be(OpposingCounselId.ToString());
        fact.Value.Raw.GetProperty("name").GetString().Should().Be("BigFirm LLP");

        // Per notes/sprk-matter-livefact-predicates.md the Phase 1 mapping is sprk_assignedlawfirm2
        fact.Evidence[0].Ref.Should().Be($"dataverse://sprk_matter/{MatterId}#opposingCounsel");
    }

    // ─── Composite predicate (currentMatterFacts) ────────────────────────────

    [Fact]
    public async Task ResolveAsync_CurrentMatterFactsComposite_ReturnsSingleFactWithAllFourSubvalues()
    {
        // The composite predicate matches the existing predict-matter-cost playbook
        // (task 060) LiveFactNode ConfigJson which uses predicate: "currentMatterFacts".
        // Verifies the resolver supports both per-predicate AND composite shapes from
        // the same sprk_matter read.
        SetupEntityServiceReturnsMatter(BuildMatter());
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(MatterSubject, "currentMatterFacts", TenantId, CancellationToken.None);

        fact.Should().NotBeNull();
        fact!.Predicate.Should().Be("currentMatterFacts");
        fact.Confidence.Should().Be(1.0);
        fact.Value.DisplayHint.Should().Be("matter-facts");

        // All 4 sub-values composed into Value.Raw
        var raw = fact.Value.Raw;
        raw.GetProperty("attorney").GetProperty("name").GetString().Should().Be("Jane Smith");
        raw.GetProperty("client").GetProperty("name").GetString().Should().Be("Acme Corp");
        raw.GetProperty("matterType").GetString().Should().Be("IP licensing");
        raw.GetProperty("opposingCounsel").GetProperty("name").GetString().Should().Be("BigFirm LLP");
    }

    [Fact]
    public async Task ResolveAsync_CurrentMatterFactsComposite_WithMissingLookups_ReturnsFactWithNulls()
    {
        // Defensive: matter row exists but some lookups are unset. The composite returns
        // a FactArtifact whose Value.Raw has null for the missing sub-values. The synthesis
        // prompt handles missing values explicitly.
        var partialMatter = new Entity("sprk_matter", MatterId);
        partialMatter["sprk_matterid"] = MatterId;
        partialMatter["sprk_assignedattorney1"] = new EntityReference("contact", AttorneyId) { Name = "Jane Smith" };
        // client, matterType, opposingCounsel intentionally unset
        SetupEntityServiceReturnsMatter(partialMatter);
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(MatterSubject, "currentMatterFacts", TenantId, CancellationToken.None);

        fact.Should().NotBeNull();
        fact!.Confidence.Should().Be(1.0);
        var raw = fact.Value.Raw;
        raw.GetProperty("attorney").GetProperty("name").GetString().Should().Be("Jane Smith");
        raw.GetProperty("client").ValueKind.Should().Be(JsonValueKind.Null);
        raw.GetProperty("matterType").ValueKind.Should().Be(JsonValueKind.Null);
        raw.GetProperty("opposingCounsel").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ─── Error / edge cases ──────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_UnsupportedPredicate_ThrowsLiveFactNotSupportedException()
    {
        // Predicates outside the Phase 1 set (totalSpend, matterDurationDays, etc.) are
        // reserved for Phase 1.5+ when the synthesis playbook portfolio grows. They MUST
        // throw LiveFactNotSupportedException so LiveFactNode emits a clean
        // InvalidConfiguration error (graceful authoring feedback for playbook authors).
        SetupEntityServiceReturnsMatter(BuildMatter());
        var sut = CreateSut();

        Func<Task> act = () => sut.ResolveAsync(MatterSubject, "unsupportedPredicate", TenantId, CancellationToken.None);

        await act.Should().ThrowAsync<LiveFactNotSupportedException>()
            .Where(ex => ex.Subject == MatterSubject && ex.Predicate == "unsupportedPredicate");
    }

    [Fact]
    public async Task ResolveAsync_InvalidSubjectScheme_ThrowsLiveFactNotSupportedException()
    {
        // Only matter: subject scheme is supported in Phase 1. document:, party:, etc. are
        // reserved for Phase 1.5+. Invalid scheme MUST surface as LiveFactNotSupportedException
        // (NOT silently return null) so playbook authoring errors are loud, not silent.
        var sut = CreateSut();

        Func<Task> act = () => sut.ResolveAsync("document:abc-123", "attorney", TenantId, CancellationToken.None);

        await act.Should().ThrowAsync<LiveFactNotSupportedException>();
    }

    [Fact]
    public async Task ResolveAsync_MalformedMatterGuid_ThrowsLiveFactNotSupportedException()
    {
        var sut = CreateSut();

        Func<Task> act = () => sut.ResolveAsync("matter:not-a-guid", "attorney", TenantId, CancellationToken.None);

        await act.Should().ThrowAsync<LiveFactNotSupportedException>();
    }

    [Fact]
    public async Task ResolveAsync_EmptyMatterGuid_ThrowsLiveFactNotSupportedException()
    {
        // Defensive against subject = "matter:00000000-0000-0000-0000-000000000000" which is
        // a valid Guid parse but a meaningless matter row reference.
        var sut = CreateSut();

        Func<Task> act = () => sut.ResolveAsync($"matter:{Guid.Empty}", "attorney", TenantId, CancellationToken.None);

        await act.Should().ThrowAsync<LiveFactNotSupportedException>();
    }

    [Fact]
    public async Task ResolveAsync_MatterNotFound_ReturnsNull()
    {
        // Spaarke.Dataverse surfaces "row not found" as InvalidOperationException with
        // a specific message; the resolver maps this to null per the ILiveFactResolver
        // contract ("Subject not found in Dataverse" — LiveFactNode emits NodeErrorCodes.InternalError).
        _entityServiceMock
            .Setup(s => s.RetrieveAsync(
                "sprk_matter",
                MatterId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Entity sprk_matter with id ... was not found."));

        var sut = CreateSut();

        var fact = await sut.ResolveAsync(MatterSubject, "attorney", TenantId, CancellationToken.None);

        fact.Should().BeNull("matter row does not exist; surface as null per ILiveFactResolver contract");
    }

    [Fact]
    public async Task ResolveAsync_LookupFieldUnset_ReturnsNull()
    {
        // The matter row exists but the specific lookup field is unset (e.g., a Tentative
        // matter without an assigned attorney yet). Resolver returns null; LiveFactNode
        // surfaces "Subject not found" — the playbook's downstream sufficiency check handles
        // missing facts (or the synthesis prompt accepts nulls in the templated facts object).
        var matterWithoutAttorney = new Entity("sprk_matter", MatterId);
        matterWithoutAttorney["sprk_matterid"] = MatterId;
        // sprk_assignedattorney1 intentionally unset
        SetupEntityServiceReturnsMatter(matterWithoutAttorney);
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(MatterSubject, "attorney", TenantId, CancellationToken.None);

        fact.Should().BeNull("attorney lookup is unset on the matter row");
    }

    // ─── Argument validation ─────────────────────────────────────────────────

    [Theory]
    [InlineData("", "attorney", "tenant-x")]
    [InlineData("   ", "attorney", "tenant-x")]
    [InlineData("matter:" + "11111111-1111-1111-1111-111111111111", "", "tenant-x")]
    [InlineData("matter:" + "11111111-1111-1111-1111-111111111111", "attorney", "")]
    public async Task ResolveAsync_BlankRequiredArguments_Throws(string subject, string predicate, string tenantId)
    {
        var sut = CreateSut();
        Func<Task> act = () => sut.ResolveAsync(subject, predicate, tenantId, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Constructor_NullEntityService_Throws()
    {
        Action act = () => new DataverseLiveFactResolver(null!, NullLogger<DataverseLiveFactResolver>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("entityService");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Action act = () => new DataverseLiveFactResolver(_entityServiceMock.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ─── Subject parser unit test ────────────────────────────────────────────

    [Theory]
    [InlineData("matter:11111111-1111-1111-1111-111111111111", "11111111-1111-1111-1111-111111111111")]
    [InlineData("MATTER:11111111-1111-1111-1111-111111111111", "11111111-1111-1111-1111-111111111111")] // case-insensitive scheme
    [InlineData("matter: 11111111-1111-1111-1111-111111111111 ", "11111111-1111-1111-1111-111111111111")] // tolerates whitespace
    public void ParseMatterSubject_ValidFormats_ReturnsGuid(string subject, string expectedGuid)
    {
        var result = DataverseLiveFactResolver.ParseMatterSubject(subject);
        result.Should().NotBeNull();
        result!.Value.Should().Be(Guid.Parse(expectedGuid));
    }

    [Theory]
    [InlineData("document:11111111-1111-1111-1111-111111111111")] // wrong scheme
    [InlineData("matter:not-a-guid")]                              // unparseable suffix
    [InlineData("matter:")]                                        // empty suffix
    [InlineData("11111111-1111-1111-1111-111111111111")]           // missing scheme
    [InlineData("")]                                               // empty
    [InlineData("matter:00000000-0000-0000-0000-000000000000")]    // Guid.Empty
    public void ParseMatterSubject_InvalidFormats_ReturnsNull(string subject)
    {
        var result = DataverseLiveFactResolver.ParseMatterSubject(subject);
        result.Should().BeNull();
    }
}
