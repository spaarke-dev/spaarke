# Spaarke Teams App (R1)

> **Portfolio**: [Project #724](https://github.com/spaarke-dev/spaarke/issues/724) · Epic [#431 EMAIL & MESSAGING](https://github.com/spaarke-dev/spaarke/issues/431) · [Board #2](https://github.com/users/spaarke-dev/projects/2)
>
> **Last Updated**: 2026-08-06
>
> **Status**: ✅ Complete (code shipped + deployed to `spaarke-bff-dev` + live-verified in Teams; merged to master via PR #723)

## Overview

R1 delivers the **Spaarke collaboration surface inside Microsoft Teams** — the current external-access SPA feature set (Secure Project Workspace: projects + documents + download) rendered as a **Teams personal app / static tab**, authenticated with the user's **workforce Microsoft Entra identity** and authorized by the user's **record membership**. The standalone external SPA and the Teams app are **one collaboration product line, two hosts, one shared core** — directed information-sharing & collaboration, *not* the system-of-record.

## Quick Links

| Document | Description |
|----------|-------------|
| [Implementation Plan](./plan.md) | WBS, phases, critical path, parallel groups |
| [Design Spec](./design.md) | Full technical design (17 sections) |
| [AI Spec](./spec.md) | AI-optimized implementation spec (16 FRs, 7 NFRs) |
| [ADR-028 A2 Amendment (draft)](./adr-028-amendment-draft.md) | Workforce-auth exemption (Path B — must merge before/with auth code) |
| [Task Index](./tasks/TASK-INDEX.md) | Task tracker + dependency graph + parallel groups |
| [AI Context](./CLAUDE.md) | Project context for Claude Code |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Complete |
| **Progress** | 100% (all tasks ✅; 6/7 graduation criteria fully verified, criterion 6-live = go-live item per accepted Path A) |
| **Target Date** | — |
| **Completed Date** | 2026-08-06 |
| **Owner** | ralph.schroeder |

## Problem Statement

Spaarke's external collaborators reach the Secure Project Workspace through a standalone CIAM-authenticated SPA. Internal staff and enterprise customers expect that same directed collaboration surface **inside Microsoft Teams**, using their workforce identity with no second login — governed by the records they are members of, not by ad-hoc grants. Today there is no Teams host, no workforce→principal resolution on the collaboration endpoints, and no record-level UX to grant external access or email members. Without R1, the collaboration product line cannot reach the enterprise Teams surface where legal teams already work.

## Solution Summary

Extend the deployed `external-spa` **in place** with a Teams host adapter (one codebase, host-detected) over a shared **standalone-MSAL** auth module whose authority is pluggable (CIAM for the SPA, workforce-multitenant + Teams SSO/NAA for Teams). The BFF resolves a workforce token to a **principal** (systemuser → ADR-034 membership, else contact → new contact-anchored membership) and enforces an **accessible-record-set** check; documents stream **broker-only** through `SpeFileStore`. A two-icon toolbar on the `TrackingFieldTrio` PCF adds a grant modal (`sprk_externalrecordaccess`) and email-members action. The Teams app ships as a v1.29 manifest via the M365 Agents Toolkit to the org catalog, with `tid`→environment routing for the three deployment models.

## Graduation Criteria

The project is considered **complete** when: *(verification evidence in [`notes/integration-verification-report.md`](./notes/integration-verification-report.md))*

- [x] A **systemuser** opens the Teams tab, signs in via workforce SSO with **no second login**, and sees exactly their **membership** records — ✅ operator-verified live (Teams web, 2026-08-06).
- [x] A **contact-only** workforce user sees exactly their **contact-anchored membership** records — ✅ logic test-verified (accessible-record-set scope); live check operator-gated.
- [x] Document download returns bytes for an authorized member and **403 with no bytes** for a non-member, across all three principal types (positive + negative) — ✅ empirically test-verified (contract + seam tests; no pointer leaked).
- [x] The same feature components render in the SPA and the Teams tab with **no duplicated feature component** (adapter-only divergence; §11 sign-off for any exception) — ✅ grep-verified (one core + thin `TeamsHostAdapter`).
- [x] The grant modal writes `sprk_externalrecordaccess` (approved membership + named users), sends an invite, and grants are revocable — ✅ backend contract test-verified; live modal operator-gated.
- [x] The app installs from the org catalog in a second (customer) tenant via admin consent; `tid`→env routing serves the correct environment — ⚠️ **routing deny-logic verified; live second-*customer*-tenant install = go-live checklist item (accepted Path A, 2026-08-06)** — needs a real second tenant.
- [x] BFF publish ≤60 MB compressed; no new HIGH-severity CVE; **no** M365 Agents SDK / Bot packages added — ✅ 46.90 MB; no HIGH/Critical CVE (Xml→8.0.4).

## Scope

### In Scope

- Teams personal app / static tab hosting the collaboration core (projects + documents + download); `external-spa` extended in place with a Teams host adapter.
- Dual-host adapter seam; shared standalone-MSAL auth module with pluggable authority (CIAM + workforce-multitenant + Teams SSO/NAA).
- Workforce→principal resolver (AAD `oid` → systemuser, else → contact); contact-anchored membership entry on `MembershipResolverService`.
- Accessible-record-set authorization; broker-only SPE download for all principals.
- Access-management surface on `TrackingFieldTrio`: person-icon grant modal + email-icon email-members; per-contact standing grant (R1).
- Multitenant workforce Entra app (reuse `1e40baad-…`) + admin consent; BFF `tid`→environment routing.
- Teams manifest v1.29 + framing headers + org-catalog packaging via M365 Agents Toolkit; new CI deploy workflow.
- Foundation spike (first): validate workforce-SSO→systemuser→membership AND workforce→contact→contact-anchored membership in a Teams tab (desktop + web); SPA still works via CIAM.

### Out of Scope

- AI exposure (chat/RAG/search) on the collaboration surface (forward constraints only).
- Native Teams-channel messaging bridge (`CommunicationType.TeamsMessage`); Teams conversational bot; Teams mobile.
- Communications / matters / service-requests / work-assignments / invoicing features (R2+).
- Full `sprk_accessgrant` Unified Access Control orchestration; extending `sprk_externalrecordaccess` beyond `sprk_project`.
- Folding the collaboration hosts onto `@spaarke/auth` (MSAL v3→v5 estate migration = separate future effort).

## Key Decisions

Locked in `design.md` §2.1 (D1–D11) and `spec.md` Owner Clarifications / Resolved Decisions (2026-08-03). Headlines:
- **Auth** = workforce SSO for Teams (not CIAM-in-Teams); CIAM for SPA; shared standalone-MSAL, pluggable authority; **extend `external-spa` in place**.
- **AuthZ** = derive-from-membership; accessible-record-set composition; non-systemuser workforce users supported (Option B).
- **Access-Permission posture** = Option A (record-level sharing gate: Restricted=off / Limited=named-only / Standard=all).
- **Role allowlist** = convention-based (`sprk_assigned*` contact lookups via metadata discovery) + exclusion list; new fields auto-qualify.
- **Standing grants + email icon** = R1 (not deferred).
- **ADR-028** → Path B amendment (A2 draft); **ADR-034** → Path C additive extension.

## External Prerequisites (admin-owned — NOT project tasks)

- `systemuser.sprk_primarycontact` linked for internal systemusers (admin activity, assumed complete 2026-08-03).
- Go-live readiness verification of those links on the target org (deployment checklist item).
