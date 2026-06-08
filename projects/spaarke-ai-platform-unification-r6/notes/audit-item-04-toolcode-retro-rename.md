# Audit Item 04 — Retroactive `sprk_toolcode` Rename to Descriptive Form

**Date**: 2026-06-07 (initial) / 2026-06-08 (consolidation executed)
**Status**: COMPLETE — 4th-pass agent executed consolidation (6 PATCH + 1 DELETE + 2 POST + script Option-A edit + legacy `Deploy-Tools.ps1`/`tools.json` archived). 8 canonical SYS-* rows verified.
**Original status (1st-pass agent)**: PARTIAL — JSON files updated; Dataverse re-seed STOP-AND-SURFACED (see §3 Decision Required)
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

## 4b. Findings A + B + C resolution (3rd-pass agent, 2026-06-08)

User direction (relayed via parent):
- **Finding A (mechanical)**: Rename `sprk_availableincontexts_value` → `sprk_availableincontexts` in the 3 affected files. No semantic change — purely fix the OData annotation typo.
- **Finding B (user override)**: All 8 typed handlers should be `sprk_availableincontexts = 100000002` (Both). Override the 3 files that defaulted to Playbook-only.
- **Finding C (mechanical)**: Flatten the `financial-calculation` JSON envelope to match siblings so `Get-PayloadFromRowJson` emits the correct sprk_* payload.

### Diff summary per file

| File | Finding A (key rename) | Finding B (Playbook → Both) | Finding C (envelope flatten) |
|---|---|---|---|
| `clause-analyzer-row.json` | ✅ `sprk_availableincontexts_value` → `sprk_availableincontexts` (kept value 100000002) | — already Both | — already flat |
| `clause-comparison-row.json` | ✅ key renamed | ✅ value 100000000 → 100000002; comment updated to cite user override 2026-06-08 | — already flat |
| `financial-calculator-row.json` | ✅ key renamed | ✅ value 100000000 → 100000002; comment updated to cite user override 2026-06-08 | — already flat |
| `financial-calculation-row.json` | — already correct key | ✅ value 100000000 → 100000002; comment updated to cite user override 2026-06-08 | ✅ flattened from `{row: {…}}` envelope to top-level `sprk_*` keys. Dropped `sprk_tooltype: 7`, `sprk_ownertype: 'SYS'`, `sprk_isimmutable: true` after MCP `describe(tables/sprk_analysistool)` confirmed NONE are real columns (real lookup is `sprk_tooltypeid` to `sprk_aitooltype`). Replaced `_task` / `_handlerSource` / `_dataverseEntity` / `deployment` envelope keys with `_comment_*` prose so the seed script's `_comment*` skip rule excludes them. Schema content + values preserved bit-for-bit otherwise. |
| `date-extractor-row.json` | — clean | — already Both | — already flat |
| `entity-extractor-row.json` | — clean | — already Both | — already flat |
| `risk-detector-row.json` | — clean | — already Both | — already flat |
| `invoice-extractor-row.json` | — clean | — already Both | — already flat |

### Validator verification (all 8 files)

Ran `scripts/Test-AnalysisToolSchemaValid.ps1` (dot-sourced) against the top-level `sprk_jsonschema` of all 8 files (serialized via `ConvertTo-Json -Depth 50 -Compress` to mimic what `Get-PayloadFromRowJson` would emit):

| File | Toolcode | `sprk_availableincontexts` | Validator |
|---|---|---|---|
| clause-analyzer | `CLAUSE-ANALYZER` | 100000002 | PASS |
| clause-comparison | `CLAUSE-COMPARISON` | 100000002 | PASS |
| date-extractor | `DATE-EXTRACTOR` | 100000002 | PASS |
| entity-extractor | `ENTITY-EXTRACTOR` | 100000002 | PASS |
| financial-calculation | `FINANCIAL-CALCULATION` | 100000002 | PASS |
| financial-calculator | `FINANCIAL-CALCULATOR` | 100000002 | PASS |
| invoice-extractor | `INVOICE-EXTRACTOR` | 100000002 | PASS |
| risk-detector | `RISK-DETECTOR` | 100000002 | PASS |

All 8 files now have uniform structure (flat top-level), correct writable field name, and consistent Both context per user direction.

---

## 5. Finding D — Pre-R6 `TL-NNN` row inventory + dependency check (3rd-pass agent, 2026-06-08)

### 5.1 Per-handler row inventory (MCP `read_query` on `sprk_analysistool`)

Query: handler-class filter across all 8 R6 handlers, ordered by `sprk_handlerclass`, `createdon`.

| Handler class | Pre-R6 row | R6 SYS- row | Notes |
|---|---|---|---|
| `ClauseAnalyzerHandler` | `23434c23-…` — name `Clause Analyzer`, toolcode `TL-001`, description "Identify clause types, assess risk, flag unusual/missing clauses", tags "search, document, retrieval, rag, semantic", `sprk_availableincontexts` = NULL, `sprk_availableadhoc` = NULL, created 2026-02-23, statecode Active | none | Pre-R6 row created via legacy seed; ADR-027 fields not populated. |
| `ClauseComparisonHandler` | `67518925-…` — name `Clause Comparison`, toolcode `TL-002`, description "Compare clauses against standard terms…", tags "analysis, retrieval, context, previous-results", contexts NULL, created 2026-02-23, Active | none | Pre-R6 only. |
| `DateExtractorHandler` | `0e956329-…` — name `Date Extractor`, toolcode `TL-003`, description "Extract and normalize dates with context…", tags "knowledge, retrieval, reference, lookup, context", contexts NULL, created 2026-02-23, Active | `b76a58b2-…` — name `SYS-Date Extractor`, toolcode `DATEXTR@v1` (NOT YET RENAMED to `DATE-EXTRACTOR`), description matches R6 JSON, contexts = 100000002 (Both), `sprk_availableadhoc` = false, created 2026-06-07, Active | TWO rows for this handler — R6 task 101 seeded the second one. R6 row's toolcode is still the old cryptic form because Seed-TypedHandlers.ps1 has not been re-run with the new descriptive code yet (this is the Option-A blocker). |
| `EntityExtractorHandler` | `10956329-…` — name `Entity Extractor\t` (note trailing tab character — data quality issue), toolcode `TL-005`, description "Extract named entities (people, orgs, dates, money, legal refs)", tags "citation, extraction, references, sourcing, evidence", contexts NULL, created 2026-02-23, Active | none | Pre-R6 only. **Side finding**: `sprk_name` has a trailing `\t` — would cause `Deploy-Tools.ps1`'s name-based upsert lookup to fail on next run. |
| `FinancialCalculationToolHandler` | none | none | Not yet seeded by either pathway. R6 task 104 JSON exists but has never been deployed. |
| `FinancialCalculatorHandler` | `15956329-…` — name `Financial Calculator`, toolcode `TL-007`, description "Extract monetary values, payment terms, financial obligations", tags "risk, red-flags, detection, compliance, warnings" (NOTE: tags mismatch — this is a Financial Calculator labeled with risk-detection tags; likely a legacy data-entry mistake), contexts NULL, created 2026-02-23, Active | none | Pre-R6 only; the description/tags reveal it was originally a placeholder for monetary-value extraction (not the R6 deterministic FxConvert/Sum/etc. handler). |
| `InvoiceExtractionToolHandler` | none | none | Not yet seeded by either pathway. |
| `RiskDetectorHandler` | `c1809840-…` — name `Risk Detector`, toolcode `TL-009`, description "Flag risks and compliance issues; contract review, risk assessment", tags NULL, contexts NULL, `sprk_availableadhoc` = false, created 2026-03-05, Active | none | Pre-R6 only (created 2 weeks later than other TL-NNN siblings — likely backfill). |

Totals: 6 of 8 R6 handlers have a pre-R6 `TL-NNN` row. 1 of 8 (`DateExtractorHandler`) has BOTH (a pre-R6 TL-003 and an R6 SYS- row). 2 of 8 (`FinancialCalculationToolHandler`, `InvoiceExtractionToolHandler`) have NEITHER row.

### 5.2 Dependency check results

#### 5.2.1 Playbook node references — Dataverse

Query 1 (toolcode strings in `sprk_playbooknode.sprk_configjson`):
`SELECT … FROM sprk_playbooknode WHERE sprk_configjson LIKE '%TL-001%' OR … '%TL-009%'`
→ **0 rows**.

Query 2 (handler-class strings in `sprk_playbooknode.sprk_configjson`):
`SELECT … FROM sprk_playbooknode WHERE sprk_configjson LIKE '%ClauseAnalyzerHandler%' OR …`
→ **0 rows**.

Query 3 (toolcode + handler-class strings in `sprk_analysisplaybook.sprk_configjson` AND `sprk_canvaslayoutjson`):
→ **0 rows** in either column for any of the 8 handlers or TL-NNN codes.

Query 4 (`sprk_analysistool.sprk_analysisid` reverse FK — could a tool be pinned to an analysis instance?):
`SELECT … FROM sprk_analysistool WHERE sprk_analysisid IS NOT NULL` → **0 rows**.

**Schema note**: There is NO direct lookup column from `sprk_playbooknode` or `sprk_analysisaction` to `sprk_analysistool`. The only structural link is `sprk_playbooknode.sprk_actionid` → `sprk_analysisaction`. Tool selection must therefore happen via JSON config (and as shown, no JSON config currently mentions any of the 8 tools). Functional implication: **the pre-R6 TL-NNN rows in Dataverse are catalog metadata only — they are not wired into any active playbook**.

#### 5.2.2 Code references in `src/`

| Hit | File | Line | Nature | Risk if pre-R6 rows mutated |
|---|---|---|---|---|
| `TL-011` | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs:241` | XML-doc-style code comment ("// calculation.* variables from FinancialCalculationToolHandler (TL-011)") | None — runtime routing is via DI-injected `_financialCalculationTool`, not toolcode string. | None. |
| `TL-009`, `TL-011` | `src/server/api/Sprk.Bff.Api/Services/Ai/IOutputOrchestratorService.cs:35-36` | XML-doc remarks on `PlaybookExecutionContext` ("extraction.* = Output from InvoiceExtractionToolHandler (TL-009)") | Documentation only. | None. |
| `SYS-TL-001..009` | `src/server/api/Sprk.Bff.Api/Services/Ai/FallbackScopeCatalog.cs:128-167` | **Hardcoded fallback catalog Names** consumed by `BuilderToolExecutor.SearchScopes` + `BuilderAgentService.BuildSystemPrompt`. Used when Dataverse returns zero tool rows (fallback only). | **Important**: these Names DO NOT correspond to any actual Dataverse row — `FallbackScopeCatalog.GetTools()` is a separately-maintained AI-builder hint list. Compared to actual Dataverse: `SYS-TL-001`=`"Entity Extractor Handler"` in fallback vs `TL-001`=`ClauseAnalyzerHandler` in Dataverse. They're already misaligned. Mutating Dataverse rows does NOT change the fallback list. |
| `SYS-TL-NNN` | `src/server/api/Sprk.Bff.Api/builder-scopes/*.json` (4 files) | AI builder agent's scope-catalog instructions (knowledge artifacts for the builder LLM) | Documentation; the LLM reads these for awareness, not as runtime keys. |
| `(TL-NNN catalog)` | `src/server/api/Sprk.Bff.Api/builder-scopes/ACT-BUILDER-004` + `005.json` | Long catalog descriptions for AI builder prompt | Documentation only. |

#### 5.2.3 Config references in `appsettings*.json`, `Program.cs`, DI modules

`Grep` for `TL-(00[1-9]|01[0-9])` across `src/`: zero matches in `appsettings.json`, `Program.cs`, or any `*Module.cs` DI file. Tool routing is by `sprk_handlerclass` (= C# nameof), not by toolcode.

#### 5.2.4 Test references

`Grep` for `TL-(00[1-9]|01[0-9])` across `tests/`: **0 matches**. No test fixture references the TL- pool.

#### 5.2.5 Parallel seed system — `scripts/seed-data/`

A separate, full-featured seed system exists at `scripts/seed-data/` (Deploy-Tools.ps1, Deploy-Playbooks.ps1, etc.) with its own JSON catalogs (`tools.json`, `playbooks.json`, `output-types.json`). This was the original pathway used to seed the pre-R6 TL-NNN rows.

**Critical mechanical detail**: `Deploy-Tools.ps1` upserts by `sprk_name` (line 65: `$filter = "$filter=sprk_name eq '$Name'"`), NOT by toolcode. The pre-R6 names (`"Clause Analyzer"`, `"Date Extractor"`, etc.) differ from the R6 names (`"SYS-Clause Analyzer"`, `"SYS-Date Extractor"`). The two seed systems are therefore **mutually idempotent** — re-running either one only touches rows it has previously created. They occupy disjoint name-spaces.

**Implication**: this is the architectural mechanism by which the pre-R6 + R6 rows co-exist in Dataverse without colliding. The pre-R6 seed system + the R6 `Seed-TypedHandlers.ps1` are **two parallel deployment pipelines** addressing different `sprk_name` domains.

### 5.3 Per-handler verdict

| Handler | Pre-R6 row? | R6 SYS- row? | Playbook deps? | Code/config/test deps? | Verdict |
|---|---|---|---|---|---|
| `ClauseAnalyzerHandler` | YES (`TL-001`) | NO | NONE | NONE (runtime); 1 fallback Name + 1 builder-scope doc reference (both inert) | **CONSOLIDATE-SAFE**: pre-R6 `TL-001` has zero active dependencies. Safe to either (a) PATCH `TL-001` row to R6 spec + new `CLAUSE-ANALYZER` toolcode + `SYS-` name (single-row consolidation), or (b) leave `TL-001` parallel and POST a new R6 row — depending on user's "consolidate vs parallel-keep" preference. |
| `ClauseComparisonHandler` | YES (`TL-002`) | NO | NONE | NONE | **CONSOLIDATE-SAFE** (same reasoning as above). |
| `DateExtractorHandler` | YES (`TL-003`) | YES (`DATEXTR@v1`) | NONE | NONE (runtime); fallback + builder-doc references inert | **CONSOLIDATE-RECOMMENDED + REQUIRES DEDUP**: TWO active rows for the same handler is wasteful. Recommend (a) PATCH the R6 `DATEXTR@v1` row to descriptive `DATE-EXTRACTOR` toolcode, then (b) DEACTIVATE the pre-R6 `TL-003` row (set `statecode=1`, do not delete — preserves audit trail). Alternative: PATCH `TL-003` instead and deactivate the R6 row; less attractive because the R6 row already has correct `sprk_availableincontexts` + `sprk_availableadhoc` values. |
| `EntityExtractorHandler` | YES (`TL-005`) | NO | NONE | NONE | **CONSOLIDATE-SAFE + SIDE FIX**: pre-R6 `TL-005` has a trailing `\t` in `sprk_name` ("Entity Extractor\t"). Any consolidation pathway should normalize this. |
| `FinancialCalculationToolHandler` | NO | NO | NONE | NONE | **CLEAN-POST**: no existing row to consolidate. R6 seed simply POSTs a new row. No collision risk. |
| `FinancialCalculatorHandler` | YES (`TL-007`) | NO | NONE | NONE | **CONSOLIDATE-WITH-CAUTION**: pre-R6 `TL-007` has description + tags that suggest it was originally intended for risk-detection ("risk, red-flags, detection, compliance, warnings"), NOT the R6 FinancialCalculatorHandler semantics (Sum/Tax/Discount/FxConvert/etc.). Whoever created `TL-007` probably attached the wrong handler class. Safe to PATCH because no playbook depends on it, but the semantic mismatch should be acknowledged in the consolidation commit message. |
| `InvoiceExtractionToolHandler` | NO | NO | NONE | NONE | **CLEAN-POST**: same as FinancialCalculationToolHandler. |
| `RiskDetectorHandler` | YES (`TL-009`) | NO | NONE | NONE | **CONSOLIDATE-SAFE**. |

### 5.4 Recommendation to user

**All 6 pre-R6 `TL-NNN` rows have ZERO production dependencies** — no playbook references them, no runtime BFF code references them, no tests reference them, no configuration references them. The only `TL-NNN` text in `src/` is:
- XML-doc comments in 2 C# files (purely documentation; can be ignored or refreshed)
- An unrelated `SYS-TL-NNN` fallback catalog in `FallbackScopeCatalog.cs` (display Names for AI builder hints; already misaligned to actual Dataverse rows; not affected by Dataverse mutations)
- Builder-scope JSON artifacts (LLM prompt knowledge, not runtime keys)

The parallel `scripts/seed-data/` deployment system upserts by `sprk_name` (not toolcode), so it occupies a disjoint name-space from `Seed-TypedHandlers.ps1` (which uses `SYS-` prefix). Re-running either system would NOT collide with the other.

**Recommended consolidation path** (subject to user approval):
1. **For the 6 handlers with pre-R6 rows + no R6 row** (Clause Analyzer/Comparison, Entity Extractor, Financial Calculator, Risk Detector — and Date Extractor which has both): PATCH the existing pre-R6 row to R6 spec (descriptive toolcode + SYS- name + R6 description + R6 schema + R6 configuration + contexts = 100000002). For Date Extractor, ALSO deactivate the duplicate `DATEXTR@v1` R6 row after patching `TL-003`. This is essentially Option B from §3 (direct PATCH) executed per-row with `sprk_handlerclass` selecting which existing row to target.
2. **For the 2 handlers with no existing row** (FinancialCalculation, InvoiceExtraction): POST a new row.
3. **For the seed script** (`Seed-TypedHandlers.ps1`): change `Find-ExistingRow` to query by `sprk_handlerclass` (Option A) so future re-runs are idempotent regardless of toolcode renames. After the one-time PATCH above, the script becomes pure UPSERT on handler-class — exactly the "stable runtime routing key" convention from §6.

**Net effect after consolidation**: 8 canonical R6 rows in Dataverse (one per handler class), each with a descriptive `sprk_toolcode` and a `SYS-` prefixed `sprk_name`, all chat-exposable (contexts = 100000002), all carrying the R6 JSON schema. Zero duplicates. The pre-R6 `TL-NNN` numbering disappears (rows physically overwritten via PATCH). The `scripts/seed-data/tools.json` legacy seed system can be retired or left dormant — it has zero dependencies in current code and only matters if a user manually re-runs `Deploy-Tools.ps1`, which would re-create `"Clause Analyzer"` etc. as duplicates alongside the consolidated `SYS-Clause Analyzer` rows. **Suggested follow-up**: archive `scripts/seed-data/tools.json` + `Deploy-Tools.ps1` (or rename to `*.archived`) once the consolidated rows are confirmed in Spaarke Dev.

**Risk register for consolidation**:
- `TL-007` (Financial Calculator pre-R6 row) has tags suggesting a misattribution at original seeding. Acknowledged in the consolidation commit message.
- `TL-005` (Entity Extractor) has a trailing tab character in its name. Normalized away by PATCH.
- `TL-001`..`TL-009` GUIDs would be preserved (since PATCH preserves the GUID); if any external system happens to cache these GUIDs from a prior export, the names/codes would change but the IDs remain stable.

**No mutation executed by 3rd-pass agent.** This dispatch was inventory + dependency check only. Awaiting user decision on:
1. Consolidate (PATCH the 6 pre-R6 rows in place per recommendation) vs parallel-keep (POST 8 new R6 rows alongside, leaving pre-R6 untouched).
2. If consolidate: archive `scripts/seed-data/tools.json` / `Deploy-Tools.ps1` to prevent accidental re-creation of pre-R6 rows on future legacy seed runs?

---

## 3b. 2nd-pass agent re-engagement (2026-06-07) — Option A elected; 4 NEW stop-and-surface findings

User instructed: "Change the upsert key from `sprk_toolcode` to `sprk_handlerclass`. Re-run script. Verify via MCP." That is Option A.

Before touching the script or executing the deploy, the 2nd-pass agent did a deeper inspection of the 8 JSON files AND re-queried Dataverse. Four issues were discovered that BLOCK clean Option-A execution. Each is filed as a binding STOP-AND-SURFACE per CLAUDE.md "ADRs Are Defaults — Challenge When Warranted" principle.

### Finding A — 3 JSON files write to the read-only OData annotation field (`sprk_availableincontexts_value`) instead of the writable column (`sprk_availableincontexts`)

Files affected (grep evidence):
- `infra/dataverse/sprk_analysistool-clause-analyzer-row.json` line 10: `"sprk_availableincontexts_value": 100000002`
- `infra/dataverse/sprk_analysistool-clause-comparison-row.json` line 10: `"sprk_availableincontexts_value": 100000000`
- `infra/dataverse/sprk_analysistool-financial-calculator-row.json` line 10: `"sprk_availableincontexts_value": 100000000`

The `_value` suffix is the OData annotation form for lookup foreign-keys / option-set value reads. It is NOT a writable column in the Web API PATCH/POST surface. Attempting to POST a payload with `sprk_availableincontexts_value` will either be silently ignored OR rejected by the Web API (depends on Dataverse version). Either way, the field will NOT be set as intended.

**Fix**: Rename the JSON key from `sprk_availableincontexts_value` → `sprk_availableincontexts` in those 3 files (no value change).

### Finding B — 3 JSON files set `sprk_availableincontexts = 100000000` (Playbook-only); user instructions require `100000002` (Both)

The user's task instructions for this audit item explicitly state: *"The `sprk_availableincontexts` is `100000002` (Both) — required for chat exposure"*. Three JSON files violate this:

- `sprk_analysistool-clause-comparison-row.json`: `100000000` (file's inline comment says: "no chat-direct invocation in R6")
- `sprk_analysistool-financial-calculation-row.json`: `100000000` (file's inline comment says: "exposed to playbook orchestration only by default")
- `sprk_analysistool-financial-calculator-row.json`: `100000000` (file's inline comment says: "no chat-direct invocation in R6")

The JSON files DOCUMENT a deliberate scoping decision: ClauseComparison + FinancialCalculator + FinancialCalculation are playbook-only by design ("invoked from Playbook nodes after upstream extraction; no chat-direct invocation in R6"). The user's instructions appear to assume all 8 should be Both.

**TRADE-OFF surface**: Which is right?
- If JSON authors' intent stands → leave 3 handlers at Playbook-only (defensible per R6 chat scope reduction); document divergence from instruction text.
- If user wants ALL 8 chat-exposed → change 3 JSON values from `100000000` to `100000002`; verify R6 FR-18/19/20 acceptance criteria still hold.

This is a deliberate decision the user should make, NOT a silent fix. Default recommendation: keep authors' intent (playbook-only for the 3), document at deployment that only 5 of 8 are chat-visible.

### Finding C — `financial-calculation` JSON file has a different structural envelope than its 7 siblings

`infra/dataverse/sprk_analysistool-financial-calculation-row.json` wraps its Dataverse columns inside a `"row":` object alongside top-level meta-keys (`_comment`, `_task`, `_handlerSource`, `_dataverseEntity`, `row`, `deployment`):

```json
{
  "_comment": "Seed row for FinancialCalculationToolHandler ...",
  "row": {
    "sprk_name": "Financial Calculation (formula)",
    "sprk_handlerclass": "FinancialCalculationToolHandler",
    "sprk_toolcode": "FINANCIAL-CALCULATION",
    ...
  },
  "deployment": { ... }
}
```

The 7 sibling files put `sprk_*` columns at the TOP level. `Get-PayloadFromRowJson` in `Seed-TypedHandlers.ps1` iterates `$obj.PSObject.Properties` at the TOP level and only skips `_comment*` keys. For this file, the iteration would emit a payload with `_task`, `_handlerSource`, `_dataverseEntity`, `row`, `deployment` keys (none of which are valid Dataverse columns) and ZERO `sprk_*` columns. The seed would either fail at Web API or write a row with all nulls.

**Fix**: Restructure `sprk_analysistool-financial-calculation-row.json` to match the sibling pattern — flatten `row.*` to top level, drop `_task`, `_handlerSource`, `_dataverseEntity`, `deployment` keys (or rename them to `_comment_*` so they're skipped). The script's per-key skip rule is `StartsWith("_comment")`.

### Finding D — Pre-R6 `TL-NNN` rows DO have `sprk_handlerclass` matching the R6 handler class names (Option A collision)

MCP query 2026-06-07 (re-run by 2nd-pass agent):

| `sprk_handlerclass` | Pre-R6 row (TL-NNN) | R6 row (SYS- / DATEXTR@v1) |
|---|---|---|
| `DateExtractorHandler` | `0e956329-…` (`TL-003`, `Date Extractor`) | `b76a58b2-…` (`DATEXTR@v1`, `SYS-Date Extractor`, contexts=100000002) |
| `ClauseAnalyzerHandler` | `23434c23-…` (`TL-001`) | (none — never seeded) |
| `ClauseComparisonHandler` | `67518925-…` (`TL-002`) | (none) |
| `EntityExtractorHandler` | `10956329-…` (`TL-005`) | (none) |
| `FinancialCalculatorHandler` | `15956329-…` (`TL-007`) | (none) |
| `RiskDetectorHandler` | `c1809840-…` (`TL-009`) | (none) |
| `FinancialCalculationToolHandler` | (none) | (none) |
| `InvoiceExtractionToolHandler` | (none) | (none) |

Under Option A (lookup by `sprk_handlerclass`), `Find-ExistingRow` returns `$response.value[0]` — the FIRST match. For 6 of 7 handler classes that have a TL-NNN row, the script would PATCH the pre-R6 TL-NNN row with the R6 descriptive code + R6 name + R6 schema + R6 config. For `DateExtractorHandler` (two existing rows), the script picks one non-deterministically (ordering depends on Web API server-side sort, typically createdon DESC or PK).

**Outcomes if Option A is executed as-is without resolving Finding D**:
1. The 6 pre-R6 `TL-001/-002/-003/-005/-007/-009` rows get REPURPOSED — their names change from "Clause Analyzer" → "SYS-Clause Analyzer", toolcodes change from `TL-001` → `CLAUSE-ANALYZER`, and their schemas/configs change to R6 spec. Any pre-R6 playbook or chat surface that references the TL-NNN code (NOT handlerclass) would break.
2. `DateExtractorHandler` gets ONE of its two rows patched (non-deterministic). The other becomes an orphan with stale metadata.
3. `FinancialCalculationToolHandler` and `InvoiceExtractionToolHandler` get NEW rows POSTed (no collision; clean).

**TRADE-OFF surface**: The script change AS INSTRUCTED would mutate pre-R6 rows. This may be intended (consolidate pre-R6 + R6 into single canonical rows) or unintended (clobbering pre-R6 catalogs that some other system pathway depends on). The user must confirm.

### Decision matrix

| Action | Finding A (field-name typo) | Finding B (chat-context scope) | Finding C (envelope shape) | Finding D (TL-NNN collision) |
|---|---|---|---|---|
| Required before any deploy | YES (will silently fail to set the field) | NEEDS USER DECISION | YES (will deploy malformed row) | NEEDS USER DECISION |
| Defaults if proceeding without user input | Fix the 3 typos (correctness) | Keep authors' intent (3 playbook-only, 5 Both) | Restructure file (correctness) | DO NOT TOUCH pre-R6 TL-NNN rows — modify script to filter `sprk_name startswith 'SYS-'` OR `sprk_toolcode` matching descriptive form, so only the R6-seeded subset is upserted |

**No script change, no JSON edits, no Dataverse mutation made by 2nd-pass agent.** All four findings surfaced for user decision.

### Suggested resolution sequence

1. **User confirms Finding B intent** (Playbook-only for 3 handlers OK, or change all to Both).
2. **User confirms Finding D scope** (touch pre-R6 TL-NNN rows, or filter them out).
3. **2nd-pass agent fixes Findings A, C** (mechanical correctness) per user confirmation on B.
4. **2nd-pass agent edits `Seed-TypedHandlers.ps1`** with the right upsert-key strategy (`sprk_handlerclass` with optional `sprk_name startswith 'SYS-'` filter, depending on D answer).
5. **Run the seed script** and capture output.
6. **Verify via MCP** per the 8-row checklist.

---

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
- Any `.cs` files (data-only change; no BFF publish-size delta)
- Any test files (data-only change)

**Modified in 2026-06-08 consolidation (4th-pass agent)**:
- `scripts/Seed-TypedHandlers.ps1` — Option A executed (`Find-ExistingRow` upsert key changed from `sprk_toolcode` to `sprk_handlerclass` with `sprk_name LIKE 'SYS-%'` safety filter; header docs + banner log line updated)
- `scripts/_archive/2026-06-pre-r6-tool-seeding/Deploy-Tools.ps1` — moved from `scripts/seed-data/` via `git mv`
- `scripts/_archive/2026-06-pre-r6-tool-seeding/tools.json` — moved from `scripts/seed-data/` via `git mv`
- `scripts/_archive/2026-06-pre-r6-tool-seeding/README.md` — new (archival rationale + DO-NOT-RE-RUN warning + scope note)

---

## 6. Consolidation execution (2026-06-08, 4th-pass agent)

User approved the full plan (steps 1-6 per audit-item-04 instructions). Execution captured below.

### Step 1 — PATCH 6 pre-R6 TL- rows to R6 spec

All 6 patches succeeded via MCP `update_record`. Each PATCH set: `sprk_toolcode` (descriptive UPPER-KEBAB), `sprk_name` (SYS-* prefix), `sprk_description`, `sprk_jsonschema`, `sprk_configuration`, `sprk_availableincontexts` = 100000002 (Both). `sprk_handlerclass` was already correct (matched the JSON file's handler class name).

| Handler class | Row GUID (preserved) | OLD → NEW `sprk_toolcode` | OLD → NEW `sprk_name` |
|---|---|---|---|
| `ClauseAnalyzerHandler` | `23434c23-d310-f111-8342-7ced8d1dc988` | `TL-001` → `CLAUSE-ANALYZER` | `Clause Analyzer` → `SYS-Clause Analyzer` |
| `ClauseComparisonHandler` | `67518925-d310-f111-8342-7c1e520aa4df` | `TL-002` → `CLAUSE-COMPARISON` | `Clause Comparison` → `SYS-Clause Comparison` |
| `DateExtractorHandler` | `0e956329-d310-f111-8342-7ced8d1dc988` | `TL-003` → `DATE-EXTRACTOR` | `Date Extractor` → `SYS-Date Extractor` |
| `EntityExtractorHandler` | `10956329-d310-f111-8342-7ced8d1dc988` | `TL-005` → `ENTITY-EXTRACTOR` | `Entity Extractor\t` (trailing tab) → `SYS-Entity Extractor` (normalized) |
| `FinancialCalculatorHandler` | `15956329-d310-f111-8342-7ced8d1dc988` | `TL-007` → `FINANCIAL-CALCULATOR` | `Financial Calculator` → `SYS-Financial Calculator` |
| `RiskDetectorHandler` | `c1809840-f718-f111-8343-7ced8d1dc988` | `TL-009` → `RISK-DETECTOR` | `Risk Detector` → `SYS-Risk Detector` |

Side note: the `EntityExtractorHandler` row's trailing `\t` data-quality issue (originally flagged in §5.2) was normalized away as part of the PATCH.

### Step 2 — DELETE redundant `DATEXTR@v1` row

GUID `b76a58b2-b862-f111-ab0c-000d3a4d8152` (formerly `sprk_toolcode = DATEXTR@v1`, `sprk_name = SYS-Date Extractor`, created 2026-06-07 by the R6 task 101 first-pass seed) — DELETEd via MCP `delete_record`. Confirmed by re-query.

Rationale: `DateExtractorHandler` had TWO active rows post-§3 (the pre-R6 `TL-003` patched in Step 1 + this R6 first-seed duplicate). Patching `TL-003` consolidated the canonical row at the older GUID; the newer GUID becomes redundant and is deleted to enforce the one-row-per-handler invariant.

### Step 3 — CREATE 2 new SYS- rows for handlers with no existing row

| Handler class | New row GUID | `sprk_toolcode` | `sprk_name` |
|---|---|---|---|
| `FinancialCalculationToolHandler` | `4738efd9-3763-f111-ab0c-70a8a53ec687` | `FINANCIAL-CALCULATION` | `SYS-Financial Calculation (formula)` |
| `InvoiceExtractionToolHandler` | `c636f1df-3763-f111-ab0c-70a8a53ec687` | `INVOICE-EXTRACTOR` | `SYS-Invoice Extractor` |

Both POSTs verified via MCP `read_query`. `sprk_availableincontexts` = 100000002 (Both); `sprk_jsonschema` + `sprk_configuration` serialized from the JSON files via the same convention `Seed-TypedHandlers.ps1` uses.

### Step 4 — Switch `Seed-TypedHandlers.ps1` upsert key to `sprk_handlerclass` with `SYS-%` safety filter

Edited `scripts/Seed-TypedHandlers.ps1`:
- `.SYNOPSIS` / `.DESCRIPTION` header updated to document new upsert key + filter rationale.
- `Find-ExistingRow` function: parameter `$ToolCode` → `$HandlerClass`; query filter `sprk_toolcode eq '$ToolCode'` → `sprk_handlerclass eq '$HandlerClass' and startswith(sprk_name,'SYS-')`. Inline doc-comment explains the rationale (handler class = stable runtime key; SYS- prefix prevents accidental PATCH of legacy non-R6 rows).
- Main loop call-site updated to pass `$handlerClass` instead of `$toolCode`.
- New log line on banner: `Upsert key  : sprk_handlerclass with sprk_name LIKE 'SYS-%' safety filter`.

Existing `Test-AnalysisToolSchemaValid.ps1` dot-source + JSON-schema validation gate preserved unchanged.

#### Idempotency re-run (2026-06-08)

Ran `Seed-TypedHandlers.ps1` against `https://spaarkedev1.crm.dynamics.com` (post-Step 1/2/3 state). All 8 handlers matched their canonical SYS- row by handler-class lookup and PATCHed — zero new POSTs, zero error/warning. Output:

```
--- FinancialCalculatorHandler (FINANCIAL-CALCULATOR) ---
  Existing row found (sprk_analysistoolid = 15956329-...) — PATCHing
  Patched.
--- EntityExtractorHandler (ENTITY-EXTRACTOR) ---
  Existing row found (sprk_analysistoolid = 10956329-...) — PATCHing
  Patched.
--- DateExtractorHandler (DATE-EXTRACTOR) ---
  Existing row found (sprk_analysistoolid = 0e956329-...) — PATCHing
  Patched.
--- InvoiceExtractionToolHandler (INVOICE-EXTRACTOR) ---
  Existing row found (sprk_analysistoolid = c636f1df-...) — PATCHing
  Patched.
--- RiskDetectorHandler (RISK-DETECTOR) ---
  Existing row found (sprk_analysistoolid = c1809840-...) — PATCHing
  Patched.
--- FinancialCalculationToolHandler (FINANCIAL-CALCULATION) ---
  Existing row found (sprk_analysistoolid = 4738efd9-...) — PATCHing
  Patched.
--- ClauseComparisonHandler (CLAUSE-COMPARISON) ---
  Existing row found (sprk_analysistoolid = 67518925-...) — PATCHing
  Patched.
--- ClauseAnalyzerHandler (CLAUSE-ANALYZER) ---
  Existing row found (sprk_analysistoolid = 23434c23-...) — PATCHing
  Patched.
```

Post-rerun count verified at 8 (no new rows). Test-AnalysisToolSchemaValid invocations completed for all 8 rows without failure — the structural validator gate is intact.

### Step 5 — Archive legacy seed pathway

`scripts/seed-data/Deploy-Tools.ps1` + `scripts/seed-data/tools.json` moved via `git mv` to:

- `scripts/_archive/2026-06-pre-r6-tool-seeding/Deploy-Tools.ps1`
- `scripts/_archive/2026-06-pre-r6-tool-seeding/tools.json`
- `scripts/_archive/2026-06-pre-r6-tool-seeding/README.md` (new — documents archival rationale + DO-NOT-RE-RUN warning + scope note re: other `scripts/seed-data/` components left in place)

The `scripts/_archive/` directory already exists in the repo with other archived artifacts (`Debug-RegistrationFailure.ps1`, etc.), so the new subdirectory follows existing repo convention.

**Surfaced scope finding (informational)**: `scripts/seed-data/` is a comprehensive legacy seed system covering 7 component types (Type Lookups, Actions, Tools, Knowledge, Skills, Playbooks, Output Types) plus a master orchestrator `Deploy-All-AI-SeedData.ps1`. Only `Deploy-Tools.ps1` + `tools.json` were archived (the audit-Item-4 scope). The master orchestrator will now break if invoked (it references `Deploy-Tools.ps1` which is no longer at its original path). This is documented in the archive README. Broader cleanup of `scripts/seed-data/` is out of scope for audit-Item-4 and should be tracked as a follow-up.

### Step 6 — Final verification

MCP query confirmed exactly 8 rows, one per R6 handler class, all with descriptive `sprk_toolcode` values and `sprk_availableincontexts = 100000002` (Both):

| # | Handler class | GUID | `sprk_toolcode` | `sprk_name` |
|---|---|---|---|---|
| 1 | `ClauseAnalyzerHandler` | `23434c23-d310-f111-8342-7ced8d1dc988` | `CLAUSE-ANALYZER` | `SYS-Clause Analyzer` |
| 2 | `ClauseComparisonHandler` | `67518925-d310-f111-8342-7c1e520aa4df` | `CLAUSE-COMPARISON` | `SYS-Clause Comparison` |
| 3 | `DateExtractorHandler` | `0e956329-d310-f111-8342-7ced8d1dc988` | `DATE-EXTRACTOR` | `SYS-Date Extractor` |
| 4 | `EntityExtractorHandler` | `10956329-d310-f111-8342-7ced8d1dc988` | `ENTITY-EXTRACTOR` | `SYS-Entity Extractor` |
| 5 | `FinancialCalculationToolHandler` | `4738efd9-3763-f111-ab0c-70a8a53ec687` | `FINANCIAL-CALCULATION` | `SYS-Financial Calculation (formula)` |
| 6 | `FinancialCalculatorHandler` | `15956329-d310-f111-8342-7ced8d1dc988` | `FINANCIAL-CALCULATOR` | `SYS-Financial Calculator` |
| 7 | `InvoiceExtractionToolHandler` | `c636f1df-3763-f111-ab0c-70a8a53ec687` | `INVOICE-EXTRACTOR` | `SYS-Invoice Extractor` |
| 8 | `RiskDetectorHandler` | `c1809840-f718-f111-8343-7ced8d1dc988` | `RISK-DETECTOR` | `SYS-Risk Detector` |

Cross-check — MCP query for any remaining cryptic codes returned zero rows:

```sql
SELECT … FROM sprk_analysistool
WHERE sprk_toolcode IN ('DATEXTR@v1','TL-001','TL-002','TL-003','TL-005','TL-007','TL-009')
→ []
```

No `@v1`, no `TL-NNN`, no cryptic 10-char codes remain on any of the 8 R6 handler rows.

---

## 7. Final state verification (per-handler)

| Handler class | Row GUID | `sprk_toolcode` | `sprk_name` | `sprk_availableincontexts` |
|---|---|---|---|---|
| `ClauseAnalyzerHandler` | `23434c23-d310-f111-8342-7ced8d1dc988` | `CLAUSE-ANALYZER` | `SYS-Clause Analyzer` | 100000002 (Both) |
| `ClauseComparisonHandler` | `67518925-d310-f111-8342-7c1e520aa4df` | `CLAUSE-COMPARISON` | `SYS-Clause Comparison` | 100000002 (Both) |
| `DateExtractorHandler` | `0e956329-d310-f111-8342-7ced8d1dc988` | `DATE-EXTRACTOR` | `SYS-Date Extractor` | 100000002 (Both) |
| `EntityExtractorHandler` | `10956329-d310-f111-8342-7ced8d1dc988` | `ENTITY-EXTRACTOR` | `SYS-Entity Extractor` | 100000002 (Both) |
| `FinancialCalculationToolHandler` | `4738efd9-3763-f111-ab0c-70a8a53ec687` | `FINANCIAL-CALCULATION` | `SYS-Financial Calculation (formula)` | 100000002 (Both) |
| `FinancialCalculatorHandler` | `15956329-d310-f111-8342-7ced8d1dc988` | `FINANCIAL-CALCULATOR` | `SYS-Financial Calculator` | 100000002 (Both) |
| `InvoiceExtractionToolHandler` | `c636f1df-3763-f111-ab0c-70a8a53ec687` | `INVOICE-EXTRACTOR` | `SYS-Invoice Extractor` | 100000002 (Both) |
| `RiskDetectorHandler` | `c1809840-f718-f111-8343-7ced8d1dc988` | `RISK-DETECTOR` | `SYS-Risk Detector` | 100000002 (Both) |

Invariants confirmed:
- Exactly 8 rows total across the 8 R6 handler classes (1:1).
- Every `sprk_toolcode` is UPPER-KEBAB-CASE, descriptive, no `@v1`, no `TL-NNN`.
- Every `sprk_name` has `SYS-` prefix (consistent with the seed script's new safety filter).
- Every `sprk_availableincontexts` is 100000002 (Both) per user direction 2026-06-08 (Finding B override).
- `DATEXTR@v1` row deleted (no duplicate for DateExtractorHandler).
- 6 of the 8 row GUIDs are the pre-R6 GUIDs preserved through PATCH — any external system that cached these IDs (e.g., a prior export) still resolves to a valid catalog entry.

---

## 8. Build / Publish-Size Impact

- **BFF publish-size delta**: 0 bytes (no `.cs` modified)
- **Test changes**: none
- **NuGet changes**: none
- **DI changes**: none
- **Endpoint changes**: none

Pure data-file edits. No CI risk surface.
