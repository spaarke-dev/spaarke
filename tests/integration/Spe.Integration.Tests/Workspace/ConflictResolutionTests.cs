// R6 task 058 / D-C-11 — Q8 conflict resolution end-to-end integration test.
//
// Validates the full stale-read → refuse → re-read → re-attempt → succeed cycle
// against the real UpdateWorkspaceTabHandler, using a fake IWorkspaceStateService
// to model the persistence tier. Also asserts the workspace.conflict_refused
// counter increments on refusal per ADR-015 (deterministic IDs only).
//
// Why this lives in Spe.Integration.Tests (not unit tests):
//   - Exercises the handler END-TO-END through the public IToolHandler surface
//     (ValidateChat → ExecuteChatAsync) and the full result-shape contract that
//     the chat agent observes (ToolResult.Data with status discriminator).
//   - Asserts cross-cutting telemetry (MeterListener) — a unit test could mock
//     the counter but the integration test confirms the static meter pipeline
//     wires through to the OTel collection surface.
//   - Asserts the persona-side instruction snippet is present so the LLM has
//     the contract for re-read behavior — this is the data half of the Q8
//     end-to-end wiring (other half is the handler refusal payload).
//
// Per POML 058 outputs:
//   1. SYS- persona snippet (validated here via the script-side text contract).
//   2. workspace.conflict_refused counter (validated here via MeterListener).
//   3. End-to-end stale-read → re-attempt success.

using System.Diagnostics.Metrics;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Spe.Integration.Tests.Workspace;

[Trait("Category", "Integration")]
[Trait("Feature", "WorkspaceConflictResolution")]
public sealed class ConflictResolutionTests
{
    private static readonly DateTimeOffset DeterministicNow = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------------
    // End-to-end: stale → refuse → re-read → re-attempt → succeed.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task StaleReadFollowedByFreshReread_SucceedsOnSecondAttempt()
    {
        // Arrange — user-edit happened at 13:00; agent's stale view says 11:00.
        const string tabId = "tab-1";
        const string storedUserEditAt = "2026-06-10T13:00:00Z";
        const string agentStaleExpected = "2026-06-10T11:00:00Z";
        const string tenantId = "tenant-1";
        var sessionGuid = Guid.NewGuid();
        var sessionId = sessionGuid.ToString("N");

        var fakeState = new FakeWorkspaceStateService(
            initialTab: BuildTab(tabId, tenantId, sessionId, lastUserEditAt: storedUserEditAt));

        var handler = new UpdateWorkspaceTabHandler(
            workspaceStateService: fakeState,
            timeProvider: new FakeTimeProvider(DeterministicNow),
            logger: NullLogger<UpdateWorkspaceTabHandler>.Instance);

        var tool = BuildUpdateTool();

        // ── Step 1: agent attempts update with STALE expected timestamp.
        var staleCtx = BuildChatCtx(
            tenantId: tenantId,
            sessionGuid: sessionGuid,
            argsJson: BuildArgsJson(tabId, expectedLastUserEditAt: agentStaleExpected));

        // Subscribe a MeterListener for the conflict counter BEFORE the call so
        // we capture the emission (the counter is a static field; MeterListener
        // must be running when Add() is invoked).
        long refusedCount = 0;
        string? capturedTabIdTag = null;
        string? capturedDecisionTag = null;
        string? capturedTenantTag = null;
        string? capturedSessionTag = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == UpdateWorkspaceTabHandler.MeterName &&
                instrument.Name == "workspace.conflict_refused")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            refusedCount += value;
            foreach (var tag in tags)
            {
                switch (tag.Key)
                {
                    case "tabId": capturedTabIdTag = tag.Value as string; break;
                    case "decision": capturedDecisionTag = tag.Value as string; break;
                    case "tenantId": capturedTenantTag = tag.Value as string; break;
                    case "sessionId": capturedSessionTag = tag.Value as string; break;
                }
            }
        });
        listener.Start();

        var staleResult = await handler.ExecuteChatAsync(staleCtx, tool, CancellationToken.None);

        // ── Step 2: refusal is a success-with-status (Q8 binding: re-actable response).
        staleResult.Success.Should().BeTrue(
            because: "Q8 stale-read refusal is a re-actable response, not an error");
        var stalePayload = staleResult.GetData<UpdateWorkspaceTabHandler.UpdateWorkspaceTabPayload>();
        stalePayload.Should().NotBeNull();
        stalePayload!.Status.Should().Be(UpdateWorkspaceTabHandler.StatusStaleRead);
        stalePayload.CurrentLastUserEditAt.Should().Be(storedUserEditAt,
            because: "the agent must see the current value so its next-turn read is fresh");

        // ── Step 3: telemetry counter emitted exactly once with deterministic-ID tags.
        refusedCount.Should().Be(1,
            because: "every stale-read refusal must increment workspace.conflict_refused per task 058");
        capturedDecisionTag.Should().Be(UpdateWorkspaceTabHandler.StatusStaleRead);
        capturedTabIdTag.Should().Be(tabId);
        capturedTenantTag.Should().Be(tenantId);
        capturedSessionTag.Should().Be(sessionId);

        // ── Step 4: persistence was NOT called (USER WINS — no mutation on refusal).
        fakeState.UpsertCallCount.Should().Be(0,
            because: "no mutation occurs on stale-read refusal (Q8 binding)");

        // ── Step 5: agent re-reads the tab (simulates the next-turn workspace snapshot
        // assembly in SprkChatAgentFactory) and re-attempts with the FRESH timestamp it
        // observed from the refusal payload's currentLastUserEditAt. Per the persona
        // snippet wired by Seed-AiPersonaDefault.ps1 (this task), the LLM is instructed
        // to do exactly this on stale_read.
        var freshExpected = stalePayload.CurrentLastUserEditAt;
        var retryCtx = BuildChatCtx(
            tenantId: tenantId,
            sessionGuid: sessionGuid,
            argsJson: BuildArgsJson(tabId, expectedLastUserEditAt: freshExpected));

        var retryResult = await handler.ExecuteChatAsync(retryCtx, tool, CancellationToken.None);

        // ── Step 6: retry succeeds and persistence is invoked.
        retryResult.Success.Should().BeTrue();
        var retryPayload = retryResult.GetData<UpdateWorkspaceTabHandler.UpdateWorkspaceTabPayload>();
        retryPayload!.Status.Should().Be(UpdateWorkspaceTabHandler.StatusApplied);
        fakeState.UpsertCallCount.Should().Be(1,
            because: "the fresh-read retry must succeed and persist exactly once");
        // Counter is not re-incremented on the success path.
        refusedCount.Should().Be(1,
            because: "applied path must NOT increment workspace.conflict_refused");
    }

    // ---------------------------------------------------------------------
    // ADR-015 audit: the dispatched counter dimensions carry deterministic
    // IDs only — no widget body / user message text / tab content.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ConflictCounter_OmitsUserContent_PerAdr015()
    {
        var tenantId = "tenant-iso";
        var sessionGuid = Guid.NewGuid();
        var sessionId = sessionGuid.ToString("N");
        var tab = BuildTab("tab-1", tenantId, sessionId, lastUserEditAt: "2026-06-10T13:00:00Z");
        // Real-world widget body contains user-authored text. Per ADR-015 binding
        // this string MUST NOT appear in any telemetry tag. Construct a fresh tab
        // copy with the sensitive body (WorkspaceTab is a sealed class, not a record).
        tab = new WorkspaceTab
        {
            Id = tab.Id,
            WidgetType = tab.WidgetType,
            WidgetData = new SummaryTabWidgetData
            {
                Body = "PRIVILEGED LEGAL DRAFT: do NOT share with adverse counsel"
            },
            SessionId = tab.SessionId,
            TenantId = tab.TenantId,
            VisibleToAssistant = tab.VisibleToAssistant,
            SourceProvenance = tab.SourceProvenance,
            MatterContext = tab.MatterContext,
            IsPinned = tab.IsPinned,
            CanEdit = tab.CanEdit,
            LastUserEditAt = tab.LastUserEditAt,
            CreatedAt = tab.CreatedAt,
            UpdatedAt = tab.UpdatedAt
        };

        var fakeState = new FakeWorkspaceStateService(initialTab: tab);
        var handler = new UpdateWorkspaceTabHandler(
            fakeState,
            new FakeTimeProvider(DeterministicNow),
            NullLogger<UpdateWorkspaceTabHandler>.Instance);

        var ctx = BuildChatCtx(
            tenantId: tenantId,
            sessionGuid: sessionGuid,
            argsJson: BuildArgsJson("tab-1", expectedLastUserEditAt: "2026-06-10T11:00:00Z"));

        var capturedTagKeys = new HashSet<string>();
        var capturedTagValues = new List<object?>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == UpdateWorkspaceTabHandler.MeterName &&
                instrument.Name == "workspace.conflict_refused")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                capturedTagKeys.Add(tag.Key);
                capturedTagValues.Add(tag.Value);
            }
        });
        listener.Start();

        await handler.ExecuteChatAsync(ctx, BuildUpdateTool(), CancellationToken.None);

        // Tag KEYS are restricted to the deterministic-ID set defined at the
        // counter emission site.
        capturedTagKeys.Should().BeEquivalentTo(new[]
        {
            "tenantId", "sessionId", "tabId", "decision"
        });

        // Tag VALUES never carry the widget body content. This is the ADR-015
        // contract verified empirically.
        foreach (var value in capturedTagValues)
        {
            var s = value?.ToString() ?? string.Empty;
            s.Should().NotContain("PRIVILEGED",
                because: "ADR-015 binding: telemetry tags carry deterministic IDs only");
            s.Should().NotContain("adverse counsel",
                because: "ADR-015 binding: no user-authored text in telemetry");
        }
    }

    // ---------------------------------------------------------------------
    // Persona snippet contract: Seed-AiPersonaDefault.ps1 carries the
    // stale_read re-read instruction. This is the DATA half of the Q8 wiring.
    // ---------------------------------------------------------------------

    [Fact]
    public void SeedAiPersonaDefaultScript_CarriesStaleReadInstruction()
    {
        // The persona-side instruction snippet is shipped as data via
        // scripts/Seed-AiPersonaDefault.ps1. This test asserts the script
        // file contains the canonical instruction text — drift here would
        // mean the deployed SYS-DEFAULT persona is missing the contract the
        // LLM needs to re-read on stale_read.
        //
        // Locating the script via repo-root crawl: walk up from the test
        // assembly directory until we find a `scripts/` sibling. Stops the
        // path from baking in repository layout assumptions.
        var assemblyDir = Path.GetDirectoryName(typeof(ConflictResolutionTests).Assembly.Location)!;
        var current = new DirectoryInfo(assemblyDir);
        DirectoryInfo? repoRoot = null;
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "scripts", "Seed-AiPersonaDefault.ps1");
            if (File.Exists(candidate))
            {
                repoRoot = current;
                break;
            }
            current = current.Parent;
        }

        repoRoot.Should().NotBeNull(
            because: "Seed-AiPersonaDefault.ps1 must be reachable from the test binary's directory chain");

        var scriptPath = Path.Combine(repoRoot!.FullName, "scripts", "Seed-AiPersonaDefault.ps1");
        var scriptText = File.ReadAllText(scriptPath);

        scriptText.Should().Contain("Workspace Tab Conflict Resolution",
            because: "the SYS-DEFAULT persona must include the conflict-resolution heading per task 058");
        scriptText.Should().Contain("stale_read",
            because: "the persona must instruct the LLM to recognize the stale_read status");
        // Case-insensitive search — the snippet uses "Re-read" (sentence start)
        // while a future re-author might lowercase. Either form satisfies the
        // contract that the LLM is instructed to re-read on stale_read.
        scriptText.ToLowerInvariant().Should().Contain("re-read the tab",
            because: "the persona must instruct the LLM to re-read on stale_read (USER WINS)");
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static AnalysisTool BuildUpdateTool() => new()
    {
        Id = Guid.NewGuid(),
        Name = "update_workspace_tab",
        Description = "Update a workspace tab",
        Type = ToolType.Custom,
        HandlerClass = nameof(UpdateWorkspaceTabHandler)
    };

    private static ChatInvocationContext BuildChatCtx(
        string tenantId,
        Guid sessionGuid,
        string argsJson) => new()
        {
            TenantId = tenantId,
            ChatSessionId = sessionGuid,
            DecisionId = Guid.NewGuid(),
            ToolArgumentsJson = argsJson,
            MatterId = null
        };

    private static WorkspaceTab BuildTab(
        string tabId,
        string tenantId,
        string sessionId,
        string? lastUserEditAt) => new()
        {
            Id = tabId,
            WidgetType = "Summary",
            WidgetData = new SummaryTabWidgetData { Body = "original body" },
            SessionId = sessionId,
            TenantId = tenantId,
            VisibleToAssistant = true,
            SourceProvenance = new WorkspaceTabSourceProvenance
            {
                Source = "agent",
                CreatedBy = "agent:test",
                CreatedAt = "2026-06-01T00:00:00Z"
            },
            MatterContext = new WorkspaceTabMatterContext
            {
                MatterId = Guid.Empty.ToString("D"),
                MatterName = "Unattached"
            },
            IsPinned = false,
            CanEdit = true,
            LastUserEditAt = lastUserEditAt,
            CreatedAt = "2026-06-01T00:00:00Z",
            UpdatedAt = "2026-06-01T00:00:00Z"
        };

    private static string BuildArgsJson(string tabId, string? expectedLastUserEditAt = null)
    {
        var expectedFragment = expectedLastUserEditAt is null
            ? ""
            : $",\"expectedLastUserEditAt\":\"{expectedLastUserEditAt}\"";
        return $$"""
                 {
                   "tabId": "{{tabId}}",
                   "widgetData": { "kind": "Summary", "body": "agent-proposed body" }
                   {{expectedFragment}}
                 }
                 """;
    }

    // Stateful fake — first GetTabsAsync returns the seeded tab; UpsertTabAsync
    // captures the mutation so the second GetTabsAsync (if any) reflects it.
    private sealed class FakeWorkspaceStateService : IWorkspaceStateService
    {
        private WorkspaceTab _currentTab;
        public int UpsertCallCount { get; private set; }

        public FakeWorkspaceStateService(WorkspaceTab initialTab)
        {
            _currentTab = initialTab;
        }

        public Task<IReadOnlyList<WorkspaceTab>> GetTabsAsync(string tenantId, string sessionId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<WorkspaceTab>>(new[] { _currentTab });

        public Task UpsertTabAsync(string tenantId, string sessionId, WorkspaceTab tab, CancellationToken cancellationToken)
        {
            UpsertCallCount++;
            _currentTab = tab;
            return Task.CompletedTask;
        }

        public Task CloseTabAsync(string tenantId, string sessionId, string tabId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task PinTabAsync(string tenantId, string sessionId, string tabId, string matterId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
