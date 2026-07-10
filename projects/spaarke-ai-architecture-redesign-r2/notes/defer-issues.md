# Deferrals & Issues — spaarke-ai-architecture-redesign-r2

> **Source of truth** for deferred work + issues. Per root CLAUDE.md §11, every entry names a **concrete failing behavior/contract** (not "future flexibility").
> Two-write rule: each entry MUST also be filed as a GitHub Issue via `/project-defer-issue-tracking` (`/defer`). `push-to-github` blocks push on entries whose **GitHub Issue** row still shows the `{URL}` placeholder.
> **Last updated**: 2026-07-09 (Wave J consolidation).

---

## DEF-001 — OutcomeCard-structured refusal disposition (from task 040)

| Field | Value |
|---|---|
| **Origin task** | 040 (refusal-affordance links) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/591 |
| **Concrete failing behavior** | A client that renders `OutcomeCard`-style structured chips (`CreatedRecord`, next-step actions) cannot render the Document-Upload refusal affordance as a structured chip — the R5-E `sprk_document` hard-block emits the deep-link only as markdown text on the plain refusal-message channel (`ToolValidationResult.Errors` / `ToolResult.ErrorMessage`). |
| **Why deferred** | `OutcomeCard.ForStoredOutcome` enforces store-before-render (ADR-040) — it requires a non-empty `ledgerOutputKey` from a persisted `SessionOutput`, but a hard-blocked write produces no stored outcome to reference. Routing a structured refusal would also require editing `TypedHandlerResumeExecutor.cs` + `ChatEndpoints.cs` (`BuildGateOutcomeMessage`), squarely inside the gate-resume machinery. |
| **Resolution path** | **PARTIAL as of task 035 (2026-07-09) — issue STAYS OPEN.** 035 wired OutcomeCard across all *SessionOutput-backed* paths, so the refusal-**capability** (no-match handler) path now emits a structured card automatically. But the R5-E pre-suspend hard-block (`SideEffectGateAIFunction.RenderPreSuspendRefusalAsync`) stores a terminal **Gate marker** (`validation-failed`), NOT a `SessionOutput` — `OutcomeCard.ForStoredOutcome` requires a SessionOutput key. Fully structuring it would need either (a) weakening store-before-render (forbidden) or (b) a non-additive `refused` `OutcomeStatus` on the frozen task-011 contract + a new client SSE channel (the hard-block returns a model-instruction *string* to the loop, not a client response). Remaining path: address (b) as an explicit contract amendment if/when a structured hard-block refusal is warranted. |
| **Severity** | Low (NFR-10 "never a dead end" already shipped via the markdown channel; capability-refusal path now structured; only the pre-suspend hard-block remains markdown-only). |

## DEF-002 — Soft-slash launcher menu UI wiring (from task 041)

| Field | Value |
|---|---|
| **Origin task** | 041 (capability-discovery READ endpoint) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/592 |
| **Concrete failing behavior** | A newly-cataloged capability (e.g. create-matter from task 042) does NOT appear in the soft-slash launcher menu until `CommandRouter.ts` is hand-edited — the `SoftSlashes` vocabulary is a hardcoded 4-item closed literal union, orthogonal to the dynamic catalog list now served by `GET /api/ai/capabilities`. The `useCapabilityDiscovery` hook is plumbed but not wired into a visible menu. |
| **Why deferred** | Converting the hardcoded union to a dynamic catalog-driven menu touches hot shared files (`CommandRouter.ts`, `CommandHelpPanel.tsx`) that task 042 also referenced; 041 deliberately built the hook uncoupled to avoid a parallel-wave merge conflict. |
| **Resolution path** | Follow-on task: render the soft-slash menu from `useCapabilityDiscovery` output, replacing/augmenting the static `SoftSlashes` union. |
| **Severity** | Medium (capability is discoverable via API but not yet surfaced in the launcher UX). |

## DEF-003 — Live create-matter Binding/Action seeding + activation (from task 042)

| Field | Value |
|---|---|
| **Origin task** | 042 (cataloged create-matter capability) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/593 |
| **Concrete failing behavior** | Conversational "create a matter" still falls to the generic `dataverse.create_record` path with no capability-specific tool description or eval coverage — the `CREATE-MATTER@v1` Action row, the `sprk_playbookconsumer` Binding row, and `ConsumerTypes.CreateMatter` activation are authored but NOT deployed live. Golden-utterances GU-065/066/067 remain `catalogStatus:"planned"`. **UPDATE (E-40, 2026-07-09):** registering the constant ahead of its row NO LONGER trips Unhealthy — `ConstantsWithoutRows` is now `Degraded` (forward-declaration), so the constant *could* be registered ahead of the row without a hard `/healthz` failure. The task-049 atomic constant+row seed + GU flip is still the resolution path (the golden-utterance planned→existing grounding guard is the remaining coupling, not the health check). |
| **Why deferred** | Live writes to shared Dataverse catalog tables were out of this task's authorized tool scope and unsafe while other agents were concurrently active on the same environment. Consistent with Model 1 GitOps + task-020 precedent. |
| **Resolution path** | Owned by the **G-R2-A gate deploy step (task 049)** — full 7-step sequence recorded in `notes/jps/create-matter-binding-row-pending-seed.json`; flip GU-065/066/067 `planned`→`existing` after seeding. |
| **Severity** | Medium (capability inert until gate-deploy; contract test has a loud tripwire asserting the live mirror does NOT yet contain create-matter). |

## DEF-004 — Ack endpoint session-ownership check (from task 037)

| Field | Value |
|---|---|
| **Origin task** | 037 (UI-action truthfulness / client-ack) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/594 |
| **Concrete failing behavior** | `POST /api/ai/chat/sessions/{sessionId}/ack` performs no session/tenant-ownership validation — any authenticated user who obtains a `(sessionId, frameId)` pair could POST a spoofed ack, resolving another user's UI-action-ack coordinator and causing a tool to report success without that user's client having rendered anything. |
| **Why deferred** | Exploitability is negligible in practice: `frameId` is an unguessable server-issued GUID (capability-token pattern), so an attacker cannot forge a valid ack without already observing the victim's SSE frame. |
| **Resolution path** | Hardening follow-up: add session-ownership/tenant validation on the ack endpoint (bind `sessionId` to the caller's identity). |
| **Severity** | Low (capability-token mitigates; defense-in-depth). |

---

## Phase E deferrals (re-parented by E-40, 2026-07-09) — formal `/defer` filing batched at 090 wrap-up

These deferrals were surfaced during Phase E and are captured here + in the cited artifacts; they are NOT lost. Per the project's deferral gate, formal GitHub-Issue filing (two-write rule) is batched at the `090` wrap-up `/defer` pass. Each names a concrete failing behavior and a re-parented owner.

- **PE-D1 — E-30 clickable file link in the upload notification.** *Failing behavior:* the upload-notification email names the file but carries no clickable link; a user-openable SharePoint Embedded URL cannot be resolved inside a coded workflow (no OBO token in `CodedWorkflowContext`). *Owner/path:* either persist the upload `WebUrl` on `ChatSessionFile` at upload time, OR an OBO-safe link facade (overlaps DEF/Fork-C below). Documented in the E-30 commit (`0507609a1`) + `SendUploadNotificationWorkflow` XML doc. *Severity:* Low (notification works; link is an enhancement).
- **PE-D2 — Live catalog wiring for the coded-dispatch capability.** *Failing behavior:* no live `sprk_playbookconsumer` Binding + Coded Action + `consumerType` + golden-utterance case exists to dispatch a coded workflow from a real chat chip/utterance — the E-30 seam is proven by seam tests but not yet a deployed feature. *Owner/path:* a deployment task (same shape as DEF-003 create-matter seed), authored when a coded chat capability is productized. *Severity:* Low (seam proven; deployment is additive).
- **PE-D3 — Multi-step "Action Engine" internals.** *Failing behavior:* there is no engine for user-triggered multi-step/agentic execution — only the reserved dispatch seam (front door). *Owner/path:* a dedicated future project (Microsoft Agent Framework spike unresolved); the seam is reserved in [ADR-043 §Forward-compatibility]. *Severity:* N/A (explicitly out of r2 scope, operator-confirmed).
- **PE-D4 — Fork-C `IDocumentProfileAi` OBO-safe facade (compose-r2 re-ping).** *Failing behavior:* compose-r2's create-on-save profile analysis has no core-owned OBO-safe `Services/Ai/PublicContracts/IDocumentProfileAi` facade (app-only `IAppOnlyAnalysisService` trips ADR-013 + MI-403s on the OBO-written file); their task 013 ships profile as a `deferred` job step until it lands. *Owner/path:* **operator scheduling decision** (accept into a core follow-on vs. compose-owns-with-ack) — surfaced in `notes/REPLY-to-compose-r2-e20-e30-forkc.md` item 4. *Severity:* Medium (R5-E full profile bar unmet until resolved).

- **PE-D5 — 🔔 Matter-level AI-retrieval ACL is not genuinely enforced (from task 063 spike).** *Failing behavior:* AI knowledge retrieval does not enforce a matter/tenant ethical wall in production: (1) `KnowledgeDocument.PrivilegeGroupIds` is never populated at ingestion (`FileIndexingService.cs:308-331`), so the `privilege_group_ids` AD-group filter always matches everything (permanent no-op); (2) the only matter-varying filter clause (`parentEntityId`) is populated straight from client-supplied `ChatHostContext.EntityId` (`PlaybookChatContextProvider.cs:323-324`) with NO BFF-side authorization that the caller may view that matter. Net: "whatever matter the client asked for, unchecked." *Owner/path:* **its own scoped SECURITY PROJECT (operator to open)** — NOT r2-core scope (task 063 pre-declared this security-escalation). Two remediation directions in `notes/063-...-findings.md` §6: (i) populate `PrivilegeGroupIds` from matter-derived AD groups at ingestion + add a BFF authz check on `HostContext.EntityId`; OR (ii) if Dataverse/SPE permissions are the intended control, formally retire `privilege_group_ids` as legacy. *Severity:* **HIGH (security — cross-matter retrieval exposure).**
  - **PE-D5 scope addition (task 052 consolidation, 2026-07-10 — §6.5 path-A candidate, operator to confirm):** the new memory-governance record-read (`GET /api/memory/records/{entity}/{id}`) is authorized at **entity-TYPE granularity** (`IDataversePrivilegeChecker.HasReadPrivilegeAsync` = impersonated `RetrieveUserPrivileges`), NOT row-level — the BFF has no generic per-row OBO record-read primitive (only `sprk_documents` via `DataverseAccessDataSource`). *Failing behavior:* a caller with type-level Read privilege but row-level denial (ethical wall / BU scoping) can read that record's MEMORY despite not being able to open the record. Same exposure class as the parent finding; `IMemoryAccessAuthorizer.CanCallerReadRecordAsync` already carries `recordId`, so row-level enforcement is a drop-in inside this security project. Documented in the authorizer/endpoint XML docs.

- **PE-D7 — AuditLog full-suite parallelism flake (from compose-r2 handoff).** *Failing behavior:* `AuditLogServiceTests.LogInteractionAsync_PartitionsByTenantId` passes in isolation but fails under full-suite parallelism (shared-state/ordering race in the test, not the product). Pre-existing, unrelated to compose/E-20. **2026-07-10 update (M2 batch consolidation):** with ~150 new tests reshuffling xUnit parallel scheduling, a SECOND test in the same class (`LogInteractionAsync_UsesCreateItemAsync_NotUpsertOrReplace`) now also flakes under full-suite runs; both pass 16/16 in class isolation — same shared-state race, wider blast. Raises the priority of the fix (isolate shared state or `[Collection]`-serialize the class). *Owner/path:* core test-hygiene backlog — isolate the shared state or mark the test non-parallel. *Severity:* Low (test-only flake; no product defect).

- **PE-D8 — Consolidate schema-card/write-contract prose into the Business slice + render envelope context into DISPATCH prompts (from task 053).** *Failing behavior:* (a) per-table write-contract/schema prose is still hand-mirrored across 6 dataverse.* tool descriptions (single-source is the task-020 JSON, but the CONTENT is repeated per tool — a schema change means N description edits) instead of being assembled once into the ContextEnvelope Business slice; (b) dispatch-path prompts do not yet carry the envelope's Business/Memory context — a dispatched capability cannot see host-record memory even though the envelope now carries it (interactive chat can). *Owner/path:* deliberate follow-on AFTER task 054 fixes budgets: (a) goes through the task-020 catalog JSON source (description slimming = catalog migration + eval re-baseline); (b) is a one-line composition change in ActionRunner (the renderer ships ready in 053) gated on budget reconciliation + eval re-baseline. Recorded in `notes/053-implementation-design.md` "Explicitly OUT". *Severity:* Medium (capability parity between interactive and dispatch context).

- **PE-D6 — Wire `ISemanticScopeProvider` into ContextBinder Semantic slice (from task 061).** *Failing behavior:* the semantic-scope provider (061) is registered + ACL-preserving but the `ContextBinder` does not yet read the Semantic slice through it — 061's AC1 wiring was deliberately deferred because 060 was concurrently editing `ContextBinder.cs`, AND because the provider is a REAL RAG-querying impl (unlike 060's Null-Object) so wiring it to run on every bind would do a semantic search per dispatch. *Owner/path:* a small follow-up that adds a retrieval-trigger signal to `ContextBindingRequest` and calls the provider ONLY when the action declares it wants retrieval (mirror 060's `envelope with { Semantic = ... }` shape, but conditional). *Severity:* Low (provider done + tested; wiring is additive + needs the conditional-trigger design).
