# Task Index — Spaarke External Access Platform (R1)

> **Generated**: 2026-07-19 by `/project-pipeline` (task-create). **Total tasks**: 25.
> **Status legend**: 🔲 not-started · 🔄 in-progress/retry · ✅ completed · ⛔ blocked

## Registry

| ID | Title | Phase | Rigor | Tier/Effort | Deps | Parallel | Status |
|----|-------|-------|-------|-------------|------|----------|--------|
| 001 | Provision Entra External ID (CIAM) tenant + user-flow config | 0 | STANDARD | sonnet/high | none | ✗ (ops, irreversible) | ✅ |
| 002 | Register CIAM-tenant app for Graph user management | 0 | STANDARD | sonnet/high | 001 | ✗ (auth-sensitive) | ✅ |
| 003 | Provision Azure Static Web Apps resource + CI/CD scaffold | 0 | STANDARD | sonnet/high | none | ✓ Group A | ✅ |
| 004 | Add `Contact.sprk_externalobjectid` Dataverse field | 0 | STANDARD | sonnet/high | none | ✓ Group A | ✅ |
| 010 | Author `staticwebapp.config.json` + SWA deploy workflow | 1 | FULL | sonnet/high | 003 | ✗ (shared workflow/config) | ✅ |
| 011 | Migrate HashRouter → BrowserRouter + in-app 404 | 1 | FULL | sonnet/high | 010 | ✗ (shares App.tsx) | 🔲 |
| 012 | Preserve deep link through the login redirect | 1 | FULL | sonnet/high | 011 | ✗ (shares App.tsx/auth) | 🔲 |
| 013 | Add SWA origin to BFF CORS + SPA redirect URIs | 1 | FULL | sonnet/high | 003 | ✗ (shares Program/AuthModule) | ✅ |
| 014 | Deploy external-spa to SWA on existing B2B + verify parity | 1 | STANDARD | sonnet/high | 010,011,012,013 | ✗ (deploy gate) | 🔲 |
| 020 | Add second "Ciam" JwtBearer scheme | 2 | FULL | **opus**/high | 001 | ✗ (shares AuthModule) | ✅ |
| 021 | Pin "Ciam" scheme on `/api/v1/external` group | 2 | FULL | **opus**/high | 020 | ✗ (shares ExternalAccessEndpoints) | ✅ |
| 022 | Build cross-tenant CIAM Graph client | 2 | FULL | **opus**/high | 002 | ✗ (new client + DI) | ✅ |
| 023 | Resolve Contact by CIAM `oid` | 2 | FULL | **opus**/high | 004,020 | ✗ (shares filter/participation) | ✅ |
| 024 | Add CIAM onboarding email template + method | 2 | STANDARD | sonnet/high | none | ✓ Group B | ✅ |
| 025 | Implement admin-initiated CIAM provisioner | 2 | FULL | **opus**/**xhigh** | 022,004,024 | ✗ (multi-file wiring) | ✅ |
| 026 | Remove vestigial synthetic SPE container grant | 2 | FULL | sonnet/high | none | ✗ (shares GrantEndpoint) | ✅ |
| 027 | Add app-only external document download endpoint | 2 | FULL | sonnet/**xhigh** | 021,023 | ✗ (shares ExternalProjectDataEndpoints) | ✅ |
| 028 | Point external-spa at the CIAM authority | 2 | FULL | sonnet/high | 020 | ✗ (SPA auth config) | ✅ |
| 029 | Core-user "Invite to Secure Workspace" trigger | 2 | FULL | sonnet/high | 025 | ✗ (new command + wiring) | ✅ |
| 030 | Unit tests for CIAM external-access surface | 2 | FULL (TEST) | sonnet/high | 020,021,022,023,025,026,027 | ✗ (spans files) | ✅ |
| 031 | Deploy BFF + verify publish size & CVE | 2 | STANDARD | sonnet/high | 030 | ✗ (deploy gate) | 🔲 |
| 040 | End-to-end parity verification (SWA + CIAM) | 3 | STANDARD | sonnet/high | 014,031 | ✗ (verification gate) | 🔲 |
| 041 | Retire Power Pages site + web-resource script | 3 | STANDARD | sonnet/high | 040 | ✗ (irreversible) | 🔲 |
| 042 | Rewrite external-access architecture + guides | 3 | MINIMAL | sonnet/high | 040 | ✓ Group C | 🔲 |
| 090 | Project wrap-up | 3 | FULL | sonnet/high | 040,041,042 | ✗ (final gate) | 🔲 |

## Dependency graph (critical path)

```
Phase 0:  001 → 002 ─────────────┐         003 ─┬─→ 010 → 011 → 012 ─┐
          003, 004 (Group A)     │              └─→ 013 ────────────┤
                                 │                                   ▼
Phase 1:                         │                          014 (deploy B2B) ─┐
                                 │                                             │
Phase 2:  020(←001) → 021 ─┐     022(←002) ─┐   024(B) ─┐                      │
          020 → 023(←004) ─┼─→ 027           025(←022,004,024) → 029           │
          020 → 028         │                026 ─┐                            │
                            └──────→ 030(←020,021,022,023,025,026,027) → 031 ──┤
                                                                                ▼
Phase 3:                                                        040 (parity, ←014,031)
                                                                 ├→ 041 (decommission)
                                                                 ├→ 042 (docs, Group C)
                                                                 └→ 090 (wrap-up, ←040,041,042)
```

**Critical path (longest chain)**: `003 → 010 → 011 → 012 → 014 → 040 → 041 → 090` (hosting/routing) intersecting `001 → 020 → 023 → 027 → 030 → 031 → 040`. The **auth chain** `001 → 020 → {021,023} → 025/027 → 030 → 031` is the deepest and gates Phase 3.

## Parallel Execution Plan

> **Note on parallelism**: This project is heavily **auth/security/deploy/irreversible**, and most Phase-2 BFF tasks touch overlapping files (`AuthorizationModule`, `ExternalAccessEndpoints`, the auth filter). Genuine parallel-safe groups are therefore limited. Waves below list what can run concurrently; everything else is sequential by dependency + file-overlap. **MAX 6 agents/wave.**

| Wave | Tasks | Prerequisite | Mode | goal-eligible |
|------|-------|--------------|------|---------------|
| 0.1 | **003, 004** (Group A, parallel) · 001 (ops, solo) | none | parallel + solo | **NO** — irreversible external provisioning |
| 0.2 | 002 | 001 | sequential | NO — auth-sensitive |
| 1.1 | 010 · 013 (different files, but both `parallel-safe:false` — run sequentially) | 003 | sequential | NO — ci-workflows / BFF |
| 1.2 | 011 | 010 | sequential | NO |
| 1.3 | 012 | 011 | sequential | NO |
| 1.4 | 014 (deploy on B2B) | 010,011,012,013 | sequential | NO — deploy |
| 2.pre | **024 (Group B)** · 026 (solo) | none | 024 parallel-safe; 026 solo | NO — BFF |
| 2.1 | 020 · 022 (different files; both `parallel-safe:false`) | 001 / 002 | sequential | NO — auth |
| 2.2 | 021 · 023 · 028 (different files; both `parallel-safe:false`) | 020 (+004) | sequential | NO — auth |
| 2.3 | 025 (provisioner) | 022,004,024 | sequential | NO — auth, xhigh |
| 2.4 | 027 · 029 | 021,023 / 025 | sequential | NO — auth |
| 2.5 | 030 (tests) | 020,021,022,023,025,026,027 | sequential | NO — TEST gate |
| 2.6 | 031 (deploy BFF) | 030 | sequential | NO — deploy |
| 3.1 | 040 (parity) | 014,031 | sequential | NO — verification gate |
| 3.2 | 041 · **042 (Group C)** | 040 | 042 parallel-safe; 041 solo | NO — irreversible / docs |
| 3.3 | 090 (wrap-up) | 040,041,042 | sequential | NO — closing gate |

**No wave is `/goal`-eligible** — the project is dominated by auth-sensitive, deploy-touching, and irreversible-provisioning work that should stop for human input at judgment boundaries (per task-create Step 3.85 exclusions). Execute wave-by-wave with per-task `task-execute`, running the Step 9.5 gates (code-review + adr-check) and a `dotnet build` between BFF waves.

### How to execute a parallel group
1. Confirm prerequisites are ✅.
2. For a parallel group (A/B/C), send ONE message with multiple `task-execute` Skill invocations (≤6).
3. `parallel-safe:false` tasks run sequentially (main-session for `.claude/`-touching, but none here touch `.claude/`).
4. `dotnet build src/server/api/Sprk.Bff.Api/` after any BFF-touching wave before dispatching the next.

## High-risk items
- **025 (provisioner)** + **027 (download authz-before-stream)** — the two `xhigh` correctness-critical tasks; 027's unauthorized→403/no-bytes negative test is the single highest-consequence property.
- **041 (decommission)** — irreversible; gated on 040 parity GREEN.
- **001/002 (CIAM tenant/app)** — external, ops-gated; block the entire auth chain.
