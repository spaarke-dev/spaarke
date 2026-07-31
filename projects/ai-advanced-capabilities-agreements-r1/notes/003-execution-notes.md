# Task 003 — Knowledge packs (NDA taxonomy home + general fallback) — execution notes

> Rigor: STANDARD · Model tier: sonnet @ high. Grounding-data authoring + registry verification; no product code
> changed (BFF is read-only per this task's boundaries). The grounding-zero risk is covered by an explicit
> empirical verification step (§5), not code review.
> Deps: 001 (registry rows — DONE), 002 (Action generalization + de-embedded taxonomy hand-off — DONE).

## Step 0 — Context read

Read (in order): 002's final `agreement-review.action.json` (systemPrompt's "HOW TO COMPARE" — retrieval, not the
prompt, must supply "the retrieved standard's own clause taxonomy"); `002-execution-notes.md` (the verbatim B1–B16
hand-off); `spaarke-nda-standard-baseline.md` (nda-r1's original synthesis); `add-reference-to-index/SKILL.md`;
`sprk_agreementtype-rows.json` (001's registry mirror); `ReferenceRetrievalService.cs` (the production retrieval
filter); `tenant-pin-analysis.md` (nda-r1's NFR-06 escalation); `ActionRunner.cs` (the ACTUAL runtime consumer of
reference grounding for this Action — see the important finding in §6).

**Az CLI + Dataverse MCP connectivity confirmed live** (not env-blocked): `az account show` returned an
authenticated session against the Spaarke Development Environment tenant (`a221a95e-6abc-4434-aecc-e48338a1b2f2`);
`az search admin-key show` and `az cognitiveservices account keys list` both succeeded — full live indexing +
verification was possible this task, unlike task 002's Reasoning-tier-deployment blocker (see §7 acceptance table).

## Step 1 — KNW-011 restructure

**Repo home correction**: the task brief guessed the pack source might live under `projects/ai-advanced-capabilities-nda-r1/notes/` or a new `projects/ai-advanced-capabilities-agreements-r1/knowledge/` folder. Investigation found
the ACTUAL indexed source for KNW-011 (and KNW-001..KNW-010) lives at
`projects/x-ai-spaarke-platform-enhancements-r1/notes/design/knowledge-sources/KNW-0NN-*.md` — a shared,
project-agnostic "knowledge-sources" catalog folder used by every prior KNW pack regardless of which project
authored the content. Per root CLAUDE.md §11 (reuse-first), this task extends that existing home rather than
starting a parallel one: KNW-011 was edited in place there, and KNW-012 was added alongside it in the same folder.

**What changed in KNW-011** (no substantive position changed — verified byte-for-byte against the nda-r1 baseline
for every Required/Acceptable/Red-flag line):
- Added a `---` horizontal-rule separator between every one of the 16 Part-B clause subsections (previously only
  Part-level sections were separated). This gives the ingest chunker's sentence/newline-boundary heuristic an
  unambiguous break candidate at every clause boundary.
- Added a short "Restructure note" provenance block explaining why (002's de-embed hand-off) and confirming no
  content changed.
- Added B1–B16 tokens to the `Keywords` front-matter line (helps keyword/BM25 leg of the hybrid search).
- Re-indexed with a smaller chunk size (see §3 for the full rationale) so each clause survives intact inside a
  single chunk.

## Step 2 — KNW-012 general fallback pack (authored fresh — no prior baseline existed)

Structure deliberately mirrors KNW-011's taxonomy pattern (G1–G16 clause IDs, `standardRef`-citable, `---`
separated) so the generalized Action's "cite the retrieved standard's own clause taxonomy" instruction works
identically for both packs — but is **explicitly fallback-grade**, per the project's Design Lens 3(d) constraint:
- Each of the 16 categories (G1 Parties & recitals ... G16 Drafting-integrity) gets ONE "Generic position" sentence
  — no Required/Acceptable/Red-flag-severity triad like KNW-011's much deeper per-clause treatment. This is the
  deliberate value asymmetry the project's `<notes>` calls for ("type packs = the value, general = fallback").
- An explicit "NO FIRM STANDARD — GENERAL REVIEW" framing paragraph the Action can ground a decline/caveat on,
  and a "Part C — When this pack is insufficient" section flagging that a more specific pack may exist/be needed.
- No confidence-score claims, no counsel-ratification checklist (unlike KNW-011 — there is no company standard
  here to ratify, only market convention).

**Justification check (§11 three-question template)**: *Existing* — no prior general/fallback knowledge pack
existed (grep-verified: zero `KNW-0` files matched "general" or "fallback" before this task). *Extension* — cannot
extend KNW-011 (NDA-specific positions) or any other single-type pack; a fallback pack is a genuinely new content
surface, not a duplicate. *Cost-of-doing-nothing* — without it, the registry's `general` (isfallback=Yes) row has
a `sprk_knowledgepackref` pointing at nothing, and the classifier's catch-all path degrades to an ungrounded
Action run for the class of documents it exists to catch (settlement agreements, hybrids, low-confidence routes).

## Step 3 — Indexing (live, both packs)

Tool: `scripts/ai-search/Add-ReferenceToIndex.ps1` (the `add-reference-to-index` skill's worked script), run twice
against the live `spaarkedev1` environment + `spaarke-search-dev` / `spaarke-openai-dev` resources.

**Chunk-size rationale (the actual judgment call this task required)**: the obvious reading of "per-clause chunks
preferred for citation granularity" is to chunk as small as possible. A dry run at the script's small end
(`-ChunkSize 400 -ChunkOverlap 50`) produced 47 chunks for KNW-011 / 30 for KNW-012 — but reading `ActionRunner.cs`
(the ACTUAL runtime consumer, not the playbook-node executor) showed `ReferenceGroundingTopK = 12` with an explicit
comment: *"a generous cap pulls the whole relevant standard (e.g. all KNW-011 clauses) rather than only the
top-5 nearest."* That design assumes the WHOLE pack fits inside ~12 retrieved chunks. Over-fragmenting to 47/30
chunks would mean only ~25% of either pack's own content could ever surface in one retrieval call, the opposite of
the intended "generous cap" effect — and would also dilute per-chunk semantic density, making each chunk rank
*lower* against a broad document-length query (fewer matching concepts per embedding). Chosen instead:
`-ChunkSize 1300 -ChunkOverlap 200` for KNW-011 and `-ChunkSize 1000 -ChunkOverlap 150` for KNW-012 — sized so the
total chunk count lands close to the TopK=12 budget while every clause still survives whole inside one chunk
(verified by inspecting all 14 + 13 chunks' actual content — see below; no B- or G-clause is split mid-content).

| Pack | Ref | Chunks (before → after) | Dataverse catalog record | Notes |
|---|---|---|---|---|
| KNW-011 (NDA) | `KNW-011` | 8 → **14** | `19552b15-068d-f111-8076-3833c5deff74` (NEWLY created — none existed before this task, a pre-existing gap from nda-r1 task 012's env-blocked session) | Old 8 chunks deleted, 14 new uploaded. All 16 B-clauses verified whole-within-one-chunk. |
| KNW-012 (general) | `KNW-012` | 0 → **13** | `990a3921-068d-f111-8076-3833c5deff74` (new) | First index of this source. All 16 G-clauses verified whole-within-one-chunk. |

Both indexed at `tenantId=system`, `documentType=legal`, 3072-dim `text-embedding-3-large` vectors, into
`spaarke-rag-references` — same convention as KNW-001..KNW-010.

Chunk-boundary spot check (full content dumped + read for both packs): every clause (B1–B16, G1–G16) appears
complete — heading through its full Required/Acceptable/Red-flags (or Generic-position) text — inside at least one
chunk; the `---` separators reliably captured the chunker's newline-snap at (or within a few chars of) each clause
boundary. Sample (KNW-011 chunk 4, retrieved live in §5's corpus-wide query):
```
### B3. Definition of Confidential Information
- **Required**: Broad enough to cover all forms — oral, visual, written, electronic, derived, pre-existing — not
  gated on "marked confidential."
- **Acceptable**: Marking requirement only if paired with a catch-all for information a reasonable person would
  deem confidential.
- **Red flags**: Marking-only definition with no oral/unmarked protection → ...
```

## Step 4 — Registry sync verification (nda + general rows)

Both `sprk_knowledgepackref` and `sprk_classificationcue` were ALREADY non-null on both rows (set by task 001) —
verified via `mcp__dataverse__read_query` against the LIVE `spaarkedev1` env that these match
`infra/dataverse/sprk_agreementtype-rows.json` exactly:

| Row | `sprk_knowledgepackref` (env) | matches seed JSON? | `sprk_classificationcue` (env) |
|---|---|---|---|
| `nda` (`d557c894-908c-f111-8077-7ced8ddc4cc6`) | `KNW-011` | Yes | non-null (set by task 001) |
| `general` (`433e1688-908c-f111-8077-7ced8ddc4cc6`) | `KNW-012` | Yes | non-null (set by task 001) |

**KNW-012 reconciliation**: the seed JSON's `$knowledgepackref-convention` comment flagged `KNW-012` as a FORWARD
REFERENCE task 003 had to resolve — either by indexing under exactly that id (no value change needed) or by
re-pointing both mirror + env to a different id. This task indexed the general pack under `KNW-012` exactly as
predicted, so **no `sprk_knowledgepackref`/`sprk_classificationcue` value changed on either row** — only the seed
JSON's `$comment` / `$knowledgepackref-convention` / `$comment-task003-reconciliation` metadata was updated to
record the resolution (no env write needed for this reconciliation beyond the two new Dataverse catalog records
created as a side effect of running `Add-ReferenceToIndex.ps1` — see §3 table).

`sprk_confidencethreshold` remains `null` on both rows (task 001's decision — global 0.85 baseline applies); this
task's acceptance criterion only requires `knowledgepackref` + `classificationcue` non-null, which was already true.

## Step 5 — Production-filter retrieval verification

Replicated the EXACT filter/search shape `ReferenceRetrievalService.BuildSearchOptions` builds (hybrid
semantic+vector search, `rag-references-semantic-config`, `contentVector3072`, `(tenantId eq '{tenant}' or
tenantId eq 'system')`) against the LIVE `spaarke-rag-references` index — real embeddings generated via the
`spaarke-openai-dev` `text-embedding-3-large` deployment, not a mock. Script:
`scripts/ai-search/Verify-ProductionFilter.ps1` written for this task (scratchpad-only, not committed to the repo
— ad hoc verification tooling, not a product deliverable).

**Positive #1 — NDA query, isolated to KNW-011** (`knowledgeSourceId eq 'KNW-011'` added to the production filter
to prove the pack itself, in isolation):
- Query: an NDA-style document excerpt ("This Mutual Non-Disclosure Agreement... Confidential Information means...").
- TenantId: real Entra tenant `a221a95e-6abc-4434-aecc-e48338a1b2f2`.
- **Result: 12 of 14 chunks returned** (TopK=12 cap reached — all results genuinely from KNW-011).

**Positive #2 — general query, isolated to KNW-012**:
- Query: a Consulting Services Agreement excerpt (not an NDA, not any other specifically-registered sub-domain).
- **Result: 12 of 13 chunks returned**, all from KNW-012.

**Positive #3 — NDA query, CORPUS-WIDE** (no `knowledgeSourceId` filter — this is the literal, unmodified shape
`ActionRunner.RetrieveReferenceGroundingAsync` calls in production; see §6 finding):
- **Result: 12 chunks**, sources `KNW-002 (7), KNW-001 (2), KNW-011 (2), KNW-012 (1)`. KNW-011 content DID surface
  (2 chunks, including the literal `### B3. Definition of Confidential Information` clause text — see §3 sample).

**Positive #4 — general query, CORPUS-WIDE**:
- **Result: 12 chunks**, sources `KNW-001 (3), KNW-008 (3), KNW-012 (2), KNW-002 (1), KNW-006 (1), KNW-009 (1),
  KNW-011 (1)`. KNW-012 content surfaced (2 chunks).

**Negative #1** — mismatched tenant (`11111111-1111-1111-1111-111111111111`), filter `tenantId eq
'<mismatched>' and knowledgeSourceId eq 'KNW-011'` (NO `system` OR-clause, i.e. the naive pre-NFR-06-fix shape):
**0 results.** Confirms the tenant pin is a real, enforced boundary — not a filter that trivially matches
everything — and that KNW-011's content is genuinely gated on the `system` sentinel being present in the filter.

**Negative #2** — same shape for KNW-012 with a different mismatched tenant (`22222222-2222-2222-2222-222222222222`):
**0 results.**

**Conclusion**: the production filter (with the `system` OR-clause) returns non-zero, correctly-grounded chunks for
both packs, under both an isolated-by-source query and the actual unscoped corpus-wide query ActionRunner issues
today; the naive mismatched-tenant filter (no OR) returns zero, proving the pin is real. **No escalation required**
— this task's `<escalation>` trigger ("if production-filter retrieval returns zero chunks for either pack and the
cause is tenant/index CONFIG") did not fire; retrieval returned non-zero for both packs.

## Step 6 — Important finding for the team (not a task-003 defect; forward note for Phase 2)

`ActionRunner.RetrieveReferenceGroundingAsync` (the actual runtime path for the `agreement-review` Action, as
opposed to the playbook-node executor) does **not** set `ReferenceSearchOptions.KnowledgeSourceIds` at all — it
searches the ENTIRE `spaarke-rag-references` corpus (all KNW-001..KNW-012, not just the classified agreement's own
pack) with `TopK=12`, `MinScore=0.0`. Grep confirms **no BFF server code reads `sprk_knowledgepackref`** yet — only
the TypeScript registry-mirror type (`sprkAnalysis.ts`, task 001) exists. This means, as of today:
- The registry's `sprk_knowledgepackref` is correctly populated and the packs are correctly indexed + retrievable
  (this task's job, done) — but the ACTUAL runtime retrieval call does not yet use `sprk_knowledgepackref` to
  scope the search to the classified type's own pack.
- Per §5 Positive #3/#4, this means a real NDA review today would retrieve only ~2 of KNW-011's 14 chunks in one
  call (crowded out by the also-relevant KNW-002 NDA checklist and KNW-001 glossary), not "the whole standard" as
  `ActionRunner`'s own code comment intends.
- This is consistent with — and does not block — this project's own task graph: wiring the classifier's resolved
  `subDomain` → `sprk_knowledgepackref` → `KnowledgeSourceIds` is Phase 2 work (020–023, "classifier / orientation
  / subDomain envelope / explicit-path bind"), not Phase 0 (this task). Flagging here so Phase 2 task authors are
  aware `KnowledgeSourceIds` scoping is the concrete mechanism still needed in `ActionRunner.cs` (read-only for
  this task; `src/server/api/**` is out of scope per task 003's HARD BOUNDARIES).

## Files changed

- `projects/x-ai-spaarke-platform-enhancements-r1/notes/design/knowledge-sources/KNW-011-spaarke-nda-standard.md`
  — restructured in place (clause separators + provenance note; no substantive position changed).
- `projects/x-ai-spaarke-platform-enhancements-r1/notes/design/knowledge-sources/KNW-012-general-agreement-review-fallback.md`
  — new file (general fallback pack).
- `infra/dataverse/sprk_agreementtype-rows.json` — comment-only reconciliation (no row values changed; both
  `nda`/`general` rows were already correct from task 001).
- `spaarke-rag-references` AI Search index — KNW-011 re-indexed (14 chunks), KNW-012 indexed fresh (13 chunks).
- 2 new `sprk_analysisknowledge` Dataverse catalog records (KNW-011 previously had none; KNW-012 new).
- This notes file.

Not touched (per HARD BOUNDARIES): `infra/dataverse/actions/**`, `outputschemas/**`, `inputschemas/**`, `tests/**`,
`src/client/shared/Spaarke.Compose.Components/**`, `.claude/**`, `current-task.md`, `TASK-INDEX.md`. `ActionRunner.cs`
/ `ReferenceRetrievalService.cs` were read-only references for verification, not modified.

## Acceptance criteria

| # | Criterion | Status | Evidence |
|---|---|---|---|
| 1 | Production-filter retrieval returns non-zero chunks for NDA pack and general pack | **PASS** | §5 Positive #1–#4 (12/14 and 12/13 isolated; 2 and 2 corpus-wide) |
| 2 | Given 002 Action + NDA pack, a sample NDA review cites B-clause `standardRef`s sourced from RETRIEVED content | **PARTIAL / ENV-BLOCKED** | Retrieval-layer half rigorously proven (§5 Positive #1/#3 show literal `### B3. Definition of Confidential Information` text returned, tenant-pinned, non-fabricated). The LLM-generation leg cannot run: task 002 confirmed no Reasoning-tier Azure OpenAI deployment is available in this environment (`DEPLOYMENT-RUNBOOK.md` / 002 notes). Not faked. |
| 3 | Given 002 Action + general pack, a non-NDA agreement review runs and cites the general pack's guidance | **PARTIAL / ENV-BLOCKED** | Same env blocker as #2. Retrieval-layer proof: §5 Positive #2/#4 show KNW-012 G-clause content (e.g. G8 Insurance, G11 Assignment) returned correctly tenant-pinned. |
| 4 | nda + general registry rows carry non-null knowledgepackref + classificationcue; seed JSON matches env | **PASS** | §4 — both fields non-null on both rows, live-env-verified to match seed JSON exactly (already true from task 001; task 003 verified + reconciled the KNW-012 forward reference). |
| 5 | Negative: mismatched tenant filter returns zero (documented, not silently ignored) | **PASS** | §5 Negative #1/#2 — 0 results for both packs under a mismatched-tenant / no-system-fallback filter. |

## Deviations / escalations

None. The task's own `<escalation>` trigger (zero chunks caused by tenant/index CONFIG) did not fire — both packs
returned non-zero chunks under the production filter. The §6 finding (ActionRunner not yet scoping by
`KnowledgeSourceIds`) is a forward-looking note for Phase 2, not a task-003 blocker: it does not prevent either
acceptance criterion from being met at the retrieval-layer, and fixing it would require editing
`src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/ActionRunner.cs`, which is out of this task's scope
(read-only boundary) and is explicitly Phase 2's job per the project's own task graph.

## Task status

POML `003-knowledge-packs-nda-and-general-fallback.poml` status updated to `completed`. Per this task's explicit
HARD BOUNDARIES, `TASK-INDEX.md` and `current-task.md` were NOT touched — the orchestrating session/human should
apply root CLAUDE.md §7's transition steps (TASK-INDEX 🔲→✅, reset `current-task.md`) after reviewing this report.
