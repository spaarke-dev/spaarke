# Spaarke External Access Platform R3

> **Last Updated**: 2026-10-10
>
> **Status**: In Progress (initialized by `/project-pipeline` 2026-10-10; execution not started)

## Overview

R3 turns the external SPA (`external.spaarke.com`, also the Teams tab) from a read-only portal into a working destination. Users can open records, submit Legal Front Door requests, get notified of new access, send messages, attach and remove their own documents, and self-register as customer staff. It also aligns SPA and Teams sign-in with the decided auth path: one single-tenant Spaarke client, Spaarke-tenant authority, `user_impersonation`, and run-time selection of each customer's backend.

## Quick Links

| Document | Description |
|----------|-------------|
| [Spec](./spec.md) | AI implementation specification (FR-01–FR-26, NFR-01–07) — the source of truth for scope |
| [Design](./design.md) | Owner-reviewed design, reuse map, §4.6 auth/backend decisions |
| [Plan](./plan.md) | Phases, WBS, critical path, risks |
| [Tasks](./tasks/TASK-INDEX.md) | Task registry, status, parallel waves |
| [Project CLAUDE.md](./CLAUDE.md) | Operating manual: binding rules, owner decisions, coordination, gotchas |
| [Auth path](./notes/r3-auth-path.md) | The definitive SPA/Teams auth path and owner decisions |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development (not started) |
| **Progress** | 0% — see `tasks/TASK-INDEX.md` |
| **Target Date** | — |
| **Completed Date** | — |
| **Owner** | Ralph Schroeder |

## Problem Statement

R2 live UAT (2026-08-12) showed the access model works, but the portal is not usable as a destination:
- clicking a grid row does nothing;
- the Legal Front Door quick-start cards open nothing;
- a contact given access to a second record is never told;
- users cannot send a message, attach a document to most records, or remove a record they created by mistake.

The SPA also signs workforce users in through `/organizations` with a baked backend. That cannot serve per-customer backends (owner D-13) or Model 1 guests. Licence-free customer staff have no way to get access at all.

## Solution Summary

Extend the R2 module-host SPA and the external-plane BFF surface (`/api/v1/external/**`) rather than fork them:
- a read-only record detail view (fields, documents/invoices, messages, events/tasks);
- four intake wizards writing `sprk_servicerequest`;
- an email plus a derived in-portal feed on new grants and messages;
- an external-plane message read and send endpoint;
- document routes on every core record;
- server-stamped creator attribution with delete-own;
- a join-link self-registration flow;
- the workforce client switch with run-time backend selection.

Every new endpoint is Tier-2 authorized and fail-closed. External-plane data access is app-only, with no OBO.

## Graduation Criteria

The project is **complete** when (spec §Success Criteria):

- [ ] 1. A partner (browser) and a workforce user (browser and Teams) open a granted record into the detail view with fields, docs/invoices, messages and events/tasks; an ungranted record is denied (E2E + negative test).
- [ ] 2. Each quick-start card opens its wizard; submitting creates a visible `sprk_servicerequest`; a CIAM caller is refused while the partner switch is off (E2E + server test).
- [ ] 3. A grant to an onboarded contact sends one email whose link opens the record, and the grant appears in the in-portal feed (E2E + email capture).
- [ ] 4. Grid columns match the owner's list (owner review).
- [ ] 5. A message send on an accessible record appears in the thread; a send outside the set returns 403 (server negative test + E2E).
- [ ] 6. A new employee of an allow-listed company self-registers from the join link and lands signed in; a non-listed tenant is refused (E2E with a test tenant) — or FR-14 is handed to R4 with a written path and the join page ships behind a flag.
- [ ] 7. Browser and Teams sign-in use the single-tenant client, Spaarke-tenant authority and `user_impersonation`, for a member and a guest (token diagnostics + FR-21 live check).
- [ ] 8. One SPA build reaches two different backends by run-time selection (dev test with two entries).
- [ ] 9. No SPA page leaves the external plane; no mock identity in production (code test + E2E).
- [ ] 10. Documents upload/download on Project, Matter, Work Assignment, Invoice and Service Request; ViewOnly cannot upload (server tests + E2E).
- [ ] 11. A user deletes a record they created; another user (even FullAccess) gets 403 (server negative tests).
- [ ] 12. With the partner switch off, partners cannot submit service requests; with it on, they see only their own (server tests both ways).
- [ ] Quality: all BFF and SPA tests pass; BFF publish ≤ 60 MB; no new HIGH CVE; code-review + adr-check clean at wrap-up.

## Scope

**In scope:** C1 record detail · C2 Legal Front Door wizards · C3 notifications · C4 grid columns · C5 Teams parity (workforce) · C6 message send · C7 self-registration · C8 auth alignment · C9 documents, creator attribution, delete-own. See `spec.md` §Scope.

**Out of scope:** Ask Legal assistant; e-signature; editing core records; new identity planes; CIAM partners in Teams; Model 2 auth; outbound email on message send; message attachments. Built elsewhere and consumed here: T240c directory and T240d CIAM-on-stamps (provisioning), ADR-028 A5 impersonated record set (UAC-r2 #1567).

## Changelog

| Date | Change |
|------|--------|
| 2026-10-10 | Initialized by `/project-pipeline` from `spec.md` |
