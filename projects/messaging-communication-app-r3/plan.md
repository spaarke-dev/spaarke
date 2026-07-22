# Project Plan: Communication Workspace — R3

> **Last Updated**: 2026-07-20
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Turn Spaarke communications from a vertical timeline into a **Teams-style conversation** — a shared two-pane widget over a two-lens model — and add first-class **record-less threads**, plus the release-gated must-haves (attachments, privilege/privacy accuracy, notification awareness, keyword search, pin).

**Scope**:
- Shared conversation widget (thread list + `ConversationView` bubbles), mount-agnostic across three surfaces.
- Focused backend increment: list-all-threads (incl. record-less), participant naming + BFF rename, single-thread DTO enrichment, honor `ThreadId` on email send.
- Release-gated must-haves: attachments (SPE), privilege/privacy markers, notification-spine awareness, Dataverse Search config, thread pin.

**Timeline**: 6 phases | **Estimated Effort**: ~25–35 focused-day-equivalents (parallelizable across waves)

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-045** — canonical send engine: reuse `EmailComposer`/`SendEmailDialog` + `sendCommunication`; **no 6th send impl**.
- **ADR-046** — ACS messaging channel: message sends stay on the ACS branch.
- **ADR-024** — regarding family: reuse the 11 typed lookups + discriminator; **no second mechanism**.
- **ADR-026** — Path-A (PCF on OOB form): right-pane PCF is the same exception R2's timeline PCFs used (**tension, Path C**).
- **ADR-028** — auth v2: all BFF calls via `@spaarke/auth` `authenticatedFetch`.
- **ADR-038** — testing: **seam tests** as DoD for the read/dispatch spine.
- **ADR-021** — Fluent v9 / dark mode via host `FluentProvider`.
- **ADR-006** — PCF-over-webresource: "Email & Messages" tab uses the sanctioned DataGrid web-resource framework (**tension, Path C**).

**From Spec**:
- Impersonation + shared access-filter; **no membership-union** (correctness-critical).
- Privilege/privacy never mis-displayed; recipient list = actual permitted recipients only.
- Polling retained for content; notification spine for **awareness only**.
- Reuse-first (§11): no new send path / regarding mechanism / grid default / widget; keep type string `communications-list` + section id `communications`.
- BFF publish ≤60 MB compressed (baseline ~46 MB); 0 new HIGH CVE.

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| `ConversationView` extends the conversation core (reducer/poll/`buildTimeline`) | No fork of proven transport/state | New presentation only; state stays shared |
| Rename via BFF endpoint, not plugin | Hard MUST NOT on plugins | New `PATCH`-style endpoint; flips `sprk_nameisautoderived` |
| List-all-threads = new `ListThreadsAsync` on `CommunicationThreadReadService` | Record-less threads invisible to every existing read | New method + endpoint; impersonated, access-filtered |
| DataGrid web-resource (not PCF) for the record tab | Sanctioned framework supersedes retired PCF grid | Form `onLoad` script + `hostFilters` |

### Discovered Resources

**Applicable ADRs** (full content loaded per-task via `docs/adr/*.md`):
- ADR-045, ADR-046, ADR-024, ADR-026, ADR-028, ADR-038, ADR-021, ADR-006

**Applicable Skills**:
- `pcf-deploy` — build/pack/deploy the right-pane conversation PCF (`npm run build:prod`)
- `code-page-deploy` — deploy the standalone Vite conversation code page + `sprk_communicationspage`
- `dataverse-create-schema` — `sprk_communicationthread` pin field (+ read-state if FR-25 built)
- `dataverse-deploy` — solution import for schema/web-resource
- `ui-test` — browser UI tests for bubbles/quick-view/modal/surfaces
- `code-review` + `adr-check` — FULL-rigor gates at task-execute Step 9.5
- `conflict-check` — before every BFF-touching wave (shared `Services/Communication/**`)

**Knowledge / Standards**:
- `.claude/constraints/bff-extensions.md` — BFF pre-merge checklist + publish-size rule
- `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` — `Xrm.WebApi` vs BFF
- `docs/standards/MODAL-DECISION-CRITERIA.md` + `.claude/patterns/ui/record-modal-selection.md` — record-scoped modal
- `docs/standards/CHAT-ATTACHMENT-POLICY.md` — attachment sizing/MIME (FR-20)
- `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` + config guide — the "Email & Messages" tab
- `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` + `BUILD-A-NEW-WORKSPACE-WIDGET.md` — Surface 2 widget
- `docs/data-model/sprk_communication.md` — **doc-drift fix** (missing `Message=100000004` + R1 columns)

**Reusable Code (canonical impls to copy/extend — all present in merged master)**:
- Conversation core: `src/client/shared/Spaarke.UI.Components/src/components/CommunicationTimeline/**`
- Bubble CSS ref: `.../SprkChat/SprkChatMessage.tsx`; popover ref: `AiSummaryPopover.tsx`
- Email compose: `.../EmailComposer/**` + `SendEmailDialog`
- Record-form PCF: `src/client/pcf/CommunicationTimelineRegarding/**`; host wrapper `MatterHeader`
- Workspace widget: `Spaarke.Communication.Components/.../CommunicationsWorkspaceWidget.tsx`; registry `WorkspaceWidgetRegistry`
- Backend: `Services/Communication/CommunicationThreadReadService.cs`, `ThreadResolver.cs`, `Api/CommunicationEndpoints.cs`, `communicationTimelineApi.ts`, `sendCommunication`

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Backend read/thread spine (FR-16/17/18/19 + doc-drift)   ← foundation; shared Services/Communication (serial)
└─ list-all-threads + ListThreadsAsync; participant naming + BFF rename; DTO enrichment; email ThreadId; seam tests

Phase 2: Shared conversation widget core (FR-01/02/03/06/09/10)    ← client shared lib
└─ ConversationView bubbles; thread list; in-conversation compose; additive filters

Phase 3: Email-in-flow + quick-view + new-thread (FR-04/05/07/08/11/12)
└─ extend SendEmailDialog (thread id + record link); MessageQuickView; forward mode; NewThreadModal; title→record link

Phase 4: Surfaces (FR-13/14/15)                                    ← mount the shared widget
└─ record right-pane PCF; SpaarkeAI workspace widget + standalone code page; Email & Messages DataGrid tab

Phase 5: Release-gated must-haves (FR-20/21/22/23/24 + FR-25 best-effort)
└─ attachments (SPE); privilege/privacy accuracy; notification awareness (gated on spine); search config; pin

Phase 6: Deploy + UAT + wrap-up
└─ deploy PCF/code page/solution (Matter pilot, then 11 entities); UAT; lessons-learned; test-diet
```

### Critical Path

**Blocking dependencies:**
- Phase 2/3/4 (UI) BLOCKED BY Phase 1 DTO enrichment (FR-18) — bubbles need `Direction` + sender identity.
- Phase 4 Surface 2 (workspace + list pane) BLOCKED BY Phase 1 FR-16 (`GET /communications/threads`).
- Phase 5 FR-22 (notification awareness) GATED ON `spaarke-notification-spine-r1` landing in master.
- Phase 6 deploy BLOCKED BY the surfaces it deploys.

**High-risk items:**
- Shared `Services/Communication/**` edits (Phase 1) — characterize existing read/send flows first; `parallel-safe:false`.
- Notification-spine contract unknown at plan time — resolve at P1; keep FR-22 late so it never blocks the rest.

### Parallel opportunities
- Phase 2 (`ConversationView`, thread list) and Phase 3 popover/new-thread are mostly independent client work once FR-18 lands — parallelizable within-wave (different files).
- Phase 4's three surfaces are independent hosts of the same widget — parallelizable.
- Phase 1's four backend items partly share files (`ThreadResolver.cs`, `CommunicationEndpoints.cs`) — serialize the shared-file edits.

---

## 4. Phase Breakdown

### Phase 1: Backend read/thread spine (foundation)

**Objectives:**
1. Make record-less threads visible via a new impersonated, access-filtered list endpoint.
2. Enrich reads so the UI can render bubbles by sender identity + direction.
3. Give threads a participant-derived name that can be renamed via the BFF (no plugin).
4. Pin an email to the active thread on the send branch.

**Deliverables:**
- [ ] `GET /api/communications/threads` + `CommunicationThreadReadService.ListThreadsAsync` (paged/searchable, impersonated, no membership-union) — FR-16
- [ ] Participant-based naming in `ThreadResolver` + BFF rename endpoint (sets `sprk_name`, flips `sprk_nameisautoderived`) — FR-17
- [ ] Single-thread read DTO enrichment: `Direction` + sender identity + `ThreadReadResult.Name` — FR-18
- [ ] Honor `ThreadId` on the email send branch (mirror `AssignExplicitThreadAsync`) — FR-19
- [ ] Doc-drift fix: `docs/data-model/sprk_communication.md` (`Message=100000004` + R1 columns)
- [ ] Seam tests `tests/integration/seam/Communication/` + participant-naming/rename unit tests — NFR-08

**Inputs**: `Services/Communication/**`, `Api/CommunicationEndpoints.cs`, R2 participant junction.
**Outputs**: new endpoint(s) + service method(s), enriched DTO, seam tests. **Placement Justification in each BFF PR; verify publish ≤60 MB.**

### Phase 2: Shared conversation widget core

**Objectives:**
1. Render the Teams-style bubble flow from the enriched DTO.
2. Provide the two-pane shell (thread list + conversation) reusing the core reducer/poll.

**Deliverables:**
- [ ] `ConversationView` — mine-right/others-left bubbles (sender identity), day dividers, sender labels, status on own bubbles — FR-02/03
- [ ] Two-pane shell + thread list (name + unread + word-filter + create-thread `＋`) — FR-01/10
- [ ] In-conversation compose (chat input via existing send path; on-demand + on-send refresh; ~5s polling retained) — FR-06
- [ ] In-conversation additive filters (word dropdown + Email/Message toggles) — FR-09
- [ ] Characterization tests for `CommunicationTimeline` before extending; component tests for bubbles/thread-list — NFR-08

**Inputs**: FR-18 DTO; `CommunicationTimeline/**`, `SprkChatMessage.tsx`.
**Outputs**: shared widget in `Spaarke.UI.Components` / `Spaarke.Communication.Components`.

### Phase 3: Email-in-flow + quick-view + new-thread

**Objectives:**
1. Make email a first-class in-flow block that opens the real composer, auto-associated.
2. Add the quick-view popover and record-less thread creation.

**Deliverables:**
- [ ] Email-in-flow compact block (subject/from/to) + single "Email" indicator + open→modal — FR-04
- [ ] Extend `SendEmailDialog`/`EmailComposer` to accept a thread id + regarding record + embedded record link; auto-associate to active thread — FR-07
- [ ] Forward action → email modal in forward mode, prefilled — FR-08
- [ ] `MessageQuickView` popover (200-char; email→to/from/date/subject) with open→pin scroll/highlight — FR-05
- [ ] `NewThreadModal` (optional record association + name + description; find-or-create via `POST /threads/direct`) — FR-11
- [ ] Conversation title links to the associated record (record-scoped modal); record-less renders plain — FR-12

### Phase 4: Surfaces

**Objectives:** Mount the shared widget in all three surfaces + the record grid tab.

**Deliverables:**
- [ ] Record right-pane conversation PCF (mirror `CommunicationTimelineRegarding`): preview (max 3 threads, last 5 comms, default auto-expanded) + footer counter + quick-view; opens shared widget as record-filtered modal — FR-13
- [ ] SpaarkeAI workspace widget (keep `communications-list` + section `communications`) + standalone Vite code page — FR-14
- [ ] "Email & Messages" record tab: DataGrid via form `onLoad` + `hostFilters` (no PCF); complete + deploy `sprk_communicationspage`; all 11 entities (Matter pilot) — FR-15

### Phase 5: Release-gated must-haves

**Objectives:** Ship the release-blocking must-haves; best-effort read-state.

**Deliverables:**
- [ ] Attachments: open/preview/download from a message (SPE) + attach-on-compose (per `CHAT-ATTACHMENT-POLICY.md`) — FR-20
- [ ] Privilege/privacy: surface `isPrivate`/`isInternalOnly`/`privilegeClassification`; recipient list = actual permitted recipients (negative-access test) — FR-21
- [ ] Notification awareness: emit + consume spine `communication-arrived` → unread badge + toast; content still polls — FR-22 **(gated on spine in master)**
- [ ] Global search: configure Dataverse Search to index `sprk_communication` (subject/body/from/to), security-trimmed — FR-23
- [ ] Thread pin (`sprk_communicationthread` pin field) — FR-24
- [ ] *(best-effort)* per-user read-state (last-seen + mark-read/unread) — FR-25; drop if significant

### Phase 6: Deploy + UAT + wrap-up

**Deliverables:**
- [ ] Deploy PCF + code page + `sprk_communicationspage` + schema/web-resource (Matter pilot → 11 entities)
- [ ] UAT across success criteria; verify BFF publish ≤60 MB, 0 new HIGH CVE
- [ ] `090-project-wrap-up`: README→Complete, lessons-learned, `/test-diet`, archive

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Dataverse Search (org search config) | Config | Low | FR-23 config task; verify in target env |
| SharePoint Embedded (attachment content) | GA | Low | Reuse existing document-viewer path |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| R2 participant junction + regarding lookups | Dataverse (in target env) | Ready (confirm task 002/003 applied) |
| Notification spine `communication-arrived` | `email-communication-solution-r4/projects/spaarke-notification-spine-r1` | **Not in master** — owner-committed |
| `EmailComposer`/`SendEmailDialog` + ADR-045 | `Spaarke.UI.Components` | Production (in master) |
| Conversation core (reducer/poll/`buildTimeline`) | `CommunicationTimeline/**` | Production (in master) |

---

## 6. Testing Strategy

**Unit Tests**:
- Participant-naming roll-up + rename edit-preserve logic; email `ThreadId` assignment.

**Integration (seam) Tests** — ADR-038 DoD:
- `GET /communications/threads`: record-less inclusion + access parity + **no membership-union** + negative-access (no over-disclosure).
- Single-thread read DTO enrichment (`Direction` + sender identity).

**Component Tests**:
- Bubbles (mine/others alignment from sender id), quick-view popover, email-in-flow block, thread list, new-thread modal.
- **Characterize** `CommunicationTimeline` + `SendEmailDialog` before extending.

**E2E / UI Tests** (`ui-test`, ADR-021 dark mode):
- Right-pane PCF → record-filtered modal navigation; workspace + standalone code page render; attachments open/download; pin persists; global search finds a comm.

---

## 7. Acceptance Criteria

Mirrors README graduation criteria + spec §Success Criteria (13 items). Each phase's deliverables above are the measurable checkpoints; the wrap-up task verifies the full set + NFR gates (publish ≤60 MB, 0 new HIGH CVE, ADR-038 tests green).

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Notification spine not in master when FR-22 starts | High | High | Keep FR-22 in Phase 5 (late); confirm contract at P1; degrade to polling-only if spine slips |
| R2 | Shared `Services/Communication/**` merge friction (r1/r2/email-r4) | Med | Med | `parallel-safe:false`; characterization tests first; `/conflict-check` before each BFF wave |
| R3 | Privilege/privacy over-disclosure | Low | High | Negative-access seam test; recipient list from permitted recipients only |
| R4 | `hostFilters` doesn't carry `sprk_regarding{type}` from form `onLoad` | Low | Med | Confirmed present in `DataGrid/fetchXmlOverlay.ts`; verify early in Phase 4 |
| R5 | Read-state (FR-25) balloons | Med | Low | Best-effort; drop if significant — not release-blocking |

---

## 9. Next Steps

1. **Review this plan.md** (phases + critical path).
2. **Run** `/task-create projects/messaging-communication-app-r3` to generate POML task files (done by pipeline Step 3).
3. **Begin** Phase 1 (backend spine) — foundation for all UI phases.

---

**Status**: Ready for Tasks
**Next Action**: Task decomposition (pipeline Step 3)

---

*For Claude Code: load relevant phase + Discovered Resources when executing tasks. All backend tasks: state Placement Justification in the PR + verify publish size.*
