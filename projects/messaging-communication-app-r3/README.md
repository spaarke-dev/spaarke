# Communication Workspace — R3

> **Last Updated**: 2026-07-20
>
> **Status**: In Progress

## Overview

R3 delivers the **Teams-style conversation experience** for Spaarke communications: a single shared **two-pane conversation widget** (thread list + chat-bubble flow) reachable from a record right-pane PCF and the SpaarkeAI workspace / standalone code page, over a two-lens model (grid = email/list, conversation = message/chat), spanning all channels. R1 shipped transport + the thread model; R2 shipped record-level read/query/organize; R3 makes communications *conversational* and adds first-class **record-less threads**.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Phases, WBS, critical path, discovered resources |
| [Design Spec](./spec.md) | AI-optimized implementation spec (25 FR / 8 NFR) |
| [Design Doc](./design.md) | Investigation-grounded design narrative |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown, dependencies, parallel groups |
| [AI Context](./CLAUDE.md) | Session-load context for this project |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development (initialized) |
| **Progress** | 0% |
| **Target Date** | — (set at Step 4.5 / via GitHub) |
| **Completed Date** | — |
| **Owner** | Spaarke |

## Problem Statement

R1/R2 gave Spaarke a working communications channel and a record-level read/query/organize layer, but the experience is still a **vertical timeline**, not a conversation. Users cannot follow a message thread the way they do in Teams (mine-right/others-left bubbles, day dividers, in-flow compose), cannot see or work with **record-less** threads at all, and the release-critical must-haves (attachments, privilege/privacy accuracy, new-communication awareness, keyword search, pin) are absent. Without R3, communications remain a list to read rather than a conversation to participate in — and privilege/privacy inaccuracy risks over-disclosure.

## Solution Summary

Reuse the existing conversation *core* (reducer, ~5s polling, `buildTimeline`, send/read APIs) and add a **presentational layer** (`ConversationView` bubbles, thread list, quick-view popover, new-thread modal) plus a **focused backend increment** (list-all-threads incl. record-less, participant-based naming + a BFF rename endpoint, single-thread DTO enrichment, honor `ThreadId` on the email send branch). The same shared widget mounts in three surfaces (record modal, workspace widget, standalone code page). Release-gated must-haves — attachments, privilege/privacy markers, notification-spine awareness, Dataverse Search config, thread pin — land in later phases of the same project. No new send path (ADR-045), no second regarding mechanism (ADR-024), no Dataverse plugin (rename via BFF).

## Graduation Criteria

The project is **complete** when:

- [ ] A record's right-pane PCF shows its threads (top 3 / last 5) and opens the shared conversation widget **as a record-filtered modal** (FR-13)
- [ ] The SpaarkeAI workspace widget **and** the standalone code page render the shared two-pane conversation, incl. a **record-less** thread listed by participant name (FR-14)
- [ ] Conversation renders **mine-right/others-left** bubbles from sender identity, status on own bubbles (FR-02/03/18)
- [ ] Email-in-flow shows one indicator + opens the real EmailComposer modal, auto-associated to the active thread (`ThreadId` stamped) (FR-04/07/19)
- [ ] `GET /communications/threads` lists all threads incl. record-less, access-filtered, **no membership-union** — verified by seam test (FR-16)
- [ ] A record-less thread auto-names from participants and can be **renamed via the BFF** (edit-preserve holds) (FR-17)
- [ ] Quick-view popover (200-char) + open→pin works (FR-05)
- [ ] Attachments open/download from a message; attach-on-compose works (SPE-backed) (FR-20)
- [ ] Privilege/privacy flags surfaced; participant/recipient list reflects **actual permitted recipients** — no over-disclosure (FR-21)
- [ ] A new communication drives an unread badge + toast via the notification spine; content still polls (FR-22)
- [ ] Global keyword search finds a communication via the Dataverse search bar, security-trimmed (FR-23)
- [ ] Thread **pin** persists (FR-24)
- [ ] BFF publish ≤60 MB compressed; 0 new HIGH CVE; ADR-038 seam + component tests green (NFR-02/07/08)

## Scope

### In Scope

- Shared two-pane conversation widget (thread list + `ConversationView`), mount-agnostic (inline / standalone code page / record-scoped modal), reusing the conversation core.
- Surface 1 — record right-pane PCF (preview + footer counter + quick-view; opens the shared widget as a record-filtered modal).
- Surface 2 — SpaarkeAI workspace widget (keep `communications-list` / section `communications`) + standalone Vite code page.
- "Email & Messages" record tab — DataGrid (email/list lens) via form `onLoad` + `hostFilters`; complete + deploy `sprk_communicationspage`. All 11 regarding-family entities (Matter pilot first).
- Backend wave: list-all-threads endpoint (incl. record-less); participant-based thread naming + BFF rename endpoint; single-thread read DTO enrichment; honor `ThreadId` on the email send branch.
- Release-gated must-haves: attachments (SPE), privilege/privacy accuracy, notification-spine awareness, Dataverse Search config, thread pin.

### Out of Scope

- Tags/categories and semantic "find similar" (future semantic-AI project).
- Thread archive/close and mute (post-R3).
- @mentions, presence, typing indicators, read receipts (transport doesn't provide).
- OOB Dataverse email form; any **second** send path / regarding mechanism / grid-config default / workspace widget.
- Dataverse **plugins** (hard MUST NOT — rename routes through the BFF).
- Reintroducing membership-union on reads (retired 2026-07-16).

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Reuse `EmailComposer`/`SendEmailDialog` + `sendCommunication` (no 6th send impl) | Canonical send engine | [ADR-045](../../.claude/adr/) |
| Rename via a **BFF endpoint**, never a Dataverse plugin | Hard MUST NOT on plugins | ADR-024 / spec |
| List-all-threads query is **impersonated + access-filtered**, no membership-union | Correctness + retired anti-pattern | Access model / R1 note |
| "Email & Messages" tab uses the DataGrid shared **web-resource** framework | Sanctioned replacement for the retired PCF grid | ADR-006 tension (Path C) |
| Right-pane conversation PCF on OOB form | Same Path-A exception R2's timeline PCFs used | ADR-026 tension (Path C) |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Notification spine (`communication-arrived`) not yet in master | High (FR-22) | High | Sequence FR-22 wiring **late**; confirm producer/consumer contract at P1 vs `spaarke-notification-spine-r1` |
| Shared `Services/Communication/**` contention with r1/r2/email-r4 | Med | Med | `parallel-safe:false` + characterization tests before extending; `/conflict-check` before each BFF wave |
| Privilege/privacy mis-display → over-disclosure | High (correctness) | Low | Negative-access seam test; recipient list from actual permitted recipients only |
| Read-state (FR-25) proves significant | Low | Med | Best-effort; drop if not "not-significant" — not release-blocking |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| R2 schema live (thread regarding lookups + `sprk_communicationparticipant` junction) | Internal | Ready (in master) | Participant naming/filters depend on the junction |
| Notification spine `communication-arrived` | Internal | **Not in master** | Owner-committed; lives in `email-communication-solution-r4/projects/spaarke-notification-spine-r1` |
| `EmailComposer`/`SendEmailDialog` + ADR-045 send engine | Internal | Ready (in master) | Extend to accept thread id + record link |
| Dataverse Search enabled + `sprk_communication` in org search config | External | Config task | FR-23 |
| SPE (attachment content) | External | Ready | Reuse existing document-viewer path |
| `sprk_communicationthread` pin field; per-user read-state (if FR-25 built) | Internal (schema) | New | Additive schema |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-07-20 | 1.0 | Project initialized via `/project-pipeline` (merged master, artifacts + tasks generated) | Spaarke |

---

*Original design: [`design.md`](./design.md). UX contract: `spaarke-prototype/projects/2026-07-communication-conversation-widget/`.*
