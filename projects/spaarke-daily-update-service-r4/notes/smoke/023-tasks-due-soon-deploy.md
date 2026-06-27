# Smoke: 023 — Migrate notification-tasks-due-soon to membership-scope (PB-020)

> **Task**: 023-migrate-tasks-due-soon-membership-scope.poml
> **Date**: 2026-06-25
> **Environment**: spaarkedev1
> **Spec refs**: FR-7 (line 142), AC-7
> **Deployer**: task-execute (STANDARD rigor) via Dataverse MCP `update_record`

---

## Deployment Outcome

- **Outcome**: **UPDATED** — `sprk_configjson` overlaid on existing row (idempotent overwrite of pre-migration state)
- **`sprk_analysisplaybookid`**: `77f77aa5-5f2d-f111-88b5-7ced8d1dc988`
- **`sprk_playbookcode`**: `PB-020`
- **`sprk_name`**: `Tasks Due Soon`
- **`sprk_playbooktype`**: `2` (Notification)
- **`statecode`** / **`statuscode`**: `0` (Active) / `1` (Active)
- **`modifiedon`**: `2026-06-25T23:39:40`

---

## Pre-deploy state (captured)

- **`sprk_configjson` size**: 295 chars (playbook-level only — schedule + category + channelLabel + channelIcon + parameters)
- **Node graph location**: 4 separate `sprk_playbooknode` rows (Start, Query Tasks Due Soon, Check Results, Create Notification)
- **LookupUserMembership (ActionType 52)**: ABSENT (matches audit 013)
- **Schedule frequency**: `hourly` (deployed) vs `daily 06:00` (repo source-of-truth — applied during deploy)

This confirms the audit-013 baseline: PB-020 PRE-MIGRATION as expected.

---

## Post-deploy verification

`sprk_configjson` now contains the full serialized payload (5,615 chars) — same convention as task 011's DAILY-BRIEFING-NARRATE deployment (nodes + edges embedded in configjson).

### Membership-scope checklist

| Verification | Result |
|---|---|
| `"actionType":52` (LookupUserMembership) present | ✅ PASS — `"name":"Lookup My Matters"` node with `__actionType:52` |
| `entityLogicalName` = `sprk_event` (NOT OOB `task`) | ✅ PASS |
| Task eventtype GUID `124f5fc9-98ff-f011-8406-7c1e525abd8b` in FetchXml filter | ✅ PASS |
| Membership-scope OR-filter: `sprk_regardingmatter IN myMatters.ids` ∪ matter-`eq-userid` ∪ record-`eq-userid` | ✅ PASS |
| Due-date filter: `sprk_duedate` OR `sprk_finalduedate` within `{{todayUtc}}..{{dueSoonWindowUtc}}` | ✅ PASS |
| Dedupe key uses `sprk_eventid` (NOT `activityid`) | ✅ PASS (`"key":"tasks-due:{{run.userId}}:{{item.sprk_eventid}}"`) |
| 5 nodes total: Start → Lookup My Matters → Query Tasks Due Soon → Check Results → Create Notification | ✅ PASS |
| Edge graph wires Start → Lookup My Matters → Query → Check → Create | ✅ PASS |
| Schedule frequency aligned to repo (`daily 06:00`) | ✅ PASS |

---

## Repo-only metadata stripped during deploy

Per task 011 precedent (annotation-strip during configjson serialization):
- `_correction` (W1 PR 3 correction notes — 2026-06-25)
- `$schema` (root)
- `playbook._dataverseRow`, `playbook._comment`, `playbook.componentJustification`
- `playbook.isPublic`, `playbook.isSystemPlaybook`, `playbook.sprk_playbooktype` (these are Dataverse row header fields, not configjson content)
- `playbook.scopes` (referenced via `actionRefs` model in BRIEF-NRRT; for PB-020 the scope is implied by the playbook type)

### Repo metadata note (spec correction reaffirmed)

Spec FR-7 line 142 originally said "Dedupe by activityid". Per CLAUDE.md § "🚨 2026-06-25 — Spaarke entity architecture" the canonical Spaarke Task entity is `sprk_event` (NOT OOB `task`); dedupe key was corrected to `sprk_eventid` during the W1 W0.4 repo-correction pass (task 015 / PR 2) and verified intact in this deployment.

---

## Acceptance criteria — task 023

| Criterion | Result |
|---|---|
| notification-tasks-due-soon.json node graph includes LookupUserMembership(sprk_matter) | ✅ PASS — present in deployed configjson |
| FetchXml unions `(sprk_regardingmatter IN myMatters.ids)` OR `(ownerid eq-userid)` with dedupe | ✅ PASS — verified via post-deploy read_query |
| jps-validate passes (per node graph integrity check) | ✅ PASS — 5 nodes, all dependsOn resolve, all outputVariable names unique, all edges reference declared nodes |
| Deployed PB-020 sprk_configjson matches repo file (annotation-stripped) | ✅ PASS |
| UAT scenario documented for due-soon variant of AC-7 | ✅ PASS — see below |

---

## UAT scenario (for AC-7 due-soon variant)

**Setup**: as test user U, ensure U is a member (`owner` / `assignedAttorney` / `assignedParalegal`) of at least one `sprk_matter` row M. Have at least one `sprk_event` row E1 with:
- `sprk_eventtype_ref` = Task GUID `124f5fc9-98ff-f011-8406-7c1e525abd8b`
- `statuscode` = `659490001` (Open)
- `sprk_regardingmatter` = M's GUID
- `sprk_duedate` or `sprk_finalduedate` within today..(today + 3 days)
- `ownerid` ≠ U (someone else owns it)

Also create E2 with:
- Same eventtype and statuscode
- `ownerid` = U (no matter linkage)
- Due-date within today..(today + 3 days)

**Expected**: PB-020 daily run produces a notification covering BOTH E1 (matter-membership scope) AND E2 (record-ownership scope) — confirming the union FetchXml.

**Counter-test**: create E3 with `ownerid` ≠ U, `sprk_regardingmatter` ≠ M (no matter U is a member of). E3 must NOT appear in the notification — confirming the membership filter actually excludes off-scope records.

---

## Parallel-safety note

Task 023 is in parallel Group D with tasks 022, 024, 025 (per TASK-INDEX W3.D wave plan). Scope is strictly PB-020 — no other playbook rows touched. Independent file surface (different `sprk_analysisplaybook` row); no contention with sibling tasks.

---

## Next steps (downstream of 023)

- Task 026 — Standardize enriched customData across all 7 notification playbooks (consolidation sweep)
- Post-PR-3 verification — re-query PB-020 + PB-021 to confirm membership convergence (per audit 013 carry-forward note 4)
