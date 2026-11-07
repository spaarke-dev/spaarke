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
| ~~Grant email to a workforce contact~~ (#2458, ISS-024) | **Not R4.** R3 fixed it in task 079 (PR #2500, dev `fcf01480f4`, 2026-11-01); the issue is closed. Its follow-ups #2499 and #2508 are listed in this section. |
| **ebilling 082c: invoice held for missing billing terms** (review 2026-11-04, ebilling brief `projects/ebilling-r1/notes/082c-brief.md` §3(c)) | ebilling codes it (082c-2 BFF, 082c-3 client). What R4 asked for: a held invoice shows one warning bar, "Received: on hold for review", **in place of** the green Accepted bar; the 202 pre-check note is provisional and drops once the final outcome arrives; small wording edits. **Held state on the invoice row:** today a held invoice's row reads "In Review" (field-secured `sprk_firmhold`; internal-only gap finding). ebilling adds the 082c-2 criterion that the partner sees "On hold for review". **Owner decision (relayed by ebilling, 2026-11-04):** keep "In Review" and explain the kind of review; no "On hold" status. ebilling adds a choice column `sprk_reviewdetail` ("Review Detail": blank / Waiting for billing terms / Billing terms could not be checked / Waiting for the firm to be confirmed) and its allow-list entry. The hold sets it; the re-check, Override release or Set firm clears it in the same transaction. **R4 renders it, all of it** (ebilling 2026-11-04: 082c-3 adds neither the grid column nor the `INVOICE_COLUMNS` entry; its client part is only the submission-outcome warning): a "Review detail" grid column after Status in `InvoicesWidget`, with the gate below and a must-not-fire test (a Paid or Rejected row that carries a value renders blank); the same field in the record's Invoices section; on the Invoice record, the status fact's secondary line, an Information field, and one fixed explanation line per option. "Your access" doesn't use it. The explanation lines (ebilling, 2026-11-04). The owner approved all three hold texts on 2026-11-05, including the firm line. **Dev state (2026-11-05):**
- The column exists on dev: `-Verify` exit 0, three pinned options, not field-secured.
- It is **not partner-readable yet**. The `ReadableColumns` entry (`ExternalAccessModule.cs:544` on `task/ebilling-082c-2`; uac-r2 re-confirmed it in its 082c-2 review) goes live only with the 082c-2 BFF deploy. Until then, blank means "normal review".
- **Backfill (on ebilling's 082c rollout checklist):** firm holds that predate the column, i.e. open `sprk_firmhold` with statuscode In Review, are backfilled to `659490002` per environment, with the owner's go and notices to uac-r2. It runs as an `-Apply` path in `Deploy-EbillingInvoiceReviewDetailColumn.ps1`, after the 082c-2 BFF deploy and before partners are told. 8 dev rows are waiting for it. Gap and unresolved holds can't predate 082c.
- R4's first E2E checks a held invoice once both have landed.

**ebilling's partner-visible changes** (merged into `work/ebilling-r1`, not yet on master; R4 reviews the master PR):
- **082c-3 (`08702087cb`):**
  - The submit 202 can carry `warning` (code `ebilling.submission.procedure_gap`, R4's sentence); it is absent when there's nothing to say.
  - The pre-check runs after the save, capped at 2 s, fails quiet, and uses only the authorized record and firm.
  - The client shows the caption under Processing and drops it at a terminal state.
  - A held Accepted shows one warning bar, "Received: on hold for review", with a polite live region.
  - R4's answer on the badge: a held Accepted shows "In Review" in the warning tone; an un-held or overridden Accepted stays green "Accepted".
- **114 (`c4b8d43e8f`):** an Override of a Procedure hold keeps the submission Accepted with `outcomeReasonCode` `ebilling.submission.overridden`, and the warning becomes null. If the SPA branches on reason codes, treat it as a released Accepted.
  - **Client gate (uac-r2 condition, binding, 2026-11-04):** render any Review detail value or explanation line only when `statuscode` = In Review: **value 1**, label "In Review", statecode 0. That is the existing value 1, relabelled from "Active" (D-41); server constant `InvoiceStatusCodes.InReview` in `Services/Finance/InvoiceLifecycleService.cs`. This applies in the grid column, the record's Invoices section, the header secondary line and the Information field. It is defence in depth: the server also blanks the value on every exit from In Review. When reasons overlap, the server picks one value, in this order: firm to be confirmed > terms could not be checked > waiting for billing terms (ontology).
  - The submission status view's 082c warning is also returned only while the linked invoice is In Review (ebilling 2026-11-05).
  - **`sprk_signal` stays internal** (checked for uac-r2's 082c-2 condition, 2026-11-05): no external surface reads `sprk_signal` data columns. For a record rule, `sprk_policycode` and `sprk_dedupekey` hold an internal `BRR-` key. R4 adds no `sprk_signal` read. If #2445's task type or assignee turns out to come from a Signal, it goes through a named external projection with uac-r2 review, never the raw table.
  - **Option values** (local choice `sprk_invoice.sprk_reviewdetail`, partner-readable through `ReadableColumns`; ebilling 2026-11-05, from `task/ebilling-082c-2`): `659490000` Waiting for billing terms · `659490001` Billing terms could not be checked · `659490002` Waiting for the firm to be confirmed. Values are never reused; **treat any other value as blank**. The client never derives a priority.
  - blank, shown **only when the status is In Review**: "The client is reviewing this invoice."
  - Waiting for billing terms: "This invoice has not been rejected. It is on hold for review because no billing terms are set up for some of its work dates on this record. If the record and the work dates are right, you don't need to do anything; the client will follow up."
  - Billing terms could not be checked: "This invoice has not been rejected. It is on hold for review because its billing terms could not be checked. You don't need to do anything; the client will follow up."
  - Waiting for the firm to be confirmed: "This invoice has not been rejected. It is on hold for review until the client confirms which firm sent it. You don't need to do anything; the client will follow up." Filed by ebilling: #2566 (raw reason code on Rejected), #2567 (firm hold shows green Accepted). Wording needs the owner's OK. |
| **Documents with no file** (#1886, R3 ISS-011) | Owner decision 2026-10-14: list them but hide Download and versions. Add `hasFile` to the external document DTO (BFF) and read it in the SPA. Pairs with #2490 and the Document preview. |

### 7.1 Carried from the R3 hand-off (R3 `README.md` "Hand-off: open issues", checked 2026-11-04)

R3 sends these open issues to "R4 backlog". None was in this design before. Each still needs an owner call: in R4 scope or deferred again. The ones marked † are outside the record-modal theme.

| Issue | R3 id | What | Note |
|---|---|---|---|
| #1650 | ISS-004 | Internal document delete orphans the SPE file when the file delete fails | Documents |
| #1839 | ISS-007 | Service-request documents are indexed with no parent, so they can't be found from the record | Documents and search; relevant to the Service Request template |
| #1636 | ISS-003 | The shared `sanitizeEmailHtml` keeps style positioning and remote images | Shared UI; affects Messages (#2453 threads) |
| #1623 | ISS-001 | `AuthorizationModule`: the Copilot audience merge runs once per process, not per options instance | † BFF auth, so it needs auth review (§6) |
| #1673 | ISS-005 | Binder-created contacts have no BU-team owner | † With provisioning (BU configuration). Pairs with #2278 and #2270 below. |
| #1737 | DEF-002 | Incident alerting for Policy Questions | † Blocked on the per-customer mailbox (#1562) |
| #1840 | ISS-008 | Unused `sprk_communication_send` web resource | † Cleanup |
| #1930 | ISS-012 | WorkforceHome scheme tests are order-dependent | † Test hygiene |
| #2499 | ISS-028 | The in-memory contact store diverges from `ContactSelect` | † Test debt; the gate file is ontology's |
| #2534 | ISS-031 | Auth architecture doc predates ADR-028 A9 | † R4 only if spaarke-auth-system-of-record-r1 doesn't resume |

**Filed in R3 but never scheduled.** These were raised during the ISS-004/005 work on 2026-10-22. R3 confirmed on 2026-11-04 that they are R4's: they are now in R3's hand-off table (`91fb4843ca`, PR #2554), and each issue has a hand-off comment. All five are still unfixed on master.

| Issue | What |
|---|---|
| #2269 | Retryable workforce binder faults (`contact_lookup_failed` / `_create_failed` / `_bind_failed`) answer 403, not 503; the SPA should retry on 503. UAC to review. |
| #2270 | Contacts that `ContactIdentityBinderFactory` creates in a foreign Dataverse environment have no owner. **Owner decision:** where does a foreign environment's owner team come from? |
| #2271 | The document-lock 409 has no ProblemDetails title or status, so clients show generic text |
| #2275 | `no-mock-identity.test.tsx` fails on its first run after an edit (cold Vite transform hits the 60 s timeout) |
| #2578 | **Dev fault, every 5 min since at least 2026-11-03:** the secured-column readability proof fails (WhoAmI 403 `0x80072560` "not a member of the organization", `ContactIdentityStore.ReadProofAsync` via `WebApiColumnReadPermissionSource`), and the proof fails closed. Filed by ontology to R3; routed to R4 on 2026-11-05. Unverified lead from uac-r2's d763 gate: it comes from the link reconciliation's pass over the registered `acme` environment (its registry row, or a missing application user there), not the BFF's own environment, and it predates R3. Ties in with #2270. **Evidence (uac-r2's gate on `20669df6a9`, 2026-11-06):** 6 faults in 30 min, the same rate as before, so the scan reorder made no difference. Each fault pairs one-to-one with an `[ID-LINK-RECON] https://spaarke-acme.crm.dynamics.com: binding column Failed … environment aborted before any decision` line. So it is the acme pass, and **the link reconciliation for acme aborts on every pass.** Likely fix: an application user for the BFF identity in spaarke-acme, or a correct or removed acme registry row. Confirm with read-only checks first. Live reads need the owner to see the commands first; a setting change needs owner approval and a rollback. |
| #2278 | Backfill the owner of contacts that already exist (`scripts/Backfill-RecordOwnership.ps1` doesn't cover `contact`). Pairs with #1673. |

**Inherited state, not issues** (confirmed with R3, 2026-11-04):
- `85fbba3f64` (registration shows "not set up" for operator-only 500s) is on master but **not deployed** to the dev SPA. It ships with R4's first SPA deploy.
- **Check in R4's first E2E:** R3's last change has no live proof on dev, because there was no invite traffic in the gate window. The change: inviting a WorkforceHome contact answers 409 `sdap.access.invite.workforce_home_contact` and writes no collision flag.
- PR #2550 (doc-comment fix in `ExternalUserContextEndpoint` plus a route-ledger cite) is **on dev**: uac-r2 deployed master `909754869f` to the dev BFF at 2026-11-05T04:16:30Z. The 30-minute gate passed, with 0 5xx. The external routes saw 0 requests in the window, so this build has no live proof on the external surface yet; R4's first E2E covers it. Since then, dev BFF is master `cc11294300` (2026-11-07T14:22:09Z: #2292, #2610, #2617; no external route change; gate passed with 0 5xx). Before that it was `20669df6a9`, host started 2026-11-06T16:44:51Z. It carries fm 049's assigned-access create grant and ordered reconciliation scans; G-8 revoke-on-change stays on; rollback target `0f53e053bf`. R4 asked uac-r2 to count the #2578 faults in its gate, split by environment.
- The dev BFF rollback snapshot for `d763dc120e` is in the R3 worktree (`deploy/rollback/wwwroot-20261103T220358Z`). R3's condition for releasing it, a passed uac-r2 deploy gate, was met on 2026-11-05. Removing the R3 worktree is R3's or the owner's call, and only with `scripts/worktree/Remove-WorktreeSafe.ps1`.
- Not R4: the ebilling invoice-visibility review. R3 checked it: `ExternalInvoiceVisibility` is unchanged and there is no access change. uac-r2 owns that watch. R3 closed #1566 and #1568 as completed.
- Known limits K2 and K3 from R3 task 090 (`notes/task-090-notes.md`): two unread counts for a user who signs in both ways, and a licensed user linked to a WorkforceHome contact's home pair loses that contact's grants. Revisit with #2508.
- Since 2026-11-03, uac-r2 coordinates dev BFF deploys (`docs/procedures/dev-bff-deploy-coordination.md`). R4's two event routes and allow-list columns go through it.

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
