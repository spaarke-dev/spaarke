<#
.SYNOPSIS
  Tests for task-132-live-gate.ps1's original-owner bookkeeping (owner round 48 (d)): ReassignMatter -Apply never
  overwrites a recorded OriginalOwner, so RestoreMatter always puts the REAL owner of the test matter back.

.DESCRIPTION
  No live call is made: az and Invoke-RestMethod are mocked; a fake matter owner is changed only by the mocked PATCH; the
  state file lives in Pester's TestDrive. Run from the repository root:

    Invoke-Pester projects/unified-access-control-r2/notes/task-132-live-gate.Tests.ps1 -Output Detailed
#>

BeforeAll {
  $script:Gate = Join-Path $PSScriptRoot 'task-132-live-gate.ps1'
  $script:Matter = [Guid]'13200000-0000-0000-0000-00000000a001'
  $script:OtherMatter = [Guid]'13200000-0000-0000-0000-00000000a002'
  $script:RealOwner = [Guid]'13200000-0000-0000-0000-00000000b001'
  $script:TeamA = [Guid]'13200000-0000-0000-0000-00000000c001'
  $script:TeamB = [Guid]'13200000-0000-0000-0000-00000000c002'
  $script:TestUser = [Guid]'13200000-0000-0000-0000-00000000d001'

  function Invoke-Gate([string]$Step, [hashtable]$Arguments) {
    & $script:Gate -Step $Step -StatePath $script:StatePath @Arguments | Out-String
  }

  function Get-Recorded([Guid]$MatterId) {
    (Get-Content $script:StatePath -Raw | ConvertFrom-Json -AsHashtable).Matters["$MatterId"]
  }
}

AfterAll {
  Remove-Variable -Name Gate132Fake -Scope Global -ErrorAction SilentlyContinue
}

Describe 'task-132-live-gate.ps1 - the original owner of a reassigned test matter' {
  BeforeEach {
    $script:StatePath = Join-Path $TestDrive "state-$([Guid]::NewGuid()).json"
    # Each matter's current owner, as Dataverse would report it; only the mocked PATCH changes it. Global, because a mock's
    # body does not run in this file's script scope.
    $global:Gate132Fake = @{ Owners = $null; Patches = [System.Collections.Generic.List[string]]::new() }
    $global:Gate132Fake.Owners = @{
      "$script:Matter"      = @{ User = "$script:RealOwner"; Team = $null }
      "$script:OtherMatter" = @{ User = $null; Team = "$script:TeamB" }
    }

    Mock az { 'test-token' }
    # Anything the filters below do not expect fails the test instead of reaching a real endpoint.
    Mock Invoke-RestMethod { throw "unexpected live call: $Method $Uri" }
    Mock Invoke-WebRequest { throw "unexpected live call: $Uri" }
    Mock Invoke-RestMethod -ParameterFilter { $Method -eq 'Patch' } {
      $id = [regex]::Match("$Uri", 'sprk_matters\(([0-9a-f-]+)\)').Groups[1].Value
      $bind = ($Body | ConvertFrom-Json).'ownerid@odata.bind'
      $global:Gate132Fake.Patches.Add("$id -> $bind")
      $target = [regex]::Match($bind, '\(([0-9a-f-]+)\)').Groups[1].Value
      $global:Gate132Fake.Owners[$id] = if ($bind -like '/teams(*') { @{ User = $null; Team = $target } } else { @{ User = $target; Team = $null } }
    }
    Mock Invoke-RestMethod -ParameterFilter { "$Uri" -like '*RetrievePrincipalAccess*' } { [pscustomobject]@{ AccessRights = 'ReadAccess' } }
    Mock Invoke-RestMethod -ParameterFilter { "$Uri" -like '*/teams(*' -and $Method -ne 'Patch' } { [pscustomobject]@{ name = 'test team' } }
    Mock Invoke-RestMethod -ParameterFilter { "$Uri" -like '*/sprk_matters(*' -and "$Uri" -notlike '*RetrievePrincipalAccess*' -and $Method -ne 'Patch' } {
      $id = [regex]::Match("$Uri", 'sprk_matters\(([0-9a-f-]+)\)').Groups[1].Value
      $o = $global:Gate132Fake.Owners[$id]
      [pscustomobject]@{ _ownerid_value = ($o.Team ?? $o.User); _owningteam_value = $o.Team; _owninguser_value = $o.User; modifiedon = '2026-10-05T00:00:00Z' }
    }
  }

  It 'records the real owner on the first -Apply' {
    Invoke-Gate ReassignMatter @{ MatterId = $Matter; ToTeamId = $TeamA; TestUserId = $TestUser; Apply = $true }

    (Get-Recorded $Matter).OriginalOwner | Should -Be "/systemusers($RealOwner)"
    $global:Gate132Fake.Owners["$Matter"].Team | Should -Be "$TeamA"
  }

  It 'keeps the FIRST recorded original owner when -Apply runs a second time (it does not record the target team)' {
    Invoke-Gate ReassignMatter @{ MatterId = $Matter; ToTeamId = $TeamA; TestUserId = $TestUser; Apply = $true }
    $second = Invoke-Gate ReassignMatter @{ MatterId = $Matter; ToTeamId = $TeamB; TestUserId = $TestUser; Apply = $true }

    $second | Should -Match 'already recorded .* KEPT'
    (Get-Recorded $Matter).OriginalOwner | Should -Be "/systemusers($RealOwner)"
    $global:Gate132Fake.Patches | Should -Be @("$Matter -> /teams($TeamA)", "$Matter -> /teams($TeamB)")
  }

  It 'restores the REAL owner after two reassigns, and marks the record restored' {
    Invoke-Gate ReassignMatter @{ MatterId = $Matter; ToTeamId = $TeamA; TestUserId = $TestUser; Apply = $true }
    Invoke-Gate ReassignMatter @{ MatterId = $Matter; ToTeamId = $TeamB; TestUserId = $TestUser; Apply = $true }
    Invoke-Gate RestoreMatter @{ MatterId = $Matter; Apply = $true }

    $global:Gate132Fake.Patches[-1] | Should -Be "$Matter -> /systemusers($RealOwner)"
    $global:Gate132Fake.Owners["$Matter"].User | Should -Be "$RealOwner"
    (Get-Recorded $Matter).Restored | Should -BeTrue
    Invoke-Gate RestoreMatter @{ MatterId = $Matter; Verify = $true } | Should -Match 'restored: YES'
  }

  It 'records afresh after a verified restore (the restored owner IS the original again)' {
    Invoke-Gate ReassignMatter @{ MatterId = $Matter; ToTeamId = $TeamA; TestUserId = $TestUser; Apply = $true }
    Invoke-Gate RestoreMatter @{ MatterId = $Matter; Apply = $true }
    $global:Gate132Fake.Owners["$Matter"] = @{ User = $null; Team = "$TeamB" }   # the matter's owner changed legitimately since
    Invoke-Gate ReassignMatter @{ MatterId = $Matter; ToTeamId = $TeamA; TestUserId = $TestUser; Apply = $true }

    (Get-Recorded $Matter).OriginalOwner | Should -Be "/teams($TeamB)"
    (Get-Recorded $Matter).Restored | Should -BeFalse
  }

  It 'keeps each matter''s own original owner (a second matter does not overwrite the first)' {
    Invoke-Gate ReassignMatter @{ MatterId = $Matter; ToTeamId = $TeamA; TestUserId = $TestUser; Apply = $true }
    Invoke-Gate ReassignMatter @{ MatterId = $OtherMatter; ToTeamId = $TeamA; TestUserId = $TestUser; Apply = $true }

    (Get-Recorded $Matter).OriginalOwner | Should -Be "/systemusers($RealOwner)"
    (Get-Recorded $OtherMatter).OriginalOwner | Should -Be "/teams($TeamB)"
  }

  It 'reads a state file of the earlier single-matter shape as that matter''s record' {
    @{ MatterId = "$Matter"; OriginalOwner = "/systemusers($RealOwner)"; ReassignedUtc = '2026-10-05T00:00:00.0000000Z' } |
      ConvertTo-Json | Set-Content $script:StatePath
    $global:Gate132Fake.Owners["$Matter"] = @{ User = $null; Team = "$TeamA" }   # already reassigned by the earlier version

    Invoke-Gate ReassignMatter @{ MatterId = $Matter; ToTeamId = $TeamB; TestUserId = $TestUser; Apply = $true }

    (Get-Recorded $Matter).OriginalOwner | Should -Be "/systemusers($RealOwner)"
  }

  It 'makes no PATCH and records nothing without -Apply (dry run)' {
    Invoke-Gate ReassignMatter @{ MatterId = $Matter; ToTeamId = $TeamA; TestUserId = $TestUser } | Should -Match 'Dry run'

    $global:Gate132Fake.Patches.Count | Should -Be 0
    Test-Path $script:StatePath | Should -BeFalse
  }
}
