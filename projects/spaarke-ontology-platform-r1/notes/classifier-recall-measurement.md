# Task 074 - classifier recall exit gate: record

> **Date**: 2026-10-07 · **Env**: `spaarkedev1` · branch `stream/d-074` (base `bf00db8c0`, includes task 072)
> **Status**: 🔴 **BLOCKED at the escalation trigger: no labelled set exists, and one cannot be built from spaarkedev1
> without owner judgement.** No recall has been measured, and the gate has **neither passed nor failed**.
> R1 cannot exit on criterion 11 / FR-40 until this is resolved.

## 1. D-49 conflict check with the email project (done BEFORE the column adds)

| Check | Result |
|---|---|
| Open PRs touching `*triage*` / `TriageCategor*` / `email-communication-intelligence*` | **none** (`gh pr list --state open`, 2026-10-07) |
| `email-communication-intelligence-r2` | Complete and archived (`a41b2a85f`, 2026-09-07: "portfolio registration - Issue #954 (Completed), .archived"). No in-flight work |
| `email-communication-intelligence-r3` | Design-only worktree (CLAUDE/design/README, empty notes). `design.md:126`: *"Triage category resolution - already fixed + UAT-passed in r2 ... not r3 scope."* |
| `origin/master` triage commits since 2026-09-20 | none that touch the `sprk_triagecategory` schema |
| Email code that reads `sprk_triagecategory` | `CommunicationEnrichmentService.ResolveTriageCategoryIdAsync` selects `sprk_name` by explicit column, and the 072 resolver selects `sprk_name` / `sprk_classifierguidance`. Neither enumerates all columns, so additive nullable columns cannot change their behaviour |

**Outcome: no objection and no overlap.** The project has no active session that could object (r2 is closed, r3
excludes triage), so the escalation trigger "the email project objects" did not fire. The record is *no conflict
found*, not a signed agreement from a person. If the owner wants explicit email-project sign-off, it is the same owner.

Separately, out of scope and already filed: ISS-007 / #1387 (`ResolveTriageCategoryIdAsync` ignores
`statecode`/`sprk_enabled`), from task 072.

## 2. Schema (D-49): created and read back

Method: Dataverse Web API, the task 007 recipe. POST `EntityDefinitions(LogicalName='sprk_triagecategory')/Attributes`,
explicit PascalCase `SchemaName`, header `MSCRM.SolutionUniqueName: OntologyPlatformSolution`, `DateTimeBehavior` on
the date. No MCP `create_table`. Then `PublishXml` for `sprk_triagecategory` only (no PublishAllXml). All HTTP 204.
Identity: `ralph.schroeder@spaarke.com` (az CLI, System Administrator). This is a schema write; no row values were written (see §4).

| Column | SchemaName | Type | Detail (read back) | MetadataId |
|---|---|---|---|---|
| `sprk_measuredrecall` | `sprk_MeasuredRecall` | Decimal | Min 0, Max 100, Precision 2, RequiredLevel None | `40e09375-b2c2-f111-a05c-3833c5e9614d` |
| `sprk_labelledsetsize` | `sprk_LabelledSetSize` | Integer | Min 0, Format None, RequiredLevel None | `45e09375-b2c2-f111-a05c-3833c5e9614d` |
| `sprk_recallmeasuredon` | `sprk_RecallMeasuredOn` | DateTime | Format DateOnly, **DateTimeBehavior DateOnly**, RequiredLevel None | `01949277-b2c2-f111-a05a-7c1e520a989f` |

**Solution membership (read back from `solutioncomponents`)**: each of the three is a componenttype-2 (attribute)
component of **`OntologyPlatformSolution`**. Note the difference from task 007: `sprk_triagecategory` is **not** a
whole-entity component of `OntologyPlatformSolution`. Before this task it belonged to `Default`, `SpaarkeMaster`
(rcb 0) and `Cr2b7d5` (rcb 1), so the columns joined the ontology solution as per-attribute components through the header.

**Values**: both gated rows (`Fee / rate change` `8b62dd84-...`, `Scope / budget change` `8d62dd84-...`) read back
`null / null / null`. That is correct: no measurement exists, and writing a placeholder would be a false record.

## 3. Why the labelled set cannot be built from spaarkedev1

All 292 `sprk_communication` rows were read on 2026-10-07. Most are system or test mail ("Test Email #N", demo-request
notifications, NDRs, "New Matter:" notifications, daily briefings). Searching every subject and plain-text body for
fee/scope language turns up only these positive candidates:

| id | Subject | Note |
|---|---|---|
| `3449bd41-24bc-f111-aaaf-3833c5e9614d` | EMPL-307998 ... | owner test probe: "two additional depositions - roughly $40-45k beyond the current budget" |
| `99eb9b52-43bc-f111-aaaf-0022482913fc` | PAT-477689 ... | **same sentence** as the row above |
| `72d96fce-37bc-f111-aaaf-3833c5e9614d` | PAT-477689 ... | probe: "increase the budget ... $14-45k beyond the current budget" |
| `8271522c-dabc-f111-aaaf-3833c5e9614d` | Email Uploaded from Outlook Add-in 9-30-2026 | **same sentence** as the row above |
| `4cded5d5-29bc-f111-aaaf-3833c5e9614d` | Form D - 2023 ... | probe: "100 additional hours - roughly $140-145k beyond the current budget" |
| `9848e2b0-8fbf-f111-aaaf-0022482913fc` | ONTOLOGY DEV SEED 005 ... additional discovery scope | task 005 seed |
| `f00b6389-8fbf-f111-aaaf-0022482913fc` | ONTOLOGY DEV SEED 005 ... 2027 rate schedule update | task 005 seed; the **only** fee/rate candidate |

That gives about 7 positives, or **4 independent texts**, with **1** fee/rate item. All were authored by the project team to be
positives, and all have already been classified by the model. Three "Fw: Lexology Pricing" rows are vendor
subscription pricing and are not obviously a matter fee change; whether they count is a labelling judgement. A recall figure here
would have a denominator of about 1-7, so it would not be a measurement. **Building the set means choosing source material and assigning
ground-truth business labels. The POML and the dispatch both reserve that for the owner.** I have assigned no labels.

## 4. 🔔 Human input required: what I need to proceed

**Decision 1: source of the ≥ 50 items (pick one)**
- **(A) Real mail, recommended.** The owner supplies real, redacted outside-counsel / client correspondence
  (`.eml`, `.msg` or pasted text) containing fee and scope changes plus ordinary traffic. This is the only option that
  measures what the §0 claim is about.
- **(B) Owner-labelled synthetic set.** I draft about 90 realistic emails to the quota below. The owner labels them **blind**,
  from the text and the live `sprk_classifierguidance` definitions only, without my intended label and before any
  classifier run. This is cheaper, but LLM-written mail is likely easier than real mail, so the result must be reported
  as "recall on a synthetic set". It does not verify the §0 claim as strongly.
- **(C) Hybrid.** (A) for whatever real mail is available, topped up with (B), with each source reported separately.

**Decision 2: what "≥ 50 items" counts.** Recall's denominator is the **positives**. D-10's own rationale ("one miss
moves the number ~2%") only holds with about 50 **positives**. On 50 items total with about 15 positives per category, one miss is
about 7%. **Proposal**: ≥ 25 positives per gated category (≥ 50 positives), plus ≥ 30 negatives of which ≥ 15 are hard
confusables (Invoice / Billing with amounts, Client instruction mentioning cost, Court / Filing with fee terms,
Scheduling of depositions, vendor pricing). That is about 80-100 items. Alternative: the literal 50 total.

**Decision 3: which recall gates.** Policy POL-COMMIT-BUDGET filters `sprk_triagecategory IN {Fee, Scope}`, so a
Fee↔Scope swap does **not** lose the signal. **Proposal**: report (i) strict per-category recall (Fee labelled → Fee
predicted; same for Scope) and (ii) **predicate recall** (labelled either → predicted either). **Gate on (ii) ≥ 80%** because it is
what the conjunction inherits, and flag any category whose strict recall is < 80% as a tie-breaker finding.
Alternative: gate on both strict figures.

**Decision 4: labelling protocol.** **Proposal**: a named labeller (owner or a legal-ops reviewer) labels each item
with one of the ten category names, using the live guidance text as the definition, before the classifier runs.
Items the labeller cannot call are marked `ambiguous` and excluded from the denominator. The exclusion rule is fixed
before the run, and the excluded count is reported. Nothing is relabelled after the run.

**Decision 5: harness and cost.** The real path is two model calls per item: rung-5 `ICommunicationClassificationAi`
(its signal is the **required** `classification` input of TRIAGE-EMAIL), then `ICommunicationTriageAi` → `ActionRunner`
→ the live `triage-email` Action with live guidance (072: enabled rows only, tie-breakers included). **Proposal**: an
opt-in `Category=Live` harness that builds the BFF DI container against spaarkedev1 and calls the two facades per
fixture item **in memory**, with no Dataverse writes and no `MatterId`, so no RAG grounding and no embedding call.
**Cost: 2 model calls per item, about 160-200 for 80-100 items, one run.** That is at the dispatch's ~200 ceiling, so a
repeat-for-stability run needs separate approval. The alternative is to ingest the items as `sprk_communication` rows through the
full enrichment pipeline. That is more faithful, because it includes matter grounding, but it writes about 90 rows and fires
downstream Signal and notification work in spaarkedev1.

Once the owner answers 1-5 (or confirms the proposals), the remaining work is: commit the set to
`tests/fixtures/ontology-classifier-recall/`, build and review the harness (Step 9.5), state the call count, run,
compute, write the two rows' three columns, and state pass or fail plainly.

## 5. Deviations from the POML
1. **Steps 1-7 not executed**. The escalation trigger is "labelling needs owner judgement" (dispatch rule), not the
   recall < 80% trigger. Nothing was measured, so neither pass nor fail is claimed.
2. **Schema done ahead of the measurement**. D-49's columns do not depend on the result. Their values stay null until
   a real figure exists.
3. **Step 8 (TASK-INDEX)** not edited, per dispatch: the main session owns `TASK-INDEX.md`. POML status set to `blocked`.
4. **"Agreement" recorded as a clean conflict check plus the r3 scope statement**, because no live email-project
   session exists (§1).
