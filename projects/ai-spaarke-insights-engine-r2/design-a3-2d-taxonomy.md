# Design A3 — 2D Classification Taxonomy

> **Wave**: A3 (Foundations)
> **Authored**: 2026-06-02
> **Author**: task-execute / task 012
> **Task**: [`tasks/012-2d-taxonomy-design.poml`](tasks/012-2d-taxonomy-design.poml)
> **Status**: Authored — initial 3 practice areas (CTRNS, IPPAT, BNKF) pending owner confirmation (PA-2 in `spec.md`)
> **Downstream consumers**: Wave D1 (task 030 schema), D2 (031 Layer 1 prompts), D3 (032 Layer 2 schemas)
> **References**: `design.md` D-P15-04 (2D taxonomy), D-P15-09 (per-area gate signals), Q-D2-1 (initial-3 selection criteria), `spec.md` PA-1 (table-as-source-of-truth), PR-1 (no new prompt entity)

---

## 0. Why this design exists

Phase 1's classification was litigation-biased (`outcomeBearing` flag) and single-dimensional (document classes only). Phase 1.5 generalises classification along **two dimensions**:

1. **Practice area** (`sprk_practicearea_ref` — existing) — the legal practice context the matter sits in
2. **Document type** (`sprk_documenttype_ref` — NEW Phase 1.5) — what KIND of document this is, scoped per practice area

The **N:N matrix** `sprk_practicearea_documenttype` carries which document types are valid for which practice area. The matrix is the routing table: Layer 1 classification (Wave D2 / task 031) emits a `document_type` aligned to the matter's practice area; Layer 2 extraction (Wave D3 / task 032) dispatches against `(practice_area, document_type)` pairs.

**Non-goals (out of scope for this design)**:
- The variant + versioning + per-tenant override mechanism — owned by Wave A4 / task 013 within `sprk_analysisaction.sprk_systemprompt`.
- The actual Layer 1 / Layer 2 prompt content — Wave D2 / D3 (031, 032).
- The universal-ingest routing logic that reads `sprk_matter.sprk_practicearea` and routes — Wave C1 / D4 (020, 033).

---

## 1. `sprk_documenttype_ref` entity — field-level design

### 1.1 Schema decisions

| Decision | Choice | Rationale |
|---|---|---|
| Entity type | Reference data (lookup-target) | Matches `sprk_practicearea_ref` / `sprk_mattertype_ref` pattern; small slow-changing dimension |
| Primary key | `sprk_documenttype_refid` (auto Guid) | Standard Dataverse convention; consistent with `_ref` family |
| Code column convention | `sprk_documenttypecode` (Text, 50) | Mirrors `sprk_practiceareacode` / `sprk_mattertypecode` exactly |
| Name column convention | `sprk_documenttypename` (Text, 200) | Mirrors `sprk_practiceareaname` |
| Description column | `sprk_documenttypedescription` (Multiline Text, 2000) | SME-authored disambiguation text; NEW (the existing `_ref` entities lack this) |
| Active flag | Use built-in `statecode`/`statuscode` | Existing `sprk_practicearea_ref` rows already follow this pattern; do NOT add a custom `sprk_active` field |
| Ownership-by-practice-area | **N:N via matrix, NOT FK** | A document type (e.g. "engagement-letter") can be valid for multiple practice areas; FK would force single-owner falsely |
| Sort order | `sprk_sortorder` (Whole Number) | Optional, for UX dropdown ordering; default `null` |
| Layer-2-schema reference | **NOT a column on this entity** | Layer 2 schema lives in `sprk_analysisaction` rows per PR-1; reverse-lookup by code suffix per A4 design |

### 1.2 Field summary table

| Logical Name | Display Name | Type | Constraints / Notes |
|---|---|---|---|
| `sprk_documenttype_refid` | Document Type | Uniqueidentifier | PK; auto |
| `sprk_documenttypecode` | Document Type Code | Text(50) | Required; unique alternate key; UPPER_SNAKE convention (e.g. `CLOSING_STATEMENT`) |
| `sprk_documenttypename` | Document Type Name | Text(200) | Required; human-readable (e.g. "Closing Statement") |
| `sprk_documenttypedescription` | Description | Multiline Text(2000) | Optional; SME-authored disambiguation |
| `sprk_sortorder` | Sort Order | Whole Number | Optional; for UX |
| `statecode` / `statuscode` | State / Status | (built-in) | Standard Active/Inactive; never deleted, only deactivated |

### 1.3 Code naming convention (per practice area)

**Convention**: `UPPER_SNAKE_CASE`, **practice-area-prefixed**, semantically descriptive.

- Format: `<PRACTICE_AREA_CODE>_<DOCUMENT_TYPE>` — e.g. `CTRNS_CLOSING_STATEMENT`, `IPPAT_PATENT_APPLICATION`, `BNKF_LOAN_AGREEMENT`
- **Rationale**: The same logical doc type (e.g. "term sheet") can have different field schemas in CTRNS vs BNKF; prefixing makes routing unambiguous and prevents accidental N:N reuse where the semantics differ.
- **Pan-area doc types** (rare; e.g. engagement letters, conflict waivers) get a special `GEN_` prefix: `GEN_ENGAGEMENT_LETTER`. These are linked to multiple practice areas via the N:N matrix.

The N:N matrix (§2) still allows the same `sprk_documenttype_ref` row to be linked to multiple practice areas, but the prefixed code makes the canonical context explicit.

---

## 2. `sprk_practicearea_documenttype` N:N matrix — field-level design

### 2.1 Relationship shape

| Decision | Choice | Rationale |
|---|---|---|
| Relationship type | **Manual intersect entity** (NOT auto-generated N:N) | Need additional columns (`is_default_layer2_schema_ref`, `sort_order`) per-pair, which Dataverse's auto N:N intersect doesn't support |
| Intersect entity name | `sprk_practicearea_documenttype` | Convention: `<entityA>_<entityB>`, sorted alphabetically per `_ref` precedent |
| Schema name display | "Practice Area / Document Type" | Used in form labels |

### 2.2 Field summary table

| Logical Name | Display Name | Type | Constraints / Notes |
|---|---|---|---|
| `sprk_practicearea_documenttypeid` | Matrix Row ID | Uniqueidentifier | PK; auto |
| `sprk_practicearea` | Practice Area | Lookup → `sprk_practicearea_ref` | Required; one half of the N:N |
| `sprk_documenttype` | Document Type | Lookup → `sprk_documenttype_ref` | Required; other half |
| `sprk_layer2actioncode` | Layer 2 Action Code | Text(100) | Optional; refers to the `sprk_analysisaction.sprk_actioncode` that handles Layer 2 for this pair (e.g. `INSIGHTS.LAYER2_EXTRACT.CTRNS.CLOSING_STATEMENT`). NULL = no Layer 2 (gate-fails for this pair) |
| `sprk_layer2required` | Layer 2 Required | Two Options (bool) | Default `false`; when `true`, the universal-ingest gate forces Layer 2 even at lower Layer 1 confidence |
| `sprk_gatesignal` | Gate Signal | Text(100) | Optional; the Layer 1 signal name (e.g. `is_closing_statement`) that activates this row's Layer 2 — see §5 |
| `sprk_sortorder` | Sort Order | Whole Number | Optional UX ordering |
| `sprk_notes` | Notes | Multiline Text(2000) | Optional SME context |
| `statecode` / `statuscode` | State / Status | (built-in) | Standard Active/Inactive |

### 2.3 Alternate keys + uniqueness

- **Composite uniqueness**: `(sprk_practicearea, sprk_documenttype)` must be unique — one row per (area × type) pair. Enforced via an alternate key on `sprk_practicearea_documenttype`.

### 2.4 Ownership semantics

- **Matrix rows are reference data**, not transactional — owned by the **prompt-curation team** (the same SMEs who own `sprk_analysisaction` rows per spec PR-1).
- Adding a row = "this pair is valid; here's its Layer 2 action."
- Deactivating a row (statecode = Inactive) = "we no longer extract this pair, but historical Observations remain."
- **Never delete** rows — historical Observations may reference them.

### 2.5 Routing semantics (consumed by Wave C1 / D4)

The universal-ingest playbook (per Wave A5 / task 014 design) routes Layer 2 dispatch as follows:

1. Layer 1 emits a `document_type_code` (e.g. `CTRNS_CLOSING_STATEMENT`).
2. Universal-ingest looks up the matrix row where `(matter.sprk_practicearea, document_type_code)` matches.
3. If found AND `sprk_layer2actioncode IS NOT NULL`: dispatch to that action.
4. If found AND `sprk_layer2actioncode IS NULL`: gate-fail — no Layer 2; emit observation with Layer 1 signal only.
5. If NOT found: gate-fail — unknown pair for this practice area; emit observation with `signal = unknown_pair_for_practice_area`.

Step 5 is the safety net: if Layer 1 emits a doc type that's NOT registered for the matter's practice area, we still log an Observation rather than crash; this is the canonical "this matter has unexpected documents" signal.

---

## 3. Initial 3 practice areas (per Q-D2-1)

### 3.1 Live query of `sprk_practicearea_ref` (Spaarke Dev, 2026-06-02)

Per PA-1 (spec.md), practice areas are sourced from `sprk_practicearea_ref` — the table IS the source of truth. Live query result:

| Code | Name | State | Notes |
|---|---|---|---|
| `APPL` | Appellate | Active | Briefs, motion practice, opinions |
| `BNKF` | Banking & Finance | Active | Loan docs, security agreements, payoff letters |
| `CTRNS` | Commercial Transactions | Active | M&A closing docs, asset purchase, financing agreements |
| `IPPAT` | Intellectual Property Patents | Active | Patent applications, office actions, issued patents |
| `IPTM` | Intellectual Property Trademarks | Active | Trademark applications, office actions, registrations |
| `MA` | Mergers & Acquisitions | Active | (Overlap with CTRNS; M&A-specific docs — deal sheet, NDA, LoI) |

**All 6 rows are active in Spaarke Dev as of 2026-06-02.**

### 3.2 Selection: CTRNS, IPPAT, BNKF (pending owner confirmation as PA-2)

Per Q-D2-1 selection criteria (SME readiness, document variety, strategic priority):

| Practice Area | SME Readiness | Document Variety | Strategic Priority | Score | Selected? |
|---|---|---|---|---|---|
| **CTRNS** Commercial Transactions | High — existing matter fixtures + r1 seed data is CTRNS-equivalent | High — closing docs, asset purchase, financing, services agreements, NDAs | High — broadest applicability; many tenants do transactional work | ⭐⭐⭐ | ✅ |
| **IPPAT** IP Patents | Medium — Spaarke has IP-focused tenants in pipeline | High — patent applications, office actions, issued patents, prior-art submissions | High — IP-heavy tenants are a beachhead segment | ⭐⭐⭐ | ✅ |
| **BNKF** Banking & Finance | Medium — owner-directed strategic priority | High — loan agreements, security agreements, intercreditor, payoff, term sheets | High — finance vertical critical for next tenant wave | ⭐⭐⭐ | ✅ |
| MA Mergers & Acquisitions | High — overlap w/ CTRNS — deferred | Medium — overlaps CTRNS for ~70% of docs | Medium — defer; CTRNS coverage subsumes most | ⭐⭐ | ❌ Defer (CTRNS covers majority) |
| IPTM IP Trademarks | Medium — overlap w/ IPPAT (same SME team) | Medium — narrower than IPPAT | Medium — IPPAT-first then IPTM | ⭐⭐ | ❌ Defer (IPPAT covers IP-shaped patterns first) |
| APPL Appellate | Low — fewer SME-active fixtures | Low — briefs + motion practice are narrow | Medium | ⭐ | ❌ Defer (Phase 2 candidate) |

**Selection: CTRNS, IPPAT, BNKF** — three distinct legal substrates (transactional, technical, financial), high SME readiness, exercises the full breadth of the 2D taxonomy.

> **Owner sign-off pending**: Spec.md PA-2 (added by this task) carries the selection. Owner may rebalance (e.g. swap BNKF→MA if a specific tenant is the driver). The downstream work in Wave D2 / D3 is parameterised on the initial 3 — swapping is cheap (re-author 3 prompts) until D7 fixtures are seeded.

---

## 4. Initial document types per practice area (3–5 each, per spec.md Wave D3)

The codes below are the **canonical starting set**. Wave D3 (task 032) selects 3–5 high-value `(area, type)` pairs from this list for Layer 2 schema authoring; the remainder ship with Layer 1 classification only (gate-fail Layer 2 with structured "not yet schematised" signal).

### 4.1 CTRNS — Commercial Transactions

| Code | Name | Layer 1 gate signal | Layer 2 priority |
|---|---|---|---|
| `CTRNS_CLOSING_STATEMENT` | Closing Statement | `is_closing_statement` | **HIGH** (D3 candidate) |
| `CTRNS_ASSET_PURCHASE_AGREEMENT` | Asset Purchase Agreement | `is_asset_purchase` | **HIGH** (D3 candidate) |
| `CTRNS_FINANCING_AGREEMENT` | Financing Agreement | `is_financing_agreement` | Medium |
| `CTRNS_SERVICES_AGREEMENT` | Services Agreement | `is_services_agreement` | Medium |
| `CTRNS_NDA` | Non-Disclosure Agreement | `is_nda` | Low (extraction value low) |

### 4.2 IPPAT — IP Patents

| Code | Name | Layer 1 gate signal | Layer 2 priority |
|---|---|---|---|
| `IPPAT_PATENT_APPLICATION` | Patent Application | `is_patent_application` | **HIGH** (D3 candidate) |
| `IPPAT_OFFICE_ACTION` | Office Action | `is_office_action` | **HIGH** (D3 candidate) |
| `IPPAT_ISSUED_PATENT` | Issued Patent | `is_issued_patent` | Medium |
| `IPPAT_PRIOR_ART_SUBMISSION` | Prior Art Submission | `is_prior_art_submission` | Medium |
| `IPPAT_RESPONSE_TO_OFFICE_ACTION` | Response to Office Action | `is_response_to_office_action` | Medium |

### 4.3 BNKF — Banking & Finance

| Code | Name | Layer 1 gate signal | Layer 2 priority |
|---|---|---|---|
| `BNKF_LOAN_AGREEMENT` | Loan Agreement | `is_loan_agreement` | **HIGH** (D3 candidate) |
| `BNKF_SECURITY_AGREEMENT` | Security Agreement | `is_security_agreement` | Medium |
| `BNKF_PAYOFF_LETTER` | Payoff Letter | `is_payoff_letter` | Medium |
| `BNKF_INTERCREDITOR_AGREEMENT` | Intercreditor Agreement | `is_intercreditor_agreement` | Low (rare) |
| `BNKF_TERM_SHEET` | Term Sheet | `is_term_sheet` | Low (early-stage; few extractable facts) |

### 4.4 Initial Wave D3 high-value pairs (5 picks)

Per spec.md Wave D3 scope (3–5 high-value pairs), the recommendation for task 032 is:

1. `CTRNS × CTRNS_CLOSING_STATEMENT`
2. `CTRNS × CTRNS_ASSET_PURCHASE_AGREEMENT`
3. `IPPAT × IPPAT_PATENT_APPLICATION`
4. `IPPAT × IPPAT_OFFICE_ACTION`
5. `BNKF × BNKF_LOAN_AGREEMENT`

These exercise: (a) two-pair-per-area in the most fixture-rich domain (CTRNS), (b) the highest-value extraction targets per area, (c) full breadth of the initial 3 areas. Wave D3 may rebalance based on SME availability.

---

## 5. Per-(practice-area, document-type) gate signals (per D-P15-09)

### 5.1 Gate signal semantics

Phase 1's binary `outcomeBearing` flag is **retired**. Each Layer 1 prompt now emits a JSON object with **practice-area-specific boolean signals** plus a `document_type_code` matching the matrix:

```jsonc
{
  "document_type_code": "CTRNS_CLOSING_STATEMENT",
  "signals": {
    "is_closing_statement": true,
    "is_asset_purchase": false,
    "is_financing_agreement": false,
    "is_services_agreement": false,
    "is_nda": false
  },
  "confidence": 0.92,
  "rationale": "Contains 'Closing Statement' header, lists buyer/seller, contains purchase-price tabulation."
}
```

The universal-ingest gate (Wave D4 / task 033) consults the matrix row matching `(matter.sprk_practicearea, document_type_code)` and uses:
- `sprk_layer2actioncode` — which Layer 2 action to dispatch (NULL → gate-fail, structured no-op)
- `sprk_layer2required` — bypass low-confidence gate (force Layer 2)
- `sprk_gatesignal` — sanity-check that the Layer 1 signal name agrees (defensive double-check)

### 5.2 Illustrative pair examples (≥3 per acceptance criterion)

#### Example 1 — `CTRNS × CTRNS_CLOSING_STATEMENT`

```
sprk_practicearea       = CTRNS
sprk_documenttype       = CTRNS_CLOSING_STATEMENT
sprk_layer2actioncode   = INSIGHTS.LAYER2_EXTRACT.CTRNS.CLOSING_STATEMENT
sprk_layer2required     = false  (default confidence gate applies)
sprk_gatesignal         = is_closing_statement
sprk_notes              = "Extracts: closing_date, parties[], purchase_price,
                           financing_terms, contingencies[]. Highest extraction
                           value for the practice area; Wave D3 priority 1."
```

#### Example 2 — `IPPAT × IPPAT_PATENT_APPLICATION`

```
sprk_practicearea       = IPPAT
sprk_documenttype       = IPPAT_PATENT_APPLICATION
sprk_layer2actioncode   = INSIGHTS.LAYER2_EXTRACT.IPPAT.PATENT_APPLICATION
sprk_layer2required     = true   (always extract — every patent app is
                                  high-signal regardless of L1 confidence)
sprk_gatesignal         = is_patent_application
sprk_notes              = "Extracts: application_number, filing_date,
                           inventors[], claims[], priority_dates[].
                           layer2required=true because patent applications
                           are always extraction-worthy when detected."
```

#### Example 3 — `BNKF × BNKF_LOAN_AGREEMENT`

```
sprk_practicearea       = BNKF
sprk_documenttype       = BNKF_LOAN_AGREEMENT
sprk_layer2actioncode   = INSIGHTS.LAYER2_EXTRACT.BNKF.LOAN_AGREEMENT
sprk_layer2required     = false
sprk_gatesignal         = is_loan_agreement
sprk_notes              = "Extracts: borrower, lender, principal_amount,
                           interest_rate, maturity_date, security_description,
                           covenants[]. Standard confidence gate applies."
```

#### Example 4 — `IPPAT × IPPAT_OFFICE_ACTION` (additional)

```
sprk_practicearea       = IPPAT
sprk_documenttype       = IPPAT_OFFICE_ACTION
sprk_layer2actioncode   = INSIGHTS.LAYER2_EXTRACT.IPPAT.OFFICE_ACTION
sprk_layer2required     = true
sprk_gatesignal         = is_office_action
sprk_notes              = "Extracts: application_number, mailing_date,
                           response_due_date, examiner_name, rejection_types[],
                           cited_references[]. Time-sensitive — drives docket."
```

#### Example 5 — `CTRNS × CTRNS_NDA` (gate-fail case)

```
sprk_practicearea       = CTRNS
sprk_documenttype       = CTRNS_NDA
sprk_layer2actioncode   = NULL   (intentional — Layer 2 not yet authored)
sprk_layer2required     = false
sprk_gatesignal         = is_nda
sprk_notes              = "Layer 1 classification only. NDAs are low extraction
                           value (parties + effective_date are usually the
                           sum total of useful facts) — Wave D3 defers to
                           Phase 2 unless tenant demand surfaces it."
```

The NULL `sprk_layer2actioncode` is the canonical **structured gate-fail** pattern: the doc is recognized, recorded as an Observation with Layer 1 signal, but no Layer 2 extraction runs. This is Phase 1.5's replacement for `outcomeBearing = false`.

---

## 6. Inputs for downstream tasks

### 6.1 Wave D1 (task 030 — schema creation)

Hand-off to task 030:
- §1.2 — `sprk_documenttype_ref` field list (use with `dataverse-create-schema` skill)
- §2.2 — `sprk_practicearea_documenttype` field list + lookup relationship targets
- §2.3 — composite alternate key
- §4.1–§4.3 — 15 seed rows for `sprk_documenttype_ref` (5 per area)
- §5.2 — 5 matrix rows to seed (the 5 illustrative pairs)

### 6.2 Wave D2 (task 031 — Layer 1 prompts)

Hand-off to task 031:
- §3.2 — confirmed initial 3 areas: CTRNS, IPPAT, BNKF
- §4.1–§4.3 — gate signal names per area (the boolean keys in the Layer 1 output)
- §5.1 — Layer 1 output JSON shape

### 6.3 Wave D3 (task 032 — Layer 2 schemas)

Hand-off to task 032:
- §4.4 — 5 recommended `(area, type)` pairs for Layer 2 schema authoring
- §5.2 — Layer 2 action code naming convention (`INSIGHTS.LAYER2_EXTRACT.<AREA>.<TYPE>`)
- `sprk_layer2required` semantics (force-extract bypass for high-value-always pairs)

### 6.4 Wave A4 (task 013 — variant + versioning design)

Note for A4: The action-code convention used here (`INSIGHTS.LAYER1_CLASSIFY.<AREA>` / `INSIGHTS.LAYER2_EXTRACT.<AREA>.<TYPE>`) **assumes the variant-action-row pattern** (Q-A4-1 option a). If A4 picks parametric-injection instead (option b), this design's `sprk_layer2actioncode` collapses to a single value per area and the `(practice_area, document_type)` lookup happens via JPS `parameters` payload. A4's decision is back-compatible: change one column's contents, not the schema.

---

## 7. ADR / constraint compliance

| ADR / Constraint | Compliance |
|---|---|
| **ADR-027** (managed-solution path) | Wave D1 (task 030) follows managed-solution promotion. **Note**: per `memory/spaarke-unmanaged-solutions.md` (2026-06-02), Spaarke currently uses unmanaged solutions everywhere; ADR-027 was amended 2026-06-02 to reflect "future direction." Wave D1 uses Spaarke's current practice. |
| **spec.md PA-1** (no hardcoded practice areas) | §3.1 queries `sprk_practicearea_ref` live; no hardcoded list. §4.1–§4.3 doc-type lists are seed data, queried back from `sprk_documenttype_ref` after creation. |
| **spec.md PR-1** (no new prompt entity) | Layer 1 / Layer 2 schemas live on `sprk_analysisaction.sprk_systemprompt`; `sprk_layer2actioncode` column on matrix only stores the action **code reference**, not prompt content. |
| **design.md D-P15-09** (per-area gate signals) | §5.1, §5.2 — gate signals per pair; `outcomeBearing` not used. |

---

## 8. Open questions for owner

| ID | Question | Default if no answer |
|---|---|---|
| **PA-2-Q1** | Confirm initial 3 = CTRNS, IPPAT, BNKF? Or swap (e.g. BNKF → MA)? | Proceed with CTRNS, IPPAT, BNKF |
| **PA-2-Q2** | Confirm `sprk_documenttype_ref` codes use `<AREA>_<TYPE>` prefixed convention (vs. shared codes with N:N owning the pair)? | Proceed with prefixed convention |
| **PA-2-Q3** | Confirm `sprk_layer2required` is a useful gate-bypass (i.e. some pairs should always Layer-2-extract)? | Proceed; column included |
| **PA-2-Q4** | Confirm the 5 D3 priority pairs in §4.4? | Wave D3 proceeds with these; rebalance allowed |

Owner-confirmation cycle for PA-2 happens in `spec.md` Owner Clarifications table (updated by this task).

---

## 9. Acceptance criteria (mapped to task 012 POML)

| Criterion | Status | Evidence |
|---|---|---|
| design-a3-2d-taxonomy.md authored with sections (1)-(5) per goal | ✅ | This file — §1 entity, §2 matrix, §3 initial 3, §4 doc types per area, §5 gate signals |
| Initial 3 practice areas confirmed with owner + recorded in spec.md | ⚠️ | Selection (CTRNS, IPPAT, BNKF) recorded as PA-2 in spec.md; **owner sign-off pending** (§3.2 + §8) |
| Entity + matrix field-level design ready for Wave D1 (030) | ✅ | §1.2 + §2.2 + §6.1 — explicit field tables + hand-off list |
| Per-(area, type) gate signals defined for ≥3 high-value pairs | ✅ | §5.2 — 5 worked examples (CTRNS×CLOSING_STATEMENT, IPPAT×PATENT_APPLICATION, BNKF×LOAN_AGREEMENT, IPPAT×OFFICE_ACTION, CTRNS×NDA gate-fail) |

---

*End — Design A3 — 2D Classification Taxonomy.*
