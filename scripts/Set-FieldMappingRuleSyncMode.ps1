#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Sets every Copy field-mapping rule's sync mode to the value its CLASS calls for - the data step of
    field-mapping-server-write-path-r1 tasks 005 (FR-05d, decision Q3) and 040 (class-aware, owner 4a). Dry-run by default;
    idempotent.

.DESCRIPTION
    /push (task 040) applies the Copy rules whose sprk_syncmode is Manual Refresh (1). Rules authored before that default to
    One-time (0) or to an empty value, so each Copy rule must be given the value its class calls for, once:

      CLASS          RULE'S TARGET COLUMN                                            VALUE
      Assigned-To    an access-conferring Assigned-To column of its target table      0  One-time
                     (the BFF's MembershipOptionsDefaults.CanonicalAccessConferringRegistry)
      Filing         a filing column of its target table - a typed filing lookup,     0  One-time ("never re-pushable")
                     the regarding pair or a regarding stamp (RecordFamilyEdges)
      Plain data     anything else                                                     1  Manual Refresh

    An Assigned-To rule is applied by a push only on a request that opts in (includeAssignedTo), whatever its value; a filing
    rule is never applied by a push. Their value stays 0 so no ordinary refresh can re-file or re-assign a record.

    The two class tables below are COPIES of the BFF's registries (a script cannot read them); the tests in
    SyncModeMigrationClassDataTests hold each copy to its source, so a column added to the registry or to a family edge fails a
    build until it is added here. A rule whose class cannot be decided (no resolvable target table) is never guessed: it stops the
    run before any write (exit 2).

    WHICH ROWS (the only rows it ever writes):
      sprk_fieldmappingrule rows with sprk_mapping_type = 0 (Copy), ACTIVE (statecode 0), whose sprk_syncmode is not the value its
      class calls for: Plain data at 0 or empty -> 1 (Migrate); Assigned-To / Filing at 1 -> 0 (Demote). An EMPTY sprk_syncmode
      reads as One-time, so an Assigned-To or Filing rule that is empty is already as the class requires and is not written.
    NEVER written: Default (1), Concat (2) and Template (3) rules; any rule already at its class value; any other column; no row
    is deleted or created. The PATCH body is exactly { "sprk_syncmode": <0 or 1> }, with If-Match '*' so it can only update.

    INACTIVE Copy rules short of their class value are listed (InactiveNotMigrated) but never touched: Dataverse refuses to update
    an inactive row, and reactivating one is a decision for the rule's owner. They fail -Verify until reactivated and re-run.

    REFUSES TO GUESS: a rule whose mapping type is outside 0-3, whose sprk_syncmode is outside {empty, 0, 1}, or whose target table
    cannot be resolved stops the run before any write (exit 2) so the mapping can be decided by a person.

    SAFETY MODEL
      - Dry-run is the DEFAULT: with no switch, nothing is written. -WhatIf forces a dry-run even with -Apply.
      - -Apply writes, then READS BACK every row and checks sprk_syncmode equals the written value (Dataverse ignores an
        unrecognised property; "the request succeeded" is not evidence of the write).
      - Idempotent: a changed rule is at its class value, so a second -Apply finds nothing to change (ToMigrate 0, ToDemote 0).
      - -Verify is read-only: exits 0 only when EVERY Copy rule (active or not) is at its class value, else exits 1.
      - The values ship only after the OWNER has reviewed a dry run for the environment (task 040). A live -Apply needs the
        owner's explicit OK (project CLAUDE.md section 5). The token is the operator's own az CLI identity; no secret is stored here.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com. Required except with -SelfTest.

.PARAMETER Apply
    Write mode. Without it the script always dry-runs.

.PARAMETER WhatIf
    Forces a dry-run even when -Apply is passed.

.PARAMETER Verify
    Read-only report; exits non-zero while any Copy rule is not at its class value. With -Apply, runs after the writes.

.PARAMETER SelfTest
    Offline: runs the pure row classifier and an in-memory apply twice over inline cases; exits 1 on any mismatch.

.EXAMPLE
    .\Set-FieldMappingRuleSyncMode.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: lists every Copy rule that would change, with its class, its current mode and the value it would take, and the
    totals. Zero writes. This is the report the owner reviews.

.EXAMPLE
    .\Set-FieldMappingRuleSyncMode.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply -Verify
    Apply (owner OK required, after a reviewed dry run), read each row back, then verify every Copy rule is at its class value.

.EXAMPLE
    .\Set-FieldMappingRuleSyncMode.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify
    Read-only baseline: exit 1 while any Copy rule is not at its class value.

.EXAMPLE
    .\Set-FieldMappingRuleSyncMode.ps1 -SelfTest
    Offline checks of the classifier and of idempotency; needs no environment.
#>

[CmdletBinding()]
param(
    [string]$EnvironmentUrl,

    [switch]$Apply,

    [switch]$WhatIf,

    [switch]$Verify,

    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

# sprk_mapping_type option values (Dataverse describe, spaarkedev1 2026-10-21): Copy 0, Default 1, Concat 2, Template 3.
$MappingCopy = 0
$KnownMappingTypes = @(0, 1, 2, 3)
# sprk_syncmode option values: One-time 0, Manual Refresh 1; an empty value is a rule created before the column was set.
$SyncOneTime = 0
$SyncManualRefresh = 1

# -- CLASS DATA: copies of the BFF's registries, held to their sources by SyncModeMigrationClassDataTests ----------------
# Keys are TARGET table logical names; values are the rule TARGET columns of that class on that table (lower case).
# BEGIN-CLASS-DATA assigned-to  (source: MembershipOptionsDefaults.CanonicalAccessConferringRegistry)
$AssignedToColumns = @{
    'sprk_analysis' = @('sprk_assignedattorney1', 'sprk_assignedattorney2', 'sprk_assignedparalegal1', 'sprk_assignedparalegal2')
    'sprk_event' = @('sprk_assignedattorney1', 'sprk_assignedattorney2', 'sprk_assignedlawfirm1', 'sprk_assignedlawfirm2', 'sprk_assignedparalegal1', 'sprk_assignedparalegal2', 'sprk_assignedto', 'sprk_assignedto1', 'sprk_assignedto2', 'sprk_assignedtoexternal', 'sprk_assignedtointernal')
    'sprk_invoice' = @('sprk_assignedto1', 'sprk_assignedto2', 'sprk_assignedtoattorney1', 'sprk_assignedtoattorney2', 'sprk_assignedtoparalegal1', 'sprk_assignedtoparalegal2')
    'sprk_matter' = @('sprk_assignedattorney1', 'sprk_assignedattorney2', 'sprk_assignedlawfirm1', 'sprk_assignedlawfirm2', 'sprk_assignedparalegal1', 'sprk_assignedparalegal2', 'sprk_assignedtoexternal', 'sprk_assignedtointernal')
    'sprk_project' = @('sprk_assignedattorney1', 'sprk_assignedattorney2', 'sprk_assignedlawfirm1', 'sprk_assignedlawfirm2', 'sprk_assignedparalegal1', 'sprk_assignedparalegal2', 'sprk_assignedtoexternal', 'sprk_assignedtointernal')
    'sprk_todo' = @('sprk_assignedto')
    'sprk_workassignment' = @('sprk_assignedattorney1', 'sprk_assignedattorney2', 'sprk_assignedlawfirm1', 'sprk_assignedlawfirm2', 'sprk_assignedlawfirmattorney1', 'sprk_assignedparalegal1', 'sprk_assignedparalegal2', 'sprk_assignedto', 'sprk_assignedtoexternal', 'sprk_assignedtointernal')
}
# END-CLASS-DATA assigned-to
# BEGIN-CLASS-DATA filing  (source: RecordFamilyEdges.All - every edge's lookup column, plus the regarding pair's type column)
$FilingColumns = @{
    'sprk_agreement' = @('sprk_regardingdocument', 'sprk_regardingmatter', 'sprk_regardingproject')
    'sprk_analysis' = @('sprk_documentid', 'sprk_regardingbudget', 'sprk_regardingcommunication', 'sprk_regardingdocument', 'sprk_regardinginvoice', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingservicerequest', 'sprk_regardingworkassignment')
    'sprk_analysisoutput' = @('sprk_analysisid')
    'sprk_attachmentartifact' = @('sprk_document', 'sprk_emailartifact')
    'sprk_billingevent' = @('sprk_invoice', 'sprk_matter', 'sprk_project', 'sprk_regardingworkassignment')
    'sprk_budget' = @('sprk_matter', 'sprk_project')
    'sprk_budgetbucket' = @('sprk_budget')
    'sprk_budgetrevision' = @('sprk_budget', 'sprk_matter')
    'sprk_communication' = @('sprk_communicationthread', 'sprk_regardinganalysis', 'sprk_regardingbudget', 'sprk_regardingevent', 'sprk_regardinginvoice', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingreportcard', 'sprk_regardingservicerequest', 'sprk_regardingworkassignment')
    'sprk_communicationattachment' = @('sprk_communication', 'sprk_document')
    'sprk_communicationparticipant' = @('sprk_communication')
    'sprk_communicationthread' = @('sprk_regardinganalysis', 'sprk_regardingbudget', 'sprk_regardingevent', 'sprk_regardinginvoice', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingservicerequest', 'sprk_regardingworkassignment')
    'sprk_decisionrecord' = @('sprk_matter', 'sprk_project', 'sprk_regardingcommunication', 'sprk_regardingdocument', 'sprk_regardingevent', 'sprk_regardinginvoice', 'sprk_regardinginvoiceline', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingservicerequest', 'sprk_regardingtodo', 'sprk_regardingworkassignment', 'sprk_workassignment')
    'sprk_document' = @('sprk_invoice', 'sprk_matter', 'sprk_parentdocument', 'sprk_project', 'sprk_relatedagreement', 'sprk_relatedcommunication', 'sprk_relatedevent', 'sprk_relatedinvoice', 'sprk_relatedmatter', 'sprk_relatedproject', 'sprk_relatedservicerequest', 'sprk_relatedtodo', 'sprk_relatedworkassignment', 'sprk_workassignment')
    'sprk_emailartifact' = @('sprk_document')
    'sprk_emailreviewlog' = @('sprk_communication')
    'sprk_event' = @('sprk_regardingagreement', 'sprk_regardinganalysis', 'sprk_regardingbudget', 'sprk_regardingcommunication', 'sprk_regardingevent', 'sprk_regardinginvoice', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingreportcard', 'sprk_regardingservicerequest', 'sprk_regardingworkassignment')
    'sprk_eventlog' = @('sprk_event')
    'sprk_externalidentifier' = @('sprk_regardinginvoice', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingworkassignment')
    'sprk_fileversion' = @('sprk_document')
    'sprk_invoice' = @('sprk_matter', 'sprk_project', 'sprk_regardingagreement', 'sprk_regardingworkassignment')
    'sprk_invoiceapproval' = @('sprk_invoice', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingworkassignment')
    'sprk_invoicecorrection' = @('sprk_invoice', 'sprk_invoiceline', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingworkassignment')
    'sprk_invoicesubmission' = @('sprk_document', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingworkassignment')
    'sprk_kpiassessment' = @('sprk_matter', 'sprk_project', 'sprk_reportcard')
    'sprk_memo' = @('sprk_regardingagreement', 'sprk_regardinganalysis', 'sprk_regardingbudget', 'sprk_regardingcommunication', 'sprk_regardingdocument', 'sprk_regardingevent', 'sprk_regardinginvoice', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingservicerequest', 'sprk_regardingworkassignment', 'sprk_reportcard')
    'sprk_policy' = @('sprk_matter')
    'sprk_policyversion' = @('sprk_policy')
    'sprk_project' = @('sprk_regardingrecordid', 'sprk_regardingrecordtype')
    'sprk_reportcard' = @('sprk_regardingmatter', 'sprk_regardingproject')
    'sprk_signal' = @('sprk_decisionrecord', 'sprk_matter', 'sprk_regardingcommunication', 'sprk_regardingdocument', 'sprk_regardingevent', 'sprk_regardinginvoice', 'sprk_regardinginvoiceline', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingservicerequest', 'sprk_regardingtodo', 'sprk_regardingworkassignment')
    'sprk_spendsignal' = @('sprk_matter', 'sprk_project', 'sprk_snapshot')
    'sprk_spendsnapshot' = @('sprk_matter', 'sprk_project')
    'sprk_todo' = @('sprk_regardingagreement', 'sprk_regardinganalysis', 'sprk_regardingbudget', 'sprk_regardingcommunication', 'sprk_regardingdocument', 'sprk_regardingevent', 'sprk_regardinginvoice', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingreportcard', 'sprk_regardingservicerequest', 'sprk_regardingworkassignment')
    'sprk_workassignment' = @('sprk_regardingevent', 'sprk_regardinginvoice', 'sprk_regardingmatter', 'sprk_regardingproject')
}
# END-CLASS-DATA filing

# PURE: the class of a rule's target column: 'AssignedTo', 'Filing' or 'Plain'. Names compare case-insensitively.
function Get-RuleClass {
    param([string]$Entity, [string]$Field)
    $e = "$Entity".Trim().ToLowerInvariant()
    $f = "$Field".Trim().ToLowerInvariant()
    if ($AssignedToColumns.ContainsKey($e) -and $AssignedToColumns[$e] -contains $f) { return 'AssignedTo' }
    if ($FilingColumns.ContainsKey($e) -and $FilingColumns[$e] -contains $f) { return 'Filing' }
    return 'Plain'
}

# PURE: the sync mode a class calls for.
function Get-ClassValue {
    param([string]$Class)
    if ($Class -eq 'Plain') { return $SyncManualRefresh }
    return $SyncOneTime
}

# PURE: what to do with one rule row. $TargetEntity is the logical name of the rule's profile's target table ('' when unknown).
# Outcome is one of:
#   Migrate               a Plain-data Copy rule, active, sprk_syncmode empty or 0   -> PATCH to 1
#   Demote                an Assigned-To / Filing Copy rule, active, at 1            -> PATCH to 0
#   Aligned               a Copy rule already at its class value (an empty value reads as One-time)
#   InactiveNotMigrated   a Copy rule, inactive, short of its class value -> reported, never written (Dataverse refuses the update)
#   NotCopy               Default / Concat / Template -> never touched
#   Unknown               mapping type outside 0-3, sync mode outside {empty, 0, 1}, or a Copy rule whose target table is unknown
#                         -> the run stops, nothing is written
# Current is the sync mode as a display string: 'empty', 'One-time' or 'Manual Refresh' (or the raw value when unknown).
# Class is 'AssignedTo' | 'Filing' | 'Plain' for a Copy rule, else ''. Target is the value the rule should take (or $null).
function Get-RuleDecision {
    param($Row, [string]$TargetEntity)
    $type = $Row.sprk_mapping_type
    $sync = $Row.sprk_syncmode
    $current = if ($null -eq $sync) { 'empty' } elseif ($sync -eq $SyncOneTime) { 'One-time' } elseif ($sync -eq $SyncManualRefresh) { 'Manual Refresh' } else { "unrecognised ($sync)" }
    if ($null -eq $type -or $KnownMappingTypes -notcontains [int]$type) {
        return @{ Outcome = 'Unknown'; Current = $current; Class = ''; Target = $null; Reason = "sprk_mapping_type '$type' is outside the known values 0-3" }
    }
    if ($null -ne $sync -and $sync -ne $SyncOneTime -and $sync -ne $SyncManualRefresh) {
        return @{ Outcome = 'Unknown'; Current = $current; Class = ''; Target = $null; Reason = "sprk_syncmode '$sync' is outside the known values (empty, 0, 1)" }
    }
    if ([int]$type -ne $MappingCopy) { return @{ Outcome = 'NotCopy'; Current = $current; Class = ''; Target = $null; Reason = $null } }
    if ([string]::IsNullOrWhiteSpace($TargetEntity) -or [string]::IsNullOrWhiteSpace($Row.sprk_targetfield)) {
        return @{ Outcome = 'Unknown'; Current = $current; Class = ''; Target = $null; Reason = "the rule's target table or target column cannot be resolved, so its class cannot be decided" }
    }
    $class = Get-RuleClass $TargetEntity $Row.sprk_targetfield
    $want = Get-ClassValue $class
    $effective = if ($null -eq $sync) { $SyncOneTime } else { [int]$sync }
    if ($effective -eq $want) { return @{ Outcome = 'Aligned'; Current = $current; Class = $class; Target = $want; Reason = $null } }
    $active = ($null -eq $Row.statecode) -or ([int]$Row.statecode -eq 0)
    if (-not $active) { return @{ Outcome = 'InactiveNotMigrated'; Current = $current; Class = $class; Target = $want; Reason = 'the rule is inactive; Dataverse refuses to update an inactive row' } }
    $outcome = if ($want -eq $SyncManualRefresh) { 'Migrate' } else { 'Demote' }
    return @{ Outcome = $outcome; Current = $current; Class = $class; Target = $want; Reason = $null }
}

# PURE: classify every row and total them. $EntityOf maps a profile id to its target table logical name; a row may also carry
# its own TargetEntity (used by the self-test). Returns @{ Items; Counts; ByClass; Unknown }.
function Get-MigrationPlan {
    param([object[]]$Rows, [hashtable]$EntityOf = @{})
    $counts = [ordered]@{ Migrate = 0; Demote = 0; Aligned = 0; InactiveNotMigrated = 0; NotCopy = 0; Unknown = 0 }
    $byClass = [ordered]@{ AssignedTo = 0; Filing = 0; Plain = 0 }
    $items = [System.Collections.Generic.List[object]]::new()
    foreach ($row in @($Rows)) {
        $profileKey = "$($row._sprk_fieldmappingprofile_value)"
        $entity = if ($profileKey -and $EntityOf.ContainsKey($profileKey)) { $EntityOf[$profileKey] } elseif ($row.PSObject.Properties['TargetEntity']) { $row.TargetEntity } else { '' }
        $d = Get-RuleDecision $row $entity
        $counts[$d.Outcome]++
        if ($d.Class) { $byClass[$d.Class]++ }
        $items.Add(@{ Row = $row; Decision = $d; Entity = $entity })
    }
    return @{ Items = $items.ToArray(); Counts = $counts; ByClass = $byClass; Unknown = @($items | Where-Object { $_.Decision.Outcome -eq 'Unknown' }) }
}

# PURE: -Verify passes only when no Copy rule, active or not, is short of its class value.
function Test-VerifyPass {
    param([hashtable]$Plan)
    return ($Plan.Counts.Migrate -eq 0) -and ($Plan.Counts.Demote -eq 0) -and ($Plan.Counts.InactiveNotMigrated -eq 0) -and ($Plan.Counts.Unknown -eq 0)
}

# == SELF-TEST (offline) ==================================================================================================
if ($SelfTest) {
    $failures = [System.Collections.Generic.List[string]]::new()
    function Assert-Case { param([string]$Name, $Actual, $Expected) if ($Actual -ne $Expected) { $failures.Add("$Name : expected '$Expected', got '$Actual'") } else { Write-Host "  ok   $Name" } }

    # A class table that is not empty: the data block was lost if this fails.
    Assert-Case 'the Assigned-To table is not empty' ($AssignedToColumns.Count -gt 0) $true
    Assert-Case 'the filing table is not empty' ($FilingColumns.Count -gt 0) $true

    # The class of a target column.
    Assert-Case 'an Assigned-To column of a matter' (Get-RuleClass 'sprk_matter' 'sprk_assignedattorney1') 'AssignedTo'
    Assert-Case 'the same name on a table that does not list it is plain' (Get-RuleClass 'sprk_document' 'sprk_assignedattorney1') 'Plain'
    Assert-Case 'a filing lookup of an event' (Get-RuleClass 'sprk_event' 'sprk_regardingmatter') 'Filing'
    Assert-Case 'the regarding pair type of a project' (Get-RuleClass 'sprk_project' 'sprk_regardingrecordtype') 'Filing'
    Assert-Case 'an ordinary text column' (Get-RuleClass 'sprk_event' 'sprk_description') 'Plain'
    Assert-Case 'the class is read without regard to case' (Get-RuleClass 'SPRK_Matter' 'SPRK_AssignedAttorney1') 'AssignedTo'
    Assert-Case 'a plain column takes Manual Refresh' (Get-ClassValue 'Plain') 1
    Assert-Case 'an Assigned-To column stays One-time' (Get-ClassValue 'AssignedTo') 0
    Assert-Case 'a filing column stays One-time' (Get-ClassValue 'Filing') 0

    $cases = @(
        @{ N = 'plain Copy, active, empty -> Migrate'; E = 'sprk_event'; R = @{ sprk_mapping_type = 0; sprk_syncmode = $null; statecode = 0; sprk_targetfield = 'sprk_description' }; O = 'Migrate' }
        @{ N = 'plain Copy, active, One-time -> Migrate'; E = 'sprk_event'; R = @{ sprk_mapping_type = 0; sprk_syncmode = 0; statecode = 0; sprk_targetfield = 'sprk_description' }; O = 'Migrate' }
        @{ N = 'plain Copy, active, Manual Refresh -> Aligned'; E = 'sprk_event'; R = @{ sprk_mapping_type = 0; sprk_syncmode = 1; statecode = 0; sprk_targetfield = 'sprk_description' }; O = 'Aligned' }
        @{ N = 'plain Copy, inactive, empty -> InactiveNotMigrated'; E = 'sprk_event'; R = @{ sprk_mapping_type = 0; sprk_syncmode = $null; statecode = 1; sprk_targetfield = 'sprk_description' }; O = 'InactiveNotMigrated' }
        @{ N = 'plain Copy, inactive, Manual Refresh -> Aligned'; E = 'sprk_event'; R = @{ sprk_mapping_type = 0; sprk_syncmode = 1; statecode = 1; sprk_targetfield = 'sprk_description' }; O = 'Aligned' }
        @{ N = 'Assigned-To Copy, empty -> Aligned (empty reads as One-time, never written)'; E = 'sprk_matter'; R = @{ sprk_mapping_type = 0; sprk_syncmode = $null; statecode = 0; sprk_targetfield = 'sprk_assignedattorney1' }; O = 'Aligned' }
        @{ N = 'Assigned-To Copy, One-time -> Aligned'; E = 'sprk_matter'; R = @{ sprk_mapping_type = 0; sprk_syncmode = 0; statecode = 0; sprk_targetfield = 'sprk_assignedattorney1' }; O = 'Aligned' }
        @{ N = 'Assigned-To Copy, Manual Refresh -> Demote'; E = 'sprk_matter'; R = @{ sprk_mapping_type = 0; sprk_syncmode = 1; statecode = 0; sprk_targetfield = 'sprk_assignedattorney1' }; O = 'Demote' }
        @{ N = 'Assigned-To Copy, Manual Refresh, inactive -> InactiveNotMigrated'; E = 'sprk_matter'; R = @{ sprk_mapping_type = 0; sprk_syncmode = 1; statecode = 1; sprk_targetfield = 'sprk_assignedattorney1' }; O = 'InactiveNotMigrated' }
        @{ N = 'filing Copy, empty -> Aligned'; E = 'sprk_event'; R = @{ sprk_mapping_type = 0; sprk_syncmode = $null; statecode = 0; sprk_targetfield = 'sprk_regardingmatter' }; O = 'Aligned' }
        @{ N = 'filing Copy, Manual Refresh -> Demote'; E = 'sprk_event'; R = @{ sprk_mapping_type = 0; sprk_syncmode = 1; statecode = 0; sprk_targetfield = 'sprk_regardingmatter' }; O = 'Demote' }
        @{ N = 'Default, empty -> NotCopy (never written)'; E = 'sprk_event'; R = @{ sprk_mapping_type = 1; sprk_syncmode = $null; statecode = 0; sprk_targetfield = 'sprk_description' }; O = 'NotCopy' }
        @{ N = 'Concat, One-time -> NotCopy (never written)'; E = 'sprk_event'; R = @{ sprk_mapping_type = 2; sprk_syncmode = 0; statecode = 0; sprk_targetfield = 'sprk_description' }; O = 'NotCopy' }
        @{ N = 'Template, empty -> NotCopy (never written)'; E = 'sprk_event'; R = @{ sprk_mapping_type = 3; sprk_syncmode = $null; statecode = 0; sprk_targetfield = 'sprk_description' }; O = 'NotCopy' }
        @{ N = 'unknown mapping type 9 -> Unknown'; E = 'sprk_event'; R = @{ sprk_mapping_type = 9; sprk_syncmode = 0; statecode = 0; sprk_targetfield = 'sprk_description' }; O = 'Unknown' }
        @{ N = 'empty mapping type -> Unknown'; E = 'sprk_event'; R = @{ sprk_mapping_type = $null; sprk_syncmode = 0; statecode = 0; sprk_targetfield = 'sprk_description' }; O = 'Unknown' }
        @{ N = 'unknown sync mode 7 on a Copy rule -> Unknown'; E = 'sprk_event'; R = @{ sprk_mapping_type = 0; sprk_syncmode = 7; statecode = 0; sprk_targetfield = 'sprk_description' }; O = 'Unknown' }
        @{ N = 'unknown sync mode 7 on a Concat rule -> Unknown (not silently skipped)'; E = 'sprk_event'; R = @{ sprk_mapping_type = 2; sprk_syncmode = 7; statecode = 0; sprk_targetfield = 'sprk_description' }; O = 'Unknown' }
        @{ N = 'a Copy rule whose target table is unknown -> Unknown (class never guessed)'; E = ''; R = @{ sprk_mapping_type = 0; sprk_syncmode = $null; statecode = 0; sprk_targetfield = 'sprk_description' }; O = 'Unknown' }
        @{ N = 'a Copy rule with no target column -> Unknown'; E = 'sprk_event'; R = @{ sprk_mapping_type = 0; sprk_syncmode = $null; statecode = 0; sprk_targetfield = $null }; O = 'Unknown' }
    )
    foreach ($c in $cases) { Assert-Case $c.N (Get-RuleDecision ([pscustomobject]$c.R) $c.E).Outcome $c.O }

    # An in-memory table, applied twice: the second pass must find nothing, and only Copy rows of the right class may change.
    $table = @(
        [pscustomobject]@{ id = 'a'; TargetEntity = 'sprk_event'; sprk_targetfield = 'sprk_description'; sprk_mapping_type = 0; sprk_syncmode = $null; statecode = 0 }
        [pscustomobject]@{ id = 'b'; TargetEntity = 'sprk_event'; sprk_targetfield = 'sprk_description'; sprk_mapping_type = 0; sprk_syncmode = 0; statecode = 0 }
        [pscustomobject]@{ id = 'c'; TargetEntity = 'sprk_event'; sprk_targetfield = 'sprk_description'; sprk_mapping_type = 0; sprk_syncmode = 1; statecode = 0 }
        [pscustomobject]@{ id = 'd'; TargetEntity = 'sprk_event'; sprk_targetfield = 'sprk_description'; sprk_mapping_type = 1; sprk_syncmode = $null; statecode = 0 }
        [pscustomobject]@{ id = 'e'; TargetEntity = 'sprk_event'; sprk_targetfield = 'sprk_description'; sprk_mapping_type = 2; sprk_syncmode = 0; statecode = 0 }
        [pscustomobject]@{ id = 'f'; TargetEntity = 'sprk_event'; sprk_targetfield = 'sprk_description'; sprk_mapping_type = 3; sprk_syncmode = $null; statecode = 0 }
        [pscustomobject]@{ id = 'g'; TargetEntity = 'sprk_event'; sprk_targetfield = 'sprk_description'; sprk_mapping_type = 0; sprk_syncmode = $null; statecode = 1 }
        [pscustomobject]@{ id = 'h'; TargetEntity = 'sprk_matter'; sprk_targetfield = 'sprk_assignedattorney1'; sprk_mapping_type = 0; sprk_syncmode = 1; statecode = 0 }
        [pscustomobject]@{ id = 'i'; TargetEntity = 'sprk_matter'; sprk_targetfield = 'sprk_assignedattorney2'; sprk_mapping_type = 0; sprk_syncmode = $null; statecode = 0 }
        [pscustomobject]@{ id = 'j'; TargetEntity = 'sprk_event'; sprk_targetfield = 'sprk_regardingmatter'; sprk_mapping_type = 0; sprk_syncmode = 1; statecode = 0 }
    )
    $before = @{}; foreach ($r in $table) { $before[$r.id] = "$($r.sprk_mapping_type)|$($r.sprk_syncmode)|$($r.statecode)" }
    for ($pass = 1; $pass -le 2; $pass++) {
        $plan = Get-MigrationPlan $table
        Assert-Case "pass $pass ToMigrate" $plan.Counts.Migrate $(if ($pass -eq 1) { 2 } else { 0 })
        Assert-Case "pass $pass ToDemote" $plan.Counts.Demote $(if ($pass -eq 1) { 2 } else { 0 })
        foreach ($i in $plan.Items | Where-Object { $_.Decision.Outcome -in 'Migrate', 'Demote' }) { $i.Row.sprk_syncmode = $i.Decision.Target }
    }
    $changed = @($table | Where-Object { "$($_.sprk_mapping_type)|$($_.sprk_syncmode)|$($_.statecode)" -ne $before[$_.id] } | ForEach-Object { $_.id }) -join ','
    Assert-Case 'only the active Copy rows short of their class value changed' $changed 'a,b,h,j'
    Assert-Case 'Verify fails while an inactive Copy rule is short' (Test-VerifyPass (Get-MigrationPlan $table)) $false
    $table[6].statecode = 0; $table[6].sprk_syncmode = 1
    Assert-Case 'Verify passes when every Copy rule is at its class value' (Test-VerifyPass (Get-MigrationPlan $table)) $true

    if ($failures.Count -gt 0) { $failures | ForEach-Object { Write-Host "  FAIL $_" -ForegroundColor Red }; Write-Host "SELF-TEST FAILED ($($failures.Count))" -ForegroundColor Red; exit 1 }
    Write-Host 'SELF-TEST PASSED'
    exit 0
}

if ([string]::IsNullOrWhiteSpace($EnvironmentUrl)) { throw '-EnvironmentUrl is required (except with -SelfTest).' }
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$IsDryRun = (-not $Apply.IsPresent) -or $WhatIf.IsPresent

# -- Auth: the operator's own az CLI identity. No secrets in this script or the repo. ---------------------------------------
function Get-DvToken {
    $t = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
    if (-not $t) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
    return $t
}
$script:Token = Get-DvToken
$script:TokenAt = Get-Date
function Get-DvHeaders {
    param([switch]$ForWrite)
    # az tokens last ~60-90 minutes; a long run re-acquires rather than failing half-way through.
    if (((Get-Date) - $script:TokenAt).TotalMinutes -gt 45) { $script:Token = Get-DvToken; $script:TokenAt = Get-Date }
    $h = @{
        Authorization      = "Bearer $script:Token"
        Accept             = 'application/json'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
    }
    if ($ForWrite) {
        $h['Content-Type'] = 'application/json'
        $h['If-Match'] = '*'            # update only; never an upsert that could mint a row
        $h['Prefer'] = 'return=minimal'
    }
    return $h
}

function Invoke-DvGet {
    param([Parameter(Mandatory)][string]$RelativePath)
    $uri = "$EnvironmentUrl/api/data/v9.2/$RelativePath"
    $all = [System.Collections.Generic.List[object]]::new()
    while ($uri) {
        $resp = Invoke-RestMethod -Uri $uri -Headers (Get-DvHeaders) -Method Get
        if ($null -ne $resp.value) { $all.AddRange(@($resp.value)) } else { return , @($resp) }
        $uri = $resp.'@odata.nextLink'
    }
    return , $all.ToArray()
}

# Every rule in every state: the statecode column is what separates an updatable rule from an inactive one.
function Read-Rules {
    return Invoke-DvGet 'sprk_fieldmappingrules?$select=sprk_fieldmappingruleid,sprk_name,_sprk_fieldmappingprofile_value,sprk_targetfield,sprk_mapping_type,sprk_syncmode,statecode&$orderby=sprk_name asc'
}

# Profile id -> the logical name of the table its rules write to (profile -> target record type -> its logical name).
function Read-ProfileTargets {
    $types = @{}
    foreach ($t in (Invoke-DvGet 'sprk_recordtype_refs?$select=sprk_recordtype_refid,sprk_recordlogicalname')) { $types["$($t.sprk_recordtype_refid)"] = "$($t.sprk_recordlogicalname)".Trim().ToLowerInvariant() }
    $map = @{}
    foreach ($p in (Invoke-DvGet 'sprk_fieldmappingprofiles?$select=sprk_fieldmappingprofileid,_sprk_targetrecordtype_value')) {
        $key = "$($p._sprk_targetrecordtype_value)"
        if ($key -and $types.ContainsKey($key)) { $map["$($p.sprk_fieldmappingprofileid)"] = $types[$key] }
    }
    return $map
}

function Write-Plan {
    param([hashtable]$Plan)
    foreach ($i in $Plan.Items | Where-Object { $_.Decision.Outcome -in 'Migrate', 'Demote', 'InactiveNotMigrated', 'Unknown' }) {
        $r = $i.Row
        Write-Host ("  {0,-20} {1}  [{2}]  table={3}  target={4}  class={5}  current={6}  to={7}  {8}" -f $i.Decision.Outcome, $r.sprk_fieldmappingruleid, $r.sprk_name, $i.Entity, $r.sprk_targetfield, $i.Decision.Class, $i.Decision.Current, $i.Decision.Target, $i.Decision.Reason)
    }
    Write-Host ("Totals: {0} rule(s) read. ToMigrate (to Manual Refresh): {1}; ToDemote (to One-time): {2}; Aligned: {3}; InactiveNotMigrated: {4}; NotCopy (never touched): {5}; Unknown: {6}" -f `
            $Plan.Items.Count, $Plan.Counts.Migrate, $Plan.Counts.Demote, $Plan.Counts.Aligned, $Plan.Counts.InactiveNotMigrated, $Plan.Counts.NotCopy, $Plan.Counts.Unknown)
    Write-Host ("Copy rules by class: Assigned-To {0}, Filing {1}, Plain data {2}" -f $Plan.ByClass.AssignedTo, $Plan.ByClass.Filing, $Plan.ByClass.Plain)
}

function Read-Plan {
    return Get-MigrationPlan (Read-Rules) (Read-ProfileTargets)
}

Write-Host "Set-FieldMappingRuleSyncMode on $EnvironmentUrl ($(if ($IsDryRun) { 'DRY RUN - nothing is written' } else { 'APPLY' }))"
$plan = Read-Plan
Write-Plan $plan

# Escalation: an unrecognised value or an undecidable class is never guessed at, and the run stops before any write.
if ($plan.Counts.Unknown -gt 0) {
    Write-Host "STOP: $($plan.Counts.Unknown) rule(s) carry a mapping type or sync mode outside the known option values, or a target table that cannot be resolved. Nothing was written; a person must decide the mapping." -ForegroundColor Red
    exit 2
}

if (-not $IsDryRun) {
    $written = 0; $failed = 0
    foreach ($i in $plan.Items | Where-Object { $_.Decision.Outcome -in 'Migrate', 'Demote' }) {
        $id = $i.Row.sprk_fieldmappingruleid
        $value = [int]$i.Decision.Target
        try {
            $body = @{ sprk_syncmode = $value } | ConvertTo-Json -Compress
            Invoke-RestMethod -Uri "$EnvironmentUrl/api/data/v9.2/sprk_fieldmappingrules($id)" -Headers (Get-DvHeaders -ForWrite) -Method Patch -Body $body | Out-Null
            $reread = (Invoke-DvGet "sprk_fieldmappingrules($id)?`$select=sprk_syncmode")[0]
            if ($reread.sprk_syncmode -ne $value) { throw "read-back mismatch: sprk_syncmode=$($reread.sprk_syncmode), expected $value" }
            $written++
        } catch {
            $failed++
            Write-Host "  FAILED $id : $($_.Exception.Message)" -ForegroundColor Red
        }
    }
    Write-Host "Applied: $written changed, $failed failed."
    if ($failed -gt 0) { exit 1 }
}

if ($Verify) {
    $verifyPlan = if ($IsDryRun) { $plan } else { Read-Plan }
    $pass = Test-VerifyPass $verifyPlan
    Write-Host ("VERIFY: {0} (Copy rules short of their class value: {1} to migrate, {2} to demote, {3} inactive)" -f $(if ($pass) { 'PASS' } else { 'NOT MET' }), $verifyPlan.Counts.Migrate, $verifyPlan.Counts.Demote, $verifyPlan.Counts.InactiveNotMigrated)
    if (-not $pass) { exit 1 }
}
exit 0
