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

rework: none task 095c
