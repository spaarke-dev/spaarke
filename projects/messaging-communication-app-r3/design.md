# Communication Workspace — R3 — Design (DRAFT for review)

> **Status**: 🟡 DRAFT — investigation-grounded, for owner review + iteration. Not yet spec'd.
> **Created**: 2026-07-20 · **Author**: Claude Code (grounded in 3 parallel investigations: client components, BFF/email infra, data model)
> **Follows**: `messaging-communication-app-r2` (Communication Workspace read/query/organize layer — code-complete, BFF deployed, PCF live).
> **Sibling reference**: `messaging-communication-app-r1` (ACS messaging channel + thread model), `email-communication-solution-r4` (EmailComposer / send engine, ADR-045).
> **Next steps after sign-off**: iterate this doc → `/design-to-spec` → `/project-pipeline`.

---

## 1. What R3 is

R3 is the **Teams-style conversation experience** for Spaarke communications. R1 shipped transport + the thread model; R2 shipped record-level read/query/organize surfaces (regarding-mode timeline, participant index, auto-threading, standalone grid page). **R3 makes communications *conversational***: a shared "conversation view" (Microsoft-Teams-style chat flow) reachable from two surfaces, with quick-view message previews, in-context reply, an email modal, and — new — **first-class threads that aren't tied to a record**.

**The organizing insight from investigation**: the *conversation core* is already built (state, polling, ordering, send/read APIs, recipient/body/attachment inputs, the email modal). The genuinely new work is **presentational + a focused backend increment**. This is not a from-scratch build.

---

## 2. Core UX principles (from owner, reconciled 2026-07-20)

1. **One unified view of all communications** — email, messages, SMS (future) — as a single stream.
2. **Filter by channel** — see only email, or only messages, etc.
3. **Communications are organized by threads** — a communication is hard-wired to exactly one `sprk_communicationthread` (already true in schema).
4. **Threads carry associations** — each record (matter/project/invoice/…) has a **default thread** named for the record (number/title).
5. **Auto-association** — a communication lands in the record's default thread unless explicitly placed in another thread or in the history of a non-default thread. *(Already implemented: the 3-tier `ThreadResolver` ladder — subject → record-default → per-user master.)*
6. **Multi-record, one-per-type, one primary** — a thread may relate to several records but only one per record-type, with one **primary** regarding record. *(Already modeled: 11 typed regarding lookups + the `sprk_regardingrecordtype_ref` discriminator.)*
7. ~~Categories/tags~~ — **CUT.** The "find related/similar" need is **deferred to a future semantic-AI project**; R3 adds no tag schema. (R2 decision Q2 stands.)
8. **Email follows the standard email data schema.**
9. **Messages share the email schema** (to/from/subject/body/dates) — the only difference is transport. *(Confirmed: one `sprk_communication` entity, transport-only difference; `sprk_communicationtype` distinguishes channel.)*

---

## 3. Locked decisions (owner, 2026-07-20)

| # | Decision |
|---|---|
| D1 | **No tags/categories** — defer "find similar" to a future semantic-AI project. |
| D2 | **Record-less threads are first-class** — a thread may have no regarding record; visible only in the workspace widget / code page. Auto-named from its to/from **participants**, with an obvious user **rename**. |
| D3 | **Keep `sprk_communicationspage`** (R2 grid page) as-is for possible future use — not the R3 surface, not retired. |
| D4 | **Surface 1 = a right-pane PCF** (fits the current main-form layout). PCF = tight list; the conversation **modal** is the Teams bubble flow. |
| D5 | **Email modal = `SendEmailDialog` (EmailComposer, email-r4)** — NOT the OOB Dataverse email form. |
| D6 | **Prototype the ConversationView first** (before committing schema/code). |

---

## 4. The two surfaces

### Two lenses over the same data (owner, 2026-07-20)

Communications are shown through **two complementary lenses**, both spanning all channels (email + message + future SMS):

| Lens | Component | Best for | Mounts |
|---|---|---|---|
| **Email / list lens** | the `sprk_communication` **DataGrid** (config `e1826c4c-…`, **existing**) — sortable/filterable table with channel/date/person chips | scanning, triage, "find that email", bulk | Record form **"Email & Messages" tab** (filtered to the record) + SpaarkeAI workspace **grid widget** (`communications-list`, existing) |
| **Message / conversation lens** | the R3 **two-pane conversation widget** (Teams-style) | threaded back-and-forth, reply/forward, chat | Record form **right-pane PCF** (preview → modal) + SpaarkeAI workspace **conversation widget** (new) |

**Scope impact:** the list lens **already exists** (R2 grid + widget) — R3 does **not** rebuild it. R3 owns two small list-lens pieces:
- **(i)** Complete + deploy the `sprk_communicationspage` grid (R2 built it; the deploy carries into R3).
- **(ii)** A **record-filtered mount** for the "Email & Messages" tab — mounted as a **web-resource grid driven by a form `onLoad` script** (NOT a PCF) that reads the current record and applies the `sprk_regarding{type}` hostFilter. Reuses the framework's `hostFilters`/parent-context and the repo's existing form-onload record-association pattern.

### Surface 1 — Record form (right-pane PCF)

A **placement-bound PCF** on each core record form (matter, project, invoice, …), mounted in the right pane (mirrors the existing `CommunicationTimelineRegarding` PCF pattern). **The PCF is a lightweight preview + launcher** — it does NOT host its own conversation modal.

- Shows the record's threads as a **tight list**: **max 3 threads**, each showing its **last 5 communications**. The **top thread is the record's default** (record number + name), **auto-expanded**.
- A **footer counter** shows total threads (e.g. "3 of 10 threads") with a link that opens the workspace widget as a modal.
- Each communication row supports a **quick-view popover**: first **200 chars** of the body (for email: to/from/date/subject), with an **open icon**.
- **Opening (a thread "open" icon, a quick-view "open", or the footer link) launches the Surface-2 two-pane widget as a MODAL, filtered to this record** — left pane = this record's threads, right pane = the conversation, optionally pre-selected to the clicked thread/message. **(Owner simplification, 2026-07-20: one shared widget, not a separate thread-scoped modal — so the user navigates to other threads inside the modal without returning to the PCF.)**

### Surface 2 — SpaarkeAI Workspace widget + standalone code page

A **Teams-like two-pane widget**, dual-use: mounted in the SpaarkeAI workspace (registry type `communications-list`) **and** mountable as a **standalone Vite code page** outside SpaarkeAI.

- **Left pane** — lists **all threads by name** (including record-less ones), with **channel/type filters** and a **word filter**. Includes **"＋ Create thread"** → **NewThreadModal** (associate to a record *optionally*, name, description).
- **Right pane** — selecting a thread opens the **ConversationView inline** (same component as Surface 1's modal), with the same in-line reply + email-modal affordances.
- **Record-less threads** live here (they have no record form to appear on). They are auto-named from participants and renamable.

### The shared surface: a two-pane widget (thread list + ConversationView)

**One shared, mount-agnostic component** — mounted inline in SpaarkeAI, as a standalone code page, AND as a modal launched from the record PCF. It takes an optional **regarding filter** (`entityType`+`id`): launched from a record it shows only that record's threads; in the pure workspace it shows all threads.

- **Left pane** — the thread list (by name; filters; ＋create). Selecting a thread loads it on the right.
- **Right pane — ConversationView** (Teams-style chat flow):
  - A **participant header** at the top: **avatars + participant names** (from the R2 participant-junction rollup).
  - Others' messages **left**, the user's **right**; **chronological, newest at the bottom**.
  - The user's own bubbles show **message status** (sent / delivered / failed, from `statuscode`).
  - A **chat input** at the base for a new message (existing message send path).
  - An **email icon** → **email modal** (`SendEmailDialog`), auto-associated to the current thread + regarding record, with an embedded record link.
  - A **Forward** action on a message → email modal in **forward** mode, prefilled. *(Drafts are NOT a chat action — they live only inside the email modal.)*
  - An **email in the flow** renders as a **chat block** (to/from/subject) with an **open icon** → opens that email in the email modal.
  - **Refresh**: ~5s polling (existing) **plus an on-demand refresh control**, and auto-refresh on send.
  - Openable **scrolled to a specific message** (from the quick-view "open" icon).

---

## 5. Component architecture & §11 Reuse Ledger

**Principle (root CLAUDE.md §11): default to reuse.** Investigation confirms the conversation *core* is fully reusable; new work is presentational.

| R3 component | Verdict | Reuse basis / what's new |
|---|---|---|
| **Conversation core** (state, polling, ordering, DTOs, send/read) | ♻️ **Reuse as-is** | `CommunicationTimeline` reducer + `useThreadPoll`/`useRegardingPoll` + `buildTimeline` + `communicationTimelineApi.ts` (`readThread`, `readByRegarding`, `sendTimelineMessage`). `sendCommunication` **already supports `threadId` + `communicationType:'message'` + `inReplyToMessageId`**. |
| **ConversationView** (Teams bubbles, mine-right/others-left, mount-agnostic) | 🆕 **Build new view, reuse core** | New bubble presentation. **Copy bubble CSS from `SprkChatMessage`** (`alignSelf: flex-end/flex-start`, `maxWidth:80%`, brand vs neutral) — but key the side on **sender identity**, not AI role. Reuse `ChannelBadge`, `UnreadIndicator` as-is. |
| **MessageQuickView** (200-char popover + open→pin) | 🆕 **Build new, copy pattern** | **Copy `AiSummaryPopover`** mechanics (lazy-on-open Popover, positioning, arrow, scroll). Sanitize body via DOMPurify like `MessageRow`. *(Note: the Calendar has no event popover to reuse — the reusable popover pattern is `AiSummaryPopover`.)* |
| **EmailModal** | ♻️ **Reuse `SendEmailDialog`** | The canonical Fluent `Dialog` email modal (EmailComposer, ADR-045). Pass `associations` built from the regarding record. **Extend** to accept a `threadId` + render a clickable record link (see backend item (d) + a small composer prop add). |
| **NewThreadModal** | 🆕 **Build new (thin), reuse parts** | Reuse `RecipientField`, `BodyEditor`, `AttachmentList`, `TimelineComposeBox`. Backend `POST /threads/direct` already does find-or-create for a record-less 1:1 thread. |
| **Thread-list pane** (Surface 1 tight list + Surface 2 left pane) | 🔧 **Extend** | Surface 1 record threads: reuse `ThreadGroup` + `readByRegarding`. Surface 2 all-threads list: needs the **new list-all-threads endpoint** (backend (a)). Workspace registration **keeps type string `communications-list` + section id `communications`**. |
| **Record-form pane PCF** (Surface 1) | 🔧 **Extend pattern** | Mirror `CommunicationTimelineRegarding` (placement-bound, reads Xrm context); `MatterHeader` host wrapper for private `FluentProvider` + theme storage. |
| **Standalone code page** (Surface 2) | 🔧 **New page, proven pattern** | Vite single-file page mounting the thread widget; pattern proven by `sprk_communicationspage` / `CommunicationPage`. Dual-use with the workspace registry (like Calendar Pattern D). |

**Net new code is presentational only**: Teams bubble rows, a quick-view popover, a new-thread modal shell, a thread-list pane, one pane PCF, one code page. **No transport/state/send/auth/recipient code is rebuilt.**

---

## 6. Backend increment (this is NOT "mostly UI")

Investigation found the read/send framework mature, but the Teams-style workspace exercises three things R1/R2 deliberately deferred. **A small backend wave precedes the UI.**

| # | Addition | Why needed | Size | BFF-hygiene note |
|---|---|---|---|---|
| **(a)** | `GET /api/communications/threads` — **list all threads incl. record-less**, paged/searchable by name, channel/type filter | Every read today is keyed by thread-id or regarding record; record-less/direct threads are invisible to any list. The Surface-2 left pane has nothing to call. | **M** | New endpoint + `CommunicationThreadReadService.ListThreadsAsync`. **Impersonated** (`MSCRMCallerID`), **no membership-union** (retired — `../messaging-communication-app-r1/notes/access-model-decision.md`). Reuse the existing access-filter seam. |
| **(b)** | **Participant-based thread naming** + a **BFF rename endpoint** | Naming today does subject→record→"Conversation"; never looks at participants. Users must be able to rename a thread and have it stick (the "obvious edit"). | **M** + **S** | Extend `ThreadResolver.BuildTopic`/`ReDeriveThreadNameAsync` to roll up the R2 participant junction. Rename is an **R3 UI action → new BFF rename endpoint** that sets `sprk_name` + flips `sprk_nameisautoderived` to Edited (so later auto-re-derive won't clobber it). **No Dataverse plugin** (hard MUST NOT) — renames route through the BFF, never a raw form edit. |
| **(c)** | Enrich single-thread read DTO with **direction + sender identity** | `ReadThreadAsync` returns the ordered flow but has no `direction` and only a raw `from` string — can't reliably render mine-right/others-left bubbles, and leaves the header `Name` null. | **S–M** | Add `sprk_direction` to the `$select`; add `Direction` + sender `systemuserid`/display-name to `ThreadMessageDto`; populate `ThreadReadResult.Name` on the single-thread path. |
| **(d)** | **Honor `ThreadId` on the email send branch** | Email sends currently **ignore** `ThreadId` (only messages honor it), so an email can't be pinned to the open conversation. | **S–M** | Mirror the message path's `AssignExplicitThreadAsync`. Regarding auto-association already works when the client passes `Associations`. |

**Reuse already in place**: `POST /threads/direct` (find-or-create record-less 1:1 thread — basis for "＋ Create thread"), `GET /threads/{id}/messages` (single-thread poll), the whole send/dispatch/participant-index pipeline.

### Placement Justification (root CLAUDE.md §10)

All four additions extend the **existing** `Services/Communication/` surface (read service, thread resolver, send request handling) — **no new AI dependency, no new package, no new background worker**. The only new endpoint (a) is a read that belongs with the other communication reads. Expected publish-size delta ≈ 0. Hot-path declaration below.

---

## 7. Data model — no schema change required

Investigation confirms:
- **Record-less threads** need **zero schema change** — regarding lookups are `RequiredLevel None`, and `sprk_threadtype = Direct 1:1 (100000001)` already models a non-record conversation.
- **Participant naming** is a **read-time computation** — aggregate the R2 message-grain `sprk_communicationparticipant` junction across a thread's messages. A denormalized thread-participant rollup is a **perf-only option, deferred** (only if a thread-*list* with participant names shows measured latency).
- **Email/Message** are one entity, transport-only difference. Channel enum: Email `100000000`, Teams `100000001`, SMS `100000002`, Notification `100000003`, **Message `100000004`**.

### ⚠️ Prerequisites to confirm before build
1. **R2 schema is live.** The R2 thread additions (task 002: 11 typed lookups + discriminator + 2 markers) and the **participant junction (task 003)** were *authored but apply-pending* when written (MCP was offline). The Surface-1 PCF now reads real thread data → task 002 is likely applied. **Confirm the participant junction (task 003) is live** — R3's person filters *and* participant-based naming depend on it.
2. **Doc-drift fix**: `docs/data-model/sprk_communication.md` is missing `Message = 100000004` and the R1 messaging columns — update as part of R3 (trust `CommunicationType.cs`).
3. **Open-thread message visibility** (`access-model-decision.md` carry-forward): confirm the owner's resolution for whether impersonated reads see Open/record-anchored messages, since the thread-list last-message preview depends on it.

---

## 8. Hot-Path Declaration (root CLAUDE.md §10 §G)

| Hot path | Touched? | Notes |
|---|---|---|
| BFF (`Sprk.Bff.Api`) | **Yes** | Backend items (a)–(d): new list endpoint, ThreadResolver naming, DTO enrichment, email ThreadId. |
| SpaarkeAi (`src/solutions/SpaarkeAi/**`) | **Yes** | Surface 2 workspace widget (dual-use; keeps `communications-list`). |
| ci-workflows | No | |
| skill-directives | No | |
| root-CLAUDE.md | No | |

Run `/conflict-check` at project start and before each BFF wave (shared `Services/Communication/`).

---

## 9. ADR alignment & tensions

| ADR / constraint | Relevance | Path |
|---|---|---|
| **ADR-045** (canonical send engine; modes/mounts; no 6th send impl) | R3 reuses `EmailComposer`/`SendEmailDialog` + `sendCommunication`; **no new send path**. | ✅ Comply |
| **ADR-046** (ACS messaging channel) | Message sends stay on the existing ACS branch. | ✅ Comply |
| **ADR-024** (regarding family) | Reuse the 11 typed lookups + discriminator; no second regarding mechanism. | ✅ Comply |
| **ADR-026** (Path-A: PCF on OOB form, no Custom Page/FCC) | Surface-1 pane PCF is the same exception R2's timeline PCFs use. | ✅ Comply (cite in PR) |
| **ADR-028** (auth v2) | All BFF calls via `@spaarke/auth` `authenticatedFetch`. | ✅ Comply |
| **Access model** (no membership-union on reads; impersonation + access-filter) | The new list-all-threads endpoint MUST use impersonation, not a hand-computed union. | ✅ Comply (binding) |
| **ADR-021** (Fluent v9, dark mode) | Bubbles/popover/modal use Fluent v9 tokens; dark mode passes through host `FluentProvider`. | ✅ Comply |
| **ADR-038** (test strategy) | Seam tests for the new read endpoint; component tests for bubble/popover/modal; participant-naming unit tests. | ✅ Comply |

No ADR *conflict* surfaced (root §6.5 not triggered). The edit-preserve mechanism is **resolved to a BFF rename endpoint** — Spaarke does not use Dataverse plugins (hard MUST NOT), so thread renames route through the R3 UI → BFF, which sets the name + the `sprk_nameisautoderived` marker.

---

## 10. Industry-pattern completeness check (so we don't miss anything)

Benchmarked against Teams, Gmail, Outlook conversation view, and Front (shared inbox). **Covered by the design**; **flagged** = decide in spec.

| Capability | Status |
|---|---|
| Unified stream + channel filter | ✅ Surface 2 filters; R2 grid chips |
| Thread/conversation grouping | ✅ core |
| Chronological flow, newest at bottom | ✅ ConversationView |
| Mine-right / others-left bubbles + sender identity | ✅ (needs backend (c)) |
| In-context reply (chat input) | ✅ reuse send path |
| Reply / Reply-All / Forward (email) | ✅ EmailComposer modes |
| Drafts | ✅ EmailComposer `draft` mode (decide if exposed in R3) |
| Attachments | ✅ existing |
| Quote-into-message / quote-into-email | ✅ existing (R2 task 063) |
| Quick-view preview + deep-link to a message | ✅ MessageQuickView + pin |
| Unread state + markers | ✅ existing (`UnreadIndicator`, unread-count) |
| Message status (sent/delivered/failed) | ✅ `statuscode` exists — **flag**: surface it in bubbles? |
| Participant list / avatars | **Flag** — show a participant header in ConversationView? (data exists) |
| Search within a conversation | **Flag** — in-thread search (out of R3?) |
| Read receipts / presence / typing | **Flag** — likely OUT (transport doesn't provide) |
| @mentions | **Flag** — likely OUT (defer with semantic-AI project) |
| Notifications ("communication-arrived") | **Flag** — R2 reserved the notification-spine kind; R3 stays BFF-polling unless owner wants push |
| Accessibility (keyboard, ARIA, SR) | ✅ required (Fluent v9 + ADR) |
| Long-thread performance (virtualization/paging) | **Flag** — existing scan bounds; add windowing if threads get large |

### 10.1 v1 must-haves — confirmed with owner (2026-07-20)

| Feature | Decision | Cost |
|---|---|---|
| **Global keyword search** | Lean on **Dataverse Search** — add `sprk_communication` (subject/body/from/to) to the search index; security-trimmed (aligns with privilege). Results open the record; deep-link into the conversation is a fast-follow. **No custom search infra.** | Low (config) |
| **Read/unread state** | Add a per-user **last-seen marker** per thread + mark-read/unread/mark-all-read. R2 flagged the backing field is missing — without it, unread badges are cosmetic. | S–M |
| **Attachments** | Open/preview/download from a message (SPE-backed) + attach-on-compose. | M |
| **Privilege / privacy accuracy** | Surface `isPrivate` / `isInternalOnly` / `privilegeClassification`; the participant/recipient display MUST reflect actual permitted recipients — never imply someone received/saw a restricted comm. Access already impersonation + filter-based. | S — correctness-critical |
| **New-communication awareness** | Wire the **notification spine** `communication-arrived` (already reserved by R2) as the **awareness layer** → unread badge + toast; conversation content stays BFF-polling. Verify the spine's producer/consumer contract at spec time. | S–M (consume existing engine) |
| **Thread lifecycle** | Archive/close, pin/favorite, mute. | M |
| **Accessibility + empty/loading/error** | Shippability floor (keyboard, ARIA, SR, states). Lower priority than the above but required. | S (ongoing) |

**Fast-follow (post-v1):** reactions + flag/follow-up · drafts view · signatures/send-as · canned replies · grid bulk actions · long-thread virtualization · in-widget search deep-linking into the conversation.
**Explicitly out:** semantic find-similar (AI project) · @mentions / presence / typing / read-receipts (transport doesn't provide).

> **Scope note:** global search (Dataverse Search) and new-comm awareness (notification spine) lean on **existing platform capabilities** — they reduce build, not add it.

---

## 11. Phased plan (proposed)

| Phase | Content | Gate |
|---|---|---|
| **P0 — Prototype** (D6) | Clickable `/prototype` of ConversationView (Teams bubbles, email-as-block, 200-char quick-view popover, chat input, email icon) with mock threads/messages. Optionally the Surface-2 two-pane layout. **Iterate the feel before any real code.** | Owner sign-off on interaction + layout |
| **P1 — Backend wave** | Items (a)–(d) + confirm R2 schema live + doc-drift fix. Seam tests. | BFF deploy + endpoints return data |
| **P2 — Shared components** | ConversationView (bubbles), MessageQuickView, NewThreadModal, thread-list pane; extend `SendEmailDialog` for thread/record-link. Jest/component tests. | Storybook/harness review |
| **P3 — Surface 1** | Right-pane PCF (mirror `CommunicationTimelineRegarding`); tight list → modal; quick-view → pinned modal. | Placement on ≥1 record form, UAT |
| **P4 — Surface 2** | Workspace widget (keep `communications-list`) + standalone code page; left-pane thread list + filters + create-thread; inline ConversationView. | Dual-deploy (LegalWorkspace + SpaarkeAi) + code page |
| **P5 — Wrap** | Participant-naming polish, edit-preserve trigger, docs, test-diet. | Merge to master |

---

## 12. Resolved decisions (owner, 2026-07-20 round 2)

| # | Decision |
|---|---|
| Q1 | **Surface 1 opens the shared two-pane widget as a modal, filtered to the record** (not a separate thread-scoped modal). The PCF shows a footer counter ("3 of 10 threads") linking to it. This simplifies the design — thread navigation happens inside the modal, not by returning to the PCF. |
| Q2 | **Yes** — participant header with avatars in ConversationView. |
| Q3 | **Yes** — show message status (sent / delivered / failed) on the user's own bubbles. |
| Q4 | **Forward = yes** (a conversation action → email modal in forward mode). **Drafts = no** chat action (email modal only). |
| Q5 | **Rename via a BFF endpoint, NO Dataverse plugin** (hard MUST NOT). "Edit-preserve" means: when a user renames a thread, flip `sprk_nameisautoderived` so auto-naming won't overwrite their name. Renames route through the R3 UI → BFF. |
| Q6 | **Keep polling + add an on-demand refresh control** (and auto-refresh on send). |
| Q7 | **Start simple** — the Surface-2 thread list is a **recency-ordered, filterable list** with a per-row unread indicator; add grouping/virtualization only if thread volume later proves it needed. |

*All §12 questions resolved — the design is coherent and ready for the P0 prototype.*

---

## 13. Risks

- **Backend is bigger than "mostly UI"** — items (a)/(b) are net-new server capability; size the backend wave honestly (combined **M–L**).
- **Impersonation + Open-thread message visibility** — the last-message-preview in the thread list can be empty under the current access model; resolve before relying on previews.
- **R2 schema apply-pending** — confirm task 003 (participant junction) is live before P1.
- **Shared-component blast radius** — extending `CommunicationTimeline`/`SendEmailDialog` touches surfaces R2/R4 ship; characterize existing behavior before extending (ADR-038).
- **Bubble semantics** — "mine vs theirs" needs reliable sender identity (backend (c)); avoid brittle email-string matching.
