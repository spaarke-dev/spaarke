# Task 022 — Deploy PB-021 (notification-tasks-overdue) Membership-Scope Union — STRUCTURAL DEFER

> **Date**: 2026-06-25
> **Task**: 022-migrate-tasks-overdue-membership-scope
> **Phase**: PR 3 (W1 Producer — customData + stubs + membership)
> **Spec ref**: R4 FR-7 (line 142), AC-7
> **Outcome**: **DEFERRED** — structural mismatch between task deployment recipe and deployed Dataverse data model. Documented per task-022 constraint "document and defer rather than improvise".

---

## a) MCP Availability + Pre-Deploy State

**MCP available**: YES. Both `mcp__dataverse__read_query` + `mcp__dataverse__update_record` loaded and exercised successfully against spaarkedev1.

**PB-021 deployed state (pre-deploy verify)**:

| Field | Value |
|---|---|
| `sprk_analysisplaybookid` | `4369cab2-5f2d-f111-88b5-7ced8d1dc988` |
| `sprk_playbookcode` | `PB-021` |
| `sprk_name` | `Tasks Overdue` |
| `statecode` | `0` (Active) |
| `sprk_configjson` (current, 152 bytes) | `{"schedule":{"frequency":"hourly"},"category":"tasks-overdue","channelLabel":"Overdue Tasks","channelIcon":"warning","parameters":{}}` |

**Confirmed GUID matches audit report 013.** Active. PRE-MIGRATION state (no LookupUserMembership node).

---

## b) Structural Divergence — Why This Task Defers

The task instruction states (paraphrased): "DEPLOY via `mcp__dataverse__update_record` on `sprk_analysisplaybook` row, setting `sprk_configjson` to the cleaned/serialized JSON string. POST-DEPLOY verify `sprk_configjson` contains the LookupUserMembership node + sprk_event entity reference + membership-scope union OR-branch."

**This recipe does not match the deployed data model.** The deployed PB-021 stores configuration across TWO entities:

1. **`sprk_analysisplaybook.sprk_configjson`** (the field the task targets) — holds ONLY playbook-level metadata: `schedule`, `category`, `channelLabel`, `channelIcon`, `parameters`. Does NOT contain nodes/edges.
2. **`sprk_playbooknode`** (child entity, 4 rows for PB-021) — holds per-node config: nodetype, executionorder, configJson (containing `__actionType`, FetchXml, etc.), dependsOnJson. Each node has its own `sprk_playbooknodeid`.

**Reference example confirming this split**: PB-016 `sprk_configjson` similarly contains only `{schedule, category, channelLabel, channelIcon, parameters}` (no nodes). Verified via direct query.

**Deployed PB-021 child nodes** (`sprk_playbooknode` rows, `sprk_playbookid = 4369cab2-...`):

| executionorder | sprk_name | sprk_nodetype | __actionType (in configJson) | sprk_playbooknodeid |
|---|---|---|---|---|
| 1 | Start | 100000002 (Control) | 33 | `4569cab2-5f2d-f111-88b5-7ced8d1dc988` |
| 2 | Query Overdue Tasks | (null) | 51 | `1cbd78b2-5f2d-f111-88b5-7c1e520aa4df` |
| 3 | Check Results | 100000002 (Control) | 30 | `20bd78b2-5f2d-f111-88b5-7c1e520aa4df` |
| 4 | Create Notification | (null) | 50 | `21bd78b2-5f2d-f111-88b5-7c1e520aa4df` |

**No LookupUserMembership (ActionType 52) node deployed.** Confirms PRE-MIGRATION baseline from audit 013.

### What a literal execution of the task instruction would do

If I followed the task literally — `update_record sprk_analysisplaybook` with `sprk_configjson` = cleaned-repo-JSON — the field would become a stringified JSON containing `nodes[]` + `edges[]` keys. However:

- The runtime engine reads nodes from `sprk_playbooknode` rows, NOT from `sprk_analysisplaybook.sprk_configjson`. So nodes embedded in the playbook field would be **runtime-ignored**.
- The migration's intent (deploy LookupUserMembership; enable membership-scope FetchXml union; satisfy AC-7) would **NOT be achieved at runtime**.
- The post-deploy verification step ("sprk_configjson contains LookupUserMembership") would textually pass, but the AC-7 manual UAT scenario would fail because the runtime executor would still use the old 4-node graph.

This would be a false-positive deployment. Hence: **defer, do not improvise**.

---

## c) Canonical Path Forward (per repo evidence)

Repo-evidence-confirmed proper deployment mechanism: **`scripts/Deploy-Playbook.ps1`** (header documents it handles both playbook rows AND child `sprk_playbooknodes`, with `-Force` to delete-and-recreate). Excerpt from script header:

> Entities: `sprk_analysisplaybooks` — Playbook records · `sprk_playbooknodes` — Playbook node records · `sprk_analysisactions` — Actions (scope) · ... · This script is idempotent when run without -Force — it skips playbooks that already exist by name. Use -Force to delete and recreate.

There is also `scripts/Deploy-NotificationPlaybooks.ps1` (likely a notification-playbook-specific wrapper — not inspected here). A future task should:

1. Inspect `Deploy-Playbook.ps1` (and `Deploy-NotificationPlaybooks.ps1`) end-to-end to confirm the deployment shape: probably reads the repo JSON, validates against `node-routing-config.schema.json`, then DELETEs existing child nodes + UPDATEs/CREATEs nodes per the repo `nodes[]` array.
2. Run with `-DefinitionFile projects/spaarke-daily-update-service/notes/playbooks/notification-tasks-overdue.json` (the corrected repo JSON is already in place from PR 2 task 015) and `-Force` to recreate.

**Alternative manual MCP path** (if the script is unavailable in CI): a sequence of MCP operations would be required:
- UPDATE the existing 4 node rows (rewriting `sprk_configjson` per the repo node defs + updating `sprk_dependsonjson` to insert "Lookup My Matters" into the chain)
- CREATE 1 new node row for "Lookup My Matters" (ActionType 52, executionorder = 2, depends on Start)
- Rewrite executionorder on existing nodes 2/3/4 to 3/4/5
- Optionally UPDATE the playbook `sprk_configjson` schedule from `hourly` → `daily 06:00` per repo
- This is ~6-10 MCP write operations and constitutes substantial improvisation — NOT done in this task.

---

## d) Repo-JSON Sanity Check (PASS)

The corrected repo file at `projects/spaarke-daily-update-service/notes/playbooks/notification-tasks-overdue.json` was inspected end-to-end and is CORRECT for deployment:

- ✅ Uses `sprk_event` (not OOB `task`) as `entityLogicalName`
- ✅ Includes Task event-type discriminator: `sprk_eventtype_ref` eq `124f5fc9-98ff-f011-8406-7c1e525abd8b`
- ✅ Includes the `Lookup My Matters` node (`actionType: 52`, ADR-034 LookupUserMembership primitive over `sprk_matter` with roles `[owner, assignedAttorney, assignedParalegal]`)
- ✅ FetchXml unions: `sprk_regardingmatter IN myMatters.ids` OR matter-owner `eq-userid` OR record-owner `eq-userid`
- ✅ Edges express the chain: `Start → Lookup My Matters → Query Overdue Tasks → Check Results → Create Notification`
- ✅ `_correction` block (repo-only metadata) documents the entity correction history per CLAUDE.md § '2026-06-25 — Spaarke entity architecture'
- ✅ Dedupe key field is `sprk_eventid` (correctly reflecting spec FR-7 's "Dedupe by activityid" wording bug — the canonical Spaarke field is `sprk_eventid` not OOB `activityid`)

**No repo-side changes required.** The repo JSON is ready for whoever owns the proper deployment task.

---

## e) Verification Outcome (per task post-deploy verify questions)

| Question | Answer | Notes |
|---|---|---|
| LookupUserMembership node present in deployed state? | **NO** | PRE-MIGRATION. No `sprk_playbooknode` row with `__actionType: 52` for PB-021. |
| sprk_event entity reference present in deployed state? | YES (in existing node 2 `Query Overdue Tasks`) | But without membership-scope union — only `ownerid eq-userid` is in the FetchXml filter. |
| Task event-type discriminator GUID `124f5fc9-…` filter present? | YES (in existing node 2 FetchXml) | Already correct. |
| Membership-scope union OR-branch present? | **NO** | Existing FetchXml has only single-condition `ownerid eq-userid`. Membership-scope OR-branch requires Lookup My Matters node + FetchXml rewrite. |

**Net**: Membership-scope migration NOT applied. AC-7 not yet satisfied. Carry forward as below.

---

## f) Carry-Forward (BINDING for downstream task)

A NEW task (to be filed during PR 3 wrap or PR 5) MUST own the proper deployment of the corrected repo JSON for PB-021 to spaarkedev1. The task should:

1. **Use `scripts/Deploy-Playbook.ps1` -Force** (the canonical mechanism) OR
2. Decompose into proper MCP operations against `sprk_playbooknode` (delete/recreate child nodes), OR
3. Whatever mechanism the W1 PR 3 wrap step ultimately specifies (per spec line 8 carry-forward note from audit 013: "W1 (or equivalent W1 wrap step) redeploys these two playbooks").

This same defer-and-carry-forward applies to PARALLEL task 023 (PB-020 `notification-tasks-due-soon`), which has an identical structural divergence pattern. Tasks 024 + 025 may or may not — they touch different playbook JSONs (matter-activity stub PB-017, work-assignments stub PB-022) and require independent verification.

---

## Sign-off

- MCP available + verified: ✅
- Pre-deploy state captured (GUID + configjson + 4 child nodes): ✅
- Repo JSON corrected state confirmed (sprk_event + LookupUserMembership + union + dedupe by sprk_eventid): ✅
- Deployment: **DEFERRED** (structural mismatch — task recipe doesn't match deployed schema; literal execution = false-positive)
- Carry-forward documented: ✅ (Deploy-Playbook.ps1 -Force is the canonical path)

Task 022 closes as DEFERRED. AC-7 manual UAT remains UNTESTABLE until the proper redeploy path is invoked. PB-021's repo-JSON correction (PR 2 task 015) and PR 3 W1 LookupUserMembership branch design (this task's repo-JSON authoring intent) are both COMPLETE in source; only deployment is deferred.
