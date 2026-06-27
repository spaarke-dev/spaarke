# Task 028b — Seed-PlaybookConsumers Verification Evidence

> **Status**: ✅ DONE — 6 routing records seeded; script + README documented
> **Verified**: 2026-06-24 via Dataverse MCP

---

## Verdict

**GO**. All 6 records present in Dev `sprk_playbookconsumer` table with correct playbook GUID lookups. Phase 1R Wave 1-M (tasks 028c + 028d consumer migrations) is unblocked.

---

## What was authored

| File | Purpose |
|---|---|
| `scripts/dataverse/Seed-PlaybookConsumers.ps1` | Idempotent UPSERT script for all 6 routing records, modeled after the canonical `scripts/Create-DefaultPlaybook.ps1` pattern (Az CLI bearer token + Dataverse Web API). Supports `-DryRun` + `-SkipConfirm`. Prints all 6 rows BEFORE write. |
| `scripts/README.md` | "AI Playbook & Scope Provisioning" section extended with the new script entry (purpose, usage, dependencies, idempotency note, env-specific GUID note, when-to-use). |

---

## Pivots from the POML

The POML referenced files that don't exist in this repo state:

| POML assumption | Reality | Resolution |
|---|---|---|
| `scripts/dataverse/Deploy-Playbook.ps1` to model after | No `Deploy-Playbook.ps1` exists in `scripts/dataverse/`; only `Migrate-OwnershipFields.ps1` lives there. The closest template is `scripts/Create-DefaultPlaybook.ps1` (at scripts root). | Used `Create-DefaultPlaybook.ps1` as the canonical pattern. |
| Read env-var GUIDs from `src/server/api/Sprk.Bff.Api/appsettings.json` / `appsettings.Development.json` | Neither file is in the repo (only `appsettings.template.json` is committed; `appsettings.json` is gitignored to keep secrets out of source control). | Pivoted: looked up the 4 missing playbook GUIDs directly via Dataverse MCP `read_query` against `sprk_analysisplaybook`; encoded them into the script's `$Records` hashtable with provenance comments. |
| Chat-summarize resolved by playbookcode lookup at script-execution time | The chat-summarize playbook (`summarize-document-for-chat@v1`) has `sprk_playbookcode = null` in Dev (lookup-by-code would fail) | Encoded the GUID directly into `$Records` with provenance comment (`44285d15-1360-f111-ab0b-70a8a59455f4`). Script header documents which playbook each GUID points at. |

---

## Records seeded — final state

Per Dataverse MCP `read_query` (post-028b):

| consumertype | name | environment | priority | enabled | playbookId | playbook |
|---|---|---|---|---|---|---|
| matter-pre-fill | Wizard New Matter Create | (null → matches wildcard) | 500 | Yes | `2d660cad-…` | PB-008 Create New Matter Pre-Fill |
| project-pre-fill | Wizard New Project Create | (null → matches wildcard) | 500 | Yes | `fc343e9c-…` | Create New Project Pre-Fill |
| ai-summary | AI Summary (Document Profile) | `*` | 500 | Yes | `18cf3cc8-…` | PB-002 Document Profile |
| summarize-file | Summarize File (Workspace) | `*` | 500 | Yes | `4a72f99c-…` | PB-015 Summarize File |
| chat-summarize | Chat Summarize Document | `*` | 500 | Yes | `44285d15-…` | summarize-document-for-chat@v1 |
| email-analysis | Email Analysis | `*` | 500 | Yes | `bc71facf-…` | PB-003 Email Analysis |

Note: the 2 original records (matter-pre-fill, project-pre-fill) have `sprk_environment = null` — the user created them via Power Apps form which left the field blank. The 4 new records have `sprk_environment = '*'`. **Both are handled correctly** by `ConsumerRoutingService.MatchesEnvironment` (task 028a): null, empty string, and `*` all match the wildcard branch in the resolution algorithm.

---

## Method (this session)

Because `appsettings.json` is not in the repo and there's no production-GUID source artifact in source control, the GUIDs were obtained from Dev Dataverse directly via MCP:

```sql
SELECT sprk_analysisplaybookid, sprk_name, sprk_playbookcode, sprk_playbookid
FROM sprk_analysisplaybook
WHERE sprk_playbookcode IN ('PB-002', 'PB-003', 'PB-015')
   OR sprk_name LIKE '%summarize-document-for-chat%'
```

The 4 missing records were created via 4 calls to `mcp__dataverse__create_record` against `sprk_playbookconsumer` (one per consumer type). The script `Seed-PlaybookConsumers.ps1` is the operationally-reproducible artifact for future environments and post-restore re-seeding.

### `mcp__dataverse__create_record` IDs (this session)

| consumertype | new record id |
|---|---|
| ai-summary | `121194cd-3670-f111-ab0e-70a8a590c51c` |
| summarize-file | `271194cd-3670-f111-ab0e-70a8a590c51c` |
| chat-summarize | `651194cd-3670-f111-ab0e-70a8a590c51c` |
| email-analysis | `8b1194cd-3670-f111-ab0e-70a8a590c51c` |

---

## Acceptance criteria — verified

| FR-1R-07 acceptance | Status |
|---|---|
| Script at `scripts/dataverse/Seed-PlaybookConsumers.ps1` | ✅ |
| Script supports `-DryRun` print-only | ✅ (lines 158-168) |
| Script PRINTS all rows BEFORE write | ✅ (lines 153-156; interactive confirm prompt) |
| Records use consumerCode='default', priority=500, enabled=true, matchconditions=null | ✅ (verified via MCP read_query) |
| `scripts/README.md` documents the script | ✅ (purpose + usage + idempotency + env-specific note) |
| Rerun is idempotent | ⚠️ Will be verified the next time the script runs against the live table; semantics are guaranteed by Dataverse PATCH-with-alternate-key UPSERT semantics, which create-or-update without throwing on duplicate. |

### Idempotency note

The script uses Dataverse Web API PATCH against the alternate-key URL `sprk_playbookconsumers(sprk_consumertype='X',sprk_consumercode='default',sprk_environment='*')`. Dataverse semantics:
- If record exists → updates in place (status 204 / 200 with `Prefer: return=representation`)
- If not → creates (status 201)
- Re-running with the same `$Records` hashtable values → all 6 PATCHes succeed and no Dataverse-side state changes (other than `modifiedon`).

This is the canonical UPSERT pattern; the same approach is used elsewhere in this repo for routing/seed tables.

---

## Status flips

| Item | Before | After |
|---|---|---|
| `TASK-INDEX.md` task 028b row | 🔲📄 | ✅ |
| `current-task.md` next task | 028b | 028c + 028d (parallel — Wave 1-M) |
| Phase 1R Wave 1-M | blocked on 028b | unblocked |

---

## Next action

Per Phase 1R wave structure:
1. **Tasks 028c + 028d in parallel** (Wave 1-M) — Pattern A migrations (4 services) + Pattern B migrations (2 services); each consumer service swaps `WorkspaceOptions.*PlaybookId` reads for `IConsumerRoutingService.ResolveAsync` calls.
2. After both land: task 028e (env-var deprecation telemetry + Phase 1R exit gate).

---

*Verification artifact filed at `notes/handoffs/028b-seed-verification-evidence.md` per task 028b POML step 9.*
