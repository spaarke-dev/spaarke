# Spaarke External Access Platform R4 — AI Implementation Specification

> **Status**: Ready for Implementation (pending owner review of this spec)
> **Created**: 2026-11-09
> **Source**: `design.md` (owner review 2026-11-08, §9.0 and §7.2)
> **Code baseline**: master `57748ebf6d`, merged into `work/spaarke-SPA-external-access-platform-r4` (`df00583315`). The anchors in this spec were re-verified there on 2026-11-09. design.md cites the older master `3531ed65e`; where the two differ, this spec is right.

## Executive Summary

Spaarke External Access is the external SPA (`src/client/external-spa`). R3 left it with one record-detail modal that stacks sections and is read-only for events and to-dos. R4 replaces that modal with a config-driven **record template** covering all six SPA record types (Matter, Project, Work Assignment, Invoice, Service Request, Document). The template lets users **act where they read**, by each record's rights, through a new shared `RecordCommandBar` and one icon map. It adds the information partners need, behind the external allow-list with UAC review. It also closes the R3 and ebilling backlog handed to R4: Review detail for held invoices, invoice Files, inline preview, grid work, messages, workforce-contact tabs, sign-in fixes, and five R3 carry-over issues. Delivery is staged in four phases, each deployed to dev and E2E-proved.

## Scope

### In Scope

**P1 — Record template and actions**
- `RecordTemplateConfig` per record type drives the modal (toolbar, header, optional tabs, two columns at 66% and 34% that stack below 1000px). All six types use it, and the R3 stacked modal is retired.
- Shared `RecordCommandBar` and the record-modal icon map.
- Per-record access: remove both ViewOnly hard-codes; hide (not disable) actions; the "Your access" header line.
- BFF: `POST /matters/{id}/events` and `POST /workassignments/{id}/events`. The delete-route lock 409 gets ProblemDetails `status` and `title` (#2271 residual).
- "Submit invoice" record action on Matter, Project and Work Assignment, opening ebilling's existing flow.

**P2 — Invoices and documents**
- 082c Review detail: the grid column, the record's Invoices section, the header secondary line, an Information field, and the explanation lines. The held-Accepted badge.
- The invoice Files section (ebilling's routes); remove invoice-document upload and versions from the SPA.
- Inline preview for PDF and images on the Document record.
- #1886: list documents with no file, but hide Download and versions.
- #2490: the record Documents list moves onto the shared `DataGrid`.

**P3 — Grids, information, messages**
- §5 Information fields per type, including internal people's names, with UAC review per column.
- #2489: grids remember column widths and sort per browser, with a "Reset columns" command.
- #2446 / #2447: Money columns sort by value, and Export writes formatted values.
- #2452: a Messages refresh button and a new-message indicator. #2453: message threads.
- #2445: matter events show the task type and assignee, with an optional event deep link.

**P4 — Access, sign-in, carry-overs, Teams**
- #2508: workforce contacts with grants see the record tabs, scoped to their grants. Contacts with no grants see none.
- #2192: a licensed user's first sign-in skips the profile form. #2155: the email-missing text. #2449: profile and registration email checks aligned.
- #2269: retryable binder faults answer 503, and the SPA auto-retries.
- #2275: test warm-up. #2499: contact store log labels and test double. #2578: the acme readability-proof fault.
- #2466: Teams mobile layout.

**Closing**
- Standards proposals: the icon map, a MODAL-DESIGN-SYSTEM note on child dialogs, and the record-modal-selection SPA case.
- The inherited first-E2E checks, and wrap-up.

### Out of Scope
- Editing core record fields. Matter, Project, Work Assignment and Invoice stay read-only to SPA users.
- The invoice submission wizard. ebilling owns it (`InvoiceSubmitPage.tsx`, `ExternalInvoiceSubmissionEndpoints.cs`); R4 only links to it.
- ebilling's BFF changes: the `/invoices/{id}/files` routes, the `sprk_reviewdetail` column, its allow-list entry, the 082c-2/082c-3 logic, and the firm-hold backfill. R4 consumes them.
- Any change to the auth planes, except #2269's status code and #2192's first-sign-in path, both under auth/UAC review.
- Grid column sets beyond §5's new fields. R3 task 002 shipped the owner's columns.
- Per-user, cross-device grid settings (#2489's later option).
- R3 carry-overs moved or deferred (design §7.2):
  - #2270 and #2278 → unified-access-control-r2.
  - #1840 and #1737 deferred.
  - #2534 → spaarke-auth-system-of-record-r1.
  - The six already-fixed issues are closed.
- Reading `sprk_signal` from any external surface.

### Affected Areas

**SPA** — `src/client/external-spa/src/`
- `components/detail/RecordDetailModal.tsx`: replaced by the template. Holds today's ViewOnly hard-code (`:524-525`) and `INVOICE_COLUMNS` (`:367`).
- `components/detail/RecordDetailFields.tsx`: becomes the Information section.
- `components/detail/RecordMessages.tsx`: threads, refresh and indicator.
- `components/DocumentLibrary.tsx`, `components/EventsCalendar.tsx`, `components/SmartTodo.tsx`: per-record access; the shared `EmptyState`. `SmartTodo.tsx:629` forces ViewOnly for non-project roots.
- `widgets/InvoicesWidget.tsx`: the Review detail column.
- `registry/widgetRegistry.ts`: the 5 `planes: ['ciam']` widgets.
- `pages/RegistrationPage.tsx`: `emailMissing` at `:73/:147/:317`.
- `api/me-client.ts`: today 401/403 reject (`:23`, `:139`), with no 503 branch.
- `components/detail/record-targets.ts`: `RECORD_LINK_TOKENS` and `FEED_RECORD_TYPES`.
- `host/TeamsHostAdapter.ts`, `components/shell/*`: Teams layout.
- `tests/no-mock-identity.test.tsx`: #2275.

**Shared library** — `src/client/shared/Spaarke.UI.Components/src/`
- The new `components/RecordCommandBar/`.
- `components/DataGrid/DataGrid.tsx`: persisted settings, Money sort and export.
- Reuse only: `SprkModal` and its presets, `HeaderToolbar`, `RecordCardShell`, `RecordHeader/*`, `EmptyState`, `StatusBadge`, `CommunicationTimeline`, `theme/scrollbar.ts`.

**BFF** — `src/server/api/Sprk.Bff.Api/`
- `Api/ExternalAccess/ExternalProjectDataEndpoints.cs`: event routes. Today it has only GET at `:439/:447`, with the project POST at `:456` as the template.
- `Api/DocumentOperationsEndpoints.cs`: delete 409 (`CheckedOutDeleteResult`, about `:494-498`).
- `Infrastructure/DI/ExternalAccessModule.cs`: `ReadableColumns`.
- `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs`: `:790-794`, #2269.
- `Infrastructure/ExternalAccess/ContactIdentityStore.cs`: `:1825/:1856`, #2499.
- `Services/ExternalAccess/IdentityLinkReconciliationJob.cs`: `:378-387`, #2578.
- `Services/Signals/AssignTaskRecipientGate.cs`: `:496`, #2499.
- `Api/ExternalAccess/Dtos/ExternalProjectDtos.cs`: `hasFile` at `:142` (read only).

**Tests**
- `tests/integration/**` (KEEP paths per ADR-038).
- `tests/integration/auth/UnifiedAccessControl/IdentityBinding/InMemoryContactIdentityStore.cs`.
- The external-spa vitest suite.

## Requirements

### Functional Requirements

**P1 — Record template and actions**

- **FR-01 Record template config.** One `RecordTemplateConfig` per type gives the title, header facts, tabs, left and right sections, and toolbar actions. The modal renders only from config.
  - *Acceptance:* All six types open in the template with the per-type layout below. Adding a type changes only the config map and its section renderers. The R3 stacked modal code path is removed. `?record={type}:{id}` deep links and notification links open the right record.

  | Type | Left (66%) | Right (34%) | Toolbar (when permitted) |
  |---|---|---|---|
  | Matter, Project | Information, Documents, Invoices | Messages, Tasks & events | Refresh · Upload · New message · Add to-do · Add event · Submit invoice · Copy link |
  | Work Assignment | Information, Documents | Messages, Tasks & events | same as Matter |
  | Invoice | Information, Files | Messages | Refresh · New message · Copy link |
  | Service Request | Information, Documents | Messages | Refresh · Upload · New message · Copy link · Delete request (requester only) |
  | Document | Information, Preview | — | Refresh · Download · Copy link |

- **FR-02 Layout and envelope.**
  - Built on SprkModal `lg`.
  - The toolbar, header and tabs stay fixed; only the content scrolls, using `thinScrollbarStyle`.
  - The columns are 66% and 34% and stack below 1000px.
  - Tabs are off by default and set per type.
  - The title is type + number.
  - It keeps R3's states (navigation, Close only, denied, error, loading, deleted) and `onBeforeNavigate`.
  - *Acceptance:* Each state renders. The layout is correct at 1440px, 1000px and 375px. Content scrolls while the chrome stays fixed.

- **FR-03 Child dialogs inside the parent.** Upload, new message, add to-do, add event, and confirm dialogs mount inside the parent SprkModal, as SprkModal presets (`FormModal`, `ConfirmModal`).
  - *Acceptance:* A test proves keyboard focus stays inside the parent and the child while a child dialog is open. While a form or confirm child is open, Esc closes neither the child nor the parent and keeps what the user typed (the presets' `explicit`/`alert` dismiss, MODAL-DESIGN-SYSTEM); Cancel or × closes the child and returns focus to the control that opened it, and Esc then closes the parent. Cancel is on the left (ADR-050).
  - *Amended 2026-11-11 (owner, task 004):* the original text said "Esc closes the child first". That conflicts with the modal standard (an accidental Esc must not discard a draft); the owner chose to comply (§6.5 Path C).

- **FR-04 Shared `RecordCommandBar`.** It lives in `@spaarke/ui-components` and takes action descriptors as props (id, label, icon, onClick, visible, tooltip, optional overflow).
  - It composes `HeaderToolbar` and Fluent v9 primitives, without duplicating them.
  - It has no Xrm, no data fetching, and no entity-name literals. Its types are exported, and it ships with tests (ADR-012).
  - *Acceptance:* A hidden action is not rendered: it is not in the DOM and gets no tab stop. Every visible action has a tooltip that is also its aria-label. It works in light, dark and high-contrast. Unit tests cover visibility, overflow and keyboard. The SPA consumes it for every record toolbar.

- **FR-05 Icon map.** Record actions use the 20Regular icons below, from `@fluentui/react-icons`:
  - Refresh `ArrowClockwise`; Upload `ArrowUpload`; New message `ChatAdd`; Add to-do `TaskListAdd`; Add event `CalendarAdd`; Submit invoice `ReceiptAdd`; Copy link `Link`; Expand section `Open`.
  - Download, Delete, History and Send use `ArrowDownload`, `Delete`, `History` and `Send`.
  - The map lives in one exported constant.
  - *Acceptance:* No record-modal action uses an icon outside the map. The closing phase proposes the map as a repo standard (FR-34).

- **FR-06 Per-record access (G2).** Remove both ViewOnly hard-codes: `RecordDetailModal.tsx:524-525` and the non-project-root force in `SmartTodo.tsx:629`. Upload, New message, Add to-do, Add event, and delete-own are driven by `/me` `writeAccessPerRecord` and each list's `canCreate`, the same way `DocumentLibrary` already works.
  - *Acceptance:* A caller with write on a matter, project or work assignment sees and can use all four actions. A View-only caller sees none of them, and sees no disabled placeholders. **No action the UI offers answers 403.** Integration tests cover matter and work-assignment to-dos and events.

- **FR-07 Hide, don't disable, and "Your access".**
  - A caller without write sees only Refresh, Copy link and Download where they apply.
  - Each read-only section shows one short line saying why, for example "You can read this thread. Posting needs Collaborate access."
  - The header names the caller's right on this record.
  - *Acceptance:* The text is correct for each access level. The "Your access" value matches what the BFF enforces for the record.

- **FR-08 BFF event routes.** Add `POST /matters/{id}/events` and `POST /workassignments/{id}/events` in `ExternalProjectDataEndpoints.cs`. Copy the project route at `:456`: the same service call, the same endpoint filters, the same DTO and the same response. Parameterise by root where the handler allows it; otherwise add two thin routes. UAC reviews them before the deploy request.
  - *Acceptance:* The integration tests must fire for: a caller with write gets 201 and the event is linked to the right root. They must not fire for: a View-only caller gets 403, an inaccessible record gets 404 or 403 matching the project route, and a non-CIAM token gets 401. The ProblemDetails `errorCode` values match the project route.

- **FR-09 Delete-route lock 409 (#2271 residual).** The `CheckedOutDeleteResult` 409 in `DocumentOperationsEndpoints.cs` gets the same additive `status = 409` and `title = "Document Locked"` that the checkout 409 has (`:157-174`). The existing fields keep their names and shapes.
  - *Acceptance:* A test asserts that the delete 409 body parses as ProblemDetails and carries `checkedOutBy`. #2271 is then closed.

- **FR-10 Submit invoice action.** On Matter, Project and Work Assignment, "Submit invoice" opens ebilling's existing submission flow for that record. It is shown only under the condition `InvoicesWidget` uses today (`/me` `firms` non-empty) and only when the caller can submit for that record.
  - *Acceptance:* It is hidden for callers without firms, and it lands on the correct record in the flow. It is never shown on Invoice, Service Request or Document.

**P2 — Invoices and documents**

- **FR-11 Review detail (082c), all render points.** Render `sprk_reviewdetail` in four places:
  - (a) a "Review detail" column after Status in `InvoicesWidget`;
  - (b) the same field in the record's Invoices section, through `INVOICE_COLUMNS`;
  - (c) the Invoice record header's status-fact secondary line;
  - (d) an Information field, plus one fixed explanation line per option.

  **Binding gate (uac-r2):** render the value or a line **only when `statuscode` = 1 (In Review)**. Values `659490000`, `659490001` and `659490002` map to the owner-approved texts (design §7, verbatim). A blank value while In Review shows "The client is reviewing this invoice." **Any other value renders as blank.** The client never derives a priority. "Your access" doesn't use this field.
  - *Acceptance:* Each of the three values renders its approved text in all four places. A **must-not-fire** test: a Paid or Rejected row that carries a value renders blank everywhere. An unknown value renders blank.
  - *Gated on* ebilling's 082c-2 BFF deploy (the allow-list entry). Before that the field is blank.

- **FR-12 Held-Accepted badge.** A held Accepted invoice shows "In Review" in the warning tone. An un-held or overridden Accepted invoice stays green "Accepted". Reason code `ebilling.submission.overridden` counts as a released Accepted.
  - *Acceptance:* All three states are tested.

- **FR-13 Invoice Files section.** The Invoice record shows Files over `GET /invoices/{id}/files` and `.../{fileId}/content`: list and download, with no upload and no versions. A pending scan (content answers 409 `file_scan_pending`) shows "Checking file…" with no Download. The SPA's invoice-document upload and versions UI is removed.
  - *Acceptance:* All three file states (clean, pending, failed) render. No invoice upload or versions control remains.
  - *Gated on* ebilling's files-routes BFF change, which ships first and back to back. Until then this FR does not deploy.

- **FR-14 Inline preview.**
  - The Document record previews PDF, PNG, JPEG, GIF and WebP files up to **25 MB** (owner, 2026-11-09).
  - The bytes come from the existing content route through `authenticatedFetch` and render from a blob URL. There is no signed URL, no SPE pointer and no Graph (ADR-007, ADR-028 A1).
  - Other types and larger files show Download only. The blob URL is revoked when the preview unmounts.
  - Model it on the shared `PreviewModal` / `RichFilePreviewDialog` pattern where it fits.
  - *Acceptance:* PDF and image preview in place. A 26 MB PDF and a .docx show Download only. There is no memory leak across repeated opens: revoke is called.

- **FR-15 Documents with no file (#1886).** The SPA reads the existing `hasFile` field on the external document DTO (`ExternalProjectDtos.cs:142`). A document with no file is listed, but has no Download, no versions and no preview.
  - *Acceptance:* Both cases render correctly in the Documents list and on the Document record.

- **FR-16 Documents on the shared DataGrid (#2490).** The record and project-page Documents list (`DocumentLibrary.tsx`) renders on the shared `DataGrid`. Columns size and resize without overlapping, and long names ellipsise with a tooltip. Upload, download and delete-own are kept, by per-record access.
  - *Acceptance:* No column overlap at 1000px. Resize works. All existing document actions still work.

- **FR-17 Shared EmptyState.** `DocumentLibrary`, `EventsCalendar`, `SmartTodo` and every template section use the shared `EmptyState`.
  - *Acceptance:* No hand-rolled empty-state styles remain in those files.

**P3 — Grids, information, messages**

- **FR-18 Information fields (G4).** Each type shows the §5 fields that **exist in live metadata**. The first task in P3 verifies each candidate column; "not found" columns are dropped, not invented. Internal people's names (responsible attorney, `sprk_assignedattorney1` / `sprk_assignedparalegal1`, work-assignment `sprk_assignedto`) may be shown (owner, 2026-11-08).
  - Every new column is added to `ReadableColumns` with **UAC's review recorded**.
  - Lookups show the formatted value (`@formattedValues`) without reading the `_ref` entity. Confirm that the module fetch returns it.
  - *Acceptance:* Each field renders. A column outside the allow-list is still refused (a regression test). The UAC review is cited in the PR.

- **FR-19 Grid settings per browser (#2489).** The shared `DataGrid` gets optional, opt-in outputs to persist column widths and sort. The SPA stores them per browser in `localStorage`, keyed by grid configuration id, wrapped in try/catch. A "Reset columns" command restores the defaults. Coordinate the API with the dataset-grid framework owners.
  - *Acceptance:* Widths and sort survive a reload. Reset restores the defaults. When storage is unavailable, the grid uses its defaults without error. Other DataGrid consumers are unchanged.

- **FR-20 Money columns (#2446, #2447).** In the shared `DataGrid`, Money columns sort by numeric value, and Export writes the formatted values.
  - *Acceptance:* Sorting $9 / $10 / $100 orders them by value. The export cell text equals the displayed value.

- **FR-21 Messages refresh and indicator (#2452).** Messages has a Refresh action and a "New message" indicator when newer messages arrive.
  - *Acceptance:* Refresh fetches new messages. The indicator shows and clears correctly.

- **FR-22 Message threads (#2453).** Messages show as threads, as in the model-driven app. Reuse `CommunicationTimeline` if it fits; otherwise extend `RecordMessages`. All HTML goes through the shared `sanitizeEmailHtml`, and `allowRemoteImages` is **never** passed.
  - *Acceptance:* Replies group under their thread. A test asserts that remote `<img>` sources are stripped.

- **FR-23 Matter events: task type and assignee (#2445, ontology D-199).** A matter's events show the task type and assignee, with an optional event deep link. Neither value comes from `sprk_signal`. If the source turns out to be a Signal, it goes through a named external projection with uac-r2 review.
  - *Acceptance:* Both values render. The data source is cited in the PR.

**P4 — Access, sign-in, carry-overs, Teams**

- **FR-24 Workforce-contact tabs (#2508).** A workforce contact (WorkforceHome) with ≥ 1 record grant sees the Projects, Matters, Work Assignments, Documents and Invoices widgets, scoped to the accessible set (Tier-2). A contact with **no grants sees none of them** (owner, 2026-11-09). Partner (CIAM) behaviour is unchanged.
  - *Acceptance:* A granted record is reachable from the tab. The no-grant case shows no tabs. The tab lists contain only granted records. UAC reviews the change.
  - Revisit R3 known limits K2 and K3 here (`notes/task-090-notes.md` in R3).

- **FR-25 Licensed user's first sign-in (#2192).** The profile form is skipped when the token email belongs to a licensed user; the gate learns this before the form. Review the 60 s link-popup timeout and set its value in the auth review.
  - *Acceptance:* A licensed user's first sign-in goes straight in. A non-licensed user still sees the form. Auth/UAC review is recorded.

- **FR-26 Email-missing text (#2155).** `RegistrationPage` `emailMissing` tells the user to ask their organisation's IT admin to set the Email on their Entra account, then sign in again.
  - *Acceptance:* The text renders for `registration_email_missing`. #2155 closes.

- **FR-27 Profile email aligned with registration (#2449).** The profile form and registration apply the same email validity rule, so an email the form shows is never refused at registration.
  - *Acceptance:* A shared test covers both paths.

- **FR-28 Retryable binder faults answer 503 (#2269).**
  - `CallerPrincipalResolver` maps `contact_lookup_failed`, `contact_create_failed` and `contact_bind_failed` to a **503** ProblemDetails with the same `reasonCode` and `errorCode`, and a retry message. Real denials stay 403.
  - `me-client.ts` retries a 503 up to **3 times, with 1 s, 2 s and 4 s backoff**, then shows "We couldn't finish signing you in. Try again", with a Retry button (owner, 2026-11-09). Check every other caller of these statuses.
  - Once ebilling-r1's task-130 batch is on master (`work/ebilling-r1` `017a965312`, reported 2026-11-10), a stale grant answers 403, not 404. The 404 branch in `pages/ProjectPage.tsx:465` is then dead: fold it into the 403 handling in the same change. The list `truncated` flag needs no change; it can now be set for invoice-linked chains too, and every list already shows `TruncatedListNotice`.
  - *Acceptance:* Resolver tests: each code gives 503, and a not-invited code still gives 403. The SPA test covers retry then success, and retry exhausted then message. UAC review is recorded.

- **FR-29 Test warm-up (#2275).** `no-mock-identity.test.tsx` warms its module graph in `beforeAll`, so the first run after an edit doesn't hit the 60 s timeout.
  - *Acceptance:* Two consecutive cold runs after a touch pass.

- **FR-30 Contact store log labels and test double (#2499).**
  - `FindSystemUsersByGuestObjectIdAsync` and `FindHomeContactsAsync` (`ContactIdentityStore.cs:1825/1856`) get fixed `logLabel`s, so a failed read never logs a tenant or oid.
  - The in-memory store stops returning `ExternalTenantId` from `GetContactAsync`, matching the real store.
  - Fix the gate's use of `RawExternalTenantId` (`AssignTaskRecipientGate.cs:496`), with ontology's agreement, since ontology owns the file.
  - *Acceptance:* A log-assertion test proves the failed reads carry no tenant or oid. The tests pass against the corrected double.

- **FR-31 acme readability-proof fault (#2578).**
  - (a) Show the owner the read-only checks before running them: the acme `sprk_dataverseenvironment` row; whether the BFF identity is an application user in spaarke-acme; then the #2105 binding columns and FLS there.
  - (b) Make the owner-approved data fix, with a rollback recorded.
  - (c) Code: a foreign-environment membership 403 logs at Warning, or the environment is skipped with a cooldown, so App Insights stops raising an Error every 5 minutes. The BFF's own environment stays fail-closed.
  - *Acceptance:* After the fix, uac-r2's gate shows 0 acme faults in 30 minutes, or the fault is logged at Warning. Own-environment behaviour is unchanged (test).

- **FR-32 Teams mobile layout (#2466).** In the Teams tab on phones, the shell, tabs and record template adapt to phone width (≤ 480px): one column, a toolbar overflow, and touch targets ≥ 44px.
  - *Acceptance:* Verified in the Teams mobile client, or at the emulated 375px width with the Teams host adapter. No horizontal scroll.

**Closing**

- **FR-33 First-E2E inherited checks.** R4's first dev E2E proves:
  - R3's WorkforceHome invite answers 409 `sdap.access.invite.workforce_home_contact` and writes no collision flag;
  - a held invoice renders Review detail (once 082c-2 is live);
  - `85fbba3f64` (the "not set up" registration text) is deployed with the first SPA deploy;
  - the external routes on the current dev BFF build carry traffic successfully.
  - *Acceptance:* Evidence is recorded in project notes.

- **FR-34 Standards proposals.** Draft the following for owner approval; `.claude/` edits happen in the main session:
  - (a) the record-modal icon map as repo guidance;
  - (b) a `MODAL-DESIGN-SYSTEM.md` note that child dialogs mount inside the parent SprkModal;
  - (c) a `record-modal-selection.md` note on the Xrm-free SPA case (SprkModal `lg`).
  - *Acceptance:* The drafts are in project notes, and the owner's decision is recorded.

### Non-Functional Requirements

- **NFR-01 Themes and accessibility.** Light, dark, high-contrast and Teams themes, using semantic tokens only. WCAG 2.1 AA. Every icon-only control has an aria-label (ADR-012, ADR-021).
- **NFR-02 Focus.** Keyboard focus never escapes the modal or its child dialogs (FR-03).
- **NFR-03 Widths.** Usable at 1440, 1280, 1000 and 375px with no horizontal page scroll.
- **NFR-04 Security.**
  - No UI-offered action answers 403 (FR-06).
  - The allow-list still refuses unlisted columns (FR-18).
  - All external access is enforced server-side via `sprk_externalrecordaccess`, and data access stays app-only (ADR-028 A1).
  - No new external read of `sprk_signal`.
  - Logs and errors carry no tenant, oid, UPN, email or token (FR-30, dev-bff-deploy-coordination).
- **NFR-05 BFF hygiene.**
  - Every BFF-touching task reports its publish-size delta against a fresh master build, and adds no HIGH CVE.
  - Changes to `Api/` or `Services/` ship with tests.
  - No new DI registration or package.
- **NFR-06 Staged delivery.** Each phase P1–P4 ends with a dev deploy and E2E. The SPA deploys through the SPA's own skill. BFF deploys go only through the uac-r2 orchestrator, with the owner's go for the exact SHA.
- **NFR-07 Tests.** KEEP paths and banned mocks per ADR-038. Every must-fire test has its must-not-fire pair where the FR names one (FR-08, FR-11, FR-28).

## Technical Constraints

### Applicable ADRs
- **ADR-001 / ADR-019:** Minimal API; ProblemDetails for every failure, with a stable `errorCode` and correlation id. Upstream failures map to 429/503/500.
- **ADR-007:** SPE access only via `SpeFileStore`; no Graph types in DTOs.
- **ADR-008:** endpoint filters for resource authorization on the new event routes.
- **ADR-010:** feature-module registration; no new interfaces without a seam (none planned).
- **ADR-012:** shared-library rules for `RecordCommandBar` and the `DataGrid` changes.
- **ADR-021:** Fluent v9; `@fluentui/react-icons`; `FluentProvider` and `makeStyles`; no hard-coded colours.
- **ADR-028:** A1 CIAM plane, app-only, server-side access. An indeterminate result is a retryable 503, never a 403. The external SPA uses its own MSAL module.
- **ADR-038:** testing strategy.
- **ADR-050:** SprkModal and its presets; Cancel on the left; no bespoke envelope.
- **ADR-052:** no new background work. The #2578 change modifies an existing job's logging and skip logic only.

### MUST Rules
- ✅ MUST build the modal and every child dialog on SprkModal and its presets (ADR-050).
- ✅ MUST return ProblemDetails with stable `errorCode`/`reasonCode` from every changed or new BFF route (ADR-019).
- ✅ MUST use endpoint filters on the new event routes, copied from the project route (ADR-008).
- ✅ MUST add every new readable column to `ReadableColumns`, with UAC review recorded.
- ✅ MUST gate Review detail rendering on `statuscode` = 1, and treat unknown values as blank.
- ✅ MUST keep `RecordCommandBar` free of Xrm, data fetching and entity literals; export its types; ship it with tests (ADR-012).
- ✅ MUST measure the publish-size delta on BFF tasks (bff-extensions).
- ❌ MUST NOT call Graph or hand out signed URLs or SPE pointers for preview (ADR-007, ADR-028 A1).
- ❌ MUST NOT pass `allowRemoteImages` to `sanitizeEmailHtml`.
- ❌ MUST NOT read `sprk_signal` from any external surface.
- ❌ MUST NOT deploy the dev BFF directly; request it through uac-r2 (dev-bff-deploy-coordination).
- ❌ MUST NOT run live reads or setting changes on Dataverse or Azure without the owner first seeing the commands (#2578).

### Existing Patterns to Follow
- Event route: the `POST /projects/{id}/events` handler, `ExternalProjectDataEndpoints.cs:456`.
- Per-record access in the SPA: `DocumentLibrary.tsx` (`writeAccessPerRecord` prop at `:701`; list `canCreate` at `:748-766`).
- ProblemDetails superset for the lock 409: `DocumentOperationsEndpoints.cs:157-174`.
- Modal presets: `components/SprkModal/presets/*` (FormModal, ConfirmModal, PreviewModal, BrowseModal).
- Toolbar slots and the tooltip/aria rule: `components/HeaderToolbar/README.md`.
- Submit invoice gating: `InvoicesWidget.tsx:66`.

## Placement & New Components (CLAUDE.md §10 / §11)

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>Y</bff>                   <!-- 2 event routes, delete-409 body, allow-list columns, #2269 resolver, #2499 log labels, #2578 log level -->
  <spaarke-ai>N</spaarke-ai>     <!-- external-spa + shared lib only -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
- **BFF placement:** all changes stay in the BFF because they extend existing external-access endpoints, its allow-list, and its existing resolver and reconciliation job. Nothing adds a new host, service, package or background worker (ADR-052).
- **Publish size:** each BFF task reports its delta. The ≤ 60 MB ceiling applies.

### New Components (§11 three-question gate)

| New component | Existing overlap | Can extend instead? | Cost of doing nothing |
|---|---|---|---|
| `POST /matters/{id}/events`, `POST /workassignments/{id}/events` | `POST /projects/{id}/events` (`ExternalProjectDataEndpoints.cs:456`) | Yes: reuse the same handler and service, parameterised by root if possible; otherwise two thin routes | "Add event" fails (no route) on matters and work assignments, though the caller has write |
| `ReadableColumns` entries (FR-18) | The existing allow-list | Yes: extend the lists | The Information section cannot show the fields partners need; the module fetch refuses them |
| `RecordCommandBar` (shared) | `HeaderToolbar` (section header slots), `DocumentToolbar` (document-only actions), `DataGrid/commandBar/CommandBar.tsx` (grid-scoped) | No as a direct extension: `HeaderToolbar` is a section-card header and `DocumentToolbar` is document-specific. **It composes `HeaderToolbar` and Fluent primitives rather than duplicating them.** | Six record types each hand-build toolbar visibility, overflow and icon logic, so hide-don't-disable drifts per type and a View-only caller can be shown an action that 403s |
| `DataGrid` persistence and Money-sort options | `DataGrid` sort and resize (no persistence) | Yes: optional, opt-in props on the existing component | Columns reset on every load (#2489); Money sorts as text (#2446/#2447) |

## ADR Tensions (CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-012 | Add to the shared library when "Used by 2+ modules/surfaces", or when it is a "Core Spaarke UX pattern" | `RecordCommandBar` starts with one consumer (the external SPA) | **C — comply** via "Core Spaarke UX pattern" | A record-level action bar with per-record visibility is a cross-surface pattern, next to `HeaderToolbar` and `DocumentToolbar`, and the owner chose shared (2026-11-08). Conditions: it composes `HeaderToolbar`, ships with tests, and has no Xrm or entity literals; the implementing task names a candidate second consumer; code-review and adr-check approve explicitly. If review rejects it, it falls back to SPA-local and the owner is told. |
| MODAL-DECISION-CRITERIA | Nested modals from different chrome families break z-order, focus and dismissal | Child dialogs mount inside the parent SprkModal | **C — comply** | They are the same chrome family (SprkModal presets inside SprkModal), so the rule's concern doesn't apply. A focus and Esc-order test guards it (FR-03), and FR-34(b) proposes documenting it. |
| record-modal-selection | Layout 1 = `Xrm.Navigation.navigateTo` at 85% | The external SPA has no Xrm | **C — comply** | Layout 2 / SprkModal `lg` is the Xrm-free case. FR-34(c) proposes adding it to the pattern. |

No other tensions. #2269's 503 follows ADR-028 ("indeterminate → retry-able 503, never a 403") and ADR-019. Blob preview through the existing content route follows ADR-028 A1 and ADR-007.

## Success Criteria
1. [ ] All six types open in the template; title, header, sections, tabs and toolbar match their config; R3's modal states and deep links still work. *Verify:* SPA tests + dev E2E per type.
2. [ ] A caller with write can upload, post, add a to-do and add an event on matters, projects and work assignments; a View-only caller sees none of these; no offered action answers 403. *Verify:* integration tests (FR-06, FR-08) + E2E with both access levels.
3. [ ] Every added field is on the allow-list with UAC review recorded; an unlisted column is still refused. *Verify:* regression test + PR citation.
4. [ ] PDF and image documents ≤ 25 MB preview in place; others download. *Verify:* SPA tests + E2E.
5. [ ] An invoice shows its Files; a pending file shows "Checking file…"; no invoice upload or versions remain. *Verify:* E2E after ebilling's routes are live.
6. [ ] Review detail renders only while In Review, at all four render points; the must-not-fire test passes. *Verify:* tests + a held-invoice E2E.
7. [ ] #2155, #2192, #2269, #2271, #2275, #2445, #2446, #2447, #2449, #2452, #2453, #2466, #2489, #2490, #2499, #2508, #2578 and #1828 (DEF-003) are closed by the described behaviour. *Verify:* issue-by-issue checklist in the wrap-up.
8. [ ] Light, dark, high-contrast and Teams themes; focus stays in the modal and its child dialogs; layout works at 1000px and 375px. *Verify:* manual + automated a11y checks.
9. [ ] Each phase deploys to dev and passes E2E. BFF deploys are uac-r2-gated, with 0 new 5xx. *Verify:* deploy-gate notes.

## Dependencies

### Prerequisites
- Branch merged with master `57748ebf6d` (done, `df00583315`).

### External
- **ebilling:**
  - The 082c-2 BFF deploy makes `sprk_reviewdetail` partner-readable. FR-11 is gated on it.
  - The `/invoices/{id}/files` routes ship first, back to back with FR-13.
  - R4 reviews ebilling's master PR (082c-3 `08702087cb`, 114 `c4b8d43e8f`).
- **uac-r2:**
  - Every dev BFF deploy goes through uac-r2.
  - UAC reviews FR-08, FR-18, FR-24, FR-28 and FR-31.
  - uac-r2 now owns #2270 and #2278.
- **Ontology:**
  - Agreement on `AssignTaskRecipientGate.cs` (FR-30).
  - D-199 decides the source of #2445's task type and assignee (FR-23).
- **Dataset-grid framework owners:** agree the `DataGrid` persistence and Money options (FR-19, FR-20).
- **Owner:**
  - Approves the #2578 read-only commands, then the acme fix and its rollback.
  - Approves each dev BFF SHA.
  - Decides on the standards proposals (FR-34).

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Prototype decisions | Title = type + number; hide, don't disable; tabs off by default; "Your access"? | Accept all four (2026-11-08) | FR-02, FR-07 |
| Owner's change list | Any prototype changes? | None (2026-11-08) | Design §3/§6 as written |
| Information fields | Show internal people's names to partners? | Yes, with UAC review per column (2026-11-08) | FR-18 |
| Doc + Service Request | On the template? | All six types (2026-11-08) | FR-01; the R3 modal is retired |
| Record toolbar | SPA-local or shared? | Shared `RecordCommandBar` (2026-11-08) | FR-04; ADR-012 tension, Path C |
| Icon map | Adopt? | Adopt and propose as a standard (2026-11-08) | FR-05, FR-34(a) |
| Submit invoice | In the record toolbar? | Yes, on Matter, Project and WA (2026-11-08) | FR-10 |
| R3 carry-over | Which are R4's? | #2269, #2275, #2499, #2578, plus #2271's delete 409; #2270/#2278 → uac-r2; six closed as fixed; #2163 closed as a duplicate (2026-11-08) | FR-09, FR-28–FR-31 |
| Preview | Types and size cap? | PDF + PNG/JPEG/GIF/WebP, 25 MB (2026-11-09) | FR-14 |
| #2508 no grants | Empty tabs or none? | Hide the tabs (2026-11-09) | FR-24 |
| #2269 503 | SPA reaction? | Auto-retry 3× (1/2/4 s), then a message with Retry (2026-11-09) | FR-28 |
| Delivery | Staged or one deploy? | Staged by phase, each with dev E2E (2026-11-09) | NFR-06; the phase plan |

## Assumptions
- **Information field list:** the exact per-type list is fixed by FR-18's live-metadata check. Candidates are design §5. Missing columns (for example `sprk_opendate` and `sprk_billingperiod`) are dropped; `createdon` stands in for open date only if the owner agrees in that task.
- **Workforce no-grant detection:** "no grants" means an empty accessible set from the existing Tier-2 resolution. No new endpoint.
- **Grid settings storage:** browser `localStorage` only. A storage failure falls back to the defaults silently.
- **Teams mobile breakpoint:** ≤ 480px is treated as phone.
- **Upload and message failures:** keep R3's existing error handling and retry behaviour.

## Unresolved Questions
- [ ] **#2192 popup timeout value.** Blocks FR-25's final value; set in the auth/UAC review.
- [ ] **#2445 data source** (ontology D-199). Blocks FR-23 if the source is a Signal.
- [ ] **`RecordCommandBar` candidate second consumer.** It is named in the FR-04 task, with code-review sign-off (ADR-012 Path C).
- [ ] **ebilling timing** for the files routes and the 082c-2 deploy. Gates FR-11 and FR-13 in P2. If they slip, P2 ships without them and they move to a later deploy.

---
*AI-optimized specification. Original design: `design.md`.*
