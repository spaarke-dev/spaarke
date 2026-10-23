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
| `scripts/Apply-DoLanePolicies.ps1` | `assign-task` added to the Decide-lane code list of the lint (without it the lint refuses the v2 plan). |
| Tests | Executor, catalog, gate tier, KnowPromotion definition, admin catalog endpoint list. |

Effect line has no trailing full stop, matching every other catalog effect line (the POML quote ends in a full stop; it reads as sentence punctuation).

Column set written (as the caller, through the child-records core): `sprk_eventname` (first line of the description, at most 200 chars), `sprk_description`, `sprk_duedate` only when given, `statuscode` 659490001 (Open), `sprk_eventtype_ref@odata.bind` = `/sprk_eventtype_refs(<Task>)`, `sprk_assignedto@odata.bind` = `/contacts(<assignee>)`, plus the base's ADR-024 regarding block.

## Findings for the main session

1. **`Apply-DoLanePolicies.ps1` does NOT stamp version 1's `sprk_inforceto`.** Confirmed from source only (script was not run). It does move `sprk_currentversion` to the definition's current version (final block, "The pointer moves only once the version exists"). It never writes an existing version row, and `Get-VersionDiffs` treats a non-null `sprk_inforceto` on a version as DRIFT (so a hand-stamped v1 would fail `-Verify` too, because the definition has no in-force-to field). The product's own publish procedure (`OntologyAdminAuthoringService`, task 033 contract) is: create the new version, stamp the OLD version's `sprk_inforceto`, move `sprk_currentversion`, re-evaluate. Effects of applying with the script as it is: v1 stays un-ended and is no longer current, which `OntologyAdminAuthoringService` classifies as a "saved draft" (not as superseded), and the nightly job logs EventId 50309 about a version neither current nor stamped out. The POML says STOP and report in this case. Per the lane rules I did not change the script or apply anything. Options: (a) extend the script to stamp the superseded version's `sprk_inforceto` (read-only compare must then accept a stamped superseded version), (b) apply with the script and stamp v1 with the product's own publish route, (c) publish v2 through the Ontology admin publish route instead of the script. This needs an owner or main-session decision before step 3.
2. **Role gap: `Spaarke Core User` has Read but not AppendTo on `sprk_EventType_Ref`** (privilege/role read on spaarkedev1, 2026-10-22; AppendTo is held only by Service Writer, Spaarke Office Add In User, System Administrator, System Customizer; Read by Basic User, Core User and others). `sprk_eventtype_ref` is user-owned. The child-records create core asks AppendTo AS THE CALLER on every record the row binds (`OwnedChildWrite.CheckCallerMayAppendToAsync`, RetrievePrincipalAccess for a user-owned target), so a Core User's Assign Task will be refused as `not_authorized` until the role is granted `prvAppendTosprk_EventType_Ref`. The executor preflight now refuses this before anything is written. Per the standing approval for Core User CRUD this is a role change for the main session (the lane may not change roles). Needed before step 4 (the dev proof) unless the proof user is an admin.
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

## Gates

See the PR description and the final report for counts: filtered Signals tests, full `Spaarke.ArchTests`, build with `--warnaserror`, publish-size delta, CVE scan.

Known limits (K-class): none raised. Code review was done inline against the diff (reuse of the base class, privilege and AppendTo checks as the caller, no app-only write, no `Services/Ai` reference); adr-check clean.

