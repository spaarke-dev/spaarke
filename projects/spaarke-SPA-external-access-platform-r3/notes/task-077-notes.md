# Task 077 — default scheme accepts the bare app-id audience (v2 tokens)

Date: 2026-10-19. Rigor: **FULL** (tags `bff-api`, `auth`, `security`; two adversarial passes). Model: Opus (session).
Origin: task 059 stage R, stop S-1 (`task-059-dev-setup-record.md` §11). Owner decision 2026-10-19: "S-1: follow recommended", "S-2: yes proceed", "yes bff auth change + #2102 and 042b" (the deploy scope).

## Why

ADR-028 A9 requires `requestedAccessTokenVersion` = 2 on the BFF app. The dev app `1e40baad` was on v1. Switching makes `aud` the bare app id for every caller, but the default scheme accepted only `api://{appId}` (`AzureAd:Audience`) and the Copilot audience.

A probe (a temporary theory in `AuthorizationModuleCiamTests`, since removed) showed that a bare-id token got 401 with both the v1 and the v2 issuer.

## What changed

| File | Change |
|---|---|
| `Infrastructure/DI/AuthorizationModule.cs` | New `PostConfigure<JwtBearerOptions>` on the default scheme, separate from the Copilot delegate, which runs once per process and only when the Copilot audience is set.<br>`AddWorkforceAudiences` merges the bare id and `api://{id}`, derived from a **GUID** `AzureAd:ClientId` (unset or placeholder: no change), into `ValidAudiences`, keeping `ValidAudience` and the Copilot audience.<br>Its chained `OnTokenValidated` refuses an **ID token** for the app (`IsIdTokenForApp`: `aud` = the bare id and neither `azpacr` nor `appidacr`). |
| `tests/unit/.../AuthorizationModuleCiamTests.cs` | 15 new cases: bare id with the v1 and v2 issuer → 200; `api://` with v1 → 200; another app, `api://other` and `api://` → 401; CIAM with the bare id → 401; two hosts in one process; resolved options (audiences plus validation flags); guest-token parameters with a v2 token; Copilot still refused for the guest link; helper null/blank/idempotent/no issuer change; ID token with `roles` → 401; `IsIdTokenForApp` table.<br>The harness tokens now carry `azpacr`/`appidacr`, as Entra access tokens do. |
| `tests/integration/auth/ExternalAccess/ExternalWorkforceLinkEndpointsTests.cs` | The harness audience list includes the bare id (shared host's ClientId is not a GUID, so the 077 merge adds nothing there); header corrected; new route case: v2 guest token, `aud` = bare id → 200 `linked`. |
| `docs/architecture/auth-security-boundaries.md` | Boundary 3 describes the second PostConfigure and the ID-token refusal. |
| `notes/task-059-dev-setup-record.md` | Stage B:<br>- precondition: live build contains 077;<br>- adds the v2 switch, the per-surface smoke check and the rollback;<br>- "do not roll the BFF back below 077 while on v2 / within 90 min".<br>Stage D: the dead `AzureAd__ValidAudiences__*` settings. §10 note. |
| `projects/...-r2/notes/teams-sso-fix-and-entra-app-ownership.md` | Correction: `AzureAd__ValidAudiences__2` has no effect. |

Claim-shape reads after the v2 switch were checked by the session and both reviewers. No change was needed:
- `appid` reads fall back to `azp` or only log;
- `unique_name`/`upn` are last fallbacks;
- issuer parsing accepts both forms;
- the OBO paths pass the token through;
- no client code inspects `aud`/`ver`/`appid`.

## Verification

- Failing first: against a no-op helper, 6 new tests failed (audience); all negatives passed.
- Seeding proofs (each restored):
  - merge placed inside the once-flag delegate → 5 fail;
  - ID-token guard disabled → `DefaultScheme_IdTokenForTheApp_WithRoles_Returns401` fails;
  - link harness without the bare id → `V2GuestToken_BareAppIdAudience_Returns200Linked` fails.
- Auth-filtered unit tests (AuthorizationModule, SpaarkeGuestTokenValidator, ExternalWorkforceLink, WorkforcePrincipalResolver, Authentication, KeylessProof, CallerIdentity): **309/309**.
- ArchTests on `f68d0f312` and on `77d74f305`: **1152/1152**.
- Full BFF unit suite on `77d74f305`: 25154 passed, 22 skipped, 45 failed. 42 of the failures took about 3 minutes each (host start-up timeouts in a 48-minute parallel run on a busy machine). Rerunning every class that had a failure, 32 classes plus the 4 display-named tests, gave **858/858**. The fast failures (WorkforceHome scheme, one link case) also pass on their own (253/253). None reproduce.
- After the re-check hardening: the classes above plus the auth filter pass.
- Publish size, fresh worktrees `C:\wt077m` (master `30324eeac`) and `C:\wt077b` (`f68d0f312`), `Compress-Archive`: 192 files each; **37.44 MB both, delta −258 bytes**. Worktrees removed with `Remove-WorktreeSafe.ps1`.
- No package change, so no new CVE.

## Placement (CLAUDE.md §10 / §11)

- **Placement:** inbound validation config for the BFF's own scheme; it lives in `AuthorizationModule` next to the existing audience merge.
- **Existing:** the Copilot merge and `BuildCiamValidAudiences`.
- **Extension:** reuses the helper; it cannot go in the Copilot delegate (it runs once per process and only conditionally).
- **Cost of doing nothing:** the ADR-required v2 switch would return 401 to every workforce surface.

## Review (two adversarial passes, Opus) and fix round 1

| # | Pass | Class | Finding | Outcome |
|---|---|---|---|---|
| 1 | 1 | F4 | Accepting the bare id also admits an **ID token** issued to a client signing in as the BFF app (`/api/config` publishes `AzureAd:ClientId` as the MSAL client). One carrying `roles` would pass Identity Web's scp-or-roles check and satisfy `SystemAdmin`. | Fixed: `IsIdTokenForApp` refusal in the 077 `OnTokenValidated`; ID-token test plus seeding proof. |
| 2 | 2 | F3 | The link-route harness used the old audience list; no route test sent the v2 guest token. | Fixed: harness and header; new route case plus seeding proof. |
| 3 | 1, 2 | K1 | No test runs Identity Web's real issuer validator (both harnesses replace it). The production authority is the single tenant, unchanged by 077. | Known limit: per-surface live smoke check after the v2 switch (059 stage B, task 074). |
| 4 | 2 | K2 | The state "app on v2 while the live build lacks 077" gives every surface a 401 (BFF rollback, or another session's deploy). | 059 stage B precondition plus the rollback rule; the deploy round's ancestor check. Merge 077 to master before stage B where possible. |
| 5 | 1 | K2 | Copilot audience lost on a second options build (once flag); pre-existing, single instance in production. | Not changed (pre-existing, outside scope); recorded. |
| 6 | 1 | K3 | The "[CRITICAL] audience list is empty" warning runs before the 077 merge and can fire falsely when `AzureAd:Audience` and the Copilot audience are both unset. Pre-existing wording; does not happen in any configured environment. | Recorded. |
| 7 | 2 | K4 | `AzureAd__ValidAudiences__0..2` on dev are dead settings; the r2 note claimed `__2` worked. | r2 note corrected; 059 stage D lists them (owner may delete). |
| 8 | 2 | K4 | `auth-security-boundaries.md` described only the Copilot merge. | Fixed. |
| 9 | 2 | K4 | Provisioning H3 does not set token version 2 on stamps today; when it does, stamps need a build with 077. | Recorded; sent to provisioning with the A9 coordination items. |
| 10 | 2 | K4 | `BuildCiamValidAudiences` is named for CIAM but now has three non-CIAM callers. | Not renamed (pre-existing name, no behaviour impact). |

### Scoped re-check of the fix diff (Sonnet, `77d74f305`): no F-class findings

| # | Class | Finding | Outcome |
|---|---|---|---|
| R1 | K2 | `IsIdTokenForApp` failed open when the validated token is not a `JsonWebToken` (no legacy handler is configured today). | Hardened: falls back to the principal's `aud`/`azpacr`/`appidacr` claims, as the FR-23 guard does; table test covers both paths. |
| R2 | K4 | The ID-token 401 test asserted only the status code. | It now also asserts the `sdap.auth.deny.id_token_on_workforce_scheme` log line. |
| R3 | K3 | The guard rests on Microsoft's claims reference: access tokens carry `azpacr`/`appidacr`, ID tokens do not. Managed-identity tokens carry `appidacr` by recollection, not re-fetched. | Live proof: 059 stage B per-surface check reads the log for this deny code. |
| R4 | K4 | "No code reads `AzureAd__ValidAudiences__*`". | Pass 2 confirmed there is no top-level `ValidAudiences` property on `JwtBearerOptions` or `MicrosoftIdentityOptions` to bind, and the probe showed the bare id refused although dev's harness-equivalent config carries no such list. Kept as stated. |
| R5 | K4 | No mixed-case or multi-audience case. | Code uses `OrdinalIgnoreCase` and `Any`; no extra test (scope). |

## Known limits

- K1 and K2 above. The live proof is 059 stage B's per-surface check.
