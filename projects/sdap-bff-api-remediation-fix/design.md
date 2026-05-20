# BFF API Remediation & Publish Debt — Design Document

> **Project**: `sdap-bff-api-remediation-fix`
> **Status**: DESIGN (revised 2026-05-20 after extraction assessment) — supersedes `approach.md`. Awaiting owner sign-off on §3 Resolved Decisions, §11 Open Questions, and authorization of Phase 0 kickoff.
> **Created**: 2026-05-20
> **Revised**: 2026-05-20 — extract-readiness Outcome E replaced with Internal AI Hygiene (in-BFF cleanup, no extraction). Decision based on [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) which concluded extraction would break documented latency budgets + transactional coupling.
> **Supersedes**: `approach.md` (2026-05-19) — kept in repo as the upstream record of the original framing
> **Role in pipeline**: This is the **design** layer. Once approved, it becomes the input to `/design-to-spec` → SPEC.md → `/project-pipeline` → tasks. No code is written from `design.md`; tasks come from SPEC.md.
> **Audience**: Project owner, ops/deploy team, AI agents executing tasks, security reviewer
> **Driver**: 2026-05-19 BFF deploy package was 75.19 MB (target ~60 MB per [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md)). Inspection of the 212 MB uncompressed publish tree revealed multi-platform native binaries, duplicate DLLs, sourcemaps in prod, and no CI gate.
> **Why this matters**: The BFF is the single backend for **every** Spaarke client surface (PCFs, Code Pages, External SPA, Office Add-ins, M365 Copilot plugin, Dataverse plugins). It hosts ~120 endpoints, ~99 DI registrations, ~13 background job types, and is on a growth trajectory (Insights Engine Phase 1 integrates INTO this process; the 2026-05-20 assessment confirmed this remains the right architecture). Debt accumulation here has the highest blast radius in the entire system.

---

## 1. Executive Summary

This document refines `approach.md` after a thorough review of existing project documentation, ADRs, constraints, skills, and parallel work streams. The investigation surfaced:

1. **Architectural decisions that bind this project**: ADR-001 (single Minimal API App Service), ADR-007 (SpeFileStore facade), ADR-010 (DI minimalism), ADR-013 (AI extends BFF, no microservice), ADR-027 (subscription isolation), ADR-028 (Spaarke Auth Architecture). The BFF is staying as a single deployable; no extraction is in scope.
2. **An existing constraint document** ([`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md)) that already binds publish location, baseline size (~60 MB / ~240 entries), stdout logging, and forbids publishing to `/tmp`. This project **extends** that constraint, it does not replace it.
3. **An existing failure-mode catalog** ([`.claude/FAILURE-MODES.md`](../../.claude/FAILURE-MODES.md)) with three active deploy-related items (G-2 health-check window, G-3 GitHub Actions version risk, AP-1 confident-but-wrong skills) that this project must honor and partially address.
4. **A canonical Insights Engine architecture** ([`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) §22.2) that explicitly **defers BFF extraction to Phase 3+** with named triggers. Extraction is not happening now — V1 integrates Insights INTO the BFF.
5. **Specific corrections to `approach.md`**: wrong test project path, conflicting `--warnaserror` baseline, missing vuln-remediation track, missing coordination with Insights Engine Phase 1, single approver risk.

The project delivers five outcomes in parallel — not one:

| Outcome | Measurable target |
|---|---|
| **A. Size reduction** | Compressed package ≤ 60 MB (constraint baseline) or, if unattainable without HIGH-risk removals, the lowest stable size + documented gap |
| **B. Security hygiene** | Zero **known** vulnerable transitives in `dotnet list package --vulnerable --include-transitive`; outdated transitives triaged with explicit defer/patch decisions |
| **C. CI guardrails** | Hard CI gate against (a) non-Linux RIDs in publish output, (b) `*.js.map` files, (c) publish size beyond documented ceiling — all configurable with explicit-acknowledgment escape hatches |
| **D. Codified prevention** | New ADR + updated [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) + updated [`.claude/skills/bff-deploy/SKILL.md`](../../.claude/skills/bff-deploy/SKILL.md) + quarterly audit cadence documented |
| **E. Internal AI Hygiene** (in-BFF, no extraction) | Introduce `Services/Ai/PublicContracts/` facade namespace; migrate the 20 inbound CRUD→AI direct dependencies through the facade; move AI-coupled job handlers from `Services/Jobs/Handlers/` into `Services/Ai/Jobs/`; document the boundary in BFF CLAUDE.md per the refined ADR-013 |

Size is the *symptom*. Security + guardrails + codification + internal hygiene are the *value*. Phase 6 is not optional — without it, the debt rebuilds in 12–18 months.

**Outcome E is in scope because** the 2026-05-20 extraction assessment showed the 20 inbound CRUD→AI dependencies are a clean-architecture violation even within one process. Refactoring them through a facade improves the codebase whether we ever split or not — and the refined ADR-013 explicitly requires the facade pattern for new external consumers going forward. Doing this work as part of remediation (when the BFF is already being touched carefully) is cheaper than doing it later.

---

## 2. Strategic Context

### 2.1 Why the BFF deserves disproportionate rigor

| Dimension | Number | Meaning |
|---|---|---|
| Client surfaces depending on it | 6+ (PCFs, Code Pages, External SPA, Outlook Add-in, Word Add-in, M365 Copilot plugin, Dataverse plugins) | A regression here cascades to every client at once |
| Endpoints | ~120 | Wide surface — hard to fully smoke-test by hand |
| DI registrations | ~99 (CLAUDE.md), violates ADR-010's ≤15 target | Pre-existing debt; out of scope here but acknowledged |
| Background services | ~13 job handlers | Long-running state, asynchronous, partial-failure modes |
| Auth flows | 8 distinct (per [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md)) | Cross-cutting reflection-heavy code (MSAL, Graph SDK, Identity.Web) — trim/AOT-hostile |
| Pre-release packages in current csproj | 3 (Azure.AI.Projects beta.8, Microsoft.Agents.AI rc1, Azure.AI.OpenAI 2.8.0-beta.1) | Higher background churn risk; pinning rationale documented inline in csproj |

### 2.2 Resolved: relationship to AI extraction

The sibling file [`CC-PROMPT-bff-extraction-assessment.md`](CC-PROMPT-bff-extraction-assessment.md) was run on 2026-05-20 and produced [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md). **Conclusion: keep AI in the BFF.** The codebase is structurally AI-dominant (69% of `Services/` LOC, 5.2× churn ratio, 100% of streaming endpoints are AI), but the operational characteristics make extraction the wrong move:

- Capability routing has <50ms targets; RAG has <100ms; streaming TTFB <500ms — a service hop violates all three
- Safety perimeter retroactively annotates streaming responses inside the same request lifecycle
- Cosmos session writes are transactional with the chat response
- 20 inbound CRUD→AI dependencies would require ~3–4 weeks of refactoring before extraction is safe
- Author overlap is HIGH (100%) — no team-shape benefit from splitting

**Outcome E of this project does the refactoring that's right whether we ever extract or not** — clean up the 20 inbound CRUD→AI deps through facades. That work has independent value (better architecture inside one process) AND, if a future trigger ever fires, makes extraction cheap. It's not extraction-prep; it's clean-architecture-prep.

**ADR-013 refined (also 2026-05-20)**: the categorical "no separate AI microservice" rule is replaced with four technical exception criteria (latency coupling, transactional coupling, integration surface bounded, no component duplication). Insights Engine's existing plan (Functions for sync, possibly a future MCP server) is unchanged.

### 2.3 Coordination matrix with other active work

| Stream | Status | Coordination risk | Plan |
|---|---|---|---|
| `sdap-bff-api-and-performance-enhancement-r1` | Active (~124–170h scope: Redis, Dataverse perf, resilience, AI pipeline perf) | LOW — orthogonal to size/publish, but both touch BFF | Confirm with owner that no overlapping changes are mid-deploy at Phase 4 kickoff |
| `ai-spaarke-insights-engine-r1` (Phase 1) | Pre-implementation (design phase) | MEDIUM — adds ~5 services + new index code to the BFF, which inflates baseline | Capture Phase 3 baseline BEFORE Engine integration starts, or after — but NOT in the middle |
| Auth v2 hardening / [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md) | Active (multi-consumer propagation) | LOW — consumer-side, not server publish | None |
| FAILURE-MODES G-2 (workflow health-check window) | Script fixed, GitHub Actions workflow not yet | LOW — this project can fix it as part of Phase 6 | Include workflow alignment in Phase 6 |
| FAILURE-MODES G-3 (GitHub Actions versions) | Open across 5 workflows including `deploy-bff-api.yml` | LOW | Fix in same Phase 6 PR if `deploy-bff-api.yml` is touched |

### 2.4 Authoritative constraint sources (binding for this project)

Tasks MUST cite these explicitly when proposing any change:

| Source | Binds |
|---|---|
| [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) | Publish location (`deploy/api-publish/`, NOT `/tmp`); zip ~240 entries / ~60 MB baseline; `stdoutLogEnabled="true"`; `appsettings.template.json` exclusion |
| [`src/server/api/Sprk.Bff.Api/CLAUDE.md`](../../src/server/api/Sprk.Bff.Api/CLAUDE.md) | All Kiota packages MUST be same version (currently `1.21.2`); Graph SDK matching version (`5.101.0`) |
| ADR-001 | Single Minimal API App Service; no Functions-hosted BFF endpoints |
| ADR-007 | SpeFileStore facade; no direct `GraphServiceClient` injection |
| ADR-010 | ≤15 non-framework DI registrations (KNOWN DEBT — not in scope here, but no change in this project may worsen the count) |
| ADR-013 | AI extends BFF; no AI microservice |
| ADR-027 | Managed solutions for production; subscription isolation |
| ADR-028 | Spaarke Auth Architecture (function-based contract, managed identity outbound) |
| [`.claude/FAILURE-MODES.md`](../../.claude/FAILURE-MODES.md) G-2, G-3, AP-1 | Existing deploy gotchas this project must not regress against, and where possible should help close |
| [`docs/standards/ANTI-PATTERNS.md`](../../docs/standards/ANTI-PATTERNS.md) #16 | Publishing BFF from `/tmp` produces incomplete packages (~22 MB vs ~61 MB) |

---

## 3. Resolved Decisions (vs approach.md open questions)

These are this design's answers to the open questions in `approach.md §9`. Owner can override any of them before kickoff, but they ARE the project's defaults.

| Question | Resolution |
|---|---|
| Production environment access + canonical prod deploy process | **Define before Phase 5 starts, NOT mid-project.** Add as Phase 0 explicit checklist item. If no canonical prod process exists at Phase 0 close, this project scopes to **dev + demo only**; prod promotion becomes a follow-up project. |
| App Insights baseline window length | **48 hours.** Longer window beats faster start; the project's calendar is bake-window-dominated anyway. |
| Tolerance for size regression on legitimate adds (CI guard) | **Hard fail by default**, with `[allow-size-growth]` PR-label escape hatch + size-delta + justification required in PR body. PRs that legitimately add functionality (e.g., Insights Engine integration) use the label with explicit acknowledgment. |
| Quarterly review cadence ownership | **Project owner**, until/unless an Operations lead exists. Add reminder in [`.claude/skills/bff-deploy/SKILL.md`](../../.claude/skills/bff-deploy/SKILL.md) as a "next review date" stamp. |
| Scope of "BFF" — just `Sprk.Bff.Api.csproj`, or also `Spaarke.Core` + `Spaarke.Dataverse`? | **Sprk.Bff.Api.csproj first.** Inventory the referenced projects (their direct + transitive package lists) but only edit them in this project if a candidate removal in Sprk.Bff.Api requires it. Wider audit of shared libs is a follow-up project (scope creep risk). |
| (NEW) Test project location | **`tests/unit/Sprk.Bff.Api.Tests/`** — confirmed via Glob. `approach.md` cites the wrong path (`src/server/api/Sprk.Bff.Api.Tests/`). All tasks must use the correct path. |
| (NEW) `dotnet build --warnaserror` as Phase 4 step | **DO NOT use.** csproj sets `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>`. Instead: Phase 3 baselines the warning count; Phase 4 verifies no NEW warnings are introduced. Tightening warnings-as-errors is a separate project. |
| (NEW) Vulnerability remediation scope | **In scope as Outcome B (parallel to size).** Phase 1 produces the vuln list; Phase 4 patches them on their own risk profile (a transitive bump can break behavior — same rigor as other MEDIUM-tier changes). |
| (NEW) Sole-approver risk | **Dual approval required** for MEDIUM/HIGH-tier candidates AND prod promotion. SAFE-tier candidates remain owner-only. |
| (RESOLVED 2026-05-20) Extraction-assessment relationship | Already run. See [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md). Conclusion: keep in BFF; this project gains Outcome E (Internal AI Hygiene) instead of extract-prep. ADR-013 refined accordingly. |

---

## 4. Out of Scope (binding)

To prevent scope creep on the heart of the system:

- ❌ Refactoring BFF business logic (auth, endpoints, services)
- ❌ New features (any)
- ❌ `PublishTrimmed=true` — Graph SDK + Microsoft.Identity.Web + EF + JSON serializers + DI rely on reflection that trimming breaks silently
- ❌ `PublishAot=true` — same reasoning, more aggressive
- ❌ Upgrading .NET SDK / target framework (8.0 → 9.0) — separate concern, separate risk profile
- ❌ Graph SDK or Kiota version changes (Kiota chain footgun — CLAUDE.md mandates all Kiota packages stay version-matched)
- ❌ Pre-release package version changes (Azure.AI.Projects, Microsoft.Agents.AI, Azure.AI.OpenAI betas) — pinning is documented inline in csproj for chain-compat reasons
- ❌ Fixing the ADR-010 DI count violation (99 vs target 15) — large architectural debt, separate project
- ❌ Infrastructure changes (App Service Plan SKU, region, runtime stack)
- ❌ Extraction of AI subsystem to `Sprk.Insights.Api` — explicitly Phase 3+ per Insights Engine architecture
- ❌ Wholesale audit of `Spaarke.Core` and `Spaarke.Dataverse` publish outputs (inventory only; edits only if Sprk.Bff.Api candidate forces it)
- ❌ Adding `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — separate project

If any of these is later identified as necessary mid-project, it becomes a separate project with its own design and risk review.

---

## 5. Guiding Principles

These constrain every decision. If a step violates one, escalate before proceeding.

| Principle | Why |
|---|---|
| **Evidence before edit** | No removal without (a) verified non-use including reflection probe (b) test plan (c) rollback path |
| **One change per deploy** | Multi-change deploys make regression attribution impossible; bake windows require isolation |
| **Dev → demo → prod with baking** | 24h dev, 48h demo, 7-day prod observation. Never promote unbaked changes. |
| **Reflection-safe (no trim, no AOT)** | The BFF's Graph SDK / DI / serialization / identity stack is fundamentally reflection-driven |
| **Reversible via git + redeploy** | Every change revertable by `git revert` + `Deploy-BffApi.ps1`. Note: rollback EXECUTES in ~10 min; rollback DECISION may take the full bake window if regression is gradual |
| **Codify or it returns** | Phase 6 is mandatory — without CI guards + ADR + skill update, the debt rebuilds within a year |
| **Don't touch prod-only state from non-prod work** | All audits, baselines, observations against `spe-api-dev-67e2xz` only unless explicitly cleared for `spaarke-demo` or prod |
| **Sub-agent write boundary respected** | Per root [`CLAUDE.md`](../../CLAUDE.md) §3, sub-agents cannot write to `.claude/` paths; main session applies those edits |
| **Coordinate with parallel BFF work** | Confirm no in-flight deploy from sibling projects before each Phase 4 / Phase 5 deploy |
| **Dual approval for MEDIUM+, dual approval for prod** | Single-point-of-failure on heart-of-system changes is not acceptable |

---

## 6. Seven-Phase Execution Model

Phases are a strict pipeline — each one's evidence is the next one's input. Within a phase, individual audit commands can run in parallel.

### Phase 0 — Pre-flight resolution (NEW vs approach.md)

**Goal**: Resolve uncertainty BEFORE work starts. Concrete checklist (no work proceeds until every box is checked):

1. ☐ Owner sign-off on this design.md
2. ☐ Owner sign-off on §3 Resolved Decisions
3. ☐ Verify no in-flight deploy from `sdap-bff-api-and-performance-enhancement-r1`
4. ☐ Coordinate baseline-capture window with `ai-spaarke-insights-engine-r1` owner (capture BEFORE Engine integration starts, or after stable — never mid-integration)
5. ☐ Decide: is Phase 5 (prod promotion) in this project, OR is this project dev+demo only? Depends on prod deploy process existing.
6. ☐ Identify dual-approver for MEDIUM/HIGH candidates (likely Auth team lead given BFF auth scope)
7. ☐ ~~Run extraction-assessment CC-PROMPT~~ **DONE 2026-05-20** — see [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md). Conclusion: keep in BFF, add Outcome E. No further action.
8. ☐ Verify the test project path matches reality: `tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj`
9. ☐ Confirm Outcome E scope with owner: facade introduction (~10 file edits) + AI-coupled job handler relocation (~6 file moves) + BFF CLAUDE.md documentation update. Estimated 3–5 days; runs in parallel with Phase 4 SAFE candidates.

**Risk**: None. All planning.

**Duration**: 1–2 days (mostly waiting on owner / dual-approver designation).

**Gate**: All 9 boxes checked (item 7 is pre-resolved). Phase 1 cannot start otherwise.

---

### Phase 1 — Inventory (READ-ONLY, no risk)

**Goal**: Produce `INVENTORY.md` — the authoritative snapshot of the BFF's runtime composition at the start of the project.

**Outputs** (in `projects/sdap-bff-api-remediation-fix/inventory/`):

1. **Direct package list**: `dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package`
2. **Transitive package list**: `... package --include-transitive`
3. **Vulnerable packages**: `... package --vulnerable --include-transitive` ← feeds Outcome B
4. **Outdated packages**: `... package --outdated`
5. **Pre-release tracker**: enumerate all `-beta`, `-rc`, `-preview` packages with inline pinning rationale from csproj comments
6. **Project reference graph**: recursive `dotnet list reference` across Sprk.Bff.Api → Spaarke.Core → Spaarke.Dataverse
7. **Direct-package usage map (static)**: for each direct package, grep for `using {PackageNamespace}` and `[assembly:]`
8. **Reflection-load probe (dynamic)** — NEW vs approach.md: instrument startup (`Program.cs` localhost-only diagnostic flag) to log `AppDomain.CurrentDomain.GetAssemblies()` after `app.Build()`. Compare to package list. Catches packages used via DI string types, config binding, EF migrations, MEL telemetry processors — things grep misses. Any divergence between static and dynamic results blocks the candidate from removal until reconciled.
9. **Native binary inventory**: enumerate `publish/runtimes/*/native/` after clean `dotnet publish`; per-platform file lists with sizes
10. **wwwroot asset inventory**: enumerate `publish/wwwroot/`; flag `.map`, README, LICENSE, source-only files
11. **Published file size table by category**: (managed dll, native lib, wwwroot, config, runtime metadata) — sum per category
12. **App Service runtime confirmation**: `az webapp show ... --query "{kind, os, linuxFxVersion, sku}"` — proves the RID we need
13. **Current deploy SHAs**: SHA-256 of all DLLs in `deploy/api-publish/` (rollback target)
14. **Current package metrics**: zip size + uncompressed size + file count vs constraint baseline (~60 MB, ~240 entries)
15. **Test project location confirmation**: actual path of `Sprk.Bff.Api.Tests.csproj`, current test counts, current pass/fail
16. **CI workflow inventory**: enumerate steps in `.github/workflows/sdap-ci.yml` AND `.github/workflows/deploy-bff-api.yml`; identify where new CI guards will plug in
17. **GitHub Actions version sanity** (G-3): list every `uses:` line in `deploy-bff-api.yml`; flag any that may not be a valid registry version

**Risk**: None. All commands are read-only.

**Duration**: 2–3 hours.

**Gate**: `INVENTORY.md` committed and reviewed by project owner. Phase 2 cannot start otherwise.

---

### Phase 2 — Categorize cleanup candidates by risk

**Goal**: Produce `CANDIDATES.md` — every potential change, risk-tiered with evidence, test plan, and rollback.

**Risk tier definitions** (binding):

| Tier | Definition | Approval |
|---|---|---|
| **SAFE** | Affects deploy artifact only (publish-time configuration). No source code change. No runtime behavior change. Examples: `--runtime linux-x64`, exclude `*.js.map` from zip, exclude `runtimes/win-x64/` from copy. | Owner ack |
| **MEDIUM** | csproj edit OR transitive-vulnerability patch. Compile-time verifiable. Reflection use must be ruled out via the Phase 1 dynamic probe. Examples: removing a direct package whose usage greps + probe agree is gone; bumping a vulnerable transitive to a non-vulnerable version on the same major. | Owner + dual approver |
| **HIGH** | Removes a transitive-only dep by removing its parent direct ref. May break reflection-loaded code. Patches a vulnerable transitive across a major version. Requires full integration test pass + 48h dev observation. | Owner + dual approver + explicit per-item go-ahead |
| **REJECT** | `PublishTrimmed=true`, AOT, Graph SDK removal, Microsoft.Identity.Web removal, Kiota single-package bumps, pre-release package version changes | Not in scope |

**For each candidate, capture**:

- Name + file/package + tier
- Current cost (KB / MB compressed and uncompressed) OR security severity (CVE ID, CVSS)
- Evidence of non-use OR vulnerability evidence
- Static-grep result + dynamic-probe result (must agree for MEDIUM/HIGH)
- Removal/patch action (csproj edit / publish flag / wwwroot exclusion / version bump)
- Test plan
- Rollback (git commit hash + redeploy command)

**Risk**: None. Read + plan only.

**Duration**: 6–10 hours (depending on candidate count).

**Gate**: Owner reviews `CANDIDATES.md`; SAFE/MEDIUM/HIGH ordering approved.

---

### Phase 3 — Baseline current behavior (BEFORE any change)

**Goal**: Capture verified-good behavior so Phase 4 has a reference for "did anything regress."

**Outputs** (in `projects/sdap-bff-api-remediation-fix/baseline/`):

1. **Test suite results**: `dotnet test tests/unit/Sprk.Bff.Api.Tests/` → file with pass/fail counts + duration
2. **Build warning count** — NEW vs approach.md: capture `dotnet build` warning count as a baseline. Phase 4 verifies no new warnings.
3. **Endpoint smoke test results**: every documented endpoint from [`auth-deployment-setup.md`](../../docs/guides/auth-deployment-setup.md) §9 + expanded route table — HTTP status + response shape per endpoint
4. **App Insights baseline metrics (48h window)**: request count, error rate, P50/P95/P99 latency, exception types, dependency call latency (Graph, Dataverse, Service Bus, Cosmos, Redis)
5. **Deployed file SHA-256s**: 6 critical files via Kudu VFS (same mechanism the deploy script's hash-verify uses)
6. **Current publish + zip metrics**: size + entry count + DLL count
7. **Reflection-load probe baseline** — NEW: capture the dynamic assembly load list so Phase 4 changes can confirm "the same assemblies still load"
8. **Extraction-assessment validation report**: output of the [`CC-PROMPT-bff-extraction-assessment.md`](CC-PROMPT-bff-extraction-assessment.md) run from Phase 0, archived here

**Risk**: None. Observation only.

**Duration**: ~2h active + 48h calendar.

**Gate**: All artifacts committed to repo before any Phase 4 change is attempted.

---

### Phase 4 — Apply changes (one at a time, staged)

**Goal**: Execute approved cleanups + security patches from `CANDIDATES.md`, one per deploy, with observation between each.

**Per-candidate procedure** (mandatory):

1. Single git commit on a feature branch with ONLY this change
2. `dotnet build` — must compile clean. Warning count must NOT exceed Phase 3 baseline.
3. `dotnet test` — same pass/fail counts as Phase 3 (any new failure = abort + revert)
4. Inspect publish output without deploying — verify size delta in expected range; verify reflection-load probe matches baseline (or differences are accounted for)
5. `Deploy-BffApi.ps1 -Environment dev` to `spe-api-dev-67e2xz` ONLY
6. Re-run Phase 3 endpoint smoke test pass — every check matches Phase 3
7. **Bake 24–48h**. Watch App Insights for: no new exception types, error rate unchanged, P95 latency within 10% of baseline, dependency success rates unchanged
8. If stable → log size delta + vuln status to `EXECUTION-LOG.md`, mark candidate complete, move on
9. If any anomaly → `git revert` + redeploy + investigate before continuing

**Recommended execution order** (SAFE first; security in parallel where tier permits; Outcome E runs as a separate parallel track):

| Order | Candidate | Tier | Expected savings / security impact |
|---|---|---|---|
| 1 | Publish with `--runtime linux-x64` (framework-dependent) | SAFE | ~25–30 MB uncompressed, ~10 MB compressed |
| 2 | Exclude `wwwroot/**/*.js.map` from publish | SAFE | ~7 MB uncompressed |
| 3 | Remove duplicate Cosmos `ServiceInterop.dll` (verify still present after step 1) | SAFE | ~10 MB uncompressed (if not already removed by RID trim) |
| 4 | Patch vulnerable transitives flagged in Phase 1 vuln scan | MEDIUM (per-package) | Each on its own risk profile; CVE-driven |
| 5+ | Candidates from CANDIDATES.md (MEDIUM/HIGH) | per tier | TBD |
| E1 | Introduce `Services/Ai/PublicContracts/` facade interface + initial implementation | SAFE (no behavior change; adds new types) | Internal hygiene; no size impact |
| E2 | Migrate 20 inbound CRUD→AI dependencies to facade (one consumer per commit, all-or-nothing per consumer) | MEDIUM (interface signature changes; tests must pass) | Internal hygiene; no size impact |
| E3 | Move 6 AI-coupled job handlers from `Services/Jobs/Handlers/` to `Services/Ai/Jobs/` | SAFE (file moves; JobType strings unchanged) | Internal hygiene; no size impact |

Outcome E (rows E1–E3) does not depend on rows 1–5 and runs in parallel. Bake windows still apply per change — but Outcome E changes are zero-runtime-behavior-change so the bake is primarily verification, not observation.

**Risk**: LOW per change (dev only, 24–48h bake, immediate rollback).

**Duration**: ~1 week per 3–5 SAFE changes; security patches add separately.

**Gate**: Each candidate's results in `EXECUTION-LOG.md` before next candidate starts.

---

### Phase 5 — Promote to spaarke-demo, then prod (CONDITIONAL on Phase 0 outcome)

**Goal**: Replicate validated changes to demo and (if in scope) production.

**Procedure**:

1. After all approved Phase 4 changes stable in dev, deploy cumulative changeset to `spaarke-demo`
2. Run smoke tests against demo
3. Bake **48h minimum**
4. If stable → schedule prod deploy with owner + ops team
5. Prod deploy follows the canonical process documented in Phase 0
6. Post-prod-deploy: monitor 7 days; record any drift

**If Phase 0 determined NO canonical prod process exists**: this project ends after demo. Prod promotion becomes a follow-up project owned by ops, using this project's tested changeset.

**Risk**: MEDIUM. Mitigations: full Phase 4 validation in dev, cumulative pre-tested in demo, deploy script hash-verify, `git revert + redeploy` rollback. Note: rollback EXECUTES in ~10 min; rollback DECISION may take the bake window if a regression is gradual.

**Duration**: 1–2 weeks calendar.

**Gate**: Owner + ops approval at each transition (dev → demo, demo → prod).

---

### Phase 6 — Prevention (codify so debt doesn't return)

**Goal**: Make debt-return loud and obvious.

**Outputs**:

1. **Deploy-script size guard**: `Deploy-BffApi.ps1` fails (not just warns) if publish zip exceeds documented ceiling (e.g., baseline + 10%) unless `-AllowOversize` is passed. Today the script only warns above 100 MB — too generous.
2. **CI check (sdap-ci.yml)**: workflow step that runs `dotnet publish --runtime linux-x64` and fails if non-Linux native runtimes are present in output
3. **CI check (sdap-ci.yml)**: step that fails if any `*.js.map` files exist in `publish/wwwroot/`
4. **CI check (sdap-ci.yml)**: step that fails on `dotnet list package --vulnerable --include-transitive` finding HIGH-severity CVE in Sprk.Bff.Api
5. **CI escape hatch**: `[allow-size-growth]` and `[allow-vuln]` PR labels with required PR-body justification
6. **GitHub Actions workflow alignment**: bring `deploy-bff-api.yml` in line with Deploy-BffApi.ps1 health-check window (G-2); fix `actions/*@vN` versions if any are invalid (G-3) — bundled into the same Phase 6 PR
7. **ADR-029** (new): "BFF publish hygiene — framework-dependent `linux-x64` runtime only; sourcemaps excluded; vulnerable-transitive scan in CI; quarterly review cadence." Records decision, rationale, review cadence. ADR-029 binds **publish-side** policy only; **extraction/service-boundary** policy stays with the refined ADR-013.
8. **[`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) update**: add Publish Hygiene subsection codifying RID + sourcemap + CI gate
9. **[`src/server/api/Sprk.Bff.Api/CLAUDE.md`](../../src/server/api/Sprk.Bff.Api/CLAUDE.md) update**: add (a) Publish Hygiene section pointing to ADR-029 + size guard; (b) AI Boundary section pointing to refined ADR-013 + `Services/Ai/PublicContracts/` facade pattern; (c) reference to the BFF additions governance constraint (`.claude/constraints/bff-extensions.md`)
10. **[`.claude/skills/bff-deploy/SKILL.md`](../../.claude/skills/bff-deploy/SKILL.md) update**: add Publish Hygiene + next-review-date stamp + reference to ADR-029
11. **[`.claude/FAILURE-MODES.md`](../../.claude/FAILURE-MODES.md) update**: new entry documenting (a) the bloat root cause + fix, (b) the "many-projects-each-adding-without-considering-overall-quality" pattern as a process failure, so future contributors don't relearn this
12. **[`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md)** (NEW): governance constraint for adding new code/features/components to the BFF — pre-merge checklist, decision criteria, scope thresholds. Loaded automatically when work touches `src/server/api/Sprk.Bff.Api/`.
13. **Root [`CLAUDE.md`](../../CLAUDE.md) imperative**: short binding section pointing to the new constraint — makes it discoverable in every session, not just BFF-touching ones
14. **Refined ADR-013 already published 2026-05-20** (parallel to this design revision) — listed here for completeness; no further work in Phase 6
15. **Quarterly audit reminder**: add to project owner's calendar — "BFF publish-debt audit + Outcome E facade compliance check" each quarter (re-run Phase 1 inventory subset; verify no new direct CRUD→AI dependencies have crept in)

**Risk**: LOW. CI changes affect future PRs; size guard is opt-in opt-out per deploy.

**Duration**: 2–3 days.

**Gate**: All CI checks pass on a fresh PR. ADR-029 published. Skill, constraint, CLAUDE.md updated. Owner + dual-approver sign-off.

---

## 7. Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Reflection-loaded code breaks after package removal | Medium (Graph SDK / DI / MSAL is reflection-heavy) | High | Tiered risk + Phase 1 dynamic probe + 24–48h dev bake + App Insights monitoring; REJECT trimming/AOT |
| Insights Engine Phase 1 integration adds size during this project | Medium | Medium (baseline drift) | Phase 0 coordination with Engine project owner; capture baseline OUTSIDE integration window |
| Non-Linux RID needed for a path missed in inventory | Low (App Service is unambiguously Linux) | Medium | RID trim deploys to dev first; deploy script's `/healthz` check catches startup failure |
| "Duplicate" file was loaded from one specific path | Low | Medium | Per-candidate test plan verifies the specific code paths |
| CI guard blocks a future legitimate non-Linux RID need | Low | Low | Guard supports `-AllowOversize` flag + `[allow-size-growth]` label with documented justification |
| Production deploy regresses despite full dev + demo validation | Very low | Very high | Cumulative pre-tested changeset; rollback documented; ops on standby for first prod deploy |
| Scope creep ("while we're at it, upgrade .NET 9 / fix the 99→15 DI debt") | Medium | High | §4 Out of Scope is binding; any addition = separate project |
| Project starves due to long bake windows | Medium | Low | §8 estimate documents 3–5 weeks calendar; expectation set up front |
| Sole approver unavailable during MEDIUM/HIGH approval window | Medium | Medium | Phase 0 names dual approver; approval can come from either |
| Vulnerability bump (e.g., transitive .x.x) introduces behavioral change | Medium (per package) | Medium | Treat each vuln patch as its own Phase 4 candidate with full bake; do not batch multiple vuln bumps |
| Pre-release package surprises (Azure.AI.Projects beta.8, etc.) regress mid-project | Low | High | Pre-release packages are explicitly REJECT tier (§4); pinning rationale in csproj is binding |

---

## 8. Estimated Effort

| Phase | Active work | Calendar time |
|---|---|---|
| 0 — Pre-flight resolution | 2–4h (mostly owner / dual-approver coord) | 1–2 days |
| 1 — Inventory | 2–3h | 1 day |
| 2 — Categorize | 6–10h | 1–2 days |
| 3 — Baseline | 2h active + 48h wait | 2–3 days |
| 4 — Apply changes | 1–2h per change × ~6–10 changes | 2–3 weeks (bake windows dominate) |
| 5 — Promote demo + prod (if in scope) | 2–4h active + bake windows | 1–2 weeks |
| 6 — Prevention | 2–3 days | 3–5 days |
| **Total** | **~4–6 days active work** | **4–6 weeks calendar** |

Calendar dominated by bake windows. Intentional — cost of high-rigor change to the heart of the system.

---

## 9. Success Criteria

The project is complete when ALL of the following are true (not just the size delta):

### Outcome A — Size
1. ✅ `INVENTORY.md` and `CANDIDATES.md` committed and approved
2. ✅ All approved SAFE candidates deployed and stable in dev for 24–48h each
3. ✅ Compressed deploy package: ≤ 60 MB (constraint baseline) — OR — lowest stable size + documented justification for the gap
4. ✅ Uncompressed publish: ≤ 150 MB (down from 212 MB)

### Outcome B — Security
5. ✅ Zero HIGH-severity CVEs in `dotnet list package --vulnerable --include-transitive`
6. ✅ Outdated transitives triaged: each has documented patch/defer decision
7. ✅ Pre-release package pinning rationale re-verified and still valid

### Outcome C — Operational
8. ✅ Zero new exception types in App Insights vs Phase 3 baseline
9. ✅ Error rates within 10% of baseline across all endpoints
10. ✅ P95 latency within 10% of baseline per endpoint

### Outcome D — Codification
11. ✅ Deploy-script size guard hard-fails by default (was warn-only)
12. ✅ CI guard: non-Linux RID detection in publish output
13. ✅ CI guard: `*.js.map` exclusion enforcement
14. ✅ CI guard: vulnerable-transitive HIGH-severity fail
15. ✅ `deploy-bff-api.yml` aligned with G-2 health-check window (120s)
16. ✅ G-3 action versions resolved in `deploy-bff-api.yml`
17. ✅ ADR-029 published (publish hygiene scope; does NOT bind extraction policy — that's ADR-013)
18. ✅ [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) updated with Publish Hygiene
19. ✅ [`.claude/skills/bff-deploy/SKILL.md`](../../.claude/skills/bff-deploy/SKILL.md) updated
20. ✅ [`.claude/FAILURE-MODES.md`](../../.claude/FAILURE-MODES.md) updated with this incident's root cause
21. ✅ `LESSONS-LEARNED.md` committed for next quarterly review

### Outcome E — Internal AI Hygiene (no extraction)
22. ✅ `Services/Ai/PublicContracts/IBffAiPublicContracts.cs` (or equivalent facade interface) created with explicit methods for each external consumer's needs (`AnalyzeForBriefing`, `ExtractInvoiceFields`, etc.)
23. ✅ All 20 inbound CRUD→AI direct dependencies migrated to consume the facade (Finance: 3, Workspace: 4, Jobs: 6, Dataverse: 2, Filters/Endpoints: 5+)
24. ✅ AI-coupled job handlers moved from `Services/Jobs/Handlers/` to `Services/Ai/Jobs/` (6 file moves; JobType registration unchanged — dispatch is by string)
25. ✅ All tests pass; no behavioral change (build verifies the interface migration; tests verify nothing regressed)
26. ✅ `src/server/api/Sprk.Bff.Api/CLAUDE.md` documents the facade pattern + the prohibition on new direct CRUD→AI dependencies (per refined ADR-013)
27. ✅ Refined ADR-013 referenced from `src/server/api/Sprk.Bff.Api/CLAUDE.md` and from the BFF additions governance constraint (see [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md))

---

## 10. Decision Authority

| Decision | Authority |
|---|---|
| Approve this design.md | Project owner |
| Approve `INVENTORY.md` (Phase 1 output) | Project owner |
| Approve Phase 0 dual-approver designation | Project owner |
| Approve cleanup candidates from `CANDIDATES.md` | Project owner |
| Per-candidate go-ahead SAFE tier (Phase 4) | Project owner |
| Per-candidate go-ahead MEDIUM tier (Phase 4) | Project owner + dual approver |
| Per-candidate go-ahead HIGH tier (Phase 4) | Project owner + dual approver + explicit per-item ack |
| Promotion dev → demo | Project owner + dual approver |
| Promotion demo → prod | Project owner + dual approver + ops team |
| ADR-029 approval | ADR review process (per project conventions) |
| Rollback after any regression | Anyone — no permission needed to revert |

---

## 11. Open Questions (narrow, specific, resolvable before kickoff)

These are the questions that MUST be answered in Phase 0. They are deliberately narrower than `approach.md §9` so they can be closed.

1. **Who is the dual approver?** (Auth team lead given BFF's auth scope is the default candidate; owner confirms.)
2. **Does a canonical prod deploy process exist today?** If yes, point to the doc. If no, scope this project to dev+demo and defer prod.
3. **Is `sdap-bff-api-and-performance-enhancement-r1` deploying in the next 6 weeks?** If yes, coordinate baseline + Phase 4 windows; if no, proceed.
4. **When does Insights Engine Phase 1 integration land in `Sprk.Bff.Api`?** Capture baseline either fully BEFORE or fully AFTER, never mid.
5. **What's the size ceiling for the CI guard?** Baseline + 10% is a default; owner can tighten to baseline + 5% if confident.
6. **Should `Spaarke.Core` and `Spaarke.Dataverse` package lists be inventoried-only, or also pruned in this project?** Default: inventory only.
7. **Outcome E facade design**: should the facade be one large interface (`IBffAiPublicContracts` with ~15 methods) or several small interfaces grouped by consumer concern (`IBriefingAi`, `IInvoiceAi`, `IRecordMatchingAi`)? Default: small focused interfaces — easier to test, easier to deprecate, lower coupling. Owner confirms.

---

## 12. Document Role + Next Step

| Doc | Role | Created by |
|---|---|---|
| `approach.md` (2026-05-19) | Original framing; superseded by this document, retained as upstream record | Project owner |
| `design.md` (this doc) | Decisions + constraints + integration. Input to SPEC.md. | This document |
| `CC-PROMPT-bff-extraction-assessment.md` | Phase 0 validation prompt; output archived in `baseline/` during Phase 3 | Project owner |
| `SPEC.md` (NEXT) | Machine-readable spec generated from design.md via `/design-to-spec` | TBD |
| `tasks/TASK-INDEX.md` + `tasks/*.poml` | Generated from SPEC.md via `/project-pipeline` | TBD |

**Next step after owner sign-off**:
1. Run `/design-to-spec projects/sdap-bff-api-remediation-fix/design.md` to produce SPEC.md
2. Owner reviews SPEC.md
3. Run `/project-pipeline projects/sdap-bff-api-remediation-fix/` to scaffold tasks aligned to Phases 0–6
4. Begin Phase 0

---

*Design awaiting owner sign-off. No work begins until §3 Resolved Decisions are confirmed, §11 Open Questions are answered, and Phase 0 is explicitly authorized.*
