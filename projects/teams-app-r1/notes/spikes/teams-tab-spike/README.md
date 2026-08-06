# Teams Tab Spike (throwaway) — Foundation Spike Task 001

> **Throwaway scaffold** for the FR-16 foundation spike. Spike-exempt from test/deploy gates.
> Do **NOT** promote into `src/` — the production adapter is task 012, the production resolver is tasks 020/021.
> Purpose: let the operator close Runs A/B in `../foundation-spike-findings.md` §5 with a fast click-through.

## What it does

A minimal Teams personal tab that:
1. Initializes the Teams JS SDK (`app.initialize()`).
2. Acquires a **workforce** token via `authentication.getAuthToken()` (Teams SSO).
3. Calls the BFF `GET /api/users/me/memberships/{entityType}` with `Authorization: Bearer <token>`.
4. Renders the HTTP status, the returned membership set, and the decoded token claims (`oid`, `tid`, `aud`) so you can eyeball audience/tenant.

This is deliberately zero-build (plain HTML + JS + Teams JS from CDN) so you can host it anywhere static.

## Operator run steps

1. **Config**: copy `config.sample.js` → `config.js` and fill:
   - `appClientId` — the multitenant workforce Entra app (`1e40baad-…`) that already carries `access_as_user` + Teams redirect URIs.
   - `bffBaseUrl` — https base URL of a running BFF reachable from Teams.
   - `entityType` — a membership-bearing entity to query (e.g. `sprk_matter`).
   - `bffScope` — the BFF audience/scope the workforce token must target (`api://<bff-app-id>/…`). **Broker-only (NFR-02): this token goes to the BFF only.**
2. **Host** `index.html` + the two `.js` files at an **https** URL. Add that host to the app's **valid domains**.
3. **Manifest**: fill the `TODO:` placeholders in `manifest.json`, zip it with a `color.png` (192×192) + `outline.png` (32×32), and **sideload** via Teams → Apps → Manage your apps → Upload a custom app.
4. **Run A** (systemuser): open the tab as a provisioned systemuser — expect no second login, `200` + membership rows. Repeat in Teams **web**.
5. **Run B** (contact-only): open as a workforce user with no systemuser row — expect **401 today** (confirms the tasks 020/021 gap; not a failure).
6. Record results in `../foundation-spike-findings.md` §5.

## Interpreting results

| Observation | Meaning |
|---|---|
| Token acquired, BFF `200` + rows (systemuser) | **Run A GO** — the load-bearing assumption holds |
| Token acquired, BFF `401` (contact-only) | **Run B CONDITIONAL-GO** — expected; 020/021 will wire the contact plane |
| `getAuthToken` fails / popup blocked (desktop) | **NO-GO signal** — escalate per task `<escalation>`; this is the documented desktop-CA risk |
| BFF `401` for a *systemuser* | audience mismatch — check `aud` claim vs `bffScope`; or the systemuser's `azureactivedirectoryobjectid` isn't set |

## Notes

- **SSO alternative (NAA)**: `getAuthToken()` is the simplest workforce-SSO path and is sufficient for this spike. Nested App Auth (MSAL `@azure/msal-browser` + `createNestablePublicClientApplication`) is the production-grade path task 011 evaluates; it is noted in `teams-sso.js` but not required to close the spike.
- If the BFF audience differs from the app's exposed API, `getAuthToken` returns a token for the app's own App ID URI — the BFF must accept that audience directly (no OBO). Verify with the decoded `aud` claim shown on the page.
