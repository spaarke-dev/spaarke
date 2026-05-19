# CLAUDE.md - Spaarke Auth v2 + Hardening

> **Project**: spaarke-auth-v2-and-hardening
> **Type**: Cross-cutting architectural refactor + security hardening
> **Branch**: `work/spaarke-auth-v2-and-hardening`
> **Worktree**: `c:/code_files/spaarke-wt-spaarke-auth-v2-and-hardening`

---

## 🚨 ACTIVE AUTH V2 REFACTOR — DO NOT REGRESS

This project is the **Spaarke Auth v2 + Hardening** refactor. Many existing auth patterns in the codebase are being replaced. To prevent agents from following stale guidance during the migration, the following rules apply throughout this project.

**Canonical design**: [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md). `ADR-027` will become canonical when Workstream F1 completes.

### ❌ MUST NOT during this refactor

- **MUST NOT** add `accessToken: string` or `token: string` props to any component, hook return, or context value
- **MUST NOT** write `fetch(url, { headers: { Authorization: \`Bearer ${...}\` }})` — use `authenticatedFetch` instead
- **MUST NOT** reference `BridgeStrategy`, `XrmStrategy`, `window.__SPAARKE_BFF_TOKEN__`, or `tokenBridge.ts` (all being removed)
- **MUST NOT** snapshot a token in React state via `useState`/`useEffect` (root cause of the 401 bug this project fixes)
- **MUST NOT** follow guidance from files prefixed `DEPRECATED-` in `.claude/patterns/auth/`
- **MUST NOT** follow the 6-strategy-cascade section of `.claude/patterns/auth/spaarke-sso-binding.md` (the MSAL config invariants section in that file — INV-1..INV-7 — IS still canonical; read past the cascade)
- **MUST NOT** add new `/debug/*` endpoints on the BFF (all being removed in C3)
- **MUST NOT** add plain-text secrets to `appsettings*.json` — Key Vault references only

### ✅ MUST during this refactor

- **MUST** use `authenticatedFetch` from `@spaarke/auth` for all BFF API calls
- **MUST** use `useAuth()` hook for token + auth state (after Workstream A2 ships)
- **MUST** consult [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md) before implementing auth-touching code
- **MUST** preserve INV-1 through INV-7 (MSAL configuration invariants from `spaarke-sso-binding.md` §"Required MSAL Configuration") — non-negotiable. These come from a hard-won past fix; regressing them brings back popup-every-tab behavior.
- **MUST** rebuild + redeploy every consumer of `@spaarke/auth` when the library changes (INV-8 — Bundling Reality). Skipping a consumer = that consumer falls back to old library = popup regression.

### When in doubt

STOP and ask. Do not infer auth patterns from existing code — most of it is being rewritten.

---

## Applicable ADRs

| ADR | Relevance |
|-----|-----------|
| ADR-003 | Authorization seams — server-side rules remain; client API surface changes |
| ADR-004 | Job contract — unchanged; background jobs already use managed identity in some places, more in C2 |
| ADR-007 | SpeFileStore facade — unchanged; SPE auth flows through existing OBO |
| ADR-008 | Endpoint filters for auth — unchanged; named scheme additions in C5 |
| ADR-009 | Redis-first caching — applies to server-side OBO token cache |
| ADR-013 | AI architecture — affected indirectly via SprkChat refactor in B3 |
| ADR-015 | AI data governance — unchanged |
| **ADR-027** (new) | **Spaarke Auth Architecture (v2)** — written as task 090 |

---

## Key Technical Decisions

- **D-AUTH-1**: Function-based contract is the only public API surface. `accessToken: string` does not exist in any component prop, hook return, or context value.
- **D-AUTH-2**: `@spaarke/auth` uses pluggable strategies. `BrowserMsalStrategy` for Dataverse PCFs + Code Pages; `OfficeNaaStrategy` for Outlook/Word Add-ins. Drops `XrmStrategy` and `BridgeStrategy` entirely.
- **D-AUTH-3**: MSAL.localStorage is the cross-tab/iframe sharing mechanism. `BroadcastChannel` reserved for invalidation messages only (logout, revocation broadcasts) — not for token transport.
- **D-AUTH-4**: MSAL config invariants (INV-1..INV-7) preserved by literal code lift from current `SpaarkeAuthProvider` constructor. No deviation.
- **D-AUTH-5**: Per-tenant deployment is the threat model. Each customer installs Spaarke into their own Azure tenant; tokens never cross customer boundaries. Multi-tenant SaaS patterns explicitly out of scope.
- **D-AUTH-6**: Managed identity everywhere for server outbound. `DefaultAzureCredential` for Cosmos, AI, Graph app-only, Dataverse. Zero client secrets in App Service config.
- **D-AUTH-7**: TypeScript branded types are NOT the enforcement mechanism (won't survive minification). The runtime boundary is `authenticatedFetch` — it's the only code that materializes Bearer tokens as strings. ESLint rule bans `Authorization: \`Bearer \${...}\`` literals outside `authenticatedFetch.ts`.
- **D-AUTH-8**: Deferred from v2: DPoP, multi-SP privilege separation, HSM-backed keys, cryptographic audit chaining, B2C portal, mobile clients. Each evaluated in §6 of audit doc.

---

## Resource Quick Reference

### Patterns (still canonical in v2)

- `.claude/patterns/auth/spaarke-sso-binding.md` §"Required MSAL Configuration" — INV-1..INV-7
- `.claude/patterns/auth/spaarke-sso-binding.md` §"Bundling Reality" — INV-8
- `.claude/patterns/auth/bff-url-normalization.md` — `buildBffApiUrl()` unchanged
- `.claude/patterns/auth/oauth-scopes.md` — scope format
- `.claude/patterns/auth/obo-flow.md` — OBO token exchange (server-side unchanged)
- `.claude/patterns/auth/dataverse-obo.md` — Dataverse OBO (unchanged)
- `.claude/patterns/auth/service-principal.md` — app-only auth (migrating to MI in C1/C2)
- `.claude/patterns/auth/token-caching.md` — SERVER-SIDE Redis OBO portion only

### Patterns (superseded — DO NOT FOLLOW)

- `.claude/patterns/auth/DEPRECATED-msal-client.md` (renamed in PF-1)
- `.claude/patterns/auth/DEPRECATED-spaarke-auth-initialization.md` (renamed in PF-2)
- `.claude/patterns/auth/spaarke-sso-binding.md` 6-strategy cascade section
- `.claude/patterns/auth/token-caching.md` client cache cascade section

### Constraints

- `.claude/constraints/auth.md` — server-side MUST/MUST NOT rules canonical; client cascade rules superseded by this project

### Architecture Docs

- `.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md` — **canonical for this project until ADR-027 lands**
- `docs/architecture/AUTH-AND-BFF-URL-PATTERN.md` — URL pattern still canonical; token acquisition section superseded
- `docs/architecture/sdap-auth-patterns.md` — OBO + server taxonomy canonical; client snapshot/cascade superseded

### Key Source Files (entry points by phase)

| Phase | Entry point | What changes |
|---|---|---|
| Pre-flight | `.claude/patterns/auth/*.md` | Rename + STOP banners |
| A — Library | `src/client/shared/Spaarke.Auth/src/` | Strategy pattern, useAuth hook, cache simplification |
| B — Consumers | All ~30 surfaces (see audit §8.1 inventory) | Function-based contract adoption |
| C — Server | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs` + `appsettings.template.json` | Hardening |
| D — Security | BFF middleware + `Sprk.Bff.Api/Program.cs` | CSP, CAE, claims, step-up |
| E — CI | `.github/workflows/` + `package.json` | Gitleaks, regression pack, Dependabot |
| F — Docs | `ADR-027` + pattern updates | Documentation completion |

---

## Task Execution Protocol

When executing tasks in this project, ALWAYS use the `task-execute` skill (mandatory per root CLAUDE.md §4). Tasks are designed for parallel execution where the `parallel-safe` flag is `Yes` in TASK-INDEX.

### Parallel execution pattern

When multiple tasks are marked parallel-safe within the same phase batch, invoke ONE message with MULTIPLE Skill tool invocations (one per task). Sequential invocations waste parallelism.

### Quality gates (per task-execute SKILL)

- Pre-flight: load applicable ADRs + constraints + patterns from this CLAUDE.md
- Step 9.5: `code-review` + `adr-check` skills run automatically for FULL rigor tasks (any task that modifies `.cs` or `.ts` files)
- Post-task: TASK-INDEX status updated; current-task.md reset

### Regression test after every Workstream

After each Workstream (Pre-flight, A, B, C, D, E, B4, F) completes, run the MSAL binding regression test from [`spaarke-sso-binding.md`](../../.claude/patterns/auth/spaarke-sso-binding.md#verification-after-changes):

```javascript
// In Edge DevTools console after Workstream completion
localStorage.clear(); sessionStorage.clear();
document.cookie.split(';').forEach(c => {
  document.cookie = c.split('=')[0].trim() + '=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/';
});
// CLOSE BROWSER. Reopen. Navigate to SpaarkeAi.
// PASS: no popup; console shows `authority: https://login.microsoftonline.com/{actual-tenant-guid}/`
// FAIL: popup OR `/organizations` in the authority
```

Acceptance criterion: PASS for every Workstream.

---

## Parallel Execution Safety

Tasks marked `parallel-safe: Yes` in TASK-INDEX can run concurrently within their phase batch. Rules:

- Tasks in the same parallel group MUST NOT modify the same files (file-level locking)
- `.claude/` paths are main-session-only — sub-agents launched via Agent tool CANNOT write to `.claude/` (per root CLAUDE.md §3). Tasks touching `.claude/` files (pre-flight, F2, F4) are main-session-only.
- Build verification runs between parallel waves
- Failed tasks retry sequentially, not re-parallelized
