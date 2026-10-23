# Task 159 notes: Decide-lane "Assign Task" (D-178)

Lane: implementation, branch `task/159-assign-task`, stopped before POML step 3 (merge, R3 deploy, policy apply, dev proof).

## Rigor and placement

- Rigor FULL (POML). Model sonnet @ high.
- **Placement (bff-extensions.md section A):** in the BFF. Pure orchestration inside the existing decision-commit route; no endpoint, no background work, no package, no new DI module (one `AddScoped<IDecisionActionExecutor, ...>` line beside its siblings in `SignalsModule`). ADR-001/002/008/010/013 considered; no `Services/Ai` reference from Signals (diff grepped); no new CRUD to AI dependency.
- **Component justification (section 11):** Existing: `assign-work`, `CreateEventExecutor` (untyped Next step), `TaskActionCore` (AI-pipeline internal). Extension: one more `ChildCreateExecutorBase` subclass and one catalog row. Cost of doing nothing: the no-budget decision keeps creating a Work Assignment, which the owner rejected.
- **Config boundary (section G):** not applicable (no playbook config field). The new policy version is policy data in `config/know-promotion.policies.json`, applied by the existing script.

## Escalation triggers (both clear)

1. `sprk_event.sprk_assignedto` on spaarkedev1 is a Lookup with Targets `["contact"]` (metadata read 2026-10-22). The Task row `124f5fc9-98ff-f011-8406-7c1e525abd8b` exists on dev with name "Task".
2. Nothing keys D-51's Know-promotion or suppression on the code `assign-work`. Non-test code that names the string: the catalog row and executor, `Apply-DoLanePolicies.ps1` (lint list, fixed), and wizard / quick-start UI ids (see the keep/fix list). The nightly job, evaluator and suppression read the policy's plan generically.

## What changed

| File | Change |
|---|---|
| `Services/Signals/Actions/DecisionActionCatalog.cs` | New row `assign-task` (Decide, not a Next step, Judgement, ResolvesSignal, Gate Tier3; params assignee Person required, description TextArea required, dueDate Date optional). |
| `Services/Signals/Actions/DecisionActionExecutors.cs` | `AssignTaskExecutor : ChildCreateExecutorBase` (table `sprk_event`, create privilege = the event one). `DecisionExec.TaskEventTypeId` (the one Signals copy of the Task row id), `DecisionExec.NameFromDescription` (first line, 200 chars; `AssignWorkExecutor` now uses it, behaviour unchanged). Preflight also asks AppendTo on the Task type row (see finding 2). |
| `Infrastructure/DI/SignalsModule.cs` | One registration line. |
| `config/know-promotion.policies.json` | POL-MATTER-NOBUDGET: v2 added (`...6312`), `currentVersion` 2, v1 untouched. |
| `scripts/Apply-DoLanePolicies.ps1` | `assign-task` added to the Decide-lane code list of the lint (without it the lint refuses the v2 plan). Fix round 1: ends the superseded version (`sprk_inforceto`) before moving the pointer; see finding 1. |
| `src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Core User.xml` | Fix round 1: `prvAppendTosprk_EventType_Ref` at `Global`, matching the dev grant. |
| `tests/scripts/Apply-DoLanePolicies.Tests.ps1` | Fix round 1: Pester tests for the end-stamp rules and the lint. |
| Tests | Executor, catalog, gate tier, KnowPromotion definition, admin catalog endpoint list. |

Effect line has no trailing full stop, matching every other catalog effect line (the POML quote ends in a full stop; it reads as sentence punctuation).

Column set written (as the caller, through the child-records core): `sprk_eventname` (first line of the description, at most 200 chars), `sprk_description`, `sprk_duedate` only when given, `statuscode` 659490001 (Open), `sprk_eventtype_ref@odata.bind` = `/sprk_eventtype_refs(<Task>)`, `sprk_assignedto@odata.bind` = `/contacts(<assignee>)`, plus the base's ADR-024 regarding block.

## Findings for the main session

1. **`Apply-DoLanePolicies.ps1` did not stamp version 1's `sprk_inforceto`: FIXED in fix round 1.** Original finding (from source, script not run): it moved `sprk_currentversion` but never ended the superseded version, and `Get-VersionDiffs` treated any `sprk_inforceto` as DRIFT. The product's own publish procedure (`OntologyAdminAuthoringService`, task 033 contract) is: create the new version, stamp the OLD version's `sprk_inforceto`, move `sprk_currentversion`, re-evaluate. Left as it was, v1 would have been classed a "saved draft" and the nightly job would log EventId 50309.
   - **New apply order** (per policy): (a) create any missing versions, (b) END each superseded version: `sprk_inforceto` = the next version's `inForceFrom`, only when it is empty and only once both rows exist, (c) move the pointer. The `sprk_inforceto` PATCH is the only write the script makes to an existing version row; a different existing end is a thrown DRIFT, never rewritten.
   - **`-Verify` / read-back** (`Get-VersionDiffs`): a superseded version must carry `sprk_inforceto` equal to the next version's `inForceFrom`; the current version must have none; anything else is DRIFT. Idempotent: a re-run on the applied state prints `UNCHANGED end of version ...` and writes nothing.
   - **Lint additions:** `currentVersion` must be the highest version number (the script only advances), and `inForceFrom` must not decrease with the version number. Both shipped definitions pass; the Do-lane definitions have one version each and are unaffected.
   - **Dry run:** the script has no path that avoids Dataverse (it reads an access token and the option sets before the lint, even under `-WhatIf`), and the lane rules forbid running it. So no `-WhatIf` transcript. Logic by case for `know-promotion.policies.json` (v1 `...6311`, v2 `...6312`):
     - *Nothing applied yet for v2:* POST v2 (create); then `ENDED version POL-MATTER-NOBUDGET v1 at 2026-10-22T00:00:00Z` (PATCH `{sprk_inforceto}` on `...6311`); then `SET current version of POL-MATTER-NOBUDGET -> v2`. Under `-WhatIf`: `would write ...` lines for v2, `would end POL-MATTER-NOBUDGET v1 at 2026-10-22T00:00:00Z (sprk_inforceto) once v1 and v2 exist`, and the pointer line.
     - *Re-run after apply:* `UNCHANGED version ... v1`, `UNCHANGED version ... v2`, `UNCHANGED end of version ... v1`, nothing written; `-Verify -RequireRows` reports every row IN SYNC.
     - *v1 ended at any other instant, or v2 ended:* throws (apply) / DRIFT (`-Verify`).
   - **Tests:** `tests/scripts/Apply-DoLanePolicies.Tests.ps1` (Pester 5, reads the shipped functions out of the script's AST, like `Invoke-OntologyStampBackfill.Tests.ps1`): 15 tests, 0 failed (15 failed against the previous script). Pester 6.2.0 locally cannot run any file in `tests/scripts` (`Export-ModuleMember` error); environment-specific, see fix round 2. These were run with Pester 5.7.1.
2. **Role gap on `sprk_EventType_Ref`: resolved on dev by the main session** (2026-10-23T00:32:20Z, `prvAppendTosprk_EventType_Ref` at Global to Spaarke Core User's root role, standing approval D-110). The repo role XML `src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Core User.xml` now carries the same privilege at `Global`. Root cause that made it matter: `sprk_eventtype_ref` is user-owned and the child-records create core asks AppendTo AS THE CALLER on every record the row binds (`OwnedChildWrite.CheckCallerMayAppendToAsync`). The executor preflight (kept) refuses a caller without it before anything is written.
2a. **What happens to open POL-MATTER-NOBUDGET Signals that cite v1 after the pointer moves** (from code, nothing changed):
   - They keep v1's plan until the policy's next evaluation pass. The wizard's plan route and the commit route both read the plan of the version the SIGNAL cites, not the policy's current version: `Api/Signals/DecisionPlanEndpoints.cs:180-185` (`decision.PolicyVersionId` from the Signal, `planService.GetAsync(versionId, ...)`), `Services/Signals/SignalCoreRecordAccess.cs:165,175` (that id is `_sprk_policyversion_value` of the Signal row), `Services/Signals/DecisionCommitService.cs:1264`.
   - The nightly `SignalReevaluationJob` (cron `30 2 * * *`, `SignalReevaluationJob.cs:50`) then supersedes and re-raises them on v2: `EvaluatePolicyAsync` takes the policy's CURRENT version (`:204`) and refuses it only if that version has ended (`:218-226`), reads the policy's Signals, and `SupersedeInPassAsync(policy.Code, versionId, read, ...)` (`:321`) closes every OPEN Signal whose cited version differs from the current one as `Superseded` (`SignalClosureService.cs:106-112`, predicate `IsSuperseded` at `:340-341`). In the same pass the planner (`SignalEpisodePlanner`, `SignalReevaluationJob.cs:1809-1812`) creates episode n+1 for a subject the v2 body still hits, unless it is suppressed (`:1805`), and the new row cites `spec.PolicyVersionId` = v2 (`:1107`). So after that pass the wizard shows v2's plan (Assign Task with Add To Do and Assign Work as Next steps). A subject that no longer meets the rule is left Superseded with no new episode.
   - Conditions: the policy must be enabled and active (`ReevaluatePolicyAsync` throws otherwise, `:168-174`; the nightly selection filters on `sprk_enabled`, `:1236`), and v2 must not carry an `sprk_inforceto` in the past (that is why the stamp must go on v1, never v2).
   - The apply script does not trigger the pass. Between the pointer move and 02:30 UTC an open v1 Signal still shows `assign-work`, and a decision committed in that window is recorded against v1's plan (a normal v1 commit; nothing fails). `SignalReevaluationJob.ReevaluatePolicyAsync` (`:164`) is the same single-policy pass the product's publish runs; the POML step 4 calls for re-evaluating or waiting for the nightly run.
3. Duplicated Task-type id constants outside Signals (not touched, listed for the operator): `Services/Ai/Nodes/ActionCore/TaskActionCore.cs:68`, `Services/Ai/Narrators/DailyBriefingCollector.cs:128`, `Services/Workspace/PortfolioService.cs:99`. Signals must not call `Services/Ai`, and a shared constant in `Spaarke.Dataverse` would be a cross-cutting change outside this task.
4. `dotnet format --verify-no-changes` reports `IMPORTS` ordering at line 1 of `SignalsModule.cs` and `DecisionActionExecutors.cs`; identical on a clean checkout of the base, so pre-existing.
5. Seam tests under `tests/integration/seam/Signals` (live Dataverse) were not run (lane: no Dataverse access beyond the read-only checks above).

## `assign-work` usage list (keep / fix)

| Where | Verdict |
|---|---|
| `Services/Signals/Actions/DecisionActionCatalog.cs` (row, comments) | Keep. `assign-work` stays a Decide-lane action and a Next step (D-178). |
| `Services/Signals/Actions/DecisionActionExecutors.cs` (`AssignWorkExecutor`, header comment) | Keep (only the name helper is shared now). |
| `Infrastructure/DI/SignalsModule.cs` | Keep; `assign-task` registration added beside it. |
| `Services/Ontology/OntologyAdminAuthoringService.cs:71` (doc comment: the catalog offers `assign-work` in both lists) | Keep; still true. |
| `scripts/Apply-DoLanePolicies.ps1:70` (Decide-lane lint code list) | **Fix, done**: `assign-task` added; `assign-work` kept. |
| `config/know-promotion.policies.json` v1 | Keep, unchanged (immutable, FR-49). v2 is the new entry. |
| Tests: `DecisionActionCatalogTests`, `DecisionActionExecutorsTests`, `DecisionGateTierTests`, `KnowPromotionPolicyDefinitionTests`, `OntologyAdminAuthoringEndpointTests` | Updated where the catalog set, the Decide list or the current plan changed; the `assign-work` cases kept. |
| `src/dataverse/solutions/SpaarkeMaster/WebResources/sprk_communicationreconciliation` (built bundle), `sprk_createinvoicewizard`, `sprk_createreportcardwizard`, `sprk_createworkassignmentwizard`, `sprk_spaarkeai` | Keep. Built bundles of the wizard follow-on cards; a different feature. |
| `Spaarke.UI.Components`: `CreateInvoiceWizard`, `CreateReportCardWizard`, `CreateRecordWizard/types.ts`, `CreateWorkAssignmentWizard/*`, `GetStartedCards/*`, `WizardFollowOns/*`, `OntologyAdmin/RuleWizard` test | Keep. Wizard follow-on card ids and the Get Started launcher, not the decision catalog. |
| `src/solutions/LegalWorkspace/.../quickSummaryConfig.ts:92` | Keep. Quick-summary action card that opens the Work Assignment wizard. |
| `src/solutions/SpaarkeAi/.../QuickStartModal.tsx`, `WelcomeStartCards.tsx`, `ConversationPane.tsx:3545` | Keep. Console quick-start "Assign Work" entries (D-178: a different feature). |

## Fix round 2 (review-159, Opus)

Each test below was run against the unfixed behaviour (the fix reverted in place, the test kept) and failed; then run again with the fix and passed.

| Item | Fix | Proof |
|---|---|---|
| F1 long "What to do" | `AssignTaskExecutor.BuildColumns` refuses `description.Length > 2000` (`DescriptionMaxLength`, equal to the MaxLength of `sprk_event.sprk_description` in `Entities/sprk_Event/Entity.xml`; a test reads the XML so the two cannot drift) with `InvalidParameter`, detail "Keep 'What to do' under 2,000 characters.", before any core call. | 2,001 chars: refused, `cores.VerifyNoOtherCalls()`; failed before (the core was called), passes after. Exactly 2,000: accepted (must-not-fire). |
| K3 Pester not in CI | New job `ontology-script-tests` in `ci-standalone-checks.yml`, a copy of `verdict-script-tests` (ubuntu, `Install-Module Pester -MinimumVersion 5.5.0`, throws on any failure or zero tests), running `Apply-DoLanePolicies.Tests.ps1` and `Invoke-OntologyStampBackfill.Tests.ps1`. Wired into `summary`, the `alerts` matrix and the header table; `.github/WORKFLOWS.md` and `docs/procedures/ci-cd-workflow.md` list it. | The PR's CI run is the proof the job works. Locally with Pester 5.7.1: Apply-DoLanePolicies 19 passed / 0 failed; Invoke-OntologyStampBackfill 10 passed / 0 failed. actionlint is not installed here; the YAML parses and follows the neighbouring job. |
| K4a nextSteps never linted | `Test-Definition` checks every `decisionPlan.nextSteps` code against `$NextStepCodes` (hardcoded, like the action-code lists in `$KindRules`; the catalog's `IsNextStep` set). No guard tied those lists to the catalog (none existed for the action lists either), so `DecisionActionCatalogScriptListsTests` (new) parses the script's lists and fails when either diverges from `DecisionActionCatalog` (action codes per lane, and Next-step codes). | Pester: `["mark-complete","bogus"]` gives two lint errors; the shipped files and the full catalog Next-step set lint clean. C#: with `assign-task` / `assign-work` removed from the script lists, the guard fails (2 tests); passes with the real script. |
| K4b equal inForceFrom | Ordering lint is now `-le` (strictly increasing); message "vN inForceFrom must be after vM inForceFrom". | Pester: an equal pair is refused; failed with the old `-lt`, passes with `-le`. The shipped v1 < v2 is accepted (must-not-fire). |

Pester proof run: with only the two lint edits reverted, 2 of 19 Apply-DoLanePolicies tests failed (equal date, bogus next step); with the fix 19 passed.

Max length in the Console: the decision wizard's `ParameterFields.tsx` renders `TextArea` as a Fluent `Textarea` with no max length, and the catalog parameter DTO (`DecisionActionParameter`) has no max-length field. No DTO field was added (instructed). The user therefore gets the executor's refusal message at commit time rather than an input limit. Listed as a follow-up for whoever extends the DTO.

Local Pester 6.2.0 failing with `Export-ModuleMember` on every file in `tests/scripts` is environment-specific (this machine also has Pester 3.4.0 on the module path); CI installs the latest Pester through the existing jobs and passes. Local runs used Pester 5.7.1.

## Gates

See the PR description and the final report for counts: filtered Signals tests, full `Spaarke.ArchTests`, build with `--warnaserror`, publish-size delta, CVE scan.

Known limits (K-class): none raised. Code review was done inline against the diff (reuse of the base class, privilege and AppendTo checks as the caller, no app-only write, no `Services/Ai` reference); adr-check clean.

