# Task 136 — Create Analysis from the hub: 404 at Finish

> Owner checklist card r1 (artifact 3FPj4oT83f25v2r8yutkMV, spaarkedev1, 2026-10-08 ~14:10Z): "NDA analysis did not run;
> A record this analysis is filed under was not found. The analysis was not saved." Console: 404.
> Investigation and fix: 2026-10-09, branch `fix/136-create-analysis-hub-404` (worktree `C:\wts-136`, off `origin/master` e9f08b764).

## 1. Root cause (proven)

**The G5 AppendTo check refuses every lookup to an organization-owned table, and the Create Analysis wizard always binds one.**

1. `CreateAnalysisWizardWidget.onFinish` (`src/client/shared/Spaarke.AI.Widgets/.../CreateAnalysisWizardWidget.tsx`) posts the
   `sprk_analysis` payload to `POST /api/v1/child-records/sprk_analysis` (uac-r2 task 147, #1312, merged 2026-10-06). The payload
   binds `sprk_documentid`, the assignee `contact`s and **`sprk_AgreementType`** — the wizard pre-selects the registry's fallback
   row ("General", `sprk_isfallback = true`), so every Finish binds an agreement type. Launched with an association it also binds
   `sprk_RegardingRecordType` → `sprk_recordtype_ref` (ADR-024 `applyResolverFields`).
2. `ChildRecordEndpoints.CreateAsync` → `OwnedChildWrite.CreateAsync` → `CheckCallerMayCreateAsync` → `CheckCallerMayAppendToAsync`
   asks `RetrievePrincipalAccess` (as the caller) for AppendTo on **every** bound record.
3. `sprk_agreementtype` (and `sprk_recordtype_ref`, `transactioncurrency`) is **OrganizationOwned**; Dataverse refuses
   RetrievePrincipalAccess for such a table: `400 0x80040800 "The 'RetrievePrincipalAccess' method does not support entities of
   type 'sprk_agreementtype'"`. `RightsOnAsync` maps a non-success to `AccessRights.None` → `ParentUnavailable = true` → the
   uniform 404 `child_record.not_found` "A record this analysis is filed under was not found. The analysis was not saved."

It is a regression from #1312: the agreement-type bind dates from 2026-07-31 (`1e1a6579b`) and worked while the wizard created
the row as the user through `Xrm.WebApi`.

### Evidence

- **Telemetry** (Log Analytics workspace `spe-logs-dev-67e2xz` / `74b7349a-…`; the App Insights component query API returned only
  today's data, the workspace holds the history): 4 × `POST /api/v1/child-records/sprk_analysis` → 404, at 14:07:57, 14:09:52,
  15:29:05, 15:29:14Z on 2026-10-08, caller systemuser `1d02f31c-…` (the owner). Each operation: mapper metadata reads for
  `sprk_document`, `contact`, `sprk_agreementtype` (no `sprk_matter`/`sprk_project` — no association); then RetrievePrincipalAccess
  200, 200, **400**; then `[CHILD-RECORD] create of sprk_analysis refused: denied=True parentUnavailable=True`. No `sprk_analysis`
  create through the route has ever succeeded (30 days: 4 attempts, 4 refusals).
- **Reproduction** (read-only GET as the owner): `RetrievePrincipalAccess` on `sprk_agreementtypes(d557c894-…)` (NDA) and on
  `(433e1688-…)` (General) → the 400 above; on a `sprk_document` → 200 with AppendTo.
- **Live metadata**: `sprk_agreementtype` `OwnershipType = OrganizationOwned`; RPA also refuses `sprk_recordtype_ref`,
  `transactioncurrency`, `systemuser`, `team`, `businessunit` (all 400 0x80040800).
- The deployed dev BFF does not carry the fix: after the 400 the trace goes straight to the refusal, with no
  `EntityDefinitions(...)?$select=…OwnershipType…` read.

### The fix already existed — PR #1391

uac-r2's **#1391** (`fix/g5-appendto-org-owned-reference`, opened 2026-10-08T00:52Z, CI green, no review decision, 239 commits
behind master at 2026-10-09) is exactly this fix: when RPA does not grant AppendTo, `CallerMayAppendToOrganizationOwnedAsync`
reads the target's metadata and, **only for an OrganizationOwned table**, asks the caller's AppendTo *privilege* (by the name
the metadata declares) and reads the row as the caller (missing row = same uniform 404). Anything else still fails closed. Per
D-67/D-68 it merges after uac-r2 approves. Task 136 does not duplicate it: the task branch **merges #1391 unchanged** onto current
master and adds the task-136 delta (§3), so the PR shrinks to that delta once #1391 merges.

## 2. Contract decision: is an analysis without a filing parent legitimate?

Yes — and the hub analysis is not parentless anyway. Associate-To is a skippable step (Skip in the wizard), and
`OwnedChildWrite.CreateAsync` owns an unfiled row by the caller's business-unit team (owner round 5). Moreover `sprk_documentid`
targets `sprk_document`, an **ownership parent**, so the resolver files every wizard analysis under the document it reviews
(test `ChildCreate_TheCreateAnalysisWizardsHubPayload_NoAssociation_...` asserts the document's BU team). The server accepts it;
the wizard needs no new requirement.

## 3. What task 136 changes

| File | Change |
|---|---|
| (merged) `OwnedChildWrite.cs` + 2 test files | #1391, unchanged |
| `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.CreateAnalysisWizard.cs` (new) | The wizard's real payloads through the real route: hub (no association) → 201; filed under a matter with `sprk_RegardingRecordType` → 201; without `prvAppendTo` on agreement types → uniform 404, nothing created |
| `SecureChildOwnershipAiToolTests.cs` (fixture) | `sprk_agreementtype` entity set, OrganizationOwned, held AppendTo; the live `sprk_analysis` lookups (navigation properties read from spaarkedev1 2026-10-09: `sprk_documentid`, `sprk_AssignedAttorney1`, `sprk_AssignedParalegal1`, `sprk_AgreementType`, `sprk_RegardingRecordType`) |
| `ChildRecordEndpoints.cs` | The create / re-file refusal log now carries `reason={Reason}` (the `Denied` text, which names the refused lookup's TABLE, never an id). The uniform 404 hides it from the caller by design, so the log was the only place to trace it, and it carried only booleans |
| `CreateAnalysisWizardWidget.tsx` + its test | A Finish retried after a failed analysis create reuses the document the earlier attempt uploaded (keyed by the uploaded files' ids) instead of uploading again and creating a second `sprk_document` + Document Profile run per retry |

### Fail-on-old proof

- BFF: with master's `OwnedChildWrite.cs` swapped in, the two positive wizard tests fail with
  `404 "A record this analysis is filed under was not found. The analysis was not saved."` (the owner's message); with the fix
  they pass (201). The negative test passes on both.
- Client: with the old widget, the retry test fails (`uploadFilesWithoutRecord` called 2 times, expected 1).

## 4. Entry points (all reach the same `onFinish`)

| Entry point | Path | Result |
|---|---|---|
| Console hub Quick Start "Agreement Review" card (r1, the canary) | `QuickStartModal` → `open_create_analysis_wizard` → `WorkspacePane` modal host | Same fault (agreement-type bind) — proven by telemetry; fixed by #1391, covered by the hub-payload test |
| Matter / Project / Document form ribbon "New Analysis" | `sprk_spaarkeai_analysisrecordlaunch.openNewAnalysisFromRecord` → Console with entity context → hub → same modal with `initialAssociation` | Same fault, twice over (agreement type + `sprk_recordtype_ref`); covered by the matter-payload test |
| Console opened in a record context (Matter/Project/Document) | `WorkspacePane.analysisInitialAssociation` → same modal | Same as above |
| Chat / Assistant (`ConversationPane` dispatch, `widget_load` of `create-analysis-wizard`) | Same widget as a workspace tab | Same fault; same fix |
| Agreement-type write after the fact (`agreementTypeLookupWrite.ts`, `PATCH /child-records/sprk_analysis/{id}`) | `RefileAsync` checks AppendTo only on ownership parents | Not affected (3 to-do re-files binding `sprk_recordtype_ref` succeeded live) |
| Playbook `analysisService.ts` | Its own AI route, not child-records | Not affected |

Wider blast radius (same root cause, same fix): **every** child-record create through `POST /api/v1/child-records/*` that binds an
organization-owned row — every to-do / event / memo / report card / analysis created *filed under a record* (resolver binds
`sprk_recordtype_ref`) and any invoice / document / budget / billing event binding `transactioncurrency`. In the 30 days of
telemetry only unfiled creates were attempted, which is why only the analysis surfaced.

## 5. Other defects found (no parking — reported to the main session)

1. **Role gap (needs owner approval — role edit):** `prvAppendTosprk_RecordType_Ref` is held only by System Administrator, System
   Customizer, Service Writer and Spaarke Access Administrator — **not Spaarke Basic User**. After #1391, a non-admin user who
   creates any child FILED UNDER a record (to-do, event, memo, analysis from a matter…) still gets this 404, correctly (a
   run-as-user create would be refused too). `prvAppendTosprk_AgreementType` is held by Spaarke Basic User (Global). The owner
   holds System Administrator, so card r1 passes after deploy regardless.
2. **Orphan document on a failed Finish (fixed here for retries):** the 14:07Z attempt left `sprk_document`
   `2cf67ed0-207b-4c58-a43d-11d8a7c9a424` (`nda_sample.pdf`) with its Document Profile, and no analysis. A retry no longer
   duplicates it; an abandoned wizard still leaves the uploaded document (a valid standalone document — not deleted).
3. **#1391 is 239 commits behind master** with CI from its old base (gotcha 2026-10-08, #1418). This branch re-runs it on the
   current base.
4. App Insights component query API (`az monitor app-insights query --app 6a76b012-…`) returns only the last ~hour of data while
   the backing workspace holds 90 days — the project gotcha's appId recipe silently finds nothing for older windows. Query the
   workspace (`az monitor log-analytics query -w 74b7349a-f88d-45a7-b180-8728807a85d7`) instead.
5. A `[DELEGATION-RPA-UNAVAILABLE]` 400 on `sprk_noaccessentries` (2026-10-06 18:06Z) is the same RPA limitation in another
   path; it is **already fixed on master** (`DelegationRuleFilter` asks the table Write privilege for the org-owned entry).

## 6. Live proof

Needs a BFF deploy to spaarkedev1 (master + #1391 + this PR) and a Console deploy for the retry fix; then the owner re-runs card
r1 (one test analysis created through the UI, per the POML). Not done here (no deploy, no dev writes).
