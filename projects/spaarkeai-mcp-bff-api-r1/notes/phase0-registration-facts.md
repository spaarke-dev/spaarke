# Phase 0 registration facts — dev (task 002)

> Non-secret identifiers only. No client secret exists for the connector app; creating one requires the owner's in-session confirmation (runbook step (b)).

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
| Credentials | none (0 passwords, 0 certificates) |
| Requested permission | BFF `1e40baad-…` delegated `user_impersonation` (`18afc847-a660-40b2-8925-747aed613f36`) |
| Admin consent | granted: `oauth2PermissionGrant` consentType `AllPrincipals`, scope `user_impersonation` |
| Redirect URIs | none yet; the Dev Portal redirect goes on the **Mobile and desktop** platform after step (b) |

**BFF app change** (`SDAP-BFF-SPE-API`, object `c2aab303-50f8-4279-9934-503ab3a4b357`):
- `api.preAuthorizedApplications` went from 10 to 11 entries. Added `04f37fe4-…` with `user_impersonation`.
- Verified after the PATCH:
  - the 10 existing entries are byte-identical;
  - `oauth2PermissionScopes` are unchanged;
  - `requestedAccessTokenVersion` is still 2;
  - `knownClientApplications` are unchanged.
- **Rollback:** PATCH `api.preAuthorizedApplications` back to the 10 entries without `04f37fe4-…`. The pre-change `api` object was captured in the session scratchpad, not committed.

## (b) Teams Developer Portal OAuth registration — pending (owner)

- Registration ID: _tbd_
- Redirect URI shown by the portal: _tbd_
- Secret required? **Yes** (the portal will not save without one, 2026-11-08). The owner confirmed it and creates it in Entra, pasting it straight into the portal; the value is never in the repo or the agent. Expiry: _tbd_ (dev Path A: up to 24 months). Named owner: the project owner.

## (c) Copilot connector "Spaarke" — pending (owner)

- Connector ID / date: _tbd_

## (d) T5 — pending
