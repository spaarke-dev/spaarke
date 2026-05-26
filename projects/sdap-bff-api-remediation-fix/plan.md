# Project Plan: BFF API Remediation & Publish Debt

> **Last Updated**: 2026-05-20
> **Status**: Ready for Tasks
> **Spec**: [`spec.md`](spec.md) | **Design**: [`design.md`](design.md)

---

## 1. Executive Summary

**Purpose**: Reduce `Sprk.Bff.Api` deploy package from 75.19 MB → ≤60 MB compressed and 212 MB → ≤150 MB uncompressed; close vulnerable-transitive HIGH CVEs; introduce CI guardrails and codified prevention so the debt does not return; introduce `Services/Ai/PublicContracts/` facade and relocate 6 AI-coupled job handlers per refined ADR-013.

**Scope** (5 outcomes):
- A. Size reduction (publish-side configuration)
- B. Security hygiene (vulnerable-transitive triage)
- C. CI guardrails (hard fails + PR-label escape hatches)
- D. Codified prevention (ADR-029, constraint + skill + FAILURE-MODES + CLAUDE.md updates)
- E. Internal AI hygiene (facade + AI job-handler relocation; no AI extraction)

**Timeline**: 4–6 weeks calendar (bake-window dominated) | **Estimated Effort**: ~4–6 days active work

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (binding strength clarified 2026-05-24):

| ADR | Binding strength | What it binds for THIS project |
|---|---|---|
| **ADR-001** Minimal API + Workers | **Project-scope** (not architectural absolute) | Do NOT move BFF endpoints to Functions during remediation — adds a second moving variable to Phase 4 bakes. Out-of-band Functions (existing webhook/Service Bus triggers) are unaffected. Moving endpoints to Functions post-project is a separate design conversation. |
| **ADR-004** Job Contract | **Technical invariant** (interface-enforced) | FR-E3 handler relocation preserves `IJobHandler<T>` + JobType string dispatch. Service Bus message contract unchanged. |
| **ADR-007** SpeFileStore Facade | **Pattern reference** | Canonical model for Outcome E facade design (task 046). Mirror the file/method shape. |
| **ADR-008** Endpoint Filters | **Preserved by no-change** | Auth filters not edited during Outcome E migration (filter signatures preserved per task 050). |
| **ADR-010** DI Minimalism | **Measurable project binding** (new) | Known violation (99+ vs ≤15) is out of scope to fix. **Task 038** captures baseline; **task 054 (gate)** verifies Phase 4 final delta is in expected range (+4 to +8 from Outcome E facade interfaces, then justified) OR larger delta requires written justification. Note: Outcome E REGISTRATION count goes UP slightly (facade adds); CONSUMER-SIDE DEPENDENCY count goes DOWN (~59 fewer constructor params) — these are distinct metrics. |
| **ADR-013 refined 2026-05-20** AI Architecture | **Technical binding** | REQUIRES `Services/Ai/PublicContracts/` facades for external CRUD consumers. FR-E1/FR-E2 enforce. |
| **ADR-027 (a)** Subscription Isolation | **Real, operational** | Phase 1–4 work targets `spe-api-dev-67e2xz` only (dev subscription). Per-environment Key Vault refs, connection strings, App Insights instances are subscription-scoped. Phase 5 demo/prod target their own subscriptions. |
| **ADR-027 (b)** Managed Solutions | **Conditionally applicable** | Managed-solution discipline applies to Power Platform components (entities, forms, web resources, PCF, plugins). This project remediates an App Service — managed solutions are NOT directly involved unless the canonical Phase 5 prod process (UQ-02) bundles related Power Platform component updates. |
| **ADR-028** Spaarke Auth v2 | **Preserved by no-change** | No auth flow modifications in this project. |
| **ADR-029** NEW BFF Publish Hygiene | **Becomes binding when Phase 6 lands** | Output of FR-D1 (tasks 076/077). After publication, every future BFF PR is governed. |

**From Spec**:

- ❌ MUST NOT set `<PublishTrimmed>true</PublishTrimmed>` or `<PublishAot>true</PublishAot>` (reflection-hostile)
- ❌ MUST NOT add new direct CRUD→AI dependencies (use facade instead)
- ❌ MUST NOT bump Kiota packages individually, Graph SDK, .NET TFM, or pre-release AI packages
- ❌ MUST NOT deploy from external temp directory (anti-pattern #16)
- ✅ MUST publish to `deploy/api-publish/` (NOT `/tmp`)
- ✅ MUST route external CRUD-side AI consumers through `Services/Ai/PublicContracts/` facade
- ✅ MUST use `Deploy-BffApi.ps1` for all BFF deploys (hash-verify + health-check + slot-swap rollback)
- ✅ MUST load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) for any task touching the BFF

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Keep AI in BFF (no extraction) | Latency budgets + transactional Cosmos + 100% streaming AI | Outcome E is in-BFF cleanup, not extract-prep |
| Framework-dependent `linux-x64` publish | App Service is unambiguously Linux | ~25–30 MB uncompressed / ~10 MB compressed savings |
| Small focused facade interfaces (`IBriefingAi`, `IInvoiceAi`, etc.) | Easier testing, lower coupling, simpler deprecation | UQ-07 default (owner confirms in Phase 0) |
| Hard-fail size guard at baseline+10% | Forces explicit acknowledgment of regressions | FR-C5 + `-AllowOversize` escape |
| **Operator-only approval model** (revised 2026-05-24) — owner sign-off + AI-directed verification + CI guards | Spaarke uses AI-directed coding procedures (`task-execute` invokes `adr-check` + `code-review` at Step 9.5 FULL rigor) + mechanical CI gates (FR-C1–C6); dual-approver enterprise pattern is unnecessary friction for single-owner operating model | Replaces dual-approver pattern across Phase 4 MEDIUM/HIGH + Phase 5 prod; see NFR-08 (revised) |

### Discovered Resources

**Applicable ADRs** (full text loaded per task via `adr-aware` + tag mapping):
- [`.claude/adr/ADR-001-minimal-api.md`](../../.claude/adr/ADR-001-minimal-api.md)
- [`.claude/adr/ADR-004-job-contract.md`](../../.claude/adr/ADR-004-job-contract.md)
- [`.claude/adr/ADR-007-spefilestore.md`](../../.claude/adr/ADR-007-spefilestore.md) — **canonical facade pattern for Outcome E**
- [`.claude/adr/ADR-008-endpoint-filters.md`](../../.claude/adr/ADR-008-endpoint-filters.md)
- [`.claude/adr/ADR-010-di-minimalism.md`](../../.claude/adr/ADR-010-di-minimalism.md) — no-worsen rule
- [`.claude/adr/ADR-013-ai-architecture.md`](../../.claude/adr/ADR-013-ai-architecture.md) — **refined 2026-05-20; binding for FR-E1/FR-E2**
- [`.claude/adr/ADR-027-subscription-isolation-managed-solutions.md`](../../.claude/adr/ADR-027-subscription-isolation-managed-solutions.md)
- [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md)
- `.claude/adr/ADR-029-bff-publish-hygiene.md` — **DOES NOT YET EXIST**; created in Phase 6 (FR-D1)

**Applicable Constraints**:
- [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) — publish location, baseline size; FR-D2 adds "Publish Hygiene" subsection
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — **binding pre-merge checklist**; already references ADR-029 as "forthcoming"
- [`.claude/constraints/api.md`](../../.claude/constraints/api.md), [`ai.md`](../../.claude/constraints/ai.md), [`jobs.md`](../../.claude/constraints/jobs.md) — tag-mapped per task

**Applicable Skills**:
- [`.claude/skills/bff-deploy/SKILL.md`](../../.claude/skills/bff-deploy/SKILL.md) — canonical deploy; FR-D3 adds "Publish Hygiene" section + next-review-date stamp
- [`.claude/skills/task-execute/SKILL.md`](../../.claude/skills/task-execute/SKILL.md) — invokes adr-aware + code-review + adr-check
- [`.claude/skills/adr-aware/SKILL.md`](../../.claude/skills/adr-aware/SKILL.md), [`.claude/skills/adr-check/SKILL.md`](../../.claude/skills/adr-check/SKILL.md), [`.claude/skills/code-review/SKILL.md`](../../.claude/skills/code-review/SKILL.md)

**Knowledge / Patterns**:
- [`src/server/api/Sprk.Bff.Api/CLAUDE.md`](../../src/server/api/Sprk.Bff.Api/CLAUDE.md) — module guidance; FR-D7 adds Publish Hygiene + AI Boundary sections
- [`src/server/api/Sprk.Bff.Api/Services/SpeFileStore.cs`](../../src/server/api/Sprk.Bff.Api/Services/SpeFileStore.cs) — **canonical facade-over-Graph-SDK pattern, model for Outcome E task 046**
- [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) — evidence base; archived to `baseline/extraction-assessment-archive.md` in Phase 3
- [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) §22.2 — extraction-trigger context
- [`docs/standards/ANTI-PATTERNS.md`](../../docs/standards/ANTI-PATTERNS.md) #16 — `/tmp` publish anti-pattern
- [`docs/guides/auth-deployment-setup.md`](../../docs/guides/auth-deployment-setup.md) §9 — endpoint smoke-test list source (Phase 3)
- [`.claude/FAILURE-MODES.md`](../../.claude/FAILURE-MODES.md) — AP-1, G-2, G-3 (existing); FR-D4 adds new bloat/process-debt entry

**Scripts**:
- [`scripts/Deploy-BffApi.ps1`](../../scripts/Deploy-BffApi.ps1) — current size guard warns at >100 MB (no fail); FR-C5 → hard-fail at baseline+10% + `-AllowOversize` escape

### Code-State Deltas vs Spec (verified during pipeline pre-flight)

1. **CRUD→AI consumer count**: spec FR-E2 cites 20 inbound deps; codebase reality is ~59 files / 148 occurrences with per-folder distribution differing from spec (Workspace 2 vs 4, Jobs 2 vs 6, Dataverse 0 vs 2, Filters 1 vs 5+). **Resolution**: Outcome E task scope defers to Phase 1 inventory; tasks reference "inventory-derived consumer list" rather than spec's preliminary "20". `spec.md` left intact; Phase 1 output is source of truth.
2. **`Services/Ai/Handlers/` already exists** (with `GenericAnalysisHandler`); FR-E3's new `Services/Ai/Jobs/` is a distinct sibling. Task 051 naming makes this clear.
3. **4 `.map` files** in `wwwroot/playbook-builder/assets/`, smaller than spec's "~7 MB" implied scale (still in scope for FR-A2).
4. **csproj has NO `<RuntimeIdentifier>`, NO `<PublishTrimmed>`, NO `<PublishAot>`** — FR-A1 cleanly applies.
5. **Pre-release pins exact match**: `Azure.AI.Projects 1.0.0-beta.8`, `Microsoft.Agents.AI 1.0.0-rc1`, `Azure.AI.OpenAI 2.8.0-beta.1`.
6. **Active confirmed vulnerability**: NU1903 HIGH on `Microsoft.Kiota.Abstractions 1.21.2` (build-warning observed at pipeline pre-flight). Will be enumerated in Phase 1.

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0 — Pre-flight resolution         (1–2 days,    9 tasks: 001–009)
  └─ Resolve UQ-01..UQ-07 (UQ-01 operator-only) + active-BFF-projects enumeration + rollback drill + sign-offs

Phase 1 — Inventory (READ-ONLY)         (1 day,       9 tasks: 010–018)
  └─ INVENTORY.md authoritative snapshot

Phase 2 — Categorize candidates         (1–2 days,    3 tasks: 020–022)
  └─ CANDIDATES.md with SAFE / MEDIUM / HIGH / REJECT tiers

Phase 3 — Baseline                      (2–3 days,    9 tasks: 030–038)
  └─ BASELINE.md + 48h App Insights window + DI registration count baseline

Phase 4 — Apply changes (one per bake)  (2–3 weeks,  15 tasks: 040–054)
  ├─ Outcome A track: SAFE candidates (sequential by 24h bake)
  ├─ Outcome B track: vuln patches (sequential by tier)
  └─ Outcome E track: facade + migration + handler relocation (parallel with A);
                       commits 046–051 squash-merge as single atomic PR (G9)

Phase 5 — Promote demo + prod           (1–2 weeks,   4 tasks: 060–063, conditional on UQ-02)
  └─ Cumulative changeset → demo (48h bake) → prod (7d observation)

Phase 6 — Prevention / codification     (3–5 days,   13 tasks: 070–082)
  └─ All sequential; .claude/ paths = main-session-only; task 082 = FR-C6 CI gate (M5 binding)

Wrap-up                                 (1 day,       1 task:  090)
  └─ LESSONS-LEARNED + repo-cleanup

Total: 63 tasks
```

### Critical Path

001 → 009 (rollback drill) + 002…007 (Phase 0 sign-offs, parallel-safe Group A) → 008 (Phase 0 gate) → 015/018 (Phase 1 reflection probe + INVENTORY.md) → 020–022 (CANDIDATES.md) → 030/033/036/037/038 (Phase 3 baseline + 48h calendar gate + DI baseline) → 040 → 041 → 042 (Outcome A SAFE candidates, 24h bake each) → 053/054 (Outcome E acceptance + Phase 4 gate) → 060/061 (demo) → 062/063 (prod, conditional) → 070–082 (codification incl. FR-C6 gate task 082) → 090

### High-Risk Items

- **015** Reflection-load dynamic probe — code instrumentation; localhost-only diagnostic flag
- **033** App Insights 48h baseline — calendar gate before Phase 4 can start
- **040/041/042** Outcome A SAFE candidates — each 24h bake; reflection-load probe must match baseline post-change
- **062** Production deploy — conditional on UQ-02; owner sign-off + AI verification + ops team authorization (per NFR-08 revised; ops still required for canonical prod process)

### Parallel-Safety Boundaries

Per [root CLAUDE.md §3 — Sub-Agent Write Boundary](../../CLAUDE.md), any task touching `.claude/` paths is **main-session-only** and `parallel-safe: false`. This affects all of Phase 6 (tasks 070–081 except 070/071–075 which touch `scripts/` and `.github/`) and any Phase 0 task that updates the constraint surface.

---

## 4. Phase Breakdown

### Phase 0 — Pre-flight resolution (9 tasks: 001–009)

**Objectives:**
1. Resolve open questions (UQ-01 operator-only / UQ-02..UQ-07) from spec
2. Document operator-only approval model per revised NFR-08 (no dual approver)
3. Enumerate + coordinate ALL active BFF-touching projects (expanded scope per senior-review M3)
4. Verify rollback capability via wall-clock drill (NFR-06 / senior-review G5)
5. Determine Phase 5 scope (in-project vs follow-up)

**Deliverables:**
- [ ] Owner sign-off on design §3 Resolved Decisions (task 001)
- [ ] Operator-only approval model documented (UQ-01 RESOLVED) (task 002)
- [ ] Prod deploy process determined (UQ-02) (task 003)
- [ ] All active BFF-touching projects enumerated + coordinated (task 004, expanded scope)
- [ ] Insights Engine baseline window + facade adoption agreement (UQ-04 + G4) (task 005)
- [ ] CI ceiling confirmed (UQ-05) (task 006)
- [ ] Outcome E scope + facade granularity + G1 handler reconciliation (UQ-06, UQ-07, G1) (task 007)
- [ ] Rollback drill wall-clock recorded (G5 / NFR-06) (task 009)
- [ ] Phase 0 gate review passed (task 008)

**Inputs**: spec.md, design.md, owner decision authority
**Outputs**: Phase 0 sign-off comment in PR; current-task.md status updates

### Phase 1 — Inventory (9 tasks: 010–018)

**Objectives:**
1. Produce `INVENTORY.md` — authoritative snapshot of BFF runtime composition
2. Identify SAFE / MEDIUM / HIGH removal candidates
3. Capture reflection-load baseline for Phase 4 comparison

**Deliverables:**
- [ ] Direct + transitive package lists (task 010)
- [ ] Vulnerable + outdated package scan (task 011) — feeds Outcome B
- [ ] Pre-release tracker + inline pinning rationale (task 012)
- [ ] Project reference graph (task 013)
- [ ] Direct-package static usage map (task 014)
- [ ] Reflection-load dynamic probe (task 015) — NEW
- [ ] Native binary + wwwroot asset inventory + size-by-category (task 016)
- [ ] App Service runtime + deploy SHAs + zip metrics (task 017)
- [ ] CI workflow inventory + G-3 version sanity + commit INVENTORY.md (task 018)

**Inputs**: Phase 0 sign-off, `Sprk.Bff.Api.csproj`, current deploy state
**Outputs**: `projects/sdap-bff-api-remediation-fix/inventory/*` + INVENTORY.md

### Phase 2 — Categorize candidates (3 tasks: 020–022)

**Objectives:**
1. Risk-tier every potential change with evidence + test plan + rollback
2. Decide SAFE → MEDIUM → HIGH execution order

**Deliverables:**
- [ ] SAFE tier candidates documented (task 020)
- [ ] MEDIUM tier candidates documented (task 021)
- [ ] HIGH tier candidates + REJECT list + commit CANDIDATES.md (task 022)

### Phase 3 — Baseline (9 tasks: 030–038)

**Objectives:**
1. Capture verified-good behavior pre-Phase-4
2. Establish numerical baselines for App Insights, tests, warnings, publish metrics, DI registrations

**Deliverables:**
- [ ] Test suite baseline (task 030)
- [ ] Build warning count baseline (task 031)
- [ ] Endpoint smoke-test results (task 032)
- [ ] App Insights 48h metrics export (task 033) — **48h calendar gate**
- [ ] Deployed file SHA-256s via Kudu (task 034)
- [ ] Publish + zip metrics (task 035)
- [ ] Reflection-load probe baseline (task 036)
- [ ] DI registration count baseline (task 038) — **makes ADR-010 binding measurable**
- [ ] Archive extraction-assessment + commit BASELINE.md (task 037)

### Phase 4 — Apply changes (15 tasks: 040–054)

**Objectives:**
1. Execute approved cleanups + security patches one-per-deploy with 24–48h bake
2. Migrate inbound CRUD→AI dependencies through facade (Outcome E parallel track)
3. Relocate 6 AI-coupled job handlers

**Deliverables — Outcome A track** (sequential by bake):
- [ ] `--runtime linux-x64` publish (task 040; FR-A1)
- [ ] Exclude `wwwroot/**/*.js.map` (task 041; FR-A2)
- [ ] Remove duplicate Cosmos `ServiceInterop.dll` if RID trim didn't (task 042; FR-A3)

**Deliverables — Outcome B track** (sequential, one per package):
- [ ] Vuln patch 1 (task 043) — known: NU1903 HIGH on `Microsoft.Kiota.Abstractions 1.21.2`
- [ ] Vuln patch 2 (task 044)
- [ ] Vuln patch 3 (task 045)
- *(Count refined post-Phase 1; placeholder 3)*

**Deliverables — Outcome E track** (parallel with A):
- [ ] Create `Services/Ai/PublicContracts/` + facade interfaces (task 046; FR-E1)
- [ ] Migrate Finance consumers (task 047; 3 files)
- [ ] Migrate Workspace consumers (task 048; ~2 files actual)
- [ ] Migrate Jobs consumers (task 049; ~2 files actual)
- [ ] Migrate Dataverse + Api/Filters + Api/Endpoints consumers (task 050; ~7 files actual)
- [ ] Relocate 6 AI-coupled job handlers to `Services/Ai/Jobs/` (task 051; FR-E3)

**Verification**:
- [ ] `dotnet test tests/unit/Sprk.Bff.Api.Tests/` — within ±5% baseline (task 052; FR-E4)
- [ ] Acceptance grep for `IOpenAiClient`/`IPlaybookService` (task 053; FR-E2 acceptance)
- [ ] Phase 4 EXECUTION-LOG.md + gate review (task 054)

### Phase 5 — Promote demo + prod (4 tasks: 060–063, conditional)

**Objectives:**
1. Replicate validated changes to demo (48h bake)
2. Replicate to prod with 7-day observation (if UQ-02 resolves "yes")

**Deliverables:**
- [ ] Demo deploy via canonical process (task 060)
- [ ] Demo smoke test + 48h bake (task 061)
- [ ] Prod deploy (task 062; conditional on UQ-02 = yes)
- [ ] 7-day prod observation + LESSONS-LEARNED entries (task 063; conditional)

If UQ-02 = "no canonical prod process": tasks 062/063 deferred to follow-up project; demo deploy + bake remain in scope.

### Phase 6 — Prevention / codification (13 tasks: 070–082)

**Objectives:**
1. Make debt-return loud and obvious via CI gates
2. Codify policy via ADR-029 + constraint + skill updates
3. Update BFF CLAUDE.md with Publish Hygiene + AI Boundary sections

All tasks `parallel-safe: false` (sequential main-session per sub-agent write boundary on `.claude/` paths).

**Deliverables:**
- [ ] `Deploy-BffApi.ps1` hard-fail size guard + `-AllowOversize` (task 070; FR-C5)
- [ ] CI: fail on non-Linux RIDs (task 071; FR-C1)
- [ ] CI: fail on `*.js.map` in publish (task 072; FR-C2)
- [ ] CI: fail on HIGH-severity vuln transitive (task 073; FR-C3)
- [ ] CI: PR-label escape hatches + PR template (task 074; FR-C4)
- [ ] Workflow alignment: G-2 health-check 120s + G-3 actions/* (task 075; FR-D5, FR-D6)
- [ ] ADR-029 (concise) at `.claude/adr/` + INDEX update (task 076; FR-D1 part 1)
- [ ] ADR-029 (full) at `docs/adr/` + INDEX update (task 077; FR-D1 part 2)
- [ ] `.claude/constraints/azure-deployment.md` Publish Hygiene subsection (task 078; FR-D2)
- [ ] `.claude/skills/bff-deploy/SKILL.md` Publish Hygiene + next-review-date (task 079; FR-D3)
- [ ] `.claude/FAILURE-MODES.md` new entry (task 080; FR-D4)
- [ ] `src/server/api/Sprk.Bff.Api/CLAUDE.md` Publish Hygiene + AI Boundary + bff-extensions ref (task 081; FR-D7)
- [ ] **FR-C6 CI gate: block direct CRUD→AI dependency injection (task 082; senior-review M5 binding)** — converts Outcome E into a permanent architectural boundary

### Wrap-up (1 task: 090)

- [ ] Project wrap-up: README→Complete, plan→✅, LESSONS-LEARNED.md commit, code-review, adr-check, repo-cleanup (task 090)

---

## 4.5. Process Rules (binding for project execution)

### PR-1 — Dependabot deferral during project window (senior-review G8)

Dependabot PRs touching `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` or its transitive chains are **AUTO-DEFERRED** to the project owner during this project (do not auto-merge). The project owner triages weekly. If a Dependabot PR conflicts with task 043–045 (vuln patches), defer the Dependabot PR.

Rationale: 15+ open Dependabot PRs touch BFF csproj. During the 4-6 week project window, additional Dependabot PRs may land. A mid-Phase-4 Dependabot merge would either (a) invalidate a 24-48h bake window or (b) bump a chain-locked dependency (e.g., Microsoft.Graph) silently. Auto-deferral with weekly triage prevents both.

### PR-2 — Outcome E single atomic PR (senior-review G9, owner binding 2026-05-24)

Tasks 046–051 are executed individually per their POML files (each task gets its own commit on a feature branch, preserving per-task rigor and review granularity in commit messages). However, ALL 6 commits are bundled into a **SINGLE ATOMIC PR** for merge to master.

Rationale: 59-file structural refactor cannot be safely `git revert`ed mid-Phase-4-bake (unrelated changes may land on top during the bake window). A single squashed merge unit means clean atomic rollback if Phase 4 surveillance detects regression. Trade-off accepted: lose per-task PR-review granularity in exchange for clean rollback path.

Mechanism: branch `feature/outcome-e-facade-migration` accumulates commits 046→047→048→049→050→051; PR opened against master with squash-merge strategy enforced; PR description summarizes each commit's contribution.

### PR-3 — Approval model (senior-review NFR-08 reframe 2026-05-24)

**Operator-only approval model.** All MEDIUM/HIGH-tier Phase 4 candidates and Phase 5 promotions require:
- (a) owner sign-off
- (b) AI-directed verification via `task-execute` skill's mandatory `adr-check` + `code-review` gates at Step 9.5 (FULL rigor for code-touching tasks)
- (c) passing CI guards (FR-C1–C6)

The dual-approver enterprise pattern is explicitly NOT used. Spaarke's single-owner + AI-directed-coding operating model provides effective check-and-balance without the friction of a second human approver. Ops team coordination for Phase 5 prod deploy is separate (operational requirement; not an approval gate).

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Azure App Service `spe-api-dev-67e2xz` | Available | Low | dev environment for Phase 1–4 |
| Azure App Service `spaarke-demo` | Available | Low | Phase 5 demo bake |
| App Insights workspace | Available | Low | Baseline + Phase 4 observation |
| Azure CLI authenticated session | Available | Low | Required for `az webapp show` queries |
| GitHub Actions write access | Available | Low | Phase 6 CI guard PRs |
| `actionlint` tool | Optional | Low | GitHub-side action linter is acceptable fallback |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| **All active BFF-touching projects** (per Phase 0 task 004 expanded scope, senior-review M3) | enumerated at Phase 0; deploy freeze commitment per project | Operator confirms coordination at Phase 0 gate |
| `sdap-bff-api-and-performance-enhancement-r1` | `projects/sdap-bff-api-and-performance-enhancement-r1/` | Active — UQ-03 + M3 coordination |
| `auth-sso-and-email-wizard-2026-05` | active worktree | UQ-03 + M3 expanded — ~30 BFF consumers affected |
| `sdap-file-upload-document-r2` | active worktree | UQ-03 + M3 expanded |
| `spaarke-self-service-registration` | active worktree | UQ-03 + M3 expanded |
| `ai-spaarke-insights-engine-r1` | `projects/ai-spaarke-insights-engine-r1/` | Pre-implementation — UQ-04 coordination + facade adoption agreement (G4) |
| `ai-spaarke-action-engine-r1` | queued for after this remediation per 2026-05-22 decision | Sequenced after Phase 6 codification |
| `Deploy-BffApi.ps1` | `scripts/Deploy-BffApi.ps1` | Production-ready |
| ADR-013 (refined) | `.claude/adr/ADR-013-ai-architecture.md` | Already published 2026-05-20 |
| `.claude/constraints/bff-extensions.md` | Already in place | Binding |

---

## 6. Testing Strategy

**Unit Tests**: All existing `tests/unit/Sprk.Bff.Api.Tests/` must pass with pass count + duration within ±5% of Phase 3 baseline (FR-E4). No new tests required for facade migration — existing tests verify behavior preservation.

**Integration Tests**: Endpoint smoke-test pass against every documented endpoint after each Phase 4 candidate deploy (mandated by per-candidate procedure step 6 in design §6 Phase 4).

**Operational Tests**: App Insights observation window (24h dev / 48h demo / 7d prod) checks:
- No new exception types vs baseline
- Error rate within 10% of baseline
- P95 latency within 10% of baseline per endpoint
- Dependency call success rates unchanged (Graph, Dataverse, Service Bus, Cosmos, Redis)

**Reflection-Load Probe**: Phase 1 captures assembly load list; Phase 4 verifies post-change matches baseline (or differences are accounted for).

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 0:**
- [ ] All 7 open questions answered (UQ-01…UQ-07)
- [ ] Phase 0 gate review passed (task 008)

**Phase 1–3:**
- [ ] INVENTORY.md, CANDIDATES.md, BASELINE.md committed and approved
- [ ] App Insights 48h baseline captured outside sibling-project integration window

**Phase 4:**
- [ ] Each Outcome A SAFE candidate stable in dev 24–48h
- [ ] Outcome B vuln patches: zero HIGH-severity CVE residual
- [ ] Outcome E grep acceptance: zero direct `IOpenAiClient`/`IPlaybookService` in `Services/{Finance,Workspace,Jobs,Dataverse}/` outside `Services/Ai/`

**Phase 5 (if in scope):**
- [ ] Demo bake 48h with no regression
- [ ] Prod 7-day observation with no regression

**Phase 6:**
- [ ] CI guards pass on a synthetic test PR
- [ ] ADR-029 published (concise + full + indexed)
- [ ] BFF CLAUDE.md Publish Hygiene + AI Boundary sections present

### Business Acceptance

- [ ] Compressed publish ≤60 MB (target) — or lowest stable size + documented gap (SC-03)
- [ ] Uncompressed publish ≤150 MB (SC-04)
- [ ] Zero HIGH CVEs (SC-05)
- [ ] LESSONS-LEARNED.md captures gotchas + future-quarterly-review prompts (SC-21)

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Reflection-loaded code breaks after package removal | Medium | High | Tiered risk + Phase 1 probe + 24–48h bake + App Insights; REJECT trim/AOT |
| R2 | Insights Engine Phase 1 inflates baseline mid-project | Medium | Medium | Phase 0 coordination (UQ-04); baseline outside integration window |
| R3 | Non-Linux RID needed for a missed code path | Low | Medium | Dev-first; healthz catches startup failure |
| R4 | CI guard blocks legitimate future need | Low | Low | `[allow-size-growth]` / `[allow-vuln]` PR-label escapes (FR-C4) |
| R5 | Prod regresses despite full validation | Very low | Very high | Cumulative pre-tested; rollback documented + rehearsed (task 009); operator-only sign-off per NFR-08 revised + ops team for prod |
| R6 | Scope creep (".NET 9 / DI fix while we're at it") | Medium | High | §Out of Scope binding; additions = separate projects |
| R7 | Long bake windows starve project | Medium | Low | Calendar duration set up front (4–6 weeks) |
| R8 | ~~Sole approver unavailable~~ — **RESOLVED 2026-05-24**: operator-only model per NFR-08 revised; AI-directed `adr-check` + `code-review` + CI guards (FR-C1–C6) provide check-and-balance without a second human approver | n/a | n/a | n/a |
| R9 | Vuln patch introduces behavioral change | Medium per package | Medium | Each as own Phase 4 candidate with full bake; no batching |
| R10 | Pre-release package churn (Azure.AI.Projects beta.8, etc.) | Low | High | REJECT tier in §4 Out of Scope; pinning rationale binding |

---

## 9. Next Steps

1. **Operator runs Phase 0 sign-offs** — invoke `/task-execute projects/sdap-bff-api-remediation-fix/tasks/001-*.poml` once tasks are generated
2. **Tasks generated**: ~55 POML files across Phase 0–6 + wrap-up (next step in pipeline)
3. **After Phase 0 gate**: proceed to Phase 1 (read-only inventory; mostly parallel-safe)
4. **Phase 4 onward**: 24–48h bake windows + owner sign-off + AI verification (NFR-08 revised) — NOT autonomous

---

**Status**: Ready for task decomposition
**Next Action**: `/project-pipeline` calls `task-create` to generate POML task files in `tasks/`

---

*For Claude Code: This plan is the source of truth for project execution. The "Discovered Resources" block in §2 supersedes any tag-mapped knowledge file list if there's a conflict — it is the curated, pipeline-verified set.*
