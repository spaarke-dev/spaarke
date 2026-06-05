# Project Plan: Spaarke AI Platform Unification R4

> **Last Updated**: 2026-05-28
> **Status**: ✅ **Complete** (shipped 2026-05-28 — see [`README.md`](README.md) and [`notes/lessons-learned.md`](notes/lessons-learned.md))
> **Spec**: [spec.md](./spec.md)
> **Authoritative source preserved**: [plan.original.md](./plan.original.md) (465-line operator-authored plan with full WBS, dependencies, risk register, acceptance criteria)
> **Lessons applied**: [`notes/lessons-learned.md`](notes/lessons-learned.md) — captures parallel sub-agent dispatch pattern, verify-then-fix protocol, operator-as-judgment-gate decisions, two-wrapper framing, master-sync-before-deploys discipline, and 5 patterns recommended for future project adoption

---

## 1. Executive Summary

**Purpose**: Consolidate the ~30 follow-up items surfaced during R3 into a single post-shipping round. Codify the new SpaarkeAi two-wrapper architecture. Address operator-mandated A-4 attachment cap raise + W-3 wizard catalog drift bug. Apply CLAUDE.md §10 BFF Hygiene governance to every BFF-touching task.

**Scope**:
- 34 IN items across 8 phases
- 14 FRs, 9 NFRs, 7 DRs, 2 PRs (per [spec.md](./spec.md))
- Documentation FIRST (Phase 1) → Substantive code (Phase 5) → Build hygiene LAST (Phase 6)

**Timeline**: 3-4 weeks sequential; 2-3 weeks parallelized | **Estimated Effort**: ~116 hours

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001** (Minimal API) — B-4, B-5 inherit endpoint patterns
- **ADR-008** (Endpoint filters) — B-5 PATCH inherits filter pipeline
- **ADR-010** (DI minimalism) — A-4, C-3 must register in existing feature modules
- **ADR-012** (Shared components) — Load-bearing for C-3, C-4, B-6, B-7 (`@spaarke/*` lib placements)
- **ADR-013** (AI architecture, refined 2026-05-20) — F-1, F-2 placement justification base
- **ADR-021** (Fluent v9 + tokens only) — Load-bearing for all UI tasks (W-3, W-4, W-5, B-3, B-6, B-7, B-8)
- **ADR-022** (React 19 for Code Pages) — Load-bearing for W-3, W-4, W-5
- **ADR-030** (PaneEventBus pattern) — NEW per A-2; W-4 and W-5 must conform
- **ADR-031** (Stage lifecycle + heavy library handling) — NEW per A-2 + D-2 amendment
- **ADR-028** (Spaarke auth v2) — Load-bearing for A-4, A-5, C-3
- **ADR-029** (BFF publish hygiene) — F-3 codifies as workflow rule; NFR-01 enforces

**From Spec**:
- CLAUDE.md §10 BFF Hygiene: every BFF addition needs Placement Justification + publish-size verification + no new HIGH-severity CVEs
- No new direct injections of `IOpenAiClient` or `IPlaybookService` outside `Services/Ai/`
- LegalWorkspace standalone code page is being RETIRED (W-6) — R3 FR-25/NFR-10 no longer apply

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Two wrappers retained (Dashboard + Direct widget) | Distinct use cases (compose vs sophisticated single-purpose) | W-1 documents both; no unification refactor |
| LegalWorkspace standalone code page retired | Replaced by SpaarkeAi; no operator demand | W-6 retirement; deploy script updates; FR-25/NFR-10 superseded |
| A-4: 25 MB cap (operator override) | Chat attachments now first-class workflow | Code change in Phase 5, not just doc |
| A-5: verify-then-fix | User feedback contradicts R3 verification | Phase 3 has dedicated verify spike |
| D-2 ADR amendment only | Bundle-size Option 2 implementation deferred indefinitely | Phase 1 doc scope cap |

### Discovered Resources

**Applicable Skills** (auto-discovered via project-pipeline Step 2):
- `.claude/skills/task-execute/` — Mandatory protocol for every task (loads knowledge, ADRs, checkpoints, quality gates)
- `.claude/skills/code-review/` — Quality gate; enforces §10 BFF Hygiene
- `.claude/skills/adr-check/` — ADR compliance validator
- `.claude/skills/code-page-deploy/` — Deploy SpaarkeAi + LegalWorkspace web resources
- `.claude/skills/bff-deploy/` — Deploy Sprk.Bff.Api to Azure App Service (A-4, B-4, B-5)
- `.claude/skills/dataverse-deploy/` — Deploy solutions via PAC CLI
- `.claude/skills/merge-to-master/` — Final merge with safety checks
- `.claude/skills/repo-cleanup/` — Phase 0 (E-1) and Phase 7 (R4 wrap-up)
- `.claude/skills/context-handoff/` — Proactive checkpointing per CLAUDE.md §5
- `.claude/skills/push-to-github/` — Commit + push during implementation
- `.claude/skills/ui-test/` — UI smoke tests (W-3, W-4, W-5 demos)
- `.claude/skills/adr-aware/` — Auto-loads ADRs per task tags
- `.claude/skills/doc-drift-audit/` — Phase 7 wrap-up drift check

**Knowledge Articles**:
- `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` — End-to-end pipeline reference (will be supplemented by W-1)
- `docs/architecture/SPAARKEAI-COMPONENT-MODEL.md` — Component inventory + PaneEventBus contract
- `docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md` — Audit that surfaced C-items
- `docs/architecture/auth-azure-resources.md` — Auth + Azure resource map
- `docs/architecture/AI-ARCHITECTURE.md` — F-1/F-2 evidence base
- `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` — Will be rewritten in W-2
- `docs/guides/SHARED-UI-COMPONENTS-GUIDE.md` — `@spaarke/ui-components` consumption
- `docs/guides/auth-deployment-setup.md` — Auth v2 operator runbook
- `docs/standards/CODING-STANDARDS.md`, `INTEGRATION-CONTRACTS.md`, `ANTI-PATTERNS.md` — Cross-cutting standards
- `docs/assessments/bff-ai-extraction-assessment-2026-05-20.md` — Evidence base for F-2 baseline of 20 direct deps
- `.claude/constraints/bff-extensions.md` — **Load-bearing** for §10 (binding pre-merge checklist)
- `.claude/constraints/azure-deployment.md` — Publish-size baseline (target of F-3 update)
- `.claude/patterns/api/endpoint-definition.md` — A-4, B-4, B-5 BFF extension reference
- `.claude/patterns/auth/spaarke-sso-binding.md` — ADR-028 invariants
- `.claude/patterns/webresource/full-page-custom-page.md` — Code Page deploy reference

**Reusable Code / Canonical Locations**:
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` — A-4 server-side cap
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatFileAttachment.ts` — A-4 client-side cap
- `src/server/api/Sprk.Bff.Api/Endpoints/Workspace/WorkspaceLayoutsEndpoint.cs` — B-4, B-5 (exact filename may vary — confirm at task time)
- `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBusContext.tsx` — W-4, W-5 dispatch wiring
- `src/solutions/WorkspaceLayoutWizard/src/App.tsx` — W-3 catalog drift fix
- `src/solutions/LegalWorkspace/src/sectionRegistry.ts` — W-3 single source of truth
- `src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts` + `src/solutions/SpaarkeAi/src/hooks/useWorkspaceLayouts.ts` — C-3 dual hook consolidation
- `src/client/shared/Spaarke.events-components/` (a.k.a. `@spaarke/events-components`) — B-6, B-7, B-8, B-11 events lib
- `src/solutions/CalendarSidePane/src/components/CalendarSection.tsx` — B-6 reconciliation target

**Scripts** (`scripts/`):
- `Deploy-BffApi.ps1` — BFF deploys for A-4, B-4, B-5
- `Build-ViteSolutionsDirect.ps1` — Build SpaarkeAi, CalendarSidePane, WorkspaceLayoutWizard
- `Build-AllClientComponents.ps1` — Lib build validation
- `Deploy-AllWebResources.ps1` — Web resource deployment
- `Deploy-DataverseSolutions.ps1` — Solution packaging
- W-6 will MODIFY existing deploy scripts to skip `sprk_corporateworkspace`

**Dataverse Touch-Points**:
- `sprk_workspacelayout` — B-4 (modifiedOn), B-5 (PATCH/ETag); rowversion field must exist for ETag
- `sprk_corporateworkspace` — W-6 retirement target (no schema change; deploy-only)

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: R3 wrap-up + retroactive memo (~4h)
└─ E-1 R3 closure  |  F-1 retroactive BFF memo

Phase 1: Documentation round (~21h)
└─ W-1 + W-2 + A-2 + C-1 + C-2 + D-2 + F-3
   Parallelizable: W-1, A-2, C-1, F-3 (wave 1) → W-2, C-2, D-2 (wave 2)

Phase 2: BFF governance audit (~2h)
└─ F-2 facade audit memo

Phase 3: UQ-03 verify + fix (~10h)
└─ A-5a verify spike (~2h) → A-5b fix (~4-8h)

Phase 4: Workspace builder + mount sources (~19h)
└─ W-3 + W-6 (parallel) → W-4 + W-5 (parallel, depend on W-1)

Phase 5: Substantive code changes (~31h)
└─ A-4, C-3, B-4, B-5 (wave 1 parallel) → C-4, B-6 (wave 2)

Phase 6: Build hygiene cluster (~21h)
└─ B-1, B-2, B-3, B-7, B-8, B-9, B-10, B-11 — mostly independent; 1-2 waves of 4-6

Phase 7: R4 wrap-up (~2h)
└─ lessons-learned + README → Complete + /repo-cleanup
```

### Critical Path

**Blocking Dependencies**:
- Phase 1 (W-1) BLOCKS Phase 4 (W-2 references it; W-4/W-5 use the model it documents)
- Phase 3 (A-5) is independent — can run in parallel with later phases if verification result is fast
- Phase 6 (build hygiene) should follow Phase 5 (substantive code) so hygiene fixes don't churn during refactors

**High-Risk Items**:
- C-3 hook consolidation — breaking either consumer is high impact; mitigate via dual-surface regression test
- W-6 LW retirement — unanticipated Dataverse form consumer could break; mitigate via consumer audit before retiring
- A-5 verification — Phase 3 may reveal a different bug; remediation re-scoped after verify completes

### Parallelization Cap

Hard cap of **6 concurrent agents per wave** (per `task-execute` skill — API overload guard). Tasks touching `.claude/` paths MUST run sequentially in the main session (per CLAUDE.md §3 Sub-Agent Write Boundary): A-2 (new ADRs in `.claude/adr/`), D-2 (ADR-031 amendment in `.claude/adr/`), F-3 (`.claude/constraints/azure-deployment.md`).

---

## 4. Phase Breakdown

> **Full WBS preserved in [plan.original.md §4](./plan.original.md)** with effort breakdown per task and per-phase acceptance + parallelization notes. This section summarizes for task-create consumption.

### Phase 0: R3 wrap-up + retroactive memo (~4h)

**Objectives**: Close R3 cleanly; publish F-1 retroactive memo.
**Deliverables**:
- [ ] R3 lessons-learned.md + R3 README → Complete + `/repo-cleanup projects/spaarke-ai-platform-unification-r3`
- [ ] `projects/spaarke-ai-platform-unification-r3/notes/bff-placement-justification-retroactive.md`
**Inputs**: R3 artifacts; CLAUDE.md §10
**Outputs**: R3 closure; retroactive audit trail
**Critical**: E-1 + F-1 are independent; can parallelize

### Phase 1: Documentation round (~21h)

**Objectives**: Establish authoritative conceptual frame for SpaarkeAi widget + dashboard architecture, ADRs, decision criteria binding subsequent work.
**Deliverables**:
- [ ] `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` (W-1)
- [ ] `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` rewritten (W-2)
- [ ] ADR-030 (PaneEventBus) + ADR-031 (stage lifecycle) — concise + full forms (A-2)
- [ ] `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` (C-1)
- [ ] `docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` (C-2)
- [ ] ADR-031 amendment for heavy library handling (D-2)
- [ ] CLAUDE.md §10 + `.claude/constraints/azure-deployment.md` updated for publish-size rule (F-3)
**Inputs**: spec.md; existing docs
**Outputs**: Authoritative architecture frame before any code change
**Parallelization**: Wave 1 (parallel): W-1, A-2, C-1, F-3. Wave 2 (sequential): W-2, C-2, D-2.

### Phase 2: BFF governance audit (~2h)

**Objectives**: Count remaining direct CRUD→AI dependencies; baseline for migration tracking.
**Deliverables**:
- [ ] `notes/bff-ai-facade-audit-2026-05.md` (F-2)
**Inputs**: `.claude/constraints/bff-extensions.md`; `docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`
**Outputs**: Audit memo with count vs 2026-05-20 baseline of 20

### Phase 3: UQ-03 verify + fix (~10h)

**Objectives**: Verify current tab persistence behavior; fix the actually-broken gap.
**Deliverables**:
- [ ] `notes/tab-persistence-verification-2026-05.md` (A-5a verify spike)
- [ ] Remediation deployed; operator confirmed (A-5b)
**Critical**: Verify-then-fix; do NOT pre-build a fix for a non-issue.

### Phase 4: Workspace builder + mount sources (~19h)

**Objectives**: Fix wizard catalog drift; wire Assistant + Context mount sources; record LW retirement.
**Deliverables**:
- [ ] `WorkspaceLayoutWizard` reads `SECTION_REGISTRY` (W-3)
- [ ] `docs/architecture/LEGALWORKSPACE-RETIREMENT.md` + deploy scripts updated (W-6)
- [ ] Assistant → Workspace demo end-to-end (W-4)
- [ ] Context → Workspace demo end-to-end (W-5)
**Parallelization**: W-3 + W-6 (parallel). W-4 + W-5 can be parallel after W-1 + W-3 land.

### Phase 5: Substantive code changes (~31h)

**Objectives**: Address operator-mandated code changes + architectural refactors + BFF DTO/endpoint improvements.
**Deliverables**:
- [ ] Chat attachment cap → 25 MB + `CHAT-ATTACHMENT-POLICY.md` (A-4)
- [ ] Consolidated `useWorkspaceLayouts` hook (C-3)
- [ ] `WorkspaceRenderer` interface (C-4)
- [ ] `WorkspaceLayoutDto.modifiedOn` field (B-4)
- [ ] BFF PUT → PATCH/ETag (B-5)
- [ ] `CalendarSidePane.CalendarSection` reconciled (B-6)
**Parallelization**: Wave 1: A-4, C-3, B-4, B-5. Wave 2: C-4, B-6.

### Phase 6: Build hygiene cluster (~21h)

**Objectives**: Address build/type/lint debt. Mostly small independent items.
**Deliverables**:
- [ ] `.gitignore` cleanup + `git rm --cached` (B-1)
- [ ] `@spaarke/ai-widgets` tsc rootDir fix (B-2)
- [ ] Telemetry constant rename (B-3)
- [ ] `useEventsBulkActions` hook extracted (B-7)
- [ ] `CalendarDrawer.eventDates` API upgrade (B-8)
- [ ] ESLint v9 flat config (B-9)
- [ ] EventsPage redeployed (B-10)
- [ ] Type-drift casts cleaned up (B-11)
**Parallelization**: All 8 items independent; 1-2 waves of 4-6 agents.

### Phase 7: R4 wrap-up (~2h)

**Objectives**: Close R4 cleanly.
**Deliverables**:
- [ ] `notes/lessons-learned.md` for R4
- [ ] R4 README → Status: Complete; graduation criteria boxes checked
- [ ] `/repo-cleanup projects/spaarke-ai-platform-unification-r4`
- [ ] `current-task.md` reset to none

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Dataverse dev environment (`spaarkedev1`) | Ready | Low | Already used in R3 |
| Azure App Service (BFF deploy slots: production + warmup) | Ready | Medium | Publish-size verification per BFF task (F-3 rule) |
| Operator review | On-demand | Medium | A-5 verify gates remediation; W-3/W-4/W-5 demos operator-visible |
| Microsoft Graph | Ready | Low | Unchanged from R3 |
| SharePoint Embedded | Ready | Low | Unchanged from R3 |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| R3 shipped + master at `3813af32` | Master | Production (2026-05-22, task 140) |
| `@spaarke/auth` v2 (`authenticatedFetch`) | `src/client/shared/Spaarke.AI.Auth/` | Production |
| PaneEventBus | `src/client/shared/Spaarke.AI.Widgets/src/events/` | Production (R2) |
| Calendar widget (Pattern D dual-use reference) | `@spaarke/events-components` + LW registration shim | Production (R3 task 115) |
| ADR-013 refined (BFF AI facade) | `.claude/adr/ADR-013-ai-architecture.md` | Current (refined 2026-05-20) |
| CLAUDE.md §10 BFF Hygiene | `CLAUDE.md` root | Current (added 2026-05-19) |

---

## 6. Testing Strategy

**Per-task verification** (every code-touching task):
- Unit tests for new behaviors
- Build verification: `npm run build` (Vite solutions) or `dotnet build` (BFF)
- Bundle-size measurement (SpaarkeAi/LegalWorkspace changes; F-3 rule)
- BFF publish-size check (`dotnet publish` → ≤60 MB; NFR-01)
- CVE check: `dotnet list package --vulnerable --include-transitive` (NFR-09)

**Phase-level verification**:
- **Phase 0**: R3 graduation criteria checklist
- **Phase 1**: All docs reviewed for terminology consistency; cross-links validated
- **Phase 2**: F-2 audit numerical claims spot-checked
- **Phase 3**: Operator end-to-end test for tab persistence
- **Phase 4**: Operator end-to-end test for wizard + Assistant + Context demos
- **Phase 5**: SpaarkeAi + CalendarSidePane regression; A-4 boundary tests at 1/10/24/26 MB
- **Phase 6**: 0 errors on `tsc --noEmit`; 0 lint errors; `git status` clean after fresh build
- **Phase 7**: `/repo-cleanup` audit clean

**Integration testing**:
- After Phase 5: Full SpaarkeAi smoke (Assistant + Workspace + Context) on dev
- After Phase 6: Smoke + EventsPage standalone (B-10)
- After Phase 7: Operator sign-off gate

**UI smoke** via `ui-test` skill (Chrome): W-3, W-4, W-5 demos; B-6 CalendarSidePane parity; B-10 EventsPage parity.

---

## 7. Acceptance Criteria

Mirrors [README.md Graduation Criteria](./README.md#graduation-criteria) and [spec.md §Success Criteria](./spec.md#success-criteria) (25 checkboxes across Code+deploy / Documentation / Behavior / Process).

---

## 8. Risk Register

Full risk register (10 items) preserved in [plan.original.md §8](./plan.original.md). Top risks:

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R-1 | A-5 verification reveals a different bug | High | Medium | Phase 3 verify-first; re-scope after verify |
| R-2 | A-4 25 MB blows bundle / BFF text-content limits | Medium | Medium | Bundle impact from text extraction (capped chars); confirm in verification |
| R-3 | C-3 hook consolidation breaks LW or SpaarkeAi | Medium | High | Test both surfaces end-to-end; keep auth-source flexible |
| R-6 | W-6 LW retirement breaks unanticipated consumer | Low | High | Audit `corporateworkspace` Dataverse references before retiring |
| R-7 | W-4/W-5 mount-source wiring exceeds estimate | Medium | Medium | Scope reduction: dispatch + one widget; broader coverage to R5 |

---

## 9. Next Steps

1. **Run** `/task-create projects/spaarke-ai-platform-unification-r4` to decompose this plan into POML task files
2. **Review** generated `tasks/TASK-INDEX.md` for parallel groups + dependencies
3. **Begin** Phase 0 (E-1 + F-1 — independent, can parallelize)

---

**Status**: Ready for Tasks
**Next Action**: Run `task-create` to generate `tasks/` POML files

---

*For Claude Code: This plan provides implementation context. The authoritative WBS with full risk register and per-task effort is at [plan.original.md](./plan.original.md). Load both files when planning task execution.*
