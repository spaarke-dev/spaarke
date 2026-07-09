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
| **Resolution path** | Follow-up after task **035** (Completion Engine + OutcomeCard all paths) lands — either an `OutcomeCard` shape amendment relaxing store-before-render for "nothing happened, here's why" outcomes, or wiring through the resume executor. |
| **Severity** | Low (NFR-10 "never a dead end" already shipped via the markdown channel; this is a fidelity upgrade). |

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
| **Concrete failing behavior** | Conversational "create a matter" still falls to the generic `dataverse.create_record` path with no capability-specific tool description or eval coverage — the `CREATE-MATTER@v1` Action row, the `sprk_playbookconsumer` Binding row, and `ConsumerTypes.CreateMatter` activation are authored but NOT deployed live (adding the constant without a live row would trip `RoutingConsumerTypeHealthCheck` → `ConstantsWithoutRows` → Unhealthy on spaarkedev1). Golden-utterances GU-065/066/067 remain `catalogStatus:"planned"`. |
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
