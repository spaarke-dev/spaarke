# Production Environment Setup R1 — Implementation Plan

> **Created**: 2026-03-11
> **Phases**: 5
> **Estimated Tasks**: ~30
> **Parallel Execution**: Optimized for Claude Code task agents

---

## Architecture Context

### Hybrid Deployment Model (Path C)

Shared platform deployed once → per-customer resources provisioned on demand.

**Shared Platform** (`rg-spaarke-platform-prod`):
- App Service Plan + BFF API (P1v3, autoscaling, staging slot)
- Azure OpenAI, AI Search Standard2, Document Intelligence S0
- App Insights + Log Analytics (180-day retention)
- Platform Key Vault

**Per-Customer** (`rg-spaarke-{customer}-prod`):
- Storage Account, Key Vault, Service Bus, Redis Cache
- Dataverse Environment (dedicated, automated via Admin API)
- SPE Containers (Spaarke-hosted)

### Discovered Resources

**ADRs** (8 applicable):
- ADR-001 (Minimal API), ADR-004 (Async Jobs), ADR-009 (Redis), ADR-010 (DI Minimalism)
- ADR-017 (Job Status), ADR-018 (Feature Flags), ADR-019 (ProblemDetails), ADR-020 (Versioning)

**Existing Infrastructure**:
- `infrastructure/bicep/modules/` — 9 reusable Bicep modules (app-service, redis, service-bus, storage, openai, ai-search, monitoring, key-vault, ai-foundry-hub)
- `infrastructure/bicep/model1-shared.bicep` — Reference for shared resources
- `infrastructure/bicep/model2-full.bicep` — Reference for full-stack provisioning
- `scripts/Deploy-BffApi.ps1` — Existing BFF deploy (needs parameterization)
- `.github/workflows/sdap-ci.yml` — Existing CI pipeline

**Key Constraints**:
- Same Entra ID tenant as dev (`a221a95e-...`)
- Production domain: `api.spaarke.com`
- Region: `westus2`
- Naming: `sprk_`/`spaarke-` standard (no legacy prefixes)

---

## Phase Breakdown

### Phase 1: Foundation — Bicep Templates & Naming Standard (Tasks 001-006)

**Goal**: Create the infrastructure-as-code foundation and formalize naming.

| Task | Description | Parallel Group | Dependencies |
|------|------------|----------------|--------------|
| 001 | Create `platform.bicep` from existing modules | P1-A | None |
| 002 | Create `customer.bicep` from existing modules | P1-A | None |
| 003 | Create production parameter files (platform-prod, demo-customer, customer-template) | P1-A | None |
| 004 | Finalize naming convention — update AZURE-RESOURCE-NAMING-CONVENTION.md to "Adopted" | P1-B | None |
| 005 | Create `appsettings.Production.json` with Key Vault references | P1-B | None |
| 006 | Validate Bicep templates with `az deployment group what-if` | P1-C | 001, 002, 003 |

**Parallel Groups**:
- **P1-A** (3 agents): Tasks 001, 002, 003 — independent Bicep files, different modules
- **P1-B** (2 agents): Tasks 004, 005 — independent docs/config work
- **P1-C** (1 agent): Task 006 — depends on P1-A completion
- **Max concurrent**: 5 agents

---

### Phase 2: Deployment Scripts (Tasks 010-016)

**Goal**: Create all deployment and provisioning automation scripts.

| Task | Description | Parallel Group | Dependencies |
|------|------------|----------------|--------------|
| 010 | Create `Deploy-Platform.ps1` (shared infrastructure deployment) | P2-A | 001 |
| 011 | Parameterize `Deploy-BffApi.ps1` (multi-environment support, staging slots) | P2-A | 005 |
| 012 | Create `Deploy-DataverseSolutions.ps1` (managed solution import in order) | P2-A | None |
| 013 | Create `Test-Deployment.ps1` (smoke tests — BFF, Dataverse, SPE, AI) | P2-A | None |
| 014 | Create `Provision-Customer.ps1` (full end-to-end customer provisioning) | P2-B | 010, 012, 013 |
| 015 | Create `Decommission-Customer.ps1` (customer teardown) | P2-B | 002 |
| 016 | Create `Rotate-Secrets.ps1` (Key Vault secret rotation) | P2-C | None |

**Parallel Groups**:
- **P2-A** (4 agents): Tasks 010, 011, 012, 013 — independent scripts, different concerns
- **P2-B** (2 agents): Tasks 014, 015 — depend on earlier scripts being complete
- **P2-C** (1 agent): Task 016 — independent utility script
- **Max concurrent**: 5 agents (P2-A + P2-C)

---

### Phase 3: Platform & Demo Deployment (Tasks 020-027)

**Goal**: Deploy shared platform and demo customer, validate everything works.

**Note**: This phase involves actual Azure deployments. Tasks are more sequential due to infrastructure dependencies, but documentation tasks can run in parallel.

| Task | Description | Parallel Group | Dependencies |
|------|------------|----------------|--------------|
| 020 | Deploy shared platform (`Deploy-Platform.ps1`) | P3-A | 010 |
| 021 | Create Entra ID app registrations (BFF API prod, Dataverse S2S prod) | P3-A | None |
| 022 | Configure custom domain `api.spaarke.com` + SSL | P3-B | 020 |
| 023 | Deploy BFF API to production (`Deploy-BffApi.ps1`) | P3-B | 020, 021 |
| 024 | Provision demo customer (`Provision-Customer.ps1 -CustomerId demo`) | P3-C | 023, 014 |
| 025 | Load sample data into demo (test documents, demo records) | P3-D | 024 |
| 026 | Configure demo user access (B2B guest invitations) | P3-D | 024 |
| 027 | Run full smoke test suite and document gaps | P3-E | 025, 026 |

**Parallel Groups**:
- **P3-A** (2 agents): Tasks 020, 021 — platform infra + app registrations are independent
- **P3-B** (2 agents): Tasks 022, 023 — both depend on platform but are independent of each other
- **P3-C** (1 agent): Task 024 — sequential, depends on BFF API deployed
- **P3-D** (2 agents): Tasks 025, 026 — independent post-provisioning tasks
- **P3-E** (1 agent): Task 027 — validation after all setup complete
- **Max concurrent**: 2 agents

---

### Phase 4: CI/CD Pipelines (Tasks 030-034)

**Goal**: Create GitHub Actions workflows for automated deployments.

| Task | Description | Parallel Group | Dependencies |
|------|------------|----------------|--------------|
| 030 | Create `deploy-platform.yml` (manual dispatch, what-if + deploy) | P4-A | 010 |
| 031 | Create `deploy-bff-api.yml` (push trigger, staging → swap → rollback) | P4-A | 011 |
| 032 | Create `provision-customer.yml` (manual dispatch, runs Provision-Customer.ps1) | P4-A | 014 |
| 033 | Configure GitHub environment protection rules (staging, production) | P4-B | 030, 031, 032 |
| 034 | Test CI/CD pipeline end-to-end (trigger deploy, verify) | P4-C | 033 |

**Parallel Groups**:
- **P4-A** (3 agents): Tasks 030, 031, 032 — independent workflow files
- **P4-B** (1 agent): Task 033 — depends on workflows existing
- **P4-C** (1 agent): Task 034 — validation
- **Max concurrent**: 3 agents

---

### Phase 5: Documentation & Wrap-up (Tasks 040-045, 090)

**Goal**: Create operational documentation, validate repeatability, close project.

| Task | Description | Parallel Group | Dependencies |
|------|------------|----------------|--------------|
| 040 | Write production deployment guide (step-by-step) | P5-A | 020-027 |
| 041 | Write customer onboarding runbook | P5-A | 014, 024 |
| 042 | Write incident response procedures | P5-A | 027 |
| 043 | Write secret rotation procedures | P5-A | 016 |
| 044 | Write monitoring and alerting setup guide | P5-A | 020 |
| 045 | Provision + decommission second test customer (repeatability validation) | P5-B | 014, 015 |
| 090 | Project wrap-up (README status, lessons learned, archive) | P5-C | All |

**Parallel Groups**:
- **P5-A** (5 agents): Tasks 040-044 — independent documentation, different topics
- **P5-B** (1 agent): Task 045 — repeatability test
- **P5-C** (1 agent): Task 090 — final wrap-up
- **Max concurrent**: 6 agents (P5-A + P5-B)

---

## Parallel Execution Summary

| Phase | Tasks | Max Concurrent Agents | Notes |
|-------|-------|----------------------|-------|
| Phase 1 | 001-006 | 5 | Bicep + naming + config all independent |
| Phase 2 | 010-016 | 5 | Scripts are largely independent |
| Phase 3 | 020-027 | 2 | Sequential infrastructure dependencies |
| Phase 4 | 030-034 | 3 | Workflow files independent |
| Phase 5 | 040-045, 090 | 6 | Documentation fully parallelizable |

**Total tasks**: 31 (including wrap-up)
**Maximum parallelism**: 6 agents (Phase 5)
**Critical path**: Phase 1 (P1-A) → Phase 2 (P2-A) → Phase 3 (sequential) → Phase 4 → Phase 5

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | File Conflict Risk | Agent Count |
|-------|-------|-------------|-------------------|-------------|
| P1-A | 001, 002, 003 | None | None — different Bicep files | 3 |
| P1-B | 004, 005 | None | None — docs vs config | 2 |
| P1-C | 006 | P1-A complete | Reads Bicep files (no write) | 1 |
| P2-A | 010, 011, 012, 013 | Phase 1 | None — different script files | 4 |
| P2-B | 014, 015 | 010, 012, 013 | None — different scripts | 2 |
| P2-C | 016 | None | None — independent utility | 1 |
| P3-A | 020, 021 | Phase 2 | None — Azure vs Entra ID | 2 |
| P3-B | 022, 023 | 020 | None — DNS vs app deploy | 2 |
| P3-C | 024 | 023, 014 | Sequential | 1 |
| P3-D | 025, 026 | 024 | None — data vs users | 2 |
| P3-E | 027 | P3-D | Sequential validation | 1 |
| P4-A | 030, 031, 032 | Phase 2 scripts | None — different workflow files | 3 |
| P4-B | 033 | P4-A | Sequential | 1 |
| P4-C | 034 | 033 | Sequential validation | 1 |
| P5-A | 040, 041, 042, 043, 044 | Phase 3 | None — different doc files | 5 |
| P5-B | 045 | 014, 015 | None — Azure operations | 1 |
| P5-C | 090 | All | Final wrap-up | 1 |

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation | Affects Tasks |
|------|-----------|--------|------------|---------------|
| Power Platform Admin API limitations | High | Medium | Document manual fallbacks in provisioning script | 014, 024 |
| Azure OpenAI quota insufficient | Medium | High | Request increases pre-deployment, have fallback capacities | 020 |
| Managed solution import failures | Medium | Medium | Test import order in dev first | 012, 024 |
| DNS propagation delays | Low | Low | Use Azure URL initially, add domain later | 022 |
| GitHub Actions secret configuration | Low | Medium | Document required secrets clearly | 030-032 |

---

## References

- [spec.md](spec.md) — AI implementation specification
- [design.md](design.md) — Original design document
- `infrastructure/bicep/modules/` — Reusable Bicep modules
- `infrastructure/bicep/model2-full.bicep` — Full-stack reference
- `scripts/Deploy-BffApi.ps1` — Existing deployment script
- `.github/workflows/sdap-ci.yml` — Existing CI pipeline
- `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` — Naming standard
- `.claude/adr/ADR-001.md` through `ADR-020.md` — Applicable ADRs
