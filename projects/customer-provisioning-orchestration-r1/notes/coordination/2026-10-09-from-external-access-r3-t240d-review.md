# To customer-provisioning-orchestration-r1 — from spaarke-SPA-external-access-platform-r3 (2026-10-09)

**Re:** your 2026-10-09 note on T240d (`notes/coordination/2026-10-09-to-external-access-r3.md`) and `notes/t240d-ciam-external-contacts-design.md`.
**Reviewed against:** code on `origin/master` @ `231c5ab2b`, and the auth system of record (`projects/spaarke-auth-system-of-record-r1/auth-system-of-record.md`; architecture draft `docs/architecture/SPAARKE-AUTH-ARCHITECTURE.md`, both on branch `work/spaarke-auth-system-of-record-r1`).
**Status:** R3's review. The owner confirms the items marked *(owner)*.

## Verdict

- **No conflict** with R3 `design.md` §4.6.
- T240d **supersedes one R3 item**: the deep link carrying `{customerId, apiBaseUrl}`. R3 drops `apiBaseUrl` (see "What R3 will change", item 2).
- The design is consistent with ADR-028 A1, A3, A4 and A6 as they run in code.
- It also closes the auth record's §12c gap "no stamp channel writes `Ciam__*`", once H4b writes the settings.
- **We accept option 3** (each customer's BFF app is also its CIAM audience), with option 2 as the fallback, **subject to S1 and to request R1 below.**

## What R3 will change (R3 owns these)

1. **Per-customer backend on the CIAM plane.**
   - Today the SPA bakes the BFF URL and scope at build time (`VITE_BFF_API_URL`, `VITE_MSAL_BFF_SCOPE` = the shared dev CIAM API app).
   - R3 will instead do this at run time:
     1. sign in to the shared CIAM authority;
     2. call the T240c directory's CIAM lookup with the customer key;
     3. acquire a token silently for `api://{bffAppId}/user_impersonation`;
     4. call `apiBaseUrl`.
   - Known keys are kept in `localStorage` and drive a customer picker.
   - The SPA already has the run-time seams this needs (`setActiveBffTokenAcquirer` / `setActiveLoginScope` in `src/client/external-spa/src/auth/`), so the change is contained.
2. **Invitation and deep-link format.**
   - The link carries the customer key, plus the record id for C3's "you've been granted access" link. It **never** carries `apiBaseUrl` or a scope.
   - `design.md` §4.6 is updated to match.
3. **Invite UX.**
   - Add the "you already have a Spaarke account — sign in with it" path.
   - The pending state depends on UAC-r2's pending-invite marker (ISS-009 / #1485).
4. **One scope name across both planes** *(owner)*. R3 intends to request `user_impersonation` on the workforce plane too (Teams tab and browser). H3 would then need **no** `access_as_user` scope. This is pending the owner's confirmation in R3's auth decision.

## Production CIAM SPA client

**Please create it** as part of the approved one-time setup in `spaarkeextid`; we need only its client id back.

| Setting | Value |
|---|---|
| Platform | Single-page application (SPA) |
| Redirect URI | `https://external.spaarke.com` — exactly the origin: no path, no trailing slash |
| Post-logout redirect URI | `https://external.spaarke.com` |
| Pre-authorized on | the directory's CIAM API app, and every customer BFF app (via H3 `PreAuthorizedClientAppIds`) |

The redirect is the bare origin because MSAL uses `redirectUri` and `postLogoutRedirectUri` = `window.location.origin` (`src/client/external-spa/src/auth/msal-config.ts:105-106`). A URI "under `https://external.spaarke.com/...`" would not match.

## Requests

**R1 — the default (workforce) scheme must refuse CIAM tokens explicitly (tenant isolation, security).**

- *Why it matters.* Under option 3, a CIAM token's `aud` is the **same app** the workforce scheme accepts for that customer. Today the audiences differ (shared dev CIAM app vs BFF app), so the question never arises.
- *What the code does today.* The default scheme sets **no** issuer validation (`AuthorizationModule.cs:49`, `AddMicrosoftIdentityWebApi`; no `ValidIssuers` or `IssuerValidator` anywhere). Refusing a `*.ciamlogin.com` issuer therefore rests on Microsoft.Identity.Web's default issuer validator. That is library behaviour, not ours.
- *The risk.* If it ever accepted such a token, a CIAM contact would authenticate on internal routes. It would then pass every route that checks only `oid` + `tid` — for example, context-free AI chat (`Api/Filters/AiAuthorizationFilter.cs:174`).
- *Asks.*
  - (a) Keep S1 (iv) and the H13 probe "a CIAM token on a workforce-only route returns 401" as **hard gates**.
  - (b) Add an explicit guard on the default scheme: reject any token whose `iss` contains `ciamlogin.com` or whose `tid` equals `Ciam:TenantId`. This mirrors `CallerPrincipalResolver.DeterminePlane` and is a small change.

**R2 — `requestedAccessTokenVersion = 2` would change workforce tokens.**

- If S1 needs v2, workforce tokens for the same app switch `aud` from `api://{appId}` to the bare `{appId}`.
- On stamps, H4b writes only `AzureAd__Audience = api://{appId}`.
- The only code that sets `ValidAudiences` is the Copilot merge (`AuthorizationModule.cs:106-126`). It runs only when `AgentToken:CopilotAudience` is set, and no stamp writes that.
- **Not verified:** whether Microsoft.Identity.Web adds the bare-GUID form by itself when `Audience` is set.
- **Ask:** if v2 is turned on, make the workforce scheme accept `{appId}` explicitly — through H4b `AzureAd__ValidAudiences__*` or in code. Then re-test the Office add-in, Dataverse-hosted clients and Teams against a v2 app.

**R3 — CIAM audience forms.** We agree with design §3: the `Ciam` scheme should accept both `{appId}` and `api://{appId}`.

**R4 — name an owner for each BFF change.** The design implies three BFF changes:

1. `Ciam` valid audiences;
2. `CiamGraphClientFactory` → `IConfidentialClientProvider.GetClientAsync(Ciam:TenantId, bffAppId)` (MI-FIC);
3. the R1 issuer guard.

None has a named owner. R3 can take 1 and 3, since both sit on the external surface, if the owner agrees *(owner)*. Item 2 fits 240d step 2.

**R5 — silent token for the second resource.** The SPA signs in with the directory's CIAM scope, then needs a token for the customer's BFF scope **without a prompt**. Please include that in S1 (iii): a second resource, acquired silently, relying on pre-authorization.

## Open items R3 tracks

- "Already has an account" invites and the marker-gated E11 bind (UAC-r2, ISS-009 / #1485) — affects R3's invite and C3 copy.
- The S1 result. Option 2 changes only the scope string the directory returns; R3's code is the same shape either way.
