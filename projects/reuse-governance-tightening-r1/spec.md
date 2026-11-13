# Reuse Governance Tightening R1 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-11-10
> **Source**: design.md (all OQ-1..OQ-16 decided; ROI-trimmed 2026-11-10, cuts recorded in design.md §10.7)

## Executive Summary

Spaarke's reuse gate (`task-create` 3.5.6 `<justification>`, re-checked by `code-review` 6.6) already works, but it finds prior art only by name, never checks a project's *plan* for duplication, does not observe duplication after merge, and has no decision path for the 9 grandfathered `@spaarke/*` packages (gaps G1–G4). This project closes those gaps with the lightest mechanism for each: a hand-curated capability index, one reuse scan per project at `/design-to-spec`, a weekly observation-only jscpd sweep plus two reviewer checks, and owner decisions on the 9 packages. It also ships five small quality-tooling items (Learn MCP, knowledge-refresh watchdog, requirements trace, re-arming `CS0618`, a C# LSP trial).

## Scope

### In Scope
- **Item 1** — `.claude/patterns/CAPABILITY-INDEX.md` (~30 hand-curated rows) and its wiring into `task-create` 3.5.6, `code-review` 6.6, `.claude/patterns/INDEX.md`, root `CLAUDE.md` §11 and `doc-drift-audit`.
- **Item 2** — a dry-run of the reuse scan (go/no-go), then, if it passes, one `code-mapper` reuse scan in `/design-to-spec` Step 3 writing `notes/reuse-scan.md`, with `project-pipeline` Step 2 as the fallback when the file is missing.
- **Item 3** — a weekly jscpd sweep (exit 0) inside `nightly-health.yml` with one `quality-alert`, `.jscpd.json`, and a capability-duplication check in `adversarial-reviewer.md`.
- **Item 4** — decision records for all 9 grandfathered packages in one `notes/package-reviews.md`; execution of the decided action for `@spaarke/ai-context`, `@spaarke/legal-workspace` and `@spaarke/document-operations` only.
- **Item 5** — (a) Microsoft Learn MCP server for `researcher`; (c) knowledge-refresh staleness watchdog; (d) requirements-trace check in `adversarial-reviewer.md`; (e) re-arm `CS0618`; (f) half-day per-session C# LSP A/B trial.

### Out of Scope
- 5(b) TypeScript strictness ratchet — deferred to a follow-up project after item 4 (design.md §7.2, §12).
- Any blocking duplication gate, Sonar/Roslyn duplicate analyzer, ast-grep, generated capability manifest, `PreToolUse` deny hook on new files, shared LSP/semantic index (design.md §10).
- Everything cut by the ROI review (design.md §10.7): `CapabilityIndexTests.cs`, new `Validate-TaskPoml.ps1` warnings, pipeline delta re-scan and absence lint, `duplication-baseline.json`, sweep fixture control, nine separate package-review files, executing more than three package actions, the monthly cloud routine, the `SPRK001`/`SPRK002` superseded-API process (`SupersededApiTests`, caller baseline, ADR backfill, marking steps), four of the seven LSP gates. **Do not re-add any of these without new evidence and an owner decision.**
- Changes to the §11 three questions or to the `<justification>` schema.
- Executing the package consolidations/retirements other than the three above (each is filed as its own project).
- Repo-wide type-aware ESLint and TypeScript `@deprecated` checking (OQ-11).
- Coding-agent eval set.
- New BFF surface (endpoint, service, DI registration, package reference) or Dataverse column.

### Affected Areas
- `.claude/patterns/CAPABILITY-INDEX.md` (new), `.claude/patterns/INDEX.md` — item 1
- `.claude/skills/task-create/SKILL.md` (Step 3.5.6), `.claude/skills/code-review/SKILL.md` (Step 6.6), `.claude/skills/doc-drift-audit/SKILL.md` — item 1 wiring
- `CLAUDE.md` (root, §11 — one line; follow the maintainer notes in its header comment) — item 1
- `.claude/skills/design-to-spec/SKILL.md` (Step 3, Step 5, Constraints lines ~40 and ~162), `.claude/skills/project-pipeline/SKILL.md` (Step 2 item 5) — item 2
- `projects/reuse-governance-tightening-r1/notes/reuse-scan-dry-run.md` (new, project-local) — item 2 dry-run record
- `.github/workflows/nightly-health.yml`, `.jscpd.json` (new), `.github/actions/quality-alert/action.yml` (consumed, not changed) — item 3, 5(c)
- `.claude/agents/adversarial-reviewer.md`, `.claude/skills/task-execute/SKILL.md` (Step 9.5 passes the POML/spec path) — item 3, 5(d)
- `.claude/adr/ADR-012-shared-components.md` (census table + amendments), `tests/Spaarke.ArchTests/SharedPackageCensusTests.cs` — item 4
- `src/client/shared/Spaarke.AI.Context/`, `src/client/shared/Spaarke.LegalWorkspace/`, `src/client/shared/Spaarke.DocumentOperations/` and their consumers: `src/solutions/SpaarkeAi/` (`package.json` lines 40, 47, 49), `src/client/code-pages/SemanticSearch/`, `src/client/shared/Spaarke.Compose.Components/` — item 4 execution
- `projects/reuse-governance-tightening-r1/notes/package-reviews.md` (new) — item 4
- `.mcp.json`, `.claude/agents/researcher.md` — 5(a)
- `knowledge/REFRESH-PROCEDURE.md` — 5(c)
- `Directory.Build.props` and any C# caller of an `[Obsolete]` member (may include `src/server/api/Sprk.Bff.Api/**` and `tests/**`) — 5(e)
- `projects/reuse-governance-tightening-r1/notes/csharp-lsp-trial.md` (new) — 5(f)
- `docs/procedures/ci-cd-workflow.md` ("Quality alerts"), `.claude/skills/INDEX.md`, `docs/INDEX.md` — doc pointers

## Requirements

### Functional Requirements

**Item 1 — Capability index**

1. **FR-01**: Create `.claude/patterns/CAPABILITY-INDEX.md` with ~30 rows and the columns Capability, Synonyms, Canonical component, Governing ADR, Extension point (design.md §3.1–3.2 seed list scopes the rows; it is not trusted). Every cell is verified with Grep/Glob before it is written. Canonical components and ADRs are written as plain backticked repo paths (a package by its `src/client/shared/...` folder, not only its `@spaarke/*` name); symbols go in their own cell, never appended to the path. Where no single canonical component exists (seed row 30, Compose vs `RichTextEditor`), the row records both and points to item 4. - Acceptance: the file exists with 25–35 rows; every row's paths resolve; `scripts/quality/Test-InstructionPaths.ps1` reports no new missing path for it; a dead path seeded in a local (uncommitted) copy is reported exactly once, recorded in the task notes as proof; row 30 names two components.
2. **FR-02**: Wire the index. `task-create` Step 3.5.6: `<existing>` consults the index first, then Greps; the answer cites `CAPABILITY-INDEX row N` + file:line, or `CAPABILITY-INDEX: no row` + the Grep command run. `code-review` Step 6.6 checks against the same index. `.claude/patterns/INDEX.md` gains one line describing the index as a different shape (concept → component). Root `CLAUDE.md` §11 gains one line: "`<existing>` starts at `.claude/patterns/CAPABILITY-INDEX.md`". `doc-drift-audit` adds the index to its scan list. The skills state that a project adding a canonical component or changing an extension point updates its row in the same PR. - Acceptance: all five edits present; no `Validate-TaskPoml.ps1` change; root CLAUDE.md edit is one line and respects its header maintainer notes.

**Item 2 — Reuse scan**

3. **FR-03**: Dry-run (go/no-go) before any skill edit. Run one `code-mapper` agent (model `sonnet`, effort `low`) with the design.md §4.1 return schema against two closed projects with known duplication: the set-regarding/field-mapping r1→r2 history and the `RichTextEditor`/Compose pair. Record in `notes/reuse-scan-dry-run.md`: capabilities extracted, searches run, what was caught, what was missed, token use. Decision rule (owner default 2026-11-10): **both caught at `low` → go**; **one missed → present the owner the measured token cost per scan and projected monthly cost at `medium`, owner decides**; **both missed → no-go, FR-04/FR-05 are not implemented** and the result is reported. - Acceptance: the record exists with all five fields and the decision; the owner has seen it before FR-04 starts.
4. **FR-04** (only if FR-03 is go): `/design-to-spec` Step 3 runs one `code-mapper` reuse scan in place of its "quick Grep/Glob" exception. The orchestrator extracts the capabilities the design introduces (concept, synonyms, interface shape), passes them with the capability index to the agent, judges the facts, and writes `projects/{name}/notes/reuse-scan.md` at Step 4 (Plan Mode is read-only), including a plan-level duplication check. ≥ 2 viable candidates for one capability → a Step 5 owner question recorded under spec "Owner Clarifications"; 1 → recorded as the `extend` target; 0 → "none found" with the searches run. The SKILL.md Constraint "MUST NOT do comprehensive resource discovery" (~line 40) and Step 3's scope limitation (~line 162) are rewritten: this one scan is the single allowed discovery pass; everything else stays with project-pipeline. `code-mapper.md` is not changed (it stays read-only and fact-only; the orchestrator writes the file). - Acceptance: the step, the file contract and both rewritten lines are present; the agent call names model and effort; Step 2.5/Step 3 numbering is unchanged.
5. **FR-05** (only if FR-03 is go): `project-pipeline` Step 2 item 5 cites `notes/reuse-scan.md` when it exists and runs the full FR-04 scan only when it does not (no partial re-scan). With ≥ 2 candidates: interactive → owner question; autonomous → STOP at the gate with the options. `task-create` Step 3.5.6 `<existing>` cites the `reuse-scan.md` row for the capability when the file exists. - Acceptance: one code path for "missing file → full scan"; no delta re-scan or absence lint added.

**Item 3 — Weekly duplication sweep**

6. **FR-06**: Add a weekly-cadence job to `.github/workflows/nightly-health.yml` (not a new workflow) that runs `jscpd` at an exact pinned version via `npx` (no `package.json` change) over `src/**` for TS/TSX and C#, with `--exitCode 0`, using a new shared `.jscpd.json`. Exclusions: `**/node_modules/**`, build output, generated code, `**/*.test.*`, `tests/**`, `.claude/worktrees/agent-*/**`. The full JSON + Markdown report (jscpd defaults 50 tokens / 5 lines) is uploaded as a workflow artifact. **No baseline file.** A cluster is *new* when at least one of its files appears in `git log --since=7.days --name-only` (checkout fetch depth must cover 7 days). The job calls `.github/actions/quality-alert` with check `weekly/duplication-sweep`: `failing` when any new cluster has ≥ 100 tokens **and** ≥ 10 lines in ≥ 2 files in different top-level areas (OQ-8); otherwise `ok` (closes the alert). The alert summary names each new cluster's two paths and the capability-index row it most likely belongs to, if any. `docs/procedures/ci-cd-workflow.md` "Quality alerts" lists the check. - Acceptance: the job never fails the workflow on duplication; a run opens or closes exactly one `quality-alert` issue; a cluster only in files unchanged for > 7 days does not alert; the exclusion is in `.jscpd.json`, not only the workflow.
7. **FR-07**: `.claude/agents/adversarial-reviewer.md` gains two checks, landed in one change: (a) **capability duplication** — new surface duplicating a capability in `.claude/patterns/CAPABILITY-INDEX.md` (same concept under another name, or a new package/service where the row says extend) is class **F2** when it adds a second implementation of an indexed capability, K-class when it is a judgment call; (b) **requirements trace** — when the caller supplies the task POML or `spec.md`, each acceptance criterion is reported as *met (file:line + test)*, *not met* (**F4** when inside the change set's scope) or *not verifiable from the diff* (K-class with a reason). The definition tells callers to pass the POML/spec path, and `task-execute` Step 9.5 passes it. - Acceptance: both checks present with the class names matching task-execute's class table; Step 9.5 passes the path.

**Item 4 — Grandfathered package review**

8. **FR-08**: Write `projects/reuse-governance-tightening-r1/notes/package-reviews.md` with one section per package listed as *Grandfathered — under review* between the `shared-package-census` markers in `.claude/adr/ADR-012-shared-components.md` (9 today). Each section: evidence for ADR-012's three tests (distinct contract, quarantine-worthy dependency, cross-surface reuse), the consumer count **re-verified from `package.json` files at task time**, a **Findings** subsection graded by severity, and one recommendation (Sanction / Consolidate into a named sanctioned package / Retire). Feature-area packages address the Pattern D dual-use counter-argument and the PCF bundle cost. Evidence gathering uses `code-mapper` (sonnet, low); the recommendation is planning-tier judgment. **Owner gate**: the owner approves each decision before FR-09 runs, including in autonomous pipeline mode. - Acceptance: 9 sections, each with evidence, count, findings and recommendation; owner approval recorded per package.
9. **FR-09**: Execute the owner-approved decision for `@spaarke/ai-context`, `@spaarke/legal-workspace` and `@spaarke/document-operations` only. Sanction → an ADR-012 amendment (Path B) naming the package and its contract outside the census table, the census row flipped to Sanctioned with the ADR citation, and `SharedPackageCensusTests.cs` updated in the same PR. Consolidate/Retire → consumers' `package.json`, import paths, deep-import paths and build config migrated; the package folder removed; census row and test entry removed in the same PR; consumers build and their tests pass. Every other package's action is filed as its own GitHub issue carrying its findings and severity. Every defect found on a real path is fixed in this project or named to the owner in the project report and the PR. - Acceptance: three actions landed; `SharedPackageCensusTests` passes; the other six filed with findings; a defect list (possibly empty) is in the report.

**Item 5 — Quality tooling (each sub-item its own PR)**

10. **FR-10** (5a): Add Microsoft's Learn MCP server (remote HTTP; confirm the current endpoint from Learn itself) to `.mcp.json` beside `dataverse`. In `.claude/agents/researcher.md`: add the server's tool names to the frontmatter `tools:` list; prose order is own memory → `knowledge/` → `docs/`/ADRs → Learn MCP → `WebSearch`/`WebFetch`; queries name the product and API surface; search then fetch one or two pages; one line forbidding customer data, tenant names or secrets in queries. A README/doc line notes the one-time per-machine approval of a project-scoped MCP server. - Acceptance: one `researcher` run cites a Learn MCP result.
11. **FR-11** (5c): Add a check to `nightly-health.yml` that reads the newest `## YYYY-MM-DD` heading of any kind in `knowledge/REFRESH-LOG.md` and reports `quality-alert` check `monthly/knowledge-refresh` as `failing` when it is more than 35 days old, else `ok`. `knowledge/REFRESH-PROCEDURE.md` gets one line naming the watchdog; `docs/procedures/ci-cd-workflow.md` lists the check. No scheduled agent or cloud routine. - Acceptance: the check exists and, on landing, fires (newest entry is 2026-08-26) — expected behaviour, not a defect.
12. **FR-12** (5e): Remove `CS0618` from `WarningsNotAsErrors` in `Directory.Build.props` (~line 41) and delete the stale justifying comment (~line 18, `DemoProvisioningOptions`). Run a clean Release build of `Spaarke.sln`. Fix what breaks: Spaarke callers of superseded members migrate; framework/NuGet obsoletions taking under an hour each are fixed, the rest filed (OQ-15). Only for callers that cannot migrate now (a filed framework obsoletion, an in-repo caller of a compatibility constant): add a `DiagnosticId` to the member and suppress that one ID narrowly (call site, or `WarningsNotAsErrors` by that ID) with a one-line reason. Add a comment beside the setting: marking a member `[Obsolete]` now fails every existing caller, so the marking change migrates them or uses a narrow `DiagnosticId` suppression. - Acceptance: `CS0618` absent from `WarningsNotAsErrors`; stale comment gone; clean Release build passes; every remaining suppression is by named ID with a reason; the PR lists framework obsoletions fixed or filed; no new test, baseline or skill step.
13. **FR-13** (5f): Run a half-day-time-boxed A/B trial of a per-session C# LSP plugin on `Sprk.Bff.Api` in one worktree: 10 fixed questions from real recent sessions (4 find-references, 3 find-implementations, 2 receiver-type/definition, 1 "what breaks if I change this signature"), answered twice by the same model (Grep-only vs LSP allowed), scored against hand-checked answers. Gates (all must hold): correctness ≥ 9/10 with LSP and none worse than Grep; **zero** false "no references"; ≤ 2 GB resident per session at steady state and ≥ 25% machine RAM free with 3 concurrent sessions. Tool calls, tokens, wall time and cold start are recorded, not gated. Memory failing while usefulness passes → recommend opt-in per session. Record in `notes/csharp-lsp-trial.md` with the owner's decision. Adoption is a separate change. - Acceptance: the note records the gate results (or "not workable today" at the half-day limit) and the decision.

### Non-Functional Requirements
- **NFR-01 (non-blocking posture)**: the sweep and the watchdog never fail a build or PR; their only output is an artifact and one `quality-alert` issue each.
- **NFR-02 (recurring cost)**: no scheduled agent runs. Recurring cost is limited to the weekly sweep and the nightly watchdog check. The reuse scan is one Sonnet `code-mapper` call per project.
- **NFR-03 (agent cost, root §8.5)**: every agent call names model and effort: `code-mapper` (sonnet, low) for the dry-run, scans and package evidence; one `adversarial-reviewer` pass over items 1–4 at project close. No fan-out beyond that. Raising scan effort above `low` requires showing the owner the measured token/cost impact first (OQ-2).
- **NFR-04 (sub-agent boundary, root §3)**: tasks editing `.claude/` (items 1, 2, 3, 5a, 5d) run in the main session, never dispatched to sub-agents.
- **NFR-05 (PR shape, owner default 2026-11-10)**: items 1–3 ship as one PR; item 4's package actions ship in their own PR(s) with `code-review`; the single `adversarial-reviewer` pass covers items 1–4 together. Each item-5 sub-item ships as its own PR with its own `code-review`.
- **NFR-06 (rigor)**: 5(e) and any task modifying `tests/**` (item 4's census test, 5(e) callers in tests) run `code-review` + `adr-check` regardless of rigor level.
- **NFR-07 (BFF hygiene, root §10)**: if 5(e) touches `src/server/api/Sprk.Bff.Api/**`, the task measures the publish-size delta against a fresh master build (expected 0) and confirms no new HIGH CVE.
- **NFR-08 (scanner hygiene)**: any repo-wide scanner excludes `.claude/worktrees/agent-*/` so local and CI runs agree.
- **NFR-09 (privacy)**: Learn MCP queries contain no customer data, tenant names or secrets.
- **NFR-10 (significance over effort, OQ-5)**: the three-package execution limit limits effort, never significance; every defect found is fixed or reported to the owner by name.

## Technical Constraints

### Applicable ADRs
- **ADR-012** (`.claude/adr/ADR-012-shared-components.md`): shared component library, the three-test bar for a new shared package, the census and its Sanctioned/Grandfathered statuses. Governs item 4 and capability-index rows for shared UI.
- **ADR-022** (React 16/17 compatibility for PCF): applies if `document-operations` is consolidated into `@spaarke/ui-components`, which PCF controls import.
- **ADR-038** (`docs/adr/ADR-038-testing-strategy.md`) + `docs/standards/TEST-ARCHITECTURE.md`: governs edits to `SharedPackageCensusTests.cs` and any test changed by 5(e).
- **ADR-001 / root §10**: no new BFF surface; 5(e) only modifies configuration and callers.
- Root CLAUDE.md §6.5 (ADR conflicts), §11 (component justification), §16 (enforcement ladder) — governance this project edits and must follow.

### MUST Rules (from ADRs)
- ✅ MUST sanction a shared package only through an ADR-012 amendment (Path B) that names it and its contract, with the census table and `SharedPackageCensusTests` updated in the same PR (the test checks both).
- ✅ MUST keep the grandfathered list shrink-only (it is a ratchet baseline); a grandfathered package is never a precedent for a new one.
- ✅ MUST verify React 16/17 compatibility for any component moved into a package PCF consumes; MUST NOT use React 18/19-only APIs (`useTransition`, `useDeferredValue`) there.
- ✅ MUST keep shared components free of `ComponentFramework.*` and direct `Xrm.WebApi` calls (runtime-context abstractions only).
- ✅ MUST move a component's tests with it; MUST NOT export components without tests (ADR-012, ADR-038).
- ❌ MUST NOT add a new `@spaarke/*` package.
- ❌ MUST NOT add a blocking duplication gate or any item listed in design.md §10 / §10.7.
- ❌ MUST NOT reintroduce the blanket `CS0618` suppression.

### Existing Patterns to Follow
- Census ArchTest + ADR table in one PR: `tests/Spaarke.ArchTests/SharedPackageCensusTests.cs` (and `tests/Spaarke.ArchTests/CredentialCensusTests.cs`).
- Quality alerts: `.github/actions/quality-alert/action.yml` as already used in `.github/workflows/nightly-health.yml`'s matrix.
- Observation-only reporting: `scripts/report-large-server-files.ps1`.
- Backticked-path checking: `scripts/quality/Test-InstructionPaths.ps1` (ratcheted in `.github/workflows/ci-tier2-advisory.yml`).
- Read-only fact agent: `.claude/agents/code-mapper.md` (unchanged; the orchestrator judges and writes).
- Reviewer checklist and F/K classes: `.claude/agents/adversarial-reviewer.md`, `.claude/skills/task-execute/SKILL.md` class table.
- Researcher: `.claude/agents/researcher.md`; MCP config: `.mcp.json`.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

Canonical copy in design.md (header; SpaarkeAi corrected to Y at `/design-to-spec`, 2026-11-10).

```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>Y</spaarkeai>
  <ci-workflows>Y</ci-workflows>
  <skill-directives>Y</skill-directives>
  <root-claude-md>Y</root-claude-md>
</hot-path-declaration>
```

- **BFF = Y** only through 5(e): `Directory.Build.props` and possible migration of obsolete-API callers. No new endpoint, service, DI registration or package (`.claude/constraints/bff-extensions.md`); publish-size delta measured and expected 0; ≤ 60 MB ceiling applies.
- **SpaarkeAi = Y** through item 4: `src/solutions/SpaarkeAi/package.json` consumes all three executed packages. Run `conflict-check` before that work.
- **Background work**: none in the BFF. The sweep and watchdog run in GitHub Actions (`nightly-health.yml`), not under ADR-052's BFF hosts.

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `.claude/patterns/CAPABILITY-INDEX.md` | `.claude/patterns/INDEX.md` + 94 pointer files map task type → pattern, not concept/synonym → component; no `CAPABILITY-INDEX.md` exists (Glob, 2026-11-10) | No: pointer files are capped at 25 lines and domain-organised; a 30-row table in `INDEX.md` would mix two lookups | `task-create` 3.5.6 passes "none found" for a capability that exists under another name (G1) |
| Reuse scan + `notes/reuse-scan.md` | `design-to-spec` Step 3 "quick Grep/Glob" exception; `project-pipeline` Step 2 item 5 §11 "Existing" answer | Yes — extends both steps (how the answer is found, where it is recorded) | A design re-implementing an existing capability yields tasks that each pass the per-task gate (G2) |
| Weekly sweep job | `nightly-health.yml` quality-alert matrix; `scripts/report-large-server-files.ps1` | Yes — a job inside `nightly-health.yml` reusing its alert step | Parallel worktrees land the same code twice and nothing reports it (G3) |
| `.jscpd.json` | None: no `.jscpd.json` (Glob) and no jscpd use in `.github/` (Grep, 2026-11-10) | No: config for a tool the repo does not use yet | Local and CI runs disagree (agent-worktree phantoms); the size floor lives only in YAML |
| Two checks in `adversarial-reviewer.md` | The definition's existing checklist | Yes — lines added to the existing list (modification) | Capability duplication and unmet acceptance criteria pass review unseen |
| `notes/package-reviews.md` | ADR-012 census "reason" column (one line each) | No: one line cannot hold three-test evidence and a migration plan | The 9 packages stay grandfathered indefinitely (G4) |
| Learn MCP entry (`.mcp.json`) + `researcher.md` tools | `.mcp.json` (`dataverse` only); researcher reaches Learn via `WebFetch` | Yes — one entry and one tool-list/prose edit | Researcher scrapes whole pages that break on moved URLs (14+ in four months) |
| Knowledge-refresh watchdog check | `knowledge/REFRESH-PROCEDURE.md`; `nightly-health.yml` matrix | Yes — one check in the existing matrix | Monthly refresh unrun for six months with no signal |
| `CS0618` re-arm | `TreatWarningsAsErrors=true`; `[Obsolete]` (21 uses in `src/`) | Yes — modification of existing config, no new surface | New calls to any obsolete API pass a green build unseen |
| `notes/csharp-lsp-trial.md`, `notes/reuse-scan-dry-run.md` | — | n/a: project-local records, not repo surface | Decisions stay unmeasured and keep being re-argued |

No new service, endpoint, DI registration, package reference, shared package or Dataverse column.

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-012 | "Adding another shared package is a high bar … New siblings require an ADR amendment"; grandfathered packages "later either sanctioned (by an amendment …) or consolidated" | Item 4 may conclude that one of the three executed packages should be **sanctioned**, which changes ADR-012's sanctioned set | B (planned amendment) | ADR-012 itself prescribes the amendment as the sanction route. Each amendment merges with its census-table and `SharedPackageCensusTests` change. Consolidate/Retire outcomes comply (Path C) and need no amendment. |

No other ADR tensions surfaced at design time. All listed ADRs apply without exception. This section may be updated if tensions emerge during implementation.

## Success Criteria

1. [ ] `.claude/patterns/CAPABILITY-INDEX.md` exists with ~30 verified rows in backticked repo paths - Verify by: `scripts/quality/Test-InstructionPaths.ps1` reports no new missing path; a seeded dead path in a local copy is reported once.
2. [ ] `task-create` 3.5.6 and `code-review` 6.6 cite the index; root CLAUDE.md §11 carries the one-line pointer; `.claude/patterns/INDEX.md` and `doc-drift-audit` reference it - Verify by: Grep for `CAPABILITY-INDEX` in each file.
3. [ ] The dry-run against the two closed projects records caught/missed/token use and the go/no-go decision, and the owner has seen it - Verify by: `notes/reuse-scan-dry-run.md`. If go: design-to-spec Step 3 runs the scan, writes `notes/reuse-scan.md`, raises ≥ 2 candidates as owner questions, and its "no comprehensive discovery" lines are rewritten; project-pipeline Step 2 runs the full scan only when the file is missing - Verify by: reading both SKILL.md files.
4. [ ] The weekly sweep runs, exits 0, excludes `.claude/worktrees/agent-*`, and opens or closes exactly one `weekly/duplication-sweep` alert, alerting only on clusters above the OQ-8 floor that touch a file changed in the last 7 days; no baseline file exists - Verify by: a `workflow_dispatch` run and its artifact/alert.
5. [ ] `adversarial-reviewer.md` carries the capability-duplication check (F2) and the requirements-trace check (F4), and `task-execute` Step 9.5 passes the POML/spec path - Verify by: reading both files; one reviewer run reports a per-criterion trace.
6. [ ] `notes/package-reviews.md` has an owner-approved decision for all 9 grandfathered packages; the three in-scope actions have landed (sanctions as ADR-012 amendments with census + test in the same PR); the other six are filed with findings; every defect found is fixed or named to the owner - Verify by: the file, `SharedPackageCensusTests` green, the filed issues, the project report.
7. [ ] 5(a): `.mcp.json` has the Learn MCP server and one `researcher` run cites a Learn MCP result - Verify by: the run transcript/notes.
8. [ ] 5(c): the `monthly/knowledge-refresh` check exists in `nightly-health.yml` and `REFRESH-PROCEDURE.md` names it - Verify by: a `workflow_dispatch` run.
9. [ ] 5(e): `CS0618` not in `WarningsNotAsErrors`, stale comment gone, clean Release build of `Spaarke.sln` passes, any suppression is by named `DiagnosticId` with a reason, the PR lists obsoletions fixed or filed - Verify by: `dotnet build Spaarke.sln -c Release` and the PR body.
10. [ ] 5(f): `notes/csharp-lsp-trial.md` records the three gates (or "not workable" at the half-day limit) and the owner's decision - Verify by: the note.

## Dependencies

### Prerequisites
- PR #2656 (ADR-012 census + `SharedPackageCensusTests`: 6 Sanctioned, 9 Grandfathered) — merged; item 4 starts from it.
- Item 1 before items 2 and 3 (both cite the index).
- FR-03 dry-run before FR-04/FR-05 (go/no-go).
- FR-08 owner approval before FR-09.
- FR-07 (capability-duplication + requirements trace) lands in one change (same file).

### External Dependencies
- Microsoft Learn MCP server endpoint (confirmed at task time) and one-time per-machine user approval of the project-scoped server.
- `jscpd` from npm, pinned exact version, via `npx` in CI.
- A Claude Code C# LSP plugin and language-server binary (e.g. `csharp-ls`) for the 5(f) trial only.
- GitHub Issues via `.github/actions/quality-alert` (existing).
- Portfolio: Epic #427 (code-quality program); Project Issue registered at `/project-pipeline` Step 4.2.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Hot-path (SpaarkeAi) | design.md said SpaarkeAi = N, but all three executed item-4 actions change `src/solutions/SpaarkeAi`. Correct it? | Yes (2026-11-10): SpaarkeAi = Y; XML `<hot-path-declaration>` added to design.md | `conflict-check` and pipeline Step 3 cover Console overlap; item 4 tasks touch a hot path |
| PR shape | One PR for items 1–4, or split? | Default accepted: items 1–3 one PR; item 4 package actions own PR(s) with `code-review`; one `adversarial-reviewer` pass over items 1–4; each item-5 sub-item own PR | NFR-05; task grouping in plan.md |
| Dry-run pass rule | What counts as "pass"? | Default accepted: both caught at `low` → go; one missed → owner sees `medium` cost and decides; both missed → no-go | FR-03 gates FR-04/FR-05 |
| Item 4 owner gate | Does execution wait for the owner in autonomous mode? | Default accepted: yes, FR-09 waits for owner approval of each decision in `package-reviews.md` | A human-gate task in the plan |
| OQ-1..OQ-16 | (decided in design.md §11; ROI review revised OQ-3, 5, 6, 10, 12, 14; OQ-13, 16 moot) | See design.md §11 | Carried into the FRs above |

## Assumptions

- **Dry-run record location**: assuming `notes/reuse-scan-dry-run.md` in this project - affects FR-03.
- **Sweep cadence inside a nightly workflow**: assuming a second (weekly) `schedule` cron on `nightly-health.yml` with the job gated on that schedule or `workflow_dispatch` - affects FR-06.
- **jscpd checkout depth**: assuming the job fetches enough history for `git log --since=7.days` - affects FR-06.
- **Consumer counts** (`ai-context` 1, `legal-workspace` 1, `document-operations` 3: SpaarkeAi, SemanticSearch code page, Compose.Components) are as Grepped on 2026-11-10 and re-verified at task time - affects FR-08/FR-09.
- **This project's own tasks** are generated with the current (pre-item-2) `project-pipeline` Step 2 - affects nothing; noted so it is not mistaken for a gap.

## Unresolved Questions

None blocking. Decisions that arise during execution and go to the owner by design:
- [ ] FR-03 result (go / raise to `medium` / no-go) - Blocks: FR-04, FR-05.
- [ ] FR-08 per-package decisions - Blocks: FR-09.
- [ ] FR-13 adoption decision - Blocks: nothing in r1 (adoption is a separate change).

---

*AI-optimized specification. Original design: design.md*
