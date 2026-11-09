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

> **Updated 2026-11-08 (live finding):** the portal **requires a client secret**, so the public-client steps below are superseded. The owner confirmed the secret. They create it in Entra (Certificates & secrets; dev expiry up to 24 months under ADR-028 A10 Path A) and paste it straight into the portal; the agent never sees it. The connector app is now a confidential client (public client flows off), and the portal's redirect URI goes on the **Web** platform, not Mobile and desktop. See `notes/decisions.md` 2026-11-08 and `notes/phase0-registration-facts.md`.

1. Entra › the connector app › Certificates & secrets › **New client secret** (owner only, after confirming in-session).
   - Description `Teams Dev Portal OAuth – dev`; Custom expiry (dev: up to 24 months, ADR-028 A10 Path A; customer stamps: 180 days).
   - Copy the **Value** (not the Secret ID); it is shown once. Never paste it into a chat, file or Key Vault.
2. Open https://dev.teams.microsoft.com › Tools › **OAuth client registration** › Register client.
3. Fill in:
   - Name: `Spaarke Copilot Connector – dev`
   - Base URL: `https://spaarke-bff-dev.azurewebsites.net`
   - Restrict usage by org: My organization only (home tenant)
   - Restrict usage by app: Any Teams app
   - Client ID: from (a) step 2
   - Client secret: the Value from step 1
   - Authorization endpoint and token endpoint: from the table above
   - Refresh endpoint: the token endpoint
   - Scope: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation`
   - PKCE: on, if offered
4. Save. Record the **OAuth client registration ID** (base64 of `{tenantId}##{guid}`; the connector needs it in step (c)) and the secret's expiry date. The portal does **not** show a redirect URI.
5. Back in Entra, add `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect` to the connector app on the **Web** platform (confidential client). If T5's sign-in fails with `AADSTS50011`, the error names the redirect actually used; add that one instead.

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
