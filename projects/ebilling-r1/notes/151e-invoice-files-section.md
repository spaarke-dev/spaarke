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
- **K4: the Quick View control XML is unverified until it is imported.** Control class id `{5C5600E0-1D6E-4205-A272-BE80DA87FD42}` follows the platform's form XML; the import is the first check.
- **K4: the list judges each file one after another (up to 50) with a storage read each.** Bounded by the cap; not parallelised to keep one schedule of reads per request.
- **K4: `ListInvoiceFiles` sequences five phases in one handler (step 0, read, decide, order, map).** Each phase is a call or a short loop; cohesive, one reason to change.

## Gate

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
