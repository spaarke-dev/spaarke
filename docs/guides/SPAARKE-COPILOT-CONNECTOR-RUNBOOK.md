# Spaarke MCP connector runbook (Microsoft 365 Copilot and Cowork)

> **Last Updated**: 2026-11-24
> **Purpose**: Register, verify, rotate and roll back the "Spaarke MCP" Copilot connector, its Entra client app and the "Spaarke" plugin package, so a second admin can reproduce the dev registration.
> **Audience**: A Global Administrator (runs every portal step) and the engineer or agent who prepares and verifies.
> **Status**: Written from the dev registration of 2026-11-08 to 2026-11-24 (`projects/spaarkeai-mcp-bff-api-r1/notes/phase0-registration-facts.md`). It has not yet been followed from scratch by a second admin; the first one to do so records what they had to fix here.

---

## Prerequisites

- [ ] The BFF with the MCP server is deployed to the target environment and `Mcp__Enabled` is `true` there (see [Kill switch and rollback](#kill-switch-and-rollback)). Dev: `https://spaarke-bff-dev.azurewebsites.net`.
- [ ] You hold the Global Administrator role in the Spaarke tenant. The same person creates the Teams Developer Portal registration and the connector, and holds the AI Administrator role (the research notes rank "same admin for both" as a precaution, `notes/phase0-results.md` and `notes/phase0-registration-facts.md` (c)).
- [ ] Every user who will use the connector has a **Microsoft 365 Copilot licence** (see [Users](#users)).
- [ ] Node 20 or later, only to build the plugin package.

**Who runs what.** The Global Admin runs every portal step (Entra, Teams Developer Portal, Microsoft 365 admin center). The agent prepares the values, runs read-only checks (Azure CLI reads, App Insights queries, HTTP probes) and records results. The agent never sees a secret value and never enters one anywhere. Creating or rotating the client secret needs the owner's confirmation in the session first (root `CLAUDE.md` §9).

## Quick Reference

Values below are the dev environment. Tenant ids and application ids are not secrets and may be written down.

| Item | Value |
|---|---|
| Spaarke tenant | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| BFF app (the resource) | `SDAP-BFF-SPE-API`, `1e40baad-e065-4aea-a8d4-4b7ab273458c`, object id `c2aab303-50f8-4279-9934-503ab3a4b357` |
| Scope | `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation` |
| Connector client app | `Spaarke Copilot Connector – dev` (en dash, U+2013), application id `04f37fe4-5714-46f9-8796-5363a7a52244`, object id `5975f073-159b-4b15-a6e1-2c188d408849` |
| Redirect URI (Web platform) | `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect` |
| Authorize endpoint | `https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/oauth2/v2.0/authorize` |
| Token and refresh endpoint | `https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/oauth2/v2.0/token` |
| MCP URL (dev) | `https://spaarke-bff-dev.azurewebsites.net/mcp/legal-operations` |
| Connector display name | `Spaarke MCP` (Copilot shows its tools as `SpaarkeMCP_…`) |
| Plugin package | display name `Spaarke`, slug `spaarke`, manifest 1.30, built from `src/solutions/CopilotPlugins/spaarke/`, output `dist/spaarke-plugin.zip` |
| Current dev client secret | description `Teams Dev Portal OAuth – dev 2`, keyId `3f36b679-8141-4858-ac05-8b11fd1985cc`, **expires 2028-11-09** |
| Where the secret lives | Only in the Teams Developer Portal OAuth registration. Never in chat, a file, the repo or Key Vault. |

**Why these shapes.** ADR-028 Amendment A10 (`.claude/adr/ADR-028-spaarke-auth-architecture.md`) requires OAuth 2.0 with an authority pinned to the Spaarke tenant, a dedicated single-tenant connector client pre-authorized on the BFF app, and forbids the connector's Entra SSO option. The connector client is **confidential**, not the public client with PKCE that A10 prefers, because the Developer Portal will not save a registration without a client secret (found 2026-11-08, recorded in `projects/spaarkeai-mcp-bff-api-r1/notes/decisions.md`). The architecture is in [`SPAARKE-MCP-SERVER-ARCHITECTURE.md`](../architecture/SPAARKE-MCP-SERVER-ARCHITECTURE.md).

## Procedure

Run the steps in order. Each ends with a check.

### Step 1: Entra app "Spaarke Copilot Connector – dev" (Global Admin)

1. Entra admin center › App registrations › New registration.
   - Name `Spaarke Copilot Connector – dev`; supported account types: **this organizational directory only**; redirect URI: leave empty.
   - Record the **Application (client) ID**.
2. Authentication:
   - Add the platform **Web** and the redirect URI `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`. Add no SPA or Mobile and desktop redirect. Leave both implicit-grant boxes off.
   - **Allow public client flows = No** (confidential client).
   - Command-line alternative: `az ad app update --id <connector app id> --web-redirect-uris "https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect"`.
3. API permissions › Add a permission › My APIs › `SDAP-BFF-SPE-API` › Delegated › `user_impersonation` (permission id `18afc847-a660-40b2-8925-747aed613f36`). Then **Grant admin consent for Spaarke**. Admin consent is consent type `AllPrincipals`.
4. Pre-authorize the client on the BFF app: `SDAP-BFF-SPE-API` › Expose an API › Authorized client applications › Add the connector's client id with the `user_impersonation` scope.
   - Dev: this took `api.preAuthorizedApplications` from 10 to 11 entries and left the 10 existing entries, the scopes, `requestedAccessTokenVersion` (2) and `knownClientApplications` unchanged.
   - Do not change the shared BFF app any other way.
5. Do **not** create a secret yet.

**Check (agent, read-only):** `az ad app show --id <connector app id>` reports `signInAudience` `AzureADMyOrg`, `isFallbackPublicClient` false, one Web redirect, and `requiredResourceAccess` naming only the BFF `user_impersonation`. `az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query api.preAuthorizedApplications` lists the connector's client id.

### Step 2: Client secret (Global Admin; owner confirms in-session first)

1. Entra › the connector app › Certificates & secrets › **New client secret**.
   - Description `Teams Dev Portal OAuth – dev` (add a number on later ones, for example `… dev 2`).
   - Expiry: dev may be up to 24 months (ADR-028 A10, resolution Path A, recorded in `notes/decisions.md` 2026-11-08). **Customer stamps stay at 180 days at most, and each customer stamp has its own connector client.**
2. Copy the **Value** (not the Secret ID). It is shown once.
3. Paste it straight into the Developer Portal in Step 3. Do not paste it into a chat, file, screenshot, the repo or Key Vault. (The first dev secret had to be deleted because its value appeared in a screenshot.)

**Check (agent, read-only):** `az ad app credential list --id <connector app id>` shows exactly one password with the expected `displayName`, `keyId` and end date. It never shows a value. Record the keyId and end date; never the value.

### Step 3: Teams Developer Portal OAuth client registration (Global Admin)

Open <https://dev.teams.microsoft.com> › Tools › **OAuth client registration** › Register client, and fill in:

| Field | Value |
|---|---|
| Name | `Spaarke Copilot Connector – dev` |
| Base URL | the **full MCP URL**: `https://spaarke-bff-dev.azurewebsites.net/mcp/legal-operations` |
| Restrict usage by org | My organization only (home tenant) |
| Restrict usage by app | Any Teams app |
| Client ID | the connector's application id |
| Client secret | the Value from Step 2 |
| Authorization endpoint | the authorize endpoint in the Quick Reference |
| Token endpoint | the token endpoint in the Quick Reference |
| Refresh endpoint | the same token endpoint |
| Scope | `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation offline_access` |
| PKCE | on, if the portal offers it |

Save, then copy the **OAuth client registration ID** with the portal's copy button (it is base64 of `{tenantId}##{registration guid}`). No trailing spaces in the Client ID or the secret. The portal shows no redirect URI; the fixed one in the Quick Reference is already on the Entra app.

> **Do not use a host-only Base URL.** The first dev registration (Base URL `https://spaarke-bff-dev.azurewebsites.net`) made the admin center answer "The requested resource does not exist" when the connector was authorized, and no request reached the BFF. Deleting the registration and re-creating it with the full MCP URL fixed it (2026-11-10).

If a later sign-in fails with `AADSTS50011`, the error names the redirect actually sent: add that exact value to the Web platform of the Entra app.

### Step 4: The Copilot connector "Spaarke MCP" (Global Admin)

1. Microsoft 365 admin center › Copilot › Connectors › Gallery › Created by your org › **Connect to MCP server**.
2. Fill in:
   - Display name `Spaarke MCP`.
   - URL: the MCP URL.
   - Authentication type: **OAuth 2.0**, with the registration ID from Step 3 as the Reference ID. **Never choose Entra SSO**: A10 forbids it, because it issues tokens from the user's home directory, which match no Spaarke `systemuser` and cannot be exchanged on-behalf-of into Spaarke Dataverse.
   - Description: one short sentence.
3. Authorize, and record the date.

The tenant registers the server **once**, as this connector. Do not also add an `agentConnectors` entry for `/mcp/legal-operations` to a plugin package: with both registered Copilot saw two copies of each tool and one prompt ran the same tool twice (`notes/phase0-results.md` T2). The tenant-wide inventory and registration rules are owned by copilot-platform-coordination (GitHub issue #3006).

### Step 5: The plugin package "Spaarke" (agent builds; Global Admin uploads)

The package carries **skills only** (`spaarke-decisions`) and no connector. A packaged skill calls the single "Spaarke MCP" connector (`notes/phase0-results.md` T10).

1. Build (agent):

   ```bash
   cd src/solutions/CopilotPlugins/spaarke
   npm run build
   ```

   `npm run build` runs `scripts/validate-plugin.mjs` first and refuses to build an invalid package, then writes `dist/spaarke-plugin.zip` (manifest and icons at the zip root, `skills/spaarke-decisions/` beside them). `npm test` runs the gate's own tests. `dist/` is git-ignored.
2. Upload (Global Admin): Microsoft 365 admin center › Agents › Tools › **Registry** › upload `spaarke-plugin.zip`, Installed for **All users** (dev: the owner's choice per upload). Approve it there.
3. Wait. The plugin appeared under Browse plugins 10 to 30 minutes after the first spike upload (2026-11-23).
4. Cowork users turn it on once: Cowork › Customize › Spaarke. After the first turn-on the skill can fail for a few minutes while its content syncs; retry in a new task (T10, first run).

**Check:** in Cowork, `/` lists `/spaarke-decisions` with the badge "Spaarke", and the task's Plugins panel shows "Spaarke (decisions)" beside "Spaarke MCP".

## Users

- Each user needs a **Microsoft 365 Copilot licence**. Microsoft's federated-connector overview lists it as a condition for seeing a connector; an unlicensed Copilot Chat ("Basic") user does not see "Spaarke MCP". Dev test account: Microsoft 365 Business Standard with Copilot plus the Copilot Business add-on.
- Each user signs in to the connector **themselves**, once: Copilot › Settings › Sources › Spaarke MCP › Connect (OAuth sign-in). There is no Entra SSO and no admin pre-connect. The admin's own account is a pre-check only; test as a licensed non-admin.
- The signed-in user must be a licensed Spaarke core user with a Dataverse `systemuser`. Any other caller gets the `UserContextRequired` refusal (A10).
- In Chat, select Spaarke MCP as a source in the chat, or Copilot may search another connected MCP server (for example Dataverse) instead.

## Verification

Run after Step 4, after Step 5 and after any BFF deploy.

1. **Anonymous request is refused.** `POST` to the MCP URL with no token, `Accept: application/json, text/event-stream` and an `initialize` body:
   - expected: `401` with `WWW-Authenticate: Bearer`, whatever `Mcp__Enabled` is (authorization runs before the kill switch).
   - `GET` on `/healthz` returns `200`.
2. **Sign-in works.** A licensed non-admin connects Spaarke MCP. Query dev Application Insights (`spe-insights-dev-67e2xz`) for requests to `/mcp/legal-operations`: an authenticated `initialize` 200, `notifications/initialized` 202, `tools/list` 200 from client `CopilotConnector`. A `GET` answered 405 is expected (the server is stateless, no SSE stream). Application Insights samples some rows (25 to 33 percent seen), so a missing row is not proof a call did not happen; the chat result is the primary evidence.
3. **The tool list is the five v1 tools.** `tools/list` returns exactly `spaarke_show_decisions`, `spaarke_get_decision_plan`, `spaarke_list_decision_page`, `spaarke_search_decision_lookup` and `spaarke_complete_decision`, and no spike tools. The admin center connector page lists the tools the server returned. The same assertion runs in CI as `RealBffHost_ToolsList_WhenEnabledWithAValidToken_IsExactlyTheFiveV1Tools` (`tests/integration/contract/Api/Mcp/McpHostContractTests.cs`). Copilot itself shows the model only the model-visible tool, `spaarke_show_decisions`; the other four are app-only.
4. **The widget renders.** As the licensed user, in Copilot Chat (Spaarke MCP selected) ask "what decisions are waiting for me in Spaarke?", and in Cowork run `/spaarke-decisions`. Expect the decisions widget with the lane counts and a Start review button that opens the Decision wizard. `resources/read` for `ui://spaarke/decisions.html` returns `text/html;profile=mcp-app`.
5. **Identity is the user's.** The first live proof was `spike_whoami` (T5, 2026-11-12): `aud` the BFF app, `scp` containing `user_impersonation`, `tid` the Spaarke tenant, and the `oid` equal to the user's Entra object id.
6. **Duplicates.** Ask Copilot to list the Spaarke tools it has: one copy of each, none twice, no spike tools.

Word and Outlook are not wizard surfaces in v1 (Word runs the tool without the widget after an approval card; Outlook does not offer the connector), see `notes/phase0-results.md` T1.

## Secret rotation

The current dev secret expires **2028-11-09**. Rotate it at least 30 days before (by 2028-10-10), or at once if a value is ever exposed. ADR-028 A10 needs a named owner (the project owner), an expiry within policy and a human confirming the rotation. Customer stamps rotate within 180 days.

1. **Owner confirms in-session** that the rotation may start (root `CLAUDE.md` §9).
2. **Create the new secret first.** Entra › connector app › Certificates & secrets › New client secret, description `Teams Dev Portal OAuth – dev {n+1}`, expiry per the rule above. Copy the Value once. Two secrets can exist at the same time, so nothing breaks yet.
3. **Update the Developer Portal registration.** Developer Portal › Tools › OAuth client registration › open the registration › replace the client secret with the new Value › Save. Do not paste the value anywhere else.
   - Not yet verified: whether editing keeps the registration ID. After Save, compare the ID with the one the connector holds. If the ID changed, update the connector's Reference ID (Step 4) before verifying. If the portal will not edit the secret in place, create a new registration as in Step 3, update the connector, and delete the old registration.
4. **Verify with a fresh sign-in.** In a new InPrivate window, a licensed non-admin disconnects and reconnects Spaarke MCP, then runs the widget prompt (Verification 4). Also check one account that was already connected before the change still works. App Insights shows authenticated `/mcp/` requests after the change time.
5. **Delete the old secret** only after step 4 passes: Entra › Certificates & secrets › delete the old keyId.
6. **Check (agent, read-only):** `az ad app credential list --id <connector app id>` shows exactly one password, the new keyId and end date.
7. **Record** the new keyId and end date in the Quick Reference of this file, in the same change. Never the value.

If step 4 fails, put the old secret back in the Developer Portal (it still exists until step 5). That is why the old secret is deleted last.

## Kill switch and rollback

| Action | How | Effect |
|---|---|---|
| **Kill switch** | Unset or set `false` the App Service setting `Mcp__Enabled` and restart the app. Dev: `spaarke-bff-dev`. | The route stays mapped. Authenticated callers get `503` ProblemDetails with `errorCode` `mcp.disabled` and no tool runs. Anonymous callers still get `401`. The setting is read once at startup, so a restart is needed. |
| Previous BFF build | Redeploy the previous SHA with the `bff-deploy` skill (dev deploys go through the uac-r2 session, with the owner's go per SHA). | Back to the earlier code. |
| Stop Copilot reaching it | Microsoft 365 admin center › remove or turn off the "Spaarke MCP" connector; remove "Spaarke" from Agents › Tools › Registry. | Copilot and Cowork stop calling. |
| Cut sign-in for everyone | Delete the client secret in Entra, or remove the connector app from the BFF app's authorized client applications. | New and refreshed sign-ins fail. The pre-change `preAuthorizedApplications` list is the 10 entries without the connector (dev, 2026-11-08). |

Turning the switch on in a new environment is a per-environment owner decision. Dev was switched on first, 2026-11-24.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Admin center: "The requested resource does not exist" at Authorize | Developer Portal registration with a host-only Base URL | Re-create the registration with the full MCP URL (Step 3). |
| Developer Portal will not save the registration | No client secret | Create one (Step 2); the connector client is confidential. |
| Sign-in fails, `AADSTS50011` | Redirect URI mismatch | Add the redirect the error names to the Entra app's Web platform. |
| Anonymous probe returns `503` | Probe was sent with a valid token, or a proxy changed the answer | An anonymous caller must get `401`. A signed-in `503` means `Mcp__Enabled` is off. |
| Signed in but `401` `mcp.token_not_accepted` | A10 refused the token: not Spaarke tenant, not the exact v2 issuer, not the BFF audience, an app-only token, or no `user_impersonation` | Check the connector was not set to Entra SSO; check the scope on the registration. The BFF logs only the deny reason. |
| The user cannot see "Spaarke MCP" | No Microsoft 365 Copilot licence | Assign one (Prerequisites). |
| Copilot answers it cannot call the tool | The chat searched another MCP server | Select Spaarke MCP as a source in the chat. |
| Cowork: tools not available | The Spaarke MCP plugin is listed but turned off | Cowork › Customize, switch it on. |
| The skill "failed" right after the plugin was turned on | Skill content still syncing | Retry in a new task after a few minutes. |
| Every tool runs twice | The server is registered twice (connector plus a package `agentConnectors`) | Keep one registration (Step 4). |
| Widget does not render but text answer works | Host does not support MCP Apps (Word, Outlook) | Use Chat or Cowork. |

## Related

- [`SPAARKE-MCP-SERVER-ARCHITECTURE.md`](../architecture/SPAARKE-MCP-SERVER-ARCHITECTURE.md): the server, its auth flow and its tools.
- [`HOW-TO-ADD-AN-MCP-TOOL.md`](HOW-TO-ADD-AN-MCP-TOOL.md): add a tool or an area.
- [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md), Amendment A10.
- [`M365-COPILOT-DEPLOYMENT-GUIDE.md`](M365-COPILOT-DEPLOYMENT-GUIDE.md): the earlier declarative agent "Spaarke AI" (a different package type and auth plane).
- `src/solutions/CopilotPlugins/spaarke/`: the plugin package source.
