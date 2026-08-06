# Task 054 — Deploy Report (R1 → dev)

> **Status**: COMPLETE (dev). Target environment = **dev (spaarkedev1 / spaarke-bff-dev)** per the R1 standing pattern; prod promotion is a separate owner-gated step, NOT in this project's scope.
> **Owner ruling (2026-07-23)**: "if 054 is a deploy we don't have to do that again." The three R1 surfaces were already deployed + smoke-verified to dev during this session's UAT-remediation batches — 054 formalizes those deploys rather than re-running them.

## What 054 requires vs. what shipped

054 acceptance criteria (BFF /healthz green · SpaarkeAi surface loads with drop-down + SNS · new Dataverse columns + Binding rows present · each entry path smoke-green) were satisfied by the deploys already performed this session. No deployable delta remained at 054 time:

- **Task 051 (evals) added TEST-ONLY files** (`tests/integration/contract/Eval/assistant-r1-eval-cases.json`, `AssistantEnhancementsR1EvalTests.cs`). Tests are not part of the BFF publish artifact or the code-page bundle — **no re-deploy triggered by 051**.

## Deployed surfaces (all dev, verified this session)

| Surface | Artifact | Verification | Date |
|---|---|---|---|
| **BFF** | `spaarke-bff-dev` (Azure App Service) | Hash-verified publish + `/healthz` 200; new `POST /api/notifications/{id}/dismiss` live (401 unauth); History endpoint live; notification idempotency fix live. Publish size 44.89 MB excl. PDBs / 48.80 MB incl. PDBs (< 60 MB ceiling — task 053). | 2026-07-22 |
| **SpaarkeAi code page** | `sprk_spaarkeai` web resource (PATCH + PublishXml) | Loads with the tool drop-down + Quick Start modal + SNS suggestion cards (collapsed banner + dismiss 'x'); `[action:]` label-strip live; registry surface routing live. | 2026-07-22 |
| **Dataverse catalog + columns** | `sprk_playbookconsumer` / `sprk_analysisaction` rows; `sprk_userprofile` columns; `sprk_requiresnoattachedrecord` grounding column; grid config `ac05e4f1` | Present on spaarkedev1. R1 Bindings: create-matter/create-task/create-todo (surface_launch), create-project, **list-tasks** (surface_launch), draft-correspondence, etc. VIEW-vs-CREATE cue authored on list-tasks/create-task/create-todo toolDescriptions. `Notifications__Suggestions__Enabled=true`. | 2026-07-22 |

## Entry-path smoke (dev, this session)

- **Text path** — chat dispatch → surface_launch (create-matter/create-task/create-todo/list-tasks) verified opening the pre-seeded wizard / OOB form / My Tasks grid tab.
- **Click path** — suggestion cards + chips (dismiss, open-record, create-task next-step) verified.
- **Event path** — Daily-Briefing proactive suggestions rendered via the notification spine (poll fallback; SignalR not provisioned on dev — non-blocking, documented follow-up).

## Escalation trigger check (054 POML)

> "If any smoke test fails post-deploy, STOP and escalate."

No smoke-test failure. Open follow-ups (non-blocking, owner-facing, do NOT gate 090): (a) "add a task" captureMode Loop-vs-Modal owner decision; (b) SignalR provisioning for live push (poll fallback works); (c) R4-7/R4-9 owner repros.

## Prod promotion (out of scope, noted for the owner)

The new `sprk_userprofile` columns + R1 Binding/Action rows + grounding column promote dev→prod via solution management when the owner schedules the prod cutover. Mirror-parity note: create-todo / create-project Bindings are live on spaarkedev1 but not yet in `infra/dataverse/sprk_playbookconsumer-rows.json` (list-tasks IS mirrored) — add them to the mirror before/with the prod promotion for repeatable seeding.
