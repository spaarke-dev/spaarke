# Task 005 — dev data + the two negative controls: record

> **Date**: 2026-10-04 (UTC) · **Status**: complete · **Env**: `spaarkedev1` · solution `OntologyPlatformSolution`
> **Policy row**: `sprk_policy` `4d204810-61bf-f111-aaaf-0022482913fc` (POL-COMMIT-BUDGET) — re-verified
> `sprk_enabled = false` ("No") after this task. Not touched.

## 0. Two readings — kept apart per the POML's own instruction

This file records **seeded** rows (I authored them to construct a known test case) separately from
**real/pre-existing** rows (already present in `spaarkedev1`, not created by this task). Seeded data proves the
Path B predicate **evaluates** correctly on a hand-built case. It proves nothing about whether the predicate
**fires on reality** — that is a different, unretired risk (design.md §9, spec risk row 1), and criterion 2
passing here is the first (evaluates) reading only.

**None of the pre-existing communications already on REAL-2026-123456.01/.02 (found before this task touched
anything — "Test Email with Attachments", "Engagement Letter Smith v Smith", etc.) were used or relied upon.**
All three test cases below rest entirely on rows this task created.

## 1. Matter assignment

| Role | Matter | GUID | Status |
|---|---|---|---|
| **POSITIVE** (Path B must fire) | `REAL-2026-123456.01` | `2444af6d-e1f2-f011-8406-7ced8d1dc988` | **Real, pre-existing** (already carried a budget per design.md §8.1 step 2's own instruction) |
| **NEGATIVE CONTROL 1** (comm + budget revision in window → must NOT fire) | `REAL-2026-123456.02` | `b68299c6-bafb-f011-8407-7c1e520aa4df` | **Real, pre-existing** (already carried a budget) |
| **NEGATIVE CONTROL 2** (no qualifying comm → must NOT fire) | `ONTOLOGY DEV SEED 005 - Negative Control 2 (No Qualifying Communication)` / number `ONTOLOGY-DEV-SEED-005-NC2` | `d14d79f4-8fbf-f111-aaaf-0022482913fc` | **SEEDED — new matter**, created by this task so NC2 did not have to risk reusing a real matter a third time. Clearly prefixed and removable. |

Only two matters in the entire `spaarkedev1` org carried a `sprk_budget` row before this task (`.01` at
$1,500; `.02` at $150,000) — confirmed by querying every `sprk_budget` row org-wide before creating anything.
A third distinct matter was needed for NC2 (reusing `.01` or `.02` a second time would not isolate the "no
communication" case), so a new, clearly-named dev matter was created rather than inventing a budget history on
an unrelated real matter.

## 2. Rows created — every GUID, every table, seeded vs. real

| # | Table | GUID | Seeded / Real | Role |
|---|---|---|---|---|
| 1 | `sprk_matter` | `d14d79f4-8fbf-f111-aaaf-0022482913fc` | **SEEDED** | NC2 matter |
| 2 | `sprk_budget` | `2a1b0d23-90bf-f111-a05b-3833c5e9614d` | **SEEDED** | NC2 budget, $10,000 |
| 3 | `sprk_spendsnapshot` | `98b7d63b-90bf-f111-a05b-3833c5e9614d` | **SEEDED** | POSITIVE matter (.01) — invoiced $1,800 ≥ budget $1,500 |
| 4 | `sprk_spendsnapshot` | `99b7d63b-90bf-f111-a05b-3833c5e9614d` | **SEEDED** | NC2 matter — invoiced $12,000 ≥ budget $10,000 |
| 5 | `sprk_budgetrevision` | `e4e5dbe6-8fbf-f111-aaaf-0022482913fc` | **SEEDED** | NC1 matter (.02) — $150,000 → $185,000, `sprk_revisedon` = 2026-10-03T14:00:00Z (inside the 30-day window) |
| 6 | `sprk_communication` | `f00b6389-8fbf-f111-aaaf-0022482913fc` | **SEEDED, but classification is LIVE** (see §3) | POSITIVE matter (.01) — "Fee / rate change" |
| 7 | `sprk_communication` | `9848e2b0-8fbf-f111-aaaf-0022482913fc` | **SEEDED, but classification is LIVE** (see §3) | NC1 matter (.02) — "Scope / budget change" |

No `sprk_signal` or `sprk_decisionrecord` rows were created — out of scope (the evaluator, task 021, is not yet
built; criterion 2's own verify-by says "evaluated by hand").

**Note on the budget and spend-snapshot rows (CLAUDE.md §0.3 discipline).** The actually-deployed
`sprk_policyversion.sprk_rulebody` for POL-COMMIT-BUDGET (task 004) is a **two-clause** predicate — it reads
only `sprk_communication` (classification + window + review outcome) and `sprk_budgetrevision` (window). It
does **not** read `sprk_spendsnapshot` or `sprk_budget` at all. Rows #2–4 above exist to satisfy design.md
§8.1's original three-part "exit condition" checklist (which this project's own notes call superseded-in-
practice by the simpler two-clause rule the policy row actually carries) and to honor the POML's literal step
2 instruction ("seed spend on a matter that already carries a budget"). **They are not required for, and are
not read by, the predicate that criterion 2 actually gates.** Flagging this explicitly so nobody later asserts
the rule's sentence says anything about spend/budget amounts — it does not.

## 3. The classification was produced by the LIVE enrichment path — not hand-written

Per the POML's hard rule and escalation trigger, the two triage categories above were **not** set directly via
a Dataverse write. They were produced by **actually calling the deployed BFF's live capture + enrichment
pipeline** against `spaarkedev1`, the same pipeline production email ingestion uses.

### Mechanism

1. **Entry point**: `POST https://spaarke-bff-dev.azurewebsites.net/api/office/save` (`OfficeEndpoints.SaveAsync`
   → `OfficeService.SaveAsync`), the same endpoint the Outlook "Save to Spaarke" add-in calls. Authenticated as
   `ralph.schroeder@spaarke.com` via `az account get-access-token --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c`
   (verified live against `/ping` and `/api/me` first).
2. For `ContentType: Email`, `OfficeService.SaveAsync` calls `EmailUploadCaptureService.CaptureAsync`
   **synchronously, inline, before returning the 202** — which (a) creates the canonical `sprk_communication`,
   (b) runs `IncomingAssociationResolver.ResolveAsync` (the Association Engine), and (c) runs
   `ICommunicationEnrichmentService.EnrichAsync`.
3. **Why the AI classifier genuinely ran, not just the caller-supplied regarding.** The Association Engine's AI
   pass (rung 4 semantic + rung 5 `AiClassificationRung`) is coded to run **unconditionally**
   (`IncomingAssociationResolver.cs:152-162`, "ALWAYS run... even when a deterministic participant/contact
   match already auto-filed") — specifically so a caller-supplied regarding target never short-circuits the
   classifier. Rung 5 fired on both emails (confirmed in `sprk_associationprovenance.rungsFired`) and persisted
   a metadata-only classification signal.
4. `CommunicationEnrichmentService.RunEmailTriageAsync` then read that persisted signal (non-null — the
   precondition the escalation trigger cared about) and called the **real TRIAGE-EMAIL catalog Action**
   (`sprk_actioncode = "triage-email"`, via `ICommunicationTriageAi`/`IActionRunner`) — a genuine Azure OpenAI
   completion, not a lookup table — which produced `category`, `summary`, `obligations`, `priority`, and
   `reviewOutcome`, persisted onto the communication by `PersistTriageResultAsync`.
5. The email **content** was written to be plausible for each category (modeled on the TRIAGE-EMAIL Action's
   own shipped examples for "Fee / rate change" and "Scope / budget change" in
   `infra/dataverse/actions/triage-email.action.json`) but the **category itself was never specified in the
   request** — the model chose it from the content.

### Evidence the classification is live, not fabricated

- **Communication 1** (`f00b6389-...`, matter `.01`): subject *"2027 rate schedule update"*, body describing a
  partner rate increase from $650→$725/hr. The model returned `sprk_triagecategory` =
  `8b62dd84-1fbc-f111-aaaf-3833c5e9614d` ("Fee / rate change" — confirmed by name lookup) and a
  model-generated `sprk_triagesummary`: *"The firm notifies a 2027 rate increase for partner time from $650 to
  $725 per hour and adds a senior associate at $410 per hour. The current scope of work remains unchanged and
  no urgent action is required."* — a paraphrase of the email, not an echo of any instruction. `reviewoutcome`
  = `File` (100000000).
- **Communication 2** (`9848e2b0-...`, matter `.02`): subject *"additional discovery scope"*, body describing
  two extra contract reviewers at +$35,000. The model returned `sprk_triagecategory` =
  `8d62dd84-1fbc-f111-aaaf-3833c5e9614d` ("Scope / budget change") and summary: *"Counsel requests client
  confirmation to add two contract reviewers for document review due to increased discovery volume. The
  estimated additional cost is $35,000 beyond the current budget."* `reviewoutcome` = `Route` (100000002).
- `sprk_associationprovenance` on communication 1 independently shows rung 5 (`AiClassification`) fired with
  its OWN (separate, metadata-only) classification call that returned category `"invoice"` — a genuinely
  different label than the TRIAGE-EMAIL Action's final `"Fee / rate change"` output, which is exactly what the
  architecture predicts (two distinct AI calls: rung 5's coarse gate signal, then the TRIAGE-EMAIL Action's
  taxonomy-mapped structuring pass) and is strong evidence neither step was shortcut.
- Both communications were classified within roughly a second of the save — consistent with one LLM call, not
  a static mapping.

**Escalation trigger not fired.** The live path was triggered successfully in dev — no hand-written category
was needed.

## 4. Hand-evaluation of the predicate (acceptance criteria 1–3)

`sprk_policyversion.sprk_rulebody` (task 004):
```
exists sprk_communication where regardingmatter=<matter>
  AND sprk_triagecategory in {Fee/rate change, Scope/budget change}
  AND sprk_receiveddate >= now-30d
  AND sprk_reviewoutcome <> Dismiss
AND
notExists sprk_budgetrevision where sprk_matter=<matter> AND sprk_revisedon >= now-30d
```

### POSITIVE — `REAL-2026-123456.01` (`2444af6d-e1f2-f011-8406-7ced8d1dc988`)

| Conjunct | Value | Holds? |
|---|---|---|
| exists comm | `f00b6389-...`, category=Fee/rate change, received 2026-10-03T12:00Z, reviewoutcome=File(≠Dismiss) | ✅ |
| notExists budgetrevision | zero `sprk_budgetrevision` rows reference this matter (verified by query) | ✅ |

**Both conjuncts hold → Path B fires.** Criterion 1 satisfied.

### NEGATIVE CONTROL 1 — `REAL-2026-123456.02` (`b68299c6-bafb-f011-8407-7c1e520aa4df`)

| Conjunct | Value | Holds? |
|---|---|---|
| exists comm | `9848e2b0-...`, category=Scope/budget change, received 2026-10-03T13:00Z, reviewoutcome=Route(≠Dismiss) | ✅ |
| notExists budgetrevision | `e4e5dbe6-...` revised 2026-10-03T14:00Z — **inside** the 30-day window | ❌ — a revision EXISTS in-window |

**`notExists` conjunct fails → Path B does NOT fire.** Criterion 2 satisfied — the exists half genuinely holds
here; only the budget-revision clause is what suppresses it, isolating that one conjunct.

### NEGATIVE CONTROL 2 — `ONTOLOGY DEV SEED 005 - Negative Control 2` (`d14d79f4-8fbf-f111-aaaf-0022482913fc`)

| Conjunct | Value | Holds? |
|---|---|---|
| exists comm | zero `sprk_communication` rows reference this matter (verified by query) | ❌ |
| notExists budgetrevision | zero rows (trivially true, and irrelevant — the first conjunct already fails) | ✅ (moot) |

**`exists` conjunct fails → Path B does NOT fire.** Criterion 3 satisfied — this matter carries a
budget-exceeding `sprk_spendsnapshot` (invoiced $12,000 ≥ budget $10,000) precisely so a reader cannot mistake
"no snapshot" for the reason nothing fires; the predicate does not read spend at all (see §2 note), and the
sole reason it does not fire is the missing qualifying communication.

## 5. What was deliberately left undone

- **No `sprk_signal` or `sprk_decisionrecord` rows.** The evaluator (task 021) does not exist yet; criterion
  2's verify-by is explicitly "evaluated by hand," which §4 above does.
- **No Do-lane seed.** Per the POML's hard rule and ISS-003 (#1050, 48/49 stranded `Draft` `sprk_event` rows),
  that is task 060's job, not this one's.
- **`sprk_policy.sprk_enabled` left at `false`** — re-verified by query after all writes (§0).
- **No `PublishAllXml` / tenant-wide publish.** Every write here is a direct Dataverse Web API / BFF-API write,
  immediately live for API consumers.
- Pre-existing communications already on `.01`/`.02` before this task (several test-looking rows with subjects
  like "Test Email with Attachments N") were left untouched and not relied upon by any of the three cases in
  §4.

## 6. Reproducibility / cleanup

To remove the seeded rows: delete, in this order (children before parents),
`sprk_communication` `f00b6389-...` and `9848e2b0-...`; `sprk_budgetrevision` `e4e5dbe6-...`;
`sprk_spendsnapshot` `98b7d63b-...` and `99b7d63b-...`; `sprk_budget` `2a1b0d23-...`; `sprk_matter`
`d14d79f4-...`. The two REAL matters (`.01`/`.02`) are untouched beyond gaining one new child
communication/spend-snapshot/budget-revision row each — no field on the matter record itself was modified.

---

## Addendum (main session, 2026-10-03): rows the task 005 inventory missed

Task 005 classified its two communications through the live Outlook "Save to Spaarke" endpoint
(`POST /api/office/save`). That path also creates a **`sprk_document`** row and uploads the `.eml` into the
matter's SharePoint Embedded container. Neither appeared in the original row list, so the seed was not fully
reversible from this record. Found by querying `sprk_document` for rows created today on the three matters.

| Table | GUID | Seeded/Real | Note |
|---|---|---|---|
| `sprk_document` | `7b00ed8f-8fbf-f111-aaaf-0022482913fc` | SEEDED | `.eml` on `REAL-2026-123456.01`, plus its file in SPE |
| `sprk_document` | `89ec13b7-8fbf-f111-aaaf-0022482913fc` | SEEDED | `.eml` on `REAL-2026-123456.02`, plus its file in SPE |

To undo: delete the `sprk_document` rows **and** the SPE files (deleting the row alone may leave the file).

**Correction on "REAL":** `REAL-2026-123456.01`/`.02` are existing dev matters from January 2026 (Real Estate
practice prefix), not production data. They are pre-existing and likely visible to the dev users, which is why
the seeded rows carry the "ONTOLOGY DEV SEED 005" name.

**Spend snapshots:** `98b7d63b-...` (on `.01`) and `99b7d63b-...` (on the NC2 dev matter) are **not read by the
seeded predicate**. The existing Finance `SignalEvaluationService` does read spend snapshots. As of this addendum
no `sprk_spendsignal` row has been created from them. **Owner decision 2026-10-03: KEEP them** (deliberate, e.g. for later Do-lane or finance testing). If the Finance signal service later produces a `sprk_spendsignal` on `REAL-2026-123456.01` or the NC2 matter, it comes from these seeded snapshots, not from real spend.
