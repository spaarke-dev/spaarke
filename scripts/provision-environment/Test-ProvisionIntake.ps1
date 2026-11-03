<#
.SYNOPSIS
  /provision-environment Step 1 — validates intake values with the same rules POST /api/runs and the handlers use.

.DESCRIPTION
  -Step selects the sub-steps (in this order when 'all'):
    1a          customerId standard ^[a-z][a-z0-9]{2,7}\z (case-sensitive, \z), not reserved (platform|shared|byok)
    1a-bis      displayName 1-160 chars, no leading/trailing whitespace, no control char, never 'Secure Record'
    1b          tenantId is a GUID (never defaulted — I1)
    1b-bis      subscriptionId GUID; dataverseEnvUrl https://spaarke-{customerId}[-{name}].crm[N].dynamics.com/
    1b-ter      tier smb|enterprise|dedicated (exact case); estimatedMonthlyUsd plain non-negative number
    1b-quater   openAiMonthlyLimitUsd: empty, or plain decimal > 0 and <= 1000000
    1b-quinquies solutionPackageType: empty, managed or unmanaged (exact case)
    1b-sexies   secureRecordSetupDryRun: absent, true or false
    1e          profile/tenancyModel enum + 1:1 pairing (retired-profile stop first) — also covers 1c
    1e-bis      identityPreset, users, B2BGuest checks (PRQ-C-14 attestation, PRQ-C-10 group + group SET ON the
                environment, PRQ-C-12 guest access, PRQ-C-11 PAYG billing — live, read-only, as the operator),
                exchangePolicyScopeGroupId, customerWorkforceTenantIds, communicationDefaultMailbox

  Exit codes:
    0  all selected sub-steps pass
    1  HARD STOP (the original procedure's `exit 1`) — in batch mode every failure is a HARD STOP
    7  interactive only: a value the original prompted for again is missing/invalid. The script prints
       FIELD=<name>; the skill asks the operator for that value, updates the intake file and reruns.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$SessionFile,
  [string[]]$Step = @('all')
)

Import-Module (Join-Path $PSScriptRoot 'ProvisionEnvironment.psm1') -Force
$s = Read-ProvisionSession -SessionFile $SessionFile
$isBatch = $s.mode -eq 'batch'
$i = Get-ProvisionIntake -Session $s

$order = @('1a', '1a-bis', '1b', '1b-bis', '1b-ter', '1b-quater', '1b-quinquies', '1b-sexies', '1e', '1e-bis')
$selected = if ($Step -contains 'all') { $order } else { $order | Where-Object { $Step -contains $_ } }
if (@($selected).Count -eq 0) { Stop-Provision "[skill] Unknown -Step '$($Step -join ',')'. Valid: all, $($order -join ', ')." }

function Hard([string]$msg) { Stop-Provision $msg 1 }
function Ask([string]$field, [string]$msg) {
  # Interactive: the original re-prompted. Batch: Stop-IfBatch — a schema-valid intake should never reach this.
  if ($isBatch) { Stop-Provision "[skill] Batch HARD STOP: $msg Fix the intake file." 1 }
  Write-Host $msg -ForegroundColor Yellow
  Write-Host "FIELD=$field"
  exit 7
}

$customerId = [string]$i['customerId']

foreach ($st in $selected) {
  switch ($st) {
    '1a' {
      # T237 (owner D10): reject, never repair — no trimming, lower-casing or auto-abbreviation.
      $reservedIds = @('platform', 'shared', 'byok')
      if (($customerId -cnotmatch '^[a-z][a-z0-9]{2,7}\z') -or ($customerId -cin $reservedIds)) {
        $why = if ($customerId -cin $reservedIds) { 'is reserved (names a non-customer resource group)' } else { "does not match the customerId standard ^[a-z][a-z0-9]{2,7}`$ (3-8 lowercase letters/digits, starts with a letter; no hyphens)" }
        if ($isBatch) { Hard "[skill] Batch HARD STOP: customerId '$customerId' $why. Fix the intake file (the schema should have caught this)." }
        Ask 'customerId' "customerId '$customerId' $why. Abbreviate a longer customer name (northwind -> nwind); the full name is captured next as displayName."
      }
    }
    '1a-bis' {
      $d = [string]$i['displayName']
      if ($d.Length -gt 160 -or $d -ne $d.Trim() -or $d -match '\p{Cc}' -or $d -ieq 'Secure Record') {
        Ask 'displayName' "displayName must be 1-160 characters, no leading/trailing space or control character, not 'Secure Record' [$customerId]"
      }
    }
    '1b' {
      # Never default, never `az account show` (I1 / FR-28). Schema format: uuid enforces this in batch mode.
      if (-not ([string]$i['tenantId'] -as [guid])) { Ask 'tenantId' "tenantId '$($i['tenantId'])' must be the customer's Entra tenant id (GUID) — supplied explicitly, never defaulted (I1)." }
    }
    '1b-bis' {
      if (-not ([string]$i['subscriptionId'] -as [guid])) { Hard "❌ subscriptionId must be the GUID of the customer's own subscription (PRQ-S-00)." }
      if ([string]$i['dataverseEnvUrl'] -notmatch "^https://spaarke-$customerId(-[a-z]+)?\.crm[0-9]*\.dynamics\.com/?$") {
        Hard "❌ dataverseEnvUrl must be https://spaarke-$customerId[-{environmentName}].crm[N].dynamics.com/ — the environment the operator created (PRQ-C-09)."
      }
    }
    '1b-ter' {
      if ([string]$i['tier'] -cnotin @('smb', 'enterprise', 'dedicated')) { Hard '❌ tier must be smb | enterprise | dedicated (exact case; T229).' }
      if ("$($i['estimatedMonthlyUsd'])" -notmatch '^[0-9]+(\.[0-9]+)?$') { Hard '❌ estimatedMonthlyUsd must be a plain non-negative number of USD (e.g. 450).' }
    }
    '1b-quater' {
      $lim = "$($i['openAiMonthlyLimitUsd'])"
      if (-not [string]::IsNullOrWhiteSpace($lim)) {
        # Same rule as L2's OpenAiMonthlyLimitRule: plain decimal, > 0, <= 1000000. Omit for no limit — never 0.
        $parsedLimit = [decimal]0
        if ($lim -notmatch '^[0-9]+(\.[0-9]+)?$' -or
          -not [decimal]::TryParse($lim, [System.Globalization.NumberStyles]::AllowDecimalPoint, [cultureinfo]::InvariantCulture, [ref]$parsedLimit) -or
          $parsedLimit -le 0 -or $parsedLimit -gt 1000000) {
          Hard '❌ openAiMonthlyLimitUsd must be a plain number of USD greater than 0 and at most 1000000 (e.g. 500), or empty for no limit.'
        }
      }
    }
    '1b-quinquies' {
      $pt = "$($i['solutionPackageType'])"
      if (-not [string]::IsNullOrWhiteSpace($pt) -and $pt -cnotin @('managed', 'unmanaged')) { Hard '❌ solutionPackageType must be managed or unmanaged (exact case), or omitted for managed.' }
    }
    '1b-sexies' {
      $dr = $i['secureRecordSetupDryRun']
      if ($null -ne $dr -and $dr -isnot [bool]) { Hard '❌ secureRecordSetupDryRun must be true or false, or omitted.' }
    }
    '1e' {
      $prof = [string]$i['profile']; $tenancyModel = [string]$i['tenancyModel']
      # The retired-profile stop runs FIRST so its specific message wins over the generic enum check.
      if ($prof -eq 'spaarke-hosted-model1-trial') {
        Hard "❌ Profile 'spaarke-hosted-model1-trial' is RETIRED (owner decision D-12, 2026-09-28). The shared trial/SMB tier no longer exists and was never implemented in the engine. Use 'spaarke-hosted-model2' (pairs with Model1, Spaarke's Azure tenant) or 'customer-owned-model2' (pairs with Model2, customer's Azure tenant)."
      }
      $validProfiles = @('spaarke-hosted-model2', 'customer-owned-model2')
      if ($prof -notin $validProfiles) { Hard "❌ Invalid profile '$prof'. Must be one of: $($validProfiles -join ', '). Per DS-5 c6-1: L2 API rejects any other value with 400." }
      if ($tenancyModel -cnotin @('Model1', 'Model2')) { Hard "❌ tenancyModel '$tenancyModel' must be exactly 'Model1' or 'Model2' (case-sensitive — L2 returns 400 otherwise)." }
      $requiredProfile = @{ 'Model1' = 'spaarke-hosted-model2'; 'Model2' = 'customer-owned-model2' }[$tenancyModel]
      if ($prof -ne $requiredProfile) { Hard "❌ tenancyModel '$tenancyModel' pairs only with profile '$requiredProfile' (received '$prof'). L2 returns 400 tenancy-profile-invalid." }
    }
    '1e-bis' {
      $tenancyModel = [string]$i['tenancyModel']; $identityPreset = [string]$i['identityPreset']
      # T232 (owner D2): Model 1 customer users are B2B guests in Spaarke's tenant — the preset is not a choice there.
      if ($tenancyModel -ceq 'Model1' -and $identityPreset -cne 'B2BGuest') { Ask 'identityPreset' "identityPreset '$identityPreset' — a Model1 run takes only B2BGuest (owner D2). Set identityPreset to B2BGuest." }
      if ($identityPreset -cnotin @('B2BGuest', 'NativeAccount')) { Ask 'identityPreset' "identityPreset '$identityPreset' must be B2BGuest or NativeAccount (exact case)." }
      $isGuest = $identityPreset -ceq 'B2BGuest'

      $users = @($i['users'])
      if ($users.Count -eq 0) { Ask 'users' 'users needs at least one entry.' }
      if ($users.Count -gt 500) { Ask 'users' "users has $($users.Count) entries — at most 500 per run." }
      $position = 0
      foreach ($u in $users) {
        $position++
        $bad = if ($isGuest) { [string]::IsNullOrWhiteSpace([string]$u['email']) } else { [string]::IsNullOrWhiteSpace([string]$u['firstName']) -or [string]::IsNullOrWhiteSpace([string]$u['lastName']) }
        if ($bad) {
          $msg = "[skill] HARD STOP: users entry $position needs $(if ($isGuest) { 'an email' } else { 'firstName + lastName' })."
          if ($isBatch) { Hard $msg } else { Ask 'users' $msg }
        }
      }
      # T232: a guest address listed twice would be invited twice (POST /api/runs refuses it too — userprov-invalid-user-entry).
      if ($isGuest) {
        $dupes = @($users | Group-Object { "$($_['email'])".Trim().ToLowerInvariant() } | Where-Object Count -gt 1)
        if ($dupes.Count -gt 0) {
          $msg = "[skill] HARD STOP: $($dupes.Count) guest email(s) appear more than once in users — list each guest once."
          if ($isBatch) { Hard $msg } else { Ask 'users' $msg }
        }
      }

      if ($isGuest) {
        # PRQ-C-14: the CUSTOMER's Entra admin confirmed outbound B2B collaboration to Spaarke's tenant.
        if (-not $i['customerOutboundB2BAttested']) {
          if ($isBatch) { Hard "[skill] Batch HARD STOP: customerOutboundB2BAttested is false/absent: the CUSTOMER's Entra admin must confirm that its cross-tenant access settings allow outbound B2B collaboration to Spaarke's tenant (PRQ-C-14). Fix the intake file." }
          if (-not $i['_b2bAttestationAnswered']) {
            Ask 'customerOutboundB2BAttested' "Has the customer's Entra admin confirmed in writing that outbound B2B collaboration to Spaarke's tenant is allowed? (PRQ-C-14) [y/N] — record true or false."
          }
          Write-Warning 'customerOutboundB2BAttested=false (operator answered N) — the PRQ-C-14 recipe in the Step 1e-ter pass will report it.'
        }
        $groupGuid = [guid]::Empty
        if (-not [guid]::TryParseExact([string]$i['environmentSecurityGroupId'], 'D', [ref]$groupGuid) -or $groupGuid -eq [guid]::Empty) {
          Ask 'environmentSecurityGroupId' "environmentSecurityGroupId is required for B2BGuest — the object id (GUID) of sprk-$customerId-users (PRQ-C-10)."
        }
        $environmentSecurityGroupId = $groupGuid.ToString('D')

        # T232 — checked now as the operator. H11 re-checks the group's NAME and guest access server-side, but only after
        # H0–H10 have built the stamp — and only the operator (a Power Platform admin) can see which group is SET ON the
        # environment, or its billing. A failed az/pac call is a stop with its own error, never an empty value read as an answer.

        # PRQ-C-10 (a): the group is this customer's security group.
        $groupJson = az rest --method get --url "https://graph.microsoft.com/v1.0/groups/$environmentSecurityGroupId`?`$select=displayName,securityEnabled" -o json
        if ($LASTEXITCODE -ne 0) { Hard "[skill] HARD STOP (PRQ-C-10): reading group $environmentSecurityGroupId failed (az output above). Is the operator signed in to the stamp's tenant?" }
        $group = $groupJson | ConvertFrom-Json
        if ($group.displayName -ne "sprk-$customerId-users" -or -not $group.securityEnabled) {
          Hard "[skill] HARD STOP (PRQ-C-10): group $environmentSecurityGroupId is '$($group.displayName)' (securityEnabled=$($group.securityEnabled)) — it must be the security group sprk-$customerId-users. H11 would refuse it (userprov-security-group-rejected)."
        }

        # PRQ-C-10 (b): it is the group SET ON the environment — the isolation boundary between Model 1 environments.
        # Tokens from az, requests via Invoke-RestMethod: on Windows `az` is az.cmd, and cmd breaks on the `)` in
        # RetrieveCurrentOrganization(...) ("?@p was unexpected at this time", 2026-10-07).
        $dvRes = ([string]$i['dataverseEnvUrl']).TrimEnd('/')
        $dvToken = az account get-access-token --resource $dvRes --query accessToken -o tsv
        $ppToken = az account get-access-token --resource 'https://service.powerapps.com/' --query accessToken -o tsv
        if ($LASTEXITCODE -ne 0 -or -not $dvToken -or -not $ppToken) { Hard '[skill] HARD STOP (PRQ-C-10): az could not get a Dataverse / Power Platform token (az output above).' }
        try {
          $org = Invoke-RestMethod -Headers @{ Authorization = "Bearer $dvToken" } `
            -Uri "$dvRes/api/data/v9.2/RetrieveCurrentOrganization(AccessType=@p)?@p=Microsoft.Dynamics.CRM.EndpointAccessType'Default'"
          $ppEnvId = "$($org.Detail.EnvironmentId)"
          if (-not $ppEnvId) { throw 'RetrieveCurrentOrganization returned no Detail.EnvironmentId' }
          $bap = Invoke-RestMethod -Headers @{ Authorization = "Bearer $ppToken" } `
            -Uri "https://api.bap.microsoft.com/providers/Microsoft.BusinessAppPlatform/scopes/admin/environments/$ppEnvId`?api-version=2021-04-01"
        } catch {
          Hard "[skill] HARD STOP (PRQ-C-10): reading the environment's security group failed — $($_.Exception.Message). The operator must be a Power Platform admin."
        }
        $boundGroup = "$($bap.properties.linkedEnvironmentMetadata.securityGroupId)"
        if ($boundGroup -ne $environmentSecurityGroupId) {
          Hard "[skill] HARD STOP (PRQ-C-10): environment $ppEnvId ($dvRes) has security group '$boundGroup' — it must be $environmentSecurityGroupId (sprk-$customerId-users). Without it every user of the tenant, other customers' guests included, is admitted. Set it (admin center → Environments → Edit → Security group), then rerun."
        }

        # PRQ-C-12: guests may use the environment.
        $restricted = az rest --method get --resource $dvRes --url "$dvRes/api/data/v9.2/organizations?`$select=restrictGuestUserAccess" --query "value[0].restrictGuestUserAccess" -o tsv
        if ($LASTEXITCODE -ne 0) { Hard '[skill] HARD STOP (PRQ-C-12): reading restrictGuestUserAccess failed (az output above).' }
        if ($restricted -ne 'false') {
          Hard "[skill] HARD STOP (PRQ-C-12): the environment restricts guest access (restrictGuestUserAccess=$restricted). Turn it off per prereqs.yaml PRQ-C-12, then rerun. H11 would refuse it (userprov-guest-access-restricted)."
        }

        # PRQ-C-11: billed pay-as-you-go on the STAMP subscription — an Enabled billing policy whose billingInstrument is
        # the stamp subscription has this environment linked. Power Platform API licensing/billingPolicies, api-version
        # 2024-10-01 (GA; verified live at T186 2026-10-14). Not `pac licensing`: preview, absent from pac 1.46.1 (T186 L12).
        $stampSub = ([guid][string]$i['subscriptionId']).ToString('D')
        $plToken = az account get-access-token --resource 'https://api.powerplatform.com' --query accessToken -o tsv
        if ($LASTEXITCODE -ne 0 -or -not $plToken) { Hard '[skill] HARD STOP (PRQ-C-11): az could not get a Power Platform API token (az output above).' }
        $plHeaders = @{ Authorization = "Bearer $plToken" }
        try {
          $policies = @((Invoke-RestMethod -Headers $plHeaders -Uri 'https://api.powerplatform.com/licensing/billingPolicies?api-version=2024-10-01').value |
            Where-Object { $_.status -ceq 'Enabled' -and "$($_.billingInstrument.subscriptionId)" -eq $stampSub })
        } catch {
          Hard "[skill] HARD STOP (PRQ-C-11): listing billing policies failed — $($_.Exception.Message). The operator must be a Power Platform admin."
        }
        $linkedPolicy = $null
        foreach ($p in $policies) {
          try {
            Invoke-RestMethod -Headers $plHeaders -Uri "https://api.powerplatform.com/licensing/billingPolicies/$($p.id)/environments/$ppEnvId`?api-version=2024-10-01" | Out-Null
            $linkedPolicy = $p; break
          } catch {
            if ($_.Exception.Response.StatusCode.value__ -ne 404) { Hard "[skill] HARD STOP (PRQ-C-11): reading policy $($p.name) failed — $($_.Exception.Message)" }
          }
        }
        if (-not $linkedPolicy) {
          Hard "[skill] HARD STOP (PRQ-C-11): environment $ppEnvId ($dvRes) is linked to no Enabled pay-as-you-go billing policy on the stamp subscription $stampSub ($($policies.Count) Enabled on it). Link it per prereqs.yaml PRQ-C-11, then rerun."
        }
        Write-Host "PRQ-C-11 OK — billing policy '$($linkedPolicy.name)' (Enabled, subscription $stampSub) has the environment linked." -ForegroundColor Green
      }

      if ([string]::IsNullOrWhiteSpace([string]$i['exchangePolicyScopeGroupId'])) {
        Ask 'exchangePolicyScopeGroupId' 'exchangePolicyScopeGroupId is required — the Exchange admin creates the group before the run (PRQ-C-08).'
      }

      # T255 (INCOMING-141): the CUSTOMER's workforce tenant id(s) — required for every model. Same shape rules as
      # POST /api/runs (CustomerWorkforceTenantsRule); L2 also refuses the CIAM tenant and Spaarke's own tenant.
      $wfIds = @($i['customerWorkforceTenantIds'] | ForEach-Object { [string]$_ } | Where-Object { $_ -and $_.Trim() })
      $parsed = @(); $bad = $null
      foreach ($v in $wfIds) { $g = [guid]::Empty; if (-not [guid]::TryParse($v.Trim(), [ref]$g) -or $g -eq [guid]::Empty) { $bad = "'$v' is not a tenant id"; break }; $parsed += $g.ToString('D') }
      if (-not $bad -and $parsed.Count -eq 0) { $bad = 'at least one is required' }
      if (-not $bad -and $parsed.Count -gt 10) { $bad = 'at most 10' }
      if (-not $bad -and @($parsed | Select-Object -Unique).Count -ne $parsed.Count) { $bad = 'a tenant is listed twice' }
      if (-not $bad -and $tenancyModel -eq 'Model1' -and $parsed -contains ([guid][string]$i['tenantId']).ToString('D')) { $bad = "$($i['tenantId']) is this Model 1 run's tenantId (Spaarke's tenant) — give the CUSTOMER's home tenant" }
      if ($bad) { Ask 'customerWorkforceTenantIds' "customerWorkforceTenantIds: $bad (PRQ-C-13)." }

      $mb = [string]$i['communicationDefaultMailbox']
      if ($mb -cnotmatch '^[^@\s]+@[^@\s]+\.[^@\s]+\z' -or $mb.Length -gt 254) {
        Ask 'communicationDefaultMailbox' "communicationDefaultMailbox '$mb' must be a mailbox address (local@domain.tld, at most 254 characters)."
      }
    }
  }
  Write-Host "  [PASS] Step $st" -ForegroundColor Green
}
exit 0
