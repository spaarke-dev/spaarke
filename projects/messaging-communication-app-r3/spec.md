# Communication Workspace — R3 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-20
> **Source**: `projects/messaging-communication-app-r3/design.md` (investigation-grounded; signed-off UX prototype at `spaarke-prototype/projects/2026-07-communication-conversation-widget/`)
> **Follows**: `messaging-communication-app-r1` (ACS channel + thread model), `messaging-communication-app-r2` (read/query/organize layer — merged to master), `email-communication-solution-r4` (EmailComposer / send engine, ADR-045)

## Executive Summary

R3 delivers the **Teams-style conversation experience** for Spaarke communications: a single shared **two-pane conversation widget** (thread list + chat-bubble flow) reachable from two surfaces — a **record right-pane PCF** and the **SpaarkeAI workspace / standalone code page** — over a **two-lens model** (grid = email/list, conversation = message/chat), both spanning all channels. R1 shipped transport + the thread model; R2 shipped record-level read/query/organize. R3 makes communications *conversational* and adds first-class **record-less threads**. The conversation *core* already exists and is reused; the new work is **presentational + a focused backend increment + the release-gated must-haves** (attachments, privilege markers, notification awareness, search config, pin).

## Scope

### In Scope
- **Shared two-pane conversation widget** (thread list + `ConversationView`), mount-agnostic (inline / standalone code page / record-scoped modal), reusing the existing conversation core (reducer, polling, `buildTimeline`, send/read APIs).
- **Surface 1 — record right-pane PCF**: preview (top 3 threads, last 5 comms, default auto-expanded) + footer counter + quick-view; opening launches the shared widget **as a modal filtered to the record**.
- **Surface 2 — SpaarkeAI workspace widget** (keep type string `communications-list`, section id `communications`) **+ standalone Vite code page**.
- **"Email & Messages" record tab** — record-filtered DataGrid (email/list lens) mounted as a web-resource grid driven by a form `onLoad` script applying the `sprk_regarding{type}` hostFilter; complete + deploy `sprk_communicationspage`. **All 11 regarding-family entities** (pilot Matter first).
- **Backend wave**: (a) list-all-threads endpoint incl. record-less; (b) participant-based thread naming + a BFF rename endpoint; (c) single-thread read DTO enrichment (direction + sender identity); (d) honor `ThreadId` on the email send branch.
- **Attachments** — open/preview/download from a message (SPE-backed) + attach-on-compose.
- **Privilege / privacy accuracy** — surface `isPrivate` / `isInternalOnly` / `privilegeClassification`; participant/recipient display reflects actual permitted recipients.
- **New-communication awareness** — consume the notification spine `communication-arrived` → unread badge + toast (content stays polling). *The spine will be made available for this project.*
- **Global keyword search** — configure Dataverse Search to index `sprk_communication` (subject/body/from/to); security-trimmed.
- **Thread pin/favorite** (pin only).
- **Message subject** — messages auto-derive a non-blank subject (grid Subject never empty); not shown in the bubble.

### Out of Scope
- Tags/categories and semantic "find similar" (deferred to a future semantic-AI project).
- **Thread archive/close and mute** (post-R3).
- @mentions, presence, typing indicators, read receipts (transport doesn't provide).
- OOB Dataverse email form (Spaarke uses EmailComposer).
- Dataverse **plugins** (hard MUST NOT — rename routes through the BFF).
- Any **second** send path, regarding mechanism, grid-config default, or workspace widget.
- Reintroducing **membership-union** on reads (retired 2026-07-16).

### Affected Areas
- `src/client/shared/Spaarke.UI.Components/src/components/CommunicationTimeline/**` — extend the core into `ConversationView` (bubble presentation); reuse reducer/poll/`buildTimeline`.
- `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/**` — extend `SendEmailDialog` to accept a thread id + render a record link.
- `src/client/shared/Spaarke.Communication.Components/**` — the workspace/thread widget (dual-use; keep `communications-list`).
- `src/client/pcf/**` — new record right-pane conversation PCF (mirror `CommunicationTimelineRegarding`).
- `src/solutions/**` — standalone conversation code page; `sprk_communicationspage` completion; form `onLoad` grid mount.
- `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs` + `Services/Communication/**` — list-all-threads, rename endpoint, DTO enrichment, email `ThreadId`, participant naming, notification producer/consumer.

## Requirements

### Functional Requirements

**Conversation core & surfaces**
1. **FR-01** — Shared two-pane conversation widget, mount-agnostic (inline / code page / record modal), takes an optional regarding filter (`entityType`+`id`). Acceptance: same component renders in all three mounts; record mode shows only that record's threads.
2. **FR-02** — Teams-style bubbles keyed on **sender identity** (mine-right/others-left), chronological newest-at-bottom, day dividers, sender labels. Acceptance: mine/others alignment derives from sender id (FR-18), not email-string matching.
3. **FR-03** — Message status (sent/delivered/failed) on the user's own bubbles.
4. **FR-04** — Email-in-flow renders as a compact block (subject/from/to) with a **single** "Email" indicator + an open-icon → email modal.
5. **FR-05** — Message quick-view popover (200-char; email → to/from/date/subject) with an **open→pin** action that scrolls/highlights the message.
6. **FR-06** — In-conversation compose (chat input) sends via the existing send path; **on-demand refresh** + auto-refresh on send; ~5s polling retained.
7. **FR-07** — Email modal reuses `SendEmailDialog`/`EmailComposer`, **extended** to accept a thread id + regarding record (auto-association) + embedded record link. Acceptance: opening from the composer auto-associates to the active thread (FR-19).
8. **FR-08** — Forward action on a message → email modal in **forward** mode, prefilled. Drafts live only in the email modal.
9. **FR-09** — In-conversation filters: word (dropdown) + Email/Message type toggles, **additive**.
10. **FR-10** — Thread list: name + unread indicator only; word-filter dropdown; create-thread (＋ icon).
11. **FR-11** — New Thread modal: **optional** record association + name + description; reuses recipient/body parts; find-or-create via `POST /threads/direct`.
12. **FR-12** — Conversation title **links to the associated record** (opens it in the record-scoped modal); record-less threads render plain title.
13. **FR-13** — Surface 1 right-pane PCF: preview (max 3 threads, last 5 comms, default thread auto-expanded) + footer counter ("N of M") + per-message quick-view; opening launches the shared widget as a **record-filtered modal**.
14. **FR-14** — Surface 2: SpaarkeAI workspace widget (keep `communications-list` + section `communications`) **and** a standalone Vite code page, both rendering the shared widget.
15. **FR-15** — "Email & Messages" record tab: record-filtered DataGrid via form `onLoad` script + `hostFilters` (no PCF); complete + deploy `sprk_communicationspage`. All 11 entities (Matter pilot first).

**Backend wave**
16. **FR-16** — `GET /api/communications/threads` — list all threads incl. record-less, paged/searchable by name, **impersonated** (`MSCRMCallerID`), access-filtered, **no membership-union**. New `CommunicationThreadReadService.ListThreadsAsync`.
17. **FR-17** — Participant-based naming in `ThreadResolver` (roll up the message-grain participant junction) for record-less threads; **+ a BFF rename endpoint** that sets `sprk_name` + flips `sprk_nameisautoderived` to Edited. **No Dataverse plugin.**
18. **FR-18** — Enrich the single-thread read DTO with `Direction` + sender identity (`systemuserid` + display name) + populate `ThreadReadResult.Name`.
19. **FR-19** — Honor `ThreadId` on the **email** send branch (mirror the message path's `AssignExplicitThreadAsync`) so an email pins to the active thread.

**Release-gated must-haves (later phases, still R3)**
20. **FR-20** — Attachments: open/preview/download from a message (SPE-backed) + attach-on-compose.
21. **FR-21** — Privilege/privacy: surface `isPrivate` / `isInternalOnly` / `privilegeClassification`; participant/recipient display reflects **actual permitted recipients** — never imply someone received/saw a restricted comm.
22. **FR-22** — New-communication awareness: emit + consume the notification-spine `communication-arrived` kind → unread badge + toast; content stays polling.
23. **FR-23** — Global search: configure Dataverse Search to index `sprk_communication` (subject/body/from/to). Acceptance: a keyword finds a matching communication via the global search bar, security-trimmed.
24. **FR-24** — Thread **pin/favorite** (pin only; archive + mute out of scope).
25. **FR-25** *(best-effort)* — Read/unread: per-user last-seen + mark-read/unread. Nice-to-have; **not release-blocking** if it proves significant work.

### Non-Functional Requirements
- **NFR-01** — Access accuracy: impersonation + the shared access-filter; **no membership-union**; privilege/privacy never mis-displayed (correctness-critical).
- **NFR-02** — BFF publish-size **≤60 MB** compressed; verify + report per BFF-touching task (baseline ~46 MB).
- **NFR-03** — Polling retained for content; notification spine for awareness only.
- **NFR-04** — Fluent v9 tokens; light + dark (ADR-021); dark mode passes through the host `FluentProvider`.
- **NFR-05** — Accessibility baseline: keyboard nav, ARIA, screen-reader for the chat flow; empty/loading/error states.
- **NFR-06** — Reuse-first (§11): no new send path (ADR-045), no second regarding mechanism, no second grid default/widget; keep type strings.
- **NFR-07** — No new HIGH-severity CVE (`dotnet list package --vulnerable --include-transitive`).
- **NFR-08** — Tests per ADR-038: **seam tests** for the new read endpoint (`tests/integration/seam/Communication/`); component tests for bubbles/popover/modal/thread-list; participant-naming + rename unit tests; characterize existing `CommunicationTimeline`/`SendEmailDialog` before extending.

## Technical Constraints

### Applicable ADRs
- **ADR-045** — canonical send engine (modes/mounts; no 6th send impl) — reuse `EmailComposer`/`SendEmailDialog` + `sendCommunication`.
- **ADR-046** — ACS messaging channel — message sends stay on the ACS branch.
- **ADR-024** — regarding family — reuse the 11 typed lookups + discriminator; no second mechanism.
- **ADR-026** — Path-A (PCF on OOB form; no Custom Page/FCC) — the right-pane PCF is the same exception R2's timeline PCFs use.
- **ADR-028** — auth v2 — all BFF calls via `@spaarke/auth` `authenticatedFetch`.
- **ADR-038** — testing strategy — seam tests as DoD for the dispatch/read spine.
- **ADR-021** — Fluent v9 / dark mode.
- **Access model** — impersonation + access-filter; **no membership-union** (`../messaging-communication-app-r1/notes/access-model-decision.md`).

### MUST Rules
- ✅ MUST reuse the existing conversation core (reducer/poll/`buildTimeline`) + `SendEmailDialog` + `sendCommunication` (already thread-aware).
- ✅ MUST issue the list-all-threads query impersonated; MUST NOT hand-compute a membership union.
- ✅ MUST keep the widget type string `communications-list` + section id `communications`.
- ✅ MUST route thread renames through a BFF endpoint; ❌ MUST NOT use a Dataverse plugin.
- ✅ MUST surface privilege/privacy accurately; ❌ MUST NOT imply access a user doesn't have.
- ❌ MUST NOT add archive/mute, tags, a second regarding mechanism, or a second grid/widget in R3.

### Existing Patterns to Follow
- Conversation core: `src/client/shared/Spaarke.UI.Components/src/components/CommunicationTimeline/**`.
- Bubble CSS reference: `SprkChat/SprkChatMessage.tsx`; popover reference: `AiSummaryPopover.tsx`.
- Record-form PCF: `src/client/pcf/CommunicationTimelineRegarding/**` (placement-bound); host wrapper `MatterHeader`.
- Read/send: `Services/Communication/CommunicationThreadReadService.cs`, `communicationTimelineApi.ts`, `sendCommunication`.
- Widget shell: `Spaarke.Communication.Components/.../CommunicationsWorkspaceWidget.tsx`; registry `WorkspaceWidgetRegistry`.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>Y</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
BFF=Y — the backend wave extends `Services/Communication/**` (reads, thread resolver, send-request handling) + one new read endpoint + one rename endpoint. **No new AI dependency, no new package, no new background worker.** Publish-size delta expected ≈0; verify ≤60 MB per BFF task per `.claude/constraints/bff-extensions.md`.

### New Components (§11 three-question gate)
| New component | Existing overlap (grep) | Extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `ConversationView` (bubble view) | `CommunicationTimeline/MessageRow.tsx` (vertical rows, no bubbles) | Extend the core; new bubble presentation | Without it there is no Teams-style flow — FR-02 fails; the vertical timeline can't render mine/others |
| `MessageQuickView` popover | `AiSummaryPopover.tsx` (AI summary) | No — copy pattern, different content | FR-05 (200-char preview + pin) has no home |
| `NewThreadModal` | none | No | FR-11 (create record-less thread) impossible from the UI |
| Record right-pane conversation PCF | `CommunicationTimelineRegarding` (regarding timeline) | Mirror it (new control, same pattern) | Surface 1 (FR-13) has no host |
| Standalone conversation code page | `sprk_communicationspage` (grid page) | No — different content (conversation, not grid) | Surface 2 standalone (FR-14) missing |
| `GET /communications/threads` + `ListThreadsAsync` | `ReadByRegardingAsync` (record-anchored only) | No — record-less threads are invisible to every existing read | FR-16 / the workspace left pane has nothing to call |
| BFF rename endpoint | `ReDeriveThreadNameAsync` (unwired) | Extend the resolver; new endpoint | FR-17 rename + edit-preserve never fires (plugins forbidden) |
| `sprk_communicationthread` pin field | none | No | FR-24 pin has no backing field |
| Per-user read-state *(if built)* | none | No | FR-25 best-effort; skip if significant |

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-006** (PCF over webresources) | "custom form UI is a PCF, not a JS web resource" | The "Email & Messages" tab mounts the DataGrid as a **web-resource** grid via a form `onLoad` script (owner-directed, FR-15) | **C (comply-with-intent)** | The DataGrid framework is the *sanctioned shared React web-resource* that supersedes the retired PCF grid (`SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`); form-`onLoad` record-scoping is within that framework, not a bespoke web resource. Cite in PR. |
| **ADR-026** (Path-A: no Custom Page/FCC) | "form-embedded rich UI needs a Path-A exception" | The right-pane conversation PCF (FR-13) | **C (comply)** | Same exception R2's `CommunicationTimeline*` PCFs already established; cite in the deploy PR. |

> No further ADR tensions surfaced at design time. All other listed ADRs apply without exception.

## Success Criteria
1. [ ] A record's right-pane PCF shows its threads (top 3 / last 5) and **opens the shared conversation widget as a record-filtered modal** — Verify: place on a Matter, open, navigate threads without returning to the PCF.
2. [ ] The SpaarkeAI workspace widget **and** the standalone code page render the shared two-pane conversation — Verify: both surfaces, incl. a **record-less** thread listed by participant name.
3. [ ] Conversation renders **mine-right/others-left** bubbles from sender identity, with status on own bubbles — Verify: seam/component test on the enriched DTO (FR-18).
4. [ ] Email-in-flow shows one indicator + opens the real EmailComposer modal, auto-associated to the active thread — Verify: send an email from the composer; confirm `ThreadId` stamped (FR-19).
5. [ ] `GET /communications/threads` lists all threads incl. record-less, access-filtered, no membership-union — Verify: seam test (record-less + access parity).
6. [ ] A record-less thread auto-names from participants and can be **renamed via the BFF** (edit-preserve holds) — Verify: rename, then trigger re-derive; name preserved.
7. [ ] Quick-view popover (200-char) + open→pin works — Verify: UI test.
8. [ ] Attachments open/download from a message; attach-on-compose works — Verify: UI test (SPE-backed).
9. [ ] Privilege/privacy flags are surfaced and the participant/recipient list reflects actual permitted recipients — Verify: negative-access test; no over-disclosure.
10. [ ] A new communication drives an unread badge + toast via the notification spine — Verify: emit `communication-arrived`, observe badge/toast; content still polls.
11. [ ] Global keyword search finds a communication via the Dataverse search bar, security-trimmed — Verify: search a subject/body term.
12. [ ] Thread **pin** works — Verify: pin/unpin persists.
13. [ ] BFF publish ≤60 MB; 0 new HIGH CVE; ADR-038 tests green — Verify: per-task measurement + CI.

## Dependencies

### Prerequisites
- **R2 schema live**: confirm task 002 (thread regarding lookups + markers) **and** task 003 (`sprk_communicationparticipant` junction) are applied in the target env — R3's person data, participant naming, and filters depend on the junction.
- **Notification spine** available for this project (owner-committed) — R3 consumes the `communication-arrived` kind.
- New schema additions: `sprk_communicationthread` **pin** field; per-user **read-state** *(only if FR-25 is built)*.
- App-user privileges carried from R2 (participant junction Create/Read/Append; Delegate role; the two messaging tables Share).

### External Dependencies
- Dataverse Search enabled + `sprk_communication` added to the org search config (FR-23).
- SPE for attachment content (existing).
- Doc-drift fix: `docs/data-model/sprk_communication.md` missing `Message = 100000004` + R1 messaging columns — correct as part of R3.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Project structure | R3+R4 split, or one project? | **One project (R3)**, phased; release-gated on the must-haves | Single coherent context; §10.1 items are later phases, not a separate project |
| v1 must-haves | Is §10.1 in scope? | Yes | Attachments/privilege/awareness/search/pin are release-gated R3 |
| Record reach | All 11 entities or a subset? | **All 11**, pilot Matter first | Placement/UAT covers 11; Matter is the pilot |
| Surfaces | PCF + Email&Messages tab + workspace widget? | Yes, all three | FR-13/14/15 all in |
| Notification spine | Depend on it, or degrade? | **Will be made available** for this project | FR-22 is a real dependency; awareness is in scope |
| Thread lifecycle | archive + pin + mute? | **Pin only**; archive + mute **out of R3** | FR-24 = pin; archive/mute deferred |
| Read/unread | Must-have or optional? | **Best-effort** — nice-to-have if not significant work | FR-25 not release-blocking |
| Categories/tags | In scope? | No — deferred to a semantic-AI project | No tag schema |
| Rename mechanism | Plugin or BFF? | **BFF endpoint** (no plugins — hard MUST NOT) | FR-17 via BFF |
| Email modal | OOB or EmailComposer? | **EmailComposer** (`SendEmailDialog`) | FR-07 reuse+extend |

## Assumptions
- **Read/unread (FR-25)**: assuming a per-user last-seen marker per thread if built; specced as behavior, mechanism left to the implementer; may be dropped if it proves significant work.
- **Attachment preview**: assuming reuse of the existing SPE document-viewer path rather than a new inline previewer.
- **Notification consumer**: assuming the spine's `communication-arrived` producer/consumer contract is confirmed at P1 (verify against `spaarke-notification-spine-r1`).
- **Grid record-scoping**: assuming the DataGrid `hostFilters`/parent-context supports the `sprk_regarding{type}` filter passed from the form `onLoad` script (confirmed present in `DataGrid/fetchXmlOverlay.ts`).

## Unresolved Questions
- [ ] **Notification-spine contract** — exact producer trigger (on capture vs on send) + consumer API. Blocks: FR-22 wiring (resolve at P1).
- [ ] **Read-state effort** — whether FR-25 is "not significant" enough to include. Blocks: nothing (best-effort); decide at P1 sizing.
- [ ] **Search deep-link** — whether v1 adds an in-widget search that deep-links into the conversation, or relies solely on the Dataverse global search bar (record landing). Blocks: nothing (fast-follow default).

---
*AI-optimized specification. Original design: `design.md`. UX contract: `spaarke-prototype/projects/2026-07-communication-conversation-widget/`.*
