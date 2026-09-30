# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-09-29 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. This began as a **design/strategy project**; on 2026-09-29 it
> also shipped **code fixes to the communication/notification path** (see §"What changed 2026-09-29").

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Phase** | Design — MVP tech spec is concrete (~700 lines); **not yet `/design-to-spec`** |
| **Branch** | `docs/ontology-platform-phase0` — clean, pushed through `446e96c6c` |
| **Status** | in-progress, no blockers |
| **Next Action** | **Run `/design-to-spec`** on `notes/mvp-technical-spec.md`. It is now grounded enough (real field names, live-verified schema, a working end-to-end trace) that the earlier hand-waving risk is gone. See "Next Actions" for what shrank and what remains |

### ⚠️ Before the next BFF deploy

`origin/master` is **35 commits ahead** as of this checkpoint. Deploying this branch without merging master
first would revert other projects' merged work. Merge, confirm `git rev-list --left-right --count
HEAD...origin/master` shows **0 on the right**, then deploy.

### Read in this order — do NOT read all five

| # | File | Read when |
|---|---|---|
| 1 | `notes/mvp-technical-spec.md` | **Always.** §0 (differentiation test) · §10 (live-verified facts) · §11 (cross-source predicate) · §16–17 (defects found + fixed) |
| 2 | `notes/mvp-synopsis.md` | Scope, in/out, success criteria |
| 3 | `notes/ontology-component-model.md` | **Vocabulary — authoritative.** Read §3 before writing anything |
| 4 | `notes/phase0-codebase-inventory.md` | Verified codebase state (another session refined this 2026-09-25) |
| 5 | `notes/spaarke-ontology-strategy-synopsis-v2.md` | Strategy/market only (1,400 lines). **Component model wins on vocabulary conflicts** |

### Critical Context

**Consolidate from third-party systems → resolve → match against the customer's rules → record what was
decided.** Budget variance is the **plumbing proof, not the pitch** — e-billing vendors already do it. The
differentiated capability is the **cross-source** rule (spec §11): *a commitment with financial consequence
was made in correspondence, and the budget does not reflect it yet.* Leading, not lagging; and the join is
the differentiator, not the arithmetic.

---

## What changed 2026-09-29 — the seventh pipeline stage now executes

Four real emails were sent through the capture path. **Seven defects were found, all fixed**, and the
communication→notification loop completed for the first time ever.

**The proof**: communication `99eb9b52`, task `sprk_event edfef460`, outbox `OUTBOX-001324`
(`kind=communication-assessed` — **the first ever, out of 326 rows**), appnotification `f0fef460`.

| # | Defect | Fix |
|---|---|---|
| 1 | Zero `sprk_communicationrule` rows → gate always fail-closed to DENY | rule row `dc423a8b` created (data) |
| 2 | RI confidence was `urgency × agreement`, so an unfilable email scored **0** regardless of urgency | weighted sum `0.7×urgency + 0.3×agreement`; regression guard pins all four one-factor-zero cases |
| 3 | Threshold 0.8 unreachable under the sum; 0.35 would have authorized **~90%** of mail (242/270 have no triage priority) | **0.45** |
| 4 | `sprk_regardingrecordtype` is a LOOKUP read as `string` → `InvalidCastException` swallowed by the NFR-05 guard, killing the whole RI action **while the logs read as success** | `ReadRegardingTypeLabel` reads by shape |
| 5 | `Deploy-ActionMirrors.ps1` could not deploy a JPS mirror and printed `UNCHANGED` | `Get-JpsSystemPrompt` + sidecar schema ownership |
| 6 | `DailyBriefingCollector` selected `sprk_eventdescription`, which **does not exist** → the briefing was blind to tasks. **The unit test pinned the bug** (shape-only assertion) | → `sprk_description` |
| 7 | Tasks created as `Draft(1)`; briefing filters `Open(659490001)`. **49 rows** were stranded in Draft | `TaskActionCore` sets Open explicitly |
| 8 | RI tasks had **both** due-date fields null; briefing task channels filter by date | due dates **declared on the rule row** (`sprk_taskduedays`=1, `sprk_taskfinalduedays`=3) |

**Prompt-structure work also shipped** (spec §16.4): output fields reordered **evidence-before-conclusions**
in both the prompt *and* `sprk_outputschemajson` (the schema is the lever — reordering only the prompt is
cosmetic); boundary examples 1→4 covering the three-way money boundary; an explicit `Unclassified` abstain row.

**Taxonomy now has 10 rows**, incl. `Fee / rate change` (75) and `Scope / budget change` (85), plus
`sprk_classifierguidance` populated on all ten with contrastive tie-breakers.
⚠️ **The guidance is INERT** — `LookupChoicesResolver` reads `sprk_name` only (spec §14 step 2).

---

## Full State

### Decisions made — do not re-litigate

| # | Decision |
|---|---|
| 1 | **Naming**: Spaarke Console · Spaarke Matter Management · Spaarke External Access · Connection Engine / Spaarke Connect |
| 2 | **Three engines**: Connection · Insights · Action — decoupled *by* the ontology. Agents are a **surface** |
| 3 | **CM-1**: Policy lives in the Insights Engine; signals are Insights outputs |
| 4 | **CM-5**: Inquiry → `sprk_servicerequest` with a direction discriminator |
| 5 | **CM-3 closed**: rule body = a Dataverse filter; materialize facts as rollup + calculated columns |
| 6 | **Ingestion = Option A** — our own worker on `UpsertMultiple` |
| 7 | **Console hosting**: web resource + `appid` + **`navbar=off`** |
| 8 | **Authority is post-MVP** — the human *is* the authority |
| 9 | **Action Engine is NOT in MVP** — the MVP needs an *Action* (data), not the engine |
| 10 | **Terminology**: Spaarke Connect's existing entities. "Ledger" → **Decision Record** |
| 11 | **Connection Engine stays in this project** |
| 12 | **§0 differentiation test is binding** |
| 13 | **Recall over precision for NOTIFYING, never for FILING** (2026-09-29). Auto-file stays at 0.85; a false file is worse than a missed notification |
| 14 | **Policy knobs are DECLARED on the rule row**, with `CommsPolicyOptions` as fallback only — threshold and both due-date counts follow this pattern |
| 15 | **LLM classifies; deterministic code decides; a human acts.** A predicate evaluated by a model makes the Decision Record an anecdote |

### Next Actions — in order

**Recommended: `/design-to-spec` on `notes/mvp-technical-spec.md`.** What today changed about scope:

**Smaller than the spec assumes** — these are now demonstrably working, not hypothetical:
- classify → bounded category → triage persist (registry-bounded, zero-deploy taxonomy)
- the policy gate (rule match, scope, threshold, fail-closed)
- the whole notification spine (task + outbox + ping + appnotification) and Daily Briefing surfacing

**Still net-new, and this is the real MVP:**
1. **The cross-source predicate evaluator** (spec §11.4) — the one differentiated thing. Bounded set is
   `{Fee / rate change, Scope / budget change}` by row id; every join already exists.
2. **The Decision Record table** (spec §12) — `CommunicationRuleDecision` already carries the full decision
   on both paths and is thrown into `ILogger`. Persist it from the *consumer* (one line before the branch);
   the gate is not touched.
3. **The worklist** — a `sprk_gridconfiguration` row. `needs-review.gridconfiguration.json` is the working
   precedent; no new UI framework.
4. **`sprk_memo` as signal source #2** (spec §13) — human-authored, polymorphic across 15 parents, covers
   the commitment made on a call rather than in writing.
5. **`$choices` guidance injection** (spec §14 step 2) — makes the ten populated guidance rows live.

### Owner decisions still open

| ID | Decision |
|---|---|
| — | **`"Form D - 2023"` tokenizer** (spec §17.5a). `WellFormedTokenPattern` needs an alpha prefix *immediately* followed by `-`/`.`, so space-bearing matter numbers never tokenize and `ExplicitReference` never fires. The rung's own docstring gives the safety argument ("precision comes from the EXACT reverse lookup, not this pattern"). Changes the engine's precision/cost profile |
| — | **The false `reason` string** (spec §17.5b): *"Reinforced confidence 0.97 in [0.50, 0.85)"* — 0.97 is not in that band; it interpolates `topConfidence` while the band uses `topDeterministicConfidence`. AP-12 in *runtime-generated* prose |
| — | **`suggest-followups` is running a stale prompt** — repo mirror is 1,058 chars longer than the live row. Another project's domain; not deployed by this project |
| — | **Is the MM connector in MVP?** Export-only ~2 months; API mirror adds ~2 |
| — | **Which platform first?** · **Department or firm?** (departments → build; firms → bind) |
| CM-2 | Object-definition registry — only if the MCP server must describe itself |
| CM-4 | Reuse "disposition" for an Inquiry's typed outcome? |

### Verified this session (don't re-verify)

- The full RI loop **works end to end** — see "What changed" above for record ids.
- `sprk_triagecategory` is a **table**, resolved per run into the prompt *and* as a constrained-decoding
  enum. A new row is live on the next enrichment with **zero deployment**.
- Taxonomy rows live **only in Dataverse**, never the repo — by design.
- `sprk_enabled` **defaults to false** on `sprk_triagecategory` create.
- `sprk_event` has `sprk_description` (not `sprk_eventdescription`); statuses are Draft(1) / Open(659490001)
  / Completed(659490002) / Cancelled(659490004).
- Daily Briefing is **deterministic-query-based**, six channels, **no appNotification dependency**.
- Spaarke does **not** use OOB `task`/`activitypointer` — tasks are `sprk_event` (FAILURE-MODES **AP-14**).
- `sprk_signaltype`/`sprk_signalvalue` are **taken** — columns on `sprk_affinity`.
- BFF telemetry is in App Insights `spe-insights-dev-67e2xz` (appId `6a76b012-…`); `traces` carries the
  `[comms-policy]`/`[comms-ri]` prefixes and `exceptions` carries the swallowed throws. **This is the only
  way to diagnose the swallow-and-log paths.**

### Research completed (in `.claude/agent-memory/researcher/`)

Fabric IQ · Foundry IQ + Work IQ · Entra Agent ID / Agent 365 / Dataverse audit · Dataverse ingestion
options. **Do not re-research these.**

---

## Session Log

| Date | Work |
|---|---|
| 2026-09-19 | Phase 0 codebase inventory |
| 2026-09-21 → 24 | Strategy synopsis → v2.2 · component model · MVP synopsis · MVP tech spec · 4 research passes · Console hosting · differentiation test |
| 2026-09-25 | Committed + pushed all design artifacts |
| 2026-09-29 | Spec §10–17. **Seven defects found and fixed; the RI loop completed for the first time.** Prompt structure reordered. Taxonomy extended + guidance authored. 4 BFF deploys. `origin/master` merged (240 commits; AP-13 collision → ours renumbered **AP-14**) |
