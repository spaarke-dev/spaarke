# r1 P4-Close Reconciliation — design §10 contingent rows 4/5/6/8/12

> **Task**: AIR2-001 (Phase 0) · **Spec**: FR-P0-01 · **Author**: task-execute (STANDARD rigor)
> **Purpose**: Re-check design §10's five contingent backlog rows against the ACTUAL r1 P4-close state (r1 = `spaarke-ai-architecture-redesign-r1`, merged to master via PR #551, post-close fix wave via PR #558; both merged — `git log --oneline` shows `c144fc465 Merge pull request #551`, `7a563db25 Merge pull request #558`). r2's worktree HEAD includes both merges (`8b0bc09d3` is the r2 A0/memory-posture merge, itself descended from `7a563db25`).
> **Method**: primary evidence = r1's own G-P4 gate file (`projects/spaarke-ai-architecture-redesign-r1/notes/g-p4-evidence.md`) + Track-B completion audit (`.../notes/track-b-completion-audit.md`, task 050, "62 rows, ZERO unexplained survivors") + r1 `tasks/TASK-INDEX.md` per-task notes, cross-checked against live grep in this worktree's `src/`.

---

## Disposition table

| Row | Item | Disposition | Unblocks |
|---|---|---|---|
| **4** | Legacy workspace tools (Get/Update/Close Workspace Tab + 4 artifact variants on `IWorkspaceStateService`) | **in-scope-FR** — r1 P4 did **NOT** close this row | FR-D-06 (task 075) |
| **5** | ADR-040 inline size-cap enforcement home | **verified-closed** — r1 already implemented + owns it | FR-B-15 (task 064) |
| **6** | create-task entity: `sprk_event(type=task)` vs `sprk_todo` | **accept-as-ruled** — r1 ruled `sprk_event`, shipped live | (no dedicated r2 FR gate; informs OutcomeCard link targets) |
| **8** | office-addins `SseClient` keep-with-reason | **accept-as-ruled** — r1 already dispositioned + grep-verified | (no dedicated r2 FR gate; closed input to client-consolidation hygiene) |
| **12** | Playbook/embeddings orphans on spaarkedev1 (`DAILY-BRIEFING-NARRATE` + `spaarke-playbook-embeddings`) | **in-scope-FR** — DAILY-BRIEFING-NARRATE sub-item is verified-closed; `spaarke-playbook-embeddings` index sub-item is the residual driving the row's overall disposition | FR-D-07 (task 076) |

---

## Row 4 — Legacy workspace tools verdict (FR-P4-01 → FR-D-06 / task 075)

**Disposition: in-scope-FR. r1 P4 did NOT finish this row — ESCALATION per task 001's own trigger.**

Evidence:
- r1's Track-B completion audit (`track-b-completion-audit.md` §11, item **O-2**, one of only 5 "operator-decision" survivors out of 62 audited rows — registered, not improvised): *"Workspace-tab tool cluster (3 rows + 3 handlers + Send legacy variants + `WorkspaceStateEndpoints`/`WorkspaceStateService` write path) → Coordinated code+row retirement **in r2**; keep `send_workspace_artifact` `widgetType:'Workspace'` leg + `GetTabsAsync` prompt block + `GET /api/workspace/state` restore."*
- r1's G-P3 round-4 UAT findings (`g-p3-uat-round4-findings.md:436`) explicitly lists as "Carried": *"FR-P4-01 legacy workspace tools verdict (r2)."*
- **Live grep in this worktree** (post both r1 merges) confirms the tools are still wired and unretired:
  - `src/server/api/Sprk.Bff.Api/Services/Workspace/IWorkspaceStateService.cs`
  - `src/server/api/Sprk.Bff.Api/Services/Workspace/WorkspaceStateService.cs`
  - `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceStateEndpoints.cs`
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/{UpdateWorkspaceTabHandler,GetWorkspaceTabContentHandler,CloseWorkspaceTabHandler,SendWorkspaceArtifactHandler}.cs`

**This is the literal example named in task 001's own escalation trigger** ("legacy workspace tools still wired to the orphaned `IWorkspaceStateService`"). It is NOT a silent scope-creep discovery, however: design.md §10 row 4 already priced in exactly this outcome ("If r1 P4 doesn't finish: core early Track-B — re-point or retire"), and r2's plan already carries a bounded, already-scoped task for it (**task 075**, `FR-D-06`, which itself reads task 001's disposition before acting and re-runs its own retire-vs-repoint escalation if a tool has a non-obvious live consumer). Per CLAUDE.md §6 / task 001's escalation trigger, this is surfaced explicitly rather than silently absorbed:

> 🔔 **Human Input Required — r1 P4 did not close row 4**
> - **Situation**: r1's own Track-B audit registered the workspace-tab tool cluster as an operator-decision item deferred to r2 (O-2), not a completed retirement. `GetWorkspaceTabContentHandler`, `UpdateWorkspaceTabHandler`, `CloseWorkspaceTabHandler`, `WorkspaceStateEndpoints`, and `WorkspaceStateService` are all still live in `src/`.
> - **Options**: (a) proceed with task 075 (FR-D-06) as already planned — re-point or retire per its own reference sweep; (b) treat as accept-as-ruled if the operator judges the O-2 keep-list (send_workspace_artifact leg, GetTabsAsync prompt block, workspace/state restore) sufficient and the rest low-priority debt.
> - **Recommendation**: (a) — this is exactly the contingency design §10 row 4 and spec FR-D-06/task 075 were built for; no unbounded scope, the retirement surface (3 tools + endpoints + service) is small and already enumerated by r1's own audit.
> - **Alternative considered**: returning the cleanup to a reopened r1 task — rejected, r1 is formally closed (PR #551/#558, wrap-up task 090 complete); reopening a closed project for a 5-item cleanup is disproportionate versus the already-scoped r2 task 075.

**Starting assumption handed to FR-D-06 / task 075**: the row is NOT closed. Task 075 should proceed directly to its Step 1 reference sweep (it does not need to re-derive that r1 left this open — that determination is made here) and act on the O-2 keep-list as the known "already-ruled-live" leg (`send_workspace_artifact`, `GetTabsAsync` prompt block, `GET /api/workspace/state`).

---

## Row 5 — ADR-040 inline size-cap enforcement home (048 ruling → FR-B-15 / task 064)

**Disposition: verified-closed. r1 already implemented and owns the enforcement home.**

Evidence:
- ADR-040 concise doc (`docs/adr/ADR-040-session-ledger.md`), section "Inline size-cap enforcement (amended 2026-07-08, task 055 per operator ruling 2026-07-07)": *"The size-cap rule is **ENFORCED at the ledger write seam**... Cap: 128 KB (`SessionLedger.InlinePayloadCapBytes`)... Enforced behavior (`SessionLedger.CapInlinePayload`, applied by BOTH Output writers — `OutputRouter` and the gate-resume writer in `TypedHandlerResumeExecutor`)."*
- r1 `tasks/TASK-INDEX.md` task 055 completion note: *"**ADR-040 size-cap ENFORCE** per operator ruling: `SessionLedger.CapInlinePayload` 128 KB + truncation marker at both write seams (OutputRouter + TypedHandlerResumeExecutor), disposition legs fail loud, 3 new boundary tests 16/16."*
- **Live grep in this worktree** confirms the enforcement code is present (grep for `CapInlinePayload`/`InlinePayloadCapBytes`/`IsTruncationMarker` returns 3 hits):
  - `src/server/api/Sprk.Bff.Api/Services/Ai/OutputRouter.cs`
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/TypedHandlerResumeExecutor.cs`
  - `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/SessionLedgerEntries.cs`

Note the design.md row 5 text ("Takes r1's ruling; if r2 → memory/ledger hardening in G-R2-B phase") anticipated r1 might only *rule* on ownership and leave implementation open. The actual outcome is stronger: r1 ruled AND implemented the enforcement point in task 055 (with a truncation-marker fallback, not the deferred blob/pointer offload — that upgrade path is still open but is NOT what row 5 was contingent on).

**Starting assumption handed to FR-B-15 / task 064**: task 064's own POML (`064-adr-040-inline-size-cap-enforcement-home.poml`) already anticipates this exact outcome ("Contingent — may reduce to a no-op if r1 already owns it"). Task 064 should be executed as a **documented verified no-op** citing this note, rather than adding a second enforcement point (which would violate ADR-040's "single enforcement point" constraint and its own Step 0 instruction to check for r1 ownership first). If r2's Memory Service later needs the cap applied to a write path that does NOT route through `OutputRouter`/`TypedHandlerResumeExecutor`, that is new surface for a different task, not this row.

---

## Row 6 — create-task entity: `sprk_event(type=task)` vs `sprk_todo` (048 ruling)

**Disposition: accept-as-ruled.**

Evidence:
- r1 `tasks/TASK-INDEX.md` task 042 note: *"🔔 sprk_event-vs-sprk_todo ruling → gate 048, catalog-data-only to change."*
- r1 `tasks/TASK-INDEX.md` task 048 (G-P3 gate) completion note: *"Rulings: tasks stay sprk_event (revisit r2)..."*
- r1 G-P3 round-4 UAT findings (`g-p3-uat-round4-findings.md:388-391`): the create-task E2E flow produces *"Record created in 'sprk_event' (id …)"* with a working `[Open record]` link, verified live including refresh-persistence.
- r1 G-P3 round-4 UAT findings (`g-p3-uat-round4-findings.md:411-413`): assignee (`sprk_assignedto1`) + regarding (`sprk_regardingmatter`) fields verified live on `sprk_event`, confirming the entity choice is fully wired, not a stub.
- **Live grep in this worktree**: `CreateNotificationNodeExecutor.cs` and the Dataverse `Handlers/` create/update/delete-record handlers reference `sprk_event`; the create-task capability is built on this entity end-to-end.

The r1 ruling is explicit and shipped, not merely proposed — the parenthetical "(revisit r2)" is a standing invitation to re-litigate, not evidence the ruling is unsettled. Design row 6's own disposition text ("Takes r1's ruling; OutcomeCard links target the right entity either way") confirms r2 doesn't need to re-decide — it only needs OutcomeCard link-generation code to target `sprk_event`, which is already the case.

**No dedicated downstream FR gate** — this row is not named in spec's Unresolved Questions as blocking a specific FR-D-0X (unlike rows 4/12); it is closed information that any r2 task touching create-task OutcomeCard links should consume.

---

## Row 8 — office-addins `SseClient` keep-with-reason (048 ruling)

**Disposition: accept-as-ruled (already grep-verified closed by r1).**

Evidence:
- r1 `tasks/TASK-INDEX.md` task 045 completion note: *"ONE SSE path client-wide grep-zero (**1 keep-with-reason: office-addins SseClient** — no @spaarke dep, richer SSE semantics)."*
- r1 `tasks/TASK-INDEX.md` task 048 completion note: *"Rulings: ... SseClient keep."*
- **Live grep in this worktree** confirms the file is still present exactly as ruled: `src/client/office-addins/shared/taskpane/services/SseClient.ts` (consumed by `src/client/office-addins/shared/taskpane/hooks/useSaveFlow.ts`).

This row was already fully dispositioned and grep-verified during r1 itself (task 045's "ONE SSE path client-wide" consolidation explicitly excluded office-addins with a stated reason, then task 048 re-confirmed the ruling at the gate). There is nothing left for r2 to decide or verify — the design's own row-8 disposition ("Accept-as-ruled") is already the settled, evidenced state.

**No dedicated downstream FR gate.**

---

## Row 12 — Playbook/embeddings orphans on spaarkedev1 (FR-D-07 / task 076)

**Disposition: in-scope-FR** (row-level; driven by the residual sub-item below). Note the row bundles two named artifacts with different sub-states: DAILY-BRIEFING-NARRATE is independently **verified-closed**; `spaarke-playbook-embeddings` is the **in-scope-FR** residual (low-severity — zero code consumers, an ops-only deletion action still pending) that keeps the overall row open for task 076.

Evidence:
- r1 Track-B completion audit (`track-b-completion-audit.md` §9, row for `DAILY-BRIEFING-NARRATE`): *"`DAILY-BRIEFING-NARRATE` playbook orphaned on spaarkedev1 | 043 | **RETIRE-data — DONE** | `sprk_analysisplaybook` `7b5a6ed3-0271-f111-ab0e-000d3a13a4cd` "Daily Briefing Narrate": read Active(0/1) → deactivated → re-read **Inactive(1/2)** (old→new shown in transcript)"* — this half of row 12 is grep/read-verified closed (NFR-13 satisfied: state read before and after, confirmed flipped).
- r1 Track-B completion audit, same table: *"Live `spaarke-playbook-embeddings` Azure AI Search index | 035 | **OPERATOR — document only (per task boundary)** | zero code consumers since 035 (writers/readers/drift-job all deleted, grep-zero above)... **Operator action**: delete index `spaarke-playbook-embeddings` on the dev (and any other env) Azure AI Search service; no code change required; safe immediately."*
- r1 Track-B audit §11, item **O-1** (one of the 5 registered operator-decision items): *"`spaarke-playbook-embeddings` Azure index | Delete on dev AI Search service (zero consumers; boundary = operator executes)."*

So: the *code-side* closure for both artifacts is done (grep-zero on all writers/readers/drift-jobs for the index; the playbook row itself is deactivated). What remains outstanding is a **non-code, ops-only action** — physically deleting the now-orphaned Azure AI Search index — which r1 explicitly registered as out-of-task-boundary (task 035's scope was code deletion, not Azure resource deletion) rather than silently leaving unaccounted-for. This is a much lower-severity gap than row 4: no live functional surface depends on it, it was already named and bounded by r1's own audit, and the residual action is a single infra deletion + a final grep-zero/catalog-absence re-verification.

**Starting assumption handed to FR-D-07 / task 076**: task 076 is read-only verification per its own POML ("if an orphan is still present, RECORD it; do not perform the cleanup here"). It should record: DAILY-BRIEFING-NARRATE closed-with-evidence (cite the O-... row above, and re-verify current Inactive state as fresh evidence per its own acceptance criteria); `spaarke-playbook-embeddings` still present as a residual with the disposition recommendation "delete the index on spaarkedev1 AI Search service" (an operator/ops action, not a further code task).

---

## Summary — downstream FR unblock

| FR | Task | Contingent row | Starting assumption from this reconciliation |
|---|---|---|---|
| **FR-D-06** | 075 | Row 4 | NOT closed by r1 (O-2). Proceed directly to reference sweep; retire/re-point per O-2's keep-list baseline (keep `send_workspace_artifact`, `GetTabsAsync` prompt block, `GET /api/workspace/state`; disposition the rest). |
| **FR-D-07** | 076 | Row 12 | DAILY-BRIEFING-NARRATE verified-closed (re-verify Inactive state as fresh evidence); `spaarke-playbook-embeddings` still live as an index — record as residual + recommend ops deletion, no code cleanup task needed. |
| **FR-B-15** | 064 | Row 5 | Already closed by r1 task 055 (`SessionLedger.CapInlinePayload`, single enforcement point at `OutputRouter` + `TypedHandlerResumeExecutor`). Execute as a documented verified no-op; do NOT add a second enforcement point. |

Rows 6 and 8 have no dedicated downstream FR gate in spec.md's Unresolved Questions list — they are accept-as-ruled and require no further r2 action beyond consuming the ruled entity/keep-decision where relevant (create-task OutcomeCard links → `sprk_event`; SSE consolidation stays office-addins-exempt).

---

## Escalation summary (per task 001's trigger)

One row (4) genuinely reveals r1 P4 did not close contingent work, matching the trigger's own named example verbatim. It is surfaced above as a 🔔 Human Input Required block rather than silently folded into "in-scope-FR" — but because design.md §10 and spec.md already pre-built task 075/FR-D-06 for exactly this contingency, the recommended path is to proceed with the already-scoped task, not to expand scope or reopen r1. Row 12's `spaarke-playbook-embeddings` residual is a much smaller, already-registered (O-1) ops-only gap, noted for completeness but not raised as a full escalation — it was already an explicit, bounded, operator-facing action item in r1's own closed gate evidence, not a surprise.
