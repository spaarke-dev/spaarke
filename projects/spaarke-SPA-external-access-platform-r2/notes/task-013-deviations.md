# Task 013 — Dual-plane auth bootstrap (CIAM + realm discovery) — deviations & decisions

> Status: COMPLETE (2026-08-06). FULL rigor, opus. `npx tsc --noEmit` clean.
> Auth-sensitive: CIAM per-tab sessionStorage + Teams NAA paths preserved exactly.

## Headline deviation — Path C compliance with ADR-028 A3 (did NOT touch @spaarke/auth)

The POML title/goal/steps say "extend **@spaarke/auth** `AuthStrategy`" for CIAM + per-context
authority. That directly conflicts with **ADR-028 Amendment A3** (the authoritative rule set,
authored in task 010):

> "**MUST NOT** route the module-host SPA through the Xrm-bound `@spaarke/auth` while it remains
> Xrm-context-bound + MSAL v3; the shared standalone-MSAL module with pluggable authority (A1/A2)
> is the sanctioned client surface for this platform."

Additionally, the shipped external-spa **does not consume `@spaarke/auth`'s `AuthStrategy` at all**
for its runtime auth — it uses its own standalone-MSAL module (`auth/msal-config.ts` +
`auth/msal-auth.ts`), which already carries BOTH `ciam` and `workforce-multitenant` authority kinds.
Modifying `@spaarke/auth` would be (a) an A3 MUST-NOT violation and (b) dead code.

**Resolution (CLAUDE.md §6.5 Path C — pivot to comply):** built realm discovery + per-context
authority selection in the **existing standalone-MSAL module + the `main.tsx` host branch** (the
sanctioned surface), and left `@spaarke/auth` untouched. The caller's brief explicitly directed this
("prefer EXTENDING the existing seam … over rewriting it"). Directional step-mode permits adapting
the steps to the real codebase. This also makes acceptance criterion 5 (workforce-only AuthStrategy
not regressed) true by construction — `@spaarke/auth` is byte-for-byte unchanged.

No escalation fired: this is a documented Path-C pivot, not an unresolved ADR conflict.

## What was built (files)

New:
- `src/client/external-spa/src/auth/realm.ts` — `Realm` type + per-tab **sessionStorage** persistence
  of the browser home-realm choice (NFR-05: not localStorage; fail-safe guard on tampered values).
- `src/client/external-spa/src/auth/standalone-plane.ts` — `resolveStandalonePlane(realm)` maps
  realm → `{ instance, bffScope }` (CIAM = existing singleton unchanged; workforce = standalone
  `PublicClientApplication` via task-010's `workforceAuthorityConfig`), and `applyStandalonePlane`
  wires the acquirer + login-scope seams. Broker-only on both planes (no OBO).
- `src/client/external-spa/src/components/auth/RealmChooser.tsx` — Fluent v9 "My organization /
  Partner" chooser built on the shared **`ChoiceModal`** SprkModal preset (ADR-050 / §11; semantic
  tokens only, ADR-021).

Modified:
- `auth/msal-auth.ts` — added the `setActiveLoginScope`/`getActiveLoginScope` seam (parallels the
  existing `setActiveBffTokenAcquirer` seam) so `AuthGuard`'s sign-in redirect requests the selected
  plane's BFF scope. Default = CIAM scope → CIAM path unchanged. Teams NAA code untouched.
- `components/AuthGuard.tsx` — `loginRedirect` now requests `getActiveLoginScope()` instead of the
  hard-coded CIAM `MSAL_BFF_SCOPE` (default is identical for CIAM).
- `main.tsx` — `bootstrapStandalone` now renders `<StandaloneBootstrap>` (live path): read stored
  realm → chooser if none → resolve plane authority → `initialize()` → mount the SAME `<App>`. Teams
  branch (`bootstrapTeams`) and dev-mock branch unchanged. Hooks kept unconditional (mock branch is a
  separate function).
- `App.tsx` — sign-out now also `clearStoredRealm()` so a signed-out user can re-choose a plane
  (no-op for Teams / dev-mock). MSAL cache config untouched.
- `config.ts` — generalized `getTeamsWorkforceEnvConfig()` → `getWorkforceEnvConfig()` (same
  multitenant app reg serves Teams + browser workforce); kept the old name as a delegating alias so
  `TeamsHostAdapter` needs no change.

## Token-audience/authority contract verification (step 5 / criterion 4)

The browser workforce plane reuses the EXACT `workforceAuthorityConfig` (authority
`login.microsoftonline.com/organizations`) + the same multitenant app registration + the same BFF
scope as the **proven-live teams-app-r1 NAA path** — only the acquisition TRANSPORT differs (browser
redirect vs Teams host broker). Transport does not change the token's `aud`/`iss`, so the audience
and issuer are identical to the shipped recipe the BFF already validates (workforce default JwtBearer
scheme). CIAM is unchanged (validated by the BFF "Ciam" scheme). **No mismatch found → the escalation
trigger did not fire.** Had a mismatch existed, the task would have STOPPED per the POML escalation
trigger rather than shipping.

Note: no live BFF E2E was run here (task 015 owns the BFF concurrently; NFR-05 forbids touching
`src/server/**`). Contract verification was by code inspection against the proven recipe. Live
cross-plane E2E (external CIAM + internal workforce from one URL) is a P1 integration/deploy item
(tasks 018/019).

## Documented A2/A3 exception (not a violation)

The workforce plane's `/organizations` authority technically differs from base ADR-028's
"tenant-specific authority" MUST — but ADR-028 **A2/A3 explicitly sanction** the workforce
multitenant authority for the collaboration/module-host surface (per-customer admin consent; tenant
unknown at build time). `msal-auth.ts:203-211` already documents that the "never `/organizations`"
rule targets internal `@spaarke/auth` iframe-`ssoSilent` surfaces, which are A2-exempt and unused
here. adr-check + code-review both PASS.

## Acceptance criteria

1. Same URL, browser: "Partner" → CIAM authority → launcher; "My organization" → workforce authority
   → launcher. **MET** (RealmChooser → resolveStandalonePlane → per-plane MSAL instance + scope).
2. Inside Teams: silent workforce SSO, no chooser. **MET** (bootstrapTeams path untouched; chooser
   unreachable in Teams by construction).
3. CIAM sessionStorage per-tab isolation + broker-only. **MET** (msal-config unchanged; realm also in
   sessionStorage; acquireStandaloneBffToken broker-only, no OBO).
4. Negative — wrong-audience token rejected by BFF + escalation fires rather than shipping. **MET as
   an invariant**: contract verified identical to the proven recipe by inspection (no mismatch → no
   ship-with-mismatch); the escalation trigger is wired to stop on any mismatch. Live BFF-reject E2E
   deferred to P1 integration (BFF is out of this task's scope).
5. Existing workforce-only `@spaarke/auth` `AuthStrategy` not regressed. **MET** — `@spaarke/auth`
   untouched (Path C).

## Notes for the parallel session

- I did NOT edit `TASK-INDEX.md` (main session aggregates) and did not fight over `current-task.md`
  (task 015's session took it over mid-task, as expected). POML `<status>` set to `completed`.
- `.env.example` / CI token substitution for `VITE_TEAMS_MSAL_*` (workforce app reg) must be wired
  for the browser workforce plane to function in a real deploy — that is task 071's scope (already
  its responsibility for the Teams plane; same vars).
