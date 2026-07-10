using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

/// <summary>
/// Vertical-slice seam tests for job-aware completion on the LIVE
/// <see cref="OutputRouter.RouteAsync"/> path (spaarke-ai-architecture-redesign-r2 task 036 live-wiring;
/// e2e-completion-audit-2026-07-10 F-3). Before this wiring, <see cref="JobAwareOutcomeProjection"/> /
/// <see cref="CompletionEngine.ComposeJobAware"/> had ZERO production callers — a job-backed side effect
/// routed through the real router always got the hardcoded <see cref="OutcomeStatus.Succeeded"/>, so the
/// NFR-12 ingestion-parity invariant ("a document-create OutcomeCard must not render Succeeded while its
/// indexing/analysis jobs are queued/running") was contract-tested but never enforced live.
/// </summary>
/// <remarks>
/// <para>
/// <b>Real path, not mocked</b>: the PRODUCTION <see cref="OutputRouter"/> and
/// <see cref="ChatSessionManager"/> (over the in-memory tenant cache with production serialization) run
/// end-to-end. The job aggregate is a REAL <see cref="JobAwareCompletionState"/> produced by the REAL
/// <see cref="JobAwareCompletionStateProjector"/> from consumer-declared stored step signals, serialized
/// into the routed payload exactly as a job-backed capability would emit it. A router-unit assertion is
/// NOT sufficient for this category (tests/CLAUDE.md seam definition, E-40) — the point is that the LIVE
/// router derives the status from the aggregate.
/// </para>
/// <para>
/// <b>KEEP rationale (maintain-class)</b>: pins the F-3 live-wiring — a job-backed routed output with an
/// incomplete aggregate is NEVER Succeeded (NFR-12), a fully-completed aggregate IS Succeeded, and a
/// non-job routed output is byte/behavior-identical to the legacy single-shot composer (regression pin).
/// Deleting any of these re-opens the exact dormant-projection gap F-3 found.
/// </para>
/// </remarks>
public sealed class JobAwareCompletionRouterSeamTests
{
    private const string TenantId = "00000000-0000-0000-0000-0000000000f3";
    private const string SessionId = "f3f3f3f3-f3f3-f3f3-f3f3-f3f3f3f3f3f3";
    private static readonly Guid BindingId = Guid.Parse("36363636-3636-3636-3636-363636363636");

    // ─────────────────────────────────────────────────────────────────────────
    // (1) INGESTION PARITY (NFR-12) on the LIVE router: a document-create whose
    //     record step completed but whose indexing step is still running renders
    //     Partial — NEVER Succeeded — through the real OutputRouter.RouteAsync.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RouteAsync_JobBackedOutput_RecordDoneIndexingRunning_YieldsPartialNeverSucceeded()
    {
        var sessions = NewSessions();
        await sessions.UpdateSessionCacheAsync(BuildSession());
        var router = new OutputRouter(sessions, Mock.Of<ILogger<OutputRouter>>());

        // REAL projector → REAL state: record Completed, indexing still Running ⇒ aggregate Partial.
        var state = ProjectState(
            CompletedSignal("record"),
            RunningSignal("indexing"));
        state.Aggregate.Should().Be(JobAwareState.Partial, "sanity: the projector computed the ingestion-parity aggregate");

        var payload = JobBackedPayload("Document created.", state);

        var routed = await router.RouteAsync(BuildSession(), BuildBinding(), payload);

        routed.Outcome.Should().NotBeNull("every completing route yields an OutcomeCard (NFR-09)");
        routed.Outcome!.Status.Should().Be(OutcomeStatus.Partial,
            "a bare record row whose downstream indexing has not finished is NEVER Succeeded on the live path (NFR-12)");
        routed.Outcome.LedgerOutputKey.Should().Be(routed.Entry.Key,
            "the card renders the STORED ledger output (store-before-render — ADR-040)");
        routed.Outcome.Completion!.Mode.Should().Be(OutcomeCompletionMode.JobAware);
        routed.Outcome.Completion.Steps.Single(s => s.Key == "indexing").Status.Should().Be("running");
        routed.Outcome.Completion.Steps.Single(s => s.Key == "record").Status.Should().Be("succeeded");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (2) A fully-completed job aggregate IS Succeeded through the same live path.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RouteAsync_JobBackedOutput_AllStepsCompleted_YieldsSucceeded()
    {
        var sessions = NewSessions();
        await sessions.UpdateSessionCacheAsync(BuildSession());
        var router = new OutputRouter(sessions, Mock.Of<ILogger<OutputRouter>>());

        var state = ProjectState(
            CompletedSignal("record"),
            CompletedSignal("indexing"));
        state.Aggregate.Should().Be(JobAwareState.Completed);

        var routed = await router.RouteAsync(
            BuildSession(), BuildBinding(), JobBackedPayload("Document created and indexed.", state));

        routed.Outcome!.Status.Should().Be(OutcomeStatus.Succeeded,
            "only a fully-completed job aggregate is Succeeded (NFR-12)");
        routed.Outcome.Completion!.Mode.Should().Be(OutcomeCompletionMode.JobAware);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (3) REGRESSION PIN: a non-job routed output is byte/behavior-identical to
    //     the legacy single-shot composer — Succeeded + SingleShot. The F-3 wiring
    //     must not perturb the (overwhelmingly common) non-job path.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RouteAsync_NonJobOutput_IsUnchangedLegacySingleShotSucceeded()
    {
        var sessions = NewSessions();
        await sessions.UpdateSessionCacheAsync(BuildSession());
        var router = new OutputRouter(sessions, Mock.Of<ILogger<OutputRouter>>());

        var payload = ParseJson("""{"summary":"The matter was summarized."}""");

        var routed = await router.RouteAsync(BuildSession(), BuildBinding(), payload);

        routed.Outcome!.Status.Should().Be(OutcomeStatus.Succeeded);
        routed.Outcome.Completion!.Mode.Should().Be(OutcomeCompletionMode.SingleShot,
            "a payload with no embedded job state is a single-shot side effect (regression pin — legacy behavior)");
        routed.Outcome.Summary.UserFacing.Should().Be("The matter was summarized.");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static ChatSessionManager NewSessions() => new(
        new InMemoryTenantCache(),
        Mock.Of<IChatDataverseRepository>(),
        Mock.Of<ILogger<ChatSessionManager>>());

    private static Binding BuildBinding() => new()
    {
        BindingId = BindingId,
        ConsumerType = "job-aware-seam-test",
        Ucid = "UC-A-1",
        Disposition = BindingDisposition.Informational,
    };

    private static ChatSession BuildSession() => new(
        SessionId: SessionId,
        TenantId: TenantId,
        DocumentId: null,
        PlaybookId: null,
        CreatedAt: DateTimeOffset.UtcNow,
        LastActivity: DateTimeOffset.UtcNow,
        Messages: Array.Empty<ChatMessage>());

    /// <summary>Projects the consumer-declared stored step signals through the REAL projector.</summary>
    private static JobAwareCompletionState ProjectState(params StoredStepSignal[] signals)
    {
        var job = new JobContract
        {
            JobType = "document.create",
            SubjectId = "drive-item-1",
            CorrelationId = "corr-1",
            IdempotencyKey = "idem-1",
        };
        return JobAwareCompletionStateProjector.Project(job, signals, DateTimeOffset.UtcNow);
    }

    private static StoredStepSignal CompletedSignal(string step) => new()
    {
        StepName = step,
        StoredStatus = JobStatus.Completed,
        Started = true,
    };

    private static StoredStepSignal RunningSignal(string step) => new()
    {
        StepName = step,
        StoredStatus = null,
        Started = true,
    };

    /// <summary>
    /// Builds a routed payload that embeds the serialized <see cref="JobAwareCompletionState"/> under the
    /// reserved <see cref="CompletionEngine.JobAwareCompletionStateField"/> — exactly the wire shape a
    /// job-backed capability would emit for the live router to detect.
    /// </summary>
    private static JsonElement JobBackedPayload(string summary, JobAwareCompletionState state)
    {
        var payload = new Dictionary<string, object?>
        {
            ["summary"] = summary,
            [CompletionEngine.JobAwareCompletionStateField] = state,
        };
        return JsonSerializer.SerializeToElement(payload);
    }

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
