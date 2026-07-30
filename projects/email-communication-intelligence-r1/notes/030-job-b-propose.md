# 030 — Job B PROPOSE (allow-list-gated, cited, pending field-update proposals)

> **Task**: 030 (P3, FULL rigor, opus/high) · **Depends on**: 020 (association), 022 (triage Action), 001 (allow-list verified) · **Blocks**: 031 (apply), 032 (feed)
> **Date**: 2026-07-29

---

## BINDING DECISION 1 (resolved) — proposal store = `sprk_emailreviewlog` `Proposed` rows (NO new table)

The POML §11 framed the pending-proposal store as "new only because there is no existing per-email
proposal record." On inspection, **`sprk_emailreviewlog` IS the designed proposal-lifecycle store** — its
`sprk_action` option-set is `Classified (100000000) / Proposed (100000001) / Approved (100000002) /
Overriden (100000003) / Dismissed (100000004) / Applied (100000005)` (task 012 verification). The
`Proposed` value exists precisely to hold a pending, human-confirmable proposal. No proposal-store table
was created by the operator, and creating one would duplicate this designed lifecycle.

**Resolution (project-scoped, supersedes the POML's "new store" wording):** each pending proposal is stored
as a **`sprk_action = Proposed`** row on `sprk_emailreviewlog`, `sprk_actortype = Machine (100000000)`,
carrying:

| Column | Value |
|---|---|
| `sprk_communication` | the email (EntityReference → `sprk_communication`) |
| `sprk_targetentity` | the associated entity logical name (text, e.g. `sprk_matter`) |
| `sprk_targetrecordid` | the associated record GUID (text) |
| `sprk_targetfield` | the allow-listed field logical name |
| `sprk_confidence` | proposal confidence (decimal) |
| `sprk_actor` | `email-propose` |
| `sprk_sourceref` | the citation locator (e.g. `body: sentence 1`, `OA_908068.pdf p.1`) |
| `sprk_aisuggestion` | the full proposal JSON (below) |
| `sprk_name` | `Proposed update: {entity}.{field}` (truncated ≤850) |

`sprk_aisuggestion` JSON shape (what **031 apply** + **032 feed** + r5 consume):

```json
{
  "field": "sprk_closingdate",
  "fieldType": "DateTime",
  "oldValue": "2026-01-01",
  "newValue": "2026-08-15",
  "citation": { "source": "body", "locator": "body: sentence 1", "quotedText": "the closing has been moved to August 15, 2026" },
  "reason": "The email states the matter's closing date changed to August 15, 2026.",
  "confidence": 0.9,
  "requireConfirm": true,
  "privilegeFlagged": false
}
```

- **Open/pending** = a `Proposed` row with **no later terminal row** (`Approved`/`Applied`/`Dismissed`/
  `Overriden`) for the same `(sprk_communication, sprk_targetentity, sprk_targetfield)`. This is exactly
  what task 032's feed queries and task 031's apply reads.
- **Append-only is correct**: a proposal is immutable once proposed; its resolution is a NEW row (031
  writes `Approved`+`Applied`; a dismiss writes `Dismissed`). This step never mutates a `Proposed` row.
- **Idempotent re-enrichment**: `LoadOpenProposalFieldsAsync` walks the log chronologically per field; an
  OPEN `Proposed` for a field suppresses a duplicate on re-run.

## BINDING DECISION 2 (honored) — the propose Action is Job B's OWN targeted extraction, not a re-classification

`PROPOSE-FIELD-UPDATES` (`sprk_actioncode = propose-field-updates`) is a single, legitimate Action call: it
takes the enabled allow-list fields (+ `sprk_fieldtype`/`sprk_extractionguidance`) + the email/attachment
text + the already-produced triage output **as grounding**, and extracts candidate NEW values. The
`<escalation>` trigger was **not** fired — targeted extraction produces a defensible old→new proposal. The
facade (`CommunicationProposeAi`) has **no** `ICommunicationClassificationAi`/`IOpenAiClient` dependency —
structurally incapable of a second classification (proven by `ProposeFieldUpdatesEvalTests`).

---

## Files created / modified

**Created**
- `infra/dataverse/actions/propose-field-updates.action.json` — the prompted Action (flat systemPrompt +
  `outputSchema`, following nda-review; NOT classic-JPS — the target fields are dynamic per-run, so no
  static `$choices`).
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ICommunicationProposeAi.cs` + `CommunicationProposeAi.cs` + `NullCommunicationProposeAi.cs` — the ADR-013 facade (mirrors `ICommunicationTriageAi`).
- `src/server/api/Sprk.Bff.Api/Services/Communication/EmailProposalShaping.cs` — pure helpers:
  `EmailUpdateFieldTypes` (as-built `sprk_fieldtype` int→label), `EmailUpdateFieldCoercion` (coerce/drop),
  `CitationVerifier` (verify-cited-text).
- Tests: `tests/integration/seam/Communication/EmailProposeSeamTests.cs`,
  `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/EmailProposalShapingTests.cs`,
  `tests/integration/contract/Eval/propose-field-updates-eval-cases.json` + `ProposeFieldUpdatesEvalTests.cs`.

**Modified**
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationEnrichmentService.cs` — new
  `ICommunicationProposeAi` ctor dep + best-effort `email-propose` step (`RunEmailProposeAsync` +
  allow-list/coerce/verify/store helpers).
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs` — `EmailPropose = "email-propose"` + `All`.
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` — Null + real facade registration.
- `infra/dataverse/sprk_playbookconsumer-rows.json` — `email-propose` Binding row.
- `tests/integration/contract/Eval/golden-utterances.json` — GU-139 (ConsumerTypes.All forcing function).
- 3 seam test ctor sites updated for the new dependency.

## The propose flow (FR-09) — how the gates work

On the enrichment path, after the `email-triage` step (best-effort via `RunStepAsync`; NFR-04 non-fatal):
1. Read the communication's 7-core regarding lookups → the associated `(entity, recordId)` (task 020). No
   association → return.
2. **Allow-list gate (SOLE gate, FR-11/C-4):** resolve the entity's `sprk_recordtype_ref` id, then query
   `sprk_emailupdatefield` where `sprk_enabled = true` AND `sprk_targetentity = {recordTypeRefId}`, reading
   `sprk_targetfieldlogicalname` (**not** `sprk_targetfield`), `sprk_fieldtype`, `sprk_extractionguidance`,
   `sprk_requireconfirm`. No enabled rows → return.
3. Invoke `ICommunicationProposeAi.ProposeAsync` with the enabled fields + email text + triage grounding.
4. Per candidate: (a) **allow-list re-check** (drop if field ∉ enabled set); (b) **coerce** per
   `sprk_fieldtype` (drop if uncoercible — Number/Currency→invariant decimal, DateTime→ISO-8601,
   Boolean→true/false, Text/Memo/OptionSet/Lookup→trimmed label for 031 to resolve); (c) **read current
   value** = `oldValue`; (d) **verify-cited-text (NFR-06)** — re-locate `quotedText` in
   subject+body+attachmentText (whitespace/case-normalized), drop if not found; (e) **no-op skip** if
   coerced newValue == oldValue; idempotency skip if an open `Proposed` exists.
5. Store survivors as `Proposed` rows. **Never writes the target record** (application is 031).
6. **ADR-015 privilege**: `privilegeFlagged` (from the reconstructed classification) is carried in the JSON
   as a flag — never suppressed, never auto-acted. Nothing auto-finalizes (all rows are `Proposed`).

## Hand-off notes

**Task 031 (apply):** read an open `Proposed` row → parse `sprk_aisuggestion` → build the
`UpdateRecordRequest` (use `fieldType` to pick `ActionFieldMapping` type; 031 resolves OptionSet/Lookup
labels + whole-vs-decimal Number via target metadata) → `IActionSeam.UpdateRecordAsync` under the
confirming user's OBO (ADR-028) → write `Approved` + `Applied` rows (`sprk_actortype = Human`); a dismiss
writes `Dismissed`. Do NOT mutate the `Proposed` row (append-only).

**Task 032 (feed):** query `sprk_emailreviewlog` for `sprk_action = Proposed` rows with no later terminal
row (the "open" definition above), ranked (by `sprk_confidence` and/or the communication's RI-confidence),
projecting the `sprk_aisuggestion` payload for r5 to render the per-field confirm card.
