# Chat ↔ Workspace Write-Side Unification (Handoff from R6)

> **Authored**: 2026-06-25
> **Source**: R6 surface-completion diagnostic identified architectural debt in chat-handler vs playbook-output workspace mutation paths
> **Owner of follow-up**: `spaarke-ai-platform-chat-routing-redesign-r1`
> **Type**: Architectural debt — scoping handoff (NOT a request to implement immediately)
> **R6 origin doc**: `projects/spaarke-ai-platform-unification-r6/notes/tier-c-diagnostic.md`

---

## Executive summary

The SpaarkeAi shell has **two parallel code paths that mutate workspace tabs**, sharing the same backing store (`IWorkspaceStateService`) but diverging in how they notify the frontend. The chat-handler path writes state and relies on the frontend to reconcile (poll + per-turn snapshot). The playbook-output path writes state AND pushes an SSE event so the frontend mounts widgets immediately.

The chat-handler path **cites ADR-030 as justification for not emitting SSE events**. After re-reading ADR-030, this citation is **misleading** — ADR-030 forbids adding a 6th channel but explicitly allows additive event types on existing channels. The chat path's "no push" design is a choice, not an ADR requirement.

**Why this is the successor project's scope**: R6's Pillar 6 shipped the divergence; the chat-routing-redesign-r1 project is already restructuring chat dispatch (Phase 5R), already touched workspace handlers (Phase 5R task 118b added `GetWorkspaceTabContentHandler` — read-side unification), and has the architectural context to unify the write-side cleanly. R6's remaining surface-completion sprint is bounded user-facing fixes (missing Dataverse rows, missing UI mounts) and doesn't have the bandwidth to redesign the mutation pipeline.

**Effort estimate**: ~1-2 working days. **Risk**: low — backward-compatible, additive, no ADR violation.

**Action**: Add to your project plan as a new WP, a WP3 extension, or an R7-backlog item depending on scoping outcome. Investigate first (notes), decide scope, then schedule.

---

## Background — how we got here

R6 (`spaarke-ai-platform-unification-r6`) was a 9-pillar architecture convergence project. Pillar 6 specifically was about **tri-directional workspace state** — meaning the Workspace pane state should be writable by:
- The user (manual interaction with widgets)
- The LLM during chat (Path A — chat tool handlers)
- A playbook during execution (Path B — output destination=workspace)

Pillar 6a built the state-layer (`IWorkspaceStateService` with Redis hot + Cosmos durable persistence) + `GET /api/workspace/state` endpoint.

Pillar 6b built the chat tool handlers + UI affordances.

Pillar 5 (Q5 re-shape) put the destination + widgetType routing config on playbook nodes so playbook outputs could land in workspace.

Each sub-pillar shipped working code, but **the integration was never fully designed across the two paths**. The chat-handler implementation chose state-write+poll; the playbook-output implementation chose state-write+SSE-push. They worked. But they're not the same pattern, and R6 closed without ever explicitly aligning them.

The R6 deliverables audit (2026-06-21) identified UI-surface gaps but did NOT flag this architectural divergence — it stayed below the audit's resolution. R6 UAT on 2026-06-25 exposed the chat-handler missing-rows issue (TIER-C), which led to deep investigation of the chat path, which surfaced this divergence.

---

## Current state — the two paths in detail

### PATH A — LLM-initiated (chat tool call)

**Trigger**: The LLM during a chat session decides to put something in the workspace.

**Tools**: 4 typed handlers, all registered as `sprk_analysistool` rows with `sprk_availableincontexts = Chat`:

| Tool name | Handler class | What it does |
|---|---|---|
| `send_workspace_artifact` | `SendWorkspaceArtifactHandler` | Create a new workspace tab with widget content |
| `update_workspace_tab` | `UpdateWorkspaceTabHandler` | Mutate existing tab's widget data (Q8 USER WINS on conflict) |
| `close_workspace_tab` | `CloseWorkspaceTabHandler` | Remove tab from workspace |
| `get_workspace_tab_content` | `GetWorkspaceTabContentHandler` | Read tab content (added by Phase 5R task 118b — read-side unification) |

**Flow**:
```
LLM calls tool → Handler.ExecuteForChatAsync()
                    │
                    ├─ Validate inputs
                    ├─ Call IWorkspaceStateService.UpsertTabAsync / CloseTabAsync
                    └─ Return ToolResult to LLM (success summary)
                    
[NO SSE EMISSION]

Frontend reconciles via:
  - GET /api/workspace/state polling
  - Per-turn workspace snapshot block in next system prompt
    (SprkChatAgentFactory.cs:346-367, capped at 2000 chars rich)
```

**Source files**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/SendWorkspaceArtifactHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/UpdateWorkspaceTabHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/CloseWorkspaceTabHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GetWorkspaceTabContentHandler.cs`

**Cited justification** (from `UpdateWorkspaceTabHandler.cs:91`):
```csharp
/// <item><strong>ADR-030</strong>: no new SSE / PaneEventBus channel introduced. The
/// client-side `workspace.tab_edited` PaneEventBus event...
```

The handler comments explicitly call out that no SSE event is emitted, citing ADR-030 compliance.

### PATH B — Playbook-initiated (output destination=workspace)

**Trigger**: A playbook node completes with `destination=workspace` in its routing config.

**Handler**: `PlaybookOutputHandler.HandleWorkspaceOutputAsync` (called via the NodeDestination switch at `PlaybookOutputHandler.cs:149-152`).

**Flow**:
```
Playbook node completes → PlaybookOutputHandler.HandleOutputAsync
                            │
                            └─ Detects NodeDestination.Workspace
                                  │
                                  ▼
                            HandleWorkspaceOutputAsync
                                  │
                                  ▼
                            EmitWorkspaceTabOpenAndStreamAsync
                                  │
                                  ├─ Emits workspace.tab_open SSE event
                                  │   { tabId, widgetType, ... }
                                  └─ Streaming continues via PlaybookExecutionEngine
                                  
Frontend reacts:
  - Receives workspace.tab_open event
  - Mounts widget IMMEDIATELY (e.g., StructuredOutputStreamWidget)
  - Continues consuming per-field FieldDelta streaming
```

**Source files**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookOutputHandler.cs:581+`
- `EmitWorkspaceTabOpenAndStreamAsync` (private helper used by both Workspace and Both destination branches)

**Why this path emits SSE**: Streaming UX requires the widget to mount BEFORE field-deltas start arriving. Without immediate mount, the frontend has nowhere to render incoming stream chunks. This forced the SSE push.

### Comparison table

| Concern | Chat handler path | Playbook output path |
|---|---|---|
| Trigger | LLM tool call | Playbook node completion |
| Validation | Per-handler (`ToolHandlerMetadata` schema) | Per-node (engine validation) |
| State write | `IWorkspaceStateService.UpsertTabAsync` | `IWorkspaceStateService` via streaming pipeline |
| Frontend notification | None — relies on poll or next-turn snapshot | `workspace.tab_open` SSE event |
| Response shape returned to LLM | ToolResult with tab-created summary | N/A (playbook output flow) |
| ADR-033 SSE writer delegate available? | NO (chat tool execution context lacks it) | YES (provided to PlaybookOutputHandler) |
| Backward compat scope | R6 Pillar 6b | R6 Pillar 5 + 6a + Phase 5R |

---

## ADR-030 deep dive — what it actually says

The handler comments cite ADR-030 as if it prohibits SSE emission from chat handlers. **The actual ADR text says no such thing.**

Source: `.claude/adr/ADR-030-pane-event-bus.md` (v2 amendment 2026-06-21).

### ADR-030 actual constraints

**Channels** (the closed union):
- Exactly 5 members: `workspace`, `context`, `conversation`, `safety`, `memory`
- Adding a 6th channel requires a successor ADR
- The 5th (`memory`) was added by this project's v2 amendment

**Event types** (the additive surface):
- Each channel has a discriminated-union event type
- New event types can be added to existing channels freely
- Existing subscribers must continue to compile and run after additions (i.e., they handle the events they care about and ignore others — TypeScript narrowing)

**MUST constraints** (verbatim from ADR-030):
- Keep all event payloads typed via `PaneChannelEventMap` (no `any`)
- Use 5-channel closed union; no 6th without successor ADR
- Dispatch via `useDispatchPaneEvent()` from React
- Subscribe via `usePaneEvent(channel, handler)` from React
- Add new event types as **additive** discriminants

**MUST NOT constraints** (verbatim):
- Use `any` in event payloads (use `unknown` with narrowing if polymorphic)
- Add a 6th channel without successor ADR
- Instantiate `new PaneEventBus()` in component code

### What ADR-030 does NOT constrain

- **Whether existing channels carry events emitted from BFF state mutations**
- **Whether chat tool handlers emit events**
- **Whether the workspace channel has a "tab opened by agent" event type**

The workspace channel already carries event types:
- `widget_load`
- `widget_update`
- `widget_action`
- `tab_change`
- `tab_count_change`
- `selection_changed`
- `tabs_clear`
- `wizard_step`
- `entity_resolved`
- `session_reset`
- `active_widget_changed`

Adding a `tab_opened_by_agent` or extending the existing `workspace.tab_open` event already emitted by the playbook output handler to fire from chat handlers would be **fully ADR-030-compliant** as additive event types on an existing channel.

### Conclusion

The chat handler comments cite ADR-030 as post-hoc justification for an implementation choice the ADR doesn't actually require. The architectural divergence between Path A and Path B is implementation-driven (likely effort/simplicity), not constraint-driven.

---

## User-visible impact

### What works today
- LLM can write to workspace via chat tools (when the rows are deployed — R6 is fixing this in surface completion)
- Playbook outputs render in workspace tabs immediately (push UX, working as designed)
- Workspace state is consistent (both paths write to the same `IWorkspaceStateService`)

### What's degraded
When the user has a conversation and the LLM says "I've added that to your workspace":
- **Today**: User waits for refetch tick (or next system-prompt update) to see the tab appear. Perceived latency: ~500ms to ~2s depending on frontend reconciliation cadence.
- **After unification**: Tab appears immediately, matching the playbook output UX.

### Why this matters
- Inconsistent UX between two semantically identical operations ("LLM produces output → goes to workspace")
- User mental-model fragmentation — why does `/summarize` show a tab immediately but "summarize and put it in the workspace" via chat show a delay?
- Potential ChatGPT-like UX expectations — modern AI chat surfaces show "tool result" updates in real time

### Severity
Not blocking (workspace works; it's just slower in one path). But it's the kind of polish gap that erodes "this feels like a real AI product" perception.

---

## Proposed unification (suggestive, not prescriptive)

This is one shape the unification could take. Your team should design what makes architectural sense given Phase 5R/7 dispatcher state.

### Shared workspace mutation pipeline

```
┌─────────────────────────────────────────────────────────────┐
│  IWorkspaceMutationPipeline (new shared service)            │
│                                                              │
│  Task<WorkspaceMutationResult> MutateAsync(                 │
│      WorkspaceMutationRequest request,                      │
│      Func<ChatSseEvent, CancellationToken, Task>?           │
│        emitSseEvent,                                        │
│      CancellationToken cancellationToken)                   │
│                                                              │
│  1. Validate request (kind, tab id, widget data, etc.)      │
│  2. Resolve tenant/session scope                            │
│  3. Write via IWorkspaceStateService                        │
│  4. If emitSseEvent is non-null: emit workspace.<event>     │
│  5. Return result                                           │
└─────────────────────────────────────────────────────────────┘
```

### Chat tool handlers become thin wrappers

```csharp
public sealed class SendWorkspaceArtifactHandler : IToolHandler
{
    private readonly IWorkspaceMutationPipeline _pipeline;

    public async Task<ToolResult> ExecuteForChatAsync(
        ChatInvocationContext context,
        // ...
        CancellationToken cancellationToken)
    {
        var result = await _pipeline.MutateAsync(
            request: new WorkspaceMutationRequest { Kind = Create, /* ... */ },
            emitSseEvent: context.WorkspaceSseWriter, // NEW field
            cancellationToken: cancellationToken);

        return ToolResult.Success($"Tab {result.TabId} created");
    }
}
```

### Playbook output handler becomes thin wrapper

```csharp
public class PlaybookOutputHandler
{
    private readonly IWorkspaceMutationPipeline _pipeline;

    private async Task<bool> HandleWorkspaceOutputAsync(
        DispatchResult dispatch,
        Func<ChatSseEvent, CancellationToken, Task> emitSseEvent,
        CancellationToken cancellationToken)
    {
        await _pipeline.MutateAsync(
            request: ConvertDispatchToMutationRequest(dispatch),
            emitSseEvent: emitSseEvent,
            cancellationToken: cancellationToken);

        return true;
    }
}
```

### ChatInvocationContext extension

```csharp
public sealed class ChatInvocationContext : IToolExecutionContext
{
    // ... existing fields ...
    
    /// <summary>
    /// SSE writer delegate provided by the chat endpoint. Optional —
    /// not all chat tool execution paths have an SSE channel (e.g., job-handler
    /// post-processing tools execute outside an HTTP request).
    /// </summary>
    public Func<ChatSseEvent, CancellationToken, Task>? WorkspaceSseWriter { get; init; }
}
```

The `ChatEndpoints` chat-message handler constructs `ChatInvocationContext` with the SSE writer; job-handler-side construction sets it to null. Pipeline tolerates either via the nullable parameter.

### Event payload (ADR-030 additive)

Workspace channel gains a new event type or extends `tab_open`:

```typescript
// In Spaarke.UI.Components workspace channel union
| { type: 'tab_opened_by_agent'; tabId: string; widgetType: string; source: 'chat-tool' | 'playbook' }
| { type: 'tab_updated_by_agent'; tabId: string; widgetType: string; source: 'chat-tool' | 'playbook' }
| { type: 'tab_closed_by_agent'; tabId: string; source: 'chat-tool' | 'playbook' }
```

(Or extend the existing `tab_open` event with a `source` field, depending on what makes the frontend subscriber code simpler.)

---

## Implementation considerations

### Backward compatibility
- Existing handlers' tests (32 + workspace state continuity tests) provide regression coverage
- Existing frontend subscribers continue to work — additive ADR-030 event types per the contract
- Frontend `GET /api/workspace/state` reconciliation continues to work as the secondary mechanism (idempotent)

### Threading + DI
- `IWorkspaceMutationPipeline` is scoped (matches `IWorkspaceStateService` scope)
- SSE writer delegates are captured at request start by `ChatEndpoints` — same pattern as ADR-033 streaming side-channel
- No new top-level DI registration (`ADR-010` compliant) — pipeline registers within existing `AnalysisServicesModule` or `ToolFrameworkExtensions`

### Telemetry (ADR-015 binding)
- Emit `context.tool_call_started` + `context.tool_call_completed` from the pipeline once (currently emitted from handlers individually — consolidates)
- ADR-015 tier-1 safety: deterministic IDs only, no widget data or user content
- Optional new telemetry: emission latency (state-write to SSE-event), measure UX improvement

### Test surface
- New unit tests on `IWorkspaceMutationPipeline` (validation, write, emit, return shape)
- Existing handler tests retargeted to verify pipeline call + return contract
- New integration test: chat tool → pipeline → IWorkspaceStateService + SSE emission (mock writer)
- Backward compat test: chat tool without SSE writer still writes state (null tolerance)

---

## R6 ↔ successor coordination

### What R6 expects from successor
- Investigate the gap (this doc + the source files referenced)
- Decide whether to unify now or document as known limitation
- If unifying: design + implement; flip `AvailableInContexts` on the 4 workspace handler `sprk_analysistool` rows if your design requires it (R6's `scripts/Seed-TypedHandlers.ps1` is the single source for this)

### What successor expects from R6
- R6 continues treating workspace handlers as `AvailableInContexts = Chat` only
- R6 will NOT modify `SendWorkspaceArtifactHandler` / `UpdateWorkspaceTabHandler` / `CloseWorkspaceTabHandler` source code during R6 surface completion sprint or closeout
- R6 will land the missing Dataverse rows (TIER-C primary fix — independent of this unification)

### Decision boundary
- If you unify the write-side: handler source moves into your project; R6's `sprk_analysistool` row schemas stay unchanged (handlers still exposed by `sprk_handlerclass = "SendWorkspaceArtifactHandler"` etc.)
- If you defer: this doc serves as R7+ backlog entry; R6 surface completion proceeds independently; user lives with the inconsistent UX

### Conflict surface (low)
- The 4 handler source files (R6 will NOT touch during sprint)
- `PlaybookOutputHandler.cs` (R6 will NOT touch)
- New shared pipeline service (greenfield, no conflict)
- `ChatInvocationContext` extension (single field add — backward compat)
- ADR-030 not modified (additive event types only)

---

## Mapping to existing successor WPs

This work plausibly fits in your existing project structure as:

### Option 1 — WP3 extension
Your WP3 already wired `NodeRoutingConfig` into `DispatchResult` structurally. Adding the shared workspace mutation pipeline could be framed as the next layer of that wiring — making the destination routing terminal-actor symmetric across chat-tool-initiated and playbook-initiated paths.

### Option 2 — New WP7
If WP4 (CapabilityRouter retirement) is your last WP and the project is in wrap-up: scope this as WP7 "Chat ↔ Workspace write-side unification" as a clean architectural finish.

### Option 3 — R7+ backlog
If your project is genuinely in closeout (Phase 7 wrap-up + task 146 UAT + task 150) and you don't want to expand scope: document this as an R7 backlog item with the analysis above. Successor of successor or a focused mini-project picks it up.

### Recommendation
**Option 1 or 2 if you have ≤1 week of bandwidth left; Option 3 if not.** The pipeline pattern is small enough (~1-2 days) that it could land before your project wraps; conversely, the unification debt is not severe enough to block your closeout if you don't have bandwidth.

---

## Effort + risk

### Effort
- **Investigation + design note**: 4 hr (~half day)
- **Implementation**: 1-2 days (pipeline service + handler refactors + tests + ChatInvocationContext extension)
- **Total**: 2-3 working days

### Risk
- **Backward compatibility**: Low — additive ADR-030 events, existing reconciliation continues to work, handlers' return shape unchanged
- **Performance**: Low — pipeline adds one layer of indirection but writes are still single Redis/Cosmos ops
- **Test surface**: Moderate — need to keep existing 32 handler tests passing while adding pipeline tests
- **Coordination with R6**: Very low — clear ownership boundary, no overlapping file edits during R6 surface sprint

### Trade-off
- **If unified**: cleaner architecture, consistent UX, ADR-030-aligned, easier R7+ extension (e.g., adding workspace.context.*-source events for telemetry)
- **If not unified**: chat path latency stays; UX gap remains; debt carries to R7+

---

## References

### Source files referenced
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/SendWorkspaceArtifactHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/UpdateWorkspaceTabHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/CloseWorkspaceTabHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GetWorkspaceTabContentHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookOutputHandler.cs:149-188` (NodeDestination switch)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookOutputHandler.cs:581+` (`HandleWorkspaceOutputAsync`)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs:346-367` (workspace state snapshot in system prompt — secondary reconciliation)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs:1827-1900+` (`BuildWorkspaceStateBlock` — what the LLM sees)

### Architecture references
- `.claude/adr/ADR-030-pane-event-bus.md` (v2 amendment 2026-06-21) — the actual constraint surface
- `.claude/adr/ADR-033-streaming-chat-tool-side-channel.md` (if present) — existing SSE writer delegate pattern for playbook path
- R6 deliverables audit: `projects/spaarke-ai-platform-unification-r6/r6-deliverables-audit.md`
- R6 TIER-C diagnostic that surfaced this: `projects/spaarke-ai-platform-unification-r6/notes/tier-c-diagnostic.md`

### Related successor work
- Phase 5R task 118b: `GetWorkspaceTabContentHandler` (read-side unification — relevant precedent)
- WP3: `NodeRoutingConfig` non-optional wiring into `DispatchResult` (structural neighbor)
- WP4: CapabilityRouter retirement (chat-routing structural rework — same neighborhood)

### R6 coordination doc
- This doc is the R6 → successor handoff
- R6 will reference this in its r7-backlog seed (task 090) regardless of whether successor takes the work

---

## Action requested

1. **Investigate** — read this doc + the source files referenced; verify the divergence; confirm ADR-030's actual text
2. **Decide scope** — Option 1 / 2 / 3 above
3. **If taking** — design note in your project's notes/ folder + add to TASK-INDEX as a new wave or task; coordinate with R6 owner so handler `AvailableInContexts` flips are timed correctly
4. **If deferring** — file in your r7-backlog.md with severity rating

R6 will not proceed with workspace handler source changes during its surface completion sprint or closeout regardless of your decision.

---

*End of handoff. Questions or context-clarification requests welcome — escalate via project owner.*
