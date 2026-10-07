# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-09-30 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first, then `design.md`. This began as a design/strategy project; on
> 2026-09-29 it also shipped **code fixes to the communication/notification path** (PR #1032), and on
> 2026-09-30 it took an **external architecture review** that corrected the central claim.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Phase** | Design — `design.md` **rev 3** reviewed and corrected; **not yet `/design-to-spec`** |
| **Branch** | `docs/ontology-platform-phase0` — **clean, fully pushed** through `d0b310c1e` |
| **Status** | in-progress, no blockers for the next step |
| **Next Action** | **Run the D-1 scoping spike** — `design.md` §8.1, timeboxed one day. `sprk_spendsnapshot` has **ZERO rows**, so the differentiated claim is untestable today. This gates every build item |
| **PR** | [#1032](https://github.com/spaarke-dev/spaarke/pull/1032) — `MERGEABLE`, CI **23 pass / 6 pending / 0 fail** at checkpoint. `BLOCKED` = required `Router` context awaiting its Tier 1 children |

### ⚠️ Three things that will bite a fresh session

1. **`origin/master` moves fast — 374 commits landed in one day.** Before ANY BFF deploy, merge master and
   confirm `git rev-list --left-right --count HEAD...origin/master` shows **0 on the right**. Deploying
   without it reverts other projects' merged work. (Done twice this session; expect to do it again.)
2. **The BFF unit suite takes ~15 minutes**, not 8. It will exceed a 600s foreground timeout. Run it with
   `run_in_background: true`. Last green: **13,044 passed / 0 failed / 54 skipped** post-merge.
3. **The swallow-and-log paths are undiagnosable without App Insights.** appId
   `6a76b012-46d9-412f-b4ab-4905658a9559`; `traces` carries `[comms-policy]` / `[comms-ri]`, `exceptions`
   carries the swallowed throws. That is how 2026-09-29's silent `InvalidCastException` was found, and it is
   the only way to find the next one.

### Read in this order — do NOT read all six

| # | File | Read when |
|---|---|---|
| 1 | **`design.md`** | **Always, first.** Rev 3 holds the decisions: §0 differentiation test · §1 the corrected claim · §5 scope + the §5.0 scope rule · §8 open decisions incl. CM-6..CM-11 · §8.1 the D-1 spike |
| 2 | `notes/defer-issues.md` | Before filing anything new. Three entries, all with GitHub Issue URLs |
| 3 | `notes/mvp-technical-spec.md` | For field-level detail. §0.3 · §3.4a (`Existence` body) · §11 (the corrected predicate + both failed attempts) · §17 (the eight fixes) |
| 4 | `notes/ontology-architecture-feedback.md` | The external review rev 3 applies. §2 (eight signal shapes) and §4 (worklist row contract) are material not yet absorbed anywhere else |
| 5 | `notes/ontology-component-model.md` | **Vocabulary — authoritative.** Read §3 before writing anything |
| 6 | `notes/mvp-synopsis.md` · `notes/phase0-codebase-inventory.md` | Scope narrative · verified codebase state |

### Critical Context

**The claim, as the predicate actually tests it** (Path B, CM-6):

> A communication on this matter was classified as a **fee or scope change** within the window, **and** no
> **budget revision** was recorded in that same window.

Two conjuncts, two sources. The spend snapshot is attached as **evidence**, not tested as a condition — the
rule asserts no comparison it did not compute.

---

## What happened 2026-09-29 — the RI loop executed for the first time

Four real emails through the capture path found **eight defects**, all fixed, and completed the
communication→notification loop for the first time ever. **Seven were in code shipped months earlier that
had never run its happy path.**

**Proof**: communication `99eb9b52` → task `sprk_event edfef460` → outbox `OUTBOX-001324`
(`kind=communication-assessed`, **the first ever out of 326 rows**) → appnotification `f0fef460`.

| # | Defect | Fix |
|---|---|---|
| 1 | Zero `sprk_communicationrule` rows → gate always fail-closed | rule row `dc423a8b` (data) |
| 2 | RI confidence was `urgency × agreement`, so an unfilable email scored **0** regardless of urgency | weighted sum `0.7×urgency + 0.3×agreement` + a regression guard on all four one-factor-zero cases |
| 3 | Threshold 0.8 unreachable; 0.35 would have authorized **~90%** of mail (242/270 lack a triage priority) | **0.45** |
| 4 | `sprk_regardingrecordtype` is a LOOKUP read as `string` → `InvalidCastException` swallowed by the NFR-05 guard, killing the RI action **while the logs read as success** | `ReadRegardingTypeLabel` reads by shape |
| 5 | `Deploy-ActionMirrors.ps1` couldn't deploy a JPS mirror and printed `UNCHANGED` | `Get-JpsSystemPrompt` + sidecar schema ownership |
| 6 | `DailyBriefingCollector` selected `sprk_eventdescription`, which **does not exist** → briefing blind to tasks. **The unit test pinned the bug** | → `sprk_description` |
| 7 | Tasks created `Draft(1)`; briefing filters `Open(659490001)`. **49 rows** stranded | `TaskActionCore` sets Open |
| 8 | RI tasks had **both** due-date fields null; task channels filter by date | due days **declared on the rule row** |

Also: prompt structure reordered **evidence-before-conclusions** in both the prompt *and*
`sprk_outputschemajson` (the schema is the lever); boundary examples 1→4; an `Unclassified` abstain row.

---

## What happened 2026-09-30 — the review corrected the central claim

`notes/ontology-architecture-feedback.md` found the rev-1 cross-source predicate contained **no budget term**
while its message asserted *"unreconciled against its budget"*. **Single-source, so it failed the §0 test it
was written to pass** — in the same spec section that says *"the join is the differentiator, not the
arithmetic."* **The join was what got dropped.**

Applied as `design.md` rev 3 and propagated to the spec and synopsis:

- **§0 inlined** into `design.md` (it was a dangling reference from decision 12, criterion 9 and §1.1), plus
  new **§0.3**: *a capability must TEST what its message CLAIMS.* Data being theoretically available is not
  the test.
- **§1's claim rewritten** to the two conjuncts Path B tests, with an explicit statement of what it does not
  assert.
- **Criterion 10 restored** (the effect loop) — criteria 1–9 covered detect/record/surface/suppress/tune/
  render and **not one required an effect**, which is also how an incumbent alerting product behaves.
- **Criterion 11 added** — a classifier-recall floor. The predicate is a conjunction and inherits its weakest
  input; at 70% recall the claim silently misses 30% of cases while every other criterion passes.
- **Off-by-one eliminated by class** — §8 now references scope items **by name**, not index.
- **Three new scope items**: `Existence` rule type (CM-7 — the predicate fitted no allowed body and §3.5
  refuses to save an invalid one, so the capability was **unsavable**), `sprk_budgetrevision` (CM-10 —
  verified necessary), the **Inquiry action** (CM-11 — restored).

### 🚩 The lesson worth carrying into the next review

**Two assertions in this project were satisfiable by the very defect they were meant to prevent:**

- Synopsis **criterion 8** read *"fires on evidence the e-billing system does not hold"* — the budget-blind
  predicate **would have passed it**, because an email *is* such evidence.
- The unit test that **pinned** `sprk_eventdescription`, a column that does not exist, staying green for
  months over a query that threw on every run.

Both were written one level too abstract. The question to ask of every criterion and every test:
**would this still pass if the implementation were wrong in the most likely way?**

---

## Decisions — do not re-litigate

| # | Decision |
|---|---|
| 1 | **Naming**: Spaarke Console · Spaarke Matter Management · Spaarke External Access · Connection Engine / Spaarke Connect |
| 2 | **Three engines**: Connection · Insights · Action — decoupled *by* the ontology. Agents are a **surface** |
| 3 | **CM-1**: Policy lives in the Insights Engine; signals are Insights outputs |
| 4 | **CM-5**: Inquiry → `sprk_servicerequest` with a direction discriminator |
| 5 | **CM-3**: rule body = a Dataverse filter; facts as rollup + calculated columns (zero C#) |
| 6 | **Ingestion = Option A** — our own worker on `UpsertMultiple` |
| 7 | **Console hosting**: web resource + `appid` + **`navbar=off`** |
| 8 | **Authority is post-MVP** — the human *is* the authority |
| 9 | **Action Engine is not in MVP** — the MVP needs an Action row, not the engine |
| 10 | **Terminology**: Spaarke Connect's existing entities. "Ledger" → **Decision Record** |
| 11 | **Connection Engine stays in this project** — harvest, don't fork |
| 12 | **§0 differentiation test is binding** (now inlined in `design.md` §0) |
| 13 | **Recall over precision for NOTIFYING, never for FILING.** Auto-file stays 0.85 — filing writes data, where a false positive is worse than a miss |
| 14 | **Policy knobs are DECLARED on the rule row**, options as fallback only |
| 15 | **LLM classifies; deterministic code decides; a human acts** |
| 16 | **CM-6 → Path B.** Path A would mean rewriting the claim to match a weaker implementation — backwards when the claim *is* the product |
| 17 | **CM-7 → `Existence` in scope.** Without it the differentiated capability cannot be saved |
| 18 | **CM-8 → deferred.** Adopt the eight-shape taxonomy as a *derivation method*, not a stored `sprk_detectionshape`; storing it needs a reader and `sprk_ruletype` already selects the evaluator |
| 19 | **No cross-clause variable passing in a rule body.** A bound reference needs two queries + correlation, breaking CM-3 and reintroducing one of the four properties that made node graphs hard. Clauses are independent, each windowed on *now* |
| 20 | **`sprk_budget.modifiedon` is NOT a substitute for revision history.** Any unrelated edit bumps it, so a stray edit suppresses a true signal — a false negative, against decision 13 |
| 21 | **§5.0 scope rule**: work goes where it is **cheapest**, not where it is topically pure — and **nothing is ever merely listed**; every item is fixed or scheduled |

---

## Next Actions — in order

1. **Run the D-1 spike** (`design.md` §8.1, one day). Four questions, with an exit condition: a matter
   carrying **both** an over-budget `sprk_spendsnapshot` **and** a scope/fee-classified communication. Until
   that pair exists, the differentiated claim cannot be built against anything real.
2. **Settle D-2, D-3, D-4**, and the deferred **`sprk_signal` shape** question — which per CM-9 now also
   covers resolution semantics (`sprk_dedupekey` alternate key, `sprk_resolutiontype`, `sprk_lastevaluated`,
   a resolution sweep). **Decide before the evaluator writes its first signal**; after that it is a migration.
3. **`/design-to-spec`** → `/project-pipeline` → `task-execute`.
4. **Merge PR #1032** once CI is terminal (watch for `Router`; Tier 2 is advisory and does not block).
5. Record the shape × binding-mode feasibility matrix (review §1.5) in component model §4.5/§4.7 —
   non-blocking. It is the concrete reason to ask for API access: **API access buys the obligation module;
   MCP alone does not.**
6. Run the §0 test retroactively across strategy-synopsis §8 Wave 2 / Wave 3 before any of those is specced.

### Open owner decisions

| ID | Decision |
|---|---|
| **D-1** | The spend-data spike (§8.1) — **the critical path** |
| **D-2** | Decision Record field list. `sprk_factsnapshot` **mandatory**; must not foreclose a **nullable** action ref |
| **D-3** | Worklist surface (Console / MDA / both) and row subject (matters or communications). Also sets the `spaarke-ai` hot-path flag |
| **D-4** | Does `sprk_memo` reuse `sprk_triagecategory`? *(recommend reuse)* |
| **D-5** | Is the MM connector in MVP? Export-only ≈2 months; API mirror adds ≈2 |
| **CM-2** · **CM-4** | Object-definition registry (post-MVP) · reuse "disposition" for an Inquiry's typed outcome (now load-bearing for criterion 10) |

### Filed and scheduled — NOT lost

| ID | Issue | What |
|---|---|---|
| ISS-001 | [#1048](https://github.com/spaarke-dev/spaarke/issues/1048) | `suggest-followups` running a stale prompt (repo mirror 1,058 chars longer than the live row). Another domain's to fix |
| ISS-002 | [#1049](https://github.com/spaarke-dev/spaarke/issues/1049) | `$choices` resolution degrades **silently** to the known 100%-null failure — the mechanism the whole taxonomy design rests on, with one Warning line as its only signal |
| ISS-003 | [#1050](https://github.com/spaarke-dev/spaarke/issues/1050) | 49 `sprk_event` rows stranded in Draft. **Not** bulk-updated — some may be genuine user drafts |

---

## Verified — do not re-verify

- The **full RI loop works end to end** (record ids above).
- `sprk_spendsnapshot` = **0 rows**; `sprk_budget` = 2; `sprk_billingevent` = 1. **No budget revision history
  exists** and auditing is not a fallback (component model §6: zero `RetrieveRecordChangeHistory`, no policy).
- `sprk_triagecategory` is a **table** (10 rows), resolved per run into the prompt *and* as a
  constrained-decoding enum → a new row is live on the next enrichment with **zero deployment**. Rows live
  **only in Dataverse**, never the repo. `sprk_enabled` **defaults to false** on create.
- `sprk_classifierguidance` populated on all 10 rows but **INERT** — `LookupChoicesResolver` reads
  `sprk_name` only (spec §14 step 2).
- Dataverse logical names carry **no underscores**. The MCP `update_table` tool derives the logical name from
  the display name and converts spaces to underscores, so it **cannot** produce a convention-correct
  multi-word column — use the metadata API (`POST EntityDefinitions/Attributes`) where `SchemaName` and
  `DisplayName` are independent. Three columns had to be recreated for this reason.
- `sprk_event` has `sprk_description` (**not** `sprk_eventdescription`); statuses Draft(1) / Open(659490001) /
  Completed(659490002) / Cancelled(659490004).
- Daily Briefing is **deterministic-query-based**, six channels, **no appNotification dependency**.
- Spaarke does **not** use OOB `task`/`activitypointer` — tasks are `sprk_event` (FAILURE-MODES **AP-14**).
- `sprk_signaltype` / `sprk_signalvalue` are **taken** — columns on `sprk_affinity`.

### Research completed (`.claude/agent-memory/researcher/`)

Fabric IQ · Foundry IQ + Work IQ · Entra Agent ID / Agent 365 / Dataverse audit · Dataverse ingestion
options. **Do not re-research.**

---

## Session Log

| Date | Work |
|---|---|
| 2026-09-19 | Phase 0 codebase inventory |
| 2026-09-21 → 24 | Strategy synopsis → v2.2 · component model · MVP synopsis · MVP tech spec · 4 research passes · Console hosting · differentiation test |
| 2026-09-25 | Committed + pushed all design artifacts |
| 2026-09-29 | Spec §10–17. **Eight defects found and fixed; the RI loop completed for the first time.** Prompt structure reordered. Taxonomy extended + guidance authored. 4 BFF deploys. Master merged (240 commits; AP-13 collision → ours renumbered **AP-14**). **PR #1032 opened** |
| 2026-09-30 | Three Dataverse columns renamed to convention. `design.md` **drafted → rev 2 → rev 3**. `notes/defer-issues.md` + **3 GitHub Issues** filed. External architecture review absorbed; spec + synopsis corrected. Master merged again (**374 commits**). 13,044 tests green |
