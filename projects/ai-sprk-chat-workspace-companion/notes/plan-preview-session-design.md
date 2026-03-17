# Plan Preview Session State Design

> **Task**: 070 — Investigation output
> **Author**: AI (Claude Sonnet 4.6)
> **Date**: 2026-03-16
> **Status**: Final — unblocks tasks 071, 072, 073

---

## 1. Existing Session Architecture (Findings)

### How ChatSession is stored today

`ChatSessionManager.cs` uses a **dual-store strategy**:

| Store | Purpose | TTL |
|-------|---------|-----|
| Redis (`IDistributedCache`) | Hot path — fast per-request lookup | 24h sliding |
| Dataverse (`sprk_aichatsummary`) | Cold path — audit trail, session recovery | Permanent |

Cache key pattern (ADR-014): `chat:session:{tenantId}:{sessionId}`

`ChatSession` is a C# `record` with the following fields:
- `SessionId` — `Guid.NewGuid().ToString("N")` at creation (32-char hex string)
- `TenantId` — from JWT `tid` claim (multi-tenant isolation)
- `DocumentId` — SPE document ID (nullable)
- `PlaybookId` — Dataverse GUID of the governing playbook (nullable)
- `CreatedAt`, `LastActivity` — UTC timestamps
- `Messages` — `IReadOnlyList<ChatMessage>` (in-memory + Redis hot copy)
- `HostContext` — `ChatHostContext?` (entity type/ID/workspace)
- `AdditionalDocumentIds` — `IReadOnlyList<string>?` (up to 5 pinned docs)

`ChatSession` is serialized to JSON and stored in Redis as a byte array. The record uses `with` expressions to produce updated copies, which are written back via `UpdateSessionCacheAsync`.

### How the sessionId flows

1. **Frontend creates session**: `POST /api/ai/chat/sessions` → BFF returns `{ sessionId, createdAt }`.
2. **Frontend stores `sessionId`** in `useChatSession` hook state.
3. **Every subsequent call** uses `sessionId` as a URL path segment:
   - `POST /api/ai/chat/sessions/{sessionId}/messages`
   - `POST /api/ai/chat/sessions/{sessionId}/refine`
   - `PATCH /api/ai/chat/sessions/{sessionId}/context`
4. **TenantId** is always extracted from the JWT `tid` claim (or `X-Tenant-Id` header), not from the URL.
5. Both `tenantId` and `sessionId` together form the Redis cache key.

---

## 2. Design Question Answers

### Q1: Where is the plan preview state stored?

**Recommended: Redis with a separate `plan:pending:{tenantId}:{sessionId}` key**

Rationale (evaluated three options):

| Option | Pros | Cons | Verdict |
|--------|------|------|---------|
| **(a) Redis separate key** | No ChatSession schema change; follows ADR-009 pattern exactly; TTL isolates pending plans from session lifecycle | Slightly more Redis key overhead | **CHOSEN** |
| **(b) Add `PendingPlan` field to `ChatSession` record** | Co-located with session; single Redis write | Serializes plan into every session cache read (plan payload can be large ~10–50 KB); complicates `ChatSession` model | Acceptable, but option (a) is cleaner |
| **(c) In-memory `ConcurrentDictionary`** | Zero latency; no serialization | Does not survive App Service instance restarts or scale-out; violates ADR-009 | Rejected |

**Justification for option (a)**:
- `ChatSessionManager` already has the `IDistributedCache` and knows the key pattern.
- A separate key with a **30-minute TTL** is appropriate: the plan preview window is an interactive gate, not long-lived.
- If the user walks away, the pending plan expires cleanly without polluting the session record.
- The `plan_preview` SSE event carries the full plan payload to the frontend immediately, so the Redis entry is only needed at approval time (`POST /plan/approve`).

**Cache key**: `plan:pending:{tenantId}:{sessionId}`

**TTL**: 30 minutes (sliding is unnecessary here; absolute expiry is sufficient).

### Q2: What is the `sessionId` — how is it generated and passed between frontend and backend?

- Generated server-side: `Guid.NewGuid().ToString("N")` — a 32-character lowercase hex string (no hyphens).
- Returned in the `POST /api/ai/chat/sessions` response as `sessionId`.
- Passed to every subsequent BFF call as a URL path segment.
- Never transmitted in the request body; always in the route.
- The frontend (`useChatSession` hook) holds the session state including `sessionId`.
- The `handlePlanProceed` callback in `SprkChat.tsx` already has access to `session?.sessionId` via closure.

### Q3: What is the `plan_preview` SSE event shape?

The BFF SSE channel uses the format:
```
data: {"type":"<event-type>","content":"<text or null>","data":{...}}\n\n
```

The existing `ChatSseEvent` record (defined in `ChatEndpoints.cs`) carries `type`, `content`, and an optional `data` object.

**Proposed `plan_preview` SSE event**:

```json
{
  "type": "plan_preview",
  "content": null,
  "data": {
    "planId": "a1b2c3d4e5f6...",
    "planTitle": "Analyze contract risk and summarize findings",
    "steps": [
      {
        "id": "step-1",
        "description": "Run ContractRiskAnalysis action on the active document",
        "status": "pending"
      },
      {
        "id": "step-2",
        "description": "Save analysis output to working document",
        "status": "pending"
      }
    ],
    "analysisId": "uuid-of-sprk_analysisoutput-record",
    "writeBackTarget": "sprk_analysisoutput.sprk_workingdocument"
  }
}
```

**Key fields**:

| Field | Type | Purpose |
|-------|------|---------|
| `planId` | `string` | Unique ID for this pending plan; stored as sub-key or the Redis value key; sent back by frontend on approval |
| `planTitle` | `string` | Display title for `PlanPreviewCard` header |
| `steps` | `PlanStep[]` | Ordered steps with id, description, status |
| `analysisId` | `string?` | GUID of `sprk_analysisoutput` record (nullable; absent for non-analysis plans) |
| `writeBackTarget` | `string?` | Canonical field path `"sprk_analysisoutput.sprk_workingdocument"` for the write-back step |

**Note**: The `useSseStream` hook in the frontend currently handles `token`, `done`, `error`, `suggestions`, `citations` event types. Task 071 must add handling for `plan_preview` — it does not accumulate into `content` but instead sets `metadata.responseType = 'plan_preview'` on the assistant message.

### Q4: What happens to the pending plan when the user approves it?

1. User clicks "Proceed" on `PlanPreviewCard`.
2. `handlePlanProceed(messageIndex)` fires in `SprkChat.tsx` (currently a stub — wired in task 072).
3. Frontend calls `POST /api/ai/chat/sessions/{sessionId}/plan/approve` with `{ planId }` in the body.
4. BFF endpoint:
   a. Extracts `tenantId` from JWT.
   b. Loads the pending plan from Redis: `plan:pending:{tenantId}:{sessionId}`.
   c. Validates `planId` matches.
   d. Begins SSE stream execution — iterates over plan steps, executing each one.
   e. Emits `step_start`, `token` (for intermediate output), and `step_complete` events per step.
   f. When all steps succeed, calls `AnalysisOrchestrationService.UpdateWorkingDocumentAsync` (task 073).
   g. Emits `done` event.
5. Frontend `useSseStream` receives streaming updates; `SprkChat.tsx` updates the `PlanPreviewCard` step statuses in real time via `updateLastMessage` / message metadata mutation.

### Q5: What is the write-back target?

- **Entity**: `sprk_analysisoutput` (Dataverse)
- **Field**: `sprk_workingdocument` (string, max ~10,000 chars in current schema)
- **Access method**: `IWorkingDocumentService.UpdateWorkingDocumentAsync(analysisId, content, ct)` — already implemented in `AnalysisResultPersistence` and called by `AnalysisOrchestrationService`.
- **Never**: SPE source files in SharePoint Embedded. The constraint from `CLAUDE.md` is explicit: "Write-back targets `sprk_analysisoutput.sprk_workingdocument` ONLY — never SPE source files."

---

## 3. Recommended Storage Mechanism: Redis Separate Key

### PendingPlan model (proposed C# record for task 071)

```csharp
/// <summary>
/// Represents a plan pending user approval.
/// Stored in Redis at key "plan:pending:{tenantId}:{sessionId}" with 30-minute TTL.
/// </summary>
public record PendingPlan(
    string PlanId,
    string SessionId,
    string TenantId,
    string PlanTitle,
    PendingPlanStep[] Steps,
    string? AnalysisId,
    string? WriteBackTarget,
    DateTimeOffset CreatedAt);

public record PendingPlanStep(
    string Id,
    string Description,
    string ToolName,       // e.g. "RunAnalysis", "SaveWorkingDocument"
    // Tool-specific parameters encoded as JSON
    string ParametersJson);
```

### Redis key helpers

Following the ADR-014 key pattern used in `ChatSessionManager`:

```csharp
internal static string BuildPendingPlanKey(string tenantId, string sessionId)
    => $"plan:pending:{tenantId}:{sessionId}";
```

TTL: `TimeSpan.FromMinutes(30)` (absolute, not sliding).

---

## 4. Proposed `POST /plan/approve` Request/Response Shape

**Endpoint**: `POST /api/ai/chat/sessions/{sessionId}/plan/approve`

**Request body**:
```json
{
  "planId": "a1b2c3d4e5f6..."
}
```

**Response**: SSE stream (`text/event-stream`), same format as `/messages` endpoint.

**SSE event sequence during approval execution**:

```
data: {"type":"plan_step_start","content":null,"data":{"stepId":"step-1","stepIndex":0}}

data: {"type":"token","content":"Running ContractRiskAnalysis..."}

data: {"type":"plan_step_complete","content":null,"data":{"stepId":"step-1","status":"completed","result":"Analysis complete: 3 risks identified"}}

data: {"type":"plan_step_start","content":null,"data":{"stepId":"step-2","stepIndex":1}}

data: {"type":"plan_step_complete","content":null,"data":{"stepId":"step-2","status":"completed","result":"Working document updated"}}

data: {"type":"done","content":null}
```

**Error case** (step failure):
```
data: {"type":"plan_step_complete","content":null,"data":{"stepId":"step-1","status":"failed","errorCode":"TOOL_EXECUTION_FAILED","errorMessage":"Analysis action timed out"}}

data: {"type":"error","content":"Plan execution halted at step 1"}
```

**HTTP error responses** (non-SSE):
- `400`: planId missing or body malformed
- `404`: session not found or pending plan not found (expired/never existed)
- `409`: plan already being executed (duplicate approval)

---

## 5. Risks and Open Questions for Task 072

### Risk 1: Plan expiry during user deliberation
The 30-minute TTL should be generous enough. If the plan expires, the `POST /plan/approve` endpoint returns 404, and the frontend should show "Plan expired — please resend your request."

### Risk 2: Concurrent approval requests
If the user double-clicks "Proceed," two approval requests could race. The BFF should delete the Redis key before execution begins (atomic delete + execute). If a second request arrives and the key is already gone, return 409 Conflict.

**Pattern**: Redis `DELETE` returns `0` (key not found) → 409. Redis `DELETE` returns `1` (key found and deleted) → proceed with execution.

### Risk 3: Step execution atomicity
If step 1 of a 3-step plan completes but step 2 fails, the write-back from step 1 may have already happened. Task 072 should document whether partial execution is rolled back or left in place. For the initial implementation, leave partial writes in place (the working document is a draft; users can re-run).

### Risk 4: AnalysisId availability
The `plan_preview` SSE event must include the `analysisId` when the plan involves write-back. The BFF must have this ID available at plan generation time (task 071). This comes from the `ChatContext.KnowledgeScope` → `AnalysisMetadata` populated by `AnalysisChatContextResolver`.

### Open Question: Should `plan_preview` replace or augment the assistant message text?
Two options:
- **Replace**: The assistant message renders only the `PlanPreviewCard` (no text above it).
- **Augment**: The assistant message renders preamble text ("I'll help you with that. Here's the plan:") followed by the card.

**Recommendation**: Support both. If `content` is non-empty in the `plan_preview` SSE event, render it as preamble text above the card. `SprkChatMessage.tsx` already handles this via the `metadata.responseType === 'plan_preview'` branch in `SprkChatMessage` — the `content` field is rendered as text above the `PlanPreviewCard`.

---

## 6. Implementation Checklist for Task 072

- [ ] Add `PendingPlan` record to `Models/Ai/Chat/PendingPlan.cs`
- [ ] Add `BuildPendingPlanKey` helper to `ChatSessionManager` (or a new `PendingPlanManager`)
- [ ] Add `StorePendingPlanAsync(plan, ct)` — serializes to JSON, stores with 30-min TTL
- [ ] Add `GetAndDeletePendingPlanAsync(tenantId, sessionId, planId, ct)` — atomic get+delete
- [ ] Register endpoint: `POST /api/ai/chat/sessions/{sessionId}/plan/approve`
- [ ] Implement execution loop: iterate steps, emit SSE events per step
- [ ] Wire write-back: call `IWorkingDocumentService.UpdateWorkingDocumentAsync` for write-back steps
- [ ] Frontend (`SprkChat.tsx`): replace `handlePlanProceed` stub with fetch call to approval endpoint
- [ ] Frontend: handle new SSE event types `plan_step_start`, `plan_step_complete` in `useSseStream`

---

*This document is the deliverable for task 070. It should be read by anyone implementing tasks 071, 072, or 073.*
