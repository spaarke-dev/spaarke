# UAT Scenario — Degraded Mode (SC-14 / FR-26)

> **Task**: 064 (Wave 5, parallel with 061-063, 065, 066)
> **Date authored**: 2026-06-11
> **Rigor Level**: STANDARD (per task POML `<rigor>STANDARD</rigor>`)
> **Persona**: Tessa — Platform SRE / UAT lead
> **Targets**: **SC-14** ("Degraded-mode UAT: when `spaarke-insights-index` is empty, narrative still produced from KPI data alone") and **FR-26** ("narrative still useful in degraded mode; limitation documented in user-facing help")
> **Sub-agent boundary**: static verification + operator script. No code modifications. Deliverable lives in `notes/uat-results/` (sub-agent-write safe).
> **Environment**: Spaarke Dev BFF (`https://spaarke-bff-dev.azurewebsites.net`) + `spaarkedev1` Dataverse
> **Parent reference**: [`../uat-scenarios.md`](../uat-scenarios.md) (Task 060). This scenario is the SC-14 sibling to A/B/C — it was not split out there because SC-14 was carried as a separate POML (Task 064).

---

## 1. What "degraded mode" means in r1

**Degraded mode = `spaarke-insights-index` returns zero `observation` artifacts for the Matter under test, while the KPI assessment data and Matter Live Facts remain available.**

This is the **Phase A files-index pipeline unhealthy scenario** explicitly called out in spec.md FR-11:

> `IndexRetrieveNode` (or `LiveFactNode`) to read Observations from `spaarke-insights-index` (optional — graceful empty if files-index pipeline unhealthy)

The `matter-health-single` playbook is **architected** for this state — verification below shows it does not require code changes or feature flags to enter degraded mode; an empty index naturally produces a degraded envelope.

### Static verification: degraded mode is a non-error path

| Verified at | Source | Finding |
|---|---|---|
| `matter-health-single.playbook.json` line 103 | `"requireEvidence": false` on the `IndexRetrieve` node | Empty observations does NOT trigger an error or decline branch. |
| Same file line 104 | `$comment-requireEvidence` | "false — empty observations is acceptable per FR-11 ('optional — graceful empty if files-index pipeline unhealthy'). EvidenceSufficiencyNode gates on KPI assessment count instead." |
| Same file lines 113, 127–129 | `EvidenceSufficiencyNode` (`checkSufficiency` node) | Gates **only** on `kpiAssessments.min: 2` — does NOT consider observation count. ≥2 KPI assessments → `sufficientBranch=synthesize`. |
| Same file lines 20, 22 | Recorded evidence rule | `"kpiAssessments": { "min": 2 }` is the entire sufficiency contract. |
| Same file line 153 | `$comment-output-shape` of `synthesize` (`AiAnalysis`) | Citations array intentionally mixed — assessment refs + document refs (the latter come from observations). Empty observations → zero `document` citation items, but assessment citations remain. |
| `notes/insight-envelope-schema.md` §2 (envelope JSON Schema) | `citations[].oneOf` | `type=document` and `type=assessment` are independent variants; an empty `document` set does not violate the schema. |

**Conclusion**: degraded mode is **explicitly designed into the playbook DAG**. No conditional code path or kill-switch is involved. The architectural guarantee for SC-14 is `requireEvidence=false` on `indexRetrieveObservations` + `EvidenceSufficiencyNode` gating on KPI assessments only.

---

## 2. Expected envelope shape — degraded vs healthy

The persisted envelope in `sprk_matter.sprk_performancesummary` matches the schema in [`../insight-envelope-schema.md`](../insight-envelope-schema.md). The **only** differences between healthy and degraded modes are in the `citations[]` array. All other fields (`schemaVersion`, `body`, `generatedAt`, `playbookName`, `tenantId`, `dimensions`) are identical in shape.

### Healthy envelope (reference — index has observations)

```json
{
  "schemaVersion": "1.0",
  "body": "Matter ABC-123 currently grades **C** with a stabilizing trend... [GuidelineCompliance assessment Q1] shows 78% adherence... per document evidence in [Engagement Letter §4.2], scope is bounded to litigation defense...",
  "citations": [
    { "type": "assessment", "id": "11111111-...", "label": "Q1 2026 Guideline Compliance assessment", "excerpt": "..." },
    { "type": "assessment", "id": "22222222-...", "label": "Q1 2026 Budget Compliance assessment", "excerpt": "..." },
    { "type": "document",   "ref": "spe://drive/X/item/Y", "label": "Engagement Letter §4.2", "chunkId": "chunk-7" }
  ],
  "generatedAt": "2026-06-11T15:42:00Z",
  "playbookName": "matter-health-single",
  "tenantId": "abc-...",
  "dimensions": ["composite","trend","themes","inflection","critical","risk","evidenceGaps"]
}
```

### Degraded envelope (this scenario — `spaarke-insights-index` empty for this Matter)

```json
{
  "schemaVersion": "1.0",
  "body": "Matter ABC-123 currently grades **C** with a stabilizing trend... [GuidelineCompliance assessment Q1] shows 78% adherence... The Outcomes Achievement dimension shows steady progress per [Outcomes Achievement assessment Q1 2026]. (Document-grounded observations are not currently available — narrative is synthesized from KPI assessments only.)",
  "citations": [
    { "type": "assessment", "id": "11111111-...", "label": "Q1 2026 Guideline Compliance assessment", "excerpt": "..." },
    { "type": "assessment", "id": "22222222-...", "label": "Q1 2026 Budget Compliance assessment", "excerpt": "..." }
  ],
  "generatedAt": "2026-06-11T15:42:00Z",
  "playbookName": "matter-health-single",
  "tenantId": "abc-...",
  "dimensions": ["composite","trend","themes","inflection","critical","risk","evidenceGaps"]
}
```

### Field-by-field expectation table

| Field | Healthy | Degraded | Verified by |
|---|---|---|---|
| `schemaVersion` | `"1.0"` | `"1.0"` (unchanged) | Envelope schema §2 — `const: "1.0"` |
| `body` (narrative) | Non-empty markdown referencing assessments + document evidence | Non-empty markdown referencing **only** KPI assessments; should mention evidence-gap dimension naturally | Playbook prompt covers `evidenceGaps` as one of the 7 dimensions |
| `citations[]` overall length | ≥1 | ≥1 (must contain ≥1 `assessment` item if Matter has ≥2 KPI assessments) | EvidenceSufficiencyNode requires `kpiAssessments.min: 2`; those assessment citations remain |
| `citations[].type=="document"` count | ≥0 | **0** (empty) | `IndexRetrieve` returned zero rows → GroundingVerify has no document chunks to attach |
| `citations[].type=="assessment"` count | ≥2 | ≥2 (unchanged) | Sourced from QueryDataverseNode `assessments` output, not from index |
| `generatedAt` | ISO-8601 UTC | ISO-8601 UTC | Playbook `persistEnvelope` field mappings |
| `playbookName` | `"matter-health-single"` | `"matter-health-single"` (unchanged) | Q-U1 bare-name rule |
| `tenantId` | UUID | UUID (unchanged) | Multi-tenant — independent of index state |
| `dimensions` | 7-element array | 7-element array (unchanged) | The `evidenceGaps` dimension naturally absorbs the missing-documents condition |
| `decline` flag | absent | **absent** (degraded is NOT decline) | Decline is gated by KPI count <2, not by empty observations |
| HTTP status of `POST /api/insights/ask` | `200 OK` | **`200 OK`** | Empty index is a successful run, not an error |
| `Content-Type` | `application/json` | `application/json` | Standard JSON, NOT `application/problem+json` |

**Invariant**: degraded mode does **NOT** produce a `decline` envelope. Decline is reserved for "insufficient KPI assessments" per `EvidenceSufficiencyNode`. The two states are distinct and orthogonal:

| State | Trigger | Envelope shape |
|---|---|---|
| **Decline** (Scenario B / SC-07) | `<2` KPI assessments | `decline=true`, `declineReason="Insufficient KPI assessments..."`, empty `body`/`citations`, NOT persisted |
| **Degraded** (this scenario / SC-14) | `≥2` KPI assessments, empty `spaarke-insights-index` for this Matter | Full narrative `body`, `citations` populated with assessments only (no documents), persisted to `sprk_performancesummary` |

---

## 3. Operator script

### Prerequisites

1. **Dev BFF healthy**: `GET https://spaarke-bff-dev.azurewebsites.net/healthz` returns `200 OK`.
2. **Kill-switch OFF**: `DocumentIntelligence:Enabled=true` AND `Insights:Enabled=true` in dev BFF App Service config (default state).
3. **Test Matter with ≥2 KPI assessments AND empty index coverage**. There are two ways to satisfy this:
   - **3a (preferred)**: A Matter that exists in `spaarkedev1` but whose source documents were **never ingested** by the files-index pipeline (e.g., a Matter created recently before the pipeline ran, or a Matter whose docs reside on a SharePoint Embedded container not wired to `spaarke-insights-index`). Verify via:
     ```
     GET /api/insights/index-status?matterId=<GUID>
     ```
     (or query the Azure AI Search `spaarke-insights-index` directly: `search.documents("matterId eq '<GUID>'")` → 0 hits)
   - **3b (fallback)**: Re-use the **Scenario A qualifying Matter** but temporarily disable the `indexRetrieveObservations` node's index hit by setting the `topK` to 0 OR by pointing the playbook at a non-existent index name. **NOT recommended** for r1 — Scenario 3a is more authentic and requires no playbook surgery.
   - **3c (last resort)**: If neither 3a nor 3b is achievable in dev, the architectural guarantee in §1 stands as static evidence and the SC-14 sign-off can be made on the basis of code-and-design review with the live invocation deferred to a separate ticket against the next dev environment refresh. Document the reason in the Sign-off table below.
4. **`sprk_aitopicregistry` row deployed**: row for topic `matter-health-single` with `sprk_cachettlminutes=60` (verified in Task 027).
5. **Browser**: Chrome/Edge, signed in as `tessa@spaarkedev1.onmicrosoft.com`, DevTools open (Console + Network tabs visible).

### Steps

| # | Action | Expected console / network signal |
|---|---|---|
| 1 | Navigate to the **empty-index Matter** form (per Prereq 3a) in `spaarkedev1` model-driven app | Form loads. Console shows `[sprk-matter-insight-card] mount-glue loaded` (FR-17 signal). |
| 2 | Wait for OnLoad pre-warm POST to complete (≤2s) | Console shows `[sprk-matter-insight-card] pre-warm POST fired` with `{question: "matter-health-single", subject: "matter:<GUID>"}`. Network tab shows `POST /api/insights/ask` returning **`200 OK`** (NOT 5xx). |
| 3 | Inspect the response body of that POST | Body matches the **Degraded envelope** template in §2: `decline` absent, `body` is non-empty markdown, `citations[]` contains assessment items, `citations[]` contains **zero** items with `"type": "document"`. |
| 4 | Click the sparkle icon on `tab_report card_section_3` | Network tab shows a **second** `POST /api/insights/ask` returning the same degraded envelope (cache hit on second attempt within TTL — see Scenario A SC-06 verification). |
| 5 | Refresh the Matter form (F5) and read `sprk_performancesummary` via Advanced Find or Web API:<br>`GET /api/data/v9.2/sprk_matters(<GUID>)?$select=sprk_performancesummary` | Field contains the degraded envelope JSON. **Critically**: the envelope IS persisted (degraded ≠ decline). |
| 6 | Open App Insights / Kusto:<br>`customEvents \| where name == "InsightWidgets.Invocation" and customDimensions.matterId == "<GUID>" \| project timestamp, customDimensions` | Telemetry rows present. `documentCitationCount` dimension (if surfaced) is `0`; `assessmentCitationCount` is ≥2. No error / exception events. |
| 7 | (Optional — operator with Azure AI Search reader role) Query `spaarke-insights-index` for this Matter:<br>`POST /indexes/spaarke-insights-index/docs/search?api-version=2024-07-01` body `{ "search": "*", "filter": "matterId eq '<GUID>'", "top": 1 }` | Returns `{"value": []}` — confirms degraded condition was the actual state, not a false-positive on the envelope shape. |
| 8 | Read the narrative body in the response. Apply the **user-utility judgment criteria** in §4 below. | Narrative is coherent, useful, and acknowledges the document-evidence gap naturally (does not pretend documents were consulted). |

### Visible-render caveat

Same as Scenarios A/B/C — the React `InsightSummaryCard` IIFE bundle is **NOT** deployed in r1 (Task 043 P1 gap, documented in `uat-scenarios.md` §"Phase 4 IIFE-bundle visible-render gap"). r1 verification is **envelope + Network-tab + Dataverse field + telemetry**; visible-render confirmation of the degraded UX (limitation banner / muted citation badges) is a **r2 / P1 follow-up**.

---

## 4. User-facing limitation requirement (FR-26)

FR-26 acceptance text: *"limitation documented in user-facing help."*

### Where the limitation MUST be documented (binding for r1 closure)

| Surface | Status as of Task 064 | Owner |
|---|---|---|
| **`docs/guides/BUILD-A-NEW-INSIGHT-CARD.md`** — Tutorial section "Degraded mode" | Authored by Task 058 (Phase 5). Verify presence of: (1) what degraded mode is, (2) how it manifests in the envelope (no document citations), (3) that it does NOT block narrative, (4) operator/admin remediation path (run files-index ingest). | Task 058 author |
| **Spaarke Insights help tooltip on the card** (planned for r2 with the IIFE bundle) | Deferred — the card itself does not render in r1. The textual limitation in the narrative `body` (§2 example, last sentence) is the in-product surface for r1. | r2 |
| **`sprk_aitopicregistry.sprk_helptext`** (or equivalent help-content column) for topic `matter-health-single` | Optional for r1. If populated, should briefly state "When document evidence is missing, the summary will rely on KPI assessment data only and will say so explicitly." | Task 027 author (verify) |

### Verification checklist for Task 064 sign-off

- [ ] **§4.A — Tutorial doc**: `docs/guides/BUILD-A-NEW-INSIGHT-CARD.md` contains a section titled (containing one of) "Degraded mode", "When documents are missing", or "Empty index handling".
- [ ] **§4.B — In-narrative disclosure**: The example degraded envelope in §2 demonstrates that the playbook prompt MUST naturally surface "document-grounded observations are not currently available" (or equivalent wording) when the observations array is empty. Operator verifies via Step 3/Step 8 above that this text or equivalent appears in the live `body`. If absent, the playbook prompt needs a one-line addition (Task 020 surface) — this would block SC-14 but is a known-cheap fix.
- [ ] **§4.C — No silent failure**: Network tab shows `200 OK`, NOT 5xx. Telemetry shows no error/exception events. Acknowledges the operational principle: degraded ≠ failure.

If §4.A is absent at Task 064 sign-off time, escalate to the doc owner (Task 058). The remediation is a documentation-only edit and does NOT block UAT envelope acceptance.

---

## 5. Acceptance criteria mapping

| POML criterion | Verified by | Pass condition |
|---|---|---|
| "Narrative renders in degraded mode" | Step 3 + Step 8 inspection of `body` field | `body` length > 0; coherent markdown; covers ≥4 of the 7 baseline dimensions; acknowledges missing-documents naturally |
| "Document citations empty but no error" | Step 3 + Step 6 | `citations[].type=="document"` count == 0; HTTP 200; no exception telemetry |
| "Limitation surfaced to user" | Step 8 + §4 checklist | EITHER (a) narrative body contains an in-text limitation sentence (preferred per Step 8) OR (b) `docs/guides/BUILD-A-NEW-INSIGHT-CARD.md` "Degraded mode" section exists (per §4.A) — both is best |

**SC-14 verification path**: Steps 1–8 of §3, plus §4 checklist items A and B (C is automatic if A/B pass).

---

## 6. Sign-off

| Signer | Role | Date | Pass / Fail | Comments |
|---|---|---|---|---|
| | | | | |
| | | | | |

---

## 7. Notes for the aggregator (Task 065/066)

- This scenario verifies **SC-14** + **FR-26** independently of Scenarios A/B/C; results should be aggregated into the Phase 6 UAT close-out alongside SC-05/06/07/08.
- The architectural guarantee in §1 (`requireEvidence=false` + KPI-only sufficiency gating) means SC-14 is **structurally** met by the playbook design; this scenario is a **behavioral confirmation**, not a discovery of new risk.
- If Prereq 3a cannot be satisfied in dev (no empty-index Matter exists), document the reason and use 3c (static review sign-off) — this is acceptable for r1 since the architectural verification in §1 is binding.
- Decline path (SC-07, Scenario B) and degraded path (this scenario, SC-14) are distinct and must not be conflated in the close-out narrative.

---

*Scenario authored 2026-06-11 by Task 064 (Wave 5, parallel sibling to 061-063, 065, 066). STANDARD rigor. Static verification + operator script. SC-14 architectural guarantee verified via `matter-health-single.playbook.json` lines 103-104 and 113-129. Behavioral confirmation deferred to operator execution per §3.*
