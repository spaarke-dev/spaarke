# Insights Engine — Phase 1 Minimum Revision

> **Status (as of 2026-05-28)**: **Integrated into [SPEC.md](SPEC.md) as the canonical Phase 1 scope.** Preserved here as the rationale narrative.
> **History**: Authored as a third review/assessment after SPEC-refinement-addendum.md (2026-05-27). Adopted with three preservations from the prior SPEC (see SPEC.md §3.5 facade placement, §3.1 `IInsightGraph` interface design preserved while Cosmos implementation defers, §3.3 Cosmos as first Phase 1.5 deliverable).
> **Note on references to SPEC-refinement-addendum.md**: That file was **deleted from the working tree on 2026-05-28** once its still-valid content (D-52 single-tenant, D-54 questions-as-playbooks) was captured in [decisions.md](decisions.md) and its superseded/deferred content was no longer needed. References to it in §9 (the supersession table) and elsewhere in this doc are preserved as historical lineage — readers wanting the original 2026-05-27 narrative can find it in git history.
> **Suggested file location**: `projects/ai-spaarke-insights-engine-r1/SPEC-phase-1-minimum.md`
> **Purpose**: After review feedback from the implementation team, the previous refinement addendum was found to be too infrastructure-heavy without committing to actual Observation production. This document corrects that, makes the Precedent concept concrete, and narrows Phase 1 to what's genuinely needed to ship a real Insights Engine — one that produces real Observations from real documents and synthesizes real Inferences over them.
> **Companion documents**: SPEC.md, design.md, decisions.md, `SPEC-refinement-addendum.md` (partially superseded — see §9).
> **Naming convention**: Plain backticks = real Spaarke components verified against ai-inventory.md or existing ADRs; `[PROPOSED: name]` = new component or entity proposed by this document; "e.g." = illustrative content, not normative naming.

---

## 0. What the Insights Engine actually is

The Insights Engine is **Spaarke's context production service** — the system that produces, persists, and serves structured contextual claims about the organization's work, with provenance, confidence where applicable, and evidence-sufficiency rules where applicable.

"Context production" is intentionally broad. It includes:
- **Deterministic claims** computed from source data (durations, aggregates, status, rule-based threshold flags)
- **Probabilistic claims** extracted from document content (outcomes, settlement amounts, decisions rendered, deal terms)
- **Synthesized claims** combining the above to answer specific questions (cost predictions, comparable-matter analyses, anomaly assessments)

AI is one technique among several. Some context production paths use no LLM at all. Some use a single LLM call for typed extraction. Some compose multiple steps including LLM synthesis. The Engine's job is to orchestrate all of these uniformly, return them in a uniform response envelope (`InsightArtifact`), apply uniform caching and authorization, and render them through a uniform UI primitive (the Insight Card).

This broader framing supersedes the narrower "Insights = AI synthesis only" framing in earlier documents. AI-only framing leaves deterministic contextual claims homeless; either they get a second parallel service or they get inconsistent rendering. Neither is correct.

---

## 1. The four-tier taxonomy made concrete

The existing taxonomy (Fact / Observation / Precedent / Inference) describes the *trust profile* of a claim — how it was produced, what evidence backs it, what confidence applies. Concrete examples:

| Tier | Example | Source | Confidence | Lives in |
|---|---|---|---|---|
| **Fact** | "Matter M-1234 has been pending 287 days" | Deterministic computation from `sprk_matter.openDate` and current date | 1.0 | Live query against Dataverse (no persistence) |
| **Observation** | "Matter M-1234 settled for $310,000" | LLM extraction from the settlement agreement document with verbatim quote | 0.91 | `[PROPOSED: insights-index]` |
| **Precedent** | "In IP licensing matters with BigFirm LLP, cure-period clauses typically survive negotiation (12 of 14 matters)" | SME-authored pattern statement, citing the supporting matters | n/a — confirmed by human | `sprk_precedent` entity + projected to `[PROPOSED: insights-index]` |
| **Inference** | "Predicted cost for this new IP licensing matter: ~$310K (confidence 0.74), based on 12 comparable matters" | Synthesis playbook combining Live Facts + Observations + Precedents | 0.0–1.0 | Returned in response; cached, not authoritatively persisted |

All four use the same `InsightArtifact` envelope when returned through the API. Surfaces render them through the Insight Card visual primitive, varying only the rendering details per tier (Facts state directly, Observations and Inferences show confidence and evidence).

---

## 2. What a Precedent actually is

This concept needs to be concrete because the data model and downstream synthesis depend on it. Here is what an SME sees when reviewing or authoring a Precedent (mocked as it would appear in Phase 1.5+ when the review queue UI ships; the data model behind it ships in Phase 1):

---

**Pattern**: IP licensing matters with BigFirm LLP

**Status**: Tentative — awaiting review *(in Phase 1 manual mode, Tentative status is set programmatically when an SME initially saves; Confirmed when they review and finalize)*

**Pattern statement** *(editable)*:
> *"In IP licensing matters where BigFirm LLP represents the counterparty, cure-period clauses survived final negotiation in 12 of 14 matters reviewed (86%). Settlement amounts in these matters ranged from $185K to $520K, with a median of $310K. Average matter duration from filing to closure: 8.4 months."*

**Supporting matters** *(14 — each click-through to matter record; each row links to the extraction source document in `spaarke-files-index`)*:

| Matter | Outcome | Cure clause | Settlement | Duration | Closed |
|---|---|---|---|---|---|
| M-2024-0341 | Favorable | Survived | $310,000 | 7 mo | 2024-08 |
| M-2024-0188 | Favorable | Survived | $245,000 | 9 mo | 2024-05 |
| ... | ... | ... | ... | ... | ... |
| M-2022-0211 | Unfavorable | Removed | n/a | 6 mo | 2022-09 |

**Cluster metrics** *(when Phase 1.5+ proposal automation produces this; manual mode in Phase 1 records the SME's basis directly)*:
- Cluster definition: `matterType = "IP licensing" AND opposingCounsel = "BigFirm LLP"`
- Sample size: 14
- Pattern consistency: 86% (12 of 14)
- Date range: 2022-01 to 2024-08
- Produced by: `closure-pattern-detection@v1`

**Reviewer actions**: Confirm pattern as written / Edit and confirm / Reject / Request more data

**Reviewer fields**: Pattern title (editable), Applies to (scope qualifiers), Reviewer notes (free text)

---

### 2.1 What this tells us about the data model

A Precedent has: a human-readable statement, a supporting cluster (the matters that ground it), a status (Tentative / Confirmed / UnderDriftReview), an effectiveness score (Phase 1.5+, automated from usage), reviewer attribution, and a vector embedding of the pattern statement for similarity retrieval.

A Precedent is **distinguished from**:
- An *Observation*, which is a single fact extracted from a single document about a single matter. A Precedent is named pattern across many Observations.
- A *raw cluster query*, which can compute the same statistics on demand. A Precedent codifies that "an attorney looked at this cluster and decided it's worth treating as institutional knowledge." The validated pattern statement is the artifact a synthesis playbook can cite by name.

### 2.2 Who writes a Precedent

Two modes, with Phase 1 supporting only the first:

**Phase 1 mode — SME manual authoring.** An attorney recognizes a pattern from matters they've worked on or reviewed. They open the admin endpoint or a simple admin form, type the pattern statement themselves, reference the supporting matters by ID, and save. The system stores, indexes, and makes the Precedent retrievable. The system contributes nothing to authoring the statement at this stage. Volume is small (a handful of Precedents in early adoption, possibly growing to dozens over a year).

**Phase 1.5+ mode — system proposes, SME refines.** Once enough Observations have accumulated, a nightly job runs configured cluster queries against the Observations data. A configured cluster category (maintained by admins) specifies grouping dimensions and thresholds — e.g., *"group Observations by (matterType, opposingCounsel) where n ≥ 12 and outcome-consistency ≥ 70%"*. For each qualifying cluster, the deterministic statistics are computed in code, and an LLM is invoked with a templated prompt that *narrates* those statistics into a one-paragraph pattern statement. The result becomes a Tentative Precedent in the review queue. The LLM never invents numbers — it only narrates ones the cluster query produced.

The data model in Phase 1 supports both modes. Only the manual mode is wired in Phase 1; the cluster-category configuration entity, the nightly job, and the review queue UI are explicitly Phase 1.5+.

### 2.3 Where Precedents live

**System of record**: `sprk_precedent` entity in Dataverse. Curation, lifecycle management, security roles, audit, and admin views all happen in Dataverse — this benefits from existing Spaarke patterns (model-driven views, business process flows, the customer's existing admin experience).

**Read-optimized projection**: `[PROPOSED: insights-index]` (one index, holds both Observations and Precedents — see §4). When a Precedent's status changes to Confirmed, a small sync job projects it to the index for retrieval by synthesis playbooks. Same pattern as `DataverseIndexSyncService` already uses for matter records.

The synthesis playbook (e.g., `predict-matter-cost`) queries `[PROPOSED: insights-index]` for applicable Precedents, not Dataverse directly — vector similarity over pattern statements is the access path, not Dataverse filtering.

---

## 3. Universal document ingest with layered extraction

The Phase 1 model for producing Observations from documents is **universal ingest with layered extraction**. Every document uploaded to SPE flows through one ingest playbook. The playbook is the same for every document — no type-based forks at the trigger level. The existing SPE-upload event is the only trigger.

### 3.1 The layered model

The ingest playbook is composed of layers. Each layer attempts to extract a specific type of signal. A layer either produces one or more Observations or produces nothing. Most layers produce nothing for most documents, and that's correct behavior.

| Layer order | Layer | What it tries to extract | What it produces if successful | What it produces if no signal |
|---|---|---|---|---|
| 1 | Document classification | What kind of document is this | Classification Observation with confidence | Classification = "other", later layers gate themselves off |
| 2 | Outcome extraction | If document is outcome-bearing — extract outcome category, settlement amount, duration | One Observation per extracted field, each with verbatim evidence quote | Nothing |
| 3+ | Future layers (Phase 1.5+) | Entity extraction, deal-terms extraction, decision extraction, risk extraction | Per-layer Observations | Nothing |

**Economics**: cheap layers gate expensive ones. Layer 1 is a single LLM call returning a typed enum — cheap. Layer 2 only fires when Layer 1 classifies the document as outcome-bearing (closing letter, settlement agreement, opinion/judgment) with confidence ≥ 0.7. A correspondence email gets classified, Layer 2 sees the classification, returns nothing, no expensive extraction call fires. This is what makes layered extraction economical on universal ingest.

### 3.2 Phase 1 ships two layers

Phase 1 implements Layer 1 (document classification) and Layer 2 (outcome extraction). Additional layers (entity, deal terms, decision, risk) are Phase 1.5+ and land per priority — entity extraction next (broad value, applies to most document types), then deal terms for transactional matters.

### 3.3 Layer 1 — Document classification — prompt specification

This prompt is the starting template for Phase 1. It will iterate based on calibration data from the Observation review surface (§5).

```
You are classifying a legal document by type. Return one classification
based on the document content:

- closing_letter: A letter or memo summarizing the outcome of a closed
  matter, typically authored at matter closure, often citing final cost,
  outcome, and key terms.
- settlement_agreement: A binding agreement settling a dispute, containing
  settlement amounts, terms, and conditions.
- decision_memo: An internal memo analyzing a legal decision or strategy,
  containing decisions rendered, rationale, alternatives.
- deal_document: A transactional document (contract, term sheet, LOI)
  containing parties, deal terms, and structure.
- pleading: A court filing (complaint, answer, motion, brief).
- opinion_judgment: A court opinion, ruling, or judgment.
- correspondence: General correspondence (email, letter) not falling into
  the above categories.
- other: Document type not in this list.

Return JSON only:
{
  "classification": "<one of the above>",
  "confidence": 0.0–1.0,
  "reasoning": "<one sentence>"
}
```

**Output of Layer 1**: one Observation of type `documentClassification` per document, written to `[PROPOSED: insights-index]`. Subject = the document; predicate = `classification`; value = the typed enum; confidence = as returned; evidence = pointer to the document.

**Gating downstream**: Layer 2 runs if and only if Layer 1 returned `closing_letter`, `settlement_agreement`, or `opinion_judgment` AND confidence ≥ 0.7. Otherwise Layer 2 returns nothing and the document contributes nothing further to the insights-index for outcome content (other layers may apply in Phase 1.5+).

### 3.4 Layer 2 — Outcome extraction — prompt specification

Starting template for Phase 1:

```
You are extracting structured outcome data from a legal document. For
each field, return the extracted value, the verbatim quote from the
document that supports the extraction, and your confidence (0.0–1.0).

If a field is not present in the document or unclear, return null with
confidence 0 and a brief explanation.

Schema:
{
  "outcomeCategory": "favorable_to_client | unfavorable_to_client | neutral | mixed | unclear",
  "settlementAmount": <USD numeric or null>,
  "settlementCurrency": "<ISO currency code, default USD>",
  "outcomeDate": "<ISO date or null>",
  "matterDurationDays": <integer or null>,
  "keyTerms": [
    {"term": "<short label>", "description": "<one sentence>"}
  ],
  "evidence": {
    "outcomeCategory": "<verbatim quote from document>",
    "settlementAmount": "<verbatim quote>",
    "outcomeDate": "<verbatim quote>",
    "matterDurationDays": "<verbatim quote>"
  },
  "confidence": {
    "outcomeCategory": 0.0–1.0,
    "settlementAmount": 0.0–1.0,
    "outcomeDate": 0.0–1.0,
    "matterDurationDays": 0.0–1.0
  }
}

Return JSON only.
```

**Post-processing — three mechanical gates before any Observation persists**:

1. **Grounding verification**. Each evidence quote is mechanically checked (substring + sliding-window match) against the source document chunks in `spaarke-files-index`. Quotes that don't appear in the source are rejected; the corresponding extracted field is dropped or annotated `[citation could not be verified]`. This is the `GroundingVerifier` primitive (per existing D-A22 / D-47).
2. **Confidence threshold gating**. Per-field thresholds (Phase 1 starting values, refined with calibration data from the review surface): `outcomeCategory ≥ 0.75`, `settlementAmount ≥ 0.85`, `outcomeDate ≥ 0.85`, `matterDurationDays ≥ 0.75`. Fields below threshold are not persisted; they may be logged for review.
3. **Per-field Observation emission**. Each surviving field becomes its own Observation in `[PROPOSED: insights-index]`. Subject = the matter the document belongs to; predicate = the field name; value = the extracted typed value; evidence = the verbatim quote + document reference; producedBy = `outcome-extraction@v1`.

### 3.5 Prompt versioning and re-extraction

Prompt templates are versioned artifacts. The version string (`outcome-extraction@v1`) propagates to `producedBy.version` on every produced Observation. When a prompt improves (`v1 → v2`), a targeted re-extraction job can re-run the playbook against documents whose Observations are still at `v1`. This is `producedBy.version`-driven re-extraction per existing decisions.md D-05.

### 3.6 Backfill

Backfill of historical documents (those uploaded to SPE before the Engine existed) is **admin-triggered only** and out of automatic scope for Phase 1. A small admin endpoint can kick off backfill against a date range or matter scope; volume estimation and cost projection are the admin's responsibility. No automated backfill in Phase 1.

---

## 4. One new index — the derived intelligence layer

### 4.1 The objective-defines-the-index principle

`spaarke-files-index` is optimized for *content retrieval* — chunked source text vectorized to support "find documents about X". Different chunk sizes, surrounding context, embedding model tuned for content similarity.

`[PROPOSED: insights-index]` is optimized for *derived intelligence retrieval* — short structured claim statements (an Observation or Precedent pattern statement, typically 1–3 sentences) vectorized to support "find Observations/Precedents matching pattern Y" with structured field filtering.

These are different retrieval objectives and the design correctly separates them into different indexes. `spaarke-files-index` is consumed as-is by this project; `[PROPOSED: insights-index]` is the one new index Phase 1 ships.

### 4.2 Index contents

One index, two artifact types differentiated by a discriminator field:

```
artifactType: "observation" | "precedent"
subject: "matter:M-1234" | "document:abc" | "party:acme" 
predicate: <string> (e.g., "outcomeCategory", "settlementAmount", "classification", or "pattern" for precedents)
value: <JSON> (typed value or pattern metadata)
confidence: 0.0–1.0 (Observations only; Precedents are SME-confirmed)
evidence: <JSON array of refs to source documents, chunks, or supporting matters>
producedBy: <string> (e.g., "outcome-extraction@v1" or "manual-sme-author")
asOf: <ISO datetime>
content: <string — the searchable text content; for Observations this is the claim and quote, for Precedents this is the pattern statement>
contentVector: <embedding of content for similarity retrieval>
status: <Observation lifecycle: produced | reviewed | superseded; Precedent lifecycle: tentative | confirmed | underDriftReview>
```

### 4.3 The SPE upload plumbing fork

When a document is uploaded to SPE, today it flows through the existing pipeline into `spaarke-files-index` (chunked content). Going forward, the same event also triggers the new ingest playbook (§3) which produces Observations into `[PROPOSED: insights-index]`.

Two consumers subscribe to the SPE-upload event:
- **Existing consumer** — chunks content for `spaarke-files-index`. No change to current behavior.
- **New consumer** — runs the ingest playbook (Layer 1 classification, optionally Layer 2 outcome extraction). Writes Observations to `[PROPOSED: insights-index]`. Reads document content from `spaarke-files-index` (already chunked) rather than re-fetching from SPE.

The new consumer is a new BackgroundService or Function per ADR-001. It follows existing patterns (same event source, same dispatch shape, same authorization model). The architectural addition is real but bounded.

### 4.4 Precedent projection

When a `sprk_precedent` row's status is set to Confirmed (Phase 1: by an SME via admin endpoint; Phase 1.5+: by the SME reviewing a system-proposed Tentative), a small sync job projects the Precedent to `[PROPOSED: insights-index]` with `artifactType = "precedent"`. This is the read-optimized projection the synthesis playbook queries.

---

## 5. The Observation review surface

Phase 1 ships an **Observation review surface** for QA-sampling produced Observations. Without it, extraction accuracy is unverified and the honesty contract loses its grounding.

**What the reviewer sees**: a list of recently-produced Observations, filterable by `producedBy` (so reviewers can focus on Layer 2 outputs), ordered by recency. Each row shows the source document (click-through to view in SPE), the extracted field name, the extracted value, the verbatim supporting quote, and the confidence. Reviewer marks the Observation as:

- **Correct** — extracted value matches the document
- **Incorrect** — extracted value does not match (with a free-text note explaining what went wrong)
- **Unclear** — document is ambiguous; reviewer cannot confidently judge

**Volume**: Phase 1 starting policy is sample ~10% of produced Observations for the first 4–6 weeks (calibration), then 1–2% ongoing (drift detection). The percentages are admin-tunable.

**Feedback loop**: review dispositions feed prompt iteration. A high incorrect-rate on a specific field signals the prompt needs adjustment; consistent failures on a specific document type signal Layer 1 classification is mis-firing. Both are inputs to `outcome-extraction@v2` and `classification@v2` over time.

**Where the UI lives**: a Dataverse model-driven view is sufficient for Phase 1. The Observations are persisted in `[PROPOSED: insights-index]` but mirrored to a `sprk_analysis` row (or new `sprk_observation` row — depends on the polymorphic pattern decision from §6 open question 4 in the previous addendum) so Dataverse model-driven views can display them. The mirror writes one Dataverse row per Observation as a side-effect of the ingest playbook.

This is a small but genuinely new Phase 1 deliverable that I missed in the previous addendum.

---

## 6. The synthesis path — `predict-matter-cost`

Phase 1 ships one synthesis question end-to-end: `predict-matter-cost`. Concrete behavior:

**Request**: `POST /api/insights/ask` with `{question: "predict-matter-cost", subject: "matter:M-1234"}`.

**Playbook execution** (high-level steps; each step is a node in the playbook):
1. Resolve matter scope (matter type, jurisdiction, deal size bucket, opposing counsel)
2. Live Fact lookup: current spend, current duration, matter status
3. Cohort retrieval: find Observations of `outcomeCategory` and `settlementAmount` from comparable matters (matching matter type, deal size bucket, optionally jurisdiction)
4. Precedent retrieval: find Confirmed Precedents whose pattern applies (matching matter type, opposing counsel)
5. Evidence sufficiency check: `comparableMatters ≥ 12`. If not, route to decline path.
6. Synthesis: combine cohort data + applicable Precedents + Live Facts into a cost prediction with confidence
7. Grounding verification: mechanically verify citations to comparable matters and Precedents
8. Return InsightArtifact envelope

**Response on success**: an Inference InsightArtifact with predicted value, confidence, evidence refs (12+ comparable matter IDs plus any cited Precedents), and reasoning summary.

**Response on insufficient evidence**: structured `DeclineResponse` per existing D-A24 / D-49 — *"need ~N more comparable matters; try widening practice area or removing jurisdiction constraint"*.

The new node executor types needed for this playbook (per existing addendum §3.3): `LiveFactNode`, `IndexRetrieveNode` (queries `[PROPOSED: insights-index]`), `EvidenceSufficiencyNode`, `DeclineToFindNode`, `GroundingVerifyNode`, `ReturnInsightArtifactNode`. All are Phase 1 deliverables.

---

## 7. Phase 1 deliverable list (corrected)

This list supersedes the deliverable summary in `SPEC-refinement-addendum.md` §6.

| ID | Deliverable | Layer |
|---|---|---|
| D-P1 | InsightArtifact envelope POCOs (Fact / Observation / Precedent / Inference) | Domain types |
| D-P2 | `[PROPOSED: insights-index]` schema + provisioning via Bicep (one index, holds Observations and Precedents with discriminator) | Infra |
| D-P3 | `sprk_precedent` Dataverse entity + admin endpoint `POST /api/insights/admin/precedents` for manual SME authoring | Entity + API |
| D-P4 | Precedent → `[PROPOSED: insights-index]` projection sync (small job, fires on Precedent status → Confirmed) | Substrate |
| D-P5 | Layer 1 — document classification — playbook node + prompt template @v1 | Extraction pipeline |
| D-P6 | Layer 2 — outcome extraction — playbook node + prompt template @v1 | Extraction pipeline |
| D-P7 | Universal ingest playbook (orchestrates layers 1 and 2; runs on every SPE upload via new consumer) | Extraction pipeline |
| D-P8 | New consumer on SPE-upload events that triggers the ingest playbook (extends existing SPE-upload plumbing) | Infra |
| D-P9 | `GroundingVerifier` mechanical citation check primitive (shared platform primitive; also used by Action Engine) | Platform primitive |
| D-P10 | Confidence threshold gating and per-field Observation emission post-processing | Extraction pipeline |
| D-P11 | Observation review surface — Dataverse model-driven view with disposition workflow (sample-based QA) | UI / Operations |
| D-P12 | New Insights-mode node executor types (`LiveFactNode`, `IndexRetrieveNode`, `EvidenceSufficiencyNode`, `DeclineToFindNode`, `GroundingVerifyNode`, `ReturnInsightArtifactNode`) | Playbook substrate |
| D-P13 | Insights playbook execution caching (Redis layer per existing D-A32, ADR-009) | Platform |
| D-P14 | `predict-matter-cost` synthesis playbook + evidence-sufficiency rule + insufficient-evidence response template | Question |
| D-P15 | API endpoint `POST /api/insights/ask` with `IInsightsAi` facade per existing §3.5 boundary | API |
| D-P16 | Smoke test + small golden dataset + eval harness baseline | Tests / Evaluation |

That's 16 deliverables — fewer than the original SPEC's 27 but materially more substantive than the previous addendum's "minimum" because it ships real Observation production rather than infrastructure-with-mock-data.

The `IInsightsAi` facade boundary per existing addendum §3.5 still applies — D-P15 must route AI consumption through the facade; D-P14 (the synthesis playbook agent invocation) lives in Zone A. Unchanged.

---

## 8. Phase 1.5 and Phase 2+ (explicitly deferred)

**Phase 1.5** (the next phase after Phase 1 ships):
- Additional extraction layers — entity extraction, deal-terms extraction, decision extraction, risk extraction
- Configured cluster category entity (admin-managed list of pattern detection rules)
- Nightly cluster summarization job (system-proposed Tentative Precedents)
- Precedent review queue UI (the mocked screen from §2)
- Precedent lifecycle automation (decay, drift detection, effectiveness scoring)
- Additional synthesis questions (predict-matter-duration, find-comparable-matters, counterparty-history, budget-overrun-risk)

**Phase 2+**:
- Insight snapshot persistence (per existing addendum §4.4) — when surfaces have save/pin/attach needs
- Catalog index for natural-language routing (per existing addendum §4.2) — when Assistant pane lands
- Cosmos graph (per existing addendum) — when traversal needs exceed Dataverse join capabilities
- Document content extraction archetypes beyond outcome (analytical extraction per existing addendum §2.5)
- Automated backfill management

**Explicitly out of scope**:
- Multi-tenant federation
- Cross-tenant Precedent sharing

---

## 9. What this document supersedes

| Earlier content | Status |
|---|---|
| `SPEC-refinement-addendum.md` §1 (five `insight-*` indexes) | **Superseded**: one new index (`[PROPOSED: insights-index]`) holds Observations and Precedents with a discriminator |
| `SPEC-refinement-addendum.md` §1 (dual-substrate framing) | **Refined**: existing operational substrate (`spaarke-files-index`, `spaarke-records-index`, `spaarke-invoices-index`, `spaarke-rag-references`) unchanged; one new derived-intelligence index added |
| `SPEC-refinement-addendum.md` §2 (Signal Contract document) | **Superseded**: the Signal Contract document is no longer needed in Phase 1 because Mode A "narrow derived projection" is replaced by Live Fact computation on read; no projection writing happens in Phase 1 except via the ingest playbook |
| `SPEC-refinement-addendum.md` §2.5 (Mode C — document content extraction as design-only) | **Superseded**: document extraction ships in Phase 1 as the universal ingest playbook with two layers |
| `SPEC-refinement-addendum.md` §4.2 (catalog index for routing) | **Deferred to Phase 2+** (no Assistant pane consumer in Phase 1) |
| `SPEC-refinement-addendum.md` §4.4 (snapshot persistence) | **Deferred to Phase 2+** (no save/pin/attach surfaces in Phase 1) |
| `SPEC-refinement-addendum.md` §6 deliverable list | **Superseded by §7 of this document** |
| Original SPEC.md D-A12 (closure-extraction design doc) | **Superseded**: §3 of this document IS the design; D-A12 is closed |
| Original SPEC.md D-A6 (Cosmos NoSQL graph) | **Deferred to Phase 2+**: Phase 1 has no graph traversal needs |

Content from `SPEC-refinement-addendum.md` that **stands unchanged**:
- §0 (multi-tenant out of Phase 1)
- §3 (questions as playbooks — the playbook substrate framing)
- §3.3 Adjustment A (new node executor types) — itemized in D-P12
- §3.3 Adjustment B (playbook metadata extension) — still needed
- §3.3 Adjustment C (execution caching) — D-P13
- The §3.5 Zone A / Zone B facade boundary — binding for all AI consumption

---

## 10. Open questions for Spaarke team

These need human judgment before Claude Code begins:

1. **Observation mirror to Dataverse**: §5 proposes mirroring each Observation to a Dataverse row for the review surface. Confirm whether this lands on `sprk_analysis` (existing polymorphic AI output entity, per userMemories standing pattern) or whether a new `sprk_observation` entity is warranted. The polymorphic-on-`sprk_analysis` answer is consistent with prior decisions; confirm before D-P11.
2. **`[PROPOSED: insights-index]` final naming**: confirm name. Suggestions: `insights-index`, `spaarke-insights`, or align to existing `spaarke-*-index` naming convention.
3. **Layer 1 starter prompt** (§3.3): the document type taxonomy should be reviewed by an SME before being committed as the v1 prompt. Adding, removing, or re-defining categories now is much cheaper than after Observations have been produced under v1.
4. **Layer 2 confidence threshold values** (§3.4): the starting values (`outcomeCategory ≥ 0.75`, `settlementAmount ≥ 0.85`, etc.) are reasonable defaults but should be confirmed with the product/SME team. Too-strict thresholds reject useful Observations; too-lenient produce noise.
5. **Observation review sampling percentage** (§5): 10% initial / 1–2% ongoing is a reasonable starting policy but should be confirmed against reviewer capacity. Higher sampling means slower drift detection at the cost of reviewer time.
6. **Phase 1 Precedent seeding**: how many SME-authored Precedents are realistic for Phase 1 launch? Zero is acceptable (the platform works without Precedents — they're an enrichment to Inferences, not a requirement). One or two is sufficient for smoke-test validation. Active SME engagement could produce more.

Claude Code should surface these as questions in its first response rather than proceeding to implementation.

---

## 11. Cross-references

- `SPEC.md` — primary spec being refined
- `design.md` §2 (taxonomy), §5 (synthesis), §6 (data flow), §8 (surface integration)
- `decisions.md` D-01 through D-51 — existing decisions preserved unless explicitly superseded
- `SPEC-refinement-addendum.md` — earlier refinement, partially superseded per §9
- ai-inventory.md §1 (AI Search inventory), §8 (playbook execution), §9 (tool framework)
- ADR-001 (minimal API + workers), ADR-008 (endpoint filter auth), ADR-009 (Redis caching), ADR-010 (DI minimalism), ADR-013 (AI architecture), ADR-015 (AI data governance), ADR-019 (ProblemDetails)
- Companion: Action Engine refinement addendum (`projects/ai-spaarke-action-engine-r1/action-engine-refinement-addendum.md`) — shared platform primitive `GroundingVerifier` (D-P9) is consumed by Action Engine R2

---

## 12. What this document does NOT change

For clarity, the following remain unchanged from earlier documents:

- The four-tier taxonomy (Fact / Observation / Precedent / Inference) per design.md §2 and D-03, D-46
- The `InsightArtifact` envelope shape per design.md §2.2
- The honesty contract and structured `insufficient_evidence` response per D-06 and D-49
- The §3.5 Zone A / Zone B facade boundary
- The Insights-questions-are-playbooks architecture per `SPEC-refinement-addendum.md` §3
- Single-tenant Phase 1 scoping per `SPEC-refinement-addendum.md` §0
- The shared `GroundingVerifier` primitive used by both Insights and Action Engine
- All ADR compliance requirements
