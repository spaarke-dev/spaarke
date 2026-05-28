using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Insights.Observations;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Insights.Observations;

/// <summary>
/// Unit tests for <see cref="ObservationMirrorMapper"/> — the pure-mapping component
/// (task 051 D-P11 mirror sync) that converts an <see cref="ObservationArtifact"/> to a
/// <c>sprk_analysis</c> <see cref="Entity"/> payload, plus the SHA-256-based idempotency
/// key computation.
/// </summary>
/// <remarks>
/// Schema mapping decisions verified here per <c>notes/sprk-analysis-polymorphic-confirmation.md</c>:
/// the <c>sprk_analysis</c> table has no source-type discriminator field, so the mapper
/// writes <c>sprk_searchprofile = "insights-observation@v1"</c> and uses
/// <c>sprk_sessionid</c> for the idempotency key.
/// </remarks>
public class ObservationMirrorMapperTests
{
    private static readonly Guid ActionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DocumentId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset FixedAsOf = new(2026, 5, 28, 14, 22, 0, TimeSpan.Zero);

    private static ObservationArtifact MakeObservation(
        string id = "obs:M-2024-0341:outcomeCategory:doc-abc123",
        string predicate = "outcomeCategory",
        string? quote = "The matter was resolved by a favorable settlement.",
        double confidence = 0.92,
        JsonElement? value = null)
    {
        var valueElement = value ?? JsonDocument.Parse("\"favorable\"").RootElement.Clone();
        var evidence = quote is null
            ? Array.Empty<EvidenceRef>()
            : new[]
              {
                  new EvidenceRef
                  {
                      RefType = "document",
                      Ref = "spe://drive/drive-xyz/item/item-abc123",
                      Quote = quote,
                  },
              };

        return new ObservationArtifact
        {
            Id = id,
            Subject = "matter:M-2024-0341",
            Predicate = predicate,
            Value = new Value { Raw = valueElement, DisplayHint = "enum" },
            Evidence = evidence,
            AsOf = FixedAsOf,
            ProducedBy = new ProducedBy
            {
                Kind = "playbook",
                Id = "playbook://outcome-extraction",
                Version = "v1",
            },
            Scope = new Scope
            {
                TenantId = "tenant-acme",
                MatterId = "M-2024-0341",
                PracticeArea = "ip-licensing",
            },
            TenantId = "tenant-acme",
            Confidence = confidence,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ComputeIdempotencyKey
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeIdempotencyKey_Deterministic_SameInputProducesSameKey()
    {
        var key1 = ObservationMirrorMapper.ComputeIdempotencyKey("obs:abc:predicate:doc-1");
        var key2 = ObservationMirrorMapper.ComputeIdempotencyKey("obs:abc:predicate:doc-1");
        key1.Should().Be(key2);
    }

    [Fact]
    public void ComputeIdempotencyKey_DifferentInput_ProducesDifferentKey()
    {
        var key1 = ObservationMirrorMapper.ComputeIdempotencyKey("obs:abc:predicate:doc-1");
        var key2 = ObservationMirrorMapper.ComputeIdempotencyKey("obs:abc:predicate:doc-2");
        key1.Should().NotBe(key2);
    }

    [Fact]
    public void ComputeIdempotencyKey_FitsIn50CharSessionIdField()
    {
        var key = ObservationMirrorMapper.ComputeIdempotencyKey("obs:abc:predicate:doc-1");
        key.Length.Should().BeLessOrEqualTo(ObservationMirrorMapper.IdempotencyKeyMaxLength);
        key.Length.Should().Be(ObservationMirrorMapper.IdempotencyKeyMaxLength); // SHA-256 hex truncated to 50
    }

    [Fact]
    public void ComputeIdempotencyKey_HexEncoded()
    {
        var key = ObservationMirrorMapper.ComputeIdempotencyKey("obs:abc:predicate:doc-1");
        // Uppercase hex per Convert.ToHexString
        key.Should().MatchRegex("^[0-9A-F]+$");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ComputeIdempotencyKey_NullOrBlank_Throws(string? observationId)
    {
        Action act = () => ObservationMirrorMapper.ComputeIdempotencyKey(observationId!);
        act.Should().Throw<ArgumentException>().WithParameterName("observationId");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BuildEntity — required fields
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildEntity_NullObservation_Throws()
    {
        Action act = () => ObservationMirrorMapper.BuildEntity(null!, ActionId, DocumentId);
        act.Should().Throw<ArgumentNullException>().WithParameterName("observation");
    }

    [Fact]
    public void BuildEntity_EmptyActionId_Throws()
    {
        Action act = () => ObservationMirrorMapper.BuildEntity(MakeObservation(), Guid.Empty, DocumentId);
        act.Should().Throw<ArgumentException>().WithParameterName("analysisActionId");
    }

    [Fact]
    public void BuildEntity_EmptyDocumentId_Throws()
    {
        Action act = () => ObservationMirrorMapper.BuildEntity(MakeObservation(), ActionId, Guid.Empty);
        act.Should().Throw<ArgumentException>().WithParameterName("documentId");
    }

    [Fact]
    public void BuildEntity_UsesCorrectEntityLogicalName()
    {
        var entity = ObservationMirrorMapper.BuildEntity(MakeObservation(), ActionId, DocumentId);
        entity.LogicalName.Should().Be(ObservationMirrorMapper.EntityName);
        entity.LogicalName.Should().Be("sprk_analysis");
    }

    [Fact]
    public void BuildEntity_SetsRequiredActionLookup()
    {
        var entity = ObservationMirrorMapper.BuildEntity(MakeObservation(), ActionId, DocumentId);
        var actionRef = entity["sprk_actionid"].Should().BeOfType<EntityReference>().Subject;
        actionRef.LogicalName.Should().Be("sprk_analysisaction");
        actionRef.Id.Should().Be(ActionId);
    }

    [Fact]
    public void BuildEntity_SetsRequiredDocumentLookup()
    {
        var entity = ObservationMirrorMapper.BuildEntity(MakeObservation(), ActionId, DocumentId);
        var docRef = entity["sprk_documentid"].Should().BeOfType<EntityReference>().Subject;
        docRef.LogicalName.Should().Be("sprk_document");
        docRef.Id.Should().Be(DocumentId);
    }

    [Fact]
    public void BuildEntity_SetsDiscriminator_OnSearchProfileField()
    {
        var entity = ObservationMirrorMapper.BuildEntity(MakeObservation(), ActionId, DocumentId);
        entity[ObservationMirrorMapper.DiscriminatorField].Should().Be(ObservationMirrorMapper.ArtifactTypeDiscriminator);
        entity["sprk_searchprofile"].Should().Be("insights-observation@v1");
    }

    [Fact]
    public void BuildEntity_SetsIdempotencyKey_OnSessionIdField()
    {
        var observation = MakeObservation();
        var expected = ObservationMirrorMapper.ComputeIdempotencyKey(observation.Id);

        var entity = ObservationMirrorMapper.BuildEntity(observation, ActionId, DocumentId);

        entity[ObservationMirrorMapper.IdempotencyKeyField].Should().Be(expected);
        entity["sprk_sessionid"].Should().Be(expected);
    }

    [Fact]
    public void BuildEntity_SetsStatus_Completed()
    {
        var entity = ObservationMirrorMapper.BuildEntity(MakeObservation(), ActionId, DocumentId);
        var status = entity["sprk_analysisstatus"].Should().BeOfType<OptionSetValue>().Subject;
        status.Value.Should().Be(ObservationMirrorMapper.AnalysisStatusCompleted);
        status.Value.Should().Be(2); // matches sprk_analysisstatus.Completed enum
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BuildEntity — display name
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildEntity_DisplayName_HasPredicateValueConfidence()
    {
        var entity = ObservationMirrorMapper.BuildEntity(MakeObservation(), ActionId, DocumentId);
        var name = entity["sprk_name"].Should().BeOfType<string>().Subject;
        name.Should().Contain("outcomeCategory");
        name.Should().Contain("favorable");
        name.Should().Contain("0.92");
    }

    [Fact]
    public void BuildEntity_DisplayName_TruncatedTo200Chars()
    {
        var longPredicate = new string('p', 250);
        var obs = MakeObservation(predicate: longPredicate);
        var entity = ObservationMirrorMapper.BuildEntity(obs, ActionId, DocumentId);
        var name = entity["sprk_name"].Should().BeOfType<string>().Subject;
        name.Length.Should().BeLessOrEqualTo(ObservationMirrorMapper.NameMaxLength);
        name.Length.Should().Be(ObservationMirrorMapper.NameMaxLength); // exactly 200 chars when input would exceed
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("null", "null")]
    public void BuildEntity_DisplayName_RendersPrimitiveValueTypes(string rawJson, string expectedFragment)
    {
        var value = JsonDocument.Parse(rawJson).RootElement.Clone();
        var entity = ObservationMirrorMapper.BuildEntity(MakeObservation(value: value), ActionId, DocumentId);
        var name = (string)entity["sprk_name"];
        name.Should().Contain(expectedFragment);
    }

    [Fact]
    public void BuildEntity_DisplayName_RendersObjectValueAsJson()
    {
        var value = JsonDocument.Parse("""{"category":"favorable","detail":"x"}""").RootElement.Clone();
        var entity = ObservationMirrorMapper.BuildEntity(MakeObservation(value: value), ActionId, DocumentId);
        var name = (string)entity["sprk_name"];
        name.Should().Contain("category");
        name.Should().Contain("favorable");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BuildEntity — timestamps
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildEntity_SetsStartedAndCompletedTimestamps_FromAsOf()
    {
        var entity = ObservationMirrorMapper.BuildEntity(MakeObservation(), ActionId, DocumentId);
        var started = entity["sprk_startedon"].Should().BeOfType<DateTime>().Subject;
        var completed = entity["sprk_completedon"].Should().BeOfType<DateTime>().Subject;
        started.Should().Be(FixedAsOf.UtcDateTime);
        completed.Should().Be(FixedAsOf.UtcDateTime);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BuildEntity — full envelope serialization
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildEntity_FinalOutput_IsParseableObservationJson()
    {
        var observation = MakeObservation();
        var entity = ObservationMirrorMapper.BuildEntity(observation, ActionId, DocumentId);

        var json = entity["sprk_finaloutput"].Should().BeOfType<string>().Subject;

        // Round-trips through the polymorphic InsightArtifact serializer
        var rehydrated = JsonSerializer.Deserialize<InsightArtifact>(json);
        rehydrated.Should().BeOfType<ObservationArtifact>();
        rehydrated!.Id.Should().Be(observation.Id);
        rehydrated.Subject.Should().Be(observation.Subject);
        rehydrated.Predicate.Should().Be(observation.Predicate);
        ((ObservationArtifact)rehydrated).Confidence.Should().Be(observation.Confidence);
    }

    [Fact]
    public void BuildEntity_FinalOutput_IncludesEvidenceArray()
    {
        var entity = ObservationMirrorMapper.BuildEntity(MakeObservation(), ActionId, DocumentId);
        var json = (string)entity["sprk_finaloutput"];
        json.Should().Contain("\"evidence\"");
        json.Should().Contain("spe://drive/drive-xyz/item/item-abc123");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BuildEntity — chathistory producer context
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildEntity_ChatHistory_IsParseableProducerContextJson()
    {
        var entity = ObservationMirrorMapper.BuildEntity(MakeObservation(), ActionId, DocumentId);
        var json = entity["sprk_chathistory"].Should().BeOfType<string>().Subject;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("producedBy").GetProperty("kind").GetString().Should().Be("playbook");
        root.GetProperty("producedBy").GetProperty("id").GetString().Should().Be("playbook://outcome-extraction");
        root.GetProperty("producedBy").GetProperty("version").GetString().Should().Be("v1");
        root.GetProperty("scope").GetProperty("tenantId").GetString().Should().Be("tenant-acme");
        root.GetProperty("scope").GetProperty("matterId").GetString().Should().Be("M-2024-0341");
        root.GetProperty("tenantId").GetString().Should().Be("tenant-acme");
        root.GetProperty("evidenceCount").GetInt32().Should().Be(1);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BuildEntity — primary quote extraction
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildEntity_WorkingDocument_ContainsVerbatimQuote()
    {
        var entity = ObservationMirrorMapper.BuildEntity(MakeObservation(), ActionId, DocumentId);
        entity["sprk_workingdocument"].Should().Be("The matter was resolved by a favorable settlement.");
    }

    [Fact]
    public void BuildEntity_NoEvidence_WorkingDocument_IsEmpty()
    {
        var entity = ObservationMirrorMapper.BuildEntity(MakeObservation(quote: null), ActionId, DocumentId);
        entity["sprk_workingdocument"].Should().Be(string.Empty);
    }
}
