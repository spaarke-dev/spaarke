# Phase 2 Wave A — Deployment Evidence

> **Date**: 2026-06-04
> **Operator**: ralph.schroeder@spaarke.com
> **Environment**: SPAARKE DEV 1 (`spaarkedev1.crm.dynamics.com`; subscription `484bc857-3802-427f-9ea5-ca47b43db0f0`)
> **Tasks**: 010 + 011 + 013 + 016 + 019 (5 tasks, 4 parallel sub-agents)

## Dataverse deploys (Spaarke Dev 1)

### Action `SUM-CHAT@v1` — task 010 (D2-01)

```
sprk_analysisactions row:
  sprk_analysisactionid:  eeb05bfd-1260-f111-ab0b-70a8a59455f4
  sprk_actioncode:        SUM-CHAT@v1
  sprk_name:              Summarize Document for Chat
  sprk_actiontypeid (FK): 3a8daf85-36da-f011-8406-7ced8d1dc988 (Summarize action type)
  sprk_systemprompt:      <populated; JPS-formatted, ~2.7 KB>
  sprk_outputschemajson:  <populated; JSON Schema strict; tldr → summary → keywords → entities>
  sprk_description:       <populated>
```

Deployed via NEW companion script `scripts/Deploy-AnalysisAction.ps1` (50 LOC). The action upsert wasn't part of the existing `Deploy-Playbook.ps1`'s flow (it resolves FKs but doesn't upsert actions), so a focused sibling script was the cleanest path per R5 §3.1 reuse mandate. Deploy-Playbook.ps1 unchanged.

Idempotency verified: 2nd run PATCHed (vs initial POST). Evidence: see deploy log output above commit.

### Playbook `summarize-document-for-chat@v1` — task 011 (D2-02)

```
sprk_analysisplaybook row:
  id:    44285d15-1360-f111-ab0b-70a8a59455f4
  name:  summarize-document-for-chat@v1
  type:  AiAnalysis (sprk_playbooktype=0)
  isSystemPlaybook: true

sprk_playbooknode row (1 node):
  id:         a7225b13-1360-f111-ab0b-7c1e521b425f
  name:       summarize
  nodeType:   AIAnalysis (100000000)
  actionId:   eeb05bfd-1260-f111-ab0b-70a8a59455f4 (SUM-CHAT@v1)
```

Deployed via existing `scripts/Deploy-Playbook.ps1` (unchanged). Idempotency verified: 2nd run skips with "Playbook already exists. Use -Force to delete and recreate."

## Frontend code (tasks 013, 016, 019)

| Task | Change | Build result |
|---|---|---|
| 013 D2-08 | `RichFilePreview` extracted from `RichFilePreviewDialog` (895 → 215 LOC dialog wrapper; 585 LOC new renderer; 4 known consumers verified unaffected) | `@spaarke/ui-components` builds clean |
| 016 D2-06 | 5 additive event types added within existing 4 channels (3 to WorkspacePaneEvent, 2 to ContextPaneEvent); zero edits to PaneEventBus.ts or PaneChannel | `@spaarke/ai-widgets` builds clean |
| 019 D2-10 | `/summarize` description + tri-mode router (`routeSummarizeIntent`); branch (c) fully behavioral via `predefinedPrompts` chip merge; branches (a)/(b) scaffolded with TODO markers pending task 020 wiring | SpaarkeAi solution: 0 errors in ConversationPane.tsx; all errors pre-existing in other files |

Frontend build order: `@spaarke/ai-outputs` → `@spaarke/auth` → `@spaarke/ai-context` → `@spaarke/ai-widgets`. (Pre-existing workspace dep order; not introduced by R5.)

## BFF publish-size impact

**0 MB** for this wave. Zero `.cs` files in `src/server/api/Sprk.Bff.Api/` were touched (data deploys + frontend changes only). Confirmed via `git diff --stat origin/master..HEAD` on this branch.

## Verification matrix

| Acceptance criterion | Status |
|---|---|
| Action visible in Spaarke Dev (010) | ✅ row id `eeb05bfd...` |
| Playbook visible in Spaarke Dev (011) | ✅ row id `44285d15...` |
| Idempotent deploys (010 + 011) | ✅ POST→PATCH on re-run for action; skip on re-run for playbook |
| Schema declaration order tldr→summary→keywords→entities preserved | ✅ verified inline in `sprk_outputschemajson` |
| `RichFilePreview` extracted (not rebuilt) (013) | ✅ diff shows lines moved, not duplicated |
| 4 known consumers of `RichFilePreviewDialog` unaffected (013) | ✅ all use unchanged `IFilePreviewDialogProps` |
| 5 new PaneEventBus event types within existing 4 channels (016) | ✅ no new channel; exhaustive-switch audit clean |
| ADR-030 channel-count = 4 maintained (016) | ✅ |
| `/summarize` description = "Summarize uploaded files or the active document" (019) | ✅ exact string |
| Tri-mode routing (019) | ✅ branch (c) behavioral; (a)/(b) scaffolded with TODOs |
| BFF publish-size delta = 0 MB | ✅ no BFF .cs changed |

## Coordination notes for downstream tasks

- **Task 012 (SessionSummarizeOrchestrator)**: action GUID `eeb05bfd-1260-f111-ab0b-70a8a59455f4` and playbook GUID `44285d15-1360-f111-ab0b-70a8a59455f4` are the deployed targets; orchestrator resolves by `actionCode='SUM-CHAT@v1'` and playbook name `summarize-document-for-chat@v1`.
- **Task 014 (POST /summarize endpoint)** + **Task 015 (InvokeSummarizePlaybookTool)**: will pick up the TODO markers task 019 left in `ConversationPane.tsx` (`TODO(r5/task-020)`) and `dispatchSummarizeIntent`.
- **Task 017 (StructuredOutputStreamWidget)**: will subscribe to the new `workspace.streaming_started/field_delta/streaming_complete` event types from task 016.
- **Task 018 (FilePreviewContextWidget)**: will consume the extracted `RichFilePreview` from task 013 + dispatch `context.file_selected` (task 016).
- **Task 022 (DocumentViewerWidget upgrade)**: will consume `RichFilePreview` from task 013.

## New script artifact

`scripts/Deploy-AnalysisAction.ps1` — companion to `Deploy-Playbook.ps1`. Upserts `sprk_analysisaction` records from a JSON definition file's `actions[]` array. Idempotent (GET-then-POST-or-PATCH by `sprk_actioncode`). Uses navigation property name `sprk_ActionTypeId@odata.bind` (Pascal case per Dataverse OData metadata, NOT the lowercase `sprk_actiontypeid`).

Future R5 playbooks (e.g., D3-01 `/analyze` proof point, task 040) that introduce new actions should use the same sibling-script pattern.
