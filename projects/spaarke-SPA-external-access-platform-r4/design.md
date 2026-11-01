# Design — Spaarke External Access Platform R4: record modal UI/UX (DRAFT for owner review)

> **Status**: 🟡 DRAFT. Owner review first; then a new R4 session runs `/design-to-spec` → `/project-pipeline`.
> **Created**: 2026-10-21, drafted in the R3 session for context continuity (owner direction 2026-10-20).
> **Predecessor**: `spaarke-SPA-external-access-platform-r3`. Dev SPA `3531ed65e`, dev BFF `4f5f22694`, UAC 036 flag ON.
> **Inputs**:
> - The prototype `c:\code_files\spaarke-prototype\projects\2026-10-spa-record-modal\`, untracked and read-only. It is the visual reference, not the spec.
> - `notes/prototype-audit-2026-10-20.md`.
> - A code inventory, 2026-10-21. File references are to master `3531ed65e`.

---

## 1. Context

R3 shipped one record-detail modal for six record types (`RecordDetailModal.tsx`).
- **What it is:** a SprkModal `lg` with fields, documents, invoices, events and tasks (read-only), and messages.
- **Problem:** it is a stack of sections, not a layout. The modal title is the record name. Events and to-dos are hard-coded View only (`RecordDetailModal.tsx:520-521`). There is no in-page preview.
- **Prototype:** it proposes one config-driven layout (toolbar, header, optional tabs, two columns) that mirrors the model-driven main form, streamlined to what SPA users can do.

R4 makes that layout production code using the shared components and the R3 data paths, and closes the R4 backlog R3 recorded.

## 2. Goals / non-goals

**Goals**
1. **G1 — Record template.** One `RecordTemplateConfig` per record type drives the modal. Adding a type means adding one config entry.
2. **G2 — Act where you read.** Upload, new message, add to-do and add event from the record, shown per record by the caller's rights (UAC #2078, `writeAccessPerRecord`).
3. **G3 — Shared components and icons.** Reuse the shared library and R3 components; adopt one icon map.
4. **G4 — Information that helps.** Each type shows the fields a partner needs. Every added column goes through the external allow-list with UAC review.
5. **G5 — Backlog.**
   - Inline document preview (DEF-003, #1828).
   - Email-missing screen text (ISS-017, #2155).
   - A licensed user's first sign-in skips the profile form (ISS-019, #2192).
   - The invoice files list (ebilling; owner decisions 2026-10-21).

**Non-goals**
- Editing core record fields. SPA users never edit Matter, Project, Work Assignment or Invoice fields.
- An invoice submission wizard. ebilling ships it (#1911: `InvoiceSubmitPage.tsx`, a "Submit invoice" action in `InvoicesWidget.tsx`, the shared `InvoiceSubmissionWizard`); R4 only links to it.
- Grid column changes. These are R3 task 002, but they share the allow-list (§5), so decide them together.
- Any change to the auth planes (R3).

## 3. The record template (G1)

The prototype contract (`src/template/types.ts`) is adopted with these changes:

| Prototype | R4 | Why |
|---|---|---|
| 4 types (Matter, Project, Work Assignment, Invoice) | **All 6 R3 types**: add Document and Service Request | The modal opens all six today (`RecordDetailFields.tsx:74-170`). Leaving two on the old modal keeps two code paths. |
| `AccessLevel` mocked per caller | Per **record**, from the BFF: `/me` `writeAccessPerRecord` plus each list's `canCreate` | UAC #2078 is live; the SPA must match what the BFF enforces |
| Toolbar `upload` on Invoice | **Removed** for Invoice; an invoice shows **Files** (list and download, no upload) | ebilling: invoice PDFs become Stored Files; no upload or versions route (owner 2026-10-21) |
| — | **"Submit invoice"** action on Matter, Project and Work Assignment, opening ebilling's wizard | Its routes hang off those roots (`/{matters,projects,workassignments}/{id}/invoice-submissions`) |
| Deep link `?record={token}:{id}` | Unchanged (`record-targets.ts:35-62`) | Notifications and email links depend on it |

**Layout:**
- Toolbar, header and tabs stay fixed; only the content scrolls.
- The two columns are 66% and 34%, and stack below 1000px.
- Tabs are off by default and set per type.
- Child dialogs mount **inside** the parent SprkModal; the prototype finding was that siblings let focus escape.
- The modal envelope keeps R3's states: navigation, Close only, denied, error, loading, deleted. It keeps `onBeforeNavigate` (`MODAL-DESIGN-SYSTEM.md:65`).

**Per type (proposal):**

| Type | Left (66%) | Right (34%) | Toolbar |
|---|---|---|---|
| Matter, Project | Information, Documents, Invoices | Messages, Tasks & events | Refresh · Upload · New message · Add to-do · Add event · Submit invoice · Copy link |
| Work Assignment | Information, Documents | Messages, Tasks & events | Same as Matter |
| Invoice | Information, Files | Messages | Refresh · New message · Copy link |
| Service Request | Information, Documents | Messages | Refresh · Upload · New message · Copy link · Delete request (requester; R3 `ServiceRequestActions`) |
| Document | Information, Preview | — | Refresh · Download · Copy link |

## 4. Acting on a record (G2)

**What the BFF has today** (`ExternalProjectDataEndpoints.cs`):

| Action | Projects | Matters | Work Assignments | Invoice | Service Request |
|---|---|---|---|---|---|
| Upload document | POST `:224` | POST `:277` | POST `:318` | retired by ebilling | POST `:359` |
| New message | POST `/{root}/{id}/messages` (`ExternalMessageEndpoints.cs:121`) | ✓ | ✓ | ✓ | ✓ |
| Add to-do | POST `:158` | POST `:191` | POST `:210` | — | — |
| Add event | POST `:456` | **missing** | **missing** | — | — |
| Delete own document / to-do / event | DELETE `:507/518/527` | ✓ | ✓ | — | — |

**R4 work:**
- **SPA.** Remove the `AccessLevel.ViewOnly` hard-code (`RecordDetailModal.tsx:520-521`). Drive Add to-do, Add event and the toggles from the per-record right, the same way `DocumentLibrary` already follows `canCreate` and `writeAccessPerRecord`.
- **BFF.** Add `POST /matters/{id}/events` and `POST /workassignments/{id}/events`, copying the project route at `:456`, with UAC review. These are the only new endpoints R4 needs for G2.
- **Hide, don't disable.** A caller without write sees only Refresh, Copy link and Download. A short line says why, for example "You can read this thread. Posting needs Collaborate access."
- **"Your access" in the header.** It names the caller's right on this record.

## 5. Information fields and the allow-list (G4)

**The rule:** the external module fetch refuses any column outside `ReadableColumns` (`Infrastructure/DI/ExternalAccessModule.cs`). Task 157 (UAC) trimmed those lists to "what the grid shows" (rule (b)). Adding a column therefore needs a stated reason and UAC review.

**Today's allow-list:**
- Project `:385-388`: id, name, number, statuscode, modifiedon.
- Matter `:501-504`: id, name, number, statuscode.
- Work Assignment `:480-484`: id, name, number, priority, response due, statuscode, regarding project.
- Invoice `:455-463`: id, name, number, date, invoice status, total, project, matter, statuscode.

**The prototype's fields checked against the schema** (`docs/data-model`). Several do not exist under the prototype's names:

| Type | Prototype field | Real column | Note |
|---|---|---|---|
| Matter | sprk_mattertype | `sprk_mattertype` (lookup → `sprk_mattertype_ref`) | Dropped by task 157 |
| Matter | sprk_practicearea | `sprk_practicearea` (lookup → `sprk_practicearea_ref`) | Dropped by task 157 |
| Matter | sprk_responsibleattorney | **not found** | Owner: which internal person is shown, if any? |
| Matter | sprk_outsidecounsel | `sprk_assignedoutsidecounsel` | Lookup |
| Matter | sprk_opendate | **not found** | Use `createdon`? |
| Matter | sprk_matterdescription | `sprk_matterdescription` | |
| Matter | modifiedon | `modifiedon` | Standard column |
| Project | sprk_projecttype | `sprk_projecttype_ref` | Dropped by task 157 |
| Project | sprk_matter | not confirmed; no project schema doc | Verify against live metadata |
| Project | sprk_projectlead | **not found**; `sprk_assignedattorney1`, `sprk_assignedparalegal1` exist | Owner: show an internal person? |
| Project | sprk_startdate / sprk_duedate | not confirmed on project | Verify |
| Project | sprk_projectdescription | `sprk_projectdescription` | |
| Work Assignment | sprk_regardingmatter | `sprk_regardingmatter` | |
| Work Assignment | sprk_assignedby | **not found**; `sprk_assignedto` exists (dropped by 157) | Owner: show the assigner? |
| Work Assignment | sprk_instructions | **not found**; `sprk_description` (5000 chars) | |
| Invoice | sprk_billingperiod | **not found** | Owner: needed? |
| Invoice | sprk_invoicedescription | not in `sprk_invoice.md` | Verify |

**Two points need a decision:**
1. **Lookups to people.** Showing internal people's names to partners (responsible attorney, project lead, assigned by) is a disclosure decision for the owner and UAC, not a layout one.
2. **Lookups to `_ref` tables.** These show a name; the SPA reads `@formattedValues`, not the reference table. Confirm the module fetch returns the formatted value without reading the reference entity.

## 6. Shared components and icons (G3)

**Reuse map** (CLAUDE.md §11; every component below exists, checked 2026-10-21):

| Need | Use | Status |
|---|---|---|
| Envelope | `SprkModal` (`components/SprkModal/SprkModal.tsx`) | As R3 |
| Section card header | `HeaderToolbar` (`components/HeaderToolbar`) | As the prototype |
| Section card body | `RecordCardShell` (`components/RecordCardShell`) | Check the fit, or keep the prototype's section card if it doesn't fit |
| Record header | `RecordHeaderShell` + `FieldGrid` (`components/RecordHeader`) | Check the fit against the "name + 3 facts + Your access" header |
| Empty states | `EmptyState` (`components/EmptyState.tsx`) | **Replace** hand-rolled ones in the prototype and in R3 (`DocumentLibrary.tsx:1025`, `EventsCalendar.tsx:723`, `SmartTodo.tsx:833`) |
| Status | `StatusBadge` | As the prototype |
| Dialogs | `FormModal`, `ConfirmModal` (SprkModal presets) | Mounted inside the parent modal |
| Scrollbar | `thinScrollbarStyle` (`theme/scrollbar.ts:32`) | Root package export not yet confirmed |
| Documents | R3 `DocumentLibrary` | Apply the overflow fix: fixed layout, ellipsis, tooltip |
| Messages | R3 `RecordMessages` | `CommunicationTimeline` exists; fit not checked |
| Tasks & events | R3 `EventsCalendar` + `SmartTodo` | Per-record access (§4) |
| Fields | R3 `RecordDetailFields` | Becomes the Information section |
| Record toolbar | **No shared one** (`DocumentToolbar` covers only document actions) | **Decide:** SPA-local, or a new shared `RecordCommandBar`. §11 answers are needed if shared. |

**Icons.** There is no repo icon standard; the only rule is `HeaderToolbar/README.md` (Fluent v9, size 20, tooltip). R4 adopts one record-modal icon map and proposes it as a standard. The proposed map (owner to confirm; every icon checked present in the installed `@fluentui/react-icons`, 2026-10-21):

| Action | Icon | Today |
|---|---|---|
| Refresh | `ArrowClockwise20Regular` | same |
| Upload | `ArrowUpload20Regular` | `DocumentToolbar` uses `Add20Regular` |
| New message | `ChatAdd20Regular` | repo uses `MailRegular` |
| Add to-do | `TaskListAdd20Regular` | R3 uses `AddRegular` |
| Add event | `CalendarAdd20Regular` | R3 uses `AddRegular` |
| Submit invoice | `ReceiptAdd20Regular` | — |
| Copy link | `Link20Regular` | repo also uses `CopyRegular` |
| Expand section | `Open20Regular` | same |
| Download / Delete / History / Send | `ArrowDownload` / `Delete` / `History` / `Send` 20Regular | same |

**Standards notes to propose** (main session, owner approval for `.claude/` edits):
- `MODAL-DESIGN-SYSTEM.md`: child dialogs mount inside the parent SprkModal.
- `record-modal-selection.md:22`: Layout 1 assumes `navigateTo`; add the Xrm-free SPA case (SprkModal `lg`).

## 7. Backlog folded in (G5)

| Item | What R4 does |
|---|---|
| **Inline preview** (DEF-003, #1828) | Document record: render the existing content route's bytes through a blob URL for PDF and images; others keep Download. No signed URL, no SPE pointer. Size cap and file-type list in the spec. |
| **Invoice files** (ebilling, owner 2026-10-21) | Invoice "Files" section over `GET /invoices/{id}/files` and `.../{fileId}/content`. A pending scan shows "Checking file…" with no Download (content answers 409 `file_scan_pending`). No upload, no versions. Drop the invoice-document upload and versions in the SPA. Ships back to back with ebilling's BFF change (BFF first), because the old invoice-document routes are retired together. |
| **Email-missing text** (#2155) | `RegistrationPage.tsx` `emailMissing`: tell the customer's IT admin to set the Email on the user's Entra account, then sign in again. |
| **Licensed user's first sign-in** (#2192) | Skip the profile form when the token email is a licensed user's (the gate learns it before the form); review the 60 s link-popup timeout. Needs UAC/auth review. |
| **Grid settings remembered** (#2489, owner 2026-10-31) | The grids remember each user's column widths and sort order. First step: per browser, through optional outputs on the shared `DataGrid`, keyed by grid configuration id, with a "Reset columns" command. Per user across devices is a later option (server store + external endpoint, §10/§11). Coordinate with the dataset-grid framework owners. |
| **Record Documents list on the shared grid** (#2490, owner UAT 2026-10-31) | The record modal's and project page's Documents list (`DocumentLibrary.tsx`) moves onto the shared `DataGrid`, so its columns size, resize and later remember like the tabs. Today its columns overlap and cannot be resized. Keep upload, download and delete-own. Fits the §3 record template. |
| **Record tabs for work-account contacts with grants** (#2508, owner UAT 2026-11-01) | A workforce contact (WorkforceHome) who holds a record grant sees no Projects, Matters, Work Assignments, Documents or Invoices tab: those widgets are partner-only (`planes: ['ciam']`). Show them to a workforce contact with grants, scoped by the accessible set (Tier-2). Decide empty-tabs vs none for a contact with no grants. Today a granted record is reachable only from the email link or the bell. |
| **Money columns in grids** (#2446, #2447) | Shared `DataGrid`: Money columns sort by value, not text, and Export writes the formatted values. The invoice grid shows two Money columns since R3 task 002. |
| **Profile email refused at registration** (#2449) | The profile form shows an email that registration then refuses; align the two checks. |
| **Messages refresh and new-message indicator** (#2452) | A refresh button and a "New message" indicator on Messages. |
| **Message threads** (#2453) | Show messages as threads, as the model-driven app does. |
| **Matter events: task type and assignee** (#2445, ontology D-199) | Show the task type and assignee on a matter's events; optional event deep link. |
| **Teams mobile layout** (#2466) | The Teams tab loads on phones but is not adapted to the surface (R3 071). |
| **Grant email to a workforce contact** (#2458, ISS-024) | Only if the owner moves it out of R3: the grant notifier must not email a home-linked workforce contact as an external person. Owner decision pending. |

## 8. ADR touchpoints

- **ADR-021** (Fluent v9, tokens), **ADR-050** and MODAL-DECISION-CRITERIA (record modal), **ADR-028** (no change to auth; the preview keeps the app-only byte stream).
- **ADR-008:** each new event route gets its own endpoint filters.
- **ADR-007:** the preview uses the existing content route, not Graph.
- **ADR tensions:** none known. The icon standard is new guidance, not a conflict.

## 9. Open questions for the owner

1. **Prototype decisions (prototype session):**
   - Title = type + number.
   - Hide, don't disable.
   - Tabs off by default.
   - "Your access" in the header.

   Accept all four?
2. **Your list of prototype changes** (audit: "the owner will send a list of changes"). It is not received yet; this draft assumes none beyond §3 and §6.
3. **Information fields** (§5): which fields per type, and whether internal people's names are shown to partners.
4. **Document and Service Request** on the template too (recommended), or left on the R3 modal?
5. **Record toolbar:** SPA-local or a shared component?
6. **Icon map** (§6): adopt, and propose it as a repo standard?
7. **"Submit invoice" in the record toolbar** (Matter, Project, Work Assignment): yes or no?
8. **Grid columns** (R3 task 002) decided together with §5, since they share the allow-list? Update 2026-10-31: R3 shipped the owner's columns (nine added, number and type columns 200px); only §5's new fields remain.

## 10. Draft success criteria

1. Every SPA record type opens in the template; title, header, sections and tabs match its config; the modal states and deep links work as in R3.
2. A caller with write on a record sees Upload, New message, Add to-do and Add event and can use them. A View-only caller does not see them. No UI-offered action answers 403. This holds on matters and work assignments too, including Add event.
3. Every added field is on the allow-list with UAC's review recorded; a column outside it is still refused.
4. A PDF or image document previews in place; other types download.
5. An invoice shows its files; a pending one shows "Checking file…"; no invoice upload or versions remain.
6. #2155 and #2192 are closed by the described behaviour.
7. Light, dark and Teams themes; keyboard focus stays inside the modal and its child dialogs; the layout works at 1000px and at phone width.

## 11. Placement Justification + hot-path declaration (CLAUDE.md §10, binding)

| Addition | New or extend | Existing / extension / cost of doing nothing |
|---|---|---|
| `POST /matters/{id}/events`, `POST /workassignments/{id}/events` | **New endpoints** in the existing `ExternalProjectDataEndpoints.cs`, copying `POST /projects/{id}/events` (`:456`) and its service call | **Existing:** the project route. **Extension:** the same handler, parameterised by root, if the code allows; otherwise two thin routes. **Cost of doing nothing:** "Add event" works only on projects. |
| Allow-list columns (§5) | **Extend** `ExternalAccessModule` `ReadableColumns` | **Existing:** the allow-list. **Cost of doing nothing:** the Information section cannot show the fields. |
| Invoice files routes | **ebilling's** change, not R4's | R4 only consumes them |

No new service, package or DI registration is planned. Publish-size delta and the CVE check are measured on the BFF task.

```xml
<hot-path-declaration>
  <bff>Y</bff>                   <!-- two event routes + allow-list columns -->
  <spaarke-ai>N</spaarke-ai>     <!-- external-spa only -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

---

*Next step: owner review of §9. Then a new R4 session runs `/design-to-spec` on this file.*
