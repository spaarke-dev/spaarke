# Pester 5. Run: Invoke-Pester tests/scripts/Migrate-DirectMessagesToOwnerTeam.Tests.ps1
# CI: .github/workflows/ci-standalone-checks.yml, job owner-teams-script-tests (Pester 5.7.1, ubuntu-latest).
# field-mapping-server-write-path-r1 task 054c: scripts/Migrate-DirectMessagesToOwnerTeam.ps1 over a mocked Dataverse (an
# in-memory world behind a mocked Invoke-RestMethod and az; nothing talks to Dataverse).
# Pins: the precondition refusal (task 054b's field security, both phases); the Stamp selection; the pair rule (exactly two
# distinct, enabled, non-application systemusers including the sender, from the thread's user owner and its Read shares; a
# team share and a share without the Read bit are not participants); the Move assign invariant (no valid stamped pair, no
# move); the Direct team checks; the per-row stops; idempotence and -Verify.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Script = Join-Path $script:RepoRoot 'scripts' 'Migrate-DirectMessagesToOwnerTeam.ps1'
    $script:Url = 'https://example.crm.dynamics.com'
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) { function global:az { throw 'Test: az is never called unmocked.' } }

    function global:MdmId([int]$n) { '00000000-0000-4000-8000-{0:d12}' -f $n }
    $global:MdmBffAppId = 'aaaaaaaa-0000-4000-8000-000000000001'
    $global:MdmIds = @{
        Bff = MdmId 1; Alice = MdmId 2; Bob = MdmId 3; Carol = MdmId 4; Dave = MdmId 5; AppUser = MdmId 6; Disabled = MdmId 7
        Root = MdmId 10; SecureBu = MdmId 11; DirectBu = MdmId 12; CustomerBu = MdmId 13
        SecureTeam = MdmId 20; DirectTeam = MdmId 21; SomeTeam = MdmId 22; DirectDefault = MdmId 23
        Reader = MdmId 30; Writer = MdmId 31
    }

    # The world every mocked request reads (global: the mock body runs from the script under test).
    function global:New-MdmWorld {
        $i = $global:MdmIds
        $global:MdmWorld = @{
            Org          = [pscustomobject]@{ name = 'test'; sharetopreviousowneronassign = $false }
            Unsecured    = @()   # 'table.column' entries reported NOT field-secured
            WriterUsers  = @([pscustomobject]@{ systemuserid = $i.Bff; fullname = 'BFF'; applicationid = $global:MdmBffAppId })
            WriterTeams  = @()
            OtherWriter  = $null # 'table.column' a third profile can update
            Users        = @(
                [pscustomobject]@{ systemuserid = $i.Bff; isdisabled = $false; applicationid = $global:MdmBffAppId; bu = $i.Root }
                [pscustomobject]@{ systemuserid = $i.Alice; isdisabled = $false; applicationid = $null; bu = $i.CustomerBu }
                [pscustomobject]@{ systemuserid = $i.Bob; isdisabled = $false; applicationid = $null; bu = $i.CustomerBu }
                [pscustomobject]@{ systemuserid = $i.Carol; isdisabled = $false; applicationid = $null; bu = $i.CustomerBu }
                [pscustomobject]@{ systemuserid = $i.Dave; isdisabled = $false; applicationid = $null; bu = $i.CustomerBu }
                [pscustomobject]@{ systemuserid = $i.AppUser; isdisabled = $false; applicationid = 'bbbbbbbb-0000-4000-8000-000000000002'; bu = $i.Root }
                [pscustomobject]@{ systemuserid = $i.Disabled; isdisabled = $true; applicationid = $null; bu = $i.CustomerBu }
            )
            Units        = @(
                [pscustomobject]@{ businessunitid = $i.Root; name = 'Spaarke'; _parentbusinessunitid_value = $null }
                [pscustomobject]@{ businessunitid = $i.SecureBu; name = 'Secure Record'; _parentbusinessunitid_value = $i.Root }
                [pscustomobject]@{ businessunitid = $i.DirectBu; name = 'Direct Messages'; _parentbusinessunitid_value = $i.Root }
                [pscustomobject]@{ businessunitid = $i.CustomerBu; name = 'Customer One'; _parentbusinessunitid_value = $i.Root }
            )
            Teams        = @(
                [pscustomobject]@{ teamid = $i.SecureTeam; name = 'Secure Record Owners'; bu = $i.SecureBu; teamtype = 0; isdefault = $false }
                [pscustomobject]@{ teamid = $i.DirectTeam; name = 'Direct Owners'; bu = $i.DirectBu; teamtype = 0; isdefault = $false }
                [pscustomobject]@{ teamid = $i.DirectDefault; name = 'Direct Messages'; bu = $i.DirectBu; teamtype = 0; isdefault = $true }
                [pscustomobject]@{ teamid = $i.SomeTeam; name = 'Customer One'; bu = $i.CustomerBu; teamtype = 0; isdefault = $true }
            )
            Memberships  = @()
            Threads      = @()
            Poa          = @()
            Messages     = @()
            Content      = @()
            Faults       = @()   # regexes: a GET whose URI matches throws
            IgnoreAssign = $false
            DropShareOnAssign = $false
            Requests     = New-Object System.Collections.Generic.List[string]
            Writes       = New-Object System.Collections.Generic.List[object]
        }
    }

    function global:Add-MdmThread([string]$Id, [string]$OwnerUser, [string]$OwnerTeam, [object[]]$Shares = @(), [object]$IsDefault = $false, [int]$Type = 100000001, [string]$CreatedOn = '2026-01-01T00:00:00Z') {
        $global:MdmWorld.Threads += [pscustomobject]@{ sprk_communicationthreadid = $Id; sprk_threadtype = $Type; sprk_isdefaultthread = $IsDefault; _owninguser_value = $OwnerUser; _owningteam_value = $OwnerTeam; createdon = $CreatedOn }
        foreach ($s in $Shares) {
            $global:MdmWorld.Poa += [pscustomobject]@{ objectid = $Id; principalid = $s.P; principaltypecode = $(if ($s.Team) { 'team' } else { 'systemuser' }); accessrightsmask = $s.Mask; changedon = $(if ($s.ChangedOn) { $s.ChangedOn } else { $CreatedOn }) }
        }
    }
    function global:Add-MdmMessage([string]$Id, [string]$Thread, [string]$SentBy, [string]$Recipient, [string]$OwnerUser = $global:MdmIds.Alice, [string]$OwnerTeam, [int]$Type = 100000004, [string]$Regarding) {
        $row = [pscustomobject]@{
            sprk_communicationid = $Id; sprk_name = "FMSWP-$Id"; sprk_communicationtype = $Type; _sprk_communicationthread_value = $Thread
            _sprk_sentby_value = $SentBy; _sprk_directrecipient_value = $Recipient; _owninguser_value = $(if ($OwnerTeam) { $null } else { $OwnerUser }); _owningteam_value = $OwnerTeam
        }
        foreach ($c in 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardinginvoice', 'sprk_regardingservicerequest', 'sprk_regardingworkassignment',
            'sprk_regardingevent', 'sprk_regardingbudget', 'sprk_regardingreportcard', 'sprk_regardinganalysis', 'sprk_regardingorganization', 'sprk_regardingaccount', 'sprk_regardingperson') {
            $row | Add-Member -NotePropertyName "_$($c)_value" -NotePropertyValue $(if ($Regarding -eq $c) { MdmId 999 } else { $null })
        }
        $global:MdmWorld.Messages += $row
    }
    function global:Add-MdmContent([string]$Set, [string]$Id, [string]$Message, [string]$OwnerUser = $global:MdmIds.Alice, [string]$OwnerTeam, [string]$CreatedBy = $global:MdmIds.Bff, [string]$ConfirmedBy) {
        $global:MdmWorld.Content += [pscustomobject]@{ Set = $Set; Id = $Id; Message = $Message; _owninguser_value = $(if ($OwnerTeam) { $null } else { $OwnerUser }); _owningteam_value = $OwnerTeam; _createdby_value = $CreatedBy; _sprk_confirmedby_value = $ConfirmedBy }
        $global:MdmWorld.Poa += [pscustomobject]@{ objectid = $Id; principalid = $global:MdmIds.Bob; principaltypecode = 'systemuser'; accessrightsmask = 1; changedon = '2026-01-01T00:00:00Z' }
    }

    function global:Invoke-MdmGet([string]$u) {
        $w = $global:MdmWorld
        $wrap = { param($rows) [pscustomobject]@{ value = @($rows) } }
        if ($u -match '/organizations\?') { return & $wrap @($w.Org) }
        if ($u -match "EntityDefinitions\(LogicalName='([^']+)'\)/Attributes\(LogicalName='([^']+)'\)") {
            return [pscustomobject]@{ IsSecured = ($w.Unsecured -notcontains "$($Matches[1]).$($Matches[2])") }
        }
        if ($u -match "/fieldsecurityprofiles\?.*name eq '([^']+)'") {
            $name = $Matches[1]
            if ($name -eq 'Spaarke BFF-Managed Field Readers') {
                $defaults = @($w.Teams | Where-Object isdefault | ForEach-Object { [pscustomobject]@{ teamid = $_.teamid; name = $_.name } })
                return & $wrap @([pscustomobject]@{ fieldsecurityprofileid = $global:MdmIds.Reader; name = $name; teamprofiles_association = $defaults; systemuserprofiles_association = @() })
            }
            return & $wrap @([pscustomobject]@{ fieldsecurityprofileid = $global:MdmIds.Writer; name = $name; teamprofiles_association = @($w.WriterTeams); systemuserprofiles_association = @($w.WriterUsers) })
        }
        if ($u -match '/systemusers\?.*applicationid eq ([0-9a-f-]+)') {
            $app = $Matches[1]
            return & $wrap @($w.Users | Where-Object { $_.applicationid -eq $app } | ForEach-Object { [pscustomobject]@{ systemuserid = $_.systemuserid; fullname = 'app' } })
        }
        if ($u -match '/systemusers\?.*_businessunitid_value eq ([0-9a-f-]+)') {
            $bu = $Matches[1]; return & $wrap @($w.Users | Where-Object { $_.bu -eq $bu })
        }
        if ($u -match '/systemusers\?.*systemuserid eq') {
            $ids = [regex]::Matches($u, 'systemuserid eq ([0-9a-f-]+)') | ForEach-Object { $_.Groups[1].Value }
            return & $wrap @($w.Users | Where-Object { $ids -contains $_.systemuserid })
        }
        if ($u -match '/teams\?.*_businessunitid_value eq ([0-9a-f-]+) and name eq ''([^'']+)''.*isdefault eq (true|false)') {
            $bu = $Matches[1]; $name = $Matches[2]; $default = $Matches[3] -eq 'true'
            return & $wrap @($w.Teams | Where-Object { $_.bu -eq $bu -and $_.name -eq $name -and [bool]$_.isdefault -eq $default })
        }
        if ($u -match '/teams\?.*isdefault eq true') { return & $wrap @($w.Teams | Where-Object isdefault) }
        if ($u -match "/fieldpermissions\?.*entityname eq '([^']+)' and attributelogicalname eq '([^']+)'") {
            $col = "$($Matches[1]).$($Matches[2])"
            $rows = @(
                [pscustomobject]@{ canread = 4; cancreate = 0; canupdate = 0; _fieldsecurityprofileid_value = $global:MdmIds.Reader; fieldsecurityprofileid = [pscustomobject]@{ name = 'Spaarke BFF-Managed Field Readers' } }
                [pscustomobject]@{ canread = 4; cancreate = 4; canupdate = 4; _fieldsecurityprofileid_value = $global:MdmIds.Writer; fieldsecurityprofileid = [pscustomobject]@{ name = 'Spaarke BFF-Managed Field Writers' } }
            )
            if ($w.OtherWriter -eq $col) { $rows += [pscustomobject]@{ canread = 4; cancreate = 0; canupdate = 4; _fieldsecurityprofileid_value = (MdmId 32); fieldsecurityprofileid = [pscustomobject]@{ name = 'Someone Else' } } }
            return & $wrap $rows
        }
        if ($u -match '/sprk_communicationthreads\?') { return & $wrap @($w.Threads | Where-Object { $_.sprk_threadtype -eq 100000001 }) }
        if ($u -match '/sprk_communications\?') {
            $rows = @($w.Messages | Where-Object { $_.sprk_communicationtype -eq 100000004 -and $_._sprk_communicationthread_value })
            if ($u -match '_sprk_directrecipient_value eq null') { $rows = @($rows | Where-Object { -not $_._sprk_directrecipient_value }) }
            if ($u -match "startswith\(sprk_name,'([^']+)'\)") { $p = $Matches[1]; $rows = @($rows | Where-Object { $_.sprk_name.StartsWith($p) }) }
            return & $wrap $rows
        }
        if ($u -match '/sprk_communications\(([0-9a-f-]+)\)') { $id = $Matches[1]; return $w.Messages | Where-Object { $_.sprk_communicationid -eq $id } }
        if ($u -match '/principalobjectaccessset\?.*objectid eq ([0-9a-f-]+)') { $id = $Matches[1]; return & $wrap @($w.Poa | Where-Object { $_.objectid -eq $id }) }
        if ($u -match "/businessunits\?.*name eq '([^']+)'") { $n = $Matches[1]; return & $wrap @($w.Units | Where-Object { $_.name -eq $n }) }
        if ($u -match '/businessunits\(([0-9a-f-]+)\)') { $id = $Matches[1]; return $w.Units | Where-Object { $_.businessunitid -eq $id } }
        if ($u -match '/teammemberships\?.*teamid eq ([0-9a-f-]+)') { $id = $Matches[1]; return & $wrap @($w.Memberships | Where-Object { $_.teamid -eq $id }) }
        if ($u -match '/(sprk_communicationattachments|sprk_communicationparticipants|sprk_emailreviewlogs|sprk_documents|sprk_decisionrecords)\?.*_(\w+)_value eq ([0-9a-f-]+)') {
            $set = $Matches[1]; $msg = $Matches[3]; $idCol = ($set.TrimEnd('s')) + 'id'
            $rows = @($w.Content | Where-Object { $_.Set -eq $set -and $_.Message -eq $msg })
            if ($u -match '_sprk_confirmedby_value eq null') { $rows = @($rows | Where-Object { -not $_._sprk_confirmedby_value }) }
            return & $wrap @($rows | ForEach-Object { $r = [pscustomobject]@{ _owningteam_value = $_._owningteam_value; _owninguser_value = $_._owninguser_value; _createdby_value = $_._createdby_value }; $r | Add-Member -NotePropertyName $idCol -NotePropertyValue $_.Id; $r })
        }
        if ($u -match '/(sprk_communicationattachments|sprk_communicationparticipants|sprk_emailreviewlogs|sprk_documents|sprk_decisionrecords)\(([0-9a-f-]+)\)') {
            $id = $Matches[2]; return $w.Content | Where-Object { $_.Id -eq $id }
        }
        throw "Test: unexpected read $u"
    }

    function global:Invoke-MdmPatch([string]$u, $Body) {
        $w = $global:MdmWorld
        $b = [Text.Encoding]::UTF8.GetString([byte[]]$Body) | ConvertFrom-Json
        $w.Writes.Add([pscustomobject]@{ Uri = $u; Body = $b })
        if (-not ($u -match '/(\w+)\(([0-9a-f-]+)\)$')) { throw "Test: unexpected write $u" }
        $set = $Matches[1]; $id = $Matches[2]
        $row = if ($set -eq 'sprk_communications') { $w.Messages | Where-Object { $_.sprk_communicationid -eq $id } } else { $w.Content | Where-Object { $_.Id -eq $id } }
        if (-not $row) { throw "Test: write to a missing row $u" }
        if ($b.'sprk_DirectRecipient@odata.bind' -match '/systemusers\(([0-9a-f-]+)\)') { $row._sprk_directrecipient_value = $Matches[1] }
        if ($b.'ownerid@odata.bind' -match '/teams\(([0-9a-f-]+)\)') {
            if (-not $w.IgnoreAssign) { $row._owningteam_value = $Matches[1]; $row._owninguser_value = $null }
            if ($w.DropShareOnAssign) { $w.Poa = @($w.Poa | Where-Object { $_.objectid -ne $id }) }
        }
    }

    # Runs the script; Output is everything it printed; Review is the review list it wrote (JSON).
    function global:Invoke-Mdm([hashtable]$Arguments) {
        $review = Join-Path ([IO.Path]::GetTempPath()) ("mdm-review-{0}.json" -f [guid]::NewGuid())
        $all = @{ EnvironmentUrl = $script:Url; BffApplicationIds = @($global:MdmBffAppId); ReviewPath = $review }
        foreach ($k in $Arguments.Keys) { $all[$k] = $Arguments[$k] }
        $global:LASTEXITCODE = 0
        $output = & $script:Script @all 6>&1 2>&1 | Out-String
        $code = $LASTEXITCODE
        $rows = if (Test-Path $review) { @(Get-Content -Raw $review | ConvertFrom-Json) } else { @() }
        Remove-Item $review -ErrorAction SilentlyContinue
        [pscustomobject]@{ ExitCode = $code; Output = $output; Review = @($rows | Where-Object { $_ }) }
    }
    function global:Invoke-MdmMove([hashtable]$Extra = @{}) {
        $a = @{ Phase = 'Move'; DirectMessageBusinessUnitName = 'Direct Messages'; DirectMessageOwnerTeamName = 'Direct Owners' }
        foreach ($k in $Extra.Keys) { $a[$k] = $Extra[$k] }
        Invoke-Mdm $a
    }
    function global:ReasonOf($r, [string]$MessageId) { @($r.Review | Where-Object { $_.MessageId -eq $MessageId })[0].Reason }
}

AfterAll {
    Remove-Variable -Scope Global -Name 'Mdm*' -ErrorAction SilentlyContinue
}

Describe 'Migrate-DirectMessagesToOwnerTeam.ps1 (mocked Dataverse)' {
    BeforeEach {
        New-MdmWorld
        Mock az { 'test-token' }
        Mock Invoke-RestMethod {
            $global:MdmWorld.Requests.Add("$Method $Uri")
            foreach ($f in $global:MdmWorld.Faults) { if ([string]$Uri -match $f) { throw "Test: simulated fault on $Uri" } }
            if ($Method -eq 'Get') { return Invoke-MdmGet ([string]$Uri) }
            if ($Method -eq 'Patch') { return Invoke-MdmPatch ([string]$Uri) $Body }
            throw "Test: unexpected $Method $Uri"
        }
        $i = $global:MdmIds
        $global:MdmT1 = MdmId 100; $global:MdmM1 = MdmId 200; $T1 = $global:MdmT1; $M1 = $global:MdmM1
    }

    Context 'Precondition: task 054b field security (both phases)' {
        It 'must fire: a column that is not field-secured refuses; nothing is selected or written (Stamp)' {
            Add-MdmThread $T1 $global:MdmIds.Alice $null @(@{ P = $global:MdmIds.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $global:MdmIds.Alice $null
            $global:MdmWorld.Unsecured = @('sprk_communication.sprk_directrecipient')
            $r = Invoke-Mdm @{ Phase = 'Stamp'; Apply = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'REFUSED'
            $r.Output | Should -Match 'sprk_communication.sprk_directrecipient is NOT field-secured'
            @($global:MdmWorld.Requests | Where-Object { $_ -match 'sprk_communications|sprk_communicationthreads' }).Count | Should -Be 0
            $global:MdmWorld.Writes.Count | Should -Be 0
        }

        It 'must fire: a thread flag that is not field-secured refuses (Move)' {
            $global:MdmWorld.Unsecured = @('sprk_communicationthread.sprk_isdefaultthread')
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'REFUSED'
            @($global:MdmWorld.Requests | Where-Object { $_ -match 'sprk_communications|businessunits' }).Count | Should -Be 0
        }

        It 'must fire: a human user on the writer profile refuses' {
            $global:MdmWorld.WriterUsers += [pscustomobject]@{ systemuserid = $global:MdmIds.Alice; fullname = 'Alice'; applicationid = $null }
            $r = Invoke-Mdm @{ Phase = 'Stamp' }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'is a HUMAN user'
        }

        It 'must fire: a team on the writer profile refuses' {
            $global:MdmWorld.WriterTeams = @([pscustomobject]@{ teamid = $global:MdmIds.SomeTeam; name = 'Customer One' })
            $r = Invoke-Mdm @{ Phase = 'Stamp' }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'writer profile holds TEAM'
        }

        It 'must fire: a BFF identity missing from the writer profile refuses' {
            $global:MdmWorld.WriterUsers = @()
            $r = Invoke-Mdm @{ Phase = 'Stamp' }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'is NOT on the writer profile'
        }

        It 'must fire: another profile that can update a secured column refuses' {
            $global:MdmWorld.OtherWriter = 'sprk_communication.sprk_sentby'
            $r = Invoke-Mdm @{ Phase = 'Stamp' }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match "profile 'Someone Else' can also write sprk_communication.sprk_sentby"
        }

        It 'must not fire: with 054b in place the dry run proceeds and exits 0' {
            $r = Invoke-Mdm @{ Phase = 'Stamp' }
            $r.ExitCode | Should -Be 0
            $r.Output | Should -Not -Match 'REFUSED'
            $r.Output | Should -Match 'DRY RUN'
        }
    }

    Context 'Stamp: selection' {
        It 'selects only an unstamped Message in a Direct, non-master thread with no typed regarding; the dry run writes nothing' {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmThread (MdmId 101) $i.Alice $null @(@{ P = $i.Bob; Mask = 1 }) -IsDefault $true    # per-user master
            Add-MdmThread (MdmId 102) $i.Alice $null @(@{ P = $i.Bob; Mask = 1 }) -Type 100000000      # not Direct
            Add-MdmMessage $M1 $T1 $i.Alice $null                                                      # selected
            Add-MdmMessage (MdmId 201) $T1 $i.Alice $null -Type 100000000                             # email
            Add-MdmMessage (MdmId 202) $T1 $i.Alice $null -Regarding 'sprk_regardingmatter'           # record-filed
            Add-MdmMessage (MdmId 203) (MdmId 101) $i.Alice $null                                     # master thread
            Add-MdmMessage (MdmId 204) (MdmId 102) $i.Alice $null                                     # not Direct
            Add-MdmMessage (MdmId 205) $T1 $i.Alice $i.Bob                                            # already stamped
            $r = Invoke-Mdm @{ Phase = 'Stamp' }
            $r.ExitCode | Should -Be 0
            $r.Output | Should -Match 'PLAN: 1 row\(s\) to stamp'
            $r.Output | Should -Match "stamp $M1 .*recipient $($i.Bob)"
            $r.Review.Count | Should -Be 0
            $global:MdmWorld.Writes.Count | Should -Be 0
        }

        It 'a null default-thread flag goes to review, never stamped' {
            Add-MdmThread $T1 $global:MdmIds.Alice $null @(@{ P = $global:MdmIds.Bob; Mask = 1 }) -IsDefault $null
            Add-MdmMessage $M1 $T1 $global:MdmIds.Alice $null
            $r = Invoke-Mdm @{ Phase = 'Stamp' }
            ReasonOf $r $M1 | Should -Be 'thread_default_flag_unread'
            $r.Output | Should -Match 'PLAN: 0 row'
        }

        It '-NamePrefix limits the selection' {
            Add-MdmThread $T1 $global:MdmIds.Alice $null @(@{ P = $global:MdmIds.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $global:MdmIds.Alice $null
            $r = Invoke-Mdm @{ Phase = 'Stamp'; NamePrefix = 'NOPE-' }
            $r.Output | Should -Match 'PLAN: 0 row'
        }

        It 'reads no Direct-team setting and no business unit or named team' {
            Add-MdmThread $T1 $global:MdmIds.Alice $null @(@{ P = $global:MdmIds.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $global:MdmIds.Alice $null
            $r = Invoke-Mdm @{ Phase = 'Stamp'; Apply = $true }
            $r.ExitCode | Should -Be 0
            @($global:MdmWorld.Requests | Where-Object { $_ -match 'businessunits|teammemberships|teamtype eq 0' }).Count | Should -Be 0
        }
    }

    Context 'Stamp: the pair rule' {
        It '<Case>' -TestCases @(
            @{ Case = 'must fire: owner + one Read share, the sender among them -> stamped'; Owner = 'Alice'; Team = $null; Shares = @(@{ P = 'Bob'; Mask = 1 }); SentBy = 'Alice'; Expect = 'Bob' }
            @{ Case = 'must fire: the sender is the shared one -> the owner is the recipient'; Owner = 'Alice'; Team = $null; Shares = @(@{ P = 'Bob'; Mask = 3 }); SentBy = 'Bob'; Expect = 'Alice' }
            @{ Case = 'a team share is not a participant -> still two, stamped'; Owner = 'Alice'; Team = $null; Shares = @(@{ P = 'Bob'; Mask = 1 }, @{ P = 'SomeTeam'; Mask = 1; Team = $true }); SentBy = 'Alice'; Expect = 'Bob' }
            @{ Case = 'a zero-mask share is not a participant -> still two, stamped'; Owner = 'Alice'; Team = $null; Shares = @(@{ P = 'Bob'; Mask = 1 }, @{ P = 'Carol'; Mask = 0 }); SentBy = 'Alice'; Expect = 'Bob' }
            @{ Case = 'a share without the Read bit is not a participant -> still two, stamped'; Owner = 'Alice'; Team = $null; Shares = @(@{ P = 'Bob'; Mask = 1 }, @{ P = 'Carol'; Mask = 6 }); SentBy = 'Alice'; Expect = 'Bob' }
            @{ Case = 'a team-owned thread: its two user Read shares are the pair -> stamped'; Owner = $null; Team = 'SomeTeam'; Shares = @(@{ P = 'Alice'; Mask = 1 }, @{ P = 'Bob'; Mask = 1 }); SentBy = 'Alice'; Expect = 'Bob' }
            @{ Case = 'must not fire: three participants'; Owner = 'Alice'; Team = $null; Shares = @(@{ P = 'Bob'; Mask = 1 }, @{ P = 'Carol'; Mask = 1 }); SentBy = 'Alice'; Reason = 'not_two_participants' }
            @{ Case = 'must not fire: one participant (owner alone)'; Owner = 'Alice'; Team = $null; Shares = @(); SentBy = 'Alice'; Reason = 'not_two_participants' }
            @{ Case = 'must not fire: the owner shared to itself is one participant'; Owner = 'Alice'; Team = $null; Shares = @(@{ P = 'Alice'; Mask = 1 }); SentBy = 'Alice'; Reason = 'not_two_participants' }
            @{ Case = 'must not fire: the sender is not one of the two'; Owner = 'Bob'; Team = $null; Shares = @(@{ P = 'Carol'; Mask = 1 }); SentBy = 'Alice'; Reason = 'sender_not_a_participant' }
            @{ Case = 'must not fire: the recipient is disabled'; Owner = 'Alice'; Team = $null; Shares = @(@{ P = 'Disabled'; Mask = 1 }); SentBy = 'Alice'; Reason = 'participant_disabled' }
            @{ Case = 'must not fire: the recipient is an application user'; Owner = 'Alice'; Team = $null; Shares = @(@{ P = 'AppUser'; Mask = 1 }); SentBy = 'Alice'; Reason = 'participant_application_user' }
            @{ Case = 'must not fire: an application-user owner'; Owner = 'AppUser'; Team = $null; Shares = @(@{ P = 'Alice'; Mask = 1 }); SentBy = 'Alice'; Reason = 'participant_application_user' }
            @{ Case = 'must not fire: the sender is disabled'; Owner = 'Disabled'; Team = $null; Shares = @(@{ P = 'Bob'; Mask = 1 }); SentBy = 'Disabled'; Reason = 'participant_disabled' }
            @{ Case = 'must not fire: no sprk_sentby'; Owner = 'Alice'; Team = $null; Shares = @(@{ P = 'Bob'; Mask = 1 }); SentBy = $null; Reason = 'no_sentby' }
        ) {
            param($Case, $Owner, $Team, $Shares, $SentBy, $Expect, $Reason)
            $i = $global:MdmIds
            $shareRows = @($Shares | ForEach-Object { @{ P = $i[$_.P]; Mask = $_.Mask; Team = [bool]$_.Team } })
            Add-MdmThread $T1 $(if ($Owner) { $i[$Owner] } else { $null }) $(if ($Team) { $i[$Team] } else { $null }) $shareRows
            Add-MdmMessage $M1 $T1 $(if ($SentBy) { $i[$SentBy] } else { $null }) $null
            $r = Invoke-Mdm @{ Phase = 'Stamp'; Apply = $true }
            $r.ExitCode | Should -Be 0
            $row = $global:MdmWorld.Messages | Where-Object sprk_communicationid -eq $M1
            if ($Expect) {
                $row._sprk_directrecipient_value | Should -Be $i[$Expect]
                $r.Review.Count | Should -Be 0
            }
            else {
                $row._sprk_directrecipient_value | Should -BeNullOrEmpty
                $global:MdmWorld.Writes.Count | Should -Be 0
                ReasonOf $r $M1 | Should -Be $Reason
            }
        }

        It 'must not fire: a share read fault sends the row to review as read_fault and writes nothing' {
            Add-MdmThread $T1 $global:MdmIds.Alice $null @(@{ P = $global:MdmIds.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $global:MdmIds.Alice $null
            $global:MdmWorld.Faults = @("principalobjectaccessset.*objectid eq $T1")
            $r = Invoke-Mdm @{ Phase = 'Stamp'; Apply = $true }
            ReasonOf $r $M1 | Should -Be 'read_fault'
            $global:MdmWorld.Writes.Count | Should -Be 0
        }

        It 'must not fire: a thread share changed after the thread was created goes to review' {
            Add-MdmThread $T1 $global:MdmIds.Alice $null @(@{ P = $global:MdmIds.Bob; Mask = 1; ChangedOn = '2026-03-01T00:00:00Z' })
            Add-MdmMessage $M1 $T1 $global:MdmIds.Alice $null
            $r = Invoke-Mdm @{ Phase = 'Stamp'; Apply = $true }
            ReasonOf $r $M1 | Should -Be 'thread_shares_changed_after_create'
            $global:MdmWorld.Writes.Count | Should -Be 0
        }
    }

    Context 'Stamp: apply, idempotence, verify' {
        It 'stamps by read-back; a second run selects nothing; -Verify exits 1 before and 0 after' {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $null
            (Invoke-Mdm @{ Phase = 'Stamp'; Verify = $true }).ExitCode | Should -Be 1
            $r = Invoke-Mdm @{ Phase = 'Stamp'; Apply = $true }
            $r.ExitCode | Should -Be 0
            $r.Output | Should -Match 'APPLIED: 1 row'
            $global:MdmWorld.Writes.Count | Should -Be 1
            $global:MdmWorld.Writes[0].Uri | Should -Match "sprk_communications\($M1\)$"
            $global:MdmWorld.Writes[0].Body.'sprk_DirectRecipient@odata.bind' | Should -Be "/systemusers($($i.Bob))"
            $again = Invoke-Mdm @{ Phase = 'Stamp'; Apply = $true }
            $again.Output | Should -Match 'PLAN: 0 row'
            $global:MdmWorld.Writes.Count | Should -Be 1
            (Invoke-Mdm @{ Phase = 'Stamp'; Verify = $true }).ExitCode | Should -Be 0
        }

        It 'a row stamped between selection and write is skipped and listed (changed_since_selection)' {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $null
            # The single-row re-read sees a recipient the selection did not.
            Mock Invoke-RestMethod -ParameterFilter { $Method -eq 'Get' -and [string]$Uri -match "sprk_communications\($($global:MdmM1)\)" } {
                [pscustomobject]@{ _sprk_directrecipient_value = $global:MdmIds.Carol; _sprk_sentby_value = $global:MdmIds.Alice; _sprk_communicationthread_value = $global:MdmT1 }
            }
            $r = Invoke-Mdm @{ Phase = 'Stamp'; Apply = $true }
            ReasonOf $r $M1 | Should -Be 'changed_since_selection'
            $global:MdmWorld.Writes.Count | Should -Be 0
        }
    }

    Context 'Move: the assign invariant' {
        It 'must fire: a message without a valid stamped pair keeps its owner and is listed; its content rows do not move' {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $null                       # no recipient
            Add-MdmMessage (MdmId 201) $T1 $i.Alice $i.Alice           # not two distinct
            Add-MdmMessage (MdmId 202) $T1 $null $i.Bob                # no sender
            Add-MdmContent 'sprk_communicationattachments' (MdmId 300) $M1
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 0
            $global:MdmWorld.Writes.Count | Should -Be 0
            ($global:MdmWorld.Messages | Where-Object sprk_communicationid -eq $M1)._owninguser_value | Should -Be $i.Alice
            ($global:MdmWorld.Content | Where-Object Id -eq (MdmId 300))._owninguser_value | Should -Be $i.Alice
            foreach ($id in $M1, (MdmId 201), (MdmId 202)) { ReasonOf $r $id | Should -Be 'no_valid_pair' }
            (Invoke-MdmMove @{ Verify = $true }).ExitCode | Should -Be 0
        }

        It 'must fire: a pair that is no longer valid at write time moves nothing of that message' {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            Add-MdmContent 'sprk_documents' (MdmId 300) $M1
            Mock Invoke-RestMethod -ParameterFilter { $Method -eq 'Get' -and [string]$Uri -match "sprk_communications\($($global:MdmM1)\)" } {
                [pscustomobject]@{ sprk_communicationid = $global:MdmM1; sprk_name = 'FMSWP-x'; _sprk_communicationthread_value = $global:MdmT1; _sprk_sentby_value = $global:MdmIds.Alice; _sprk_directrecipient_value = $null; _owninguser_value = $global:MdmIds.Alice; _owningteam_value = $null }
            }
            $r = Invoke-MdmMove @{ Apply = $true }
            $global:MdmWorld.Writes.Count | Should -Be 0
            ReasonOf $r $M1 | Should -Be 'no_valid_pair'
        }
    }

    Context 'Move: apply, content rows, idempotence, verify' {
        It 'moves the content rows then the message; skips Direct- and Secure-owned rows and a non-gate Decision Record' {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            Add-MdmContent 'sprk_communicationattachments' (MdmId 300) $M1
            Add-MdmContent 'sprk_communicationparticipants' (MdmId 301) $M1
            Add-MdmContent 'sprk_emailreviewlogs' (MdmId 302) $M1
            Add-MdmContent 'sprk_documents' (MdmId 303) $M1
            Add-MdmContent 'sprk_decisionrecords' (MdmId 304) $M1                                   # gate record (BFF-created, unconfirmed)
            Add-MdmContent 'sprk_decisionrecords' (MdmId 305) $M1 -CreatedBy $i.Alice               # not a gate record
            Add-MdmContent 'sprk_decisionrecords' (MdmId 306) $M1 -ConfirmedBy $i.Alice             # confirmed: not a gate record
            Add-MdmContent 'sprk_documents' (MdmId 307) $M1 -OwnerTeam $i.SecureTeam                # Secure-owned
            Add-MdmContent 'sprk_documents' (MdmId 308) $M1 -OwnerTeam $i.DirectTeam                # already Direct
            Add-MdmMessage (MdmId 201) $T1 $i.Alice $i.Bob -OwnerTeam $i.SecureTeam                 # Secure-owned message

            (Invoke-MdmMove @{ Verify = $true }).ExitCode | Should -Be 1
            $dry = Invoke-MdmMove @{}
            $dry.ExitCode | Should -Be 0
            $global:MdmWorld.Writes.Count | Should -Be 0

            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 0
            $moved = @($global:MdmWorld.Writes | ForEach-Object { ([regex]::Match($_.Uri, '\(([0-9a-f-]+)\)$')).Groups[1].Value })
            $moved | Should -Be @((MdmId 300), (MdmId 301), (MdmId 302), (MdmId 303), (MdmId 304), $M1)
            @($global:MdmWorld.Writes | Where-Object { $_.Body.'ownerid@odata.bind' -ne "/teams($($i.DirectTeam))" }).Count | Should -Be 0
            ($global:MdmWorld.Content | Where-Object Id -eq (MdmId 305))._owninguser_value | Should -Be $i.Alice
            ($global:MdmWorld.Content | Where-Object Id -eq (MdmId 307))._owningteam_value | Should -Be $i.SecureTeam
            ($global:MdmWorld.Messages | Where-Object sprk_communicationid -eq (MdmId 201))._owningteam_value | Should -Be $i.SecureTeam

            $again = Invoke-MdmMove @{ Apply = $true }
            $again.Output | Should -Match 'PLAN: 0 message'
            $global:MdmWorld.Writes.Count | Should -Be 6
            (Invoke-MdmMove @{ Verify = $true }).ExitCode | Should -Be 0
        }

        It '-Verify exits 1 when a leftover is seeded after a complete move, and -Apply moves it' {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob -OwnerTeam $i.DirectTeam
            (Invoke-MdmMove @{ Verify = $true }).ExitCode | Should -Be 0
            Add-MdmContent 'sprk_communicationattachments' (MdmId 300) $M1                            # a content row left behind
            $v = Invoke-MdmMove @{ Verify = $true }
            $v.ExitCode | Should -Be 1
            $v.Output | Should -Match "VERIFY FAIL: message $M1"
            (Invoke-MdmMove @{ Apply = $true }).ExitCode | Should -Be 0
            $global:MdmWorld.Writes.Count | Should -Be 1
            (Invoke-MdmMove @{ Verify = $true }).ExitCode | Should -Be 0
        }
    }

    Context 'Move: per-row stops' {
        BeforeEach {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            $global:MdmWorld.Poa += [pscustomobject]@{ objectid = $M1; principalid = $i.Bob; principaltypecode = 'systemuser'; accessrightsmask = 1; changedon = '2026-01-01T00:00:00Z' }
        }

        It 'must fire: sharetopreviousowneronassign on refuses -Apply before any write' {
            $global:MdmWorld.Org.sharetopreviousowneronassign = $true
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'sharetopreviousowneronassign'
            $global:MdmWorld.Writes.Count | Should -Be 0
        }

        It 'must fire: a share count that fell stops the run' {
            $global:MdmWorld.DropShareOnAssign = $true
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'share count fell'
        }

        It 'must fire: an owner that did not land stops the run' {
            $global:MdmWorld.IgnoreAssign = $true
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'read-back of sprk_communication'
        }

        It 'must not fire: shares kept and owner landed -> exit 0' {
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 0
            $r.Output | Should -Match 'shares 1 -> 1'
        }
    }

    Context 'Move: the Direct team checks (refuse, select nothing)' {
        It '<Case>' -TestCases @(
            @{ Case = 'the unit is the root'; Setup = { ($global:MdmWorld.Units | Where-Object name -eq 'Direct Messages')._parentbusinessunitid_value = $null }; Expect = 'bu_is_root' }
            @{ Case = 'the unit is not a direct child of the root'; Setup = { ($global:MdmWorld.Units | Where-Object name -eq 'Direct Messages')._parentbusinessunitid_value = $global:MdmIds.CustomerBu }; Expect = 'bu_not_root_child' }
            @{ Case = 'the unit is the Secure Record unit'; Setup = { $script:BuName = 'Secure Record'; $script:TeamName = 'Secure Record Owners' }; Expect = 'bu_is_secure_record' }
            @{ Case = 'the team has a member'; Setup = { $global:MdmWorld.Memberships = @([pscustomobject]@{ teamid = $global:MdmIds.DirectTeam; systemuserid = $global:MdmIds.Alice }) }; Expect = 'team_has_members' }
            @{ Case = 'the unit has a user (even disabled)'; Setup = { ($global:MdmWorld.Users | Where-Object systemuserid -eq $global:MdmIds.Disabled).bu = $global:MdmIds.DirectBu }; Expect = 'bu_has_users' }
            @{ Case = 'the team named is the default team'; Setup = { $script:TeamName = 'Direct Messages' }; Expect = 'team_is_default' }
            @{ Case = 'no team of that name'; Setup = { $script:TeamName = 'Nobody' }; Expect = 'team_not_found' }
            @{ Case = 'no unit of that name'; Setup = { $script:BuName = 'Nowhere' }; Expect = 'bu_not_found' }
            @{ Case = 'two units of that name'; Setup = { $global:MdmWorld.Units += [pscustomobject]@{ businessunitid = (MdmId 14); name = 'Direct Messages'; _parentbusinessunitid_value = $global:MdmIds.Root } }; Expect = 'bu_ambiguous' }
            @{ Case = 'a configured Secure Record unit that is not found'; Setup = { $script:Extra = @{ SecureRecordBusinessUnitName = 'Missing Secure' } }; Expect = 'secure_record_bu_not_found' }
        ) {
            param($Case, $Setup, $Expect)
            $script:BuName = 'Direct Messages'; $script:TeamName = 'Direct Owners'; $script:Extra = @{}
            Add-MdmThread $T1 $global:MdmIds.Alice $null @(@{ P = $global:MdmIds.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $global:MdmIds.Alice $global:MdmIds.Bob
            & $Setup
            $moveArgs = @{ Apply = $true; DirectMessageBusinessUnitName = $script:BuName; DirectMessageOwnerTeamName = $script:TeamName }
            foreach ($k in $script:Extra.Keys) { $moveArgs[$k] = $script:Extra[$k] }
            $r = Invoke-MdmMove $moveArgs
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match "REFUSED: the Direct team is not usable \($Expect"
            @($global:MdmWorld.Requests | Where-Object { $_ -match 'sprk_communications' }).Count | Should -Be 0
            $global:MdmWorld.Writes.Count | Should -Be 0
        }

        It 'must not fire: with no Secure Record unit and the default Secure names, the Direct team resolves' {
            $global:MdmWorld.Units = @($global:MdmWorld.Units | Where-Object name -ne 'Secure Record')
            $r = Invoke-MdmMove @{}
            $r.ExitCode | Should -Be 0
            $r.Output | Should -Match "Direct team $($global:MdmIds.DirectTeam)"
        }

        It '-Phase Move without the two Direct names throws before any read' {
            { & $script:Script -EnvironmentUrl $script:Url -Phase Move -BffApplicationIds $global:MdmBffAppId -ReviewPath (Join-Path ([IO.Path]::GetTempPath()) 'x.json') } |
                Should -Throw '*-DirectMessageBusinessUnitName and -DirectMessageOwnerTeamName*'
            $global:MdmWorld.Requests.Count | Should -Be 0
        }
    }
}
