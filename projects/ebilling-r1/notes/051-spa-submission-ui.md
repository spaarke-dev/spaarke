# Task 051 - SPA submission UI (shared wizard component) (FR-18, FR-19, FR-45)

Status: built, including the "Choose firm" step (section 7) and the review round (section 8: sections 4 and 6 describe the first build; section 8 supersedes them where they differ); unit tests green; browser ui-tests deferred to task 053, the WP2 dev deploy (section 6). Rigor STANDARD for the build, FULL for the review round.
Built on 044 (routes and codes) and D-91 (firm invoice number required at upload).

**Decision (owner, 2026-10-17): add a firm step.** A caller who belongs to several firms chooses which one the invoice is from, in a required step.

## 1. What was built

| File | Role |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/InvoiceSubmission/InvoiceSubmissionWizard.tsx` | The shared wizard: Record, File, Details over `WizardShell` + `FileUploadZone`; Finish submits; result screen mounts the Outcome panel |
| `.../InvoiceSubmission/InvoiceSubmissionOutcome.tsx` | Processing / Result screen: polls the host's `getStatus`; Accepted (tracking number), Rejected (reason code + host report slot, placeholder until 134), Failed (retry guidance). Also used alone for resume |
| `.../InvoiceSubmission/types.ts`, `messages.ts`, `index.ts` | Callback contract, `InvoiceSubmissionError`, `isEbillingDisabledError`, message tables by error code |
| `.../components/index.ts` | One barrel line (shared file) |
| `src/client/external-spa/src/pages/InvoiceSubmitPage.tsx` | Route page: availability probe, wizard, or resume view |
| `src/client/external-spa/src/App.tsx` | One `Route` (shared with SPA-r3) |
| `src/client/external-spa/src/widgets/InvoicesWidget.tsx` | "Submit invoice" action above the grid (shared with SPA-r3) |
| `src/client/external-spa/src/api/web-api-client.ts` | `submitInvoice`, `getInvoiceSubmission`, `probeInvoiceSubmission`, `listInvoiceCoreRecords`, `toInvoiceSubmissionError` (appended section; shared with SPA-r3) |

### Hand-off to task 052 (internal entry point)

Mount `<InvoiceSubmissionWizard>` with three callbacks and nothing else is required (a fourth, `loadFirms`, is optional):

```ts
loadRecords: () => Promise<{ records: InvoiceCoreRecordOption[]; truncated?: boolean }>
submit:      (input: { record, file, firmInvoiceNumber, poNumber?, firmId? }) => Promise<{ submissionId, trackingNumber, ... }>
getStatus:   (submissionId) => Promise<{ submissionId, status, trackingNumber, outcomeReasonCode, ... }>
loadFirms?:  () => Promise<{ firmId: string; name: string }[]>   // omit it and there is no firm step
```

`firmId` arrives on `submit`'s input only when the user chose one (more than one firm); the host sends it as the `firmId` query parameter of the route.

Reject with `InvoiceSubmissionError({ statusCode, errorCode, detail })` for a server refusal (the SPA adapts `ApiError` in
`toInvoiceSubmissionError`). Optional props: `embedded`, `title`, `onSubmitted`, `renderReport` (the Rejected report slot for 134), `maxFileSizeBytes`
(default 25 MB, the server default; a convenience only), `pollIntervalMs`, `doneLabel`. The internal route's `firmId` query parameter is the host's
concern: `submit` is where it is added.

## 2. How the flow meets the requirements

- **D-91**: the Details step cannot finish while the firm invoice number is blank (whitespace counts as blank); the PO number is optional.
- **FR-18 resume**: after the 202 the page rewrites its address to `/invoices/submit?submissionId={id}` with `history.replaceState` (no router navigation, so the
  wizard stays on its result screen). Reloading or returning to that address renders the Outcome panel alone from `GET .../invoice-submissions/{id}`. Nothing is
  stored in the browser.
- **FR-45 / the 503 probe**: `probeInvoiceSubmission` calls `GET /api/v1/external/invoice-submissions` (the caller's own list). Only a 503 whose
  `errorCode` is `ebilling.disabled` means "off". The escalation question (can the probe tell "disabled" from an outage?) is answered by the code: 044's other 503s
  carry `ebilling.submission.record_unavailable`, `.firm_unverifiable` or `ebilling.tracking.unavailable`, and any non-`ebilling.disabled` failure leaves the entry
  visible (tests cover an outage 503 and a 403). The page runs the same probe first and replaces itself with "Invoice submission is not available" when off.
- **ADR-002 / FR-17**: the client creates no `sprk_invoice` / `sprk_invoicesubmission` and decides no authorization. A refusal shows the server's ProblemDetails `detail`;
  with no detail, a fixed sentence by `errorCode`, then by HTTP status. The wizard stays on Details and the user can retry.
- **Standing constraint (uac-r2 #2047)**: no upload or storage route taking `sprk_billingevent` as parent was added. The vitest suite asserts that every request the
  flow makes is the probe, a record read, or an `invoice-submissions` route on matters / projects / work assignments.
- **ADR-012**: the shared component imports no SPA auth, Xrm or BFF URL (a jest test scans its imports).
- **ADR-021**: Fluent v9 tokens only; a vitest test scans the new UI files for hex / rgb / hsl literals. The dark theme comes from the existing FluentProvider.
- **NFR-12**: no `package.json` change in either package.

## 3. Deviations from the POML and from what the brief assumed

1. **`firmId` is a query parameter, not a form field.** The POML says multipart `firmId`; 044 (the code) reads it from the query. The SPA sends it only when the user
   chose a firm (several firms, section 7); a single-firm caller sends nothing and the server takes the only firm. 044's contract is unchanged.
2. **Record picker data.** There is no external list route for matters or work assignments. Matters and work assignments are read through the Tier-2 module read seam
   (`gridDataverseClient.retrieveMultipleRecords`, FetchXML limited to the columns `ExternalAccessModule` admits: id, name, number); projects use `GET /projects`.
   If one source fails the others are still offered and the picker says the list is partial.
3. **Client-side PDF check needed a validator.** `FileUploadZone` accepts a file whose extension OR MIME type is in its accepted set, and the Word / Excel MIME types are
   always in it, so `acceptedExtensions: ['.pdf']` alone let a `.docx` through (a jest test failed on it). The wizard passes a `customValidator`. The shared zone itself
   was not changed.
4. **Phone width.** `WizardShell` draws a fixed 200 px step list beside the content, unusable at 375 px, and the modal preset is 62vw wide. The SPA page mounts the wizard
   `embedded`, and the wizard hides the step list under 600 px with a media-query rule on its own wrapper (each step shows "Step n of N"). `WizardShell` /
   `WizardStepper` were NOT changed (other wizards use them); a shared responsive mode would be the better long-term fix and is for the owner of that component.
5. **Result screen title is fixed.** `WizardShell`'s success config has a static icon and title, so the result screen titles itself "Invoice received" (true for every
   outcome: a tracking number was issued) and the status badge and messages below it carry Processing / Accepted / Rejected / Failed.
6. **`AiProgressStepper`** (named in the POML's composition list) was not used: it is an AI-run progress view; a spinner with "Processing" and the status badge fit this
   screen. `StatusBadge`, `WizardShell`, `FileUploadZone`, `UploadedFileList` were reused.
7. **Dev mock** (`mock-service.ts`) was not extended for the new routes (not in the POML's file list).
8. **TASK-INDEX.md** was not updated (brief: the main session owns it).

## 4. For SPA-r3 (shared files touched)

- `src/client/external-spa/src/widgets/InvoicesWidget.tsx`: the grid binding is unchanged (`createGridWidgetBody(CONFIG_ID)`), now wrapped in a column with a right-aligned
  "Submit invoice" button above it, rendered only inside a router and only after the probe has not answered `ebilling.disabled`. The export is still
  `InvoicesWidgetBody` (named and default), so `widgetRegistry.ts` and `DetailDocumentsDelete.test.tsx` are untouched.
- `src/client/external-spa/src/App.tsx`: one import and one `<Route path="/invoices/submit">` beside `/upload`.
- `src/client/external-spa/src/api/web-api-client.ts`: header comment lines, three new imports (`ApiError`, `gridDataverseClient`, the shared InvoiceSubmission module) and
  an appended "Invoice submission" section before the `webApiClient` object. No existing export changed.
- No open PR touched these files when checked (2026-10-17); the SPA-r3 worktree had no uncommitted change under `src/client/external-spa`.

## 5. Open items / known limits

- **Firm list unknown.** If `/me` carries no firm list (an older BFF, eBilling off, a failed membership or name read) or the read fails, the wizard shows no firm step; a
  several-firm caller then still gets 044's 400 `firm_required` message on submit. Unknown is never treated as "no firm" on the server (`Firms` is `null`, not `[]`).
- **View-only records.** The picker lists every readable project / matter / work assignment; a record where the caller lacks Create answers 403 `record_forbidden` at
  submit (shown by code). The server stays the authority (ADR-002).
- **Reason codes** are shown verbatim ("Reason: duplicate_invoice") until 049 / 134 define user-facing text and the report slot.
- **Lost 202.** 044 does not recognise a repeated upload (INV.2): after a network failure with no response the wizard says the submission could not be confirmed and to check
  the invoices list before sending again.
- The record read asks for 200 rows per matter / work-assignment source and flags a longer list as partial.

## 6. Evidence

Original build (before the firm step): shared jest 29 passed; SPA `InvoiceSubmitPage.test.tsx` 18 passed, whole SPA suite 37 files / 1037 passed. After the firm step: section 7.

- BFF arch guards that read the SPA: `ExternalSpaGridViewSelectorGuardTests` 231 passed, `ClientUploadRouteAgreementTests` 6 passed. The guard first FAILED on the widget
  (`useInRouterContext` imported outside App.tsx: any router name is refused); the widget now reads `UNSAFE_NavigationContext` (null outside a router) instead, so the
  unchanged `DetailDocumentsDelete.test.tsx`, which renders the grid body with no router, still passes.
- Seeding proof: with the probe made to answer "disabled" for every failure, the outage-503 page test and the outage-503 / 403 widget test fail (2 failed / 16 skipped
  run); restored, all 18 pass. The `.docx` client-side check failed before the custom validator was added.
- Typecheck (`tsc --noEmit`) and lint (`eslint src tests`) clean in external-spa; `eslint` clean and `tsc` free of new errors in the shared library (its existing errors are
  the unbuilt `@spaarke/auth` / `@spaarke/sdap-client` sibling packages, also behind 6 jest suites that fail to load there on this machine); `vite build` succeeds.
- **ui-tests deferred to task 053 (the WP2 dev deploy).** The browser ui-tests (happy path against the real BFF, phone width at 375 px, dark mode walk-through, now
  including the Choose firm step for a several-firm contact) need the SPA and the BFF deployed, which is what 053 does; they were not run here. What stands in for them
  until then: tokens-only file scans, the emitted `@media (max-width: 600px)` rule asserted in jest, and wire-level vitest tests.

## 7. Follow-up: the "Choose firm" step (owner, 2026-10-17: add a firm step)

**What changed**

- **BFF, `GET /api/v1/external/me`** gains `firms: [{ firmId, name }]` (additive; `contactId`, `email`, `projects`, `systemUserId` unchanged in name, type and value).
  The set is the caller contact's CONFERRING organization memberships, read through `ExternalInvoiceSubmissionAuthorizationFilter.ReadCallerFirmsAsync`: the one definition
  044's firm resolution now also calls (a pure extraction, same behaviour), so the SPA is offered exactly the set `firmId` is validated against. Names come from
  `sprk_organization.sprk_name`; an inactive or missing firm is left out (a submission for it is refused anyway). `firms` is `[]` for a caller with no firm, and `null`
  (unknown, never "no firm") when eBilling is off (nothing is read), when the membership read or a name read faults, or when the contact is unreadable. `/me` never fails
  for it. Only the caller's own ids are ever looked up, so no other firm's id or name can leave.
- **Why `/me` and no new route** (CLAUDE.md §11): (1) Existing: `/me` already returns the caller's own access context and is caller-scoped. (2) Extension: it can carry one
  more caller-scoped list; a new route would add a ledger entry and a second round trip for no reason. (3) Cost of nothing: a several-firm caller gets 400 `firm_required` and
  cannot submit. Same route, same ledger entry (`GET /api/v1/external/me`, `ExpectedEndpointFileCount` stays 136). The route is mapped to a new
  `ExternalUserContextEndpoint.HandleAsync`, which calls the unchanged `Handle` and adds `Firms`; the ledger's cited lines (:45, :74) did not move.
- **Submission**: 044 reads `firmId` from the QUERY (`?firmId=`), accepts one of the caller's firms (403 `firm_forbidden` otherwise, 400 `firm_invalid` if malformed), and with
  none named uses a single firm or answers 400 `firm_required` for several. The SPA sends `?firmId={chosen}` only when the user chose one; a single-firm caller sends nothing
  (as before). 044's contract is unchanged.
- **Wizard**: new optional `loadFirms` callback and `firmId` on the submit input. The "Choose firm" step is the first step and exists only when `loadFirms` resolves with more
  than one firm; a firm must be chosen to advance (and the wizard refuses to submit without one). With no callback, no firm, one firm, or a failed load, there is no step and no
  `firmId`. The shell is not mounted until the firm list is known (the shell fixes its steps at mount). Step captions are now "Step n of N" (3 or 4); Details shows the chosen
  firm.
- **SPA**: `ExternalUserContextResponse.firms` type, `listInvoiceFirms()` (reads `/me`), `submitInvoice` appends `?firmId=` (GUID-checked), the page passes `loadFirms`.

**Files touched in this follow-up, by owner**

| Owner | Files |
|---|---|
| uac-r2 (task 136) / external-access-r3 (task 054) / teams-app-r1 (task 025): `/me` | `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalUserContextEndpoint.cs` (new `HandleAsync` appended after `Handle`), `.../Dtos/ExternalUserContextResponse.cs` (optional trailing `Firms`, new `ExternalFirmEntry`), `.../ExternalAccessEndpoints.cs` (one token: the `/me` route maps `HandleAsync`) |
| ebilling-r1 (044) | `.../ExternalInvoiceSubmissionAuthorizationFilter.cs` (extracted `ReadCallerFirmsAsync`; behaviour unchanged), `.../Services/Finance/Submission/InvoiceSubmissionService.cs` (`ReadActiveFirmNamesAsync`), `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalUserContextFirmsTests.cs` (new) |
| ebilling-r1 (051) | shared `.../InvoiceSubmission/` (`InvoiceSubmissionWizard.tsx`, `types.ts`, `index.ts`, wizard jest), `src/client/external-spa/src/pages/InvoiceSubmitPage.tsx`, `src/client/external-spa/tests/InvoiceSubmitPage.test.tsx` |
| SPA-r3 (shared) | `src/client/external-spa/src/api/web-api-client.ts` (`listInvoiceFirms`, `?firmId=` in `submitInvoice`, import of `getExternalUserContext`), `src/client/external-spa/src/auth/bff-client.ts` (optional `firms` on `ExternalUserContextResponse`) |

**Evidence**

- BFF (`-m:1`, affected classes only): `ExternalUserContextFirmsTests` 16 + `CallerPrincipalTests` (the existing `/me` tests through the unchanged `Handle`) = 39 passed. Before the
  change the new tests did not compile (no `HandleAsync`). ArchTests: `RouteAuthorizationGuard*` and `ADR010` 92 passed; `ADR001`, `ADR008` and Finance guards 15 passed. The whole unit project was not run (brief).
  The tests cover: several, one and zero firms; another firm's id and name never read or serialized; inactive firm omitted; eBilling off (null, no read); unreadable and thrown
  membership reads and a faulted name read (null); unreadable contact; a workforce caller with no contact; existing fields' values and camelCase wire names.
- Shared jest `InvoiceSubmission`: 38 passed (9 new). Against the previous wizard, 5 of the new "Choose firm" tests fail and the 4 "no step" cases pass, as they should.
- SPA vitest: `InvoiceSubmitPage.test.tsx` 25 passed (7 new: several firms send `?firmId=`, Next blocked until a pick, one / none / null / failed `/me` send none, a refused firm is
  shown); whole suite 37 files, 1043 of 1044 passed on the full run; the one failure (`DocumentUploadPage.externalUpload` 401 case, unrelated to this change) passed on its own (25/25), a
  load-sensitive timing flake. `tsc --noEmit` and `eslint src tests` clean in external-spa; `eslint` clean and no `tsc` error in `InvoiceSubmission` in the shared library.

## 8. Review round (uac-r2 F1/F3/F4, owner D-106, K1-K3, R3 comments, DEF-004). Rigor FULL (auth filters)

Base 7c0daf8c9. Commits: server `54ebabd9d`, shared wizard `2e9886f4a`, SPA and notes follow (see the report). No live writes, nothing under `.claude/`.

### 8.1 What changed

| Item | Change | Where |
|---|---|---|
| uac F1 | `ReadActiveFirmNamesAsync` selected `sprk_name`, which does not exist on `sprk_organization` (the 400 was swallowed into a null firm list, so /me never carried a firm). Now one batched `QueryExpression`: `ColumnSet("statecode", RegardingNameFields.PrimaryNameField("sprk_organization"))` = `sprk_organizationname`, `In` on `sprk_organizationid` (chunked), `statecode = 0`. Null on a fault; a missing or inactive firm is left out; the caller's order is kept | `InvoiceSubmissionService.cs` |
| uac F3 | `ExternalUserContextFirmsTests` rewritten (24 tests): the organization double validates the column set and refuses an unknown column; exact `["statecode","sprk_organizationname"]` assertion; single-query assertion; must-not-fire (a second contact's other firm never appears in the caller's `firms`) and must-fire (the caller's own firm does) | `ExternalUserContextFirmsTests.cs` |
| uac F4 | A systemuser is refused on the three external invoice POSTs in `ExternalInvoiceSubmissionAuthorizationFilter.DecideSubmitAsync`, first, before any read or write: 403 `ebilling.submission.use_internal_route`, Warning log. /me returns `firms = []` and reads nothing for a systemuser. `ReadCallerFirmsAsync` reads the contact state first: an Inactive contact confers no firms (UAC task 137), an unreadable contact is unknown (null). One test per route proves a systemuser is refused before any write; a deactivated-contact test | filter, `ExternalUserContextEndpoint.cs`, `InvoiceSubmissionEndpointsContractTests.cs` |
| D-106 | The chosen firm must be linked to the chosen record. See 8.2 | filter, endpoints, wizard, SPA client |
| K1 | `listInvoiceFirms()` returns `null` for unknown (was `[]`). On a 400 `firm_required` the wizard reloads the firms, inserts the Choose firm step with the reason, and keeps the file, the number and the record | wizard, `web-api-client.ts` |
| K2 | /me: the flag check is first; `InvoiceSubmissionService` and `ExternalParticipationService` are resolved from `RequestServices` only after it (an eBilling-off or systemuser caller resolves and reads nothing); the firm names are one query, not one per firm; the "no extra cost" comment is replaced by the real cost (one membership read, one name read, both only when eBilling is on) | `ExternalUserContextEndpoint.cs` |
| K3 | `[property: JsonIgnore(Condition = WhenWritingNull)]` on `Firms`: an unknown list is omitted from the wire, `[]` is sent as `[]` | `ExternalUserContextResponse.cs` |
| Trivial | /me has its own log message ("[EBILLING-FIRMS] ... the caller's firms are unknown", Warning) instead of the shared "[CONTACT-GRANT] ... refusing (fail closed)" at Error. `ContactGrantorAuthorizationFilter` is not modified | filter |
| R3 (a) | `UNSAFE_NavigationContext` removed from `InvoicesWidget`. `useInRouterContext` cannot be used: the arch guard refuses any imported router name outside `App.tsx` (the finding in section 6). The widget takes an optional `onNavigate` prop from `WidgetBodyProps`; `App.tsx` passes `navigate` to `WorkspaceHomePage`, which hands it to the widget body. Without it (a test, a surface with no router) the action is not shown | `InvoicesWidget.tsx`, `PlaceholderWidgetBody.tsx`, `WorkspaceHomePage.tsx`, `App.tsx` |
| R3 (b) | No list GET on mount. The widget reads /me once (`useCallerContext`) and shows "Submit invoice" only when `firms` is non-empty. `probeInvoiceSubmission` (used by the page only) reads /me first (`firms` present = on); only when `firms` is absent does it fall back to the submissions list to tell "off" (503 `ebilling.disabled`) from "unknown" | `web-api-client.ts`, `InvoicesWidget.tsx` |
| R3 (c) | The picker says when a source failed to load (`records-partial`: the list is incomplete) apart from a capped list (`records-truncated`: the search box searches only the shown records). An empty list from a failed read is not shown as "no records". A failed firm-records read throws (the failure is shown, never an unfiltered list) | wizard, `listInvoiceCoreRecords` |
| R3 (d) | The /me ledger HandlerDecision now names `ExternalUserContextEndpoint.HandleAsync` | `RouteAuthorizationGuardTests.Ledger.cs` |

Behaviour change to note for SPA-r3: an outage of /me now hides the widget's Submit action (before, the list probe kept it through an outage). The page itself still works from a direct link.

### 8.2 D-106: the firm is linked to the record

- **Link used (reuse, nothing new in the model):** `sprk_assignedlawfirm1` / `sprk_assignedlawfirm2` on the matter, project or work assignment, read through the existing `ExternalParticipationService.GetReferencedOrganizationIdsAsync(entity, [recordId])`. A firm that is only granted access (an org-level grant row) is NOT counted as linked: that fails closed. Owner confirmed by D-110: "linked" means the assigned law firms only.
- **Server:** `DecideSubmitAsync` ends with `DenyUnlinkedFirmAsync`: 403 `ebilling.submission.firm_not_linked` with a fixed detail (no firm or record names), 503 `firm_unverifiable` when the link cannot be read. It runs last, so a caller who failed any earlier check learns nothing about links. It applies to every external submit, single-firm callers included. (Round 1 only: the internal route was not changed then. The check now lives in the service and the internal route enforces it, see section 9.)
- **Picker:** new `GET /api/v1/external/invoice-submissions/firm-records?firmId=` in the existing `ExternalInvoiceSubmissionEndpoints.cs`. Justification (CLAUDE.md section 11): (1) Existing: the same participation read the submit gate uses; (2) Extension: the Tier-2 module read is allow-listed to id, name and number, so the link cannot be asked for there and the filter has to be server-side; (3) Cost of nothing: the picker offers records the server refuses with `firm_not_linked`. It returns ids only, drawn from the caller's OWN readable matters, projects and work assignments, and `partial` when some record's links could not be read. The firm must be one of the caller's own (403 `firm_forbidden`), a systemuser is refused. The SPA filters its three existing record reads to those ids.
- **Wizard:** records load after the firm is known (the one firm of a single-firm caller, or the one chosen in the Choose firm step), and again when the choice changes; a record picked under another firm is dropped. With firms unknown (null, no firm, a failed load, no `loadFirms`) the list is not narrowed and the server checks the link on submit.
- **Resolved in round 2 (section 9):** the owner decided D-110 (every channel, assigned firms only) and the check moved into `InvoiceSubmissionService.SubmitAsync`; the internal route enforces it with the same codes.
- **Known limit:** the record reads cap at 200 rows per source before the filter, so a linked record beyond the cap is not offered; the `records-truncated` notice says so.
- **Ledger / ADR-010:** the route sits in an existing governed file, so `ExpectedEndpointFileCount` stays 137 (the entry text was updated); no DI registration was added, so the ADR-010 ceiling (166) is unchanged.

### 8.3 ExternalWrites guard exemption rows to add (#2078's guard is not on master, so none were added here)

Route + reason, for each of the three:

| Route | Reason |
|---|---|
| `POST /api/v1/external/matters/{id}/invoice-submissions` | a systemuser cannot reach it (refused in ExternalInvoiceSubmissionAuthorizationFilter before any write) |
| `POST /api/v1/external/projects/{id}/invoice-submissions` | a systemuser cannot reach it (refused in ExternalInvoiceSubmissionAuthorizationFilter before any write) |
| `POST /api/v1/external/workassignments/{id}/invoice-submissions` | a systemuser cannot reach it (refused in ExternalInvoiceSubmissionAuthorizationFilter before any write) |

`GET .../invoice-submissions/firm-records` is a read (no row), and /me writes nothing.

### 8.4 DEF-004 answers (R3), from the code

- **(a) Creator.** The document row the SPA submit creates is stamped with the caller's contact: `ExternalDataService.CreateDocumentAsync` calls `StampCreator(body, creator)`, which sets `sprk_createdbycontact` for a contact principal (R3 task 030 / FR-25) and `sprk_createdbyperson` for a systemuser principal. A systemuser can no longer reach this path (refused in the filter), so the contact stamp is the only one an external submit writes. The `sprk_invoicesubmission` row records the contact in `sprk_submitter` (`InvoiceSubmissionService.cs` ~401); `sprk_createdbycontact` is not a column of that table (the data-model doc lists it on document, to do, event and communication), and `owner.ApplyTo(row)` stamps only `sprk_createdbyperson`, which a contact does not have. So the submitter is named on the submission and the creator contact on the document. **Not a gap**; note for the owner: nothing deletes a submission, so the delete-own rule does not read either column for it.
- **(b) Owner.** Set by an explicit owner-team rule, not left as the app user: `IRecordOwnershipResolver.ResolveOwnerAsync(ForParents(core record))` runs before any write; a refusal is 409 (`OwnerRefused`) and nothing is written; `owner.ApplyTo(row)` sets `ownerid` = the resolved team on the submission row, and `CreateDocumentAsync` requires the same team (`RequireOwner`, the document create refuses without one). **No gap.**
- **(c) Rights.** The caller's rights are the evaluator's post-veto answer on the principal (`principal.RightsOn(core root, id)`), the same expression the external document upload uses, and a submit needs Read and Create on the core record the route names. A submission has exactly one parent (the core record), so there is no further parent to union: whatever the evaluator composes for that record (inherited, granted, vetoed) is the answer. A caller with Read and Create on the record but no active firm membership is refused 403 `firm_forbidden` before any write (`External_caller_with_no_firm_is_403_firm_forbidden`), and one whose firm is not linked to the record is refused 403 `firm_not_linked`.

### 8.4b Evidence (review round)

- **BFF** (`-m:1`, filtered `External|InvoiceSubmission|Firms|RouteAuthorization`): 3803 passed, 0 failed. **ArchTests**: 1160, 1159 passed on the first run; the one failure was
  `ExternalSpaGridViewSelectorGuardTests` reading the phrase `from "unknown"` inside two of my new doc comments as an import (fixed by rewording, commit e0a1c84c3); the guard
  classes (`ExternalSpaGridViewSelectorGuardTests`, `ClientUploadRouteAgreementTests`) re-ran 237 passed. Route ledger count stays 137; ADR-010 unchanged.
- **Fail before / pass after (server):** with the old `sprk_name` column, 11 of the 24 `ExternalUserContextFirmsTests` fail; with the systemuser refusal, the link check and the
  inactive-contact rule neutralised, 14 contract and firms tests fail; restored, all pass.
- **Shared jest** `InvoiceSubmission`: 51 passed (2 suites). Against the previous wizard, 12 of the 46 wizard tests fail (the 34 others pass).
- **SPA vitest**: 37 files / 1054 passed; `InvoiceSubmitPage.test.tsx` 35 tests (was 25). Against the previous SPA sources (`web-api-client`, widget, page, `WorkspaceHomePage`,
  `App`, `WidgetBodyProps`), 12 of the 35 fail and 23 pass. `tsc --noEmit` and `eslint` clean in external-spa; `eslint` clean on the shared `InvoiceSubmission` folder.
- **Publish size** (framework-dependent linux-x64 `dotnet publish -c Release`, `Compress-Archive -CompressionLevel Optimal`): base `7c0daf8c9` 192 files 37.727 MB; tip 192 files
  37.733 MB; delta +0.006 MB (no `.csproj` change: no new package, no new CVE; `dotnet list package --vulnerable` not re-run for that reason). Measured against the branch base, not a fresh
  master build.

### 8.5 Same-class defects found elsewhere (not fixed: other owners' code; reported)

A Grep for a wrong-name-column read on `sprk_organization` (the class of F1) found more. `sprk_organization`'s primary name is `sprk_organizationname`; `sprk_name` does not exist there (`RegardingNameFields`, `RegardingNameFieldsTests`):

- `src/server/api/Sprk.Bff.Api/Services/ExternalAccess/AssignedAccessStore.cs:879` and `:881` (`ReadSubjectNamesAsync`, uac-r2): selects `sprk_organizationid,sprk_name` from `sprk_organizations`. The 400 is swallowed ("Display only: a failure is empty"), so Manage Access suggestions show organizations with no name.
- `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/projectService.ts:209-211` and `CreateProjectWizard.tsx:146-149`: the organization search selects, filters and orders on `sprk_name`.
- `src/client/pcf/CommunicationConnections/CommunicationConnections/CommunicationConnectionsApp.tsx:240`: `sprk_organization: 'sprk_name'` in the primary-name fallback map (degrades to the GUID).

## 9. Fix round 2 (owner D-110, coordinator additions). Rigor FULL (auth)

Base `be1c60e38`. Commits: server `254d06045`, shared reader `a93d97c49`, SPA `b7519d388`; this note follows. No live writes, nothing under `.claude/`, `TASK-INDEX.md` and `current-task.md` untouched.

### 9.1 D-110: the link holds on every channel

- **Owner:** `InvoiceSubmissionService.SubmitAsync`, the one decision point both routes call (ADR-002 WP-1). After the firm-is-active check and before anything is read for the write: 403 `ebilling.submission.firm_not_linked`, 503 `ebilling.submission.firm_unverifiable` when the record's assigned firms cannot be read, the same codes and the same detail text whichever route called; the detail names no firm and no record. Registered as invariant **I-20** in `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` section 5 (the registry had no row for D-106 in round 1; added here. The number I-20 may collide with another branch's row: renumber on merge).
- **One definition of "linked":** `CheckFirmLinkAsync` (the live read) and the pure `LinkOf` (a missing, unreadable or defaulted answer is `Unreadable`; an empty firm id never matches; an empty slot is absent from the set, so a null never matches). The external filter's early refusal calls `CheckFirmLinkAsync`; the picker applies `LinkOf` to its batched read. The static `IsFirmLinkedAsync` of round 1 is gone, so there is no second copy to drift.
- **Linked = the record's assigned law firms only** (`sprk_assignedlawfirm1/2`), owner-confirmed by D-110. A firm with only an org-level grant is refused; a wire test proves the grant table is never read.
- **Placement (why the service depends on an external-access class):** `ExternalParticipationService` lives in `Infrastructure.ExternalAccess`, a namespace the service already depends on (`ExternalDataService`, `CallerPrincipal`). The read is app-only and channel-neutral (not the external caller's rights), so the service takes the concrete class: no new interface, no new seam, no DI registration (ADR-010; `FinanceModule` registers `InvoiceSubmissionService` scoped and `ExternalAccessModule` registers the typed client). Route ledger stays **137** (text of two entries updated), ADR-010 DI ceiling stays **166**.
- **Internal route's firm input (checked, no STOP):** `POST /api/finance/{matters|projects|workassignments}/{id}/invoice-submissions?firmId=` takes a required `firmId` query. `FinanceAuthorizationFilter` checks Read + AppendTo on the record and AppendTo on the firm; it never checked that the firm belongs to the record. Now the service refuses an unlinked firm (internal tests below).
- **Internal record picker:** none exists in 044 or 051. Task 052 (not started) supplies the internal `loadRecords`. Hand-off: it must list only records whose `sprk_assignedlawfirm1/2` includes the chosen firm (a server-side filter on those two columns), and it must not rely on that for safety: the server refuses regardless.
- **Cost:** an external submit reads the links twice (the filter's early copy, then the service). One extra single-record query per submit; it keeps the early refusal before the upload is read. Accepted (K).

### 9.2 Coordinator additions, one by one

| Item | Verdict | Proof |
|---|---|---|
| 1a firmId re-checked against the caller's ACTIVE memberships before listing | held (round 1), tests added | `DecideFirmRecordsAsync` uses `ReadCallerFirmsAsync` (Active contact, conferring memberships; Inactive contact confers none; unreadable is 503). Tests: a firm only another contact belongs to is refused (403 `firm_forbidden`), and a deactivated contact's firm is refused |
| 1b only records the caller can already reach, intersected with the assigned firm | held, **tightened** | The candidates are the principal's composed, Read-gated sets; round 2 narrows them to what a submit needs (Read + Create), so a view-only record is not offered only to be refused. Tests: the intersection test (a record linked to the firm but outside the access set is neither returned nor looked up; a readable record linked to another firm is left out), and the view-only test |
| 1c authorized like the other external routes; systemuser refused | held | `.AddExternalInvoiceSubmissionAuthorizationFilter(FirmRecords)`, `.RequireEbillingEnabled()`, `.RequireRateLimiting("dataverse-query")`; a systemuser gets 403 `use_internal_route` first, reading nothing (test asserts zero membership reads) |
| 1d bounded | **fixed** (was unbounded) | At most `MaxFirmRecordsPerKind` = 200 ids per kind, from at most `MaxFirmRecordCandidatesPerKind` = 500 examined candidates per kind (id order, stable); either cut sets `truncated`; `partial` stays for unreadable links. The SPA ORs `truncated` into the existing `records-truncated` notice |
| 2 D-110 on the internal path, tested there | done | `Internal_caller_filing_under_a_firm_not_linked_to_the_record_is_403_firm_not_linked_and_nothing_is_written` (matters, projects, work assignments), the linked control, unreadable links 503, `Both_channels_refuse_an_unlinked_firm_with_the_same_status_and_code`, and the service matrix on both channels |
| 3 either slot unreadable is 503; a null never matches | done, **plus a reader fix** | Round 1's shared reader read an omitted `_sprk_assignedlawfirm1_value` / `2` property as null (= "no firm"), so a property absent from the response looked like an empty slot. Now a row missing either lookup is `Unresolved`. Wire tests: slot 1 omitted (503, even when slot 2 names the firm), slot 2 omitted (503, even when slot 1 names the firm), omitted beside empty (503, not 403), both null (403), record not returned (503), failed read (503), and the service-level unit cases (`LinkOf`, thrown read) |
| 4 firms = [] for a systemuser AND a deactivated linked contact | confirmed | `ExternalUserContextFirmsTests`: `A_workforce_systemuser_gets_an_empty_firm_list_even_with_a_linked_contact_that_has_firms`, `A_deactivated_contact_confers_no_firms`, with `An_active_contact_is_the_control_for_the_deactivated_one` |

### 9.3 For uac-r2 (shared file, additive but stricter)

`ExternalParticipationService.OrganizationReferenceRow` now records whether each org-lookup property was present in the JSON (`AssignedLawFirm1Returned` / `AssignedLawFirm2Returned`), and `GetReferencedOrganizationIdsAsync` answers `ReferencedOrganizations.Unresolved` for a row missing either. Effects: the invoice link read fails closed (503) instead of reading an unread slot as empty; the ethical-wall veto, the other consumer of this read, now treats such a row as a forced deny (it already did so for a row not returned, and `GetRootRecordFlagsAsync` already treats an EMPTY `sprk_issecure` as unreadable). Live dev (2026-10-17): an empty lookup is returned as an explicit `null`, so a healthy row is unaffected; if some tenant's Dataverse ever omits null lookups, every record would deny, which is loud (an outage), not silent. Please look at it when reviewing.

### 9.4 R3 behaviour note (round 1 change, re-checked, kept)

The widget reads `/me` once; a failed `/me` read now hides the widget's Submit action (round 1; before, the list probe kept it through an outage). Owner direction: failing closed is acceptable. For SPA-r3: do not re-add a list GET to keep the action visible; the page itself still works from a direct link.

### 9.5 Known limits (K) and found in passing

- K: two reads per external submit (9.1). Up to 3 kinds x 10 chunked reads (500 candidates, 50 per query) per picker call in the worst case, sequential; the cap is the bound.
- K: a caller with more than 500 submit-able records of one kind may have a linked record beyond the examined candidates; the picker says so (`truncated`). A search-by-firm on the server would remove it; not built (section 11 default to reuse).
- K: a record whose assigned firms change between the link read and the row create is not re-checked (same request, milliseconds).
- K: the picker's order is by id (stable), the SPA sorts by name.
- Found in passing, fixed: the invariant registry had no row for the link (I-20 added).
- Found in passing, not fixed (other owners; unchanged from 8.5): `sprk_name` on `sprk_organization` in `AssignedAccessStore.cs:879/881` (uac-r2), `projectService.ts`, `CommunicationConnectionsApp.tsx:240`. PR #2078 (uac-r2) also edits `web-api-client.ts`: merge order matters, the round 2 edit there is two lines.
- Same-class search (Step 9.5 rule 6): other readers that treat an absent lookup or flag as empty: `GetRootRecordFlagsAsync` (already fail closed on an empty `sprk_issecure`), `FindRootsReferencingOrganizationAsync` (reads ids by filter, not slots), the veto consumer (benefits from the fix). Other places that decide "firm linked to record": none left (the filter delegates, the picker uses `LinkOf`).

### 9.6 Evidence (round 2)

- **BFF** (filtered `External|InvoiceSubmission|Firms|RouteAuthorization`, the unit assembly that also compiles the contract and auth suites): 3866 passed, 0 failed. **ArchTests**: 1170 passed, 0 failed (route ledger 137, ADR-010 ceiling 166 unchanged).
- **Fail before / pass after (server):** with the service's link check neutralised (the external filter's copy kept, i.e. the round 1 state), 36 of the 161 `InvoiceSubmission*` tests fail (the internal route accepts an unlinked firm; every channel-matrix, unreadable and wire case fails); with the omitted-lookup hardening removed from the reader, 10 of 161 fail (every omitted-slot case); with the picker bounds and the Read + Create narrowing removed, 3 of the 80 contract tests fail (candidate cap, return cap and truncated flag, view-only record offered). Restored, all pass.
- **SPA**: `tsc --noEmit` clean; vitest 37 files / 1055 passed (was 1054). With the `truncated` OR removed, 1 of the 36 `InvoiceSubmitPage` tests fails. The shared package is not touched, so its jest suite was not re-run.
- **Publish size** (`dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, framework-dependent linux-x64 as the csproj sets it, PDBs as the csproj publishes them, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`; both sides built from `git archive` exports at short paths `C:\wt051r2b` and `C:\wt051r2t`): base `be1c60e38` 192 files 37.754 MB; tip 192 files 37.755 MB; delta +0.001 MB. Measured against the branch base, not a fresh master build (master carries other projects' merges that this task did not cause). No `.csproj` change: no new package, so `dotnet list package --vulnerable` was not re-run.
- **Whole assembly:** two runs of the entire `Sprk.Bff.Api.Tests` assembly in this environment timed out full-host tests at 3 to 4 minutes (`TaskCanceledException` on `HttpClient`, classes unrelated to this work: document identity, compose seams, provisioning idempotency). The affected classes pass in isolation (155 passed, 0 failed), the full host builds with the new constructor dependency (`ConfigContractTests` 7 passed), and the whole assembly was not compared with the base. Treated as load-induced; reported, not investigated further.

### 9.7 Step 9.5 (code-review and adr-check, run on the round 2 diff)

- **code-review** (all dimensions; metrics: the service grew by about 70 lines and one constructor parameter, 12 in total; cohesive, the link check is a decision of the submission and sits with the other submit checks): no Critical. Warnings: (1) the invariant registry had no row for the link, an ADR-002 WP-1 gap: **F2, fixed** (I-20); (2) the shared reader change affects the veto: reported to uac-r2 (9.3), not a defect in this change. Suggestions: the picker's read fault is caught without its own log line (the reader logs its own fault; **K4**); two reads per external submit (**K3**, 9.5). AI-smell scan: no new interface, no log-and-rethrow, no null guard on a non-nullable, comments explain decisions.
- **adr-check**: ADR-001, 007, 008, 009, 010, 013, 019, 028, 038, 044 compliant (no Graph type, filters kept, the link read is live and uncached, no new interface or registration, the same ProblemDetails `errorCode` on both channels, app-only broker read, TestServer fake instead of a mocked handler, GUIDs compared as `Guid`). ADR-002: WP-1 owner now the service plus registry row I-20 (was a violation until the row was added), WP-2 the filter and picker are previews or early copies only, WP-6 fails closed on every unreadable case. BFF hygiene section 10: no new endpoint, service registration, package or background work this round (placement as in 9.1); publish-size delta above; tests updated under `tests/unit` and `tests/integration/contract`.

## 10. Fix round 3 (a deleted record gets 422 again, comment wording). Rigor FULL (auth)

Base `dd5c19602` (the tip of `work/ebilling-r1` at the start of the round). Commits: item 1 `d3b6eced6` (server, tests, I-20 row), item 2 `39e6a431f` (comments); this note follows. uac-r2 accepted the org-reference reader change (`a93d97c49`) as shaped; this round does not touch it beyond comments. No live writes, nothing under `.claude/`, `TASK-INDEX.md` and `current-task.md` untouched.

### 10.1 Item 1: a just-deleted record answered 503 "try again shortly"

- **Cause.** The org-reference reader cannot tell a row it was not given (the record was deleted, or the id is wrong) from a fault: both are `Unresolved`, which the link check maps to `Unreadable` -> 503. Round 2 put the link check ahead of `DeriveResolverFieldsAsync`, the step that used to answer a missing record 422 `ebilling.submission.record_unavailable`, so a submit on a deleted record got a retryable 503 that retrying never clears. The external route filter's early copy did the same one step earlier.
- **Fix (a), the external filter's early copy.** `DenyUnlinkedFirmAsync` refuses only a definite `NotLinked` (403, code only, before the upload is read). `Unreadable` returns null: it passes through to the service. That is never a pass for the write: the service always runs its own check.
- **Fix (b), the order in `SubmitAsync`** (supersedes the order in 9.1): validate -> firm is active -> **read the record's own fields (a missing record is 422)** -> **firm-link check (403 / 503)** -> resolve the owner -> resolve the container -> write. The link check is still before the owner check and before any write. So **422 means the record is not there, 403 means the firm is not linked, and 503 `firm_unverifiable` means only a real fault** (a read that threw, or, for a record that exists, a link read that returned no row or a row missing a lookup property).
- **No new existence oracle.** The caller's rights on the record are checked first on both routes (the filters, unchanged; `The_link_is_not_read_for_a_caller_who_failed_an_earlier_check` shows no link read happens for a caller who failed an earlier check), so a caller reaches a 422 only for a record they could already read.
- **Shared unit harness.** With the new order a project or work-assignment submission reads its own record before the link, and the shared `Harness` served only a matter. It now serves a project and a work-assignment row too (`OtherCoreRecordsExist`, default true). The assertions that used to stop at 422 `record_unavailable` after a passed link ("linked control" for projects and work assignments, in the unit matrix, in the contract `AssertPastTheFilterAsync` and in the internal linked control) now expect `Received` / 202 for every kind, which is the stronger statement.
- **Docs.** The I-20 row in `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` section 5 states the order and the three outcomes.

### 10.2 Item 2: comment wording

The comments that said column security omits a column are now "a property absent from the response": `ExternalParticipationService.cs` (the comment where an unresolved row is mapped, and the note on `AssignedLawFirm1Returned`), the wire-test header, the `Omitted` constant and the `Matter` helper summary, and the 9.2 row 3 above. The comments also say outright that the reader is app-only and that nothing rests on column-security behaviour. Comment-only: no behaviour change in that commit.

### 10.3 Evidence (round 3)

- **Tests added (all on the BFF unit assembly, which also compiles the contract suite):**
  - unit: a record that cannot be found is 422 with its links unreadable, on both channels and all three kinds, link never read, nothing written (6 rows); a real fault reading the links of an existing record is still 503 after the record was read (2 rows; the link was asked for exactly once); the "no link read for an earlier refusal" and the 422/503 core-record tests also assert no link read for a missing record;
  - contract: external and internal, per kind, record gone plus `LinksUnreadable` -> 422 and nothing written (3 + 3 rows); external real fault on an existing record -> 503 with two link reads (the filter let it through, the service read again); external NotLinked -> 403 from the filter with one link read and the record never retrieved (proves the early refusal is still early);
  - wire (real reader over the TestServer fake): a record that cannot be found -> 422 and no request reaches Dataverse; a record that exists but is not returned by the link read -> 503 (renamed from "does not come back", now says what it proves).
  - NotLinked from the service on both channels is covered by the existing matrix (`A_firm_not_linked_to_the_record_is_403...`, the wire tests) and still passes.
- **Fail before / pass after.** With both fixes reverted to `dd5c19602`: 17 of the 179 `InvoiceSubmission*` tests fail. With only fix (a) reverted (filter refuses 503 early again): 4 fail (the three external 422 rows, and the real-fault test, which sees one link read instead of two). With only fix (b) reverted (link before the record read): 16 fail (every 422-not-503 row on both channels, the wire row, and the two existing tests `A_core_record_that_does_not_exist_is_422_and_one_that_cannot_be_read_is_503` and `The_link_is_not_read_for_a_request_an_earlier_check_refuses`, which now assert the order). Restored byte for byte: 179 of 179 pass.
- **BFF** (filtered `External|InvoiceSubmission|Firms|RouteAuthorization`): 3886 passed, 0 failed. **ArchTests**: 1175 passed, 0 failed (route ledger 137, ADR-010 ceiling 166 unchanged; no endpoint, registration or package this round).
- **Publish size:** not re-measured. The round reorders two existing statements, changes one `return` and adds comments; no `.csproj`, package, endpoint or registration changed (round 2 measured +0.001 MB against its base).
- **Review:** a self-review of the round diff and its direct callers and callees (`CheckFirmLinkAsync` is called only by the service and the filter; the picker uses the pure `LinkOf` on its own batched read, unchanged). The `code-review` and `adr-check` skills were not re-run for this small diff; ADR positions are as in 9.7 (WP-1 owner is still the service, WP-6 still fails closed: the pass-through in the filter is not a pass for the write, and the real-fault test proves 503 still comes from the service).

### 10.4 Known limit and found in passing

- K: a record deleted in the milliseconds between the service's record read and its link read answers 503 once; the retry answers 422. Not worth a second read to close.
- K: a submit on an existing record whose firm is unlinked now costs the service one extra record read before its 403 on the internal route (the external route still refuses early from the filter before the service is reached). One single-record read; accepted.
- Further defects found: none. Unchanged from 9.5 and 8.5: the other owners' `sprk_name` on `sprk_organization` call sites.
