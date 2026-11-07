# Copilot connector registration — dev (DRAFT, task 002)

> Draft runbook for ADR-028 A10. Task 042 finalises it into `docs/guides/SPAARKE-COPILOT-CONNECTOR-RUNBOOK.md` once the connector is re-pointed at the real endpoint.
> **Who runs what:** the owner (Global Admin) runs every portal step. The agent only prepares the steps, records the results and verifies with read-only queries.
> **No secret values in this repo.** If a client secret turns out to be needed, it is created only after the owner confirms in-session, and it lives only in the Teams Developer Portal registration.

## Fixed values (dev)

| Item | Value | Source |
|---|---|---|
| Spaarke tenant | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | `az account show`, 2026-11-07 |
| BFF app (resource) | `SDAP-BFF-SPE-API`, `1e40baad-e065-4aea-a8d4-4b7ab273458c` | `az ad app show`, 2026-11-07 |
| Scope | `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation` | A10 (= A8 scope) |
| Authorize endpoint | `https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/oauth2/v2.0/authorize` | A10 (Spaarke-tenant authority) |
| Token endpoint | `https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/oauth2/v2.0/token` | A10 |
| Dev BFF base URL | `https://spaarke-bff-dev.azurewebsites.net` | `src/solutions/CopilotAgent/appPackage/env.dev.json` |
| MCP endpoint (spike) | `https://spaarke-bff-dev.azurewebsites.net/mcp/legal-operations` | live only after the spike deploys |
| Client display name | `Spaarke Copilot Connector – dev` (en dash) | naming note §1 |
| Connector display name | `Spaarke` | naming note §1 |

## (a) Entra app registration: "Spaarke Copilot Connector – dev" — run by the owner

1. Entra admin center › App registrations › New registration.
   - Name: `Spaarke Copilot Connector – dev`.
   - Supported account types: **this organizational directory only** (single tenant).
   - Redirect URI: leave empty for now. It comes from step (b).
2. Overview: record the **Application (client) ID**.
3. Authentication › Advanced settings › **Allow public client flows = Yes** (public client, PKCE, no credential).
4. API permissions › Add › My APIs › `SDAP-BFF-SPE-API` › Delegated › `user_impersonation`. Then **Grant admin consent for Spaarke**.
5. Certificates & secrets: **create nothing.**
6. Pre-authorize the client on the BFF app:
   - `SDAP-BFF-SPE-API` › Expose an API › Authorized client applications › Add.
   - Client ID from step 2; scope `user_impersonation`.

## (b) Teams Developer Portal OAuth client registration — run by the owner

1. Open https://dev.teams.microsoft.com › Tools › **OAuth client registration** › Register client.
2. Fill in:
   - Name: `Spaarke Copilot Connector – dev`
   - Base URL: `https://spaarke-bff-dev.azurewebsites.net`
   - Restrict usage by org: home tenant
   - Client ID: from (a) step 2
   - Authorization endpoint and token endpoint: from the table above
   - Refresh endpoint: the token endpoint
   - Scope: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation`
   - PKCE: **on**
   - Client secret: **leave empty.** If the portal refuses to save without one, STOP. That is the escalation point: the agent asks the owner to confirm before any secret is created.
3. Save. Record:
   - the **OAuth client registration ID** (the connector needs it in step (c));
   - the **redirect URI** the portal shows.
4. Back in Entra:
   - Open the connector app › Authentication › Add a platform › **Mobile and desktop applications** › add the portal's redirect URI.
   - Use this platform because a Web redirect would force a client secret at token redemption, and an SPA redirect only accepts browser (cross-origin) redemption.
   - If T5 shows token redemption failing, the next thing to try is the **Web** platform plus the confidential-client branch (needs owner confirmation).

## (c) Copilot connector "Spaarke" — run by the owner, AFTER the spike is live on dev

Gate: uac-r2 has deployed the spike, and anonymous `/mcp/legal-operations` returns 401.

1. Microsoft 365 admin center › Copilot › Connectors › Gallery › Created by your org › **Connect to MCP server**.
2. Fill in:
   - Display name `Spaarke`;
   - URL `https://spaarke-bff-dev.azurewebsites.net/mcp/legal-operations`;
   - Auth: **OAuth 2.0** with the registration ID from (b). **Never Entra SSO** (A10 MUST NOT).
   - Description: a short one.
3. Record the connector ID and the date.

## (d) T5 — run by a licensed NON-admin user, with the agent recording

1. Copilot › Settings › Sources › Spaarke › **Connect**, then sign in.
2. Ask Copilot to call `spike_whoami`.
3. Record:
   - whether the decoded claim names `aud`, `scp`, `tid` and `oid` are present;
   - the returned `systemuserid`;
   - the date;
   - pass or fail.

## Scriptable?

| Step | Scriptable | How |
|---|---|---|
| (a) | Yes | `az ad app create`, `az ad app permission add`/`admin-consent`, Graph PATCH for `preAuthorizedApplications` (later stamps) |
| (b) | Not known (verify) | Dev Portal UI; a Graph/Teams API is not confirmed |
| (c) | Not known (verify) | Admin center UI |
| (d) | No | Interactive user consent |
