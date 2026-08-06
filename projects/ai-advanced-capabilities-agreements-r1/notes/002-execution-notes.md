# Task 002 — agreement-review Action generalization + FR-05 schema split — execution notes

> Rigor: FULL · Model tier: opus @ high · TEST-MODIFYING override (eval assets touched) → quality gates unconditional.
> Spec: FR-01 (partial — Action side only) / FR-05 (schema split) / FR-06 (one general Action).

## Step 0 — Blast radius (grep evidence, 2026-07-31)

Two DISTINCT string keys share the value `nda-review`, decoupled by the Binding table:

```
client dispatch key  ──►  Binding row  ──►  Action row
   consumerType             sprk_action        sprk_actioncode
   "nda-review"            (GUID lookup)       "nda-review"
```

### A. `consumerType` "nda-review" — the CLIENT dispatch/routing key (NOT renamed by task 002)
| Location | Kind | Owned by |
|---|---|---|
| `infra/dataverse/sprk_playbookconsumer-rows.json:572` (Binding `consumerType`) | routing key | task 002 may touch, but LEFT AS-IS |
| `src/solutions/SpaarkeAi/.../ConversationPane.tsx` (`ndaReviewConsumerType` state, default `"nda-review"`) | client dispatch | HOT FILE (021/031/041) — off-limits |
| `src/solutions/SpaarkeAi/.../localActionChips.ts:40,134` (`local:nda-review`, docType→consumerType map) | client chip map | other-task-owned |
| `src/solutions/SpaarkeAi/.../useConsumerChips.tsx` | client chip | other-task-owned |
| `src/client/shared/Spaarke.UI.Components/.../surfaceLaunchRegistry.ts:163` (`'nda-review'` surface entry) | surface identity | ADR-039/§10 keeps surface identity in code — off-limits |
| `tests/integration/contract/Eval/nda-review-eval-cases.json` + `NdaReviewDispatchEvalTests.cs` | DISPATCH eval — asserts `consumerType:"nda-review"` | task 002 (regression only) |

**Decision on consumerType: DO NOT RENAME.** Generalizing the *trigger* (removing NDA gating, routing non-NDA
agreements) is **FR-01 proper**, owned by the rename/classifier tasks (010 rename, 020–023 classifier/orientation),
and its references live in off-limits client + surface files. Renaming it here would break client dispatch I cannot fix
in this task → exactly the "leave things broken" failure the escalation guards against. So the client keeps triggering
via the `nda-review` consumerType; task 002 generalizes only the *Action it routes to*.

### B. `actionCode` "nda-review" — the Binding→Action link projection (RENAMED → `agreement-review`)
| Location | Kind | Action |
|---|---|---|
| `infra/dataverse/actions/nda-review.action.json` (`actionCode` field + filename) | Action mirror | RENAME → `agreement-review.action.json` |
| `infra/dataverse/outputschemas/nda-review.schema.json` (`$id`,`title` + filename) | output mirror | RENAME → `agreement-review.schema.json` |
| `infra/dataverse/inputschemas/nda-review.input.schema.json` (`actionCode` field + filename) | input mirror | RENAME → `agreement-review.input.schema.json` |
| `infra/dataverse/sprk_playbookconsumer-rows.json:579` (Binding `actionCode` field) | deploy-time projection | EDIT nda-review → agreement-review (consumerType stays) |
| `tests/eval/legal-eval-config.yaml:31` (`target_action.actionCode`) | eval traceability | EDIT → agreement-review |
| `tests/eval/README.md`, `citation_accuracy.py` docstring | doc references | UPDATE |
| `src/server/api/.../Compose/ComposeSummaryPageGenerator.cs:9-10,147-148` | XML DOC COMMENTS only (not routing) | STALE-comment tolerated (BFF hot file; comment-only) — noted, not touched |

**No server-side branch keys on the `actionCode` string** (`grep "nda-review" src/**/*.cs` = doc comments only). The
runtime resolves the Action via the Binding's `sprk_action` **GUID lookup**, never by the actionCode string, so the
rename is invisible to the production dispatch path.

### Live env state (spaarkedev1, MCP-verified 2026-07-31)
- Action row **IS deployed**: `sprk_analysisaction` GUID `34c9ecf2-cb10-f111-8342-7ced8d1dc988`, `sprk_actioncode=nda-review`, `sprk_modeltier=100000002 (Reasoning)`. (nda-r1 notes said "env-blocked / not seeded" — stale; it was seeded since.)
- Binding row deployed: `sprk_playbookconsumer` GUID `683051bd-2989-f111-8077-7ced8ddc4a05`, `consumerType=nda-review`, `sprk_action → 34c9ecf2…`, `sprk_disposition=100000000 (Informational)`.
- No `agreement-review` Action row exists yet.

## Step 1 — Rename decision + rationale

**DECISION: Generalize-in-place by UPDATING the existing Action row (GUID preserved), renaming `actionCode` nda-review → agreement-review.**

- "Generalize-in-place" in the **deployment** sense = `update_record` on the SAME row `34c9ecf2…` (systemPrompt, outputschema, name, description, actioncode). No new row, **no orphan**, and the Binding's `sprk_action` GUID lookup stays wired with zero env edit to the Binding.
- "Rename" in the **naming** sense = `actionCode` becomes `agreement-review` (matches spec FR-06 "One general agreement-review Action" + the deliverable name `agreement-review.action.json`; a type-agnostic Action must not be permanently named `nda-review`, else every future per-type consumer binds to an Action labelled "nda-review" — the exact naming-drift this project kills).
- **Atomicity confirmed → NO escalation.** consumerType (client key) is deliberately unchanged; actionCode rename is fully covered inside task-002 files + one in-place row update. There is NO server-side hardcoded consumerType/actionCode branch that a rename cannot cover atomically (the escalation trigger condition is NOT met).
- Transitional asymmetry (Binding `consumerType=nda-review` → `actionCode=agreement-review`) is INTENTIONAL and documented in the Binding `$comment`: the client still triggers via the shipped `nda-review` consumerType (its rename is FR-01/010/020), while the Action it routes to is now the generalized `agreement-review`.

Rejected alternatives:
- *New `agreement-review` row + deprecate `nda-review` row* — would orphan/dead-leave the old row on a shared dev env ("dual Actions"); the in-place update is cleaner and keeps the Binding GUID wired.
- *Keep `actionCode=nda-review`, generalize content only* — contradicts the deliverable name + FR-06; leaves a type-agnostic Action permanently mis-named.

## De-embedded taxonomy — HAND-OFF TO TASK 003 (NDA knowledge pack)

The generalized systemPrompt refers to **"the retrieved standard's own clause taxonomy"** and no longer embeds any
NDA labels. Task 003 must place the following **verbatim** into the NDA knowledge pack (KNW-011) so the model emits a
valid `standardRef` for NDA reviews. This is the ONLY NDA-specific content removed from the prompt (the Part-A overall
risk rubric is general method and STAYS in the prompt):

### NDA clause taxonomy (B1–B16) — names only (positions already live in KNW-011 RAG per nda-r1 $comment-deembed)
```
B1  Parties & mutuality
B2  Purpose
B3  Definition of Confidential Information
B4  Exclusions/carve-outs
B5  Use & standard of care
B6  Permitted recipients
B7  Compelled disclosure
B8  Term & confidentiality period
B9  Return/destruction
B10 Residual knowledge
B11 Restrictive covenants
B12 No warranty / no obligation
B13 Remedies
B14 Assignment
B15 Governing law & disputes
B16 Drafting-integrity (mechanical: inconsistent defined terms, inconsistent entity names, broken
    cross-references, numbering errors, duplicated provisions, missing schedules/exhibits, internal contradictions)
```
Also removed: the NDA-specific `standardRef` example strings ("B3 - Definition of Confidential Information") and the
literal "16-clause"/"KNW-011"/"B1-B16" references in the prompt body. The NDA pack should surface the B1–B16 taxonomy
(+ the Required/Red-flag substantive positions already in KNW-011) as the retrieved standard for `subDomain=nda`, so a
review dispatched for an NDA still cites `B3`, `B11`, etc. — now sourced from the pack, not the prompt.

> **NDA closed-set eval dependency**: `tests/eval/metrics/citation_accuracy.py` hardcodes `VALID_STANDARD_REFS = {B1..B16}`.
> That grader stays NDA-taxonomy-specific (it grades the 6-NDA closed set). Per-type packs (lease/employment) supply
> their own taxonomy + their own future eval; the general metric is not widened here (env-blocked; out of task-002 scope).

## Steps 2–6 — execution results (2026-07-31)

### Step 2–3 — generalized Action + FR-05 schema split (authored + deployed)
- `infra/dataverse/actions/agreement-review.action.json` (new) — generalized systemPrompt (8907 chars):
  type-agnostic role, ADVISORY GROUNDING RULES kept verbatim-in-spirit, Part-A risk rubric kept,
  HOW-TO-COMPARE now references "the retrieved standard's own clause taxonomy" (NO embedded B1–B16),
  scope guard generalized to "is this an agreement?" (+ "no standard retrieved → decline"), and the
  flaggedClause (grounded fact) / assessment (reasoned judgment) split made explicit + MARKER-FREE.
- `infra/dataverse/outputschemas/agreement-review.schema.json` (new) — closed 6-field item contract
  `{sectionRef, quotedText, riskLevel, flaggedClause, assessment, standardRef}`; verified BYTE-IDENTICAL
  (schema body) to the Action's embedded `outputSchema`.
- `infra/dataverse/inputschemas/agreement-review.input.schema.json` (new) — generalized documentText wording.
- Old `nda-review.{action,schema,input.schema}.json` DELETED (rename complete).
- **Schema validation**: PASSES the OpenAI-Structured-Outputs subset (additionalProperties:false,
  object-level required arrays, enums for closed sets, no anyOf/oneOf/discriminator, no property-level
  boolean required) — checked offline mirroring `OpenAiFunctionSchemaValidator` + confirmed by the new
  C# contract test (below). NOTE: the `jps-validate` SKILL targets the JPS `prompt/v1` format
  (`instruction.role`/`output.fields`); these mirror-first `sprk_analysisaction` files are a different
  shape (raw `systemPrompt` + `outputSchema`), so the skill's checks don't apply — the OpenAI-subset
  validator (server twin + the two Catalog contract-test classes) is the real gate. Documented honestly.
- **DEPLOY (spaarkedev1, in-scope)**: `Deploy-Actions.ps1` is the LEGACY R4 doc-intelligence script
  (reads `scripts/seed-data/actions.json`, keys by `sprk_name`, writes only 4 fields — NOT a vehicle for
  mirror-first rows; the seed's own `$comment` says deploy via Dataverse MCP). Deployed by **UPDATING the
  existing row in place** via Web API PATCH (a scratchpad one-off reading the mirror file so the 8.9KB
  prompt + 4.3KB schema are never hand-escaped): row `34c9ecf2…` → sprk_actioncode=agreement-review,
  sprk_name="Agreement Review", generalized sprk_systemprompt, split sprk_outputschemajson, generalized
  sprk_inputschema. Live execution was ALREADY env-blocked (no Reasoning-tier Azure OpenAI deployment,
  task 013), so the row is dormant catalog data — the in-place update breaks no in-flight live run.
- **VERIFY**: (a) `scripts/Verify-OutputSchemaField.ps1` → PASSED (column Memo/1MB/None/custom, queryable).
  (b) MCP readback: actionCode=agreement-review, outputSchema has flaggedClause+assessment, `explanation`
  removed, systemPrompt has no B1–B16 taxonomy block. (c) Binding `683051bd…` still resolves consumerType
  `nda-review` → sprk_action `34c9ecf2…` (now agreement-review), disposition Informational UNCHANGED.

### Step 4 — Binding/consumer row migration
- `sprk_playbookconsumer-rows.json`: Binding `actionCode` field nda-review → agreement-review + a
  `$comment-agreements-r1-002` documenting the asymmetry. consumerType/disposition/risk UNTOUCHED
  (ADR-043 single-source DispositionRoutability; the Informational→Compose flip is task 030).
  `Seed-PlaybookConsumers.ps1 -DiffOnly` not run — the ONLY env change needed was the in-place Action-row
  update (the Binding's GUID lookup is unchanged), so no consumer re-seed is required this task.

### Step 5 — eval suites
- **DISPATCH eval** (`tests/integration/contract/Eval/nda-review-eval-cases.json` + `NdaReviewDispatchEvalTests.cs`):
  DELIBERATELY UNCHANGED. It keys on `consumerType="nda-review"` (unchanged), asserts nothing about the
  output fields, and never reads the Binding `actionCode` field → it is a pure REGRESSION check here and
  stays green (routing generalization is FR-01/010/020, not task 002).
- **NEW C# contract test** `tests/integration/contract/Catalog/AgreementReviewOutputSchemaContractTests.cs`
  (KEEP-path, maintain-class, §11-justified): validates the new output-schema mirror against
  `OpenAiFunctionSchemaValidator`, pins the closed 6-field item contract, and asserts the FR-05 split
  (flaggedClause+assessment present, `explanation` absent). Closes the gap that `ComposeR2OutputSchemaContractTests`
  only covers the 5 compose rows (no output-schema-mirror validation covered nda-review/agreement-review).
- **Output-quality eval** (`tests/eval/`, env-blocked live grading): fixture `sample-nda-review-output.json`
  split explanation→flaggedClause/assessment; `test_citation_accuracy_offline.py` extended with an FR-05
  split-shape assertion; `citation_accuracy.py` docstring updated (metric reads quotedText+standardRef only —
  scoring unchanged); `legal-eval-config.yaml` retargeted to agreement-review + 2 new cases; README refreshed.
- **NEW cases**: `tests/eval/cases/neg-04-non-agreement-invoice.md` (non-agreement → scope-guard decline)
  + `tests/eval/cases/agr-01-employment-non-nda-generalization.md` (non-NDA agreement → NOT declined on
  scope grounds). These are OUTPUT-behavior cases (env-blocked live grading — no Azure OpenAI); authored,
  config-validated, but their live pass/fail cannot be graded in-repo.

### Step 6 — sanity run (offline)
- No offline LLM harness exists (all live grading is env-blocked). Offline-runnable proofs, ALL GREEN:
  - `python tests/eval/fixtures/test_citation_accuracy_offline.py` → ALL OFFLINE CHECKS PASSED (5 checks incl. new FR-05 split assertion).
  - `python tests/eval/metrics/load_eval_config.py` → PARSED + VALIDATED (11 cases, 6 NDA, 12 planted, 4 rubric dims).
  - `dotnet test --filter <AgreementReviewOutputSchemaContractTests|NdaReviewDispatchEvalTests|CatalogInputSchemaContractTests|ComposeR2OutputSchemaContractTests>` → **71 passed, 0 failed, 0 skipped**; BFF builds clean (warnings only).

### NOT MINE (sibling task 001 concurrent work in the shared worktree — untouched by task 002)
`src/client/shared/Spaarke.UI.Components/src/types/sprkAnalysis.ts`, `infra/dataverse/sprk_agreementtype-rows.json`,
`tasks/001-*.poml`, `tasks/TASK-INDEX.md`, `current-task.md`, `notes/001-execution-notes.md` — all task 001's changes.

### Quality-gate self-review (TEST-MODIFYING override — unconditional)
- **ADR-039**: grounded invariant (a) preserved + STRENGTHENED (fact/judgment split is now structural, not
  inline prose markers); advisory MUSTs kept verbatim-in-spirit; decline-if-ungrounded generalized; no new
  intent mechanism; routing config stays in the Binding table; no new amendment (advisory inherited). Eval
  obligation met: dispatch golden-utterance suite green (no regression) + output-schema validated by the new
  contract test + offline fixture/config. PASS.
- **ADR-043**: disposition/risk untouched (single-source DispositionRoutability); only actionCode/consumer
  migration edit. PASS.
- **ADR-016**: modelTier stays Reasoning; temperature 0.3 unchanged. PASS.
- **ADR-038**: new test in KEEP path (contract/**), maintain-class contract anchor, named per convention, no
  banned antipattern (reads a file + asserts schema shape — genuine contract test, not DI/ctor/mirror-mock). PASS.
- **§10 BFF Hygiene**: task 002 adds NO BFF code (data/prompt/test/eval only) → no publish-size/Placement
  obligation fires for this task. PASS.
- **§11**: new C# test class carries an explicit existing/extension/cost justification; no new services/entities. PASS.

### Escalation: NONE. The actionCode rename is atomically covered (no server-side consumerType/actionCode branch); consumerType left unchanged by design. Escalation trigger condition not met.
