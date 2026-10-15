# Task 043: the Content Understanding client and `IInvoiceAi.ReadInvoiceAsync`

**Code**
- `src/server/api/Sprk.Bff.Api/Services/Ai/ContentUnderstanding/`:
  - `ContentUnderstandingClient.cs` (REST, page ranges, retries, error classification);
  - `InvoiceAnalyzerResultMapper.cs` (fields → DTOs, source → page + box, range merge);
  - `InvoiceExtractionCompletion.cs` (spike `complete()` port + day/month post-check);
  - `ContentUnderstandingHealthCheck.cs`.
- `Configuration/ContentUnderstandingOptions.cs`, plus `…Validator.cs`.
- `PublicContracts/IInvoiceAi.cs`, `InvoiceAi.cs`, `NullInvoiceAi.cs`, `InvoiceAiContracts.cs` (DTOs and reason codes).
- `Infrastructure/DI/AnalysisServicesModule.cs` (unconditional registration).
- Analyzer definitions: `infrastructure/content-understanding/sprk-invoice-v2.json` (GA) and `sprk-invoice-v2p.json` (preview).
- Deploy script: `scripts/content-understanding/Deploy-Analyzers.ps1`, which now takes the api-version per file.

## D-81 as built (path-A exception, cite in the PR)

- **Primary:** `2026-06-01-preview` + `sprk_invoice_v2p`. This is the built-in default of `ContentUnderstandingOptions` and of `appsettings.template.json`.
- **Fallback:** `2025-11-01` + `sprk_invoice_v2`. Switching needs configuration only: `ContentUnderstanding:ApiVersion` and `ContentUnderstanding:AnalyzerId`, changed together.
- The seam test runs `ReadInvoiceAsync` under both configurations (`InvoiceReadSeamTests` `…PreviewConfiguration…` / `…GaConfiguration…`). The live test passed against both analyzers on dev.
- **Keeping them in step is enforced, not prose.** `tests/Spaarke.ArchTests/ContentUnderstandingAnalyzerPairTests.cs` fails when the two files differ in field schema, models, base analyzer or processing location, or in any config key except the preview-only `allowInputTruncation` / `workflow`. It has must-fire and must-not-fire controls, and it also pins the BFF's default pair to the preview file.
- **No SDK package.** `Azure.AI.ContentUnderstanding` 1.1.0 does not cover the preview API, and a prerelease SDK is not allowed, so the client is a thin REST client over `IHttpClientFactory`.
  - Auth is the DI `TokenCredential` (the UAMI-pinned `DefaultAzureCredential` from `Program.cs`), scope `https://cognitiveservices.azure.com/.default`. There is no key path.
  - This supersedes the POML's "exactly one PackageReference" criterion and NFR-12's SDK line. **The csproj is unchanged.**

## Wire behaviour (measured on dev 2026-10-15, both API versions)

| Input | Service answer | Reason code |
|---|---|---|
| Encrypted PDF, corrupt PDF | sync 400, innererror `FailedToExtractPageCount` | `invoice.read.unreadable` |
| Empty file | sync 400 `ContentEmpty`, but the client never sends one | `invoice.read.unreadable` |
| 302-page PDF, whole | sync 400 `InputPageCountExceeded` (service max 300 / request) | `invoice.read.page_limit` |
| 302-page PDF, `range=1-2` | 202 → Succeeded (the limit applies to the analysed range) | — |
| `range` starting past the last page | sync 400 `InvalidPagesOutOfRange` | end of document (client) |
| `range=2-8` on a 2-page PDF | Succeeded, existing pages only | — |
| Blank PDF, text-as-image PDF | Succeeded, a `fields` object whose fields carry a confidence and no value | `invoice.read.no_invoice` |
| Succeeded with no document content, or a content without a `fields` object | not seen on our analyzers (only `prebuilt-read`, which has no field schema, returns no `fields`) | `invoice.read.service_failed` (`MalformedResult`), never `no_invoice` |
| HTTP 413 on submit | not measured; the file is over the service's request size | `invoice.read.unreadable`, not retried |
| Analyzer id on the wrong api-version / missing | 404 `ModelNotFound` | `invoice.read.service_failed` |
| Unknown operation id | 404 `OperationNotFound` | `invoice.read.service_failed` |

- Ranged results use **original-file page numbers** on both versions.
- The preview returns `metadata.pageCount` as a string, and it is the **whole file's** count, not the range's: `range=1-2` of the 302-page PDF reports "302" (`prebuilt-read`, re-probed 2026-10-15 after review), and `range=21-26` of the 26-page B-0150 reports "26". The range loop relies on this; `InvoiceReadSeamTests.…PreviewPageCountOnALaterRange_IsTheWholeFileCount` pins it. GA returns no page count.
- On a preview analyzer, the service adds `config.workflow` (server-resolved; a PUT accepts only `default` / `agentic`) and `config.allowInputTruncation`. The script ignores the first. The file pins the second to `false`.

## Technical choices made here (not owner decisions)

1. **`analyzeBinary` with the `range` query parameter**, not JSON `inputs[].range`. It sends raw bytes, without base64, and works on both versions.
2. **Page ranges.** `PageRangeSize` (default 20, 1..300) pages per call, merged.
   - A document of up to 20 pages is one call, as D-49 asked.
   - Spike condition 3 asked for ranges on long documents, because the analyzer loses rows on very long documents read whole.
   - The size is an infrastructure option, not a D-59 setting: D-59 lists the *coding* chunk size, not this one. Tasks 161 / 162 re-measure it.
3. **Page limit.**
   - Preview: `metadata.pageCount` > `MaxPages` refuses the read after the first range.
   - GA: the client reads ranges up to `MaxPages`. If it has not reached the end, it submits a one-page range at `MaxPages + 1`. If that page exists, the read is refused. That probe operation is abandoned unread: one extra page billed, on documents over the limit only.
   - Input truncation is never enabled.
4. **Merge.** A header field takes the found value with the highest confidence across ranges (on a tie, the earliest range). An empty value never beats a found one. Rows are concatenated in range order and numbered per table.
5. **Retries.**
   - Submit retries 429 / 500 / 502 / 503 / 504 up to 3 attempts. It honours `Retry-After`, capped at 30 s, and otherwise waits 2 s × attempt.
   - Polls continue through transient statuses until `OperationTimeoutSeconds` (default 600; the spike's slowest call was 364 s).
   - All waits run on the injected `TimeProvider`.
   - `PollIntervalMilliseconds` is validated to 250..30000 (default 2000), so a misconfiguration cannot poll in a tight loop for the whole timeout.
6. **Token safety.** The bearer token is sent only to the configured endpoint's host. An `Operation-Location` on any other host, or not https, fails as `service_failed` before any request is made to it.
7. **Not configured** (empty `Endpoint`):
   - `ReadInvoiceAsync` throws `FeatureDisabledException("ai.invoice.read.not_configured")`;
   - the health check reports Healthy with "not configured";
   - the host starts.
   - A malformed value stops the host at startup (`ContentUnderstandingOptionsValidator`, `ValidateOnStart`). That includes an unreplaced `#{CONTENT_UNDERSTANDING_ENDPOINT}#` token: `src/server/api/Sprk.Bff.Api/appsettings.tokens.md` now documents the token and says to set it to an empty string where e-billing reading is not deployed.
8. **Registration is unconditional** (§10 F.1). The real `InvoiceAi` (compound-AI-ON) takes the client as a ctor dependency. The client has no AI-gate dependency. `NullInvoiceAi.ReadInvoiceAsync` throws `ai.invoice.disabled` like its siblings.
9. **ADR-013 boundary.** The client is `public` only because the public `InvoiceAi` constructor takes it. Everything else in the folder is internal: the analyze / lookup methods, the mapper, the completion step, the exception and the result records. `ContentUnderstandingClient` is added to `ADR013_AiBoundaryTests.ForbiddenAiInternalTypes`, so CRUD code that injects it fails the build gate.
10. **Health check.**
    - Catalog tag (`/healthz/catalog`, memoised), name `content-understanding`, failure status Unhealthy.
    - It GETs the configured analyzer on the configured api-version.
    - A 404, a `failed` analyzer, or a token or transport failure produces a description starting `content_understanding.analyzer_missing`.
    - It is never on the `/healthz` liveness probe (contract test).
11. **Logs** carry the document id, analyzer, api-version, range, page count, HTTP status, the service error **code** and timings. They never carry values, narratives or service error messages (ADR-015).

## Deterministic completion (spike conditions 1 and 2)

`InvoiceExtractionCompletion` ports `score.py complete()` line for line: rate from the timekeeper summary, clock-notation hours, then the arithmetic fill-ins, with the same tolerances (0.02 / 0.60) and banker's rounding to 2 places. It then applies the day/month post-check.

- Every changed value keeps the analyzer's value in `ReturnedValue` and is marked in `Repair`:
  - `ClockHoursConverted`
  - `RateFromTimekeeperSummary`: page, box and confidence are the summary row's.
  - `ComputedFromRow`: no page, box or confidence.
  - `DayMonthSwapped`
- **Values that need no repair are returned as read.** The spike rounded every hours / rate / amount to 2 places; the port rounds only for its comparisons and for the values it changes, so an unchanged value never differs from the analyzer's without a `Repair` mark. Task 049 rounds on write if it needs to.
- **One deviation from the spike:** a summary name that normalises to nothing ("(AB)") is not a lookup key. Otherwise it would give its rate to every row with no timekeeper. The initials key ("ab") still applies.
- **Known limit (K-class):** when the analyzer reads the billing period itself month-first, the rows agree with it and nothing is swapped. Live on dev, B-0002 on GA got 5 row dates swapped back and its period was read correctly. The preview read B-0002 needing no swap.

Live evidence on dev (`ContentUnderstandingLiveTests.ReadInvoiceAsync_SampleInvoice…`, counts only):

| Item | Analyzer | Lines | Clock-hours repairs | Date swaps | Fee rows where h × r = amount |
|---|---|---|---|---|---|
| B-0008 (hh:mm) | `sprk_invoice_v2` GA | 26 | 18 | 0 | 21 / 21 |
| B-0008 | `sprk_invoice_v2p` preview | 26 | 0 | 0 | 21 / 21 |
| B-0002 (GBP, dd/mm) | `sprk_invoice_v2` GA | 16 | 0 | 5 | 10 / 10 |
| B-0002 | `sprk_invoice_v2p` preview | 15 | 0 | 0 | 10 / 10 |

- The v2 wording alone did not fix GA hours or dates. The 043 smoke test before completion had only 3 / 21 B-0008 rows balancing, and B-0002 dates were month-first. Completion closes both.
- The GA / preview line counts differ on B-0002 (16 vs 15). Accuracy against ground truth is tasks 161 / 162's job, not measured here.

## DTOs task 049 depends on (`InvoiceAiContracts.cs`)

- `ExtractedInvoice`: `Header`, `Lines` (fees, then expenses), `TimekeeperSummary`, `PageCount`, `AnalyzerId`, `ApiVersion`.
- `ExtractedInvoiceField<T>`: every value. `T` is `string?`, `DateOnly?` or `decimal?`. It carries:
  - `Value`: after completion; null when the document does not show it.
  - `Confidence`: 0–1, or null. A null value often still carries a confidence.
  - `Page`: 1-based, original file.
  - `BoundingBox`: `{Page, X, Y, W, H}`, normalised 0–1, origin top-left, 4 dp. It is the union of the source regions on the first region's page.
  - `Repair` and `ReturnedValue`.
- `ExtractedInvoiceLine`: `LineType` (Fee / Expense), `RowIndex` (per table) and the FR-21 columns.
  - The members a row type does not use are empty fields.
  - `Narrative` is the fee narrative or the expense description.
  - `RowBoundingBox` is the union of the row's value boxes on the page of its first located value.
- Reason codes are on `InvoiceAiException` (`ConsumerType` = `InvoiceAiException.ReadCapability` = `invoice-read`, which is **not** a `ConsumerTypes` key):
  - `invoice.read.unreadable`
  - `invoice.read.page_limit`
  - `invoice.read.no_invoice`
  - `invoice.read.service_failed`
- No-invoice rule: no invoice number, invoice date, total due or fees total, and zero rows.

## Deviations from the POML

- **Package:** none added (D-81; see above). Step 1's researcher check of the 1.1.0 SDK was not needed. The REST surface was verified by live probes instead.
- **One analyze call:** one call per document of up to `PageRangeSize` pages, ranges beyond that (spike condition 3).
- **Test placement:** KEEP paths (ADR-038 / tests/CLAUDE.md), not `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/ContentUnderstanding/`.
  - Seam: `tests/integration/seam/ContentUnderstanding/`. A hand-written fake service at the HTTP boundary replays recorded results. It is not `Mock<HttpMessageHandler>` (B1).
  - Domain: `tests/unit/domain/Ai/ContentUnderstanding/`.
  - Host contract: `tests/integration/contract/Api/ContentUnderstandingHostContractTests.cs`.
  - ArchTest: `tests/Spaarke.ArchTests/ContentUnderstandingAnalyzerPairTests.cs`.
- **Fixtures** are embedded C# raw strings (`ContentUnderstandingRecordedResults.cs`), so the csproj is unchanged.
  - They are real spike responses for synthetic B-0002, trimmed of spans, markdown and tables.
  - They keep 3 fee, 2 expense and 3 summary rows, with values, confidences, sources and page sizes as returned.
- **Bundles:** no router (spike §7: the single analyzer extracts bundled invoices correctly). Condition 4 therefore does not apply.
- **TASK-INDEX:** not updated here. The main session owns it.

## Analyzer deployment (dev, `spaarke-docintel-invoices-dev`, 2026-10-15)

- **`-WhatIf`:** both analyzers planned "create", exit 0.
- **Mismatch guard:** `-ApiVersion 2025-11-01` with the v2p file exits 2 ("Analyzers do not move between api-versions").
- **Backward compatibility:** `sprk-invoice-v1.json` reports NO CHANGE, exit 0.
- **Live run 1:** `sprk_invoice_v2`@GA NO CHANGE (created earlier the same day and verified); `sprk_invoice_v2p`@preview CREATED and verified.
- **Live run 2:** both NO CHANGE, exit 0.
- **Deleted from dev** (spike-only; each GET 200 → DELETE 204 → GET 404):
  - `sprk_invoice_v1_cls`@GA
  - `sprk_invoice_router`@GA
  - `sprk_invoice_router_p`@preview
  - scratch `sprk_probe_043p`@preview
- **Kept:** `sprk_invoice_v1` (GA) and `sprk_invoice_v1p` (preview).
- **Not done here (deploy work):** setting `ContentUnderstanding__Endpoint` on `spaarke-bff-dev`. Until then the dev BFF reports "not configured".

## BFF hygiene (§10)

Publish-size delta, measured 2026-10-15 with `dotnet publish -c Release src/server/api/Sprk.Bff.Api/` from clean source exports of each commit, zipped with `Compress-Archive -CompressionLevel Optimal` (PDBs included):

| Build | Files | Zip (B) | Uncompressed (B) |
|---|---|---|---|
| origin/master `645f48a86` | 192 | 38,902,440 | 129,944,116 |
| this branch `3afd0f6eb` | 192 | 38,956,888 | 130,115,296 |
| **Delta** | 0 | **+54,448 (+0.05 MB)** | +171,180 |

- Code only: no package, csproj or `Directory.Packages.props` change; the same 192 files; `Sprk.Bff.Api.dll` +140,800 B.
- The delta is an upper bound for 043: it also contains the `work/ebilling-r1` commits not yet on master.
- Well under the +5 MB per-task threshold; total ~38.96 MB, under the 55 MB review line.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages, so no new HIGH CVE.
- Full unit suite (`tests/unit/Sprk.Bff.Api.Tests`, 23,318 tests, 42 min under machine load): 23,289 passed, 23 skipped, and 6 WebApplicationFactory contract tests failed with ~3-minute HTTP timeouts (`Issue975_*`, `Document*IdentityContractTests`, `ExternalMessagePostContractTests`). None touches this task's code. Rerun alone, all 35 tests in those classes pass (27 s). ArchTests 1052 / 1052.

## Review findings (task-execute Step 9.5)

- **code-review:** no Critical. The client (~640 lines) is cohesive: one REST conversation (submit, poll, range loop, classify). `ReadNotConfiguredCode` sits between methods (suggestion, left). `IsEmptyRead` treats a header with only a firm name as no invoice (K-class: by the FR-04 rule as written).
- **adr-check:** ADR-010 (no new interfaces; the client, mapper and completion are concretes; the 1:1 interface ceiling in `ADR010_DITests.cs` is unchanged), ADR-013 (facade; client on the forbidden list), ADR-015 (no values or messages in logs), ADR-028 (managed identity, no key), ADR-032, ADR-038 (KEEP paths, fake at the HTTP boundary) compliant. D-81 path A is recorded in `spec.md` ADR Tensions and `design.md`.
- **adversarial review (one pass, Opus):** seven findings, all resolved in this task:
  1. Preview `pageCount` might be per range (F2 if true): re-probed, it is whole-file; pinned by a seam test (above).
  2. A Succeeded result with no content was read as an empty invoice → `no_invoice` (fixed): now `service_failed` / `MalformedResult`; three-case seam theory.
  3. Completion does not round unchanged values (K1): kept, documented above.
  4. An unreplaced endpoint token stops the host (K3): intended (fail at startup, not at the first read); token documented in `appsettings.tokens.md`.
  5. Each range call uploads the whole file (K2): known limit of `analyzeBinary` + `range`. A 300-page file is 15 uploads of the same bytes. Splitting the PDF in the BFF would need a PDF library (a package and publish-size cost) for a cost the spike did not measure as a problem; tasks 161 / 162 re-measure.
  6. Poll interval could be 0 (K4): floor of 250 ms added; validator test cases for 249 and 30001.
  7. HTTP 413 classified `service_failed` (K1): now `unreadable`, not retried; seam test.
- **Fail-before proofs:** with the old `ReadRange`, the 413 classification, the 0 ms floor and completion neutralised, 16 of the new tests fail (3 malformed-result, 1 x 413, 1 validator floor, 10 completion, 1 seam clock-hours); with the census entry removed, `IOptionsDriftTests` fails naming `ContentUnderstandingOptions`.

rework: review fixes in-task (adversarial findings 2, 6, 7; see Review findings) — task 043
