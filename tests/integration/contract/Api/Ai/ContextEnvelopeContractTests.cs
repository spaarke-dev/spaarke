using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Contract test for <see cref="ContextEnvelope"/> v1 (spaarke-ai-architecture-redesign-r2 task
/// AIR2-015, spec FR-A0-01, design D-M2). Locks the canonical six-slice shape, the stability classes,
/// the per-slice budget fields, the NFR-04 stable-prefix-before-volatile-tail ordering, the ADR-040
/// ledger-facade (references-not-copies) invariant, the NFR-07 counts-only posture, and the
/// tolerant-reader / partial-slice rules.
/// </summary>
/// <remarks>
/// Self-contained by design (task AIR2-015 parallel-safety): pure shape + reference producer/consumer
/// round-trip. No DI, no <c>WebApplicationFactory</c>, no <c>Mock&lt;HttpMessageHandler&gt;</c>, and no
/// DI-registration assertions (ADR-038). It is a contract-anchor (maintain-class) test — it fails the
/// instant the slice set, ordering, or ledger-facade guarantee regresses.
/// </remarks>
public class ContextEnvelopeContractTests
{
    // ---------------------------------------------------------------------
    // AC1 — six canonical slices, each with a stability class + budget field;
    //       version field + tolerant-reader posture present.
    // ---------------------------------------------------------------------

    [Fact]
    public void Contract_DeclaresExactlySixCanonicalSlices_InOrder()
    {
        ContextEnvelope.CanonicalAssemblyOrder.Should().Equal(
            ContextSliceKind.User,
            ContextSliceKind.Workspace,
            ContextSliceKind.Business,
            ContextSliceKind.Organizational,
            ContextSliceKind.Semantic,
            ContextSliceKind.Memory);

        Enum.GetValues<ContextSliceKind>().Should().BeEquivalentTo(new[]
        {
            ContextSliceKind.User,
            ContextSliceKind.Workspace,
            ContextSliceKind.Business,
            ContextSliceKind.Memory,
            ContextSliceKind.Organizational,
            ContextSliceKind.Semantic,
        }, "FR-A0-01 fixes the canonical six-slice set {User, Workspace, Business, Memory, Organizational, Semantic}");
    }

    [Fact]
    public void Contract_EverySlice_CarriesAStabilityClassAndBudgetField()
    {
        var envelope = BuildRepresentativeEnvelope();

        foreach (var kind in ContextEnvelope.CanonicalAssemblyOrder)
        {
            var meta = envelope.MetaFor(kind);
            meta.Should().NotBeNull($"the {kind} slice must be assembled in the representative envelope");

            // Stability class is a first-class enum field on every slice.
            meta!.Stability.Should().BeOneOf(
                SliceStability.StablePrefix, SliceStability.SemiStable, SliceStability.VolatileTail);

            // Budget field exists on every slice (int?). Task 054 (FR-B-05) ratified the budgeted slices
            // against the FR-P0-02 measurement, so a budget-bearing slice is no longer provisional; the
            // interface-only slices (Organizational/Semantic) declare no budget.
            if (meta!.BudgetTokens is not null)
            {
                meta.BudgetIsProvisional.Should().BeFalse(
                    $"the {kind} slice's budget was ratified by task 054 (FR-B-05) — no longer provisional");
            }
        }
    }

    [Fact]
    public void Contract_CarriesVersionStamp()
    {
        BuildRepresentativeEnvelope().Version.Should().Be("context-envelope/v1");
        ContextEnvelope.SchemaVersionValue.Should().Be("context-envelope/v1");
    }

    [Fact]
    public void Contract_BindingBudgets_ClearMeasuredBaseline()
    {
        // Task 002 baseline (notes/prompt-assembly-baseline.md §4) measured Environment ~111 and
        // Business ~1,118 — both ABOVE the a-priori D-M2 estimates (≤50 / ≤1,200). Task 054's binding
        // budgets (FR-B-05) MUST clear those measured floors so the real assembly is not born breaching.
        EnvelopeBudget.Environment.Should().BeGreaterThan(111,
            "Environment budget must clear the measured ~111 clock-directive floor");
        EnvelopeBudget.Business.Should().BeGreaterThan(1118,
            "Business budget must clear the measured ~1,118 baseline");
    }

    // ---------------------------------------------------------------------
    // AC2 — stable-prefix slices ordered BEFORE the volatile ledger tail (NFR-04).
    // ---------------------------------------------------------------------

    [Fact]
    public void AssemblyOrder_AllStablePrefixSlices_PrecedeTheVolatileTail()
    {
        var envelope = BuildRepresentativeEnvelope();
        var order = ContextEnvelope.CanonicalAssemblyOrder;

        int LastIndexOf(SliceStability s) =>
            order.Select((k, i) => (k, i)).Where(t => envelope.MetaFor(t.k)?.Stability == s).Select(t => t.i).DefaultIfEmpty(-1).Max();
        int FirstIndexOf(SliceStability s) =>
            order.Select((k, i) => (k, i)).Where(t => envelope.MetaFor(t.k)?.Stability == s).Select(t => t.i).DefaultIfEmpty(int.MaxValue).Min();

        var lastStablePrefix = LastIndexOf(SliceStability.StablePrefix);
        var firstVolatile = FirstIndexOf(SliceStability.VolatileTail);

        lastStablePrefix.Should().BeLessThan(firstVolatile,
            "NFR-04: every stable-prefix slice must be assembled before the volatile ledger tail for prompt-cache stability");
    }

    [Fact]
    public void AssemblyOrder_TheVolatileTail_IsTheMemoryConversationSlice_LastInOrder()
    {
        var envelope = BuildRepresentativeEnvelope();

        ContextEnvelope.CanonicalAssemblyOrder[^1].Should().Be(ContextSliceKind.Memory,
            "the volatile ledger tail (Memory.Conversation) must sort last");
        envelope.Memory!.Meta.Stability.Should().Be(SliceStability.VolatileTail);
    }

    // ---------------------------------------------------------------------
    // AC3 — producer assembles a minimal envelope from primitives (Conversation via the
    //       ledger facade); consumer reads it — round-trip proven.
    // ---------------------------------------------------------------------

    [Fact]
    public void Producer_AssemblesMinimalEnvelope_Consumer_ReadsPresentSlicesInOrder()
    {
        var conversation = new[]
        {
            new LedgerEntryReference { Key = "binding-abc@t1", UcId = "uc-classify", Disposition = "informational" },
            new LedgerEntryReference { Key = "binding-abc@t2", UcId = "uc-summarize", Disposition = "informational" },
        };

        var envelope = ContextEnvelopeReferenceProducer.Assemble(
            userFragment: "How should I proceed?",
            workspaceFragment: "The current date is 2026-07-08.",
            businessFragment: "This chat is hosted on the matter record Smith v. Jones.",
            conversation: conversation);

        var present = ContextEnvelopeReferenceConsumer.PresentSlicesInOrder(envelope);

        present.Should().ContainInOrder(
            ContextSliceKind.User, ContextSliceKind.Workspace, ContextSliceKind.Business, ContextSliceKind.Memory);
        present.Should().NotContain(ContextSliceKind.Organizational, "the org provider is interface-only in r2 (empty)");
        present.Should().NotContain(ContextSliceKind.Semantic, "the semantic provider is interface-only in r2 (empty)");

        // The consumer summary is identifiers-and-counts only (NFR-07) — no slice content leaks.
        var summary = ContextEnvelopeReferenceConsumer.RenderPresenceSummary(envelope);
        summary.Should().Contain("Memory=VolatileTail");
        summary.Should().NotContain("Smith v. Jones", "the presence summary must never carry slice content (NFR-07)");
        summary.Should().NotContain("How should I proceed", "the presence summary must never carry slice content (NFR-07)");

        envelope.Memory!.Meta.ReferenceCount.Should().Be(2, "the ledger-facade reference count is carried as a count");
    }

    // ---------------------------------------------------------------------
    // AC4 — unknown extra slice/field ignored (tolerant reader); budget fields carry counts only (NFR-07).
    // ---------------------------------------------------------------------

    [Fact]
    public void TolerantReader_UnknownSliceAndUnknownField_AreIgnoredOnDeserialize()
    {
        var envelope = BuildRepresentativeEnvelope();
        var node = JsonNode.Parse(JsonSerializer.Serialize(envelope, ContextEnvelope.JsonOptions))!.AsObject();

        // A future v-next adds a slice and a field this v1 reader has never seen.
        node["experimentalSlice"] = new JsonObject { ["meta"] = new JsonObject { ["stability"] = 1 } };
        node["futureField"] = 42;
        node["user"]!.AsObject()["someFutureUserField"] = "ignored";

        var act = () => JsonSerializer.Deserialize<ContextEnvelope>(node.ToJsonString(), ContextEnvelope.JsonOptions);
        act.Should().NotThrow("tolerant readers ignore unknown slices/fields (additive-only evolution)");

        var roundTripped = JsonSerializer.Deserialize<ContextEnvelope>(node.ToJsonString(), ContextEnvelope.JsonOptions)!;
        roundTripped.Version.Should().Be("context-envelope/v1");
        roundTripped.User.Should().NotBeNull();
        roundTripped.Memory!.Meta.Stability.Should().Be(SliceStability.VolatileTail);
    }

    [Fact]
    public void BudgetAndCountFields_CarryCountsOnly_NoContentMember_OnSliceMeta()
    {
        // NFR-07: SliceMeta is the telemetry surface — it must expose counts only, never a content member.
        var stringProps = typeof(SliceMeta).GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .ToList();

        stringProps.Should().BeEmpty(
            "SliceMeta must carry counts/enums/bools only (NFR-07) — no string member that could hold slice content");
    }

    // ---------------------------------------------------------------------
    // AC5 — no Mock<HttpMessageHandler>, no DI-registration assertions (ADR-038).
    //       (Enforced by construction: this whole class is pure + DI-free.)
    // ---------------------------------------------------------------------

    // ---------------------------------------------------------------------
    // AC6 — NEGATIVE: an envelope that copies ledger CONTENT into Memory.Conversation
    //       (rather than referencing entries) fails a test assertion (ADR-040 no parallel cache).
    // ---------------------------------------------------------------------

    [Fact]
    public void LedgerFacade_ConversationReferenceType_ExposesNoContentBearingMember()
    {
        // The Memory.Conversation element type is the structural guarantee of "references not copies".
        var elementType = typeof(MemorySlice).GetProperty(nameof(MemorySlice.Conversation))!
            .PropertyType.GetGenericArguments().Single();
        elementType.Should().Be<LedgerEntryReference>();

        HasContentBearingMember(elementType).Should().BeFalse(
            "ADR-040: the ledger facade references entries by key — it must NOT expose a content/payload/text member that would let a copied ledger payload into the envelope");
    }

    [Fact]
    public void LedgerFacade_ContentDetector_IsProvenToCatchAContentCopy()
    {
        // NEGATIVE CONTROL (mirrors the sibling BusinessSliceDeterminism discipline): a would-be
        // "copy the ledger content into the slice" regression introduces a content-bearing member.
        // The SAME detector used by the real guarantee above must FLAG it — proving that test would
        // actually catch the ADR-040 violation rather than pass by construction.
        HasContentBearingMember(typeof(BadLedgerRefWithContent)).Should().BeTrue(
            "the detector must catch a reference type that carries copied ledger content — otherwise the ADR-040 guarantee above is vacuous");
    }

    // ---------------------------------------------------------------------
    // Partial / missing-slice states (goal + tolerant-reader).
    // ---------------------------------------------------------------------

    [Fact]
    public void PartialEnvelope_MissingSlices_Deserialize_AndAreReportedAbsent()
    {
        const string partialJson = """
        {
          "version": "context-envelope/v1",
          "user": { "meta": { "stability": 0, "budgetTokens": 300, "present": true }, "fragment": "hi" },
          "memory": { "meta": { "stability": 2, "present": false }, "conversation": [], "items": [] }
        }
        """;

        var envelope = JsonSerializer.Deserialize<ContextEnvelope>(partialJson, ContextEnvelope.JsonOptions)!;

        envelope.Workspace.Should().BeNull("an omitted slice is a legitimate partial state");
        envelope.Business.Should().BeNull();
        envelope.MetaFor(ContextSliceKind.Workspace).Should().BeNull();

        var present = ContextEnvelopeReferenceConsumer.PresentSlicesInOrder(envelope);
        present.Should().Equal(new[] { ContextSliceKind.User },
            "only the User slice is present-and-flagged in this partial envelope");
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static ContextEnvelope BuildRepresentativeEnvelope() =>
        ContextEnvelopeReferenceProducer.Assemble(
            userFragment: "What next?",
            workspaceFragment: "The current date is 2026-07-08.",
            businessFragment: "Hosted on the matter record.",
            conversation: new[]
            {
                new LedgerEntryReference { Key = "b1@t1", UcId = "uc-1", Disposition = "informational" },
            },
            memoryItems: new[]
            {
                new MemoryItemReference { ItemId = "mem-1", Scope = "record", Source = "explicit" },
            });

    private static bool HasContentBearingMember(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => p.PropertyType == typeof(string)
                      && (p.Name.Contains("Content", StringComparison.OrdinalIgnoreCase)
                          || p.Name.Contains("Payload", StringComparison.OrdinalIgnoreCase)
                          || p.Name.Contains("Text", StringComparison.OrdinalIgnoreCase)
                          || p.Name.Contains("Body", StringComparison.OrdinalIgnoreCase)));

    // Simulated ADR-040 regression used ONLY by the negative-control test above.
    private sealed record BadLedgerRefWithContent
    {
        public required string Key { get; init; }
        public string? Payload { get; init; } // <-- the forbidden copied-content member
    }
}
