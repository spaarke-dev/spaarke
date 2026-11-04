# Spaarke External Access Platform R3

> **Portfolio**: [Project #1734](https://github.com/spaarke-dev/spaarke/issues/1734) · [Board #2](https://github.com/users/spaarke-dev/projects/2) · Epic [#2099 EXTERNAL ACCESS SPA](https://github.com/spaarke-dev/spaarke/issues/2099) — _registered 2026-10-12; Epic linked 2026-10-18_

> **Last Updated**: 2026-11-03
>
> **Status**: Complete (2026-11-03). Production go-live (task 073) moved to customer-provisioning-orchestration-r1 (#2532).

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
| **Phase** | Complete |
| **Progress** | 100% of R3 tasks (073 handed to provisioning; 060 cancelled) — see `tasks/TASK-INDEX.md` |
| **Target Date** | — |
| **Completed Date** | 2026-11-03 |
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

- [x] 1. A partner (browser) and a workforce user (browser and Teams) open a granted record into the detail view with fields, docs/invoices, messages and events/tasks; an ungranted record is denied (E2E + negative test).
- [x] 2. Each quick-start card opens its wizard; submitting creates a visible `sprk_servicerequest`; a CIAM caller is refused while the partner switch is off (E2E + server test).
- [x] 3. A grant to an onboarded contact sends one email whose link opens the record, and the grant appears in the in-portal feed (E2E + email capture).
- [x] 4. Grid columns match the owner's list (owner review).
- [x] 5. A message send on an accessible record appears in the thread; a send outside the set returns 403 (server negative test + E2E).
- [x] 6. A new employee of an allow-listed company self-registers from the join link and lands signed in; a non-listed tenant is refused (E2E with a test tenant) — or FR-14 is handed to R4 with a written path and the join page ships behind a flag.
- [x] 7. Workforce sign-in (browser and Teams) uses the A9 multitenant client at the customer's home directory with `user_impersonation`; licensed users resolve through the stored link; CIAM partners unchanged (token diagnostics + FR-21 live check).
- [x] 8. One SPA build reaches two different backends by run-time selection (dev test with two entries).
- [x] 9. No SPA page leaves the external plane; no mock identity in production (code test + E2E).
- [x] 10. Documents upload/download on Project, Matter, Work Assignment, Invoice and Service Request; ViewOnly cannot upload (server tests + E2E).
- [x] 11. A user deletes a record they created; another user (even FullAccess) gets 403 (server negative tests).
- [x] 12. With the partner switch off, partners cannot submit service requests; with it on, they see only their own (server tests both ways).
- [x] Quality: all BFF and SPA tests pass; BFF publish ≤ 60 MB; no new HIGH CVE; code-review + adr-check clean at wrap-up.

Evidence for criteria 1–12: `notes/task-072-e2e-record.md` (the owner's both-plane E2E on 2026-10-31, plus the server tests each row cites). Criterion 6 was met in full, not handed to R4: tasks 047, 058 and 074, with the unlisted-directory refusal in task 071. Quality: `notes/task-090-notes.md`.

## Scope

**In scope:** C1 record detail · C2 Legal Front Door wizards · C3 notifications · C4 grid columns · C5 Teams parity (workforce) · C6 message send · C7 self-registration · C8 auth alignment · C9 documents, creator attribution, delete-own. See `spec.md` §Scope.

**Out of scope:** Ask Legal assistant; e-signature; editing core records; new identity planes; CIAM partners in Teams; Model 2 auth; outbound email on message send; message attachments. Built elsewhere and consumed here: T240c directory and T240d CIAM-on-stamps (provisioning), ADR-028 A5 impersonated record set (UAC-r2 #1567).

## Hand-off: open issues

R3 closes with these filed issues still open. Each has a home:

| Issue | Home |
|---|---|
| #1623 Copilot audience merge applied once per process | R4 backlog |
| #1633 Chat streaming contract test flaky under load | SpaarkeAi chat owners (test reliability) |
| #1636 Shared email HTML sanitizer | R4 backlog (shared UI) |
| #1650 Internal document delete can orphan the SPE file | R4 backlog (documents) |
| #1673 Binder-created contacts have no BU-team owner | R4 backlog, with provisioning (BU configuration) |
| #1736 Generate and send the standard NDA from intake | A future intake/NDA project |
| #1737 Incident alerting for Policy Questions | R4 backlog |
| #1829 External SPA create invoice | spaarke-ontology-platform-r1 (invoice intake) |
| #1830 Dev SPE JIT-grant revocation sweep failing | unified-access-control-r2 |
| #1839 Service-request documents indexed with no parent | R4 backlog (documents/search) |
| #1840 Unused `sprk_communication_send` web resource | R4 backlog (cleanup) |
| #1864 Internal Only enforced by BFF reads, not Dataverse security | unified-access-control-r2 (task 191) |
| #1886 Document lists show rows with no file | R4 backlog (documents) |
| #1930 WorkforceHome scheme tests order-dependent | R4 backlog (test hygiene) |
| #2125 Grid overview tool and external-flagged callers | unified-access-control-r2 |
| #2269 Retryable binder faults answer 403, should be 503 | R4 backlog (UAC reviews) |
| #2270 Contacts created in a foreign environment have no owner | R4 backlog; needs an owner decision on the owner-team source |
| #2271 Document lock 409 has no ProblemDetails title or status | R4 backlog (documents) |
| #2275 `no-mock-identity.test.tsx` cold-start timeout | R4 backlog (test hygiene) |
| #2278 Backfill the owner of existing app-owned contacts | R4 backlog, with #1673 |
| #2415 Removal sweep telemetry on an empty pass | unified-access-control-r2 (removal sweep) |
| #2499 Contact identity store in-memory double | R4 backlog (test debt) |
| #2534 Auth architecture doc predates ADR-028 A9 | spaarke-auth-system-of-record-r1, else R4 |
| #2543 Provisioning timeout test flaky under load | unified-access-control-r2 (test owner) |

Already in R4's `design.md` §7: #2446, #2447, #2489, #2490, #2508.

#2269, #2270, #2271, #2275 and #2278 were filed on 2026-10-22 with "Owner: R3" during the ISS-004/ISS-005 work, but were not in this table until 2026-11-04, when R4 asked. All five were still open and unfixed on master then. #1566 (FR-18, task 042) and #1568 (FR-19, task 011) were closed as done the same day.

## Changelog

| Date | Change |
|------|--------|
| 2026-10-10 | Initialized by `/project-pipeline` from `spec.md` |
| 2026-10-13 | ADR-028 A9: workforce contacts sign in with their home directory; re-plan of C7 |
| 2026-10-31 | Owner both-plane E2E passed (task 072); grids approved (task 002) |
| 2026-11-01 | Task 079 (#2458 shared home-tenant read) merged and deployed to dev |
| 2026-11-03 | Task 073 moved to customer-provisioning-orchestration-r1 (#2532). Wrap-up (task 090): review fixes, test diet, close-out |
