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

- **Superseded 2026-11-10:** the registration below was deleted and re-created with Base URL = the full MCP URL. See (c).
- Old registration ID: `YTIyMWE5NWUtNmFiYy00NDM0LWFlY2MtZTQ4MzM4YTFiMmYyIyMyMmNjNzMzYS0yMTU1LTQ0M2YtOThlNy1mOGM0ZDU4YzgyYmU=`. It is base64 for `a221a95e-6abc-4434-aecc-e48338a1b2f2##22cc733a-2155-443f-98e7-f8c4d58c82be`, the tenant ID plus the registration GUID. The connector in step (c) needs this ID.
- Redirect URI: the portal does **not** display one for an OAuth client registration. Microsoft's guidance for Teams/Copilot OAuth registrations is the fixed `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`, on the connector app's **Web** platform. If T5's sign-in fails with `AADSTS50011`, the error names the redirect actually used; correct the redirect to that value.
- Secret: **required** (the portal will not save without one, 2026-11-08). The owner created it in Entra and pasted it straight into the portal; the value is never in the repo or the agent. **Expiry: 2028-11-08** (24 months, ADR-028 A10 Path A, dev only). Named owner: the project owner. Rotate before expiry (runbook owed with task 052).

## (c) Copilot connector "Spaarke" — pending (owner)

- Gate checked 2026-11-09: an anonymous `POST /mcp/legal-operations` (initialize, with the MCP Accept header) on the dev BFF returns `401`, `WWW-Authenticate: Bearer`. The spike is live and auth is enforced.
- **Blocked 2026-11-09:** the admin center form ("Connect to MCP server": Authentication type OAuth 2.0, Reference ID = the (b) registration ID) answers **Authorize** with "The requested resource does not exist." The ID was pasted correctly and the type was OAuth 2.0. It still failed after the owner added `offline_access` to the registration scope. At that point the registration's Base URL was still host-only.
  - **The click never reached our server.** Over the 4 hours covering the attempts, the dev BFF's Application Insights (`spe-insights-dev-67e2xz`, unsampled: rows = itemCount) shows no request to `/mcp/*` or `/.well-known/*` apart from the agent's own probes; the rest was the app, `/healthz` and always-on `GET /`. So the failure is Microsoft's lookup of the registration. The missing RFC 9728 protected-resource metadata on the spike is not the cause at this step.
  - **Discovery change on hold.** Serving that metadata needs a new anonymous route, a Permanent `AnonymousByDesign` waiver in the route-auth ledger (which conflicts with the uac-r2 spike condition) and a deploy. It is not built.
  - **Next checks, in order:** (1) Base URL = the full MCP endpoint URL; (2) wait 10–15 minutes after saving; (3) delete and re-create the Dev Portal registration with Base URL = MCP URL from the start; (4) Microsoft support / Q&A.
  - **Research 2026-11-09** (researcher memory `federated-connector-reference-id-not-exist-2026-11-09.md`): the error is undocumented, with no public report of it. The setup meets every documented requirement.
    - **Refuted:** the admin center expecting an Agents Toolkit registration; licence or Frontier (a missing licence hides the form rather than failing the lookup); DCR.
    - **Plausible but unproven:** base URL (the Dev Portal treats it as a prefix, so host-only should pass); propagation; a stale registration after an edit.
    - **Ranked steps:** (1) a **fresh** registration with Base URL = MCP URL, the ID copied with the portal's copy button, retried in a new InPrivate window after 15 minutes; (2) the same Global Admin creates both the registration and the connector, holding the AI Administrator role; (3) no trailing spaces in the registration's Client ID or secret; (4) a Microsoft support case with the admin center's failing network request (dev tools) and the fact that no request reached the BFF.
    - **Rejected:** the agent's "try Entra SSO as a diagnostic" — A10 forbids that connector option and it issues tokens of a different shape; it needs an owner decision first.

- **Unblocked 2026-11-10 (owner).** The Dev Portal registration was deleted and re-created with Base URL = the full MCP URL and a new secret. The owner reports the connector is **linked and ready to use**.
  - Connector display name: **`Spaarke MCP`**. This deviates from naming note §1 (`Spaarke`); the owner was told and the rename is open.
  - Secret, read 2026-11-10 (metadata only): exactly one password, `Teams Dev Portal OAuth – dev 2`, keyId `3f36b679-8141-4858-ac05-8b11fd1985cc`, start 2026-11-10 18:39 local, end **2028-11-09 18:39** local. The old keyId `fafba383-…`, whose value was exposed in a screenshot, is deleted. Web redirect unchanged; `isFallbackPublicClient` false.
  - App Insights, 3 hours to 18:52Z: no request to `/mcp/*` or `/.well-known/*` from Microsoft yet. Linking did not call the server.
  - New registration ID: _tbd (owner)_ · Connector ID: _tbd (owner)_ · Date: 2026-11-10

## (d) T5 — pending
