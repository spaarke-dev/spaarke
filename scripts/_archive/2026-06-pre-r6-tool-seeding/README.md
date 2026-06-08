# Archive — Pre-R6 Tool Seeding Pathway (2026-06)

**Archived**: 2026-06-08
**Reason**: Superseded by `scripts/Seed-TypedHandlers.ps1` + `infra/dataverse/sprk_analysistool-*-row.json` per R6 audit Item 4 consolidation.

---

## What's here

- `Deploy-Tools.ps1` — formerly `scripts/seed-data/Deploy-Tools.ps1`. Pre-R6 pathway for seeding `sprk_analysistool` rows. Upserted by `sprk_name` (not toolcode).
- `tools.json` — formerly `scripts/seed-data/tools.json`. Source catalog used by `Deploy-Tools.ps1`. Created the 6 pre-R6 `TL-NNN` rows (Clause Analyzer / Clause Comparison / Date Extractor / Entity Extractor / Financial Calculator / Risk Detector).

## Why archived

R6 audit Item 4 (2026-06-07 / 2026-06-08) consolidated all 8 R6 typed-handler `sprk_analysistool` rows into a single canonical R6 pathway:

- **Source of truth**: `infra/dataverse/sprk_analysistool-<handler>-row.json` (one file per handler)
- **Deploy script**: `scripts/Seed-TypedHandlers.ps1` (upserts by `sprk_handlerclass` with `sprk_name` LIKE `SYS-%` safety filter)
- **Validator**: `scripts/Test-AnalysisToolSchemaValid.ps1` (catalog-write-time JSON Schema structural validation)

During consolidation the 6 pre-R6 `TL-NNN` rows that this archived script created were PATCHed in place to R6 spec (descriptive `sprk_toolcode`, `SYS-` `sprk_name` prefix, R6 schema, R6 configuration, `sprk_availableincontexts` = 100000002 / Both). The GUIDs were preserved.

## Do NOT re-run

Re-running this script would attempt to recreate the pre-R6 rows by `sprk_name` (e.g., `"Clause Analyzer"` — note: no `SYS-` prefix). Since the consolidated canonical rows are now named `"SYS-Clause Analyzer"`, the upsert-by-name lookup would MISS the canonical row and POST a fresh duplicate row with the OLD pre-R6 schema, pre-R6 description, and `sprk_toolcode = TL-NNN`. This would re-introduce the exact divergence the audit consolidated.

The only valid reason to re-run is explicit restoration of pre-R6 state (e.g., compatibility test against a frozen pre-R6 baseline). Even then, prefer cloning these files into a sandboxed environment rather than running them against shared Spaarke Dev.

## Scope note

Only `Deploy-Tools.ps1` + `tools.json` from `scripts/seed-data/` are archived here. The other components in `scripts/seed-data/` (Actions, Knowledge, Skills, Playbooks, Output Types, Type Lookups, the master `Deploy-All-AI-SeedData.ps1` orchestrator, and the Verify-* / Query-* helpers) are NOT in scope of audit Item 4 and remain in place. The master orchestrator's call to `Deploy-Tools.ps1` will fail until either (a) `Deploy-All-AI-SeedData.ps1` is updated to skip tool seeding or to invoke `Seed-TypedHandlers.ps1` instead, or (b) the orchestrator itself is archived as part of a follow-up cleanup. This is a known, intentional limitation of the minimal-scope audit-Item-4 archival.

## Related

- `projects/spaarke-ai-platform-unification-r6/notes/audit-item-04-toolcode-retro-rename.md` — full audit narrative + per-row PATCH/POST evidence
- `scripts/Seed-TypedHandlers.ps1` — current canonical seed script
- `infra/dataverse/sprk_analysistool-*-row.json` — current canonical seed source files
