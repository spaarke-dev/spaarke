using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Insights.Precedents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Insights.Precedents;

/// <summary>
/// Unit tests for <see cref="PrecedentProjectionMapper"/> — the pure mapping helper that
/// builds <c>spaarke-insights-index</c> documents from <see cref="PrecedentRecord"/> rows
/// per SPEC §3.4.2 worked example.
/// </summary>
/// <remarks>
/// Zone B per SPEC §3.5; tests verify the SPEC §3.4.2 row shape (artifactType, predicate,
/// nested value/raw/scope, evidence[] of supporting-matter refs, contentVector dims=3072,
/// status=confirmed). The 3072 dim check ties to the deployed <c>spaarke-insights-index</c>
/// schema (D-P2 task 010).
/// </remarks>
public class PrecedentProjectionMapperTests
{
    private static readonly Guid PrecedentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string TenantId = "tenant-acme";

    private static PrecedentRecord MakeRecord(
        string? name = "BigFirm cure-period precedent",
        string patternStatement = "In IP licensing matters where BigFirm LLP represents the counterparty, cure-period clauses survived final negotiation in 12 of 14 matters reviewed.",
        int statusValue = PrecedentStatus.Confirmed,
        string? producedBy = "manual-sme-author")
        => new(
            Id: PrecedentId,
            Name: name ?? string.Empty,
            PatternStatement: patternStatement,
            StatusValue: statusValue,
            ReviewerByUserId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ProducedBy: producedBy);

    private static ReadOnlyMemory<float> MakeVector(int dims = 3072)
    {
        var vec = new float[dims];
        for (var i = 0; i < dims; i++)
        {
            vec[i] = i / (float)dims; // deterministic, non-trivial
        }
        return vec;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BuildDocumentId
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildDocumentId_ProducesExpectedFormat()
    {
        var id = PrecedentProjectionMapper.BuildDocumentId(PrecedentId);
        id.Should().Be($"prec:{PrecedentId:N}:v1");
        id.Should().StartWith("prec:");
        id.Should().EndWith(":v1");
    }

    [Fact]
    public void BuildDocumentId_DeterministicForSameInput()
    {
        var a = PrecedentProjectionMapper.BuildDocumentId(PrecedentId);
        var b = PrecedentProjectionMapper.BuildDocumentId(PrecedentId);
        a.Should().Be(b, "the id must be stable so MergeOrUpload is idempotent");
    }

    [Fact]
    public void BuildDocumentId_EmptyGuid_Throws()
    {
        Action act = () => PrecedentProjectionMapper.BuildDocumentId(Guid.Empty);
        act.Should().Throw<ArgumentException>().WithParameterName("precedentId");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BuildDocument — happy path field-by-field
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildDocument_ProducesExpectedSpec342Shape()
    {
        // Arrange
        var record = MakeRecord();
        var vector = MakeVector();
        var supporting = new[]
        {
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        };
        var asOf = new DateTimeOffset(2026, 5, 28, 14, 22, 0, TimeSpan.Zero);

        // Act
        var doc = PrecedentProjectionMapper.BuildDocument(record, TenantId, vector, supporting, asOf);

        // Assert — discriminator + envelope fields per SPEC §3.4.2
        ((string)doc[PrecedentProjectionMapper.FieldId]).Should().Be($"prec:{PrecedentId:N}:v1");
        ((string)doc[PrecedentProjectionMapper.FieldTenantId]).Should().Be(TenantId);
        ((string)doc[PrecedentProjectionMapper.FieldArtifactType]).Should().Be("precedent");
        ((string)doc[PrecedentProjectionMapper.FieldSubject]).Should().Be($"pattern:{PrecedentId:N}");
        ((string)doc[PrecedentProjectionMapper.FieldPredicate]).Should().Be("pattern");
        ((string)doc[PrecedentProjectionMapper.FieldStatus]).Should().Be("confirmed");
        ((string)doc[PrecedentProjectionMapper.FieldProducedBy]).Should().Be("manual-sme-author");
        ((string)doc[PrecedentProjectionMapper.FieldContent]).Should().Be(record.PatternStatement);
        ((DateTimeOffset)doc[PrecedentProjectionMapper.FieldAsOf]).Should().Be(asOf);
    }

    [Fact]
    public void BuildDocument_ConfidenceFieldOmitted_PerSpec342()
    {
        // SPEC §3.4.2: "confidence": null — Precedents are SME-confirmed. The mapper
        // omits the field entirely (Azure Search treats absent as null).
        var doc = PrecedentProjectionMapper.BuildDocument(
            MakeRecord(), TenantId, MakeVector(), Array.Empty<Guid>(), DateTimeOffset.UtcNow);

        doc.ContainsKey(PrecedentProjectionMapper.FieldConfidence).Should().BeFalse(
            "Precedents are SME-confirmed per SPEC §3.4.2 — confidence MUST NOT be written");
    }

    [Fact]
    public void BuildDocument_ContentVectorIs3072Dims_MatchingDeployedSchema()
    {
        var doc = PrecedentProjectionMapper.BuildDocument(
            MakeRecord(), TenantId, MakeVector(), Array.Empty<Guid>(), DateTimeOffset.UtcNow);

        var vector = (float[])doc[PrecedentProjectionMapper.FieldContentVector];
        vector.Should().NotBeNull();
        vector.Length.Should().Be(3072,
            "spaarke-insights-index schema (D-P2 task 010) requires contentVector dims=3072 to match text-embedding-3-large");
    }

    [Fact]
    public void BuildDocument_ValueNested_HasRawAndDisplayHint()
    {
        var supporting = new[] { Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc") };
        var doc = PrecedentProjectionMapper.BuildDocument(
            MakeRecord(), TenantId, MakeVector(), supporting, DateTimeOffset.UtcNow);

        var value = (Dictionary<string, object?>)doc[PrecedentProjectionMapper.FieldValue]!;
        value[PrecedentProjectionMapper.FieldValueDisplayHint].Should().Be("precedent-statement");

        var raw = (Dictionary<string, object?>)value[PrecedentProjectionMapper.FieldValueRaw]!;
        raw[PrecedentProjectionMapper.FieldRawPatternTitle].Should().Be("BigFirm cure-period precedent");
        raw[PrecedentProjectionMapper.FieldRawSampleSize].Should().Be(1);
        ((string[])raw[PrecedentProjectionMapper.FieldRawSupportingMatters]!).Should()
            .ContainSingle(s => s == supporting[0].ToString("D"));
    }

    [Fact]
    public void BuildDocument_ValueRawScope_PopulatedWithSchemaShape()
    {
        // Per SPEC §3.4.2 the scope sub-object has matterType + opposingCounsel. Phase 1
        // does not yet surface these from the Dataverse row, but the schema fields must
        // be present so downstream filter queries don't error on missing-path. The mapper
        // emits an empty-valued scope object.
        var doc = PrecedentProjectionMapper.BuildDocument(
            MakeRecord(), TenantId, MakeVector(), Array.Empty<Guid>(), DateTimeOffset.UtcNow);

        var value = (Dictionary<string, object?>)doc[PrecedentProjectionMapper.FieldValue]!;
        var raw = (Dictionary<string, object?>)value[PrecedentProjectionMapper.FieldValueRaw]!;
        raw.Should().ContainKey(PrecedentProjectionMapper.FieldRawScope);

        var scope = (Dictionary<string, object?>)raw[PrecedentProjectionMapper.FieldRawScope]!;
        scope.Should().ContainKey(PrecedentProjectionMapper.FieldScopeMatterType);
        scope.Should().ContainKey(PrecedentProjectionMapper.FieldScopeOpposingCounsel);
    }

    [Fact]
    public void BuildDocument_Evidence_OnePerSupportingMatter()
    {
        var supporting = new[]
        {
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        };

        var doc = PrecedentProjectionMapper.BuildDocument(
            MakeRecord(), TenantId, MakeVector(), supporting, DateTimeOffset.UtcNow);

        var evidence = (Dictionary<string, object?>[])doc[PrecedentProjectionMapper.FieldEvidence]!;
        evidence.Should().HaveCount(3, "one evidence ref per supporting matter per SPEC §3.4.2");

        foreach (var ev in evidence)
        {
            ev[PrecedentProjectionMapper.FieldEvidenceRefType].Should().Be("supporting-matter");
            ((string)ev[PrecedentProjectionMapper.FieldEvidenceRef]!).Should().StartWith("matter://");
        }
    }

    [Fact]
    public void BuildDocument_NoSupportingMatters_ProducesEmptyEvidenceArray()
    {
        var doc = PrecedentProjectionMapper.BuildDocument(
            MakeRecord(), TenantId, MakeVector(), Array.Empty<Guid>(), DateTimeOffset.UtcNow);

        var evidence = (Dictionary<string, object?>[])doc[PrecedentProjectionMapper.FieldEvidence]!;
        evidence.Should().BeEmpty();

        var raw = (Dictionary<string, object?>)((Dictionary<string, object?>)doc[PrecedentProjectionMapper.FieldValue]!)[PrecedentProjectionMapper.FieldValueRaw]!;
        raw[PrecedentProjectionMapper.FieldRawSampleSize].Should().Be(0);
    }

    [Fact]
    public void BuildDocument_ValueJsonRoundTrips_AsParseableJson()
    {
        var doc = PrecedentProjectionMapper.BuildDocument(
            MakeRecord(), TenantId, MakeVector(), new[] { Guid.NewGuid() }, DateTimeOffset.UtcNow);

        var valueJson = (string)doc[PrecedentProjectionMapper.FieldValueJson]!;
        valueJson.Should().NotBeNullOrEmpty();

        // Should parse cleanly as JSON; per SPEC §3.4.2 the value object has displayHint + raw.
        var parsed = JsonDocument.Parse(valueJson);
        parsed.RootElement.TryGetProperty(PrecedentProjectionMapper.FieldValueDisplayHint, out var dh).Should().BeTrue();
        dh.GetString().Should().Be("precedent-statement");
        parsed.RootElement.TryGetProperty(PrecedentProjectionMapper.FieldValueRaw, out _).Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BuildDocument — argument validation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildDocument_NullRecord_Throws()
    {
        Action act = () => PrecedentProjectionMapper.BuildDocument(
            null!, TenantId, MakeVector(), Array.Empty<Guid>(), DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentNullException>().WithParameterName("record");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildDocument_BlankTenantId_Throws(string tenantId)
    {
        Action act = () => PrecedentProjectionMapper.BuildDocument(
            MakeRecord(), tenantId, MakeVector(), Array.Empty<Guid>(), DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }

    [Fact]
    public void BuildDocument_EmptyVector_Throws()
    {
        Action act = () => PrecedentProjectionMapper.BuildDocument(
            MakeRecord(), TenantId, ReadOnlyMemory<float>.Empty, Array.Empty<Guid>(), DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentException>().WithParameterName("contentVector");
    }

    [Fact]
    public void BuildDocument_NullSupportingMatters_Throws()
    {
        Action act = () => PrecedentProjectionMapper.BuildDocument(
            MakeRecord(), TenantId, MakeVector(), null!, DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentNullException>().WithParameterName("supportingMatterIds");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DerivePatternTitle helper
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DerivePatternTitle_UsesNameWhenPopulated()
    {
        var record = MakeRecord(name: "Custom title", patternStatement: "Full statement here");
        PrecedentProjectionMapper.DerivePatternTitle(record).Should().Be("Custom title");
    }

    [Fact]
    public void DerivePatternTitle_FallsBackToPatternStatementWhenNameEmpty()
    {
        var record = MakeRecord(name: "", patternStatement: "Fallback statement");
        PrecedentProjectionMapper.DerivePatternTitle(record).Should().Be("Fallback statement");
    }

    [Fact]
    public void DerivePatternTitle_TruncatesAt200CharsWhenFallingBack()
    {
        var longStatement = new string('x', 300);
        var record = MakeRecord(name: "", patternStatement: longStatement);
        var title = PrecedentProjectionMapper.DerivePatternTitle(record);
        title.Length.Should().Be(200);
    }
}
