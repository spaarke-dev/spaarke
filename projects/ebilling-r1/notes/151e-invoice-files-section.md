# Task 151e: Invoice form Files section (folds in 151d)

Branch `worktree-agent-a222e9efd2d39dbc2`, from `work/ebilling-r1` at `ca795e6293`. Rigor FULL (BFF route, code page, form source). Model: Sonnet @ high. Owner asked 2026-11-03. Builds on 090 (`StoredFileAccessFilter`, the content route) and 151b (`src/solutions/ApExport` is the page pattern).

Nothing was deployed, imported or published, and no live Dataverse or Azure write was made. The deploy steps for the main session are at the end.

## What was built

| Component | Where | What it does |
|---|---|---|
| `GET /api/finance/invoices/{invoiceId}/files` | `Api/Finance/StoredFileEndpoints.cs` (`ListInvoiceFiles`) | Lists the invoice's servable files, Invoice PDF first, then Supporting files by added date. Each item: id, name, purpose, size, content type, added on, `scanState` (`Ready` or `Pending`), source email (document id and name) |
| One decision for list and content | `StoredFileAccessFilter.DecideRowAsync` (extracted from `DecideAsync`) | The content route's rule (parent Read as the caller, the copy rule, `JudgeServableAsync`) now lives in one method. The content route calls it for one row, the list for each row. No second copy of the rule |
| `CallerRightsMemo` | same file | The only caller of `GetCallerRightsForAuthorizationGateAsync` in this file; asks each record once per request (the invoice and the source email once, not once per file) |
| `StoredFileService.ReadDocumentNameAsync`, `Name` / `SizeBytes` on `StoredFileRow` | `Services/Finance/Files/StoredFileService.cs`, `StoredFileContracts.cs` | Reads the source email's name and the file's name and size. The two response records are in `StoredFileContracts.cs` |
| Invoice Files page | `src/solutions/InvoiceFiles` (web resource `sprk_invoicefiles`) | React code page after `src/solutions/ApExport`: `@spaarke/auth`, Fluent v9, the host `FluentProvider` theme (light and dark), jest. Lists the files with Open (new tab), Download and a link to the source email Document; spinner, empty, error with Retry, and a "Scan in progress" row with disabled buttons |
| Invoice main form | `sprk_Invoice/FormXml/main/{93aa1c69-...}.xml` | New Files section holding the `sprk_invoicefiles` web resource (PassParameters, so the page reads `id` and `typename`); a Quick View row on the Export Batch lookup (151d) |
| Batch quick form | `sprk_billingexport/FormXml/quick/{2c568b46-...}.xml` | Name and Owner rows replaced by one read-only Sent to AP Date row, so the quick view on the invoice shows only that |

## Behaviour

- Authorization: group `RequireInternalFinanceCaller` (step 0), then `AddFinanceAuthorizationFilter("finance.read", invoice, routeKey: invoiceId, UniformNotFound)`, then `RequireEbillingEnabled`. No Read on the invoice, an unknown invoice and a step-0 refusal answer the same uniform 404.
- A file is listed when the content route would serve it (`Ready`) or answer 409 only because its scan has no verdict (`Pending`, shown "Scan in progress", buttons disabled). Anything the content route would 404 is not listed.
- A fault in any read (rows, rights, storage, document name) answers 503 `file_store_unavailable`, never a partial list. More than 50 files for one invoice (`SubmissionCommitGate.MaxFiles` refuses such an invoice at commit) is also a 503, never a truncated list.
- No native `sprk_storedfile` subgrid, and no role, privilege or field-security change.

## CLAUDE.md section 11 justification

**`GET /api/finance/invoices/{invoiceId}/files`**
1. Existing: `GET /api/finance/files/{fileId}/content` serves one file by id (`StoredFileEndpoints.cs`); no route lists an invoice's files (`ReadRowsForInvoiceAsync` is called only by `SubmissionCommitGate`, which is not a caller-facing read, and it applies no per-caller rule).
2. Extension: the content route takes a file id and returns bytes; a list is a different resource and shape. Its rule was extracted, not copied (`DecideRowAsync`), so extending the content route would have changed its contract.
3. Cost of doing nothing: the form cannot show which files an invoice has, because no role grants Read on `sprk_storedfile` and the owner ruled out a native subgrid; a client-side list would need a second copy of the access rule.

**`src/solutions/InvoiceFiles` page.** Existing: `ApExport` is a code page for a different entity and flow; nothing renders an invoice's files. Extension: not possible, a code page web resource is one page per web resource name and the Files section embeds its own. Cost of doing nothing: no Files section on the form.

**`CallerRightsMemo`.** Existing: `CallerRecordAccessProbe` answers one record per call. Extension: caching inside the probe would change every other caller's timing-equalized behaviour. Cost: a 50-file invoice would ask the invoice's rights up to 50 times.

## BFF placement

In the BFF: it is a Finance read next to the content route, authorized with the BFF's own endpoint filters (ADR-001, ADR-008), through `StoredFileService` and `CallerRecordAccessProbe`. No new DI registration, package, background work or Dataverse column; no AI dependency (ADR-013). No `FinanceModule.cs` change was needed.

## Deviations

1. **Fast-forward failed.** `git merge --ff-only` was refused because the worktree branch had diverged (it was created from master). After confirming its old HEAD was reachable from other branches, the branch was reset to `ca795e6293` with `git reset --hard`.
2. **List equals content over "listed with sourceEmail null".** The POML says a file whose source email the caller cannot read is listed with `sourceEmail` null; it also says the list is exactly what the content route serves. They conflict, because the copy rule makes the content route 404 such a file. The invariant won: such a file is NOT listed. A test pins this.
3. **151d by Quick View.** The Sent to AP Date is a Quick View of the Export Batch lookup, with its quick form reduced to that one read-only field. No new invoice column and no script.
4. **50-file cap.** Added (`MaxListedFiles`, 503 above it), matching the commit gate's limit.
5. **`Name` added to `StoredFileContracts.ReadColumns`** (the file's name and size for the list). It is read by every row read, the content route's too.
6. **Navigator side pane not registered.** The page is a form-embedded web resource, not a code page opened from the app; `ensureNavigatorSidePane()` does not apply.
7. **Source-scan test count changed.** `StoredFileEndpointsTests` counted the gate calls in the endpoint source; the gate call is now in `CallerRightsMemo`, so it asserts 1 gate call and 2 `rights.GetAsync(` calls.
8. **Response records moved to `StoredFileContracts.cs`.** `InboundBodyDtoMappingGuardTests` read `public sealed record X(` under `Api/**` as an inbound body; outside `Api/**` they are plain contracts.

## Review (task-execute Step 9.5)

code-review and adr-check were run by the implementer. The independent adversarial review and the publish-size delta measurement were NOT run here; the main session does both (brief).

- ADRs considered: ADR-001/008 (Minimal API, endpoint filters: compliant), ADR-002 (read-only, no write path: compliant), ADR-006 (code page web resource, as ApExport: compliant), ADR-007 (no Graph types: compliant), ADR-010 (no interface, no registration; `CallerRightsMemo` is a concrete internal class: compliant), ADR-013 (no AI: compliant), ADR-021 (Fluent v9, no colour literals, scan test with must-fire control: compliant), ADR-028 (`authenticatedFetch` only, no raw Authorization header: compliant).
- F-class findings: none open.
- Fix-the-class search (`GetCallerRightsForAuthorizationGateAsync` callers): in `StoredFileEndpoints.cs` it is now called only from `CallerRightsMemo` (the source-scan test counts it). The other callers (`StoredFileService.cs:424`, `InvoiceSubmissionEndpoints.cs`, `RecordFamilyEndpoints.cs`, `FieldMappingEndpoints.cs`, `RecordRouteAccessAuthorizationFilter.cs`, `MappingSourceReadProbe.cs`, `TodoSourceAccessFilter.cs`) each ask once for their own record and were not changed. `ReadRowsForInvoiceAsync` already existed (the commit gate calls it); this change added one caller.

### Known limits (K-class), not changed

- **K3: the Sent to AP Date Quick View needs Read on `sprk_billingexport`.** A user without it sees nothing in that row (the same caveat as 151c-1b K3). No privilege was changed.
- **K3: a user who cannot read the source email does not see the Invoice PDF.** The PDF is a copy of the email's attachment; the content route 404s it for that user, so the list does not show it. It is the rule, kept in one place, not a defect of the list.
- **K4: the Quick View form XML as a whole is unverified until it is imported.** The control's parameter shape was verified against dev's own forms in the review fix (F2 below); the import is still the first check of the whole form.
- **K4: the list judges each file one after another (up to 50) with a storage read each.** Bounded by the cap; not parallelised to keep one schedule of reads per request.
- **K4: `ListInvoiceFiles` sequences five phases in one handler (step 0, read, decide, order, map).** Each phase is a call or a short loop; cohesive, one reason to change.

## Review log (adversarial review, 2026-11-03; fixes on `work/ebilling-r1` from `f62300190e`)

| # | Finding | Fix | Where | Seed proof |
|---|---|---|---|---|
| F1 | A rights fault hid files instead of answering 503. `CallerRightsMemo` asked the gate variant, which answers `None` for a 429, a 5xx, an OBO failure or a WhoAmI failure; `DecideRowAsync` then dropped the file (a partial list or "No files"). The task says a read fault is 503, never a partial or empty list. | `ListInvoiceFiles` prefetches the caller's rights once with `GetCallerRightsForRecordsOrUnknownAsync` (task 081's variant) for the invoice and the distinct source documents of this invoice's own rows. Any unknown (null) answer is the route's read-fault 503 (`file_store_unavailable`). The answers seed the memo (`CallerRightsMemo.Seed`); `DecideRowAsync` and the content route are unchanged. | `Api/Finance/StoredFileEndpoints.cs` `ListInvoiceFiles` (prefetch and 503), `CallerRightsMemo.Seed` | `ReadFault_503("rights-email")` and `("rights-invoice")` fail (200: a partial or empty list) with the null check removed and a null answer read as `None`; pass restored |
| F1b | (the review's K2) A step-0 refusal inside the handler whose `RefusalReasonCode` is `SystemFailureReasonCode` answered the uniform 404. A fault is not a "not found". | That reason answers 503 `file_store_unavailable`; every other refusal (flagged external, no active row) stays the uniform 404. | `StoredFileEndpoints.cs` `ListInvoiceFiles`, the step-0 branch | `StepZeroSystemFailureInHandler_503_RealRefusalStays404` fails (404 where 503 is expected) with the old branch; its control (a real refusal at the handler's step 0) stays 404 |
| F2 | The Quick View's `QuickForms` parameter held literal child elements; the platform stores an escaped string. | Copied the platform's shape (evidence below): `QuickForms` is escaped text of `<QuickFormIds><QuickFormId entityname="...">GUID</QuickFormId></QuickFormIds>`, `ControlMode` `Edit` beside it, the control `disabled="false"`. | `sprk_Invoice/FormXml/main/{93aa1c69-...}.xml` (and the managed copy) | `Form_ShowsBatchSentToApDate` parses the real shape; it fails on the old form (`QuickForms` has child elements); a control in the test pins that check |
| F3a | The two managed form copies were not in step with the 151e edits. | Both edits mirrored into `{93aa1c69-...}_managed.xml` and `{2c568b46-...}_managed.xml`, as task 015's commit `b2d8cb6dda` did by hand (the same change as the unmanaged copy). One export convention carried over: the managed copies hold `description=""` on cell labels (section and tab labels keep their text), so the new cells' labels are blank there. | the two `_managed.xml` files | none (source files; the form tests read the unmanaged copies) |
| F3b | The form references the web resource `sprk_invoicefiles`, which is not in `WebResources/` or `Other/Solution.xml`. | **Not brought into source; recorded as a task 153 prerequisite** (below). | none | none |

### F1 design notes

- **Unknown is not denied.** `GetCallerRightsForRecordsOrUnknownAsync` keeps "Dataverse said no" (a 403: `AccessRights.None`, the file is not listed) apart from "Dataverse said nothing" (null: 503). A 404 from Dataverse after its re-asks is also null.
- **No timing equalization needed.** The invoice was already authorized by the route's filter on the equalized gate path, and the source document ids come from the file store (the app's own read), never from the caller. The list therefore offers no unknown-versus-denied oracle, so the unequalized variant is fine here. (The content route keeps the equalized gate variant: its `fileId` is caller input.)
- **Prefetch scope.** Only rows whose parent is this invoice contribute source documents; a row of another parent is never asked about. An invoice with no such rows makes no rights call (an empty list is not a fault).
- **Risk to watch (not changed):** a source email that was deleted would answer 404 from `RetrievePrincipalAccess`, which is "unknown", so the whole list answers 503 while such a copy exists. Before this fix the file was silently dropped. Revisit if a deleted-email case shows up in UAT; the fix would be a typed "record gone" answer from the probe (task 081's contract), not a change in this list.
- **Calls.** One batched call (OBO exchange and `WhoAmI` once, then one `RetrievePrincipalAccess` per record at bounded concurrency) replaces 1 + distinct-document sequential gate asks.
- **Test rig.** `RecordingRightsProbe` (`tests/integration/contract/Finance/Files/StoredFileFakes.cs`) gained an `Unknown` set and an override of `GetCallerRightsForRecordsOrUnknownAsync` that answers null for those records (and for every record when there is no token, as the real one does) and records each ask in `Calls`.

### F2 evidence: the Quick View control's real shape

Read-only query of `systemforms` on spaarkedev1 (2026-11-03), filtering `contains(formxml,'5C5600E0-1D6E-4205-A272-BE80DA87FD42')`: four forms (Account main form, Account for Interactive experience, Social Profile, Social Profile for Interactive experience). The Account main form (`8448b78f-8f42-454e-8e2a-f8196b0419af`), control `contactquickform`, verbatim:

```xml
<control id="contactquickform" classid="{5C5600E0-1D6E-4205-A272-BE80DA87FD42}" datafieldname="primarycontactid" disabled="false"><parameters><QuickForms>&lt;QuickFormIds&gt;&lt;QuickFormId entityname="contact"&gt;29DE27BC-A257-4F29-99CF-BAB4A84E688F&lt;/QuickFormId&gt;&lt;/QuickFormIds&gt;</QuickForms><ControlMode>Edit</ControlMode></parameters></control>
```

So `QuickForms` is an escaped string (text), the quick form id has no braces and is upper case, `ControlMode` is `Edit` (a Quick View displays read-only regardless), and the control is `disabled="false"`. The invoice form now carries the same shape with `sprk_billingexport` and `2C568B46-E9DD-4774-BCFA-00C436CB4EBA`.

### F3b decision: the web resource is a task 153 prerequisite, not source

Checked: `WebResources/` does hold code pages (`sprk_NavigatorPane.html`, `sprk_invoicespage.html`, `sprk_communicationspage.html` and others, each with a `.data.xml` carrying a Dataverse-assigned `WebResourceId` and `FileName`), and `Other/Solution.xml` lists type 61 root components for them. But the in-family precedent does not follow it: `sprk_apexport` (the 151b ApExport page, the same pattern as this page) is deployed on spaarkedev1 (id `837ac16f-8bd7-f111-84e8-7ced8dda31f8`) and is NOT in `WebResources/` or `Solution.xml`; only its ribbon script is. `sprk_invoicefiles` does not exist on dev yet (queried read-only).

Why it was not added now: the `WebResourceId` is assigned by Dataverse on first creation, and the existing `.data.xml` files carry exported ids. An invented id would collide with the web resource that `scripts/Deploy-WebResourceInline.ps1` creates (deploy step 2 creates it before the form import, because the form needs it to exist), and a committed copy of the 970 KB build output goes stale on the next page change. The right order is deploy, then export.

**Task 153 prerequisite** (add to its deploy list): after `sprk_invoicefiles` is created on dev and the form imported, add the web resource to SpaarkeMaster (add it to the solution, export, commit `WebResources/sprk_invoicefiles.html` with its `.data.xml` and the type 61 root component); do the same for `sprk_apexport`. Until then the source form references a web resource the solution does not carry, so importing SpaarkeMaster into an environment where the page was not deployed first fails on the missing dependency (dev is fine: the deploy step runs first).

### Out of scope (K-class, left as they are)

- Latency of up to 50 sequential judgements (K4, as above).
- The "Source email" label for a non-email source document.
- Deviation 2: a file whose source email the caller cannot read is not listed. The owner is being asked.

## Gate

**After the review fixes (tip of the fix commit):** five test projects built, 0 warnings, 0 errors; unit filter `Finance|Invoice|Ebilling|StoredFile` 6446 passed, 0 failed, 5 skipped; ArchTests in full 1928 passed, 0 failed; InvoiceFiles jest 4 suites, 41 tests passed. `InvoiceFilesListTests` + `StoredFileEndpointsTests`: 73 passed (the new cases are 2 `ReadFault_503` theory rows, the step-0 test, and the changed form test). With all fixes removed together: 4 failed (both `rights-*` rows, the step-0 test, the form test), 22 of 26 in `InvoiceFilesListTests` passed.

**Original gate (151e, before the review):**

- Five test projects built (`Sprk.Bff.Api.Tests`, `Spaarke.ArchTests`, `Sprk.Bff.Api.IntegrationTests`, `Spe.Integration.Tests`, `Sprk.Provisioning.ControlPlane.LoadTests`): 0 warnings, 0 errors.
- Unit filter `Finance|Invoice|Ebilling|StoredFile`: 6418 passed, 0 failed, 5 skipped (live-service tests).
- `InvoiceFilesListTests` (21) and `StoredFileEndpointsTests` fail with the shared rule broken and pass restored (seed proof: one failure with the parent-Read check removed from `DecideRowAsync`, then restored).
- ArchTests in full: 1918 passed, 1 failed, 1919 total, twice. The one failure both times is `ContactCreateOwnerPolicyGuardTests.Control_ManyAssignmentsToOneLocal_ResolveInBoundedTime` ("ISS-005 guard: 60 assignments to one local resolve in bounded time"), a wall-clock check (under 5 s) over a synthetic in-memory string that reads none of this task's files; it took 5 s and 8 s inside the full run on a machine with another agent building the BFF, and passes alone (the 129 tests matching ISS-005 all passed in 14 s). Found in passing, not caused by this task: a load-sensitive timing threshold (K4); not changed. The earlier real failure (`InboundBodyDtoMappingGuardTests`, the response records under `Api/**`) is fixed (deviation 8). `RouteAuthorizationGuardTests` and the finance step-0 coverage guard pass unchanged.
- Page: jest 4 suites, 41 tests passed; `npm run build` (surface gate, vite single file) produced `dist/sprk_invoicefiles.html`, 970,225 bytes.
- `scripts/check-task-status-drift.ps1 -Project ebilling-r1`: 151e agrees (POML completed, index done). It still reports 151d as an index row with no POML (it never had one) and 17 older POML/index disagreements (for example 150, 151, 151a); none were caused by this task and none were changed.

## Deploy steps (main session; nothing is done yet)

1. Build the page: from `src/solutions/InvoiceFiles` run `npm install --legacy-peer-deps --no-audit --no-fund`, then `npm run build`. Output: `dist/sprk_invoicefiles.html`.
2. Deploy the web resource BEFORE importing the form (the form references it): `scripts/Deploy-WebResourceInline.ps1 -WebResourceName sprk_invoicefiles -FilePath <path>/dist/sprk_invoicefiles.html -DisplayName "Invoice Files"`. Publish the web resource.
3. Import the two forms with `scripts/Import-SolutionScoped.ps1` for `sprk_Invoice` (main form `{93aa1c69-0406-f111-8406-7c1e525abd8b}`) and `sprk_billingexport` (quick form `{2c568b46-e9dd-4774-bcfa-00c436cb4eba}`), then an entity-scoped publish of both entities (not a publish-all).
4. The BFF route ships only with the next BFF deploy (SPA-r3 owns the dev BFF; ebilling never deploys it). Until then the Files section shows its "temporarily unavailable" or "not available" message.
5. Verify on an invoice with a PDF and supporting files: the Files section lists the Invoice PDF first; Open shows it in a new tab; Download saves it under its name; the source email link opens the Document; the Export Batch Quick View shows Sent to AP Date (when the batch has one).

rework: none task 151e
