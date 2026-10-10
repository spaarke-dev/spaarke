# Task 074 - classifier recall exit gate: record

> **Dates**: 2026-10-07 (schema, set drafted) · 2026-10-09 (run) · **Env**: `spaarkedev1` · branch `stream/d-074-run`
> (base `cd470f100` on `docs/ontology-platform-design`)
> **Status**: 🔴 **GATE FAILED: predicate recall 12/55 = 21.8% against the 80% floor (D-10 / D-64), measured on a synthetic
> set.** STOPPED at the escalation trigger. R1 does not exit on criterion 11 / FR-40 until recall improves or the §0 claim
> is re-scoped (both owner decisions). **The measurement found a probable root cause upstream of the classifier, §6.3:**
> the deployed TRIAGE-EMAIL prompt is corrupted, so the task-072 guidance has never reached the model. Read §6 first;
> §1-§4a are the history that led to the run.
> **Update 2026-10-10 (§7):** the owner repaired the corruption, but the pre-run render check found a **second, older
> blocker**: the Action's example inputs are objects, the JPS contract requires strings, so the prompt still falls back
> to flat text and the guidance is still absent. **Run 2 was NOT executed** (0 model calls). STOPPED again for an owner decision.

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

## 4a. D-64 (owner, 2026-10-07): labelling set drafted, awaiting blind owner labels

The owner chose the **synthetic** source. Report the result as *"recall on a synthetic set"*. Quotas: ≥ 25 fee/rate
positives, ≥ 25 scope/budget positives, ≥ 30 negatives with half hard confusables. **Gate**: combined fee-OR-scope
recall ≥ 80%, with strict per-category recall reported. **Labeller**: the owner. **One run** (about 2 calls per item); a second run only if
the first lands near 80%, and only after asking.

| File | Contents |
|---|---|
| `notes/074-labelling-set.json` | **92** items `{id, subject, from, body}`, ids `L001`-`L092` assigned **after** a seeded shuffle, **no category, intent or label field** (verified by a key scan). 88 distinct senders; body length 72-935 chars (median 311). Outside-counsel, client, vendor, court-notice, opposing-counsel and bulk mail |
| `notes/074-drafting-intent.json` | **SEALED.** The drafter's intended mix: 28 fee, 28 scope, 18 hard-confusable negatives, 18 other negatives. 17 items are tagged `borderline` with the reason. Use it only for the composition check after labelling. It is **not ground truth** |
| `notes/074-category-definitions.md` | The 10 enabled rows and their guidance, read live from spaarkedev1 with the production guidance URL (alphabetical, flattened as the prompt renders them), plus labelling instructions (`AMBIGUOUS` = excluded from the denominator) |

Buffer: 28 per gated category rather than 25, so up to 3 owner labels per category can differ from the intent
and the quota still holds. **The classifier has NOT been run on any item, and must not run until the labels are back.**

## 5. Deviations from the POML
1. **2026-10-07: steps 1-7 not executed** at first. The trigger then was "labelling needs owner judgement" (dispatch
   rule). D-64 (synthetic set) and D-116 (Claude labels with safeguards) later unblocked them; see §6.
2. **Schema done ahead of the measurement**. D-49's columns do not depend on the result. They were written after the
   run (§6.5).
3. **Step 8 (TASK-INDEX) and the POML** not edited, per dispatch: the main session owns both.
4. **"Agreement" recorded as a clean conflict check plus the r3 scope statement**, because no live email-project
   session exists (§1).
5. **The set is synthetic and AI-labelled (D-64, D-116)**, not "real communications" as POML step 1 says. Both are owner
   decisions.

## 6. Result (2026-10-09): 🔴 FAIL, and why

### 6.1 The numbers: recall on a synthetic set

Set: `tests/fixtures/ontology-classifier-recall/labelled-set.json`, **92 items: 55 positives (27 Fee / rate change,
28 Scope / budget change) and 37 negatives**. The set is synthetic (D-64); a Claude agent drafted it.

| Measure | Value | Gate |
|---|---|---|
| **Predicate recall** (labelled Fee-or-Scope → predicted Fee-or-Scope) | **12 / 55 = 21.8%** (Wilson 95%: 12.9% - 34.4%) | **≥ 80% on ≥ 50 positives (D-10 / D-64): FAIL** |
| Strict recall, Fee / rate change | 4 / 27 = **14.8%** | reported |
| Strict recall, Scope / budget change | 8 / 28 = **28.6%** | reported |
| Predicate precision | 12 / 12 = **100%** (no false positives) | information |
| Strict precision, Fee / Scope | 4/4 = 100% · 8/8 = 100% | information |
| Exact category accuracy (all 10 categories) | 40 / 92 = 43.5% | information |

Where the 43 misses went: **Invoice / Billing 22**, Client instruction 11, Administrative 10. By category: Fee →
Invoice / Billing 19 of its 23 misses; Scope → Client instruction 10, Administrative 7, Invoice / Billing 3. The
classifier is not noisy. It is conservative and never over-claims the gated categories, but it misses most of them.
That is the failure decision 13 (recall over precision) warns about. Full per-item output, confusion matrix and
warnings: `notes/074-run-results.json`.

### 6.2 About the key (D-116): AI-labelled, and the failure does not depend on it

- The key is **AI-labelled** for 68 items (a fresh blind Claude agent) and **owner-labelled** for 24 (L001-L024).
  No labeller saw classifier output or the sealed `074-drafting-intent.json`, which was not used for the key or the
  score. Agreement on the owner's 24: **exact 20/24, predicate 22/24**. On those 24 items the AI labeller called more
  items positive than the owner did (L009, L018: owner Scheduling, Claude Scope). A positive-leaning key makes the
  gate **harder** for the classifier, not easier.
- **Sensitivity (no relabelling):** owner-labelled subset 1/10 = 10.0%; Claude-labelled subset 11/45 = 24.4%. Even
  the extreme case, where *every* Claude-labelled positive the classifier missed is treated as a negative, gives
  12/21 = **57.1%**, still below 80%. **The FAIL holds whatever the key's quality.**
- Nothing was relabelled after the run.

### 6.3 Root cause (high confidence): the deployed TRIAGE-EMAIL prompt is corrupted. Filed as F-53 / #1584

Every one of the 92 TRIAGE-EMAIL calls logged `PromptSchemaRenderer: Failed to parse JPS schema; falling back to flat
text rendering`. Diagnosis:
1. In spaarkedev1, the `triage-email` Action's `sprk_systemprompt` (`c1fa96bf-...`, modified 2026-09-29) has all 9
   `instruction.constraints` entries replaced by `{"Length": N}` objects, which is what a PowerShell `ConvertTo-Json`
   over string objects produces. The repo mirror `infra/dataverse/actions/triage-email.action.json` is intact.
2. Reproduced offline: deserializing the live row into `PromptSchema` (`Constraints: IReadOnlyList<string>`) gives
   `JsonException ... Path: $.instruction.constraints[0]`.
3. On that exception the renderer sends the **raw JSON** as the prompt (`PromptSchemaRenderer.cs:151-160`). The 072
   `## Allowed values` section, with the Fee / Scope / Invoice tie-breakers, is only built on the JPS path, so **it
   never reached the model**. Logged prompt length about 11.4-11.8k, against a 10,623-char raw prompt plus input: no ~4k
   guidance block. The `$choices` enum still applies, through the schema.
4. **This is production behaviour, not a harness artefact.** In the deployed dev BFF's App Insights over the last 30
   days there were **18 email-triage runs and 18 JPS fallbacks**, with the same first and last timestamps
   (2026-09-29 16:39 → 2026-10-08 12:30).

The failure pattern fits. The classifier was never told the tie-breakers. Without "if an invoice already reflects the
amount, use Invoice / Billing; *otherwise* Fee / rate change", fee-schedule letters land in Invoice / Billing. Rung 5's
free-form categories on the positives also lean that way (`general-correspondence` 32, `invoice` 15).

**What this does NOT show:** recall with the intended prompt. That is unmeasured. The 21.8% is a true measurement of
the deployed system. It does not show what the classifier can do with the guidance, and it does not prove that
repairing the row will reach 80%.

### 6.4 Method, cost, validity

- **Harness**: `tests/integration/seam/Ai/ClassifierRecallLiveHarness.cs` (opt-in, `Category=Live`). It runs the real
  production chain against live spaarkedev1 rows: rung 5 `AiClassificationRung` → `CommunicationClassificationAi` →
  `AssociationStatusMapper` provenance → camelCase JSON round trip → `PersistedClassificationSignalReader` →
  `CommunicationTriageAi` → `ActionResolver` (live `email-triage` binding → Action `c1fa96bf-...`, modelTier Fast,
  temp 0.2) → `ActionRunner` (live `$choices`, enabled rows only, task 072). Deployment `gpt-4o-mini` for both calls.
  No Dataverse writes; no `MatterId`. Grounding is withheld in production anyway since uac-r2 task 176, so triage
  there is context-free too. Identity: the operator's `AzureCliCredential` (ADR-028 E-2), not the App Service MI.
- **Pre-flight (no model cost) passed**: the live enum held every key label; the guidance carried both tie-breakers.
  The guidance is *resolved* correctly. It is the *rendering* that falls back (§6.3).
- **Model calls**: attempt 0 aborted at DI wiring (`IRetrievalAccessTrim`, added on this base by uac-r2 task 176)
  after **1** call, with no result produced or seen. Attempt 1, **the** run: **184** calls (92 rung-5 + 92 triage,
  every item produced a signal). Total **185**. The harness now resolves the whole graph before the first call.
- **Run validity checks passed**: no `$choices` / guidance resolution failure, no triage failure, no Error-level log,
  every signalled item triaged. The renderer fallback is a Warning the harness recorded but did not fail on, because
  it is production behaviour (§6.3). A re-measure after the fix should confirm zero fallbacks in
  `074-run-results.json` `warnings`.
- **One run, temperature 0.2**: a sample, not an average. A second run was not done. The result is nowhere near the line
  (upper CI bound 34.4%), and D-64 allows a second run only near 80%, with approval.

### 6.5 D-49 persistence (written after the run, read back)

| Row | `sprk_measuredrecall` | `sprk_labelledsetsize` | `sprk_recallmeasuredon` |
|---|---|---|---|
| Fee / rate change (`8b62dd84-...`) | **14.81** | 92 | 2026-10-09 |
| Scope / budget change (`8d62dd84-...`) | **28.57** | 92 | 2026-10-09 |

Per-row recall is the category's **strict** recall. The gate figure (predicate 21.8%) is not per-category, so it lives
here. `sprk_labelledsetsize` is the size of the set the measurement ran on (92). The per-category denominators are 27
and 28. For task 103's admin page: show these columns as "measured on a synthetic set". Written with the operator's
identity (System Administrator), HTTP 204 each.

### 6.6 🔔 Escalation: owner decisions needed (not taken here)

1. **Repair the TRIAGE-EMAIL row** (#1584 / F-53). The Action belongs to email-communication-intelligence, and 072
   deliberately did not touch it. Recommended: restore `sprk_systemprompt` from the repo mirror with a verified direct
   write (`Deploy-ActionMirrors.ps1` cannot deploy a JPS mirror, mvp-technical-spec §16.2), read it back, confirm
   `PromptSchema` deserializes.
2. **Then re-measure once** with the same fixture and harness (~184 calls), and overwrite the D-49 columns.
3. **If recall is still below 80% after the repair**, the POML's options remain: improve the classifier (guidance,
   prompt, model tier; note rung 5's `invoice` lean) or re-scope the §0 claim.

Recommendation: 1 then 2. The current number measures a broken prompt, so improving or re-scoping before the repair
would decide on the wrong evidence.

## 7. 2026-10-10: row repaired, pre-run check FAILED, run 2 not executed

**What the owner did (coordinator, approved option 1 then 2):** the spaarkedev1 `triage-email` row
(`c1fa96bf-...`, modified **2026-10-10T13:33:08Z**) now holds exactly the repo mirror, minus the six deploy-row
scalars and every `$comment*` key. Before writing, the owner confirmed that every non-corrupt value was byte-identical
to the mirror. Exactly 33 fields were `{"Length"|"Count": N}` artefacts: the 9 constraints, the 5 tags, and fields in
the examples. Read-back matched, with 0 artefacts left. A backup of the corrupt row is in the coordinator's scratchpad
(`triage-fix/live-row-backup.json`).

**Pre-run check (dispatch step 1)** used the same code with zero model calls. The real `CommunicationTriageAi` →
`ActionResolver` (live binding) → `ActionRunner` → `LookupChoicesResolver` → `PromptSchemaRenderer` ran against the
repaired row, with only `IOpenAiClient` mocked to capture the final prompt. **It FAILED:**

| Check | Repaired row | Same row, example inputs stringified (in memory only) |
|---|---|---|
| `PromptSchema` deserializes | ❌ `JsonException ... Path: $.examples[0].input` | ✅ |
| Renderer takes the JPS path (no fallback warning) | ❌ falls back to flat text | ✅ |
| `## Allowed values for 'category'` present | ❌ | ✅ |
| Fee and Scope tie-breaker lines present | ❌ | ✅ both |
| `{"Length": N}` artefacts in prompt | none (repair worked) | none |

**Second cause (older than the corruption):** `PromptSchema.ExampleEntry.Input` is `required string`, and the JPS
authoring contract (`docs/guides/JPS-AUTHORING-GUIDE.md`, "examples Section") defines `examples[].input` as a string.
`triage-email.action.json` has used **object** example inputs since its first commit (`71ac39087`, task 022,
2026-07-29). So this Action has **never** rendered on the JPS path. The 2026-09-29 corruption was a second,
independent defect, and the repair, though necessary, could not on its own get the guidance into the prompt. Only
`triage-email` among the repo mirrors has object example inputs. Added to #1584 (comment 6098080127) and F-53.

**What run 1 (§6, 21.8%) therefore measured:** the classifier with **no** guidance, no rendered constraints and a
raw-JSON prompt. This was the deployed state before *and* after the corruption.

**Run 2: NOT executed.** Dispatch step 1 made a JPS-path render the precondition for spending ~184 calls. A run now
would measure the same flat-text prompt, with the constraints restored but still no guidance. Model calls this round: **0**.
D-49 values unchanged (run 1: Fee 14.81, Scope 28.57, n 92, 2026-10-09).

### 7.1 🔔 Owner decision needed

**Recommended (Path C, comply with the JPS contract):** re-author the 4 `examples[*].input` values as **strings** in
the mirror and the row. The verified mechanical form is a JSON-string of each object; prose is fine too. Then verify
with a read-back plus a local render showing no fallback and the "Allowed values" section present. After that, task
074 runs once with the unchanged fixture, key and harness. The Action belongs to the email project, so this is the
owner's call, like the first repair.

Rejected alternative: widening `ExampleEntry.Input` to accept objects. That changes the shared renderer and the JPS
contract for every Action in order to accommodate one non-conformant mirror (ADR-change territory, §6.5 Path B).
