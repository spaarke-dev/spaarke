# Project Plan: Reuse Governance Tightening R1

> **Last Updated**: 2026-11-11
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md) · **Design**: [design.md](design.md)

---

## 1. Executive Summary

**Purpose**: Close reuse gaps G1–G4 (name-only `<existing>` search, no plan-level duplication check, no post-merge duplication signal, no decision path for 9 grandfathered packages) with the lightest mechanism each, plus four small quality-tooling items.

**Scope**:
- Capability index + wiring (FR-01, FR-02)
- Reuse-scan dry-run (go/no-go) and, if go, the scan in `/design-to-spec` + `project-pipeline` fallback (FR-03..FR-05)
- Weekly `jscpd` sweep + F2/F4 reviewer checks (FR-06, FR-07)
- Package decisions for 9, execution for 3 (FR-08, FR-09)
- Learn MCP, knowledge watchdog, `CS0618` re-arm, C# LSP trial (FR-10..FR-13)

**Timeline**: effort-based, no dates | **Estimated Effort**: 8–12 days (about 17 tasks of 2–4 h, plus owner gates)

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-012**: shared packages need the three-test bar; sanction only via an ADR-012 amendment with census table + `SharedPackageCensusTests` in the same PR; the grandfathered list only shrinks; no `ComponentFramework.*` / direct `Xrm.WebApi` in shared code; components ship with tests.
- **ADR-022**: anything moved into `@spaarke/ui-components` (consumed by PCF) stays React 16/17-compatible; no `useTransition` / `useDeferredValue`.
- **ADR-038**: behaviour tests for what a consumer would notice; test edits trigger code-review + adr-check.

**From Spec**:
- Observation only for the sweep and watchdog (NFR-01); no recurring agent cost (NFR-02).
- Tasks editing `.claude/` run in the main session (NFR-04, root §3).
- PR shape (NFR-05): items 1–3 one PR; item 4 own PR(s); each item-5 sub-item own PR.
- Nothing from design.md §10 / §10.7 is re-added.

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Index paths as backticked repo paths | Puts the index under the existing `Test-InstructionPaths.ps1` | No new ArchTest |
| Dry-run gates the item-2 skill edits | A scan that misses known duplicates is not worth wiring | Tasks 012/013 conditional on 011 |
| Stateless 7-day "changed" trigger | Hash baselines raise false "new" clusters on every edit | No baseline file |
| Execute only 3 of 9 package actions | "≤ 3 consumers" covered 6 of 9 | 6 filed as projects |
| Re-arm `CS0618` | Its justification (DemoProvisioning) is stale | Compiler is the enforcement rung |

### Discovered Resources

**Applicable ADRs**: `.claude/adr/ADR-012-shared-components.md`, ADR-022 (React 16 compatibility), `docs/adr/ADR-038-testing-strategy.md`.

**Applicable Skills**:
- `.claude/skills/task-create/` — edited at Step 3.5.6 (line ~246) and its `<existing>` rule (line ~252)
- `.claude/skills/code-review/` — edited at Step 6.6 (line ~350)
- `.claude/skills/design-to-spec/` — Step 3 + Constraints (lines ~40, ~162), Step 5 question routing
- `.claude/skills/project-pipeline/` — Step 2 Part 1 item 5 (line ~143)
- `.claude/skills/task-execute/` — Step 9.5 (line ~463) passes the POML/spec path to the reviewer
- `.claude/skills/doc-drift-audit/` — scan list (lines ~73, ~83)
- `.claude/skills/ai-procedure-maintenance/` — checklist for editing skills/agents
- `.claude/skills/adr-check/`, `.claude/skills/code-review/` — gates for item 4 and 5(e)
- `.claude/skills/conflict-check/` — before item 4 (SpaarkeAi hot path) and skill edits
- `.claude/skills/code-page-deploy/` — SpaarkeAi / SemanticSearch DEV deploy after item 4
- `.claude/skills/ci-cd/` — workflow conventions for `nightly-health.yml`
- `.claude/skills/project-defer-issue-tracking/` — filing the six package actions and framework obsoletions

**Agents**: `.claude/agents/code-mapper.md` (unchanged; sonnet, low), `.claude/agents/adversarial-reviewer.md` (edited; fable, high), `.claude/agents/researcher.md` (edited; tools line 4, order line 22).

**Knowledge / docs**:
- `docs/procedures/ci-cd-workflow.md` — "Quality alerts" section (lines ~41–50, ~180)
- `knowledge/REFRESH-PROCEDURE.md`, `knowledge/REFRESH-LOG.md`
- `docs/standards/TEST-ARCHITECTURE.md`, `tests/CLAUDE.md` (census rules)
- `.claude/patterns/INDEX.md` (94 pointer files)
- `.claude/constraints/bff-extensions.md` (5(e) publish-size delta)

**Reusable code / canonical implementations**:
- `tests/Spaarke.ArchTests/SharedPackageCensusTests.cs`, `tests/Spaarke.ArchTests/CredentialCensusTests.cs` — census + ADR table pattern
- `.github/workflows/nightly-health.yml` — quality-alert matrix job (~line 1256: `uses: ./.github/actions/quality-alert` with `check` / `status` / `summary` / `dry-run`); cron at line 31
- `.github/actions/quality-alert/action.yml`
- `scripts/quality/Test-InstructionPaths.ps1` (ratcheted in `.github/workflows/ci-tier2-advisory.yml`)
- `scripts/report-large-server-files.ps1` — observation-only report precedent
- `Directory.Build.props` lines 6 (`TreatWarningsAsErrors`), 18 (stale comment), 41 (`CS0618`)

**Scripts**: `scripts/Validate-TaskPoml.ps1` (lint), `scripts/quality/Test-InstructionPaths.ps1`, `scripts/worktree/Remove-WorktreeSafe.ps1` (if item-5 PR branches use extra worktrees).

**Hot-path overlap (Step 2 pre-step)**: BFF (20 active projects), SpaarkeAi (12, including `spaarkeai-compose-r4` and `spaarke-dataset-grid-framework-r2`, which created `@spaarke/legal-workspace`), skill-directives (many). Open PRs overlapping: #935 (`work/code-quality-and-assurance-r4`: ADR-012 + `SharedPackageCensusTests.cs`), #909 (dependabot: `nightly-health.yml`).

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Capability index (item 1)            → 001, 002
Phase 2: Reuse scan (item 2)                  → 010 (dry-run, owner gate), 011, 012 (conditional)
Phase 3: Sweep + reviewer checks (item 3, 5d) → 020, 021
Phase 4: Package reviews (item 4)             → 030 (owner gate), 031, 032, 033, 034, 035 (deploy)
Phase 5: Quality tooling (item 5)             → 040 (5a), 041 (5c), 042 (5e), 043 (5f)
Phase 6: Review and wrap-up                   → 080 (adversarial pass + PR for items 1–3), 090 (wrap-up)
```

### Critical Path

**Blocking Dependencies:**
- 001 BLOCKS 002, 010, 020, 021 (they cite the index)
- 010 (owner go/no-go) BLOCKS 011, 012
- 030 (owner approval) BLOCKS 031, 032, 033, 034
- 031–033 BLOCK 035 (DEV deploy)
- 002, 012, 020, 021, 031–034 BLOCK 080
- 080 and 040–043 BLOCK 090
- Phase 5 tasks are independent of Phases 1–4 and of each other.

**High-Risk Items:**
- 033 `document-operations` consolidation (3 consumers, one a shared package) — Mitigation: build + test every consumer, ADR-022 check if folded into `ui-components`.
- 042 `CS0618` re-arm (unknown count of hidden obsoletions) — Mitigation: OQ-15 one-hour rule; narrow `DiagnosticId` suppressions.

---

## 4. Phase Breakdown

### Phase 1: Capability index

**Objectives:** concept → canonical component lookup in one read (G1).

**Deliverables:**
- [ ] `.claude/patterns/CAPABILITY-INDEX.md` (~30 verified rows) — task 001
- [ ] Wiring in `task-create` 3.5.6, `code-review` 6.6, `.claude/patterns/INDEX.md`, root `CLAUDE.md` §11, `doc-drift-audit` — task 002

**Inputs**: design.md §3, spec FR-01/FR-02. **Outputs**: the index + five edits.

### Phase 2: Reuse scan

**Objectives:** check a project's design for capability duplication once, before tasks (G2).

**Deliverables:**
- [ ] `notes/reuse-scan-dry-run.md` with caught/missed/tokens and the owner's go/no-go — task 010
- [ ] (if go) `/design-to-spec` Step 3 scan, `reuse-scan.md` contract, lines ~40/~162 rewritten — task 011
- [ ] (if go) `project-pipeline` Step 2 fallback + `task-create` 3.5.6 citation — task 012

### Phase 3: Sweep and reviewer checks

**Deliverables:**
- [ ] `.jscpd.json` + weekly job in `nightly-health.yml` + `ci-cd-workflow.md` line — task 020
- [ ] `adversarial-reviewer.md` F2 + F4 checks; `task-execute` 9.5 passes the path — task 021

### Phase 4: Package reviews

**Deliverables:**
- [ ] `notes/package-reviews.md`, 9 sections, owner-approved — task 030
- [ ] `@spaarke/ai-context` action — task 031
- [ ] `@spaarke/legal-workspace` action — task 032
- [ ] `@spaarke/document-operations` action — task 033
- [ ] Six other actions filed as issues with findings; defect list to owner — task 034
- [ ] DEV deploy + smoke of SpaarkeAi and SemanticSearch (owner OK) — task 035

### Phase 5: Quality tooling (each its own PR)

**Deliverables:**
- [ ] 5(a) Learn MCP in `.mcp.json` + `researcher.md` — task 040
- [ ] 5(c) `monthly/knowledge-refresh` watchdog — task 041
- [ ] 5(e) `CS0618` re-armed, clean Release build — task 042
- [ ] 5(f) `notes/csharp-lsp-trial.md` with owner decision — task 043

### Phase 6: Review and wrap-up

**Deliverables:**
- [ ] One `adversarial-reviewer` pass over items 1–4; findings fixed; PR for items 1–3 — task 080
- [ ] Wrap-up: `/test-diet`, lessons learned, README/TASK-INDEX final — task 090

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Microsoft Learn MCP server | GA | Low | Confirm endpoint from Learn at task time |
| `jscpd` (npm) | Stable | Low | Exact pinned version via `npx` |
| C# LSP plugin / `csharp-ls` | Unknown | Med | Half-day time-box; "not workable" is a valid result |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| ADR-012 census | `.claude/adr/ADR-012-shared-components.md` | Current (PR #2656) |
| `SharedPackageCensusTests` | `tests/Spaarke.ArchTests/SharedPackageCensusTests.cs` | Current |
| quality-alert action | `.github/actions/quality-alert/action.yml` | Production |
| `code-mapper` agent | `.claude/agents/code-mapper.md` | Current |

---

## 6. Testing Strategy

**Unit / ArchTests**: `SharedPackageCensusTests` must pass after each item-4 change; moved components keep their tests (ADR-012, ADR-038). No new tests are required by items 1–3 or 5 (design §10.7 removed them).

**Integration**: consumer builds for SpaarkeAi, SemanticSearch and Compose after item 4; clean Release build of `Spaarke.sln` for 5(e); `workflow_dispatch` runs of `nightly-health.yml` for the sweep and watchdog (dry-run alerts off master).

**Proofs**: seeded dead path reported by `Test-InstructionPaths.ps1` (001); one `researcher` run citing Learn MCP (040); one reviewer run producing a per-criterion trace (021).

---

## 7. Acceptance Criteria

See spec.md "Success Criteria" 1–10; each phase's deliverables above map to them (Phase 1 → 1–2, Phase 2 → 3, Phase 3 → 4–5, Phase 4 → 6, Phase 5 → 7–10).

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Reuse scan misses known duplicates at `low` | Med | Med | Dry-run go/no-go; cost shown before any raise (OQ-2) |
| R2 | Package consolidation breaks a consumer | Med | High | Per-consumer build + tests; DEV deploy with owner OK |
| R3 | Hidden obsoletions behind `CS0618` | Med | Med | One-hour rule; file the rest; narrow suppression |
| R4 | Merge friction on ADR-012 / census test with PR #935 | Med | Med | Merge master before task 030; coordinate with r4 |
| R5 | Sweep alert noise | Low | Low | OQ-8 floor; tune after four weekly reports |

---

## 9. Next Steps

1. Generate task files (`task-create`) and pass the Completeness Lint.
2. Register the project (Epic #427).
3. Begin Phase 1 (task 001) and Phase 5 tasks in parallel where allowed.

---

**Status**: Ready for Tasks
**Next Action**: task-create

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
