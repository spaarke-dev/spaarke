# Email Communication Solution R3

> **Status**: Design (pending `/design-to-spec`)
> **Created**: 2026-06-05
> **Parent**: Follows [email-communication-solution-r2](../email-communication-solution-r2/) (server-side Communication Service, completed 2026-04)
> **Inputs**: [Communication Architecture Assessment (2026-06-05)](../../docs/assessments/communication-architecture-assessment-2026-06-05.md)

## Purpose

R2 built the **server-side** unified Communication Service (`sprk_communicationaccount`, Graph subscriptions for inbound, OBO for individual send, `.eml` archival, association resolver). **R3 fixes the client-side fragmentation that R2 did not address**: 6 different ways to send email, stale duplicate component forks in LegalWorkspace, no canonical email composer UI.

## Scope (one-line)

Replace 6 ad-hoc send-email implementations with ONE canonical `<EmailComposer />` React component (used both inline-as-step in wizards and standalone in a Code Page that replaces the Dataverse standard form for `sprk_communication`), driven by ONE typed `sendCommunication()` BFF wrapper, with the LegalWorkspace duplicate forks retired.

## Documents

- [`design.md`](design.md) — full design (this project's authoritative reference)
- [Communication Architecture Assessment](../../docs/assessments/communication-architecture-assessment-2026-06-05.md) — the audit that motivated R3
- `spec.md` — produced by `/design-to-spec` after design review (TBD)
- `plan.md` — produced by `/project-pipeline` (TBD)

## Out of scope (explicit)

- Outlook add-in changes
- `/api/v1/emails/*` Power Apps Email-activity subsystem (dead in production usage)
- Non-email `CommunicationType` values (TeamsMessage, SMS, Notification)
- Server-side Communication Service refactor (R2 already did this)
- Inbox/thread browse UI (separate project)
