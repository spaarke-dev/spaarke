<#
.SYNOPSIS
    READ-ONLY rollout census for ebilling-r1 082c (brief C20; ontology D-205 item 2): how many legacy billing rule sets an environment has, how
    often a firm's legacy set and a client-default legacy set share a checklist rule type, and the K5 record-rule count.

.DESCRIPTION
    082c applies BillingRuleCatalogue.PrecedenceOf to legacy rule sets too (D-205 item 2, the owner's D-172 Q7): limits replace, checklists add.
    Before 082c a firm's legacy rule replaced the client default's of the same type for EVERY type (D-39). So wherever a firm's Published legacy
    set and a Published client-default legacy set both hold an in-force rule of a checklist type, both now apply on that firm's invoices: the
    client default's items are checked as well (stricter; it may raise new RequiredData rejections). This script counts those overlaps.

    Legacy set: sprk_ruleset with sprk_domain Billing and NO sprk_procedure (the partition of BillingFactLoader.LoadLegacyRulesAsync). Only
    Published sets (statuscode 659490000) are judged; a policy counts when it is enabled and active; a version counts when it can be in force
    (it is the policy's current version, or it has an in-force-to date: BillingRuleCandidate.CanBeInForce). The precedence key of these types is
    the rule type alone (BillingRuleDefinition.PrecedenceKey adds a fee kind for AddedFee only, which replaces).

    K5 (brief C20): record rules (policies in a record-scoped set, sprk_scope Record 100000002 or Firm record 100000003) whose Published version
    has no sprk_basedonversion. The resolver faults on such a row when its record rule is in force; backfill or retire them before 082c ships.

    Linked projects with an empty pair (brief 082c (b), ontology D-205 item 4; 082c-2): projects linked to a matter through the Project to
    Matter N:N (intersect sprk_project_sprk_matter) whose BFF-managed pair (sprk_regardingrecordid, sprk_regardingrecordtype) is empty. The
    resolver walks the pair, not the N:N, so such a project gives its invoices no matter Procedures and no fault. Above 0 is a rollout gate:
    backfill the pair with the owner's go. Re-runnable; the same count is the access reconciliation pass's ProjectsWithMatterLinkButNoPair
    (task 195, every run). The create path is pinned by CreateProjectWizard.projectFiling.test.tsx ("writes the N:N, THEN files the project
    under the matter through the BFF"). It can still grow: a refused or secure-blocked filing keeps the link with a visible warning, and a
    link added outside the wizard (a subgrid "Add existing") writes no pair. Run it before each rollout, not once.

    Output is counts only: no record id, name, rule body or code. It makes GET requests only (one function, Invoke-DvGet); it writes nothing.

.PARAMETER DataverseUrl
    Target environment, e.g. https://spaarkedev1.crm.dynamics.com. Defaults to $env:DATAVERSE_URL. Required.

.PARAMETER AsJson
    Print the census as one JSON object instead of a table.

.EXAMPLE
    pwsh scripts/ebilling/Get-EbillingLegacyRuleOverlap.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com
#>
#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'

if (-not $DataverseUrl) { throw 'DataverseUrl is required (parameter or $env:DATAVERSE_URL).' }
$DataverseUrl = $DataverseUrl.TrimEnd('/')
$ApiBase = "$DataverseUrl/api/data/v9.2"

# ---- The fixed values (each mirrors a BFF constant) --------------------------------------------------------------------------------
$DomainBilling = 100000000          # RuleDomains.PolicyDomainBilling
$RuleSetPublished = 659490000       # BillingFactLoader.RuleSetPublished
$ScopeClientDefault = 100000000     # RuleSetScope.ClientDefault
$ScopeFirm = 100000001              # RuleSetScope.Firm
$ScopeRecord = 100000002            # RecordRuleSet.ScopeRecord
$ScopeFirmRecord = 100000003        # RecordRuleSet.ScopeFirmRecord
$PublishStatePublished = 100000001  # RuleSetService.PublishStatePublished

# The checklist types: every rule type BillingRuleCatalogue.PrecedenceOf classes Additive, plus ExpenseRule (Split: its non-reimbursable codes
# add, its cap replaces), less SubmissionIntegrity (a system type, never judged from a body). PINNED here, not derived: keep it in step with
# BillingRuleCatalogue.PrecedenceOf (BillingRulePrecedenceTests checks this line). AiObservation is Additive in the catalogue although D-205's
# list does not name it; it is counted, and reported on its own line.
$ChecklistRuleTypes = @{ 100000003 = 'ApprovedTimekeeper'; 100000007 = 'NonBillableActivity'; 100000008 = 'ExpenseRule'; 100000012 = 'RequiredData'; 100000013 = 'AiObservation' }

# ---- Dataverse access: GET only ----------------------------------------------------------------------------------------------------
$token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if (-not $token) { throw "Could not get a token for $DataverseUrl (az login?)." }
$headers = @{
    Authorization      = "Bearer $token"
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    Prefer             = 'odata.maxpagesize=5000'
}

function Invoke-DvGet([string]$Path) {
    $rows = [System.Collections.Generic.List[object]]::new()
    $uri = "$ApiBase/$Path"
    while ($uri) {
        try { $page = Invoke-RestMethod -Method Get -Uri $uri -Headers $headers }
        catch {
            $detail = if ($_.ErrorDetails.Message) { $_.ErrorDetails.Message } else { $_.Exception.Message }
            throw "GET $uri failed: $detail"
        }
        foreach ($row in $page.value) { $rows.Add($row) }
        $uri = $page.'@odata.nextLink'
    }
    return , $rows
}

# ---- Read ---------------------------------------------------------------------------------------------------------------------------
$sets = Invoke-DvGet "sprk_rulesets?`$select=sprk_rulesetid,sprk_scope,_sprk_firm_value,statuscode,_sprk_procedure_value&`$filter=sprk_domain eq $DomainBilling"
$policies = Invoke-DvGet "sprk_policies?`$select=sprk_policyid,_sprk_ruleset_value,_sprk_currentversion_value,sprk_enabled,statecode&`$filter=sprk_domain eq $DomainBilling and _sprk_ruleset_value ne null"
$versions = Invoke-DvGet "sprk_policyversions?`$select=sprk_policyversionid,_sprk_policy_value,sprk_ruletype,sprk_inforceto,sprk_publishstate,_sprk_basedonversion_value&`$filter=_sprk_policy_value ne null"
$matterLinks = Invoke-DvGet "sprk_project_sprk_matterset?`$select=sprk_projectid,sprk_matterid"
$projects = Invoke-DvGet "sprk_projects?`$select=sprk_projectid,sprk_regardingrecordid,_sprk_regardingrecordtype_value"

# ---- Legacy sets ------------------------------------------------------------------------------------------------------------------
$legacy = @($sets | Where-Object { -not $_._sprk_procedure_value -and $_.sprk_scope -in @($ScopeClientDefault, $ScopeFirm) })
$publishedLegacy = @($legacy | Where-Object { $_.statuscode -eq $RuleSetPublished })
$setById = @{}
foreach ($s in $publishedLegacy) { $setById[$s.sprk_rulesetid] = $s }

# The checklist types each Published legacy set holds in force.
$policyById = @{}
foreach ($p in $policies) {
    if ($p.sprk_enabled -eq $true -and $p.statecode -eq 0 -and $setById.ContainsKey($p._sprk_ruleset_value)) { $policyById[$p.sprk_policyid] = $p }
}

$typesBySet = @{}
foreach ($v in $versions) {
    $p = $policyById[$v._sprk_policy_value]
    if ($null -eq $p) { continue }
    $canBeInForce = ($p._sprk_currentversion_value -eq $v.sprk_policyversionid) -or ($null -ne $v.sprk_inforceto)
    if (-not $canBeInForce -or -not $ChecklistRuleTypes.ContainsKey([int]$v.sprk_ruletype)) { continue }
    $setId = $p._sprk_ruleset_value
    if (-not $typesBySet.ContainsKey($setId)) { $typesBySet[$setId] = [System.Collections.Generic.HashSet[int]]::new() }
    [void]$typesBySet[$setId].Add([int]$v.sprk_ruletype)
}

$defaultTypes = [System.Collections.Generic.HashSet[int]]::new()
foreach ($s in @($publishedLegacy | Where-Object { $_.sprk_scope -eq $ScopeClientDefault })) {
    if ($typesBySet.ContainsKey($s.sprk_rulesetid)) { $defaultTypes.UnionWith($typesBySet[$s.sprk_rulesetid]) }
}

$firmTypes = @{}
foreach ($s in @($publishedLegacy | Where-Object { $_.sprk_scope -eq $ScopeFirm -and $_._sprk_firm_value })) {
    $firm = $s._sprk_firm_value
    if (-not $firmTypes.ContainsKey($firm)) { $firmTypes[$firm] = [System.Collections.Generic.HashSet[int]]::new() }
    if ($typesBySet.ContainsKey($s.sprk_rulesetid)) { $firmTypes[$firm].UnionWith($typesBySet[$s.sprk_rulesetid]) }
}

$overlapPairs = 0
$firmsAffected = 0
$byType = [ordered]@{}
foreach ($key in @($ChecklistRuleTypes.Keys | Sort-Object)) { $byType[$ChecklistRuleTypes[$key]] = 0 }
foreach ($firm in $firmTypes.Keys) {
    $shared = @($firmTypes[$firm] | Where-Object { $defaultTypes.Contains($_) })
    if ($shared.Count -gt 0) { $firmsAffected++ }
    foreach ($type in $shared) { $overlapPairs++; $byType[$ChecklistRuleTypes[$type]]++ }
}

# ---- K5 ---------------------------------------------------------------------------------------------------------------------------
$recordSetIds = [System.Collections.Generic.HashSet[string]]::new()
foreach ($s in @($sets | Where-Object { $_.sprk_scope -in @($ScopeRecord, $ScopeFirmRecord) })) { [void]$recordSetIds.Add($s.sprk_rulesetid) }
$recordPolicyIds = [System.Collections.Generic.HashSet[string]]::new()
foreach ($p in @($policies | Where-Object { $recordSetIds.Contains($_._sprk_ruleset_value) })) { [void]$recordPolicyIds.Add($p.sprk_policyid) }
$recordPublished = @($versions | Where-Object { $recordPolicyIds.Contains($_._sprk_policy_value) -and $_.sprk_publishstate -eq $PublishStatePublished })
$k5 = @($recordPublished | Where-Object { -not $_._sprk_basedonversion_value })
$k5Rules = @($k5 | Select-Object -ExpandProperty _sprk_policy_value -Unique)

# ---- 082c (b): linked projects with an empty pair --------------------------------------------------------------------------------
$linkedProjectIds = [System.Collections.Generic.HashSet[string]]::new()
foreach ($l in $matterLinks) { [void]$linkedProjectIds.Add($l.sprk_projectid) }
$linkedEmptyPair = @($projects | Where-Object {
        $linkedProjectIds.Contains($_.sprk_projectid) -and ([string]::IsNullOrWhiteSpace($_.sprk_regardingrecordid) -or -not $_._sprk_regardingrecordtype_value)
    })

# ---- Report -----------------------------------------------------------------------------------------------------------------------
$census = [ordered]@{
    environment                       = $DataverseUrl
    billingRuleSets                   = $sets.Count
    procedureRuleSets                 = @($sets | Where-Object { $_._sprk_procedure_value }).Count
    legacyRuleSets                    = $legacy.Count
    legacyPublishedClientDefaultSets  = @($publishedLegacy | Where-Object { $_.sprk_scope -eq $ScopeClientDefault }).Count
    legacyPublishedFirmSets           = @($publishedLegacy | Where-Object { $_.sprk_scope -eq $ScopeFirm }).Count
    firmsWithAPublishedLegacyFirmSet  = $firmTypes.Count
    clientDefaultChecklistTypes       = @($defaultTypes | ForEach-Object { $ChecklistRuleTypes[$_] })
    checklistOverlapFirmTypePairs     = $overlapPairs
    checklistOverlapFirms             = $firmsAffected
    checklistOverlapByType            = $byType
    recordRuleSets                    = $recordSetIds.Count
    recordRules                       = $recordPolicyIds.Count
    recordRulePublishedVersions       = $recordPublished.Count
    k5PublishedVersionsWithoutBasedOn = $k5.Count
    k5RecordRulesAffected             = $k5Rules.Count
    projectsLinkedToAMatter           = $linkedProjectIds.Count
    linkedProjectsWithAnEmptyPair     = $linkedEmptyPair.Count
}

if ($AsJson) { $census | ConvertTo-Json -Depth 4 }
else { [pscustomobject]$census | Format-List | Out-String -Width 200 | Write-Output }
