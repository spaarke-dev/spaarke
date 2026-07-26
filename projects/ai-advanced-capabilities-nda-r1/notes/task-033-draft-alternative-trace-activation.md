# Task 033 — Draft Alternative activation + execution-trace flow

**Status**: complete (verification-only, zero production files changed) · **Rigor**: FULL

## Finding: the requested mechanism already shipped, before this project's dependency landed

The task asked to "resolve the `compose-draft-alternative` `bindingId:''` stub to the real
binding" in `ComposeAiToolbar.tsx` (~:324), via the same no-hardcoded-GUID capability-discovery
mechanism task 022 used. Investigation found this was **already built and shipped** by the prior
merged `spaarkeai-compose-r2` project (git `83dfd3067` "feat(compose-r2): AI-action toolbar
activation wiring (task 048...)"), plus `compose-r4` tasks 040/041/064 for the apply-side +
trace pieces — all landed on `master` before this NDA project's task 022 even ran. There was
nothing left to wire; making `ComposeAiToolbar.tsx`'s stub a hardcoded GUID would have been a
regression against the file's own documented design, not a fix.

## The mechanism, verified piece by piece

### 1. bindingId resolution — `useComposeToolbarActivation` (compose-package-local sibling of task 022's `useCapabilityDiscovery`)

`src/client/shared/Spaarke.Compose.Components/src/widgets/useComposeToolbarActivation.ts`
fetches `GET /api/ai/capabilities?surface=compose` (the SAME `CapabilityDiscoveryEndpoints.cs`
read task 022 used) and, for every returned capability whose `consumerType` matches a
`DEFAULT_ACTIONS` id, calls `registerComposeAiToolbarAction({ ...match, bindingId: capability.bindingId })`
— filling in the real deployed GUID and leaving label/tooltip/placement untouched. No hardcoded
GUID, no new endpoint, no consumer→GUID resolver (ADR-039). A non-matching capability (e.g. the
whole-document `compose-summarize` binding) is skipped, never appended.

The hook is wired into BOTH known Compose mount hosts:
- `src/solutions/SpaarkeAi/src/components/workspace/ComposeDirectWidget.tsx` (line 166) — the
  Direct `widgetType: 'compose'` door. This is the door the NDA flow's
  `mountFileInCompose(fileId, fileName)` (task 022) opens a classified file through.
- `src/solutions/LegalWorkspace/src/sections/composeEditor.registration.ts` (line 153) — the
  layout-door section shim.

`ComposeAiToolbar.tsx`'s `DEFAULT_ACTIONS` array ships `bindingId: ''` for `compose-draft-alternative`
BY DESIGN (see the file's own "PHASE-4 STUB BOUNDARY" doc comment, ~:36) — the button renders
disabled until `registerComposeAiToolbarAction` overwrites it at runtime, which the activation
hook above already does on every Compose mount. Editing the stub to a literal GUID would violate
both the task's own "no hardcoded GUID" constraint and this established pattern.

### 2. The Binding row is NOT missing — already seeded, live

`infra/dataverse/sprk_playbookconsumer-rows.json` (a live export, `$source:
https://spaarkedev1.crm.dynamics.com`, `$exported: 2026-07-07`) already carries:

```json
{
  "consumerType": "compose-draft-alternative",
  "actionCode": "compose-draft-alternative",
  "disposition": 100000006,          // ComposeDisposition.Compose
  "risk": 100000000,
  "surfaces": "workspace,compose",
  "toolDescription": "Propose an alternative rewrite of this clause, grounded in matter/firm playbook and precedent context where available. Produces a pending track-change ... does not persist the edit until accepted."
}
```

`ConsumerRoutingService.ListTextProjectableBindingsAsync` filters on non-empty `toolDescription`
(present) then on the requested surface (`"compose"` ⊂ `"workspace,compose"`) — so
`GET /api/ai/capabilities?surface=compose` returns this row and the activation hook registers it.
No binding-row edit was needed (the task's own fallback clause — "+ binding row json if a row is
genuinely missing" — did not apply).

### 3. Execution trace surfaces during a rewrite — already auto-opens on Compose-tab activation

`ContextPaneController.tsx` (~:566-576) auto-selects `execution-trace` as the Context-pane tool
whenever a `tab_change` event's `widgetData` carries `widgetType === 'compose'` OR
`layoutName === 'Compose'` — a one-shot select-on-activate (a later manual pick is not forced
back). This is not gated to any one action; it fires whenever the user is in Compose, so it
surfaces before/during ANY toolbar dispatch, including Draft Alternative. The hosted view is the
core `ExecutionTraceWidget` (`@spaarke/ai-widgets`), bound via `ComposeTraceHost.tsx` to the
active `chatSessionId` + a `restoreTrace` reader that GETs
`/api/ai/chat/sessions/{id}/trace` (`ISessionTraceReader`, ADR-040 ledger) — read-only,
audit-only, no local trace-rendering invented for Compose (project charter §3.4).

Covered by `ContextPaneController.compose-trace-autoopen.test.tsx` (4 tests: default state, the
`workspace`-wrapped compose seed, the DIRECT `compose` widgetType, the `layoutName==='Compose'`
discriminant, and the negative case for a non-Compose tab) — all passing.

### 4. Per-section, not batch-applied

`compose-draft-alternative` carries `materializesInEditor: true` (DEF-09) in
`ComposeAiToolbar.tsx`'s `DEFAULT_ACTIONS`. On click, the toolbar reads ONLY the current
selection's text (`editor.state.doc.textBetween(from, to)`) and dispatches ONE request; the
Action's output schema (`compose-draft-alternative.action.json`) enforces "Emit ONE payload (one
target_text/new_text pair). Do not propose multiple disjoint edits in a single response." The
dispatch is routed to the editor's own DOCUMENT session (not the chat session) so the
redline-materialize read coincides with the write, and materializes as ONE pending track-change
the attorney accepts/rejects/undoes (FR-17) — never an auto-applied, document-wide edit. Forcing-
tested end-to-end (real dispatch, real session-keyed in-memory ledger, not mocked away) in
`ConversationPane.compose-draft-alternative-session-routing.e2e.test.tsx` — both the
`materializesInEditor` routing case and the informational-action regression guard pass.

## Verification performed this task (no code changed — read + test only)

- `npx tsc --noEmit` in `Spaarke.Compose.Components` — clean, exit 0.
- `npx jest useComposeToolbarActivation.test.tsx ComposeAiToolbar.test.tsx ComposeWorkspace.redline-from-ledger.test.tsx` (same package) — 3 suites / 28 tests, all passing.
- `npx jest ConversationPane.compose-draft-alternative-session-routing.e2e.test.tsx ContextPaneController.compose-trace-autoopen.test.tsx ComposeTraceHost.reachable.test.tsx` (SpaarkeAi) — 3 suites / 10 tests, all passing.

## §10 BFF Hygiene

- **Placement Justification**: N/A — zero BFF (`Sprk.Bff.Api`) files touched or needed; the
  capability-discovery endpoint and the session-trace read endpoint this task relies on already
  exist and were unmodified.
- **Publish size**: unchanged.
- **Hot-path**: BFF touched = **NO**. SpaarkeAi = **NO** (no files under
  `src/solutions/SpaarkeAi/**` or `Sprk.Bff.Api` were edited — only shared-lib and solution files
  were READ for verification).

## Files changed

- `projects/ai-advanced-capabilities-nda-r1/tasks/033-draft-alternative-trace-activation.poml` —
  status → completed, completion-finding notes appended.
- `projects/ai-advanced-capabilities-nda-r1/tasks/TASK-INDEX.md` — 033 → ✅.
- `projects/ai-advanced-capabilities-nda-r1/current-task.md` — task transition.
- `projects/ai-advanced-capabilities-nda-r1/notes/task-033-draft-alternative-trace-activation.md` (this file) — new.

**No files under `src/`, `infra/`, `.claude/`, or `tests/` were modified.** `ComposeAiToolbar.tsx`
was read but not edited — its `bindingId: ''` stub is correct as authored (see Finding above).
The Binding row json was read but not edited — the row is not missing.

## Live/env-blocked steps

- Manual live-UI verification (open NDA-REVIEW → Compose → select a flagged clause → Draft
  Alternative → verify a standards-based rewrite returns and applies per-section, with the
  Execution Trace panel visible) requires a live org + deployed client — env-blocked in this
  session (no live Dataverse credentials), flagged per project convention, NOT faked. Automated
  coverage instead: the 6 test suites listed above (38 tests total) exercise the real
  registration → dispatch → session-routing → trace-autoopen chain end-to-end with only the
  true network boundary (fetch/SSE) mocked.

## Follow-ons for dependent tasks

- **032** (right-gutter comment layout, after 040) is unaffected by this task — different files
  (comment-thread layout, not the AI toolbar).
- No new follow-on risk identified: the activation mechanism this task verified is shared
  infrastructure already exercised by 4 of the 5 toolbar actions (explain, compare,
  defined-terms already covered by `useComposeToolbarActivation.test.tsx`'s positive case; this
  task closes the loop specifically for `compose-draft-alternative`, the one edit-producing
  action).
