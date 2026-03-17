# Lessons Learned — ai-sprk-chat-workspace-companion

> **Project**: SprkChat Analysis Workspace Companion
> **Completed**: 2026-03-16
> **Total Tasks**: 29 across 11 parallel groups

---

## 1. Parallel Execution Worked Exceptionally Well

**What happened**: The project was decomposed into 11 parallel execution groups (A through K). Tasks in different modules — BFF API, shared UI library, and code pages — ran simultaneously with zero file conflicts.

**Key insight**: As long as each parallel group owns distinct files (no shared imports that would conflict), parallel task execution dramatically reduces wall-clock time. The decomposition from 29 sequential tasks to 11 batched groups was the single biggest efficiency gain.

**Recommendation for future projects**: Invest time upfront in the TASK-INDEX.md parallel group design. Map file ownership explicitly. The rule is simple: if two tasks touch different files and neither imports from the other, they can run in parallel.

**Numbers**: 29 tasks would have taken ~29 serial steps. 11 parallel batches compressed this to ~11 rounds. Groups A, B, C ran 4+ tasks simultaneously.

---

## 2. `mousedown` vs `click` — Critical Pattern for Toolbar Buttons

**What happened**: The `InlineAiToolbar` inline action buttons were initially wired with `onClick`. This caused the browser's `selectionchange` event to fire on mousedown, collapsing the text selection before the click handler could capture it. The toolbar would disappear before the action fired.

**Root cause**: Browser fires `selectionchange` → React re-renders → toolbar hides, all before `onClick` triggers.

**Fix**: Use `onMouseDown` with `event.preventDefault()` on all toolbar action buttons. This prevents the selection collapse while still capturing the user intent.

```tsx
// CORRECT — preserves text selection
<Button onMouseDown={(e) => { e.preventDefault(); onAction(action); }}>
  {action.label}
</Button>

// WRONG — selection collapses before handler fires
<Button onClick={() => onAction(action)}>
  {action.label}
</Button>
```

**Spec reference**: FR-04 explicitly required `mousedown`. This was documented in the spec, but the implementation detail (`event.preventDefault()`) was discovered during task 011.

---

## 3. Plan Preview SSE Design — Redis is Correct for Multi-Instance App Service

**What happened**: Task 070 investigated how to store "pending plan" state between the `plan_preview` SSE emit and the user's `POST /plan/approve` call. Two options were considered: (a) store in `ChatSession` Redis record, (b) store in a separate Redis key.

**Decision**: Separate Redis key `plan:pending:{tenantId}:{sessionId}` with 30-minute absolute TTL.

**Why not in-memory**: Azure App Service runs multiple instances. The `plan_preview` SSE response and the `POST /plan/approve` request may hit different instances. In-memory state would cause 404 on approval ~50% of the time under load.

**Why not on ChatSession**: Adding `PendingPlan` as a field on `ChatSession` inflates every session cache read with plan payload data. Plans are ephemeral (30-minute TTL); sessions are longer-lived.

**Atomic get-and-delete on approval**: The approval handler uses Redis `GETDEL` semantics (get-and-delete atomically) to prevent double-execution if the user clicks "Proceed" twice rapidly.

---

## 4. SPE Safety Constraint — Grep as Integration Test Supplement

**What happened**: FR-12 requires that `sprk_analysisoutput.sprk_workingdocument` is the ONLY write target. SPE source files MUST NOT be written.

**What we did**: Two layers of enforcement:
1. `WorkingDocumentToolsTests.WriteBackToWorkingDocumentAsync_DoesNotCallSpeFileStore_NorAnyChatClientWrite` — xUnit test asserting `SpeFileStore` mock receives zero write calls.
2. Manual grep after every write-back task: `grep -r "UploadContent|PutContent|UpdateFile" src/server/api/Sprk.Bff.Api/Services/Ai/` — must return zero matches except the safety comment.

**Recommendation**: For any project with a "MUST NOT write to X" constraint, add both a unit test assertion AND a grep check to the wrap-up task. The grep gives a human-readable audit trail; the test gives regression protection.

---

## 5. BroadcastChannel Name Must Be Verified Against Subscriber

**What happened**: Task 012 created `useInlineAiActions.ts` which publishes to a BroadcastChannel. The subscriber in `SprkChatPane` was already established in Phase 1 under channel name `'sprk-inline-action'`.

**Risk**: If the publisher and subscriber use different channel names, events are silently dropped — no runtime error, no warning. The toolbar appears to work but nothing happens in SprkChat.

**Mitigation**: Task 012 explicitly verified the subscriber channel name before writing the publisher. The channel name `'sprk-inline-action'` is now documented in `inlineAiToolbar.types.ts` as a constant `INLINE_ACTION_CHANNEL`.

**Recommendation**: Extract BroadcastChannel names to shared constants in the types file. Never hardcode channel name strings in both publisher and subscriber independently.

---

## 6. Route Registration — Multiple MapGroup Calls with Same Prefix Are Safe

**What happened**: `AnalysisChatContextEndpoints.cs` and `ChatEndpoints.cs` both call `app.MapGroup("/api/ai/chat")`. This raised a question: do duplicate group prefixes conflict or shadow each other?

**Answer**: In ASP.NET Core Minimal API, multiple `MapGroup()` calls with the same prefix are **additive**. Routes from both groups are registered and accessible. There is no shadowing or silent failure.

**The 404 in the deployed environment** (task 082 notes) was a stale deployment artifact — the new build was not fully active on the App Service at the time of testing. The code was correct.

**Recommendation**: When BFF endpoints return unexpected 404 in a deployed environment, first verify the deployment completed successfully and the App Service restarted with the new binary before investigating route registration.

---

## 7. Task Decomposition Quality Correlates Directly with Execution Quality

**What happened**: Tasks where the POML file had detailed `<steps>`, explicit `<constraints>`, and named `<knowledge><files>` were executed cleanly in one pass. Tasks with vague step descriptions required more back-and-forth.

**Best tasks in this project**: Tasks 020-022 (BFF endpoint) and 040-043 (UI components) — each had 6-8 concrete steps, named the exact files to create, and referenced specific ADRs.

**Recommendation**: Time spent on detailed task decomposition during `task-create` directly reduces total implementation time. The ratio is approximately 1:3 — one hour of detailed task writing saves three hours of rework.

---

*This document was created as part of task 090 (project wrap-up) on 2026-03-16.*
