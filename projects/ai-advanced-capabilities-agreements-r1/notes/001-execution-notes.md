# Task 001 — Execution Notes: sprk_agreementtype registry mirror + seeds

> **Executed**: 2026-07-31 · **Rigor**: FULL (override UP from authored STANDARD — modifies `sprkAnalysis.ts`)

## Summary

Authored the first code/infra mirror for the env-only `sprk_agreementtype` table, loaded the 7 remaining identity
rows, and filled behavior-column values (knowledge pack ref + classification cue) for the `nda` + `general` rows.
Verified the `sprk_key` alternate key is enforced (empirical duplicate-create rejection). tsc build for
`Spaarke.UI.Components` is clean for the file this task touched (2 pre-existing, unrelated module-resolution
errors remain — see Deviations).

## Env state (spaarkedev1) — before / after

| | Before | After |
|---|---|---|
| Rows | 3 (general, nda, employment) | 10 (+ lease, asset-purchase, services, licensing, vendor, partnership, loan) |
| Selectable | 3/3 Yes | 10/10 Yes |
| Fallback | 1 (general) | 1 (general) — unchanged |
| Behavior columns | all null | nda + general: `sprk_knowledgepackref` + `sprk_classificationcue` populated; `sprk_confidencethreshold` left null (global 0.85 baseline) on both, per instruction |

### New rows created (GUIDs)

| `sprk_key` | `sprk_name` | `sprk_agreementtypeid` | `sprk_sortorder` |
|---|---|---|---|
| lease | Lease / Real Property | `caa461f3-fb8c-f111-8076-3833c5deff74` | 40 |
| asset-purchase | Asset Purchase | `cca461f3-fb8c-f111-8076-3833c5deff74` | 50 |
| services | Services / MSA | `dea461f3-fb8c-f111-8076-3833c5deff74` | 60 |
| licensing | Licensing / IP | `e6a461f3-fb8c-f111-8076-3833c5deff74` | 70 |
| vendor | Vendor / Procurement | `f0a461f3-fb8c-f111-8076-3833c5deff74` | 80 |
| partnership | Partnership / JV | `f9a461f3-fb8c-f111-8076-3833c5deff74` | 90 |
| loan | Loan / Financing | `fba461f3-fb8c-f111-8076-3833c5deff74` | 100 |

All 7 created with `sprk_isselectable=true`, `sprk_isfallback=false`. All GUIDs bare-lowercase (ADR-044) as returned
by the MCP `create_record` tool — verified no braces/uppercase present.

### Existing rows updated (behavior values only — no identity columns touched)

| `sprk_key` | `sprk_agreementtypeid` | `sprk_knowledgepackref` | `sprk_classificationcue` (truncated) | `sprk_confidencethreshold` |
|---|---|---|---|---|
| nda | `d557c894-908c-f111-8077-7ced8ddc4cc6` | `KNW-011` (already-indexed Spaarke NDA Standard, nda-r1 task 012) | "Agreement whose PRIMARY subject is confidentiality / non-disclosure obligations..." | null (global baseline) |
| general | `433e1688-908c-f111-8077-7ced8ddc4cc6` | `KNW-012` (**forward reference** — not yet indexed; task 003 owns authoring/indexing the general pack) | "Fallback classification for any agreement that is clearly a contract..." | null (global baseline) |

`employment` was left fully untouched (identity-only; no pack owned by this project — out of scope per design Lens 3d).

## Alt-key verification (binding per escalation trigger)

Owner confirmed 2026-07-31 the `sprk_key` alternate key was created. Empirically re-verified per the task's
instruction: attempted `mcp__dataverse__create_record('sprk_agreementtype', {sprk_key: 'general', sprk_name:
'ZZTEST-DuplicateKeyProbe-001-DELETE-ME', ...})`.

**Result**: rejected outright by the API — `"Entity Key Key violated. A record with the same value for Key already
exists. A duplicate record cannot be created."` No row was created (the rejection happens before insert), so there
was nothing to clean up. **The alt key is real and enforced.** Escalation trigger did NOT fire (alt key present +
confirmed, not missing).

## Naming footgun — verified + NOT silently fixed (important for main session)

Verified via `mcp__dataverse__describe('tables/sprk_analysis')`: the lookup attribute FROM `sprk_analysis` TO this
table is `sprk_agreementtype` (OData `_sprk_agreementtype_value`). `sprk_agreementtypeid` is `sprk_agreementtype`'s
own primary key column, unrelated to the lookup attribute name.

**Discovery**: the current worktree's `src/client/shared/Spaarke.UI.Components/src/types/sprkAnalysis.ts` still has
the WRONG OData field (`_sprk_agreementtypeid_value`) in `ISprkAnalysisRecord` + `SPRK_ANALYSIS_SELECT` (added by
commit `1e1a6579b`). A fix commit exists — `7e022e7dd` ("fix(analysis): correct A1 lookup attr name
sprk_agreementtype (was sprk_agreementtypeid)") — but per `git merge-base --is-ancestor 7e022e7dd HEAD` it is **NOT
an ancestor of this branch's HEAD**. It lives on the `work/ai-advanced-capabilities-analysis-hub-r1` branch and is
also **not yet on `origin/master`**. The hub's own coordination doc (`COORDINATION-hub-r1-ANSWERS-to-agreements-r1-Q1-Q5.md`)
claims this is "FIXED" — true upstream, but not yet merged into this worktree.

**Decision**: per task step 4 ("extend, don't restate") and to avoid merge drift with the incoming `7e022e7dd`
commit, I did **NOT** duplicate that fix in `sprkAnalysis.ts` myself. I added the new `ISprkAgreementTypeRecord` /
`SprkAgreementTypeKey` mirror additively, with a JSDoc note pointing at this exact discrepancy so nobody assumes it's
already corrected. **Flagging for the main session**: when `7e022e7dd` (or the next master merge that carries it)
lands, the existing `_sprk_agreementtypeid_value` occurrences in this file will be corrected automatically via that
merge — no action needed from this task, but don't be surprised by the merge diff.

## Deliverables

1. **`infra/dataverse/sprk_agreementtype-rows.json`** (new) — all 10 rows, identity + behavior columns, GUIDs
   recorded, header block documents: the naming footgun (with the unmerged-fix discovery above), the alt-key
   verification evidence, the sortorder decision (see below), the `KNW-012` forward-reference convention, and the
   MCP-driven load procedure (no dedicated deploy script exists yet for this table — noted as a gap, not filled,
   since authoring one was out of this task's scope).
2. **`src/client/shared/Spaarke.UI.Components/src/types/sprkAnalysis.ts`** (extended, +112 lines) — added
   `ISprkAgreementTypeRecord`, `SprkAgreementTypeKey` (closed union of the 10 seeded keys), `SPRK_AGREEMENT_TYPE_KEYS`,
   `SPRK_AGREEMENT_TYPE_FALLBACK_KEY`, `SPRK_AGREEMENT_TYPE_SELECT`. Did not touch the pre-existing
   `ISprkAnalysisRecord` / `SPRK_ANALYSIS_SELECT` fields (see naming-footgun section above).

## Decisions / deviations worth flagging

1. **Sortorder**: all 3 pre-existing rows had `sprk_sortorder = NULL` (verified via `read_query` before any
   changes) — there was no existing sequence to "continue" as the task context assumed. Per hub's Part D,
   `sprk_sortorder` is a hub-owned identity column, so I did **not** backfill it on the 3 pre-existing rows. For the
   7 new rows (which I *am* responsible for loading per Q3), I assigned `40/50/60/70/80/90/100` continuing an
   implied `general=10/nda=20/employment=30` baseline, leaving room for hub to backfill the first 3 without collision.
   Documented in the seed JSON's `$sortorder-decision` header.
2. **`KNW-012` forward reference**: task 003 (knowledge-packs) has not yet authored/indexed the general fallback
   pack. Per the orchestrating context's instruction ("use the pack id/name that task 003 will author... noting the
   forward reference"), I assigned `general.sprk_knowledgepackref = "KNW-012"` — verified free via repo-wide grep
   (zero hits) at authoring time, continuing the `KNW-001..011` sequence. Task 003 MUST either index its pack under
   this id or update both the live row and this seed JSON to match (ADR-027 mirror-must-match-env). Flagged loudly
   in the JSON header (`$knowledgepackref-convention`) so task 003 doesn't miss it.
3. **No dedicated deploy script authored**: `scripts/seed-data/Deploy-TypeLookups.ps1` targets a different
   `$entitySets` map (`sprk_analysisactiontype`/`sprk_aiskilltype`/`sprk_aitooltype`) and isn't a drop-in fit for
   this table's shape without modification. Rather than bolt on a mismatched extension or author a new
   `Deploy-AgreementTypes.ps1` (not requested by the task, and risks scope creep beyond "author the seed JSON"), I
   loaded the 10 rows via direct MCP calls (permitted per ADR-027 per the task's own framing: "MCP create/update
   driven from the authored JSON is acceptable as the load vehicle") and documented the gap + a recipe for a future
   task in the seed JSON's `$load-procedure` header.
4. **`SprkAgreementTypeKey` closed union vs. the "zero-code for new types" design principle**: the task explicitly
   instructed adding "key constants (the 10 sprk_key literals as a const union/map)," and root CLAUDE.md §11's
   "no parallel TS constant list... beyond the generated mirror" phrasing implies the generated mirror itself may
   enumerate the keys. To avoid this union becoming a silent drift risk when an 11th type is added later, I typed
   `ISprkAgreementTypeRecord.sprk_key` as plain `string` (not the narrow union) — reading live rows never breaks;
   only code that explicitly imports `SprkAgreementTypeKey` for an intentionally-closed switch needs updating when
   a new type is registered. Flagging this as a design-tension worth the reviewer's attention, not a violation.

## Quality-gate self-review (FULL rigor — code-review + adr-check essentials)

**Scope**: `infra/dataverse/sprk_agreementtype-rows.json` (new, data/config) +
`src/client/shared/Spaarke.UI.Components/src/types/sprkAnalysis.ts` (extended, +112/-0 lines, 227→339).

- **ADR-044 (GUID canonicalization)**: PASS. All GUIDs in the seed JSON (3 pre-existing + 7 new) are bare-lowercase,
  no braces — verified by inspection of both the MCP `create_record` return values and the `read_query` verification
  read. No `@odata.bind` construction in this task's scope.
- **ADR-027 (mirror-first + idempotent seed, no ad-hoc console edits)**: PASS with a documented process note — GUIDs
  for the 7 new rows could not be pre-authored (Dataverse mints them), so the practical sequence was
  create-via-MCP-then-mirror (matching the existing `sprk_playbookconsumer-rows.json` precedent, itself framed as "a
  projection of the LIVE table"), not literal JSON-then-load. Direction-of-truth and idempotent-mirror-matches-env
  intent are both satisfied; documented explicitly in the seed JSON header.
- **Project constraint — behavior values only, no schema changes**: PASS. `describe('tables/sprk_agreementtype')`
  was run before and would show identical column set after (no create/alter-attribute calls made this task).
- **Project constraint — no parallel registry list**: addressed under Decisions/deviations #4 above; judged
  compliant (mirror-only, live table remains authoritative for anything beyond the key string).
- **AI code-smell scan** (Step 5.5, TypeScript file):
  1. Interface w/ single implementation — N/A (pure data-shape interfaces in a types file, not a DI seam; ADR-010
     doesn't apply to type declarations).
  2. Try/catch log-rethrow — none introduced (no runtime logic added).
  3. Null check on non-nullable — none introduced.
  4. Code-restating comments — none; all new JSDoc explains WHY (footgun history, ownership split, forward
     references), not WHAT.
  5. Method >3 responsibilities — N/A (no methods/functions added, only types/const arrays).
  **AI Smell Score**: 0 findings.
- **Quantitative metrics**: `sprkAnalysis.ts` grew 227→339 lines (+49%), which crosses the code-review skill's raw
  "file grew >20%" Warning threshold. In context this is expected/Neutral: the file is a pure type-mirror file, the
  growth is proportional to the new registry surface (1 new interface + 4 new consts, each carrying the same JSDoc
  density as the existing `ISprkAnalysisRecord`/`ANALYSIS_WORK_TYPE_IDS` block it sits beside), and no logic/branch
  complexity was added (cyclomatic complexity delta = 0).
- **Component Justification (root CLAUDE.md §11 / code-review Step 6.6)**: the task POML's own
  `<justification>` block (existing/extension/cost-of-doing-nothing) already satisfies this gate — cited: existing
  = "repo-wide grep finds `sprk_agreementtype` ONLY in coordination docs" (re-verified true before this task: the
  ONLY code references were the hub's partial fields); extension = "extends the existing `sprkAnalysis.ts` types
  file and the existing seed-data script pattern"; cost-of-doing-nothing = "classifier routing has only 3 of 10
  types and null behavior columns — routing/grounding cannot work" (task 020 depends on exactly this).
- **BFF Hygiene (Step 6.5)**: N/A — no files under `src/server/api/Sprk.Bff.Api/` touched.
- **Security review**: N/A — no secrets, no auth surface, no user input handling in either file.

**Verdict**: Clean. No Critical or Warning findings. One Suggestion: consider authoring a dedicated
`Deploy-AgreementTypes.ps1` in a future task once the registry stabilizes (currently MCP-loaded; documented as a
known gap, not a defect).

## Verification evidence

Final `read_query` (`SELECT ... FROM sprk_agreementtype ORDER BY sprk_sortorder`) returned exactly 10 rows, all
`sprk_isselectable=true`, exactly one `sprk_isfallback=true` (`general`), `nda`+`general` carrying non-null
`sprk_knowledgepackref`+`sprk_classificationcue` with `sprk_confidencethreshold` null on both. `npx tsc --noEmit`
in `Spaarke.UI.Components` reports 2 errors, both in `EntityCreationService.ts` / `useWizardPageBootstrap.ts`
(missing type declarations for `@spaarke/sdap-client` / `@spaarke/auth`) — pre-existing, unrelated to this task's
files (neither error references `sprkAnalysis.ts`; both sibling packages have empty `node_modules` in this fresh
worktree checkout, a pre-existing environment/build-order gap, not something this task introduced or is scoped to
fix).
