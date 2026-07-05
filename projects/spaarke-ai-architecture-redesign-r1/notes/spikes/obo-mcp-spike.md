# Spike report — OBO exchange for Dataverse `mcp.tools` + `/api/mcp` (FR-P0-08, task 010)

> **Date**: 2026-07-05 · **Environment**: spaarkedev1 (`https://spaarkedev1.crm.dynamics.com`), tenant `a221a95e-6abc-4434-aecc-e48338a1b2f2`
> **Verdict**: **FAIL-with-path** — the OBO exchange for the delegated `mcp.tools` scope is blocked by a missing consent grant (AADSTS65001), NOT by any structural incompatibility. A control run through the identical OBO pipeline with `/.default` succeeds, and `/api/mcp` is live and enforcing the `mcp.tools` scope. One small admin action (documented below) would unblock a re-run.
> **Spike script**: [`obo-mcp-spike.ps1`](obo-mcp-spike.ps1) (throwaway; reads secrets from gitignored `config/secrets.local.json`; nothing in `src/`).

---

## 1. What was tested

Per the July 2026 research note (`notes/audit-inputs/research-dataverse-mcp-2026-07.md`, follow-up 1): can the confidential BFF client (SDAP-BFF-SPE-API, `1e40baad-e065-4aea-a8d4-4b7ab273458c`) OBO-exchange an inbound user token for the delegated **Dynamics CRM → `mcp.tools`** scope and call the GA Dataverse MCP endpoint at `/api/mcp` under the user's identity?

The flow mirrors the production OBO exemplar `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/Dataverse/DataverseUserClient.cs` (task 008): same confidential client, same config keys (`AzureAd:TenantId/ClientId/ClientSecret` equivalents from `config/*.local.json`), same `AcquireTokenOnBehalfOf` semantics — executed as raw `POST /oauth2/v2.0/token` with `grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer` for transparency of error codes.

## 2. Reproduction steps (exactly what ran)

1. **User assertion** — `az account get-access-token --resource api://1e40baad-...` as dev user `ralph.schroeder@spaarke.com`. Decoded claims (redacted to identity-shape only):
   - `aud=api://1e40baad-e065-4aea-a8d4-4b7ab273458c` (the BFF app — same audience a PCF/code page token has)
   - `scp=SDAP.Access user_impersonation`, `appid=04b07795-...` (Azure CLI public client)
2. **OBO exchange** — `POST https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token` with `client_id`/`client_secret` = BFF confidential client, `assertion` = token from step 1, `requested_token_use=on_behalf_of`, and each scope form below.
3. **MCP call** — `POST https://spaarkedev1.crm.dynamics.com/api/mcp` with JSON-RPC `initialize` (protocolVersion `2025-06-18`), `Accept: application/json, text/event-stream`.

## 3. Evidence — scope forms tried and exact results

| # | OBO scope requested | Result |
|---|---|---|
| 1 | `https://spaarkedev1.crm.dynamics.com/mcp.tools` (research-note form) | **`invalid_grant` / AADSTS65001**, suberror `consent_required`: "The user or administrator has not consented to use the application with ID '1e40baad-…' named 'SDAP-BFF-SPE-API'. Send an interactive authorization request for this user and resource." (trace `b0af9a9c-48da-448a-b430-7ce9883c5100`, 2026-07-05 21:55:35Z) |
| 2 | `https://spaarkedev1.crm.dynamics.com/api/mcp/mcp.tools` (form advertised by the resource metadata, §3.3) | **Identical AADSTS65001 / `consent_required`** (trace `ef56add0-96db-41b2-9799-fbda029a4100`, 2026-07-05 21:56:51Z) |
| 3 | `https://spaarkedev1.crm.dynamics.com/.default` (**control** — what `DataverseUserClient` uses in production) | **OBO SUCCESS**: Bearer token issued, `expires_in≈4027`, returned `scope=… user_impersonation … .default`; decoded Dataverse token: `aud=https://spaarkedev1.crm.dynamics.com`, `scp=user_impersonation`, `upn=ralph.schroeder@spaarke.com` — user identity preserved end-to-end |

### 3.1 `/api/mcp` behavior with the control token

`POST /api/mcp` with the `user_impersonation`-scoped OBO token → **HTTP 401**, empty body, header:

```
WWW-Authenticate: Bearer resource_metadata="https://spaarkedev1.crm.dynamics.com/.well-known/oauth-protected-resource"
```

i.e. the MCP endpoint is live and explicitly rejects non-`mcp.tools` tokens. The MCP `initialize`/`tools/list` steps were therefore never reachable in this run.

### 3.2 The scope EXISTS in the tenant

`az ad sp show --id 00000007-0000-0000-c000-000000000000` (Dynamics CRM first-party SP):

```json
{ "value": "mcp.tools", "id": "a4c5bee6-25ff-4bb5-b926-b7eb8062ae7a", "type": "User",
  "adminConsentDisplayName": "Access Dataverse MCP tools as organization users" }
```

`"type": "User"` — user-consentable, admin consent not structurally required (tenant policy may still demand it; conditional-access policy IDs `capolids` appeared in the 65001 claims).

### 3.3 Resource metadata confirms the required scope string

`GET https://spaarkedev1.crm.dynamics.com/.well-known/oauth-protected-resource`:

```json
{ "resource_name": "Dataverse MCP Server",
  "resource": "https://spaarkedev1.crm.dynamics.com/api/mcp",
  "authorization_servers": ["https://login.microsoftonline.com/a221a95e-.../v2.0"],
  "scopes_supported": ["openid","profile","offline_access",
                       "https://spaarkedev1.crm.dynamics.com/api/mcp/mcp.tools"],
  "code_challenge_methods_supported": ["S256"] }
```

### 3.4 Root cause — the exact missing grant

- **BFF app registration** (`az ad app show --id 1e40baad-...`) requests on resource `00000007-0000-0000-c000-000000000000` (Dynamics CRM) only ONE delegated scope: id `78ce3f0f-a1ce-49c2-8cde-64b5c0896db4` = `user_impersonation`. It does **not** request `mcp.tools` (`a4c5bee6-25ff-4bb5-b926-b7eb8062ae7a`).
- **Tenant grant** (`oauth2PermissionGrants` for BFF SP `d93c832e-9b1d-4ccc-a2a8-9419fbf3fc18`): the Dynamics CRM grant (consentType `AllPrincipals`) covers `user_impersonation` only.
- Because OBO uses pre-consented (static) permissions — no interactive consent UI is possible mid-exchange — the missing grant fails the exchange with AADSTS65001 exactly as the task's "likely failure mode" predicted.

## 4. The unblock path (admin actions — NOT performed, per task boundary)

1. **Add the delegated permission** to app `1e40baad-e065-4aea-a8d4-4b7ab273458c`: resource `00000007-0000-0000-c000-000000000000` (Dynamics CRM), scope id `a4c5bee6-25ff-4bb5-b926-b7eb8062ae7a` (`mcp.tools`), type `Scope`.
2. **Grant admin consent** for it (extend the existing `AllPrincipals` grant `user_impersonation` → `user_impersonation mcp.tools`), e.g. Entra portal "Grant admin consent" or Graph `oauth2PermissionGrants` PATCH.
3. **PPAC allow-list** — per the research note + Learn "other clients" doc, the client ID must also be allow-listed for MCP in the Power Platform admin center for the environment.
4. Re-run `obo-mcp-spike.ps1` (default scope form). Expected: OBO succeeds → `initialize` + `tools/list` return the GA tool surface under the user's roles.

## 5. What this means for per-tool MCP transport (recommendation)

**The per-tool MCP transport option REMAINS OPEN.** Nothing observed suggests a structural blocker:

- The OBO **mechanics** are proven end-to-end for this exact confidential client against this exact resource (control run #3 issues a user-identity Dataverse token). `mcp.tools` is an ordinary delegated scope on the same Dynamics CRM resource — after consent, the same exchange has no known reason to behave differently.
- `/api/mcp` is **GA and live** on spaarkedev1, correctly advertising its OAuth protected-resource metadata and enforcing scope — consistent with the research note.
- The failure is a **two-line consent/config gap** (plus PPAC allow-list), not a protocol or platform incompatibility. Entra's "no documented app-only flow" concern is confirmed irrelevant to the BFF path: OBO is delegated by construction.

**Recommendation** (per revised D10 framing): proceed unchanged with native `dataverse.*` handlers over BFF OBO → Web API (`/.default`) as the runtime path — no metering, no new grant, proven today. Treat runtime MCP transport as a **deferred, viable** option: when/if a per-tool swap is actually wanted, execute §4's admin actions first, then re-run this spike to completion (initialize + tools/list + one metered `search_data` sanity call) before committing. Do not add the `mcp.tools` grant speculatively — it widens the BFF's delegated surface and, per the research note, MCP calls are metered.

## 6. Boundaries / hygiene check

- ✅ No `src/**` changes; spike script + report live in `notes/spikes/` only
- ✅ No app registration, consent, or PPAC changes made (verdict is FAIL-with-path; stopped per protocol)
- ✅ No secrets or raw tokens in this report or the script (script reads the gitignored `config/secrets.local.json`; report shows claim shapes and AADSTS traces only)
