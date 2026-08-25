# Implementation Plan — Spaarke Auth v4: Zero-Secret BFF Confidential Credential

> **Generated**: 2026-08-19 by `/project-pipeline` from [`spec.md`](spec.md)
> **Epic**: AUTH & SSO (#426) · **Risk**: HIGH (OBO fails closed) · **Rollout**: dev only
> **Branch**: `work/spaarke-auth-v4-dataverse-MI` · **Worktree**: `c:/code_files/spaarke-wt-spaarke-auth-v4-dataverse-MI`

---

## 1. Objective

Replace `BFF-API-ClientSecret` with a Managed-Identity-issued federated credential (MI-FIC) across every
BFF-identity confidential client — including the OBO paths three prior audits concluded could never be
secret-free — and leave CI-enforced forcing functions behind so the failure cannot recur.

## 2. Architecture Context

### Discovered resources

| Type | Resource | Relevance |
|---|---|---|
| **ADR** | [`ADR-028`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) | Canonical auth. **Amendment A4** (secret-free confidential credential) + **E-3** (transitional retained secret, time-boxed to THIS project) applied 2026-08-17 |
| **ADR** | `ADR-003` | Server seams + OBO |
| **ADR** | `ADR-008` | Endpoint filters — the row-level auth surface we must not regress |
| **ADR** | `ADR-009` | Redis-first caching — interacts with the FR-A2 CCA-cache change |
| **ADR** | `ADR-010` | DI minimalism — **1:1-interface ceiling is a live CI gate** (`ADR010_DITests.cs:164` = 153) |
| **ADR** | `ADR-027` | Subscription isolation / tenancy |
| **ADR** | `ADR-032` | Null-Object kill-switch (if any path stays feature-gated) |
| **ADR** | `ADR-038` | Testing strategy — 7 KEEP paths; bans DI-registration + ctor-null tests |
| **Constraint** | [`.claude/constraints/auth.md`](../../.claude/constraints/auth.md) | Corrected 2026-08-17; line 108 was the false premise |
| **Constraint** | [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) | Binding pre-merge checklist (BFF=Y) |
| **Pattern** | `.claude/patterns/auth/service-principal.md` | Updated to A4 |
| **Skill** | `adr-check`, `code-review`, `bff-deploy`, `push-to-github`, `test-diet` | Gates + deployment |
| **Script** | `scripts/Register-EntraAppRegistrations.ps1` | **FR-C4 target** — FIC automation home |
| **Script** | `scripts/Rotate-Secrets.ps1`, `Seed-ProductionKeyVault.ps1`, `Configure-ProductionAppSettings.ps1` | Secret-retirement surface |
| **Canonical impl** | `DataverseUserClient.cs:55-56,91` | Static CCA cache keyed `(tenant\|client)` — the pattern to copy |
| **Canonical impl** | `CiamGraphClientFactory.cs:129-133` | Secret-free confidential client already in production (certificate) |
| **Canonical impl** | `ContentSafetyTokenProvider.cs:55` | Existing MI path not currently selected (FR-E1) |
| **Canonical impl** | `MembershipJunctionUpdaterHost.cs:120` | Service Bus namespace + MI (FR-E2) |
| **Test** | `tests/Spaarke.ArchTests/LayerDependencyTests.cs:43` | FR-14 — forbids `Spaarke.Dataverse` → other Spaarke projects |
| **Test** | `tests/Spaarke.ArchTests/ADR010_DITests.cs:164` | 1:1-interface ceiling 153 — **must be raised to 154** |

### Live state (verified 2026-08-19)

App reg `SDAP-BFF-SPE-API` `1e40baad-…` · `AzureADMultipleOrgs` · 1 secret (exp 2027-12-19) · 0 certs ·
**2 FICs** (GitHub OIDC + `mi-bff-api-dev-assertion` created for this project).
App Service `spaarke-bff-dev` / `rg-spaarke-dev` · **UserAssigned only** `mi-bff-api-dev`
(clientId `5967251e-…`, principalId `9fd47efb-…`) · plan `spaarke-dev-plan` **P1v3** · **0 slots**.

### Hot-path declaration

`bff=Y` · `spaarkeai=N` · `ci-workflows=N` · `skill-directives=Y` · `root-claude-md=N`
⚠️ skill-directives overlaps `code-quality-and-assurance-r3`, `dataverse-access-unification-r1`,
`dotnet-10-upgrade-r1`. BFF overlaps 28 of 30 active projects (known systemic finding).

## 3. Phase Breakdown

| Phase | Name | Tasks | Gate |
|---|---|---|---|
| **0** | Spike — prove OBO under MI-FIC | 001–003 | Empirical proof, or pivot to certificate (Option B) |
| **1** | Prerequisites — defects that block the migration | 010–011 | Tests green, no behaviour change |
| **2** | Credential provider seam | 020–024 | Both credentials work; ordered fallback proven |
| **3** | Rollout, removal, FIC automation (dev only) | 030–033 | §6.1 OBO checklist green; soak before removal |
| ~~**4**~~ | ⏭️ ~~Power BI — UAMI-as-principal~~ **DEFERRED 2026-08-19 (owner)** | 040–042 | *Parked — Power BI not yet in use at Spaarke* |
| **5** | Group 2 non-Entra credentials (parallel) | 050–056 | Per-credential migration or documented reason |
| **6** | Forcing functions (anti-recurrence) | 060–063 | Build fails on a deliberately-introduced violation |
| **9** | Wrap-up | 090 | `/test-diet`, publish size, lessons-learned |

### Phase 0 — Spike (001–003)

Prerequisites are already provisioned: the dev MI-FIC exists, app-reg/UAMI are same-tenant, the App Service
runs UAMI-only, and the plan supports slots. What remains is empirical proof that **our** OBO chain works
under a FIC-authenticated client — the one thing documentation cannot settle.

- **001** Create the dev deployment slot (P1v3; zero exist). Assign `mi-bff-api-dev`. Needed for Phase 3 anyway.
- **002** Spike branch: `.WithClientAssertion` + `ManagedIdentityClientAssertion`; prove OBO → Graph/SPE **and**
  → Dataverse `user_impersonation`; long-running OBO; local-dev ordered fallback.
- **003** Record the credential decision with evidence. Pivot gate to Option B (KV certificate) if the spike fails.

### Phase 1 — Prerequisites (010–011)

Both are pre-existing defects, independently correct, and both de-risk the migration.

- **010** MI-flag gating defect (FR-A1) — `DataverseAccessDataSource.cs:53`, `DataverseWebApiClient.cs:42`.
- **011** DI lifetimes (FR-A2) — transient/scoped → singleton-cached CCAs; **record the ADR-009 decision**.

### Phase 2 — Credential provider seam (020–024)

- **020** `IClientAssertionProvider` in `Spaarke.Dataverse` + `ManagedIdentityAssertionProvider` in the BFF
  (FR-B1). **Raise `ADR010_DITests` ceiling 153→154 in the same PR.** Register via a feature module.
- **021** Ordered credential selection, config-driven (FR-B2) — the rollback mechanism; must be built (E4′).
- **022** Migrate the 6 BFF-identity confidential clients (FR-B3).
- **023** UAMI/app-reg conflation guard + test (FR-B4) — silent failure mode.
- **024** Relax the three config validators (FR-B5).

### Phase 3 — Rollout, removal, FIC automation (030–033)

- **030** `Register-EntraAppRegistrations.ps1` FIC extension (FR-C4). **Sequence early** —
  `customer-provisioning-orchestration-r1` task 130 (Wave G-3) is soft-blocked on it.
- **031** Deploy to slot; run the §6.1 OBO checklist + inbound-validation regression (FR-C1).
- **032** Flip via slot swap; soak (FR-C2). **No in-session flips.**
- **033** Remove the secret — app settings, Key Vault, the lowercase alias, 11 scripts, ~25 docs (FR-C3).

### Phase 4 — Power BI (040–042) — ⏭️ **DEFERRED (owner, 2026-08-19)**

> *"we can ignore Power BI if it is not readily available and defined — we are not yet using Power BI (it will
> be in the future but we can address the MI at that time)."*

`PowerBi:ClientSecret` stays. It is a separate credential from `BFF-API-ClientSecret` and no OBO path reads it,
so this does not weaken FR-C3 or the fail-closed surface. The deferral is made **visible** rather than silent:
task 060 allowlists Power BI *with this reason*, task 061 keeps both sites in the census as still-secret-backed,
and success criterion 10 is waived-with-reason at wrap-up.

Re-open specification (unchanged, still correct):

- **040** ⚠️ **Verify service-principal *profiles* work under a managed identity** — the gating unknown. **Still
  unanswered — deferral does not resolve it.**
- **041** Tenant setting + workspace grants (FR-D1); rework both services (FR-D2).
- **042** Remove `PowerBi:ClientSecret` (FR-D3).

### Phase 5 — Group 2 non-Entra credentials (050–056, parallel workstream)

Independent of the OBO migration. Two are near-immediate — the MI path already exists in-repo.

- **050** Content Safety (FR-E1) · **051** Service Bus + SAS rotation (FR-E2) · **052** Azure OpenAI E-2,
  custom-subdomain check first (FR-E3) · **053** AI Search ×2 (FR-E4) · **054** DocIntel ×3 (FR-E5) ·
  **055** `Analysis:PromptFlowKey` disposition (FR-E6) · **056** Bing KV-by-name hygiene (FR-E7).

### Phase 6 — Forcing functions (060–063)

- **060** ArchTest credential ban with E-1/E-3 allowlist + negative control (FR-F1).
- **061** Credential census — **source/assembly analysis, not DI resolution** (FR-F2).
- **062** Startup assertion, non-Development (FR-F3).
- **063** Pre-declare ArchTests MAINTAIN-class for `/test-diet` (FR-F0).

### Phase 9 — Wrap-up (090)

`/test-diet`, publish size vs the 44.96 MB baseline, lessons-learned, README status, INDEX.md update.

## 4. Critical Path

```
001 → 002 → 003 → 010 → 011 → 020 → 021 → 022 → 031 → 032 → 033 → 090
```

Longest dependency chain. Phase 4 (Power BI), Phase 5 (Group 2), Phase 6 (forcing functions) and task 030
(FIC automation) all branch off and rejoin at 090.

## 5. Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| — | 001 | none | Slot creation, serial |
| — | 002, 003 | 001 | Spike, serial (decision gate) |
| **A** | 010, 011 | 003 | Different files; both prerequisite defects |
| **B** | 020 | 011 | Serial — everything downstream depends on the seam |
| **C** | 021, 023, 024 | 020 | Independent surfaces of the provider |
| **D** | 022 | 021 | Serial — the migration itself; highest blast radius |
| **E** | **030** ⏩ | 020 | FIC automation. **Pulled forward (owner, 2026-08-19): run immediately after 020, ahead of 021/022** — provisioning Wave G-3 is soft-blocked on it. 040 removed (Power BI deferred) |
| **F** | 050, 051, 052, 053, 054, 055, 056 | none | Group 2 — independent of everything; can start any time |
| **G** | 060, 061, 062, 063 | 022 | Forcing functions — need the end-state shape to assert against |
| — | 031 → 032 → 033 | 022 | Serial by construction (deploy → flip → soak → remove) |
| — | 090 | all | Wrap-up |

**Max concurrency: 6 agents per wave.** Group F is 7 tasks — split 6 + 1, or run as two waves.

⚠️ **`parallel-safe: false`** for any task touching `.claude/` (skill directives) — main-session only, per the
sub-agent write boundary. Applies to 033 (constraint/pattern updates) and 063.

## 6. Risk Register

| # | Risk | Mitigation |
|---|---|---|
| R1 | **OBO fails closed** — breakage locks out every user immediately and totally | Staged slot rollout; secret retained as ordered fallback until 033; soak before removal; no in-session flips |
| R2 | Phase 0 spike fails — MI-FIC + our OBO chain incompatible | Pivot to Option B (KV certificate), already proven in-repo by `CiamGraphClientFactory`. The provider seam is credential-agnostic, so Phase 2 work is not wasted |
| R3 | **Silent misconfiguration** — wrong FIC issuer/subject/audience creates cleanly, fails only at exchange | 023 conflation guard + test; 030 verifies by performing an actual exchange, not by a successful create |
| ~~R4~~ | Power BI SP profiles unsupported under a managed identity | **Retired from this project 2026-08-19** — Phase 4 deferred. The risk is not resolved, it is *deferred with the work*: 040 must verify before 041/042 are ever attempted |
| R5 | 46 test fixtures break | Nullable provider parameter with null default; NFR-04 |
| R6 | `ADR010_DITests` ceiling failure reddens CI | Raise 153→154 in the same PR as 020 (already an acceptance criterion) |
| R7 | Forcing functions deleted by `/test-diet` at wrap-up | 063 pre-declares them MAINTAIN-class |
| R8 | Cross-project collision on `scripts/` and `.claude/` | Coordination notes sent to both sibling projects; `/conflict-check` per PR; `.claude/` tasks are main-session-only |
| R9 | Provisioning's Model 2 FIC issuer may be cross-tenant | Raised back in `PROVISIONING-CHANGE-REQUEST.md` §9.2; must settle before their Wave G-3 |

## 7. Cross-Project Coordination

- **`customer-provisioning-orchestration-r1`** — change request **ACCEPTED + APPLIED** 2026-08-19. They own
  Model 1/Model 2 app-reg shape, I6, FR-39 pluggability. **We owe them task 030 before their Wave G-3.**
  One item raised back (§9.2 Model 2 tenancy).
- **`dataverse-access-unification-r1`** — parallel, not a prerequisite. Four-file interlock; whoever touches
  `DataverseServiceClientImpl.cs` second inherits the other's shape.
- **Open PR #293** (`Azure.Identity` 1.17.1→1.21.0) — relevant to `ClientAssertionCredential`. Coordinate.

## 8. Estimated Effort

| Phase | Tasks | Estimate |
|---|---|---|
| 0 Spike | 3 | 1–2 days |
| 1 Prerequisites | 2 | 1 day |
| 2 Provider seam | 5 | 3–4 days |
| 3 Rollout + FIC automation | 4 | 2–3 days + soak |
| ~~4 Power BI~~ | ~~3~~ | ⏭️ **deferred — 0 days in this project** |
| 5 Group 2 | 7 | 2–3 days (parallel) |
| 6 Forcing functions | 4 | 1–2 days |
| 9 Wrap-up | 1 | 0.5 day |

**Total: 29 tasks authored — 26 active (~11–15 working days), 3 deferred (Phase 4 Power BI)** excluding soak. Note the original design's ~350–550 LOC estimate is
**understated** — it assumed the declarative adoption ruled out by E4′.

## 9. References

- [`spec.md`](spec.md) — the 23 FRs + 6 NFRs this plan decomposes
- [`design.md`](design.md) — problem, options, ADR tension record
- [`notes/PHASE-0-LIVE-VERIFICATION.md`](notes/PHASE-0-LIVE-VERIFICATION.md) — live state + E4′ correction
- [`notes/CREDENTIAL-INVENTORY.md`](notes/CREDENTIAL-INVENTORY.md) — `file:line` audit of every auth site
- [`notes/PROVISIONING-CHANGE-REQUEST.md`](notes/PROVISIONING-CHANGE-REQUEST.md) + [response](notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md)
- [`notes/COORDINATION-DATAVERSE-ACCESS-UNIFICATION.md`](notes/COORDINATION-DATAVERSE-ACCESS-UNIFICATION.md)
