# Task 014 — Invoice lifecycle transition service (notes)

> 2026-10-15. Rigor FULL, Sonnet @ high. Files: `Services/Finance/InvoiceLifecycleService.cs` (new), `Infrastructure/DI/FinanceModule.cs` (+1 registration), `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/InvoiceLifecycleServiceTests.cs` (new), `tests/Spaarke.ArchTests/ADR010_DITests.cs` (ceiling 160 -> 161).

## 1. What was built

- `IInvoiceLifecycleService.TransitionAsync(invoiceId, InvoiceLifecycleStatus target, ct)` returns `InvoiceTransitionResult { Outcome = Transitioned | AlreadyInTarget | Refused, Requested, Current, Reason }`.
- Reason codes (stable strings, `InvoiceTransitionReasons`): `invoice.transition_not_allowed`, `invoice.not_found`, `invoice.status_unknown` (stored statuscode not one of the seven: fail closed, no write), `invoice.concurrent_change` (three conflicts in a row; nothing written, caller may retry).
- Numbers live once in `InvoiceStatusCodes` (012's values: 1 / 659490000..659490004 / 2, state 0 for In Review, Approved, Sent to AP; 1 for the rest). 012's recorded values equal the FR-06 list, so the POML escalation trigger did not fire.
- Write = `statuscode` + `statecode` in one PATCH (the pairing Dataverse requires; same shape as `sprk_event` `UpdateEventStatusAsync`), conditional on `versionnumber` via `IFieldMappingDataverseService.UpdateRecordFieldsIfUnchangedAsync` (If-Match). A 412 re-reads and re-validates (up to `InvoiceReviewService.MaxConditionalWriteAttempts` = 3, the same constant); a 404 on write is `not_found`.
- The enum is `InvoiceLifecycleStatus`, not `InvoiceStatus`: `FinanceBaseValues.InvoiceStatus` is the retired `sprk_invoicestatus` column constant (015 removes it) and the two names would read as the same thing.

## 2. Placement and CLAUDE.md §10 / §11

- **Placement (§10 bullet 1):** BFF, `Services/Finance`, next to the other write-path owners; it is a synchronous, in-request, transactional status move on a record the BFF already writes (ADR-052 signals B1/B2), not background work, so no Function / Container Apps Job.
- **ADRs:** ADR-002 (one server-side owner, WP-1; security/final states fail closed, WP-6), ADR-010 (feature-module registration, unconditional; ceiling below), ADR-038 (tests), ADR-001/008 not touched (no endpoint).
- **§11 Existing:** Grep of `Services/**` for `statecode` / `statuscode` writers of `sprk_invoice` finds none (only `EbillingSettingsReader` reads statecode of its own table); `InvoiceReviewService` writes the retired `sprk_invoicestatus`; the conditional-write primitive and retry shape are reused from it (constants `InvoiceEntity`, `VersionNumber`, `MaxConditionalWriteAttempts`), not copied.
- **§11 Extension:** No. `InvoiceReviewService` is the classification-gate / confirm service; adding transitions gives it a second reason to change and four later callers (103, 104, 150, 151) need the transition without the review flow.
- **§11 Cost of doing nothing:** FR-06's "Paid -> In Review refused" fails and each of those four tasks writes status directly, breaking ADR-002's one-owner rule.
- **New surface:** 1 interface, 1 class, 1 DI line, 3 small public types (`InvoiceLifecycleStatus`, `InvoiceTransitionResult`, `InvoiceTransitionOutcome`) + two static classes of constants. All in one file as the POML specifies; one cohesive responsibility (status move), so no decomposition needed (§11.5).

## 3. ADR-010 1:1 ceiling

`tests/Spaarke.ArchTests/ADR010_DITests.cs`: `knownOneToOneCeiling` 160 -> **161** (+1, only `IInvoiceLifecycleService -> InvoiceLifecycleService`; verified by running the test at 160, which fails and lists exactly that interface as the new one). Seam justification is in the comment block there: it is the Moq boundary of four later callers. Tasks 016 and 043 in parallel worktrees may raise the same constant; the merge will need the numbers added (each +1 or more) and the comments kept.

## 4. Publish size and CVE (CLAUDE.md §10 bullet 3)

Both from short-path trees (`git archive` of the base commit `9223a0a57` and of `HEAD`, extracted to `C:\tmp\t014\base` / `head`); `dotnet publish -c Release -r linux-x64 --self-contained false src/server/api/Sprk.Bff.Api/`; `Compress-Archive -CompressionLevel Optimal` over the output; PDBs included; 192 files each.

| Tree | Compressed |
|---|---|
| base (fresh build of the project branch before this task) | 38,927,108 B = 37.124 MB |
| head (this task) | 38,930,277 B = 37.127 MB |
| **Delta** | **+3,169 B (+0.003 MB)** |

No package reference changed, so no new CVE source (`dotnet list package --vulnerable` not re-run for an unchanged package set).

## 5. Tests

`InvoiceLifecycleServiceTests`: 28 cases, 0 failed. Covers: each of the 7 allowed transitions writes the right statuscode + paired statecode conditional on the read version; all 49 (current, requested) pairs (7 write, 7 no-op, 35 refused with no write); the four named disallowed examples; AlreadyInTarget; a conflict after which the other writer made the invoice Rejected (retry refuses, never writes version 2); a conflict after which the other writer made the same move (AlreadyInTarget); a conflict that leaves the move allowed (retries on the fresh version); conflict on every attempt (3 attempts, `concurrent_change`, no unconditional update); missing invoice; delete between read and write; unknown / null stored status (fail closed); unreadable version (throws, no write); 012's live values pinned; DI gating.

Fail-before: with `IsAllowed` forced to `true` and the DI line removed, 7 of 28 failed (`EveryPair_BehavesPerTheFr06Matrix`, the four named disallowed cases, the conflict-refuses case, the DI case); restored, 28 pass.

Also run: all `Finance` tests + `AnalysisServicesModuleGatingTests` (285 pass); full `tests/Spaarke.ArchTests` (1045 pass).

ADR-038 B3 note: the DI test is a gating-contract test (fails if the registration is wrapped in a feature flag; precedent `AnalysisServicesModuleGatingTests`) required by the task's acceptance criterion, not a bare "resolves" test. It is the one place this change leans on a B3-adjacent pattern; drop it if a structural guard replaces it.

## 6. Review (code-review + adr-check + one adversarial pass)

An independent adversarial agent could not be launched (concurrent sub-agent cap); the adversarial pass was done by the executor against the contract: mutation checks of the tests (matrix opened, DI removed, versions asserted exactly), the conditional-write primitive (`DataverseWebApiService.UpdateRecordFieldsIfUnchangedAsync`: 412 -> `DBConcurrencyException`, 404 -> `KeyNotFoundException`), `ConvertJsonElementToObject` (numbers come back as `long`; parsed with `long.TryParse` on `ToString()`), and the sprk_event status precedent for the paired PATCH. The main session may still want an independent read.

- **F2 fixed:** enum `InvoiceStatus` clashed in name with the retired `FinanceBaseValues.InvoiceStatus` column constant -> renamed `InvoiceLifecycleStatus` (commit 2).
- **ADR-010:** warning, resolved by the justified ceiling raise (Path A-style documentation at the point of decision; no ADR text changes).
- **ADR-038 B3:** see section 5. Warning, kept deliberately, disclosed.
- **K-class:** (K2) after three consecutive conflicts the call returns `concurrent_change` rather than throwing, which a caller must map to a retryable response; (K4) null-argument ctor guards follow `InvoiceReviewService` idiom though NRT makes them redundant; (K2) the first live exercise of the paired statecode+statuscode PATCH on `sprk_invoice` will be task 103 (not exercised against dev here: it would change real dev invoices that cannot be moved back by the service).
- **F1 found in passing, out of this task's scope, filed:** https://github.com/spaarke-dev/spaarke/issues/1957 — status can be changed outside the service via the OOB Activate / Deactivate commands (Paid -> In Review via Activate) and direct Web API writes; ADR-002 forbids a plugin, so it needs an owner decision (ribbon removal, field security, reconciliation job). Not yet in `defer-issues.md` (parallel mode: main session to add ISS entry).
- **Class search (Step 9.5 rule 6):** Grep for other writers of invoice `statecode` / `statuscode` in `src/server/api/Sprk.Bff.Api/Services/Finance` and `Api/Finance`: none. The mechanical "only writer" ArchTest is deferred to 015 as the POML says.

rework: fix-round-1 task 014
