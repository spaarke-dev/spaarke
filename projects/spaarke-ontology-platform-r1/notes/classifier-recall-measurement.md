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
> **Update 2026-10-10 (§8), CURRENT:** example inputs fixed (mirror PR #1602, dev row written 15:12:08Z). The render is
> verified on the JPS path with the guidance present. **Run 2: predicate recall 19/55 = 34.5% (Wilson 95% 23.4-47.7%).
> The gate STILL FAILS.** The binding constraint is now the two-stage design (§8.4): TRIAGE-EMAIL is told to map
> rung 5's free-form category, and rung 5 has no fee/scope vocabulary. STOPPED: classifier-design decision for the owner.

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

## 8. 2026-10-10: example inputs fixed, run 2. 🔴 FAIL at 34.5%

### 8.1 Row write (owner-approved: second fix plus re-run)

| Step | Evidence |
|---|---|
| Mirror | `infra/dataverse/actions/triage-email.action.json`: the 4 `examples[*].input` values become the compact JSON string of the same object. **PR #1602 to master** (branch `fix/triage-email-example-inputs-string`, not merged). The parsed document equals `origin/master` apart from those 4 values (checked in code). The repo's pre-commit prettier also collapsed 3 short arrays onto one line each (whitespace only; stated in the PR; hook not bypassed). The 29 triage-related tests pass |
| Backup | Live row read at 13:33:08Z (ETag `W/"27409480"`) and saved to the scratchpad as `triage-fix-074/row-backup-before-074-input-fix.json` (SHA-256 `2A3AC860...27006F`, 10,642 chars) |
| New value | Built from the **backup's own text** by replacing only the 4 input spans, so everything else is byte-identical. Cross-checked: it equals the PR #1602 mirror minus every `$comment*` key and exactly the six deploy scalars (`actionCode`, `actionType`, `description`, `modelTier`, `name`, `temperature`), the same rule the 13:33Z repair used |
| Write | `PATCH sprk_analysisactions(c1fa96bf-...)` `sprk_systemprompt` with `If-Match: W/"27409480"`, which means no blind overwrite. **HTTP 204**, `modifiedon` **2026-10-10T15:12:08Z**. Identity: the operator's az CLI (`ralph.schroeder@spaarke.com`) |
| Read-back | Byte-identical to the intended text (10,726 chars; SHA-256 `51D9A572...BB1FE6`, saved as `row-readback-after-074-input-fix.json`) |
| Only-change proof | Against the 13:33Z backup: all **79** leaf values outside `examples[*].input` are identical; all **8,469** characters outside the 4 input spans are byte-identical; top-level key order is unchanged; each new string parses back to its 13:33Z object |

### 8.2 Render check before the run (same path, OpenAI mocked, 0 model calls)
Real `CommunicationTriageAi` → `ActionResolver` → `ActionRunner` → `LookupChoicesResolver` → `PromptSchemaRenderer`
against the written row: **no fallback warning**, `## Allowed values for 'category'` present with **both** tie-breaker
lines (`Fee / rate change — The PRICE of work ...`, `Scope / budget change — The AMOUNT of work ...`), the `## Constraints`
and `## Examples` sections rendered, and no raw-JSON prompt (`"$schema"` absent).

### 8.3 Run 2 numbers: recall on a synthetic set, same fixture, key and harness as run 1

The harness and fixture are unchanged since run 1's code (`git diff 04a27d9c5 HEAD -- tests/` is empty). Run at
2026-10-10T15:27Z: **184 model calls**, **0 warnings** (every triage on the JPS path), every item signalled and triaged.
Full output: `notes/074-run2-results.json`.

| Measure | Run 1 (2026-10-09, flat text, no guidance) | **Run 2 (2026-10-10, JPS + guidance)** | Gate |
|---|---|---|---|
| **Predicate recall** (fee-or-scope) | 12/55 = 21.8% (12.9-34.4%) | **19/55 = 34.5% (23.4-47.7%)** | ≥ 80%: **FAIL** |
| Strict recall, Fee / rate change | 4/27 = 14.8% | **7/27 = 25.9%** | reported |
| Strict recall, Scope / budget change | 8/28 = 28.6% | **11/28 = 39.3%** | reported |
| Predicate precision | 12/12 = 100% | 19/20 = 95.0% (one false positive: L018, owner label Scheduling, Claude label Scope, a disputed item) | information |
| Strict precision, Fee / Scope | 100% / 100% | 100% / 84.6% | information |
| Exact accuracy (10 categories) | 40/92 = 43.5% | 45/92 = 48.9% | information |
| Owner-labelled / Claude-labelled positives | 1/10 · 11/45 | 2/10 · 17/45 | |
| Worst case for the key (missed Claude-labelled positives treated as negatives) | 12/21 = 57.1% | 19/27 = **70.4%** | still FAIL |

Run 2 misses (36): Fee → Invoice / Billing 12, Administrative 4, Client instruction 3; Scope → Client instruction 8,
Administrative 7, Court / Filing 1, Scheduling 1. Nothing was relabelled.

### 8.4 Why it still fails: the classifier is anchored on rung 5, and rung 5 has no fee/scope vocabulary

Recall by the **upstream** rung-5 free-form category (positives only):

| Rung-5 category | Positives | Run 1 caught | Run 2 caught |
|---|---|---|---|
| `general-correspondence` | 31-32 | 5 | **8 / 31 (26%)** |
| `invoice` | 15 | 1 | **3 / 15 (20%)** |
| money-specific (`budget-request`, `fee-proposal`, `billing-rate-update`, `budget-approval`, ...) | 8 | 6 / 8 | **8 / 8 (100%)** |

- TRIAGE-EMAIL's first constraint reads: *"Map the classification's freeform category (e.g. 'court-notice', 'invoice',
  'general-correspondence') onto the CLOSEST matching entry in the allowed category list"*. Its output-schema
  description says the same. It is designed as a **structuring pass over rung 5's decision** (FR-05, "no second
  classification").
- Rung 5 (`CommunicationClassificationAi`, a code-constant prompt with no taxonomy) labels **46 of 55** positives as
  `general-correspondence` or `invoice`. Its prompt's category examples are court-notice, invoice, esign-completion,
  scheduling and general-correspondence, with no fee/rate or scope/budget concept.
- When rung 5 *does* emit a money-specific category, triage catches it: 8/8 in run 2, 6/8 in run 1. When it does not, triage, told to map the
  rung-5 category, mostly maps `invoice` → Invoice / Billing and `general-correspondence` → Client instruction /
  Administrative. The 072 guidance raised recall by 12.7 points, but it acts only inside a mapping step told to defer
  upstream.

This is an observation from one run at temperature 0.2. The mechanism is consistent across both runs and both
categories.

### 8.5 D-49 values (overwritten with run 2, read back)

| Row | `sprk_measuredrecall` | `sprk_labelledsetsize` | `sprk_recallmeasuredon` |
|---|---|---|---|
| Fee / rate change | **25.93** | 92 | 2026-10-10 |
| Scope / budget change | **39.29** | 92 | 2026-10-10 |

HTTP 204 each; read back at `modifiedon` 15:28:08Z. Same semantics as §6.5: strict per-category recall; the gate figure is in this note.

### 8.6 🔔 Escalation: owner decisions (POML trigger: "improve the classifier ... or re-scope", both owner decisions)

The two runs bracket the problem. Fixing the prompt mechanics (run 1 → run 2) is necessary but worth only about 13
points. The remaining gap is classifier design. Options, cheapest first. **None has been taken; each changes the email
project's classifier**:

1. **Let TRIAGE-EMAIL classify from the message, with rung 5 as a hint.** Rewrite constraint 1 and the `category`
   description so the message text and the Allowed-values definitions decide, with the rung-5 category as advisory
   only. It is a data-only change to the Action (no code; FR-05's "no second classification CALL" still holds). This
   targets the 46/55 anchored misses directly.
2. **Give rung 5 the vocabulary.** Add fee/rate and scope/budget concepts to `CommunicationClassificationAi`'s prompt
   (code change in `Services/Ai/PublicContracts/`; BFF hygiene applies).
3. **Model tier.** Both stages run on `gpt-4o-mini` (Fast). A Standard-tier triage may follow the tie-breakers better.
   This changes cost.
4. **Re-scope the §0 claim** if none of the above clears 80% within the owner's budget.

Recommendation: **option 1** first (data-only, cheap, targets the measured mechanism), verify the render, then one
re-measure (~184 calls). Run 2's D-49 values stand until then.

**Model calls, all of task 074:** run 1 185 (incl. aborted attempt 0) + run 2 184 = **369**. Render checks: 0.

## 9. 2026-10-10: D-117 part (a). TRIAGE-EMAIL decides category from the email text (data only)

**Owner decision D-117** (`notes/decisions.md`): both classification steps read the one editable taxonomy
(`sprk_triagecategory` name + guidance). (a) TRIAGE-EMAIL decides from the email text against the category
definitions, with rung 5's category as a hint only; this section, data only. (b) Rung 5 reads the same taxonomy;
lane-rung5, BFF code, separate PR to master. Run 3 happens only with both live.

### 9.1 What changed: category-only carve-outs, 8 strings

The literal brief named constraint 1 and the category description. The anchoring also sat in the role, the task,
constraint 6 ("do NOT re-run independent classification ... you are STRUCTURING ..."), both `input` descriptions, and
the structured-output schema's own `category` description, which the model also receives. Changing only the first two
would have left the prompt contradicting itself, and run 3 would have measured that contradiction. Each string below is
limited to the **category** decision. Rung 5 stays the primary signal for priority, obligations and review outcome.
FR-05 holds: one model call, no second classification call. No fee/scope words are added beyond the live guidance.

| Location | Change in substance |
|---|---|
| `sprk_systemprompt` `instruction.role` | "Your job is NOT to re-classify ... reconcile it" → "For the CATEGORY, you decide from the email text itself, judged against the firm's category definitions ...; the upstream freeform category is a hint only." |
| `instruction.task` | category = "the ONE entry ... whose definition best fits the email text ... apply the category definitions, including their tie-breakers; use the classification's freeform category only as a hint" |
| `instruction.constraints[0]` (constraint 1) | "Map ... onto the CLOSEST matching entry" → "Choose category by reading the email text ... against the definition of each entry ..., including the tie-breakers ... The classification's freeform category ... is a HINT only: when it conflicts ..., the email text and the definitions decide." |
| `instruction.constraints[5]` | Prefixed "Apart from category ..."; the STRUCTURING rule now applies to priority, obligations and reviewOutcome |
| `input.classification.description` | REUSE for urgency/obligations/actions/rationale; the category is a hint |
| `input.message.description` | "the primary evidence for the category decision" (was "ONLY as supporting grounding ... never ... re-classify") |
| `output.fields[category].description` | "the ONE entry ... whose definition best fits the email text. The classification's freeform category is a hint only." |
| `sprk_outputschemajson` `properties.category.description` (row only; not version-controlled) | same text as the field description above |

### 9.2 Mirror and row (evidence)

- **Mirror:** PR **#1602** (to master), commit `38cd9977b` on `fix/triage-email-example-inputs-string`. **7+/7-**
  lines; exactly the 7 intended leaves changed and the other 91 are untouched (checked in code). The 29 triage tests pass.
  Commented on the PR; the coordinator's title is kept.
- **Row backup** (current at 15:12:08Z, ETag `W/"27409778"`, identical to the §8.1 read-back): scratchpad
  `triage-fix-074/row-backup-before-d117.{systemprompt,outputschema}.json` (SHA-256 `51D9A572CBD6...`,
  `BA10900FF379...`).
- **Write:** one `PATCH` of both columns with `If-Match: W/"27409778"`, HTTP **204**, `modifiedon` **2026-10-10T15:47:57Z**,
  operator identity (az CLI).
- **Read-back:** byte-identical to the intended values (prompt SHA `B6601F7789AD...`, schema `8986E76AFF66...`).
- **Only-change proof:** leaf diff against the backup is 7 of 83 prompt leaves and 1 of 25 schema leaves, exactly the
  intended ones. **Reversing the edits on the read-back reproduces both backups byte-for-byte.** The new prompt equals
  the PR #1602 mirror minus `$comment*` and the 6 deploy scalars.

### 9.3 Render check (same path as §8.2, OpenAI mocked, 0 model calls)
JPS path with no fallback; `## Allowed values for 'category'` with both tie-breaker lines; the new role and constraint 1
text present; the old "onto the CLOSEST matching entry", "NOT to re-classify" and "you are STRUCTURING" (constraint 6)
phrasing absent. The schema actually sent to the model carries the new category description, without the old one,
plus the live `$choices` enum.

**Not re-measured yet** (dispatch): run 3 waits for lane-rung5's rung-5 change, applied to this harness worktree only
for the run.

### 9.4 `sprk_outputschemajson` before/after (NOT version-controlled, so recorded here verbatim for reproducibility)

Row `sprk_analysisactions(c1fa96bf-2697-f111-b8dc-7ced8ddc4a05)`, spaarkedev1. **One** leaf changed:
`properties.category.description`. Reversing it on the after-text reproduces the before-text byte-for-byte (§9.2).
Not in the repo (mvp-technical-spec §16.3); the coordinator is logging that as a separate issue.

**Before** (as of 15:12:08Z; SHA-256 `BA10900FF37948BFA14B4115BB4A94ED89C660C2406F0510D2BF2E249F25983E`):

```json
{"$schema":"http://json-schema.org/draft-07/schema#","type":"object","additionalProperties":false,"required":["summary","obligations","category","priority","reviewOutcome"],"properties":{"summary":{"type":"string","description":"EXACTLY 2 lines (two short sentences) summarizing what this email is and what it requires, for a reviewer scanning a triage queue.","maxLength":320},"obligations":{"type":"array","description":"Concrete, human-readable action-phrase obligations grounded in the classification's obligations/suggestedActions signal and the message text (e.g. 'Respond to court by Friday, Aug 15'). Empty array when none apply — never fabricated.","items":{"type":"string","maxLength":200},"maxItems":8},"category":{"type":"string","description":"The triage category, mapped from the classification's freeform category onto ONE entry of the firm's Dataverse-configured taxonomy (sprk_triagecategory). Never invent a value outside the allowed list.","maxLength":100},"priority":{"type":"string","description":"Triage priority, derived from the classification's urgency signal and the mapped category's severity. One of the firm's configured priority levels (sprk_communication.sprk_triagepriority).","maxLength":20},"reviewOutcome":{"type":"string","description":"Suggested initial review routing for a human reviewer to confirm or override — never a final auto-applied decision. One of the closed review-outcome set (sprk_communication.sprk_reviewoutcome; D-05 — 'review outcome', not 'disposition').","maxLength":20}}}
```

**After** (written 15:47:57Z, read back byte-identical; SHA-256 `8986E76AFF66CD0F5EA99DACE39C42AADAF23AA1883C61F15835B68166EC8304`):

```json
{"$schema":"http://json-schema.org/draft-07/schema#","type":"object","additionalProperties":false,"required":["summary","obligations","category","priority","reviewOutcome"],"properties":{"summary":{"type":"string","description":"EXACTLY 2 lines (two short sentences) summarizing what this email is and what it requires, for a reviewer scanning a triage queue.","maxLength":320},"obligations":{"type":"array","description":"Concrete, human-readable action-phrase obligations grounded in the classification's obligations/suggestedActions signal and the message text (e.g. 'Respond to court by Friday, Aug 15'). Empty array when none apply — never fabricated.","items":{"type":"string","maxLength":200},"maxItems":8},"category":{"type":"string","description":"The triage category: the ONE entry of the firm's Dataverse-configured taxonomy (sprk_triagecategory) whose definition best fits the email text. The classification's freeform category is a hint only. Never invent a value outside the allowed list.","maxLength":100},"priority":{"type":"string","description":"Triage priority, derived from the classification's urgency signal and the mapped category's severity. One of the firm's configured priority levels (sprk_communication.sprk_triagepriority).","maxLength":20},"reviewOutcome":{"type":"string","description":"Suggested initial review routing for a human reviewer to confirm or override — never a final auto-applied decision. One of the closed review-outcome set (sprk_communication.sprk_reviewoutcome; D-05 — 'review outcome', not 'disposition').","maxLength":20}}}
```
