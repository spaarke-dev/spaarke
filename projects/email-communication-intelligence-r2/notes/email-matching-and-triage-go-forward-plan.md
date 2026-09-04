# Email Matching & Triage — Go-Forward Plan

> **Created**: 2026-09-03 · **Owner-directed** (this session)
> **Supersedes**: `notes/email-record-matching-approaches.md` (reviewed, worthwhile residue extracted here, then deleted — see §"Provenance" below)
> **Related**: `notes/DEFECT-triage-not-populating-root-cause.md`, `docs/architecture/communication-intelligence-architecture.md` (§3–§7, refreshed 2026-09-03), `notes/016-affinity-rung-complete.md` (R-1 affinity loop)

---

## Why this plan exists

The strategy note `email-record-matching-approaches.md` proposed a 5-tier "matching ladder" as if greenfield. A codebase inventory (2026-09-03) found the ladder is **already built** as the **13-rung Association Engine** (`Services/Communication/Engine/`) — deterministic-first, confidence-banded (noisy-OR reinforcement), with a C-1 auto-file narrowing + core-writable gate and an AI-never-auto-files invariant. That engine is now documented canonically in `docs/architecture/communication-intelligence-architecture.md` §4–§7 (refreshed this session, 6→13 rungs).

**Assessment:** the note had ~2 genuinely additive ideas; the rest was already built or already documented better. Per owner direction, the additive ideas are **extracted + documented here** (so they are not lost), folded into the plan below alongside the two standing priorities (P1 triage, P2 semantic search), and the original note is deleted to avoid the "is this greenfield?" confusion.

---

## The 5-tier proposal, mapped to what exists (one-line each)

| Proposed tier | Built as | State |
|---|---|---|
| 1 Structural/deterministic | Rungs order 0–1: ExplicitReference, IdentifierReverseLookup (7-type), RecipientAlias (Bcc), TrackingToken (signed footer), ThreadContinuity | ✅ Mature — the only auto-file-eligible signals |
| 2 Registry/rule | ParticipantCorrelationRung + ADR-048 `sprk_communicationparticipant` index + `sprk_affinity` store | ✅ Mature but split across 3 mechanisms |
| 3 Statistical linkage | `AffinityRung`/`sprk_affinity` frequency learning | ⚠️ **Learning loop not closed** (write hook = R-1) |
| 4 Embedding retrieval | `SemanticMatchRung` over `spaarke-records-index` (`text-embedding-3-large`, 3072-dim, hybrid RRF) | ✅ Covered (org not embedded) |
| 5 Generative | `AiClassificationRung` + triage/propose/create-task Actions | ✅ Covered, deliberately constrained (never auto-files) |

Two framing corrections captured in the arch-doc refresh: the engine does **not** short-circuit ("each tier fires only if prior fails") — it runs all rungs and **reinforces** agreeing signals via noisy-OR; and `IEmailFilterService` (the note's "Tier-1 home") **does not exist** — identifier extraction lives in the rungs. Do not build it (§11).

---

## Extracted ideas (the durable record of what was worth keeping)

### IDEA-1 — Tiered evaluation harness (genuinely absent, high value)
Precision/recall for the matching engine, **broken out per rung**, against a **golden labeled set** of historical email→record links — not an aggregate accuracy number. This is the pre-requisite gate before changing any auto-file threshold or kill-switch. Today there is no such harness; threshold changes (e.g. the 0.85 auto-file bar, the C-1 narrowing) are owner-judgment without a measured precision/recall backstop. → Plan item **G4**.

### IDEA-2 — Party / relationship graph (future extension, beyond flat index)
Today Tier-2 is a flat participant index + affinity frequency store. The note's insight: a `Person ↔ Organization ↔ Matter/Project/Invoice` **relationship graph** would let Tier-2 resolve not just "this address belongs to counterparty X" but "counterparty X is active on 3 matters — which one," which is the tractable form of Spaarke's two-sided (firm + client) data position. This is a real extension, not R2 scope. → Plan item **G5 (parked)**.

Also validated-not-new (kept because the note reinforces existing design): provenance-carried-on-the-link (already `sprk_associationprovenance`), no-silent-override (worth an explicit invariant test), and the feedback loop (= closing the affinity write, G3).

---

## Plan

| # | Item | ADR gate | Size | Status |
|---|---|---|---|---|
| **P1** | ~~Seed the triage routing catalog~~ → **catalog ALREADY SEEDED (verified live).** Real residual = triage CATEGORY resolution (100% miss). **FIX DONE (code) 2026-09-03**: (1) `LookupChoicesResolver` entity-set pluralization y→ies (`sprk_triagecategory`→`sprk_triagecategories`, was `…categorys`→404) so the 7 taxonomy names reach the triage prompt; (2) tolerant category name-match in `ResolveTriageCategoryIdAsync` (defense). 2 regression tests green. **Needs BFF redeploy + live re-capture to confirm.** | none | S→M | 🟩 code-done, deploy-pending |
| **P2** | ~~Email `.eml` semantic indexing~~ → **ALREADY WORKING (verified live 2026-09-03: 10/10 recent `.eml` archives `searchindexed=True`).** Pillar-B #5 was stale (fix #3 `c0bd37fdc` on branch/deployed). Owner's question answered — see "P2 FINDING". Only residual = an **optional** email-metadata-facet enhancement (owner decision). | none | — | 🟩 done / decision-pending |
| **G1** | **Refresh `communication-intelligence-architecture.md`** §3–§5 (6→13 rungs, always-run AI, C-1 narrowing, 10-step pipeline). | none | S | ✅ **DONE 2026-09-03** |
| **G2** | **Delete the categorization dead-seam** (`RunCategorizationAsync`) — no persistence target; category+urgency already delivered by triage. Closes ESCALATION E1. Arch-doc §3 updated. | none | XS | ✅ **DONE 2026-09-03** (build clean) |
| **G3** | ~~Close the affinity feedback loop~~ → **ALREADY DONE (R-1, 2026-08-06).** `POST /api/communications/{id}/confirm-affinity` + `AffinityConfirmationRecorder` + `EmailWorkspace.tsx` client wiring + tests all shipped. Learning loop is closed. | none | M | ✅ done (prior) |
| **G4** | **Tiered eval harness** (IDEA-1): golden labeled email→record set; per-rung precision/recall; the gate before any threshold/kill-switch change. **Enhancement — no labeled dataset exists today; a mini-project.** `/defer` + owner scope decision (not a bug). | none | M–L | ⏸️ deferred (owner decision) |
| **G5** | **Tier-3 probabilistic scorer + party/relationship graph** (IDEA-2 + Fellegi-Sunter/GBT). **ADR-013 forbids ML in this path today → requires a §6.5 Path-B amendment FIRST.** Park; `/defer` + ADR-013 note. | **ADR-013 (Path B)** | L | ⏸️ parked |

**Note on G5 vs G3:** closing the affinity *loop* (G3) is deterministic frequency counting and needs no ADR change; only the *trained classifier* (G5) needs the ADR-013 conversation. Do not let the G5 gate block G3.

---

## P1 FINDING — the catalog was already seeded; triage works; category is the real bug (2026-09-03)

Read-only verification against `spaarkedev1` **overturned** `DEFECT-triage-not-populating-root-cause.md` (dated 2026-08-13, now stale):

- **Catalog EXISTS + enabled + wired**: all 3 `sprk_analysisaction` rows (triage-email / propose-field-updates / create-task-from-email) and all 3 `sprk_playbookconsumer` rows (email-triage / email-propose / email-create-task) are present, `sprk_enabled=true`, each with a valid `_sprk_action_value` lookup. (Someone seeded them after 2026-08-13, likely via `scripts/seed-email-intelligence-actions.ps1` + `Seed-PlaybookConsumers.ps1`.)
- **Triage WORKS on real substantive inbound captures**: e.g. `PAT-415062 Outlook to Spaarke` — `sprk_triagepriority`=Low, `sprk_riconfidence`=0.25, `sprk_reviewoutcome` set, and a **rich real `sprk_triagesummary`** ("This email discusses a new patent application… includes linked documents such as an invoice and a signed NDA…"). Provenance shows `AiClassification` fired.
- **The one genuine bug — triage CATEGORY never resolves (100% miss)**: `sprk_triagecategory` is empty on every row despite a **populated 7-row taxonomy** (Client instruction · Court / Filing · Invoice / Billing · Scheduling · Opposing counsel · Administrative · Marketing / Noise). Root cause: `PersistTriageResultAsync` → `ResolveTriageCategoryIdAsync` does an **EXACT `sprk_name` match** (`CommunicationEnrichmentService.cs:649`), and the triage Action's category output (`$choices = "lookup:sprk_triagecategory.sprk_name"`, `output.fields[0]`) is **not binding the model to those exact 7 names** — the constrained-decoding `sprk_outputschemajson` derives category as a *free string* (per FR-16), so the taxonomy vocabulary reaches the model only as soft prompt text (via `LookupChoicesResolver` at render time — which may not be resolving on this path). The model emits a category label outside the 7 → exact match fails → category left unset (best-effort, logged). **This is the real "fix triage" work.**
- **Not bugs (explained)**: trivial/empty emails ("To Do Add In") and outbound/programmatic rows get no triage because `AiClassification` produced no signal to reuse — acceptable. `riconfidence`=0.25 is a real computed value (`UrgencyWeight(Low) × deterministicAgreement 1.0`), not a floor.

**Re-scoped P1 = fix category resolution.** Candidate fixes (pick after owner steer): (a) verify `LookupChoicesResolver` actually injects the 7 names into the rendered triage prompt; (b) make the category constrained-decoding schema an **enum of the live taxonomy names** (contradicts FR-16 "dynamic" — needs a decision); (c) add a **fuzzy/normalized** name match + optional create-on-miss in `ResolveTriageCategoryIdAsync` (ADR-024 says never fabricate — so normalize-match, not create). Needs a live re-capture with the actual emitted `result.Category` logged to confirm which.

## P2 FINDING — .eml is already indexed; emails are NOT searched differently (2026-09-03)

Live verification overturned Pillar-B UAT finding #5 (like P1's root-cause doc, it went stale):

- **`.eml` archives ARE search-indexed**: 10/10 most-recent `sprk_document` email-archives have `sprk_searchindexed=true` with `sprk_searchindexcompletedon` timestamps (2026-09-01 → 09-03). The prior "false" was the pre-fix state; **fix #3 (`c0bd37fdc`, email-artifact priority-option bug) is on-branch and effective** — it had aborted `UploadFinalizationWorker` before `EnqueueRagIndexingAsync` (the step only emails reach, since only they create a `sprk_emailartifact`).
- **Are emails searched/analyzed differently? — NO** (the owner's question). Captured `.eml` goes through the **identical** pipeline as any document: MimeKit extraction (`TextExtractorService.ExtractFromEmlAsync`) → same `ITextChunkingService` chunking → same embeddings → **same `spaarke-files-index`**. There is no email-specific chunking, no separate email index. The ONLY email-specific step is text extraction (MimeKit/MsgReader flattens headers+body to a `=== EMAIL MESSAGE ===` / `=== EMAIL BODY ===` text blob). Attachments are indexed as their own child `sprk_document` records (not recursively from the `.eml`).
- **The one latent gap (optional enhancement, owner decision):** email-native metadata (from / to / cc / **thread** / sent-date) is **flattened into free text, not indexed as structured filterable facets**. `TextExtractorService` produces an `EmailMetadata` object but `FileIndexingService` reads only `extraction.Text` and discards it; `KnowledgeDocument`'s only facets are `ParentEntityType/Id/Name` + `FileType`. So you can *semantic-search* email content today, but you cannot *filter* the RAG index by sender/date/thread. Adding that = extend `KnowledgeDocument` + the `spaarke-files-index` schema + carry `EmailMetadata` through `FileIndexingService` → **requires an index-schema migration + reindex** (non-trivial). **Not built — flagged for owner decision** (was not part of the "send .eml to the index" ask).
- **Minor housekeeping (noted, not changed — out of the real-time-capture scope):** `BulkRagIndexingJobHandler` catch-up filters on `sprk_ragindexedon eq null` while the success path writes `sprk_searchindexed*` — a field split-brain, moot while `ScheduledRagIndexing.Enabled=false`.

**Net: P2 core ask is satisfied in live. No code change required.** Open decision = build email-metadata facets (Y/N).

## Sequencing

1. **P1** — seed triage catalog (verify dev empty first; touches live `spaarkedev1` → confirm before writing).
2. **G2** — delete categorization dead-seam (cheap; do alongside P1's arch-doc touch).
3. **P2** — email `.eml` semantic indexing + the "searched differently" investigation.
4. **G4** — eval harness, then **G3** — affinity loop.
5. **G5** — parked behind ADR-013 Path-B; `/defer`.

---

## P1 ROOT-CAUSE CORRECTION (2026-09-04) — the y→ies fix was necessary but insufficient

UAT round-1 (2026-09-04) FALSIFIED the "category fixed" claim: the deployed PR #936 build still left `sprk_triagecategory` = `None` on a fresh substantive capture (`Fw: LITG-119896 Monte Rosa…` — triaged fine on priority/summary/RI-conf/review, category empty). Empirical root-cause (Empirical-Reproduction-FIRST, §F.3) found the deeper gap:

- **Triage runs on the Linear AI Consumer path** (`CommunicationTriageAi` → `IActionResolver`/`IActionRunner`), **not the node path**. `LookupChoicesResolver.ResolveFromJpsAsync` is invoked **only** by `AiAnalysisNodeExecutor` (node path). So the PR #936 `y→ies` fix was on a resolver **the triage path never calls** — correct, but insufficient.
- The TRIAGE-EMAIL Action declares **`structuredOutput:true`**, so `PromptSchemaRenderer` renders only *"Return valid JSON matching the provided schema"* — the output fields (and any `— one of:` enum) are NOT rendered into the prompt. And the catalog `sprk_outputschemajson` leaves `category` a **free string** (FR-16). So the 7 taxonomy names reached the model through **neither** channel → free-form label → no taxonomy match → category unset (100% miss, since triage shipped).
- **Real fix (`ceba44928`, ActionRunner, linear-path only):** (1) pre-resolve `$choices` before render (mirror the node path) via `IServiceScopeFactory` (Scoped resolver from the Singleton runner); (2) inject the resolved values as a JSON-Schema **`enum`** on the matching output property so structured-output decoding **enforces** the live taxonomy. Resolved per-run from live Dataverse → **FR-16 dynamism preserved** (catalog schema stays free string; an admin-added category appears next run). Best-effort/non-fatal (NFR-04). Also fixes any other JPS linear Action with `$choices` (prefills), which had the same latent gap.
- Seam test `tests/integration/seam/Ai/ActionRunnerChoicesResolutionSeamTests.cs` (real ActionRunner+PromptSchemaRenderer+LookupChoicesResolver; positive = enum lands on category, control = free string, Verify pins `sprk_triagecategories`). 80-test ActionRunner/renderer/linear regression green; 26 triage/enrichment green.
- Deployed to `spaarke-bff-dev` (45.45 MB, hash-verified, healthy). **Awaiting UAT round-2** (fresh capture → category populates). **NOT yet merged to master.**

## Session closeout (2026-09-03)

| Item | Final status |
|---|---|
| **P1 triage category fix** | ✅ **DONE + DEPLOYED** — PR #936 (`edaac1f88`) merged to master; BFF deployed to `spaarke-bff-dev` (45.45 MB, hash-verified, healthy). **UAT confirmation pending**: send a test email → verify `sprk_triagecategory` now populates (was 100% empty). |
| **P2 .eml indexing** | ✅ already working live (verified). Optional email-metadata-facets enhancement = owner decision (no new index; add fields to `spaarke-files-index`). |
| **G1 arch doc** | ✅ merged (#936). |
| **G2 categorization dead-seam** | ✅ merged (#936). |
| **G3 affinity loop** | ✅ already done (R-1). |
| **G4 eval harness / G5 scorer+graph** | ✅ **SCOPED** → `G4-G5-matching-enhancements-scope.md`. **No ADR amendment required** (ADR-013 permits ML via facade; ADR-045 only bars AI/ML auto-file). Build order + sizing documented; awaiting owner go-ahead to build. |
| **Minor — #4 attachments** | ✅ verified working live (child docs linked to parent `.eml`). |
| **Minor — ribbon icons** | ✅ not a live issue (deployed XML manifest icons all exist). |
| **Minor — Entra redirect URIs** | ✅ documented in `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §7.3 (H3) — `brk-multihub://<host>` + `https://<host>/auth-callback.html` SPA redirects, per host. |
| **Minor — §A.4 SaveFlow footer** | Recommendation: keep the richer "Document Saved" card (owner decision open). |

## Provenance

`email-record-matching-approaches.md` was reviewed 2026-09-03. Its worthwhile residue (IDEA-1, IDEA-2, and the framing corrections) is captured above; its 5-tier taxonomy is superseded by the as-built engine (`communication-intelligence-architecture.md` §4–§7). The original note is **deleted** — retaining it risks a future reader treating the built ladder as greenfield and rebuilding it. This plan + the refreshed architecture doc are the go-forward references.
