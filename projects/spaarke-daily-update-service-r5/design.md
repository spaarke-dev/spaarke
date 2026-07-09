# Spaarke Daily Update Service R5 — Design Charter

> **Status**: DRAFT v0.2 — 2026-07-08, operator review complete (§6 resolved) → ready for `/design-to-spec`
> **Authors**: R7 W12 UAT feedback capture (operator, 2026-07-01/02, verbatim in [`notes/inbound-from-r7/`](notes/inbound-from-r7/)) + Claude Fable 5 drafting with post-r1-close reconciliation
> **Predecessor**: [`spaarke-daily-update-service-r4`](../spaarke-daily-update-service-r4/) (hallucination Round 1) · R7 W12 widget cutover (Round 2a prompt tightening)
> **Scope ruling (operator, 2026-07-08)**: ALL substantive Daily Briefing work was REMOVED from `spaarke-ai-architecture-redesign-r2` — **this project owns the entire briefing surface**, including hallucination remediation. There is no r2-core "Wave 0" briefing fix wave.
> **Authoritative context**:
> - The six inbound notes: [`01 hallucinations/determinism`](notes/inbound-from-r7/01-llm-hallucinations-and-determinism.md) · [`02 monitored-for schema`](notes/inbound-from-r7/02-monitored-for-schema.md) · [`03 code-review follow-ups`](notes/inbound-from-r7/03-code-review-followups.md) · [`04 latent bugs`](notes/inbound-from-r7/04-latent-bugs.md) · [`05 deploy governance`](notes/inbound-from-r7/05-deploy-safety-governance.md) · [`06 choice coercion`](notes/inbound-from-r7/06-choice-field-coercion-in-updaterecord.md)
> - Platform as-built: [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../../docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) v0.5 · ADR-039/ADR-040 (Accepted) · r2 core charter [`design.md`](../spaarke-ai-architecture-redesign-r2/design.md) (coordination only — see §6)

---

## 0. What changed since the notes were captured (2026-07-01 → 2026-07-08)

The six notes pre-date the redesign-r1 close. Four ground-truth shifts materially change their recommendations — this charter reconciles them so `/design-to-spec` doesn't build against deleted surfaces:

1. **Daily Briefing is now a CODED composite, not a playbook.** redesign-r1 task 043 rebuilt it as `DailyBriefingCompositeService` (`ICodedWorkflow`); the `DAILY-BRIEFING-NARRATE` playbook (`7b5a6ed3…`) was **deactivated** by task 050. Note 01's "simplify the playbook's narrator nodes" translates to: **simplify the coded workflow** — drop per-channel LLM narrate calls, keep the TL;DR call. This makes Track A *cheaper* than the note estimated. The `BRIEF-NARRATE-CHANNEL` / `BRIEF-NARRATE-TLDR` Action rows are still live catalog data.
2. **The canvas playbook builder is GONE.** redesign-r1 task 053 de-scoped PlaybookBuilder to a BA *catalog* editor (Actions + Bindings); task 050 deleted the builder endpoints incl. the Wave-3 schema endpoint note 06's Track A depends on. Note 06's "authoring-time gate in the canvas" has **no surface to land on** — the durable fix re-targets runtime coercion + `jps-validate` (D-4).
3. **Groundedness checking was silently OFF.** The 2026-07-08 safety-perimeter fix (PR #567) found `GroundednessCheckService` attached **no auth header** — every groundedness check 401-failed-open. Part of why hallucinations reached users despite the service existing. It is fixed; r5 is the first consumer of real scores (D-2).
4. **The note-05 deploy-race class is structurally mitigated.** Since 2026-07-08 the Deploy SpaarkeAi pipeline works (first green in its history) and deploys **master only**; BFF likewise auto-deploys master via Environment Promotion. The 2026-06-30 overwrite incident cannot recur between merged states; the master-sync-first rule remains for manual worktree deploys (D-6).

Also inherited as baseline (do NOT re-scope): everything in the notes' "What R7 already delivered" list, plus r1's per-tenant metering (task 054 — briefing runs are telemetered on the coded entry path) and the golden-utterance eval gate.

---

## 1. Problem statement

R4 (temperature 0, grounding instruction, `EntityNameValidator`) and R7 W12 Round 2a (PAIRING RULE, GROUNDING CHECK, AGGREGATION/STRUCTURAL PREFERENCE prompt tightening) are **two rounds of instruction-based mitigation — and the operator still caught a fabricated pairing in UAT**: a bullet asserting *"'Follow up with client' related to CMRCL-680582 [2]"* where the title, the regarding record, and the citation came from **three different items**. The r1 redesign proved the same lesson on the write side: **prompt steering is the weakest enforcement layer; where correctness matters, mechanize**. R5 applies that doctrine to briefing *rendering*: item-level facts become deterministic; the LLM keeps only the abstraction layer it's good at — and that layer gets a working groundedness gate and an eval family.

Around that core, R5 carries the accumulated briefing backlog: the Monitored-For semantic schema (the operator's "note it for next project"), the R7 code-review debt, one latent OData-casing bug, and the Choice-coercion trap that 500s Update Record writes.

---

## 2. Delivered product (user terms — acceptance backbone)

| Gate | The user can now… |
|---|---|
| **G-R5-A (Trustworthy briefing)** | Open the Daily Briefing and every item-level line — title, sender, date, regarding record, link — is **fact-derived from the source record** and always correct (click any link: it matches the text). The TL;DR remains a synthesized 2–3 sentence summary (counts, themes, top action) built on **deterministically-computed facts** — it never asserts a data point (count, date, record) that isn't computed from source, and never names an item it can't anchor (non-resolving anchors are dropped). Zero cross-item pairing is *structurally impossible*, not prompt-discouraged. |
| **G-R5-D (Appealing briefing)** | Open the Daily Briefing and it *looks like a product someone designed* — visual hierarchy, spacing, typography, section rhythm, and reason/priority affordances are deliberate and pleasant, not the current flat/unappealing layout. Redesigned via the `/prototype` workflow (Fluent v9 + ADR-021 tokens, light + dark), operator-approved in the harness before production wiring. |
| ~~**G-R5-B (Monitored-For)**~~ | **DEFERRED (operator, 2026-07-08)** — prerequisites + other considerations; not the material issue. Re-scoped to a later round (see D-3, §6 Q3/Q4). |
| **G-R5-C (Hardening)** | Invisible but binding: the collaborator-scope fix (assigned attorneys see their matters again), Choice-field writes that stop 500ing, tests over the fragile client surfaces, and the collector de-duplicated. |

Browser rule carried from r1 verbatim: gates are operator-executed browser UAT on spaarkedev1; curl/tests/logs never satisfy them.

---

## 3. Core decisions

### D-1. Fully-deterministic Activity Notes (note 01 **Track A** — adopted)

**Decision**: Channel sections render **deterministically** from `items[]` — each row composed from known-safe source-record fields (title/subject, party, date, flags, reason chip, regarding name + server-composed link). **Zero LLM involvement in item-level rendering.** The LLM keeps: TL;DR abstract (counts + themes + top action, never item titles), and 3–5 aggregated key takeaways. `BRIEF-NARRATE-CHANNEL` is **retired** (catalog-data change; eval cases updated per NFR-06); `BRIEF-NARRATE-TLDR` survives. The coded composite simplifies: collect → assemble view model → ONE TL;DR LLM call → render (fewer LLM calls, faster, cheaper — also visible in the task-054 metering).

**Why Track A over Track B** (structured-schema bullets + widget validation): the operator's own framing — *"should these be more deterministic statements where pull from specific fields?"* — plus the decisive evidence that two rounds of prompt engineering did not close the class. Track B remains documented as the fallback posture if UAT judges the deterministic prose too flat (§7 Q2), and D-2's tuple validator is Track B's core idea applied to the one surface that stays LLM. The widget precedent exists: `HighPrioritySection` mini-report cards (R7 W12) are already this pattern — channel sections follow it.

**What we lose + mitigation** (from the note, accepted): cross-item synthesis lives ONLY in the TL;DR/takeaways; deterministic rows may be styled prose-like ("Contract review for Smith Industries due 4/28") without losing determinism.

### D-2. TL;DR stays LLM — but on a 100%-accuracy standard, not a probabilistic gate (operator reframe, 2026-07-08)

**Operator ruling**: there is **no groundedness threshold**. We cannot tell a user they have tasks, due dates, or new records unless it is 100% accurate — whether a data point exists is not a probabilistic question. The LLM does not *decide* what exists; it surfaces the most important of the already-true items and adds narrative. Correctness is **by construction, not by score**.

Three layers on the one surface that remains generated:
1. **Deterministic factual scaffolding**: every fact the TL;DR rests on — counts, dates, record names, "you have N …" — is **computed deterministically from the source records and handed to the LLM as ground truth**. The LLM composes the 2–3 sentence prose and prioritizes; it never introduces a fact of its own.
2. **Binary anchor resolution** (replaces the old threshold policy): TL;DR + takeaways emit against a schema (counts/themes/action slots + `itemRefs[]`); any named anchor must carry an `itemId` that resolves against `items[]` — the widget **drops** non-resolving anchors. There is no warn/withhold band. `GroundednessCheckService` is **demoted** from a user-facing gate to an eval/telemetry signal only (it never decides what the user sees).
3. **Eval family** (note 01 test cases, formalized): mixed-item corpus / aggregation-preference / grounding round-trip / TL;DR-abstraction cases join the golden-utterance suite as merge gates (NFR-06) — this is how the 100% standard is *proven*. The deterministic channel renderer gets *unit* tests instead — that's the point.

### D-3. Monitored-For schema (note 02 — **DEFERRED, operator, 2026-07-08**)

**Deferred to a later round.** There are prerequisites and other considerations to work through, and this is not the material Daily Briefing issue — R5 focuses on accuracy (and now appearance, D-8). The design below is preserved as the starting point for that future round; **nothing here is built in R5**, and no `sprk_monitor*` schema is deployed.

<details><summary>Preserved future-round design (not in R5 scope)</summary>

Global Choice `sprk_monitorreason` + `sprk_monitornotes` (Memo) on the 7 entities (**Primary + Notes** model); retire `sprk_monitor` with backfill-to-`Other` in a follow-up release after validation. Value list, change-tracking-per-entity, and the reason-chip rendering all move with it. Deploy via `dataverse-create-schema`; backfill script one-time.

</details>

### D-4. Choice-coercion fix (note 06 — **re-targeted**: runtime + validator, canvas track obsolete)

Note 06's Track A (canvas authoring gate) has no surface — the canvas is deleted (§0.2). Re-ruled ordering:
- **Primary — Track B runtime coercion**: `UpdateRecordNodeExecutor.CoerceFieldValue` gains metadata-driven coercion — when the mapping type is `string` but the target column is Choice/Boolean/Number, resolve via cached column metadata (label→option value, case-insensitive) instead of 500ing; unmatchable labels fail loud with the label + valid options in the error. This is **hardening a frozen-engine executor, not new capability** — permitted under the freeze (defect class, not feature).
- **Secondary — authoring-time check in `jps-validate`**: Step 7.7 already validates node `sprk_configjson` against `GetConfigSchema()` from source; extend it to flag `type:"string"` mappings whose target column is Choice (the cheap authoring gate, no UI needed).
- **Sweep**: audit existing playbook nodes' fieldMappings for the pattern (note 06 test case 3/4 list); restore `sprk_documenttype` to the Profile Document node once coercion ships.

### D-5. Tech-debt sweep (notes 03 + 04 — one bounded phase)

The five note-03 items + the note-04 one-liner, with one **critical addition** the notes couldn't know: item 1 (revert the collector's membership-resolver bypass) **must re-flip the test that pins the bypass** — `DailyBriefingCollectorTests.CollectAsync_OwnershipGate_UsesOwnerScopedQueryExpressions_ResolverBypassed` was rewritten 2026-07-08 (PR #558) to *deliberately* assert the bypass so that reverting it forces this exact test conversation; the revert re-asserts resolver routing + adds the collaborator smoke test (`sprk_assignedattorney1` user sees an assigned, non-owned matter). Also in the sweep: jest for `NarrativeCitedText.buildSegments` / `classifyDueDate` / `isEmptyResponse` / `useInlineTodoCreate`; collapse the 7 `QueryHighPriority*` helpers into one spec-driven method; Promise-cache the primary-contact lookup; fix the truncation comment; the `EventDetailSidePane/TodoSection.tsx:233` PascalCase `@odata.bind` one-liner + the repo-wide grep audit; and the **OData naming convention** documented in `docs/standards/` ("binding a lookup → PascalCase SchemaName; filtering/selecting → lowercase LogicalName").

### D-6. Deploy governance (note 05 — disposition: adopt-the-rule, reject-new-mechanism)

The incident class is structurally mitigated: both deploy pipelines now ship **master only** (§0.4), so merged work cannot be overwritten by a stale bundle. Retained as binding project convention: **master-sync-first before any manual worktree deploy** (the note's 5-step sequence), plus the `projects/INDEX.md` hot-path check. The "reserved deploy window" flag-file mechanism is **rejected** — post-2026-07-08 it solves a problem the pipeline change already solved. Any new deploy script warns when the branch is behind origin/master (cheap, one-line check).

### D-7. Not re-litigated

R4/R7's shipped decisions stand (temperature 0, `EntityNameValidator`, mini-report cards, rotating emoji, prompt-tightened Action rows remain until D-1 retires the channel row). The briefing stays on the **coded-composite + catalog-Action** architecture — no new playbooks, no new dispatch mechanisms, no manifest tables (ADR-039 posture).

### D-8. Visual redesign via `/prototype` (operator add, 2026-07-08 — co-headline with D-1)

**Decision**: the Daily Briefing UI is **redesigned**, not just made accurate. The current layout is flat and unappealing; R5 treats visual quality as a first-class deliverable (gate **G-R5-D**). Design happens **in the prototype harness first, production wiring second**:

1. **Prototype in `spaarke-prototype`** — scaffold a harness for the briefing widget (`prototype-harness-setup` for a production component: the `Spaarke.DailyBriefing.Components` shared lib) with HMR + mocked briefing data (`prototype-harness-extend` for any entity factories/presets needed). Iterate on hierarchy, spacing, typography, section rhythm, priority/reason affordances, empty/loading/error states, light **and** dark — against realistic mixed-item data (reuse the D-2 eval corpus as harness fixtures so design and accuracy share ground truth).
2. **Operator-approved in the harness** before any production change — screenshots / live harness review is the sign-off, mirroring the browser-UAT rule.
3. **Port to production** — the approved design lands in the shared lib components (`HighPrioritySection`, channel sections, TL;DR block) under Fluent v9 + **ADR-021 design tokens** (no hard-coded colors; dark-mode verified). Determinism (D-1) and anchor resolution (D-2) are preserved — redesign changes presentation, never the fact contract.

**Constraint (CLAUDE.md §11 reuse-first)**: redesign **extends the existing** `Spaarke.DailyBriefing.Components` surface — no parallel component tree. Net-new components only where an archetype in `BUILD-A-NEW-WORKSPACE-WIDGET.md` genuinely has no home for the pattern. **Non-goal**: no new widget framework, no layout-engine work, no scope beyond the briefing's own surfaces.

**Sequencing note**: D-8 design can proceed in parallel with D-1/D-2 backend work (harness uses mocked data), but production port lands **after** D-1's deterministic view-model is stable, so the redesign renders against the final data shape — not the retired channel-narration shape.

---

## 4. Explicit non-goals

- **No LLM channel narration rescue attempts** — Round 3 of prompt engineering is not a track; Track A is the decision (fallback = §7 Q2 ruling, not silent drift).
- **No new briefing entry paths** — the coded/event path stands as r1 left it.
- **No Compose/assistant scope** — Compose r2 and the r2 core own theirs; r5 touches only briefing surfaces (+ the two cross-cutting fixes D-4/D-5 explicitly listed).
- **No Monitored-For schema work in this release** — the entire `sprk_monitorreason`/`sprk_monitornotes` scope (D-3) is deferred to a later round per operator ruling (2026-07-08).
- **No groundedness threshold / warn-withhold band** — rejected per the D-2 operator reframe; correctness is by construction (deterministic facts + binary anchor resolution), not by score.
- **No EventDetailSidePane fix** — the side-pane is not currently in use; its `@odata.bind` one-liner is deferred (the repo-wide OData grep audit still runs and fixes any *in-use* occurrences).
- **No new widget framework or layout engine** (D-8) — the redesign extends the existing `Spaarke.DailyBriefing.Components` surface only; it is a visual/UX redesign, not an architecture change.

---

## 5. Constraints, hot paths, ADR posture

- **Hot-path declaration**: <hot-path-declaration> BFF=**Y** (`Services/Ai/Narrators/DailyBriefing*`, `UpdateRecordNodeExecutor`, DTO/endpoint touches) · SpaarkeAi=**Y** (`Spaarke.DailyBriefing.Components` shared lib + `EventDetailSidePane`) · ci-workflows=**N** · skill-directives=**Y** (`jps-validate` Step 7.7 extension — main-session-only edit) · root-CLAUDE.md=**N** </hot-path-declaration>
- **Coordination with the r2 core** (its worktree is active): briefing work was removed from r2 by operator ruling (header). r5's BFF surface is the briefing-specific slice of `Services/Ai/` (Narrators/DailyBriefing*, the frozen-engine executor fix); the r2 core owns the shared internals (gate engine, Binder, Memory, Completion). Register both in `projects/INDEX.md`; `/conflict-check` before waves; if r2's Completion/OutcomeCard contracts land first, the briefing's action buttons adopt them opportunistically — **not a dependency**.
- **Frozen-engine rule**: D-4's executor change is defect-hardening, explicitly justified against the "no new capability on the engine" rule (state in PR description).
- **Catalog governance**: retiring `BRIEF-NARRATE-CHANNEL` + editing `BRIEF-NARRATE-TLDR` follow mirror-first authoring + eval-case obligation (NFR-06) + `OpenAiFunctionSchemaValidator` rules; rows updated via BA editor/MCP (Seed-JpsActions is retired).
- **Binding ADRs**: 039/040 (Accepted), 013/037 (amended), 015/016 (briefing data tiers/budgets), 021 (Fluent tokens — reason-chip colors), 022, 024 (todo regarding), 029 (publish per-task verification; baseline 49.63 MB incl. PDBs), 032, 038 (test shapes — the sweep's new tests land at KEEP paths; TEST-MODIFYING rigor applies).
- **Schema work** via `dataverse-create-schema`; 7-entity column adds documented in `docs/data-model/`.
- **Telemetry**: NFR-07 identifiers-only holds; briefing runs already metered (054) — D-1 should visibly reduce per-run token counts (a measurable win; capture before/after in the gate evidence).

---

## 6. Operator decisions (RESOLVED 2026-07-08)

1. **TL;DR groundedness thresholds (D-2)** → **No thresholds.** We cannot report tasks/dates/records to a user unless 100% accurate; existence is not probabilistic. The LLM surfaces the most important already-true items and adds narrative only. D-2 rewritten to deterministic factual scaffolding + binary anchor resolution; `GroundednessCheckService` demoted to eval/telemetry signal.
2. **Deterministic-prose fallback (D-1)** → whatever preserves the 100%-accuracy standard; the structural guarantee is non-negotiable.
3. **Monitored-For value list (D-3)** → **Deferred** with all of D-3.
4. **Change tracking per entity (D-3)** → moot (D-3 deferred); no change tracking needed for Event/Todo regardless.
5. **EventDetailSidePane one-liner (D-5)** → **Deferred** — the side-pane is not currently in use.
6. **Visual redesign (D-8, operator add)** → **In scope, co-headline.** The briefing is unappealing today; redesign it via the `/prototype` harness workflow, operator-approved in the harness, then ported to the existing shared-lib components under Fluent v9 + ADR-021 tokens.

**Scope after these rulings**: R5 = **accuracy core** (D-1 + reframed D-2 + eval family) + **visual redesign** (D-8, `/prototype`-driven) + the **full D-4/D-5 hardening sweep** (Choice-coercion, collaborator-scope, collector de-dup, client-helper tests, `QueryHighPriority*` collapse, primary-contact cache, OData convention doc + grep audit) + D-6 deploy convention. The sweep is kept in one project deliberately so the debt items are not lost track of (operator, 2026-07-08). **Deferred**: all of D-3 Monitored-For, the EventDetailSidePane one-liner.

---

## 7. What /design-to-spec should produce

- FRs grouped by G-R5-A/B/C with **browser UAT scripts as acceptance criteria**; the note-01 test cases and note-02/06 test cases become FR acceptance lines.
- Phase shape (suggested): **Phase 0** quick fixes (OData convention doc + repo-wide grep audit) → **Phase A** deterministic Activity Notes + deterministic-fact TL;DR + eval family (D-1/D-2, the accuracy headline) → **Phase D (parallel with A)** `/prototype` visual redesign in the harness → operator sign-off → production port after A's view-model stabilizes (D-8, the appearance headline) → **Phase B** Choice-coercion + tech-debt sweep (D-4/D-5, EventDetailSidePane one-liner excluded) → wrap-up with `/test-diet` + `/defer`. (Monitored-For phase removed — D-3 deferred.)
- Rigor: code tasks FULL; `tests/**`-touching TEST-MODIFYING; schema tasks reference `dataverse-create-schema`; the `jps-validate` extension is a main-session task (skill-directive hot path).
- NFRs carried: eval-suite green merge gate; publish-size per-task verification; NFR-07; grep-zero retirement verification for `BRIEF-NARRATE-CHANNEL` consumers; ADR-021 dark-mode checks on widget changes.
- Before-and-after evidence obligations: token-count reduction per briefing run (054 metering), zero cross-pairing on the mixed-item corpus, collaborator-scope smoke.

---

*DRAFT v0.1 for operator review. On approval (and §6 answers), create the worktree (`/worktree-setup projects/spaarke-daily-update-service-r5`) and run `/design-to-spec projects/spaarke-daily-update-service-r5`.*
