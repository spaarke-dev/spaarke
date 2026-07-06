# Task 031 — Confirmation Gate Unification (D12 / FR-P2-02) — Execution Notes

> Date: 2026-07-06 · Branch: `work/spaarke-ai-architecture-redesign-r1` · Rigor: FULL
> Status: code complete; commit/status updates owned by the main session per wave protocol.

## What shipped

**ONE Confirmation Gate.** `PendingPlanManager` generalized into THE unified pending store:

- **New generalized surface** (all `virtual`, Null-peer overridden per ADR-032):
  - `SuspendInvocationAsync(PendingInvocation)` — ledger `SessionGate` pending marker FIRST (ADR-040 storage-precedes-rendering), then resumable payload to Redis (`pending-gate` resource, key `{sessionId}:{gateId}`, 30-min TTL, ADR-014 tenant-scoped).
  - `GetInvocationAsync` / `ResumeInvocationAsync` (get-then-delete, double-confirm → null/409 semantics) / `RejectInvocationAsync` (idempotent).
  - `WriteGateMarkerAsync` — ledger-marker-only API for gate presentations whose resumable state is elsewhere in the same store (plan preview, FR-48 options) and for task 032's `elicitation` markers.
  - `static RequiresConfirmation(ToolSideEffectClass?, BindingRisk, bool dispatchUncertain)` — THE metadata-driven gate decision (ADR-039): `write`/`communicate` gate; `AlwaysConfirm` gates anything; `ConfirmWhenUncertain` gates when the dispatcher self-reports uncertainty.
- **Ledger**: `ChatSessionManager.AppendGateAsync` (mirrors task 030's `AppendToolChainAsync` re-fetch-then-append pattern; allocates per-session gate turn ordinal; resolutions reuse the pending turn).
- **Catalog serving**: `AnalysisTool.SideEffectClass` (+ `ToolSideEffectClass` enum, values 100000000..100000003 per schema-p0 column dictionary) mapped from `sprk_sideeffectclass` in `AnalysisToolService` (list + single-row paths; unknown/legacy → null, never throws).
- **Metadata-driven detection**: `CompoundIntentDetector` name lists DELETED; single-call gating keys on a caller-supplied `toolName → ToolSideEffectClass?` lookup (keys cover raw `sprk_name` AND `SanitiseToolName` output); 2+ calls remains a structural trigger. `ChatEndpoints.SendMessageAsync` builds the lookup from tool rows only on tool-proposing turns.
- **FR-48 must-click = gate presentation**: `playbook_options` writes a pending Gate marker (`options-{guid}`) BEFORE the SSE render; `ExecutePlaybookAsync` (the user's pick) resolves the session's latest unresolved options marker as confirmed.
- **Plan preview = gate presentation**: pending marker (GateId = PlanId) written before `plan_preview`; `ApprovePlanAsync` writes the confirmed marker before execution.
- **Telemetry (NFR-09 / ADR-016)**: structured events `gate_suspended` / `gate_confirmed` / `gate_rejected` / `gate_confirm_miss` / `gate_reject_miss` / `gate_marker` — identifiers + counts only (NFR-07; `ArgsJson` never logged).

## Delete inventory (hard cutover, NFR-08)

| Deleted | Where |
|---|---|
| `POST /sessions/{sessionId}/actions/{actionId}/confirm` endpoint mapping + `ConfirmActionAsync` handler (Task R2-052 stub — no server emitter for `action_confirmation` existed; dead end-to-end) | `Api/Ai/ChatEndpoints.cs` |
| `ActionConfirmRequest` / `ActionConfirmResult` DTOs | `Api/Ai/ChatEndpoints.cs` |
| `CompoundIntentDetector.WriteBackToolNames` (6 names) + `ExternalActionToolNames` (4 names) hardcoded gating lists | `Services/Ai/Chat/CompoundIntentDetector.cs` |
| Client POST to the deleted route (`dispatchConfirmedAction`) — replaced with a local, loudly-logged failure result; dialog becomes a unified-gate presentation at W-P2-B (task 032) | `SprkChat/hooks/useActionHandlers.ts` |
| Stale comments referencing the deleted lists | `AgentToolCatalogProjector.cs` (comment), `WorkingDocumentTools.cs` (comments), `infra/.../working-doc-write-back-row.json` (comment) |

## Grep-zero evidence (shown)

```
$ grep -rn -E "ConfirmActionAsync|ActionConfirmRequest|ActionConfirmResult" src/ tests/
(no matches)
$ grep -rn "actions/{actionId}/confirm" src/ tests/          # incl. client template-string form
(no matches)
$ grep -rn -E "WriteBackToolNames|ExternalActionToolNames" src/ tests/ infra/
(no matches)
```

Remaining mentions of the deleted list names exist ONLY in `projects/*audit*/notes/**` historical evidence documents (records of the pre-D12 state, not code).

## Seed-row follow-up

`infra/dataverse/sprk_analysistool-working-doc-write-back-row.json` now declares `sprk_sideeffectclass = 100000001 (Write)` so the migrated write-back tool keeps its always-gated behavior under the declared-metadata gate. **Deployment note**: re-run `scripts/Seed-TypedHandlers.ps1` against spaarkedev1 to upsert the column. The dataverse-* rows (tasks 008/009) already declare it. Sibling rows WORKING-DOC-EDIT / WORKING-DOC-APPEND-SECTION were left undeclared (Read-shaped preview/stream operations; persistence only flows through WRITE-BACK) — operator may choose to declare them Write if preview-edit should also gate.

## Tests

- New `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/ConfirmationGateUnificationTests.cs` (13 tests, maintain-class contract anchors): policy matrix (class × risk × uncertainty), suspend→ledger-pending+payload, resume→confirmed marker + turn correlation, double-confirm 409 semantics, reject idempotency, marker pending/confirmed correlation, detector declared-class gating (write gates / read doesn't / structural 2+ / null-lookup degrades).
- Updated `ChatSessionPlanEndpointTests.PendingPlanManagerTests` ctor for the new `ChatSessionManager` dependency.
- Targeted run: **57/57 green**. Full suite: 8016 total, 7908–7909 passed, 101 skipped, failures = 6 known pre-existing (ExecutorConfigSchemas, KnowledgeDeploymentConfig, DailyBriefingCollector, TemplateContextBuilder TextOnly, SessionFilesCleanup, AuditLogService flake) + 1 belonging to task 030's in-flight health-check escalation (`RoutingConsumerTypeHealthCheckTests...ReturnsDegradedNamingOrphanHandler` expects Degraded, 030's escalation now returns Unhealthy — 030 owns that file/test).
- Eval suite (NFR-02): no golden-utterance eval case exercises the deleted confirm endpoint or name-list gating (grep-zero across `tests/`); gate-specific eval cases land with task 037 (FR-P2-08 compound + injection families).

## Publish size (ADR-029 / CLAUDE.md §10.4)

`dotnet publish -c Release` → compressed **45.60 MB** (270 files) vs prior baseline **~46.91 MB** = **−1.31 MB** (deletion task; ceiling 60 MB — no escalation). Note the working tree also carries task 030's factory shrink, so the delta is the combined wave effect.

## Step 9.5 quality gates

- **code-review**: PASS — 0 Critical; 3 documented warnings (W1 suspend-on-missing-session skips marker with loud log; W2 per-turn tool-catalog query on the legacy detection path, dies at task 034; W3 client dead-leg returns local failure until 032 rewires).
- **adr-check**: PASS — 0 violations; ADR-039/040/032/010/009/014/013/008/015/016/028 compliant; NetArchTest ADR-010 ceiling failure is pre-existing/known.

## Integration points for W-P2-B (tasks 032 / 034)

1. **Loop suspend seam (030→gate)**: at the loop's tool-invocation boundary call `PendingPlanManager.RequiresConfirmation(tool.SideEffectClass, binding.Risk, dispatchUncertain)`; when true, build a `PendingInvocation` (GateId = new guid, ToolId, BindingId, ledger-vocab class via `ToLedgerSideEffectClass`, ArgsJson) and `SuspendInvocationAsync` INSTEAD of executing; surface GateId to the presentation.
2. **Resume execution (032)**: user confirmation → `ResumeInvocationAsync(tenant, session, gateId)` → execute the returned invocation via the loop; rejection → `RejectInvocationAsync`. No dedicated gate-resolve endpoint was added in 031 (zero live consumers; §11 default-to-reuse) — 032 chooses in-turn resume or adds the endpoint with the loop contract.
3. **Elicitation markers (032)**: `WriteGateMarkerAsync(kind: "elicitation", status: pending/…)` — vocabulary already supported by `SessionGate.Kind`.
4. **Client presentation (032/W-P2-B)**: `ActionConfirmationDialog` + `dispatchConfirmedAction` are retained as the FR-48 presentation shells; rewire `dispatchConfirmedAction` to the unified gate resume when the client leg lands.
5. **Legacy path death (034/035/036)**: the ChatEndpoints declaration-lookup + CompoundIntentDetector flow is interim; the hard cutover replaces it with the loop-native gate at the invocation seam.
