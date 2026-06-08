# Audit Item 04 — Retroactive `sprk_toolcode` Rename to Descriptive Form

**Date**: 2026-06-07
**Status**: PARTIAL — JSON files updated; Dataverse re-seed STOP-AND-SURFACED (see §3 Decision Required)
**Tagging**: R6 audit item (not a numbered task; no `TASK-INDEX.md` or `current-task.md` updates)
**Trigger**: User manually extended `sprk_toolcode` column from 10 → 100 chars and requested descriptive kebab-case codes with NO `@v1` / version suffix anywhere (cross-environment migration concern — version suffixes look bad in migration scripts).

---

## 1. Before / After Toolcode Mapping (8 handlers)

| # | Handler class | OLD `sprk_toolcode` (cryptic, ≤10 chars) | NEW `sprk_toolcode` (descriptive) | JSON file |
|---|---|---|---|---|
| 1 | `DateExtractorHandler` | `DATEXTR@v1` | `DATE-EXTRACTOR` | `infra/dataverse/sprk_analysistool-date-extractor-row.json` |
| 2 | `FinancialCalculatorHandler` | `FIN-CALC@v1` | `FINANCIAL-CALCULATOR` | `infra/dataverse/sprk_analysistool-financial-calculator-row.json` |
| 3 | `ClauseComparisonHandler` | `CLAUSE-CMP@v1` | `CLAUSE-COMPARISON` | `infra/dataverse/sprk_analysistool-clause-comparison-row.json` |
| 4 | `FinancialCalculationToolHandler` | `FIN-CALC-FORMULA@v1` | `FINANCIAL-CALCULATION` | `infra/dataverse/sprk_analysistool-financial-calculation-row.json` |
| 5 | `EntityExtractorHandler` | `ENT-EXT@v1` | `ENTITY-EXTRACTOR` | `infra/dataverse/sprk_analysistool-entity-extractor-row.json` |
| 6 | `ClauseAnalyzerHandler` | `CLZ-AN@v1` | `CLAUSE-ANALYZER` | `infra/dataverse/sprk_analysistool-clause-analyzer-row.json` |
| 7 | `RiskDetectorHandler` | `RISK-DET@1` | `RISK-DETECTOR` | `infra/dataverse/sprk_analysistool-risk-detector-row.json` |
| 8 | `InvoiceExtractionToolHandler` | `INVEXT@v1` | `INVOICE-EXTRACTOR` | `infra/dataverse/sprk_analysistool-invoice-extractor-row.json` |

**Naming convention**: UPPER-KEBAB-CASE, mirrors handler class semantics, **NO `@v1` or version suffix anywhere**. For the two "Financial*" handlers, distinguished by `CALCULATOR` (sum/tax/discount/FX) vs `CALCULATION` (formula: interest, NPV/FV, loan payment, ROI).

All 8 JSON files have been edited; each carries a new `_comment_toolcode` documenting the rename + audit date + rationale. Grep verification: zero remaining `@v1` substrings in `infra/dataverse/sprk_analysistool-*-row.json` `sprk_toolcode` values.

---

## 2. Upsert Mechanism — `Seed-TypedHandlers.ps1` (key field confirmation)

**Confirmed**: `scripts/Seed-TypedHandlers.ps1` upserts by `sprk_toolcode` (NOT by `sprk_handlerclass`).

Evidence — line 128 of `Seed-TypedHandlers.ps1`:
```powershell
$query = "$BaseUrl/api/data/v9.2/sprk_analysistools?`$filter=sprk_toolcode eq '$ToolCode'&`$select=sprk_analysistoolid,sprk_name,sprk_handlerclass,sprk_toolcode"
```

The `Find-ExistingRow` function looks up rows by `sprk_toolcode`. If the JSON's `sprk_toolcode` no longer matches an existing row's `sprk_toolcode`, the script POSTs a NEW row alongside the old one — it does NOT detect that the same `sprk_handlerclass` already exists.

---

## 3. STOP-AND-SURFACE: Re-seeding NOT executed; user decision required

Per project CLAUDE.md "ADRs Are Defaults — Challenge When Warranted" principle, the original task instructions explicitly listed this as a stop-trigger:

> "Seed script upserts by `sprk_toolcode` instead of `sprk_handlerclass` (would make rename destructive)"

**This condition holds.** Re-running `Seed-TypedHandlers.ps1` after the JSON edits would create duplicate rows in Spaarke Dev, NOT rename the existing ones.

### Current state of Dataverse (verified via MCP read_query 2026-06-07)

Query: `SELECT sprk_analysistoolid, sprk_name, sprk_handlerclass, sprk_toolcode FROM sprk_analysistool WHERE sprk_handlerclass IN (<8 handler classes>)`

| Existing row(s) | `sprk_name` | `sprk_handlerclass` | `sprk_toolcode` | Source |
|---|---|---|---|---|
| `b76a58b2-…` | SYS-Date Extractor | `DateExtractorHandler` | `DATEXTR@v1` | Seeded via `Seed-TypedHandlers.ps1` (R6 task 101) |
| `0e956329-…` | Date Extractor | `DateExtractorHandler` | `TL-003` | Pre-R6 manual seed (different pathway) |
| `67518925-…` | Clause Comparison | `ClauseComparisonHandler` | `TL-002` | Pre-R6 manual seed |
| `23434c23-…` | Clause Analyzer | `ClauseAnalyzerHandler` | `TL-001` | Pre-R6 manual seed |
| `10956329-…` | Entity Extractor | `EntityExtractorHandler` | `TL-005` | Pre-R6 manual seed |
| `15956329-…` | Financial Calculator | `FinancialCalculatorHandler` | `TL-007` | Pre-R6 manual seed |
| `c1809840-…` | Risk Detector | `RiskDetectorHandler` | `TL-009` | Pre-R6 manual seed |

**Observations**:
- Only `DateExtractorHandler` has been seeded by `Seed-TypedHandlers.ps1` so far (the only row with `sprk_name` prefix "SYS-"). The remaining 7 handlers have pre-R6 `TL-001..TL-009` rows that the script has never touched.
- `DateExtractorHandler` has **TWO rows**: the R6 `DATEXTR@v1` row AND a pre-R6 `TL-003` row.
- `FinancialCalculationToolHandler` and `InvoiceExtractionToolHandler` have NO existing rows (not yet deployed).
- **Collision check** (target descriptive codes already in use): None — query for `sprk_toolcode IN ('DATE-EXTRACTOR', 'FINANCIAL-CALCULATOR', 'CLAUSE-COMPARISON', 'FINANCIAL-CALCULATION', 'ENTITY-EXTRACTOR', 'CLAUSE-ANALYZER', 'RISK-DETECTOR', 'INVOICE-EXTRACTOR')` returned 0 rows.

### Options surfaced for user decision

**Option A — Change the seed script's upsert key to `sprk_handlerclass` (lowest risk)**
Modify `Find-ExistingRow` in `Seed-TypedHandlers.ps1` to query by `sprk_handlerclass` instead of `sprk_toolcode`. Run script — DateExtractor's `DATEXTR@v1` row gets PATCHed to `DATE-EXTRACTOR`. For the other 7 handlers, the script will find ZERO rows (the existing `TL-00x` rows don't match the script's expected handler classes in a clean way — wait, actually they DO match by handler class). This needs careful thought:
  - Patching by `sprk_handlerclass` would PATCH the pre-R6 `TL-00x` rows to the new code, name, schema, etc. (effectively repurposing those rows).
  - DateExtractor would patch ONE of its two rows; which one? `Find-ExistingRow` returns `$response.value[0]` — first match. Need explicit ordering or `OnlyHandler` per-row execution.
  - **Cleanest sub-variant**: change script to query by `sprk_handlerclass`, run with `-OnlyHandler` per-handler so user can pick which row to patch (and manually delete the orphan after).

**Option B — Direct PATCH of each existing row via Web API (most explicit)**
Skip the script entirely for this rename. Issue a direct `PATCH /api/data/v9.2/sprk_analysistools(<id>)` per-row with `{ "sprk_toolcode": "<NEW>" }` (and optionally `sprk_name`, `sprk_description`, schema fields to bring pre-R6 rows up to R6 spec). User chooses which existing row to patch (e.g., for `DateExtractorHandler`, patch `DATEXTR@v1` row to new code + delete or repurpose `TL-003`). Then re-run the seed script — it'll find each row by new code and PATCH (now idempotent).

**Option C — Delete the old rows, run script fresh (destructive — user explicit approval required)**
DELETE all matching rows in the table for these 8 handler classes, then run `Seed-TypedHandlers.ps1`. Script creates 8 fresh rows with descriptive codes. Risks: any FK references in `sprk_analysisaction`, playbook nodes, or execution traces pointing at the deleted IDs would break. Need broader impact assessment first.

**Option D — Leave Dataverse alone for now; JSON-only update (CURRENT STATE)**
JSON files are now source-of-truth for any FUTURE clean-environment deploy. Existing Spaarke Dev rows continue to use old codes (cryptic + `@v1` + `TL-00x` pattern). Document the divergence; address it when deploying to a new environment or when an existing handler's behavior changes.

### Recommendation

**Option A or B**, depending on how much pre-R6 row cleanup the user wants. Option A is the smaller surgical change (one script function edit + per-handler script runs); Option B is the most explicit (zero script changes, fully under user control). Both avoid the destruction risk of C. Option D is reversible but leaves divergence.

**No action taken pending user direction.** JSON edits are committed-safe (file-level, no Dataverse mutation); the script change + Dataverse mutation step is paused awaiting decision.

---

## 4. Deployment Evidence

**Not captured** — re-seed deferred pending §3 decision. After user chooses Option A / B / C / D, re-seed (or per-row PATCH) will be executed and output captured in a follow-up amendment to this note.

---

## 5. MCP Verification

**Not executed** — verification deferred until §3 decision yields a Dataverse state change.

Pre-change state captured in §3 table above (the 7 existing rows discovered + 2 missing handlers).

---

## 6. Convention for Downstream Waves (Wave 7–9 chat-tool migrations)

The R6 plan calls for chat-tool migrations in Wave 7 (4 trivial), Wave 8 (4 citation/SSE), and Wave 9 (1 streaming). Those tasks will add NEW `sprk_analysistool` rows for the migrated tools. Convention going forward:

**For all NEW `sprk_analysistool` rows seeded during R6 (and beyond)**:
- ✅ **Use UPPER-KEBAB-CASE descriptive codes** (e.g., `DOCUMENT-SEARCH`, `KNOWLEDGE-RETRIEVAL`, `TEXT-REFINEMENT`, `VERIFY-CITATIONS`, `WEB-SEARCH`, `CODE-INTERPRETER`, `LEGAL-RESEARCH`, `WORKING-DOCUMENT-TOOLS`)
- ❌ **NO `@v1` or numeric / version suffix** in `sprk_toolcode` (versioning, if needed, will be a separate field or table strategy — not embedded in the code; cross-environment migration scripts must read cleanly)
- ❌ **NO cryptic abbreviations** (e.g., `DOCSRCH@v1`) — the 10-char column constraint that motivated the original cryptic codes has been lifted by the user's manual column extension to 100 chars
- ✅ **`sprk_handlerclass`** remains the stable runtime routing key (= `nameof` C# class); `sprk_toolcode` is the human-readable identifier used for upsert + cross-environment migration
- ✅ When adding seed JSON files for new tools, mirror the format in `infra/dataverse/sprk_analysistool-*-row.json` (descriptive code at top, no `@v1`, `_comment_toolcode` noting the convention)
- ⚠️ **Before re-running `Seed-TypedHandlers.ps1` or extending it**, consider Option A from §3 — switch upsert key to `sprk_handlerclass` for resilience to future code renames. Until then, treat `sprk_toolcode` as immutable once seeded.

---

## 7. Files Changed

- `infra/dataverse/sprk_analysistool-date-extractor-row.json` — `DATEXTR@v1` → `DATE-EXTRACTOR`
- `infra/dataverse/sprk_analysistool-financial-calculator-row.json` — `FIN-CALC@v1` → `FINANCIAL-CALCULATOR`
- `infra/dataverse/sprk_analysistool-clause-comparison-row.json` — `CLAUSE-CMP@v1` → `CLAUSE-COMPARISON`
- `infra/dataverse/sprk_analysistool-financial-calculation-row.json` — `FIN-CALC-FORMULA@v1` → `FINANCIAL-CALCULATION`
- `infra/dataverse/sprk_analysistool-entity-extractor-row.json` — `ENT-EXT@v1` → `ENTITY-EXTRACTOR`
- `infra/dataverse/sprk_analysistool-clause-analyzer-row.json` — `CLZ-AN@v1` → `CLAUSE-ANALYZER`
- `infra/dataverse/sprk_analysistool-risk-detector-row.json` — `RISK-DET@1` → `RISK-DETECTOR`
- `infra/dataverse/sprk_analysistool-invoice-extractor-row.json` — `INVEXT@v1` → `INVOICE-EXTRACTOR`
- `projects/spaarke-ai-platform-unification-r6/notes/audit-item-04-toolcode-retro-rename.md` (this file)

**NOT modified**:
- `tasks/TASK-INDEX.md` (audit, not numbered task — per instruction)
- `projects/spaarke-ai-platform-unification-r6/current-task.md` (audit, not numbered task — per instruction)
- `scripts/Seed-TypedHandlers.ps1` (paused pending Option A/B/C/D decision)
- Any `.cs` files (data-only change; no BFF publish-size delta)
- Any test files (data-only change)

---

## 8. Build / Publish-Size Impact

- **BFF publish-size delta**: 0 bytes (no `.cs` modified)
- **Test changes**: none
- **NuGet changes**: none
- **DI changes**: none
- **Endpoint changes**: none

Pure data-file edits. No CI risk surface.
