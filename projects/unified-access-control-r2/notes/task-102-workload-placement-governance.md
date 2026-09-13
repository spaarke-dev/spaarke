# Task 102 — Workload-placement governance (ADR-052)

> **Task**: `tasks/102-workload-placement-governance.poml` · FULL · opus@high
> **Sessions**: 9 (steps 0–2: evaluation, drafts, Fable-tier review → v2) · 10 (steps 3–7, 2026-09-13)
> **Evidence base**: [`decisions/workload-placement-policy-evaluation.md`](decisions/workload-placement-policy-evaluation.md)

## 1. What was decided

Owner decisions D1–D7 (2026-09-12), recorded in the evaluation note: no prohibition of Azure Functions; placement
decided per workload on merit; **fewer moving parts** breaks ties; a Function reuses the stamp's managed identity
(Model 1 shared app acceptable); a new ADR; every document aligned plus a drift guard.

**Identity wording (owner, 2026-09-13 — "use whatever is consistent with existing approach").** Three options were
put: (1) reuse the stamp UAMI **app-only**; (2) also let the Function act as the BFF app registration via the
UAMI's federated credential; (3) a dedicated identity per Function. **Option 1 adopted** — it is exactly ADR-028
A4's app-only row, the path the BFF's own managed-identity work uses, and the precedent the provisioning control
plane (the one existing separate host) already follows. Option 2 rejected: OBO needs a user token a Function never
holds, and it would give a Function the BFF's delegated power. Option 3 is kept for isolation cases, with owner
approval. Two corrections came out of checking the code:

- "No new grants" covers the managed-identity app-only row only. The BFF's **CIAM Graph** (`CiamGraphClientFactory`,
  certificate) and **Power BI** (`ReportingEmbedService`, secret) clients are other app registrations and are not
  inherited.
- The UAMI can technically mint the MI-FIC assertion for the BFF app registration, so "never act as the BFF app
  registration" is **enforced** — `WorkloadPlacementGuardTests` bans MSAL confidential-client / assertion / OBO
  types under `src/server/functions/**` (POML AC (c) extended).

## 2. What was applied

| Area | Files |
|---|---|
| **New ADR** | `docs/adr/ADR-052-workload-placement.md`, `.claude/adr/ADR-052-workload-placement.md` |
| **Amended ADRs (path B)** | ADR-001 A1 (full rewritten with superseded sections in reasoned markers; concise replaced) · ADR-004 A1 · ADR-036 A1 · ADR-013 (pointers + non-generic `IJobHandler` sample) · ADR-002 (pointer, both) · ADR-032 (`IJobHandler<T>` → `IJobHandler`) |
| **Indexes** | `docs/adr/INDEX.md`, `docs/adr/README-ADRs.md`, `docs/adr/ADR-VALIDATION-PROCESS.md`, `.claude/adr/INDEX.md`, `CROSS-REFERENCE-MAP.md` |
| **Directives (`.claude/`, main session)** | constraints `api`, `ai`, `plugins`, `jobs` (rewritten), `bff-extensions` (§A.1, §D, §E, decision table, sources); patterns `api/background-workers`, `api/scheduled-jobs` (rewritten to the runtime as built), `api/endpoint-definition`, `auth/graph-webhooks`, `testing/integration-tests`; skills `code-review` (+ checklist), `adr-check` (+ validation rules), `adr-aware`, `task-create`, `design-to-spec`, `mcp-tool-handler`, `project-setup` template; root `CLAUDE.md` §17 row; `.claude/CHANGELOG.md` |
| **Docs (sub-agent, 47 files)** | standards, architecture (incl. INSIGHTS-ENGINE — history in markers, D-20 recorded as the project's own decision), data-model, procedures, guides (`BACKGROUND-JOBS-ADMIN-GUIDE` corrected to the in-memory runtime; deployment guide §8), PR template, `.coderabbit.yaml`, `infra/insights` (README + Bicep header), `knowledge/` pointer notes, 18 `src/server` comment-only edits, 4 active projects' CLAUDE.md pointer lines |
| **ArchTests** | `ADR001_MinimalApiTests` (method + parameter attribute scan; scoped message) · NEW `WorkloadPlacementDocDriftTests` · NEW `WorkloadPlacementGuardTests` (timer ratchet, host-neutrality, Functions-project guard) |
| **Project** | evaluation note §4.4 refinement; POML AC (c); this note |

## 3. Follow-ups filed (step 6)

| # | Issue |
|---|---|
| [#976](https://github.com/spaarke-dev/spaarke/issues/976) | Migrate the 14 hand-rolled timer `BackgroundService`s to `IScheduledJob` |
| [#977](https://github.com/spaarke-dev/spaarke/issues/977) | Office workers + membership topic → ADR-004 job contract |
| [#978](https://github.com/spaarke-dev/spaarke/issues/978) | `IndexingWorkerHostedService` completes messages on failure |
| [#979](https://github.com/spaarke-dev/spaarke/issues/979) | `office-profile` queue has no sender |
| [#980](https://github.com/spaarke-dev/spaarke/issues/980) | Enable Service Bus duplicate detection on the BFF queues |
| [#981](https://github.com/spaarke-dev/spaarke/issues/981) | `MembershipEventPublisher` sets no MessageId |
| [#982](https://github.com/spaarke-dev/spaarke/issues/982) | BFF ↔ L2 MessageId derivation drift |
| [#983](https://github.com/spaarke-dev/spaarke/issues/983) | `DataverseBackgroundJobStore` (durable history, fleet-wide disable) |
| [#984](https://github.com/spaarke-dev/spaarke/issues/984) | `IIdempotencyService` check-then-set, fails open → atomic claim *(beyond §5 — ADR-004 A1 §4 cites it)* |
| [#985](https://github.com/spaarke-dev/spaarke/issues/985) | `infra/insights` Function Bicep predates ADR-052 *(beyond §5 — POML notes cite it)* |
| [#986](https://github.com/spaarke-dev/spaarke/issues/986) | SPE container-type grants: topology guide vs the managed-identity app-only path *(found this session)* |

## 4. Findings during execution (and what was done)

1. **The drift guard found its own holes on the first pass.** Markdown bold (`**MUST NOT** use Durable Functions`)
   and "Do not use Durable Functions" both walked past the v1 patterns. Fixed: emphasis and code ticks are blanked
   (index-preserving) before matching, multi-word patterns use `\s+`, the Durable pattern covers "do not use" /
   "don't use", and `NegativeControl_FormattingDoesNotDefeatTheGuard` pins bold, backticks and line wraps.
2. **ADR-052 overstated ArchTest coverage of a Functions project.** Only the credential guards and the I2/I3
   tenant-isolation tests scan `src/server/**`; I4 (`Sprk.Bff.Api/Services`), I5 (`Sprk.Bff.Api/Infrastructure/Graph`
   and siblings) and I6 (BFF + `src/server/shared`) do not. ADR-052 §5, §6, §10 and Compliance now say so, and
   widening I4–I6 is part of the first Function's one-time setup.
3. **SPE app-only access is only as good as the container-type grant.** The SPE topology guide says to grant the
   BFF *app registration*; the code does app-only Graph as the *managed identity*. ADR-052 §4 now states the access
   precisely (whatever the BFF's managed-identity path has) and #986 reconciles guide, provisioning and code.
4. **ADR-032's stale item was `IJobHandler<T>`, as POML 102 said.** An early grep for "Functions" missed it; the
   drift scan found it.
5. **A BFF-scoped rule is not drift.** "Azure Functions are not permitted inside the BFF assembly" is ADR-001's live
   rule; the flat-ban patterns skip a match scoped, within the sentence, to the BFF / `Sprk.Bff.Api`. Rephrasing
   was preferred over markers for every current (non-historical) sentence.
6. **The timer inventory matches the evaluation.** 23 `BackgroundService` subclasses in the BFF: 14 hand-rolled
   timers (ratchet baseline 14) + 9 others (6 queue/topic consumers, a Null-Object, a work processor, a one-shot
   migration). All three `IScheduledJob`s are host-neutral today.
7. **Left for the owner**: Insights Engine D-20 still records "no Durable Functions" as that project's decision —
   now marked as a project choice, not a platform rule; revisiting it is their call.

## 5. Verification

- Targeted run (session 10): 64 ADR-001 / ADR-052 tests, 63 passed; the one failure was the repository drift scan
  naming exactly two `.claude/` lines, both then fixed.
- Full ArchTests suite + final drift scan: see § 7 (filled at Step 9).
- Publish size: **not applicable** — no BFF runtime code changed (comment-only edits in `src/server`, see §7; tests).

## 6. Step 9.5 quality gates — findings and resolutions

Two independent read-only reviewers (code-review; adr-check), both coverage-first.

**adr-check — "pass with fixes"; no contradiction of D1–D7 or ADR-028 A4; every code claim it spot-checked held.**
Fixed: the identity ban lacked `ClientAssertionCredential` / `ClientCertificateCredential` (High); the concise ADR's
ADR-038 link was broken; "Proposed — Accepted when task 102 merges" would be false after merge → **ADR-052 Accepted
2026-09-13** everywhere; Dataverse caller impersonation (ADR-028 A5) was unaddressed → explicit MUST NOT in §6 plus
the guard (🔔 flagged to the owner — it follows from the existing §5 rule, so it tightens, never relaxes); concise
gaps (orchestration payloads, first-Function setup items); invariant numbering (guide I2–I5 vs ArchTest I6);
date-stamped claims that will age; stale counts and the retry formula (`JobRetryPolicy` uses `2^(attempt-2)`);
comments across `src/server` still crediting ADR-001 with the `BackgroundService` pattern (reworded, and now a
banned phrasing); the `.claude/adr/INDEX.md` "26 other BackgroundServices" line.

**code-review — no Critical; all `src/server` edits confirmed comment-only.** Fixed: `BffScope` let a flat ban
hide behind a later "in the BFF" (now must follow immediately); house-style `MUST NOT use` / `never use`,
comment-prefix line wraps, underscore emphasis and `IJobHandler{T}` crefs evaded; the Durable pattern flagged
ADR-001's own BFF-scoped rule; an unclosed marker could borrow a later closer (now paired); reason parsed beyond the
opener; root `*.md` and `scripts/` unscanned; no assertion that a project CLAUDE.md was scanned; reparse-point loops;
ratchet baselines were `<=` (now exact, plus an `OtherServiceBaseline`) and simple-name collisions were silent (now
fail); the Functions-project guard only walked `src/`, only read one attribute order and one SDK (now repo-wide,
order-insensitive, in-process SDK / WebJobs / Durable Task worker, `Directory.*.props`); rules 2 and 3 lacked
maintenance procedures and rule 3 an owner-approved exception list; ADR-001's package rule lacked controls
(`Adr001ControlFixtures.cs`) and the attribute scan missed return values and assembly attributes; the
`HandlerEnvelope` comment named the wrong consumer.

**Accepted as documented limits**: a timer on a direct `IHostedService` or in a shared library (none today);
HTML emphasis; neutral "not Durable Functions" clauses; marker syntax inside code spans; `//` inside string
literals in the identity scan.

**Escalated to the owner (🔔 scope)**: not-started task POMLs in ACTIVE projects still say "No Azure Functions" —
`ai-spaarke-action-engine-r1` tasks 001/014/022/030/040/042/070 and `sdap-SPE-admin-app-r2` task 050 (plus active
specs). The approved scope excluded `projects/` except active CLAUDE.md, so neither editing other projects' task
files nor widening the guard to them was done without a decision.

## 7. Step 9 results

- **Full ArchTests suite: 288 / 288 passed** (197 before task 102; the rest are the new rules, their controls and the
  drift guard's theory cases).
- **Final drift scan: 0 findings** over the whole scope. The scan asserts more than 500 files, every root kind, and at
  least one active project's CLAUDE.md, so it cannot pass by scanning nothing.
- **`src/server`: comment-only.** Mechanical check against `ae27527a7`: 86 changed lines in 33 files, 0 non-comment.
- **Acceptance criteria**: all met. ADR-052 full + concise, indexed and Accepted; ADR-001/004/013/036 amended by
  pointer; no banned phrasing outside reasoned markers; code-review / adr-check / adr-aware / task-create /
  design-to-spec route host → ADR-052, schedule → ADR-036, queue → ADR-004; ADR-001 ArchTest message scoped and its
  scan reads method, parameter, return and assembly attributes, with controls; the drift guard, ratchet,
  host-neutrality and Functions-project guards all carry negative + positive controls; ADR-004 A1 withdraws the Durable
  ban and ADR-052 §7 keeps Durable Task out of the BFF; ADR-036 A1 §5 records the dev-only interim; the admin guide
  no longer claims Dataverse-backed behaviour; follow-ups filed (11, against 8 required); CHANGELOG entry written.
- **Publish size: not applicable** — no BFF runtime code changed.
- **Open for the owner (non-blocking)**: the not-started POMLs in active projects (§6); confirming the Function
  impersonation MUST NOT (§6); Insights D-20 (§4.7).

_(filled below at completion)_
