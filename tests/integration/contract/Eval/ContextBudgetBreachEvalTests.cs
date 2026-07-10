using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.Eval;

/// <summary>
/// AIR2-054 (FR-B-05 / FR-D-02) — the ContextEnvelope token-budget BREACH-FAILS-EVAL gate, joined to the
/// golden-utterance MERGE GATE via the <c>Category=GoldenUtteranceEval</c> trait (no CI-YAML change; the
/// trait IS the registration — see <c>tests/integration/contract/Eval/README.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is the mechanical enforcement of the binding per-slice + ceiling budgets ratified against the
/// FR-P0-02 measurement (<c>notes/prompt-assembly-baseline.md</c>; reconciliation
/// <c>notes/054-budget-reconciliation.md</c>). It drives the REAL production budget path end to end — real
/// <see cref="ContextEnvelopeReferenceProducer"/> assembly, real <see cref="EnvironmentFactsProducer"/> /
/// <see cref="HostIdentityProducer"/> / <see cref="ConversationContextProducer"/> rendering, and the real
/// <see cref="EnvelopeBudget.Evaluate"/> checker — so a budget breach is caught mechanically before merge
/// rather than in UAT. No <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI-registration assertions (ADR-038);
/// the whole class is pure + DI-free (KEEP path <c>tests/integration/contract/**</c>).
/// </para>
/// <para>
/// <b>How this is the gate</b>: a REPRESENTATIVE record-context turn MUST produce no breach (so a future
/// change that bloats a stable-prefix slice past budget turns this eval RED), and a CRAFTED overflow turn
/// MUST be DETECTED as a breach — never silently truncated to fit (the FR-B-05 negative criterion). If the
/// evaluator ever regressed to silent truncation or stopped detecting an overflow, the negative tests go
/// RED and fail the <c>Category=GoldenUtteranceEval</c> gate (FR-D-02).
/// </para>
/// </remarks>
[Trait("Category", "GoldenUtteranceEval")]
public class ContextBudgetBreachEvalTests
{
    // =====================================================================================
    // Ratified budgets (FR-B-05) — pin the reconciliation. A silent change to any binding
    // budget fails the merge gate here, forcing the reconciliation note to be updated too.
    // =====================================================================================

    [Fact]
    public void BindingBudgets_MatchTheRatifiedReconciliation()
    {
        EnvelopeBudget.Environment.Should().Be(150, "measured ~111 clock directive; ≤50 estimate predated it");
        EnvelopeBudget.User.Should().Be(300, "measured ~40; keeps the D-M2 ceiling for long dictated turns");
        EnvelopeBudget.Business.Should().Be(1500,
            "measured ~1,118 with a ~968-token unconditional-directive protocol floor; raised from ≤1,200 so playbook content fits");
        EnvelopeBudget.RecordMemory.Should().Be(600, "measured 157; kept (docs target 200–500 at higher density)");
        EnvelopeBudget.Conversation.Should().Be(2000,
            "kept at the D-M2 estimate; the ~8,000 structural worst-case is gated HERE (breach-fails-eval), not by tightening R1 constants");
        EnvelopeBudget.EnvelopeCeiling.Should().Be(4200,
            "independent bound, tighter than the 4,550 sum-of-maxes; normal turns sit at ~50–57%");
    }

    // =====================================================================================
    // POSITIVE — a representative record-context turn (mirrors baseline §2 ~2,025–2,410) is
    // within every budget. Guards the negatives against vacuity: if this ever breaches, a
    // stable-prefix slice bloated past its ratified budget and the gate goes RED.
    // =====================================================================================

    [Fact]
    public void RepresentativeRecordContextTurn_IsWithinAllBudgets_NoBreach()
    {
        var envelope = BuildEnvelope(
            userFragment: "Can you summarize the latest filing and note any deadlines?",
            businessFragment: HostIdentityProducer.BuildEnrichmentBlock(
                "matter", "11111111-1111-1111-1111-111111111111", "Acme Corp v. Beta LLC", "matter record"),
            outputs: new[] { BuildOutput("bind-classify@t1", "uc-classify", 1, PayloadString(230)) });

        var conversationTokens = RenderedConversationTokens(new[]
        {
            BuildOutput("bind-classify@t1", "uc-classify", 1, PayloadString(230)),
        });

        var report = EnvelopeBudget.Evaluate(envelope, conversationTokens);

        report.HasBreach.Should().BeFalse(
            $"a representative record-context turn sits well under the budgets ({report.RenderSummary()})");
        report.TotalTokens.Should().BeLessThan(EnvelopeBudget.EnvelopeCeiling);
    }

    // =====================================================================================
    // NEGATIVE — a crafted Business overflow is DETECTED as a breach, and the fragment is
    // NOT truncated to fit (the FR-B-05 "fail, not silently truncate" criterion).
    // =====================================================================================

    [Fact]
    public void CraftedBusinessOverflow_IsDetectedAsBreach_AndNotSilentlyTruncated()
    {
        // ~2,000 tokens (8,000 chars) > the 1,500 Business budget. A sentinel proves NFR-07 later.
        const string sentinel = "SENTINEL_BUSINESS_CONTENT_MARKER";
        var oversizedBusiness = sentinel + new string('x', 8_000);

        var envelope = BuildEnvelope(
            userFragment: "hi",
            businessFragment: oversizedBusiness,
            outputs: Array.Empty<SessionOutput>());

        var report = EnvelopeBudget.Evaluate(envelope);

        report.HasBreach.Should().BeTrue("an 8,000-char Business fragment (~2,000 tokens) exceeds the 1,500 budget");
        report.BreachedSlices.Should().Contain(ContextBudgetSlice.Business);

        // NOT silently truncated: the envelope still carries the full fragment; the checker FLAGS the
        // breach for the caller instead of clipping content to fit the budget.
        envelope.Business!.Fragment.Should().Be(oversizedBusiness,
            "budget enforcement surfaces the breach — it must never mutate/truncate the assembled fragment");
        envelope.Business.Meta.EstimatedTokens.Should().BeGreaterThan(EnvelopeBudget.Business);
    }

    // =====================================================================================
    // NEGATIVE — the baseline §4 finding-3 structural risk: the Conversation ledger tail can
    // reach ~8,000 tokens (MaxContextOutputs=8 × MaxContextPayloadChars=4,000). Rendered by the
    // REAL producer, it MUST breach the 2,000 Conversation budget (and the envelope ceiling) and
    // MUST NOT be silently clipped to fit — this is the mechanical gate on finding 3.
    // =====================================================================================

    [Fact]
    public void CraftedConversationOverflow_StructuralWorstCase_IsDetectedAsBreach_NotClipped()
    {
        var outputs = Enumerable.Range(1, ConversationContextProducer.MaxContextOutputs)
            .Select(i => BuildOutput($"bind-x@t{i}", "uc-x", i, PayloadString(ConversationContextProducer.MaxContextPayloadChars)))
            .ToArray();

        // Rendered by the REAL production producer (no reimplementation).
        var rendered = ConversationContextProducer.BuildLedgerOutputsContext(outputs);
        rendered.Should().NotBeNull();
        var conversationTokens = EnvelopeBudget.EstimateTokens(rendered);

        conversationTokens.Should().BeGreaterThan(EnvelopeBudget.Conversation,
            "the 8×4,000-char structural worst case renders far above the 2,000-token Conversation budget (finding 3)");

        var envelope = BuildEnvelope(
            userFragment: "and the earlier summaries?",
            businessFragment: HostIdentityProducer.BuildEnrichmentBlock(
                "matter", "22222222-2222-2222-2222-222222222222", null, null),
            outputs: outputs);

        var report = EnvelopeBudget.Evaluate(envelope, conversationTokens);

        report.HasBreach.Should().BeTrue();
        report.BreachedSlices.Should().Contain(ContextBudgetSlice.Conversation);
        report.BreachedSlices.Should().Contain(ContextBudgetSlice.Envelope, "8,000+ conversation tokens also blow the ceiling");

        // NOT clipped: the real producer preserved all eight windowed outputs' content; the budget check
        // flags the overflow rather than the pipeline silently dropping context to stay under budget.
        (rendered!.Length / 4).Should().BeGreaterThan(EnvelopeBudget.Conversation,
            "the rendered conversation content is preserved in full — the breach is surfaced, not truncated to fit");
    }

    // =====================================================================================
    // NEGATIVE — the envelope CEILING is an independent bound: every per-slice count can be
    // UNDER its own budget while the sum breaches ≤4,200. The ceiling catches that.
    // =====================================================================================

    [Fact]
    public void CeilingOverflow_AllSlicesUnderTheirOwnBudget_ButSumBreachesCeiling()
    {
        // business 1,490 (<1,500) + user 290 (<300) + env ~111 (<150) + recordMemory 590 (<600)
        // + conversation 1,990 (<2,000) = 4,471 > 4,200 — ceiling breaches, no per-slice does.
        var envelope = BuildEnvelope(
            userFragment: new string('u', 1_160),          // (1160+3)/4 = 290
            businessFragment: new string('b', 5_960),       // (5960+3)/4 = 1490
            outputs: Array.Empty<SessionOutput>());

        var report = EnvelopeBudget.Evaluate(envelope, conversationTokens: 1_990, recordMemoryTokens: 590);

        report.HasBreach.Should().BeTrue();
        report.BreachedSlices.Should().Equal(new[] { ContextBudgetSlice.Envelope },
            "no single slice exceeds its budget — only the envelope ceiling (independent bound) breaches");
        report.TotalTokens.Should().BeGreaterThan(EnvelopeBudget.EnvelopeCeiling);
    }

    // =====================================================================================
    // Boundary — exactly-at-budget is NOT a breach; one token over IS (proves the check is a
    // strict-greater-than, not vacuously always/never breaching).
    // =====================================================================================

    [Fact]
    public void BudgetBoundary_AtBudgetPasses_OneTokenOverBreaches()
    {
        var atBudget = BuildEnvelope(businessFragment: new string('b', 6_000)); // (6000+3)/4 = 1500 == budget
        EnvelopeBudget.Evaluate(atBudget).BreachedSlices.Should().NotContain(ContextBudgetSlice.Business,
            "exactly at budget (1,500) is not a breach");

        var overBudget = BuildEnvelope(businessFragment: new string('b', 6_004)); // (6004+3)/4 = 1501 > budget
        EnvelopeBudget.Evaluate(overBudget).BreachedSlices.Should().Contain(ContextBudgetSlice.Business,
            "one token over budget (1,501) is a breach");
    }

    // =====================================================================================
    // ADR-040 / NFR-07 — ledger entries travel as REFERENCES (content rendered outside the
    // envelope); the budget report carries counts only, never slice content.
    // =====================================================================================

    [Fact]
    public void LedgerConversation_TravelsAsReferences_ContentMeasuredOutsideTheEnvelope()
    {
        var outputs = new[]
        {
            BuildOutput("bind-a@t1", "uc-a", 1, PayloadString(600)),
            BuildOutput("bind-a@t2", "uc-a", 2, PayloadString(600)),
        };
        var envelope = BuildEnvelope(userFragment: "recap?", outputs: outputs);

        // The envelope holds REFERENCES with zero copied content; the tokens come from the separately
        // rendered producer output — proving "references in the envelope, content rendered outside".
        envelope.Memory!.Conversation.Should().HaveCount(2);
        envelope.Memory.Meta.EstimatedTokens.Should().Be(0, "no ledger content is copied into the envelope (ADR-040)");
        envelope.Memory.Meta.ReferenceCount.Should().Be(2);

        var conversationTokens = RenderedConversationTokens(outputs);
        conversationTokens.Should().BeGreaterThan(0, "the conversation content is rendered outside the envelope, then budgeted");
    }

    [Fact]
    public void BudgetReport_RenderSummary_CarriesCountsOnly_NeverSliceContent()
    {
        const string sentinel = "SENTINEL_BUSINESS_CONTENT_MARKER";
        var envelope = BuildEnvelope(businessFragment: sentinel + new string('x', 8_000));

        var summary = EnvelopeBudget.Evaluate(envelope).RenderSummary();

        summary.Should().Contain("Business=", "the summary reports per-slice counts");
        summary.Should().Contain("!", "the breached line is marked");
        summary.Should().NotContain(sentinel, "NFR-07: the budget telemetry summary must never carry slice content");
    }

    // -------------------------------------------------------------------------------------
    // Helpers — drive the REAL producers; no reimplementation of the assembly/render path.
    // -------------------------------------------------------------------------------------

    private static ContextEnvelope BuildEnvelope(
        string? userFragment = null,
        string? businessFragment = null,
        IReadOnlyList<SessionOutput>? outputs = null) =>
        ContextEnvelopeReferenceProducer.Assemble(
            userFragment: userFragment,
            // The real Workspace/Environment fragment (the measured ~111-token clock directive).
            workspaceFragment: EnvironmentFactsProducer.BuildCurrentDateDirective(
                DateTimeOffset.Parse("2026-07-10T12:00:00Z")),
            businessFragment: businessFragment,
            conversation: ConversationContextProducer.BuildConversationTail(new ChatSession(
                SessionId: "sess-budget-eval",
                TenantId: "tenant-budget-eval",
                DocumentId: null,
                PlaybookId: null,
                CreatedAt: default,
                LastActivity: default,
                Messages: Array.Empty<ChatMessage>())
            {
                Outputs = (outputs ?? Array.Empty<SessionOutput>()).ToList(),
            }));

    private static int RenderedConversationTokens(IReadOnlyList<SessionOutput> outputs) =>
        EnvelopeBudget.EstimateTokens(ConversationContextProducer.BuildLedgerOutputsContext(outputs));

    private static SessionOutput BuildOutput(string key, string ucId, int turn, JsonElement payload) => new()
    {
        Key = key,
        BindingId = key.Split('@')[0],
        UcId = ucId,
        Turn = turn,
        Disposition = "informational",
        Payload = payload,
        CreatedAt = DateTimeOffset.Parse("2026-07-10T12:00:00Z"),
    };

    private static JsonElement PayloadString(int chars) =>
        JsonSerializer.SerializeToElement(new string('s', chars));
}
