# 095c: Submit Invoice from email with no firm (escalated before implementation)

> Status 2026-11-03: **stopped at step 1, under the POML escalation trigger and the run brief.** No code changed. No live write made. This note records the alternate-key finding, the blocker it uncovered, the options, the read-only evidence, the verification script for the main session to run after the owner's OK, and the draft contract delta (not sent).

## 1. What was asked

Mode New on `POST /api/finance/invoice-submissions/from-email` accepts `firmId` null or absent. The submission row is written with `sprk_vendororg` null, the tracking number is `{firmInvoiceNumber}.{n}` in a no-firm sequence, and "the alternate key must still guard uniqueness" (POML, tracking-number constraint). The run brief adds: assume the alternate key does not enforce uniqueness when a key column is null, so the allocator must guarantee no-firm uniqueness race-safely; if no race-safe design exists without a schema change, stop and report the options.

## 2. Findings (read-only, spaarkedev1, 2026-11-03)

### 2.1 The alternate key

`GET EntityDefinitions(LogicalName='sprk_invoicesubmission')/Keys`:

| Key | Columns | Index status | Managed |
|---|---|---|---|
| `sprk_invoicesubmission_firm_trackingnumber` | `sprk_trackingnumber`, `sprk_vendororg` | Active | No |

This is the only key on the table.

### 2.2 The blocker: `sprk_vendororg` is SystemRequired

`GET .../Attributes` (custom columns that are not None):

| Column | Required level |
|---|---|
| `sprk_vendororg` | **SystemRequired** |
| `sprk_trackingnumber` | SystemRequired |
| `sprk_name` | ApplicationRequired |

SystemRequired is enforced by the platform on every create and update. This was measured live in task 021 (`notes/021-invoiceapproval-schema.md` deviation 1): an ApplicationRequired column accepted a null on an API write, and a SystemRequired one refused it. Task 020 set this level on purpose, and its script gives the reason (`scripts/ebilling/Deploy-EbillingSubmissionSchema.ps1` step 4b):

> A row without a firm or a tracking number is not covered by the key (two rows could then carry the same tracking number) and D-70 scopes external access by firm, so both are SystemRequired: the platform refuses a null on every write.

**So a submission row with no firm cannot be written today, by any design.** Before any allocator logic matters, the POML's "the row is written with `sprk_vendororg` null" needs a schema change: lowering `sprk_vendororg` on `sprk_invoicesubmission` from SystemRequired. That is a key-column metadata change, which needs the owner's OK and notices to peers (POML escalation; project CLAUDE.md §5). It is also the class of change that task 020's own reasoning rejected.

For comparison: `sprk_invoice.sprk_vendororg` is None (an invoice row with no firm can be written), and `sprk_billingevent.sprk_vendororg` is ApplicationRequired (not enforced on API writes, DEF-009).

### 2.3 How the key treats a null (research)

- Microsoft Learn, "Work with alternate keys" (updated 2026-03-30, https://learn.microsoft.com/power-apps/developer/data-platform/define-alternate-keys-entity), says that alternate keys "use database indexes to enforce uniqueness". **It says nothing about null key values.**
- Community sources agree that a row with a null in any key column is not covered. The Packt *Microsoft Dynamics 365 Extensions Cookbook* ("Duplicate detection using alternate keys", https://packtpub.com/en-gb/product/microsoft-dynamics-365-extensions-cookbook-9781786464170/chapter/no-code-extensions-1/section/duplicate-detection-using-alternate-keys-ch01lvl1sec11) shows the generated SQL: a unique nonclustered index with `WHERE <col> IS NOT NULL AND ...` on every key column, and says that an empty field "will not be flagged as a duplicate". https://dev.to/sapotacorp/alternate-keys-in-dataverse-when-they-help-vs-hurt-3d6n and https://www.hubsite365.com/en-ww/crm-pages/dataverse-alternate-keys-explained-integration-upsert-duplicate-prevention.htm say the same: nulls repeat, and keys apply only to non-null values.
- Task 020's comment (2.2) records the same assumption for this table.

Conclusion: **assume the key does not cover a row whose `sprk_vendororg` is null.** Two no-firm rows with the same tracking number would both be accepted. The script in section 4 proves this on dev once the column can hold a null. It cannot be proved today, because the platform refuses the null first (2.2).

## 3. Options (owner decision; none is implemented)

Each option keeps the external SPA and internal wizard routes requiring the firm, and each keeps a firm that is sent checked as today.

| | A. Lower the column; the allocator guards uniqueness in code | B. Lower the column, plus a firm-scope column and a new key (recommended) | C. A sentinel "No firm" organization |
|---|---|---|---|
| Schema | `sprk_vendororg` SystemRequired to None on `sprk_invoicesubmission` | As A, plus a new text column `sprk_trackingscope` (SystemRequired; the firm id, or the literal `none`), backfilled on existing rows, and a new key on (`sprk_trackingnumber`, `sprk_trackingscope`) | None. One data row per environment (the sentinel organization) and a setting naming it |
| Who guards uniqueness | Allocator code (protocol below) | The platform index, as for firms today; the allocator stays as it is, apart from writing the scope | The existing key, natively |
| Meets "the row is written with `sprk_vendororg` null" | Yes | Yes | **No**: the row names the sentinel |
| Meets "the alternate key must still guard uniqueness" | **No** (code guards it) | Yes | Yes |
| Fail-closed risk | The protocol must be right; its proof is a concurrency test, not the platform | Low: every reader sees a true null, and the key is the platform's | **High**: every reader that does not know about the sentinel treats it as a real firm (rule sets, AP vendor, `IsActiveFirmAsync`, the external `In callerFirmIds` filter if a contact is ever linked to it). The POML forbids any reader treating "no firm" as a match, and a sentinel invites exactly that |
| Other costs | Notices (uac-r2: D-70; ontology-r1) | As A, plus a key build (index job, on a table with rows), a backfill script, SpaarkeMaster entries, and a provisioning step for stamps | Seeding on every stamp; a config key; a guard in every reader |

**Recommendation: B.** It is the only option that meets both POML constraints, and it keeps uniqueness on the strongest mechanism available, the platform index (root CLAUDE.md §16 enforcement ladder). A is acceptable if the owner wants one schema change instead of two. C is not recommended.

**A's protocol** (race-safe without a further schema change, for the record). A no-firm row is created **tentative**, with its tracking number set and `sprk_submissionsequence` null. Tentative rows are invisible to the sequence read, which filters on a non-null sequence. Then:

1. The allocator queries for any other row with a null firm holding the same tracking number (case-insensitive, any state).
2. If there is none, it **confirms** the row by setting the sequence and returns.
3. If there is one, it **yields**: it moves its own row to the next number and repeats from step 1.

A confirmed row never yields. Safety follows from write-then-read. A row confirms only if its read, which comes after its own write committed, saw no other holder. For two rows holding the same number, one read must come after both writes committed, so at most one of them confirms. A redelivered tentative row (one that crashed between its create and its confirm) resumes at step 1, and a confirmed row is returned as `AlreadyCreated`. If the platform does treat a null as a value after all, the existing alternate-key retry still applies, so the design is correct under either behaviour. Liveness: two tentative rows that see each other both yield. This is bounded by `MaxAttempts` and ends in the existing retryable 503 `ebilling.tracking.unavailable`. Known limit K2: a tentative duplicate is visible to internal readers for one round trip.

## 4. Verification script (for the main session; run only after the owner's OK)

**Part 1** proves the blocker today with one insert that is expected to be refused. **Part 2** runs only after the owner has lowered `sprk_vendororg` (option A or B). It shows how the existing key treats a null, using two inserts with the same tracking number and no firm. Both parts clean up by id, and the cleanup runs even if a step fails. The tracking numbers carry a unique run tag, so nothing collides with real data.

```powershell
# 095c alternate-key verification (spaarkedev1). Owner OK required before running. Part 2 only after sprk_vendororg is lowered.
param([switch]$Part2)
$ErrorActionPreference = 'Stop'
$org = 'https://spaarkedev1.crm.dynamics.com'
$api = "$org/api/data/v9.2"
$tok = az account get-access-token --resource $org --query accessToken -o tsv
$h = @{ Authorization = "Bearer $tok"; Accept = 'application/json'; 'Content-Type' = 'application/json'; Prefer = 'return=representation' }
$tag = 'VERIFY-095C-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$created = [System.Collections.Generic.List[string]]::new()

# Read-only: the key and the required level (expect the key Active; the level SystemRequired before Part 2).
Invoke-RestMethod -Headers $h -Uri "$api/EntityDefinitions(LogicalName='sprk_invoicesubmission')/Keys?`$select=LogicalName,KeyAttributes,EntityKeyIndexStatus" |
    Select-Object -ExpandProperty value | Format-Table LogicalName, KeyAttributes, EntityKeyIndexStatus
$lvl = (Invoke-RestMethod -Headers $h -Uri "$api/EntityDefinitions(LogicalName='sprk_invoicesubmission')/Attributes(LogicalName='sprk_vendororg')?`$select=RequiredLevel").RequiredLevel.Value
"sprk_vendororg RequiredLevel = $lvl"

function New-NoFirmRow([string]$label) {
    $body = @{ sprk_name = "$tag $label"; sprk_trackingnumber = "$tag.1" } | ConvertTo-Json
    try {
        $r = Invoke-RestMethod -Method Post -Headers $h -Uri "$api/sprk_invoicesubmissions" -Body $body
        $created.Add($r.sprk_invoicesubmissionid); "$label : CREATED $($r.sprk_invoicesubmissionid)"
    } catch {
        "$label : REFUSED $($_.ErrorDetails.Message)"
    }
}

try {
    if (-not $Part2) {
        # Part 1: expect REFUSED (SystemRequired on sprk_vendororg).
        New-NoFirmRow 'part1-insert'
    } else {
        if ($lvl -eq 'SystemRequired') { throw 'Part 2 needs sprk_vendororg lowered first (owner OK, option A or B).' }
        # Part 2: two no-firm rows, same tracking number. Expected under the research in section 2.3: both CREATED (the key ignores a null).
        # If the second is REFUSED with 0x80060892, the key treats null as a value.
        New-NoFirmRow 'part2-insert-1'
        New-NoFirmRow 'part2-insert-2'
    }
} finally {
    # Cleanup: delete every row this run created, by id; then confirm none is left under the tag.
    foreach ($id in $created) { Invoke-RestMethod -Method Delete -Headers $h -Uri "$api/sprk_invoicesubmissions($id)"; "deleted $id" }
    $left = Invoke-RestMethod -Headers $h -Uri "$api/sprk_invoicesubmissions?`$select=sprk_invoicesubmissionid&`$filter=startswith(sprk_trackingnumber,'$tag')"
    "rows left under $tag : $($left.value.Count)"
}
```

Record the output here (2.3) when it is run. Part 1's expected result is `REFUSED` with the platform's required-field fault. Part 2's expected result is two `CREATED` rows. If Part 2 refuses the second insert with `0x80060892`, option A's code guard is still correct but is not needed, and B's new key is not needed either: only the required-level change remains.

## 5. Draft contract delta for word-add-in-r1 (not sent; the main session sends it once the owner picks an option)

Section 2, firm fields, the `firmId` row: "`firmId`: GUID or null. **Optional for `mode: New`** (owner 2026-11-03, EB-D-185): absent or null submits the invoice without a firm. When it is sent, it is checked as today (`firm_invalid`, `firm_not_linked`, the 403). For `Correction`, a sent `firmId` must equal the replaced invoice's firm. A correction of a no-firm invoice sends no firm; one that sends a firm is refused 409 `correction_not_allowed` reason `different_firm`."

Section 4: "In the `X-Idempotency-Key` binding, a null or absent `firmId` binds as an empty value, so a retry with `firmId` null or absent is the same request. K is unchanged."

Section 9, a new row, Accepted:

| status | outcomeReasonCode | Meaning |
|---|---|---|
| Accepted | `ebilling.submission.firm_missing` | Added 2026-11-03 (095c). The invoice was accepted with no firm and is held until a reviewer sets one. It cannot be approved until then. This **is a hold**. The pane shows: **"Submitted without a firm. A reviewer will add the firm before approval."** |

The 202 body is unchanged. The pane reads the code from `GET statusUrl` (or from section 11's read) once the job has finished, exactly as for any other hold.

## 6. Preliminary reader inventory (for the re-run; grep `sprk_vendororg|VendorOrg` in `Sprk.Bff.Api`, 24 files)

The POML's list covers most of these. Five files carry the firm and are **not on the POML's list**: `InvoiceAdjustmentService.cs` (5 hits), `InvoiceCorrectionService.cs` (2), `InvoiceCorrectionReevaluation.cs` (2) and the job handlers `InvoiceExtractionJobHandler.cs` (2) and `AttachmentClassificationJobHandler.cs` (1). The re-run must show each one safe with a null firm. `ebilling.submission.firm_missing` already exists in `SubmissionReasonCodes.cs:73`, and `SubmissionEvaluationOutcome.cs:143` returns it as a rejection today. D-201 places the new system finding under `BILL-SYSTEM-SUBMISSION` in `Services/Signals/Billing`.

## 7. Next step

The owner picks A, B or C (recommendation B). Then:

1. Run section 4 Part 1 (owner OK).
2. Make the schema change through an idempotent script under `scripts/ebilling/`, with notices to uac-r2 (D-70 rationale), ontology-r1 and SPA-r3 before and after.
3. Run Part 2.
4. Re-run 095c against this note.

The D-106 amendment (EB-D-185) is drafted in section 5 and goes into the decision log when the owner confirms the option.

> Section 7 is history: the owner chose B (2026-11-03) and the main session approved the phase probe and the script order (POML constraint "main-session design 2026-11-03"). Sections 8 and 9 record part 095c-1.

## 8. Part 095c-1: schema script, SpaarkeMaster source, allocator and phase probe (2026-11-03)

No live write was made; the schema script was not run against any environment. Its `-WhatIf` and `-Verify` (both read only) were run against spaarkedev1 (8.6).

### 8.1 What changed

| Item | File | What |
|---|---|---|
| Schema script | `scripts/ebilling/Deploy-EbillingSubmissionTrackingScopeSchema.ps1` | The 9-step order below, idempotent, `-WhatIf`, `-Verify`, `-KeepFirmKey` (stop before step 9), `-CacheWaitSeconds` (301 to 3600, default 330). Escalation (exit 2) on a conflicting column or key, a scope that differs from its row's firm, a duplicated (tracking number, scope) pair, or a Failed key index. |
| Solution source | `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_invoicesubmission/Entity.xml` | `sprk_vendororg` RequiredLevel `none`; new `sprk_TrackingScope` (nvarchar 36, `systemrequired`, audited, not secured; `IntroducedVersion` 1.2.4.0); the key `sprk_invoicesubmission_scope_trackingnumber` on (`sprk_trackingnumber`, `sprk_trackingscope`; 1.2.4.0) is added **next to** `sprk_invoicesubmission_firm_trackingnumber`, which stays in source (8.8; review fix F1). |
| Allocator | `Services/Finance/Submission/TrackingNumberAllocator.cs`, `ITrackingNumberAllocator.cs` | `CreateSubmissionAsync(Guid submissionId, Guid? firmId, ...)`. `Guid.Empty` still throws; null means "no firm". New reason `ebilling.tracking.firm_required`. The scope column is owned (a caller's `fields` cannot name it). |
| Phase probe | `Services/Finance/Submission/TrackingScopePhase.cs` (new) | `TrackingScopePhase` (P0, P1, P2); `TrackingScopePhaseProbe` (a `RetrieveEntityRequest` with `EntityFilters.Entity | Attributes` over `TryUnwrapServiceClient`, the path `ComposeIdentityKeyHealthCheck` uses; published metadata); `TrackingScopePhaseCache` (process-wide static, forward-only, 5 minutes until P2). No DI registration: the allocator's public constructor now also takes `IDataverseService` (already a singleton). |
| Generic-write refusal | `Services/Dataverse/ParentLineage.cs` (`InheritedAccessPermission.AccessColumns`) | `sprk_trackingscope` joins the submission's refused columns: an Update Record node or output mapping running app-only cannot set it. The allocator is its typed writer. |
| Callers | `InvoiceSubmissionService`, `SubmitInvoiceFromEmailOperation` | Unchanged: they pass a `Guid`, which converts to `Guid?`. No intake HTTP or service code changed (095c-2). |

### 8.2 The phases (what the allocator does)

| Phase | Read when | Counts per | Writes | No firm |
|---|---|---|---|---|
| P0 | `sprk_trackingscope` is not in the metadata, or no read has succeeded yet | firm (`sprk_vendororg`) | firm; no scope | refused `ebilling.tracking.firm_required`, nothing read or written |
| P1 | the column exists, and no key named `sprk_invoicesubmission_scope_trackingnumber` on exactly (`sprk_trackingnumber`, `sprk_trackingscope`) is Active | firm | firm and scope (firm id, D form) | refused, as P0 |
| P2 | that key is Active | scope (`sprk_trackingscope`) | firm (when there is one) and scope (firm id, or `none`) | numbered `{firmInvoiceNumber}.{n}` in the `none` scope; no firm written |

- The phase is read on the first allocation and re-read at most every 5 minutes until it is P2; after P2 it is never read again. It never moves backward: a lower or unreadable read keeps the cached phase. An unreadable read (no ready ServiceClient, a fault) logs a warning and counts as a read for the 5-minute window.
- The stale case: an instance that holds P0 while the column is SystemRequired gets `0x80040200` ("cannot be NULL") on its create, which writes nothing. It forces one re-read: if the phase moved, it retries in the new phase (counted per firm in P1, so the number is the same one the firm key guards); if it still reads P0, it returns `ebilling.tracking.unavailable` (the intake's existing retryable 503). A `0x80040200` outside P0 propagates as before.
- Detection is a metadata read on first use, not a setting: nothing has to be changed by hand on each environment, and a stamp provisioned from SpaarkeMaster reaches P2 by itself once its key index is Active.

### 8.3 The script order and why it is safe

1. Lower `sprk_vendororg` to None. Nothing writes a null firm: the allocator refuses one before P2.
2. Add `sprk_trackingscope` at None (published at once, entity scope). Instances move to P1 within 5 minutes and write the scope.
3. Wait `-CacheWaitSeconds` (more than the 300 s cache). Skipped when the column is already SystemRequired.
4. Backfill every row whose scope is null: its firm id in D form (lower case, as `Guid.ToString("D")`), or `none` with no firm. The null set is re-queried until it is empty (a shrinking filtered set is not paged by nextLink).
5. Raise the column to SystemRequired. The module's `Set-ColumnRequiredLevel` refuses while any row holds a null; the script backfills and retries (3 rounds).
6. Re-backfill (a stale instance can write between 4 and 5), then verify every row: no null scope, scope equals its row's firm (or `none`), no duplicated (tracking number, scope) pair. Any problem is an escalation: the key is not created.
7. Add the scope key; wait for Active (`Add-AlternateKey`, 400 s, Failed stops with exit 2).
8. Verify: the key Active, the column SystemRequired, `sprk_vendororg` None, no null scope.
9. Re-read the scope key as Active, then delete the firm key (by MetadataId) and wait until it is gone. The only destructive step (owner OK).

Uniqueness at every moment: until step 7 every row has a firm and the firm key guards it. From step 7 both keys stand. After step 9 the scope key alone guards every row, because from step 5 no row can be written without a scope, and every row's scope is its firm (step 6), so a firm's numbers are unique in its scope exactly as they were per firm. The POML escalation trigger ("the old key retired only after the new one is Active") does not fire.

### 8.4 Component justification (CLAUDE.md section 11)

- **`ebilling.tracking.firm_required`.** Existing: `ebilling.tracking.unavailable`. Extension: that code means "retry later", and a no-firm request before P2 must not be retried (it cannot succeed until the script runs); a separate stable code lets 095c-2 answer 400 `firm_invalid` "A firm is required." Cost of doing nothing: a no-firm create before P2 would either be written unguarded (a duplicate number is possible, notes 2.3) or loop on 503.
- **`TrackingScopePhase.cs` (enum, probe, cache).** Existing: `ComposeIdentityKeyHealthCheck` (a key-status probe for another table, a health check). Extension: its probe is `protected virtual` on a hosted service and reports health; the allocator needs a cached, forward-only phase with a forced re-read, so the request shape is reused, not the class; refactoring the Compose check is out of scope. Cost of doing nothing: the intake cannot tell P0 from P2, so it either refuses no-firm requests forever or accepts them before the key guards them.
- **`sprk_trackingscope` and the scope key.** Existing: the firm key. Extension: it cannot cover a null firm (notes 2.3); option B was chosen by the owner. Cost of doing nothing: no-firm uniqueness rests on code (option A), which the POML rules out.
- **No new DI registration, no new package, no new endpoint.**

### 8.5 Tests

| Test | Where | Proves |
|---|---|---|
| `P0_AFirmSubmission_IsNumberedPerFirm_AndWritesNoScope` and every existing allocator test (unchanged, running in P0) | `tests/integration/data-mutation/FinanceSubmission/TrackingNumberAllocatorTests.cs` | P0 is today's behaviour |
| `BeforeP2_ASubmissionWithNoFirm_IsRefusedAsFirmRequired_AndNothingIsReadOrWritten` (P0, P1) | same | no unguarded no-firm row |
| `P1_AFirmSubmission_WritesItsFirmAsTheScope_AndStillCountsPerFirm` | same | P1 writes the scope, counts a row the backfill has not reached |
| `P2_AFirmSubmission_IsCountedPerScope_...`, `P2_ASubmissionWithNoFirm_IsNumberedInTheNoFirmScope_AndHasNoFirm`, `P2_ARedeliveredNoFirmCreate_...` | same | P2 counting and writing; idempotency with a null firm |
| `AStaleP0Instance_..._ReReadsThePhaseOnce_AndRetriesWithTheScope`, `AStaleP0Instance_WhosePhaseStillReadsP0_AnswersTheRetryableRefusal_...`, `ARequiredColumnRefusalOutsideP0_Propagates_...` | same | steps 3 to 5: a retryable refusal, never a 500, never a duplicate |
| `TenParallelCreatesWithNoFirm_AllGetADistinctNumberInTheNoFirmScope` | same | the race test with a null firm: .1 to .10, the scope key refusing the losers while the firm key (still Active) ignores them |
| `TrackingScopePhaseTests` (9 cases) | `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Submission/` | the probe (P0, P1 for a key Pending / InProgress / Failed / on other columns, P2) and the cache (5 minutes, forward-only, forced read, never after P2) |
| `AnAppOnlyWriteOfTheSubmissionsTrackingScope_IsRefused_...` + the column in the per-table and reader lists | `tests/integration/data-mutation/ChildAccessPermission/AccessTableAppOnlyWriterTests.cs` | the generic-write refusal, with must-not-fire controls |
| `SubmissionTrackingScopeNotAccessGuardTests` (3) | `tests/Spaarke.ArchTests/` | only the allocator, the probe and the refusal list name the column; `InvoiceSubmissionService` (ListAsync), `ExternalInvoiceSubmissionAuthorizationFilter` (DecideReadOneAsync) and `ExternalInvoiceSubmissionEndpoints` (ReplacementGate) never do; controls fire and do not fire |

Review round 1 (see 10) adds: `BeforeP2_ARedeliveredNoFirmCreate_ThatAP2InstanceAlreadyNumbered_ReturnsThatRow_NotFirmRequired` (P0, P1), `AP1InstanceAndAP2Instance_NumberingOneFirmAndNumberTogether_NeverShareANumber_AndNeitherFaults` (both keys present; firm key retired), `TwoForcedReReads_QueuedBehindEachOther_DoOneMetadataRead_...` and its must-not-fire control, and tightened P1 and P2 tests (asserted create and read counts; a firm-A row filed in the `none` scope).

The in-memory table now models both keys (each ignores a row whose key column is null), the firm key's retirement and the scope column at SystemRequired (`0x80040200`).

Fail-before proofs (mutations of the new source, each restored byte-identical, SHA-256 checked): see 8.7.

### 8.6 Read-only runs against spaarkedev1

`-WhatIf`: pre-flight shows the firm key Active, no scope key, `sprk_vendororg` SystemRequired, no scope column, 10 rows (none without a firm), phase P0; then every step reports what it would do (lower, create, wait 330 s, backfill all rows, raise, re-backfill, create the key, retire the firm key). Nothing written.

`-Verify`: exit 1 with four gaps (step 1 `sprk_vendororg` not None; step 2 no column; step 7 no key; step 9 the firm key present), phase P0.

### 8.7 Fail-before proofs

Each mutation was applied to the new source, the suite run, and the file restored (SHA-256 of all four files checked OK afterwards).

| Mutation | Suite | Failed |
|---|---|---|
| no scope written in P1/P2 | unit (977) | 5 |
| P2 counts per firm, not per scope | unit | 2 |
| no-firm not refused before P2 | unit | 2 |
| no stale-P0 catch (0x80040200 propagates) | unit | 2 |
| the cache takes a lower phase | unit | 1 |
| no cache (read every call) | unit | 1 |
| P2 for a key in any status | unit | 3 |
| `sprk_trackingscope` removed from `AccessColumns` | ArchTests (3) / unit | 1 / 7 |
| the replacement gate names the scope | ArchTests (3) | 2 |

Review round 1 (10), same method (mutate the source, run, restore from a copy):

| Mutation / old code | Suite | Failed (test) |
|---|---|---|
| existing-row check after the `firm_required` refusal (the original order, `git checkout` of the committed file) | allocator tests (data-mutation) | 2 (`BeforeP2_ARedeliveredNoFirmCreate_..._NotFirmRequired`, P0 and P1) |
| the refusal text of the original `ParentLineage.cs` ("decides who may see what") | `AccessTableAppOnlyWriterTests` | 1 (`AnAppOnlyWriteOfTheSubmissionsTrackingScope_...`) |
| P1 counts per scope instead of per firm (`AddSequenceScope`: `phase != P0`) | allocator tests | 1 (`P1_AFirmSubmission_WritesItsFirmAsTheScope_AndStillCountsPerFirm`) |
| P2 counts per firm instead of per scope (`AddSequenceScope`: scope only for a null firm) | allocator tests | 1 (`P2_AFirmSubmission_IsCountedPerScope_...`); the old P2 test passed this mutation, which is why the firm-A row in the `none` scope was added |
| the forced re-read made even when a read started after the caller's refusal (skip condition removed) | `TrackingScopePhaseTests` | 1 (`TwoForcedReReads_QueuedBehindEachOther_...`) |

### 8.8 Provisioning (stamps)

**Source keeps both keys (review fix F1).** `SpaarkeMaster` carries the scope column SystemRequired, `sprk_vendororg` None, the scope key **and** the firm key. Retiring the firm key from source is a later release, done only after every environment that has submission rows has run the schema script through step 9 (steps 1 to 8 retire nothing; step 9 deletes the firm key by hand).

Why both stay: a managed upgrade (`StageAndUpgradeAsync`, `DataverseWebApiSolutionImporter`) deletes components that are present in the installed solution and missing from the new one. With the firm key dropped from source, an upgrade of an environment that has rows would delete the firm key at once. The new scope key then builds over the non-null rows only (the column is new, so every existing row is null) and reads Active, so the BFF reads P2 while no key guards the existing rows: P2 would number a firm's submission from a count of scoped rows only and issue a number a pre-upgrade row already holds. With both keys in source, the upgrade leaves the firm key where it is, and the firm key keeps refusing a duplicate until the script's backfill and step 9.

**A stamp** (no rows) imports this and has both keys. It needs no backfill and no script; until the scope key index is Active (seconds on an empty table) instances read P1 (count per firm, write the scope). Both keys guard a firm row; the firm key is retired from source by the later release, which also removes the step-9 deletion from the script's required path. Nothing else is needed from customer-provisioning-orchestration-r1 beyond the notice in 9.

**An environment that already has rows** must run the script (steps 1 to 8, then 9 with the owner's OK) and must not rely on the import to do it. Importing this SpaarkeMaster version before the script has run adds the column as SystemRequired with null rows; the firm key stays (both keys in source), the scope key builds over the null-free subset and reads Active (P2 for an instance), and the script's steps 4 and 6 backfill and verify the rows. Until the backfill, a P2 instance counts the scope's rows only, so it can pick a number a null-scope row holds: the firm key refuses that write (the row has a firm), the allocator retries past it (or, past ten such numbers for one invoice number, answers the retryable 503), so the number is still never duplicated; a no-firm submission is the case that is unguarded, and it exists only after P2 is read, so run the script immediately after such an import.

### 8.9 Deviations and known limits

- **Deviation: the probe is a copy of the Compose request shape, not a call into `ComposeIdentityKeyHealthCheck`.** Its fetch is a `protected virtual` member of a hosted health check for another table; making it shared would change the Compose check (out of scope).
- **Deviation: a null firm before P2 is a refusal (`firm_required`), not an exception.** The allocator is the single owner of the number, so it holds the rule as defence in depth; 095c-2 maps the code to the 400 and decides whether the intake checks the phase earlier.
- **Publish-size delta and CVE check:** not measured in this part (no package added, three small source files). 095c-4's full gate measures it against a fresh master build.
- **K1, constraint on 095d (assign-firm).** Assigning a firm to a `none`-scope row (a no-firm from-email submission that a person later files to a firm) without renumbering breaks two things: `Test-ScopeData` (script step 6 and `-Verify`) reads a row whose scope is `none` but whose firm is set as "a scope that is not its firm", and P2 counts per scope, so the number that row holds is not in the firm's scope and P2 can issue the same `{n}` again for that firm. 095d's assign-firm operation must therefore **renumber the row into the firm's scope and set `sprk_trackingscope` to the firm id (D form) in the same write**, through the allocator (the single owner of the number), so the backfill rule "scope = the row's firm, or `none` with no firm" holds at every moment. `Test-ScopeData` stays strict; do not relax it to accept a firm row in `none`. (The allocator tests seed such a row only to prove the count is per scope.)
- K2: the phase never moves backward in a running process. If the scope key were dropped after P2, a running instance would keep counting per scope until restarted. No flow drops it.
- K2: an unreadable metadata read leaves an instance at its cached phase for 5 minutes. In P0 that is today's behaviour, and after step 5 the stale case above applies.
- K4: `sprk_trackingscope` is not field-secured. A user with Write on a submission could edit it natively. It decides no access (the ArchTest above); it is refused to app-only generic writers. Field security would need a profile change and a uac-r2 notice; not asked.

## 9. Notice for uac-r2

For uac-r2 (and ontology-r1, SPA-r3, customer-provisioning-orchestration-r1). Nothing has been run. The main session runs the script with the owner's OK after this notice, and sends it.

**For customer-provisioning-orchestration-r1 (stamps).** A new stamp imports SpaarkeMaster, which now carries `sprk_trackingscope` (SystemRequired), `sprk_vendororg` at None, and both alternate keys (`sprk_invoicesubmission_firm_trackingnumber` and `sprk_invoicesubmission_scope_trackingnumber`; the firm key stays until a later release retires it). A stamp has no rows, so it needs no backfill and no script: the BFF reads its phase from metadata and reaches P2 by itself once the scope key index is Active. An environment that already has submission rows must not be brought up by import alone: it needs `scripts/ebilling/Deploy-EbillingSubmissionTrackingScopeSchema.ps1` (steps 1 to 9) run by the main session with the owner's OK. If an upgrade of an existing environment is ever scripted, it must run that script right after the import.

**What changes on `sprk_invoicesubmission`.**

- `sprk_vendororg` (the firm) goes from SystemRequired to None. Only the internal from-email route, mode New, will ever send a null firm (095c-2). The external SPA route and the internal wizard route keep requiring it.
- A new text column `sprk_trackingscope` (36 characters, SystemRequired from step 5): the firm id in D form, or the literal `none`.
- The key changes. The old key is `sprk_invoicesubmission_firm_trackingnumber` on (`sprk_trackingnumber`, `sprk_vendororg`). The new key is `sprk_invoicesubmission_scope_trackingnumber` on (`sprk_trackingnumber`, `sprk_trackingscope`). The old key is retired only after the new one reads Active (script step 9, owner OK). The solution source keeps both keys; removing the old key from source is a later release, after every environment with rows has run the script.

**Backfill rule.** Each existing row's scope is its `sprk_vendororg` id in lower-case D form. A row with no firm gets `none`; on dev there are no such rows (10 rows, all with a firm). The script verifies every row against this rule before it creates the key, and stops if any row differs.

**Access: nothing changes.**

- The scope is a numbering column, not an access input. External visibility of a submission is still decided by `sprk_vendororg` and the core record: `InvoiceSubmissionService.ListAsync` filters the firm in the caller's firms, and `DecideReadOneAsync` and the external replacement gate need the view's firm in `scope.FirmIds`.
- A no-firm submission therefore stays invisible externally: the list omits it and its single read is the uniform 404.
- `SubmissionTrackingScopeNotAccessGuardTests` (ArchTests) fails if any of those three gates, or any unreviewed type, names `sprk_trackingscope`.
- The column is added to `AccessColumns` for `sprk_invoicesubmission`, so an app-only generic writer is refused. Its only writer is the allocator, at create, plus the script's one backfill.

**Constraint for task 095d (assign a firm to a no-firm submission).** A no-firm row has scope `none`. Assigning it a firm must renumber the row into the firm's scope and set `sprk_trackingscope` to the firm id (D form) in the same write, through the allocator. Otherwise the row reads as "a scope that is not its firm" (the script's data check, which stays strict) and P2, which counts per scope, can issue the same number again for that firm. uac-r2: if your work lets a person set `sprk_vendororg` on an existing submission by any other path, tell the main session, because that path has the same problem.

**Behaviour in each phase.** The BFF reads the phase from metadata and caches it for 5 minutes until P2. The phase only moves forward.

| Phase | When | Firm submission | No-firm submission (from email, New) |
|---|---|---|---|
| P0 | before step 2 | today's behaviour: per firm, no scope written | 400 `ebilling.submission.firm_invalid` "A firm is required." (095c-2); the allocator refuses `firm_required`, nothing written |
| P1 | steps 2 to 6 (column present, new key not Active) | per firm, guarded by the old key; the scope is written | as P0 |
| P2 | from step 7 (new key Active) | per scope, guarded by the new key (and the old key until step 9) | accepted; numbered `{n}` in the `none` scope; guarded by the new key only (the old key ignores a null firm) |

**A stale instance during steps 3 to 5.** An instance may still hold P0 because its cache has not expired or its metadata read failed.

- Until step 5 its writes carry a firm but no scope. The old key guards them, and steps 4 and 6 backfill them.
- From step 5 the platform refuses its create with `0x80040200`, and nothing is written. The allocator then re-reads the phase once:
  - P1 or later: it retries with the scope. The number is counted per firm and the old key guards it.
  - still P0: it returns `ebilling.tracking.unavailable`, which the intake answers as a retryable 503.
- It is never a 500 and never a duplicate number.
- During steps 7 to 9 every instance writes the scope, so retiring the old key leaves no row unguarded.

**Order of the run.** `-WhatIf`, then a run with `-KeepFirmKey` (steps 1 to 8), then `-Verify` (expect only the step-9 gap), then a run without it (step 9, owner OK), then `-Verify` (PASS). Notify uac-r2, ontology-r1 and SPA-r3 before the first write and after the last.

## 10. Review round 1 fixes (2026-11-03)

| Finding | Fix |
|---|---|
| F1 source drops the firm key | `Entity.xml` keeps `sprk_invoicesubmission_firm_trackingnumber` next to the scope key; `IntroducedVersion` of the scope column and key is 1.2.4.0 (the solution version). 8.8 rewritten; 9 gains the customer-provisioning-orchestration-r1 notice. |
| F1 existing-row check first | `TrackingNumberAllocator.CreateSubmissionAsync` runs `FindExistingAsync` / `Reconcile` before the phase read and the `firm_required` refusal. Test: a P0 and a P1 allocator given a no-firm request for an id whose `none`-scope row exists return that row, with no sequence read and no create. |
| F3 P1 and P2 tests prove their names | P1 asserts one create and one sequence read; P2 adds a firm-A row filed in `none` and asserts `.2` with one create and one read. Both mutations (opposite counting rule) fail the matching test (8.7); the old P2 test did not fail its mutation. |
| K3 two phases on one number | `AP1InstanceAndAP2Instance_...` (both keys; firm key retired): two instances with different cached phases allocate on one (firm, number); one takes `.1`, the other retries to `.2`; both Created, never a duplicate or a fault. The in-memory store gained `AfterSequenceRead` to hold both callers after they have each taken an answer (the existing `BeforeSequenceRead` does not make both read before either writes). These two cases pass on the old code too: they are regression coverage, not fail-before proofs. |
| Schema script | (a) entity-scoped `PublishXml` after step 1 and after step 5 when the run changed the column (never `PublishAllXml`); (b) step 9 re-counts rows without a scope next to the Active re-read, non-zero stops with exit 2 before the delete; (c) pre-flight stops with exit 2 on a key over (`sprk_trackingnumber`, `sprk_trackingscope`) under another name (`Get-ScopeKey` now matches by name only; `-Verify` reports such a key as a gap); (d) `$State.SolutionHeader` replaces the unset `$script:SolutionHeader`. |
| K2 one forced re-read at a time | `TrackingScopePhaseCache` counts reads started (`ReadsStarted`); `GetAsync(..., refusedAtRead)` skips a forced read when a read started after the caller's refusal (reads are serial, so it has completed). The allocator takes the count at the refusal. Tests: two queued forced re-reads do one read; a refusal after the last read started still reads (must-not-fire). |
| ParentLineage text | The `sprk_trackingscope` entry of the access-column refusal says "it decides no access; only the tracking number allocator writes it"; other columns keep their text. |
| K1 to 095d | 8.9 and 9: assign-firm renumbers into the firm's scope and sets `sprk_trackingscope` to the firm in one write; `Test-ScopeData` stays strict. |

Read-only runs against spaarkedev1 after the script edits: `-WhatIf` (exit 0) lists pre-flight (firm key Active, no scope key, `sprk_vendororg` SystemRequired, no scope column, 10 rows, none without a firm, phase P0) and the nine steps as would-do (lower the firm to None, create the column, wait 330 s, backfill, raise, re-backfill, create the key, retire the firm key), 2 created / 2 changed, nothing written. `-Verify` (exit 1) lists the same four gaps as before (steps 1, 2, 7, 9). The step-9 recount, the misnamed-key stop and the publishes only run in an apply and were checked by parsing and reading, not run.

Gate: all five test projects build with 0 errors; full `Spaarke.ArchTests` 1931 passed; the filtered `Sprk.Bff.Api.Tests` run 6756 passed, 2 failed (`InvoiceSubmissionByEmailContractTests.Step0Refusal_IsTheUniform404`, both cases: a 095a test defect from `1d5248b181`, see below); the allocator tests 55 of 55 (983 with the phase and access-writer classes).

Found, not fixed (not this change): `Step0Refusal_IsTheUniform404` asserts `_queries.Should().BeEmpty()` after `NoMatchBodyAsync()` has already run the handler and recorded its queries (`tests/integration/contract/Api/Finance/InvoiceSubmissionByEmailContractTests.cs:664-672`). Owner: 095a. The fix is to clear `_queries` after the no-match call.

## 11. Part 095c-2: the intake takes an optional firm (2026-11-03)

### 11.1 What changed

- `InvoiceSubmissionRequest.FirmId`, `FromEmailSubmissionInput.FirmId` and `FromEmailSubmitInput.FirmId` are `Guid?`. Null means no firm. The empty id never means "no firm"; it is 400 `firm_invalid`.
- From-email HTTP: a null or absent `firmId` is no longer a body-shape error. A null firm adds no AppendTo check on the firm, because there is no firm record.
- **Phase gate (section 8.2), before anything is saved or filed.**
  - `FromEmailSubmissionService.CheckFirmAsync` asks `InvoiceSubmissionService.CanSubmitWithoutFirmAsync`. That asks `ITrackingNumberAllocator.NumbersSubmissionsWithoutFirmAsync`, which reads the shared, forward-only phase cache and is true only in P2.
  - In P0 and P1, a no-firm request (New or Correction) is 400 `ebilling.submission.firm_invalid` "A firm is required.".
  - The allocator still refuses on create (`firm_required`). `SubmitInvoiceFromEmailOperation` maps that refusal to the same 400. This covers a phase that changed between the two reads.
- **Which entries take a null firm.**
  - `DecideForFromEmailAsync` is the only entry that accepts one. It skips the active-firm and firm-link checks.
  - `ValidateFields` refuses a null firm on every other entry (SPA, internal upload, from-document, confirm) with 400 `firm_invalid`.
  - `DecideAsync` throws if a null firm reaches it from any other caller.
- **Correction rule.** `InvoiceSubmissionService.CorrectionKeepsFirm(original, requested)` does both firm comparisons (intake and prefill).
  - A null firm equals a null firm only.
  - A firm on one side only is 409 `correction_not_allowed` / `different_firm`.
  - The empty id never matches.
- **Idempotency.** `FromEmailSubmissionKey.CacheBinding` binds a null firm as an empty value, so a null firm and an absent one are the same request. K is unchanged. `SameFirmDetails` compares the nullable firm.
- **Correction prefill (contract section 10).**
  - An invoice with no firm is 200 with `"firm": null`; `CorrectionPrefillResponse.Firm` is nullable.
  - A firm whose name cannot be read is still 503.
  - The section 8 firms route is unchanged.
- External, wizard and SPA routes are unchanged; they always set a firm.

### 11.2 Component justification (CLAUDE.md section 11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `ITrackingNumberAllocator.NumbersSubmissionsWithoutFirmAsync` | `TrackingScopePhaseCache` (095c-1), read inside `CreateSubmissionAsync` | This is the extension: one read of the same cache, added to the existing interface | The intake cannot refuse a no-firm request in P0/P1 before it saves and files the email; the job would refuse it after the writes |
| `InvoiceSubmissionService.CanSubmitWithoutFirmAsync` | The allocator member above | A one-line virtual seam on the existing service, which the from-email service already holds. It keeps the allocator out of `FromEmailSubmissionService`'s constructor | The from-email service would need a new dependency and a DI change for one call |
| `InvoiceSubmissionService.CorrectionKeepsFirm` | The two inline `!=` firm checks it replaces | It replaces them in place | With `Guid?`, the inline checks treat a null original as either "any firm" or "never matches". A no-firm invoice could then be corrected with a firm, or never corrected at all |

Placement: this extends the existing Finance Submission services in `Sprk.Bff.Api`. It adds no service, endpoint, DI registration, package or background work.

Publish size: no package or asset was added, only about 120 lines of BFF source, so the expected delta is a few KB. It was not measured against a fresh master publish in this part; the 095c-4 full gate measures it.

### 11.3 Tests and seed proofs

All new and changed tests pass:

- Unit, `FromEmailSubmissionServiceTests`:
  - No firm before the scope key is Active is 400 before any write (New and Correction).
  - No firm in P2 is submitted with no firm.
  - A firm that is sent in P2 is still checked.
  - A correction of a no-firm invoice keeps the null firm; sending a firm instead is 409 `different_firm`.
- Unit, `InvoiceCorrectionIntakeTests`: a no-firm invoice corrected without a firm is allowed; a firm on one side only is 409 (3 rows).
- Unit, `InvoiceSubmissionServiceTests`: a null firm on the SPA, internal and existing-document entries is 400.
- Unit, `SubmitInvoiceFromEmailOperationTests`: a no-firm submission reaches the allocator with no firm; the allocator's `firm_required` refusal is 400.
- Contract, `InvoiceSubmissionFromEmailContractTests`:
  - A null or absent firm is 400 in P0/P1 and 202 in P2, with no firm access check.
  - A null firm and an absent one are one cached request.
  - The same header with a firm reaches the handler.
  - A Correction of a no-firm invoice is 202, and 409 when it sends a firm.
- Contract: `InvoiceCorrectionPrefillContractTests.NoFirmInvoice_Is200_WithFirmNull`.
- Data-mutation: `TrackingNumberAllocatorTests.NumbersSubmissionsWithoutFirm_IsTrueOnlyInP2`.

The `firmId` body-shape row was removed, because the behaviour it pinned is gone.

Seed proofs, one each:

- The P0/P1 refusal (`NoFirm_BeforeTheScopeKeyIsActive_Is400FirmInvalid_BeforeAnyWrite` and the contract P0/P1 cases) fails under M1.
- The prefill null-firm path (`NoFirmInvoice_Is200_WithFirmNull`) fails under M2.

Fail-before proofs: for each mutation, the source was mutated and built, the filtered tests were run, and the file was restored byte for byte (SHA-256 checked).

| Mutation | Tests failed |
|---|---|
| M1: the phase gate is bypassed (no firm is always accepted) | 4 (service ×2, contract ×2) |
| M2: the prefill answers 503 for a null firm | 1 (prefill) |
| M3a: a null firm never matches | 4 (unit, contract, prefill) |
| M3b: a null original matches any firm | 2 |
| M4: the cache binding ignores the firm | 2 (the same-header test and the existing `Key_DoesNotChangeWithTheFirmOrTheNumber_ButTheCacheBindingDoes`) |
| M5: other entries accept a null firm | 3 |
| M6: the body shape requires `firmId` again | 7 (contract) |
| M7: `firm_required` is not mapped to 400 | 1 (operation) |

### 11.4 Deviations and known limits

- current-task.md and the POML status are not updated here; the main session owns them.
- The P0/P1 check reuses the allocator's phase cache through a new interface member instead of a separate probe, so the intake and the create read the same phase.
- A Correction may also send no firm, but only when the replaced invoice has none (the correction rule). In P0/P1 it is refused the same way as New.
- A Correction sent without a firm in P0/P1 now gets 400 `firm_invalid` "A firm is required.". Before, it got the body-shape ValidationProblem 400 for `firmId`.
- **K1 (safe until 095c-3).**
  - In P2, a no-firm submission is accepted and queued.
  - `SubmissionSnapshot.Read` still requires a firm and returns `FirmMissing`, so the job ends the submission Failed. No invoice is created and no rule runs.
  - A stamp from the current SpaarkeMaster source reaches P2 without the schema script, so this fail-closed job is the guard there.
  - Approval and AP export cannot see a no-firm invoice until 095c-3 creates one.

### 11.5 Hand-off

095c-3 (pipeline):

- Make `SubmissionSnapshot.FirmId` `Guid?`, and stop `Read` failing a no-firm submission as `FirmMissing`.
- Make the job skip the firm-scoped steps and record the `firm_missing` finding.
- Add the contract section 9 outcome row.
- Change the commit-time correction re-check in `InvoiceSubmissionPersister` (`InvoiceSubmissionPersister.cs:1205`, now `old firm is not {} firm || firm != Submission.FirmId`) to use `CorrectionKeepsFirm`. Otherwise a correction of a no-firm invoice is refused at commit.
- Make the persister's writes of `snapshot.FirmId` (`:1344`, `:1419`) write no firm lookup for null.
- Decide the persister's invoice lookup by firm and number (`:1104`, `:1115`) for a no-firm submission. Today it skips the lookup for the empty firm.

095c-4 (readers and contract):

- The approval and AP-export refusals for a no-firm invoice.
- The remaining readers of `sprk_vendororg` (section 6 inventory).
- The final contract text.
- The publish-size measurement.

### 11.6 Review fixes (095c-2)

**F1: a retry of an accepted no-firm submission could get 400 `firm_invalid`.**

- **Cause.** `CheckFirmAsync` ran the no-firm phase check at step 4, before the replay of K (step 6). It read only this instance's phase cache. Instance A (P2) accepts a no-firm New with 202; the retry lands on instance B (still P1, cache refreshes every 5 minutes) and gets 400 "A firm is required." although the submission exists. A resubmit with a firm then gets 409 `document_already_submitted`. This breaks the allocator's contract (`TrackingNumberAllocator`: a redelivery on a stale P1 instance must return the row, not refuse it).
- **Fix (chosen: move the check, not a forced re-read).** `FromEmailSubmissionService`: `CheckFirmAsync` passes a null firm (nothing to check); the phase gate is the new `CheckNoFirmPhaseAsync`, called as step 6b after the replay of K and the generation cap, before step 7 (communication read, correction checks, save, submit). A replay is answered before the gate is consulted. A refusal still happens before any save, filing or submit. Why not `force: true`: a forced re-read costs a metadata call on every no-firm request in P0/P1, still races a not-yet-refreshed instance, and leaves the check ahead of the replay, whose answer needs no phase at all.
- **Cost.** In P0/P1 a no-firm request now does the marks check, the Correction's Write check, the operation-availability and caller reads, and the attempt reads of K before it is refused. All are read-only.
- **Test.** `FromEmailSubmissionServiceTests.NoFirm_RetryOfAnAcceptedSubmission_OnANonP2Instance_IsReplayedNotRefused`: a no-firm Received row exists for K and the phase is not P2. The retry gets the replayed 202 (same submission id, job id, tracking number and .eml), saves nothing, submits nothing, and never consults the phase.
- **Seed proof.** The fix was reverted (the file restored from `HEAD`), the service test class run: 1 failed (the new test, `Receipt` null), 82 passed. The fixed file was then restored byte for byte (SHA-256 `b7024850…3734` before and after), and the class run again: 83 passed. `NoFirm_BeforeTheScopeKeyIsActive_Is400FirmInvalid_BeforeAnyWrite` (New and Correction) passes under the fix and was in the 82.
- **Gate.** All five test projects (`Sprk.Bff.Api.Tests`, `Spaarke.Core.Tests`, `Spaarke.Scheduling.Tests`, `Spaarke.ArchTests`, `Sprk.Bff.Api.IntegrationTests`) build with 0 errors and 0 warnings. Filtered `Sprk.Bff.Api.Tests` (`Submission|Finance|Office`): 6609 passed, 0 failed, 10 skipped. `Spaarke.ArchTests` filtered to `Submission|TrackingScope`: 42 passed, 0 failed.

## 12. Part 095c-3: the pipeline runs a no-firm submission (2026-11-03)

### 12.1 What changed

- **Read.** `SubmissionSnapshot.FirmId` is `Guid?` (`HasNoFirm`).
  - `Read` accepts a null firm only on the Internal channel (the from-email intake is the only entry that sends one, 095c-2).
  - The empty id on any channel, and a null firm on an SPA submission, still end Failed `firm_missing` before anything else is read.
- **The job (`InvoiceSubmissionProcessor`).** For a no-firm submission:
  - It adds the pipeline finding `firm_missing`.
  - Resolution reads no roster and no rates. Every fee line is unmatched (`Stage None`, reason `None`, which the approved-timekeeper default reads as unmatched), and every rate is `NotFound`. The arithmetic is still checked.
  - Coding skips the firm-code step (`LineCodingRequest.IncludeFirmCodes = false`, passed to a new `CodeSetResolver.ResolveAsync` overload). The record's code set still applies, and billed codes are kept.
  - Rule evaluation is skipped entirely, including the Duplicate priors and the replaced-invoice read.
  - The outcome is `Accepted` (or `AcceptedUncoded`) with reason `firm_missing`, and has no evaluation or decision. No new outcome kind was added.
- **Persist (`InvoiceSubmissionPersister`).**
  - `RulesNotChecked(outcome)` flags every line `rules.not_checked` with "The billing rules were not checked on this line because the invoice has no firm." (EB-D-175). No line of a no-firm invoice is Clean.
  - There is no firm lookup on the invoice or its lines when the firm is null.
  - `TerminalFor` keeps the reason code of an Accepted outcome only when it is `firm_missing`.
  - `Validate` refuses, before any write, a no-firm outcome that is not exactly a held acceptance (an evaluation or decision, a rejection kind, a missing hold finding or reason).
  - The commit-time correction re-check uses `InvoiceSubmissionService.CorrectionKeepsFirm`.
  - The commit-time re-read also re-evaluates when the firm changed since the evaluation (see 12.3).
- **Supersedes lookup (`FindSupersededInvoiceAsync`), decided.** A no-firm submission is never linked to a Returned to Firm invoice, and the lookup does not run. The tracking scope `none` is a numbering scope, not an identity: two no-firm invoices with one number are not the same firm's invoice. A null firm never matches another null. 095d (assign firm) must re-run the pipeline, and with it this lookup, once the firm is known. A Correction of a no-firm invoice still supersedes by its explicit target (`CorrectionKeepsFirm`, null to null).
- **After-commit (`InvoiceSubmissionAfterSteps`).** `firm_missing` is a system finding. It is written as the SubmissionIntegrity Signal on the invoice under BILL-SYSTEM-SUBMISSION, headline "Firm needed", template "This invoice was submitted without a firm, so its billing rules were not checked. A reviewer must add the firm before it can be approved." It is flag-only. No Decision Record is written, and the terminal status is Accepted `ebilling.submission.firm_missing`.
- **Signals/Billing (for ontology-r1 review).**
  - New: `BillingReasonCodes.SubmissionFirmMissing` (`submission.firm_missing`), `BillingSystemFindings.FirmMissing`, the template and headline, and flag-only membership.
  - Changed: the mapper's primary-reason order on a shared (policy, subject) group. `NotePrecedence` puts the firm hold first, then the invoice-number mismatch (the D-125 rule, unchanged), then the code order. Before, only the mismatch was lifted.
- **Contract.** Section 9 has the Accepted `ebilling.submission.firm_missing` row.

### 12.2 Component justification (CLAUDE.md section 11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `submission.firm_missing` reason, finding, template, headline | The SubmissionIntegrity system findings (invoice-number mismatch, coding notes) | Added to that catalogue, not a new policy | The hold has no Signal; a reviewer sees In Review with no reason and no "add the firm" instruction |
| `LineCodingRequest.IncludeFirmCodes` and the `CodeSetResolver.ResolveAsync` overload | Step 1 of `CodeSetResolver` (the firm's codes) | A flag on the existing request; the old signature delegates with `true` | Step 1 would choose a code set from codes no firm stands behind |
| `NotePrecedence` in the mapper | The invoice-number-only `ThenBy` it replaces | Replaces it in place | `coding.no_code_set` sorts before `submission.firm_missing`, so the Signal would be titled by the coding note and hide the hold |
| `RulesNotCheckedNoFirmMessage` and the `RulesNotChecked(outcome)` overload | `RulesNotChecked(evaluation, lineCount)` | Wraps it; that overload is unchanged | A null evaluation reads as "nothing refused" and every line is Clean |
| `SubmissionSnapshot.HasNoFirm`, `SubmissionEvaluationOutcome.RulesSkippedForNoFirm` | None (one-line properties) | Read-only properties on the existing records | Each caller would repeat the null test |

Placement: this extends the existing Finance Submission, Resolution, Coding and Signals/Billing services in `Sprk.Bff.Api`. There is no new service, endpoint, DI registration, package or background work. Publish size was not measured (source only, about 400 lines); the 095c-4 gate measures it.

### 12.3 Deviation: the firm change re-evaluates at commit

This was not in the hand-off. `sprk_vendororg` is no longer SystemRequired after the schema script, so the firm can be set or cleared between the evaluation and the commit. The commit-time re-read compared only the core record. It now also compares the firm and answers `ReEvaluate`, as for a changed record. Without this, an evaluation made with a firm could be committed on a submission that no longer has one, or the reverse.

### 12.4 Tests and seed proof

Each new test has a must-not-fire control.

- Processor:
  - No firm is Accepted `firm_missing`: no roster or rate read, no firm-code step, and no rule judged (a blocking rule does not block). Control: the same rule blocks with a firm.
  - No firm and no code set is AcceptedUncoded with the firm hold as reason.
  - The empty id, or no firm on an SPA submission, still fails closed (theory).
- Resolution: a null firm reads no roster or rate, leaves every fee line unmatched, and still checks the arithmetic. Control: the same input with a firm matches.
- Coding seam: without the firm-code step, the record's code set applies and billed codes are kept.
- Persister (`InvoiceSubmissionPersisterTests.NoFirm.cs`):
  - **SEEDED.** A no-firm invoice is committed In Review with no firm lookup, every line Flagged `rules.not_checked`, and Accepted `firm_missing`. Control: a firm invoice's lines are Clean.
  - `TerminalFor` keeps an Accepted reason only for `firm_missing`.
  - A malformed no-firm outcome (four cases) is refused before any write.
  - A firm set or cleared since the evaluation re-evaluates and persists nothing (two cases).
  - A no-firm Correction of a no-firm invoice commits and supersedes it.
  - A firm on one side only at commit is `different_firm` (two cases).
  - No firm is never linked to a predecessor, and nothing is queried.
- After-steps:
  - The "Firm needed" Signal is written on the invoice, with no record and terminal Accepted `firm_missing`.
  - With no code set as well, the firm hold titles the one invoice Signal.
- Signals/Billing (`BillingSystemPolicyTests`): `firm_missing` joins the per-reason theories (unique template, flag-only, decided name). The firm hold titles a group shared with the mismatch and a coding note.

Seed proof (one, for the fail-closed "no Clean line" guard). The `RulesSkippedForNoFirm` branch of `RulesNotChecked(outcome)` was disabled (`if (false && ...)`), built and run on the filtered gate. Exactly the SEEDED test failed (1 failed, 10779 passed). The file was restored byte for byte (SHA-256 `4199214e…a84e` before and after), rebuilt, and the persister tests passed (212).

Gate:

- All five test projects build with 0 errors and 0 warnings.
- The filtered `Sprk.Bff.Api.Tests` run: 10780 passed, 0 failed, 7 skipped. The 6756 in section 11 was before the 086/089a merges. `Step0Refusal_IsTheUniform404` now passes.
- Full `Spaarke.ArchTests`: 1951 passed.

### 12.5 Known limits and hand-off

- **K1 (095c-4, before P2 is turned on anywhere).** Approval does not yet refuse a no-firm invoice. Today the lines are Flagged, so I-2 refuses approval until a reviewer decides each line. A reviewer who accepts every line could then approve an invoice with no firm. The `held` check does not cover it, because the submission is Accepted, not Blocked. Nothing reaches this path before P2, which needs the schema script. 095c-4 owns the approval and AP-export refusals and the other readers, including `EligibleApproverResolver` and `InvoiceCorrectionReevaluation`.
- **K2 (095d).** Assigning the firm must re-run the pipeline: resolution, the firm-code step, rule evaluation with Duplicate priors, and the supersedes lookup. It must also renumber into the firm's scope (section 9). The `firm_missing` Signal is then resolved by the re-run.
- `firm_missing` has two meanings by outcome: Failed (empty id, SPA with no firm) and Accepted (the hold). `TerminalFor` and contract section 9 keep them apart.
- **ontology-r1 review.** The new reason, template and headline, and the `NotePrecedence` change in `BillingFindingSignalMapper`. The firm hold now outranks the invoice-number mismatch as the title.
- current-task.md, TASK-INDEX.md and the POML status are not updated here; the main session owns them.

rework: none task 095c

## 13. Owner decision: no-firm invoices stay visible externally (2026-11-03)

uac-r2 asked for this decision before 095c-2 goes into a deploy request.

- **Question.** An invoice with no firm is visible in the external portal to every outside participant with rights on its matter or project. Through option A and the `ExternalInvoiceVisibility` source rule, that includes people from other firms. Should it be hidden from external users until a firm is set?
- **Owner's answer (typed in session ebilling-r1-7b): leave it visible.** Today's rule stays: matter or project rights decide visibility, with no firm dimension. 095d does not need to change external visibility.
- **What the 095c-2 review confirmed:** no external route treats "no firm" as "matches any firm".
  - The submissions list filters on `sprk_vendororg In callerFirmIds`, so a null firm never matches.
  - The submission read-one (`ExternalInvoiceSubmissionAuthorizationFilter`) and the replacement gate both require `FirmId: { }` and a firm in scope.
  - A no-firm **submission** is therefore never shown to a firm. A no-firm **invoice**, created from 095c-3 on, is visible through the matter or project rule only (`ExternalAccessModule.cs` ~510-518, invoice documents through `RightsThroughParents`). That is the same reach any firm's invoice has.

## 14. Part 095c-4: readers and contract (2026-11-03)

### 14.1 What changed

- **The hold, one definition (`InvoiceFirmHold`, new static reader).** An invoice **needs a firm** while it has no firm, or a submission linked to it (`sprk_invoicesubmission.sprk_invoice`) stores the `firm_missing` hold. The hold is any one of these:
  - `sprk_outcomereasoncode` = `ebilling.submission.firm_missing`;
  - a `pipelineFindings[]` entry with that reason;
  - a `lineFindings[]` entry `rules.not_checked` with the new `cause: "ebilling.submission.firm_missing"`, which the persister now writes on every no-firm line.
  - Findings that cannot be parsed, or that are not an object, count as held (fail closed).
  - A firm invoice's `rules.not_checked` line (a refused rule, EB-D-175) has no `cause` and does not hold.
- **Approval (`InvoiceApprovalService`).**
  - The approve path, which is the held-approval path (`ApproveHeldAsync`, after the FR-20 check and before I-2), returns 409 `ebilling.approve.firm_missing` "This invoice has no firm. A reviewer must add the firm before it can be approved." It covers an invoice with no lines, so the "Firm needed" Signal is never closed by an approval (EB-D-172).
  - `GetAsync`: `canApprove` is false and the DTO's new trailing `firmNeeded` is true.
  - A read fault is the existing 503.
- **AP export (`ApExportService`).**
  - The selection (step 4) refuses a no-firm or held invoice per invoice with `invoice_firm_missing`, so the request is `SelectionRefused` and nothing is claimed. A read fault on the holds is `Unavailable`.
  - The re-check after the claim (step 7) re-reads `sprk_vendororg` and the holds, and answers 409 `ebilling.export.firm_missing` with every claim released. This is the Sent-to-AP writer's guard: `TrySendAsync` (step 9) is the only caller of the lifecycle's move to Sent to AP, and step 7 runs before any file or status write.
- **Return to Firm (`InvoiceRejectReturnService`).** Return is 409 `ebilling.invoice.firm_missing` "This invoice has no firm to return it to. Add the firm first, or reject the invoice." Reject is allowed.
- **Corrections (`InvoiceCorrectionService`, `InvoiceCorrectionReevaluation`).**
  - A line or header correction of an invoice that needs a firm is 409 `ebilling.correction.firm_missing`, with nothing written.
  - The re-evaluation's `InvalidOperationException` ("The invoice names no firm", ~1428) is now the typed `CorrectionFirmMissingException`, thrown before the findings patch and before the empty id could reach `Build`.
  - The service maps that exception to 409 `firm_missing` "…the correction was saved but its rules were not re-run…". This is the path where the firm was cleared between the check and the re-evaluation.
  - The correction stays unapplied, so approval stays held (CorrectionsPending).
- **Adjustments (`InvoiceAdjustmentService`).**
  - The rule options, and an adjustment that cites a rule, are 409 `ebilling.adjustment.firm_missing` for an invoice with no firm.
  - `ReadOptionsAsync` no longer loads rules for `Guid.Empty`.
  - An adjustment without a rule is allowed, and its row has no firm lookup.
- **Endpoints.** `CorrectionStatus.FirmMissing` and `AdjustmentStatus.FirmMissing` are added to the existing 409 lists. No new route.
- **`EligibleApproverResolver`: no change.** It reads no firm: approvers come from the core record's roles, so it is safe with a null firm. Approval itself is refused above.
- **K4: the persister's submission update is conditional.**
  - The update now carries `ConcurrencyBehavior.IfRowVersionMatches` on the version re-read (`versionnumber` is added to `SubmissionColumns`).
  - A fault at that request index re-reads the submission:
    - gone: `SubmissionMissing`;
    - firm or core record changed: `ReEvaluate`;
    - any other change, or a re-read fault: Retry `ebilling.submission.changed`.
  - A re-read with no row version is Retry `ebilling.submission.row_version_missing`, and no transaction is sent.
- **Job handler wording.** `ReEvaluate` carries the persister's reason. A firm change is logged as "the firm of submission … changed" and retried as `ebilling.submission.firm_changed`. A core-record change stays `ebilling.submission.core_record_changed`. Both codes are now in `SubmissionReasonCodes`.
- **uac-r2 (binding, 2026-11-03): the hold cannot be cleared outside the pipeline.**
  - `sprk_invoicesubmission.sprk_outcomereasoncode` joins the submission's `InheritedAccessPermission.AccessColumns`. The app-only generic writers (output mapping, the Update Record node) are refused it per column, with the message "it decides no access, but it holds the invoice from approval and export; only the submission pipeline writes it".
  - `sprk_vendororg` was already on that list.
- **Forms (source only, not deployed).**
  - `sprk_vendororg` is read-only on the sprk_invoice main form `{93aa1c69…}` and on the sprk_invoicesubmission main form `{c412f942…}`, together with that form's `sprk_outcomereasoncode`. Both the unmanaged and the `_managed` copies are changed.
  - `Deploy-EbillingFormsViews.ps1` makes the same change: `ForceReadOnly` on the invoice, and `ReadOnly` on the submission's general section. It was not run.

### 14.2 uac-r2 record: the stored hold, not the firm column

- **The form controls are a convenience, not the guard.** A user with Write on the invoice or the submission can still set `sprk_vendororg` through the Web API. The BFF's generic update tools already refuse the submission's `sprk_vendororg` (ParentLineage). Only a direct Dataverse write remains.
- **The guards key on the stored hold.** That covers approve (`ApproveHeldAsync`), `canApprove` (`GetAsync`), Return to Firm, AP-export selection and re-check, and corrections. "No firm" is an additional condition. A firm set natively neither clears the hold nor unlocks a step. Proved by `FirmSetNatively_WhileHeld_ApproveIsStill409` and `ApExportFirmHoldTests.FirmSetNatively_WhileHeld_IsRefused_NothingWritten`.
- **No other path clears it.** Every writer of the reason or the findings was checked:
  - The persister and the after-steps are the pipeline.
  - The commit gate's undo writes Failed with another code. The invoice is compensated away, and if it lingers, the no-firm and line markers still hold.
  - `SubmissionOverrideService` acts only on a Blocked submission. An Accepted `firm_missing` submission is refused there (AcceptedByProcessing).
  - The job's failure writes touch only unlinked or non-terminal submissions.
  - The correction re-evaluation removes only the coding `HoldCodes`. `firm_missing` stays, and the system Signal is refreshed from `SystemFindings`.
  - The generic app-only writers are now refused the reason column (above).
- **Known limit (K-uac, accepted as a direct-write limit):** a user with Write on the submission who rewrites, through the Web API, both `sprk_outcomereasoncode` **and** `sprk_findingsjson`, removing the pipeline finding and every line cause, lifts the hold. While the invoice still has no firm, the "no firm" condition still refuses. The hold is lost only when the same user also sets the invoice's `sprk_vendororg` natively. That is three deliberate direct writes against Spaarke-set columns. Closing it needs field security on the reason, the findings and the firm columns, or a privilege change, which is a uac-r2 decision (see 14.7).

### 14.3 Component justification (CLAUDE.md section 11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `InvoiceFirmHold` (static reader, enum `InvoiceFirmNeed`) | `FinanceBaseValues.HeldSubmissionConditions` and each service's `IsHeldAsync` (the FR-20 hold) | FR-20 is a column filter the pending-spend anti-join also uses; the firm hold must also read the stored findings, which FetchXML cannot. One reader keeps one definition for six callers. | A no-firm invoice, or one whose firm was set natively, is approved and exported |
| The codes `ebilling.approve.firm_missing`, `.export.firm_missing`, `.invoice.firm_missing`, `.correction.firm_missing`, `.adjustment.firm_missing`, and `invoice_firm_missing` | Each service's own code catalogue | Added to those catalogues, not a new type | A reviewer gets a generic 409, or a 500 (the correction's `InvalidOperationException`) |
| `CorrectionStatus.FirmMissing`, `AdjustmentStatus.FirmMissing` | The status enums the endpoints map | Added members; the existing 409 lists | No status to map, so the endpoints answer 500 |
| `InvoiceApprovalStateDto.FirmNeeded` (trailing, default false) | `CanApprove` | Additive field | The pane cannot say why approval is off |
| The line-finding `cause` member | `lineFindings[].code` | Additive member on the existing entry | A firm invoice's refused-rule `rules.not_checked` line would be read as a firm hold (EB-D-175), or a hold whose reason was rewritten would be lost |
| `SubmissionReasonCodes.CoreRecordChanged` and `.FirmChanged`; the persister's `ebilling.submission.changed` and `row_version_missing` | `CoreRecordChangedCode` in the job handler | Moved into the shared catalogue; two new transient codes | A firm change is logged and retried as a core-record change; a K4 race has no code |
| `sprk_outcomereasoncode` in `AccessColumns` | The submission's access-column list (firm, invoice, number, scope) | One more entry | A generic tool clears the hold |
| `CorrectionFirmMissingException` | `CorrectionTotalsRefusedException` (the same pattern) | Same pattern | The 500 stays |

**Placement:** BFF only, inside the existing Finance services in `Sprk.Bff.Api`. No new service, DI registration, endpoint, package or background work.

### 14.4 Reader inventory (section 6): each reader with a null firm

| Reader | With a null firm |
|---|---|
| `InvoiceApprovalService` | Refused (above) |
| `ApExportService` (and its `ValueReader`) | Refused (above); `firm.name` is read null-safe |
| `InvoiceRejectReturnService` | Return refused, Reject allowed |
| `InvoiceCorrectionService` (the timekeeper read ~542 included), `InvoiceCorrectionReevaluation` | Refused before any read that needs the firm (above) |
| `InvoiceAdjustmentService` | Rule options and cited rules refused; the row's firm lookup is written only for a real firm (it already was) |
| `EligibleApproverResolver` | Reads no firm; no change |
| `FinanceSummaryService` | `VendorOrgId` is nullable; no change |
| `InvoiceContextService` | Renders the "firm unresolved" sections; no change |
| `InvoiceReviewReadService` | The header firm is null; no change |
| `InvoiceIndexDocument` | `vendorOrgId` is null; no change |
| `RuleSetService.GetFirmClauseAsync` | `Guid.Empty` gives no text; not reached for a no-firm invoice (the adjustment refuses first) |
| `InvoiceExtractionJobHandler` | Legacy path; `VendorOrgId` is always null there; no change |
| `AttachmentClassificationJobHandler` | Uses only the "VendorOrgMatch" label; no change |
| `BillingFactLoader` | Not called for a no-firm invoice (095c-3: rules are skipped) |
| `FinanceEndpoints` and `InvoiceReviewService` (the old review route) | Still require `VendorOrgId`; a no-firm invoice is refused there, as before |
| `FinanceAuthorizationFilter` | A column-name constant only |
| Intake, allocator, persister, processor, after-steps | Handled in 095c-1 to 095c-3 |

### 14.5 Tests and seed proofs

New:

- `InvoiceApprovalFirmHoldTests`, 10 tests:
  - zero lines, no firm: 409, and the Signal stays open;
  - the firm set natively while held: 409, and `canApprove` false;
  - each stored marker holds alone (four cases, including unreadable findings);
  - must not fire: a firm invoice with a refused-rule line approves;
  - must not fire: a firm invoice with zero lines and no hold approves;
  - Return is refused and Reject is allowed;
  - the pure `IsHeld` reader.
- `ApExportFirmHoldTests`, 9 tests:
  - no firm;
  - the firm set natively while held;
  - each marker (three cases);
  - must not fire: no hold, the invoice exports;
  - a hold read fault is Unavailable;
  - a hold that appears, or a firm that is cleared, after the claim: 409 `ebilling.export.firm_missing`, with every claim released and nothing sent or written (two cases).
- `InvoiceSubmissionPersisterTests.Concurrency.cs`, 6 tests:
  - the update is `IfRowVersionMatches` on the re-read version;
  - the firm or core record changed between the re-read and the commit: `ReEvaluate` with `FirmChanged` or `CoreRecordChanged`, nothing persisted (two cases);
  - any other change: Retry `ebilling.submission.changed`;
  - no row version: Retry, no transaction;
  - the no-firm line findings carry the cause, and a firm invoice's committed submission is not held.
- The persister test store (`Store`) now models the submission's `versionnumber` and faults a stale conditional update.
- `InvoiceSubmissionProcessingJobHandlerTests`: a firm-change `ReEvaluate` is worded and retried as `firm_changed`; a core change keeps its wording (two cases).
- Corrections:
  - no firm: 409 on a line and on a header, nothing written;
  - held: 409, nothing written;
  - the firm cleared before the re-evaluation: 409, the value saved, the findings untouched.
- Adjustments:
  - rule options with no firm: 409, and no firm is asked;
  - a cited adjustment: 409, nothing written;
  - must not fire: an uncited adjustment is applied without a firm lookup.
- `AccessTableAppOnlyWriterTests`: the reason is refused to the output mapping and to the Update Record node (two spellings). Must not fire: the status column, the same name on the invoice, and the caller's own write. The ArchTest description is updated.

Seed proofs. For each, the guard was mutated, built, and the named tests were run. The file was then restored byte for byte, with the SHA-256 the same before and after.

| Guard | Mutation | Result | SHA-256 (prefix) |
|---|---|---|---|
| Approval refusal (zero lines, native firm) | the `CheckAsync` condition made never true | 6 of 10 failed, among them the zero-line and native-firm tests | `57219e889169968d` |
| The stored hold (not the firm column) | `CheckAsync` skips the hold read | 6 of 10 failed, among them native firm and Return | `00e953d467654481` |
| AP-export refusal | the selection filter removed | 5 of 9 failed (no firm, native firm, three markers). The step-7 guard still refused them, as 409 rather than 400 | `85a4cc6f6e22c4a1` |
| K4 concurrency condition | `ConcurrencyBehavior` dropped from the submission update | 4 of 4 failed (firm, core, other change, the condition itself) | `3a38972be21ae44d` |

Gate (on the branch with every change):

- All five BFF test projects build with 0 errors and 0 warnings: `Sprk.Bff.Api.Tests`, `Spaarke.ArchTests`, `Sprk.Bff.Api.IntegrationTests`, `Spe.Integration.Tests` and `Sprk.Provisioning.ControlPlane.NightlyTests`.
- The filtered `Sprk.Bff.Api.Tests` run, with 095c-3's filter (`FullyQualifiedName~Finance|~Billing|~Submission|~Signals|~Invoice|~Resolution|~Coding`): 10930 passed, 0 failed, 7 skipped. 095c-3's 10780 was measured on its own branch, before the 089b merge; this task adds about 40 of the tests counted. The extra classes outside that filter (`~ApExport|~AccessTableAppOnlyWriter|~ChildAccessPermission|~Jobs.Handlers`): 1957 passed, 0 failed.
- Full `Spaarke.ArchTests`: 1951 passed, 0 failed (after the fix below).
  - The first run failed two tests.
  - `FindingsJsonAppOnlyReadGuardTests` C1 failed, correctly: `InvoiceFirmHold` is a new reader of the field-secured `sprk_findingsjson` (task 122). It reads app-only, so it is now in `ReviewedTypes`, and the task 122 inventory has row 9. C2 and C3 pass for it.
  - `ContactCreateOwnerPolicyGuardTests` "ISS-005 guard: 60 assignments … in bounded time" failed under the load of the parallel gate. It passes alone, and 095c-4 does not touch it: a pre-existing timing flake, reported.
- Publish size (bff-hygiene item 4, `dotnet publish -c Release`, zipped with PDBs, from fresh `git archive` trees): 39.36 MB zipped at 78bc434c03 (192 files): +0.007 MB against the base 5d3c3b60da (39.35 MB) and +0.062 MB against master aef86b89f7 (39.30 MB). This is under the +5 MB threshold and the 55 MB review level. The trees were left at `C:\wt95c4m`, `C:\wt95c4b` and `C:\wt95c4x`, and can be deleted. No package changed, so there is no new CVE (no `PackageReference` edits in this diff).

### 14.6 Deviations

- **The lifecycle is not guarded itself.** `InvoiceLifecycleService` reads through `IFieldMappingDataverseService`, and its existing tests carry no firm. The Sent-to-AP guard therefore sits in the export's step 7, which is the only caller of that transition and runs before any write.
- **Adjustments check "no firm" only, not the stored hold.** A cited rule needs a firm id to load rules. A held invoice whose firm was set natively would list that firm's rules. Approval and export still refuse such an invoice.
- **The submission form's `sprk_outcomereasoncode` is also read-only.** This was not asked for. It makes the reason a typed-writer column on the form too.
- **The persister answers Retry when the re-read has no row version.** It does this instead of sending an unconditional update (fail closed).
- **Contract section 9 gains four transient Failed codes.** `core_record_changed` is existing; `firm_changed`, `changed` and `row_version_missing` are new. Section 12 is new. This is additive, and word-add-in-r1 should acknowledge it.

### 14.7 Known limits and decisions needed

- **K-uac (above):** three deliberate direct Web API writes can lift the hold. **uac-r2:** field security on `sprk_invoicesubmission.sprk_outcomereasoncode` and `sprk_findingsjson`, or no Write on submissions for reviewers? (Both are text columns, which can be field-secured.)
- **The approval DTO's `firmNeeded`** is additive. The reviewer pane does not read it yet; it shows the 409 sentence.
- **ontology-r1:** no new Signal or reason in 095c-4. The line-finding `cause` member is a findings-document member, not an ontology term.
- current-task.md, TASK-INDEX.md and the POML status are not updated here; the main session owns them.

### 14.8 095d must (constraints from the 095c-3 review K5, K6 and 095c-4)

1. **Clear the hold only through the pipeline.** Assigning the firm re-runs the submission. The new outcome must remove all three markers: the reason `firm_missing`, the `firm_missing` pipeline finding, and every line finding with `cause: ebilling.submission.firm_missing`. Removing one leaves the invoice held, by design. The assign-firm writer is typed; no generic writer may set the reason (AccessColumns).
2. **K5: reset the line decisions.** No-firm lines are Flagged `rules.not_checked`, including AcceptedUncoded lines whose codes are still owed, so a reviewer can decide them. 095d must reset line statuses and discard line decisions made while there was no firm. Otherwise decisions on lines no rule checked count toward approval after the re-run. "Firm needed" also outranks "Lines not coded" in the shared Signal: the re-run must restore the coding hold's title.
3. **K6: Duplicate and supersedes.** Firm submissions find earlier invoices only by firm, so a no-firm copy of the same invoice is invisible to them, and the reverse holds too. 095d must re-run the rules, including Duplicate with its priors, and the supersedes lookup, before approval is possible.
4. **Renumber into the firm's scope** (section 9).
5. **Re-check the export.** An invoice assigned a firm after approval cannot exist, because approval is refused while it needs one. 095d must keep that true: no path from held straight to Approved.
6. **Clear the invoice's own hold (095c-5).** Assign-firm clears `sprk_invoice.sprk_firmhold` through its typed, app-only writer, in the same write as the renumber. No other writer clears it (AccessColumns refuses the generic writers), and the assign-firm type must be added to `InvoiceFirmHoldAppOnlyGuardTests`' reviewed list. Clearing the submission markers without the column, or the column without the markers, leaves the invoice held, by design.

rework: none task 095c

### 14.x Hold-lift known limit: disposition (uac-r2, 2026-11-04)

- **Dev evidence (uac-r2, read-only):** `sprk_invoicesubmission.sprk_findingsjson` is field-secured. Only "Spaarke BFF-Managed Field Writers" and System Administrators can update it. `sprk_outcomereasoncode` and `sprk_vendororg` are not secured.
- **The hold condition** (`InvoiceFirmHold.IsHeld`, `InvoiceFirmHold.cs:115-149`) holds when **any one** of these is true: the reason is `firm_missing`, the findings have the `firm_missing` pipeline finding, or the findings have a `rules.not_checked` line with cause `firm_missing`. The persister stores every no-firm outcome with the `firm_missing` pipeline finding (`InvoiceSubmissionPersister.cs:2203-2214`).
- **Result (corrected, uac-r2 2026-11-04):** the findings-JSON path is closed by existing field security, but (c) does NOT hold. `IsHeldAsync` finds the hold through the submission's link to the invoice (`sprk_invoicesubmission.sprk_invoice`), which is not field-secured, so the hold can be hidden by clearing or repointing that link, or by deleting the submission. Removing reviewers' Write was rejected, because reviewers need Write for their real work.
- **Fix:** task 095c-5 (`tasks/095c5-invoice-firm-hold-column.poml`) adds a field-secured `sprk_invoice.sprk_firmhold`. Held = the invoice's own hold OR any linked submission's hold. It blocks the master PR.
- **Done (095c-5, section 15):** the column, its reader, the persister write, the generic-writer refusal and the script are in place. The column exists on an environment only after the script's `-Apply` (main session).
- **To add in the 095c-4 review fix round:** a test that rewrites the reason, sets the invoice firm and leaves the findings unchanged. Approval must stay 409 and export must stay refused. Done (section 15.6, Pin).

## 15. Part 095c-5: the invoice's own firm hold (2026-11-04)

Task `tasks/095c5-invoice-firm-hold-column.poml` (uac-r2 binding, section 14.x). The defect it closes: the hold was found only through the submission's
link to the invoice, so unlinking, repointing or deleting the submission hid it (`InvoiceFirmHold.HeldInvoicesAsync`, before this task).

### 15.1 What changed

- **New column `sprk_invoice.sprk_firmhold`** (text, 100, field-secured). SpaarkeMaster `Entities/sprk_Invoice/Entity.xml` (`IsSecured=1`) and
  `Other/FieldSecurityProfiles.xml`: Writers read/create/update, Readers read. No other profile.
- **Held = the invoice's own hold OR any linked submission's hold.** `InvoiceFirmHold.HeldInvoicesAsync` first reads `sprk_firmhold` of the invoices
  (app-only, `IGenericEntityService`), then the submissions as before. `IsInvoiceHeld`: null or blank does not hold; any other value or shape holds.
  The value is always re-read app-only; the caller's row is never used for it (a caller-token read of a secured column is null, not an error).
- **The persister sets it** on the invoice create of a held (no-firm) outcome, in the same `ExecuteTransaction` as the held submission update. A
  firm invoice's create does not name the column. 095d's assign-firm is the only clearer (14.8 item 6).
- **Generic writers refuse it.** `InheritedAccessPermission.AccessColumns` gains `sprk_invoice`: `sprk_firmhold` and `sprk_vendororg` (095c-4 review,
  15.6). Comparisons are now case-insensitive.
- **Script** `scripts/ebilling/Set-EbillingInvoiceFirmHoldColumn.ps1` (modelled on `Set-EbillingFindingsJsonFieldSecurity.ps1` and the ResubmissionOf
  script): `-WhatIf` / `-Apply` / `-Verify`. It creates the column in the solution, secures it with the grants in the same step (a failed grant
  reverts the securing, so no BFF identity is left masked), fails when any other profile (System Administrator excepted) can create or update it, publishes, and backfills invoices whose linked
  submission is held (the same three markers as `IsHeld`; an unreadable findings document holds).
- **Guards:** `InvoiceFirmHoldAppOnlyGuardTests` (ArchTest, the findings-JSON guard's C1/C2/C3 shape) and
  `InvoiceFirmHoldFieldSecurityAgreementTests` (script, solution source and BFF constants agree).

**Column type.** Text, not Boolean or BigInt: it stores the hold's reason (`ebilling.submission.firm_missing`, the value the outcome uses), and
BigInt cannot be field-secured. Any non-blank value holds, so an unknown future value fails closed.

### 15.2 Reviewed readers and writers (the guard's list)

| Type | Use | Identity |
|---|---|---|
| `InvoiceFirmHold` | reads it (`HeldInvoicesAsync`, called by `CheckAsync` and `IsHeldAsync` from the callers listed below) | app-only `IGenericEntityService` |
| `InvoiceSubmissionPersister` | writes it on the held invoice create | app-only transaction |
| `InheritedAccessPermission` | names it only to refuse it to the generic writers | (registration only) |
| 095d assign-firm (planned) | clears it | must be typed, app-only; must be added to the guard's reviewed list |

Callers of `InvoiceFirmHold` (read from the code, 095c-5 pass-2 review; every one passes its own app-only `_entities`, never the caller's token):

| Caller | Where | Reads |
|---|---|---|
| `InvoiceApprovalService` | ~:326 (approval-state GET), ~:534 (approve) | `CheckAsync` |
| `InvoiceRejectReturnService` | ~:423 (Return to Firm) | `CheckAsync` |
| `InvoiceCorrectionService` | ~:407, ~:700 (line and header correction) | `CheckAsync` |
| `ApExportService` | ~:331, ~:440 (create batch, re-check), ~:575 (candidates) | `HeldInvoicesAsync` |
| `InvoiceAdjustmentService` | ~:259 (rule options), ~:467 (**every** adjustment add since pass-2 item 5, cited or not) | `CheckAsync` |
| `InvoiceSubmissionService.JudgeCorrectionAsync` | ~:1687 (intake correction naming a firm), **and the correction prefill** through `JudgeCorrectionPrefillAsync` ~:1790 | `IsHeldAsync` |
| `InvoiceSubmissionPersister` | ~:1237 (correction commit) | `IsHeldAsync` |

### 15.3 P0/P1 environments, and a deviation

At P0 and P1 a no-firm submission is refused at intake, so the persister never writes the column and firm creates never name it: a P0/P1
environment without the column still persists submissions. **Deviation:** the readers name the column for every invoice, so in an environment where
the column does not exist yet, approve, Return to Firm, correction and AP export answer 503 (fail closed) until the script runs or SpaarkeMaster is
imported. The POML expected P0/P1 to work without the column. I did not gate the read on the phase, because a phase probe that reads wrong would then
skip the hold. **Order:** run the script (or import SpaarkeMaster) before deploying this BFF build. Owner choice if that order cannot be kept: a
phase-gated read at P0/P1.

**Full route list (pass-1 review K4, read from the code).** While the column is missing, each of these answers 503 (a read fault is never "not held"):

| Route | Entry point | Hold read | Answer |
|---|---|---|---|
| Approve | `InvoiceApprovalService` ~:534 | `CheckAsync` | 503 unavailable |
| Approval-state GET | `InvoiceApprovalEndpoints.cs` ~:83 (`GetApproval` catch) from `InvoiceApprovalService.GetAsync` ~:326 | `CheckAsync` | 503 unavailable |
| Return to Firm | `InvoiceRejectReturnService` ~:423 | `CheckAsync` | not decided (unavailable) |
| Line and header correction | `InvoiceCorrectionService` ~:407 and ~:700 | `CheckAsync` | unavailable |
| AP export: create batch and re-check | `ApExportService` ~:331 and ~:440 | `HeldInvoicesAsync` | unavailable |
| AP candidates GET | `ApExportEndpoints.cs` ~:199 (`GetCandidates` catch) from `ApExportService.ListCandidatesAsync` ~:575 | `HeldInvoicesAsync`, only when Approved, unbatched invoices exist | 503 unavailable |
| Adjustment rule options, and a rule-citing adjustment add (review K2) | `InvoiceAdjustmentService` ~:259 and ~:469 | `CheckAsync` | unavailable |
| Intake correction naming a firm (review K1) | `InvoiceSubmissionService.JudgeCorrectionAsync` ~:1687; persister commit ~:1237 | `IsHeldAsync` | 503 record_unavailable at the intake; Retry at commit |
| **Correction prefill GET** (pass-2 review, item 1) | `GET /api/finance/invoices/{invoiceId}/correction-prefill`: `InvoiceSubmissionFromEmailHttp.GetCorrectionPrefillAsync` ~:603-606 calls `InvoiceSubmissionService.JudgeCorrectionPrefillAsync`, which calls `JudgeCorrectionAsync` ~:1682-1694 | `IsHeldAsync`, on **every prefill of an invoice that has a firm** (since pass-1 K1) | 503 (`PrefillUnavailable`); the Outlook add-in's correction prefill fails |
| **Adjustment add without a rule** (pass-2 review, item 5) | `InvoiceAdjustmentService.ApplyAddAsync` ~:467 | `CheckAsync`, now for every add (before: only a rule-citing add) | `Unavailable` (`ReadFailed`) |

**The POML's P0/P1 expectation ("the column can be absent without blocking ordinary approval") is not met.** Approval, the approval-state read, the
AP candidates list, and (after review K1 and K2) a firm-naming intake correction and the adjustment rule options all fail closed on an environment
without the column, at every phase. The mitigation is the binding deploy pre-check in 15.9 (the column exists, is secured, and carries the Writers and
Readers grants before any BFF build with 095c-5 deploys), plus the SpaarkeMaster import on new environments. **Owner decision pending:** whether this is
acceptable, or whether a phase-gated read at P0/P1 is wanted instead. Code change for K4: none.

**Pass-2 review (item 1): a third read page, and a fourth route.** The owner's acceptance in 15.11 ("two read pages - yes proceed") named the
approval-state GET and the AP candidates GET. The correction prefill GET is a **third** read page that answers 503 while the column is missing: since
pass-1 K1 it reads the hold on every prefill of a firm invoice. (The Outlook add-in's correction prefill is the reader; see the notice in 15.12.) The
same mitigation covers it, unchanged: the 15.9 pre-check (the column exists, is secured, carries the Writers and Readers grants) before any BFF build
with 095c-5 deploys, plus the SpaarkeMaster import on new environments. The rule-less adjustment add (last row) is a **fourth** route that now reads the
hold; it is an add on an In Review invoice, not a read page, and the same mitigation covers it. Neither is a code change.

### 15.4 Component justification (CLAUDE.md section 11)

- **`sprk_firmhold`.** Existing: the submission's stored hold. Extension: not enough, because the link from submission to invoice is the bypass. Cost
  of nothing: an unlinked, repointed or deleted submission lets an invoice without a firm be approved and exported.
- **`IsInvoiceHeld`, `InvoiceHoldColumn`, `InvoiceHoldValue`** extend the existing `InvoiceFirmHold`.
- **`FirmDetailFor` in the approval, Return and correction codes, and `FirmMissingStop` / `SelectionRefusedDetail` in AP export** are new methods in
  existing classes. Cost of nothing: a held invoice that shows a firm is told it "has no firm" (K1).
- **The script.** Existing: the findings-JSON script secures an existing column; this one must also create and backfill. Cost of nothing: no way to
  create the column on an environment.
- **The ArchTest and the agreement test.** Existing: the findings-JSON guard, which is fixed to its column. Cost of nothing: a new caller-token reader
  reads null and lets a held invoice through; or the script and the source drift apart silently.

### 15.5 -WhatIf on spaarkedev1 (read-only)

Column absent. Preconditions all OK; both BFF application users are System Administrators. 6 submissions are linked to invoices; 0 of their
invoices are held. **BACKFILL COUNT 0.** 8 invoices have no firm (legacy rows; already refused as no firm). No form or view binds the column, and no
other profile names it, so the escalation trigger (an existing reader blanked) did not fire. "DRY RUN complete - nothing was written."

Main session, with the owner's go and the uac-r2 notice:

```
pwsh scripts/ebilling/Set-EbillingInvoiceFirmHoldColumn.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Apply
pwsh scripts/ebilling/Set-EbillingInvoiceFirmHoldColumn.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify
```

**Pass-1 review K3: -WhatIf again, after the backfill was extended (2026-11-04, spaarkedev1, read-only, no `-Apply`).** Preconditions all OK as above; the
column is still absent. 6 submissions are linked to invoices. **BACKFILL COUNT (submission hold): 0. BACKFILL COUNT (empty firm): 8** (the same 8 legacy
invoices that have no firm). Distinct invoices `-Apply` would write: 8. "DRY RUN complete - nothing was written." `-Apply` was not run.

### 15.6 095c-4 review fixes

- **F1.** The AP Export page shows its own text for `invoice_firm_missing` (`exportFlow.ts`, jest test), and the `apExportApi.ts` comment lists it.
  The selection sentence now fits the reasons (contract section 12).
- **Related: `sprk_invoice.sprk_vendororg` refused to the generic writers** (AccessColumns; `AccessTableAppOnlyWriterTests`). The legitimate BFF
  writers, none of which goes through a generic writer: the persister's invoice create (`InvoiceSubmissionPersister.FillInvoice`, app-only
  transaction); 095d's assign-firm (planned, typed); the SPA intake writes the submission's firm through the allocator, not the invoice's;
  `InvoiceExtractionJobHandler` (legacy) does not write it. Client code (ApExport, LegalWorkspace, SmartTodo) only reads it. The field-mapping push asks
  as the caller, so it is unaffected.
- **K1.** Each route chooses its sentence by `InvoiceFirmNeed`: NoFirm keeps the 095c-4 sentence, Held says the invoice is waiting for its firm to
  be assigned (approve, Return to Firm, correction, AP export re-check). Contract section 12 has both columns.
- **K2.** `ApExportService.ListCandidatesAsync` leaves out no-firm and held invoices, using the same reader. A hold read fault throws (503), never the
  unfiltered list.
- **K3.** `IsHeld` also treats `ArgumentException` and `InvalidOperationException` from reading the findings as held (a duplicated member throws
  lazily on read). Duplicate-key test added.
- **K4.** Contract section 12: "Every refusal writes nothing" now names its one exception, the "firm cleared after the check" row, which saves the
  value and does not re-run the rules.
- **Pin.** The reason rewritten and the invoice firm set with the findings unchanged: approval stays 409 and the export stays refused (unit and
  integration tests).

### 15.7 Tests and seed proofs

New: `InvoiceFirmHoldColumnTests` (unit), `ApExportFirmHoldColumnTests`, `InvoiceFirmHoldFieldSecurityAgreementTests`,
`InvoiceFirmHoldAppOnlyGuardTests`, the persister test `HeldOutcome_SetsTheInvoiceHold_InTheSameTransaction`, the AccessTableAppOnlyWriter invoice
test and the jest firm-missing test. Changed: the AccessTableAppOnlyWriter map pin and its must-not-fire cases (the invoice's `sprk_vendororg` is now
refused), and the correction Held test (its sentence).

Seed proofs (each mutation applied, built, the named tests run, the file restored and its SHA-256 checked):

| Seed | Mutation | Result | File SHA-256 (restored, same) |
|---|---|---|---|
| M1 | `InvoiceFirmHold`: the invoice-side read ignored | 10 of 26 fail (`InvoiceFirmHoldColumnTests`, `ApExportFirmHoldColumnTests`: unlinked, repointed, deleted, invoice hold alone, caller-token null) | c62c914c944c257c |
| M2 | `InvoiceFirmHold`: the invoice read fault swallowed | 3 of 26 fail (missing column, read fault) | c62c914c944c257c |
| M3 | `IsHeld` catches only `JsonException` (K3) | 3 of 3 fail (`DuplicateKeys_Hold`) | c62c914c944c257c |
| M4 | the persister stops setting `sprk_firmhold` | 1 of 1 fails (`HeldOutcome_SetsTheInvoiceHold_InTheSameTransaction`) | 0ee16de7bbf2aaeb |
| M5 | `AccessColumns` without the invoice's two columns | 2 of 2 fail (the invoice write refusal, the map pin) | 89e0e97141400797 |
| M6 | `ListCandidatesAsync` unfiltered (K2) | 1 of 1 fails (`Candidates_LeaveOutEveryInvoiceTheExportRefuses`) | 5381d3ea69a8e089 |
| M7 | a new unreviewed type names `sprk_firmhold` | 1 of 9 fails (ArchTest C1) | 7c93408449f49bae |

### 15.8 Known limits

- The deviation in 15.3.
- Adjustments: the 14.6 limit is closed by pass-1 review K2 (15.10). The rule options and a rule-citing add now use `InvoiceFirmHold.CheckAsync` (app-only)
  and refuse NoFirm or Held with `ebilling.adjustment.firm_missing`, the held sentence for Held, as approve does.
- A human System Administrator can always write a secured column (K6). The app-only read returns null if a BFF application user is neither in the
  Writers profile nor a System Administrator, and a null reads as "no hold". The script's `-Verify` precondition p2 (the profile's members are exactly
  the BFF application users) and p6 (each BFF user's System Administrator role), and the dev setup (both BFF application users hold System
  Administrator), cover this.
- The correction prefill (`JudgeCorrectionPrefillAsync`) asks the intake guard with the invoice's own firm; for an invoice held with a firm set natively
  the guard now refuses, and the prefill reports it as `no_submission`, not as a held invoice. A read fault in the guard is passed on as the prefill's 503.
- **Script `-Verify` and the empty-firm list (pass-2 review, item 3).** A user can still write `sprk_vendororg` after deploy, so an ordinary invoice
  whose firm was cleared later has no firm and no hold. The BFF already refuses such an invoice as NoFirm. `-Apply` still backfills it (idempotent), but
  `-Verify` now reports that list as INFO ("-Apply sets it"), not as a failure. The submission-hold gap (an invoice a submission holds, with no hold of
  its own) stays a FAILURE.
- **Script backfill finds held submissions through the derivation as well as the link (pass-2 review, item 4).** I chose to port the derivation. Reason:
  `InvoiceSubmissionPersister.InvoiceIdFor` is deterministic (the first 16 bytes of SHA-256 of `ebilling-invoice-<submission id, D format>`, read as
  `new Guid(byte[16])`), so PowerShell reproduces it exactly with `[Guid]::new([byte[]]$bytes[0..15])`; `InvoiceSubmissionPersisterTests.InvoiceIdFor_MatchesTheBackfillScriptsPort`
  pins three vectors produced by the PowerShell function against the C#, and the agreement test pins the key text. The script now reads every
  submission (no `_sprk_invoice_value ne null` filter) and holds the invoice its link names **and** the invoice its id derives, only when that invoice
  exists (a PATCH on an id that does not exist would create a row). Dev: 10 submissions, 0 held, so the derivation found nothing extra there.
- **Script p0 checks the length (pass-2 review, item 2).** An existing column must be a String with `MaxLength` of at least the hold value's length (32);
  otherwise p0 FAILs in every mode and `-Apply` is refused. There is no Pester file for this script (the agreement test is the only automated check on
  it), so the check is pinned by a source-scan case in `InvoiceFirmHoldFieldSecurityAgreementTests` and was run on the helper with lengths 31, 32, 100 and
  unreadable.
- current-task.md, TASK-INDEX.md and the POML status are not updated here; the main session owns them.

### 15.9 Deploy order (uac-r2, 2026-11-04, binding)

uac-r2 accepted the deviation in 15.3. When the column is missing, approval and export answer 503. That fails closed, and the read is not gated on the phase.

Before deploying any BFF build that carries 095c-5, uac-r2 reads the attribute metadata on dev. All three must hold:
- `sprk_invoice.sprk_firmhold` exists;
- it is field-secured;
- it carries the Writers grant (read, create, update) and the Readers grant (read).

If any of these is missing, uac-r2 does not deploy. The column is created by `Set-EbillingInvoiceFirmHoldColumn.ps1 -Apply` (or, on a new environment, by the SpaarkeMaster import). It runs in the ebilling main session, after the owner's go, with notices sent before and after.

A second, independent pre-check (task 095d-1a, 16.6.1) applies before any BFF build that carries 095d-1: `Deploy-EbillingSubmissionPreviousSequenceColumn.ps1 -Verify` exits 0 (`sprk_invoicesubmission.sprk_previoussequence` exists, a whole number, not secured).

### 15.10 095c-5 pass-1 review (2026-11-04)

Branch `task/ebilling-095c-5-fix1`. Findings are described as defect and location only; the steps to reach them are not recorded here.

| # | Defect | Location | Disposition |
|---|---|---|---|
| K1 | A correction compared the writable firm columns only, so a held invoice whose firm column was set could be superseded by a correction naming that firm, by a new invoice with no hold | `InvoiceSubmissionService.JudgeCorrectionAsync` (~:1673, intake) and `InvoiceSubmissionPersister` (~:1235, commit) | **Fixed.** Both read the hold app-only (`InvoiceFirmHold.IsHeldAsync`) when the request names a firm and treat a held target like a no-firm target: refused `different_firm` (409; `CorrectionDifferentFirm` at commit), the same code and sentence as the existing refusal. A hold read fault is 503 `record_unavailable` at the intake and Retry at commit. A held no-firm invoice corrected without a firm is still allowed (it stays held). Tests: `InvoiceCorrectionIntakeTests` (K1 block), `InvoiceSubmissionPersisterTests.NoFirm` (K1 block). Seed proof: the intake held check removed, 2 of 2 must-fire cases fail; restored, file SHA-256 identical. |
| K2 | Adjustments checked the firm column only | `InvoiceAdjustmentService` (~:251 rule options, ~:457 cited add) | **Fixed.** Both use `InvoiceFirmHold.CheckAsync` (app-only) and refuse NoFirm or Held with the existing `ebilling.adjustment.firm_missing`; the sentence follows the need (`AdjustmentCodes.FirmDetailFor`), as approve does. A fault is `Unavailable`. Tests: `InvoiceAdjustmentServiceTests` (held with a firm: rule options and cited add refused; ordinary firm: allowed). |
| K3 | A legacy invoice with no firm has no hold of its own and no linked submission hold | `Set-EbillingInvoiceFirmHoldColumn.ps1` (e) (~:465-472) | **Fixed in the script.** The backfill also sets the hold on every invoice with an empty `sprk_vendororg` and a blank `sprk_firmhold`. `-WhatIf` reports the submission-hold and empty-firm counts separately (15.5: 0 and 8 on dev). `-Verify` failed for either gap (pass-2 review item 3: now only the submission-hold gap fails; the empty-firm list is INFO). Idempotent: a second `-Apply` finds both lists empty. No script test pins the backfill query (the agreement test pins the hold value and the profiles). |
| K4 | The deviation's route list was incomplete | notes 15.3 | **Notes only.** The full list is in 15.3, including the approval-state GET and the AP candidates GET. The POML's P0/P1 expectation is not met; mitigation is 15.9 and the SpaarkeMaster import. **Owner decision pending.** |
| K5 | Tests proved less than their names | `InvoiceFirmHoldColumnTests` (~:102, ~:177-181) | **Fixed.** The "correction state" name was dishonest (the test covers approve and Return to Firm): renamed, and `InvoiceCorrectionServiceTests.HeldByTheInvoiceColumnAlone_Correction_Is409FirmMissing_WritesNothing` covers `CorrectAsync` (line and header) with the invoice hold alone and a firm set. The caller-token test's route half duplicated `TheInvoiceHoldAlone_IsHeld` and was removed; the name now says what it proves (`CheckAsync` answers Held). |
| K6 | The app-only read depends on the BFF user's profile | notes 15.8 | **Notes only.** Bullet added to 15.8. |

Publish size: no new package, endpoint or DI registration, so no publish-size delta applies.

### 15.11 Applied on dev (2026-11-04)

- **Owner go** typed in the main session on 2026-11-04 ("095c - yes proceed"). The owner also accepted the 503 on the approval-state GET and the AP candidates GET wherever the column is missing ("two read pages - yes proceed"). This resolves the pending decision in 15.3. The mitigation is the pre-check in 15.9, plus the SpaarkeMaster import on new environments.
- **Ran** `Set-EbillingInvoiceFirmHoldColumn.ps1 -BffApplicationIds 5967251e-…,1e40baad-… -Apply`:
  - The column was created in SpaarkeMaster and secured.
  - Writers hold read, create and update; Readers hold read only. No other profile can create or update it.
  - The column was masked for 27.1 s while the grants were applied.
  - Only `sprk_invoice` was published.
  - The backfill set the hold on 8 invoices: 0 held through a submission, 8 with no firm.
- **`-Verify` passed** at 2026-11-04T14:16:03Z with 0 gaps remaining.
- **uac-r2 was notified** before and after (session 20; the copy sent to session 09 was held by that session).

### 15.12 095c-5 pass-2 review (2026-11-04)

Branch `task/ebilling-095c-5-fix2`. Findings are described as defect and location only; the steps to reach them are not recorded here.

| # | Defect | Location | Disposition |
|---|---|---|---|
| 1 | The correction prefill is a third read page that answers 503 without the column; the notes named two | `InvoiceSubmissionService.JudgeCorrectionAsync` ~:1682-1694, reached from `InvoiceSubmissionFromEmailHttp.GetCorrectionPrefillAsync` ~:603-606 | **Notes only.** Row added to the 15.3 route table; the 15.11 acceptance named two pages and the 15.9 pre-check covers this third one; the reader table (15.2) lists the adjustment, intake, prefill and persister callers; notice drafted below. |
| 2 | Precondition p0 checked the type of an existing column, not its length | `Set-EbillingInvoiceFirmHoldColumn.ps1` p0 (~:192) | **Fixed.** p0 also requires `MaxLength` of at least the hold value's length and fails with a message naming both. No Pester file exists for the script; pinned by a source-scan case in `InvoiceFirmHoldFieldSecurityAgreementTests`. |
| 3 | `-Verify` failed on an invoice with no firm and no hold, which is an ordinary state after deploy | `Set-EbillingInvoiceFirmHoldColumn.ps1` (e) (~:481-489 before) | **Fixed in the script.** `-Verify` reports the empty-firm list as INFO with the count and "-Apply sets it"; `-Apply` still backfills it; the submission-hold gap stays a FAILURE. Script help and 15.8 updated. |
| 4 | The backfill found held submissions only through their link | `Set-EbillingInvoiceFirmHoldColumn.ps1` (e) query (~:468 before) | **Fixed in the script** by porting `InvoiceSubmissionPersister.InvoiceIdFor` (15.8 says which choice and why); test vectors in `InvoiceSubmissionPersisterTests`. A simulated run (four submissions: link cleared, linked, derived invoice absent, not held) found the link-cleared one, skipped the absent invoice and wrote nothing for the non-held one. |
| 5 | A rule-less adjustment copied an unconfirmed firm onto its line when the invoice was held for its firm and its firm column was set directly | `InvoiceAdjustmentService` `BuildRow` (~:638) | **Fixed.** `ApplyAddAsync` reads `InvoiceFirmHold.CheckAsync` once (before: only for a cited rule) and uses the one result for both the cited-rule refusal and the firm copy; the line takes the firm only when the answer is None. A fault on that read is `Unavailable` for every add (see the fourth route in 15.3). Tests: `InvoiceAdjustmentServiceTests` held with a firm, rule-less, line has no firm (must fire; fails on the old code); ordinary invoice, rule-less, line has the firm (must not fire). |

Publish size: no new package, endpoint or DI registration, so no publish-size delta applies.

**`-Verify` against spaarkedev1 (read-only, 2026-11-04, after items 2 to 4): PASS, exit 0.**

```
Environment : https://spaarkedev1.crm.dynamics.com (org 'spaarkedev1')
Mode        : VERIFY (read-only)

Preconditions
  OK       (p0) sprk_invoice.sprk_firmhold exists (String, MaxLength >= 32) and can be secured for read, create and update
  OK       (p1) profile 'Spaarke BFF-Managed Field Writers' exists (917b64c5-47bf-f111-a05b-3833c5e9614d)
  OK       (p7) profile 'Spaarke BFF-Managed Field Readers' exists (42e542c8-47bf-f111-aaaf-0022482913fc)
  OK       (p1) 'Spaarke BFF-Managed Field Writers' is in SpaarkeMaster
  OK       (p7) 'Spaarke BFF-Managed Field Readers' is in SpaarkeMaster
  OK       (p2) BFF app user '# mi-bff-api-dev' is a member of Spaarke BFF-Managed Field Writers
  OK       (p2) BFF app user 'SDAP-BFF-SPE-API' is a member of Spaarke BFF-Managed Field Writers
  OK       (p3) no business-unit default team holds Spaarke BFF-Managed Field Writers
  OK       (p4) no field-mapping rule, AI topic-registry row or email update field targets sprk_firmhold
  OK       (p5) sprk_invoice.sprk_firmhold is in SpaarkeMaster (ViaTable)
  INFO     (p6) 8 sprk_invoice row(s) carry a firm hold now
  INFO     (p6) no form or system view binds sprk_firmhold (no existing reader to blank)
  INFO     (p6) BFF app user '# mi-bff-api-dev' holds System Administrator (full field access; no masked window for it)
  INFO     (p6) BFF app user 'SDAP-BFF-SPE-API' holds System Administrator (full field access; no masked window for it)
  OK       (p8) reader profile on default team 'Secure Record' (BU 'Secure Record')
  OK       (p8) reader profile on default team 'Spaarke Business Unit 1' (BU 'Spaarke Business Unit 1')
  OK       (p8) reader profile on default team 'Spaarke Demo' (BU 'Spaarke Demo')
  OK       (p8) reader profile on default team 'Spaarke Dev 1' (BU 'Spaarke Dev 1')
  OK       (p8) reader profile on default team 'Spaarke Test 1' (BU 'Spaarke Test 1')
  OK       (p8) reader profile on default team 'Spaarke' (BU 'Spaarke')
  OK       (p9) the operator (1d02f31c-1872-f011-b4cb-7c1e52671ad0) holds System Administrator: the backfill reads the field-secured submission findings

(s) The column
  OK       sprk_invoice.sprk_firmhold exists
(a) Field-level security
  OK       sprk_invoice.sprk_firmhold is field-secured
(b) Permissions on sprk_invoice.sprk_firmhold
  OK       Spaarke BFF-Managed Field Writers (read=4 create=4 update=4)
  OK       Spaarke BFF-Managed Field Readers (read=4 create=0 update=0)
(c) Other profiles
  OK       no profile besides 'Spaarke BFF-Managed Field Writers' and the System Administrator profile (572329c1-a042-4e22-be47-367c6374ea45) can create or update sprk_invoice.sprk_firmhold ('Spaarke BFF-Managed Field Readers' is checked in (b): read only)

(e) Backfill
  INFO     10 submission(s) read; 0 held for a firm; 0 invoice(s) held by a submission (0 found only through InvoiceIdFor, not through the link); 8 invoice(s) with no firm
  INFO     BACKFILL COUNT (submission hold): 0 invoice(s) held by a submission need sprk_firmhold = ebilling.submission.firm_missing
  INFO     BACKFILL COUNT (empty firm): 0 invoice(s) with no firm need sprk_firmhold = ebilling.submission.firm_missing
  INFO     BACKFILL COUNT (distinct, written by -Apply): 0 invoice(s)
  OK       every invoice a submission holds for a firm carries its own hold

PASS: sprk_invoice.sprk_firmhold exists and is field-secured, the BFF writer profile holds read/create/update, the reader profile holds read, no other
profile (the System Administrator profile excepted) can create or update it, and every invoice a submission holds for a firm carries its hold.
```

(The "Column :" header line is omitted; long lines are unwrapped as printed.) The empty-firm path (an invoice with no firm and no hold, reported as INFO) is not
reached on dev, where the 8 no-firm invoices already carry their hold; it was exercised in a simulated run (see item 4).

**Notice for word-add-in-r1 (draft; the main session sends it).**

> Subject: ebilling-r1 095c-5 - the correction prefill answers 503 on an environment without `sprk_invoice.sprk_firmhold`
>
> `GET /api/finance/invoices/{invoiceId}/correction-prefill` now reads the invoice's firm hold (app-only) for every invoice that has a firm. On an
> environment where the column `sprk_invoice.sprk_firmhold` does not exist, the read faults and the route answers 503 (the existing "prefill
> unavailable" answer; nothing is written). Dev has the column since 2026-11-04 14:16Z, so dev answers normally. No contract change: the
> request, the response shape, and the 503 body are unchanged. Handle the 503 as you already do for the prefill's other read faults (offer a retry; do
> not treat it as "no submission"). Any other environment gets the column from the deploy pre-check (15.9) or the SpaarkeMaster import before a BFF
> build with 095c-5 deploys.

## 16. Part 095d: design stop (2026-11-04)

Task `tasks/095d-set-invoice-firm.poml`. The implementer stopped before writing code (escalation). Nothing was changed.

### 16.1 The conflict

Renumbering a submission into the firm's sequence is safe under concurrency in exactly one form: a single transaction, conditional on the row version, that writes the submission's firm, scope, sequence and tracking number together with the invoice's firm, `sprk_firmhold` and the lines' firm. Dataverse enforces the scope alternate key on update, so a lost race fails the whole transaction and the allocator retries.

That leaves three rules of the task that cannot all hold:

- **Record first** (ontology D-201, K-3): the number is stored with the decision before the Decision Record, and the record never differs from the applied number.
- **Nothing changes on failure** (acceptance criterion).
- **Safe under concurrency:** a number is reserved only once it is written under the scope key, and the Decision Record writer (`WriteBillingDecisionAsync`) cannot join that transaction.

| Option | Order | Breaks |
|---|---|---|
| 1. Apply, then record | One apply transaction (with retry), then the record, then close the firm_missing Signal, then re-run the rules | Record first (K-3). If the record write fails, the firm already sees the submission before any record exists. A retry must finish the job. |
| 2. Reserve, record, apply (**recommended**) | Write only the scope, number and stored decision (the firm stays null, so the submission is not visible yet); then the record and the Signal close; then the firm, hold and lines transaction | "Nothing changes on failure": the number changes at the reservation. The in-between row has a scope that is not its firm, which the strict scope check flags. uac-r2 condition (e) may allow this as a state that the census or a retry can see. |
| 3. D-201 literally | Pick the number in memory, store it, record, apply | Unsafe: a concurrent intake can take the number. |
| 4. Keep the number | No renumber | Unsafe: it breaks the strict scope check and lets the allocator reuse the `.n`, or collide with the firm's own number. |

For option 2, the criterion would be reworded to: "A failure leaves a state that a retry finishes and that never shows the submission to the firm."

### 16.2 Size: split proposed

- **The rules re-run needs a second-evaluation path in the persister.** Today a linked submission resumes and writes nothing (`ResumeAsync`). The re-run covers: `rules.not_checked` flags, the line-decision reset (§14.8 item 2), Duplicate and supersedes, new Signals, and the held set.
- **The number appears in several places:** the invoice tracking-number column, the line names, the approval name, the stored-file names and the findings `trackingNumber`. Owner question: rename all of them, or only the two number columns?
- **Proposed split:**
  - **095d-1** (Opus): renumber, route, the atomic write in the chosen order, the decision code, the Signal close, guards, race and seed tests.
  - **095d-2** (Opus, after 082 and 092 merge): the persister's second evaluation.
  - **095d-3** (Sonnet): the "Set firm" UI and its tests.

### 16.3 Open questions

- **Owner, ontology-r1 (K-3), uac-r2 (condition (e)):** which order, 1 or 2?
- **Owner:** which names carry the new number?
- **Re-run in the request:** confirm the re-run makes no AI call. Only the code-set resolver's firm-code step runs again.
- **Approval afterwards:** Flagged lines need a line decision before approval. Is that what the criterion intends?
- **word-add-in-r1:** a 095a read or replay returns the new number. The add-in must not cache the number from intake.

### 16.4 Decisions (2026-11-04)

- **Order: option 1, apply then record.** The owner chose it in the main session after reading both peer analyses. They had first picked option 2, then changed to option 1 once the peers' views arrived, and confirmed it ("that's fine as long as have confirmed the decision analysis"). ontology-r1 recorded its reading of D-201 in its `notes/decisions.md` (`269cfd8221`): the record names the stored number, a retry never allocates a second number, and approval already applies first. uac-r2 accepts option 1.
- **Binding conditions** (ontology-r1 and uac-r2):
  1. Every permanent refusal is checked before the apply transaction: Preflight, the HumanDecisionSignalClose authorization, the core-record derivation and Read on the firm, as for approval.
  2. The stored decision goes in the same apply transaction, conditional on the row version. It holds the firm, note, previous and new number, the firm_missing Signal set, its read time and the decider. A repeat rebuilds the identical record (same id and hash) from the row and never reallocates.
  3. A failure after the apply answers 503 `incomplete` with `failedStep`. The repeat finishes record → close → link → re-run without a second apply, and a repeat that names a different firm is refused.
  4. The firm_missing Signal is closed only after the record exists (D-122).
  5. Rows that are applied but have no record are reported by the census or the schema check: INFO inside a stated window, FAIL after it.
- **Renumber: everything.** The new number goes everywhere it appears: the submission and invoice number columns, the line names, the approval name, the stored-file names and the findings `trackingNumber`.
- **AI in the re-run** (the owner's direction, relayed by uac-r2 and confirmed here): re-run the AI only where a rule's outcome can change with it. If a rule does not depend on the AI analysis, do not call the AI; if a fresh AI run can change the outcome, re-run it. The PR and notes list each rule in the re-run as "AI: yes/no" with the reason.
- **Final review before merge:** the main session's adversarial passes, then uac-r2's full review of the diff, then ontology's K-3 view.
- **word-add-in:** the add-in must not cache the intake number, and the 095a read returns the applied number.
- **Split:**
  - 095d-1: renumber, route, atomic apply, record, Signal close, guards, incomplete reporting.
  - 095d-2: the persister's second evaluation and the rule re-run, after 082 and 092 merge.
  - 095d-3: the UI.

### 16.5 095d-1 (2026-11-04): task file written; stopped before code

Task file: `tasks/095d1-set-firm-apply.poml`. It is scoped to 095d-1. It takes in the 16.4 conditions, the uac-r2 additions and ontology-r1's three binding additions (renames, the transaction ceiling, the old-to-new mapping in the stored decision). It states that 095d-1 makes no AI call. The implementer stopped before writing code, for two reasons: two decisions are needed, and the task is too big for one agent. Nothing outside the task file and this section was changed.

**Facts read from the code (for the decisions):**

- **The firm_missing Signal is shared.** `BillingFindingSignalMapper.MapGroup` writes one SubmissionIntegrity Signal per (policy, invoice) group. The firm hold shares that Signal with the invoice-number mismatch and the coding notes; `NotePrecedence` (~:425) only makes it the title. Closing "the firm_missing Signal" after the 095d-1 record therefore also closes those notes. Nothing raises them again until the 095d-2 re-run. This sits uneasily with D-201: "ResolvedSignalIds holds only the firm_missing Signal(s), never other open findings."
- **Stored-file names live in Dataverse.**
  - The display name is `sprk_storedfile.sprk_name` (`StoredFileSchema.Name`; written by `StoredFileService.BuildRow` ~:271).
  - The blob path is built from ids only (`StoredFileBlobName.Build`: tenant, parent table, parent id, file id), and the blob metadata holds no name.
  - So a rename is a Dataverse column update, and it can join the apply transaction. uac-r2 allows this ("inside the apply transaction where the columns allow it"). In the transaction, "never named with another firm's sequence" holds by construction, and the `files` failedStep and the old-number census row disappear.
  - It does need a name-only entry in `InvoicePdfWriterGuardTests.ApprovedWriters`. That guard allows only BuildRow and two status-only updates today.
- **Transaction ceiling.** The intake commit is already bounded at `InvoiceSubmissionPersister.MaxRequestsPerTransaction` = 1000 (~:100, ~:702). It covers the invoice create, the approval, the N lines, the files (`SubmissionCommitGate.MaxFiles` = 50) and the submission. The apply is 1 submission + 1 invoice + 1 approval + N lines (+ F files if renamed in it), so any invoice the intake created fits. Lines are added later only by adjustments, and those are refused while the invoice is held. The ceiling check before the apply is therefore a defensive refusal with tests at the ceiling and one above it, not a split.
- **Census.** There is no census script. The reporting can go in an existing `-Verify`: `Deploy-EbillingSubmissionTrackingScopeSchema.ps1` (the scope phases) or `Set-EbillingInvoiceFirmHoldColumn.ps1` (the hold). The latter already reads the findings, so it is preferred.
- **After 095d-1 alone,** the invoice stays held. The three submission markers (14.8 item 1) are cleared only by the 095d-2 re-run. Approval keeps answering 409 (Held sentence) until then. That is safe, but 095d-1 must not merge to master without 095d-2.

**Decisions needed:**

1. **ontology-r1:** close the shared Signal in 095d-1 anyway (095d-2 raises the remaining notes again as new D-13 keys), defer the close to 095d-2 after the re-run, or close it only when firm_missing is its only reason. **Recommendation:** defer the close to 095d-2. The record then names the set, and 095d-2 closes it after the re-run has written the replacement notes. Condition 4 (close only after the record) still holds.
2. **ontology-r1 (their addition 1 assumed the file store is not Dataverse):** put the file renames in the apply transaction (recommended, for the reason above) or keep them after the record.

**Proposed split** (each part is one agent, run in this order):

- **095d-1a:** `TrackingNumberAllocator.RenumberSubmissionAsync`. It takes the companion requests built per number, sends them in one transaction with IfRowVersionMatches, retries on the alternate key, skips hidden numbers, runs only at P2, and checks the ceiling. Tests: races, ceiling at and above, row-version conflict. It does not touch the persister.
- **095d-1b:** the service and route. The refusals, the stored decision member and the apply through 1a. The decision code and the exact-set guard, ParentLineage, the hold guard and the stored-file guard entries. A must-fire and a must-not-fire test for each refusal, and seed proofs for the before-apply check and the hold clear.
- **095d-1c:** the completion step. The record, then the close (per decision 1), the link, 503 incomplete with failedStep and the repeat, and the `-Verify` INFO/FAIL window. A seeded failure at each step, then a repeat. Then the code-review and adr-check gates over 1a-1c, and the publish-size measurement.

rework: none task 095d-1

### 16.6 095d-1a (2026-11-04): the allocator renumber

Branch `task/ebilling-095d-1a` (from `work/ebilling-r1` 0a37980d9d). Only the allocator, its interface, its tests and one ArchTests fixture changed. `InvoiceSubmissionPersister.cs` is untouched (its constant is referenced, not copied). No package, no DI registration, so no publish-size measurement for 1a; 1c measures 1a-1c together.

**The API 1b calls** (`ITrackingNumberAllocator`):

```csharp
Task<RenumberResult> RenumberSubmissionAsync(
    Guid submissionId, string rowVersion, Guid firmId, string firmInvoiceNumber,
    Func<RenumberCandidate, RenumberApply> buildApply, CancellationToken ct = default);

record RenumberCandidate(string TrackingNumber, int Sequence);
record RenumberApply(IReadOnlyDictionary<string, object>? SubmissionFields, IReadOnlyList<OrganizationRequest> Requests);
record RenumberResult(RenumberOutcome Outcome, string? TrackingNumber, int? Sequence, string? Reason);
enum RenumberOutcome { Renumbered, AlreadyRenumbered, Conflict, Refused }   // AlreadyRenumbered: review F4

// The ceiling, asked before the apply:
static bool TrackingNumberAllocator.FitsRenumberTransaction(int callerRequestCount);
const int TrackingNumberAllocator.MaxRenumberCallerRequests; // InvoiceSubmissionPersister.MaxRequestsPerTransaction - 1 = 999
```

- **Who sends the transaction.** The brief said the method "returns the request(s) the caller adds to its apply transaction", and also that the allocator retries a lost race. Only the sender can retry, so the shape is the one 16.5 states: 1b BUILDS its part of the transaction per number (`buildApply`), and the allocator SENDS the one transaction. The transaction is the submission update (index 0: firm, scope, sequence, tracking number, plus 1b's `SubmissionFields`, at `rowVersion` with `IfRowVersionMatches`), then 1b's `Requests` in order.
- **What 1b puts where.** The findings `trackingNumber` and the stored decision go in `SubmissionFields`, because one row cannot be updated twice in one transaction under its row version. The invoice (firm, tracking-number column, `sprk_firmhold` clear, at its row version), the lines, the approval and the file renames go in `Requests`. `Requests` accepts only `UpdateRequest` and `CreateRequest` on a table other than `sprk_invoicesubmission` (an allowlist, review K3); any other request type, or `SubmissionFields` that name an owned column (`sprk_vendororg`, `sprk_trackingscope`, `sprk_submissionsequence`, `sprk_trackingnumber`, `sprk_firminvoicenumber`, `sprk_previoussequence` (K4), the id), throws `ArgumentException` before anything is sent.
- **The builder is called once per attempt** with that attempt's number, so every name 1b builds always matches the number written. It must be side-effect free. The stored decision's "new number" is `candidate.TrackingNumber`.
- **Outcomes 1b maps:**
  - `Renumbered`: committed; the result carries the number.
  - `AlreadyRenumbered` (review F4): the precondition read found the submission already in this firm's scope, holding a sequence, at the version the caller passed. Nothing was sent and the builder was not called; the result carries the held number. 1b finishes the steps after the apply (record, close, link, re-run) and never repeats the apply. It is a separate outcome, not a flag on `Renumbered`, as 045 has `Created` and `AlreadyCreated`: a `switch` on the outcome forces 1b to choose, and the caller's requests did not run in this call (they ran in the commit that moved the submission, in the same transaction).
  - `Conflict`: the submission is gone or is not at `rowVersion` at the precondition read, or a row-version mismatch anywhere in the transaction (the submission's or a caller row's). It is never retried, and nothing is written. 1b re-reads and answers "already applied for this firm" (finish: call again at the version it read) or 409. A repeat at the old version is always `Conflict`; a repeat at the version the commit left is `AlreadyRenumbered`. Neither allocates twice.
  - `Refused` + `ebilling.tracking.unavailable`: the retry bound was exhausted (`MaxAttempts` = 10 lost races, or `MaxHiddenSkips` = 64 skips). Retryable 503.
  - `Refused` + `ebilling.tracking.scope_key_inactive` (new): not P2. 503 (the "not phase P2" refusal in the 1b list). 1b checks it before the apply; this is the allocator's own fail-closed copy.
  - `Refused` + `ebilling.tracking.too_many_requests` (new): more than 999 caller requests. 1b refuses first with `FitsRenumberTransaction`; this is the fail-closed copy.
  - `Refused` + `ebilling.tracking.schema_missing` (new, K4): `sprk_previoussequence` does not exist in this environment yet. Found by the precondition read, before anything is sent. 503 (retry after the schema script has run); see 16.6.1.
  - `Refused` + `ebilling.tracking.not_unnumbered` (new, review F4): the submission is not a no-firm row in the `none` scope and is not already renumbered into the requested firm's scope (another firm's row, a row with a firm filed in some other scope, a row in another firm's scope). A repeat that names a different firm ends here. Not retryable; 1b answers a refusal.
  - `ArgumentException`: also a `firmInvoiceNumber` that is not the row's own (compared exactly after trimming; the caller passes the number from the read that gave the version).
  - Any other fault propagates on the first attempt, including an unknown outcome (a timeout). That is 045's rule; 1b's re-read decides.
- **Precondition read (review F4).** Before the loop (and after the P2 check) the submission is read by id: row version, scope, firm, sequence, tracking number, firm invoice number. The order of the decision: missing row or stale version, `Conflict`; firm invoice number not the row's, `ArgumentException`; already in this firm's scope with a sequence held, `AlreadyRenumbered`; not a no-firm row in `none`, `Refused` `not_unnumbered`; else go on. The update's `IfRowVersionMatches` then pins exactly the state that was read, so a change between the read and the write is the transaction's `Conflict`. Why: the conditional update alone cannot tell a repeat that must finish from one that must refuse. A caller that re-read after a `Conflict` holds the post-commit version, at which the update succeeds and would allocate a second number (and a repeat naming another firm would move the submission again).
- **Race.** It copies `CreateSubmissionAsync`'s loop: read the highest sequence in the firm's scope (P2: `sprk_trackingscope` = the firm id) for the firm invoice number, write n+1, and let the scope key (and the firm key, while it is not retired) refuse a taken number on update. A refused transaction rolls back whole, including 1b's apply. A race is an alternate-key fault (`0x80060892`) on the submission update (index 0, or no index), or `0x80040237` ("Cannot insert duplicate key") at index 0 (review K2: on that update the only unique indexes are the two keys). A key fault on a caller request (any other index) propagates, and so does `0x80040237` with no index.
- **Collision in the firm scope.** As 045 (task 094): a visible `{number}.{n}` is passed by the highest-sequence read; a number held by an undone attempt (its firm invoice number cleared, so the read cannot see it) is refused by the key, then skipped (`floor`), within `MaxHiddenSkips`, not counted as a lost race.
- **Fault classification.** `DataverseServiceClientImpl.IsRowVersionMismatch` is checked first. The key duplicate is `0x80060892` on the fault or any inner fault, then `IsAlternateKeyDuplicate`. The inner-fault walk is new: in an `ExecuteTransactionFault`, the request's own code can be the inner fault, which `IsAlternateKeyDuplicate` does not walk. **Known limit (live seam, review K2):** that this is where Dataverse puts the code for a key fault on update inside a transaction is not measured live. 1a makes no live writes. **The live seam test stays a merge gate for 1b/1c:** one live test (extend `TrackingNumberAllocatorLiveTests`) that renumbers two no-firm submissions into one firm scope at the same time and asserts BOTH the faulted request index AND the fault code (`0x80060892` or `0x80040237`) of the loser, then that the loser retried to the next n. Until it has run, the index/code pairs the allocator accepts are the two it was built for; a third pair would propagate as a 500 (fail-closed: nothing is written, no number is lost).

**Justification (CLAUDE.md 11).**
- **Existing:** `CreateSubmissionAsync` numbers only at create.
- **Extension:** this is that extension, in the same class, because the allocator is the only writer of the number columns (D-37, ADR-002). A second writer elsewhere would split the loop.
- **Cost of doing nothing:** assign-firm cannot renumber race-safely, because the number must be written under the scope key in the one apply transaction.
- The two reason codes are new contract strings. `FitsRenumberTransaction` reuses the persister's constant.

**Tests** (`tests/integration/data-mutation/FinanceSubmission/TrackingNumberAllocatorTests.Renumber.cs`, a partial of the 045 suite, whose in-memory table now also models a transaction: the row version, then both keys, on update, all or nothing, faults mapped by the real `MapTransactionFailure`):
- the happy path in the firm scope, and `.1` for a firm with no such number;
- a lost race retried to the next n, with one apply;
- the bound at 9 and 10 lost races;
- a row-version conflict on the submission, on a caller row, and a repeat at the old version;
- the hidden skip, and its bound;
- the ceiling at 999 (a 1000-request transaction runs) and at 1000 (refused, nothing sent), plus the static check;
- P0 and P1 refused;
- a caller key fault and another fault propagate;
- owned columns and submission targets refused;
- bad arguments.

**Seed proof.** With the key-duplicate `catch` removed, 5 of the 23 renumber tests fail (the lost race, the bound at 9 and 10, the hidden skip and its bound: the key fault propagates). Restored, all 24 pass (the 23 and the static ceiling test).

#### Fix round (adversarial review of 1a, 2026-11-04)

Commit `ffd4046de8` (code and tests); this section is the notes commit. Same branch, `task/ebilling-095d-1a`. Files: `TrackingNumberAllocator.cs`, `ITrackingNumberAllocator.cs`, the two test files. No package, DI or persister change, so still no publish-size measurement for 1a.

- **F4 (fixed): the precondition read.** Described above (the "Precondition read" bullet and the outcome list). New: outcome `AlreadyRenumbered`, reason `ebilling.tracking.not_unnumbered`. Before the fix a caller that re-read after a `Conflict` and repeated at the post-commit version ran the conditional update successfully and took a second number from the firm's sequence (and a repeat naming another firm would have moved the submission again). Now it gets the held number back (or a refusal), with nothing sent.
- **K1 (known limit, recorded; test added).** A key duplicate with no faulted index is treated as a race (kept). A fault with no index may be a concurrent request's fault surfaced by the shared client (GitHub #971), so the "race" can be a misclassified cause. The cost is one wasted attempt that skips a number. Nothing is double-written: the retry's update is conditioned on the row version, so if the first transaction had in fact committed, the retry is a `Conflict`, and the caller's re-read then reaches `AlreadyRenumbered`. 045's create has the same exposure (idempotent by submission id there). Test: no-index `0x80060892` is retried.
- **K2 (fixed, merge gate stated).** `0x80040237` at index 0 is a race as well (on the submission update the only unique indexes are the two keys). At index 1, or with no index, it propagates. Tests: index 0 retried, index 1 and no index propagate. The live seam test stays a merge gate for 1b/1c, as stated under "Fault classification" above: two renumbers into one firm scope, asserting both the faulted index and the code.
- **K3 (fixed).** `Requests` is an allowlist: `UpdateRequest` or `CreateRequest` on a table other than `sprk_invoicesubmission`. Any other request type (association, state change, delete, unknown), a null request, or a target-less one throws `ArgumentException` before anything is sent. The old denylist read only the `Target` parameter, so a `SetStateRequest` on a submission (its parameter is `EntityMoniker`) passed it; there is a test for exactly that request. Tests: each type refused, a create of a submission refused, a create on another table accepted.
- **K4 (RESOLVED by the owner, 2026-11-04: "New column, never reuse"): number reuse in the `none` scope.** After S is renumbered from `INV-7.1` (none) to `INV-7.3` (firm A), the `none` scope held no `INV-7.1`, so the next no-firm intake of `INV-7` was numbered `INV-7.1`: two submissions held `INV-7.1` over time. The key could not prevent it (S had left the scope), and no existing column kept the old number (`sprk_trackingnumber` and `sprk_submissionsequence` are overwritten; `sprk_firminvoicenumber` and `sprk_name` hold the number without its n; the old-to-new mapping is only in 1b's stored decision and the Decision Record). The review's options were A (a new allocator-owned column), B (an audit read of the Decision Record) and C (accept reuse). **The owner chose A, with the whole-number variant `sprk_previoussequence`, and "never reuse".** B and C are rejected; this replaces the earlier "stopped" options. The fix and its definition are in 16.6.1. Nothing in 1b or 1c changes (the allocator's public API is the same; the column is written inside `RenumberSubmissionAsync`), except that 1b maps one more refusal, `ebilling.tracking.schema_missing`, to a 503.
- **K5 (fixed).** The owned-column test now also covers `sprk_submissionsequence`, `sprk_firminvoicenumber` and `sprk_invoicesubmissionid`; the bad-arguments test also covers a blank firm invoice number and a null builder (`ArgumentNullException`).

**Interpretations in the fix round** (flagged for review):

- A missing submission at the precondition read is `Conflict` (the row changed since the caller read it: it is gone), not a thrown fault.
- `not_unnumbered` is stricter than "another firm, or scope not none": it refuses any row that is not a no-firm row in the `none` scope and not already in the requested firm's scope with a sequence held. The extra case is a row with this firm but scope `none` (a firm filed in the `none` scope): the strict scope check flags it, so a renumber does not silently complete it.
- The firm invoice number is compared exactly after trimming (`inv-7` is not `INV-7`), because it is the number the row holds, read with the version; create's `Reconcile` compares case-insensitively because there the number is retyped.
- The row version is read as the persister reads it: the `versionnumber` column, else `Entity.RowVersion`; neither readable throws `InvalidOperationException` (fail closed, nothing written).
- `0x80040237` with no faulted index propagates (only index 0 is known to be the submission update).

**Tests and seed proof (fix round).** Filter `TrackingNumber|Allocator` in `Sprk.Bff.Api.Tests`: **122 pass, 0 fail, 2 skipped** (the live tests, which need the dev environment); of those, 52 are the `095d-1a` renumber cases (the 24 above, reshaped, plus the fix round's). `ArchTests` filtered to the guards that name the allocator (`SubmissionTrackingScopeNotAccess`, `ResubmissionOfAppOnlyWrite`, `SubmissionSourceEmailAppOnly`, `RecordOwnerAssignmentCensus`, `ADR010`): 86 pass. Seed proof for F4: with the precondition read removed (the call to `DecideFromStateAsync`), **11 of the renumber tests fail**, among them the repeat-at-the-post-commit-version test (a second number is allocated), the other-firm repeat, the four not-unnumbered states, the foreign firm invoice number (two), the stale version at the read, the missing row, and the repeat at the old version (which now asserts that the repeat sent nothing); with it restored byte-identical (SHA-256 checked), all 122 pass.

#### 16.6.1 K4 fix: never reuse a no-firm tracking number (095d-1a-1, 2026-11-04)

Branch `task/ebilling-095d-1a` (from `e1079c0bf0`). **Owner decision (typed in the main session, 2026-11-04): "New column, never reuse."** It replaces the K4 "stopped" options above. Files: `TrackingNumberAllocator.cs`, `ITrackingNumberAllocator.cs` (one reason code and its docs), `TrackingScopePhase.cs` (the warn-once flag, and one pre-existing whitespace fix), the new schema script, `SpaarkeMaster/Entities/sprk_invoicesubmission/Entity.xml`, `docs/data-model/sprk_invoicesubmission.md`, `tasks/095d1-set-firm-apply.poml` (one constraint), tests. No package, endpoint or DI registration, so there is no publish-size measurement for 1a.

**The column.** `sprk_invoicesubmission.sprk_previoussequence` (schema name `sprk_PreviousSequence`): **Whole number (Integer, not BigInt), minimum 1, maximum 2147483647, optional, not field-secured.** The range is the range of `sprk_submissionsequence`, whose values it copies. Dataverse cannot field-secure a BigInt, and a masked read would read as "no previous sequence", so the column is deliberately not secured. It holds the row's `none`-scope sequence from before a renumber; it is empty on every row that was never renumbered, so there is no backfill (no row can have been renumbered before the column exists: the renumber is refused without it).

**What the allocator does.**
- **The renumber writes it.** The precondition read (F4) already reads the row; the sequence it holds is carried to the submission update, which writes `sprk_previoussequence` together with the firm, scope, sequence and number, at the row version the update is conditioned on. So it lands with the move or not at all (a lost race rolls it back with the rest, and the retry writes it again). A row with no sequence writes none (the minimum is 1). `PreviousSequenceColumn` is in `OwnedColumns`: a create's `fields` or a renumber's `SubmissionFields` naming it (any case) throw `ArgumentException`.
- **The `none` scope's next-number read** (`ReadHighestSequenceAsync`; the column applies only to the `none` scope at P2) is the larger of two bounded top-1 reads, **in this order**: (1) the highest sequence of the rows in scope `none` with this firm invoice number (the existing read; the hidden/undone skip is untouched); (2) the highest `sprk_previoussequence` of the rows with this firm invoice number, in any scope. Two queries, not one: a single query cannot take the sequence of the rows still in `none` AND the previous sequence of the rows that left it, because the first read must exclude the renumbered rows' new (firm-scope) sequences (counting them would skip numbers). The order closes the window for a renumber that lands between the two reads of one create: a row leaves `none` in one update, so a row that moves between the two reads was seen by the first, and one that moved before the first is seen by the second. It does not make a create atomic with its reads (known limit K2 of the re-check round below). The scope key decides a race only while the row that holds the number is still in the scope; this read only raises the floor. A firm's scope never reads the column.
- **Without the column.** The renumber answers `Refused` with the new code **`ebilling.tracking.schema_missing`**, from the precondition read (which names the column, so it faults before anything is sent: no build, no transaction, nothing written). **I did not reuse `scope_key_inactive`**: that code says "not P2" (run the scope script), this one says "run the previous-sequence script"; 1b maps both to a 503, but an operator needs to be told which script. The create keeps working: the previous-sequence read's missing-column fault is caught, the create falls back to the scope-only read, and a warning (`ebilling.tracking.schema_missing`, no business data) is logged **once per process** (the flag lives on the shared `TrackingScopePhaseCache`, the allocator's one piece of process-wide state). The fallback is complete, not a risk: with no column no renumber has run, so no number has left `none`.
- **How "missing" is recognised** (mirroring 095c-5, narrowed). 095c-5's `InvoiceFirmHold` lets any read fault throw and every caller answers unavailable. The renumber does the same in effect (it refuses), but the create must tell "the column does not exist" from any other fault, or a timeout would be read as "no previous sequences" and numbers could be reused. So the detector `IsPreviousSequenceColumnMissing` accepts only Dataverse's attribute-does-not-exist fault, `0x80041103` ("'sprk_invoicesubmission' entity doesn't contain attribute with Name = 'sprk_previoussequence'"), as the typed fault code (including inner faults) or, where the client surfaces only text, as a message that names the code or "contain attribute with Name" AND this column. Another column's fault, another code naming the column, and every other exception propagate. **Measured on dev, read only (2026-11-04):** a fetch naming `sprk_previoussequence` answered HTTP 400, `0x80041103`, with that message. The SDK path's exception shape (typed fault or wrapped text) is not measured live; both are accepted. No metadata probe and no new cache of the column's existence were added (CLAUDE.md 11).

**The schema script: a sibling, not an extension (deviation from "preferred").** `scripts/ebilling/Deploy-EbillingSubmissionPreviousSequenceColumn.ps1` with `-WhatIf`, `-Apply` and `-Verify` (it refuses to run with no mode). I did not extend `Deploy-EbillingSubmissionTrackingScopeSchema.ps1`: that script is the nine phased steps of the scope key (a cache wait, a destructive firm-key retirement as step 9), its `-Verify` means "all nine steps done, firm key retired" (it fails on an environment run with `-KeepFirmKey`), so it cannot be the deploy gate for this one column, and `-Apply` on it would run the phases. The sibling follows `Deploy-EbillingSentToApDateColumn.ps1` (one column, escalation exit 2 for a conflicting column, entity-scoped `PublishXml` for `sprk_invoicesubmission` only and only when this run wrote) and `Set-EbillingInvoiceFirmHoldColumn.ps1` (the three modes); the definition is `New-WholeNumberDef` from `Deploy-EbillingSubmissionSchema.ps1`. `-Verify` (read only) exits 1, naming each gap, when the column is missing, is not an Integer, has a minimum above 1 or a maximum below 2147483647, or is field-secured; exit 0 otherwise. **On dev, read only, 2026-11-04:** `-Verify` exited **1** ("sprk_invoicesubmission.sprk_previoussequence does not exist"); `-WhatIf` printed "WOULD CREATE sprk_previoussequence (Integer)", nothing written; the gap logic was also run on existing dev columns (`sprk_submissionsequence`: 0 gaps, so the specified shape equals the existing sequence column; `sprk_trackingnumber` and `sprk_findingsjson`: "not a whole number"). The SpaarkeMaster source (`Entity.xml`) carries the column (int, 1 to 2147483647, `IsSecured` 0), so a stamp provisioned from it needs no script. `-Apply` was NOT run.

**BINDING DEPLOY PRE-CHECK (independent of, and next to, 15.9).** Before deploying any BFF build that carries 095d-1 (1a-1c), on that environment `pwsh scripts/ebilling/Deploy-EbillingSubmissionPreviousSequenceColumn.ps1 -Verify` exits 0, i.e. `sprk_invoicesubmission.sprk_previoussequence` exists as a not-secured whole number 1 to 2147483647. The column is created by `-Apply` (the ebilling main session, after the owner's go, with notices to uac-r2 and ontology-r1) or by the SpaarkeMaster import on a new environment. If the pre-check is not met the BFF stays safe (renumber refused with `schema_missing`, create unchanged), but the Set-firm feature does not work, so the deploy must wait. The 15.9 pre-check (the firm-hold column, secured, Writers and Readers grants) still applies and is independent. Also recorded in `tasks/095d1-set-firm-apply.poml`. The main session adds it to the Master PR checklist in `current-task.md` (not edited here).

**Tests.** `TrackingNumberAllocatorTests.PreviousSequence.cs` (a partial of the 045 suite; its in-memory table now models the column and an environment without it: a query or write that names `sprk_previoussequence` faults with `0x80041103`) and `TrackingNumberPreviousSequenceColumnAgreementTests.cs` (script constants, script behaviours with seeded drifts, and `Entity.xml` against the allocator constant and the sequence column's range). Cases: the renumber writes the column in the update (sequence 1 and 4), and a lost race writes it only with the committed attempt; after `INV-7.1` is renumbered, a no-firm intake of `INV-7` gets `INV-7.2`, then `.3`; several renumbered rows make the floor the highest previous sequence; a mix of live `none` rows and renumbered rows (six combinations); another firm invoice number's previous sequences and live rows do not count; a firm's scope does not count the column and never reads it; before P2 it is not read; a renumber committing just before, or between, the intake's two reads is still counted; without the column the renumber is `Refused` `schema_missing` with nothing built or sent, and before P2 it still refuses `scope_key_inactive`; without the column a no-firm create falls back, numbers on, and warns once (two creates, one warning); a firm's create is unaffected; a previous-sequence read fault that is not a missing column propagates and creates nothing; the detector fires on the typed fault, the wrapped fault, an inner fault, and text, and not on another column, another code or any other failure; `sprk_previoussequence` is refused as a caller column in both the create's `fields` and the renumber's `SubmissionFields`.

**Counts.** Filter `TrackingNumber|Allocator` in `Sprk.Bff.Api.Tests`: **163 pass, 0 fail, 2 skipped** (the live tests, which need the dev environment), up from 122 (28 allocator cases including added columns in the owned-column theories, 13 agreement cases). `Spaarke.ArchTests` filtered to `SubmissionTrackingScopeNotAccess`, `ResubmissionOfAppOnlyWrite`, `SubmissionSourceEmailAppOnly`, `RecordOwnerAssignmentCensus`, `ADR010`, `AccessTableReaderScan` and `InvoiceFirmHoldAppOnlyGuard`: **101 pass**. `dotnet format whitespace --verify-no-changes` on the three changed source files: clean. The full suite was not run (the machine is loaded; the brief said filtered only).

**Seed proofs.** (1) With the previous-sequence term dropped from `ReadHighestSequenceAsync` (the read returns the scope read only), **9 of the new cases fail**: the reuse test (`INV-7.1` is issued again), several renumbered rows, three of the six mix rows, a renumber just before the intake's read, the other-number must-not-fire (`inv-8.10`), the missing-column fallback and the read-fault propagation (no read is made); restored byte-identical (SHA-256 `870159e0db80...a137` before and after, `cmp` identical), all 163 pass. (2) With the two reads swapped (previous sequence first), exactly the "renumber just before the intake's read" case fails: it pins the order; restored byte-identical.

**Component justification (CLAUDE.md 11).** *Existing*: the `none` scope's read (`ReadHighestSequenceAsync`) and the 095c-5 and scope-script helpers; the new column overlaps with nothing (`sprk_submissionsequence` is overwritten by the renumber, which is the defect). *Extension*: the scope read, the renumber update, the schema-script helpers and the missing-column handling are extended, not duplicated; a new script only because the scope script's `-Verify` cannot gate one column. *Cost of doing nothing*: a no-firm intake issues a tracking number a renumbered submission already holds in a firm scope's history (the owner's "never reuse").

**Known limits and notes for review.**
- **Undone renumbered row.** The previous-sequence read finds rows by their firm invoice number, which the undo of a commit (`SubmissionCommitGate`) clears. The undo runs only inside the intake's commit, before assign-firm can renumber the row, so it is not a path today; if one is added, a renumbered row whose number is cleared would hide its previous sequence.
- **Not in `AccessColumns` (K4 of the first fix round): superseded.** `sprk_previoussequence` was first left out of the app-only generic writers' refused set for uac-r2 to decide. uac-r2 asked for it to be changed in 1a instead: see "Re-check round, K4 -> item 5" below.
- **One extra read per no-firm create attempt**, and, where the column is missing, one failing read per attempt (not cached; the warning is once). Bounded by `MaxAttempts`/`MaxHiddenSkips`; the missing-column state lasts only between a BFF deploy and the script, which the pre-check forbids.
- **Live seam.** The 16.6 merge gate stands (the transaction fault index and code for a key fault on update). This round adds one more thing not measured against the SDK client: the exception shape of the missing-column read (the fault code and message were measured on dev through the Web API; both the typed and the text shape are accepted).
- **Pre-existing, fixed in passing.** `TrackingScopePhase.cs` read `force &&refusedAtRead` (a missing space that `dotnet format whitespace --verify-no-changes` reports); fixed because the file is in this diff.

rework: none task 095d-1a

#### Re-check round (scoped re-check of the K4 fix, 2026-11-04)

Branch `task/ebilling-095d-1a`. Files: `TrackingNumberAllocator.cs`, `ITrackingNumberAllocator.cs`, `ParentLineage.cs` (`InheritedAccessPermission.AccessColumns`), the allocator renumber tests, `AccessTableAppOnlyWriterTests.cs`, `AccessTableReaderScanTests.cs` (one text). No package, endpoint or DI registration, so no publish-size measurement.

- **F4 (fixed): `AlreadyRenumbered` matched any native firm-F row.** The precondition read answered `AlreadyRenumbered` for any row of firm F in F's scope holding a sequence, which includes a row created natively in F's scope (never a no-firm row). It now also requires `sprk_previoussequence` to be non-null (only a renumber writes it; the read already fetched it); otherwise the row falls to `Refused` `ebilling.tracking.not_unnumbered`. Order checked: the precondition read names the column, so where the column is missing the read faults and the renumber is refused `schema_missing` BEFORE this check runs (`DecideFromStateAsync`: the read, then the version, then the firm invoice number, then this check). New case in the not-unnumbered theory, "this firm and scope, sequence held, never renumbered": **fails before the fix** (`AlreadyRenumbered` `INV-7.1` where `not_unnumbered` is expected; 1 failed, 9 passed of the F4 cases) and passes after. **Edge for 1b:** a no-firm row that held NO sequence in `none` (a row the allocator never numbered) is renumbered without writing a previous sequence (the column's minimum is 1), so a repeat after a lost reply on such a row is now refused `not_unnumbered` instead of `AlreadyRenumbered`. The allocator numbers every no-firm row it creates, so only a legacy row without a sequence can be in that state; 1b's re-read of the stored decision decides it.
- **K2 (known limit, wording only; code unchanged).** The class remarks and 16.6.1 no longer imply the two reads rule out an interleaving. The bound: two creates C0 and C1 of the same no-firm number both read the highest as n-1; C0 commits `.n`; before C1's write lands, C0's row completes intake, a firm is assigned and the renumber commits (the row leaves `none` and records n); C1 can then commit `.n` again. The window is one create's write latency, against a full intake plus a human step. Recorded as a known limit.
- **K3 (doc).** `RenumberOutcome.AlreadyRenumbered` now states that the outcome does not say WHICH decision renumbered the row. 1b must compare the row's stored decision, not its own request, before it finishes the record, the close, the link or a re-run, and treat a different stored decision as a conflict.
- **K4 -> item 5 (changed here, at uac-r2's request): `AccessColumns`.** `InheritedAccessPermission.AccessColumns["sprk_invoicesubmission"]` now also lists `sprk_submissionsequence` and `sprk_previoussequence`, next to `sprk_trackingscope`, with the one-line comment "allocator-owned (095d-1a)". Their refusal text says they decide no access and only the tracking number allocator writes them (the scope column's text). **`sprk_firminvoicenumber` is NOT listed: the stop-first grep found a second writer.** Writers found (all of `src`, by column name and by the `FirmInvoiceNumberColumn` / `SequenceColumn` / `PreviousSequenceColumn` constants):
  - `sprk_submissionsequence`: only the allocator (`BuildRow` at create, `BuildRenumberUpdate` at the renumber). Read by `SubmissionEvaluationOutcome`, `BillingFactLoader` and the allocator.
  - `sprk_previoussequence`: only the allocator (`BuildRenumberUpdate`). Read by the allocator.
  - `sprk_firminvoicenumber`: the allocator (`BuildRow` at create) AND **`SubmissionCommitGate`'s undo** (`SubmissionCommitGate.cs` ~:429 sets it to null with the other commit columns, via `_entities.UpdateAsync`). The persister's create and commit paths (`BuildSubmissionUpdate`) do not write it. The undo is a typed writer, not a generic one, so listing the column would not break it; it was left out because the instruction was to stop on any writer other than the allocator. **Decision for uac-r2 / the owner:** list it, or leave it.
  - Tests: a theory in `AccessTableAppOnlyWriterTests` (`AnAppOnlyWriteOfAnAllocatorOwnedSequenceColumn_...`), one row per column: the output mapping and the Update Record node are refused in two key forms with nothing written, the refusal text, must-not-fire (another table, a neighbouring name, the caller's own write), and the REAL allocator renumbers a no-firm row over a mocked client, the one submission update it sends carrying the column (**both rows fail before the `AccessColumns` change** and pass after). The `AccessColumns` census list in the same file and the `sprk_invoicesubmission` text in `AccessTableReaderScanTests` name the two columns.
- **Tests and counts.** `TrackingNumber|Allocator` in `Sprk.Bff.Api.Tests`: **166 pass, 0 fail, 2 skipped** (was 163 + 2 skipped; +1 F4 case, +2 here; the live tests are skipped). `AccessTableAppOnlyWriter`: 940 pass (includes the 2 new rows). `Spaarke.ArchTests` filtered to `AccessTableAppOnlyWriter|AccessTableReaderScan|ParentLineage|SubmissionTrackingScopeNotAccess`: 10 pass (the writer tests themselves compile into `Sprk.Bff.Api.Tests`). No test class is named for `ParentLineage` or `RecordTableRules`; those filters matched nothing further. `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings. `dotnet format whitespace --verify-no-changes` on the three changed source files: clean.

### 16.7 ontology-r1 decisions for 095d-1b/1c (2026-11-04, their notes/decisions.md d9305192d4; binding)

- **The shared firm_missing Signal: option (b).** 095d-1 does not close it.
  - The assign-firm record's `ResolvedSignalIds` is EMPTY. Its facts carry the group's Signal id and state that this decision cleared the firm_missing reason.
  - The 095d-2 re-run re-maps the group. If any reason remains, the same Signal stays open and its title is recomputed (the coding hold's title comes back, §14.8 item 2). If none remains, it is closed as `ConditionCleared`, with the assign-firm record id in the close facts. It is NOT closed as `Acted`.
  - This replaces D-201's "closed as Acted and linked to the record" for this shared Signal.
  - Condition 4 holds: nothing closes before the record.
  - 095d-1 never reaches master without 095d-2.
- **Stored-file renames go INSIDE the apply transaction.** They are updates to the `sprk_storedfile.sprk_name` column; `StoredFileBlobName.Build` uses ids only (StoredFileContracts.cs:298-305).
  - The "files" failedStep and its census row are dropped.
  - The `InvoicePdfWriterGuardTests.ApprovedWriters` entry is name-only, with a must-not-fire test proving it writes no other column.
- **Ceiling and reporting:**
  - the ceiling is a refusal before the apply, using `MaxRequestsPerTransaction` = 1000;
  - "applied, no record yet" rows are reported by `Set-EbillingInvoiceFirmHoldColumn.ps1 -Verify`.

### 16.8 095d-1b (2026-11-04): the Set-firm service and route

**What landed.** `POST /api/finance/invoices/{invoiceId}/firm` (in `InvoiceApprovalEndpoints.cs`, no new file) and
`Services/Finance/InvoiceAssignFirmService.cs`. Body `{ firmId, note? }`; 200 `{ outcome: assigned | alreadyAssigned, decisionId,
trackingNumber, previousTrackingNumber, rulesRerun: "pending" }`.

**Authorization (no new privilege or role).** Step 0 (group), then two filters as the caller, fail closed: Write on the invoice under the
new key `ebilling.invoice.assign_firm` (= `AccessRights.Write`, the uniform 404), then Read (`read`) and AppendTo (`finance.attach_invoice`)
on the body's firm (403; a body with no firm is 400 `ebilling.assign_firm.firm_required` before any firm query); then the kill switch.
The decider is the caller's `systemuserid` (403 `ebilling.decider.unresolved` when unproven). The service asks, as the caller,
`HumanDecisionSignalClose.AuthorizeAsync` (AppendTo on the core record, Write on the invoice as the firm_missing Signal's subject) and the
recorder's filing. Interpretation: 16.7 removed the close from 095d-1, but the POML's refusal list keeps the rights check, and AppendTo on
the core record is what the record in the decider's name needs, so it stays. 403 codes render as `reasonCode` (`ProblemDetailsHelper.Forbidden`,
as approve); every other refusal as `errorCode`.

**Refusals, all before the apply (condition 1), in order.** The decision lock and the line lock (409 busy, 503 on a lock fault); the invoice
(404 uniform) and its ONE submission (the row whose id derives the invoice; else 409 `no_submission`). Then, unless a stored decision makes
the call a repeat: Active and In Review (409 `not_in_review`); no firm on the invoice or the submission (409 `firm_already_set`); held,
app-only (`InvoiceFirmHold.IsHeldAsync`; 409 `not_held`, 503 on a read fault); one core record (500 `core_unresolved`); the firm active
(400 `ebilling.submission.firm_invalid`, 503 `firm_unverifiable`) and linked (`CheckFirmLinkAsync`: 403 `firm_not_linked`, 503); P2
(503 `ebilling.tracking.scope_key_inactive`); the Signal-close rights (403 `ebilling.decision.not_authorized`, 503); the filing (500, 503);
the stored findings an object (500 `findings_unreadable`); the ceiling, `FitsRenumberTransaction(1 + lines + renamed approvals + renamed
files)`, and the findings size for the longest number the allocator can hand out (409 `too_large`); `BillingDecisionRecorder.Preflight`
of the decision rebuilt from that longest candidate (500). A refusal writes nothing (each test asserts no renumber and no Dataverse write).

**The apply (condition 2).** One call to `RenumberSubmissionAsync(submission, its versionnumber, firm, its own firm invoice number, build)`.
The builder is pure and runs once per attempt with that attempt's number. It returns: `SubmissionFields` = the findings with
`trackingNumber` replaced and the stored decision under `assignFirm` (every other member kept); requests: the invoice (firm,
`sprk_trackingnumber`, `sprk_firmhold` = null; `IfRowVersionMatches` on the invoice's version read), each line (firm; name when it is
`LineNameFor(old, i)`), the approval (name when it is `Approval {old}`), each Stored File whose name starts with the old number's name
segment followed by `.`, `-` or nothing (name only, cut to 400; `INV-7.30.pdf` is not `INV-7.3`'s). The submission markers (reason code,
findings) are NOT cleared: the invoice stays held until 095d-2's re-run (095d-1 acceptance).

**The stored decision** (`assignFirm`, schema 1): `decisionId` (`IdOf("ebilling-assign-firm-{submissionId:D}")`), `firmId`, `note`,
`previousTrackingNumber`, `previousSequence` (null for a legacy no-sequence row), `trackingNumber`, `sequence`, `deciderUserId`,
`decidedOn` (to the second), `firmMissingSignals` (`ResolvedSignalSet` JSON: the invoice's open `BILL-SYSTEM-SUBMISSION` Signals and the
read time), `renamed[]` (`entity, id, column, from, to`), `rulesRerun` "pending". No new column (escalation trigger not fired).

**Outcomes.** `Renumbered` gives 200 assigned. `Conflict` re-reads: a stored decision now present is judged as a repeat, else 409
`changed`. `Refused`: `unavailable`, `scope_key_inactive`, `schema_missing` give 503 with that code; `too_many_requests` gives 409
`too_large`; `not_unnumbered` gives 409 `not_renumberable`. `ArgumentException` gives 500; any other fault 503 `unavailable` (the outcome
is unknown; the repeat reads the stored decision and never applies twice).

**Repeats (1a K3; 16.6.1).** The stored decision is written only by the apply. A request that finds one skips the refusals the apply itself
ended (firm set, hold cleared) and: another firm is 409 `assigned_other_firm` (names no firm; the allocator is not asked); the same firm
asks the allocator at the current version with a builder that throws: `AlreadyRenumbered` naming the stored number is 200
`alreadyAssigned`; another number is 409 `changed`; the legacy edge (`not_unnumbered`, stored `previousSequence` null, the row in the
stored firm's scope with the stored number) is 200 `alreadyAssigned`; `not_unnumbered` otherwise is 409 `changed`; other refusals keep
their code.

**Catalog.** `assign-firm` ("Set firm"), Judgement, declared `Tier3, Reversible false, ExternallyVisible true, RecordOfTruthImpact true`
(the POML); `DecisionGateTier.Resolve` gives **Tier4** (not reversible and externally visible), pinned in `DecisionActionCatalogTests`.
Values `firm` (Lookup, required) and `note` (TextArea). Subject: the invoice (`BillingSubjectEntityOf`).

**Guards.** `InvoiceFirmHoldAppOnlyGuardTests` and `FindingsJsonAppOnlyReadGuardTests` reviewed lists (and row 10 in
`122-findings-json-field-secure.md`): the service reads both app-only and writes both through the allocator's transaction. `SubmissionTrackingScopeNotAccessGuardTests`: it reads `sprk_trackingscope` app-only only to confirm a legacy repeat (no access input). The method is `SetFirmAsync`, not `AssignAsync`, so the ontology Assign census (`RecordOwnerAssignmentCensusTests`) does not read it as a record re-own.
`InvoicePdfWriterGuardTests.ApprovedWriters`: `InvoiceAssignFirmService::StoredFileRename`, name only, with
`AssignFirmsStoredFileRename_WritesTheNameOnly` (its IL loads only `sprk_storedfile` and `sprk_name`; the built update has one attribute;
the scan sees no other Set-firm writer). `RouteAuthorizationGuardTests` ledger text, `OperationAccessPolicyCompletenessTests` rows,
`DecisionRecordWriterTests.Billing` C2 rows, `DecisionCommitServiceTests` (the commit route refuses the code).

**AccessColumns (uac-r2, binding).** `sprk_firminvoicenumber` is listed for `sprk_invoicesubmission` ("allocator-owned; SubmissionCommitGate
undo clears it (095d-1a)"); its refusal says "it decides no access; only the tracking number allocator sets it".
`AccessTableAppOnlyWriterTests` (output mapping and Update Record node refused; must-not-fire on another table, a neighbouring name and the
caller's write) and `InvoiceSubmissionPersisterTests.Gate_Undo_StillClearsTheFirmInvoiceNumber_AfterItIsListedInAccessColumns`.

**Tests.** `InvoiceAssignFirmServiceTests` (61) and `InvoiceAssignFirmEndpointContractTests` (route: 401; no Write 404 before the firm is
asked; Read and AppendTo on the firm each needed; no firm 400; kill switch 503; step 0; decider unresolved; service codes). Two tests run the
REAL allocator: the one transaction (submission first, `sprk_firmhold` cleared, stored decision) and a repeat after the commit that sends
nothing. Fail-before: with the old `ParentLineage`, catalog and policy restored, 27 of the new tests fail.
Seed proofs (2026-11-04, restored byte-identical, `cmp`): the not-held check disabled makes `Refused_NotHeld` fail; the `sprk_firmhold`
clear removed makes `RealAllocator_TheOneTransaction_ClearsTheHold_AndCarriesTheStoredDecision` and the apply test fail.

**Seams for 095d-1c.**
- `InvoiceAssignFirmService.BuildDecision(invoiceId, submissionId, stored)` rebuilds the Decision Record from the stored decision alone
  (same id and hash on a repeat); 1c calls `BillingDecisionRecorder.RecordAsync` with it after `Assigned` and on `AlreadyAssigned`.
- `AssignFirmStoredDecision.Read(findings)` is the repeat's source; `ResolvedSignalIds` stays null (16.7); the firm_missing Signal set is in
  the facts (`firmMissingSignals`, `firmMissingCleared`, `policyCode`).
- `AssignFirmResult` has no `Incomplete` outcome yet: 1c adds 503 `incomplete` with `failedStep` (record, link) and the repeat that finishes
  from the stored decision. The repeat path today answers `alreadyAssigned` without recording.
- The link and the `-Verify` INFO/FAIL window for "applied, no record yet" rows.

**Known limits (K-class, none on a real path).**
- K1: only the invoice update is row-version conditioned (with the submission's, by the allocator). Line, approval and file updates are not:
  their names have no other writer, and the line lock is held across the apply.
- K2: a stored decision present makes the call a repeat without re-asking the Signal-close rights or the filing; the route filters (Write on
  the invoice, Read and AppendTo on the firm) still run. 1c must re-ask what its record write needs.
- K3: `RenamedFile` cuts a renamed name to 400 characters; the stored mapping keeps the cut name.

**095d-1b adversarial pass 1 and fix round 1 (2026-11-04).** One full adversarial pass; three findings, all fixed in this round.

| # | Finding | Fix | Proof |
|---|---|---|---|
| F1 | Large invoices could never get a firm. `BuildDecision` put the full `renamed` list into the Decision Record facts; above about 450 renamed rows the facts passed the writer's 100,000-character cap, the preflight refused, and the route answered 500 `decision_invalid` with the enum name as its detail. The tests used a no-op preflight, and `Refused_AboveTheCeiling(998, assigned: true)` proved nothing. | The facts hold a bounded `renamedCounts` (lines, approval, storedFiles, findings) beside the existing `previousTrackingNumber` and `trackingNumber`. The full old-to-new mapping stays in the stored `assignFirm` member only. A preflight refusal is a stated sentence (no enum name, no exception message). | `World` now preflights through the REAL `BillingDecisionRecorder` by default. `Refused_AboveTheCeiling(998, true)` passes the real preflight at the transaction ceiling. Seed: the full list put back in the facts makes it fail (Refused, not Assigned); restored byte-identical (`cmp`). |
| F1 (repeat) | `JudgeRepeatAsync` mapped every exception from the allocator's precondition read to 409 `changed` ("The invoice changed... Reload."), so a timeout on a repeat sent the reviewer to reload a row that had not changed. | 409 `changed` only for the builder's trip (`RepeatMustNotApplyException`, a private `InvalidOperationException` subtype, so the allocator's own `InvalidOperationException`s are not mistaken for it). Any other fault is 503 `unavailable`, logged at Warning with the type name only. | `Repeat_AllocatorFault_IsUnavailable_ExceptTheBuildersTrip` (timeout and allocator fault: 503; builder trip: 409). Both 503 cases fail on the old service. |
| F3 | The findings-size refusal (409 `too_large` above 1,048,576 characters, measured with the longest number the allocator can hand out) had no test. | Tests only. | `Refused_FindingsAboveTheSize_JustOver` (must fire: refused, nothing written, no preflight) and `..._AtTheLimit_IsAssigned` (must not fire). Seed: the check disabled makes the must-fire test fail; restored (`cmp`). |

**Known limits recorded by this pass.**
- K2 (CORRECTED in fix round 3: this line missed the core-record rights; see the uac-r2 review below): a repeat by a different caller is confirmed safe in 1b (the stored decision names the firm and the number; a different firm is 409). 095d-1c MUST ask the Signal-close rights (and the filing) again before it records on a repeat; 1b answers `alreadyAssigned` without them.
- The 1c seams: every `Assigned` in 1b leaves an "applied, no record" row (the apply committed, no Decision Record, firm_missing Signal still open). 1b therefore stays off any environment users touch until 1c lands.
- 095d-2 must keep the stored `assignFirm` member (the full mapping lives only there now; the facts carry counts).
- K3 (display only): the line Signals keep the old invoice number in `sprk_regardingrecordname`. 095d-2's re-map should refresh it, or it is recorded there as a limit.

#### 095d-1b adversarial pass 2 and fix round 2 (2026-11-04)

No F findings. Six known-limit items (K1-K6); K1, K3 and K4 are fixed, K2, K5 and K6 are recorded with their remedies.

**K1: check order (fixed; uac-r2 ruling, binding).** The caller's rights on the core record (the Signal-close rights: AppendTo on the core record and Write on the Signal's subject) and the recorder's filing now come BEFORE any firm read whose answer can change the result (active, linked, existence) in `InvoiceAssignFirmService.SetFirmAsync`. The question sent to uac-r2 was: can Write on an invoice exist without access to its core record? Answer: yes, Write on an invoice does not imply AppendTo on its core record. A caller who fails the check gets ONE refusal (403 `ebilling.decision.not_authorized` from the service) whatever firm id it names.
- The endpoint's firm-rights filter (Read and AppendTo on the firm, 403) runs before the service and is the same for a missing and an unreadable firm. To make the two refusals identical in status AND body, the route now renders the service's `not_authorized` refusal with the filter's body (`sdap.access.deny.insufficient_rights`, "Access denied"; `InvoiceApprovalEndpoints.AssignFirm`). Chosen over a new core-record filter ahead of the firm filter because that filter would have to read the invoice and its core record before the service's own guards, a second copy of the derivation; the body mapping is one switch arm and cannot drift from the filter.
- Other refusal orders: only the above. Phase P2, the findings checks and every status and code are unchanged; the firm-invalid, not-linked and unverifiable refusals now follow the rights and the filing. One consequence: a caller who fails the core-record check on a not-held or not-In-Review invoice still gets those invoice-state refusals first (they depend on the invoice, not the firm).
- Tests: service `NoCloseRights_IsRefusedTheSame_WhateverFirmIsNamed` (5 firm states, plus "the firm is never read"); contract `NoCoreRecordRights_IsRefusedIdentically_WhateverFirmIsNamed` through the real filter stack (linked, unlinked, inactive, unknown: one status, one body). Seed: order reverted, 6 of the new tests fail; endpoint mapping reverted, the contract test fails (the unknown firm's body differs). Both restored byte-identical (`cmp`).

**K2: dead ends refused cleanly but with no next step (known limit; operator remedy each).**
- (a) Findings that would exceed 1 MB at the line ceiling: 409 `too_large`. Remedy: the operator compacts or splits the invoice. R2 candidate: compact the mapping.
- (b) `sprk_vendororg` set directly through the Web API while the invoice is held: 409 `firm_already_set` forever. Remedy: the operator clears the stored firm, or clears the hold through the 095d-2 re-run.
- (c) A submission whose invoice lookup was unlinked or repointed: 409 `no_submission`. Remedy: the operator relinks it.

**K3: fixed, and an open question.** The `AssignFirmRename` summary now says the Decision Record carries counts only and the full mapping lives in the stored `assignFirm` member. `renamedCounts` gained `firmSetLines`: every line the apply wrote the firm on, renamed or not, so the record states every row the apply wrote (`lines` stays the renamed lines). The count is stored in the `assignFirm` member (`firmSetLines`), because the decision is rebuilt from the stored member alone; a stored member without it is unreadable (`findings_unreadable`; nothing is stored yet in any environment). Open question for ontology: do counts (not the old-to-new mapping) satisfy its 16.7 addition 3, given the mapping lives only in the stored member?

**K4: fixed.** The `ArgumentException` case now also asserts `RenumberArgs` is set (the allocator was reached) and `Preflighted` holds exactly one decision (the Decision Record preflight ran first).

**K5: the 2-minute lock against the allocator's worst case (known limit).** The invoice-decision lock lasts 2 minutes; the allocator's worst case can run longer. The row versions still prevent a second apply; only the unconditioned name updates (the approval's and the stored files') rely on the lock.

**K6: known limit, safe.** A pre-send `InvalidOperationException` from the allocator gives the "may not have been set" text; that is safe, because the repeat decides from the stored decision.

#### 095d-1b uac-r2 review and fix round 3 (2026-11-04)

uac-r2 approved everything else: the rights order, the route body, the kept order, the firm filter, `ParentLineage` and the policy key.

**F1 (binding): the stored-decision branch ran before the core-record rights.** `AssignHeldAsync` sent a stored decision to `JudgeRepeatAsync` before `_closeRights`. For an invoice whose firm was already set, a caller with Write on the invoice and no AppendTo or close rights on the core record got an answer that depended on the firm named: the same firm gave 200 `alreadyAssigned` with the DecisionId and the tracking numbers, and another firm gave 409 `assigned_other_firm`.

**Fix.** The single-core check and `_closeRights` moved into one helper (`CheckCoreRightsAsync`) that both paths call. A repeat now runs it BEFORE `JudgeRepeatAsync`; a failing caller gets the same `not_authorized` result as a first apply, so the route renders the same `sdap.access.deny.insufficient_rights` body. The filing is not asked for a repeat. The first-apply order is unchanged (the helper sits where the inline check was). `AfterConflictAsync` is reached only after that check and is left as it was, with a comment saying so.

**Correction of the earlier K2 line.** "A repeat by a different caller is confirmed safe in 1b" (adversarial pass 1) was wrong: it looked at the stored decision's content and missed the core-record rights, so a caller without them could read the DecisionId and tracking numbers, and could tell which firm was stored. The 1c requirement stands (1c still asks the close rights and the filing before it records on a repeat); the rights part now also holds for the 1b answer.

**Tests.** Service `Repeat_WithoutCoreRights_IsRefusedTheSame_WhateverFirmIsNamed` (the stored firm and another firm: one `not_authorized` result, no DecisionId or tracking numbers, nothing written, no filing call). Contract `RepeatWithoutCoreRecordRights_IsRefusedIdentically_AsTheFirstApply` through the real filter stack (assigned with the stored firm, assigned with another firm, not yet assigned: one status and one body less `traceId`). The existing repeat tests for a caller with rights are unchanged. Seed: the check moved back below `JudgeRepeatAsync`; all three new cases failed; the fix was restored byte-identical (`cmp`).

### 16.9 095d-1c: Decision Record, Signal facts, Incomplete (2026-11-04)

Branch `task/ebilling-095d-1c` (from `work/ebilling-r1` `637da3e730`). Files: `InvoiceAssignFirmService.cs`, `InvoiceApprovalEndpoints.cs` (the route's Incomplete arm and its description), `scripts/ebilling/Set-EbillingInvoiceFirmHoldColumn.ps1` (check (f)), and tests (`InvoiceAssignFirmServiceTests.cs` and its new partial `.Record.cs`, `InvoiceAssignFirmEndpointContractTests.cs`, `InvoiceFirmHoldFieldSecurityAgreementTests.cs`, `TrackingNumberAllocatorLiveTests.cs` made partial, and its new partial `.Renumber.cs`). No package, no DI registration, no new column, no new privilege, no new writer entry point. No escalation trigger fired.

**What landed.**
- **Apply, then record (option 1).** After `RenumberSubmissionAsync` answers `Renumbered`, the service writes ONE Decision Record through the existing billing path, `BillingDecisionRecorder.RecordAsync` (which calls `DecisionRecordWriter.WriteBillingDecisionAsync`). The decision is `BuildDecision(invoiceId, submissionId, stored)`, where `stored` is the decision the committed attempt stored. The builder runs once per attempt, so the service keeps the last one built and checks that its number is the committed number. If the numbers ever differ (the allocator cannot do this), the answer is Incomplete, and the repeat records from the row. The public constructor passes `recorder.RecordAsync`, and the test seam takes a `record` function next to `preflight`. The record is written inside both locks.
- **Signal facts (16.7).** `ResolvedSignalIds` stays null and no Signal is closed. The facts are 1b's, unchanged: `firmMissingSignals` (the set and its read time), `firmMissingCleared` true, `policyCode`, `previousTrackingNumber` → `trackingNumber`, and the bounded `renamedCounts` (`firmSetLines`, `lines`, `approval`, `storedFiles`, `findings`). There is no per-row list. 16.7 does not conflict with the code: `BuildDecision` already had this shape, so 1c changes no fact.
- **Incomplete.** This is the new outcome `AssignFirmOutcome.Incomplete`, code `ebilling.assign_firm.incomplete`, status 503. `failedStep` is `record` when the writer refused, faulted or gave no answer (any exception except a cancellation of this request). It is `filing` when a repeat cannot confirm the filing (below). Every Incomplete answer has one fixed sentence: "The firm was set, but its decision was not recorded. Repeat the request to finish it." The route renders `errorCode`, `failedStep` and `detail` only. It sends no decision id, no tracking number, no submission id and no exception text. The log carries the refusal reason or the exception type name, never a message.
- **The repeat that finishes.** The order is 1b's, kept. For a stored decision, the core-record rights (`CheckCoreRightsAsync`: the single core, then `HumanDecisionSignalClose.AuthorizeAsync` as this caller) run first. Then `JudgeRepeatAsync`. When the allocator confirms the stored number (`AlreadyRenumbered`, or the 16.6.1 legacy edge), `CompleteRepeatAsync` asks the filing (`CheckFilingAsync`) and then records `BuildDecision` of the stored member alone. That is the same id and the same hash, so the writer answers a second call with the first record (`AlreadyRecorded`) and writes nothing. The repeat's builder still throws, so a repeat never applies. In a conflict lost to a commit for the same firm (`AfterConflictAsync`), the rights and the filing were already asked before this request's own apply attempt, so the filing is not asked again. A repeat naming another firm, a disagreeing number, or a builder trip records nothing.
- **200 means recorded.** Since 1c, `assigned` and `alreadyAssigned` are answered only after the record was written or found. The 200 body is unchanged: `AssignFirmResult.DecisionRecordId` is set but not rendered, keeping 1b's contract and the brief's "no ids" rule.

**Decisions and interpretations (for review).**
1. **There is no `link` step in 1c.** In approval, "link" writes the record id onto the approval row. Set firm has no column for that. Adding one is an escalation trigger, and the record is found by its key (`BillingDecisionKey`) anyway. The Signal link belongs to the close, which 16.7 moves to 095d-2. So `failedStep` is `record` or `filing`. This deviates from 1b's seam note ("failedStep (record, link)").
2. **The record names the stored decider, not the completing caller.** A repeat by another reviewer who holds the core-record rights finishes the record. The record still names the reviewer whose apply it records (`stored.DeciderUserId`), because the hash covers the confirmer, so a different confirmer would be refused as other content (K2). The completing caller's own rights are asked first, so a caller without them gets the 1b refusal and records nothing.
3. **On a repeat, the filing is asked after the repeat is confirmed and before the record.** It is not asked before `JudgeRepeatAsync`. The rights already make a caller without access see one answer whatever firm it names, and the filing depends on the invoice only. A transient fault and a mismatched core stamp both give `failedStep` `filing` (503), because the firm is set, so a "nothing changed" refusal would be untrue. A permanent mismatch will not clear on a repeat; check (f) reports it once the window has passed.
4. **Check (f) lives in `Set-EbillingInvoiceFirmHoldColumn.ps1`** (16.7, binding; it already reads every submission's findings as System Administrator). No new script.

**Check (f): `-Verify` "applied, no record".** The check is read-only in every mode. For each submission whose findings carry the `assignFirm` member:
- The invoice is the one the submission id derives (`Get-InvoiceIdFor`, as the service reads it).
- The key is `BillingDecisionKey.IdOf("sprk_invoice", invoice, decisionId)`, computed by a PowerShell twin.
- The check reads `sprk_decisionrecords` filtered on `_sprk_regardinginvoice_value` and `sprk_actioncode eq 'assign-firm'` (top 10), and matches `"reviewId":"<key>"` in `sprk_steps`.
- With no match, the row is INFO while `decidedOn` is within `-AppliedNoRecordWindowMinutes` (default 60, range 1 to 10080). After that window, `-Verify` reports it as FAIL (exit 1); `-WhatIf` and `-Apply` only report it.
- A member that cannot be read is FAIL in `-Verify`.

The constants, the key derivation and the behaviours are pinned in `InvoiceFirmHoldFieldSecurityAgreementTests`. The key vector was computed by the script's own functions in PowerShell 7.6 for submission `095d1b00-0000-4000-8000-000000000001`: invoice `029c724a-2f72-4a72-ea7b-4afcf625895d`, decision `b2a5b9b4-fd9d-68e8-abd8-711ccfc0be29`, key `cdd9ab2a-00b1-bbc7-bfbb-9a52d592570b`. The C# gives the same three. The pinned behaviours are the key text, the `sprk_steps` match, the window comparison, FAIL under `-Verify`, the GET (never a write), and the derived invoice id. Each is checked against a seeded drift.

**Read-only run on spaarkedev1 (2026-11-04, GETs only).** `-Verify -BffApplicationIds 5967251e-...,1e40baad-...` exited **0**. Check (f) found 0 submissions with a Set-firm decision; no assign-firm has run on dev. The decision-record query shape was checked separately with a GET: the filter on `_sprk_regardinginvoice_value` and `sprk_actioncode` answered 200, and an existing invoice-filed record's `sprk_steps` holds `"reviewId":"<guid>"` in the form the match expects.

**Known limits (K-class, none on a real path).**
- **K1.** Check (f) trusts any `assign-firm` row on the invoice that carries the key. The writer's own lookup also requires `createdby` = the writer identity, but the script does not know that identity. A person would have to hand-craft a Decision Record carrying the key to hide a missing one from the report. That does not hide it from the BFF: the writer would refuse to treat that row as the record, and would write its own.
- **K2.** A cancelled request after the apply propagates the cancellation, as approval does. The client gets no Incomplete body, and the repeat or check (f) covers it.
- **K3.** The live seam test was not run (it writes to Dataverse; the owner's go is needed). The 16.6 merge gate therefore stays open. Until it has run, a key fault in a pair that differs from the two the allocator accepts would be a 500, with nothing written.

**The live seam test (16.6 merge gate, NOT RUN).** `TrackingNumberAllocatorLiveTests.TwoRenumbersIntoOneFirmScope_TheLosersFaultIsALostRacePair_AndItRetriesToTheNextNumber`, in the new partial `TrackingNumberAllocatorLiveTests.Renumber.cs`.
- It is skipped unless `SPAARKE_LIVE_TRACKING_DATAVERSE_URL` is set. It also skips, with the reason, when a no-firm create or the renumber is refused (`scope_key_inactive`, `schema_missing`).
- Per round, it creates two no-firm submissions with one firm invoice number, then renumbers both into one firm's scope at once. The production allocator runs over the production SDK client. A pass-through Strict mock around `ExecuteTransactionAsync` records each refused transaction's `FailedRequestIndex` and the fault codes in its chain.
- It asserts that both renumbers commit, as `.1` and `.2`. Every refused transaction must be (index 0 or none, `0x80060892`) or (index 0, `0x80040237`). The loser must hold `.2`, and both rows must be in the firm's scope with a previous sequence.
- It allows up to 5 rounds to see a collision; with none, it fails ("the seam was not measured").
- It deletes its rows by id and prefix, and asserts by query that none are left.

**Tests.**
- New service cases in `InvoiceAssignFirmServiceTests.Record.cs` (16):
  - the record after the apply: built from the stored decision, with the facts as above, and last in the order;
  - the record refused, faulting or unanswered gives Incomplete at `record` (3 cases);
  - with the REAL allocator, Incomplete and then the repeat: the same decision recorded, one transaction, and the order rights → filing → record;
  - the record already written gives 200 with that record;
  - the rights asked again on a repeat (refused 403, unavailable 503, granted 200);
  - the filing on a repeat (a transient fault, and another core record) gives Incomplete at `filing` and nothing recorded;
  - a builder trip applies and records nothing;
  - a conflict lost to the same firm records without a second filing;
  - another firm, or another number, records nothing (2 cases).
- Two existing repeat tests also assert the record (`Repeat_SameFirm_IsAlreadyAssigned`) or no record (`Repeat_WithoutCoreRights_...`).
- Contract: `RecordFailsAfterTheApply_Is503Incomplete_WithTheStepOnly`. It runs through the real route and filters. The body holds `errorCode`, `failedStep` and the sentence, and no id, number or exception text.
- Agreement: 8 new cases for check (f).
- Before the change, the new tests do not compile against 1b's service (no `record` seam, no `Incomplete`). The fail-closed guards are proven by the seeds below.

**Gates.**
- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings.
- Filtered `Sprk.Bff.Api.Tests`, `InvoiceAssignFirm|DecisionRecordWriter|BillingDecisionRecorder|DecisionCommit|TrackingNumberAllocator|SignalEvaluation|BillingFindingSignalMapper|InvoiceFirmHoldFieldSecurityAgreement`: **903 pass, 0 fail, 3 skipped** (the live tests).
- `Spaarke.ArchTests` `DecisionRecord|Immutability|RouteAuthorizationGuard|FinanceStepZeroCoverage|InvoicePdfWriter`: **117 pass**. The assign-firm guards (`FindingsJsonAppOnlyReadGuard|InvoiceFirmHoldAppOnlyGuard|SubmissionTrackingScopeNotAccess|RecordOwnerAssignmentCensus|BillingDecision`) also pass: **80**.
- `dotnet format whitespace --verify-no-changes`: the two source files are clean. The test files report only ENDOFLINE, as do the unchanged LF files next to them (repo state).
- The full suite was not run (the machine is loaded; the brief asked for filtered runs).

**Seed proofs (2026-11-04).** Each seed was restored byte-identical (`cmp`, SHA-256 `4369d68b4fc76d18...` after the last restore). Each was run on the `InvoiceAssignFirm` filter (109 tests).
1. **The record failing after the apply gives 503 Incomplete, never 200.** Seed: the record's catch answers `Assigned`. **5 fail**: the three record-failure cases, the real-allocator Incomplete-then-repeat case and the contract 503 case.
2. **A repeat never applies twice.** Seed: the repeat's builder `_ => throw new RepeatMustNotApplyException()` replaced by one that builds an apply. **4 fail**: `Repeat_ThatReachesTheApplyBuilder_NeverAppliesAndRecordsNothing`, 1b's builder-trip case, `Repeat_SameFirm_IsAlreadyAssigned` and `Conflict_LostToACommittedDecision(sameFirm)`.
3. **A repeat asks the rights again before recording.** Seed: the stored-decision branch derives the core without calling `CheckCoreRightsAsync`. **7 fail**: the three `Repeat_ReasksTheRights_...` cases, the real-allocator repeat (its order), 1b's two fix-round-3 service cases and the fix-round-3 contract case.

**Publish size.** Method: `git archive` of each commit, then `dotnet publish src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release -r linux-x64 --self-contained false`, framework-dependent, PDBs included, 192 files each side, measured uncompressed (`du -sb`). Base: the work base `637da3e730`, which holds 1a and 1b.
| | Base `637da3e730` | Head `cf97f7c7c6` | Delta |
|---|---:|---:|---:|
| Publish folder | 137,561,672 | 137,569,632 | **+7,960 B** |
| `Sprk.Bff.Api.dll` | 25,351,680 | 25,358,848 | +7,168 B |
| `Sprk.Bff.Api.pdb` | 4,380,884 | 4,381,676 | +792 B |

No package or file was added. The delta is far below the +5 MB threshold, and the total is far below 55 MB of first-party output (the folder is dominated by framework-dependent third-party assemblies, unchanged).

**ADR check (inline).**
- ADR-002: one owner. The service is the only writer of the assign-firm record and the allocator the only writer of the number. No plugin.
- ADR-019: stable codes, and the fixed sentence with no exception text.
- ADR-028 and the auth constraints: no new right, no new route. The caller's rights are asked again before a repeat records.
- ADR-010: no DI change.
- ADR-038: unit, contract and agreement tests; the live test is opt-in and skipped by default.
- CLAUDE.md 11, component justification. The new surface is one outcome, one code and one script check.
  - Existing: the 1b seam (`BuildDecision`), the recorder, and the firm-hold script's `-Verify`.
  - Extension: each of those is extended; nothing new is added beside them.
  - Cost of doing nothing: every `Assigned` leaves an "applied, no record" row, the Decision Record of an externally visible Tier4 decision never exists, and nobody is told.

**For 095d-2.**
- Close the shared Signal as ConditionCleared after the re-run, with the assign-firm record id in the close facts (16.7). Find the record by `BillingDecisionKey` (`sprk_invoice`, invoice, `DecisionIdFor(submission)`); `BillingDecisionRecorder.FindStoredResolvedSetAsync` uses the same lookup.
- Keep the stored `assignFirm` member: the full mapping lives only there, and check (f) and the repeat read it.
- A re-run that rewrites the findings must keep `assignFirm` intact, or the repeat answers `findings_unreadable` and check (f) loses the row.
- `rulesRerun` stays "pending" in the stored member. If 095d-2 changes it, the change must not alter `BuildDecision`'s hashed values (`firm`, `note`), because only the facts carry `rulesRerun`.
- The 16.6 live seam gate (K3 above) still has to run with the owner's go before 095d-1 merges to master. 095d-1 still never reaches master without 095d-2.

#### 16.9 fix round 1 (adversarial pass 1 of 095d-1c, 2026-11-04)

Scoped test and hardening fixes; no new endpoint, column, privilege or response-body change.

**What changed.**
- **A (F3), the repeat records from the stored member alone.** Test `World.Assign()` takes `deciderUserId` (default `Decider`) and `ct`. New `Repeat_ByAnotherCaller_RecordsTheStoredMemberAlone`: caller A with note N1 ends Incomplete (record fails); caller B repeats with N2; exactly one record is written by the repeat, equivalent to `BuildDecision(InvoiceId, SubmissionId, AssignFirmStoredDecision.Read(<stored findings>))`, decider A (not B), values carry N1 and not N2, no apply. `Repeat_ReasksTheRights_...` now repeats as another caller with a request note and, in the granted case, asserts the record's decider is the stored one and it holds no `note`.
- **B (F3), the cancellation filter.** `RecordFails_AfterTheApply_IsIncomplete_Never200` gains `timeout-cancel` (a `TaskCanceledException` while the request token is not cancelled: 503 incomplete `record`). New `RecordCancelled_ByTheCallersToken_Propagates`: the caller's token is cancelled while the record is in flight and the `OperationCanceledException` propagates (neither Incomplete nor 200); both locks released.
- **C (K3 hardening).** `JudgeRepeatAsync` refuses as the existing 409 `changed` when `stored.DecisionId != DecisionIdFor(submissionId)`, after the same-firm check and before the allocator is asked or anything is recorded. Test `Repeat_StoredDecisionIdNotDerivedFromTheSubmission_IsChanged_AndRecordsNothing` (nothing recorded, allocator not asked).
- **D (K2).** A repeat's completion log ("Decision Record {RecordId} ({State})") now also carries `{CallerUserId}`: the caller's systemuserid is threaded `SetFirmAsync` to `AssignHeldAsync`/`AfterConflictAsync` to `JudgeRepeatAsync` to `CompleteRepeatAsync` to `RecordAsync(callerUserId)`. The first call's log is unchanged (`callerUserId` null). Asserted in the A test and, negatively, in `Assigned_RecordsTheStoredDecision_AfterTheApply` (test `World` now captures logs through a `CapturingLogger`).
- **E (K1).** On a repeat, a filing mismatch whose code is `core_unresolved` (the derived core stamp is another record, or a permanent derivation error) logs at Error with `Permanent=true` and `FilingCode`; a transient fault does not. Response body unchanged. Test `Repeat_FilingMismatch_PermanentIsMarkedInTheLog` (transient: no marker; permanent: one marker).
- **F (K4).** `Get-AssignFirmStored` in `scripts/ebilling/Set-EbillingInvoiceFirmHoldColumn.ps1` (check (f)) treats a member as readable only with the keys `AssignFirmStoredDecision.Read` needs: `decisionId`, `firmId`, `deciderUserId` (Guids), `previousTrackingNumber`, `trackingNumber` (non-empty text), `sequence`, `firmSetLines` (integers), `decidedOn`, `firmMissingSignals` (`setReadAtUtc` and a `signals` array of `{id, policyCode}`), and for a `renamed` array each entry's `entity`, `id`, `column`, `from`, `to`. A member missing one, or findings JSON that does not parse, is FAIL under `-Verify` (INFO otherwise) with a neutral message that names only the submission id the script already prints; unreadable rows now also stop the "(f) OK" line. The script stays read-only (`Invoke-DvGet` only). No Pester tests exist for this script: parse-checked with `[System.Management.Automation.Language.Parser]::ParseFile` (0 errors) and the six helper functions were run over 16 sample submissions (good, empty signals/renamed, each required key missing, signals without `setReadAtUtc`, bad rename entry, findings not JSON, no member, blank): all classified as expected. That harness is a scratch script, not committed.

**Tests.** Filtered run (`InvoiceAssignFirm | BillingDecisionRecorder | InvoiceFirmHoldFieldSecurityAgreement | DecisionRecordWriter`): 0 failed, 454 passed (448 before this round; +6 cases: `timeout-cancel`, the cancel-propagates test, the another-caller repeat, the stored-id test and the two filing-marker cases). `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors.

**Seed proofs** (each restored; `git diff --exit-code` on the file passed after each restore, run on the committed tree):
- Seed 4, the repeat builds the decision from the request (`stored = stored with { DeciderUserId = deciderUserId, Note = note }` in the repeat branch of `AssignHeldAsync`): `Repeat_ByAnotherCaller_RecordsTheStoredMemberAlone` FAILS, and the tightened `Repeat_ReasksTheRights_..._None` FAILS (2 failed).
- Seed 5a, the filter simplified to `ex is not OperationCanceledException`: the `timeout-cancel` case FAILS (1 failed).
- Seed 5b, the filter removed (`catch (Exception ex)`): `RecordCancelled_ByTheCallersToken_Propagates` FAILS (1 failed).
- Seed 6, the stored-id check disabled (`if (false && ...)`): `Repeat_StoredDecisionIdNotDerivedFromTheSubmission_...` FAILS (1 failed).
- Not seeded: D and E are log-only; their tests assert the structured parameters (a log-only test proves the parameter is present, not that an operator reads it).

**Skipped / out of scope.** The writer's duplicate-record window (pre-existing, not 1c). Nothing else skipped.

rework: none task 095d-1c
