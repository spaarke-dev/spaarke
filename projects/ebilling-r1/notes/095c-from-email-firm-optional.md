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
- Publish size (bff-hygiene item 4, `dotnet publish -c Release`, zipped with PDBs, from fresh `git archive` trees): PUBLISH_RESULT. No package changed, so there is no new CVE (no `PackageReference` edits in this diff).

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

rework: none task 095c
