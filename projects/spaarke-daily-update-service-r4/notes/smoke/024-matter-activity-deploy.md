# Task 024 — PB-017 notification-matter-activity Deploy Smoke

> **Date**: 2026-06-25
> **Task**: 024 — Implement notification-matter-activity playbook (stub → full)
> **Environment**: spaarkedev1
> **Playbook GUID**: `24051c80-5f2d-f111-88b5-7ced8d1dc988`
> **Repo file**: `projects/spaarke-daily-update-service/notes/playbooks/notification-matter-activity.json`

---

## Outcome

**STUB → FULL transition deployed successfully.** PB-017 sprk_configjson rewritten from 1-node placeholder (schedule + channel only, no graph) to fully functional 5-node graph backed by Spaarke custom `sprk_event` entity.

---

## Pre-Deploy State (Stub)

`sprk_configjson` contained ONLY:
- `schedule` block (cron `0 8 * * 1-5`, America/Chicago)
- `parameters.timeWindow` block (24h default)
- `channel` + `channelLabel` metadata

**No `nodes`. No `edges`. No `playbook` block. No query. No CreateNotification.** Users received zero matter-activity notifications because the playbook had no executable graph.

---

## Post-Deploy State (Full)

Full implementation deployed per R4 spec FR-8. Top-level structure now includes:

- `playbook` block — name, description, isPublic=true, sprk_playbooktype=2, capabilities=["notify"]
- `schedule` + `parameters` + `category` + `channel` + `channelLabel` (preserved)
- `nodes` (5 entries) + `edges` (4 entries)

### Node Graph (all 5 nodes verified post-deploy)

| # | Node | actionType | canvasType | Notes |
|---|------|-----------|-----------|-------|
| 1 | Start | null (33) | start | Scope: `user-matters`; resolveUserId=true |
| 2 | Lookup My Matters | **52** | lookupUserMembership | entityType=`sprk_matter`; roles=[owner, assignedAttorney, assignedParalegal] (ADR-034) |
| 3 | Query Matter Activity | 51 | queryDataverse | FetchXml on `sprk_event` with link-entity `sprk_matter`; membership-scope union: (a) `sprk_regardingmatter IN myMatters.ids` OR (b) record-owner OR (c) matter-owner = current user; last-x-hours window |
| 4 | Has Activity? | 30 | condition | Branches to "Create Notification" when count > 0 |
| 5 | Create Notification | 50 | createNotification | One appnotification per sprk_event; regardingType=`sprk_event`; deduplication keyed on `matter-activity:{userId}:{eventId}:{modifiedon}`; actionUrl deep-links to Spaarke event record |

### Edges (4 verified)

1. Start → Lookup My Matters
2. Lookup My Matters → Query Matter Activity
3. Query Matter Activity → Has Activity?
4. Has Activity? → Create Notification (condition: true)

### Spaarke Custom Entity Compliance

- ✅ Query target: `sprk_event` (Spaarke event entity, NOT OOB task/appointment)
- ✅ Membership target: `sprk_matter`
- ✅ Linked entity: `sprk_matter` (for matter-owner check)
- ✅ Notification regardingType: `sprk_event`
- ✅ actionUrl etn: `sprk_event`

---

## Deployment Mechanics

- **Tool**: `mcp__dataverse__update_record`
- **Entity**: `sprk_analysisplaybook`
- **GUID**: `24051c80-5f2d-f111-88b5-7ced8d1dc988`
- **Field updated**: `sprk_configjson` (whole-document replace; stub had no nodes/edges so a patch would have been incorrect)
- **Repo-only metadata stripped** before deploy: `$schema`, `_correction` block
- **statecode** before & after: 0 (Active) — preserved

---

## Component Justification (CLAUDE.md §11)

1. **Existing** — PB-017 stub row in spaarkedev1; sprk_configjson contained channel metadata + schedule only, no graph. No overlap with PB-019 (new-events) because PB-019 is type-specific (new event creation) and PB-017 is general activity stream across all event types and matter relationships.
2. **Extension** — YES. This is a stub → full promotion (extension, not new component). The same Dataverse row is updated in place; the same sprk_playbookid, sprk_name, channel, schedule remain.
3. **Cost-of-doing-nothing** — Users receive ZERO matter-activity notifications. The stub never queries Dataverse and never creates notifications. AC-8 fails by construction without this deployment.

---

## AC-8 Readiness

After PR 3 W1 producer enrichment (tasks 020–021) and this deployment, the playbook will:
- Resolve current user's matter membership via ActionType 52 LookupUserMembership
- Fetch sprk_event records modified within the configurable time window (default 24h) bound to those matters (or owned by user directly or via matter ownership)
- Create one matter-activity-category appnotification per modified sprk_event with deep-link to the record

UAT scenario: After scheduling, modify a sprk_event regarding a matter the user owns; within scheduler execution, user receives matter-activity-channel notification.

---

## Verification Evidence

- Pre-deploy MCP query: returned 1-node stub (channel/schedule only).
- Post-deploy MCP query: returned full 5-node graph with all expected actionType values (33, 52, 51, 30, 50) and all 4 edges.
- statecode = 0 (Active) preserved across update.
