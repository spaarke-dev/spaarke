# Task 055 — Direct messaging UI and the Messages view (notes)

Branch `task/fm-055` from `work/field-mapping-server-write-path-r1` (aa3214b7eb). Rigor FULL. Issue #2922.

## Alignment check (owner direction 2026-11-22: 055 first, then 056; 055 and 056 aligned; other projects aligned)

- **Hosts of `ConversationWorkspace`** (all on master):
  - all-mode: `sprk_communicationconversationpage` (`src/solutions/sprk_communicationconversationpage/src/App.tsx`) and `CommunicationsWorkspaceWidget` (`Spaarke.Communication.Components`; mounted by LegalWorkspace and the Console workspace widgets). Both already pass `navigationService`, so neither host changes.
  - record mode: the `CommunicationConversationPanel` PCF (React 16, `regarding` set). 055 does not change record mode.
- **messages-enhancements-r1** (requirements only, not started, no session): MR-2 ("message a person" entry point) is this task; MR-1 (private message in a record's context) is task 056. Both moved here by the owner's 2026-11-20 decision. Its `requirements.md` is marked accordingly in this task's commit so neither is built twice. MR-1's access rules (UAC-r2, 2026-11-09) are the starting point for 056a.
- **messaging-communication-app-r3** (built `NewThreadModal`, the shell and `GET /threads`): no open work on these files.
- **unified-access-control-r2**: owns the Direct access files; reviews the two server changes below (they change how Direct threads are created and listed).
- **Open PRs**: #1293 touches `ConversationView.forward.test.tsx` only among these components; no overlap with 055's files.

## Design

### Client (`@spaarke/ui-components`)

- **Messages view.** In all-mode `ConversationWorkspace` gets a two-tab switch above the thread list: **All** (today's list) and **Messages** (the caller's 1:1 Direct conversations). Record mode shows no switch. The Messages list calls `listThreads({ kind: 'direct' })`; each row shows the other person's name.
- **New direct message.** In the Messages tab the ＋ opens `NewDirectMessageModal`: one internal user (the Dataverse lookup side pane on `systemuser`, filtered to enabled, non-application users other than the caller) and an optional first message. Submit: `startDirectThread` (find-or-create of the pair's thread, so a second "new" message to the same person opens the same thread), then, when a body is given, `sendCommunication` on that thread. The shell selects the thread. In the All tab the ＋ keeps today's record-anchored `NewThreadModal`.
- The user filter is a shared constant beside `ACTIVE_CONTACT_LOOKUP_FILTERS` (`LookupField/lookupFilters.ts`), reused by 056's "private to …" picker.

### Server (BFF)

1. **`POST /api/communications/threads/direct` applies the Direct participant rule.** The other participant must read enabled and not an application user (`DirectMessageRecipient.ReadEligibilityAsync`, the rule the outbound pair routing already applies). Otherwise 400 `VALIDATION_ERROR`, and nothing is created or shared. A fault of the read refuses (503). The picker filter is a convenience; this is the rule.
2. **`GET /api/communications/threads?kind=direct`.** The impersonated query (unchanged visibility gate) adds `sprk_threadtype eq Direct` and no typed regarding lookup. Each row is then kept only when the one Direct-pair shape rule (`DirectMessageRecipient.ReadStrictPairShapeAsync`) reads it a strict pair AND the caller is its owner or its other participant; a fault drops the row (narrower, never wider). Rows carry `counterpartName` (the other participant's full name). Paging signals come from the rows read, as for withheld rows today. No `kind` (or `kind=all`) is unchanged. Any other value is 400.

### §11 answers

- **`kind=direct` filter** — Existing: `GET /threads` all-mode list. Extension: it extends that endpoint (no new endpoint). Cost of doing nothing: the client would filter a mixed page, so a user with many record threads sees an empty or partial Messages view, and a reader with wider thread Read would see other people's Direct threads (AC3 fails).
- **`counterpartName` on the list row** — Existing: the thread name, which is "Direct Conversation" for every pair and is shared by both participants. Extension: an optional field on the existing row. Cost of doing nothing: every Messages row reads "Direct Conversation".
- **`NewDirectMessageModal`** — Existing: `NewThreadModal` (record-anchored: name, required record, `createRecordThread`). Extension: its contract is record-anchored; a Direct variant shares only the `FormModal` shell and the message box, and branching both flows in one component would make each harder to follow. Cost of doing nothing: no screen starts a Direct conversation (AC1).
- **Eligibility check on `POST /threads/direct`** — modification of an existing endpoint's validation; no new surface.

### 055 ↔ 056 alignment

- MR-1 (UAC-r2's recommendation): a private message in a record's context is a Direct message in the pair's thread that carries its own non-filing context reference; owner the Direct team; audience the stamped pair. So the pair thread 055 lists is where 056's messages live too, and the Messages view shows them. 056 adds the context reference and the record-surface display for the pair.
- 056's "private to …" picker reuses 055's user filter and the server participant rule.
- Nothing in 055 assumes a pair thread carries no message-level context.

## Known limits

- A Direct thread can still be renamed by its owner from the message pane, which renames it for both; the Messages view shows the other person's name, not the thread name.
