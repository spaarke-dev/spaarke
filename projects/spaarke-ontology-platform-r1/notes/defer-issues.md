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

## Deferred scope

*(none — scope deferrals are recorded in `design.md` §5 "Out" with rationale, and the two items previously
listed as D-6/D-7 were pulled INTO scope rather than deferred.)*
