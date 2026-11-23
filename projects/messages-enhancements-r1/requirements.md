# Messages Enhancements R1 — requirements

> **Status**: requirements capture only; not planned or scheduled. Next step: `/design-to-spec` when the project starts.
> **Captured**: 2026-11-09, from a design discussion in field-mapping-server-write-path-r1 (task 054) with the owner and
> unified-access-control-r2 (UAC-r2).
> **Predecessors**: `projects/messaging-communication-app-r1`, `-r2`, `-r3` (the messaging and conversation module).

## 1. Purpose

Collect the next round of enhancements to Spaarke's internal messages and conversations in one place:
- the owner's new requirement for private messages in a record's context;
- Microsoft Teams integration;
- the enhancements the earlier messaging projects deferred.

## 2. How messages work today (kept as is — owner, 2026-11-09)

**Two kinds of conversation thread** (`sprk_communicationthread.sprk_threadtype`):

- **Record-anchored thread.** It is started from, or filed under, a core record such as a Matter. It is owned by the record's team, so everyone who can read the record's communications reads it. On a secure record that is the Secure team only.
- **Direct 1:1 thread.** It is a person-to-person conversation between two internal users with no record. It is created by the server when a message resolves no record.
  - **Direct messages** are owned by a dedicated, memberless Direct team (field-mapping-server-write-path-r1 task 054). The two participants get access through grants made by the BFF (UAC-r2 task 198).
  - Each message records its own two participants: `sprk_sentby` and `sprk_directrecipient`. Both are written by the BFF and field-secured (owner decision 2026-11-09, option A).
  - A Direct thread never files its messages under a record, whatever lookups it carries.

**Filing a message from a Direct conversation under a record** (an explicit action) makes that message a record message:
- it is owned by the record's team and readable by the record's readers;
- the two participants keep their own access.

There is no "private, but in the context of a record" today: a message is either private or record-visible.

**Where conversations appear.** The shared `ConversationWorkspace` component is hosted by:
- the standalone conversation page (`sprk_communicationconversationpage`), which lists all of the user's threads, including Direct ones;
- the SpaarkeAI Communications workspace widget (`CommunicationsWorkspaceWidget`);
- the record-form conversation panel (PCF `CommunicationConversationPanel`, in a record-filtered modal).

## 3. Requirements

### MR-1 Private 1:1 message in a record's context (owner, 2026-11-09)

**Need.** A user can send another user a private message *about* a Matter (or another core record):
- it shows in that record's context for the two participants only;
- the record's team cannot read it;
- the record's ownership rules never apply to it.

**Access model** (UAC-r2's review, 2026-11-09). Binding rules for the design:

1. **A non-filing context reference.**
   - The message (not the thread) carries its own context lookup column or columns. The owner stays the Direct team, and the audience stays exactly the stamped pair {`sprk_sentby`, `sprk_directrecipient`}.
   - The context column is never a filing lookup, never the `sprk_regarding{type}` pair, and never a core-ancestor stamp column. Those columns drive child inheritance and the BFF's record scoping, and any of them would make the message record-visible.
   - It appears in none of these registries: SecureChildLineage, FilingParentOf, RecordFamilyEdges, InvoiceLinks, ScopeDimension, RootLinks. An ArchTest names the column and enforces this.
   - It may reference core records only (matter, project, work assignment), not invoices or other child records.
   - It is BFF-written only, field-secured like the 054b columns.
   - It is not modelled as a filed message with Restricted access: Restricted means "internal only", not "these two people", and a filed message is readable by the whole record team.
2. **Secure and Restricted records.**
   - At send, BOTH participants must hold Read on the referenced record, checked as each user (an impersonated probe, never app-only). Otherwise the send is refused.
   - On a Restricted record, a participant flagged external is refused (the existing Restricted rule).
   - The refusal uses one neutral message. It never tells the sender whether the record is secure, Restricted or simply unreadable by the recipient.
   - If a participant later loses Read on the record, they still see the message, but the record reference (name, number, link) is hidden for them. The context display is resolved as the viewer, never app-only.
3. **Record surfaces.**
   - Record-context surfaces select messages by FILING lookups only. Many of them read app-only through the BFF: timeline, counts, previews, Daily Briefing collectors, the notification feed, AI tools, RAG. Row security alone therefore does not limit them.
   - Only a per-viewer path, filtered to {`sprk_sentby`, `sprk_directrecipient`} = the viewer, reads the context lookup.
   - Counts by the context lookup are excluded or computed per viewer.
   - AI and RAG index entries for such messages carry the pair as their access list, never the record's, or the context lookup is kept out of the index.
   - A guard lists the only allowed readers of the context column.
4. **Filing later.**
   - A participant may file the message under the record with the explicit file action, behind a confirmation that the record team will then see it.
   - The existing filing rules then apply: record-team ownership; participants keep their access; a secure record goes to the Secure team; on a Restricted record, external participants lose access.
   - Filing to the context record is the default suggestion, never automatic.
5. **Compatibility with UAC-r2 task 198.**
   - Direct messages stay recognisable by Direct-team ownership.
   - 198's grants to record members and its revoke on unfile read filing lookups only; the context column never feeds them. The rule-1 ArchTest covers this.
   - Gate Decision Records for Direct messages stay shared with the pair (D-215).

**Open question.** Context on the message (UAC-r2's recommendation: one pair thread can then mix contexts) or on the thread (it changes `ThreadResolver`'s pair-reuse key)?

### MR-2 Start a private chat from the UI

- The server endpoint `POST /api/communications/threads/direct` exists (`CommunicationEndpoints.cs`), but no screen calls it; the "New thread" dialog creates record-anchored threads only. Add an explicit "message a person" entry point in the conversation surfaces.
- The endpoint has no name or description and does not say whether the thread was created or found (#670).

### MR-3 Microsoft Teams integration

- **Today:** Teams is a host surface only (a static personal tab, `projects/teams-app-r1`). The earlier messaging design ruled out native Teams chat through the Graph chat and channel APIs (`messaging-communication-app-r1/design.md`). The messaging code has an unused `CommunicationType.TeamsMessage` seam. The ACS chat service excludes Teams identities (`AcsThreadService.cs`).
- **Need:** messages integrate with Teams. The project decides the model, for example:
  - (a) Spaarke conversations surfaced in Teams (tab or bot);
  - (b) Teams chats and channel messages captured into Spaarke threads;
  - (c) ACS–Teams interoperability, so a Spaarke user can message a Teams user.

  Each changes the earlier "no native Teams chat" policy and needs an ADR. Each also has to respect MR-1's access rules: captured Teams content gets an owner and audience under the same model.
- **Related out-of-scope items to revisit:** a Teams bot, Teams mobile, and bot-initiated proactive messages (`projects/teams-app-r1/spec.md`, `projects/sdap-teams-app/spec.md`).

### MR-4 Deferred by the earlier messaging projects

| Item | Source | Status |
|---|---|---|
| Live channel: real-time append, typing, read receipts (headless `@azure/communication-chat`) | messaging-communication-app-r1 `design.md`; r3 `spec.md` | deferred |
| @mentions and presence | r3 `spec.md`, `design.md` | out of scope |
| Thread archive/close and mute (pin was built) | r3 `spec.md` | deferred |
| Reactions, flag/follow-up, drafts view, signatures/send-as, canned replies, bulk actions, long-thread virtualization | r3 `design.md` | idea |
| Search result deep-links into the conversation; in-thread search; participant header/avatars | r3 `design.md` | deferred / undecided |
| Tags and categories; semantic "find similar" | r3 `spec.md` | deferred to an AI project |
| SMS channel | r3 `design.md`; `ICommunicationChannelIngestor.cs` | future |
| Voice/video on the same ACS identity | r1 `design.md` | idea |
| External-portal messaging beyond plain text (attachments, email) | r1 `design.md`; `external-spa/.../RecordMessages.tsx` | partly built |
| Message content pushed live (today: badge and toast are pushed, content is polled) | r1 `design.md`; notification spine | partly built |
| Responsive-Intelligence action on `communication-arrived` | `CommunicationRiActionService.cs` | future |
| RegardingResolver catalog gaps for the thread's regarding family (`sprk_servicerequest`, `account`; contact column name) | messaging-communication-app-r2 `notes/r2-closeout-and-r3-handoff.md` §6 | deferred |
| Open defects: #666, #667, #669, #670, #672 | messaging-communication-app-r3 `notes/defer-issues.md` | open |

## 4. Dependencies

- **field-mapping-server-write-path-r1 task 054.** Direct-team ownership, `sprk_directrecipient`, and field security on `sprk_sentby`, `sprk_directrecipient` and the message's thread lookup (054b). MR-1 builds on these.
- **unified-access-control-r2 task 198.** The share side for Direct messages, and the rules MR-1 must keep.
- **spaarke-ontology-platform-r1.** Gate Decision Records on Direct messages.

## 5. Open questions for the project

1. MR-1: context on the message or on the thread.
2. MR-3: which Teams model, or which combination; the ADR change.
3. Which MR-4 items are in R1.
