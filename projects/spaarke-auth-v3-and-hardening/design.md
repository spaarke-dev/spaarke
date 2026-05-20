# Spaarke Auth v3 + Hardening — Project Design

> **Project**: `spaarke-auth-v3-and-hardening`
> **Status**: Design / Scope-locked (awaiting tasks generation)
> **Created**: 2026-05-19
> **Predecessor**: [`spaarke-auth-v2-and-hardening`](../spaarke-auth-v2-and-hardening/) (merged to master 2026-05-19)
> **Branch (proposed)**: `work/spaarke-auth-v3-and-hardening`
> **Audience**: Project owner, AI agents executing tasks, future incident responders

---

## 1. Executive Summary

Spaarke Auth v2 (completed 2026-05-19) delivered the **function-based client contract**, **MSAL invariants enforcement**, **server-side managed-identity migration** (Graph + Dataverse outbound), **HMAC-validated webhooks**, **named API-key auth schemes**, **PostConfigure idempotency**, **audit logging middleware**, and **rate limiting**. It also delivered consumer migration (6 PCFs, 13 Code Pages, 3 standalone JS), Office Add-ins consolidation, and full canonical documentation (ADR-028, sso-binding pattern, constraints, deployment guide).

**Auth v3 picks up the explicitly-deferred work** plus the **auth-related carryovers** from v2:

- **Phase D — Reasonable security hardening**: CSP + Trusted Types, Continuous Access Evaluation (CAE), claims hardening (`oid` as canonical identity), step-up authentication scaffolding, refresh-token rotation integration test
- **Phase E — CI hygiene**: gitleaks GitHub Action, auth regression Playwright pack, Dependabot for npm + nuget
- **Phase G — Secret + KV consolidation** (formerly v2 task 040): rotate `AzureAd__ClientSecret` + `AgentToken__ClientSecret`, convert remaining plain-text webhook signing keys to Key Vault references, coordinate App Service restart
- **Phase H — Auth-related residual cleanup** (carryover from v2): D-AUTH-7 exception-site cleanup, dead `accessToken: string` props, deprecated authService.ts removal

Scope discipline: anything **not auth-related** is explicitly excluded (see §3).

---

## 2. Scope — Phases included

### 2.1 Phase D — Reasonable security hardening (from v2 task 060–064)

| Task | Title | Scope |
|---|---|---|
| **D-1** (was 060) | Add CSP + Trusted Types middleware on BFF | Strict policy: `script-src 'self'`, no inline, no eval. Returns CSP headers on all responses. Must coexist with MSAL iframes — test the SSO binding ritual after deploy. |
| **D-2** (was 061) | Enable Continuous Access Evaluation (CAE) on Microsoft.Identity.Web | Configure middleware to honor CAE revocation events. Wires up the server-side logout endpoint deferred from v2 task 014 (slim scope). |
| **D-3** (was 062) | Identity claims hardening | Grep + replace `email`/`upn` used as canonical identity with `oid` (Azure AD object ID). Audit log writes use `oid` everywhere. |
| **D-4** (was 063) | Step-up auth scaffolding | `[RequiresStepUp]` attribute + middleware. Apply to 2–3 sensitive operations as proof points (TBD: deletions, BU/Account provisioning, secret-rotation endpoints). Returns 401 with claims challenge on tagged endpoints. |
| **D-5** (was 064) | Refresh-token rotation integration test | Confirms MSAL issues a new RT on each refresh. End-to-end test against a real BFF instance, not a mock. |

**Phase D gate**: CSP headers present in all responses. CAE configured + revocation events honored. Audit log writes use `oid` everywhere. Step-up middleware returns 401 with claims challenge on tagged endpoints. **MSAL browser SSO regression test PASSES** (CSP can break MSAL iframes; CAE forces re-auth — both client-facing).

### 2.2 Phase E — CI hygiene (from v2 task 070–072)

| Task | Title | Scope |
|---|---|---|
| **E-1** (was 070) | Gitleaks GitHub Action workflow | Blocks merge on any detected secret. Configure ignorelist for `**/.env.example` and known-false-positive patterns. |
| **E-2** (was 071) | Auth regression Playwright pack | Playwright script that runs the `spaarke-sso-binding` ritual against a synthetic consumer. Runs on every PR to catch INV-1..INV-8 violations. Replaces the manual cadence documented in [`spaarke-auth-v2-and-hardening/CLAUDE.md`](../spaarke-auth-v2-and-hardening/CLAUDE.md). |
| **E-3** (was 072) | Dependabot config for npm + nuget | Opens PRs for security updates. Scoped to `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` + `src/client/shared/Spaarke.Auth/package.json` + `src/client/office-addins/package.json` + the PCFs and Code Pages. Coordinates with `sdap-bff-api-remediation-fix` for the BFF package management. |

**Phase E gate**: Every PR runs gitleaks + auth regression test before merge. Dependabot opens PRs for security updates within 24h of CVE publication.

### 2.3 Phase G — Secret + Key Vault consolidation (from v2 task 040)

| Task | Title | Scope |
|---|---|---|
| **G-1** (was 040) | Rotate `AzureAd__ClientSecret` (BFF app reg) | Issue new client secret in the BFF app registration. Store in Key Vault as `BFF-API-ClientSecret` (already canonical). Coordinate App Service restart. |
| **G-2** (was part of 040) | Rotate `AgentToken__ClientSecret` (Copilot agent secret) | Same pattern — new secret → Key Vault → App Service config → restart. |
| **G-3** | Convert plain-text webhook signing keys to Key Vault references | Both `Communication__WebhookSigningKey` and `EmailProcessing__WebhookSigningKey` currently shipped as plain-text app settings (v2 deferred per `feedback-question-urgency-for-dev-only-infra-tasks` memory). Move to Key Vault references using the same secret-URI pattern as `BFF-API-ClientSecret`. |
| **G-4** | Remove `Dataverse-ClientSecret` from Key Vault | After v2 Phase C, Dataverse uses MI via `DefaultAzureCredential`. The legacy `Dataverse-ClientSecret` is unused — confirm with a 7-day no-access audit on the Key Vault secret, then remove. |

**Phase G gate**: No plain-text auth secrets in App Service config. All auth-related secrets accessed via Key Vault references. MI's `Key Vault Secrets User` role still sufficient (no role escalation). Smoke tests pass after rotation (OBO endpoint round-trip, Copilot token validation).

**Note**: v2 explicitly deferred this work per user direction ("dev env, no external users, no production data — low blast radius"). v3 should revisit at the **right time** — typically tied to a production-readiness gate or the first external-user onboarding milestone.

### 2.4 Phase H — Auth-related residual cleanup (carryover from v2)

These are auth touchpoints flagged during v2 audit but deferred to keep v2 scope focused. Each is small but they accumulate as latent ban-rule violations.

| Task | Title | Scope |
|---|---|---|
| **H-1** | Remove dead `accessToken: string` props in `SpeDocumentViewer` | PCF still has props that were never wired into the v2 contract. Remove. Rebuild PCF. Deploy to both envs. |
| **H-2** | Remove `bffTokenProvider` prop from `DocumentUploadWizard` wizard tree | Still threaded through the wizard component hierarchy but no longer used. Cleanup pass. |
| **H-3** | Delete deprecated `PlaybookBuilder/services/authService.ts` | Zero consumers per v2 audit. Safe delete + verify no breakage. |
| **H-4** | Migrate remaining D-AUTH-7 exception sites in `Spaarke.UI.Components/src/services/document-upload/*` and `useAiSummary.ts` | These build raw Bearer headers in implementation code. Migrate to `authenticatedFetch` per v2 canonical contract. Document any sites that cannot be migrated and the justification. |
| **H-5** | Migrate Office Add-ins to use `@spaarke/auth` `useAuth()` (full migration, beyond v2 081/082 staleness fix) | v2 task 080 added `OfficeNaaStrategy` to `@spaarke/auth`. 081/082 fixed *staleness* via per-call `getAccessToken()`. **Full migration** (Office Add-ins consume `useAuth()` + drop their own `NaaAuthService.ts`) was explicitly deferred — out of v2 B4 scope. v3 H-5 closes the gap. |
| **H-6** | Spaarke-demo environment BFF Application User + Exchange policy parity | If BFF starts calling spaarke-demo's Dataverse, add MI as Dataverse Application User. If demo will use Email/Communication modules, set up Exchange ApplicationAccessPolicy for the demo MI (per [`auth-deployment-setup.md §7`](../../docs/guides/auth-deployment-setup.md#7-exchange-online--applicationaccesspolicy-for-mailbox-access)). |

**Phase H gate**: Zero D-AUTH-7 exception sites except the 4 known-justified types (SSE/XHR/Dataverse-direct/third-party-SDK) — each documented with justification comment. Office Add-ins use `useAuth()`. No dead auth props in any consumer.

---

## 3. Explicitly Out of Scope

To prevent scope creep on a project that touches the heart of the auth system:

### 3.1 NOT included (per user direction 2026-05-19)

- ❌ Non-auth client secret rotations (anything that isn't `AzureAd__ClientSecret`, `AgentToken__ClientSecret`, webhook signing keys, or related auth secrets)
- ❌ The 3 pre-existing wizard payload bugs from v2 task 031 (CreateMatter N:N, CreateProject + CreateWorkAssignment `createRecord`) — data-layer issues, not auth
- ❌ SemanticSearch Code Page `@lexical/react` webpack issue — build tooling, not auth
- ❌ 4 PCFs missing eslint devDep (carryover from v2) — tooling cleanup
- ❌ `Deploy-SpaarkeAi.ps1` CREATE branch bug — deploy tooling
- ❌ Duplicate `sprk_DocumentOperations.js` cleanup — codebase hygiene
- ❌ `@spaarke/ui-components` jest config can't run tests — test tooling
- ❌ `SpaarkeAi` blob ERR_FILE_NOT_FOUND — separate UI bug
- ❌ BFF publish-size debt (multi-platform native runtimes, sourcemap leakage) — belongs to `sdap-bff-api-remediation-fix`
- ❌ AI Search indexing pipeline silent-failure bug — belongs to `ai-search-indexing-fix`
- ❌ Secure Project privilege filtering (write side) — belongs to `sdap-secure-project-module-r2`

### 3.2 Architectural decisions firmly OUT of scope

- ❌ Multi-tenant SaaS auth (Spaarke is per-tenant per ADR-028 D-AUTH-5)
- ❌ DPoP, multi-SP privilege separation, HSM-backed keys, cryptographic audit chaining (D-AUTH-8: deferred from v2 audit)
- ❌ B2C portal auth (separate workstream)
- ❌ Mobile-client auth (no mobile clients today)
- ❌ Removing OBO (still required by spec for middle-tier confidential credential — Phase C kept `BFF-API-ClientSecret` for this reason)

---

## 4. Inputs from v2

### 4.1 Canonical references

| Document | What it provides |
|---|---|
| [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) | Canonical architectural decisions — MUST be honored in v3 |
| [`.claude/patterns/auth/spaarke-sso-binding.md`](../../.claude/patterns/auth/spaarke-sso-binding.md) | INV-1..INV-8 MSAL invariants — MUST NOT be regressed |
| [`.claude/constraints/auth.md`](../../.claude/constraints/auth.md) | Client function-based contract + Server Phase C rules |
| [`docs/guides/auth-deployment-setup.md`](../../docs/guides/auth-deployment-setup.md) | Operator runbook (10 sections; §7 Exchange policy is critical for any new env) |
| [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md) | Pre-v2 audit; some findings still apply (D-AUTH-7 inventory) |

### 4.2 D-AUTH key decisions inherited (binding for v3)

From v2 [`CLAUDE.md`](../spaarke-auth-v2-and-hardening/CLAUDE.md):

- **D-AUTH-1**: Function-based contract is the only public client API surface. `accessToken: string` does not appear in any component prop, hook return, or context value.
- **D-AUTH-2**: `@spaarke/auth` uses pluggable strategies (`BrowserMsalStrategy`, `OfficeNaaStrategy`).
- **D-AUTH-3**: MSAL.localStorage for cross-tab/iframe sharing; `BroadcastChannel` for invalidation messages only.
- **D-AUTH-4**: MSAL config invariants preserved literally — no deviation.
- **D-AUTH-5**: Per-tenant deployment threat model — tokens never cross customer boundaries.
- **D-AUTH-6**: Managed identity everywhere for server outbound (Phase C completed Graph + Dataverse; v3 G-3 extends to webhook signing keys via KV refs).
- **D-AUTH-7**: TypeScript branded types are NOT the enforcement mechanism — `authenticatedFetch` is the runtime boundary. ESLint rule bans `Authorization: \`Bearer ${...}\`` outside `authenticatedFetch.ts`. v3 H-4 closes remaining exception sites.
- **D-AUTH-8**: DPoP / multi-SP / HSM / audit chaining / B2C / mobile remain deferred (out of v3 scope per §3.2).

### 4.3 Verified-good v2 state (don't re-investigate)

- Function-based contract live across 6 PCFs + 13 Code Pages + 3 standalone JS — deployed dual-env (spaarkedev1 + spaarke-demo)
- BFF deployed to `spe-api-dev-67e2xz` with all Phase C hardening live + smoke-tested
- Managed identity active for Graph (`Graph__ManagedIdentity__Enabled=true`) + Dataverse (`DefaultAzureCredential` cascade across 13 files)
- Exchange ApplicationAccessPolicy in place for BFF MI (mailbox access via Graph app-only working — `Test-ApplicationAccessPolicy = Granted`)
- HMAC-SHA256 webhook validation live (Communication + Email)
- Named API key schemes registered (BuilderAdmin + Rag)
- Audit middleware emitting `oid`, `appid`, `obo`, `tenantId`, `correlationId`
- Rate limiting policies: 3 new policies (`webhook-graph`, `api-key-admin`, `api-key-rag`)

---

## 5. Project Structure & Conventions

### 5.1 Standard project files (TBD on project init)

```
projects/spaarke-auth-v3-and-hardening/
├── README.md                # Project overview + graduation criteria
├── design.md                # THIS DOCUMENT — scope + carryovers
├── CLAUDE.md                # AI agent context (constraints, ADRs, must/must-not rules)
├── plan.md                  # Implementation plan + WBS
├── current-task.md          # Active task state (for context recovery)
└── tasks/
    ├── TASK-INDEX.md        # Task tracker
    └── *.poml               # Per-task POML files (one per Phase D/E/G/H item)
```

These get generated on `/project-pipeline projects/spaarke-auth-v3-and-hardening`.

### 5.2 Task execution conventions

Same as v2:
- Tasks executed via `task-execute` skill (mandatory per root CLAUDE.md §4)
- Parallel-safe tasks within a phase: ONE message, MULTIPLE Skill invocations
- Sub-agents cannot write to `.claude/` (root CLAUDE.md §3) — tasks touching those paths are main-session-only
- Quality gates: Step 9.5 runs `code-review` + `adr-check` for FULL-rigor tasks

### 5.3 Regression test cadence (risk-tiered, inherited from v2)

Run the MSAL binding regression test (per [`spaarke-sso-binding.md`](../../.claude/patterns/auth/spaarke-sso-binding.md#verification-after-changes)) **only when a workstream can plausibly affect browser SSO**:

| Phase | Touches browser SSO? | MSAL regression required? |
|---|---|---|
| Phase D | ✅ (CSP can block iframes; CAE forces re-auth) | YES — after each Phase D deploy |
| Phase E | ❌ (CI + dependency tooling) | NO (but E-2 Playwright pack automates this going forward) |
| Phase G | ⚠️ Indirect (secret rotation + App Service restart) | YES — after each rotation; OBO smoke check sufficient if no client changes |
| Phase H | ✅ (consumer code changes) | YES — after each consumer rebuild + redeploy |

**After v3 E-2 ships**, this test runs automatically in CI on every PR. Manual cadence remains the interim contract.

---

## 6. Phase Sequencing & Dependencies

```
v2 close-out
    │
    ├── Phase G (Secret + KV consolidation)
    │     └── independent; can start anytime once v3 kicks off
    │
    ├── Phase D (Security hardening) — all 5 tasks parallel-safe within phase
    │     └── depends on Phase A (already done in v2)
    │
    ├── Phase H (Auth residual cleanup)
    │     ├── H-1, H-2, H-3, H-4: independent — can parallelize
    │     └── H-5 depends on Phase A + v2 080 (already done)
    │     └── H-6 depends on operational decision to deploy demo BFF
    │
    └── Phase E (CI hygiene)
          └── E-2 Playwright depends on Phase D done (or at least Phase D in dev) for stability
          └── E-1, E-3 independent — can start anytime
```

**Recommended execution order**:

1. **Wave 1** (parallel): Phase E-1 (gitleaks), E-3 (Dependabot) — no dependencies, low risk, fast wins
2. **Wave 2** (parallel): Phase H-1, H-2, H-3, H-4 — cleanup; small, isolated
3. **Wave 3** (parallel): Phase D-1, D-2, D-3, D-4, D-5 — security middleware; coordinate deploys + browser regression test after each
4. **Wave 4** (sequential): Phase G-1 → G-2 → G-3 → G-4 — secret rotations should not stack; one at a time with smoke check between
5. **Wave 5**: Phase H-5 (Office Add-ins full migration) — depends on Phase D stability
6. **Wave 6**: Phase E-2 (Playwright pack) — captures all of Phase D's CSP/CAE flows
7. **Wave 7**: Phase H-6 (demo env parity) — when operations decides

---

## 7. Estimated Effort

| Phase | Tasks | Active work | Calendar |
|---|---|---|---|
| Phase D — Security hardening | 5 (parallel) | ~11h | 1–2 weeks (deploys + browser regression checks pace this) |
| Phase E — CI hygiene | 3 (mostly parallel) | ~7h | 1 week |
| Phase G — Secret + KV | 4 (sequential) | ~4h | 1 week (mostly waiting for smoke checks between rotations) |
| Phase H — Residual cleanup | 6 (parallel + Office Add-ins migration) | ~10h | 1–2 weeks |
| **Total** | **18 tasks** | **~32h** | **~4–6 weeks calendar** |

Calendar time is dominated by:
- Per-deploy smoke + regression-test windows (Phase D + G)
- Sequencing rule (one secret rotation at a time)
- Office Add-ins host-app manual verification (Phase H-5)

---

## 8. Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| CSP middleware (D-1) breaks MSAL iframes | Medium | High | Strict mode in dev first; full SSO binding ritual after each deploy; rollback = git revert + redeploy |
| CAE (D-2) forces unexpected re-auth in a host that doesn't support it | Low | Medium | Test in Power Apps, Code Page, Office Add-in hosts before promoting |
| Claims hardening (D-3) misses a `email`/`upn` usage that breaks downstream | Medium | Medium | Compile-time grep is exhaustive; runtime check via App Insights `oid` claim emission post-deploy |
| Refresh-token integration test (D-5) is flaky against real MSAL | Medium | Low | Isolate to a single CI job; mark as `[Trait("Category", "Flaky")]` if it can't be stabilized |
| Secret rotation (G-1) breaks OBO during the window between new-secret-issued and App-Service-restart | Low | High | Issue new secret with overlap (both old + new valid); deploy + restart; verify; then revoke old |
| Webhook signing key rotation (G-3) breaks active Microsoft Graph subscriptions | Medium | Medium | Coordinate with the subscription renewal pattern; sequence: KV ref → restart → re-register subscription with new key |
| Office Add-ins full migration (H-5) breaks host bindings | Medium | High | Deploy to test SWA env first; verify in Outlook web + Word web; promote to prod-equivalent only after manual smoke pass |
| Gitleaks (E-1) blocks legitimate PRs (false positives) | Low | Low | Configurable ignorelist; first 2 weeks tune as new false positives surface |
| Phase E-2 Playwright pack is brittle (UI-driven tests) | Medium | Low | Run against a stable synthetic consumer, not a production-equivalent SPA; use Playwright's auto-wait + retries |
| Scope creep ("while we're at it…") | High (any project) | Medium | §3 Out of Scope is binding; new asks become separate projects |

---

## 9. Success Criteria

The project is complete when:

1. ✅ All Phase D, E, G, H tasks are deployed to dev, baked, and verified
2. ✅ Production deploy completes for any phase that touches production (per ops cadence)
3. ✅ MSAL browser regression test passes after every consumer-affecting deploy
4. ✅ CI pipeline runs gitleaks + Playwright auth regression on every PR
5. ✅ Dependabot is opening PRs for security updates
6. ✅ Zero plain-text auth secrets in App Service config (all via Key Vault references)
7. ✅ No `accessToken: string` props anywhere in `src/client/` (D-AUTH-1 enforced)
8. ✅ Audit middleware emits `oid` as canonical identity in all log entries
9. ✅ ADR-028 updated (or ADR-029 added) to capture v3 decisions (especially Phase D + G)
10. ✅ `auth-deployment-setup.md` updated with any new operator steps from Phase G
11. ✅ A `LESSONS-LEARNED.md` captures gotchas for the next auth project

---

## 10. Cross-Project Dependencies

| Other project | Interaction |
|---|---|
| `sdap-bff-api-remediation-fix` | Phase E-3 (Dependabot) must coordinate scope with the BFF publish-debt project's package-management approach. If BFF debt project introduces version pinning or `RuntimeIdentifier` constraints, Dependabot config must respect them. |
| `sdap-secure-project-module-r2` | Phase G (KV rotations) must not break R2's external-caller authorization path (`ExternalCallerAuthorizationFilter`) which reads claims via `Microsoft.Identity.Web`. Smoke-test external SPA after each Phase G rotation. |
| `ai-search-indexing-fix` | Phase H Office Add-ins migration (H-5) may surface adjacent SendToIndex/Office-driven flows that interact with the indexing pipeline. Coordinate via shared status reviews. |

---

## 11. Open Questions (resolve before kickoff)

1. **Production environment**: Phase G requires production deploy at some point. Who owns prod? When is the right time? (v2 G was deferred specifically because dev had no external users.)
2. **Phase E-2 synthetic consumer**: which app gets used for the Playwright regression — a new minimal test SPA, or one of the existing PCFs / Code Pages?
3. **Phase D-4 step-up endpoints**: which 2–3 operations get tagged as `[RequiresStepUp]` for proof-of-concept? Recommended candidates: BU/Account provisioning (`ProvisionMatterEndpoint`, `ProvisionProjectEndpoint`); secret-rotation endpoints; admin-only deletions.
4. **Phase H-5 host coverage**: which Office hosts must verify before declaring done — Outlook web only, or also Outlook desktop + Word web + Word desktop?
5. **Phase G coordination with R2**: should Phase G wait for R2 stabilization (to avoid disrupting external caller paths during R2 rollout)?
6. **Phase E-1 gitleaks ignorelist**: do we have existing patterns we already know to ignore (e.g., test fixtures, example tokens)?

---

## 12. References

- v2 project: [`projects/spaarke-auth-v2-and-hardening/`](../spaarke-auth-v2-and-hardening/)
- v2 task index: [`projects/spaarke-auth-v2-and-hardening/tasks/TASK-INDEX.md`](../spaarke-auth-v2-and-hardening/tasks/TASK-INDEX.md)
- v2 context recovery: [`projects/spaarke-auth-v2-and-hardening/current-task.md`](../spaarke-auth-v2-and-hardening/current-task.md)
- Canonical ADR: [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md)
- Pre-v2 audit: [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md)
- Deployment guide: [`docs/guides/auth-deployment-setup.md`](../../docs/guides/auth-deployment-setup.md)
- Related projects:
  - [`sdap-bff-api-remediation-fix/approach.md`](../sdap-bff-api-remediation-fix/approach.md)
  - [`ai-search-indexing-fix/ISSUE.md`](../ai-search-indexing-fix/ISSUE.md)
  - [`sdap-secure-project-module-r2/`](../sdap-secure-project-module-r2/)

---

## 13. Next Steps to Initiate

Once this design is approved:

1. Create remaining standard project files (README.md, CLAUDE.md, plan.md, current-task.md, tasks/TASK-INDEX.md)
2. Run `/project-pipeline projects/spaarke-auth-v3-and-hardening` to generate the 18 tasks (organized by Phase D/E/G/H)
3. Resolve §11 Open Questions with the owner
4. Begin Wave 1 (Phase E-1 + E-3 in parallel — fast wins, zero risk)

---

*Design awaiting owner approval. Tasks generation deferred until §11 Open Questions are resolved.*
