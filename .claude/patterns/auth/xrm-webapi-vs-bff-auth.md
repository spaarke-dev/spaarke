# Xrm.WebApi vs BFF API Authentication

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Deciding whether a Code Page or PCF control needs MSAL/@spaarke/auth bootstrap.

## Read These Files
1. `src/client/shared/Spaarke.Auth/src/index.ts` — @spaarke/auth library (needed for BFF calls)
2. `src/solutions/LegalWorkspace/src/main.tsx` — Code Page WITH auth bootstrap (calls BFF API)

## Constraints
- **ADR-001**: BFF API endpoints require Bearer token via MSAL OBO
- **ADR-008**: Auth is per-endpoint — Xrm.WebApi uses session cookie automatically

## Key Rules
- Xrm.WebApi (Dataverse CRUD): session cookie, automatic — NO MSAL needed, NO bootstrap
- BFF API (`/api/*`): Bearer token required — MUST use `@spaarke/auth` bootstrap
- SharePoint/Graph via OBO: goes through BFF — MUST use auth bootstrap
- AI operations (OpenAI, Doc Intel): goes through BFF — MUST use auth bootstrap
- If page ONLY reads/writes Dataverse records → skip auth bootstrap entirely
