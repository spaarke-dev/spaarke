# Spaarke External Access Platform — Custom SPA + Entra External ID (R1)

> **Quick Links**: [plan.md](plan.md) · [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) · [spec.md](spec.md) · [design.md](design.md)

## Overview

This project migrates the **hosting + identity layer** of Spaarke's external-facing Secure Project Workspace — from **Power Pages + Entra B2B guests** to a **custom React SPA on Azure Static Web Apps + Microsoft Entra External ID (CIAM)** — while leaving the BFF business logic, the SPA feature set, and the three-plane `sprk_externalrecordaccess` authorization model unchanged. It also adds the minimum file-content download and the core-user invite trigger needed to make the migrated portal usable.

This is a **Type-2 (external MAU / CIAM)** project. Type-1 full-license/model-driven-app user provisioning (the existing demo-registration system) is out of scope.

## Problem Statement

The external portal is a React 18 SPA hosted on Power Pages and authenticated via Entra B2B guests. Two of the three original reasons for choosing Power Pages have eroded (self-registration and licensing), the third (Contact authz) is already duplicated in the BFF, and Azure AD B2C is end-of-sale. A June/July 2026 outage (a decommissioned BFF host) exposed how brittle the Power Pages web-resource deploy path is. The B2B-guest model is also the direct cause of recurring corporate-account-vs-guest login conflicts.

## Proposed Solution

Swap the front door only: host the existing SPA on Azure Static Web Apps (real CI/CD, clean URLs) and authenticate external users against a dedicated Entra External ID (CIAM) tenant. A Phase-0 spike confirmed the portal is a **pure BFF-broker** — all SPE/Dataverse access is app-only, the external user's token never goes downstream — so a CIAM identity suffices and **no per-user workforce B2B guest is required**. Everything below the BFF auth filter is untouched.

## Scope

### In Scope
- Host `src/client/external-spa/` on Azure Static Web Apps; `HashRouter` → `BrowserRouter` + deep-link-through-login; security headers; CORS/redirect-URI updates (Phase 1, on existing B2B to isolate routing regressions).
- Entra External ID (CIAM) tenant + app registration; second JwtBearer scheme; admin-initiated CIAM provisioner (account create + `oid` link + onboarding email); core-user "Invite to Secure Workspace" trigger; Contact resolution by `oid` (Phase 2).
- App-only document content download (reusing `SpeFileStore.DownloadFileAsync`), keyed on `documentId`, authz-before-stream.
- `Contact.sprk_externalobjectid` schema field.
- Decommission Power Pages site + web-resource script; rewrite docs; apply ADR-028 Amendment A1 (Phase 3).

### Out of Scope
- Any change to the three-plane authz model (beyond dropping the vestigial synthetic SPE grant).
- Type-1 full-license/MDA provisioning (existing demo-registration system).
- Self-service sign-up + "Legal Front Door" (future; must not preclude).
- Inline preview/thumbnails UX (R2); UI/UX redesign (R2).
- Existing-user migration (N/A — no production users); Teams integration (future).

## Graduation Criteria

- [ ] SPA loads and functions from the SWA origin with clean-URL deep links (direct + through-login).
- [ ] A core user invites an outside-counsel Contact; the attorney signs in via CIAM and sees the assigned Project + documents (end-to-end).
- [ ] Onboarding is idempotent — re-invite creates no second CIAM account.
- [ ] An authorized Contact downloads a document; an **unauthorized** Contact gets 403 with **no bytes** (positive + negative tests).
- [ ] No workforce B2B guest is created for any external user.
- [ ] BFF publish size ≤60 MB; no new HIGH CVE; BFF build + tests pass.
- [ ] Power Pages site + web-resource script decommissioned after parity; docs rewritten.
- [ ] ADR-028 Amendment A1 applied (done).

## Status

**Phase**: Planning → ready for task execution. **Spec**: [spec.md](spec.md) (owner-reviewed, BFF-audit-reconciled). **ADR-028 Amendment A1**: applied. **Phase-0 spike**: GREEN.
