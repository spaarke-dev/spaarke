# Task 041 — licensed user's first sign-in skips the profile form (FR-25, #2192)

Rigor FULL (tags auth, BFF + SPA code); opus @ high; directional. Commits `026ff0396f`, `f3da9c8f7a` (+ close commit).

## What changed

- **BFF** `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs`, `WorkforcePrincipalStrategy`:
  - the 403 `sdap.access.registration_required` body gains one boolean extension, `licensedUser`;
  - it is true only when the caller's own home-token email is an enabled licensed user. The email is `ExternalRegistrationEndpoints.TokenEmail`: the email claim only, exactly one value, never UPN or preferred_username. The check is the same read (`IContactIdentityStore.FindEnabledSystemUsersByEmailAsync`) and the same decision (`ContactBindingDecision.DecideLicensedUserEmail`) the registration POST uses for `licensed_user_link_required`;
  - every doubt gives false: no store, no email or two emails, Unknown/Failed, an exception, a disabled user;
  - cancellation rethrows; the log carries the exception type only;
  - the read runs only on the registration_required branch.
- **Placement:** in the BFF, inside the existing strategy, because it is the only place that builds the registration_required body.
  - No new endpoint, scope, DI registration or package.
  - `IContactIdentityStore` is an optional last constructor parameter, so the ~15 existing unit constructions compile unchanged. The host resolves the existing singleton (`ExternalAccessModule.cs:250`).
  - A new `/me/licensed` endpoint was rejected: it would be new surface and a new scope question (escalation trigger).
- **SPA:**
  - `registration-client.ts`: `RegistrationPrefill.licensedUser` is set from `body.licensedUser === true` (an older BFF without the field gives false).
  - `RegistrationPage.tsx`: the initial view is `linkRequired` when the hint is true. No popup opens until the "Link my account" click.
  - `WorkforceHomeGate.tsx`: doc only. The gate passes the prefill through; the silent stored link still runs first.

## Popup timeout (auth/UAC review)

- **Found:** the link popup uses msal-browser 5.5.0's default `system.popupBridgeTimeout` = 60000 ms (`DEFAULT_POPUP_TIMEOUT_MS`). The guest-link PCA (`getGuestLinkInstance`, `msal-auth.ts`) sets no `system` options; the spec's "60 s" is that default.
- **Options:**
  - (a) keep the 60 s default;
  - (b) an explicit value on the guest-link instance only;
  - (c) configurable per build.
- **Decision (uac-r2, 2026-11-25): (a), keep 60 s.** The popup is user-started, a timeout leaves the person on the link step with "try again" and writes nothing, and no E2E evidence shows 60 s is short. Revisit with (b) on the guest-link instance only if MFA timeouts show up in practice.
- **Applied:** no code change needed.

## Auth/UAC review (uac-r2, 2026-11-25) — no objection

uac-r2 read the staged diff read-only and ran no tests. Findings, none blocking:
1. No new disclosure: the same fact, claim, read and decision as the POST's `licensed_user_link_required`, about the caller's own token email only.
2. Fails closed.
3. Grants nothing; the popup is click-only; the link route proves the guest.
4. Known limit: one systemuser-by-email read per registration_required 403 on any external route. This is bounded: it needs a valid accepted-tenant home token, it is a single query, the routes are rate-limited, and the resolver already reads Dataverse for an unresolved caller. No change asked. → **K-class, recorded here; no change.**

## Adversarial reviews

**Pass 1 (opus): no F-class defects.**
- Stale gate comment ("a second registration_required always shows the form") → **fixed** in `f3da9c8f7a`.
- K: the hint skips the binder's column-readability probes. In a mis-provisioned environment a licensed user reaches the link step instead of the POST's environment refusal. Deliberate; documented in the strategy's XML doc.
- K (pre-existing): a person with the hint true but no usable guest account ends at "contact your administrator", as before, one screen earlier.
- K: `/me` is a cheaper self-probe than the rate-limited POST (same as UAC finding 4).

**Pass 2 (opus, auth/security focus): no F-class defects.**
- K4: an unregistered caller costs one more uncached Dataverse read per external request. `/me` has no rate limiter, and the read needs an allowlisted-tenant user token. Bounded; the same limit UAC accepted. Optional later: a short negative cache, or `dataverse-query` on `/me`. No change.
- K: an unverified email (no `xms_edov` check) gives the same answer the registration POST already gives; a true hint only opens the link step, and `/workforce/link` still needs a Spaarke-tenant guest token. No change.
- K (test gap): no case had `email` and `ClaimTypes.Email` differing only in case → **added** (`..._TheSameEmailAsBothClaimTypesDifferingInCase_IsOneEmail_HintIsTrue`). The "home identity vs `httpContext.User`" gap is harmless, because a token authenticates through one scheme only; no test added.
- Checked clean: claim shapes and query escaping, which principals reach the branch, no server or SPA authorization effect, disclosure (body, logs, timing), cancellation, test strength.

## Tests

- **BFF:** `tests/integration/auth/ExternalAccess/LicensedFirstSignInHintTests.cs` (KEEP path), 11 cases (one added after review pass 2).
  - The real home `WorkforcePrincipalResolver` and real `ContactIdentityBinder` over `InMemoryContactIdentityStore`.
  - Must-fire: an enabled licensed email → true, 403 kept, no writes, the systemuser not named.
  - Must-not-fire: no user, disabled, another email, failed read, throwing read, UPN only, two emails (the read never runs for the last two), other denies carry no hint.
  - Through the real host: `GET /api/v1/external/me` true/false with the DI-built strategy.
  - `CallerPrincipalResolverTests` + new: 64/64.
- **SPA:** `workforce-home-auth.test.ts`, 6 new gate cases.
  - The hint opens the link step: no form, no popup before the click, and the click gives `[ME, LINK, ME]` with no registration POST.
  - A cancelled popup stays out of the workspace with nothing posted.
  - A silent link still wins.
  - `false` / `"true"` / `1` each show the form.
  - Mutation check: hard-wiring the parse to false fails the 2 hint tests.
  - The full run of the 3 named files: 223/225. The 2 timeouts (the AuthGuard sign-in test and the Teams AC1 bootstrap) are unrelated and passed alone (~14 s each under a 60 s limit); a known load flake.
  - tsc and eslint clean.

## Quality gates

- **Publish size**, fresh master `1b89025736` vs branch `026ff0396f`: short-path worktrees `C:\wt041m` / `C:\wt041b`, `dotnet publish -c Release`, `Compress-Archive`, 194 files each. Zips 43,619,678 → 43,621,079 bytes, **+1,401 bytes** (≈ 43.6 MB, under 55/60 MB).
- **CVE:** `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages; no package or csproj change.
- **ArchTests:** 2831/2831.
- **`/conflict-check`:** 34 open PRs, none touch the task's files.
- **ADR check:**
  - ADR-001/008: no new endpoint or filter.
  - ADR-010: no new registration.
  - ADR-019: an extension on the existing ProblemDetails; reasonCode unchanged.
  - ADR-028 A9: no scope, client, authority or credential change; UAC reviewed.
  - ADR-038: KEEP path; module-boundary doubles only.
  - ADR-007/052: n/a.
  - ADR-021: no new UI.
  - Infrastructure → `Api` static call: precedent `WorkforcePrincipalResolver` → `MembershipEndpoints.ExtractAadObjectId`.
  - All compliant.

## Deploy

Not deployed. The BFF part reaches dev only through uac-r2, after the merge, with the owner naming the exact master SHA in uac-r2's session.
