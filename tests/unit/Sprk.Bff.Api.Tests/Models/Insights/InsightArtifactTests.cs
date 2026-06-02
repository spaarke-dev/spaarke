using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Insights;
using Xunit;

namespace Sprk.Bff.Api.Tests.Models.Insights;

/// <summary>
/// Round-trip tests for the <see cref="InsightArtifact"/> envelope POCOs (D-P1).
/// <para>
/// Verifies all four tiers (Fact / Observation / Precedent / Inference) serialize and deserialize
/// through System.Text.Json with the correct <c>type</c> discriminator, that <see cref="EvidenceRef"/>
/// shape matches the SPEC §3.4.1 worked example, and that <see cref="DeclineResponse"/> round-trips
/// all five D-49 fields.
/// </para>
/// </summary>
public class InsightArtifactTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // System.Text.Json default; explicit for test clarity.
        PropertyNamingPolicy = null
    };

    private static JsonElement Raw(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    // ─── Tier 1: Fact ───────────────────────────────────────────────────────

    [Fact]
    public void FactArtifact_RoundTrips_WithFactDiscriminator()
    {
        var fact = new FactArtifact
        {
            Id = "fact:M-1234:matterDurationDays",
            Subject = "matter:M-1234",
            Predicate = "matterDurationDays",
            Value = new Value { Raw = Raw("287"), DisplayHint = "duration-days" },
            AsOf = new DateTimeOffset(2026, 5, 19, 8, 30, 0, TimeSpan.Zero),
            ProducedBy = new ProducedBy { Kind = "query", Id = "query://matter-duration" },
            Scope = new Scope { TenantId = "tenant-acme", MatterId = "M-1234" },
            TenantId = "tenant-acme"
        };

        var json = JsonSerializer.Serialize<InsightArtifact>(fact, JsonOptions);
        json.Should().Contain("\"type\":\"fact\"");

        var deserialized = JsonSerializer.Deserialize<InsightArtifact>(json, JsonOptions);
        deserialized.Should().BeOfType<FactArtifact>();
        var deserializedFact = (FactArtifact)deserialized!;
        deserializedFact.Confidence.Should().Be(1.0);
        deserializedFact.Id.Should().Be(fact.Id);
        deserializedFact.Subject.Should().Be(fact.Subject);
        deserializedFact.Value.DisplayHint.Should().Be("duration-days");
        deserializedFact.Value.Raw.GetInt32().Should().Be(287);
    }

    // ─── Tier 2: Observation (SPEC §3.4.1 worked example shape) ─────────────

    [Fact]
    public void ObservationArtifact_RoundTrips_WithObservationDiscriminator_AndEvidenceRefs()
    {
        var obs = new ObservationArtifact
        {
            Id = "obs:M-2024-0341:outcomeCategory:doc-abc123",
            Subject = "matter:M-2024-0341",
            Predicate = "outcomeCategory",
            Value = new Value
            {
                Raw = Raw("\"favorable_to_client\""),
                DisplayHint = "enum"
            },
            Confidence = 0.91,
            Evidence = new List<EvidenceRef>
            {
                new()
                {
                    RefType = "document",
                    Ref = "spe://drive/acme-matters/item/closing-letter-M-2024-0341.docx",
                    Quote = "The matter concluded with terms favorable to our client, securing all material rights sought in the original complaint."
                },
                new()
                {
                    RefType = "playbook-run",
                    Ref = "playbook://outcome-extraction@v1/run-2026-04-12T08:30:00Z"
                }
            },
            AsOf = new DateTimeOffset(2026, 4, 12, 8, 30, 0, TimeSpan.Zero),
            ProducedBy = new ProducedBy
            {
                Kind = "playbook",
                Id = "playbook://outcome-extraction@v1",
                Version = "v1"
            },
            Scope = new Scope { TenantId = "tenant-acme", MatterId = "M-2024-0341" },
            TenantId = "tenant-acme",
            Embedding = new float[] { 0.0234f, 0.1872f }
        };

        var json = JsonSerializer.Serialize<InsightArtifact>(obs, JsonOptions);
        json.Should().Contain("\"type\":\"observation\"");
        json.Should().Contain("\"refType\":\"document\"");
        json.Should().Contain("\"quote\":\"The matter concluded");
        json.Should().Contain("\"version\":\"v1\"");

        var deserialized = JsonSerializer.Deserialize<InsightArtifact>(json, JsonOptions);
        deserialized.Should().BeOfType<ObservationArtifact>();
        var deserializedObs = (ObservationArtifact)deserialized!;
        deserializedObs.Confidence.Should().Be(0.91);
        deserializedObs.Evidence.Should().HaveCount(2);
        deserializedObs.Evidence[0].RefType.Should().Be("document");
        deserializedObs.Evidence[0].Quote.Should().StartWith("The matter concluded");
        deserializedObs.Evidence[1].RefType.Should().Be("playbook-run");
        deserializedObs.Evidence[1].Quote.Should().BeNull();
        deserializedObs.ProducedBy.Version.Should().Be("v1");
        deserializedObs.Embedding.Should().NotBeNull();
        deserializedObs.Embedding!.Count.Should().Be(2);
    }

    // ─── Tier 3: Precedent (SPEC §3.4.2 worked example shape) ───────────────

    [Fact]
    public void PrecedentArtifact_RoundTrips_WithPrecedentDiscriminator_AndNoConfidence()
    {
        var precedentRaw = """
        {
          "patternTitle": "IP licensing matters with BigFirm LLP — cure-period clauses survive",
          "scope": { "matterType": "IP licensing", "opposingCounsel": "BigFirm LLP" },
          "supportingMatters": ["M-2024-0341", "M-2024-0188", "M-2024-0099"],
          "sampleSize": 14,
          "patternConsistency": 0.86
        }
        """;

        var precedent = new PrecedentArtifact
        {
            Id = "prec:bigfirm-cure-period-survives:v1",
            Subject = "pattern:ip-licensing-bigfirm-llp",
            Predicate = "pattern",
            Value = new Value
            {
                Raw = Raw(precedentRaw),
                DisplayHint = "precedent-statement"
            },
            Evidence = new List<EvidenceRef>
            {
                new() { RefType = "supporting-matter", Ref = "matter://M-2024-0341" },
                new() { RefType = "supporting-matter", Ref = "matter://M-2024-0188" }
            },
            AsOf = new DateTimeOffset(2026, 5, 15, 14, 22, 0, TimeSpan.Zero),
            ProducedBy = new ProducedBy { Kind = "agent", Id = "manual-sme-author" },
            Scope = new Scope { TenantId = "tenant-acme", PracticeArea = "ip-licensing" },
            TenantId = "tenant-acme",
            Status = "confirmed"
        };

        var json = JsonSerializer.Serialize<InsightArtifact>(precedent, JsonOptions);
        json.Should().Contain("\"type\":\"precedent\"");
        json.Should().Contain("\"status\":\"confirmed\"");
        // Precedents are SME-confirmed — no probabilistic confidence in shape.
        json.Should().NotContain("\"confidence\"");

        var deserialized = JsonSerializer.Deserialize<InsightArtifact>(json, JsonOptions);
        deserialized.Should().BeOfType<PrecedentArtifact>();
        var deserializedPrec = (PrecedentArtifact)deserialized!;
        deserializedPrec.Status.Should().Be("confirmed");
        deserializedPrec.Value.DisplayHint.Should().Be("precedent-statement");

        // Verify nested JSON structure survives round-trip in Value.Raw.
        var rawProp = deserializedPrec.Value.Raw;
        rawProp.GetProperty("patternTitle").GetString().Should().StartWith("IP licensing matters with BigFirm");
        rawProp.GetProperty("sampleSize").GetInt32().Should().Be(14);
        rawProp.GetProperty("scope").GetProperty("opposingCounsel").GetString().Should().Be("BigFirm LLP");
        rawProp.GetProperty("supportingMatters").GetArrayLength().Should().Be(3);

        deserializedPrec.Evidence.Should().HaveCount(2);
        deserializedPrec.Evidence[0].RefType.Should().Be("supporting-matter");
    }

    // ─── Tier 4: Inference (SPEC §3.4.3 worked example shape) ───────────────

    [Fact]
    public void InferenceArtifact_RoundTrips_WithInferenceDiscriminator_AndOptionalReasoning()
    {
        var inference = new InferenceArtifact
        {
            Id = "inf:M-NEW-0042:predictedCost:run-abc",
            Subject = "matter:M-NEW-0042",
            Predicate = "predictedCost",
            Value = new Value { Raw = Raw("280000"), DisplayHint = "currency-usd" },
            Confidence = 0.74,
            Reasoning = "Composed from 12 comparable IP-licensing matters and 1 cited Precedent.",
            Evidence = new List<EvidenceRef>
            {
                new() { RefType = "comparable-matter", Ref = "matter://M-2024-0341" },
                new() { RefType = "comparable-matter", Ref = "matter://M-2024-0188" },
                new()
                {
                    RefType = "playbook-run",
                    Ref = "playbook://predict-matter-cost@v1/run-2026-05-28T10:00:00Z"
                }
            },
            AsOf = new DateTimeOffset(2026, 5, 28, 10, 0, 0, TimeSpan.Zero),
            ProducedBy = new ProducedBy
            {
                Kind = "playbook",
                Id = "playbook://predict-matter-cost@v1",
                Version = "v1"
            },
            Scope = new Scope
            {
                TenantId = "tenant-acme",
                MatterId = "M-NEW-0042",
                PracticeArea = "ip-licensing"
            },
            TenantId = "tenant-acme"
        };

        var json = JsonSerializer.Serialize<InsightArtifact>(inference, JsonOptions);
        json.Should().Contain("\"type\":\"inference\"");
        json.Should().Contain("\"reasoning\":");

        var deserialized = JsonSerializer.Deserialize<InsightArtifact>(json, JsonOptions);
        deserialized.Should().BeOfType<InferenceArtifact>();
        var deserializedInf = (InferenceArtifact)deserialized!;
        deserializedInf.Confidence.Should().Be(0.74);
        deserializedInf.Reasoning.Should().StartWith("Composed from 12");
        deserializedInf.Value.Raw.GetInt32().Should().Be(280000);
        deserializedInf.Evidence.Should().HaveCount(3);
    }

    // ─── EvidenceRef shape (SPEC §3.4.1 acceptance criterion) ───────────────

    [Fact]
    public void EvidenceRef_SerializesWith_RefType_Ref_AndOptionalQuote()
    {
        var withQuote = new EvidenceRef
        {
            RefType = "document",
            Ref = "spe://drive/abc/item/xyz",
            Quote = "verbatim text"
        };
        var withoutQuote = new EvidenceRef
        {
            RefType = "fact-source",
            Ref = "dataverse://sprk_matter/M-1234#totalSpend"
        };

        var jsonWithQuote = JsonSerializer.Serialize(withQuote, JsonOptions);
        jsonWithQuote.Should().Contain("\"refType\":\"document\"");
        jsonWithQuote.Should().Contain("\"ref\":\"spe://drive/abc/item/xyz\"");
        jsonWithQuote.Should().Contain("\"quote\":\"verbatim text\"");

        var jsonWithoutQuote = JsonSerializer.Serialize(withoutQuote, JsonOptions);
        jsonWithoutQuote.Should().Contain("\"refType\":\"fact-source\"");
        jsonWithoutQuote.Should().Contain("\"ref\":\"dataverse://sprk_matter/M-1234#totalSpend\"");
        // Optional quote omitted when null — default System.Text.Json behaviour drops nulls
        // only with WriteIndented/IgnoreNullValues; default writes "quote":null. Accept either.
        // We assert the round-trip preserves null below.

        var roundTripped = JsonSerializer.Deserialize<EvidenceRef>(jsonWithoutQuote, JsonOptions);
        roundTripped.Should().NotBeNull();
        roundTripped!.RefType.Should().Be("fact-source");
        roundTripped.Quote.Should().BeNull();
    }

    // ─── DeclineResponse (D-49 acceptance criterion) ────────────────────────

    [Fact]
    public void DeclineResponse_RoundTrips_AllFiveFields()
    {
        var decline = new DeclineResponse
        {
            Reason = "insufficient-evidence",
            Explanation = "Only 4 comparable matters were found; the predict-matter-cost playbook requires at least 12.",
            MinimumEvidenceNeeded = new Dictionary<string, object>
            {
                ["comparableMatters"] = new Dictionary<string, int> { ["have"] = 4, ["need"] = 12 }
            },
            SuggestedActions = new[]
            {
                "Broaden the matter-type filter from 'IP licensing' to 'IP'",
                "Author a Precedent for this opposing counsel"
            },
            ConfidenceInDecline = 0.95
        };

        var json = JsonSerializer.Serialize(decline, JsonOptions);
        json.Should().Contain("\"reason\":\"insufficient-evidence\"");
        json.Should().Contain("\"explanation\":");
        json.Should().Contain("\"minimumEvidenceNeeded\":");
        json.Should().Contain("\"suggestedActions\":");
        json.Should().Contain("\"confidenceInDecline\":0.95");

        var deserialized = JsonSerializer.Deserialize<DeclineResponse>(json, JsonOptions);
        deserialized.Should().NotBeNull();
        deserialized!.Reason.Should().Be("insufficient-evidence");
        deserialized.Explanation.Should().StartWith("Only 4 comparable matters");
        deserialized.MinimumEvidenceNeeded.Should().ContainKey("comparableMatters");
        deserialized.SuggestedActions.Should().HaveCount(2);
        deserialized.SuggestedActions[0].Should().StartWith("Broaden the matter-type filter");
        deserialized.ConfidenceInDecline.Should().Be(0.95);
    }

    // ─── Cross-tier polymorphism — round-tripping a heterogeneous array ─────

    [Fact]
    public void InsightArtifact_PolymorphicArray_PreservesEachTierType()
    {
        InsightArtifact[] artifacts = new InsightArtifact[]
        {
            new FactArtifact
            {
                Id = "fact:1",
                Subject = "matter:M-1",
                Predicate = "p",
                Value = new Value { Raw = Raw("1"), DisplayHint = "duration-days" },
                AsOf = DateTimeOffset.UtcNow,
                ProducedBy = new ProducedBy { Kind = "query", Id = "q" },
                Scope = new Scope { TenantId = "t" },
                TenantId = "t"
            },
            new ObservationArtifact
            {
                Id = "obs:1",
                Subject = "matter:M-1",
                Predicate = "p",
                Value = new Value { Raw = Raw("1"), DisplayHint = "enum" },
                Confidence = 0.8,
                AsOf = DateTimeOffset.UtcNow,
                ProducedBy = new ProducedBy { Kind = "playbook", Id = "pb", Version = "v1" },
                Scope = new Scope { TenantId = "t" },
                TenantId = "t"
            },
            new PrecedentArtifact
            {
                Id = "prec:1",
                Subject = "pattern:x",
                Predicate = "pattern",
                Value = new Value { Raw = Raw("{}"), DisplayHint = "precedent-statement" },
                AsOf = DateTimeOffset.UtcNow,
                ProducedBy = new ProducedBy { Kind = "agent", Id = "manual-sme-author" },
                Scope = new Scope { TenantId = "t" },
                TenantId = "t",
                Status = "confirmed"
            },
            new InferenceArtifact
            {
                Id = "inf:1",
                Subject = "matter:M-1",
                Predicate = "predictedCost",
                Value = new Value { Raw = Raw("1"), DisplayHint = "currency-usd" },
                Confidence = 0.7,
                AsOf = DateTimeOffset.UtcNow,
                ProducedBy = new ProducedBy { Kind = "playbook", Id = "pb", Version = "v1" },
                Scope = new Scope { TenantId = "t" },
                TenantId = "t"
            }
        };

        var json = JsonSerializer.Serialize(artifacts, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<InsightArtifact[]>(json, JsonOptions);

        roundTripped.Should().NotBeNull();
        roundTripped!.Should().HaveCount(4);
        roundTripped![0].Should().BeOfType<FactArtifact>();
        roundTripped![1].Should().BeOfType<ObservationArtifact>();
        roundTripped![2].Should().BeOfType<PrecedentArtifact>();
        roundTripped![3].Should().BeOfType<InferenceArtifact>();
    }
}
