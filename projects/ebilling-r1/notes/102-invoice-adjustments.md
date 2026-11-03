# Task 102 - Invoice-level adjustments (FR-32)

Branch `task/ebill-102-adjustments` (from work/ebilling-r1 e2ed5e55c1). Not merged.

## 1. What was built

| Surface | Detail |
|---|---|
| `GET /api/finance/invoices/{id}/adjustment-rules` | Rule options a reviewer may cite: published billing rules of the applicable rule sets (client default + the invoice firm's) in force on the invoice date (D-74). No document link. Fails closed (unknown rule type, no date, load fault = empty or 503). `finance.read` |
| `POST .../adjustments` | `{adjustmentId, method: percent\|fixed, value, reason, ruleId?}`. Percent in (0,100] applies to fees after line adjustments (D-42); fixed in (0, 1e9]; two decimals; reason mandatory. Server computes the amount, writes one Adjustment line, then 016 `RecalculateAsync` stores the totals (D-130). Approved total below zero (billed >= 0) = 400 `ebilling.adjustment.negative_total`, nothing written. `ebilling.invoice.decide` |
| `DELETE .../adjustments/{lineId}` | Deactivates the Adjustment line (statecode 1; NFR-09, never deleted), row-version conditional, then recalculates. |
| 016 / 100 / billing reads | `InvoiceTotalsService` and 100's `LineQuery` leave deactivated Adjustment lines out (`ExcludeInactiveAdjustments`, `IsCounted`); `BillingFactLoader` prior lines likewise. Deactivated Fee and Expense lines are still counted. |

Files: `Services/Finance/InvoiceAdjustmentService.cs`, `Api/Finance/InvoiceAdjustmentEndpoints.cs`; registration lines in `FinanceModule.cs` (one) and `EndpointMappingExtensions.cs` (one); ArchTests ledgers (route GovernedFile, `ExpectedEndpointFileCount` 143 -> 144, census UnscannedWriter entry).

## 2. Component justification (root section 11)

| Surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `InvoiceAdjustmentService` | `InvoiceLineDecisionService` (line writes), `InvoiceTotalsService` (totals) | Line decisions change the status of existing lines; an adjustment is a new line with its own validation, rule options and idempotence, so it is separate. It reuses the lock, id hashing, ownership and totals services. | FR-32 has no write path. |
| `InvoiceAdjustmentEndpoints` | `InvoiceLineReviewEndpoints` (same group, filters, `Refused`) | A separate file keeps 100's routes untouched while 101 and 103 edit the same area. | Same. |
| Edits to 016 / 100 / `BillingFactLoader` | The POML escalation trigger: 016 counted inactive Adjustment lines | Fixed in place (a filter), not a second totals path (I-1). | A removed adjustment would still reduce every total. |

Placement (section 10): `Sprk.Bff.Api` Services/Finance + Api/Finance beside tasks 100 and 016; no new package, no background work.

## 3. Behaviour and decisions

- Idempotent: the line id is a deterministic hash of (invoice, client adjustmentId). A repeat with the same content finishes the totals (`alreadyApplied`); the same id with other content, or after removal, is 409 `id_reused`.
- Lock: the per-invoice processing lock is shared with line decisions (`ebilling-line-decision:`), 2 minute lease. No Decision Record (D-75 is per invoice decision; an adjustment is not one).
- Ownership: the new row asks `IRecordOwnershipResolver.ResolveOwnerAsync(RecordOwnershipContext.ForParents(...))` over the invoice's one core record (secure-if-any), applied with `owner.ApplyTo(row)`; creator via `RecordCreatorPerson.Stamp`; core lookup column from `InvoiceFamilyCoreRecordGuard.CoreRecordLookups`; 013 guard checked before the write. It is a ForParents create (no ForRow).
- Post-write steps (create, the totals step, the remove's recalculation) run with `CancellationToken.None` so a cancelled request cannot leave a committed line without its totals (review F2).
- If the totals step refuses after the create (another writer lowered the billed total), the new line is deactivated.
- D-129: a credit memo (billed < 0) has no FR-32 floor from this task; the percent base is clamped at 0.

## 4. Seeding proof and fail-before

- Before-state proofs (mutations, then restored): 016 `IsCounted` + both query filters removed = 4 of the adjustment/totals/review-read tests fail; `BillingFactLoader` filter removed = `APriorsRemovedAdjustment_...` fails; `LineQuery` filter removed = `TheLineReadExcludesOnlyDeactivatedAdjustmentLines_Task102` fails; old service vs the three review-fix tests = 3 fail.
- Fail-closed rule types: unknown type, system type and ontology-domain rules are seeded and must be absent from the options.

## 5. Review (step 9.5)

adversarial-reviewer (one pass): F2 cancelled request skipped totals, F1 remove said "saved", F4 OpenAPI statuses, F3 contract test seeded no row - all fixed (commit "task 102 review"). Known limits:

- K1: the In Review check precedes the write and nothing that changes invoice status takes the line-decision lock; an add or remove can land after approval. Same window as 100's line decisions. Task 103 (approve) should take the same lock (`InvoiceLineDecisionService.LockKeyPrefix`) or the add should re-check status after the write.
- K2: if the post-create recalculation refuses and the undo also fails, a repeat returns 400 while the active line stays; DELETE recovers it.
- K3: the lock lease is not renewed; totals stay consistent through recalculate-then-undo.
- K4: a new adjustment can reuse a removed adjustment's `sprk_linesequence`.
- K5: adjustment lines are stamped Accepted, so the read model's Accepted count includes them; the UI filters by cost type.
- Filed, not fixed (pre-existing, 100): `InvoiceLineDecisionService.cs:542,561` also pass the request token after the first write.

adr-check: ADR-001 (Minimal API), ADR-002 (no plugin), ADR-008 (endpoint filters), ADR-010 (concrete service, ArchTests pass), ADR-019 (stable problem codes), ADR-034 (inheritance, caller-scoped authorization), ADR-038 (tests): no violation.

## 6. Gates, publish size, packages

See the task report for counts. `dotnet list package --vulnerable --include-transitive`: none.
Publish size (linux-x64, framework-dependent, zip Optimal): base (work tip e2ed5e55c1, which this branch starts from) 40,586,201 bytes; this branch 40,609,993 bytes; delta +23,792 bytes (+0.02 MB). The baseline is the branch base rather than a fresh master build, so the delta isolates this task; total is about 38.7 MB, under the 55 MB review line.
