// FR-11 (spaarkeai-compose-r4 task 003) — round-trip + wire-contract tests for the shared,
// versioned OPERATION SCHEMA (Services/Compose/Operations/ComposeOperation.cs). This is the spine
// both the client (compose-operations.ts) and the server (ComposeShadowPatchEngine, task 030)
// implement identically; the acceptance criterion is that a serialized op log round-trips
// client → server → client WITHOUT LOSS and that both ends validate against the SAME schema.
//
// Each test names a concrete production behavior that breaks if deleted (FR-11 acceptance):
//   - a full ten-op log serializes → deserializes → re-serializes byte-stable (lossless round-trip)
//   - every op's `type` discriminator serializes as the EXACT FR-11 wire literal the TS client keys
//     on (a DIFFERENT codebase depends on these strings — this is a cross-language contract anchor)
//   - the polymorphic deserializer reconstructs each of the ten derived op types with its fields
//   - the envelope carries the schema-version field the client validates against
//
// Banned-pattern compliance (tests/CLAUDE.md B1-B17): pure domain-serialization contract test — no
// Mock<HttpMessageHandler> (B1), no DI-registration (B3), no ctor-null (B4), no getter/mirror (B6/B16).
// This is NOT a trivial STJ round-trip (B12): it locks the CROSS-LANGUAGE wire discriminators + the
// full closed op-set reconstruction that the client compiles against — behavior a consumer notices.

using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose.Operations;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class ComposeOperationSchemaTests
{
    // Web defaults mirror the BFF's wire serialization (camelCase, case-insensitive read). The op
    // records use explicit [JsonPropertyName] + [JsonPolymorphic], so shape is deterministic either way.
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    /// <summary>A canonical op log exercising ALL TEN op types, each anchored by paraId + run-local
    /// point/range, plus the structural ops' second-paragraph (newParaId/targetParaId) references.</summary>
    private static ComposeOperationLog BuildCanonicalLog() => new()
    {
        Operations = new ComposeOperation[]
        {
            new InsertTextOperation
            {
                ParaId = "0A1B2C3D",
                At = new ComposeRunPoint(0, 5),
                Text = "hello",
                Marks = new[] { ComposeMarkType.Bold, ComposeMarkType.Italic },
            },
            new DeleteRangeOperation
            {
                ParaId = "0A1B2C3D",
                Range = new ComposeRunRange(new ComposeRunPoint(0, 2), new ComposeRunPoint(1, 4)),
            },
            new ReplaceRangeOperation
            {
                ParaId = "1122AABB",
                Range = new ComposeRunRange(new ComposeRunPoint(0, 0), new ComposeRunPoint(0, 3)),
                Text = "world",
                Marks = new[] { ComposeMarkType.Underline },
            },
            new SetMarkOperation
            {
                ParaId = "1122AABB",
                Range = new ComposeRunRange(new ComposeRunPoint(0, 1), new ComposeRunPoint(0, 6)),
                Mark = ComposeMarkType.Bold,
            },
            new ClearMarkOperation
            {
                ParaId = "1122AABB",
                Range = new ComposeRunRange(new ComposeRunPoint(0, 1), new ComposeRunPoint(0, 6)),
                Mark = ComposeMarkType.Italic,
            },
            new SplitParagraphOperation
            {
                ParaId = "DEADBEEF",
                At = new ComposeRunPoint(1, 2),
                NewParaId = "CAFEF00D",
            },
            new MergeParagraphOperation
            {
                ParaId = "CAFEF00D",
                TargetParaId = "DEADBEEF",
            },
            new InsertParagraphOperation
            {
                ParaId = "DEADBEEF",
                NewParaId = "0BADF00D",
                Position = ComposeParagraphPosition.After,
            },
            new DeleteParagraphOperation
            {
                ParaId = "0BADF00D",
            },
            new SetBlockAttrOperation
            {
                ParaId = "DEADBEEF",
                Attr = ComposeBlockAttr.Alignment,
                Value = "Center",
            },
            // R5 task 012 (G12) — imported-revision reconciliation ops (Single scope, native-id addressed).
            new AcceptRevisionOperation
            {
                ParaId = "DEADBEEF",
                Scope = ComposeRevisionScope.Single,
                RevisionId = "17",
            },
            new RejectRevisionOperation
            {
                ParaId = "CAFEF00D",
                Scope = ComposeRevisionScope.Single,
                RevisionId = "18",
            },
        },
    };

    [Fact(DisplayName = "FR-11: a full twelve-op log round-trips serialize → deserialize → serialize byte-stable")]
    public void OperationLog_WithAllTwelveOpTypes_RoundTripsWithoutLoss()
    {
        // Arrange
        var log = BuildCanonicalLog();

        // Act — client → server (serialize), server parse (deserialize), server → client (re-serialize)
        var json1 = JsonSerializer.Serialize(log, WireOptions);
        var roundTripped = JsonSerializer.Deserialize<ComposeOperationLog>(json1, WireOptions);
        var json2 = JsonSerializer.Serialize(roundTripped, WireOptions);

        // Assert — lossless: the re-serialized bytes equal the originals
        roundTripped.Should().NotBeNull();
        json2.Should().Be(json1);
        roundTripped!.SchemaVersion.Should().Be(ComposeOperationSchema.Version);
        roundTripped.Operations.Should().HaveCount(12);
    }

    [Fact(DisplayName = "FR-11: the polymorphic deserializer reconstructs each of the twelve derived op types")]
    public void OperationLog_AfterRoundTrip_ReconstructsEveryDerivedOpTypeAndFields()
    {
        // Arrange
        var json = JsonSerializer.Serialize(BuildCanonicalLog(), WireOptions);

        // Act
        var log = JsonSerializer.Deserialize<ComposeOperationLog>(json, WireOptions)!;

        // Assert — each op reconstructs to its concrete derived type with its fields intact
        log.Operations[0].Should().BeOfType<InsertTextOperation>()
            .Which.Should().BeEquivalentTo(new { ParaId = "0A1B2C3D", Text = "hello" });
        ((InsertTextOperation)log.Operations[0]).At.Should().Be(new ComposeRunPoint(0, 5));
        ((InsertTextOperation)log.Operations[0]).Marks.Should().Equal(ComposeMarkType.Bold, ComposeMarkType.Italic);

        log.Operations[1].Should().BeOfType<DeleteRangeOperation>()
            .Which.Range.End.Should().Be(new ComposeRunPoint(1, 4));

        log.Operations[2].Should().BeOfType<ReplaceRangeOperation>()
            .Which.Text.Should().Be("world");

        log.Operations[3].Should().BeOfType<SetMarkOperation>()
            .Which.Mark.Should().Be(ComposeMarkType.Bold);

        log.Operations[4].Should().BeOfType<ClearMarkOperation>()
            .Which.Mark.Should().Be(ComposeMarkType.Italic);

        log.Operations[5].Should().BeOfType<SplitParagraphOperation>()
            .Which.NewParaId.Should().Be("CAFEF00D");

        log.Operations[6].Should().BeOfType<MergeParagraphOperation>()
            .Which.TargetParaId.Should().Be("DEADBEEF");

        log.Operations[7].Should().BeOfType<InsertParagraphOperation>()
            .Which.Position.Should().Be(ComposeParagraphPosition.After);

        log.Operations[8].Should().BeOfType<DeleteParagraphOperation>()
            .Which.ParaId.Should().Be("0BADF00D");

        log.Operations[9].Should().BeOfType<SetBlockAttrOperation>()
            .Which.Should().BeEquivalentTo(new { Attr = ComposeBlockAttr.Alignment, Value = "Center" });

        log.Operations[10].Should().BeOfType<AcceptRevisionOperation>()
            .Which.Should().BeEquivalentTo(new { ParaId = "DEADBEEF", Scope = ComposeRevisionScope.Single, RevisionId = "17" });

        log.Operations[11].Should().BeOfType<RejectRevisionOperation>()
            .Which.Should().BeEquivalentTo(new { ParaId = "CAFEF00D", Scope = ComposeRevisionScope.Single, RevisionId = "18" });
    }

    [Fact(DisplayName = "FR-11: every op serializes with its exact FR-11 `type` discriminator (cross-language wire contract)")]
    public void OperationLog_WhenSerialized_EmitsExactWireDiscriminatorsAndSchemaVersion()
    {
        // Arrange + Act
        var json = JsonSerializer.Serialize(BuildCanonicalLog(), WireOptions);

        // Assert — the TS client keys on these exact strings; a rename here silently breaks the client
        foreach (var discriminator in new[]
                 {
                     "\"type\":\"insertText\"",
                     "\"type\":\"deleteRange\"",
                     "\"type\":\"replaceRange\"",
                     "\"type\":\"setMark\"",
                     "\"type\":\"clearMark\"",
                     "\"type\":\"splitParagraph\"",
                     "\"type\":\"mergeParagraph\"",
                     "\"type\":\"insertParagraph\"",
                     "\"type\":\"deleteParagraph\"",
                     "\"type\":\"setBlockAttr\"",
                     "\"type\":\"acceptRevision\"",
                     "\"type\":\"rejectRevision\"",
                 })
        {
            json.Should().Contain(discriminator);
        }

        // The envelope carries the schema-version field both ends validate against
        json.Should().Contain("\"schemaVersion\":\"compose-ops-v2\"");
        ComposeOperationSchema.Version.Should().Be("compose-ops-v2");
    }

    [Fact(DisplayName = "FR-11: an op log authored as raw client JSON deserializes on the server (client→server direction)")]
    public void OperationLog_FromRawClientJson_DeserializesToTypedOps()
    {
        // Arrange — the shape the TS client emits (camelCase, discriminator-first)
        const string clientJson = """
        {
          "schemaVersion": "compose-ops-v2",
          "operations": [
            { "type": "insertText", "paraId": "0A1B2C3D", "at": { "runIndex": 0, "offset": 5 }, "text": "hi", "marks": ["Bold"] },
            { "type": "splitParagraph", "paraId": "DEADBEEF", "at": { "runIndex": 1, "offset": 2 }, "newParaId": "CAFEF00D" },
            { "type": "setBlockAttr", "paraId": "DEADBEEF", "attr": "Style", "value": "Heading1" }
          ]
        }
        """;

        // Act
        var log = JsonSerializer.Deserialize<ComposeOperationLog>(clientJson, WireOptions)!;

        // Assert — server reconstructs the typed union from client-authored JSON
        log.SchemaVersion.Should().Be("compose-ops-v2");
        log.Operations.Should().HaveCount(3);
        log.Operations[0].Should().BeOfType<InsertTextOperation>();
        log.Operations[1].Should().BeOfType<SplitParagraphOperation>()
            .Which.NewParaId.Should().Be("CAFEF00D");
        log.Operations[2].Should().BeOfType<SetBlockAttrOperation>()
            .Which.Value.Should().Be("Heading1");
    }
}
