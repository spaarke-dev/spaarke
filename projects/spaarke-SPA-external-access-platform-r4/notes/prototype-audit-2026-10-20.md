# R4 input: prototype vs shared components and icons (2026-10-20)

This is a read-only audit made from the R3 session, to feed `design.md`. Prototype: `c:\code_files\spaarke-prototype\projects\2026-10-spa-record-modal\`; it is untracked in the prototype repo, so treat it as read-only. Repo references are to R3 at master `0b976628e`.

## Owner direction
- R4 = SPA UI/UX: one config-driven record modal for Matter, Project, Work Assignment and Invoice (see the prototype README).
- The prototype is the visual reference, not the spec. It does not yet follow all shared components and tool icons; `design.md` corrects that. The owner will send a list of changes.
- `design.md` is refined in the R3 session (context continuity). A new R4 session then runs `/design-to-spec` → `/project-pipeline`.

## 1. Hand-rolled in the prototype where something shared or existing applies
| Prototype piece | Use instead | Note |
|---|---|---|
| Section card body (`RecordSection.tsx`) | Shared `HeaderToolbar` (already used) + check `RecordCardShell` | Possible fit |
| Empty states (`DocumentsSection.tsx:58,103`) | Shared `EmptyState` (`components/index.ts:165`) | R3 also hand-rolls (`DocumentLibrary.tsx:1025`, `EventsCalendar.tsx:723`, `SmartTodo.tsx:833`): fix both |
| Record header (`RecordModalHeader.tsx`) | Check `RecordHeaderShell` / `RecordHeader` (`.claude/patterns/ui/record-header-composition.md`) | A form-style header with lookups; fit not confirmed |
| Record toolbar (`RecordToolbar.tsx`, Fluent `Toolbar`) | No shared record command bar (`DocumentToolbar` covers only document actions) | Decide: new shared component or SPA-local |
| Documents table (Fluent `Table`) | R3 `DocumentLibrary.tsx` (upload, download, history, delete) | Reuse R3; apply the overflow fix (fixed layout + ellipsis + tooltip) |
| Messages (`MessagesSection.tsx`) | R3 `RecordMessages.tsx`; library has `CommunicationTimeline`, `MessageQuickView`, `ConversationView` (fit unchecked) | Reuse R3 |
| Tasks & events calendar/agenda (365 lines) | R3 `EventsCalendar.tsx` + `SmartTodo.tsx` (with `DeleteOwn`, `AccessLevel`) | Reuse R3; today they are read-only (`RecordDetailModal.tsx:517-518`) |
| Info fields | R3 `RecordDetailFields.tsx` | Reuse; new fields need the external column allow-list |
| Modal envelope | R3 `RecordDetailModal.tsx:606-660` (nav, Close, denied/error/loading/deleted) | The prototype copies it; keep `onBeforeNavigate` (`MODAL-DESIGN-SYSTEM.md:65`) |
| Scrollbar (`RecordModal.tsx:83`) | Shared `thinScrollbarStyle` (`MODAL-DESIGN-SYSTEM.md:112`) | Confirm |

## 2. Icons
- **There is no icon standard.** No registry, no Regular/Filled rule, no size rule in docs/standards or .claude/patterns. The only rule: `HeaderToolbar/README.md:10,27-34` (Fluent v9, size 20, tooltip required). The prototype uses unsized `…Regular`; R3 mostly does too.
- Where the prototype differs from repo usage:
  - New message: `ChatAddRegular` vs the repo's `MailRegular`.
  - Add task: `TaskListAddRegular` vs R3's `AddRegular`.
  - Add event: `CalendarAddRegular` vs R3's `AddRegular`.
  - Copy link: `LinkRegular` (repo also has `CopyRegular`).
  - Upload/section add: `ArrowUploadRegular` / `AddRegular` vs `DocumentToolbar`'s `Add20Regular`.
- Matches: Refresh `ArrowClockwiseRegular`, Expand `OpenRegular`, Delete `DeleteRegular`, Download `ArrowDownloadRegular`, `HistoryRegular`, `SendRegular`, `LockClosedRegular`.
- **Design decision needed:** an icon map for the record modal (and possibly a repo icon standard), with sizes per `HeaderToolbar`.

## 3. Modal rules
- Envelope OK (SprkModal `lg`, nav, Close only; no nested shell).
- Child `FormModal`/`ConfirmModal` must mount inside the parent `SprkModal`. Mounted as siblings, focus escaped. The standards don't say this yet: propose a doc addition.
- `record-modal-selection.md:22` says Layout 1 = `navigateTo` 85%; it does not cover the Xrm-free SPA (R3 and the prototype use SprkModal `lg`). Propose a doc note.

## 4. Gaps R4 must design (beyond layout)
- Toolbar actions Add task / Add event: no external-plane create routes (R3 is read-only for events and to-dos). Needs BFF routes + UAC review.
- New info fields (Matter type, practice area, responsible attorney, open date, description; Project type/lead/dates; WA instructions; Invoice billing period/description) are mostly NOT in `ExternalAccessModule` ReadableColumns: an allow-list change (UAC review).
- Grid columns (R3 task 002) touch the same allow-list. Decide together.
- Backlog: #2155 (email-missing screen text).
