# AI Seed Drift Resolution — 2026-08

> **Project**: `customer-provisioning-orchestration-r1`
> **Task**: 004 (Phase A doc consolidation + audits)
> **Scope**: Read-only reconciliation of two-source AI seed drift
> **Consumers**: Phase C' **H12a** (AI seed chain per FR-15) + **H12b** (app-config seed per FR-16) declarative manifest authoring
> **Binding rule**: This note DECIDES; H12a/H12b IMPLEMENTS. No seed file was edited in the course of writing this note.
> **Author**: task-execute (Wave 0 Batch 1, sub-agent)

---

## 1. Background

Two source trees hold AI-platform seed data today:

| Tree | Heritage | Latest commit date range | Deployer entry point |
|---|---|---|---|
| `scripts/seed-data/` | **MVP** — AI Document Intelligence R4 | 2026-01-05 → 2026-02-13 | `scripts/seed-data/Deploy-All-AI-SeedData.ps1` (orchestrates per-type `Deploy-*.ps1`) |
| `infra/dataverse/` | **R7** — post-ADR-039 catalog governance | 2026-06-10 → 2026-08-13 | `scripts/Deploy-Playbook.ps1` (individual playbook files) + `scripts/dataverse/Seed-PlaybookConsumers.ps1` (mirror round-trip) |

The MVP tree predates **ADR-039** (accepted 2026-07-05) which introduced the *single AI routing surface* invariant (`sprk_playbookconsumer` Binding table) and the "one catalog, round-tripping seed" contract. The R7 tree was authored **against** ADR-039. The drift was recorded as **R14** in `design.md §12` and Gap 1 in `PROJECT-UPDATE §6`; **spec.md FR-15** mandates that H12a resolve it via a declarative manifest naming the authoritative source per artifact. This note is that per-artifact declaration.

---

## 2. Governing invariants (must not be violated by any decision below)

- **ADR-039 MUST NOT**: no routing config surface outside the `sprk_playbookconsumer` Binding table (line 69: *"the audit found four"*).
- **ADR-039 MUST NOT**: `spaarke-playbook-embeddings` index is retired (spec §3 R14 / project CLAUDE.md ADR-039 row).
- **ADR-039 MUST NOT**: no second intent-detection mechanism.
- **spec.md FR-15**: seed chain terminates at *playbook consumers*; `sprk_aimodeldeployment` rows are H12a placeholders that H12c populates.
- **spec.md FR-16**: H12b app-config seed is DAG-parallel with H12a (no dependency chain).
- **`Seed-PlaybookConsumers.ps1` (lines 42–45)**: *"The Binding table is the ONE routing surface — the mirror is a projection OF the table, never a second source of truth."* — this is the round-trip contract H12a inherits.
- **Root CLAUDE.md §11**: default to reuse. No decision below introduces a NEW seed source; every "authoritative" pick is one of the two existing trees or is annotated as a delta H12a must construct from live-environment export.

---

## 3. Direct evidence inventory (raw file/commit facts)

### 3a. `scripts/seed-data/` — MVP tree

| File | Records | git latest | Deployer |
|---|---|---|---|
| `type-lookups.json` | 4 typed lookup sets (action / tool / skill / knowledge types) | 2026-01-08 (#103) | `Deploy-TypeLookups.ps1` |
| `actions.json` | 6 actions (Extract Entities, Classify, …) | 2026-02-13 (Finance addition) | `Deploy-Actions.ps1` |
| `tools.json` | **MISSING** (deployer references but file does not exist in tree) | — | `Deploy-Tools.ps1` (would fail) |
| `knowledge.json` + `knowledge-content/` | 5 knowledge records + attached content files (km-006, km-010, km-013, km-016, km-017 …) | 2026-01-05 (#97) | `Deploy-Knowledge.ps1` |
| `skills.json` | 3 skills (Contract Analysis, Risk Assessment, …) | 2026-01-05 (#97) | `Deploy-Skills.ps1` |
| `playbooks.json` | 4 MVP playbooks: **Quick Document Review (PB-001)**, **Contract Analysis (PB-002)**, **Document Profile (PB-011)**, **Risk Scan (PB-004)** | 2026-02-13 | `Deploy-Playbooks.ps1` |
| `output-types.json` | 5 output types for Document Profile (`sprk_tldr` / `sprk_summary` / `sprk_keywords` / `sprk_documenttype` / `sprk_entities`) | 2026-01-05 (#97) | `Deploy-OutputTypes.ps1` |

**Latent bug** (found while auditing): `Deploy-All-AI-SeedData.ps1` lines 58–83 declare `tools.json` as a required file and fail prereq check if absent — but no `tools.json` is present in `scripts/seed-data/`. Running the deployer today exits with `✗ tools.json not found`. Interpretation: tool authoring migrated to `infra/dataverse/` (per-tool rows) without back-porting; the MVP deployer's tool step is dead code. H12a must not resurrect it.

### 3b. `infra/dataverse/` — R7 tree

| Artifact family | Path | Files | git latest |
|---|---|---|---|
| Actions | `actions/*.action.json` | 17 (agreement-classify, agreement-review, compose-*, create-task-from-email, list-tasks, nda-standard-summary, propose-field-updates, suggest-followups, triage-email) | 2026-08-07 (master merge) |
| Input schemas | `inputschemas/*.input.schema.json` | 20 | 2026-08-07 |
| Output schemas | `outputschemas/*.schema.json` | 21 | 2026-08-07 |
| Playbooks | `playbooks/*.json` | 1 real + 1 target-state (`summarize-document-for-workspace-v1-multinode.json` — marked *UNDEPLOYED BY DECISION* per `$comment-051-ruling`) | 2026-07-07 (task 051) |
| Tools (per-row) | `sprk_analysistool-*-row.json` | **39** individual tool rows (analysis-refine, citation-verify, clause-analyzer, dataverse-{crud/query/search}, email-draft*, memory-write, recall-session-file, web-search, …) | 2026-08-13 (r3 UAT fix) |
| Playbook consumers (Binding) | `sprk_playbookconsumer-rows.json` | 1 mirror file (rows array — the "one routing surface" projection) | 2026-07-07 (task 051 regen) |
| Agreement types | `sprk_agreementtype-rows.json` | 1 mirror | 2026-07-31 (agreements-r1 W4) |
| Action outputSchema patches | `sprk_analysisaction-outputschemajson.json` | 1 patch mirror | 2026-07-07 |

**Documentary provenance** (from `Seed-PlaybookConsumers.ps1` header, lines 7–12): *"REGENERATED 2026-07-07 by spaarke-ai-architecture-redesign-r1 task 051 (FR-P4-02) from the LIVE spaarkedev1 Binding table. The previous version of this script was a stale chat-routing-redesign-r1 (2026-06-24) projection: 7 playbook-only rows on the abandoned R4 taxonomy, none of the FR-P0-03 extended columns."*

### 3c. App-config (FR-16) sources (neither in `scripts/seed-data` nor `infra/dataverse`)

| Artifact | Current source | Notes |
|---|---|---|
| DataGrid configs (`sprk_gridconfiguration`) | `scripts/seed-reconciliation-gridconfig.ps1` (2 rows: NEEDS_REVIEW, EMAIL_REVIEW_ALL/COMPLETED) | Reads JSON from `src/client/shared/Spaarke.Communication.Components/**` — *config-in-code* pattern. Idempotent by id/name. |
| Field-mapping profiles + rules (`sprk_fieldmappingprofile` / `sprk_fieldmappingrule`) | **No seed script** — maker-authored per `docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md`; Web-API seeding recipe documented but no repo JSON | `sprk_fieldmappingprofiles` + `sprk_fieldmappingrules` entity sets; profile+rule schema in framework doc. |
| System workspace layouts (`sprk_workspacelayout`) | `scripts/system-layouts.json` + `scripts/Deploy-SystemWorkspaceLayouts.ps1` (Round-8 W2a task 108; `sprk_issystem=true`) | Single declarative + idempotent source; also idempotent schema half. |
| Chart definitions | `scripts/create-test-chartdefinitions.ps1`, `scripts/Create-UpcomingTodosChartDefinitions.ps1` (per-chart-family scripts, not manifest-driven) | No consolidated JSON manifest today. |

---

## 4. Authoritative-source decision matrix

Per FR-15 (AI seed chain) + FR-16 (app-config seed). Every artifact type appears exactly once.

### 4a. FR-15 AI seed chain — H12a manifest

| # | Artifact | scripts/seed-data current | infra/dataverse current | **AUTHORITATIVE for H12a** | Rationale (concrete evidence) |
|---|---|---|---|---|---|
| 1 | **type-lookups** (action/tool/skill/knowledge types) | `type-lookups.json` (4 sets, 2026-01-08) | — (none) | **scripts/seed-data/type-lookups.json** | ONLY source; global-option-set backing values that both trees implicitly depend on. Neither ADR-039 nor R7 redefined the option sets. Migration delta = re-export into per-choice-file shape if H12a picks per-file convention. |
| 2 | **actions** | `actions.json` monolith (6 rows, 2026-02-13) | `actions/*.action.json` per-file (17 files, 2026-08-07) | **infra/dataverse/actions/** | (a) post-ADR-039 authoring cadence (Aug vs Feb); (b) 17-vs-6 catalog completeness; (c) per-action-file shape aligns with `Deploy-Playbook.ps1` per-file deployer; (d) MVP action names (`Extract Entities`, `Classify Document`) map to the newer catalog's `entity-extractor` / `agreement-classify` (renamed + refined). MVP `actions.json` is superseded. |
| 3 | **tools** | `tools.json` **MISSING** (Deploy-All prereq refers to a file that does not exist) | 39 per-row `sprk_analysistool-*-row.json` files (2026-08-13) | **infra/dataverse/ (39 tool rows)** | Only real source. `scripts/seed-data/Deploy-Tools.ps1` + `tools.json` are dead-code carry-overs from MVP; running the master deployer today already fails at prereq check on this gap. No migration — R7 tree IS the reality. |
| 4 | **knowledge** | `knowledge.json` (5 records) + `knowledge-content/` attached files | — (none) | **scripts/seed-data/knowledge.json + knowledge-content/** | ONLY source. Knowledge records were not re-authored in the R7 push. H12a treats this as pass-through until a future project supersedes it. Attachment binary handling is an H12a concern (not this note). |
| 5 | **skills** | `skills.json` (3 skills: SKL-001 SKL-006 SKL-008) | — (none) | **scripts/seed-data/skills.json** | ONLY source. R7 catalog governance did not re-authoritize skills. H12a passes through. |
| 6 | **playbooks** | `playbooks.json` (4 MVP: Quick Doc Review, Contract Analysis, Document Profile PB-011, Risk Scan) | `playbooks/summarize-document-for-workspace-v1-multinode.json` (target-state, UNDEPLOYED BY DECISION per `$comment-051-ruling`) + shipped playbooks *not* in infra/dataverse (they live in live env only, per FR-P4-02 round-trip principle) | **HYBRID — per-playbook decision** (see §5 migration deltas) | The R7 principle (per `Seed-PlaybookConsumers.ps1` header) is *live-env is truth; the mirror is a projection*. R7 deliberately did NOT export every playbook to a repo file — the mirror-first path applies. But H12a for a *new* customer needs SOME repo source. Resolution: keep MVP `playbooks.json` as the H12a authoritative for the 4 MVP playbooks (their shape works in current engine) until a Phase C' export produces per-playbook R7-shape mirrors. Do NOT deploy the `-multinode` target-state file (spec MUST NOT: no new capability on the frozen engine per ADR-039 amendment). |
| 7 | **output-types** | `output-types.json` (5 output types for Document Profile) | `sprk_analysisaction-outputschemajson.json` (per-action outputSchema patch mirror) | **scripts/seed-data/output-types.json** (for the Document Profile playbook path) — with **explicit annotation** that R7's `outputSchema is intrinsic to the action` principle (FR-P4-02) means new actions author outputSchema on the action row, not as separate `sprk_aioutputtype` records | Both sources exist; the R7 principle says future outputSchema is action-intrinsic — but the 5 MVP output-type ROWS backing `sprk_document` field mappings (`sprk_tldr` etc.) are still consumed by the shipped Document Profile playbook (per README lines 108–116). H12a keeps the MVP rows AND applies the R7 patch mirror on top for actions that have both. Deletion of MVP rows would break UniversalQuickCreate PCF's auto-summary flow. |
| 8 | **playbook consumers** (`sprk_playbookconsumer`) — the ADR-039 **single AI routing surface** | — (none) | `sprk_playbookconsumer-rows.json` (mirror) + `scripts/dataverse/Seed-PlaybookConsumers.ps1` deployer with round-trip contract | **infra/dataverse/sprk_playbookconsumer-rows.json** | Unambiguous — ADR-039 line 69 forbids any other routing config surface. Deployer already implements the FR-P4-02 round-trip contract (`-Export` / `-DiffOnly` / `Seed`). H12a WRAPS this deployer as its playbook-consumer step; H12a does not re-invent the mirror. |
| 9 | **sprk_aimodeldeployment** | — (none) | — (none) | **PLACEHOLDER — H12c populates** (per spec FR-15 acceptance: *"`sprk_aimodeldeployment` rows placeholder (H12c will populate)"* + FR-17) | H12a MUST NOT populate; H12c reads customer-specific OpenAI endpoint from Bicep output + writes runtime references. |

### 4b. FR-16 app-config seed — H12b manifest

| # | Artifact | Current source | **AUTHORITATIVE for H12b** | Rationale |
|---|---|---|---|---|
| 10 | **DataGrid configs** (`sprk_gridconfiguration`) | `scripts/seed-reconciliation-gridconfig.ps1` (config-in-code, reads shared-lib JSON verbatim, idempotent by id/name) | **`scripts/seed-reconciliation-gridconfig.ps1` pattern**, generalized. H12b authors a manifest listing (configId, name, source-JSON-path) rows and calls the shared idempotency logic. | The 2 currently-seeded reconciliation configs are the only real source-of-truth JSONs in the repo; other `sprk_gridconfiguration` rows live only in live env. H12b MUST NOT invent config JSONs; where a config exists only in live env, H12b defers to a Phase C' export step (mirror-file principle borrowed from `Seed-PlaybookConsumers.ps1`). |
| 11 | **field-mapping profiles + rules** (`sprk_fieldmappingprofile` / `sprk_fieldmappingrule`) | No repo source. Maker-authored in-env per `FIELD-MAPPING-ADMIN-GUIDE.md`. Web-API seeding recipe documented (guide §"Web-API seeding recipe"). | **H12b MUST author a mirror file** (`infra/dataverse/sprk_fieldmappingprofile-rows.json` + `sprk_fieldmappingrule-rows.json`) by exporting live spaarkedev1 as first authoritative snapshot; thereafter live-env is truth per the R7 round-trip pattern. | No hand-authored source exists today. Documented Web-API recipe is a MAKER pattern, not a deployment pattern. The R7 mirror-file pattern (Section 2 invariants + `Seed-PlaybookConsumers.ps1` model) is the correct extension surface — H12b generalizes the pattern rather than inventing a new one. |
| 12 | **system workspace layouts** (`sprk_workspacelayout`, `sprk_issystem=true`) | `scripts/system-layouts.json` + `scripts/Deploy-SystemWorkspaceLayouts.ps1` (task 108, schema half + data half both idempotent) | **`scripts/system-layouts.json` + `scripts/Deploy-SystemWorkspaceLayouts.ps1`** unchanged | Single declarative source-of-truth JSON, single idempotent deployer, schema-column-additive on first run. No drift; no migration. H12b invokes this deployer verbatim. |
| 13 | **chart definitions** | `scripts/create-test-chartdefinitions.ps1`, `scripts/Create-UpcomingTodosChartDefinitions.ps1` (per-chart-family scripts; NOT manifest-driven) | **H12b MUST consolidate into declarative manifest** (`infra/dataverse/sprk_chartdefinition-rows.json` — mirror pattern) by exporting existing chart-family script outputs into a single JSON. Existing per-family scripts become chart-authoring tools; H12b consumes only the manifest. | Two per-family scripts violate the "single manifest per artifact type" H12a/H12b principle. Following the ADR-039-derived R7 pattern (mirror file + one deployer) keeps H12b idempotent and diffable. Interim: H12b may invoke the two existing scripts directly with a `--seed-only` flag until the mirror is authored. Non-trivial migration delta — flagged in §5. |

---

## 5. Migration deltas (Phase C' H12a/H12b task inputs — NOT this task)

Every row below is work assigned to Phase C' handler authoring — this note surfaces the deltas so H12a/H12b can size their acceptance criteria. **No delta is executed here.**

### 5a. H12a AI seed chain migration deltas

| # | Delta | Effort class | Notes |
|---|---|---|---|
| M1 | **Retire `scripts/seed-data/Deploy-Tools.ps1` + the phantom `tools.json` reference in `Deploy-All-AI-SeedData.ps1`.** | Trivial (deletion + master-script edit) | Prevents the prereq-check-failure trap for anyone who runs the MVP deployer. Not blocking H12a (H12a will not invoke `Deploy-All-AI-SeedData.ps1` directly) but leaves a clean tree. |
| M2 | **Deploy 39 tool rows from `infra/dataverse/sprk_analysistool-*-row.json`.** | Small (per-file loop, reuse `Deploy-Playbook.ps1`-style upsert-by-name logic) | Idempotency key: `sprk_name` per row. |
| M3 | **Deploy 17 action files from `infra/dataverse/actions/*.action.json` + their input/output schemas** from `inputschemas/` + `outputschemas/`. | Small (reuse existing `Deploy-Playbook.ps1` action-loading path) | Ordering: schemas → actions. `sprk_actioncode` is portable key. |
| M4 | **Deploy 4 MVP playbooks from `scripts/seed-data/playbooks.json`** using existing `Deploy-Playbooks.ps1` OR the newer `Deploy-Playbook.ps1` per-file path (H12a picks). | Small | `Document Profile (PB-011)` is a system playbook consumed by UniversalQuickCreate PCF — cannot be dropped. |
| M5 | **Do NOT deploy `infra/dataverse/playbooks/summarize-document-for-workspace-v1-multinode.json`** (marked UNDEPLOYED BY DECISION per its `$comment-051-ruling`; would extend the frozen engine per ADR-039 amendment). | Zero (no-op) | Include an explicit exclusion in the manifest so no future auto-discovery pass sweeps it in. |
| M6 | **Deploy 5 output-type rows** from `scripts/seed-data/output-types.json` (backing Document Profile field mappings on `sprk_document`). | Small | Do NOT delete these when applying the R7 principle "outputSchema is intrinsic to action" — the 5 rows are historical bindings to `sprk_document` fields consumed by shipped code. |
| M7 | **Apply per-action `sprk_analysisaction.sprk_outputschemajson` patches** from `infra/dataverse/sprk_analysisaction-outputschemajson.json`. | Trivial (PATCH loop keyed on `sprk_actioncode`) | Runs AFTER action rows are created (M3). |
| M8 | **Invoke `scripts/dataverse/Seed-PlaybookConsumers.ps1`** (default seed mode, `-SkipConfirm`) as the terminal H12a step. | Trivial (single script call) | Depends on M3 (action codes) + M4 (playbook names) being present in target env. Mirror is `infra/dataverse/sprk_playbookconsumer-rows.json`. |
| M9 | **Include `-DiffOnly` verification step** after M8 as H12a idempotency assertion. | Trivial | Exit 0 == zero drift. Feeds H12a's "second run is no-op" acceptance criterion in spec.md L1 pipeline test. |
| M10 | **Author `infra/dataverse/type-lookups-*.json` per-choice-file mirrors** OR keep monolithic `scripts/seed-data/type-lookups.json` — H12a authorial choice; both are one-file-per-artifact-type-scope compatible. | Small (if refactored) or Trivial (if kept) | Non-blocking; the H12a manifest can reference either shape. Recommendation: keep the MVP monolith (no evidence R7 refactored it) and defer refactor to a future ADR-039-alignment sweep. |
| M11 | **DO NOT** populate `sprk_aimodeldeployment` in H12a — H12c reads Bicep output for customer's OpenAI endpoint. | Zero (no-op / explicit skip) | Enforces FR-15 acceptance boundary + FR-17 handler ownership. |
| M12 | **DO NOT** touch `spaarke-playbook-embeddings` index (retired per ADR-039 amendment / FR-P2-06). | Zero (no-op / explicit exclusion) | Enforces ADR-039 MUST NOT. |

### 5b. H12b app-config seed migration deltas

| # | Delta | Effort class | Notes |
|---|---|---|---|
| N1 | **Generalize `seed-reconciliation-gridconfig.ps1` into a `sprk_gridconfiguration` mirror-driven seeder.** | Medium (script refactor) | Manifest lists `(gridConfigId, name, sourceJsonPath)` triples; script upserts by id-or-name. Existing 2 reconciliation configs migrate first. |
| N2 | **Export live `sprk_gridconfiguration` rows not in the shared-lib source-of-truth** into a mirror JSON (Phase C' snapshot). | Medium | First run against spaarkedev1 becomes the authoritative snapshot; ongoing R7-style round-trip thereafter. |
| N3 | **Author `infra/dataverse/sprk_fieldmappingprofile-rows.json` + `sprk_fieldmappingrule-rows.json` by exporting live spaarkedev1** — no repo source exists today. | Medium | Uses `FIELD-MAPPING-ADMIN-GUIDE.md` Web-API recipe as the read path (`GET /api/data/v9.2/sprk_fieldmappingprofiles?$expand=sprk_fieldmappingrule_FieldMappingProfile`). |
| N4 | **Reuse `scripts/Deploy-SystemWorkspaceLayouts.ps1` verbatim** for workspace layouts. | Zero (call existing) | Manifest lists it as-is; no shape change. |
| N5 | **Consolidate `create-test-chartdefinitions.ps1` + `Create-UpcomingTodosChartDefinitions.ps1` into `infra/dataverse/sprk_chartdefinition-rows.json` mirror + a single seeder.** | Medium-Large | Non-trivial because per-family scripts have hard-coded shape logic. Interim: H12b invokes the two per-family scripts directly (parameterized) until mirror is authored. Fast-follow to Phase C'-post. |

---

## 6. Non-goals of this note (H12a IS NOT constrained to)

- Delete `scripts/seed-data/` — retention preserves MVP audit trail; H12a simply does not invoke `Deploy-All-AI-SeedData.ps1`.
- Retro-fit `Deploy-*.ps1` MVP deployers to per-artifact-file shape — H12a can wrap either shape.
- Redesign `sprk_playbookconsumer` mirror shape — R7 shipped it; H12a consumes as-is.
- Populate customer-specific OpenAI deployment endpoints — H12c owns that.
- Address `spaarke-playbook-embeddings` index at all — retired.

---

## 7. Escalation triggers this audit did NOT hit (per POML `<escalation>`)

No artifact type left with unresolved ambiguity. Every row in §4 has a concrete authoritative source AND a rationale citing evidence (git commit date, file line, ADR-039 clause, or scripted deployer contract). No arbitrary calls were made. **Escalation not invoked.**

---

## 8. Traceability

- **Spec**: FR-15 (H12a AI seed chain), FR-16 (H12b app-config seed), FR-17 (H12c runtime references), R14 (two-source drift), Success Criterion 15.
- **Design**: §11.3 (BFF Job Handler Ecosystem — H12a/b/c row 149), §12 R13/R14, §12 R14 amendment 1420, PROJECT-UPDATE §6 Gap 1.
- **ADRs**: ADR-039 (grounded execution / closed catalogs / single routing surface — the binding rule), ADR-013 (AI architecture — PublicContracts facade if H12a needs AI).
- **Source scripts (unchanged by this note)**:
  - `scripts/seed-data/Deploy-All-AI-SeedData.ps1`
  - `scripts/seed-data/Deploy-{TypeLookups,Actions,Knowledge,Skills,Playbooks,OutputTypes}.ps1`
  - `scripts/dataverse/Seed-PlaybookConsumers.ps1`
  - `scripts/Deploy-Playbook.ps1`
  - `scripts/Deploy-SystemWorkspaceLayouts.ps1` + `scripts/system-layouts.json`
  - `scripts/seed-reconciliation-gridconfig.ps1`
- **Downstream consumers**: Phase C' H12a POML (to be authored) + H12b POML (to be authored).

---

*End of resolution note. `git diff` for task 004 shows ONLY this file added.*
