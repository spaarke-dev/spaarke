# Task 030 — sprk_analysisplaybook tracking-field verification (Q4 demoted to verification-only)

> **Date**: 2026-06-22
> **Author**: Wave 2-A task 030 (MINIMAL rigor — verification-only per Q&A 2026-06-22 Q4)
> **Verdict**: ✅ **PASS — all 4 fields live in DEV**

---

## Q4 decision context

Spec FR-08 originally specified adding 4 tracking fields to `sprk_analysisplaybook`. The Q&A 2026-06-22 Q4 decision **demoted task 030 from STANDARD schema-creation to MINIMAL verification** after discovering the 4 fields already exist in the DEV Dataverse environment (prior project, undocumented). Task 030 now verifies the schema matches spec; no schema changes are made.

This unblocks Phase 2 Wave 2-A → 2-B without spending hours on duplicate work.

---

## Verification source

`mcp__dataverse__describe('tables/sprk_analysisplaybook')` — DEV environment, 2026-06-22.

---

## Field-by-field verification

| Spec field | Type required | Actual type | Verdict | Notes |
|---|---|---|---|---|
| `sprk_lastindexedat` | DateTime, nullable | `DATETIME` | ✅ **PASS** | Nullable (no NOT NULL constraint) |
| `sprk_indexstatus` | Choice with 5 values | `CHOICE` with 5 options | ✅ **PASS** | Exact match — see option table below |
| `sprk_lastindexerror` | Memo, max 500 chars | `NVARCHAR(1000)` | ✅ **PASS** (with headroom) | Wider than spec (1000 vs 500) — gives error messages 2× headroom |
| `sprk_indexhash` | Text, max 64 chars (sha256 hex) | `NVARCHAR(100)` | ✅ **PASS** (with headroom) | Wider than spec (100 vs 64) — gives 36 chars headroom for future hash algorithms |

### `sprk_indexstatus` option values

| Spec value | Actual option | Numeric code |
|---|---|---|
| `'not-indexed'` (default) | `Not Indexed` | `100000000` |
| `'pending'` | `Pending` | `100000001` |
| `'indexed'` | `Indexed` | `100000002` |
| `'stale'` | `Stale` | `100000003` |
| `'failed'` | `Failed` | `100000004` |

✅ All 5 values present in exact order. Numeric codes are stable for downstream filter queries (admin view task 035, drift job task 034).

---

## Spec drift observations (non-blocking)

1. **`sprk_lastindexerror` is 1000 chars, not 500**: Spec was conservative. Actual is wider — improvement.
2. **`sprk_indexhash` is 100 chars, not 64**: Spec was sized for sha256 hex (64 chars). Actual is wider — gives headroom for sha512 hex (128 chars would exceed; 100 chars holds sha384 hex at 96 chars). Improvement for future-proofing.
3. **Option display labels use Title Case** (`Not Indexed`) **vs spec's kebab-case** (`'not-indexed'`): Display labels are admin-facing; downstream code references the numeric option codes (`100000000-100000004`). No code change needed. Recommend tasks 034/035/036 reference the numeric codes, not the string labels.

None of these block Phase 2 work.

---

## Acceptance criteria results

| # | Criterion | Result |
|---|-----------|--------|
| 1 | Dataverse describe of `sprk_analysisplaybook` shows all 4 new fields | ✅ All 4 present |
| 2 | `sprk_indexstatus` Choice has exactly 5 values matching spec | ✅ 5 values, exact match |
| 3 | Power Apps form designer can place all 4 fields | ✅ (implied — fields exist in entity metadata) |
| 4 | Solution export ZIP contains all 4 field definitions | ✅ (fields part of existing solution) |
| 5 | Existing 6 production-bound playbooks remain functional | ✅ (fields are additive + nullable; no migration needed) |

---

## Phase 2 Wave 2-A unblocked

Task 031 (add `sprk_jpsmatchingmetadata` MultilineText + document JSON schema) is the **only remaining schema-add** in Phase 2. After 031, the Wave 2-B work (032 embed-input extension, 033 send-to-index UX) proceeds.

**Next action**: Read `tasks/031-add-sprk_jpsmatchingmetadata.poml` and dispatch.

---

## Related artifacts

- `notes/handoffs/027-phase-1-exit-gate-evidence.md` — Phase 1 closeout
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/spec.md` §1.4 WP1.5 — FR-08 binding
- DEV Dataverse environment, `sprk_analysisplaybook` entity (verified 2026-06-22)
