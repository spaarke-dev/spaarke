# reuse-governance-tightening-r1 — Design

> **Status**: Design draft for owner review. Do **not** run `/design-to-spec` or `/project-pipeline` until the owner has answered the Open Questions (§11).
> **Revised**: 2026-11-10 — added workstream 5, "Quality tooling quick wins" (§7), at the owner's request: the low-priority review items that are not large-effort tickets.
> **Created**: 2026-11-10 (recreated; the 2026-09-02 draft was lost: it was in no worktree and never committed).
> **Sources**: the owner's 2026-11-10 evaluation of an external "Claude Code Quality Recommendations" document (items B1, B2, B4, B7, B8); the Sept 2026 owner decision recorded in auto-memory `project_reuse-governance-tightening-r1.md` (the only surviving summary of the lost draft); the current repo at `d64a45071b`.
> **Hot-path surfaces**: skill-directives (Y — `task-create`, `project-pipeline`, `.claude/agents/adversarial-reviewer.md`), ci-workflows (Y — one scheduled job), BFF (N), SpaarkeAi (N), root CLAUDE.md (N, unless OQ-7 is answered "yes").

---

## 1. Problem and premise

The external review assumed that agents in this repo miss prior art and write duplicates (under-reuse). The evidence in the repo points the other way, and the owner confirmed that reading in September 2026:

- **The reuse gate already works.** `task-create` Step 3.5.6 requires a `<justification>` with an `<existing>` answer that cites file:line Grep evidence; `code-review` Step 6.6 re-checks it; `scripts/Validate-TaskPoml.ps1` keys on `role="new"`. In September, 448 POMLs carried `<justification>`. Roughly 40% of the external recommendations already existed.
- **The live risk is over-promotion.** There are 15 `@spaarke/*` packages under `src/client/shared/`, under an ADR (ADR-012) that calls `@spaarke/ui-components` the "single source of truth". Until 2026-11-10 an open-ended "… etc." in ADR-012 covered 11 of them. PR #2656 closed that gap: the census now lists 6 sanctioned and 9 grandfathered packages, and `tests/Spaarke.ArchTests/SharedPackageCensusTests.cs` fails the build when the folders, the ADR table and the test disagree.

What is still weak is narrower than the external review suggested:

| Gap | Where it bites |
|---|---|
| **G1 — `<existing>` is only as good as the Grep the author thought to run.** Grep finds names; it does not find the same capability under a different name (`RegardingResolver` vs `polymorphic-resolver` vs "set-regarding" vs "association"). | `task-create` 3.5.6, `code-review` 6.6 |
| **G2 — the plan itself is never checked for duplication.** 3.5.6 runs per task, after decomposition. A project whose *design* re-implements an existing capability produces 20 individually-justified tasks that each pass the gate. | `project-pipeline` Step 2 (item 5 asks for the §11 "Existing" answer, but by name search, in the orchestrator's own context) |
| **G3 — nobody observes duplication after merge.** There is no signal when two projects land the same capability in parallel worktrees (13 of 17 worktrees touched the BFF in one count). | Nothing today |
| **G4 — the 9 grandfathered packages have no decision path.** The census stops new ones; it does not resolve the existing ones. | ADR-012 census |

This project closes G1–G4 with the lightest mechanism that works for each. It deliberately does **not** add a blocking duplication gate (§10). A fifth workstream (§7) bundles six small quality-tooling items from the same review that the owner asked to include because none is a large-effort ticket.

## 2. Goals and non-goals

**Goals**
1. A task author, reviewer or scanning agent can go from a *concept* ("send an email", "resolve the regarding record", "open a modal") to the canonical component, its ADR and its extension point in one read.
2. A project's design is checked for capability duplication once, before tasks are generated, and the result is a file that later steps cite.
3. Duplication that lands anyway becomes visible within a week, as one alert, without blocking anyone's build.
4. Each grandfathered shared package gets an owner decision: sanction, consolidate or retire.
5. Six small tooling items (§7) land with the same posture: census-and-ratchet where a rule can be checked, a measured trial where the answer is unknown.

**Non-goals**
- No change to the §11 three questions or to the `<justification>` schema.
- No blocking gate on duplication counts (§10.2).
- No generated or exhaustive capability catalogue (§10.4).
- No executing the consolidations themselves inside r1 beyond the ones the owner marks cheap (OQ-5); larger consolidations are filed as their own projects.
- No BFF code change. This project touches no `src/server/**` file, so the BFF publish-size and placement obligations (root CLAUDE.md §10) do not apply. Item 5(e) adds an ArchTest that *reads* BFF IL; it changes no BFF code.

## 3. Item 1 — Capability index (B1)

### 3.1 What

One hand-curated file, `.claude/patterns/CAPABILITY-INDEX.md`, of about 30 rows:

| Column | Content |
|---|---|
| Capability | The thing a task wants to do, in plain words ("Send an email from the BFF") |
| Synonyms | The other names an author might search for ("mail, notify by email, Graph sendMail, outbound message") |
| Canonical component | The file or package to extend, as a repo path (and symbol) |
| Governing ADR | The concise ADR in `.claude/adr/` that constrains it |
| Extension point | *How* to add to it: the interface to implement, the registry to add a row to, the options class to extend |

Each row may link the existing pointer file in `.claude/patterns/**` for the "how". The index is the concept→component lookup; the pattern files stay the how-to. The index is *not* a 25-line pointer file, so `.claude/patterns/INDEX.md` gets one line describing it as a different shape.

### 3.2 Seed rows (to be verified, not trusted)

The first task verifies every cell with Grep/Glob before it is written; this list only scopes the ~30 rows. Paths named in root `CLAUDE.md` §13/§17 are the most reliable starting points.

| # | Capability | Likely canonical component | ADR |
|---|---|---|---|
| 1 | Authenticate a client surface / SSO / authenticated fetch | `@spaarke/auth` | ADR-028 |
| 2 | Call Graph / OBO from the BFF | `Infrastructure/Graph/GraphClientFactory.cs` | ADR-028, ADR-007 |
| 3 | Normalize the BFF base URL | `.claude/patterns/auth/bff-url-normalization.md` target | ADR-028 |
| 4 | Create/update a Dataverse record with an invariant (no plugins) | `Services/Dataverse/CoreAncestorResolver.cs` + write-path architecture | ADR-002 |
| 5 | Resolve a polymorphic regarding / set-regarding / field mapping | field-mapping framework (`docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md`) | ADR-024 |
| 6 | Queue background work | `IJobHandler` / `ServiceBusJobProcessor.cs` | ADR-004, ADR-052 |
| 7 | Run something on a schedule | `IScheduledJob` | ADR-036, ADR-052 |
| 8 | Cache in Redis / per request | `.claude/patterns/caching/*` targets | ADR-009 |
| 9 | Authorize an endpoint | endpoint filters | ADR-008, ADR-003 |
| 10 | Return errors (ProblemDetails) | `.claude/patterns/api/error-handling.md` target | ADR-001 |
| 11 | Resilient outbound HTTP | `.claude/patterns/api/resilience.md` target | — |
| 12 | Send email | `.claude/patterns/api/send-email-integration.md` target | ADR-045 |
| 13 | Read/write SPE documents and containers | SPE services (`spe-integration` skill) | ADR-007 |
| 14 | Extract text from a document | `.claude/patterns/ai/text-extraction.md` target | ADR-013 |
| 15 | Index a document for retrieval | `.claude/patterns/ai/indexing-pipeline.md` target | ADR-013 |
| 16 | Run an AI analysis / playbook node | `AnalysisOrchestrationService.cs`, node executors | ADR-013–016 |
| 17 | Expose AI to another BFF module | `PublicContracts` facade | root §10, ADR-013 |
| 18 | Stream an AI response (SSE) | `.claude/patterns/ai/streaming-endpoints.md` target | — |
| 19 | Notify a user (push / poll) | `@spaarke/notifications` | ADR-047 |
| 20 | Open a modal / record modal | `SprkModal`, `modal-shell.md` | ADR-050 |
| 21 | Multi-step wizard / create-record wizard | `WizardShell`, `CreateRecordWizard` | ADR-012 |
| 22 | Data grid / dataset views | DatasetGrid + datagrid framework | ADR-012 |
| 23 | Lookup field | `LookupField` | ADR-012 |
| 24 | Simple choice dialog | `ChoiceDialog` | ADR-012 |
| 25 | Side pane / Navigator | `ensureNavigatorSidePane()` | — |
| 26 | Theme resolution (code page) | `resolveCodePageTheme()` | ADR-021 |
| 27 | Charts, KPI cards | `@spaarke/visuals` | ADR-012 (2026-07-12) |
| 28 | Workspace widget in the three-pane shell | `@spaarke/ai-widgets` + widget guide | ADR-030, ADR-031 |
| 29 | Open in Word / download / delete a document | `@spaarke/document-operations` (grandfathered; see item 4) | ADR-012 |
| 30 | Rich-text / document editing | Compose (`@spaarke/compose-components`) vs `RichTextEditor` — **two answers today**; the row records both and points to item 4 | ADR-049, ADR-012 |

Row 30 is deliberate: where the index cannot name one canonical component, the row says so. That makes the index a by-product detector of over-promotion.

### 3.3 Wiring

- `task-create` Step 3.5.6: `<existing>` **consults the capability index first**, then Greps. The answer cites either `CAPABILITY-INDEX row N` plus file:line, or `CAPABILITY-INDEX: no row` plus the Grep command shown (today's rule).
- `code-review` Step 6.6 and the adversarial reviewer (item 3) use the same index.
- Row maintenance: a project that adds a canonical component, or changes an extension point, updates the row in the same PR. `doc-drift-audit` already runs at project transitions; it gets the index added to its scan list.

### 3.4 Why hand-curated

A generated manifest was rejected earlier and stays rejected (§10.4): the BFF alone had ~940 service files in September, which is a listing, not an index, and too large for an agent to read usefully. Thirty rows that a human chose are the point. The cost of curation is bounded by the freshness guard in §3.5.

### 3.5 Enforcement-ladder choice (§16)

| Concern | Rung | Mechanism |
|---|---|---|
| Rows point at paths that no longer exist | **ArchTest / source-scan guard** | New `tests/Spaarke.ArchTests/CapabilityIndexTests.cs`: parse the table; every canonical-component path exists on disk; every ADR cited exists in `.claude/adr/`. Must-fire control: a seeded row with a missing path fails. Must-not-fire control: the real file passes. Runs in the `Stop` hook and CI with the other ArchTests. |
| `<existing>` ignores the index | **Lint** (script) | `scripts/Validate-TaskPoml.ps1`: when a task has a `role="new"` file, `<existing>` must contain `CAPABILITY-INDEX row` or `CAPABILITY-INDEX: no row`. Warning first (shadow window), then error — see OQ-3. |
| Whether the row chosen is the *right* one | **Prose** | Judgment; reason it cannot be mechanised: choosing the canonical component for a concept is semantic. Checked by `code-review` 6.6. |

## 4. Item 2 — Reuse scan in `project-pipeline` Step 2 (B2, B4)

### 4.1 What

A new sub-step in `project-pipeline` Step 2 Part 1, after item 5 (canonical implementations):

1. The orchestrator extracts from `spec.md`/`design.md` the list of **capabilities the project introduces** (not file names): each new service, endpoint, package, Dataverse column and UI surface, expressed as a concept with its likely synonyms and the interface it would expose.
2. It launches **one `code-mapper` agent** (model `sonnet`; see OQ-2 on effort) with that list, the capability index, and instructions to search **by concept, synonym and interface shape**, not by the name the spec uses: e.g. for "matter-number generator" also search `INumberGenerator`, `sequence`, `autonumber`, `alternate key`, and for an interface, methods with the same signature.
3. The agent returns facts in a fixed schema (below). Per its definition it does not judge and does not write files.
4. The orchestrator (planning tier) judges the facts and writes **`projects/{name}/notes/reuse-scan.md`**, including a **plan-level duplication check**: does any capability in the design duplicate an existing one, or does the design add a package/service where the index row says "extend X"?
5. **Two or more viable candidates for one capability → ask the human** (interactive: a question; autonomous: STOP at this gate with the options, like the other safety gates). One candidate → record it as the `extend` target. Zero → record "none found" with the searches run.
6. `task-create` Step 3.5.6 `<existing>` **cites `notes/reuse-scan.md`** (the row for that capability) when the file exists.

Return schema (passed as `schema` in a workflow `agent()` call, or required in the prompt):

```json
{ "capabilities": [ {
    "concept": "…", "searched": ["terms and patterns actually run"],
    "candidates": [ { "path": "…:line", "symbol": "…", "match": "name|synonym|interface|index-row", "evidence": "…" } ] } ],
  "notCovered": "what was not searched" }
```

### 4.2 A deviation from the brief, and why

The brief says the `code-mapper` agent *writes* `notes/reuse-scan.md` and *checks the plan for duplication*. `.claude/agents/code-mapper.md` forbids both: it is read-only ("Don't edit or create files in any checkout") and it reports facts, leaving judgment to the caller. The design keeps the agent definition intact and splits the work: **the agent finds, the orchestrator judges and writes**. This also keeps the judgment on the planning tier, as root `CLAUDE.md` §8.5 and `project-pipeline` "Model and effort" require, and costs one Sonnet agent plus a short orchestrator pass. (Writing under `projects/` is not blocked by the §3 sub-agent boundary; the reason is the agent's own contract.) If the owner prefers the agent to write the file, that needs a new agent definition, not a change to `code-mapper` (OQ-1).

A second consequence: Step 2 Part 1 runs in Plan Mode (read-only). The scan runs in Part 1; `reuse-scan.md` is written at the start of Part 2, with the other artifacts.

### 4.3 Enforcement-ladder choice

| Concern | Rung | Mechanism |
|---|---|---|
| The scan is skipped | **Script lint** at the pipeline's existing Completeness Lint gate | Step 3 does not start if `notes/reuse-scan.md` is absent for a project whose spec introduces new surface (the §11 table in the spec is non-empty). Checked by the same lint pass that already gates Step 3. |
| `<existing>` does not cite the scan | **Lint** | `Validate-TaskPoml.ps1` accepts `reuse-scan.md` as a valid `<existing>` citation (alongside the index row). |
| The ≥ 2-candidate human decision | **Prose + gate** | A judgment call by definition; the gate is that the pipeline stops, not that a tool decides. |

## 5. Item 3 — Weekly duplication sweep (B7, B8)

### 5.1 What

**Observation only. It never fails a build or a PR.**

1. **Syntactic sweep**: `jscpd` (exact version pinned, run via `npx`, no `package.json` change) over `src/**` for TS/TSX and C#, `--exitCode 0`. It writes a JSON + Markdown report as a workflow artifact.
2. **Exclusions**: `**/node_modules/**`, build output, generated code, `**/*.test.*`, `tests/**`, and **`.claude/worktrees/agent-*/**`**. Agent worktrees hold full source-tree copies and would report every file as duplicated. In CI that folder is absent (`.claude/worktrees/` is gitignored), but the exclusion lives in the shared `.jscpd.json` so that a local run gives the same answer as CI.
3. **Baseline**: a committed `scripts/quality/duplication-baseline.json` lists the known clone clusters (fingerprinted by file pair + hash, not by line number). This is a ratchet baseline in the §16 sense, but **observing, not gating**.
4. **One alert**: the sweep calls `.github/actions/quality-alert` with check `weekly/duplication-sweep`. Status `failing` when the run finds clone clusters **not in the baseline** above a size floor (e.g. ≥ 50 tokens, ≥ 2 files in different top-level areas); status `ok` otherwise, which closes the alert. A permanently open alert would be noise in the `SessionStart` quality banner, so "new since baseline" — not "any duplication" — is the trigger. The summary names each new cluster with its two paths and the capability-index row it most likely belongs to, if any.
5. **Reviewer item**: `.claude/agents/adversarial-reviewer.md` gains one check: *"New surface that duplicates a capability in `.claude/patterns/CAPABILITY-INDEX.md` (same concept under another name, or a new package/service where the row says extend). Class F4 when it adds a second implementation of an indexed capability; K-class when it is a judgment call."* This is the capability-level half that jscpd cannot see.

Where it runs: **extend `nightly-health.yml`** with a weekly-cadence job rather than adding a workflow (root §11 "extension"), reusing its quality-alert matrix plumbing — see OQ-4 for the alternative.

### 5.2 Enforcement-ladder choice

| Concern | Rung | Mechanism |
|---|---|---|
| Syntactic clones landing | **CI observation** (deliberately below the "lint rule" rung) | Weekly job, exit 0, one quality-alert issue. Not a gate: §10.2. |
| Capability-level duplication in a change set | **Prose** in the reviewer definition | Reason it cannot be mechanised: "same capability under a different name" is semantic; jscpd and ArchTests see only shape. |
| Phantom duplication from agent worktrees | **Config** | Exclusion in `.jscpd.json`, with a must-not-fire control: the sweep's own test run on a fixture tree containing a `.claude/worktrees/agent-x/` copy reports zero clusters from it. |

## 6. Item 4 — Review backlog for the 9 grandfathered packages

### 6.1 What

For each package between the `shared-package-census` markers in `.claude/adr/ADR-012-shared-components.md` with status *Grandfathered — under review*, produce a one-page decision record in `projects/reuse-governance-tightening-r1/notes/package-reviews/{package}.md` with the evidence for ADR-012's three tests (distinct contract, quarantine-worthy dependency, cross-surface reuse), its consumer count, and one recommendation:

- **Sanction** → an ADR-012 amendment (Path B, §6.5) that names the package and its contract outside the census table; flip the row and the `SharedPackageCensusTests` entry to Sanctioned with the ADR citation (the test checks both).
- **Consolidate** → into a named sanctioned package; a migration plan (consumers' `package.json`, import paths, PCF deep-import paths, build config). Cheap ones execute in r1; others are filed as their own projects.
- **Retire** → no remaining reason to exist (1 consumer, or a pure re-export); fold into its consumer.

The owner decides each one. Evidence gathering is `code-mapper` work (sonnet, low); the recommendation is planning-tier judgment.

### 6.2 Starting hypotheses (from the census reasons; the review may overturn any of them)

| Package | Census note | Hypothesis |
|---|---|---|
| `@spaarke/sdap-client` | 18 consumers; overlap with BFF adapters in `ui-components` unassessed | Sanction *or* consolidate — decided by the overlap assessment; high consumer count makes consolidation expensive |
| `@spaarke/smart-todo-components` | Feature area, Pattern D dual-use | Consolidate (fails "distinct contract") — check PCF bundle impact first |
| `@spaarke/communication-components` | Feature area, Pattern D dual-use | Consolidate (same reasoning) |
| `@spaarke/daily-briefing-components` | Feature area, Pattern D dual-use | Consolidate (same reasoning) |
| `@spaarke/compose-components` | TipTap may justify quarantine | Sanction (TipTap is the quarantine case ADR-012 test 2 describes); also settles capability-index row 30 |
| `@spaarke/document-operations` | Same contract as `ui-components` | Consolidate into `ui-components` (likely cheap) |
| `@spaarke/ai-outputs` | Boundary with `ai-widgets` unassessed | Consolidate into `ai-widgets` unless the contracts differ |
| `@spaarke/ai-context` | 1 consumer | Retire into its consumer or consolidate |
| `@spaarke/legal-workspace` | Source-only re-export boundary; 1 consumer | Retire, unless the FR-10 boundary still needs a package (then sanction narrowly) |

The "feature area" packages carry a real counter-argument the review must address: Pattern D dual-use (one feature shipped as both a workspace widget and a standalone code page) is a cross-surface reuse case, and folding them into `ui-components` grows a package that PCF consumers import. ADR-012 already says a different feature area is not a distinct contract; the review checks whether the PCF bundle cost changes that answer.

### 6.3 Enforcement-ladder choice

Already at the **ArchTest** rung: `SharedPackageCensusTests` treats the grandfathered list as a ratchet baseline that may only shrink. This item works the baseline down; it adds no new mechanism. Each resolution changes the ADR table and the test census in the same PR, which the test enforces.

## 7. Item 5 — Quality tooling quick wins

Added at the owner's request: the low-priority items from the 2026-11-10 review that are not large-effort tickets. Sizes are the owner's (S / M). Each sub-item is independent of items 1–4 and of the others, and can run in parallel.

### 7.1 (a) Microsoft Learn MCP server for research — S

**What.** Add Microsoft's Learn MCP server (remote HTTP; the task confirms the current endpoint from Learn itself) to `.mcp.json` next to the existing `dataverse` server. Update `.claude/agents/researcher.md` so that, after its local sources (its memory, `knowledge/`, `docs/`, ADRs), it queries the Learn MCP **before** `WebSearch`/`WebFetch`.

**A detail the brief does not mention.** `researcher.md` restricts its tools in frontmatter (`tools: Read, Bash, Glob, Grep, WebSearch, WebFetch`). Unless the Learn server's tool names are added to that list, the agent cannot call the server, whatever its prose says. The task adds them and proves it with one research call that cites a Learn MCP result.

**Constraints.** The server sends queries to Microsoft. The queries are about public platform documentation, but the researcher prose gets one line saying they must never contain customer data, tenant names or secrets. A project-scoped `.mcp.json` server needs one-time user approval on each machine; the README line says so.

**Enforcement rung: prose.** "Prefer Learn over web search" is a choice about research order, so a check cannot enforce it. The tool-list half is enforced by the platform: without the entry, the call fails.

### 7.2 (b) TypeScript strictness ratchet — M

**What.** In the owner's 2026-11-10 count, 49 of 76 tsconfigs are strict and 4 configs use type-aware ESLint.
1. Add a census ArchTest, `TsconfigStrictnessCensusTests.cs`. It enumerates every `tsconfig*.json` under `src/` (excluding `node_modules`, build output and `.claude/worktrees/**`). It resolves each `extends` chain, parsing the files as JSONC because tsconfig allows comments, and lists those where `strict` is not true. The list is a **ratchet baseline**: a new non-strict tsconfig fails, and a file that becomes strict must be removed from the baseline, so the list can only shrink. Each baseline entry carries a one-line reason, per the `tests/CLAUDE.md` census rules.
2. Move the shared packages under `src/client/shared/` to `strict: true` first. They are imported by every surface, so their loose types reach the most code. Each package is its own task; a package whose strict migration exceeds about a day of errors is split off and filed.
3. Type-aware ESLint is **not** extended in r1 beyond the packages made strict here. Extending it repo-wide is a later ratchet; see OQ-11.

**Enforcement rung: ArchTest + ratchet baseline.** It has must-fire controls (a seeded non-strict tsconfig, and a strict file that is still listed in the baseline) and a must-not-fire control (a tsconfig that inherits `strict` through `extends`).

### 7.3 (c) Scheduled draft of the monthly `knowledge/` refresh — S–M

**What.** `knowledge/REFRESH-PROCEDURE.md` prescribes a monthly refresh. `knowledge/REFRESH-LOG.md` shows that no full monthly refresh has run since the initial curation on 2026-05-14; only interim updates have (2026-07-14 and 2026-08-26). A scheduled routine runs Steps 1–5 of the procedure (re-clone, diff, update the topics, check the 6 skills) and opens a **draft** PR with the log entry. The owner reviews and merges it; the routine never merges. Sign-off stays with a human, as the procedure requires.

**Where it runs (OQ-12).** Either a Claude Code scheduled cloud routine, which can do the judgment-heavy diff-and-update steps, or a GitHub Action that only re-clones and diffs, then opens an issue listing what changed for a session to act on. I recommend the routine, because the procedure's Steps 2–3 are judgment work. Either way, it opens a draft PR or issue in this repo and nothing else.

**Enforcement rung: scheduled automation.** It replaces a prose cadence that has not been followed for six months. Its failure (no draft in a month) is visible: the routine posts a `quality-alert` (`monthly/knowledge-refresh`) when it cannot produce a draft.

### 7.4 (d) Requirements-trace check in the adversarial reviewer — S

**What.** One check is added to `.claude/agents/adversarial-reviewer.md`. When the caller supplies the task POML or `spec.md`, map each acceptance criterion (`<acceptance-criteria>` or the spec's Success Criteria) to the diff hunk and test that satisfy it. Report each criterion as *met (file:line + test)*, *not met* or *not verifiable from the diff*. A *not met* criterion inside the change set's scope is **F1**; *not verifiable* is K-class with a reason. The definition also tells the caller to pass the POML/spec path, because the reviewer cannot trace criteria it was not given. `task-execute` Step 9.5, which launches the reviewer, passes it.

**Enforcement rung: prose** in the agent definition. Whether a hunk satisfies a criterion is semantic. The Completeness Lint already guarantees that criteria exist and are a closed set (`task-create` Step 3.5.5b), which is what makes the trace possible.

Items (d) and 3's capability-duplication check edit the same file; they land in one change.

### 7.5 (e) Census of superseded-but-not-`[Obsolete]` APIs — M

**What.** A census ArchTest, `SupersededApiCensusTests.cs`, built on the existing `tests/Spaarke.ArchTests/IlCallScan.cs`. Its census lists the BFF/shared C# APIs that an ADR or architecture doc names as superseded. Each entry has its replacement, an ADR citation and a reason; the first task populates the list from the ADRs' "replaces"/"superseded" language. For each entry the test reads the compiled IL and records the **types that call it**, against a ratchet baseline. A new caller fails; a removed caller must be dropped from the baseline.

**Why a census rather than `[Obsolete]`.** `Directory.Build.props` sets `TreatWarningsAsErrors=true`, so marking a still-used API `[Obsolete]` turns every existing call into a CS0618 build error. That is probably why superseded APIs are left unmarked. An IL caller-census gives the same "no new callers" signal without breaking the build, and `[Obsolete]` is applied once the baseline reaches zero. The test also fails when an entry has zero callers and still lacks `[Obsolete]` (or has not been deleted), so the last step is not forgotten.

**Enforcement rung: ArchTest + ratchet baseline.** IL scan, not text, so receiver-type and lambda cases are caught, as `IlCallScan` documents. It has a must-fire control (a seeded call to a listed API) and a must-not-fire control (the call to the replacement API).

### 7.6 (f) Per-session C# language-server trial — S

**What.** A time-boxed trial in **one** worktree of the per-session C# language-server plugin for Claude Code. On `src/server/api/Sprk.Bff.Api` (and `Spaarke.sln` if feasible), measure cold-start time to the first usable result, steady-state memory, CPU while idle, and whether find-references and go-to-definition answers beat Grep on three real questions from recent sessions. Record the numbers and a recommendation in `notes/csharp-lsp-trial.md`. The owner decides adoption. Adoption would be a separate change to `.claude/settings.json` or the plugin configuration, gated on that decision.

**Relation to the rejected shared index (§10.6).** The September rejection was about one **shared** index serving about 17 worktrees, which is stale for all but one of them. A per-session plugin indexes only its own worktree, so it does not have that staleness problem. Its open risk is cost: N concurrent sessions run N language servers on one machine. The trial measures exactly that cost, and does not reopen the shared-index decision.

**Enforcement rung: n/a.** This is a measured decision, not a rule. If adopted, the plugin setting is the mechanism.

### 7.7 Enforcement summary for item 5

| Sub-item | Rung | Why not higher |
|---|---|---|
| (a) Learn MCP | Platform (tool allow-list) + prose | Research order is judgment |
| (b) TS strictness | ArchTest + ratchet | Already near the top; a compiler flag cannot ratchet across 76 configs |
| (c) Knowledge refresh | Scheduled automation + quality-alert | The content update needs review by a person |
| (d) Requirements trace | Prose (reviewer) | Semantic mapping |
| (e) Superseded APIs | ArchTest (IL) + ratchet | `[Obsolete]` would break the build under `TreatWarningsAsErrors` |
| (f) C# LSP | n/a (trial) | Decision, not rule |

## 8. Root CLAUDE.md §11 justification for each new surface

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `.claude/patterns/CAPABILITY-INDEX.md` | `.claude/patterns/INDEX.md` and its 94 pointer files (verified 2026-11-10) map *task type → pattern*, not *concept/synonym → canonical component*. | No: the pattern files are capped at 25 lines each and organised by domain, so no file can hold a cross-domain concept lookup; a 30-row table added to `INDEX.md` would double its length and mix two lookups. | `<existing>` keeps depending on the author guessing the right name: `task-create` 3.5.6 passes with "none found" for a capability that exists under another name (G1). |
| `tests/Spaarke.ArchTests/CapabilityIndexTests.cs` | `SharedPackageCensusTests.cs` parses an ADR table between markers. | Partly: copy its table-parsing approach; it cannot be the same class, because it asserts a different invariant over a different file. | The index rots silently: a row points at a moved or deleted component and sends agents to a dead path, which is worse than no index. |
| Reuse-scan sub-step in `project-pipeline` Step 2 + `notes/reuse-scan.md` | Step 2 item 5 already asks for the §11 "Existing" answer per new surface. | Yes — this **extends item 5** rather than adding a step: it changes *how* the answer is found (concept/synonym/interface search by one agent) and *where it is recorded* (a file later steps cite). | A design that re-implements an existing capability passes every per-task gate (G2), because each task's `<existing>` is answered locally after the plan is fixed. |
| Weekly sweep job | `nightly-health.yml` (8 quality-alert checks), `scripts/report-large-server-files.ps1` (observation-only size report). | Yes — a job inside `nightly-health.yml` on a weekly cron, reusing its quality-alert step. | Parallel worktrees can land the same code twice and nothing reports it until someone trips over it (G3). |
| `.jscpd.json` + `scripts/quality/duplication-baseline.json` | None found (a repo-wide `jscpd` Grep timed out at 20 s; to re-check in task 1 with a scoped search). | No: configuration and baseline for a tool the repo does not use yet. | Without the baseline every week's run alerts on known clusters and the alert is ignored; without the shared config local and CI runs disagree (agent-worktree phantoms). |
| One check in `adversarial-reviewer.md` | The definition's checklist. | Yes — a line added to the existing list (modification of existing surface). | Capability-level duplication in a change set is invisible to every syntactic tool. |
| `notes/package-reviews/*.md` | ADR-012 census "reason" column (one line each). | No: one line cannot hold the three-test evidence and a migration plan. | The 9 grandfathered packages stay grandfathered indefinitely (G4); "under review" with no review is the old "etc." again. |
| (a) Learn MCP entry in `.mcp.json` + `researcher.md` tool list | `.mcp.json` (one server: `dataverse`); `researcher.md` already prefers `learn.microsoft.com`, reached through `WebFetch`. | Yes — one entry added to the existing file and one tool-list and prose change to the existing agent; no new file. | The researcher scrapes Learn pages through generic web fetch, which returns whole pages instead of targeted doc chunks and loses to a moved URL (REFRESH-PROCEDURE.md recorded 14+ moved Learn URLs in four months). |
| (b) `TsconfigStrictnessCensusTests.cs` | The census tests (`SharedPackageCensusTests`, `CredentialCensusTests`, …) in `tests/Spaarke.ArchTests/`; there is no tsconfig check today (Grep of ArchTests for `tsconfig`: no match). | No: it asserts a new invariant over a different file set; it copies the census structure. | New tsconfigs can land non-strict (27 of 76 are today) and nothing stops the count from growing, so strictness moves backwards silently. |
| (c) Scheduled refresh routine | `knowledge/REFRESH-PROCEDURE.md` (a manual monthly ritual). | Yes — it automates that procedure's Steps 1–5 and leaves Step 6 sign-off to a person; the procedure doc gets a pointer, not a rewrite. | The procedure has not run in six months (last full curation 2026-05-14), so the 6 knowledge-base skills serve guidance that the procedure itself says drifts within four months. |
| (d) Requirements-trace check | `adversarial-reviewer.md` checklist; Completeness Lint (criteria exist). | Yes — a line in the existing definition. | A change set can pass review while an acceptance criterion has no implementation, because nothing maps criteria to the diff. |
| (e) `SupersededApiCensusTests.cs` | `IlCallScan.cs` (the IL reference scanner it reuses); `[Obsolete]` (unusable under `TreatWarningsAsErrors` while callers remain). | Partly: it reuses `IlCallScan` and adds only the census and baseline; a new test class is needed for a new invariant. | New code keeps calling APIs that an ADR has already replaced, and nothing reports it until a migration finds the extra callers. |
| (f) `notes/csharp-lsp-trial.md` | Rejected shared index (§10.6). | n/a — a trial record inside this project, not repo surface. Adoption would be a separate, justified change. | The per-session question stays open indefinitely and keeps being re-argued without numbers. |

No new service, endpoint, DI registration, package reference or Dataverse column.

## 9. Placement, sequencing and constraints

- **Sub-agent write boundary (root §3).** Items 1, 2, 3, 5(a) and 5(d) edit `.claude/`; those tasks run in the main session, not in sub-agents (`project-pipeline` "MUST NOT dispatch a task that touches `.claude/`").
- **Agent cost (§8.5).** Every agent call names its model: `code-mapper` (sonnet) for the scan and package evidence; the one `adversarial-reviewer` pass (fable) at project close. No fan-out beyond that; the workflow size guideline is small.
- **Order.** Item 1 first (items 2 and 3 cite the index). Item 4 and the item-5 sub-items are independent and can run in parallel; 5(d) lands in the same change as item 3's reviewer check (same file). 5(b) and 5(e) add ArchTests, so per root §8 they run code-review + adr-check whatever their rigor level.
- **Recurring cost.** 5(c) adds a monthly scheduled agent run; its model and effort are stated in the routine definition (`.claude/constraints/agent-cost.md`). 5(f) is a one-off measurement in one worktree. Item 2 changes the pipeline that will build *this* project's tasks, so this project's own tasks are generated with the old Step 2; that is fine.
- **Validation.** The first project to run the new Step 2 after merge is the real test; r1 dry-runs the scan against two closed projects with known duplication (candidates: the set-regarding/field-mapping r1→r2 history and the RichTextEditor/Compose pair) and records whether it would have caught them.
- **Docs.** `docs/procedures/ci-cd-workflow.md` "Quality alerts" gains the new check; `.claude/skills/INDEX.md` / `docs/INDEX.md` pointers as needed.

## 10. Rejected alternatives

Do not re-adopt these without new evidence.

1. **Sonar / a Roslyn duplicate-code analyzer.** `Directory.Build.props` sets repo-wide `TreatWarningsAsErrors=true`; a duplication analyzer turns on as day-one build errors across ~940 BFF files. Run as warnings-only it is the jscpd sweep with more infrastructure.
2. **A jscpd hard gate** (fail CI over a duplication threshold). It repeats the god-class LOC ratchet that was **retired 2026-08-20** (`.claude/patterns/testing/god-class-ratchet.md`): it gates on a count proxy for a judgment-laden property, freezes existing values, and blocks normal work until someone bumps a waiver. Observation at exit 0 is the posture we converged on.
3. **ast-grep for C# structure.** `tests/Spaarke.ArchTests/SourceScan.cs` already does structural source scanning in blocking CI; a second structural engine adds a tool and a rule language without a new capability.
4. **A generated capability manifest.** ~940 BFF service files produce a listing too large for an agent to read and that names files, not capabilities. The hand-curated index (item 1) is the answer to the same need.
5. **A `PreToolUse` deny hook on new files** (e.g. refuse `Write` of a new `*Service.cs` without a justification). §16 requires hooks to be narrow, under 5 seconds and free of false positives; "is this new file a duplicate?" is semantic, so the hook would either be a name heuristic (false positives on every legitimate new file, then disabled) or slow. The `<justification>` gate sits at the right point — task authoring — already.
6. **A shared LSP / semantic-index MCP sidecar.** With ~17 active worktrees, one shared index is stale for every worktree but one, and N indices cost N builds. **Separate, still open:** a *per-session* C# LSP plugin (go-to-definition and find-references inside one worktree) is a different question — it helps navigation, not cross-project reuse. Item 5(f) runs a measured trial of it; this rejection does not cover it.

## 11. Open questions for the owner

| # | Question | Recommendation |
|---|---|---|
| OQ-1 | The brief has the `code-mapper` agent *write* `reuse-scan.md` and *judge* plan duplication; its definition forbids both. Keep the split (agent finds, orchestrator judges and writes), or create a new agent definition that writes? | Keep the split (§4.2). |
| OQ-2 | `code-mapper` runs at effort `low`. Concept/synonym/interface search is harder than "where is X used". Accept `low`, or run this one call at `medium` via a workflow `agent()` (the definition fixes effort for the Agent tool)? | Try `low` in the dry-run (§9); raise only if it misses a known duplicate. |
| OQ-3 | Should `Validate-TaskPoml.ps1` require the `CAPABILITY-INDEX row` / `no row` citation as an error immediately, or warn for a shadow window first? | Warn for two projects, then error. |
| OQ-4 | Sweep placement: a weekly job inside `nightly-health.yml` (extension, recommended by root §11) or a separate `weekly-duplication-sweep.yml` (simpler cron, isolates a new tool)? | Inside `nightly-health.yml`. |
| OQ-5 | Item 4 scope: does r1 only *decide* the 9 packages (records + ADR amendments for sanctions), or also *execute* the cheap consolidations (`document-operations`, `ai-context`, `legal-workspace` look cheapest)? | Decide all 9; execute only those with ≤ 3 consumers; file the rest. |
| OQ-6 | Should the reuse scan also run earlier, in `design-to-spec`, where re-scoping a duplicate is cheapest? The brief places it in `project-pipeline` Step 2. | Step 2 for r1 (it has the spec's §11 table to work from); revisit after the first two real runs. |
| OQ-7 | Root `CLAUDE.md` §11 names the three questions but not the index. Add a one-line pointer there ("`<existing>` starts at `.claude/patterns/CAPABILITY-INDEX.md`")? It would make root CLAUDE.md a hot-path touch. | Yes, one line; `task-create` alone is read only at task-authoring time. |
| OQ-8 | jscpd new-cluster floor (tokens, cross-area rule) for the alert. | Start at 50 tokens / different top-level areas; tune after four weeks of reports. |
| OQ-9 | Epic and portfolio registration: which Epic does this project sit under (the code-quality program, Epic #427, is the obvious parent)? | Epic #427. |
| OQ-10 | 5(b): which shared packages move to `strict` in r1, given that a migration can be large? | All shared packages whose strict build shows fewer than ~100 errors; split off and file the rest. |
| OQ-11 | 5(b): extend type-aware ESLint beyond the packages made strict in r1? | Not in r1; a second ratchet after the strict baseline shrinks. |
| OQ-12 | 5(c): a Claude Code scheduled cloud routine (does the update and opens a draft PR) or a GitHub Action (diffs only, opens an issue)? A cloud routine also needs a cloud environment with repo access. | Cloud routine, opening a draft PR only. |
| OQ-13 | 5(e): who owns the initial superseded-API list? The ADRs say "replaces" in prose, so populating the census takes judgment. | The planning tier drafts it from the ADRs; the owner approves the list before the test lands. |
| OQ-14 | 5(f): which plugin to trial, and the go/no-go thresholds (e.g. cold start < 60 s, < 1.5 GB per session)? | Set the thresholds before the trial so the result is not argued afterwards. |

## 12. Out of scope / future

- **Coding-agent eval set** (15–20 replayable tasks with known-good outcomes, used to compare models, effort levels and procedure changes). It came from the same review, but its effort is L, so the owner left it out of r1. It is the natural next project once items 2 and 5(d) have changed how reviews and plans are checked, because it would measure whether they work.
- Repo-wide type-aware ESLint (OQ-11).
- Executing large package consolidations from item 4 (filed as their own projects).

## 13. Success criteria

1. `.claude/patterns/CAPABILITY-INDEX.md` exists with ~30 rows; `CapabilityIndexTests` passes and its must-fire control fails on a seeded dead path.
2. `task-create` 3.5.6 and `code-review` 6.6 cite the index; `Validate-TaskPoml.ps1` checks the citation (at the strength OQ-3 sets).
3. `project-pipeline` Step 2 runs the reuse scan, writes `notes/reuse-scan.md`, stops on ≥ 2 candidates, and the dry-run against two closed projects is recorded with what it caught and missed.
4. The weekly sweep runs, exits 0, excludes `.claude/worktrees/agent-*`, and opens/closes exactly one `quality-alert` issue (`weekly/duplication-sweep`) based on clusters not in the baseline.
5. `adversarial-reviewer.md` carries the capability-duplication check pointing at the index.
6. Each of the 9 grandfathered packages has an owner-approved decision record; every *sanction* has landed as an ADR-012 amendment with the census table and `SharedPackageCensusTests` updated in the same PR; consolidations are executed or filed per OQ-5.
7. (a) `.mcp.json` carries the Learn MCP server, and one `researcher` run cites a Learn MCP result. (b) `TsconfigStrictnessCensusTests` passes with its controls, and the baseline is smaller than 27 at project close. (c) The scheduled routine has produced at least one draft refresh PR (or issue) that the owner reviewed. (d) The reviewer reports a per-criterion trace when given a POML. (e) `SupersededApiCensusTests` passes with an owner-approved census and its controls. (f) `notes/csharp-lsp-trial.md` records the measurements and the owner's adopt or no-adopt decision.
