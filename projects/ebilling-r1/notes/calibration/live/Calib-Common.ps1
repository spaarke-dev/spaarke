# Shared helpers for the 161 live scripts (dot-source). Nothing here runs on load.
#
# OWNER GATE (project CLAUDE.md section 5, task 161 rules): every script that talks to dev is written but NOT run by the agent that
# authored it. Each one (a) prints its plan and does nothing under -WhatIf, and (b) refuses to touch dev unless -OwnerApproval carries the
# owner's recorded OK (free text: who, when, which message). The approval is echoed into the output manifest so the run can be audited.

Set-StrictMode -Version Latest

function Assert-OwnerApproval {
    param([string]$OwnerApproval)
    if ([string]::IsNullOrWhiteSpace($OwnerApproval)) {
        throw "Refusing to run against dev: pass -OwnerApproval '<who approved, when, where recorded>' (task 161: seeding, submitting and scoring against dev need the owner's OK). Use -WhatIf to see the plan."
    }
}

function Get-DevToken {
    param([Parameter(Mandatory)][string]$Resource)
    # Entra token from the signed-in az session; no key or secret is read (FR-47 style, as 005's harness).
    $t = & az account get-access-token --resource $Resource --query accessToken -o tsv
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($t)) { throw "az account get-access-token failed for $Resource" }
    return $t.Trim()
}

function Invoke-Dv {
    param([string]$Method = 'GET', [Parameter(Mandatory)][string]$Url, [string]$Token, $Body, [hashtable]$Headers = @{})
    $h = @{ Authorization = "Bearer $Token"; 'OData-Version' = '4.0'; Accept = 'application/json' } + $Headers
    if ($null -ne $Body) { return Invoke-RestMethod -Method $Method -Uri $Url -Headers $h -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 8) }
    return Invoke-RestMethod -Method $Method -Uri $Url -Headers $h
}

function Get-ColumnSet {
    # Preflight: which of the wanted columns exist on the table, and which required columns we would leave empty.
    param([string]$DataverseUrl, [string]$Token, [string]$LogicalName, [string[]]$Wanted)
    $r = Invoke-Dv -Url "$DataverseUrl/api/data/v9.2/EntityDefinitions(LogicalName='$LogicalName')/Attributes?`$select=LogicalName,RequiredLevel,AttributeType" -Token $Token
    $all = @{}
    foreach ($a in $r.value) { $all[$a.LogicalName] = $a }
    $present = @($Wanted | Where-Object { $all.ContainsKey($_) })
    $missing = @($Wanted | Where-Object { -not $all.ContainsKey($_) })
    $required = @($all.Values | Where-Object { $_.RequiredLevel.Value -in 'SystemRequired', 'ApplicationRequired' -and $_.LogicalName -notin $Wanted -and $_.AttributeType -notin 'Uniqueidentifier', 'Virtual', 'EntityName' } | ForEach-Object LogicalName)
    return [pscustomobject]@{ Present = $present; Missing = $missing; RequiredNotWritten = $required }
}

function Read-ReferenceData {
    param([Parameter(Mandatory)][string]$Path)
    $sets = @()
    foreach ($f in (Get-ChildItem -Path $Path -Filter 'RS-*.json' | Sort-Object Name)) {
        $sets += (Get-Content -Raw -Encoding utf8 $f.FullName | ConvertFrom-Json)
    }
    if ($sets.Count -eq 0) { throw "No RS-*.json under $Path" }
    # BINDING (guidelines README): one firm record per reference-data KEY, never one per name. Six names occur in two rule sets with
    # different caps and rosters; sharing a record would merge their rosters and rate cards.
    $seen = @{}
    foreach ($s in $sets) {
        foreach ($firm in $s.firms) {
            if ($seen.ContainsKey($firm.key)) { throw "Duplicate firm key '$($firm.key)' ($($s.rule_set_id) and $($seen[$firm.key])): one firm record per key." }
            $seen[$firm.key] = $s.rule_set_id
        }
    }
    return $sets
}

