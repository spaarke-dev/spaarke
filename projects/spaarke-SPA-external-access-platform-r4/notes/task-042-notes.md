# Task 042 — email-missing text, and one email rule for the profile form (FR-26 #2155, FR-27 #2449)

Rigor FULL (BFF `.cs` + `.tsx`); the POML's sonnet tier ran on the Opus session @ high; directional. Commit `c6643923b3` (+ master merge `7c3bfef679`, + close commit).

## What changed

- **BFF** `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs`, `WorkforcePrincipalStrategy`, the 403 `registration_required` branch:
  - the prefill's `email` is now `ExternalRegistrationEndpoints.TokenEmail(home)`: the `email` claim (or its `ClaimTypes.Email` mapping) only, trimmed, no control characters, exactly one distinct value (case-insensitive), else null. That is the rule the registration route applies before it answers 403 `registration_email_missing`;
  - it was `WorkforceCallerClaims.TokenEmail`, which falls back to `preferred_username` and `upn`. A UPN-only sign-in saw its UPN on the form and was refused on Continue (#2449);
  - `WorkforceCallerClaims.TokenEmail` is **unchanged** (principal composition and the binder still use its fallback); given/family names still come from `WorkforceCallerClaims.From`;
  - task 041's `licensedUser` hint now takes the same computed email (`IsLicensedUserEmailAsync(string? email, …)`) instead of recomputing it: one value for both fields.
- **SPA**
  - `RegistrationPage.tsx`: the initial view is `linkRequired` when the hint is true, else **`emailMissing` when `prefill.email` is blank**, else the form. With no email the registration can only refuse, so the form is never shown.
  - The `emailMissing` body (FR-26): "Your sign-in did not include an email address, so we cannot register you. Ask your organization's IT administrator to set the Email on your Microsoft Entra account, then sign in again." Title and icon unchanged. No claim names, no server text.
  - `registration-client.ts`: doc comment on `RegistrationPrefill.email` only; parsing unchanged (a non-string or missing value is `''`).
- **Docs:** `EXTERNAL-ACCESS-TEAMS-APP-GUIDE.md`, the Users checklist line and the troubleshooting row: the known-defect notes are replaced by the new behaviour.
- **Review fix:** the email-missing screen's **Sign out** button (browser only): `App.tsx` → `WorkforceHomeGate` → `RegistrationPage` `onSignOut` (see the review below).

## Decisions

- **No email → the emailMissing screen first, not the form** (POML step 2 "decide and record"). The form could only end on the same screen after a wasted submit.
- **Order:** the `licensedUser` hint wins over an empty email. The BFF only sets the hint from a non-null registration email, so both cannot come from this BFF; the order only matters for a malformed body.
- **Older BFF during rollout:** an older BFF still prefills a UPN; the SPA shows the form and the POST refuses with the new text, as before. No compatibility issue in either deploy order.
- **Email stays display-only** (ADR-028 A9): nothing new reads it for authorization; the registration still reads the token, never the body.

## Tests

- **BFF** `tests/integration/regression/Issue2449_RegistrationEmailRuleTests.cs` (KEEP path), 10 cases:
  - claim-shape table through the real `WorkforcePrincipalStrategy`: email only; `preferred_username` only; `upn` only; two different emails; an email with a control character; plus one extra row, email + a different `preferred_username` (the original report's shape: the form shows the email, never the UPN). For each row the prefill email equals `ExternalRegistrationEndpoints.TokenEmail` and an explicit expected value (null for the four refuse rows);
  - `WorkforceCallerClaims.TokenEmail` keeps its UPN fallback (2 cases, the "unchanged" acceptance criterion);
  - through the real host (`ExternalRegistrationEndpointsTests.ConfiguredFixture`): the same token at `GET /me` then `POST /registration`. Email claim → the prefilled email is the registered contact's email; `preferred_username` only → no prefilled email (the UPN appears nowhere in the body) and the POST answers 403 `registration_email_missing` with no writes.
  - Mutation check: restoring `prefill.Email` fails the 4 refuse rows and the host UPN test; the agreeing rows still pass.
  - With related suites (041 hint, registration endpoint, `CallerPrincipalResolverTests`, `WorkforceCallerClaims`): 152/152.
- **SPA** `workforce-home-auth.test.ts`:
  - `registration_email_missing` after a submit shows the FR-26 text (heading, the IT-administrator sentence, not the old "Contact your administrator.", no codes or claim names, no form, no Continue / Try again);
  - a `registration_required` with an empty, blank, null or missing email opens on that screen with no form and nothing posted (4 cases);
  - Sign out on that screen calls the shell sign-out; no button without one (2 cases, added after review).
  - Mutation checks: the old text fails the FR-26 case; removing the guard fails all 4 FR-27 cases.
  - Gate block 37/39 in one run: the 2 failures were the block's first test timing out on a cold start (68 s) and the next test seeing its leaked render; both pass alone. The known load flake, also seen in 041.
  - `registration-refusals`, `registration-validation`, `teams-home-auth`: 120/120. `tsc` and `eslint` clean.

## Quality gates

- **Publish size**, fresh master `d148580e15` vs branch `7c3bfef679`: short-path worktrees `C:\wt042m` / `C:\wt042b`, `dotnet publish -c Release`, `Compress-Archive`, 194 files each. Zips 43,633,252 → 43,633,453 bytes, **+201 bytes** (≈ 43.6 MB, under 55/60 MB).
- **CVE:** no package or csproj change; `dotnet list package --vulnerable --include-transitive` reports none.
- **`/conflict-check`:** 35 open PRs, none touch the task's files. Master merge: only the 041 squash touched `RegistrationPage.tsx` / `CallerPrincipalResolver.cs`; resolved with the branch copies.
- **ADR check** (mapped ADRs + index pass):
  - ADR-001/008: no new endpoint or filter.
  - ADR-010: no new DI registration.
  - ADR-019: the existing ProblemDetails; `reasonCode` unchanged, and the `email` extension may now be null for shapes that used to carry a UPN.
  - ADR-028 A9: the email stays display-only; no scope, client, authority or credential change. The registration still reads the token, never the body.
  - ADR-021: existing `EmptyState` and Fluent `Button` with semantic tokens; no new styles.
  - ADR-038: KEEP path (`tests/integration/regression/Issue2449_*`); module-boundary doubles only.
  - ADR-012/050/052: not applicable.
  - All compliant.
- **Adversarial review** (opus, one pass; not an auth-tagged task): **no fix-now runtime defect.** The prefill and the route call the same function on the same identity. `registration_required` only comes from `ResolveHomeAsync`, which needs a WorkforceHome identity, so the strategy's `httpContext.User` fallback is test-only. Findings:
  - **F2, fixed:** the guide's troubleshooting row still said "Profile form, then …" with a known-defect note → rewritten (`EXTERNAL-ACCESS-TEAMS-APP-GUIDE.md`, the "We could not read your email address" row).
  - **F2, fixed:** `WorkforceHomeGate.tsx` header comment did not list the email-missing first screen → updated.
  - **K2, fixed in scope:** "then sign in again" was hard to act on. A reload keeps the cached token, which still has no email, until it expires, and the screen did not point to the header's Sign out. The email-missing screen now has a **Sign out** button in the browser. It is the shell's own `handleSignOut` (`App.tsx`), threaded through `WorkforceHomeGate` → `RegistrationPage` as an optional `onSignOut`. `App` passes none in Teams, which has no account of ours to sign out of. No third copy of the sign-out logic. Two tests (must-fire: the click calls the shell sign-out and posts nothing; must-not-fire: no button without it). Re-check of the fix diff: tsc and eslint clean, gate block 41/41, the 8 App shell test files 84/84.
  - **K1, filed:** a licensed user with no `email` claim whose silent link failed gets no "Link my account" on the email-missing screen. This predates the task (they reached it after the form before) → **D-42 #3201** (Idea).
  - **K3, documented:** Entra can omit an unverified-domain email from the token, so setting the Email may not help → an operator note in the guide row. The screen text is unchanged.
  - **K4, kept:** `WorkforceCallerClaimsTokenEmail_KeepsItsUpnFallback` passes before the fix. It is the acceptance criterion "WorkforceCallerClaims.TokenEmail unchanged", not a regression test; it guards the blast-radius constraint, so it stays.
  - **K4, kept:** the claim-shape table mocks the resolver (it only answers `registration_required`). The two host tests use the real resolver and route.

## Known limits

- **In Teams** there is no Sign out. After the admin's fix, the new token (with the email) arrives when the cached one expires, up to about an hour. The guide row says so.

## Deploy

Not deployed. The BFF part reaches dev only through uac-r2, after the merge, with the owner naming the exact master SHA in uac-r2's session.
