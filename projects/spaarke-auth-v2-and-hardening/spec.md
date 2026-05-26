# Spec — Spaarke Auth v2 + Hardening

> **Authoritative source**: [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md). This spec is a distillation; the audit doc is canonical and has full rationale.

## Problem

Three crosscutting weaknesses in the current auth surface, manifesting as the SpaarkeAi 401 bug (confirmed root cause: `IDX10223: token expired` after >80 min idle, App Insights `CopilotAuth` trace `2026-05-18T23:17:26Z`):

1. **Snapshot leaks**: ~12 client surfaces capture `accessToken: string` from React state and propagate via props. Provider's refresh + 401-retry mechanisms can't see these — they're closed over stale values.
2. **Strategy gaps**: `BridgeStrategy` and `XrmStrategy` in `@spaarke/auth` don't validate JWT `exp`. Proactive refresh exists but is opt-in (disabled by default).
3. **Environment leakage**: Plain-text `ClientSecret` in App Service config. `TenantId: "common"` hardcoded. Copilot audience UUID hardcoded. `/debug/*` endpoints anonymous in production. Webhook `clientState` not crypto-validated.

## Goal

Rebuild the client-side auth contract and harden the server-side auth surface so that:

- Stale-token bugs become **structurally impossible** (function-based API replaces value-based `token: string`)
- Per-tenant deployment is mechanical (4 client env vars + 8 server settings + Key Vault populated = deployable)
- MSAL configuration invariants (INV-1 through INV-7) are **preserved by literal code lift** — no regression to the hard-won fix that eliminated popup-every-tab behavior
- Security posture matches reasonable enterprise expectations (CSP, CAE, claims hardening, HMAC webhooks, managed identity, named auth schemes) — without going to Fort Knox (no DPoP, no multi-SP separation, no cross-tenant patterns)

## Scope

In scope (this project):

- `@spaarke/auth` library refactor — pluggable strategy pattern, `useAuth()` hook, function-based API, BroadcastChannel for invalidation events (not for token transport)
- All ~30 client consumers migrated (SpaarkeAi, AnalysisWorkspace, PlaybookBuilder, DocumentRelationshipViewer, SemanticSearch, External SPA, Office Add-ins, PCFs, shared libs)
- BFF server hardening — managed identity, named API key scheme, HMAC webhooks, audit middleware, rate limiting, debug-endpoint removal, secret rotation, appsettings template fixes
- Reasonable security defense-in-depth — CSP + Trusted Types, CAE, claims hardening (`oid`), step-up auth scaffolding, refresh-token rotation test
- CI hygiene — gitleaks, automated MSAL-binding regression test pack, Dependabot
- Documentation — `ADR-027`, pattern + constraint updates, new-environment setup guide

Out of scope (deferred to v3 or non-code):

- DPoP / sender-constrained tokens
- Multi-SP privilege separation within a single install
- HSM-backed key management
- Cryptographic audit log chaining (customer's Sentinel handles immutability)
- B2C anonymous portal
- Cross-tenant collaboration / federation
- Mobile clients
- Multi-tenant SaaS shared-instance patterns (per-tenant install is the model)
- Threat model document (SOC 2 prep — non-code)
- Paid penetration test (non-code; recommend scheduling before GA)
- SOC 2 audit engagement (non-code)

## Non-functional requirements

- **Performance**: 6-strategy cascade reduced to 3 strategies (MSAL + in-memory cache + BroadcastChannel for invalidation). First-request latency unchanged or improved.
- **Resilience**: 401 retry with cache clear + backoff (3 attempts, 500/1000/2000ms). Proactive refresh 5 min before JWT `exp`.
- **Security**: No `accessToken: string` in public APIs. Authentication boundary fully enforced at `authenticatedFetch`. ESLint rule prevents `Authorization: Bearer ${...}` literals outside that file.
- **Environment independence**: Adding a new customer-tenant deployment requires exactly 4 client env vars + 8 server settings + Key Vault populated. No code changes per tenant.
- **Regression**: INV-1 through INV-8 preserved. Acceptance test runs after every Workstream.
- **Cross-iframe sharing**: MSAL.localStorage (built-in). BroadcastChannel reserved for logout/invalidation messages.

## Success criteria

1. Idle-then-resume test: leave SpaarkeAi page idle >80 min, return, attempt chat — succeeds (current behavior: 401)
2. All ~30 client consumers use `useAuth()` + `authenticatedFetch` exclusively (no `accessToken: string` props)
3. MSAL regression test (clear all caches → close browser → reopen → no popup → `authority` contains tenant GUID) passes
4. `AzureAd__ClientSecret` and `AgentToken__ClientSecret` rotated and stored as Key Vault references in App Service
5. No `/debug/*` endpoints accessible in production builds
6. Webhook handlers (`/api/communications/incoming-webhook`, `/api/v1/emails/webhook-trigger`) require HMAC-SHA256 signatures
7. Graph app-only + Dataverse service identity use `DefaultAzureCredential` (managed identity)
8. CSP headers + Trusted Types enabled in production
9. Continuous Access Evaluation (CAE) enabled on Microsoft.Identity.Web
10. Audit middleware emits `oid`, `appid`, `obo`, `tenantId`, `correlationId` on every authenticated request
11. Gitleaks runs on every PR
12. `ADR-027: Spaarke Auth Architecture` written and approved
13. Customer-tenant deployment checklist documented in `docs/guides/auth-deployment-setup.md`

## Constraints

- **MSAL invariants are non-negotiable**: cacheLocation must be localStorage, storeAuthStateInCookie must be true, authority must contain the actual tenant GUID. INV-1..INV-7 from `spaarke-sso-binding.md`.
- **Bundling reality (INV-8)**: every `@spaarke/auth` consumer must be rebuilt + redeployed in lockstep. Single-PR migration to minimize INV-8 violations.
- **Pre-flight before Workstream A**: filename surgery + STOP banners + project CLAUDE.md prohibition land first to prevent agent/dev regression during the refactor.
- **Per-tenant deployment model**: don't add multi-tenant code patterns; don't add cross-customer features; treat each install as fully isolated.
- **No documentation-only or security-testing tasks in this scope**: ADR-027 and pattern updates ship as part of code work (Workstream F). Threat models, pen tests, SOC 2 work scheduled separately (audit §7.2).

## Dependencies

- Branch `work/spaarke-ai-platform-unification-r2` at commit `9d918a65` (where the audit doc lives) — this worktree is branched from it
- `@spaarke/auth` is consumed by ~30 bundles; rebuild discipline is critical
- Secret rotation requires brief App Service restart in dev environment
- Customer Conditional Access policies stay customer-owned (per-tenant model)
