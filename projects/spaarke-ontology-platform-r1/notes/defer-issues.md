# Defer / Issue Tracking — Spaarke Ontology Platform R1

> **Protocol**: [`/project-defer-issue-tracking`](../../../.claude/skills/project-defer-issue-tracking/SKILL.md).
> **The two-write rule**: every entry here MUST carry a GitHub Issue URL. An entry without one is invisible
> to anyone outside this project, which is the failure this file exists to prevent.
>
> **Owner directive, 2026-09-30**: *"we do not want to bury or miss or lose any work items — so they must be
> either fixed or scheduled, not merely added to a list."* Accordingly:
> - Work that is **cheaper to do now**, because this project already holds the diagnosis, is pulled **into
>   scope** (`design.md` §5) and becomes a spec item → task. It does **not** appear here.
> - Work that genuinely belongs to another domain, or needs a decision this project cannot make, is filed
>   **here AND as a GitHub Issue**.
>
> **Pulled INTO scope rather than deferred** (see `design.md` §5): the space-bearing matter-number tokenizer
> (was D-6) and the false association `reason` string (was D-7). Both had complete diagnoses in hand;
> re-deriving them later would cost more than fixing them here.

---

### ISS-004 — the Console rename: engineering identifiers deferred

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | when the active SpaarkeAi worktree count drops, or alongside solution packaging |
| **Filed** | 2026-10-02 |
| **Source** | Owner decision 2026-10-02 during ontology-platform-r1 design review (item A) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1095 |
| **Task** | None — deferred by explicit owner decision (2026-10-02; concurrency with 37 SpaarkeAi worktrees); stays deferred under D-81 |

**Description**

The **product** surface is renamed to **Spaarke Console** now. The **engineering identifiers** are not: the
Dataverse web resource `sprk_spaarkeai` and the source directory `src/solutions/SpaarkeAi/`.

Measured: **32 occurrences across 16 files in `src/`**, plus 3 deploy scripts, 1 workflow and 1 ribbon XML —
**~21 live files, entirely mechanical.**

**Why it is deferred is concurrency, not difficulty.** `projects/INDEX.md` lists **37 of 62 active projects
with `SpaarkeAi = Y`**, several actively editing the files that carry the name. The rename would hand those 37
worktrees a merge conflict they did not ask for.

It is **not** blocked by broken deep links, which was the first reason given and was wrong: R1 is dev-only with
no managed solutions, and deep links are server-generated in `HandoffUrlBuilder`, so a coordinated redeploy
regenerates them.

**Must land as one change**: web resource + source directory + ribbon XML + 16 launch points +
`HandoffUrlBuilder`, deployed together.

---

## Issues (newly-discovered defects, cross-project)

### ISS-001 — `suggest-followups` is running a stale prompt in dev

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-09-30 |
| **Source** | Exposed by the `Deploy-ActionMirrors.ps1` fix in PR #1032 — with the deployer working, `-Filter '*' -DryRun` reports drift it previously could not see |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1048 |
| **Task** | [125](../tasks/125-action-mirror-drift-suggest-followups-create-task.poml) (no-parking sweep 2026-10-08) |

**Description**

The repo mirror `infra/dataverse/actions/suggest-followups.action.json` is **1,058 characters longer** than
the `sprk_systemprompt` on the live `sprk_analysisaction` row, and its `sprk_description` differs by 75
characters. The BFF reads `sprk_systemprompt` **at runtime** to build the model request, so that capability
is currently being asked for an **older contract than the repo declares**.

This is the same failure class `Deploy-ActionMirrors.ps1`'s own header documents: `spaarkeai-compose-r8`
added `target_para_id` to four compose actions, the mirror never reached Dataverse, the model was still asked
for `target_text`, and every AI edit arrived with no anchor — discovered only at UAT on 2026-08-26. The
difference now is that the deployer reports the drift instead of hiding it.

**Not fixed by this project deliberately**: deploying it changes `suggest-followups`' runtime behaviour, and
this project cannot validate that capability's output. Two other mirrors also drift
(`create-task-from-email`, `propose-field-updates`) but only in `sprk_outputschemajson` formatting
(indented → compact), which is semantically inert.

⚠️ **One live consequence already observed**: App Insights shows repeated
`HTTP 400 invalid_request_error … response_format 'Create_Task_From_Email' … 'required' is required to be
supplied and to be an array including every key in properties. Missing 'dueDate'` at 2026-09-29 18:59–19:09.
The `create-task-from-email` schema may be more than cosmetically stale — worth checking before dismissing
it as formatting.

**Entry-points**

- `pwsh -ExecutionPolicy Bypass -File scripts/Deploy-ActionMirrors.ps1 -Filter 'suggest-followups' -DryRun`
- `infra/dataverse/actions/suggest-followups.action.json`
- KQL: `exceptions | where timestamp > ago(7d) | where outerMessage contains "response_format"` (App Insights appId `6a76b012-46d9-412f-b4ab-4905658a9559`)

**Suggested fix** (if known)

Run the fixed deployer for that action once its owner has confirmed the repo mirror is the intended contract.
Separately, verify whether `create-task-from-email`'s live schema is missing `required: [dueDate]` and is the
cause of the observed 400s — if so that is a real defect, not formatting drift.

**Estimated effort**: 1–2 hours including verification of the 400s
**Blockers**: needs the communication/AI-actions domain owner to confirm the repo mirror is authoritative
**Related**: PR #1032 · `design.md` §8 D-8 · `Deploy-ActionMirrors.ps1` header (compose-r8 incident)

---

### ISS-002 — `$choices` resolution degrades silently to the 100%-null failure

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-09-30 |
| **Source** | Noted while verifying the taxonomy mechanism for `design.md` §4 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1049 |
| **Task** | [123](../tasks/123-triage-category-resolution-and-choices-visibility.poml) (counter added by 072 on this branch; 123 covers the remaining ActionRunner paths + alert) |

**Description**

`ActionRunner.ResolveLookupChoicesAsync` is best-effort by design (NFR-04): a missing scope factory, a missing
resolver, a non-JPS prompt, or **any Dataverse read failure** returns `null` and the prompt renders with
unresolved `$choices`, logged only at Warning.

That degraded state **is** the defect that produced *"category null on 100% of captures"* before the
2026-09-04 fix. So the system can silently return to a known-broken condition, and the only signal is one
Warning line in a sink nobody watches. Live data shows the shape of it: of 270 communications, 28 carry a
triage priority but only 15 carry a category — all 13 gap cases predate the fix.

This matters more than a typical monitoring gap because the **entire ontology design rests on this
mechanism** — registry-bounded classification is what makes the taxonomy declarable without a deployment
(`design.md` §4). If it fails quietly, every rule whose predicate names a category stops matching, and
nothing errors.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/ActionRunner.cs:379-405` (`ResolveLookupChoicesAsync`)
- `:424` (`EnrichOutputSchemaWithResolvedChoices`)
- KQL: `traces | where message contains "$choices pre-resolution failed" or message contains "output-schema $choices enum injection failed"`
- Detection query: communications with a triage priority but no category —
  `SELECT COUNT(*) FROM sprk_communication WHERE sprk_triagepriority IS NOT NULL AND sprk_triagecategory IS NULL`

**Suggested fix** (if known)

Two candidates, not exclusive: (a) raise the log to Error and add an alert on the two Warning messages;
(b) emit a metric for resolved-choice count per run so a drop to zero is visible. Do **not** make resolution
fatal — NFR-04 is deliberate, and a hard failure would take the whole enrichment path down.

**Estimated effort**: 2–4 hours
**Blockers**: needs an owner decision on where Spaarke alerts (no alerting convention found in this session)
**Related**: `design.md` §9 risks · `notes/mvp-technical-spec.md` §10.2–10.3

---

### ISS-003 — 49 `sprk_event` rows stranded in Draft, invisible to Daily Briefing

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-09-30 |
| **Source** | Found while fixing `TaskActionCore`'s missing `statuscode` (PR #1032) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1050 |
| **Task** | [060](../tasks/060-iss003-stranded-draft-events.poml) — done (D-74) |

**Description**

`TaskActionCore` never set `statuscode`, so every task it created took `sprk_event`'s default of `Draft(1)`,
while `DailyBriefingCollector`'s task channels filter `sprk_eventtype_ref = Task AND statuscode = Open
(659490001)`. PR #1032 fixes **new** tasks. It does not remediate the **49 existing rows** in spaarkedev1
that are sitting in Draft and therefore invisible to the briefing.

This is filed rather than fixed because a bulk update is **not obviously safe**: some of those 49 may be
genuine user-authored drafts, where "Draft" is correct and flipping them to Open would fabricate work items
on someone's list. The distinction has not been established.

**Entry-points**

- `SELECT statuscode, statecode, COUNT(sprk_eventid) FROM sprk_event GROUP BY statuscode, statecode`
  → Draft 49 / Open 16 / Completed 7 / Cancelled 1
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/ActionCore/TaskActionCore.cs` (the fix)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs` (`EventStatusOpen`)

**Suggested fix** (if known)

First **classify** the 49: which were created by the application user (system-generated, so Draft is a bug)
versus by a human (Draft may be intentional)? `createdby` plus `sprk_regardingcommunication IS NOT NULL`
should separate them. Then flip only the system-generated set. Dev-only data today, so the cost of getting it
wrong is low — but the same latent problem will exist in any environment where tasks were created before the
fix ships.

**Estimated effort**: 1–2 hours including the classification query
**Blockers**: none technically; wants a decision on whether human-authored Drafts are to be left alone (recommend yes)
**Related**: PR #1032 · `design.md` §3.1 · FAILURE-MODES AP-14

---

### ISS-005 — C-21: Pillar-9 `getAgentVisibleState` client derivation is structurally bypassed

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round (privacy/architecture judgment call) |
| **Filed** | 2026-10-03 |
| **Source** | Task 080 (six-hazards cleanup), escalated per root CLAUDE.md §6.5 rather than guessed |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1112 |
| **Task** | [086](../tasks/086-dead-code-own-pr.poml) — done (PR #1120 deleted it; issue closed) |

**Description**

`@spaarke/ai-widgets`' Pillar-9 `getAgentVisibleState()` machinery (`pillar9-visibility.ts`,
`getWorkspaceWidgetVisibleStateFn`, 8+ per-widget visibility wrappers) is an ADR-015-governed privacy contract
with extensive doc comments describing caps and minimization — and is never consulted at runtime. Verified: it
has zero call sites outside its own test; the server independently re-derives the same shapes via its own C#
mirror (`SprkChatAgentFactory.TryDeriveVisibleState`), by design (the server's own comment: "not by trusting
client serialization"); and as of a later change the server's actual prompt output is trimmed to
`{type, label, active}` only, so even the server's own richer derivation output is mostly unused today.

**Why not fixed here**: three remediation paths exist (delete as dead code / wire into the live request path /
re-document as non-authoritative), and choosing between them is a privacy + architecture judgment call — path
2 in particular would mean trusting client-computed data for an LLM prompt, which root CLAUDE.md §6 reserves
for human sign-off. The hazard this project found ("fixing a privacy bug here changes nothing at runtime")
remains true regardless of which path is chosen, so it is reported rather than guessed.

**Entry-points**: see the GitHub issue for the full file:line list (TS + the C# mirror).

**Suggested fix**: owner picks one of the three paths in the issue body; re-run `/adr-check` against ADR-015
once chosen.

**Estimated effort**: 2-4 hours once a path is chosen (mostly path A or C; path B is materially larger)
**Blockers**: owner decision on which path
**Related**: `notes/reuse-verification-2026-10-02.md` §8.8 finding X15 · task 080 · ADR-015

---

### ISS-006 — C-19 residual: `useKeyboardShortcuts.ts` liveness unresolved

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | low (cleanup follow-up, no hazard) |
| **Filed** | 2026-10-03 |
| **Source** | Task 080 (six-hazards cleanup) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1113 |
| **Task** | [086](../tasks/086-dead-code-own-pr.poml) — done (PR #1120 deleted both; issue closed) |

**Description**

C-19 deleted the confirmed-dead `CommandRegistry` cluster (`CommandRegistry.ts`,
`EntityConfigurationService.ts`, `CustomCommandFactory.ts`, `components/Toolbar/CommandToolbar.tsx` — zero
consumers outside their own tests). `src/hooks/useKeyboardShortcuts.ts` was NOT deleted: it has one real,
non-test importer, `components/PageChrome/CommandBar.tsx`. Per the task's own escalation trigger, a live
consumer stops a deletion — so it was kept. But `PageChrome/CommandBar.tsx` itself appears to have zero
consumers outside its own test (grepped its barrel-exported types across `src/solutions/` and `src/client/pcf/`;
none found), so this may simply push the same dead-code question one level up a chain that is itself
unreachable. Confirming that needs a more thorough consumer trace than this cleanup task's narrow scope covered
(see the reuse audit §8.6's own warning about grep over/under-counting).

**Entry-points**: see the GitHub issue.

**Suggested fix**: trace `PageChrome`'s real reachability (including lazy/dynamic imports); if genuinely
unreachable, delete `useKeyboardShortcuts.ts` + `PageChrome/CommandBar.tsx` + its barrel export together.

**Estimated effort**: 30-60 minutes
**Blockers**: none
**Related**: `notes/reuse-verification-2026-10-02.md` §8.8 finding X11 · task 080

---

### ISS-008 — `OntologyPlatformSolution` held 11 core tables whole (export would overwrite them)

> Numbered ISS-008 because ISS-007 is already used in the main worktree's uncommitted copy of this file.

| Field | Value |
|---|---|
| **Status** | Done (fixed 2026-10-08 00:09 UTC, owner decision D-60) |
| **Urgency** | now |
| **Filed** | 2026-10-07 |
| **Source** | Stream C2, task 047 live describe of `sprk_workassignment` (its solution list) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1385 (closed, completed) |
| **Task** | [069](../tasks/069-d60-solution-hygiene.poml) — done |

**Description**

In spaarkedev1, `OntologyPlatformSolution` held `sprk_budget`, `sprk_communication`, `sprk_document`, `sprk_event`,
`sprk_invoice`, `sprk_matter`, `sprk_project`, `sprk_recordtype_ref`, `sprk_servicerequest`, `sprk_todo` and
`sprk_workassignment` as whole-entity components with rootcomponentbehavior 0. They were added when this project's
lookups to them were created under the solution header. **Failure mode**: an export ships those tables' every column,
form, view and ribbon as they are in dev. An unmanaged import overwrites the target's customizations; a managed import
layers over core tables this project does not own.

**Fix (done)**: `RemoveSolutionComponent` ×11 (references only; nothing deleted). The project's own
`sprk_servicerequest` columns from task 001 were re-added as attribute components. 17 → 10 components. Evidence:
[`d60-solution-hygiene.md`](d60-solution-hygiene.md).

**Entry-points**: `GET /api/data/v9.2/solutioncomponents?$filter=_solutionid_value eq f258ed0a-a6be-f111-aaad-7c1e520a989f`

**Prevention**: when a lookup from an ontology table to another domain's table is created under
`MSCRM.SolutionUniqueName: OntologyPlatformSolution`, re-check the solution's component list afterwards. Remove any
foreign table it picked up with rcb 0.

**Estimated effort**: done (≈1 hour)
**Blockers**: none
**Related**: D-60 · task 047 · `notes/007-schema-verification.md` (solution membership)

---

### ISS-009 — G5 AppendTo check denies every business-owned lookup target (`systemuser`/`team`)

| Field | Value |
|---|---|
| **Status** | Open (owned by unified-access-control-r2) |
| **Urgency** | next-round |
| **Filed** | 2026-10-07 |
| **Source** | Stream C2, follow-up to PR #1391 (D-68) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1399 |
| **Task** | [126](../tasks/126-appendto-business-owned-lookup-targets-uac.poml) (uac-r2 picks the rule on #1355) |

**Description**

After #1391, `OwnedChildWrite.CheckCallerMayAppendToAsync` answers AppendTo for user/team-owned targets
(`RetrievePrincipalAccess`) and organization-owned targets (table AppendTo privilege + a caller read). Business-owned
tables (`systemuser`, `team`, `businessunit`, measured live in spaarkedev1) fit neither path, so a lookup to them is
denied with the uniform not-found for every caller. **Failure mode**: fails closed (nothing over-granted). No current
browser create sends such a lookup; the chat `dataverse.create_record` tool and any future create that does will be refused.

**Why not in #1391**: for business-owned tables Dataverse checks AppendTo at a depth relative to the target's business
unit; choosing that rule is uac-r2's access-model call, and getting it wrong over-grants. The issue carries a suggested fix.

**Entry-points**: `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/Dataverse/OwnedChildWrite.cs` (`CheckCallerMayAppendToAsync`)

**Estimated effort**: small once uac-r2 picks the depth rule
**Blockers**: uac-r2 decision
**Related**: #1391 · D-68 · task 046 (work-assignment create, if it sends a user/team lookup) · task 048 (moves this file)

---

### ISS-013 — `EventRoutesLiveTests`: 097 leg leaks the analysis row when `OData-EntityId` is missing

| Field | Value |
|---|---|
| **Status** | Open — fixed by task 106 (POML constraint) |
| **Urgency** | next-round |
| **Filed** | 2026-10-08 |
| **Source** | Independent re-check of PR #1359 (task 098) final round, K1/K2 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1402 (filed as ISS-011, renumbered: task 112 took ISS-011/012) |
| **Task** | [106](../tasks/106-todo-date-columns-date-only-own-pr.poml) — done (PR #1429; issue closed) |

**Description**: test code only. `CreateAnalysisAsync` in the 097 live leg returns `created[^1]` (an event id) when
Dataverse omits `OData-EntityId`, leaking the analysis row; the Briefing leg's find-by-name + register-for-cleanup
pattern should be copied. The "found by name" message also counts every registered event.

**Related**: #1359 · task 106

---

### ISS-014 / ISS-015 / ISS-016 — found by task 106, outside its scope

| Id | GitHub | What | Urgency |
|---|---|---|---|
| ISS-014 | [#1412](https://github.com/spaarke-dev/spaarke/issues/1412) | The **Deploy SpaarkeAi** workflow fails on master | next-round (affects task 114's deploy path) |
| ISS-015 | [#1416](https://github.com/spaarke-dev/spaarke/issues/1416) | Daily Briefing `emailShareDraft` test mock is wrong (one pre-existing jest failure) | next-round |
| ISS-016 | [#1417](https://github.com/spaarke-dev/spaarke/issues/1417) | SmartTodo jest config key typo `setupFilesAfterEach` (should be `setupFilesAfterEnv`), so the setup file never loads | next-round (small) |

Filed 2026-10-08 by the task 106 author; reported to the owner.

**Task** (no-parking sweep 2026-10-08): ISS-014 → [127](../tasks/127-spaarkeai-ribbon-build-clean-checkout-own-pr.poml) · ISS-015, ISS-016 → [128](../tasks/128-client-test-harness-repairs-own-pr.poml)

---

## Deferred scope

*(none — scope deferrals are recorded in `design.md` §5 "Out" with rationale, and the two items previously
listed as D-6/D-7 were pulled INTO scope rather than deferred.)*

---

### ISS-007 - source freshness + landing-contract columns (D-50)

| Field | Value |
|---|---|
| **Status** | Open |
| **Filed** | 2026-10-07 (task 057) |
| **Source** | D-50; `notes/v4-prototype-vs-solution.md` #8; `notes/v4-reconciliation.md` H-5 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1381 |
| **Task** | None — deferred by explicit owner decision D-50; stays deferred under D-81 |

Not built in R1: per-source freshness (sources, last sync, cadence) and the landing columns `sourcesystem` /
`sourceid` / `sourceetag` / `sourceasof` (spec section 2.1). R1 renders a null fact as Missing (`EvidenceLine`).

---

## Owner decisions 2026-10-03 (cleanup placement)

| Item | Decision | Where it lands |
|---|---|---|
| **C-21** Pillar-9 `getAgentVisibleState` shim (ISS-005, #1112) | **DELETE.** The server re-derives the shape itself and trims it to identity fields; wiring the client copy live would feed browser-computed data into an LLM prompt. | Dead-code PR for `Spaarke.UI.Components`; trace `SerializedWidgetState.ts` / `WorkspaceTab.ts` consumers first. Closes #1112. |
| **C-17** to-do due-date tier scheme | **3/7/10 days.** | This branch (Do lane). |
| Cleanup placement rule | Fix everything, never defer to issues; items unrelated to ontology go to their own PRs grouped by area. | See `notes/cleanup-placement-plan.md`. |

### ISS-010 — Triage category resolution ignores `statecode` and `sprk_enabled`

> Renumbered from a duplicate ISS-007 on 2026-10-07 (ISS-007 is the source-freshness deferral, #1381; ISS-008/009 were taken).

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-07 |
| **Source** | Independent review of task 072 (stream D) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1387 |
| **Task** | [123](../tasks/123-triage-category-resolution-and-choices-visibility.poml) |

**Description**

`CommunicationEnrichmentService.ResolveTriageCategoryIdAsync` (email project) resolves the model's category name
with an exact query and a normalized fallback over up to 200 rows; neither filters on `statecode` or
`sprk_enabled`. A disabled or deactivated category the model still emits is saved on the communication, although the
classifier is meant never to see it (spec `mvp-technical-spec.md:956`).

**Entry-points**: `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationEnrichmentService.cs` (~634-675).

**Suggested fix**: add both conditions to both queries, reusing `LookupChoicesResolver.AdditionalFilterFor("sprk_triagecategory")`; add a disabled-row test.

**Estimated effort**: 1 hour
**Blockers**: none; the file is owned by the email project
**Related**: task 072, issue #1049

---

### ISS-011 — Create Work Assignment never reports a committed create to `launchSurface`

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-08 |
| **Source** | Task 112 review (found in passing; pre-existing) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1420 |
| **Task** | [113](../tasks/113-migrate-remaining-wizards-in-app.poml) (amended 2026-10-08) |

**Description**

`WorkAssignmentWizardDialog` has no `onComplete` seam, so neither the code page nor `InAppWizardHost` writes
`completeHandoff`. `launchSurface({ consumerType: 'create-work-assignment' })` therefore always resolves as cancelled,
and Quick Start's `onRecordCreated` (task 064 E1b) never fires for "Assign Work".

**Suggested fix**: add `onComplete` and route the success close through `completeOrClose`, as `CreateMatterWizard`
does; wire it in both hosts.

**Estimated effort**: 1-2 hours
**Blockers**: none
**Related**: task 112

---

### ISS-012 — `CreateProjectWizardWidget` still opens Create Project via its own `navigateTo`

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round (candidate for task 113) |
| **Filed** | 2026-10-08 |
| **Source** | Task 112 (outside its file scope) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1421 |
| **Task** | [113](../tasks/113-migrate-remaining-wizards-in-app.poml) (amended 2026-10-08) |

**Description**

The `create-project-wizard` workspace widget (`Spaarke.AI.Widgets/.../CreateProjectWizardWidget.tsx` ~:140-170) calls
`Xrm.Navigation.navigateTo(webresource)` directly. That path keeps the white platform header that task 112 removed
everywhere else.

**Suggested fix**: replace it with the shared `navigateToWebResourceSurfaceAsync`, which opens the wizard in-app when
the host is mounted.

**Estimated effort**: 1 hour
**Blockers**: none
**Related**: task 112, D-26

---

---

### ISS-017 — `GridOverviewHandler` resolves `{{today}}` in UTC (D-25)

| Field | Value |
|---|---|
| **Status** | Open |
| **Filed** | 2026-10-08 (independent review of PR #1429, task 106) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1447 |
| **Task** | [124](../tasks/124-grid-overview-today-user-local-own-pr.poml) |

`GridOverviewHandler.cs:214` computes today as the UTC date; D-25 wants the caller's day. No live grid uses `{{today}}` yet.

---

### ISS-018 — Notification playbooks fail in dev (FetchXML id lists, Condition operands, Due Soon stubs, silent scheduler)

| Field | Value |
|---|---|
| **Status** | Open — fix in progress |
| **Filed** | 2026-10-08 (task 068) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1452 |
| **Task** | [120](../tasks/120-notification-playbooks-iss018-fix.poml) (D-77..D-80) |

Full evidence: [`iss-018-fetchxml-list-rendering-investigation.md`](iss-018-fetchxml-list-rendering-investigation.md).

---

### Issues filed without an ISS number (tasks 056, 111)

| GitHub | What | Task |
|---|---|---|
| [#1388](https://github.com/spaarke-dev/spaarke/issues/1388) | WorkspaceLayoutWizard: 14 jest tests fail on master (stale test ids) | [128](../tasks/128-client-test-harness-repairs-own-pr.poml) |
| [#1392](https://github.com/spaarke-dev/spaarke/issues/1392) | SemanticSearchControl PCF: jest red (9 of 11 suites) | [128](../tasks/128-client-test-harness-repairs-own-pr.poml) |
| [#1428](https://github.com/spaarke-dev/spaarke/issues/1428) | External SPA hand-off (056 wizard + 106 To Do changes undeployed) | [122](../tasks/122-external-spa-dev-deploy.poml) — replaces the hand-off (D-81) |

---

## D-106 — Ontology critical path only (owner, 2026-10-09): issues documented for post-project review

> **Rule (supersedes the 2026-10-08 no-parking rule):** fix an issue in this project **only if ontology functionality
> (spec + goals) needs it**. Everything else is documented here, with a GitHub link, and reviewed with the owner
> **after the project completes**. Nothing here is fixed or deployed in the meantime. Open PRs stay open (not merged,
> not closed) so their review history is kept.

### Deferred tasks (open work, not ontology functionality)

| Task | What | State at deferral | GitHub |
|---|---|---|---|
| 121 | LegalWorkspace 106 date filters live on dev; LW deploy path | Not started; owner checklist r6 open | [#1428](https://github.com/spaarke-dev/spaarke/issues/1428) (context) |
| 122 | External SPA dev deploy (056/098/106/111 changes) | Not started | [#1428](https://github.com/spaarke-dev/spaarke/issues/1428) |
| 125 | ISS-001 drifted AI action mirrors, `Create_Task_From_Email` 400s | Not started | [#1048](https://github.com/spaarke-dev/spaarke/issues/1048) |
| 129 | D-63 completion: chart/view/grid definitions off `sprk_finalduedate`; VisualHost 1.4.39 (`customcontrols.version` still 1.4.38) | Not started; owner checklist r8 open | task POML |
| 131 | Retire notification playbooks: repo deletions + docs | [#1493](https://github.com/spaarke-dev/spaarke/pull/1493) round 2 done (`afd2ced88`), needs re-check plus `.claude` edits (dataverse-create-schema SKILL.md:671 points at the deleted script; ADR-034:23/94 "(retired, D-100)"). [#1507](https://github.com/spaarke-dev/spaarke/pull/1507) code deletion (-20 KB), unreviewed; the ADR-036:21 edit belongs with #1507. **Dev is already retired** (7 playbooks inactive, alert deleted) | #1493, #1507 |
| 132 | Invalid appnotification option values in non-playbook writers (held-email alert; last-day grant reminder never delivered) | [#1494](https://github.com/spaarke-dev/spaarke/pull/1494) approvable (`c86ff04dd`); must merge **after #1493** (4 expected Issue1452 failures until then) | #1494 |
| 133 | System playbooks read-only in the Designer | [#1497](https://github.com/spaarke-dev/spaarke/pull/1497) round 2 pushed (`b2d1d776b`), not re-checked | #1497 |
| 134 | uac-r2: PB-07 membership role credit; AI-EX7 AiAnalysis write tools; PB-28 notification access | **Filed by uac-r2 2026-10-09, uac-r2 owns the fixes:** PB-07 [#1546](https://github.com/spaarke-dev/spaarke/issues/1546), AI-EX7 [#1547](https://github.com/spaarke-dev/spaarke/issues/1547), PB-28 [#1548](https://github.com/spaarke-dev/spaarke/issues/1548) (keep New Documents / New Emails / New Events notifications paused until #1548 lands). Notification Lookup nodes direction: all 7 use `targeting: "people"` with no `roles` filter (ADR-034 A3), keep the record-owner `ownerid eq-userid` branch, drop the linked-matter-owner branch (post-project, D-105) | [#1355 comment](https://github.com/spaarke-dev/spaarke/issues/1355#issuecomment-6089838200) |
| 136 | Create Analysis hub 404: the task's own changes (route tests, refusal log, retry-upload fix) | [#1501](https://github.com/spaarke-dev/spaarke/pull/1501), review requested changes (F-1..F-3 below). **The root-cause fix (#1391) stays on the ontology critical path (D-68)** | #1501 |

### Findings documented, not fixed

| # | Finding | Where found | GitHub |
|---|---|---|---|
| F-1 | A missing AppendTo privilege on an org-owned lookup table returns 404 "filed under … not found" instead of a 403 naming the privilege (uac-r2's `OwnedChildWrite.cs:501-545`, `ChildRecordEndpoints.cs:215`) | #1501 review F1-a | [#1501](https://github.com/spaarke-dev/spaarke/pull/1501); asked on [#1355](https://github.com/spaarke-dev/spaarke/issues/1355#issuecomment-6080141491) |
| F-2 | A failed Create Analysis Finish orphans the uploaded `sprk_document` (no compensating delete). Dev orphan `2cf67ed0-207b-4c58-a43d-11d8a7c9a424` (`nda_sample.pdf`) | #1501 review F1-c | #1501 |
| F-3 | #1501 tests send status 100000001, not the wizard's real value (1); the changed-file retry case is untested | #1501 review F2-a/b | #1501 |
| F-4 | Demo runs the 2026-05-19 canvas PlaybookBuilder, which saves canvas JSON and deletes unmarked nodes from the browser (bypasses any server guard). Legacy `PlaybookBuilderHost` PCF still in dev (on no form) | #1497 review F2 (cancelled task 138) | [#1497](https://github.com/spaarke-dev/spaarke/pull/1497) |
| F-5 | Per-node create/update/delete endpoints can change system playbook nodes; they also write configs without `__canvasNodeId`, which 133's rule would treat as protected | #1497 author + review K3 (cancelled task 137) | [#1498](https://github.com/spaarke-dev/spaarke/issues/1498) |
| F-6 | Matter Health: the Insights card is mounted nowhere in production; its form glue reads `artifact.body` / `decline.message`, which the API doesn't return; master's `matter-health-single.playbook.json` differs from dev's synced nodes; engine PB-02 skip propagation, AgentService prompt-from-Action and GroundingVerify fixes are unmerged | Task 135 (cancelled, D-105) | [#1496](https://github.com/spaarke-dev/spaarke/pull/1496) (closed; full findings in its body) |
| F-7 | Finance Invoice Processing playbook `1e657651-9308-f111-8407-7c1e520aa4df` left active (production invoice extraction resolves it by GUID). Recorded, no action | Task 135 | #1496 |
| F-8 | Scoped-publish wrappers (`Deploy-NoAccessEntryRibbon.ps1`, `Deploy-SecureChildNewCommands.ps1`, `Set-AccessRibbon.ps1`, `Deploy-FieldMappingAdminSolution.ps1`) can't pass `-AllowPendingCollateral`, and their printed re-run skips their own verification. The pre-flight misses derived entities (K1); an in-solution app's out-of-solution settings are unchecked (K2); the per-entity check is slow on SpaarkeMaster (K3, about 25 min vs about 1 min in bulk) | #1467 round-8 review F3, K1-K3 | [#1467](https://github.com/spaarke-dev/spaarke/pull/1467) |
| F-9 | Insights Layer-2 gate-fail skip is double-counted; universal-ingest `emitObservations` is skipped on the sufficient path | #1496 review #3/#4 | #1496 |
| F-10 | About 205 pre-existing broken Markdown links repo-wide (Tier 2 advisory validator) | #1467 author, 2026-10-09 | CI on #1467 |
| F-11 | The dev BFF restarted on its own between 04:15Z and 04:45Z on 2026-10-09 (same build `e9f08b764`) | Task 120 author | deploy-log "D-100 retire" |
| F-12 | Scoped publish (#1467 round 9): a fully-included entity (`rootcomponentbehavior` 0) is skipped entirely in the D-103 collateral check, so someone else's pending form/view/chart on it is overwritten (if in the zip) or published without a stop (if not, or on `-PublishOnly`). A narrower D-103 hole. Precise fix: exclude by the form/view/chart ids in the zip's `customizations.xml`. Also N2: a zip root missing `behavior` is treated as fully owned; N3: the sitemap read-back passes if the filtered unpublished query returns no row | #1467 round-9 review N1-N3 (non-blocking for ontology deploys) | [#1467](https://github.com/spaarke-dev/spaarke/pull/1467) |
| F-13 | `OwnedChildWrite.cs` ~line 340 `SecureFilingRefused` text says "from chat" but also reaches non-chat callers (the decision commit route) — uac-r2 code | Task 070 author | [#1509](https://github.com/spaarke-dev/spaarke/pull/1509) |
| F-14 | ADR-010 arch test caps 1:1 interfaces at 157 (fails on a new one): a constraint on 043/044 executor design, not a defect | Task 070 author | #1509 |
| F-15 | `Spaarke.UI.Components` package-level `tsc --noEmit` reports 9 errors on master and the project branch: `FileUploadService.ts` (5, `'error' is of type 'unknown'`, e.g. lines 139/143/144); unresolved `@spaarke/auth` / `@spaarke/sdap-client` in `AccessGrantModal/types.ts`, `EntityCreationService.ts`, `document-upload/types.ts`, `useWizardPageBootstrap.ts` | Task 051 author | [#1508](https://github.com/spaarke-dev/spaarke/pull/1508) |
| F-16 | `npm install --legacy-peer-deps` in `src/solutions/SpaarkeAi` rewrites `package-lock.json` (stale lockfile) | Task 051 author | #1508 |
| F-17 | `buildDynamicWorkspaceConfig` rowHeight test fails deterministically (expects "480px", gets "100vh") in code #1415 doesn't touch | #1415 master-merge agent | [#1415](https://github.com/spaarke-dev/spaarke/pull/1415) |
| F-18 | `ConversationView.forward` / `emailInFlow` jest tests fail under full parallel run, pass alone (timeout-sensitive) | #1415 master-merge agent | #1415 |
| F-19 | WorkspaceLayoutWizard's own `tsc` has errors from #1480 code (`App.tsx`, `ArrangeStep.tsx`, `SectionStep.tsx`: unused declarations; `scope` not in `LayoutJson`); the SpaarkeAi typecheck gate counts surface-owned errors only (236 pre-existing shared-library errors) | #1415 master-merge agent | #1415 |
| F-20 | Built artifacts `TrackingFieldTrio/.../bundle.js` and `SpaarkeMaster/WebResources/sprk_spaarkeai` still contain `legacySize` (clear on rebuild) | #1415 master-merge agent | #1415 |
| F-21 | Inquiry executor (#1512): no idempotency key / resume-send (a SendFailed Outbound row stays and inflates any "inquiries sent" count); a Dataverse ClientFailure on the caller check (e.g. throttling) is reported as Refused, not transient; recipients not validated as addresses (Graph rejects; no injection: Graph SDK JSON as the user); registered in CommunicationModule while siblings are in SignalsModule; adds a non-AI caller of the AI-namespace write core (include in task 048's move); Action row/Binding not authored though spec line-76 table lists them (owner decision after 043); should the plan hide send-budget-inquiry on Secure matters (owner) | #1512 review | [#1512](https://github.com/spaarke-dev/spaarke/pull/1512) |
| F-22 | Row component (#1513): line accessible name reads age as "3d" (needs aria-label); due date uses the browser locale (`toLocaleDateString(undefined)`); assignee-local (D-25) vs viewer-local display can differ for a WA assigner in another zone (owner chose viewer-local); shared `parseDueDate` accepts impossible dates ("2026-02-30"; Dataverse never returns them); barrel exports `composeIssueLine`/`formatAge`/`formatDueState` (test-only surface); no CI job runs the Worklist jest suite; notes header stale | #1513 review c-k | [#1513](https://github.com/spaarke-dev/spaarke/pull/1513) |
| F-23 | `RecordOwnerAssignmentCensusTests.cs:1418` regex `\b(?:_dataverse|user)\s*\.` misses `_user.PatchAsync` (no `\b` after `_`), so the census never sees ReviseBudget/ExtendResponseDate/RecordTheResponse executor writes (uac-r2 test) | Task 044 author | [#1515](https://github.com/spaarke-dev/spaarke/pull/1515) |
| F-24 | `EventEndpoints.ResolveActingUserContactAsync` logs `todo_unassigned ... sprk_assignedto left blank` whenever the caller has no linked contact, misleading for reschedule/reassign (now also called by 044's route) | Task 044 author | #1515 |
| F-25 | Full BFF unit suite: host-based contract tests time out (3-4 min each) under load; `PredictMatterCost` eval test runs ~18 min | Task 044 author | #1515 |
| F-26 | Row component tones on the transient hover background fall to 4.19-4.40:1 contrast (light yellow, dark red, dark orange); all tiers are >= 4.5:1 on the card background | #1513 round 2 author | [#1513](https://github.com/spaarke-dev/spaarke/pull/1513) |
| F-27 | **User-visible (owner should know):** the budget inquiry email and its replies thread under the service request, not the matter, so they do NOT appear on the matter's communication timeline (`/by-regarding` filters the thread's single regarding record). Criterion 10 / FR-36/37 are met via `sprk_servicerequest.sprk_regardingmatter`. Also: users with only a matter-level grant reach the email only if they can read the service request (core record, direct grants) | #1512 round-2 review (a) | [#1512](https://github.com/spaarke-dev/spaarke/pull/1512) |
| F-28 | Inquiry preflight duplicates `OwnedChildWrite`'s refusals; a NEW refusal added to `CreateAsync` would surface only at execute (fails closed, after earlier writes). Lasting fix: a check-only mode in uac-r2's core. Also: after the row exists the send runs with `CancellationToken.None`, so the whole post-send pipeline completes after a client disconnect (bounded by Polly/HttpClient timeouts); unresolved-owner and empty-firm-guard cases untested (both fail closed) | #1512 round-2 review (b)(c) | #1512 |
| F-29 | ISS-002: `$choices` degrade paths not counted / not logged as Error (task 123's second half, split off under D-106) | ISS-002 | [#1049](https://github.com/spaarke-dev/spaarke/issues/1049) |
| F-30 | `TodoDetail.tsx` `toDateInputValue` (~line 138) formats with `toISOString().split('T')[0]` (UTC date): after ~20:00 in a US zone the date input shows the next day (D-25 hazard `formatDateOnly` documents); reachability not checked | Task 067 author | [#1525](https://github.com/spaarke-dev/spaarke/pull/1525) |
| F-31 | `useKanbanColumns.ts` doc comment above `assignColumnByDate` says the score is the within-column sort key; nothing in the hook computes or uses it (stale) | Task 067 author | #1525 |
| F-32 | `Spaarke.UI.Components` standalone `tsc`/dist build fails in a fresh worktree (`@spaarke/auth`, `@spaarke/sdap-client` unbuilt), so jest suites that mock `@spaarke/ui-components` need a prebuilt dist; `SmartTodo.Components/package-lock.json` stale (rewritten by install) | Task 067 author | #1525 |
| F-33 | Demo loader `scripts/Load-DemoSampleData.ps1:366-367` / `demo-records.json` writes `sprk_eventdescription`, which `sprk_event` lacks (has `sprk_description`): creates 400 | Task 066 author | [#1528](https://github.com/spaarke-dev/spaarke/pull/1528) |
| F-34 | Stale copy `src/dataverse/solutions/SpaarkeMaster/WebResources/sprk_event_ribbon_commands.js` lacks task 098's date helpers; deploy from `src/solutions/EventCommands` | Task 066 author | #1528 |
| F-35 | `EventStatusWritePathTests.cs` pins the side-pane map by regex over `App.tsx` (`STATUSCODE_STATECODE_MAP[^{]*\{`), so any reformat breaks it | Task 066 author | #1528 |
| F-36 | **Six task/event views + 2 grid configs + 2 charts + a personal view still filter on `sprk_eventstatus`** and are wrong today: e.g. "All Tasks Open" family returns 15 rows and misses 18 of 24 open tasks (27 events Open by `statuscode`). Fix as one Dataverse change; also add `statuscode` (hidden) to the **Event modal form** (90d2eff7) so the ribbon enable rule stops falling back to `statecode` there. Column removal (`sprk_eventstatus`) only after both (task 066 prerequisite list) | #1528 review F2-B + owner decisions 1/2/4 | [#1528](https://github.com/spaarke-dev/spaarke/pull/1528) |
| F-37 | Event ribbon save-then-PATCH not atomic: save ok + PATCH fail leaves history/completed date without status, no refresh, generic error, retry duplicates history; Reassign PATCHes after the owner change (Write Deep fails for cross-BU/secure targets); ribbon mutation gaps (save/PATCH order, statecode fallback, PATCH failure); EventsPage/calendar pinned as constants only; `client-tests.yml` nightly-only so jest/node:test never run in PR CI; repo-walk guard 45-63 s | #1528 review | #1528 |
| F-38 | 066 notes nits: data line says 10 rows disagree (live: 18); decision 2 drops that modal + six side-pane forms still fall back to `statecode` (Complete/Cancel stay enabled on finished events); LegalWorkspace isn't a separate bundle (2 bundles carry the reconcile tab, not 3); `Deploy-Release.ps1` Phase 4 doesn't redeploy the ribbon JS / Console / CommunicationReconciliation / EventDetailSidePane while Phase 3 imports the stale ribbon copy (F-34); deploy note must say BFF FIRST (a new client against an old BFF silently ignores `statusCode`) | #1528 round-2 review | [#1528](https://github.com/spaarke-dev/spaarke/pull/1528) |
| F-39 | `CommunicationQueueFeedService.GetQueueFeedAsync` `Total` = ranked.Count, capped by `top` and `MaxCandidateScan` (200), so it understates beyond 200; stale "never reconciled" comment in `register-workspace-widgets.test.ts:417-420` | Task 054 author | task 054 |
| F-40 | To Do score (#1525): stale comments (`dateLocal.ts:28-30` says the formula stays in `todoScoring.ts`; `todoScoreMappings.ts:9,38` + guard title `todoScoreMappings.test.ts:177`; `useKanbanColumns.ts:74-89,112`); the no-second-copy test catches only a verbatim copy in 4 folders; `@param now` cites D-25 (server evaluator) where FR-63 (browser zone) applies; built bundles in `SpaarkeMaster/WebResources` (`sprk_corporateworkspace`, `sprk_alldocuments`, `sprk_analysisworkspace`, `sprk_calendarsidepane.html`) keep the old math until redeployed (AllDocuments/CalendarSidePane need the ui-components dist rebuilt first); Console `SmartTodoWidget.tsx:240` orders by `sprk_priorityscore`, not the composite (pre-existing); the new test files run only locally (CI runs only the Compose jest suite) | #1525 review | [#1525](https://github.com/spaarke-dev/spaarke/pull/1525) |
| F-41 | Category-to-team routing keys on the model's raw category string, not the resolved row (`CommunicationEnrichmentService.cs:609,731`, `CategoryRoutingGate.cs:52`), so a disabled/inactive name resolution drops can still assign `ownerid` when routing is on (off by default, `CategoryRoutingOptions.cs:38`); fix: route only when a category id resolved. Also: the normalized-fallback 200-row set can differ from the offered first-200-by-name set above 200 enabled rows (live 10); `statecode = 0` written twice (guarded by tests) | #1526 review | [#1526](https://github.com/spaarke-dev/spaarke/pull/1526) |
| F-42 | Reconciliation count hook (#1531): no refresh handle (stale if 059 shows the card beside a live reconciliation tab); a `distinct` / one-to-many outer join in a future config would over-count; CI never runs `Spaarke.Communication.Components` jest (`sdap-ci.yml:481-486` only builds) | #1531 review | [#1531](https://github.com/spaarke-dev/spaarke/pull/1531) |
| F-43 | Disposition tallies (#1532) run as the caller and undercount silently (Console/Basic users have no Read on `sprk_servicerequest`; Core User Deep): contract must say so; a cross-BU firm Report Card would undercount; the Report Card has no reader yet (`v4-prototype-vs-solution.md` #37) — decide app-only aggregate behind a Report Card privilege when built | #1532 review | [#1532](https://github.com/spaarke-dev/spaarke/pull/1532) |
| F-44 | Pre-existing client test breakage: `buildDynamicWorkspaceConfig.test.ts:292` test (h) (same as F-17); DailyBriefing `emailShareDraft.test.ts` / `legalWorkspaceSectionRegistry.test.ts` fail to compile (TS2307 `@spaarke/ui-components/types/LookupTypes`, `react`, `@fluentui/react-icons`, `@spaarke/communication-components`); DailyBriefing `tsc` resolves `@spaarke/ui-components` to an unbuilt dist | Task 052 author | [#1536](https://github.com/spaarke-dev/spaarke/pull/1536) |
| F-45 | #1531: `extractEntityFromFetchXml` exported from the DataGrid barrel/package root with no consumer; PR body lacks §11 answers for the two new UI.Components exports; loading spinner is a bare div (layout may jump; 059 to check); 2 unused eslint-disable warnings on moved lines | #1531 round-2 review | [#1531](https://github.com/spaarke-dev/spaarke/pull/1531) |
| F-46 | Lane count cards (#1536): "Coming due" card lacks W-7's "next due 2 Oct" note; Decide cards always "oldest N days" where the prototype shows "N overdue"; progress line not announced to screen readers (`laneCountFilters.ts:141` aria-label omits "n of m done today"); a stale `selectedKey` re-activates a filter when an item of that type reappears (`laneCountFilters.ts:68-76`) | #1536 review | [#1536](https://github.com/spaarke-dev/spaarke/pull/1536) |
| F-47 | CI Prettier gate can never fail: `sdap-ci.yml:396-402` runs `prettier --write` then `--check` | #1536 review | #1536 |
| F-48 | Inquiry disposition (#1532): ~~anyone with Write on the service request can record the disposition, not only the Assigned To Internal~~ **resolved as intended (D-114, owner 2026-10-09): anyone who can edit the service request may record it**; confirming a Suggested reply association sets Resolved for all core lookups on that reply and skips the client's `confirm-affinity` learning call; inbound step 4.9 adds one Dataverse read per inbound email (could reuse the in-memory association decision) | #1532 round-2 review | [#1532](https://github.com/spaarke-dev/spaarke/pull/1532) |
| F-49 | CI "Server tests (office scope)" (`office-addins-tests.yml:643`, `timeout-minutes: 20`) runs the office shard in ~16 min on a good runner, so a slow runner hits the 20-min cap and the job is cancelled (#1532 run 37990392445, 2026-10-09: cancelled at 20m16s mid-run, no test failed). Not an ontology defect; rerun clears it | #1532 CI | #1532 |
| F-50 | **Fixing now in #1553 (uac-r2 asked; 054 created the surface). uac-r2 confirmed the configured-savedquery retrieval is intended (Q1).** `resolveSource` / `fetchConfigRecord` (`DataGrid/resolveGridSource.ts`, moved there by 054) are public exports of `@spaarke/ui-components` and do not check the external host: a future external-SPA consumer calling them would list saved views, bypassing the grid's external-host rule. Not reachable today (only `DataGrid.tsx` calls them, and it refuses savedquery-set first; the 054 card is not mounted externally). Fix later: gate inside the resolver or stop exporting them. Also for uac-r2 to confirm: on the external host a `savedquery` source still calls `retrieveSavedQuery(<configured id>)` (unchanged, narrower than "no saved-query retrieval") | #1553 author | [#1553](https://github.com/spaarke-dev/spaarke/pull/1553) |
| F-51 | Stale path references after 048 moved `OwnedChildWrite`/`DataverseWriteItemMapper` to `Services/Dataverse/`: `projects/spaarke-ai-architecture-redesign-r1/spec.md` (~353, ~391), uac-r2 `notes/task-146-server-child-writers.md` and `tasks/146-server-child-writers-secure-owner.poml` cite `Services/Ai/Handlers/Dataverse/OwnedChildWrite.cs`; `tests/Spaarke.ArchTests/TenantIsolation/I6_ObAppRegDerivationTests.cs` (~98) comment cites `Services/Ai/Handlers/Dataverse/DataverseUserClient.cs`, moved by an earlier task. Historical/doc only | #1555 author | [#1555](https://github.com/spaarke-dev/spaarke/pull/1555) |
| F-52 | **uac-r2 is fixing it (2026-10-10, branch `fix/uac-r2-1575-scheduler-refresh-tick`: keep an unchanged definition's next fire across refresh).** `Spaarke.Scheduling/ScheduledJobHost.RefreshDefinitionsAsync` (~590) resets every job's next fire to `GetNextOccurrence(now)`, which excludes now, so the tick due at the hourly refresh minute is dropped. Live: refresh 01:28:00Z skipped both secure-child jobs; 42 of 61 even-minute refreshes over 3 days had no share run. The 2-minute secure-sync bound becomes ~4 min about hourly (039 gate's first revoke check failed on it; re-run passed) | Task 039 live gate | [#1575](https://github.com/spaarke-dev/spaarke/issues/1575) |
