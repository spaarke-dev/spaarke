# Lessons Learned — spaarke-ai-architecture-redesign-r1

> Written at wrap-up (task 090 step 6), 2026-07-08. Every claim below traces to
> `tasks/TASK-INDEX.md` annotations, `notes/g-p*-uat-round*-findings.md`,
> `notes/track-b-completion-audit.md`, `notes/goal-feature-evaluation.md`, or `current-task.md`.

## 1. What the project shipped

The ADR-039/040 architecture is live end-to-end: a budgeted agent-turn loop (per-turn budget 8,
deterministic pre-filter, citation enforcement — task 030) dispatching **closed catalogs**
(Action + Binding rows in Dataverse, boot-reconciled by health checks — tasks 003–005) through
**ONE confirmation gate** driven by declared side-effect class (031/037), with every execution
writing an addressable `{bindingId}@t{n}` ledger SessionOutput before rendering (021). Chat NL
hard-cut to the loop at 034; the dispatcher stack, legacy Chat/Tools, engine shells, and the
PlaybookBuilder canvas were deleted grep-zero (035/036/044/053 — Track B netted roughly −25k
LOC, 62 audit rows with zero unexplained survivors). New capabilities shipped as catalog data
(create-task, draft-correspondence, Daily Briefing as first coded composite), a BA catalog
editor replaced the canvas, and per-tenant metering counters + KQL pack landed at 054.

## 2. What worked

- **Parallel task-execute agent waves (max 6) with main-session verify/commit.** 51 tasks in
  ~3 days; the main session verified builds between waves and committed selectively, excluding
  files owned by still-running parallel agents (current-task.md wave protocol) — attribution
  stayed clean even when agents shared surfaces.
- **Pre-authored `/goal` wave conditions (NFR-10 pilot).** The goal-feature-evaluation.md verdict
  — "adopt at wave level, never at phase-gate level" — held exactly. Conditions demanding shown
  evidence + scope binds + turn caps kept autonomous waves honest; `/goal clear` before every
  browser gate preserved human judgment where it mattered.
- **Grep-zero-verified deletions with shown output (NFR-08).** The Track-B completion audit
  fresh-grepped all 62 rows rather than trusting batch-time evidence — and caught survivors the
  batches had missed (FallbackScopeCatalog lived until 050's Scope B). "Deleted" without shown
  grep output would have lied several times.
- **Hard-cutover doctrine (no parallel-run, no compat shims).** Every retirement was total
  (dual-path grep-zero at 020, intentHint end-to-end at 034), so no ghost path ever muddied a
  UAT diagnosis. The 037 eval expansion found loop-invoked write tools running UNGATED post-
  cutover — the cutover forced the defect into the open instead of letting the legacy gate mask it.
- **Six-round browser UAT at G-P3 caught what tests can't.** Model fabrication, confirm-resume
  silence, tab-restore races, relative-date hallucination ("tomorrow" → 6/13/2024), leaked
  model-facing instruction text — all invisible to 7,600+ green unit tests and a green eval gate.
  Empirical-Reproduction-FIRST (App Insights + transcript before any fix) kept round fixes honest.
- **The sub-agent `.claude` write boundary as a real safety net.** Every UAT fix wave ended with a
  "For the main session (.claude write boundary)" section; skill fixes (jps-action-create required-
  ban, seed-script pointers) were applied deliberately by the main session, never smeared in by
  parallel agents.
- **Catalog-data-first behavior change.** The G-P1 operator UX ruling ("auto-classify,
  chip-offered summarize") shipped primarily by editing two Binding rows — the second-product
  demo working as designed.

## 3. What hurt / would do differently

- **One catalog row 400'd the entire platform.** CREATE-TASK@v1's property-level
  `"required": true` (invalid JSON Schema) made Azure OpenAI reject EVERY loop turn (G-P3 round 1,
  finding 1). Projection-time schema validation (`OpenAiFunctionSchemaValidator`, per-tool
  exclusion, Degraded health dimension) should have existed from day 1 of "catalog rows become
  tool schemas" — closed catalogs need closed validation at the same boundary.
- **The model fabricated writes until honesty was pinned — in layers.** "Task has now been
  created" with no tool call (round 1, H6); then `capability_* completed` read as "record created"
  (round 2, R2-B); then confirm-loops re-drafting instead of invoking the write tool (round 3,
  R3-1); then invented record URLs (round 4, R4-3). Each needed a directive + result-text +
  catalog-description mirror. Lesson: every tool result and gate outcome must state explicitly
  what did NOT happen.
- **Confirm-resume 502s from unvalidated model payloads.** Model-composed lookups without GUIDs
  died at the mapper AFTER the user clicked Confirm (rounds 2–3), surfacing as silence then as
  502s (fixed to 422). Gate **pre-suspend validation** — validate the payload before showing the
  dialog — is filed for r2; guidance-only steering was repeatedly insufficient (R5-E: guidance ≠
  enforcement; the sprk_document ban only held once hard-coded).
- **Anti-fabrication pins accrete into passivity.** By round 5 the directive stack had a dozen
  "never claim / never guess / omit if unresolved" bullets — safety by subtraction. The r2 D-F0
  Resourcefulness Doctrine (strategy meta-prompt, read/write safety asymmetry, degradation
  ladder, refusal-with-affordance-links) is the diagnosis: honesty pins alone produce a timid agent.
- **CI auto-deploy of master clobbered the dev App Service mid-UAT** (18:09Z incident,
  current-task.md). Branch-deployed UAT builds on an environment CI also targets need an explicit
  active-sha check (`az rest deployments list`) before every UAT round — we learned it the hard way.
- **Publish size grew +3.98 MB despite Track B** (task 055: 49.63 MB incl. PDBs vs the 2026-05-26
  baseline; +1.22 of it master drift; NFR-01 "net reduction" not met in absolute terms). Capability
  growth outpaces deletion; deletion buys headroom, not a downward trend.
- **Test-suite contention between parallel agents.** Two agents running testhost concurrently
  produced parallel-run flakes that pass in isolation (round 4: `AnalysisToolDtoTests` 68/68
  isolated) and forced a curated KNOWN-failures list into every agent brief. Serialize full-suite
  runs, or give agents targeted-filter runs only.
- **Three-mirror validator maintenance is fragile.** The server `OpenAiFunctionSchemaValidator`
  plus two client twins (BA editor, ScopeConfigEditor PCF) must stay rule-identical by hand; the
  triple-twin hoist is a filed defer. Same pattern in guidance: handler Metadata + live catalog
  row + repo seed mirror needed parity edits every UAT round.
- **Publish-size measurement itself bit us twice** — stale output dirs (round 1: 49.94 vs 46.82)
  and post-test incremental PDB inflation (round 3). Rule: clean obj/bin, fresh directory, every time.

## 4. Process lessons

- **UAT-freeze deferral (task 053) worked.** With UAT rounds live on the deployed build, 053
  shipped its client leg and deferred the server deletion, which task 050 executed once the gate
  closed (42 files, grep-zero shown). Deleting server surface mid-UAT would have contaminated the
  round diagnostics.
- **Six small UAT rounds beat one big gate.** Each round produced a pinned findings file
  (defect → App Insights evidence → root cause → fix inventory → next-round script), fixes
  deployed same-day, and the next round regression-swept the previous one. One monolithic gate
  would have entangled the fabrication, gate-resume, and client-race defect families.
- **Operator rulings recorded at decision-point (CLAUDE.md §6.5) prevented silent scope drift.**
  Auto-summarize retirement (G-P1), soft-slash → P3, analysis.rerun ungated accept-with-note,
  tasks-stay-sprk_event, size-cap → 055, G-M maker-gate deferral — every ruling is quoted in a
  findings file or TASK-INDEX row with its supersession noted, so "why does the spec say X but the
  product do Y" always has a citable answer.
- **The r2 charter was written WHILE r1 closed** (operator overlap ruling 2026-07-07: 051/052/053
  dispatched early; the r2-design-v0.2 agent ran alongside the 050/054/055 close-out). Momentum
  carried straight into r2 instead of dying in wrap-up; UAT findings flowed into r2 design rows
  the same day they were found (R5-E verified its own candidate already covered by r2 D-C2).

## 5. Pointers for r2

- **D-F0 Resourcefulness Doctrine** — the systematic answer to §3's honesty-pin accretion.
- **Memory service** — session history browsing/deletion + portfolio-context bias (R5-C note) are
  explicitly r2 memory scope.
- **Compose r2 satellite** — separate project absorbing D-C* incl. the save-back/document-creation
  leg (R3-4/R5-E bar: full ingestion parity, not just SPE upload + row).
- **Briefing-hallucination fix wave** — immediate, operator to supply the example.
- **19-row inherited backlog** — `../spaarke-ai-architecture-redesign-r2/design.md` §10 carries
  the named candidates (gate pre-suspend validation, validator triple-twin hoist, trace
  ledger-read surface, Confirmation Policy v2, create-matter capability, appid links, and the rest).
