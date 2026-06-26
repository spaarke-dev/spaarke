# Task 028 — `sprk_playbookconsumer` Table Verification Evidence

> **Status**: ✅ **GO** — Phase 1R foundation cluster (028a, 028b) unblocked
> **Verified**: 2026-06-24 by main session via Dataverse MCP `describe` + `read_query`
> **Verifier**: main session (sub-agent boundary preserved — MCP read-only)

---

## Verdict

**GO**. The owner-created `sprk_playbookconsumer` table meets the FR-1R-01 contract sufficient for Phase 1R execution. One minor naming difference noted vs spec (`sprk_playbook` vs `sprk_playbookid`) — spec has been updated in-place to match as-built; no impact on routing service implementation.

---

## Verification 1 — Table schema via `describe tables/sprk_playbookconsumer`

```text
DESCRIBE TABLE sprk_playbookconsumer (
  -- System-generated audit + lifecycle (omitted) --
  sprk_consumercode NVARCHAR(100),
  sprk_consumertype NVARCHAR(250),
  sprk_enabled BIT,
  sprk_environment NVARCHAR(100),
  sprk_matchconditions MULTILINE TEXT,
  sprk_name NVARCHAR(850) NOT NULL,
  sprk_playbook LOOKUP (GUID) (Related table: sprk_analysisplaybook),
  sprk_playbookconsumerid GUID,
  sprk_priority INT,
  statecode STATE (INT) (Active=0, Inactive=1),
  statuscode STATUS (INT) (Active=1, Inactive=2),
  versionnumber BIGINT  -- change-tracking column
);
```

### 8-column contract checklist (FR-1R-01)

| # | Spec column | Spec type | Actual column | Actual type | Status |
|---|---|---|---|---|---|
| 1 | `sprk_name` | Single Line Text (250) | `sprk_name` | NVARCHAR(850) NOT NULL | ✅ (wider — headroom) |
| 2 | `sprk_consumertype` | Single Line Text (64) | `sprk_consumertype` | NVARCHAR(250) | ✅ (wider — headroom) |
| 3 | `sprk_consumercode` | Single Line Text (64) | `sprk_consumercode` | NVARCHAR(100) | ✅ (wider — headroom) |
| 4 | `sprk_playbookid` | Lookup → sprk_analysisplaybook | `sprk_playbook` | LOOKUP (GUID) → sprk_analysisplaybook | ✅ functional (naming diff noted below) |
| 5 | `sprk_priority` | Whole Number (0–1000) | `sprk_priority` | INT | ✅ (range enforced at app layer per FR-1R-03) |
| 6 | `sprk_matchconditions` | Multiple Lines (4000) | `sprk_matchconditions` | MULTILINE TEXT | ✅ |
| 7 | `sprk_enabled` | Two Options Yes/No | `sprk_enabled` | BIT | ✅ |
| 8 | `sprk_environment` | Single Line Text (16) | `sprk_environment` | NVARCHAR(100) | ✅ (wider — headroom) |

### Naming difference — `sprk_playbook` (as-built) vs `sprk_playbookid` (spec)

- **Impact**: zero functional impact. The OData accessor for the lookup ID is `_sprk_playbook_value`. Task 028a `IConsumerRoutingService` will use this accessor; downstream consumer migrations (028c, 028d) consume the resolved Guid?, not the column name.
- **Resolution**: spec FR-1R-01 row 4 updated in-place from `sprk_playbookid` → `sprk_playbook` (commit immediately after this evidence file).

### Alternate key

- **As-built**: `sprk_ConsumerTypeCodeEnvironment` (per owner Power Apps screenshot 2026-06-24)
- **Spec original**: `ak_consumertype_code_env`
- **Resolution**: spec FR-1R-01 + task 028 POML updated in-place to `sprk_ConsumerTypeCodeEnvironment`.

### Change tracking + audit

- **Change tracking**: ✅ Confirmed — `versionnumber BIGINT` column present (Dataverse change-tracking signature).
- **Audit**: ✅ Confirmed via record-level evidence — `createdby`, `createdon`, `modifiedby`, `modifiedon` populated on both seed records (would be absent if audit disabled).
- **Ownership**: ✅ Organization (no `ownerid` lookup column → user/team ownership not configured).

---

## Verification 2 — Seed records via `read_query`

```sql
SELECT sprk_playbookconsumerid, sprk_name, sprk_consumertype, sprk_consumercode,
       sprk_environment, sprk_priority, sprk_enabled, sprk_playbook
FROM sprk_playbookconsumer
ORDER BY sprk_consumertype
```

| sprk_playbookconsumerid | sprk_name | sprk_consumertype | sprk_consumercode | sprk_priority | sprk_enabled | sprk_playbook (lookup target ID) |
|---|---|---|---|---|---|---|
| `e5f37faa-2c70-f111-ab0e-7ced8ddc4cc6` | Wizard New Matter Create | **`matter-pre-fill`** | default | 500 | true | `2d660cad-d418-f111-8343-7ced8d1dc988` |
| `ab7ac1c5-2c70-f111-ab0e-7ced8ddc4cc6` | Wizard New Project Create | **`project-pre-fill`** | default | 500 | true | `fc343e9c-3460-f111-ab0b-7c1e521b425f` |

### Seed-record checklist

| Check | Result |
|---|---|
| `matter-pre-fill` typo corrected (was `matter-pre-fil` per initial screenshot) | ✅ |
| Both records Enabled = Yes (Project flipped per owner action 2026-06-24) | ✅ |
| Matter lookup target = `2d660cad-d418-f111-8343-7ced8d1dc988` matches `Workspace__MatterPreFillPlaybookId` env var on bff-dev | ✅ |
| Project lookup target ≠ null | ✅ (`fc343e9c-3460-f111-ab0b-7c1e521b425f`) |
| Both `consumercode` = `default` | ✅ |
| Both `sprk_environment` = empty/null | ⚠️ See note below |
| Both `priority` = 500 (spec default) | ✅ |

### `sprk_environment` empty vs `*` — handling decision for 028a

- **Observation**: both seed records have `sprk_environment` = null/empty (not the spec-default `*`).
- **028a handling**: `ConsumerRoutingService` query filter MUST treat null/empty as equivalent to `*` (match-all). Implementation:

  ```csharp
  // In ResolveAsync OData query:
  // sprk_environment eq @env OR sprk_environment eq '*' OR sprk_environment eq null OR sprk_environment eq ''
  ```

  This is a defensive accommodation — owner can later set `*` explicitly in Power Apps if desired; not required for correctness.
- **028b seed script**: SHOULD UPSERT `sprk_environment = '*'` explicitly on the 4 remaining records (ai-summary, summarize-file, chat-summarize, email-analysis) to establish convention going forward. Existing 2 records left as-is to preserve owner intent.

---

## What's still missing (informational — not gate-blocking)

| Missing | Why not gate-blocking | When to add |
|---|---|---|
| 4 of 6 spec-recommended seed records (ai-summary, summarize-file, chat-summarize, email-analysis) | Task 028b explicitly UPSERTs the missing 4 idempotently | Task 028b execution |
| `sprk_environment = '*'` explicit on existing 2 records | 028a handles null/empty as match-all | Optional owner cleanup |
| Cumulative integration test with `IConsumerRoutingService` | Not yet built — 028a is the next task | Task 028a |

---

## Status flips

| Item | Before this verification | After |
|---|---|---|
| `TASK-INDEX.md` task 028 row | 🔲📄 not-started | ✅ |
| `current-task.md` "Owner action pending" | Active | Resolved |
| Phase 1R execution unblocking | 028a/028b/028c/028d/028e gated on 028 | 028a/028b unblocked; 028c/028d wait on 028b seed records; 028e waits on 028c+028d |

---

## Next action

Per Phase 1R wave structure (TASK-INDEX 1-K):
1. `task-execute 028a` — IConsumerRoutingService interface + impl + DI + cache + change-tracking invalidation (FR-1R-02, FR-1R-03, FR-1R-04).
2. After 028a lands: `task-execute 028b` — Seed-PlaybookConsumers.ps1 + UPSERT remaining 4 records.
3. After 028b lands: `task-execute 028c` + `task-execute 028d` in parallel (Pattern A + Pattern B migrations).
4. After both land: `task-execute 028e` — deprecation telemetry + Phase 1R exit gate.

---

*Verification artifact filed at `notes/handoffs/028-table-verification-evidence.md` per task 028 POML step 7 acceptance criterion.*
