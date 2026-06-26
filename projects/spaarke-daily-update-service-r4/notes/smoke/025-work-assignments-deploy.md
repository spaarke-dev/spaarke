# Smoke: 025 — Deploy PB-022 notification-work-assignments STUB → FULL implementation

> **Task**: 025-implement-work-assignments-playbook.poml
> **Date**: 2026-06-25
> **Environment**: spaarkedev1
> **Spec refs**: FR-9 (line 148), AC-9 (line 149)
> **Deployer**: task-execute (STANDARD rigor) via Dataverse MCP

---

## Stub → Full transition summary

| Aspect | BEFORE (stub) | AFTER (full) |
|---|---|---|
| Node count | 1 (createNotification only) | 5 (Start → Lookup → Query → Condition → CreateNotification) |
| Edges | 0 (no graph) | 4 edges |
| `LookupUserMembership` (ActionType 52) | ❌ absent | ✅ "Lookup My Matters" node, `entityType=sprk_matter`, roles=[owner, assignedAttorney, assignedParalegal] |
| `QueryDataverse` (ActionType 51) | ❌ absent | ✅ "Query New Work Assignments" — `entityLogicalName=sprk_workassignment` |
| Entity target | n/a (broken `{{task.*}}` mustache referencing no upstream node) | ✅ **`sprk_workassignment`** (custom Spaarke entity — NOT OOB `task`) |
| Time-window filter | n/a | ✅ `createdon` OR `modifiedon` last-x-hours = `{{timeWindowHours}}` (24h default) |
| Membership-scope filter | n/a | ✅ `sprk_regardingmatter IN {{joinIds myMatters.ids}}` OR matter-owner=eq-userid OR record-owner=eq-userid |
| `CreateNotification` (ActionType 50) | ✅ present but broken mustache | ✅ enriched — `iterateItems=true`, regardingType=`sprk_workassignment`, deduplication keyed on `workassignmentid` |
| Functional state | Inert (zero notifications produced) | Membership-scoped, time-windowed, dedup-aware emit stream per spec FR-9 |

---

## Deployment Outcome

- **Outcome**: **UPDATED** (whole `sprk_configjson` replacement on existing PB-022 row)
- **Playbook**: `sprk_analysisplaybookid = be7874be-5f2d-f111-88b5-7ced8d1dc988` ("New Work Assignments")
- **Method**: `mcp__dataverse__update_record` on `sprk_analysisplaybook`, field `sprk_configjson`
- **Pre-deploy state**: Stub header only (schedule + parameters + channel) — no nodes, no edges
- **Post-deploy state**: Full header + 5 nodes + 4 edges (membership-scoped, sprk_workassignment, 24h-window)
- **`statecode`**: `0` (Active) — preserved
- **`sprk_playbooktype`**: `2` (Notification) — preserved
- **Payload length**: 5,217 chars (minified JSON in `sprk_configjson`)

---

## Pre-deploy Verification (Stub baseline captured)

```sql
SELECT sprk_analysisplaybookid, sprk_name, sprk_playbooktype, statecode, sprk_configjson
FROM sprk_analysisplaybook
WHERE sprk_analysisplaybookid = 'be7874be-5f2d-f111-88b5-7ced8d1dc988'
```

Result:
- `sprk_name = "New Work Assignments"`
- `sprk_playbooktype = 2 (Notification)`
- `statecode = 0 (Active)`
- `sprk_configjson` = header-only stub (no `nodes`, no `edges` keys present) — matches audit 014 baseline

---

## Post-deploy Verification (Full implementation confirmed)

```sql
SELECT sprk_analysisplaybookid, sprk_name, statecode, sprk_configjson
FROM sprk_analysisplaybook
WHERE sprk_analysisplaybookid = 'be7874be-5f2d-f111-88b5-7ced8d1dc988'
```

Post-deploy `sprk_configjson` parsed and asserted:

| Criterion | Result |
|---|---|
| `statecode = 0` (Active) | ✅ PASS |
| `LookupUserMembership` node present with `__actionType: 52`, `entityType: "sprk_matter"` | ✅ PASS — "Lookup My Matters" |
| `QueryDataverse` node references `entityLogicalName: "sprk_workassignment"` (NOT OOB `task`) | ✅ PASS — "Query New Work Assignments" |
| FetchXml time-window filter present: `createdon` OR `modifiedon` `last-x-hours` = `{{timeWindowHours}}` (24h default per parameter) | ✅ PASS |
| FetchXml membership-scope filter: `sprk_regardingmatter` IN `{{joinIds myMatters.ids}}` | ✅ PASS |
| `CreateNotification` node enriched: `iterateItems=true`, regardingType=`sprk_workassignment`, dedup key=`work-assignments:{{run.userId}}:{{item.sprk_workassignmentid}}` | ✅ PASS |
| Edges: 4 (Start→Lookup→Query→Condition→CreateNotification true-branch) | ✅ PASS |

---

## AC-9 (Spec line 149) — UAT script

**Scenario**: When a `sprk_workassignment` row is created (or modified) within the last 24 hours AND `sprk_regardingmatter` is one of the user's membership matters (or the user owns the assignment or the matter), an `appnotification` row appears in the Daily Briefing widget under the "work-assignments" channel.

**UAT steps**:
1. Sign in as a test user who is owner / assignedAttorney / assignedParalegal on at least one `sprk_matter`.
2. Create a `sprk_workassignment` row with `sprk_regardingmatter` = that matter, `statecode` = 0 (Active), `sprk_responseduedate` = future date.
3. Trigger PB-022 dispatch (cron `0 */2 * * 1-5` America/Chicago, or manual via PlaybookBuilder run).
4. Open the Daily Briefing widget; verify under "New Work Assignments" channel: a notification row with title `New assignment: {{sprk_name}}`, body referencing `{{sprk_regardingrecordname}}` and `{{sprk_responseduedate}}`, `actionUrl` deep-linking to the work-assignment record, and `regardingType=sprk_workassignment`.
5. Verify dedup: re-run PB-022 within the same hour — same row does NOT produce a duplicate notification (key `work-assignments:{userId}:{workassignmentid}` honored).

**Expected pass criteria**:
- Notification appears in widget within one dispatch cycle.
- `customData.category = "work-assignments"`, `customData.regardingType = "sprk_workassignment"`, `customData.regardingId` matches the work-assignment GUID.
- Dedup prevents duplicate emit.

---

## Component Justification (CLAUDE.md §11)

- **Existing**: Stub PB-022 (1 `createNotification` node referencing `{{task.*}}` mustache that no upstream node materialized; deployed `sprk_configjson` was header-only).
- **Extension**: Yes — functional promotion of the existing stub to a full membership-scoped query+emit graph (no new playbook row; same GUID, same `sprk_playbookcode`, same channel/category).
- **Cost-of-doing-nothing**: Users receive zero notifications when work assignments are created/updated on their matters — the work-assignments channel in the Daily Briefing widget remains permanently empty regardless of activity, breaking the user-trust contract of "Daily Update" surfacing relevant changes.

---

## Acceptance criteria — task 025

| Criterion | Result |
|---|---|
| `notification-work-assignments.json` repo file contains functional node graph (no longer stub) | ✅ PASS (already rewritten in PR 2 task 015) |
| Graph includes `LookupUserMembership(sprk_matter)` + `QueryDataverse(sprk_workassignment)` + `CreateNotification` | ✅ PASS (5-node graph deployed) |
| Component Justification block present | ✅ PASS (above + in repo JSON `_correction` block) |
| Deployed PB-022 `sprk_configjson` matches repo file (semantic equivalence) | ✅ PASS (post-deploy read-back confirmed) |
| UAT scenario documented for AC-9 | ✅ PASS (above) |
| `sprk_workassignment` custom entity used throughout (NOT OOB `task`) | ✅ PASS (3 references: `entityLogicalName`, `entity name=` in FetchXml, `regardingType` in CreateNotification + itemNotification) |

---

## Sources

| Item | Path / GUID |
|---|---|
| Task POML | `projects/spaarke-daily-update-service-r4/tasks/025-implement-work-assignments-playbook.poml` |
| Audit baseline | `projects/spaarke-daily-update-service-r4/notes/audit/014-pb-017-022.md` |
| Repo JSON source-of-truth | `projects/spaarke-daily-update-service/notes/playbooks/notification-work-assignments.json` |
| Deployed playbook GUID | `be7874be-5f2d-f111-88b5-7ced8d1dc988` |
| Spec FR-9 | line 148 |
| Spec AC-9 | line 149 |
| MCP tool | `mcp__dataverse__update_record` (deploy) + `mcp__dataverse__read_query` (verify) |

---

## Parallel-group note

Task 025 is in parallel Group D (with 022, 023, 024). This deployment touched ONLY PB-022 (`sprk_analysisplaybookid = be7874be-…`) — no other playbook rows affected. Safe to merge alongside tasks 022 / 023 / 024.
