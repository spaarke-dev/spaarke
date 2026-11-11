# Pester 5. Run: Invoke-Pester tests/scripts/Migrate-DirectMessagesToOwnerTeam.Tests.ps1
# CI: .github/workflows/ci-standalone-checks.yml, job owner-teams-script-tests (Pester 5.7.1, ubuntu-latest).
# field-mapping-server-write-path-r1 task 054c: scripts/Migrate-DirectMessagesToOwnerTeam.ps1 over a mocked Dataverse (an
# in-memory world behind a mocked Invoke-RestMethod and az; nothing talks to Dataverse).
# Pins: the precondition refusal (task 054b's field security, both phases); the Stamp selection; the pair rule (exactly two
# distinct, enabled, non-application systemusers including the sender, from the thread's user owner and its Read shares; a
# team share and a share without the Read bit are not participants); the Move assign invariant (no valid stamped pair, no
# move); the Direct team checks; the Assign-cascade refusal; the gate-record creator list (separate from the BFF list); the
# thread's own regarding lookups; a team-owned thread has no owner participant; the write-time re-read; a write that throws
# keeps the review list; OData literals are percent-encoded; the per-row stops; idempotence and -Verify; the Move -Apply
# acknowledgement (-DirectShareJobLive); the participants keep Read before anything of a message moves (owner-only grant,
# confirmed by read-back); the per-reason review counts.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Script = Join-Path $script:RepoRoot 'scripts' 'Migrate-DirectMessagesToOwnerTeam.ps1'
    $script:Url = 'https://example.crm.dynamics.com'
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) { function global:az { throw 'Test: az is never called unmocked.' } }

    function global:MdmId([int]$n) { '00000000-0000-4000-8000-{0:d12}' -f $n }
    $global:MdmBffAppId = 'aaaaaaaa-0000-4000-8000-000000000001'
    $global:MdmGateAppId = 'cccccccc-0000-4000-8000-000000000003'   # the ontology writer's identity: in neither the BFF list nor the writer profile
    $global:MdmIds = @{
        Bff = MdmId 1; Alice = MdmId 2; Bob = MdmId 3; Carol = MdmId 4; Dave = MdmId 5; AppUser = MdmId 6; Disabled = MdmId 7; GateWriter = MdmId 8; Disabled2 = MdmId 9
        Root = MdmId 10; SecureBu = MdmId 11; DirectBu = MdmId 12; CustomerBu = MdmId 13
        SecureTeam = MdmId 20; DirectTeam = MdmId 21; SomeTeam = MdmId 22; DirectDefault = MdmId 23
        Reader = MdmId 30; Writer = MdmId 31; Other = MdmId 32
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
            OtherWriterName = 'Someone Else'
            OtherWriterId   = $global:MdmIds.Other
            DuplicateProfile = $null   # a profile name returned twice
            ReaderMissingTeams = @()    # default team ids left off the reader profile
            WriterCanRead = 4; WriterCanUpdate = 4; ReaderCanRead = 4
            Cascades     = @{}          # table -> @(@{ Child; Attribute; Assign })
            PatchFault   = $null       # regex: a PATCH whose URI matches throws
            Users        = @(
                [pscustomobject]@{ systemuserid = $i.Bff; isdisabled = $false; applicationid = $global:MdmBffAppId; bu = $i.Root }
                [pscustomobject]@{ systemuserid = $i.Alice; isdisabled = $false; applicationid = $null; bu = $i.CustomerBu }
                [pscustomobject]@{ systemuserid = $i.Bob; isdisabled = $false; applicationid = $null; bu = $i.CustomerBu }
                [pscustomobject]@{ systemuserid = $i.Carol; isdisabled = $false; applicationid = $null; bu = $i.CustomerBu }
                [pscustomobject]@{ systemuserid = $i.Dave; isdisabled = $false; applicationid = $null; bu = $i.CustomerBu }
                [pscustomobject]@{ systemuserid = $i.AppUser; isdisabled = $false; applicationid = 'bbbbbbbb-0000-4000-8000-000000000002'; bu = $i.Root }
                [pscustomobject]@{ systemuserid = $i.Disabled; isdisabled = $true; applicationid = $null; bu = $i.CustomerBu }
                [pscustomobject]@{ systemuserid = $i.GateWriter; isdisabled = $false; applicationid = $global:MdmGateAppId; bu = $i.Root }
                [pscustomobject]@{ systemuserid = $i.Disabled2; isdisabled = $true; applicationid = $null; bu = $i.CustomerBu }
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
            GrantFault   = $false      # a GrantAccess POST throws
            GrantNoop    = $false      # a GrantAccess POST is accepted but leaves no Read share (the read-back lacks Read)
            Requests     = New-Object System.Collections.Generic.List[string]
            Writes       = New-Object System.Collections.Generic.List[object]
        }
    }

    function global:Add-MdmThread([string]$Id, [string]$OwnerUser, [string]$OwnerTeam, [object[]]$Shares = @(), [object]$IsDefault = $false, [int]$Type = 100000001, [string]$CreatedOn = '2026-01-01T00:00:00Z', [string]$Regarding) {
        $row = [pscustomobject]@{ sprk_communicationthreadid = $Id; sprk_threadtype = $Type; sprk_isdefaultthread = $IsDefault; _owninguser_value = $OwnerUser; _owningteam_value = $OwnerTeam; createdon = $CreatedOn }
        # The thread's own typed regarding lookups (no sprk_regardingreportcard: the thread table has no such column).
        foreach ($c in 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardinginvoice', 'sprk_regardingservicerequest', 'sprk_regardingworkassignment',
            'sprk_regardingevent', 'sprk_regardingbudget', 'sprk_regardinganalysis', 'sprk_regardingorganization', 'sprk_regardingaccount', 'sprk_regardingperson') {
            $row | Add-Member -NotePropertyName "_$($c)_value" -NotePropertyValue $(if ($Regarding -eq $c) { MdmId 998 } else { $null })
        }
        $global:MdmWorld.Threads += $row
        foreach ($s in $Shares) {
            $global:MdmWorld.Poa += [pscustomobject]@{ objectid = $Id; principalid = $s.P; principaltypecode = $(if ($s.Team) { 'team' } else { 'systemuser' }); accessrightsmask = $s.Mask; changedon = $(if ($s.ChangedOn) { $s.ChangedOn } else { $CreatedOn }) }
        }
    }
    function global:Add-MdmMessage([string]$Id, [string]$Thread, [string]$SentBy, [string]$Recipient, [string]$OwnerUser = $global:MdmIds.Alice, [string]$OwnerTeam, [int]$Type = 100000004, [string]$Regarding) {
        $row = [pscustomobject]@{
            sprk_communicationid = $Id; sprk_name = "FMSWP-$Id"; sprk_communicationtype = $Type; _sprk_communicationthread_value = $Thread
            _sprk_sentby_value = $SentBy; _sprk_directrecipient_value = $Recipient; _owninguser_value = $OwnerUser; _owningteam_value = $OwnerTeam
        }
        foreach ($c in 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardinginvoice', 'sprk_regardingservicerequest', 'sprk_regardingworkassignment',
            'sprk_regardingevent', 'sprk_regardingbudget', 'sprk_regardingreportcard', 'sprk_regardinganalysis', 'sprk_regardingorganization', 'sprk_regardingaccount', 'sprk_regardingperson') {
            $row | Add-Member -NotePropertyName "_$($c)_value" -NotePropertyValue $(if ($Regarding -eq $c) { MdmId 999 } else { $null })
        }
        $global:MdmWorld.Messages += $row
    }
    function global:Add-MdmContent([string]$Set, [string]$Id, [string]$Message, [string]$OwnerUser = $global:MdmIds.Alice, [string]$OwnerTeam, [string]$CreatedBy = $global:MdmIds.Bff, [string]$ConfirmedBy) {
        $global:MdmWorld.Content += [pscustomobject]@{ Set = $Set; Id = $Id; Message = $Message; _owninguser_value = $OwnerUser; _owningteam_value = $OwnerTeam; _createdby_value = $CreatedBy; _sprk_confirmedby_value = $ConfirmedBy }
        $global:MdmWorld.Poa += [pscustomobject]@{ objectid = $Id; principalid = $global:MdmIds.Bob; principaltypecode = 'systemuser'; accessrightsmask = 1; changedon = '2026-01-01T00:00:00Z' }
    }

    # What a server does with a request URL: the fragment never arrives, the query splits on '&', each value is decoded ('+' is a
    # space). A literal built without percent-encoding therefore breaks here the way it would break against Dataverse.
    function global:ConvertFrom-MdmUri([string]$u) {
        $q = $u.IndexOf('?')
        if ($q -lt 0) { return $u }
        $query = $u.Substring($q + 1)
        $h = $query.IndexOf('#'); if ($h -ge 0) { $query = $query.Substring(0, $h) }
        $parts = @($query -split '&' | ForEach-Object {
                $eq = $_.IndexOf('=')
                if ($eq -lt 0) { $_ } else { $_.Substring(0, $eq + 1) + [Uri]::UnescapeDataString($_.Substring($eq + 1).Replace('+', ' ')) }
            })
        return $u.Substring(0, $q + 1) + ($parts -join '&')
    }

    function global:Invoke-MdmGet([string]$u) {
        $w = $global:MdmWorld
        $u = ConvertFrom-MdmUri $u
        $wrap = { param($rows) [pscustomobject]@{ value = @($rows) } }
        if ($u -match '/organizations\?') { return & $wrap @($w.Org) }
        if ($u -match "EntityDefinitions\(LogicalName='([^']+)'\)/Attributes\(LogicalName='([^']+)'\)") {
            return [pscustomobject]@{ IsSecured = ($w.Unsecured -notcontains "$($Matches[1]).$($Matches[2])") }
        }
        if ($u -match "EntityDefinitions\(LogicalName='([^']+)'\)/OneToManyRelationships") {
            $table = $Matches[1]
            $rels = @([pscustomobject]@{ SchemaName = "x_$table"; ReferencingEntity = 'sprk_unrelated'; ReferencingAttribute = "sprk_$table"; CascadeConfiguration = [pscustomobject]@{ Assign = 'NoCascade' } })
            foreach ($c in @($w.Cascades[$table])) {
                if ($c) { $rels += [pscustomobject]@{ SchemaName = "c_$table"; ReferencingEntity = $c.Child; ReferencingAttribute = $c.Attribute; CascadeConfiguration = [pscustomobject]@{ Assign = $c.Assign } } }
            }
            return & $wrap $rels
        }
        if ($u -match "/fieldsecurityprofiles\?.*name eq '([^']+)'") {
            $name = $Matches[1]
            if ($name -eq 'Spaarke BFF-Managed Field Readers') {
                $defaults = @($w.Teams | Where-Object { $_.isdefault -and $w.ReaderMissingTeams -notcontains $_.teamid } | ForEach-Object { [pscustomobject]@{ teamid = $_.teamid; name = $_.name } })
                $one = [pscustomobject]@{ fieldsecurityprofileid = $global:MdmIds.Reader; name = $name; teamprofiles_association = $defaults; systemuserprofiles_association = @() }
            }
            else {
                $one = [pscustomobject]@{ fieldsecurityprofileid = $global:MdmIds.Writer; name = $name; teamprofiles_association = @($w.WriterTeams); systemuserprofiles_association = @($w.WriterUsers) }
            }
            if ($w.DuplicateProfile -eq $name) { return & $wrap @($one, $one) }
            return & $wrap @($one)
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
                [pscustomobject]@{ canread = $w.ReaderCanRead; cancreate = 0; canupdate = 0; _fieldsecurityprofileid_value = $global:MdmIds.Reader; fieldsecurityprofileid = [pscustomobject]@{ name = 'Spaarke BFF-Managed Field Readers' } }
                [pscustomobject]@{ canread = $w.WriterCanRead; cancreate = 4; canupdate = $w.WriterCanUpdate; _fieldsecurityprofileid_value = $global:MdmIds.Writer; fieldsecurityprofileid = [pscustomobject]@{ name = 'Spaarke BFF-Managed Field Writers' } }
            )
            if ($w.OtherWriter -eq $col) { $rows += [pscustomobject]@{ canread = 4; cancreate = 0; canupdate = 4; _fieldsecurityprofileid_value = $w.OtherWriterId; fieldsecurityprofileid = [pscustomobject]@{ name = $w.OtherWriterName } } }
            return & $wrap $rows
        }
        if ($u -match '/sprk_communicationthreads\?') { return & $wrap @($w.Threads | Where-Object { $_.sprk_threadtype -eq 100000001 }) }
        if ($u -match '/sprk_communicationthreads\(([0-9a-f-]+)\)') { $id = $Matches[1]; return $w.Threads | Where-Object { $_.sprk_communicationthreadid -eq $id } }
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
        if ($w.PatchFault -and $u -match $w.PatchFault) { throw "Test: simulated write fault on $u" }
        $w.Writes.Add([pscustomobject]@{ Uri = $u; Body = $b })
        if (-not ($u -match '/(\w+)\(([0-9a-f-]+)\)$')) { throw "Test: unexpected write $u" }
        $set = $Matches[1]; $id = $Matches[2]
        $row = if ($set -eq 'sprk_communications') { $w.Messages | Where-Object { $_.sprk_communicationid -eq $id } } else { $w.Content | Where-Object { $_.Id -eq $id } }
        if (-not $row) { throw "Test: write to a missing row $u" }
        if ($b.'sprk_DirectRecipient@odata.bind' -match '/systemusers\(([0-9a-f-]+)\)') { $row._sprk_directrecipient_value = $Matches[1] }
        if ($b.'ownerid@odata.bind' -match '/teams\(([0-9a-f-]+)\)') {
            if (-not $w.IgnoreAssign) { $row._owningteam_value = $Matches[1] }   # the user-owner column is left as it was (Dataverse returns it beside the team)
            if ($w.DropShareOnAssign) { $w.Poa = @($w.Poa | Where-Object { $_.objectid -ne $id }) }
        }
    }

    # The GrantAccess action: recorded in Writes (in order with the assigns), then a systemuser share with the Read bit lands on
    # the target unless GrantNoop is set; GrantFault throws before anything is recorded.
    function global:Invoke-MdmPost([string]$u, $Body) {
        $w = $global:MdmWorld
        if ($u -notmatch '/GrantAccess$') { throw "Test: unexpected action $u" }
        if ($w.GrantFault) { throw "Test: simulated GrantAccess fault on $u" }
        $b = [Text.Encoding]::UTF8.GetString([byte[]]$Body) | ConvertFrom-Json
        $w.Writes.Add([pscustomobject]@{ Uri = $u; Body = $b })
        if ($b.Target.'@odata.id' -notmatch '^sprk_communications\(([0-9a-f-]+)\)$') { throw "Test: GrantAccess on an unexpected target $($b.Target.'@odata.id')" }
        $target = $Matches[1]
        if ($b.PrincipalAccess.Principal.'@odata.id' -notmatch '^systemusers\(([0-9a-f-]+)\)$') { throw 'Test: GrantAccess to a principal that is not a systemuser' }
        $user = $Matches[1]
        if ($b.PrincipalAccess.AccessMask -ne 'ReadAccess') { throw "Test: GrantAccess with mask $($b.PrincipalAccess.AccessMask)" }
        if (-not $w.GrantNoop) { $w.Poa += [pscustomobject]@{ objectid = $target; principalid = $user; principaltypecode = 'systemuser'; accessrightsmask = 1; changedon = '2026-06-01T00:00:00Z' } }
    }
    # A systemuser share on a row (default: the Read bit).
    function global:Add-MdmShare([string]$Object, [string[]]$Users, [int]$Mask = 1) {
        foreach ($u in $Users) { $global:MdmWorld.Poa += [pscustomobject]@{ objectid = $Object; principalid = $u; principaltypecode = 'systemuser'; accessrightsmask = $Mask; changedon = '2026-01-01T00:00:00Z' } }
    }
    function global:WriteKinds() { @($global:MdmWorld.Writes | ForEach-Object { if ($_.Uri -match '/GrantAccess$') { 'grant' } else { ([regex]::Match($_.Uri, '/(\w+)\([0-9a-f-]+\)$')).Groups[1].Value } }) }

    # Runs the script; Output is everything it printed; Review is the review list it wrote (JSON).
    function global:Invoke-Mdm([hashtable]$Arguments) {
        $review = Join-Path ([IO.Path]::GetTempPath()) ("mdm-review-{0}.json" -f [guid]::NewGuid())
        $all = @{ EnvironmentUrl = $script:Url; BffApplicationIds = @($global:MdmBffAppId); ReviewPath = $review }
        foreach ($k in $Arguments.Keys) { $all[$k] = $Arguments[$k] }
        $global:LASTEXITCODE = 0
        $thrown = $null
        try { $output = & $script:Script @all 6>&1 2>&1 | Out-String } catch { $thrown = $_.Exception.Message; $output = '' }
        $code = $LASTEXITCODE
        $rows = if (Test-Path $review) { @(Get-Content -Raw $review | ConvertFrom-Json) } else { @() }
        Remove-Item $review -ErrorAction SilentlyContinue
        [pscustomobject]@{ ExitCode = $code; Output = $output; Review = @($rows | Where-Object { $_ }); Thrown = $thrown }
    }
    function global:Invoke-MdmMove([hashtable]$Extra = @{}) {
        # The acknowledgement is passed by default; the tests of the refusal call Invoke-Mdm (or pass DirectShareJobLive = $false).
        $a = @{ Phase = 'Move'; DirectMessageBusinessUnitName = 'Direct Messages'; DirectMessageOwnerTeamName = 'Direct Owners'; GateRecordWriterApplicationIds = @($global:MdmGateAppId); DirectShareJobLive = $true }
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
            if ($Method -eq 'Post') { return Invoke-MdmPost ([string]$Uri) $Body }
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

        It 'must fire: a profile NAMED System Administrator (not the built-in one) that can update a column refuses' {
            $global:MdmWorld.OtherWriter = 'sprk_communication.sprk_sentby'
            $global:MdmWorld.OtherWriterName = 'System Administrator'
            $r = Invoke-Mdm @{ Phase = 'Stamp' }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match "profile 'System Administrator' can also write sprk_communication.sprk_sentby"
        }

        It 'must not fire: the built-in System Administrator profile (its fixed id) may update a column' {
            $global:MdmWorld.OtherWriter = 'sprk_communication.sprk_sentby'
            $global:MdmWorld.OtherWriterName = 'System Administrator'
            $global:MdmWorld.OtherWriterId = '572329c1-a042-4e22-be47-367c6374ea45'
            $r = Invoke-Mdm @{ Phase = 'Stamp' }
            $r.ExitCode | Should -Be 0
            $r.Output | Should -Not -Match 'REFUSED'
        }

        It 'must fire: <Case>' -TestCases @(
            @{ Case = 'two profiles named like the writer profile'; Setup = { $global:MdmWorld.DuplicateProfile = 'Spaarke BFF-Managed Field Writers' }; Expect = "2 profile\(s\) named 'Spaarke BFF-Managed Field Writers'; exactly one is required" }
            @{ Case = 'two profiles named like the reader profile'; Setup = { $global:MdmWorld.DuplicateProfile = 'Spaarke BFF-Managed Field Readers' }; Expect = "2 profile\(s\) named 'Spaarke BFF-Managed Field Readers'; exactly one is required" }
            @{ Case = 'a business unit default team is missing from the reader profile'; Setup = { $global:MdmWorld.ReaderMissingTeams = @($global:MdmIds.SomeTeam) }; Expect = "default team 'Customer One' is NOT on the reader profile" }
            @{ Case = 'the writer profile cannot read a column'; Setup = { $global:MdmWorld.WriterCanRead = 0 }; Expect = 'writer profile does not read and update sprk_communication' }
            @{ Case = 'the writer profile cannot update a column'; Setup = { $global:MdmWorld.WriterCanUpdate = 0 }; Expect = 'writer profile does not read and update sprk_communication' }
            @{ Case = 'the reader profile cannot read a column'; Setup = { $global:MdmWorld.ReaderCanRead = 0 }; Expect = 'reader profile does not read sprk_communication' }
        ) {
            param($Case, $Setup, $Expect)
            & $Setup
            $r = Invoke-Mdm @{ Phase = 'Stamp' }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'REFUSED'
            $r.Output | Should -Match $Expect
            @($global:MdmWorld.Requests | Where-Object { $_ -match 'sprk_communications\?|sprk_communicationthreads\?' }).Count | Should -Be 0
        }

        It 'must fire: a precondition refusal under -Verify exits non-zero and selects nothing (<Phase>)' -TestCases @(
            @{ Phase = 'Stamp' }
            @{ Phase = 'Move' }
        ) {
            param($Phase)
            Add-MdmThread $T1 $global:MdmIds.Alice $null @(@{ P = $global:MdmIds.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $global:MdmIds.Alice $null
            $global:MdmWorld.Unsecured = @('sprk_communication.sprk_sentby')
            $r = if ($Phase -eq 'Move') { Invoke-MdmMove @{ Verify = $true } } else { Invoke-Mdm @{ Phase = 'Stamp'; Verify = $true } }
            $r.ExitCode | Should -Not -Be 0
            $r.Output | Should -Match 'REFUSED'
            @($global:MdmWorld.Requests | Where-Object { $_ -match 'sprk_communications|sprk_communicationthreads|businessunits' }).Count | Should -Be 0
            $global:MdmWorld.Writes.Count | Should -Be 0
        }

        It 'throws on an empty -BffApplicationIds (a list of separators) before any read' {
            $r = Invoke-Mdm @{ Phase = 'Stamp'; BffApplicationIds = @(',') }
            $r.Thrown | Should -Match '-BffApplicationIds names no application id'
            $global:MdmWorld.Requests.Count | Should -Be 0
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

        It 'a Direct-typed thread with a typed regarding lookup is not Direct: its messages go to review (<Column>)' -TestCases @(
            @{ Column = 'sprk_regardingmatter' }
            @{ Column = 'sprk_regardingperson' }
            @{ Column = 'sprk_regardingorganization' }
        ) {
            param($Column)
            Add-MdmThread $T1 $global:MdmIds.Alice $null @(@{ P = $global:MdmIds.Bob; Mask = 1 }) -Regarding $Column
            Add-MdmMessage $M1 $T1 $global:MdmIds.Alice $null
            $r = Invoke-Mdm @{ Phase = 'Stamp'; Apply = $true }
            $r.ExitCode | Should -Be 0
            ReasonOf $r $M1 | Should -Be 'thread_has_regarding'
            $r.Output | Should -Match 'PLAN: 0 row'
            $global:MdmWorld.Writes.Count | Should -Be 0
        }

        It 'selects the thread''s own regarding columns, never sprk_regardingreportcard (the thread table has none)' {
            Add-MdmThread $T1 $global:MdmIds.Alice $null @(@{ P = $global:MdmIds.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $global:MdmIds.Alice $null
            $r = Invoke-Mdm @{ Phase = 'Stamp' }
            $threadReads = @($global:MdmWorld.Requests | Where-Object { $_ -match '/sprk_communicationthreads\?' })
            $threadReads.Count | Should -Be 1
            $threadReads[0] | Should -Match '_sprk_regardingmatter_value'
            $threadReads[0] | Should -Match '_sprk_regardingperson_value'
            $threadReads[0] | Should -Not -Match 'reportcard'
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

        It 'a team-owned thread whose user-owner column is also set (as Dataverse returns it) has no owner participant: one user share is not a pair' {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $i.SomeTeam @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $null
            $r = Invoke-Mdm @{ Phase = 'Stamp'; Apply = $true }
            $r.ExitCode | Should -Be 0
            ReasonOf $r $M1 | Should -Be 'not_two_participants'
            ($global:MdmWorld.Messages | Where-Object sprk_communicationid -eq $M1)._sprk_directrecipient_value | Should -BeNullOrEmpty
            $global:MdmWorld.Writes.Count | Should -Be 0
        }

        It 'a team-owned thread with a stray user-owner column and two user Read shares: the shares are the pair' {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Carol $i.SomeTeam @(@{ P = $i.Alice; Mask = 1 }, @{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $null
            $r = Invoke-Mdm @{ Phase = 'Stamp'; Apply = $true }
            $r.ExitCode | Should -Be 0
            ($global:MdmWorld.Messages | Where-Object sprk_communicationid -eq $M1)._sprk_directrecipient_value | Should -Be $i.Bob
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

    Context 'Stamp: a write that throws keeps the review list' {
        It 'writes the review list gathered so far, then rethrows' {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage (MdmId 199) $T1 $null $null            # no sentby: review
            Add-MdmMessage $M1 $T1 $i.Alice $null                  # stamped
            Add-MdmMessage (MdmId 201) $T1 $i.Alice $null          # its PATCH throws
            $global:MdmWorld.PatchFault = "sprk_communications\($(MdmId 201)\)"
            $r = Invoke-Mdm @{ Phase = 'Stamp'; Apply = $true }
            $r.Thrown | Should -Match 'simulated write fault'
            @($r.Review).Count | Should -Be 1
            ReasonOf $r (MdmId 199) | Should -Be 'no_sentby'
            $global:MdmWorld.Writes.Count | Should -Be 1
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

        It 'a message that gained a typed regarding lookup since selection is not stamped' {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $null
            Mock Invoke-RestMethod -ParameterFilter { $Method -eq 'Get' -and [string]$Uri -match "sprk_communications\($($global:MdmM1)\)" } {
                [pscustomobject]@{ sprk_communicationid = $global:MdmM1; _sprk_directrecipient_value = $null; _sprk_sentby_value = $global:MdmIds.Alice; _sprk_communicationthread_value = $global:MdmT1; _sprk_regardingmatter_value = (MdmId 999) }
            }
            $r = Invoke-Mdm @{ Phase = 'Stamp'; Apply = $true }
            ReasonOf $r $M1 | Should -Be 'changed_since_selection'
            $global:MdmWorld.Writes.Count | Should -Be 0
        }

        It 'a thread that no longer qualifies since selection: its message is not stamped (<Case>)' -ForEach @(
            @{ Case = 'retyped'; Type = 100000000; IsDefault = $false; Regarding = $null; Expect = 'no longer Direct-typed' }
            @{ Case = 'now the master thread'; Type = 100000001; IsDefault = $true; Regarding = $null; Expect = 'per-user master thread' }
            @{ Case = 'gained a regarding lookup'; Type = 100000001; IsDefault = $false; Regarding = '00000000-0000-4000-8000-000000000998'; Expect = 'typed regarding lookup' }
        ) {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $null
            $global:MdmRecheck = @{ Type = $Type; IsDefault = $IsDefault; Regarding = $Regarding }
            Mock Invoke-RestMethod -ParameterFilter { $Method -eq 'Get' -and [string]$Uri -match "sprk_communicationthreads\($($global:MdmT1)\)" } {
                [pscustomobject]@{ sprk_communicationthreadid = $global:MdmT1; sprk_threadtype = $global:MdmRecheck.Type; sprk_isdefaultthread = $global:MdmRecheck.IsDefault; _sprk_regardingmatter_value = $global:MdmRecheck.Regarding }
            }
            $r = Invoke-Mdm @{ Phase = 'Stamp'; Apply = $true }
            ReasonOf $r $M1 | Should -Be 'changed_since_selection'
            @($r.Review | Where-Object { $_.MessageId -eq $M1 })[0].Detail | Should -Match $Expect
            $global:MdmWorld.Writes.Count | Should -Be 0
        }
    }

    Context 'Application ids are normalised before they reach a filter' {
        It 'a braced, upper-case id is sent as the plain dashed form (<Param>)' -ForEach @(
            @{ Param = 'BffApplicationIds' }
            @{ Param = 'GateRecordWriterApplicationIds' }
        ) {
            $raw = if ($Param -eq 'BffApplicationIds') { $global:MdmBffAppId } else { $global:MdmGateAppId }
            $r = Invoke-MdmMove @{ $Param = @("{$($raw.ToUpperInvariant())}") }
            $r.Thrown | Should -BeNullOrEmpty
            $r.ExitCode | Should -Be 0
            @($global:MdmWorld.Requests | Where-Object { $_ -match "applicationid eq $raw" }).Count | Should -BeGreaterThan 0
            @($global:MdmWorld.Requests | Where-Object { $_ -match 'applicationid eq [{(]' }).Count | Should -Be 0
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

    Context 'Move: the gate-record creator list' {
        It 'must fire: -Phase Move without -GateRecordWriterApplicationIds throws before any read' {
            $r = Invoke-Mdm @{ Phase = 'Move'; DirectMessageBusinessUnitName = 'Direct Messages'; DirectMessageOwnerTeamName = 'Direct Owners' }
            $r.Thrown | Should -Match '-GateRecordWriterApplicationIds'
            $global:MdmWorld.Requests.Count | Should -Be 0
        }

        It 'must fire: an empty list (separators only) throws before any read' {
            $r = Invoke-MdmMove @{ GateRecordWriterApplicationIds = @(',') }
            $r.Thrown | Should -Match '-GateRecordWriterApplicationIds'
            $global:MdmWorld.Requests.Count | Should -Be 0
        }

        It 'must fire: a gate-writer id with no application user refuses; nothing is selected' {
            $r = Invoke-MdmMove @{ GateRecordWriterApplicationIds = @('dddddddd-0000-4000-8000-000000000009'); Apply = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'REFUSED: the gate record writer is not usable \(gate_record_writer_not_found'
            @($global:MdmWorld.Requests | Where-Object { $_ -match 'sprk_communications\?|sprk_communicationthreads\?' }).Count | Should -Be 0
        }

        It 'must not fire: the gate writer is not on the writer profile and need not be (the lists are separate)' {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            Add-MdmShare $M1 @($i.Alice, $i.Bob)                    # both participants already hold Read on the message
            Add-MdmContent 'sprk_decisionrecords' (MdmId 300) $M1 -CreatedBy $i.GateWriter
            Add-MdmContent 'sprk_decisionrecords' (MdmId 301) $M1 -CreatedBy $i.Bff
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 0
            ($global:MdmWorld.Content | Where-Object Id -eq (MdmId 300))._owningteam_value | Should -Be $i.DirectTeam
            ($global:MdmWorld.Content | Where-Object Id -eq (MdmId 301))._owningteam_value | Should -BeNullOrEmpty
        }

        It 'a Decision Record created by an id in neither list is not moved' {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            Add-MdmContent 'sprk_decisionrecords' (MdmId 300) $M1 -CreatedBy $i.Carol
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 0
            ($global:MdmWorld.Content | Where-Object Id -eq (MdmId 300))._owningteam_value | Should -BeNullOrEmpty
            @($global:MdmWorld.Writes | Where-Object { $_.Uri -match [regex]::Escape((MdmId 300)) }).Count | Should -Be 0
        }
    }

    Context 'Move: the Assign cascade refusal' {
        BeforeEach {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            Add-MdmShare $M1 @($i.Alice, $i.Bob)                    # both participants already hold Read on the message
        }

        It 'must fire: <Case>' -TestCases @(
            @{ Case = 'sprk_communication cascades Assign to a child'; Table = 'sprk_communication'; Child = 'sprk_communicationattachment' }
            @{ Case = 'a content table (sprk_document) cascades Assign to a child'; Table = 'sprk_document'; Child = 'sprk_documentchild' }
            @{ Case = 'the gate Decision Record table cascades Assign'; Table = 'sprk_decisionrecord'; Child = 'sprk_decisionchild' }
        ) {
            param($Case, $Table, $Child)
            $global:MdmWorld.Cascades[$Table] = @(@{ Child = $Child; Attribute = 'sprk_parent'; Assign = 'Cascade' })
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match "REFUSED: an Assign cascade is not accepted \($Table->$Child\)"
            @($global:MdmWorld.Requests | Where-Object { $_ -match 'sprk_communications\?|sprk_communicationthreads\?' }).Count | Should -Be 0
            $global:MdmWorld.Writes.Count | Should -Be 0
        }

        It 'must fire under -Verify too (exit non-zero, nothing selected)' {
            $global:MdmWorld.Cascades['sprk_communication'] = @(@{ Child = 'sprk_x'; Attribute = 'sprk_parent'; Assign = 'Active' })
            $r = Invoke-MdmMove @{ Verify = $true }
            $r.ExitCode | Should -Be 1
            @($global:MdmWorld.Requests | Where-Object { $_ -match 'sprk_communications\?' }).Count | Should -Be 0
        }

        It 'must not fire: a cascade to a child the owner accepted proceeds' {
            $global:MdmWorld.Cascades['sprk_communication'] = @(@{ Child = 'sprk_communicationattachment'; Attribute = 'sprk_communication'; Assign = 'Cascade' })
            $r = Invoke-MdmMove @{ Apply = $true; AcceptedAssignCascade = @('sprk_communicationattachment') }
            $r.ExitCode | Should -Be 0
            $r.Output | Should -Match 'ACCEPTED'
            ($global:MdmWorld.Messages | Where-Object sprk_communicationid -eq $M1)._owningteam_value | Should -Be $global:MdmIds.DirectTeam
        }

        It 'must not fire: no Assign cascade (only NoCascade relationships) proceeds' {
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 0
            $r.Output | Should -Match 'Assign-cascading relationships from the tables this script assigns: 0'
        }
    }

    Context 'Move: the thread''s own regarding lookups' {
        It 'a Direct-typed thread with a typed regarding lookup is not Direct: nothing of its messages moves, and the message is listed' {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 }) -Regarding 'sprk_regardingmatter'
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            Add-MdmContent 'sprk_documents' (MdmId 300) $M1
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 0
            $global:MdmWorld.Writes.Count | Should -Be 0
            ReasonOf $r $M1 | Should -Be 'thread_has_regarding'
            (Invoke-MdmMove @{ Verify = $true }).ExitCode | Should -Be 0
        }
    }

    Context 'Move: the write-time re-read' {
        BeforeEach {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            Add-MdmShare $M1 @($i.Alice, $i.Bob)                    # both participants already hold Read on the message
            Add-MdmContent 'sprk_documents' (MdmId 300) $M1
        }

        It 'a message that became Secure-owned since selection moves nothing of it' {
            Mock Invoke-RestMethod -ParameterFilter { $Method -eq 'Get' -and [string]$Uri -match "sprk_communications\($($global:MdmM1)\)" } {
                [pscustomobject]@{ sprk_communicationid = $global:MdmM1; sprk_name = 'FMSWP-x'; _sprk_communicationthread_value = $global:MdmT1; _sprk_sentby_value = $global:MdmIds.Alice; _sprk_directrecipient_value = $global:MdmIds.Bob; _owninguser_value = $null; _owningteam_value = $global:MdmIds.SecureTeam }
            }
            $r = Invoke-MdmMove @{ Apply = $true }
            $global:MdmWorld.Writes.Count | Should -Be 0
            ReasonOf $r $M1 | Should -Be 'changed_since_selection'
        }

        It 'a message that gained a typed regarding lookup since selection moves nothing of it' {
            Mock Invoke-RestMethod -ParameterFilter { $Method -eq 'Get' -and [string]$Uri -match "sprk_communications\($($global:MdmM1)\)" } {
                [pscustomobject]@{ sprk_communicationid = $global:MdmM1; sprk_name = 'FMSWP-x'; _sprk_communicationthread_value = $global:MdmT1; _sprk_sentby_value = $global:MdmIds.Alice; _sprk_directrecipient_value = $global:MdmIds.Bob; _owninguser_value = $global:MdmIds.Alice; _owningteam_value = $null; _sprk_regardingmatter_value = (MdmId 999) }
            }
            $r = Invoke-MdmMove @{ Apply = $true }
            $global:MdmWorld.Writes.Count | Should -Be 0
            ReasonOf $r $M1 | Should -Be 'changed_since_selection'
        }

        It 'a thread that gained a typed regarding lookup since selection moves nothing of its message' {
            Mock Invoke-RestMethod -ParameterFilter { $Method -eq 'Get' -and [string]$Uri -match "sprk_communicationthreads\($($global:MdmT1)\)" } {
                [pscustomobject]@{ sprk_communicationthreadid = $global:MdmT1; sprk_threadtype = 100000001; sprk_isdefaultthread = $false; _sprk_regardingmatter_value = (MdmId 998) }
            }
            $r = Invoke-MdmMove @{ Apply = $true }
            $global:MdmWorld.Writes.Count | Should -Be 0
            ReasonOf $r $M1 | Should -Be 'changed_since_selection'
        }

        It 'a thread that is no longer Direct-typed since selection moves nothing of its message (<Case>)' -ForEach @(
            @{ Case = 'retyped'; Type = 100000000; IsDefault = $false; Expect = 'no longer Direct-typed' }
            @{ Case = 'now the master thread'; Type = 100000001; IsDefault = $true; Expect = 'per-user master thread' }
            @{ Case = 'flag unread'; Type = 100000001; IsDefault = $null; Expect = 'per-user master thread' }
        ) {
            $global:MdmRecheck = @{ Type = $Type; IsDefault = $IsDefault }
            Mock Invoke-RestMethod -ParameterFilter { $Method -eq 'Get' -and [string]$Uri -match "sprk_communicationthreads\($($global:MdmT1)\)" } {
                [pscustomobject]@{ sprk_communicationthreadid = $global:MdmT1; sprk_threadtype = $global:MdmRecheck.Type; sprk_isdefaultthread = $global:MdmRecheck.IsDefault }
            }
            $r = Invoke-MdmMove @{ Apply = $true }
            $global:MdmWorld.Writes.Count | Should -Be 0
            ReasonOf $r $M1 | Should -Be 'changed_since_selection'
            @($r.Review | Where-Object { $_.MessageId -eq $M1 })[0].Detail | Should -Match $Expect
        }

        It 'must not fire: an unchanged message and thread move as planned' {
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 0
            $global:MdmWorld.Writes.Count | Should -Be 2
        }
    }

    Context 'Move: a write that throws keeps the review list' {
        It 'writes the review list gathered so far, then rethrows' {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            Add-MdmShare $M1 @($i.Alice, $i.Bob)                    # both participants already hold Read on the message
            Add-MdmContent 'sprk_documents' (MdmId 300) $M1
            Add-MdmMessage (MdmId 201) $T1 $i.Alice $null          # no valid pair: review
            $global:MdmWorld.PatchFault = "sprk_documents\($(MdmId 300)\)"
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.Thrown | Should -Match 'simulated write fault'
            @($r.Review).Count | Should -Be 1
            ReasonOf $r (MdmId 201) | Should -Be 'no_valid_pair'
        }
    }

    Context 'OData literals' {
        It 'a unit and a team name with &, #, + and spaces are percent-encoded and resolve' {
            ($global:MdmWorld.Units | Where-Object name -eq 'Direct Messages').name = 'Legal & Co #1+2'
            ($global:MdmWorld.Teams | Where-Object name -eq 'Direct Owners').name = 'Direct+Owners & Co #2'
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            Add-MdmShare $M1 @($i.Alice, $i.Bob)                    # both participants already hold Read on the message
            $r = Invoke-MdmMove @{ Apply = $true; DirectMessageBusinessUnitName = 'Legal & Co #1+2'; DirectMessageOwnerTeamName = 'Direct+Owners & Co #2' }
            $r.ExitCode | Should -Be 0
            $r.Output | Should -Match "Direct team $($i.DirectTeam)"
            ($global:MdmWorld.Messages | Where-Object sprk_communicationid -eq $M1)._owningteam_value | Should -Be $i.DirectTeam
            $unitReads = @($global:MdmWorld.Requests | Where-Object { $_ -match '/businessunits\?' -and $_ -match 'Legal' })
            $unitReads.Count | Should -BeGreaterThan 0
            $unitReads[0] | Should -Match 'Legal( |%20)%26( |%20)Co( |%20)%231%2B2'
        }
    }

    Context 'Move: apply, content rows, idempotence, verify' {
        It 'moves the content rows then the message; skips Direct- and Secure-owned rows and a non-gate Decision Record' {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            Add-MdmShare $M1 @($i.Alice, $i.Bob)                    # both participants already hold Read on the message
            Add-MdmContent 'sprk_communicationattachments' (MdmId 300) $M1
            Add-MdmContent 'sprk_communicationparticipants' (MdmId 301) $M1
            Add-MdmContent 'sprk_emailreviewlogs' (MdmId 302) $M1
            Add-MdmContent 'sprk_documents' (MdmId 303) $M1
            Add-MdmContent 'sprk_decisionrecords' (MdmId 304) $M1 -CreatedBy $i.GateWriter          # gate record (the gate writer's, unconfirmed)
            Add-MdmContent 'sprk_decisionrecords' (MdmId 305) $M1 -CreatedBy $i.Alice               # not a gate record
            Add-MdmContent 'sprk_decisionrecords' (MdmId 309) $M1 -CreatedBy $i.Bff                 # the BFF's own: not a gate record
            Add-MdmContent 'sprk_decisionrecords' (MdmId 310) $M1 -CreatedBy $i.Carol               # a creator in neither list
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
            foreach ($n in 309, 310) { ($global:MdmWorld.Content | Where-Object Id -eq (MdmId $n))._owningteam_value | Should -BeNullOrEmpty }
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
            Add-MdmShare $M1 @($i.Alice, $i.Bob)                    # both participants already hold Read on the message
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

    Context 'Move: the -DirectShareJobLive acknowledgement' {
        BeforeEach {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            Add-MdmShare $M1 @($i.Alice, $i.Bob)
        }

        It 'must fire: Move -Apply without -DirectShareJobLive refuses before any selection or write' {
            $r = Invoke-MdmMove @{ Apply = $true; DirectShareJobLive = $false }
            $r.ExitCode | Should -Be 1
            $r.Output | Should -Match 'REFUSED: -Phase Move -Apply needs -DirectShareJobLive'
            @($global:MdmWorld.Requests | Where-Object { $_ -match 'sprk_communications\?' }).Count | Should -Be 0
            $global:MdmWorld.Writes.Count | Should -Be 0
            ($global:MdmWorld.Messages | Where-Object sprk_communicationid -eq $M1)._owningteam_value | Should -BeNullOrEmpty
        }

        It 'must not fire: -DryRun and -Verify do not need it (<Mode>)' -ForEach @(
            @{ Mode = 'DryRun'; Code = 0 }
            @{ Mode = 'Verify'; Code = 1 }   # the message is not on the Direct team yet: the gate runs and fails on that
        ) {
            $r = Invoke-MdmMove @{ $Mode = $true; DirectShareJobLive = $false }
            $r.ExitCode | Should -Be $Code
            $r.Output | Should -Not -Match 'REFUSED'
            @($global:MdmWorld.Requests | Where-Object { $_ -match 'sprk_communications\?' }).Count | Should -BeGreaterThan 0
            $global:MdmWorld.Writes.Count | Should -Be 0
        }

        It 'must not fire: -Phase Stamp -Apply does not need it' {
            $r = Invoke-Mdm @{ Phase = 'Stamp'; Apply = $true }
            $r.ExitCode | Should -Be 0
            $r.Output | Should -Not -Match 'DirectShareJobLive'
        }
    }

    Context 'Move: the participants keep Read before anything of a message moves' {
        BeforeEach {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
        }

        It 'must fire: the sender owns the message and has no share -> GrantAccess (Read) before any assign, confirmed, then the rows move' {
            $i = $global:MdmIds
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob                 # owned by Alice (the sender)
            Add-MdmShare $M1 @($i.Bob)                             # the recipient already reads it
            Add-MdmContent 'sprk_documents' (MdmId 300) $M1
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 0
            WriteKinds | Should -Be @('grant', 'sprk_documents', 'sprk_communications')
            $g = $global:MdmWorld.Writes[0].Body
            $g.Target.'@odata.id' | Should -Be "sprk_communications($M1)"
            $g.PrincipalAccess.Principal.'@odata.id' | Should -Be "systemusers($($i.Alice))"
            $g.PrincipalAccess.AccessMask | Should -Be 'ReadAccess'
            $r.Output | Should -Match "granted Read to $($i.Alice) on message $M1 .*confirmed by read-back"
            ($global:MdmWorld.Messages | Where-Object sprk_communicationid -eq $M1)._owningteam_value | Should -Be $i.DirectTeam
            $r.Review.Count | Should -Be 0
        }

        It 'must not fire: a participant who already holds a Read share gets no second grant' {
            $i = $global:MdmIds
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            Add-MdmShare $M1 @($i.Alice)
            Add-MdmShare $M1 @($i.Bob) -Mask 3                      # Read + Write: the Read bit is held
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 0
            WriteKinds | Should -Be @('sprk_communications')
        }

        It 'must fire: the recipient is not the owner and holds no share -> no grant, nothing of the message moves (participants_not_readable)' {
            $i = $global:MdmIds
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            Add-MdmShare $M1 @($i.Alice)
            Add-MdmContent 'sprk_documents' (MdmId 300) $M1
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 0
            $global:MdmWorld.Writes.Count | Should -Be 0
            ReasonOf $r $M1 | Should -Be 'participants_not_readable'
            ($global:MdmWorld.Messages | Where-Object sprk_communicationid -eq $M1)._owninguser_value | Should -Be $i.Alice
            ($global:MdmWorld.Messages | Where-Object sprk_communicationid -eq $M1)._owningteam_value | Should -BeNullOrEmpty
            ($global:MdmWorld.Content | Where-Object Id -eq (MdmId 300))._owningteam_value | Should -BeNullOrEmpty
        }

        It 'must fire: the owner is granted but the read-back lacks Read -> nothing moves (participant_grant_unconfirmed)' {
            $i = $global:MdmIds
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            Add-MdmShare $M1 @($i.Bob)
            Add-MdmContent 'sprk_documents' (MdmId 300) $M1
            $global:MdmWorld.GrantNoop = $true
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 0
            WriteKinds | Should -Be @('grant')
            ReasonOf $r $M1 | Should -Be 'participant_grant_unconfirmed'
            @($r.Review | Where-Object { $_.MessageId -eq $M1 })[0].Detail | Should -Match 'no Read share'
            ($global:MdmWorld.Messages | Where-Object sprk_communicationid -eq $M1)._owningteam_value | Should -BeNullOrEmpty
            ($global:MdmWorld.Content | Where-Object Id -eq (MdmId 300))._owningteam_value | Should -BeNullOrEmpty
        }

        It 'must fire: the grant throws -> nothing moves, listed, and the run goes on to the next message' {
            $i = $global:MdmIds
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            Add-MdmShare $M1 @($i.Bob)
            Add-MdmMessage (MdmId 201) $T1 $i.Alice $i.Bob
            Add-MdmShare (MdmId 201) @($i.Alice, $i.Bob)
            $global:MdmWorld.GrantFault = $true
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.Thrown | Should -BeNullOrEmpty
            $r.ExitCode | Should -Be 0
            ReasonOf $r $M1 | Should -Be 'participant_grant_unconfirmed'
            @($r.Review | Where-Object { $_.MessageId -eq $M1 })[0].Detail | Should -Match 'threw'
            ($global:MdmWorld.Messages | Where-Object sprk_communicationid -eq $M1)._owningteam_value | Should -BeNullOrEmpty
            ($global:MdmWorld.Messages | Where-Object sprk_communicationid -eq (MdmId 201))._owningteam_value | Should -Be $i.DirectTeam
        }

        It 'must fire: a participant holding a share without the Read bit -> no grant, no assign (participant_share_without_read)' {
            $i = $global:MdmIds
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            Add-MdmShare $M1 @($i.Bob)
            Add-MdmShare $M1 @($i.Alice) -Mask 2                    # Write only: never upgraded here
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 0
            $global:MdmWorld.Writes.Count | Should -Be 0
            ReasonOf $r $M1 | Should -Be 'participant_share_without_read'
        }

        It 'must not fire: one participant disabled, the other readable -> moves (the disabled one is skipped)' {
            $i = $global:MdmIds
            Add-MdmMessage $M1 $T1 $i.Alice $i.Disabled
            Add-MdmShare $M1 @($i.Alice)
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 0
            WriteKinds | Should -Be @('sprk_communications')
            ($global:MdmWorld.Messages | Where-Object sprk_communicationid -eq $M1)._owningteam_value | Should -Be $i.DirectTeam
        }

        It 'must fire: both participants disabled -> nothing moves (participants_not_readable)' {
            $i = $global:MdmIds
            Add-MdmMessage $M1 $T1 $i.Disabled $i.Disabled2 -OwnerUser $i.Disabled
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.ExitCode | Should -Be 0
            $global:MdmWorld.Writes.Count | Should -Be 0
            ReasonOf $r $M1 | Should -Be 'participants_not_readable'
        }

        It 'must fire: the message''s share read faults -> nothing moves (read_fault)' {
            $i = $global:MdmIds
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            Add-MdmShare $M1 @($i.Alice, $i.Bob)
            Add-MdmContent 'sprk_documents' (MdmId 300) $M1
            $global:MdmWorld.Faults = @("principalobjectaccessset.*objectid eq $M1")
            $r = Invoke-MdmMove @{ Apply = $true }
            $r.Thrown | Should -BeNullOrEmpty
            $global:MdmWorld.Writes.Count | Should -Be 0
            ReasonOf $r $M1 | Should -Be 'read_fault'
        }

        It '-DryRun prints the owner grant it would make and the rows that stay, and writes nothing' {
            $i = $global:MdmIds
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob
            Add-MdmShare $M1 @($i.Bob)
            Add-MdmMessage (MdmId 201) $T1 $i.Alice $i.Bob          # Bob holds no share and does not own it
            $r = Invoke-MdmMove @{}
            $r.ExitCode | Should -Be 0
            $r.Output | Should -Match "WOULD grant Read to systemuser $($i.Alice) on message $M1"
            $r.Output | Should -Match "would move nothing of message $(MdmId 201): participants_not_readable"
            ReasonOf $r (MdmId 201) | Should -Be 'participants_not_readable'
            $global:MdmWorld.Writes.Count | Should -Be 0
            @($global:MdmWorld.Requests | Where-Object { $_ -notmatch '^Get ' }).Count | Should -Be 0
        }

        It '-Verify: a Direct-owned message whose enabled participant lacks Read fails (participant_without_read); with both readers it passes' {
            $i = $global:MdmIds
            Add-MdmMessage $M1 $T1 $i.Alice $i.Bob -OwnerTeam $i.DirectTeam
            Add-MdmShare $M1 @($i.Alice)
            $v = Invoke-MdmMove @{ Verify = $true }
            $v.ExitCode | Should -Be 1
            ReasonOf $v $M1 | Should -Be 'participant_without_read'
            $v.Output | Should -Match "VERIFY FAIL: message $M1 is on the Direct team and $($i.Bob) holds no Read share"
            Add-MdmShare $M1 @($i.Bob)
            $ok = Invoke-MdmMove @{ Verify = $true }
            $ok.ExitCode | Should -Be 0
            $ok.Review.Count | Should -Be 0
            $global:MdmWorld.Writes.Count | Should -Be 0
        }

        It '-Verify: a disabled participant without Read does not fail the gate' {
            $i = $global:MdmIds
            Add-MdmMessage $M1 $T1 $i.Alice $i.Disabled -OwnerTeam $i.DirectTeam
            Add-MdmShare $M1 @($i.Alice)
            (Invoke-MdmMove @{ Verify = $true }).ExitCode | Should -Be 0
        }
    }

    Context 'The review list prints a count per reason' {
        It 'Move dry run, Apply and Verify each print a "review:" line with each reason and its count' {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $i.Alice $null                   # no_valid_pair
            Add-MdmMessage (MdmId 201) $T1 $i.Alice $i.Alice       # no_valid_pair
            Add-MdmMessage (MdmId 202) $T1 $i.Alice $i.Bob         # participants_not_readable (Bob: no share, not the owner)
            foreach ($mode in @{}, @{ Apply = $true }) {
                $r = Invoke-MdmMove $mode
                $r.ExitCode | Should -Be 0
                $r.Output | Should -Match 'review: no_valid_pair 2, participants_not_readable 1'
            }
            $v = Invoke-MdmMove @{ Verify = $true }
            $v.Output | Should -Match 'review: no_valid_pair 2(\r?\n|$)'
        }

        It 'Stamp prints the count per reason, and "none" when nothing is listed' {
            $i = $global:MdmIds
            Add-MdmThread $T1 $i.Alice $null @(@{ P = $i.Bob; Mask = 1 })
            Add-MdmMessage $M1 $T1 $null $null                      # no_sentby
            Add-MdmMessage (MdmId 201) $T1 $null $null              # no_sentby
            Add-MdmThread (MdmId 101) $i.Alice $null @()
            Add-MdmMessage (MdmId 202) (MdmId 101) $i.Alice $null   # not_two_participants
            (Invoke-Mdm @{ Phase = 'Stamp' }).Output | Should -Match 'review: no_sentby 2, not_two_participants 1'
            $global:MdmWorld.Messages = @()
            (Invoke-Mdm @{ Phase = 'Stamp' }).Output | Should -Match 'review: none'
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
            $r.Output | Should -Match 'shares 2 -> 2'   # Bob's Read share plus the owner's confirmed Read grant
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
            { & $script:Script -EnvironmentUrl $script:Url -Phase Move -BffApplicationIds $global:MdmBffAppId -GateRecordWriterApplicationIds $global:MdmGateAppId -ReviewPath (Join-Path ([IO.Path]::GetTempPath()) 'x.json') } |
                Should -Throw '*-DirectMessageBusinessUnitName and -DirectMessageOwnerTeamName*'
            $global:MdmWorld.Requests.Count | Should -Be 0
        }
    }
}
