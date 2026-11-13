# reuse-governance-tightening-r1 — Design

> **Status**: Design complete; all open questions are decided and the ROI review trims are applied. Awaiting the owner's final approval before `/design-to-spec`.
> **Revised**: 2026-11-10 — added workstream 5, "Quality tooling quick wins" (§7), at the owner's request: the low-priority review items that are not large-effort tickets. Revised again the same day with owner answers: OQ-10 to OQ-16 decided; 5(e) rewritten as a full superseded-API process; 5(f) now explains how the language server would be used and fixes the go/no-go thresholds. Then OQ-1 to OQ-7 and OQ-9 decided; the reuse scan moved to `/design-to-spec` (OQ-6); OQ-8 decided (alert at ≥ 100 tokens and ≥ 10 lines across areas). All open questions are now decided. Then trimmed by an independent Fable ROI and complexity review (owner-approved, same day). Each cut is recorded with its reason in §10.7. In summary: no new ArchTests, baselines or scheduled routines; 5(b) deferred; 5(e) reduced to re-arming `CS0618`; item 4 executes at most three packages; and the reuse scan is gated on its dry-run.
> **Created**: 2026-11-10 (recreated; the 2026-09-02 draft was lost: it was in no worktree and never committed).
> **Sources**: the owner's 2026-11-10 evaluation of an external "Claude Code Quality Recommendations" document (items B1, B2, B4, B7, B8); the Sept 2026 owner decision recorded in auto-memory `project_reuse-governance-tightening-r1.md` (the only surviving summary of the lost draft); the current repo at `d64a45071b`.
> **Hot-path surfaces**: skill-directives (Y — `design-to-spec`, `task-create`, `project-pipeline`, `code-review`, `.claude/agents/adversarial-reviewer.md`, `.claude/agents/researcher.md`), ci-workflows (Y — the sweep job and the knowledge-refresh watchdog), BFF (Y — item 5(e) edits `Directory.Build.props` and may migrate obsolete-API callers; no new surface), SpaarkeAi (Y — item 4's three executed actions change `src/solutions/SpaarkeAi`, which consumes `@spaarke/ai-context`, `@spaarke/document-operations` and `@spaarke/legal-workspace`; corrected at `/design-to-spec`, 2026-11-10), root CLAUDE.md (Y — one pointer line in §11, OQ-7). **Portfolio**: Epic #427, the code-quality program (OQ-9).

```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>Y</spaarkeai>
  <ci-workflows>Y</ci-workflows>
  <skill-directives>Y</skill-directives>
  <root-claude-md>Y</root-claude-md>
</hot-path-declaration>
```

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

This project closes G1–G4 with the lightest mechanism that works for each. It deliberately does **not** add a blocking duplication gate (§10). A fifth workstream (§7) bundles small quality-tooling items from the same review that the owner asked to include because none is a large-effort ticket. Five of the six stay in r1; one is deferred (§7.2).

## 2. Goals and non-goals

**Goals**
1. A task author, reviewer or scanning agent can go from a *concept* ("send an email", "resolve the regarding record", "open a modal") to the canonical component, its ADR and its extension point in one read.
2. A project's design is checked for capability duplication once, before tasks are generated, and the result is a file that later steps cite.
3. Duplication that lands anyway becomes visible within a week, as one alert, without blocking anyone's build.
4. Each grandfathered shared package gets an owner decision: sanction, consolidate or retire.
5. Five small tooling items (§7) land with the same posture: use the existing mechanism where one does the job, and run a measured trial where the answer is unknown.

**Non-goals**
- No change to the §11 three questions or to the `<justification>` schema.
- No blocking gate on duplication counts (§10.2).
- No generated or exhaustive capability catalogue (§10.4).
- No executing the consolidations themselves inside r1 beyond the ones the owner marks cheap (OQ-5); larger consolidations are filed as their own projects.
- No new BFF surface. Item 5(e) edits `Directory.Build.props` and may touch BFF source: it migrates callers of obsolete members and, where needed, adds a `DiagnosticId` to an existing `[Obsolete]`. It adds no endpoint, service, registration or package, so the publish-size delta should be zero; the task still measures it, per root CLAUDE.md §10, because it edits files under `Sprk.Bff.Api`.

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

- Every canonical-component path and every ADR in the index is written as a plain backticked repo path, e.g. `` `.claude/adr/ADR-028-spaarke-auth-architecture.md` ``. A package is named by its folder (`` `src/client/shared/...` ``), not only by its `@spaarke/*` name, and a symbol goes in its own cell, not on the path. This puts the file under the existing path checker (§3.5).
- `task-create` Step 3.5.6: `<existing>` **consults the capability index first**, then Greps. The answer cites either `CAPABILITY-INDEX row N` plus file:line, or `CAPABILITY-INDEX: no row` plus the Grep command shown (today's rule).
- `code-review` Step 6.6 and the adversarial reviewer (item 3) use the same index.
- Root `CLAUDE.md` §11 gets one line pointing at the index ("`<existing>` starts at `.claude/patterns/CAPABILITY-INDEX.md`"). **Decided (OQ-7).** This makes root CLAUDE.md a hot-path touch; the edit follows the maintainer notes in that file's header comment (root §18).
- Row maintenance: a project that adds a canonical component, or changes an extension point, updates the row in the same PR. `doc-drift-audit` already runs at project transitions; it gets the index added to its scan list.

### 3.4 Why hand-curated

A generated manifest was rejected earlier and stays rejected (§10.4): the BFF alone had ~940 service files in September, which is a listing, not an index, and too large for an agent to read usefully. Thirty rows that a human chose are the point. The cost of curation is bounded by the freshness guard in §3.5.

### 3.5 Enforcement-ladder choice (§16)

| Concern | Rung | Mechanism |
|---|---|---|
| Rows point at paths that no longer exist | **Existing source-scan guard** (no new test) | `scripts/quality/Test-InstructionPaths.ps1` already checks every backticked repo path in `.claude/patterns/*.md` and `.claude/adr/*.md` (line 41). It runs ratcheted in CI (`ci-tier2-advisory.yml`) and in `doc-drift-audit`. Writing the index's paths and ADRs as backticked repo paths puts the index under that check. The task proves it once: a seeded dead path in a local copy is reported. |
| `<existing>` ignores the index | **Prose**, checked by `code-review` 6.6 | Not linted. The existing `<justification>` check in `Validate-TaskPoml.ps1` is already warning-only, so a second warning nobody reads adds nothing (ROI review 2026-11-10; revises OQ-3). |
| Whether the row chosen is the *right* one | **Prose** | Judgment; reason it cannot be mechanised: choosing the canonical component for a concept is semantic. Checked by `code-review` 6.6. |

## 4. Item 2 — Reuse scan, first in `/design-to-spec`, confirmed in `project-pipeline` Step 2 (B2, B4)

### 4.0 Where it runs — decided (OQ-6)

The scan runs **once, in `/design-to-spec` Step 3**. There it replaces the "quick Grep/Glob for an existing component" exception that step already makes for the spec's §11 table. That is the cheapest point to catch a duplicating design, before the spec is written around it.
- **Two or more candidates** become a question in design-to-spec's review step (Step 5), recorded under the spec's "Owner Clarifications".
- **`project-pipeline` Step 2 item 5** runs the full scan **only when `notes/reuse-scan.md` does not exist** (for example, a project that arrives with a hand-written `spec.md`). Otherwise it cites the file. There is no partial re-scan of changed capabilities (ROI review 2026-11-10).
- **design-to-spec's own contract changes.** Its SKILL.md says it "MUST NOT do comprehensive resource discovery" and that Step 3 loads ADR constraints only (lines 40 and 162). The task rewrites both lines: the one `code-mapper` scan is the single allowed discovery pass, and everything else stays with project-pipeline.

The cost stays at one scan per project.

**Go/no-go before wiring (ROI review).** The dry-run (§9) runs first, against two closed projects with known duplication. If a `low`-effort scan misses both, the skill edits in this item are not made. The result goes to the owner with the token cost, per OQ-2.

### 4.1 What

The scan's steps (run by `/design-to-spec` Step 3, or in full by `project-pipeline` Step 2 when no scan exists):

1. The orchestrator extracts from `spec.md`/`design.md` the list of **capabilities the project introduces** (not file names): each new service, endpoint, package, Dataverse column and UI surface, expressed as a concept with its likely synonyms and the interface it would expose.
2. It launches **one `code-mapper` agent** (model `sonnet`; see OQ-2 on effort) with that list, the capability index, and instructions to search **by concept, synonym and interface shape**, not by the name the spec uses: e.g. for "matter-number generator" also search `INumberGenerator`, `sequence`, `autonumber`, `alternate key`, and for an interface, methods with the same signature.
3. The agent returns facts in a fixed schema (below). Per its definition it does not judge and does not write files.
4. The orchestrator (planning tier) judges the facts and writes **`projects/{name}/notes/reuse-scan.md`**, including a **plan-level duplication check**: does any capability in the design duplicate an existing one, or does the design add a package/service where the index row says "extend X"?
5. **Two or more viable candidates for one capability → ask the human** (in design-to-spec: a Step 5 review question; in project-pipeline, interactive: a question; autonomous: STOP at this gate with the options, like the other safety gates). One candidate → record it as the `extend` target. Zero → record "none found" with the searches run.
6. `task-create` Step 3.5.6 `<existing>` **cites `notes/reuse-scan.md`** (the row for that capability) when the file exists.

Return schema (passed as `schema` in a workflow `agent()` call, or required in the prompt):

```json
{ "capabilities": [ {
    "concept": "…", "searched": ["terms and patterns actually run"],
    "candidates": [ { "path": "…:line", "symbol": "…", "match": "name|synonym|interface|index-row", "evidence": "…" } ] } ],
  "notCovered": "what was not searched" }
```

### 4.2 A deviation from the brief, and why

The brief says the `code-mapper` agent *writes* `notes/reuse-scan.md` and *checks the plan for duplication*. `.claude/agents/code-mapper.md` forbids both: it is read-only ("Don't edit or create files in any checkout") and it reports facts, leaving judgment to the caller. The design keeps the agent definition intact and splits the work: **the agent finds, the orchestrator judges and writes**. This also keeps the judgment on the planning tier, as root `CLAUDE.md` §8.5 and `project-pipeline` "Model and effort" require, and costs one Sonnet agent plus a short orchestrator pass. (Writing under `projects/` is not blocked by the §3 sub-agent boundary; the reason is the agent's own contract.) **Decided (OQ-1): keep the split.**

A second consequence: both skills run their discovery in Plan Mode (read-only). The scan runs during discovery, and `reuse-scan.md` is written when the skill starts writing files: design-to-spec Step 4, or project-pipeline Step 2 Part 2.

**Effort — decided (OQ-2): `low`, the definition's default.** The dry-run (§9) records the scan's token use and whether it missed a known duplicate. If it misses one, the change to `medium` is proposed to the owner *with* the measured token cost per scan and the projected monthly cost at the current project rate, before anything is changed. It is one Sonnet call per project, so the delta is expected to be small, but the owner sees the number first.

### 4.3 Enforcement-ladder choice

| Concern | Rung | Mechanism |
|---|---|---|
| The scan is skipped | **Built-in fallback** (no lint) | A missing `notes/reuse-scan.md` makes project-pipeline Step 2 run the full scan, so a project cannot reach Step 3 without one. A separate absence lint would never fire. |
| `<existing>` does not cite the scan | **Prose**, checked by `code-review` 6.6 | Same reasoning as §3.5: no new lint rule. |
| The ≥ 2-candidate human decision | **Prose + gate** | A judgment call by definition; the gate is that the pipeline stops, not that a tool decides. |

## 5. Item 3 — Weekly duplication sweep (B7, B8)

### 5.1 What

**Observation only. It never fails a build or a PR.**

1. **Syntactic sweep**: `jscpd` (exact version pinned, run via `npx`, no `package.json` change) over `src/**` for TS/TSX and C#, `--exitCode 0`. It writes a JSON + Markdown report as a workflow artifact.
2. **Exclusions**: `**/node_modules/**`, build output, generated code, `**/*.test.*`, `tests/**`, and **`.claude/worktrees/agent-*/**`**. Agent worktrees hold full source-tree copies and would report every file as duplicated. In CI that folder is absent (`.claude/worktrees/` is gitignored), but the exclusion lives in the shared `.jscpd.json` so that a local run gives the same answer as CI.
3. **No baseline file (ROI review 2026-11-10).** A committed baseline of known clusters, fingerprinted by hash, would raise a false "new" cluster every time someone edits a file that is already cloned. Instead, the trigger is stateless: a cluster is **new** when at least one of its files changed in the last 7 days (`git log --since=7.days --name-only`). "Changed", not only "added", so code pasted into an existing file still shows up.
4. **One alert**: the sweep calls `.github/actions/quality-alert` with check `weekly/duplication-sweep`. Status `failing` when the run finds a **new** cluster above a size floor (≥ 100 tokens **and** ≥ 10 lines, in ≥ 2 files in different top-level areas — decided OQ-8; the full report keeps jscpd's 50 / 5 defaults for browsing). Otherwise the status is `ok`, which closes the alert. A permanently open alert would be noise in the `SessionStart` quality banner, so "new this week", not "any duplication", is the trigger. The summary names each new cluster with its two paths and the capability-index row it most likely belongs to, if any.
5. **Reviewer item**: `.claude/agents/adversarial-reviewer.md` gains one check: *"New surface that duplicates a capability in `.claude/patterns/CAPABILITY-INDEX.md` (same concept under another name, or a new package/service where the row says extend). Class F2 ('duplicates an existing mechanism', task-execute's class table) when it adds a second implementation of an indexed capability; K-class when it is a judgment call."* This is the capability-level half that jscpd cannot see.

Where it runs: **extend `nightly-health.yml`** with a weekly-cadence job rather than adding a workflow (root §11 "extension"), reusing its quality-alert matrix plumbing. **Decided (OQ-4).**

### 5.2 Enforcement-ladder choice

| Concern | Rung | Mechanism |
|---|---|---|
| Syntactic clones landing | **CI observation** (deliberately below the "lint rule" rung) | Weekly job, exit 0, one quality-alert issue. Not a gate: §10.2. |
| Capability-level duplication in a change set | **Prose** in the reviewer definition | Reason it cannot be mechanised: "same capability under a different name" is semantic; jscpd and ArchTests see only shape. |
| Phantom duplication from agent worktrees | **Config** | Exclusion in `.jscpd.json`. No fixture control: `.claude/worktrees/` is gitignored and absent in CI; the exclusion exists only so a local run agrees with CI. |

## 6. Item 4 — Review backlog for the 9 grandfathered packages

### 6.1 What

For each package between the `shared-package-census` markers in `.claude/adr/ADR-012-shared-components.md` with status *Grandfathered — under review*, write a decision record as one section of a single file, `projects/reuse-governance-tightening-r1/notes/package-reviews.md` (nine sections, not nine files). Each section gives the evidence for ADR-012's three tests (distinct contract, quarantine-worthy dependency, cross-surface reuse), its consumer count, and one recommendation:

- **Sanction** → an ADR-012 amendment (Path B, §6.5) that names the package and its contract outside the census table; flip the row and the `SharedPackageCensusTests` entry to Sanctioned with the ADR citation (the test checks both).
- **Consolidate** → into a named sanctioned package; a migration plan (consumers' `package.json`, import paths, PCF deep-import paths, build config). Only those in r1's execution scope (below) run in r1; the others are filed as their own projects.
- **Retire** → no remaining reason to exist (1 consumer, or a pure re-export); fold into its consumer.

The owner decides each one. Evidence gathering is `code-mapper` work (sonnet, low); the recommendation is planning-tier judgment.

**Scope rule — decided (OQ-5), narrowed by the ROI review (2026-11-10), with a guard against burying issues.** r1 decides all 9 packages. It carries out at most three of the resulting actions: the two single-consumer packages (`ai-context`, `legal-workspace`) and `document-operations`. Every other consolidation or retirement is filed as its own project. The original "≤ 3 consumers" rule would have covered 6 of the 9 by `package.json` count (`ai-context` 1, `legal-workspace` 1, `compose-components` 2, `document-operations` 3, `ai-outputs` 3, `daily-briefing-components` 3). That is up to five consolidations, not a cheap tail. The task re-verifies these counts before acting. The limit is on **effort only, never significance**:
- Each decision record has a **Findings** subsection, graded by severity. Examples: two packages implement the same capability with different behaviour (a user could see different results on two surfaces), a duplicate that has already drifted, or a shared package that breaks an ADR-012 MUST.
- Any finding that is a defect on a real path is **fixed in this project or reported to the owner by name** in the project report and the PR, per root CLAUDE.md §8.5 ("every defect found is fixed in scope, or filed and reported"). Filing it is not enough; the owner sees it.
- A filed consolidation's GitHub issue carries the decision record's findings and severity, so the backlog says *why* it matters, not just "consolidate X".

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

Added at the owner's request: the low-priority items from the 2026-11-10 review that are not large-effort tickets. Sizes are the owner's (S / M). Each sub-item is independent of items 1–4 and of the others, and can run in parallel. **ROI review (2026-11-10):**
- (b) is deferred until item 4 lands.
- (c) keeps only its watchdog.
- (e) shrinks to re-arming `CS0618`.
- (f) cuts its gates to three and is time-boxed.
- Each sub-item ships as its own PR, so the reuse work's review is not mixed with unrelated tooling.

### 7.1 (a) Microsoft Learn MCP server for research — S

**What.** Add Microsoft's Learn MCP server (remote HTTP; the task confirms the current endpoint from Learn itself) to `.mcp.json` next to the existing `dataverse` server. Update `.claude/agents/researcher.md` so that, after its local sources (its memory, `knowledge/`, `docs/`, ADRs), it queries the Learn MCP **before** `WebSearch`/`WebFetch`.

**A detail the brief does not mention.** `researcher.md` restricts its tools in frontmatter (`tools: Read, Bash, Glob, Grep, WebSearch, WebFetch`). Unless the Learn server's tool names are added to that list, the agent cannot call the server, whatever its prose says. The task adds them and proves it with one research call that cites a Learn MCP result.

**Pre-filtering: not needed, and scoping happens at query time instead (owner Q1, 2026-11-10).** The Learn MCP server is search-on-demand: each call returns a few relevant doc chunks or one fetched page, and nothing is loaded up front. The size of Learn therefore costs nothing until a query runs, and the server has no setting that limits it to a subset of topics. Relevance comes from three rules in the researcher prose:
1. `knowledge/` stays first. Its topic folders (Foundry, SharePoint Embedded, Dataverse MCP, Azure AI Search, M365 Copilot, Fluent UI v9, …) are the curated, Spaarke-relevant layer, and the Learn MCP is the fallback for what they do not cover or for checking whether they are current.
2. Queries name the product and the API surface ("SharePoint Embedded container permissions Graph API"), never a bare concept ("permissions"), so results stay inside the Spaarke stack.
3. Search, then fetch the one or two pages that matter, rather than reading many search hits.

If the owner later wants hard scoping, the place for it is the researcher's topic list, not the server.

**Constraints.** The server sends queries to Microsoft. The queries are about public platform documentation, but the researcher prose gets one line saying they must never contain customer data, tenant names or secrets. A project-scoped `.mcp.json` server needs one-time user approval on each machine; the README line says so.

**Enforcement rung: prose.** "Prefer Learn over web search" is a choice about research order, so a check cannot enforce it. The tool-list half is enforced by the platform: without the entry, the call fails.

### 7.2 (b) TypeScript strictness ratchet — DEFERRED (not in r1)

**Deferred by the ROI review (2026-11-10); revises OQ-10.** Three reasons:
- It does not address G1–G4.
- Item 4 may merge or retire up to 9 of the 15 shared packages it would convert, so doing it first spends effort on packages that may disappear.
- Its census scope is unsettled: the review counted 118 `tsconfig*.json` under `src/`, against the 76 below.

It runs after item 4, on the packages that remain, as a follow-up project (§12). The design below is kept for that project.

**What.** In the owner's 2026-11-10 count, 49 of 76 tsconfigs are strict and 4 configs use type-aware ESLint.
1. Add a census ArchTest, `TsconfigStrictnessCensusTests.cs`. It enumerates every `tsconfig*.json` under `src/` (excluding `node_modules`, build output and `.claude/worktrees/**`). It resolves each `extends` chain, parsing the files as JSONC because tsconfig allows comments, and lists those where `strict` is not true. The list is a **ratchet baseline**: a new non-strict tsconfig fails, and a file that becomes strict must be removed from the baseline, so the list can only shrink. Each baseline entry carries a one-line reason, per the `tests/CLAUDE.md` census rules.
2. Move the shared packages under `src/client/shared/` to `strict: true` first. They are imported by every surface, so their loose types reach the most code. Each package is its own task; a package whose strict migration exceeds about a day of errors is split off and filed.
3. Type-aware ESLint is **not** extended in r1 beyond the packages made strict here. Extending it repo-wide is a later ratchet; see OQ-11.

**Enforcement rung: ArchTest + ratchet baseline.** It has must-fire controls (a seeded non-strict tsconfig, and a strict file that is still listed in the baseline) and a must-not-fire control (a tsconfig that inherits `strict` through `extends`).

### 7.3 (c) Staleness watchdog for the `knowledge/` refresh — S

**What.** `knowledge/REFRESH-PROCEDURE.md` prescribes a monthly refresh. `knowledge/REFRESH-LOG.md` has never recorded a full monthly refresh after the initial curation on 2026-05-14. Topics have instead been refreshed on demand, by projects that needed them (2026-07-14, 2026-08-26). A small job in `nightly-health.yml` reads the newest dated `## YYYY-MM-DD` heading in `REFRESH-LOG.md`, of any kind. It reports `quality-alert` check `monthly/knowledge-refresh` as failing when that date is more than 35 days old. It uses the existing alert plumbing. The alert appears in the `SessionStart` quality banner, and whoever acts on it runs the procedure by hand. On the day it lands it fires, because the newest entry is 2026-08-26; that is intended. `REFRESH-PROCEDURE.md` gets one line naming the watchdog.

**Cloud routine dropped (ROI review 2026-11-10; revises OQ-12, makes OQ-16 moot).** A monthly scheduled routine (Sonnet at `high`) needs a cloud environment set up outside the repo and a draft PR for the owner to review every month. That is a recurring cost for work the on-demand pattern already does when a project needs it. The alert gives the prompt; a person or a session runs the refresh. If the alert stays open and ignored for two months, the routine can be reconsidered with that evidence.

**Enforcement rung: CI watchdog (quality-alert).** It replaces a prose cadence that has not been followed for six months with a visible signal; the content update stays a person's job.

### 7.4 (d) Requirements-trace check in the adversarial reviewer — S

**What.** One check is added to `.claude/agents/adversarial-reviewer.md`. When the caller supplies the task POML or `spec.md`, map each acceptance criterion (`<acceptance-criteria>` or the spec's Success Criteria) to the diff hunk and test that satisfy it. Report each criterion as *met (file:line + test)*, *not met* or *not verifiable from the diff*. A *not met* criterion inside the change set's scope is **F4** (task-execute's class for an unmet acceptance criterion); *not verifiable* is K-class with a reason. The definition also tells the caller to pass the POML/spec path, because the reviewer cannot trace criteria it was not given. `task-execute` Step 9.5, which launches the reviewer, passes it.

**Enforcement rung: prose** in the agent definition. Whether a hunk satisfies a criterion is semantic. The Completeness Lint already guarantees that criteria exist and are a closed set (`task-create` Step 3.5.5b), which is what makes the trace possible.

Items (d) and 3's capability-duplication check edit the same file; they land in one change.

### 7.5 (e) Re-arm the obsolete-API check — S

**Revised by the ROI review (2026-11-10).** The earlier draft built a full superseded-API process: two Spaarke diagnostic IDs, an IL caller-ratchet ArchTest with its baseline, an ADR backfill, and new marking steps in `task-create` and `code-review`. The review found that the reason for the problem no longer exists. Once one setting is fixed, the compiler does most of that work.

**What is broken today (verified 2026-11-10).**
- `Directory.Build.props` sets `TreatWarningsAsErrors=true`, but line 41 adds `CS0618` to `WarningsNotAsErrors`. A new call to any obsolete API, whether Spaarke, framework or NuGet, is therefore a warning nobody sees in a green build.
- The comment that justifies it (line 18: "14x obsolete `DemoProvisioningOptions.Environments`/`DefaultEnvironment`") is stale. `DemoProvisioningOptions.cs` no longer has any `[Obsolete]` member.
- `src/` has 21 `[Obsolete]` attributes in 15 files. 17 are provisioning rejection-code constants kept for external compatibility; about 4 are true supersessions. None uses `DiagnosticId`.

**The fix.**
1. Remove `CS0618` from `WarningsNotAsErrors`, along with the stale comment at line 18. Run a clean Release build of `Spaarke.sln`.
2. Fix what breaks:
   - A Spaarke caller of a superseded member migrates to the replacement.
   - Framework and NuGet obsoletions that take under an hour each are fixed. The rest are filed and listed in the PR (OQ-15).
3. Some callers cannot migrate now: a filed framework obsoletion, or an in-repo caller of a compatibility constant. Only for those, give the member a `DiagnosticId` (for example `SPRK001`) and suppress that one ID narrowly, with a one-line reason. The suppression goes at the call site, or in `WarningsNotAsErrors` by that ID. The blanket `CS0618` suppression does not come back.
4. A comment beside the setting in `Directory.Build.props` says that marking a member `[Obsolete]` now fails every existing caller, so the change that marks it must migrate the callers or use step 3. The comment sits where the rule bites, so there are no new skill steps.

**Scope.** C# only. TypeScript's `@deprecated` needs type-aware ESLint (`@typescript-eslint/no-deprecated`), which is out of this project (OQ-11, §12).

**Enforcement rung: compiler.** With `TreatWarningsAsErrors`, a plain `[Obsolete]` fails any new caller at compile time. That is a higher rung than an ArchTest, with no new test, baseline or diagnostic convention to maintain. The publish-size delta is still measured if the fix touches `Sprk.Bff.Api` (root §10).

### 7.6 (f) Per-session C# language-server trial — S

**How it would be used (owner Q4).** Today an agent answers code-structure questions with text search: Grep for a name, read the hits, and guess which ones are the real call or implementation. A C# language server is the same Roslyn engine that powers IDE navigation. Through a Claude Code LSP plugin (C# plugins in the official marketplace wrap a server such as `csharp-ls`; the trial confirms which one), the agent gets a code-intelligence tool with compiler-exact answers:
- **Find references**: every real call site of a method, including calls through interfaces, DI, extension methods and lambdas, with no matches from comments or same-named symbols.
- **Find implementations**: every class that implements an interface. This is the exact question item 2's reuse scan asks ("is there already an `INumberGenerator`?").
- **Go to definition / hover**: the actual type of a receiver, through `var`, generics and overloads.
- **Diagnostics after an edit**: compile errors reported seconds after the agent saves a file, before a full `dotnet build`.

The expected gain is fewer wrong answers and fewer tool calls on navigation questions. The expected cost is a background process per session, which takes time to load the solution and holds memory while it runs.

**Making sure it is actually useful.** The trial is an A/B comparison, not an impression. Ten fixed questions are taken from real recent sessions: 4 find-references, 3 find-implementations, 2 receiver-type/definition, and 1 "what breaks if I change this signature". The same model answers each twice in one worktree, once Grep-only and once with the LSP tool allowed. The trial records correctness against a hand-checked answer, tool calls, tokens and wall time. The result goes in `notes/csharp-lsp-trial.md` with the owner's decision. Adoption is then a separate change: the plugin setting, plus a one-time install of the language-server binary on each machine.

**Time-box: half a day (ROI review 2026-11-10).** If the plugin cannot be made to answer on `Sprk.Bff.Api` within that time, the result is "no-adopt, not workable today", and the trial stops.

**Go / no-go thresholds — decided (OQ-14), cut to three by the ROI review.** These are the three gates that decide adoption. Tool calls, tokens, wall time and cold-start time are still recorded for context, but they are not gates. All three must hold:

| Gate | Threshold |
|---|---|
| Correctness | ≥ 9 of 10 questions correct with LSP, and none worse than the Grep answer |
| False "no references" | **Zero.** A false negative is the dangerous failure for reuse decisions, because it says "nothing exists" when something does |
| Memory | ≤ 2 GB resident per session at steady state; with 3 concurrent sessions, ≥ 25% of machine RAM still free |

If memory fails but usefulness passes, the fallback is opt-in per session (enabled only for navigation-heavy work such as item 2's scans), not a project-wide setting.

**Relation to the rejected shared index (§10.6).** The September rejection was about one **shared** index serving about 17 worktrees, which is stale for all but one of them. A per-session plugin indexes only its own worktree, so it does not have that staleness problem. Its open risk is cost: N concurrent sessions run N language servers on one machine. The trial measures exactly that cost, and does not reopen the shared-index decision.

**Enforcement rung: n/a.** This is a measured decision, not a rule. If adopted, the plugin setting is the mechanism.

### 7.7 Enforcement summary for item 5

| Sub-item | Rung | Why not higher |
|---|---|---|
| (a) Learn MCP | Platform (tool allow-list) + prose | Research order is judgment |
| (b) TS strictness | Deferred (follow-up project) | — |
| (c) Knowledge refresh | CI watchdog (quality-alert) | The content update needs a person; the alert is the prompt |
| (d) Requirements trace | Prose (reviewer) | Semantic mapping |
| (e) Obsolete APIs | Compiler: `CS0618` re-armed under `TreatWarningsAsErrors` | Already the top rung |
| (f) C# LSP | n/a (trial) | Decision, not rule |

## 8. Root CLAUDE.md §11 justification for each new surface

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `.claude/patterns/CAPABILITY-INDEX.md` | `.claude/patterns/INDEX.md` and its 94 pointer files (verified 2026-11-10) map *task type → pattern*, not *concept/synonym → canonical component*. | No: the pattern files are capped at 25 lines each and organised by domain, so no file can hold a cross-domain concept lookup; a 30-row table added to `INDEX.md` would double its length and mix two lookups. | `<existing>` keeps depending on the author guessing the right name: `task-create` 3.5.6 passes with "none found" for a capability that exists under another name (G1). |
| Reuse scan in `/design-to-spec` Step 3 (confirmed in `project-pipeline` Step 2) + `notes/reuse-scan.md` | design-to-spec Step 3 already makes a "quick Grep/Glob" exception for the spec's §11 table; pipeline Step 2 item 5 already asks for the §11 "Existing" answer per new surface. | Yes — this **extends both existing steps** rather than adding one: it changes *how* the answer is found (concept/synonym/interface search by one agent) and *where it is recorded* (a file later steps cite). | A design that re-implements an existing capability passes every per-task gate (G2), because each task's `<existing>` is answered locally after the plan is fixed. |
| Weekly sweep job | `nightly-health.yml` (8 quality-alert checks), `scripts/report-large-server-files.ps1` (observation-only size report). | Yes — a job inside `nightly-health.yml` on a weekly cron, reusing its quality-alert step. | Parallel worktrees can land the same code twice and nothing reports it until someone trips over it (G3). |
| `.jscpd.json` | None found (a repo-wide `jscpd` Grep timed out at 20 s; to re-check in task 1 with a scoped search). | No: configuration for a tool the repo does not use yet. No baseline file (§5.1.3). | Without the shared config, local and CI runs disagree (agent-worktree phantoms) and the size floor lives only in the workflow. |
| One check in `adversarial-reviewer.md` | The definition's checklist. | Yes — a line added to the existing list (modification of existing surface). | Capability-level duplication in a change set is invisible to every syntactic tool. |
| `notes/package-reviews.md` (one file, nine sections) | ADR-012 census "reason" column (one line each). | No: one line cannot hold the three-test evidence and a migration plan. | The 9 grandfathered packages stay grandfathered indefinitely (G4); "under review" with no review is the old "etc." again. |
| (a) Learn MCP entry in `.mcp.json` + `researcher.md` tool list | `.mcp.json` (one server: `dataverse`); `researcher.md` already prefers `learn.microsoft.com`, reached through `WebFetch`. | Yes — one entry added to the existing file and one tool-list and prose change to the existing agent; no new file. | The researcher scrapes Learn pages through generic web fetch, which returns whole pages instead of targeted doc chunks and loses to a moved URL (REFRESH-PROCEDURE.md recorded 14+ moved Learn URLs in four months). |
| (b) `TsconfigStrictnessCensusTests.cs` | — | Deferred to a follow-up project (§7.2); no surface in r1. | — |
| (c) Watchdog job | `knowledge/REFRESH-PROCEDURE.md` (a manual monthly ritual); `nightly-health.yml` quality-alert matrix. | Yes — one more check in the existing matrix; the procedure doc gets a one-line pointer. | The procedure has not run in six months (last full curation 2026-05-14), so the 6 knowledge-base skills serve guidance that the procedure itself says drifts within four months, and nothing says so. |
| (d) Requirements-trace check | `adversarial-reviewer.md` checklist; Completeness Lint (criteria exist). | Yes — a line in the existing definition. | A change set can pass review while an acceptance criterion has no implementation, because nothing maps criteria to the diff. |
| (e) `Directory.Build.props` change (remove the `CS0618` downgrade) | `TreatWarningsAsErrors=true` and the standard `[Obsolete]` attribute (21 uses in `src/`). | Yes — pure modification of existing configuration; no new surface. A `DiagnosticId` is added only to members whose callers cannot migrate now. | With `CS0618` downgraded repo-wide, a new call to *any* obsolete API, including framework obsoletions, passes a green build unseen. |
| (f) `notes/csharp-lsp-trial.md` | Rejected shared index (§10.6). | n/a — a trial record inside this project, not repo surface. Adoption would be a separate, justified change. | The per-session question stays open indefinitely and keeps being re-argued without numbers. |

No new service, endpoint, DI registration, package reference or Dataverse column.

## 9. Placement, sequencing and constraints

- **Sub-agent write boundary (root §3).** Items 1, 2, 3, 5(a) and 5(d) edit `.claude/`; those tasks run in the main session, not in sub-agents (`project-pipeline` "MUST NOT dispatch a task that touches `.claude/`").
- **Agent cost (§8.5).** Every agent call names its model: `code-mapper` (sonnet) for the scan and package evidence; the one `adversarial-reviewer` pass (fable) at project close. No fan-out beyond that; the workflow size guideline is small.
- **Order.** Item 1 first (items 2 and 3 cite the index). Item 2's dry-run comes before its skill edits and decides whether they are made (§4.0). Item 4 and the item-5 sub-items are independent and can run in parallel. 5(d) lands in the same change as item 3's reviewer check, because they edit the same file. 5(e) changes the build configuration, so it runs code-review + adr-check whatever its rigor level.
- **PRs.** Items 1–4 form the reuse change set and get the project's one `adversarial-reviewer` pass. Each item-5 sub-item ships as its own PR with its own `code-review`, so unrelated tooling does not dilute that review.
- **Recurring cost.** None beyond the weekly sweep and the nightly watchdog check: no scheduled agent runs. 5(f) is a one-off, half-day measurement in one worktree. Item 2 changes the pipeline that will build *this* project's tasks, so this project's own tasks are generated with the old Step 2; that is fine.
- **Validation.** The dry-run runs the scan against two closed projects with known duplication and records whether it would have caught them and its token use. Candidates: the set-regarding/field-mapping r1→r2 history, and the RichTextEditor/Compose pair. It is the go/no-go for item 2's wiring. After merge, the first project to run the new design-to-spec Step 3 is the real test.
- **Docs.** `docs/procedures/ci-cd-workflow.md` "Quality alerts" gains the new check; `.claude/skills/INDEX.md` / `docs/INDEX.md` pointers as needed.

## 10. Rejected alternatives

Do not re-adopt these without new evidence.

1. **Sonar / a Roslyn duplicate-code analyzer.** `Directory.Build.props` sets repo-wide `TreatWarningsAsErrors=true`; a duplication analyzer turns on as day-one build errors across ~940 BFF files. Run as warnings-only it is the jscpd sweep with more infrastructure.
2. **A jscpd hard gate** (fail CI over a duplication threshold). It repeats the god-class LOC ratchet that was **retired 2026-08-20** (`.claude/patterns/testing/god-class-ratchet.md`): it gates on a count proxy for a judgment-laden property, freezes existing values, and blocks normal work until someone bumps a waiver. Observation at exit 0 is the posture we converged on.
3. **ast-grep for C# structure.** `tests/Spaarke.ArchTests/SourceScan.cs` already does structural source scanning in blocking CI; a second structural engine adds a tool and a rule language without a new capability.
4. **A generated capability manifest.** ~940 BFF service files produce a listing too large for an agent to read and that names files, not capabilities. The hand-curated index (item 1) is the answer to the same need.
5. **A `PreToolUse` deny hook on new files** (e.g. refuse `Write` of a new `*Service.cs` without a justification). §16 requires hooks to be narrow, under 5 seconds and free of false positives; "is this new file a duplicate?" is semantic, so the hook would either be a name heuristic (false positives on every legitimate new file, then disabled) or slow. The `<justification>` gate sits at the right point — task authoring — already.
6. **A shared LSP / semantic-index MCP sidecar.** With ~17 active worktrees, one shared index is stale for every worktree but one, and N indices cost N builds. **Separate, still open:** a *per-session* C# LSP plugin (go-to-definition and find-references inside one worktree) is a different question — it helps navigation, not cross-project reuse. Item 5(f) runs a measured trial of it; this rejection does not cover it.

### 10.7 Trimmed in the ROI review (Fable, 2026-11-10; owner-approved)

These were in an earlier version of this design and were cut because an existing mechanism already does the job or the return did not justify the cost. Do not re-add them without new evidence.

| Cut | Why | What does the job instead |
|---|---|---|
| `CapabilityIndexTests.cs` | Duplicates `scripts/quality/Test-InstructionPaths.ps1`, which already checks backticked paths in `.claude/patterns/*.md` | The existing path checker (§3.5) |
| `Validate-TaskPoml.ps1` citation warnings (index row, reuse-scan) | The existing justification check is already warning-only; a second unread warning adds nothing | `code-review` 6.6 |
| Pipeline delta re-scan and Step 3 absence lint | The fallback (full scan when the file is missing) makes absence impossible, and partial re-scans add a third code path | One scan per project (§4.0) |
| `duplication-baseline.json` and its hash fingerprints | Any edit to a cloned file changes its hash, so every week raises false "new" clusters | Stateless "changed in the last 7 days" trigger (§5.1) |
| Sweep fixture control | `.claude/worktrees/` is absent in CI | Config exclusion only |
| Nine package-review files | One file with nine sections reads the same | `notes/package-reviews.md` |
| Executing up to five consolidations | The "≤ 3 consumers" rule covered 6 of 9 packages | Execute three at most; file the rest (§6.1) |
| 5(b) strictness census and migration in r1 | Unrelated to G1–G4; item 4 may remove the packages it would convert | Follow-up project after item 4 (§12) |
| 5(c) monthly cloud routine | Recurring agent cost, an external cloud environment and a monthly PR review, for work done on demand anyway | The watchdog alert (§7.3) |
| 5(e) `SPRK001`/`SPRK002` convention, `SupersededApiTests`, caller baseline, ADR backfill, marking steps | The blocker behind the `CS0618` downgrade is gone; the compiler fails new callers once it is re-armed | `CS0618` under `TreatWarningsAsErrors` (§7.5) |
| 5(f) four of the seven gates | A one-off measurement does not need seven gates | Three gates and a half-day time-box (§7.6) |

## 11. Open questions for the owner

| # | Question | Recommendation |
|---|---|---|
| OQ-1 | The brief has the `code-mapper` agent *write* `reuse-scan.md` and *judge* plan duplication; its definition forbids both. Keep the split (agent finds, orchestrator judges and writes), or create a new agent definition that writes? | **Decided 2026-11-10**: keep the split (§4.2). |
| OQ-2 | `code-mapper` runs at effort `low`. Concept/synonym/interface search is harder than "where is X used". Accept `low`, or run this one call at `medium` via a workflow `agent()` (the definition fixes effort for the Agent tool)? | **Decided 2026-11-10**: `low`. If the dry-run shows a raise is needed, the measured token and cost impact goes to the owner before any change (§4.2). |
| OQ-3 | Should `Validate-TaskPoml.ps1` require the `CAPABILITY-INDEX row` / `no row` citation as an error immediately, or warn for a shadow window first? | **Decided 2026-11-10**: warning. **Revised by the ROI review**: no lint rule at all; `code-review` 6.6 checks it (§3.5, §10.7). |
| OQ-4 | Sweep placement: a weekly job inside `nightly-health.yml` (extension, recommended by root §11) or a separate `weekly-duplication-sweep.yml` (simpler cron, isolates a new tool)? | **Decided 2026-11-10**: extension, inside `nightly-health.yml`. |
| OQ-5 | Item 4 scope: does r1 only *decide* the 9 packages (records + ADR amendments for sanctions), or also *execute* the cheap consolidations? | **Decided 2026-11-10**: decide all 9; execute those with ≤ 3 consumers; file the rest. **Narrowed by the ROI review**: execute only `ai-context`, `legal-workspace` and `document-operations`; file the rest. The limit is on effort, never significance: defects found are fixed or reported to the owner by name (§6.1). |
| OQ-6 | Should the reuse scan also run earlier, in `design-to-spec`, where re-scoping a duplicate is cheapest? | **Decided 2026-11-10**: yes. It runs in design-to-spec Step 3; pipeline Step 2 runs the full scan only when none exists (no delta re-scan, per the ROI review; §4.0). |
| OQ-7 | Root `CLAUDE.md` §11 names the three questions but not the index. Add a one-line pointer there? | **Decided 2026-11-10**: yes, one line (§3.3). |
| OQ-8 | jscpd new-cluster floor (tokens, lines, cross-area rule) for the alert. | **Decided (owner, 2026-11-10)**: ≥ 100 tokens **and** ≥ 10 lines, across different top-level areas. The full report keeps jscpd's default (50 tokens / 5 lines) for browsing. 50 is jscpd's out-of-the-box default, not an alerting recommendation; PMD CPD, the other common detector, defaults to 100 tokens. Tune after four weekly reports. |
| OQ-9 | Epic and portfolio registration. | **Decided 2026-11-10**: Epic #427, the code-quality program. |
| OQ-10 | 5(b): which shared packages move to `strict` in r1? | **Decided 2026-11-10**: all shared packages whose strict build shows fewer than ~100 errors; the rest are split off and filed. **Superseded by the ROI review**: 5(b) is deferred to a follow-up project after item 4 (§7.2); this decision carries over to it. |
| OQ-11 | 5(b): extend type-aware ESLint beyond the packages made strict in r1? | **Decided 2026-11-10**: not in this project (§12). |
| OQ-12 | 5(c): cloud routine or GitHub Action? | **Decided 2026-11-10**: Claude Code scheduled cloud routine, opening a draft PR only; plus the CI watchdog. **Revised by the ROI review**: the routine is dropped; only the watchdog ships (§7.3). |
| OQ-13 | 5(e): does the superseded-API list need approval? | **Decided 2026-11-10**: no. **Moot after the ROI review**: there is no superseded-API list or backfill in r1 (§7.5). |
| OQ-14 | 5(f): go/no-go thresholds for the language-server trial. | **Decided 2026-11-10**: common-practice thresholds in §7.6, fixed before the trial runs. **Cut to three gates and a half-day time-box by the ROI review.** |
| OQ-15 | 5(e): when `CS0618` is re-armed, a fresh count may show framework or NuGet obsoletions hidden behind the suppression (the last count is from 2026-08-15). Fix them within this project, or list them with reasons and file them? | **Decided 2026-11-10**: fix those that take under an hour each; file the rest, with the suppression narrowed to their diagnostic IDs only. |
| OQ-16 | 5(c): the cloud routine needs a cloud environment connected to the GitHub repo. Does one exist, or is creating it part of this project? | **Decided 2026-11-10**: creating it is the owner's one-time setup step. **Moot after the ROI review**: no cloud routine (§7.3). |

## 12. Out of scope / future

- **Coding-agent eval set** (15–20 replayable tasks with known-good outcomes, used to compare models, effort levels and procedure changes). It came from the same review, but its effort is L, so the owner left it out of r1. It is the natural next project once items 2 and 5(d) have changed how reviews and plans are checked, because it would measure whether they work.
- **TypeScript strictness ratchet (5(b))**, deferred by the ROI review. It runs after item 4, on the shared packages that survive it, and settles the tsconfig census scope first (76 vs 118). The design in §7.2 and the OQ-10 decision carry over.
- Repo-wide type-aware ESLint (OQ-11), and with it the TypeScript half of obsolete-API checking (`@deprecated` + `@typescript-eslint/no-deprecated`).
- Executing the package consolidations that item 4 files (each its own project).

## 13. Success criteria

1. `.claude/patterns/CAPABILITY-INDEX.md` exists with ~30 rows. Its paths and ADRs are backticked repo paths, and `scripts/quality/Test-InstructionPaths.ps1` reports no new missing path for it; a seeded dead path in a local copy is reported once, as proof.
2. `task-create` 3.5.6 and `code-review` 6.6 cite the index, and root CLAUDE.md §11 carries the one-line pointer.
3. The dry-run against two closed projects records what the scan caught, what it missed and its token use, and the owner sees the result. If it passes:
   - `/design-to-spec` Step 3 runs the scan, writes `notes/reuse-scan.md`, and raises ≥ 2 candidates as owner questions.
   - design-to-spec's "no comprehensive discovery" lines are rewritten to allow it.
   - `project-pipeline` Step 2 runs the full scan only when the file is missing.
4. The weekly sweep runs, exits 0, excludes `.claude/worktrees/agent-*`, and opens or closes exactly one `quality-alert` issue (`weekly/duplication-sweep`). It alerts only on clusters above the OQ-8 floor that touch a file changed in the last 7 days. There is no baseline file.
5. `adversarial-reviewer.md` carries the capability-duplication check (F2) and the requirements-trace check (F4), both pointing at the right inputs.
6. `notes/package-reviews.md` has an owner-approved decision for each of the 9 grandfathered packages.
   - Every *sanction* has landed as an ADR-012 amendment, with the census table and `SharedPackageCensusTests` updated in the same PR.
   - The actions for `ai-context`, `legal-workspace` and `document-operations` are executed; the rest are filed with their findings.
   - Every defect found is fixed or named to the owner.
7. Item 5, each sub-item in its own PR:
   - **(a)** `.mcp.json` carries the Learn MCP server, and one `researcher` run cites a Learn MCP result.
   - **(b)** Deferred (§12).
   - **(c)** The `monthly/knowledge-refresh` watchdog check exists in `nightly-health.yml`, and `REFRESH-PROCEDURE.md` names it.
   - **(d)** The reviewer reports a per-criterion trace when given a POML.
   - **(e)** `CS0618` is no longer in `WarningsNotAsErrors`, and the stale comment is gone. A clean Release build passes. Any remaining suppression is by named `DiagnosticId` with a reason. The PR lists the framework obsoletions that were fixed or filed.
   - **(f)** `notes/csharp-lsp-trial.md` records the result against the three §7.6 gates (or "not workable" at the half-day limit) and the owner's decision.
