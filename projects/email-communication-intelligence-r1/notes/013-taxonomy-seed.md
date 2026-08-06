# 013 — Category Taxonomy + Priority-Weight Config Seed (LIVE `spaarkedev1`)

> **Task**: 013 (P1, seed-only) · **Date**: 2026-07-29 · **Method**: Dataverse MCP `describe` + `read_query` + `create_record` against live `spaarkedev1`. **No schema created/altered — table already existed (operator-created, per task 001 note); this task seeds config data only.**
> **Verdict**: ✅ **PASS.** Table verified present with the exact FR-16/D-03 shape; 7 starter taxonomy rows seeded (0 already present — table was empty); idempotency check performed before insert.

---

## 1. Verified column logical names (`describe tables/sprk_triagecategory`)

Table already exists (collection `sprk_triagecategories`) — confirms task 001's finding ("`sprk_triagecategory` config table exists as a lookup target"). No schema change made.

| FR-16 concept | Verified live column | Type | Notes |
|---|---|---|---|
| Category name (primary) | **`sprk_name`** | NVARCHAR(850) NOT NULL | Matches schema-to-create.md §4 exactly — no naming delta. |
| Priority weight | **`sprk_priorityweight`** | INT (Whole Number) | Matches schema-to-create.md §4 exactly — no naming delta. |
| Enabled kill-switch | **`sprk_enabled`** | BIT (Two Options) | Matches schema-to-create.md §4 exactly — no naming delta. |

No optional `sprk_description` / `sprk_extractionguidance` columns exist on the live table — POML marked these optional/lean; not added (out of scope for a seed-only task; schema changes are not this task's job).

Standard system columns present (createdon, statecode/statuscode, organizationid, etc.) — no other custom columns beyond the 3 above.

**No column-name assumption was wrong** — `sprk_name`/`sprk_priorityweight`/`sprk_enabled` are exactly as documented in `notes/schema-to-create.md` §4; unlike task 013's own POML caution ("may be `sprk_triagecategoryname`"), the live table uses the simple `sprk_name` primary column.

---

## 2. Idempotency check (pre-seed query)

```sql
SELECT sprk_triagecategoryid, sprk_name, sprk_priorityweight, sprk_enabled FROM sprk_triagecategory
```

Result: **`[]`** (0 rows) — table was empty. All 7 starter rows below are newly created; none were skipped as duplicates.

---

## 3. Rows seeded (7 of 7 — category → priority-weight map for task 024)

All rows created via `mcp__dataverse__create_record`, `sprk_enabled = true` (Yes) on every row. Verified live post-insert via `read_query`.

| `sprk_name` | `sprk_priorityweight` | `sprk_enabled` | `sprk_triagecategoryid` (GUID) |
|---|---|---|---|
| Court / Filing | 100 | Yes | `65310056-598b-f111-8077-7ced8ddc4cc6` |
| Client instruction | 80 | Yes | `5ac90050-598b-f111-8077-7ced8ddc4cc6` |
| Opposing counsel | 70 | Yes | `7d310056-598b-f111-8077-7ced8ddc4cc6` |
| Invoice / Billing | 60 | Yes | `71310056-598b-f111-8077-7ced8ddc4cc6` |
| Scheduling | 50 | Yes | `73310056-598b-f111-8077-7ced8ddc4cc6` |
| Administrative | 30 | Yes | `80310056-598b-f111-8077-7ced8ddc4cc6` |
| Marketing / Noise | 10 | Yes | `8b310056-598b-f111-8077-7ced8ddc4cc6` |

**Rows already present (skipped)**: none — table was empty prior to this task.

This is the exact starter set + weights from `notes/schema-to-create.md` §4 ("Starter seed: `Client instruction` · `Court / Filing` · `Invoice / Billing` · `Scheduling` · `Opposing counsel` · `Administrative` · `Marketing / Noise`"), with the priority weights assigned per this task's directive (higher = more urgent).

---

## 4. §11 Component Justification (new-component rule — table pre-existed, seed is the new artifact)

1. **Existing** — No category taxonomy *data* existed (table was schema-only, 0 rows, per the pre-seed query above). Grep-confirmed no hardcoded category enum is being introduced by this task.
2. **Extension** — The table itself already exists (operator-created in a parallel schema pass, confirmed by task 001); this task extends it with data (a seed), not new schema. No new table/column was created.
3. **Cost-of-doing-nothing** — Without seed rows, `sprk_communication.sprk_triagecategory` (task 011's lookup) has no valid targets to bind to, and the triage Action (022/023) has no taxonomy to classify into or priority weights to read — a concrete contract failure, not "future flexibility."

---

## 5. Downstream unblock status

| Task | Depends on | Status |
|---|---|---|
| 011 (triage fields — `sprk_communication.sprk_triagecategory` lookup) | Seeded rows to bind to | ✅ **UNBLOCKED** — 7 valid lookup targets exist |
| 022/023 (Triage Action / classifier) | Taxonomy + weights as data | ✅ **UNBLOCKED** — classifier can resolve category names to the 7 rows above and read `sprk_priorityweight` for the priority scorer (D-08) |
| 024 (RI-confidence / priority scorer) | Category → weight map | ✅ **UNBLOCKED** — use the table in §3 above verbatim: `Court / Filing`=100, `Client instruction`=80, `Opposing counsel`=70, `Invoice / Billing`=60, `Scheduling`=50, `Administrative`=30, `Marketing / Noise`=10 |
| 060 (deploy) | Seed data present in solution-bound environment | ✅ Seed rows live in `spaarkedev1` (organization-owned config, per schema-to-create.md §4 ownership note) |

**No column-name assumption failed. No duplicate rows. No schema modified.**
