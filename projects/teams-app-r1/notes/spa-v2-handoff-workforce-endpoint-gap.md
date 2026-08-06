# Handoff to SPA v2 — Workforce (Teams) plane needs collaboration endpoints it doesn't have yet

> **From**: teams-app-r1 (Teams host for the collaboration SPA)
> **Date**: 2026-08-05
> **Status**: Auth chain proven end-to-end in a live Teams tab; blocked on a BFF endpoint-coverage decision.

## TL;DR

We added a **Teams host** to `external-spa` (one shared collaboration core, two hosts: the CIAM external SPA + a Teams workforce tab). In a live Teams tab, **workforce SSO/NAA now works end-to-end** — a workforce token is acquired and sent to the BFF. But the SPA then **401s on every data call**, because the SPA's entire data layer targets the **CIAM-only** `/api/v1/external/*` endpoints, and the **workforce plane was built as a minimal parallel group** (`/api/v1/collab`) that only has `/me` + a document-download route — not the projects/documents/events/etc. data surface. **The workforce host authenticates but has no data endpoints to call.**

This needs an architecture decision (below) that affects the shared SPA + BFF, hence the handoff.

## What works (verified live, Teams web client)

- SWA deploy of `external-spa` with `staticwebapp.config.json` framing: `Content-Security-Policy: frame-ancestors 'self' https://teams.microsoft.com https://*.cloud.microsoft`, no `X-Frame-Options` → **tab frames in Teams**.
- Host detection + Teams host adapter → selects the **workforce** auth strategy (not CIAM) inside Teams.
- **MSAL v5 NAA** (`createNestablePublicClientApplication`) acquires a **workforce** token for `api://1e40baad-…/access_as_user` and hands it to the BFF (broker-only; no downstream exchange).

## The blocker (the actual issue)

After sign-in the app calls **`GET /api/v1/external/me`** and gets **401**. Root cause is BFF endpoint topology:

| BFF group | Auth scheme | Endpoints |
|---|---|---|
| `/api/v1/external/*` | **CIAM only** (`RequireAuthorization(CiamExternal)`) | **Full data layer**: `/me`, `/projects`, `/projects/{id}/documents`, `/events`, `/todos`, `/contacts`, `/organizations` |
| `/api/v1/external-access/*` | workforce default | internal mgmt: grant / revoke / invite |
| `/api/v1/collab/*` (new, teams-app-r1) | workforce default | **only** `/me` (principal context) + broker document download |

The SPA's data client (`src/client/external-spa/src/api/web-api-client.ts`) hardcodes `/api/v1/external/*` for **all** data. In Teams the workforce token hits the CIAM-scheme `/external` endpoints → 401. The workforce `/collab` group can't serve the app because the data endpoints (`/projects`, `/documents`, …) were never built there.

## Why this happened

teams-app-r1 task 020's intent was "**generalize** the collaboration endpoints to the workforce plane." The implementation instead added a **separate `/api/v1/collab` group** (to avoid regressing the CIAM path, FR-15) and only wired `/me` + download into it. That's sufficient to prove auth, but it leaves the workforce host with no data surface — the gap surfaced only at live integration.

## Decision needed — two ways to close it

**Option A (recommended): dual-scheme the `/api/v1/external/*` collaboration read + download endpoints.**
Accept **both** the CIAM scheme **and** the workforce scheme on those endpoints; resolve either principal (CIAM contact via `sprk_externalobjectid`, or workforce systemuser/contact via the teams-app-r1 `WorkforcePrincipalResolver`) → compose the accessible-record-set (already built) → return the same shape. **Client unchanged.**
- Pro: one endpoint set, matches the "generalize" intent, smaller change.
- Con: touches the shared CIAM endpoints — must preserve CIAM behavior exactly (FR-15). Each handler must resolve principal-agnostically.

**Option B: build the full `/api/v1/collab/*` data set + make the client host-aware.**
Mirror `/projects`, `/documents`, etc. under `/collab`, and switch the SPA's base path to `/collab` when host = Teams.
- Pro: clean plane separation, zero CIAM-regression risk.
- Con: more work (duplicate endpoint set + client routing seam).

**The core question for SPA v2**: should the collaboration BFF endpoints be **principal-agnostic (one set, two auth planes)** or **plane-partitioned (two parallel sets, host-aware client)**? That's an architecture stance for the shared SPA/BFF, not a teams-app-r1-local call.

## Bonus: reusable Entra config recipe for any workforce/Teams hosting of the SPA

Getting workforce NAA to work in Teams required these on the workforce app (`1e40baad-…`), all discovered by peeling back `AADSTS` errors:
1. Multitenant (`AzureADMultipleOrgs`) + `access_as_user` scope exposed.
2. **Pre-authorize the Teams client apps** on `access_as_user`: `1fec8e78-bce4-4aaf-ab1b-5451cc387264` (desktop/mobile) + `5e3ce6c0-2b1f-4285-8d4b-75ee78787346` (web). Without this, Teams SSO can't issue a token.
3. **SPA redirect URIs** must include:
   - the app origin: `https://{swa-host}`
   - **the NAA broker redirect: `brk-multihub://{swa-host}`** ← this was the final blocker (`AADSTS700046: Invalid Reply Address … must be of Single Page Application type`). MSAL v5 NAA sends `redirect_uri=brk-multihub://{host}`; it must be registered as an SPA reply address.
4. Note: this app issues **v1 access tokens** (`requestedAccessTokenVersion` = null). Confirm the BFF workforce scheme accepts the `api://1e40baad-…` audience / v1 issuer before assuming the data-plane 401 is only the scheme mismatch above (the current 401 is the CIAM-scheme mismatch, not a token-rejection — but worth double-checking once Option A/B lands).

## Repro

Teams web → open the tab → F12 → the failing call is `GET https://spaarke-bff-dev.azurewebsites.net/api/v1/external/me` → 401. Auth itself succeeds (no `AADSTS`, token present).
