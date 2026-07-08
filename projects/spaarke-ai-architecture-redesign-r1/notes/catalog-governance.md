# Task 051 — Catalog Governance (FR-P4-02)

> **Date**: 2026-07-07 · **Environment**: spaarkedev1 (`https://spaarkedev1.crm.dynamics.com`) ·
> Executed as a sub-agent (dispatched early by operator direction while gate 048 is in UAT); all
> `.claude/catalogs/` content is delivered via the notes-handoff pattern per the POML constraint
> (CLAUDE.md §3 sub-agent write boundary). Live reconciliation used read-only MCP queries; the two
> environment writes below are documented old→new and re-read verified.

---

## Deliverable 1 — ONE refreshed `scope-model-index.json`

### Single-copy proof (grep shown)

```
$ find . -iname '*scope-model-index*' (excl. node_modules/.git)
./.claude/catalogs/scope-model-index.json        ← the ONLY copy repo-wide
```

The `docs/ai-knowledge/catalogs/` twin deleted by Track-B batch 4 (task 073) has **not resurfaced**.

### Regeneration

- `scripts/Refresh-ScopeModelIndex.ps1` was **BROKEN** (pre-existing drift): its knowledge query
  400-failed because `$select` still referenced **`sprk_externalid`**, a column that no longer
  exists on `sprk_analysisknowledge` (live `describe` shows the code column is now
  **`sprk_knowledgecode`**, plus `sprk_knowledgeid`). **REPAIRED** (not retired — CLAUDE.md §11
  reuse-over-parallel; the jps-scope-refresh skill contract is unchanged).
- Script also extended to carry the post-redesign taxonomy: every entry now carries the deployed
  row **GUID (`id`)**; actions carry **`kind`** (Prompted/Coded), **`modelTier`**
  (Fast/Standard/Reasoning), **`workflowClass`**; tools carry **`toolId`** (loop namespace id) and
  **`sideEffectClass`** (Read/Write/Communicate/Pure); knowledge carries **`deliveryType`**.
  Curated fields (tags/documentTypes/contentType) still merge from the existing file; static
  `models`/`modelSelectionRules` preserved.
- Run output (2026-07-07): **Actions 60 · Skills 31 · Knowledge 31 · Tools 40** — matching
  independent MCP `COUNT(*)` queries exactly. 131 entries added, 30 descriptions updated,
  5 stale entries removed vs the 2026-03-05 index.
- The stale **`compositions`** section (PB-001..PB-010 — abandoned pre-redesign taxonomy,
  inventory §5.4 "actively misleading") was reset to `{}` in the regenerated output. Refresh
  preserves it as `{}` going forward.

### Spot-check vs deployed GUIDs (≥3 required; 7 shown)

| Index entry | id in regenerated index | Independent MCP query result | Match |
|---|---|---|---|
| SUM-CHAT@v1 (Prompted/Fast) | `eeb05bfd-1260-f111-ab0b-70a8a59455f4` | same (task-020 row) | ✅ |
| REF-CHAT@v1 (Prompted/Fast) | `8d337be2-3d79-f111-ab0e-7ced8ddc4cc6` | same | ✅ |
| DAILY-BRIEFING@v1 (Coded, `DailyBriefingNarrator`) | `2fa8ab19-7879-f111-ab0e-7ced8ddc4cc6` | same (task-043 row) | ✅ |
| CREATE-TASK@v1 (Prompted/Standard) | `b66c8dda-8279-f111-ab0e-7ced8ddc4cc6` | same (task-042 row) | ✅ |
| EMAIL-DRAFT tool (`email.draft`, Communicate) | `bc11e90d-6b79-f111-ab0e-7ced8ddc4cc6` | same (task-041 row) | ✅ |
| DATAVERSE-CREATE-RECORD (`dataverse.create_record`, Write) | `18b3531f-ba78-f111-ab0e-7ced8ddc4a05` | same | ✅ |
| KNW-001 knowledge (RAG Index) | `331cd212-ca18-f111-8343-7c1e520aa4df` | same | ✅ |

### Handoff (MAIN SESSION action required)

The regenerated index is at
**`projects/spaarke-ai-architecture-redesign-r1/notes/catalog/scope-model-index.regenerated.json`**
(92.9 KB). The main session must **MOVE** it over `.claude/catalogs/scope-model-index.json`
(move, not copy — the notes artifact is a handoff, not a second catalog copy):

```powershell
Move-Item -Force projects/spaarke-ai-architecture-redesign-r1/notes/catalog/scope-model-index.regenerated.json .claude/catalogs/scope-model-index.json
Remove-Item projects/spaarke-ai-architecture-redesign-r1/notes/catalog -Recurse   # empty dir cleanup
```

Future refreshes run the repaired script directly from the main session:
`pwsh scripts/Refresh-ScopeModelIndex.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com`.

---

## Deliverable 2 — `Seed-PlaybookConsumers.ps1` regenerated from the Binding table

### What was wrong

`scripts/dataverse/Seed-PlaybookConsumers.ps1` was the 2026-06-24 chat-routing-redesign-r1
projection: **7 playbook-only rows**, hard-coded dev GUIDs, `sprk_playbookid` taxonomy, none of the
FR-P0-03 extended columns, and 11 of the 18 live rows missing (incl. every post-redesign
capability Binding). Task 043 explicitly deferred regeneration to this task.

### What shipped

Rewritten as a **data-driven projection tool** with three modes (regeneration direction:
table → script, per the spec MUST):

- **Mirror**: `infra/dataverse/sprk_playbookconsumer-rows.json` — all 18 enabled rows, all
  extended columns (`ucid`, `disposition`, `risk`, `captureMode`, `surfaces`, `chipTransitions`,
  `onEventBindings`, `matchConditions`, `toolDescription` — incl. the G-P3 UAT round-2/3-hardened
  create-task ASSIGNEE + POST-CONFIRMATION rules, 2,478 chars, carried verbatim). Lookups are
  **portable**: `actionCode` / `playbookName` resolved per environment at seed time — no GUIDs in
  semantic fields.
- **`-Export`**: live table → mirror (the regeneration verb).
- **`-DiffOnly`**: the round-trip proof — re-exports live in memory and diffs field-by-field
  against the mirror; exit 1 on any drift.
- **Seed** (default): mirror → environment via alternate-key UPSERT
  (`sprk_ConsumerTypeCodeEnvironment`); null mirror lookups CLEAR the target's lookup
  (DELETE `$ref`) so seeding converges; missing Action/playbook targets fail loudly per row.

The task-040 partial mirror `infra/dataverse/sprk_playbookconsumer-insights-rows.json` (3 insights
rows only) is **superseded and deleted** — one mirror, one source. (Doc references in
`docs/data-model/sprk-playbookconsumer.md`, `docs/guides/BUILD-A-NEW-INSIGHT-CARD.md`,
`docs/guides/INSIGHTS-ENGINE-GUIDE.md` flagged to task 052 / doc-drift-audit.)

### Round-trip demonstration (shown)

```
$ pwsh scripts/dataverse/Seed-PlaybookConsumers.ps1 -Export
Exported 18 rows -> infra/dataverse/sprk_playbookconsumer-rows.json

$ pwsh scripts/dataverse/Seed-PlaybookConsumers.ps1 -DiffOnly
=== Seed-PlaybookConsumers: DIFF (round-trip proof) ===
ROUND-TRIP CLEAN: 18 mirror rows == 18 live rows, zero semantic drift.
(exit 0)
```

Seed `-DryRun` renders all 18 rows correctly (transcript). Seeding a clean environment requires
the referenced Action rows / playbooks to exist first (script fails loudly per row naming the
missing `actionCode`/`playbookName`).

### Reconciliation table — live `sprk_playbookconsumer` (18 enabled rows) vs seeds, row by row

| consumerType / code | Row id | Target (live) | Old seed (2026-06-24) | New mirror | Notes |
|---|---|---|---|---|---|
| ai-summary / default | `121194cd…` | playbook **Document Profile** | GUID-hardcoded, no ext cols | ✅ by name | legacy engine consumer |
| chat-classify / default | `5f3898d8…` | action **CLS-CHAT@v1** | ❌ missing | ✅ | + onEventBindings `document_uploaded(1)`, chips |
| chat-summarize / default | `651194cd…` | action **SUM-CHAT@v1** | ❌ stale (playbook `44285d15…` — cleared at task 020 cutover) | ✅ | + onEventBindings `document_uploaded(2)`, chip |
| chat-summarize / matter-summary | `05618e5d…` | action SUM-CHAT@v1, **Work Product** | ❌ missing | ✅ | task 047 leg |
| compose-summarize / default | `986799ad…` | playbook **Document Summary** | GUID-hardcoded | ✅ by name | row id differs from old seed's provenance note — name-keyed upsert makes this moot |
| create-task / default | `3d9724e5…` | action **CREATE-TASK@v1** | ❌ missing | ✅ | UAT-hardened tooldescription carried |
| daily-briefing-narrate / default | `b4503359…` | action **DAILY-BRIEFING@v1** | ❌ missing | ✅ | task 043 cutover (playbook cleared) |
| daily-briefing-narrate / email | `800cc81f…` | action DAILY-BRIEFING@v1, **Email** | ❌ missing | ✅ | `briefing_scheduled` event binding, scheduler surface |
| document-profile / default | `a2bd24e7…` | playbook Document Profile + action ACT-011 | ❌ missing | ✅ | |
| draft-correspondence / default | `f7dc4a00…` | action **DRAFT-CORR@v1** | ❌ missing | ✅ | task 041 |
| email-analysis / default | `8b1194cd…` | playbook **Email Analysis** | GUID-hardcoded | ✅ by name | |
| insights-ask / default | `f32a7931…` | playbook **matter-health-single**, prio 500 | ❌ missing | ✅ | task 040 |
| insights-ask / matter-health-single | `f82a7931…` | same playbook, **prio 400** | ❌ missing | ✅ | W-2 fix priority preserved |
| insights-search / default | `f89fa738…` | **no target** (catalog registration) | ❌ missing | ✅ | target-less by design |
| matter-pre-fill / default | `e5f37faa…` | playbook Create New Matter Pre-Fill + ACT-023 | partially (playbook only) | ✅ | **env write 1** (below) |
| no_match_handler / default | `48dcd7ec…` | action **REF-CHAT@v1**, ucid L4-REFUSAL | ❌ missing | ✅ | task 033 refusal |
| project-pre-fill / default | `ab7ac1c5…` | playbook Create New Project Pre-Fill + ACT-024 | partially (playbook only) | ✅ | **env write 2** (below) |
| summarize-file / default | `271194cd…` | playbook Summarize File + ACT-025 | GUID-hardcoded | ✅ by name | |

All 18 lookup targets verified resolvable against the live Action/playbook catalogs (MCP queries,
transcript). `universal-ingest@v1` has NO row — reconfirmed correct per the task-040 explicit
decision (playbook does not exist on spaarkedev1; seed the row only when it ships).

### Environment writes (the only two; old → new, re-read verified)

| Row | Column | Old | New | Justification |
|---|---|---|---|---|
| matter-pre-fill `e5f37faa-2c70-f111-ab0e-7ced8ddc4cc6` | `sprk_environment` | `null` | `*` | Seed-side truth: router treats null/empty/`*` identically (`PublicContracts/Binding.cs:50`), but the alternate key `sprk_ConsumerTypeCodeEnvironment` cannot address a NULL-environment row — a seed re-run would CREATE A DUPLICATE row instead of converging. All 16 other rows use `*`. Semantics unchanged; idempotency restored. |
| project-pre-fill `ab7ac1c5-2c70-f111-ab0e-7ced8ddc4cc6` | `sprk_environment` | `null` | `*` | Same. |

---

## Deliverable 3 — `sprk_nodetype` gap ruling

**RULING: the option-set gap NO LONGER EXISTS (resolved by schema evolution); the multinode
playbook remains UNDEPLOYED BY DECISION (frozen engine).**

Evidence (live spaarkedev1 `describe tables/sprk_playbooknode`, 2026-07-07):

1. The **`sprk_nodetype` column does not exist** on `sprk_playbooknode`. It was removed pre-R7
   and replaced by **`sprk_executortype`** (global Choice `sprk_playbookexecutortype`, 33 values)
   — consistent with `.claude/skills/jps-scope-refresh/SKILL.md` (R7 FR-33 note).
2. `sprk_executortype` **already includes `Deliver Composite (42)`**. The blocker the playbook
   file documents ("sprk_nodetype choice missing 100000004") is therefore obsolete — there is no
   missing option value to add, and adding one is impossible (no such column).
3. The playbook `summarize-document-for-workspace-v1-multinode` is **not deployed**
   (`sprk_analysisplaybook` scan: only the single-node sibling
   `summarize-document-for-workspace@v1`, `9b44cb6a-c370-f111-ab0e-7ced8ddc4a05`, exists).
4. **Why not deploy it now that the schema allows it**: (a) the engine is FROZEN — spec MUST NOT
   land new capability on it, and a new multinode variant is new engine capability, not
   maintenance; (b) the file's `nodeType: "DeliverComposite"` / `100000004` vocabulary predates
   the executortype migration and would need rewriting to `sprk_executortype = 42`, plus the
   documented `Deploy-Playbook.ps1` harness extension — engine-side work explicitly out of scope;
   (c) the shipped workspace summarize path routes via the `summarize-file` Binding to the
   deployed single-node playbook.

The obsolete blocker comment in
`infra/dataverse/playbooks/summarize-document-for-workspace-v1-multinode.json` is annotated
in place (`$comment-051-ruling`) so no future reader chases the dead `sprk_nodetype` remediation.

---

## Scripts fixed / retired / created (seed-governance sweep)

| Script | Disposition |
|---|---|
| `scripts/Refresh-ScopeModelIndex.ps1` | **FIXED** (400 root cause: dead `sprk_externalid` column; + post-redesign fields; + FormattedValue annotation header) |
| `scripts/dataverse/Seed-PlaybookConsumers.ps1` | **REGENERATED** (data-driven Seed/Export/DiffOnly; mirror-file single source; round-trip proven) |
| `infra/dataverse/sprk_playbookconsumer-rows.json` | **CREATED** (the full-table mirror, 18 rows) |
| `infra/dataverse/sprk_playbookconsumer-insights-rows.json` | **DELETED** (superseded partial mirror — one source) |
| `scripts/Seed-JpsActions.ps1` | **RETIRED (deleted)** — unrunnable: both JPS source dirs (`projects/ai-json-prompt-schema-system/…`, `projects/jps-server-rollout/…`) no longer exist; purpose belongs to the frozen engine. Replacement documented in `scripts/README.md`: `jps-action-create` skill + `infra/dataverse/inputschemas/` mirror-first authoring. (Deferred from Track-B batch 4 / task 073 "regen deferred to 050/051".) |
| `scripts/Seed-TypedHandlers.ps1` | **FIXED** — `EMAIL-DRAFT` map entry added (task 041 created the row + mirror but never registered it in the seed map; map↔mirror-files diff now 36 = 36, 1:1 with the 36 live SYS- tool rows) |
| `scripts/README.md` | Updated entries for all of the above |

**Legacy tool rows note (for task 050 / FR-P4-01)**: 4 non-SYS `sprk_analysistool` rows remain
live with no seed mirror — `TL-004` Document Classifier, `TL-006` Summary Generator, `TL-008`
Search Documents, `TL-010` General Analysis (legacy engine handlers). Out of FR-P4-02 scope
(tool-row seeding is Seed-TypedHandlers' SYS-prefixed contract); flagged as Track-B audit input.

**Knowledge duplicates note (catalog data, NOT changed)**: `sprk_analysisknowledge` carries the 10
`KNW-001..010` coded rows AND 10 older null-code rows with `KNW-0xx-*` names (both RAG Index,
different eras). ADR-039 says the index reflects deployed rows exactly, so both sets appear in the
regenerated index (null-code rows fall back to name-as-code). Dedup/deactivation is a catalog-data
decision for the operator / task 050 — not silently performed here.

## Test results

- `CatalogInputSchemaContractTests`: **14/14 green** (inputschemas mirrors untouched; sanity).
- No `src/**` code touched (dispatch boundary — UAT fix waves own `Services/Ai`); no test-suite
  impact expected or introduced. Known pre-existing failures list unchanged and not re-litigated
  here (scripts/data-only task).
- JSON validity: playbook file + mirror file parse clean post-edit (shown in transcript).

## MAIN SESSION checklist (.claude write boundary)

1. **Move** `notes/catalog/scope-model-index.regenerated.json` → `.claude/catalogs/scope-model-index.json` (command above).
2. `.claude/skills/jps-scope-refresh/SKILL.md` — no contract change needed (script path/behavior
   preserved), but its Step 0 quick-check queries `sprk_analysisknowledge` with
   `sprk_code`/`sprk_externalid`-era column names; optional touch-up to `sprk_knowledgecode`.
3. `.claude/skills/jps-action-create/SKILL.md`, `jps-playbook-design/SKILL.md`,
   `jps-validate/SKILL.md` reference the retired `Seed-JpsActions.ps1` — remove/replace those
   pointers (jps-action-create writes rows directly; that is the replacement).
4. G-P3 round-1/round-3 pending items (unchanged, restated): jps-action-create property-level
   `"required": true` ban + `infra/dataverse/inputschemas/` mirror pointer.
