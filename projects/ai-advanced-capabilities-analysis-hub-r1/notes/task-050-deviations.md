# Task 050 — Entry matrix host routing: deviations + scoping decisions

> Documented per task-execute Step 8 ("Document any deviation"). No `<escalation>` trigger fired —
> the 2b/2d modal REUSES the existing SpaarkeAi mount (no new host abstraction needed; the 21-MUST
> embedded-mode contract is satisfied by the existing `LegalWorkspaceApp`-embedded reference impl),
> and none of the 3 convergent threads revealed a scope-ballooning missing dependency. These are
> scoping/interpretation decisions, mirroring the precedent set by the 030/040/041 deviation notes.

## What was delivered (the four-case host mapping + 3 threads)

**URL-param convention (consumed here; emitted by task 052's ribbon):** the four cases route via one
experience (the three-pane) in two hosts (workspace / code-page modal), discriminated by which params
`main.tsx` reads — NO new URL-param convention (mirrors the shipped `composeMode` read):

| Case | Trigger param(s) | Host | Routing |
|---|---|---|---|
| 2a new-in-workspace | `worktype` (no record ctx) | workspace | `analysis-hub` tab, no forced regarding |
| 2b new-in-record | `worktype` + record ctx (`entityLogicalName`/`entityId`) | modal (openSpaarkeAi target=2) | `analysis-hub` tab, regarding=parent pre-set, cards showing |
| 2c existing-in-workspace | (hub grid row click) | workspace | task-031 reopen (`session_switch`) — unchanged |
| 2d existing-in-record | `analysisId` | modal | resolve bound session → `session_switch`, no cards |

Chain: `main.tsx` (parse) → `App` (props) → `ThreePaneShell` (`AnalysisLaunchContext`) →
`WorkspacePane` (auto-install effect). ADR-039 / §13.3: deterministic CODE path only — no
`surfaceLaunchRegistry` (grep-clean; task 053 enforces).

## 1. Thread 2 (wizard-finish → three-pane execution launch) is COMPLETED BY Thread 1, not by new code

Task 040 scoped `onFinish` to a status-flip + `document-viewer` dispatch (`notes/task-040-deviations.md`
§1). That dispatch was always present — it simply never fired, because the wizard could not render its
`config` without the deep services (it showed the interim "Connecting to workspace services…" branch).
**Thread 1 (host-service injection) makes the entire finish flow live:** wiring
`dataService`/`navigationService`/`searchUsers`/`authenticatedFetch`/`bffBaseUrl` at the SpaarkeAi shell
(`WorkspacePane` widget_load handler, ADR-012-correct layer) lets `onFinish` actually create the
`sprk_analysis` and dispatch `document-viewer` — which IS the "running analysis opens in the three-pane."
No new SPE-pointer resolution or Compose-open was added (see §3) — that would have been the
scope-ballooning dependency the escalation trigger guards against.

## 2. Thread 1 injection lives in WorkspacePane (solution), NOT in the shared-lib hub/wizard

The context-agnostic `create-analysis-wizard` (@spaarke/ai-widgets, ADR-012) MUST NOT construct
Xrm-coupled services itself. The SpaarkeAi SOLUTION is the correct layer — `WorkspacePane`'s
`widget_load` handler merges the host services OVER the dispatcher-supplied `widgetData` (preserving
`workTypeValue` + the 2b `initialAssociation`), reusing the SAME `createXrmDataService` /
`createXrmNavigationService` / `searchUsersAndContacts` factories `ConversationPane` already uses
(DATA-ACCESS-DECISION-CRITERIA: host-context Xrm.WebApi, no BFF/OBO). This covers EVERY dispatcher of
that type (the 2a hub card, the 2b record modal) with one injection point.

## 3. Thread 3 (activeWorkType) — the live connection is the worktype→activeWorkType mapping, not a Compose-open

Task 041 wired the full additive `activeWorkType` plumbing (…→ `ComposeLaunchContext` → `ComposeEditor`
`getToolsForSurface`) but noted no live dispatch site. This task adds the live connection at the ENTRY:
`main.tsx` maps the Agreement Review work-type Choice value (`100000000`) → `activeWorkType='agreement-analysis'`
and threads it through the already-shipped `activeWorkType` chain (an explicit `activeWorkType` param still
wins). A Compose surface opened during an Agreement analysis is therefore palette-scoped.

**Deliberately NOT done (escalation boundary):** making the wizard-finish OPEN the created document in
Compose would require resolving the `sprk_document` → SPE pointer (`speDriveItemId`/`speDriveId`) — a
genuine missing dependency (the wizard has only the `documentId`; `/documents/upload` returns no SPE
pointer). Per task 041's own §3 reasoning and this task's escalation guidance, a speculative Compose-open
with no consumer was avoided; the current flow opens `document-viewer` (which needs no SPE pointer).

## 4. 2b regarding pre-set falls out of entityContext presence — no cross-wiring

`AnalysisHubWidget` reads `entityContext` (via `useAiSession`) and threads `initialAssociation` to the
wizard ONLY when a record context is present AND its entity type is a supported regarding target
(Matter/Project/Document). In 2a `entityContext` is null → no `initialAssociation` → empty regarding.
So "only 2b pre-sets regarding=parent" is structural, not a branch. `entityContext` carries no display
name, so `recordName` uses the record GUID (best-effort; the wizard shows it pre-selected and editable) —
a display-name resolution is a documented follow-up.

## 5. 2d restores the transcript (session_switch); file-tab restore is a documented boundary

The 2d existing-analysis modal resolves the bound session via the task-031
`GET /ai/chat/sessions/by-analysis/{id}` endpoint and dispatches `conversation.session_switch` (reusing
the hub's exact endpoint + event contract). It does NOT additionally dispatch a `document-viewer` load,
because that endpoint returns no document pointer — the hub's grid reopen (2c) can load the file only
because it has the grid row. Full 2d file restore would need a separate Dataverse read of the analysis's
`sprk_documentid`; that is owned by the session-binding tasks (020–025) and the session's own
tab-persistence. A 404 (no session ever bound) is a graceful no-op — never mints an empty session
(mirrors task 031's escalation contract).

## 6. launch-resolver.ts NOT modified (params are consumed in main.tsx; emitters are task 052)

The POML lists `launch-resolver.ts` as an optional output ("if a case needs a context-pre-set param").
The `analysisId`/`worktype`/`regarding` param EMITTERS on `openSpaarkeAi`/`buildLaunchUrl` are explicitly
task 052's deliverable (`052-extend-openspaarkeai-ribbon-launcher.poml`). This task only CONSUMES them
(reads the URL directly in `main.tsx`), so `launch-resolver.ts` was left untouched — no divergence from
the `composeMode` precedent, no premature edit of the shared primitive 052 owns.

## Tests

- `AnalysisHubWidget.regarding-preset.test.tsx` (NEW): 2b Matter/Project pre-set; 2a no-context omit;
  unsupported-entity omit; dark-mode. The existing `AnalysisHubWidget.test.tsx` 2a dispatch assertion
  is unchanged (no entityContext → identical payload).
- `WorkspacePane.analysis-entry.test.tsx` (NEW): mode='new' → `analysis-hub` auto-install; mode='existing'
  → by-analysis lookup + `session_switch` (+ no hub cards); Thread-1 injection preserves `workTypeValue`.
- 6 existing `WorkspacePane.*` test ThreePaneShell mocks gained `useAnalysisLaunch: () => null` (WorkspacePane
  now consumes it; ConversationPane tests are unaffected — ConversationPane does not call it).

## Gates + build

- SpaarkeAi `typecheck` (surface-gate): surface-owned 0 errors. AI.Widgets `tsc --noEmit`: clean.
- Jest: all new suites pass; all `WorkspacePane` (12 suites / 30) + `AnalysisHubWidget` (2 suites / 12) pass.
  Full-suite runs surfaced two PRE-EXISTING failures unrelated to this task:
  `register-workspace-widgets.test.ts:379` (communications displayName 'Messages' vs test's 'Communications'
  drift — file untouched by 050) and `HardSlashExecutor.test.ts:379` (a `<100ms` timing flake — passes 43/43
  in isolation).
- No BFF touched → publish-size / CVE N/A (client-only). ADR-039 grep-clean; ADR-021/012/028/030 compliant.
