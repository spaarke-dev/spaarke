# Task 014 — `sprk_playbookid` Backfill Evidence

> **Generated**: 2026-06-22 (main session, post-Q1 refactor)
> **Task**: 014 — Backfill `sprk_playbookid` on production-bound playbooks
> **Scope**: REVISED per Q&A 2026-06-22 Q1 — backfill `sprk_playbookid` (NOT `sprk_playbookcode`) to enable code-side stable-ID lookups via the new `/api/ai/playbooks/by-id/{id}` endpoint.
> **Tooling**: Dataverse MCP `update_record` (2 writes); MCP `read_query` for verification.

## Pre-state (read 2026-06-22 prior to writes)

| Playbook | `sprk_playbookid` pre-state | `sprk_playbookcode` |
|---|---|---|
| summarize-document-for-chat@v1 | NULL | NULL |
| summarize-document-for-workspace@v1 | NULL | NULL |
| Document Profile | `18cf3cc8-02ec-f011-8406-7c1e520aa4df` ✅ already populated | `PB-002` |
| Create New Matter Pre-Fill | `2d660cad-d418-f111-8343-7ced8d1dc988` ✅ already populated | `PB-008` |
| Create New Project Pre-Fill | `fc343e9c-3460-f111-ab0b-7c1e521b425f` ✅ already populated | NULL |

3 of 5 rows already followed the existing convention (`sprk_playbookid = sprk_analysisplaybookid` PK GUID); 2 rows had NULL `sprk_playbookid`.

## Writes performed

```
mcp__dataverse__update_record(
  tablename: "sprk_analysisplaybook",
  recordId:  "44285d15-1360-f111-ab0b-70a8a59455f4",
  item:      { "sprk_playbookid": "44285d15-1360-f111-ab0b-70a8a59455f4" }
)
→ "Record updated successfully."

mcp__dataverse__update_record(
  tablename: "sprk_analysisplaybook",
  recordId:  "302e6da6-f363-f111-ab0c-7ced8ddc4cc6",
  item:      { "sprk_playbookid": "302e6da6-f363-f111-ab0c-7ced8ddc4cc6" }
)
→ "Record updated successfully."
```

## Post-state (verified 2026-06-22 after writes)

| Playbook | `sprk_playbookid` post | `sprk_playbookcode` |
|---|---|---|
| summarize-document-for-chat@v1 | **`44285d15-1360-f111-ab0b-70a8a59455f4`** ✅ | NULL (unchanged) |
| Document Profile | `18cf3cc8-02ec-f011-8406-7c1e520aa4df` ✅ (unchanged) | `PB-002` (unchanged) |
| Create New Project Pre-Fill | `fc343e9c-3460-f111-ab0b-7c1e521b425f` ✅ (unchanged) | NULL (unchanged) |
| Create New Matter Pre-Fill | `2d660cad-d418-f111-8343-7ced8d1dc988` ✅ (unchanged) | `PB-008` (unchanged) |
| summarize-document-for-workspace@v1 | **`302e6da6-f363-f111-ab0c-7ced8ddc4cc6`** ✅ | NULL (unchanged) |

## Acceptance criteria

| Criterion | Status |
|---|---|
| All 5 production-bound playbooks have `sprk_playbookid` matching their `sprk_analysisplaybookid` PK GUID | ✅ |
| No playbook NAME modified | ✅ (only `sprk_playbookid` written) |
| No playbook GUID PK modified | ✅ |
| No playbook output-schema (`sprk_configjson`) modified | ✅ |
| No `sprk_playbookcode` value modified | ✅ (admin slug field untouched) |
| Existing convention preserved (sprk_playbookid mirrors PK GUID) | ✅ |

## Environment

- Environment: DEV (Dataverse MCP default binding)
- Tenant: same tenant as the originating session
- Higher-environment promotion: NOT performed here — owned by Phase 1 deploy task (026) per project scope

## What's NOT in scope (separate)

- The 6th playbook from original spec (`"Summarize New File(s)"`) — DROPPED per Q2 (does not exist; wizard error filed as B-015 for separate triage)
- `InvoiceExtractionJobHandler.cs:310` uses literal string `"PB-013"` as a `sprk_playbookcode` slug but now calls `GetByIdAsync` which queries `sprk_playbookid`. This will fail at runtime against Dataverse. Naturally addressed by Phase 6 task 129 (extract-invoice playbook authoring) OR can be a small Phase 1 follow-up task. Filed as note for visibility.

## Decision: ✅ GO

Wave 1-D and downstream Pattern A migrations (tasks 015, 016, 017, 018, 019) can proceed using `GetByIdAsync` against `sprk_playbookid`. Per-environment configuration values for `WorkspaceOptions.*PlaybookId` are the GUIDs above (DEV environment); deploy task 026 needs to populate the equivalent GUIDs for higher environments.
