<#
.SYNOPSIS
    eBilling R1 task 018 - creates the UTBMS reference tables sprk_billingcodeset and sprk_billingcode (FR-08) and the
    default-code-set mapping lookups on sprk_mattertype_ref and sprk_practicearea_ref (FR-22).

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports every
    item as skipped and makes no write (no publish either).

    Items (tasks/018-utbms-code-sets.poml, spec FR-08):
      1  Table sprk_billingcodeset (organization-owned): primary name sprk_name, sprk_body (text, e.g. ABA / LOC),
         sprk_version, sprk_sourceurl (URL), and (task 054, D-93) the matching tuning text sprk_description and
         sprk_matchhints (multiline; read by code-set matching). Status is the standard statecode / statuscode (Active / Inactive).
      2  Table sprk_billingcode (organization-owned): primary name sprk_code, sprk_codeset (lookup -> sprk_billingcodeset,
         delete restricted), sprk_codetype local choice Task / Activity / Expense, sprk_phase, sprk_description,
         sprk_guidancetext (multiline; read by the coding Action). Status is statecode / statuscode.
      3  Alternate key sprk_billingcode_codeset_code_key on (sprk_codeset, sprk_code); waits for the index to be Active.
      4  Lookup sprk_defaultbillingcodeset -> sprk_billingcodeset on sprk_mattertype_ref and sprk_practicearea_ref.

    After it runs, re-run scripts/ebilling/Deploy-EbillingInvoiceLineSchema.ps1: task 010 deferred the Task / Activity /
    Expense code lookups on sprk_billingevent until sprk_billingcode exists.

    NEVER deletes or renames anything. If an existing column has the same logical name but a different type (or a lookup
    targets a different table, an existing choice has different options, or an existing key has other attributes), the
    script STOPS with exit code 2 and an ESCALATION message (project CLAUDE.md section 5).

    ADR-002: no plugin, business rule, calculated / rollup column or flow. Rows are loaded by Seed-EbillingBillingCodes.ps1.

    Required levels (DEF-006 / DEF-009): ApplicationRequired is enforced by FORMS only, not by API writes. The primary names
    are set to ApplicationRequired after the create (the create body's level is ignored). The key columns sprk_codeset and
    sprk_code are raised to SystemRequired (enforced on every write) when no existing row holds a null. sprk_codetype stays
    ApplicationRequired: required by the BFF write path and the seed, not enforced on API writes.

    Eventually consistent metadata reads: a 404 right after a create is not trusted (the item is re-read); a create
    refused with "already exists" counts as skipped after a type check on a settled read; a column create issued right
    after its table was created is retried while the table is still propagating. Customization / publish locks and
    transient faults are retried with backoff.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingBillingCodeSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingBillingCodeSchema.ps1

.NOTES
    Project: ebilling-r1, task 018. Dev only in R1. The Dataverse helpers live in EbillingSchema.psm1 (shared by the
    eBilling schema scripts). Pattern: scripts/ebilling/Deploy-EbillingInvoiceLineSchema.ps1
    (task 010); table creation follows scripts/Deploy-ChartDefinitionEntity.ps1; the alternate key follows
    scripts/Set-AssignedAccessLedgerSchema.ps1.
    Exit codes: 0 = done (created / skipped), 1 = error, 2 = escalation (destructive or conflicting).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster'
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
$CodeSetTable = 'sprk_billingcodeset'
$CodeTable = 'sprk_billingcode'
$KeySchemaName = 'sprk_billingcode_codeset_code_key'

# Shared Dataverse helpers (scripts/ebilling/EbillingSchema.psm1). The state lists are shared by reference.
$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $CodeSetTable
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$script:Expect = $State.Expect
$script:Planned = $State.Planned
$script:JustCreated = $State.JustCreated

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 018 - UTBMS code set schema" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    foreach ($ref in 'sprk_mattertype_ref', 'sprk_practicearea_ref') {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$ref')?`$select=LogicalName" -Retries 4)) {
            throw "$ref does not exist in $EnvironmentUrl."
        }
    }

    # --- 1. sprk_billingcodeset ---------------------------------------------------------------------------------
    Write-Host "1. Table $CodeSetTable" -ForegroundColor Cyan
    Add-Table -Table $CodeSetTable -Display 'Billing Code Set' -Plural 'Billing Code Sets' `
        -Description 'A versioned UTBMS code set (litigation, counseling, project, ...) that billing codes belong to.' `
        -PrimaryColumn 'sprk_name' -PrimaryDisplay 'Name' -PrimaryDescription 'Name of the code set.' -PrimaryLength 200
    Set-PrimaryNameLength $CodeSetTable 'sprk_name' 200
    Set-PrimaryNameRequired $CodeSetTable 'sprk_name'
    Add-Column 'sprk_body' (New-StringDef 'sprk_Body' 'Body' 'Standards body that publishes the set, e.g. ABA or LOC.' 100) 'String'
    Add-Column 'sprk_version' (New-StringDef 'sprk_Version' 'Version' 'Published version of the set, e.g. 1997 or 2023-04.' 50) 'String'
    Add-Column 'sprk_sourceurl' (New-StringDef 'sprk_SourceURL' 'Source URL' 'Where the set was downloaded from (utbms.com).' 500 'Url') 'String'
    # Task 054 (D-93): code-set matching tuning text, read by CodeSetProposalService and sent to the invoice-codeset-match Action.
    Add-Column 'sprk_description' (New-MemoDef 'sprk_Description' 'Description' 'The work this set''s task codes cover. Sent to the code-set matching Action and embedded with the set. An administrator edit is never overwritten by the seed.' 4000) 'Memo'
    Add-Column 'sprk_matchhints' (New-MemoDef 'sprk_MatchHints' 'Match Hints' 'Matter types and practice areas this set fits, synonyms, and what it is not for. Sent to the code-set matching Action. An administrator edit is never overwritten by the seed.' 4000) 'Memo'

    # --- 2. sprk_billingcode ------------------------------------------------------------------------------------
    Write-Host "2. Table $CodeTable" -ForegroundColor Cyan
    Add-Table -Table $CodeTable -Display 'Billing Code' -Plural 'Billing Codes' `
        -Description 'A UTBMS task, activity or expense code, with the guidance text the coding Action reads.' `
        -PrimaryColumn 'sprk_code' -PrimaryDisplay 'Code' -PrimaryDescription 'The code as billed, e.g. L110 or E101. Unique within its code set.' -PrimaryLength 100
    Set-PrimaryNameLength $CodeTable 'sprk_code' 100
    Add-Lookup -Column 'sprk_codeset' -SchemaName 'sprk_CodeSet' -RelSchema 'sprk_billingcodeset_billingcode_codeset' -Target $CodeSetTable `
        -Display 'Code Set' -Description 'The code set this code belongs to.' -DeleteBehavior 'Restrict' -RequiredLevel 'ApplicationRequired'
    # sprk_codeset and sprk_code are the alternate key columns: a code without a set (or without a code) cannot be addressed by the
    # key and a nameless code is unusable, so the platform enforces both on every write (SystemRequired; DEF-009).
    Set-LookupRequiredLevel $CodeTable 'sprk_codeset' 'SystemRequired'
    Set-StringRequiredLevel $CodeTable 'sprk_code' 'SystemRequired'
    Add-LocalChoice -Column 'sprk_codetype' -Schema 'sprk_CodeType' -Display 'Code Type' -Description 'Task, Activity or Expense.' `
        -Labels @('Task', 'Activity', 'Expense') -RequiredLevel 'ApplicationRequired'
    Add-Column 'sprk_phase' (New-StringDef 'sprk_Phase' 'Phase' 'Phase code the task rolls up to (e.g. L100). Empty for activity and expense codes.' 20) 'String'
    Add-Column 'sprk_description' (New-StringDef 'sprk_Description' 'Description' 'Short name of the code, e.g. Fact Investigation/Development.' 500) 'String'
    Add-Column 'sprk_guidancetext' (New-MemoDef 'sprk_GuidanceText' 'Guidance Text' 'Definition and usage guidance sent to the coding Action with the code. An administrator edit is never overwritten by the seed.' 10000) 'Memo'

    # --- 3. Alternate key ---------------------------------------------------------------------------------------
    Write-Host "3. Alternate key (sprk_codeset, sprk_code)" -ForegroundColor Cyan
    Set-EbEntity $CodeTable
    Add-AlternateKey -Table $CodeTable -Schema $KeySchemaName -Display 'Code Set and Code' -Attributes @('sprk_codeset', 'sprk_code')

    # --- 4. Default code set on the mapping tables --------------------------------------------------------------
    Write-Host "4. sprk_defaultbillingcodeset on the matter type and practice area tables" -ForegroundColor Cyan
    Set-EbEntity 'sprk_mattertype_ref'
    Add-Lookup -Column 'sprk_defaultbillingcodeset' -SchemaName 'sprk_DefaultBillingCodeSet' -RelSchema 'sprk_billingcodeset_mattertype_ref_defaultcodeset' -Target $CodeSetTable `
        -Display 'Default Billing Code Set' -Description 'UTBMS code set used to code invoice lines for matters of this type (FR-22).'
    Set-EbEntity 'sprk_practicearea_ref'
    Add-Lookup -Column 'sprk_defaultbillingcodeset' -SchemaName 'sprk_DefaultBillingCodeSet' -RelSchema 'sprk_billingcodeset_practicearea_ref_defaultcodeset' -Target $CodeSetTable `
        -Display 'Default Billing Code Set' -Description 'UTBMS code set used to code invoice lines for matters in this practice area (FR-22).'

    # --- Publish (only when something changed) ------------------------------------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$CodeSetTable</entity><entity>$CodeTable</entity><entity>sprk_mattertype_ref</entity><entity>sprk_practicearea_ref</entity></entities></importexportxml>"
        } | Out-Null
        Write-Host "  customizations published" -ForegroundColor Green
    }

    # --- Verification (metadata read of everything this script owns) --------------------------------------------
    if (-not $DryRun) {
        Write-Host "Verify" -ForegroundColor Cyan
        $bad = [System.Collections.Generic.List[string]]::new()
        foreach ($e in $script:Expect) {
            $a = $null
            for ($i = 0; $i -lt 12 -and -not $a; $i++) {
                $a = Get-Attr $e.Table $e.Column
                if (-not $a) { Start-Sleep -Seconds 5 }
            }
            if (-not $a) { $bad.Add("$($e.Table).$($e.Column) missing") }
            elseif ($a.AttributeType -ne $e.Type) { $bad.Add("$($e.Table).$($e.Column) is $($a.AttributeType), expected $($e.Type)") }
        }
        foreach ($t in $CodeSetTable, $CodeTable) {
            $own = Get-DvOrNull "EntityDefinitions(LogicalName='$t')?`$select=OwnershipType,PrimaryNameAttribute,EntitySetName" -Retries 4
            if (-not $own) { $bad.Add("$t not readable"); continue }
            Write-Host ("  {0}: {1}, primary name {2}, entity set {3}" -f $t, $own.OwnershipType, $own.PrimaryNameAttribute, $own.EntitySetName)
            $sc = Get-DvOrNull "EntityDefinitions(LogicalName='$t')/Attributes(LogicalName='statuscode')/Microsoft.Dynamics.CRM.StatusAttributeMetadata?`$expand=OptionSet(`$select=Options)" -Retries 4
            if ($sc) { Write-Host ("    statuscode: " + (($sc.OptionSet.Options | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)/state$($_.State)" }) -join ', ')) }
        }
        $k = Get-Keys $CodeTable | Where-Object { $_.SchemaName -eq $KeySchemaName } | Select-Object -First 1
        if (-not $k) { $bad.Add("key $KeySchemaName missing") }
        elseif ($k.EntityKeyIndexStatus -ne 'Active') { $bad.Add("key $KeySchemaName is $($k.EntityKeyIndexStatus), expected Active") }
        else { Write-Host ("  key {0} on ({1}): {2}" -f $k.SchemaName, (@($k.KeyAttributes) -join ', '), $k.EntityKeyIndexStatus) }
        if ($bad.Count -gt 0) { throw "Verification failed: $($bad -join '; ')" }
        Write-Host "  all $($script:Expect.Count) expected columns present with the right type" -ForegroundColor Green
    }
}
catch [EscalationException] {
    Write-Host ""
    Write-Host "ESCALATION - STOPPED, nothing further was changed: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Items already created stay (the script is re-runnable)." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
Write-Host ("created : {0}  {1}" -f $script:Created.Count, ($script:Created -join ', '))
Write-Host ("changed : {0}  {1}" -f $script:Changed.Count, ($script:Changed -join ', '))
Write-Host ("skipped : {0}" -f $script:Skipped.Count)
if ($State.Deferred.Count -gt 0) { Write-Host ("deferred: {0}  {1}" -f $State.Deferred.Count, ($State.Deferred -join ', ')) -ForegroundColor DarkYellow }
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
