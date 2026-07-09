using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for <see cref="CompletionEngine"/> — the single composer that yields an
/// <see cref="OutcomeCard"/> on every side-effect path (spaarke-ai-architecture-redesign-r2
/// task 035, FR-A1-06, design D-F2).
///
/// <para>
/// <b>KEEP rationale (maintain-class)</b>: these facts anchor the FR-A1-06 completion contract
/// that later phases build on — the next-step chips derived from the Binding's DECLARED
/// transitions (task 062 reads them), the store-before-render invariant (the card references the
/// STORED ledger key — ADR-040), the audience split (user-facing sentence never leaks the
/// internal detail), and the NFR-12 ingestion-parity guarantee inherited through the task-036
/// <see cref="JobAwareOutcomeProjection"/> (a not-yet-finished job is never Succeeded). Each is
/// branched behavior a caller would notice, not a mirror of an implementation line.
/// </para>
/// </summary>
public class CompletionEngineTests
{
    private static readonly Guid BindingId = Guid.Parse("3a1c30d1-cccc-f111-ab0e-70a8a590c51c");

    // ─── Next-step chips: from the Binding's DECLARED transitions (catalog data) ───────────────

    [Fact]
    public void MapNextStepChips_FromDeclaredTransitions_DerivesChipsWithActionKind()
    {
        var binding = BuildBinding() with
        {
            ChipTransitions = new[]
            {
                new ChipTransition { ChipLabel = "Analyze the document", TargetBindingId = "UC-A-2" },
                new ChipTransition { ChipLabel = "Open the library", TargetBindingId = null },
            },
        };

        var chips = CompletionEngine.MapNextStepChips(binding);

        chips.Should().HaveCount(2);
        chips[0].Label.Should().Be("Analyze the document");
        chips[0].ActionKind.Should().Be("invoke_capability", "a transition naming a target Binding invokes a capability");
        chips[1].Label.Should().Be("Open the library");
        chips[1].ActionKind.Should().Be("navigate", "a bare label with no target Binding is a navigation placeholder");
    }

    [Fact]
    public void MapNextStepChips_DropsTransitionsWithNoLabel_NeverRendersACaptionlessChip()
    {
        var binding = BuildBinding() with
        {
            ChipTransitions = new[]
            {
                new ChipTransition { ChipLabel = "  ", TargetBindingId = "UC-A-2" },
                new ChipTransition { ChipLabel = null, TargetBindingId = "UC-A-3" },
                new ChipTransition { ChipLabel = "Keep me", TargetBindingId = "UC-A-4" },
            },
        };

        CompletionEngine.MapNextStepChips(binding).Should().ContainSingle().Which.Label.Should().Be("Keep me");
    }

    [Fact]
    public void MapNextStepChips_LegacyBindingNoTransitions_ReturnsEmpty()
    {
        CompletionEngine.MapNextStepChips(BuildBinding()).Should().BeEmpty();
    }

    // ─── Auto-executed / event path: compose FROM the stored entry (store-before-render) ────────

    [Fact]
    public void ComposeForRoutedOutput_ReferencesStoredLedgerKey_WithChipsAndStatusSucceeded()
    {
        var entry = BuildStoredEntry("""{"summary":"The matter was summarized."}""");
        var binding = BuildBinding() with
        {
            ChipTransitions = new[] { new ChipTransition { ChipLabel = "Draft an email", TargetBindingId = "UC-B-1" } },
        };

        var card = CompletionEngine.ComposeForRoutedOutput(entry, binding);

        card.LedgerOutputKey.Should().Be(entry.Key, "the card renders the STORED ledger output (ADR-040)");
        card.Status.Should().Be(OutcomeStatus.Succeeded);
        card.Summary.UserFacing.Should().Be("The matter was summarized.");
        card.NextSteps.Should().ContainSingle().Which.Label.Should().Be("Draft an email");
        card.TraceRef.Should().Be(entry.Key, "the trace ref defaults to the ledger key");
        card.Completion!.Mode.Should().Be(OutcomeCompletionMode.SingleShot);
    }

    [Theory]
    [InlineData("email", "The message was prepared.")]
    [InlineData("work_product", "The work product was saved.")]
    [InlineData("notification", "A notification was created.")]
    public void ComposeForRoutedOutput_PayloadWithoutSummary_FallsBackToDispositionSentence(
        string disposition, string expected)
    {
        var entry = BuildStoredEntry("""{"data":{"k":"v"}}""") with { Disposition = disposition };

        CompletionEngine.ComposeForRoutedOutput(entry, BuildBinding())
            .Summary.UserFacing.Should().Be(expected);
    }

    // ─── Gated confirm-resume: audience split + server-composed link ────────────────────────────

    [Fact]
    public void ComposeForGateResume_CarriesAudienceSplitAndServerComposedLink()
    {
        var link = OutcomeCardLink.ServerComposed(
            "https://org.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=sprk_task&id=abc",
            "Open record", "record");

        var card = CompletionEngine.ComposeForGateResume(
            ledgerOutputKey: "loop@t3",
            userFacing: "Task 'Review NDA' was created.",
            internalDetail: "tell the user the task is ready",
            link: link);

        card.LedgerOutputKey.Should().Be("loop@t3");
        card.Status.Should().Be(OutcomeStatus.Succeeded);
        card.Summary.UserFacing.Should().Be("Task 'Review NDA' was created.");
        card.Summary.Internal.Should().Be("tell the user the task is ready");
        card.Render().UserSummary.Should().Be("Task 'Review NDA' was created.",
            "the user projection never leaks the internal/model-facing detail");
        card.Link!.Url.Should().Contain("pagetype=entityrecord");
        card.NextSteps.Should().BeEmpty("the reserved 'loop' resume path declares no chip transitions");
    }

    [Fact]
    public void ComposeForGateResume_BlankUserFacing_FallsBackToCompleted()
    {
        CompletionEngine.ComposeForGateResume("loop@t1", userFacing: "   ")
            .Summary.UserFacing.Should().Be("Completed.");
    }

    // ─── Job-aware: NFR-12 ingestion parity is inherited via the task-036 projection ────────────

    [Fact]
    public void ComposeJobAware_RecordDoneButIndexingPending_IsPartialNeverSucceeded()
    {
        var entry = BuildStoredEntry("""{"summary":"Document created."}""");
        var state = new JobAwareCompletionState
        {
            JobId = Guid.NewGuid(),
            JobType = "document.create",
            Steps = new[]
            {
                new JobStepCompletion { StepName = "record", State = JobAwareState.Completed },
                new JobStepCompletion { StepName = "indexing", State = JobAwareState.Running },
            },
            Aggregate = JobAwareState.Partial,
        };

        var card = CompletionEngine.ComposeJobAware(entry, BuildBinding(), state);

        card.Status.Should().Be(OutcomeStatus.Partial,
            "a bare record row whose downstream indexing has not finished is never Succeeded (NFR-12)");
        card.Completion!.Mode.Should().Be(OutcomeCompletionMode.JobAware);
        card.Completion.Steps.Should().HaveCount(2);
        card.Completion.Steps.Single(s => s.Key == "indexing").Status.Should().Be("running");
    }

    [Fact]
    public void ComposeJobAware_AllStepsCompleted_IsSucceeded()
    {
        var entry = BuildStoredEntry("""{"summary":"Done."}""");
        var state = new JobAwareCompletionState
        {
            JobId = Guid.NewGuid(),
            JobType = "document.create",
            Steps = new[] { new JobStepCompletion { StepName = "record", State = JobAwareState.Completed } },
            Aggregate = JobAwareState.Completed,
        };

        CompletionEngine.ComposeJobAware(entry, BuildBinding(), state)
            .Status.Should().Be(OutcomeStatus.Succeeded);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static Binding BuildBinding() => new()
    {
        BindingId = BindingId,
        ConsumerType = "chat-summarize",
        Ucid = "UC-A-1",
        Disposition = BindingDisposition.Informational,
    };

    private static SessionOutput BuildStoredEntry(string payloadJson) => new()
    {
        Key = SessionLedger.BuildOutputKey(BindingId.ToString(), 1),
        BindingId = BindingId.ToString(),
        UcId = "UC-A-1",
        Turn = 1,
        Disposition = "informational",
        Payload = ParseJson(payloadJson),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
