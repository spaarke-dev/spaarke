# Phase 0 registration facts — dev (task 002)

> Non-secret identifiers only. The connector app has one client secret, created by the owner on 2026-11-08 and held only in the Teams Developer Portal registration; its value is never recorded here.

## (a) Entra app — done 2026-11-08

Created by the main session with the Azure CLI under the owner's signed-in account, on the owner's explicit "yes, run it" (2026-11-08).

| Item | Value |
|---|---|
| Display name | `Spaarke Copilot Connector – dev` (en dash, U+2013, verified in Graph) |
| Application (client) ID | `04f37fe4-5714-46f9-8796-5363a7a52244` |
| App object ID | `5975f073-159b-4b15-a6e1-2c188d408849` |
| Service principal object ID | `f841f04b-4d78-4c62-a012-0d988f402791` |
| Tenant | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| Sign-in audience | `AzureADMyOrg` (single tenant) |
| Allow public client flows | Set to Yes at creation; switched to **No** on 2026-11-08 because the Dev Portal requires a secret, which makes this a confidential client |
| Credentials | 1 password, 0 certificates (verified 2026-11-09 by reading metadata only): `Teams Dev Portal OAuth – dev`, keyId `fafba383-8b89-409f-8e5a-94b459e474ff`, start 2026-11-09 14:35 local, end **2028-11-08 14:35** local. `isFallbackPublicClient` is false. |
| Requested permission | BFF `1e40baad-…` delegated `user_impersonation` (`18afc847-a660-40b2-8925-747aed613f36`) |
| Admin consent | granted: `oauth2PermissionGrant` consentType `AllPrincipals`, scope `user_impersonation` |
| Redirect URIs | **Web:** `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`, added by the owner with `az ad app update --web-redirect-uris` on 2026-11-09. The read-back confirmed it, with no SPA or public-client redirects, implicit grant off for both tokens, and the audience, the secret count and `requiredResourceAccess` unchanged. |

**BFF app change** (`SDAP-BFF-SPE-API`, object `c2aab303-50f8-4279-9934-503ab3a4b357`):
- `api.preAuthorizedApplications` went from 10 to 11 entries. Added `04f37fe4-…` with `user_impersonation`.
- Verified after the PATCH:
  - the 10 existing entries are byte-identical;
  - `oauth2PermissionScopes` are unchanged;
  - `requestedAccessTokenVersion` is still 2;
  - `knownClientApplications` are unchanged.
- **Rollback:** PATCH `api.preAuthorizedApplications` back to the 10 entries without `04f37fe4-…`. The pre-change `api` object was captured in the session scratchpad, not committed.

## (b) Teams Developer Portal OAuth registration — done 2026-11-09 (owner)

- Registration ID: `YTIyMWE5NWUtNmFiYy00NDM0LWFlY2MtZTQ4MzM4YTFiMmYyIyMyMmNjNzMzYS0yMTU1LTQ0M2YtOThlNy1mOGM0ZDU4YzgyYmU=`. It is base64 for `a221a95e-6abc-4434-aecc-e48338a1b2f2##22cc733a-2155-443f-98e7-f8c4d58c82be`, the tenant ID plus the registration GUID. The connector in step (c) needs this ID.
- Redirect URI: the portal does **not** display one for an OAuth client registration. Microsoft's guidance for Teams/Copilot OAuth registrations is the fixed `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`, on the connector app's **Web** platform. If T5's sign-in fails with `AADSTS50011`, the error names the redirect actually used; correct the redirect to that value.
- Secret: **required** (the portal will not save without one, 2026-11-08). The owner created it in Entra and pasted it straight into the portal; the value is never in the repo or the agent. **Expiry: 2028-11-08** (24 months, ADR-028 A10 Path A, dev only). Named owner: the project owner. Rotate before expiry (runbook owed with task 052).

## (c) Copilot connector "Spaarke" — pending (owner)

- Gate checked 2026-11-09: an anonymous `POST /mcp/legal-operations` (initialize, with the MCP Accept header) on the dev BFF returns `401`, `WWW-Authenticate: Bearer`. The spike is live and auth is enforced.

- Connector ID / date: _tbd_

## (d) T5 — pending
