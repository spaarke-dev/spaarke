# Spaarke Auth v2 + Hardening

> **Status**: In Progress
> **Branch**: `work/spaarke-auth-v2-and-hardening`
> **Worktree**: `c:/code_files/spaarke-wt-spaarke-auth-v2-and-hardening`
> **Created**: 2026-05-18
> **Type**: Cross-cutting refactor + security hardening
> **Tasks**: 0/49 complete across 8 phases (~68 hours estimated)

## Overview

System-wide rebuild of the client-side authentication architecture and hardening of the BFF-side auth surface. Triggered by intermittent 401 errors in the SpaarkeAi Code Page, where the root cause (confirmed via App Insights `CopilotAuth` trace — `IDX10223: token expired`) revealed a systemic snapshot-based token propagation pattern across ~30 client surfaces.

The work is **NOT** a "fix the 401" project — that would be a patch. This is a foundational architectural rebuild that eliminates the entire class of stale-token bugs structurally, plus the security defense-in-depth that comes with it.

## Authoritative design

[`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md) is the canonical design document. It captures:

- §1-3 — Current state inventory + findings
- §4 — Target architecture (function-based contract, pluggable strategies, MSAL invariants preservation, per-tenant deployment model)
- §5 — Final scope (6 workstreams, ~68 hours)
- §6 — Explicitly out of scope (DPoP, multi-SP, etc.)
- §7 — SOC 2 / enterprise review readiness map
- §8 — Pre-flight conflict map + three-layer enforcement strategy

`ADR-027: Spaarke Auth Architecture` will be produced as part of Workstream F (Phase 7) and will supersede this audit doc as canonical.

## Why this project exists

Three crosscutting weaknesses found in the audit, none of which can be fixed by a patch:

1. **Snapshot leaks** — 12+ client surfaces capture `accessToken: string` from React state and propagate it via props. The provider's refresh + retry mechanisms can't see these — they're closed over stale values.
2. **Strategy gaps** — `BridgeStrategy` and `XrmStrategy` in `@spaarke/auth` don't validate JWT `exp`. Proactive refresh exists but is opt-in.
3. **Environment leakage** — Plain-text client secrets in App Service config; hardcoded `TenantId: "common"`; hardcoded Copilot UUID; debug endpoints anonymous in production; webhook clientState not crypto-validated.

## Key deliverables

### Code

- `@spaarke/auth` v2: pluggable strategy pattern, `useAuth()` hook, function-based contract (no `accessToken: string` in public API), MSAL config invariants (INV-1..INV-7) preserved by literal lift, BroadcastChannel for invalidation events only
- All ~30 client consumers migrated (SpaarkeAi, AnalysisWorkspace, PlaybookBuilder, DocumentRelationshipViewer, SemanticSearch, External SPA, Office Add-ins, PCFs, shared libs)
- BFF server hardening: managed identity everywhere, named API key auth scheme, HMAC webhook signatures, audit middleware, rate limiting, debug endpoint removal, secret rotation to Key Vault
- Reasonable security: CSP + Trusted Types, Continuous Access Evaluation enabled, claims hardening (`oid` everywhere), step-up auth scaffolding
- CI hygiene: gitleaks, automated regression test pack, Dependabot

### Documentation

- `ADR-027: Spaarke Auth Architecture` (replaces or amends prior auth ADRs)
- Updated `.claude/patterns/auth/spaarke-sso-binding.md` (MSAL invariants stay; cascade section retired)
- Updated `.claude/constraints/auth.md` (function-based contract is binding rule)
- New `docs/guides/auth-deployment-setup.md` (new-environment setup checklist: 4 client env vars + 8 server settings + Key Vault secret names)

## Graduation criteria

1. No `accessToken: string` or `token: string` appears in any public API surface
2. All ~30 consumers use `useAuth()` hook + `authenticatedFetch` exclusively
3. MSAL invariants INV-1..INV-7 verified preserved (regression test passes)
4. No plain-text secrets in App Service config; all in Key Vault references
5. No `/debug/*` endpoints in production builds
6. Audit middleware emits standard identity fields on every authenticated request
7. `ADR-027` written and approved
8. Customer security review checklist (audit §7.3) maps to delivered evidence
9. Idle-then-resume test: leave SpaarkeAi page idle >80 min, return, attempt chat — succeeds (the original 401 symptom)

## Quick Links

- [Specification](spec.md)
- [Implementation Plan](plan.md)
- [Design (audit doc pointer)](design.md)
- [Architecture](architecture.md)
- [Task Index](tasks/TASK-INDEX.md)
- [AI Context](CLAUDE.md)
- [Authoritative Audit](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md)
