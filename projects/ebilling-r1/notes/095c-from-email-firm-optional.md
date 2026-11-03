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
| Solution source | `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_invoicesubmission/Entity.xml` | The end state: `sprk_vendororg` RequiredLevel `none`; new `sprk_TrackingScope` (nvarchar 36, `systemrequired`, audited, not secured); the key `sprk_invoicesubmission_scope_trackingnumber` on (`sprk_trackingnumber`, `sprk_trackingscope`) replaces `sprk_invoicesubmission_firm_trackingnumber`. |
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

### 8.8 Provisioning (stamps)

A stamp imports SpaarkeMaster with the end state: the column SystemRequired, `sprk_vendororg` None, the scope key only. It has no rows, so it needs no backfill and no script. Until the key index is Active (seconds on an empty table) instances read P1: they count per firm and write the scope, and no key guards the row (the same window the firm key has on a new stamp today). Nothing else is needed from customer-provisioning-orchestration-r1.

Do not import this SpaarkeMaster version into an environment that already has submission rows before the script has run there: the import would add the column as SystemRequired with null rows and build the scope key over the null-free subset, leaving the firm key in place. That fails safe (the firm key still refuses a duplicate; a firm submission may need more attempts, or answer the retryable 503, until the script's backfill runs), but the order above is the supported path for dev.

### 8.9 Deviations and known limits

- **Deviation: the probe is a copy of the Compose request shape, not a call into `ComposeIdentityKeyHealthCheck`.** Its fetch is a `protected virtual` member of a hosted health check for another table; making it shared would change the Compose check (out of scope).
- **Deviation: a null firm before P2 is a refusal (`firm_required`), not an exception.** The allocator is the single owner of the number, so it holds the rule as defence in depth; 095c-2 maps the code to the 400 and decides whether the intake checks the phase earlier.
- **Publish-size delta and CVE check:** not measured in this part (no package added, three small source files). 095c-4's full gate measures it against a fresh master build.
- K2: the phase never moves backward in a running process. If the scope key were dropped after P2, a running instance would keep counting per scope until restarted. No flow drops it.
- K2: an unreadable metadata read leaves an instance at its cached phase for 5 minutes. In P0 that is today's behaviour, and after step 5 the stale case above applies.
- K4: `sprk_trackingscope` is not field-secured. A user with Write on a submission could edit it natively. It decides no access (the ArchTest above); it is refused to app-only generic writers. Field security would need a profile change and a uac-r2 notice; not asked.

## 9. Notice for uac-r2

For uac-r2 (and ontology-r1, SPA-r3). Nothing has been run. The main session runs the script with the owner's OK after this notice.

**What changes on `sprk_invoicesubmission`.**

- `sprk_vendororg` (the firm) goes from SystemRequired to None. Only the internal from-email route, mode New, will ever send a null firm (095c-2). The external SPA route and the internal wizard route keep requiring it.
- A new text column `sprk_trackingscope` (36 characters, SystemRequired from step 5): the firm id in D form, or the literal `none`.
- The key changes. The old key is `sprk_invoicesubmission_firm_trackingnumber` on (`sprk_trackingnumber`, `sprk_vendororg`). The new key is `sprk_invoicesubmission_scope_trackingnumber` on (`sprk_trackingnumber`, `sprk_trackingscope`). The old key is retired only after the new one reads Active.

**Backfill rule.** Each existing row's scope is its `sprk_vendororg` id in lower-case D form. A row with no firm gets `none`; on dev there are no such rows (10 rows, all with a firm). The script verifies every row against this rule before it creates the key, and stops if any row differs.

**Access: nothing changes.**

- The scope is a numbering column, not an access input. External visibility of a submission is still decided by `sprk_vendororg` and the core record: `InvoiceSubmissionService.ListAsync` filters the firm in the caller's firms, and `DecideReadOneAsync` and the external replacement gate need the view's firm in `scope.FirmIds`.
- A no-firm submission therefore stays invisible externally: the list omits it and its single read is the uniform 404.
- `SubmissionTrackingScopeNotAccessGuardTests` (ArchTests) fails if any of those three gates, or any unreviewed type, names `sprk_trackingscope`.
- The column is added to `AccessColumns` for `sprk_invoicesubmission`, so an app-only generic writer is refused. Its only writer is the allocator, at create, plus the script's one backfill.

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

rework: none task 095c
