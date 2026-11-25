# Current Task State

> **Last Updated**: 2026-11-25 ~18:40Z (task 040 completed; PR being opened)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active** | **Task 041**: a licensed user's first sign-in skips the profile form (FR-25, #2192). FULL rigor, opus @ high, directional. POML `tasks/041-licensed-first-signin.poml`. Status: in-progress since 2026-11-25 ~19:00Z. |
| **In flight** | PR #3160 (task 040) has auto-merge on, by the owner's go. After it merges, the dev deploy: the BFF through uac-r2 (owner go on the exact merged SHA, in uac-r2's session) and the SPA (owner go). |
| **Done (041)** | Step 0.5: conflict check clean (32 open PRs, none on 041's files). Step 1 trace done (below). No files changed yet. |
| **Next action** | Step 2, BFF part. In `CallerPrincipalResolver.cs` `WorkforcePrincipalStrategy` (registration_required branch ~:792-811), add extension `licensedUser` (bool). True only when `ContactBindingDecision.DecideLicensedUserEmail(store.FindEnabledSystemUsersByEmailAsync(email), email) == LicensedUser`, with email = `ExternalRegistrationEndpoints.TokenEmail(home)` (the strict email-claim rule the registration POST uses, NOT `WorkforceCallerClaims.TokenEmail`). If there is no email, the outcome is Unknown/NotLicensed, or the read throws: false, and the throw is logged by code only. Inject `IContactIdentityStore` as an OPTIONAL last ctor param (`= null`; ~15 test sites build the strategy with 3 args; the store is already a registered singleton, `ExternalAccessModule.cs:250`). Then the SPA: `parseRegistrationRequired` returns `licensedUser`; the gate's `afterSilentLinkFailure` and the `no_systemuser` branch pass it; `RegistrationPage` opens in `linkRequired` when the hint is true; NO auto-popup. Tests: `tests/unit/.../CallerPrincipalResolverTests.cs` holds the registration_required cases (extend them, or add a KEEP-path test under `tests/integration/auth/ExternalAccess/`); SPA `tests/workforce-home-auth.test.ts`, `registration-refusals.test.ts`. Then step 3: find the 60 s popup timeout (msal-auth.ts `acquireTokenPopup` ~:1449, likely the MSAL default) and request the auth/UAC review from uac-r2. |

## Task 041 trace (step 1)
- Gate (`WorkforceHomeGate.tsx`): `/me` 403 `registration_required` → silent guest token → `POST /workforce/link`. With no token (`afterSilentLinkFailure`) or `no_systemuser` it shows `RegistrationPage` (form). The form POST → 403 `licensed_user_link_required` → `linkRequired` view ("Link my account", popup on click only).
- BFF: the licensed check runs only inside `ContactIdentityBinder.RegisterHomeContactAsync` (~:1040), after the readability probes. Email = `ExternalRegistrationEndpoints.TokenEmail` (email claim only, exactly one value). The prefill email uses `WorkforceCallerClaims.TokenEmail` (with upn fallbacks), which is different.
- Precedent for Infrastructure calling an Api static: `WorkforcePrincipalResolver` uses `MembershipEndpoints.ExtractAadObjectId`.

## Task 040 outcome (details in notes/task-040-notes.md)
- Server: `ModuleEntitlementResolver.IsGrantedWorkforceContact` adds `assigned-work` for a workforce contact that can read ≥ 1 project, matter or WA. The code is stripped from role-map extras, so a systemuser never gets it. No new endpoint, field, DI or package.
- SPA: the five record tabs share `RECORD_TAB_GATE` (ciam + workforce).
- Review F1 (owner 2026-11-25: hide it for now): Submit invoice is hidden on the workforce plane (`callerMaySubmitInvoices` in `recordAccess.ts`, plane from `activePlaneOrNull()`). The question went to ebilling as D-41 #3159.
- Known limits filed: D-39 #3153 (the ceiling maps one table to five tabs), D-40 #3154 (K3). K2 is unchanged.
- uac-r2 review: no objection (quoted in the notes).

## Live on dev
SPA `0267ee64a2` (rollback `3067fb3bdc`); BFF `a1b7db1def` since 2026-11-25 15:58:53Z (rollback `ef0c566d2c`; uac-r2 deploys; owner go per SHA). Task 040 is not in it. Master has moved past `0267ee64a2`, including ebilling ISS-052 #3082 (`2ac250cae4`): list it in the next SPA deploy proposal.

## Owner questions open
- Track the raw filing-reference footer seen in an older PRJ-000046 system email? (Told to the owner; not filed. The repo is public.)
- Track the residual export-hardening gaps privately?
- Docx preview as an Idea?
- "The client is reviewing this invoice." beyond the Information field?
- `useHostRowSource` row in `.claude/patterns/CAPABILITY-INDEX.md` row 31 (`.claude` edit, owner approval)?

## Filed
- D-24 #2939 … D-33 #3010; D-34 #3045 done; D-35 #3046; D-36 #3050; D-37 #3129; D-38 #3149; D-39 #3153; D-40 #3154; D-41 #3159.

## Merge trap (seen 2026-11-22, again 2026-11-25)
Master holds older squashes of this project's notes. On a master merge, keep the branch's copies of `projects/spaarke-SPA-external-access-platform-r4/**` (`git checkout --ours`).
