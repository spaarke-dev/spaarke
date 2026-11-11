# Task 052 - CreateInvoiceWizard rewired to the internal entry point (FR-19, D-19, D-24, D-91, ADR-002)

Status: built; shared-library jest, lint, client-lint and the code-page production build green; **browser ui-tests deferred to the WP4 deploy (task 112)** (section 6). Rigor: STANDARD authored; run as FULL (the task changes `.ts` / `.tsx`, so the decision tree overrides up). Step mode directional.
Built on 044 (internal routes), 051 (shared `InvoiceSubmissionWizard`) and 105 (override routes and the server signal). Neither escalation trigger fired: 105's `GET /api/finance/submissions/{id}/blocked` returns `canOverride` / `canReturn` computed by the server, and `POST .../return` exists (F-7 was decided as D-73: Return needs no privilege).

## 1. What was built

| File (under `src/client/shared/Spaarke.UI.Components/src/components/`) | Role |
|---|---|
| `CreateInvoiceWizard/CreateInvoiceWizard.tsx` (rewritten) | The shared `InvoiceSubmissionWizard` mounted on the internal routes. Props: `open`, `onClose`, `dataService` (reads only), `embedded`, `authenticatedFetch`, `bffBaseUrl`, `initialAssociation`, `lockAssociation`. Without the fetch or the base URL it renders an "unavailable" message and no flow |
| `CreateInvoiceWizard/internalInvoiceSubmission.ts` (new) | Adapter: `loadRecords`, `loadFirms`, `submit`, `getStatus` for the 051 wizard, plus `getBlocked` and `decide` for the panel. Handles both a thrown `ApiError` and a returned non-2xx response |
| `CreateInvoiceWizard/BlockedSubmissionPanel.tsx` (new) | Findings summary, Override (only when the server says `canOverride`), Return to firm (only when `canReturn`); both through the canonical `FormModal` with a mandatory reason (ADR-050) |
| `CreateInvoiceWizard/index.ts` | Barrel: wizard, panel, adapter and its types |
| `InvoiceSubmission/InvoiceSubmissionWizard.tsx`, `InvoiceSubmissionOutcome.tsx` | Three additive optional props (section 3, deviation 4). The SPA does not pass them: its behaviour is unchanged |
| `src/solutions/CreateInvoiceWizard/src/main.tsx` | Three props removed (`navigationService`, `resolveSpeContainerId`, `tenantId`) |
| `scripts/quality/client-lint/eslint-suppressions.json`, `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` | The two stale entries for the deleted files removed; the inventory line says `invoiceService.ts` is gone |

### Flow

1. **Record**: the launch record (`initialAssociation`, a matter, project or work assignment) starts chosen; with `lockAssociation` it is the only record offered and nothing is read. Otherwise the picker lists active matters, projects and work assignments read through `IDataService` (200 rows per source; `partial` / `truncated` are said on screen by the shared step).
2. **Firm**: the firm is the one the record is linked to (D-106), read from the existing `GET /api/finance/core-records/{Matter|Project|WorkAssignment}/{id}/firms`. One firm: the adapter names it. None: refused before any upload (`ebilling.submission.no_firm_linked`, a client-worded precondition). Several: the shared wizard's "Choose firm" step (a locked launch asks up front; an unlocked one asks when the user submits, through the 051 `firm_required` mechanism, keeping the file and the details).
3. **File** and **Details** (firm invoice number required, D-91; PO optional): the shared steps.
4. **Submit**: multipart `POST /api/finance/{matters|projects|workassignments}/{id}/invoice-submissions?firmId=`. Then Processing, then Accepted (tracking number) or Rejected, read from `GET /api/finance/invoice-submissions/{id}`.
5. **Held submission** (Rejected with `ebilling.submission.blocked`): the findings panel. Override and Return to firm per the server signals; the status poll keeps running while held, so an override flips the screen to Accepted (with an "Accepted (overridden)" note) and a return shows "Returned to the firm".

The client creates nothing: `grep` for `createRecord` of `sprk_invoice` / `sprk_invoicesubmission` and for `createInvoice` over `src/client` finds nothing, and a jest scan holds that line (section 5).

## 2. Acceptance criteria

| Criterion | Evidence |
|---|---|
| Submits through the internal route, shows the server result; user selects firm and core record | `CreateInvoiceWizard.test.tsx` (submit end to end, several firms, unlocked record choice); adapter tests for each route |
| Negative (ADR-002): no `createRecord` of `sprk_invoice`, no `invoiceService.createInvoice` in `src/client` | scan test `internalInvoiceSubmission.test.ts` "ADR-002 / FR-17" (fails when the old wizard and service are put back, section 5); the wizard test also asserts `createRecord` / `updateRecord` are never called |
| Negative (FR-19): no Override option without the server signal; a 403 is shown and nothing changes | panel tests (no Override text at all when `canOverride` is false); wizard test "a 403 from the override call is shown and nothing changes" |
| Override needs a non-empty reason before the call | panel and wizard tests: the submit button is disabled for blank / spaces and no request is made |
| `launchCreate.ts`, `CreateAnalysisWizardWidget.tsx`, `CreateRecordWizard` compile and their tests pass | none imports a symbol this task removed (they only mention the wizard in comments; `launchCreate` opens the entity form, `VisualHostRoot` opens the code page by name). The `CreateRecordWizard` suites are in the green full run (section 4) |
| Every ui-test, including dark mode | **Not run**: needs the deployed code page and a browser. Section 6 |
| Shared library tests exit 0; the code page builds; no new npm package | section 4; `package.json` of both packages unchanged |

## 3. Deviations from the POML

1. **Deleted instead of modified**: `invoiceService.ts`, `CreateInvoiceStep.tsx`, `formTypes.ts`. Their only job was the client-side create and its form (name, description, vendor organization, date); the server flow takes the firm invoice number from the shared Details step (D-91), so nothing in them is needed ("keep only what the new flow needs" = nothing).
2. **Follow-on cards dropped, all three** (Send Email, Add To Do, Assign Work). They bound to the new invoice's id, which does not exist when the wizard finishes: the server creates the invoice after processing, and the 044 status view carries no invoice id. The "View Invoice" button went with them. `WizardFollowOns`, `createTodoRegardingChild` and the other wizards that use them are untouched.
3. **Firm picker is "the firms linked to the record", not "active organizations".** D-106 makes the server refuse a firm that is not linked to the record, so a list of all active organizations would offer firms the server then refuses. The existing internal route `GET .../core-records/{type}/{id}/firms` (044 / 095) returns exactly the active, linked firms; the picker uses it. Consequence: the firm is always linked by construction, so `firm_not_linked` cannot arise from this wizard.
4. **Shared 051 component extended with three additive optional props**: `InvoiceSubmissionWizard.initialRecord` (start with the launch record chosen: preserves the old `initialAssociation` behaviour without a hidden step), and `keepPolling` / `renderAccepted` on the wizard and on `InvoiceSubmissionOutcome`. Without `keepPolling` a Rejected answer stops the poll, so after an override the screen would still say Rejected; this is what lets it follow the server to Accepted. Defaults are unchanged (controls in the tests); the SPA passes none of them.
5. **Props removed from `ICreateInvoiceWizardProps`**: `navigationService`, `resolveSpeContainerId`, `tenantId` (no file upload to SPE, no indexing, no record opening any more). `main.tsx` updated. `authenticatedFetch` and `bffBaseUrl` stay optional in the type (as before) but without them the wizard shows an unavailable message.
6. **Association step**: the old `AssociateToStep` (Dataverse lookup side pane) is gone with `CreateRecordWizard`; the shared Record step replaces it (a pick list, filterable). `INVOICE_REGARDING_TARGETS` is now unused (K4).

## 4. Results

- Shared library, full jest: **361 suites / 5927 tests passed**, 0 failed. (On a fresh worktree 45 suites cannot load because `@spaarke/sdap-client` is unbuilt; `npm run build` in `Spaarke.SdapClient` fixes the environment, nothing in the repo changed. That run, before the build: 316 passed / 45 failed to load / 5599 tests, none failing on an assertion.)
- New tests: 66 (adapter 30, panel 17, wizard end to end 11, Outcome hooks 4, `initialRecord` 4). Deleted: about 25 (section 7) plus one case of `webApiCreateError.test.ts`.
- `tsc --noEmit` (shared library): 2 errors, both `Cannot find module '@spaarke/auth'` (unbuilt sibling), none in a changed file.
- `eslint` on the touched folders: clean. `client-lint check --package ui-components`: ok (it first caught an empty `catch`, fixed).
- Code page `npm run build` (vite): ok, `dist/index.html` 948 kB (gzip 263 kB).
- The SPA (`external-spa`) was not rebuilt: it passes none of the new props and the shared defaults are pinned by tests; it picks the change up on its next build.

## 5. Fail-before evidence

| Proof | How | Result |
|---|---|---|
| `keepPolling`, `renderAccepted`, `initialRecord`, and the whole wizard flow need the 051 additions | the two 051 files put back to `HEAD`, then the three affected test files run | **11 failed** / 8 passed (the 8 are the controls and the unaffected cases); restored: all pass |
| ADR-002 scan has teeth | the old `CreateInvoiceWizard.tsx` and `invoiceService.ts` put back as extra files in the folder | scan fails naming `createInvoice` and `createRecord of sprk_invoice`; files removed |
| Override option follows the server signal | panel test `canOverride: false` asserts no element mentions "override" | passes only because the button renders only on the signal |

## 6. ui-tests (deferred)

The four ui-tests (internal submission, Override without privilege, Override with privilege, dark mode) need the code page deployed and a browser session; this task does not deploy (task 112) and the session has no browser. What stands in until then: jest runs the real wizard, `WizardShell`, `FormModal` and Fluent end to end against a fake of the internal routes; a scan asserts no colour literal in the new UI files (ADR-021: tokens only, dark mode comes from the host `FluentProvider`).

## 7. Tests deleted (ADR-038)

All covered only the removed client create path; none is under a KEEP path (`tests/**`).

| Test | Why |
|---|---|
| `__tests__/invoiceService.resolver.test.ts` (12) | `createInvoice`'s resolver / field-mapping behaviour; the code is gone |
| `__tests__/invoiceService.documentBind.test.ts` (1) | the client file-upload bind; gone |
| `__tests__/CreateInvoiceStep.test.tsx` (5) | the removed form step |
| `__tests__/CreateInvoiceWizard.associateToStep.test.ts` (7) | `resolveInvoiceAssociateToStepConfig` for the removed `AssociateToStep` wiring; replaced by `resolveInitialInvoiceRecord` tests in `internalInvoiceSubmission.test.ts` |
| `utils/__tests__/webApiCreateError.test.ts`, the "Invoice" case and two imports | it drove `InvoiceService.createInvoice` for the BFF-403 sentence; the other four wizards' cases stay |

## 8. Component justification (CLAUDE.md section 11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `internalInvoiceSubmission.ts` | The SPA's `web-api-client.ts` adapter (other transport, other routes) and `decisionWizardService` (other routes) | Neither calls the internal `/api/finance` routes; the shared wizard is host-agnostic by design (ADR-012), so the host adapter lives with the host | The wizard has no way to reach the server: FR-19 and ADR-002 fail |
| `BlockedSubmissionPanel.tsx` | `InvoiceSubmissionOutcome`'s `renderReport` slot (051 left it for exactly this) | The slot is empty by design; the findings and the two decisions are internal-only (D-24) and must not ship in the SPA bundle's shared component | A held submission has no Override or Return in the UI: FR-19 fails, the invoice stays held forever |
| three optional props on the 051 component | the same component | Without them the held result cannot follow the server to Accepted, and the launch record cannot start chosen | A stale "Rejected" after an override; a hidden-step regression for launches from a record |

## 9. Known limits (K) and findings

- **K1** The status poll runs at the 2 s default while a held submission is on screen (until it is decided or the dialog closes). A held invoice has no other signal; the poll is a single GET. Not tuned.
- **K2** The record picker reads 200 rows per source and says so when a source is full (`truncated`); with more records the user relies on the launch record (the usual entry) or the search box over the loaded rows.
- **K3** The record picker is not filtered by firm. The firm is derived from the chosen record (section 3, item 3), so there is no mismatch to filter.
- **K4** (outside this task) `INVOICE_REGARDING_TARGETS` in `AssociateToStep/types.ts` has no consumer left. Left in place (shared export; removing it is a separate change).
- **K5** (outside this task) `launchCreate('invoice')` (Communication "Link Invoice") opens the OOB `sprk_invoice` entity form, not this wizard. Whether that entry should open the wizard instead is a product question for the owner; not changed.
- Pattern grep (Step 9.5 rule 6): `createRecord(.*sprk_invoice` and `createInvoice` over `src/client` (not node_modules): no match left in source; the only hits are the scan test's own strings.

## 10. What the deploy (task 112) needs

- Rebuild and deploy the `sprk_createinvoicewizard` web resource with `scripts/Deploy-WizardCodePages.ps1` (it builds `src/solutions/CreateInvoiceWizard`; no new file, no package change). The launchers that open it by name (`VisualHostRoot`, checked) need no change.
- The BFF on dev must carry 044, 095 (the firms route) and 105 (the override routes); the eBilling flag on; the "eBilling Override" privilege is on dev for Spaarke Core User (105, D-142), so the Override button appears for that role and for nobody else.
- The SPA bundle picks up the shared-component change on its next build (additive).
- Then run the four ui-tests of the POML in the deployed environment, including dark mode and the 403 on override as a user without the privilege.

rework: fix-round-1 task 052
