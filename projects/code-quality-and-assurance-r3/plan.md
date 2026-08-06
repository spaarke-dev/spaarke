# Project Plan: Code Quality & Assurance R3

> **Last Updated**: 2026-08-06
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Run a standing quality *program* that re-baselines the codebase grade honestly, drives verified per-surface assessments to an A+ senior-panel standard, and hardens forcing-functions so the grade holds as the codebase grows.

**Scope**:
- Rubric (`CODE-QUALITY-RUBRIC.md`) + living `SCORECARD.md` + reusable `quality-assessment` Workflow
- Full Fable-verified read-only assessment of every surface (gating deliverable)
- BFF surface remediation (already assessed): dead code, 2 bugs, downcast, `@spaarke/auth`, facade, migration, DI, hygiene
- Forcing-functions (ArchTests, analyzers, CI gates) — per-surface activation
- Horizontal sweeps (security, test quality, CVE, observability, doc-drift)

**Timeline**: Multi-wave standing program (assessments run anytime; remediation sequenced into quiet windows) | **Estimated Effort**: initial task set ~30 tasks; surfaces 2–6 remediation task-created after their assessments.

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-028** — Spaarke Auth v2 (canonical): all client→BFF auth via `@spaarke/auth`. Drives the Finance auth closure + security horizontal.
- **ADR-013** (refined 2026-05-20) — AI facade: CRUD uses `Services/Ai/PublicContracts/`; no direct AI-internal injection. Drives BFF facade compliance.
- **ADR-032** — Null-Object kill-switch: preserve verified seams (`StubInsightGraph`, Todo `NullObject/`+`Placeholder/` pair).
- **ADR-038** — Testing strategy: KEEP categories, behavior-over-mocks, coverage = observation, `/test-diet` at wrap-up.
- **ADR-010** — DI minimalism: `CommunicationModule` decomposition adds helpers only.
- **ADR-022** — PCF platform libraries (PCF surface assessment).
- **ADR-002** — Plugins (`BaseProxyPlugin` invert-vs-decommission).

**From Spec**:
- Single project / single worktree; surfaces = workstreams on one branch.
- Assessment-first: full Fable-verified assessment produces the documentation that gates remediation planning.
- Behavior-preserving by default; delete > deprecate; adversarial verification is non-negotiable.
- BFF publish ≤ 60 MB compressed (baseline 46.89 MB); no new NuGet packages; `/conflict-check` before every remediation PR.

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Multi-agent Workflow as assessment engine | Program scale (6 surfaces × 11 dims × adversarial verify) is its core use case | Reusable `quality-assessment` Workflow; per-run operator opt-in |
| Finance auth via `@spaarke/auth` (not HMAC) | Owner directive: canonical ADR-028 path; fix any web-resource gap "here and elsewhere" | Migrate `sprk_subgrid_parent_rollup.js` caller; may elevate to security horizontal |
| Assessment-first, remediation-later per surface | Can't write remediation tasks against un-verified findings | Surfaces 2–6 remediation deferred until their assessment designs exist |
| Per-surface forcing-function activation | No repo-wide big-bang while another surface is dirty | Each surface flips its own gate as its last workstream step |

### Discovered Resources

**Applicable Skills**:
- `.claude/skills/adr-check/` + `code-review/` — Step 9.5 gates (unconditional on TEST-MODIFYING tasks)
- `.claude/skills/test-diet/` — project-close test reconciliation (ADR-038)
- `.claude/skills/conflict-check/` — before every remediation PR (BFF contention)
- `.claude/skills/doc-drift-audit/` — doc-drift horizontal
- `.claude/skills/devops-idea-create/` + `devops-project-register/` — NG1 Idea + Epic #427 registration
- Workflow tool — the `quality-assessment` engine

**Knowledge / constraints**:
- `.claude/constraints/bff-extensions.md` — binding BFF governance (§10)
- `docs/adr/ADR-028` / `ADR-013` / `ADR-032` / `ADR-038` / `ADR-010` / `ADR-022` / `ADR-002`
- `docs/assessments/bff-ai-extraction-assessment-2026-05-20.md` — evidence base
- `docs/standards/TEST-ARCHITECTURE.md`; `.claude/patterns/auth/spaarke-sso-binding.md`

**Reusable Code / patterns**:
- BFF assessment (proven method) — `projects/bff-api-cleanup-remediation-r1/design.md`
- `CommunicationTriageAi` / `CommunicationEnrichmentService` — facade reference for A-1
- `Spaarke.Dataverse/DataverseServiceClientImpl` — home for `UnwrapServiceClient`
- `Spaarke.ArchTests` — extend into fitness functions

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: Program foundation (rubric, scorecard, quality-assessment Workflow, portfolio)
Phase 1: Full Fable-verified assessment (6 surfaces read-only) + re-baseline  ← GATING
Phase 2: BFF surface remediation (already assessed — 8 tasks)
Phase 3: Horizontal sweeps (security / test / CVE / observability / doc-drift)
Phase 4: Forcing-functions authoring (ArchTests, mechanical baseline, CI gates) — per-surface activation
Phase 5: (DEFERRED) surfaces 2–6 remediation — task-created after each assessment design
Phase 9: Wrap-up (reconciliation, /test-diet, final SCORECARD aggregate)
```

### Critical Path

**Blocking Dependencies:**
- Phase 1 assessments BLOCK Phase 5 (surface remediation) — each surface's design gates its tasks.
- Phase 0 rubric + Workflow BLOCK Phase 1 (assessments use them).
- BFF Phase 2 is NOT blocked (already assessed) — can run in quiet windows now.
- Forcing-function gates (Phase 4) activate per-surface as each surface's remediation completes.

**High-Risk Items:**
- BFF contention (19 worktrees) — Mitigation: `/conflict-check` each PR; small PRs; quiet windows; A/B tranche.
- Data-driven dispatch hides live consumers — Mitigation: Dataverse pre-check before any rename/delete.
- `@spaarke/auth` web-resource gap (Finance auth) — Mitigation: verify caller feasibility first; elevate to horizontal if real.
- False-positive deletions — Mitigation: mandatory Fable adversarial-verification stage.

---

## 4. Phase Breakdown

### Phase 0: Program Foundation

**Objectives:** Author the shared artifacts every surface references; stand up the assessment engine; register the program.

**Deliverables:**
- [ ] `docs/standards/CODE-QUALITY-RUBRIC.md` (D1–D11 + A–F scale)
- [ ] `notes/SCORECARD.md` (living scorecard, BFF row seeded)
- [ ] Reusable `quality-assessment` multi-agent Workflow (fan-out → Fable verify → design)
- [ ] Portfolio registration under Epic #427 + `projects/INDEX.md` row + NG1 filed as Idea

**Critical Tasks:** Rubric FIRST (assessments score against it); Workflow before Phase 1.

**Outputs**: rubric, scorecard, workflow script, portfolio Issue.

### Phase 1: Full Assessment (GATING)

**Objectives:** Run the Fable-verified read-only assessment across all remaining surfaces; publish the honest re-baseline.

**Deliverables:**
- [ ] Assessment `design.md` for: shared client libs, shared server libs, PCF controls, Dataverse model + ALM, code pages + build sprawl, plugins
- [ ] Re-baseline consolidation → honest aggregate published in `SCORECARD.md`

**Inputs**: rubric + `quality-assessment` Workflow (Phase 0). **Outputs**: per-surface designs + re-baseline.

### Phase 2: BFF Surface Remediation (already assessed)

**Objectives:** Execute the BFF workstream's 6-phase remediation as small PRs.

**Deliverables:**
- [ ] Dead-code deletion (~2.7k LOC, 6 items incl. StubLiveFactResolver)
- [ ] 2 bug fixes (invoice cast, dead `.eml` builder) + 13→1 downcast consolidation
- [ ] Auth closure via `@spaarke/auth` (Finance + healthz + OBO/User)
- [ ] Facade compliance (4 violations → `PublicContracts/`)
- [ ] `Endpoints/`→`Api/` migration + `CommunicationModule` decomposition + Finance rename (Dataverse pre-check) + optional Phase-5 helpers
- [ ] Repo hygiene (2 tarballs untracked, artifacts removed)

**Inputs**: `bff-api-cleanup-remediation-r1/design.md`. **Outputs**: cleaned BFF; publish-size + Dataverse-precheck reports.

### Phase 3: Horizontal Sweeps

**Deliverables:** security (`@spaarke/auth` consistency), test quality (`/test-diet` + 138-failing reconciliation), dependency/CVE, observability, doc-drift.

### Phase 4: Forcing-Functions (per-surface activation)

**Deliverables:** expanded `Spaarke.ArchTests` fitness functions; mechanical baseline (analyzers-as-errors, `.editorconfig`, strict ESLint, `tsc --noEmit`); CI gates (CVE/size/doc-drift).

### Phase 5: Surface Remediation (DEFERRED)

Task-created after each Phase-1 assessment design exists. Not decomposed in this pipeline run.

### Phase 9: Wrap-up

**Deliverables:** R3-draft reconciliation record; `/test-diet` gate; final `SCORECARD.md` aggregate + lessons-learned.

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Fable model (adversarial verify) | GA | Low | Manual agent fan-out fallback |
| Workflow tool (per-run opt-in) | GA | Low | Operator invokes "use a workflow" each assessment |
| Dataverse (handler pre-check) | GA | Low | Read-only MCP check before rename/delete |
| Epic #427 | Exists | Low | Verify at registration; verify no orphan R3 Issue |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| BFF assessment design | `projects/bff-api-cleanup-remediation-r1/` | Complete |
| `Spaarke.ArchTests` | `tests/**/Spaarke.ArchTests/` | Extend |
| `ci-cd-unit-test-remediation-r1` | owns `.github/workflows` edits | Coordinate |

---

## 6. Testing Strategy

**Unit / behavior** (ADR-038 KEEP categories): focused test for the invoice-totals path; ArchTest fitness functions fail-on-seeded-violation.
**Integration**: full `dotnet test` green after every BFF phase; route-dump diff for the folder migration.
**Assessment verification**: mandatory Fable adversarial pass per surface (non-negotiable).
**Coverage**: observation, never a gate (ADR-038 / NFR-07).

---

## 7. Acceptance Criteria

See [README.md Graduation Criteria](README.md#graduation-criteria) and [spec.md §Success Criteria](spec.md). Verification methods are attached per criterion in the spec.

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Program sprawl / never "done" | Med | Med | Fixed rubric + SCORECARD makes progress measurable; each surface independently shippable |
| R2 | Assessment findings rot unremediated | Med | High | Every assessment → tracked tasks; forcing-functions prevent re-drift |
| R3 | Broad branch contends with 19 BFF worktrees | High | Med | Read-only assess anytime; small PRs in quiet windows; `/conflict-check`; single INDEX row |
| R4 | False-positive deletions | Med | High | Mandatory Fable adversarial verification; check test-only consumers + data-driven dispatch |
| R5 | `@spaarke/auth` web-resource gap breaks legacy caller | Med | Med | Verify caller feasibility first; elevate to security horizontal |

---

## 9. Next Steps

1. **Review this plan.md + spec.md** (esp. FR-09 `@spaarke/auth` reframing; FR-05 assess-first ordering).
2. **Register portfolio** under Epic #427; add `projects/INDEX.md` row; file NG1 as Idea.
3. **Kick off Phase 0** task 001 (rubric), then the `quality-assessment` Workflow (task 003) — operator opt-in ("use a workflow").

---

**Status**: Ready for Tasks
**Next Action**: `/task-create` produced tasks/ + TASK-INDEX.md; execution is operator-gated (initialize-only).

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
