# BFF API Remediation & Publish Debt — AI Implementation Specification

> **Status**: Ready for Implementation (pending owner sign-off on §3 of [`design.md`](design.md) + §11 Open Questions)
> **Created**: 2026-05-20
> **Source**: [`design.md`](design.md) (594 lines, authored 2026-05-20 from `approach.md` + extraction assessment + multi-agent investigation)
> **Sibling artifacts**: [`design.md`](design.md), [`CC-PROMPT-bff-extraction-assessment.md`](CC-PROMPT-bff-extraction-assessment.md), [`approach.md`](approach.md) (upstream record)
> **Evidence base**: [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md)

---

## Executive Summary

The `Sprk.Bff.Api` deploy package grew from ~65 MB to 75.19 MB (compressed) and 212 MB (uncompressed) by 2026-05-19. Root causes include multi-platform native binaries shipped to a Linux App Service, sourcemaps in production output, duplicate Cosmos `ServiceInterop.dll`, no CI guard against drift, and 20 inbound CRUD→AI direct dependencies that violate clean-architecture boundaries. This project executes five parallel outcomes (size reduction, security hygiene, CI guardrails, codified prevention, internal AI hygiene) in a strict 7-phase pipeline (Phase 0–6) with dev → demo → prod promotion gated by 24–48h observation windows. Extraction of AI to a separate service is explicitly out of scope per the 2026-05-20 assessment and refined ADR-013; in-BFF facade introduction is in scope.

## Scope

### In Scope

- **Outcome A (Size)**: framework-dependent `linux-x64` publish; exclude `wwwroot/**/*.js.map`; eliminate duplicate Cosmos native DLLs; target ≤60 MB compressed (per [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) baseline)
- **Outcome B (Security)**: zero HIGH-severity CVEs in transitive graph; triage every outdated transitive; re-verify pre-release pinning rationale
- **Outcome C (CI guardrails)**: hard CI gates against non-Linux RIDs, sourcemaps, vulnerable transitives, oversize publish; PR-label escape hatches
- **Outcome D (Codification)**: ADR-029 (publish hygiene), updates to [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md), [`.claude/skills/bff-deploy/SKILL.md`](../../.claude/skills/bff-deploy/SKILL.md), [`.claude/FAILURE-MODES.md`](../../.claude/FAILURE-MODES.md), `src/server/api/Sprk.Bff.Api/CLAUDE.md`, GitHub workflow alignment (G-2/G-3)
- **Outcome E (Internal AI Hygiene)**: introduce `Services/Ai/PublicContracts/` facade; migrate 20 inbound CRUD→AI direct dependencies through facade; relocate 6 AI-coupled job handlers to `Services/Ai/Jobs/`; document the boundary

### Out of Scope (binding)

- Refactoring BFF business logic (auth, endpoints, services beyond Outcome E facade migration)
- New features of any kind
- `<PublishTrimmed>true</PublishTrimmed>` or `<PublishAot>true</PublishAot>` (reflection-hostile)
- .NET SDK / target framework upgrade (8.0 → 9.0)
- Graph SDK / Kiota version changes (chain-locked per [`src/server/api/Sprk.Bff.Api/CLAUDE.md`](../../src/server/api/Sprk.Bff.Api/CLAUDE.md))
- Pre-release package version changes (Azure.AI.Projects, Microsoft.Agents.AI, Azure.AI.OpenAI betas)
- Fixing the ADR-010 DI minimalism violation (99+ vs ≤15) — separate project
- Infrastructure changes (App Service Plan SKU, region, runtime stack)
- Extraction of AI subsystem to separate service (governed by refined ADR-013; explicitly deferred per 2026-05-20 assessment)
- Wholesale audit of `Spaarke.Core` / `Spaarke.Dataverse` publish outputs (inventory only)
- Adding `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — separate project

### Affected Areas

| Path | Change Type |
|---|---|
| [`src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj`](../../src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj) | RID property, possible package removals/patches |
| [`src/server/api/Sprk.Bff.Api/Services/Ai/`](../../src/server/api/Sprk.Bff.Api/Services/Ai/) | New `PublicContracts/` subfolder; new `Jobs/` subfolder receiving 6 relocated handlers |
| [`src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/`](../../src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/) | Remove 6 AI-coupled handlers (moved to `Services/Ai/Jobs/`) |
| `src/server/api/Sprk.Bff.Api/Services/Finance/` | Migrate 3 files to consume facade instead of `IOpenAiClient`/`IPlaybookService` directly |
| `src/server/api/Sprk.Bff.Api/Services/Workspace/` | Migrate 4 files to consume facade |
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/` | Migrate 2 files |
| `src/server/api/Sprk.Bff.Api/Api/Filters/` | Migrate 5+ filters/endpoints |
| [`src/server/api/Sprk.Bff.Api/CLAUDE.md`](../../src/server/api/Sprk.Bff.Api/CLAUDE.md) | Add Publish Hygiene + AI Boundary sections |
| [`scripts/Deploy-BffApi.ps1`](../../scripts/Deploy-BffApi.ps1) | Hard-fail size guard (was warn-only), update threshold |
| [`.github/workflows/sdap-ci.yml`](../../.github/workflows/sdap-ci.yml) | New CI steps: RID check, sourcemap check, vuln scan |
| [`.github/workflows/deploy-bff-api.yml`](../../.github/workflows/deploy-bff-api.yml) | G-2 health-check window alignment, G-3 actions/* version fixes |
| [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) | Publish Hygiene subsection |
| [`.claude/skills/bff-deploy/SKILL.md`](../../.claude/skills/bff-deploy/SKILL.md) | Publish Hygiene section + next-review-date stamp |
| [`.claude/FAILURE-MODES.md`](../../.claude/FAILURE-MODES.md) | New entry (bloat root cause + process-failure pattern) |
| New: `.claude/adr/ADR-029-bff-publish-hygiene.md` and `docs/adr/ADR-029-bff-publish-hygiene.md` | New ADR (concise + full) |
| `tests/unit/Sprk.Bff.Api.Tests/` | No changes to test count; facade migration verified by existing tests |

---

## Requirements

### Functional Requirements

#### Outcome A — Size Reduction

- **FR-A1**: Configure `Sprk.Bff.Api.csproj` and/or publish script to publish with `--runtime linux-x64` (framework-dependent — NOT self-contained). **Acceptance**: `publish/runtimes/` contains only `linux-x64/` (or no native runtime folders if framework-dependent removes them entirely); zero `win-x64`, `win-x86`, `osx-x64`, `osx-arm64`, `linux-musl-x64`, `linux-arm64` subdirs.
- **FR-A2**: Exclude `wwwroot/**/*.js.map` from publish output. **Acceptance**: `find publish/wwwroot -name "*.map" | wc -l` returns 0.
- **FR-A3**: If duplicate Cosmos `ServiceInterop.dll` remains after FR-A1, remove it. **Acceptance**: only one copy of `ServiceInterop.dll` in publish tree (or zero if RID trim removed it).
- **FR-A4**: Compressed deploy package ≤60 MB. **Acceptance**: `Deploy-BffApi.ps1` output reports zip size ≤60 MB on dev deploy. If unattainable without HIGH-risk removals, document the gap in `LESSONS-LEARNED.md` and accept ≤65 MB.
- **FR-A5**: Uncompressed publish ≤150 MB. **Acceptance**: `du -sm deploy/api-publish/` returns ≤150 MB.

#### Outcome B — Security Hygiene

- **FR-B1**: Zero HIGH-severity CVEs in `dotnet list package --vulnerable --include-transitive` output for `Sprk.Bff.Api`. **Acceptance**: command output contains zero entries with severity "HIGH" (MEDIUM/LOW entries documented in `LESSONS-LEARNED.md`).
- **FR-B2**: Each outdated transitive (from `dotnet list package --outdated`) has documented patch-or-defer decision in `CANDIDATES.md`. **Acceptance**: every entry in outdated list has a row in `CANDIDATES.md` with disposition.
- **FR-B3**: Re-verify pinning rationale for 3 pre-release packages (`Azure.AI.Projects 1.0.0-beta.8`, `Microsoft.Agents.AI 1.0.0-rc1`, `Azure.AI.OpenAI 2.8.0-beta.1`) still applies. **Acceptance**: inline csproj comments updated if rationale changed; unchanged if still valid.

#### Outcome C — CI Guardrails

- **FR-C1**: CI step in `.github/workflows/sdap-ci.yml` that runs `dotnet publish --runtime linux-x64` and fails if non-Linux native runtimes are present in output. **Acceptance**: workflow step exists, has test that fails when a `win-x64/` folder is intentionally injected.
- **FR-C2**: CI step that fails if any `*.js.map` files exist in `publish/wwwroot/`. **Acceptance**: workflow step exists, validated by test injection.
- **FR-C3**: CI step that runs `dotnet list package --vulnerable --include-transitive` and fails on HIGH-severity findings. **Acceptance**: workflow fails on HIGH; passes on MEDIUM/LOW; reports counts.
- **FR-C4**: PR labels `[allow-size-growth]`, `[allow-vuln]`, and `[allow-direct-ai-inject]` provide explicit-acknowledgment escape hatches for FR-C1/C2/C3/C5/C6. PR body MUST contain justification when label used. **Acceptance**: workflow honors labels; PR template includes justification field.
- **FR-C5**: `Deploy-BffApi.ps1` hard-fails (not warn-only) if publish zip exceeds documented ceiling (default: baseline + 10%) unless `-AllowOversize` flag is passed. **Acceptance**: script exits non-zero on oversize without flag; passes with flag.
- **FR-C6**: CI step in `.github/workflows/sdap-ci.yml` fails any PR that introduces direct injection of AI-internal types (`IOpenAiClient`, `IPlaybookService`, plus the facade-internal services `IBriefingService`, `IInvoiceService`, `IRecordMatchingService`, `IWorkspacePrefillService`) anywhere in `src/server/api/Sprk.Bff.Api/` outside the `Services/Ai/` namespace. Mechanism: literal grep with `--exclude-dir=Services/Ai`. **Acceptance**: workflow step exists; synthetic-PR injection (e.g., `IOpenAiClient` in a Finance file) fails CI; `[allow-direct-ai-inject]` label + body justification permits override. This converts Outcome E from a one-time refactor into a permanent architectural boundary; without it, the facade decays as new code drifts back to direct injection.

#### Outcome D — Codified Prevention

- **FR-D1**: New ADR-029 (concise at `.claude/adr/ADR-029-bff-publish-hygiene.md`; full at `docs/adr/ADR-029-bff-publish-hygiene.md`) records publish hygiene policy: `linux-x64` only, no sourcemaps, vuln scan in CI, quarterly review cadence. **Acceptance**: both files exist, ADR-029 added to both INDEX.md files.
- **FR-D2**: [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) gains a "Publish Hygiene" subsection codifying FR-A1, FR-A2, FR-C1/C2/C3, FR-C5. **Acceptance**: file contains new subsection citing ADR-029.
- **FR-D3**: [`.claude/skills/bff-deploy/SKILL.md`](../../.claude/skills/bff-deploy/SKILL.md) updated with Publish Hygiene section + next-review-date stamp (set to baseline+90 days). **Acceptance**: file contains section; stamp is a parseable date.
- **FR-D4**: [`.claude/FAILURE-MODES.md`](../../.claude/FAILURE-MODES.md) gains new entry (e.g., AP-2 or G-4) documenting (a) bloat root cause + fix, (b) "many-projects-each-adding-without-considering-overall-quality" pattern as a process failure. **Acceptance**: file contains new entry with date 2026-05-20+.
- **FR-D5**: `.github/workflows/deploy-bff-api.yml` health-check window updated to match `Deploy-BffApi.ps1` defaults (24 retries × 5s = 120s) per FAILURE-MODES.md G-2. **Acceptance**: workflow YAML shows matching values.
- **FR-D6**: `.github/workflows/deploy-bff-api.yml` `actions/*@vN` references audited; any invalid major versions corrected per FAILURE-MODES.md G-3. **Acceptance**: `actionlint` run against the workflow returns no "unknown reference" errors.
- **FR-D7**: `src/server/api/Sprk.Bff.Api/CLAUDE.md` gains (a) Publish Hygiene section pointing to ADR-029, (b) AI Boundary section pointing to refined ADR-013 + facade pattern, (c) reference to [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md). **Acceptance**: file contains all three sections.
- **FR-D8**: Project `LESSONS-LEARNED.md` committed capturing surprises, gotchas, and future risks for next quarterly review. **Acceptance**: file exists at `projects/sdap-bff-api-remediation-fix/LESSONS-LEARNED.md`.

#### Outcome E — Internal AI Hygiene

- **FR-E1**: Create `Services/Ai/PublicContracts/` namespace with focused facade interfaces (small interfaces grouped by consumer concern per design §11 Q7 default: e.g., `IBriefingAi`, `IInvoiceAi`, `IRecordMatchingAi`, `IWorkspacePrefillAi`). **Acceptance**: namespace exists with at least one interface per current consumer (Finance, Workspace, Jobs handlers, Dataverse, Filters); implementation classes wire to existing `IOpenAiClient`/`IPlaybookService` internally.
- **FR-E2**: Migrate all inbound CRUD→AI direct dependencies (preliminary count "20" from initial design; **actual count and per-folder distribution defer to Phase 1 inventory output per PF-3**) to consume facade interfaces instead of `IOpenAiClient`, `IPlaybookService`, or other AI-internal types directly. **Acceptance (production-scope only)**: `grep -rn "IOpenAiClient\|IPlaybookService" src/server/api/Sprk.Bff.Api/ --include='*.cs' --exclude-dir=Services/Ai --exclude-dir=bin --exclude-dir=obj` returns zero matches. **Test code is explicitly out of scope** (tests legitimately mock AI internals; `tests/unit/Sprk.Bff.Api.Tests/` is excluded). **DI registration modules are special-cased**: `Infrastructure/DI/AiModule.cs` may still reference AI internals — that's where the facade is wired to its underlying implementations; the boundary IS those modules.
- **FR-E3**: Relocate AI-coupled job handlers from `Services/Jobs/Handlers/` to `Services/Ai/Jobs/`. **Authoritative handler list defers to Phase 1 inventory (task 015 reflection probe + task 014 static usage map; Phase 0 task 007 confirms scope) per the "AI-coupled rule"**: a handler is AI-coupled if its compiled assembly references the `Sprk.Bff.Api.Services.Ai.*` namespace AND it is not CRUD-coupled (does not require `Spaarke.Dataverse` / `Microsoft.Xrm.Sdk`). Preliminary list from initial design (6 handlers: AppOnlyDocumentAnalysisJobHandler, EmailAnalysisJobHandler, AttachmentClassificationJobHandler, RagIndexingJobHandler, InvoiceExtractionJobHandler, ProfileSummaryJobHandler) is non-binding — senior review 2026-05-24 surfaced that 2 of these may be CRUD-coupled and 3 others (BulkRagIndexing, DocumentProcessing, InvoiceIndexing) may belong in the list. Phase 1 inventory is the source of truth. JobType string registration unchanged (dispatch by string). **Acceptance**: files at new paths per inventory-derived list; old paths absent for those moved; `JobProcessingModule` registration succeeds; integration tests pass.
- **FR-E4**: All existing tests pass with no behavior change. **Acceptance**: `dotnet test tests/unit/Sprk.Bff.Api.Tests/` pass count + duration within ±5% of Phase 3 baseline.
- **FR-E5**: Refined ADR-013 (2026-05-20) referenced from `src/server/api/Sprk.Bff.Api/CLAUDE.md` AI Boundary section; facade pattern documented with code example. **Acceptance**: section exists with `[ADR-013]` link and an example showing wrong-vs-correct injection.

### Non-Functional Requirements

- **NFR-01**: Each Phase 4 candidate change gets a 24–48h dev observation window before next candidate. **Verify**: `EXECUTION-LOG.md` records deploy timestamp + next-deploy timestamp ≥24h later for each candidate.
- **NFR-02**: Each Phase 4 candidate: zero new exception types in App Insights vs Phase 3 baseline. **Verify**: App Insights query result diff against baseline.
- **NFR-03**: Each Phase 4 candidate: P95 latency within 10% of Phase 3 baseline per endpoint. **Verify**: App Insights metrics query.
- **NFR-04**: Each Phase 4 candidate: error rate within 10% of baseline. **Verify**: App Insights metrics query.
- **NFR-05**: Each Phase 4 candidate: dependency call success rate (Graph, Dataverse, Service Bus, Cosmos, Redis) unchanged. **Verify**: App Insights dependency telemetry.
- **NFR-06**: Rollback executable via `git revert` + `Deploy-BffApi.ps1` within 10 minutes of decision (decision may take the full bake window if regression is gradual). **Verify**: rehearsal in Phase 0 if process is unfamiliar.
- **NFR-07**: All Phase 1–4 work targets dev (`spe-api-dev-67e2xz`) only. Demo/prod touch only in Phase 5 with explicit owner authorization (+ ops authorization for prod). **Verify**: deploy logs show no demo/prod hits in Phase 1–4 commits.
- **NFR-08** (revised 2026-05-24): **Owner sign-off + AI-directed verification model.** MEDIUM/HIGH tier candidates (Phase 4) and Phase 5 promotions require: (a) owner approval, (b) successful AI-directed verification via `task-execute` skill's mandatory `adr-check` + `code-review` gates at Step 9.5 (FULL rigor), (c) passing CI guards (FR-C1–C6). The "dual approver" enterprise pattern is explicitly NOT used — verification rigor comes from AI-directed checks layered with owner judgment, not from a second human reviewer. **Verify**: PR/promotion notes record owner ACK + adr-check pass + code-review pass + CI green.
- **NFR-09**: Build warning count must NOT exceed Phase 3 baseline for any Phase 4 change. (csproj sets `TreatWarningsAsErrors=false`; tightening is a separate project — this NFR just guards against regression.) **Verify**: `dotnet build` warning count comparison.
- **NFR-10**: Reflection-load probe (Phase 1 deliverable) results match between baseline and post-change for any MEDIUM/HIGH candidate. **Verify**: assembly list diff.
- **NFR-11**: No `<PublishTrimmed>`, `<PublishAot>`, .NET version bump, Graph SDK / Kiota version change, or pre-release package version change in any Phase 4 commit. **Verify**: csproj diff review during code review.

---

## Technical Constraints

### Applicable ADRs

- **ADR-001** (Minimal API + Workers) — single Minimal API App Service; Functions only for narrow out-of-band integration
- **ADR-004** (Job Contract) — Service Bus job handlers use `IJobHandler<T>` pattern; relocation in FR-E3 preserves contract
- **ADR-007** (SpeFileStore Facade) — no direct `GraphServiceClient` injection; pattern echoed by new `Services/Ai/PublicContracts/` facade
- **ADR-008** (Endpoint Filters) — authorization via endpoint filters, not global middleware (Phase 0–6 doesn't change this; preserves existing)
- **ADR-010** (DI Minimalism) — known existing violation (99+ vs ≤15 target); not in scope to fix here; no change in this project may worsen the count
- **ADR-013 (refined 2026-05-20)** (AI Architecture) — extension policy with four exception criteria; **REQUIRES** `Services/Ai/PublicContracts/` facades for external CRUD consumers (binding for FR-E1, FR-E2)
- **ADR-027** (Subscription Isolation + Managed Solutions) — managed solutions for prod; informs Phase 5 promotion process
- **ADR-028** (Spaarke Auth Architecture) — auth flows preserved; no change in this project
- **ADR-029 (NEW)** — publish hygiene policy; output of FR-D1

### MUST Rules (extracted from ADRs + constraints)

- ✅ **MUST** publish to `deploy/api-publish/` (NOT `/tmp`, NOT in source tree) per [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md)
- ✅ **MUST** verify ~240 entries / ~60 MB before deploying (per existing constraint; this project lowers the implicit ceiling)
- ✅ **MUST** set `stdoutLogEnabled="true"` in published `web.config`
- ✅ **MUST** keep all Kiota packages version-matched at `1.21.2` (current) — no individual bumps
- ✅ **MUST** route all SPE operations through `SpeFileStore` (ADR-007)
- ✅ **MUST** route external CRUD-side AI consumers through `Services/Ai/PublicContracts/` facade (refined ADR-013)
- ✅ **MUST** use endpoint filters for authorization (ADR-008)
- ✅ **MUST** use `Deploy-BffApi.ps1` for all BFF deploys (enforces hash-verify, health-check window, slot-swap rollback)
- ✅ **MUST** load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) for any task touching the BFF (binding governance)
- ❌ **MUST NOT** set `<PublishTrimmed>true</PublishTrimmed>` or `<PublishAot>true</PublishAot>` (reflection-hostile to Graph SDK, Identity.Web, EF, DI, JSON serializers)
- ❌ **MUST NOT** add new direct CRUD→AI dependencies (use facade instead)
- ❌ **MUST NOT** bump Kiota packages individually, Graph SDK, .NET TFM, or pre-release AI packages
- ❌ **MUST NOT** deploy from external temp directory (anti-pattern #16, ~22 MB incomplete packages)
- ❌ **MUST NOT** publish `appsettings.json` files; secrets via Key Vault references only

### Existing Patterns

- **Deploy script + skill**: [`scripts/Deploy-BffApi.ps1`](../../scripts/Deploy-BffApi.ps1) + [`.claude/skills/bff-deploy/SKILL.md`](../../.claude/skills/bff-deploy/SKILL.md) — canonical deployment with hash-verify + auto-recovery + health-check
- **Constraint file pattern**: see [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) for the format that the new ADR-029 supplements
- **Failure mode entry pattern**: see [`.claude/FAILURE-MODES.md`](../../.claude/FAILURE-MODES.md) AP-1, G-2, G-3 for the format FR-D4 follows
- **Facade pattern (ADR-007 echo)**: [`SpeFileStore.cs`](../../src/server/api/Sprk.Bff.Api/Services/SpeFileStore.cs) is the canonical facade-over-Graph-SDK; Outcome E facade mirrors this pattern for AI internals
- **Module DI pattern**: see existing `Add*Module()` extensions in `Sprk.Bff.Api/Program.cs` — facade DI registration follows the same shape

---

## Success Criteria

### Outcome A — Size

- [ ] **SC-01**: `INVENTORY.md` and `CANDIDATES.md` committed and approved. **Verify**: file presence + owner sign-off comment in PR.
- [ ] **SC-02**: All approved SAFE candidates deployed and stable in dev for 24–48h each. **Verify**: `EXECUTION-LOG.md` timestamps.
- [ ] **SC-03**: Compressed package ≤60 MB OR lowest stable size + documented gap. **Verify**: `Deploy-BffApi.ps1` output.
- [ ] **SC-04**: Uncompressed publish ≤150 MB. **Verify**: `du -sm deploy/api-publish/`.

### Outcome B — Security

- [ ] **SC-05**: Zero HIGH-severity CVEs in vulnerable-transitives scan. **Verify**: `dotnet list package --vulnerable --include-transitive`.
- [ ] **SC-06**: Outdated transitives triaged with documented decisions. **Verify**: `CANDIDATES.md` row count = outdated count.
- [ ] **SC-07**: Pre-release pinning rationale re-verified. **Verify**: csproj inline comments match current chain reality.

### Outcome C — Operational

- [ ] **SC-08**: Zero new exception types in App Insights vs Phase 3 baseline. **Verify**: App Insights query.
- [ ] **SC-09**: Error rates within 10% of baseline. **Verify**: App Insights metrics.
- [ ] **SC-10**: P95 latency within 10% of baseline per endpoint. **Verify**: App Insights metrics.

### Outcome D — Codification

- [ ] **SC-11**: Deploy-script size guard hard-fails by default. **Verify**: integration test of `Deploy-BffApi.ps1`.
- [ ] **SC-12**: CI guard for non-Linux RID detection. **Verify**: PR test injection.
- [ ] **SC-13**: CI guard for `*.js.map` exclusion. **Verify**: PR test injection.
- [ ] **SC-14**: CI guard for vulnerable-transitive HIGH-severity fail. **Verify**: PR test injection.
- [ ] **SC-15**: `deploy-bff-api.yml` aligned with G-2 health-check window (120s). **Verify**: workflow YAML.
- [ ] **SC-16**: G-3 action versions resolved in `deploy-bff-api.yml`. **Verify**: `actionlint` clean.
- [ ] **SC-17**: ADR-029 published (concise + full + indexed). **Verify**: file presence + INDEX.md entries.
- [ ] **SC-18**: `.claude/constraints/azure-deployment.md` updated with Publish Hygiene. **Verify**: section present.
- [ ] **SC-19**: `.claude/skills/bff-deploy/SKILL.md` updated. **Verify**: section + next-review-date present.
- [ ] **SC-20**: `.claude/FAILURE-MODES.md` updated with bloat root cause + process pattern. **Verify**: new entry present.
- [ ] **SC-21**: `LESSONS-LEARNED.md` committed. **Verify**: file present in project folder.

### Outcome E — Internal AI Hygiene

- [ ] **SC-22**: `Services/Ai/PublicContracts/` facade namespace created. **Verify**: directory + at least 4 interfaces present.
- [ ] **SC-23**: All inbound CRUD→AI direct dependencies (per Phase 1 inventory) migrated to facade. **Verify**: production-scope grep (per FR-E2 acceptance) returns zero outside `Services/Ai/` and `Infrastructure/DI/`.
- [ ] **SC-24**: AI-coupled job handlers (per Phase 1 inventory + AI-coupled rule) relocated to `Services/Ai/Jobs/`. **Verify**: directory listing + JobType dispatch tests.
- [ ] **SC-25**: All tests pass; no behavioral regression. **Verify**: `dotnet test` pass count + duration matches baseline ±5%.
- [ ] **SC-26**: `src/server/api/Sprk.Bff.Api/CLAUDE.md` documents facade pattern + ADR-013 boundary. **Verify**: sections present.
- [ ] **SC-27**: Refined ADR-013 referenced from BFF CLAUDE.md and `.claude/constraints/bff-extensions.md`. **Verify**: links present.
- [ ] **SC-28**: FR-C6 CI gate live; synthetic-PR injection of `IOpenAiClient` in a CRUD file fails CI. **Verify**: `.github/workflows/sdap-ci.yml` contains the gate; test-injection PR fails as expected; `[allow-direct-ai-inject]` label + justification permits override.

---

## Dependencies

### Prerequisites

- Owner sign-off on [`design.md`](design.md) §3 Resolved Decisions
- Owner sign-off on §Unresolved Questions (esp. prod process determination per UQ-02; remaining UQs)
- Coordination with **ALL active BFF-touching projects** (Phase 0 task 004 enumerates and triages: `sdap-bff-api-and-performance-enhancement-r1`, `auth-sso-and-email-wizard-2026-05`, `sdap-file-upload-document-r2`, `self-service-registration`, plus any others surfaced) — no in-flight BFF deploy during Phase 3 baseline + Phase 4 bake windows
- Coordination with `ai-spaarke-insights-engine-r1` owner — capture baseline (Phase 3) fully BEFORE Engine integration starts, or fully AFTER stable; produce written agreement that Insights Engine PRs merged after Phase 4 task 046 (facade creation) MUST use `Services/Ai/PublicContracts/` facade

### External Dependencies

- Azure App Insights access (Phase 3 baseline + Phase 4 observation)
- Azure App Service `spe-api-dev-67e2xz` (dev environment for Phase 1–4)
- Azure App Service `spaarke-demo` (Phase 5 demo bake)
- Azure CLI authenticated session for `az webapp show` queries
- GitHub Actions write access (Phase 6 CI guard PRs)
- Optional: `actionlint` tool for FR-D6 verification

---

## Owner Clarifications

*Captured during design.md authoring (multi-turn conversation 2026-05-20):*

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Extraction question | Should AI subsystem be extracted from BFF? | No — keep in BFF (per 2026-05-20 assessment + refined ADR-013) | Outcome E is in-BFF facade introduction, not extract-prep |
| Insights Engine Phase 1 | New AI subsystem in own service? | No — keep in BFF (latency + transactional coupling) | Project scope stays focused on bloat + facades |
| §22.2 numerical triggers | Treat as hard rules? | No — refined ADR-013 supersedes; technical criteria, not numbers | Open-ended reassessment, no calendar deadline |
| Outcome E scope | Should we restructure AI in BFF? | Yes — low-risk facade + handler relocation work | Added Outcome E with 6 FRs (FR-E1 through FR-E5 + tests) |
| BFF additions governance | New process imperative needed? | Yes — root CLAUDE.md + binding constraint | Created [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md); root CLAUDE.md §10 |

## Assumptions

*Proceeding with these assumptions (owner has not yet explicitly confirmed):*

- **Approval model** (revised 2026-05-24): **Owner sign-off + AI-directed verification.** Per NFR-08, the dual-approver enterprise pattern is explicitly NOT used. Verification rigor comes from `task-execute` skill's mandatory `adr-check` + `code-review` gates (Step 9.5 at FULL rigor) plus CI guards (FR-C1–C6) plus owner judgment. Rationale: single project owner + AI-directed coding procedures provide effective check-and-balance without the friction of a second human approver. Applies to Phase 4 MEDIUM/HIGH AND Phase 5 prod promotion.
- **Prod deploy process**: assumed undefined → this project scopes to dev+demo unless owner identifies an existing canonical process in Phase 0. Affects whether Phase 5 stays in-project or becomes a follow-up.
- **CI guard size ceiling**: baseline + 10% (e.g., if baseline is 58 MB, ceiling is 64 MB). Tighter if owner prefers.
- **Facade interface granularity**: small focused interfaces grouped by consumer concern (`IBriefingAi`, `IInvoiceAi`, etc.) — not one large `IBffAiPublicContracts`. Easier testing, lower coupling, simpler deprecation.
- **Shared-library scope**: `Spaarke.Core` and `Spaarke.Dataverse` inventoried only, NOT pruned in this project.
- **Build flag policy**: `dotnet build` (without `--warnaserror`) — Phase 3 baselines warning count; Phase 4 verifies no new warnings. Tightening warnings-as-errors is a separate project.

## Unresolved Questions

*Phase 0 resolution items per [`design.md`](design.md) §11; do not block design approval but must close before Phase 1:*

- [x] **UQ-01** (RESOLVED 2026-05-24): ~~Who is the dual approver?~~ **Resolution**: Operator-only model adopted. Per revised NFR-08, the dual-approver enterprise pattern is not used; verification rigor comes from AI-directed checks (`adr-check` + `code-review` at task-execute Step 9.5) + CI guards + owner judgment. Phase 0 task 002 documents this decision rather than designating a second human approver.
- [ ] **UQ-02**: Does a canonical prod deploy process exist? **Blocks**: Phase 5 scope determination (Phase 5 cannot start otherwise; project may scope to dev+demo only).
- [ ] **UQ-03**: Is `sdap-bff-api-and-performance-enhancement-r1` deploying in next 6 weeks? **Blocks**: Phase 4 scheduling.
- [ ] **UQ-04**: When does Insights Engine Phase 1 integration land in `Sprk.Bff.Api`? **Blocks**: Phase 3 baseline timing (capture before OR after, never mid).
- [ ] **UQ-05**: CI guard size ceiling — baseline + 10% (default) or +5% (tighter)? **Blocks**: FR-C5 implementation.
- [ ] **UQ-06**: `Spaarke.Core` / `Spaarke.Dataverse` scope — inventory-only (default) or also prune? **Blocks**: Phase 1 inventory scope (minor).
- [ ] **UQ-07**: Facade interface design — small focused interfaces (default assumption) or one large interface? **Blocks**: FR-E1 implementation kickoff.

---

## Project Structure (post-pipeline expected layout)

```
projects/sdap-bff-api-remediation-fix/
├── approach.md                    # Upstream record (preserved)
├── design.md                      # Design layer (input to this spec)
├── spec.md                        # THIS FILE
├── CC-PROMPT-bff-extraction-assessment.md  # Phase 0 validation prompt (now resolved)
├── README.md                      # Generated by /project-pipeline
├── CLAUDE.md                      # Generated by /project-pipeline (AI agent context)
├── current-task.md                # Generated by /project-pipeline (active task tracker)
├── PLAN.md                        # Generated by /project-pipeline (high-level plan)
├── tasks/
│   ├── TASK-INDEX.md              # Generated by /project-pipeline
│   └── *.poml                     # Generated by /project-pipeline (per-task work items)
├── inventory/                     # Phase 1 deliverables
│   ├── INVENTORY.md
│   ├── packages-direct.txt
│   ├── packages-transitive.txt
│   ├── vulnerable.txt
│   ├── outdated.txt
│   ├── native-runtimes.txt
│   ├── wwwroot-assets.txt
│   └── reflection-probe.txt
├── baseline/                      # Phase 3 deliverables
│   ├── BASELINE.md
│   ├── tests.txt
│   ├── endpoints-smoke.json
│   ├── app-insights-48h.json
│   ├── deployed-sha256.txt
│   ├── publish-metrics.txt
│   ├── reflection-probe-baseline.txt
│   └── extraction-assessment-archive.md  # Copy of docs/assessments/bff-ai-extraction-assessment-2026-05-20.md
├── CANDIDATES.md                  # Phase 2 deliverable
├── EXECUTION-LOG.md               # Phase 4 ongoing
└── LESSONS-LEARNED.md             # Phase 6 deliverable
```

---

*AI-optimized specification. Original: [design.md](design.md). Ready for `/project-pipeline projects/sdap-bff-api-remediation-fix`.*
