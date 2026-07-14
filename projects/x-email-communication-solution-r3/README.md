# Email Communication Solution R3

> **Last Updated**: 2026-06-05
>
> **Status**: In Progress (planning complete; implementation pending)

## Overview

R3 ends client-side fragmentation in Spaarke's email-send pipeline by replacing 6 ad-hoc implementations with one canonical `<EmailComposer />` engine + 3 semantic wrappers + 1 typed `sendCommunication()` BFF wrapper + 1 standalone Code Page that replaces the auto-generated Dataverse standard form for `sprk_communication`. ADR-033 codifies the canonical pattern so future surfaces don't re-fragment. R3 is the client-side companion to R2's server-side Communication Service (completed April 2026).

## Quick Links

| Document | Description |
|----------|-------------|
| [Design](./design.md) | Original human design document |
| [Spec](./spec.md) | AI-optimized implementation specification (27 FRs, 9 NFRs) |
| [Project Plan](./plan.md) | Wave decomposition, WBS, dependencies, risks |
| [Task Index](./tasks/TASK-INDEX.md) | All tasks + status + parallel-execution groups |
| [CLAUDE.md](./CLAUDE.md) | AI context (auto-load on task work) |
| [current-task.md](./current-task.md) | Active task state (context recovery) |
| [Driver Assessment](../../docs/assessments/communication-architecture-assessment-2026-06-05.md) | Communication Architecture Assessment 2026-06-05 |
| [Parent project (R2)](../email-communication-solution-r2/) | Server-side Communication Service (April 2026) |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Implementation (Wave 0 — Foundations) |
| **Progress** | 0% |
| **Target Date** | TBD |
| **Completed Date** | — |
| **Owner** | spaarke-dev |

## Problem Statement

Six different ways to send email exist across Spaarke's client surfaces (`sprk_communication_send.js` webresource, `SendEmailDialog`, `SendEmailStep` in CreateRecordWizard, `SummarizeFilesDialog`, `DocumentEmailWizard`, inline `fetch` in LegalWorkspace forks). Each implementation has subtly different recipient normalization, validation, error contracts, attachment cap enforcement, and auth bootstrap. Three documented behavioral divergences (assessment §3.1–§3.3) and one latent bug (`DocumentEmailWizard` line 494 sending `sprk_document` GUIDs instead of `driveItem` IDs) trace directly to this fragmentation. LegalWorkspace forks of shared-lib components compound the problem — the same email-touching wizard exists in two source trees, with drift between them. Without consolidation, every new surface adds a 7th, 8th, … implementation.

## Solution Summary

Build one canonical `<EmailComposer />` React engine + 3 thin wrappers (`<SendEmailStep />`, `<SendEmailDialog />`, `<SendEmailPage />`) + 1 standalone Code Page that replaces the auto-generated `sprk_communication` standard form. Refine `sendCommunication()` (the typed BFF wrapper) to use canonical `attachmentDriveItemIds` and throw typed `SendCommunicationError`. Two surgical, non-breaking BFF additions: rename `AttachmentDocumentIds` → `AttachmentDriveItemIds` (with alias for migration) and capture `Internet-Message-Id` post-send for inbound reply-thread closure. Retire `sprk_communication_send.js` (~2.3K LOC across two copies). Migrate all 6 existing callers onto the canonical wrappers. Codify the pattern in new ADR-033. Update 13 documentation files so the canonical pattern is the only path forward.

## Graduation Criteria

The project is **complete** when (from spec §Success Criteria):

- [ ] Exactly **one** client function (`sendCommunication()`) handles every email send — verified by grep audit `fetch.*communications/send` = 0 hits in `src/` (excluding bundles)
- [ ] Exactly **one** React component family (`<EmailComposer />` + 3 wrappers) renders the email-send UX — verified by import grep across all surfaces
- [ ] Zero callers send inline `fetch` to `/api/communications/send` — verified by assessment grep audit re-run
- [ ] LegalWorkspace contains zero forked copies of email-touching shared-lib components — verified by per-component audit
- [ ] `sprk_communication` Code Page is the default form opened from Dataverse views — verified by opening a record and confirming Code Page loads
- [ ] ADR-033 exists and is referenced from `.claude/constraints/bff-extensions.md` and root `CLAUDE.md` §16 — verified by file existence + cross-reference grep
- [ ] All 7 waves merged to master — verified by PR list in project tracking
- [ ] Smoke tests pass for every migrated surface — 5 wizards send-email + Summarize email step + FilePreview email dialog + DocumentEmailWizard with attachments + Code Page compose/view/reply/forward/draft
- [ ] Reply-thread closure functional — send → recipient replies → inbound reply auto-associates via `In-Reply-To` header matching
- [ ] `attachmentDriveItemIds` rename does not break existing callers — regression on DocumentEmailWizard with attachments
- [ ] No new lint or TypeScript errors in `@spaarke/ui-components` after each wave — `npm run lint` + `npm run build`
- [ ] CI passes on master after each wave merge — GitHub Actions run status
- [ ] `/adr-check` against this spec returns no violations

## Scope

### In Scope

- `<EmailComposer />` engine + 3 wrappers (`SendEmailStep`, `SendEmailDialog`, `SendEmailPage`) + sub-components (`RecipientField`, `BodyEditor`, `AttachmentList` + 4 source pickers, `ComposerActionBar`) in `@spaarke/ui-components`
- `EmailComposer` Code Page replacing the auto-generated `sprk_communication` form (3 entry surfaces: ribbon, Form Component Control, embeddable)
- Refined `sendCommunication()` with `attachmentDriveItemIds` + typed `SendCommunicationError`
- BFF non-breaking additions: `AttachmentDriveItemIds` DTO field + `Internet-Message-Id` post-send capture
- Dataverse schema check/addition for `sprk_inreplyto` + `sprk_internetmessageid`
- Migration of 5 create-record wizards (CreateMatter/Project/Event/Todo/WorkAssignment), `SummarizeFilesDialog`, `FilePreviewDialog`, `DocumentEmailWizard`
- Retirement: `sprk_communication_send.js` (both copies) + LegalWorkspace email-touching duplicate forks
- New ADR-033 (Communication architecture) + 13 documentation updates

### Out of Scope

- Outlook add-in changes (its own UI and document-archive flow)
- `/api/v1/emails/*` Power Apps Email-activity subsystem (dead in production usage)
- Non-email `CommunicationType` values (TeamsMessage, SMS, Notification)
- Server-side Communication Service refactor (R2 already shipped this)
- Inbox / thread browser / list view UI for `sprk_communication` (separate project)
- Server-Side Sync / Dataverse OOB `email` activities (Spaarke deliberately avoids these)

## Key Decisions

| Decision | Rationale | Reference |
|----------|-----------|-----------|
| Build the canonical engine in `@spaarke/ui-components` (not a new package) | Lives alongside existing wizards/dialogs; no new package boundary | spec §Affected Areas |
| 3 thin wrappers, not direct engine usage | Owner-clarified: wrappers carry semantic prop API; engine is internal | Owner Clarifications (spec) |
| Standalone Code Page (not embedded in entity form) | ADR-026 (Full-page custom page standard); matches `DocumentRelationshipViewer` pattern | ADR-026 |
| Form Component Control swap with standard form retained as admin fallback | Reversibility (NFR-07); supports UQ1 incremental migration | spec NFR-07 |
| React 18 only — no PCF React 16 compat | No PCF hosts the composer directly; wizards launch Code Pages | NFR-01 (spec) |
| Non-breaking BFF DTO rename via alias | Concurrent caller migration without coordination | NFR-05 (spec) |
| Single PR for all 5 wizard migrations (Wave 5) | Owner-clarified: minimizes CI/CD overhead | Owner Clarifications |
| Wrappers go in `@spaarke/ui-components` (not LegalWorkspace) | Per ADR-033 (new); eliminates fork pattern | ADR-033 |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Form Component Control swap breaks existing form customizations (custom JS, business rules) | High | Med | UQ1 audit in Wave 2 task 035; retain standard form as admin fallback (NFR-07); rollback path documented |
| `Internet-Message-Id` retrieval strategy fails for some Graph senders | Med | Med | UQ3 1-hour spike in Wave 0 task 003 to validate strategy; best-effort failure mode does not block send (FR-22) |
| Wave 5 single-PR shape produces an unreviewable diff (5 wizards) | Med | Med | Owner-decided trade-off; PR description must split into per-wizard sections; reviewer follows the section structure |
| ADR-033 number collision (parallel project claims 033) | Low | Low | Confirmed free at pipeline pre-flight (highest existing = ADR-032); Wave 0 task 001 re-checks before file creation |
| `sprk_communication_send.js` ribbon-button audit misses a consumer → broken ribbon | Med | Low | Wave 6 task 080 audits ALL ribbon definitions for the webresource reference before delete; PR description lists every entry point |
| PCFs bundling `@spaarke/ui-components` (SemanticSearchControl, SpeDocumentViewer) need rebuild after Waves 3–5 | Low | High (will definitely require) | Track in PR descriptions; Wave 7 wrap-up confirms no PCF references stale composer code |
| LegalWorkspace fork retirement breaks unsuspecting LegalWorkspace consumer | High | Low | Empirical scan confirmed only `CreateMatter/SendEmailStep.tsx` is a true fork; other dirs exist but have no email step. Wave 5 task 074 documents the delta. |
| `attachmentDriveItemIds` rename breaks existing `attachmentDocumentIds` callers during transition | High | Low | Non-breaking alias (FR-21); BFF accepts both during transition; deprecation warning logged; R4 removes the alias |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| R2 (email-communication-solution-r2) | Internal | ✅ Complete (April 2026) | Server-side Communication Service in production |
| `@spaarke/auth` v2.0.0+ | Internal | ✅ Available | Code Page bootstrap (ADR-028) |
| BFF `/api/communications/send` endpoint | Internal | ✅ Operational in dev | Existing endpoint receives canonical requests |
| Communication Architecture Assessment | Internal | ✅ Complete | `docs/assessments/communication-architecture-assessment-2026-06-05.md` |
| Dataverse customization access | External | ✅ Required | Form Component Control swap, schema additions, web resource deletion |
| Dataverse columns `sprk_inreplyto`, `sprk_internetmessageid` | External | ⏸ Verify (Wave 0 task 004) | Add if missing |
| `code-page-deploy` skill | Internal | ✅ Available | Wave 2 |
| Wizard solution build pipelines (5 wizards + SummarizeFiles + LegalWorkspace) | Internal | ✅ Operational | Multiple waves |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | spaarke-dev | Overall accountability |
| AI Implementer | Claude Code | Task execution via `/task-execute` |
| Reviewer | spaarke-dev | Code review at end of each wave |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-06-05 | 0.1 | Initial scaffolding (design.md + spec.md + README) | spaarke-dev |
| 2026-06-05 | 0.2 | Planning artifacts generated by `/project-pipeline` (plan, CLAUDE.md, tasks/, TASK-INDEX) | Claude Code |

---

*Template-derived. Updated by `task-execute` and the wrap-up task.*
