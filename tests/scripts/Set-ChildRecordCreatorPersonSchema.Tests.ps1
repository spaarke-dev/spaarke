# Pester 5. Run: Invoke-Pester tests/scripts/Set-ChildRecordCreatorPersonSchema.Tests.ps1
# Ontology task 147 (D-135 Q27, #1777): the child creator-person schema script accepts the maker's relationship-name form
# (`{table}_CreatedByPerson_systemuser`) as the SAME relationship it creates under its own name
# (`sprk_systemuser_{table}_createdbyperson`), instead of reporting "created another way".
# Task 147 round 2 (D-136, review F3): the narrow profile "Spaarke Ontology Writer Field Writers" is created and checked, the BFF
# writers rule is unchanged, and a $SolutionHomes table's missing column is never created by -Apply.
# Task 142 (#1783 / D-135): sprk_decisionrecord joins $Tables and $SolutionHomes exactly as the budget revision did.
# Task 143 (#1783 / D-135): sprk_signal joins $Tables and $SolutionHomes exactly as the Decision Record did.
# ebilling-r1 task 081 (model 13, O-23): sprk_procedure and sprk_procedureversion join $Tables with SpaarkeMaster as their only home, and
# their column (created unsecured by 080) is secured only after the script finds it holds no value.
# uac-r2 review and UAC #2276: the empty-before-securing gate covers EVERY table, and -Apply counts again after securing (a race FAILs).
#
# The script talks to a live environment on load, so it is never run here. The relationship-name functions are read out of
# the script's own AST and exercised against a fake reader: the code under test is the shipped code, not a copy.

BeforeAll {
    $script:ScriptPath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path 'scripts' 'Set-ChildRecordCreatorPersonSchema.ps1'
    $errors = $null
    $script:Ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$null, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }

    # Define exactly the functions the relationship check is made of, from the script itself.
    foreach ($name in 'RelationshipSchemaName', 'Get-CreatorPersonRelationshipNames', 'Find-CreatorPersonRelationship') {
        $fn = $script:Ast.Find({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true)
        if (-not $fn) { throw "The script no longer defines $name" }
        . ([scriptblock]::Create($fn.Extent.Text))
    }

    # A fake relationship reader: knows the named relationships, records every name asked for.
    function Get-FakeReader([string[]]$Existing) {
        $asked = [System.Collections.Generic.List[string]]::new()
        $read = { param($name) $asked.Add($name); if ($Existing -contains $name) { [pscustomobject]@{ SchemaName = $name } } else { $null } }.GetNewClosure()
        [pscustomobject]@{ Read = $read; Asked = $asked }
    }
}

Describe 'Find-CreatorPersonRelationship' {
    It 'accepts the maker form sprk_budgetrevision_CreatedByPerson_systemuser (not "created another way")' {
        $reader = Get-FakeReader @('sprk_budgetrevision_CreatedByPerson_systemuser')
        $rel = Find-CreatorPersonRelationship 'sprk_budgetrevision' $reader.Read
        $rel | Should -Not -BeNullOrEmpty
        $rel.SchemaName | Should -Be 'sprk_budgetrevision_CreatedByPerson_systemuser'
    }

    It 'prefers the form the script creates, and stops there' {
        $reader = Get-FakeReader @('sprk_systemuser_sprk_todo_createdbyperson', 'sprk_todo_CreatedByPerson_systemuser')
        $rel = Find-CreatorPersonRelationship 'sprk_todo' $reader.Read
        $rel.SchemaName | Should -Be 'sprk_systemuser_sprk_todo_createdbyperson'
        $reader.Asked.Count | Should -Be 1
    }

    It 'still answers nothing for a name it does not know, so the script reports it for a hand check' {
        $reader = Get-FakeReader @('sprk_budgetrevision_SomeOtherName_systemuser')
        Find-CreatorPersonRelationship 'sprk_budgetrevision' $reader.Read | Should -BeNullOrEmpty
        $reader.Asked | Should -Be @('sprk_systemuser_sprk_budgetrevision_createdbyperson', 'sprk_budgetrevision_CreatedByPerson_systemuser')
    }

    It 'does not accept another table''s relationship' {
        $reader = Get-FakeReader @('sprk_signal_CreatedByPerson_systemuser')
        Find-CreatorPersonRelationship 'sprk_budgetrevision' $reader.Read | Should -BeNullOrEmpty
    }
}

Describe 'the script''s table and solution wiring' {
    It 'lists sprk_budgetrevision in $Tables and checks its column against the solutions that carry it, not SpaarkeCore' {
        $text = Get-Content -Raw $script:ScriptPath
        $text | Should -Match '(?m)^\$Tables = @\([^)]*''sprk_budgetrevision''[^)]*\)'
        $homes = [regex]::Match($text, '(?ms)^\$SolutionHomes = @\{(.*?)^\}').Groups[1].Value
        $homes | Should -Match '(?m)^\s+sprk_budgetrevision = @\(''SpaarkeMaster'', ''OntologyPlatformSolution''\)'
    }

    It '142: lists sprk_decisionrecord in $Tables and gives it the same solution homes as the budget revision' {
        $text = Get-Content -Raw $script:ScriptPath
        $text | Should -Match '(?m)^\$Tables = @\([^)]*''sprk_decisionrecord''[^)]*\)'
        $homes = [regex]::Match($text, '(?ms)^\$SolutionHomes = @\{(.*?)^\}').Groups[1].Value
        $homes | Should -Match '(?m)^\s+sprk_decisionrecord = @\(''SpaarkeMaster'', ''OntologyPlatformSolution''\)'
    }

    It '143: lists sprk_signal in $Tables and gives it the same solution homes as the Decision Record' {
        $text = Get-Content -Raw $script:ScriptPath
        $text | Should -Match '(?m)^\$Tables = @\([^)]*''sprk_signal''[^)]*\)'
        $homes = [regex]::Match($text, '(?ms)^\$SolutionHomes = @\{(.*?)^\}').Groups[1].Value
        $homes | Should -Match '(?m)^\s+sprk_signal\s+= @\(''SpaarkeMaster'', ''OntologyPlatformSolution''\)'
        # exactly the three ontology tables, the four eBilling tables (ebilling-r1 task 013), the two Procedure tables and the two
        # policy tables (task 081), and the rule set and Procedure assignment (task 090) have solution homes; every other child's
        # column stays in SpaarkeCore
        @([regex]::Matches($homes, '(?m)^\s+(sprk_\w+)\s+=') | ForEach-Object { $_.Groups[1].Value } | Sort-Object) |
            Should -Be @('sprk_budgetrevision', 'sprk_decisionrecord', 'sprk_externalidentifier', 'sprk_invoiceapproval',
                'sprk_invoicecorrection', 'sprk_invoicesubmission', 'sprk_policy', 'sprk_policyversion', 'sprk_procedure',
                'sprk_procedureassignment', 'sprk_procedureversion', 'sprk_ruleset', 'sprk_signal')
    }

    It 'ebilling-r1 081: lists the Procedure and policy tables in $Tables with SpaarkeMaster as their only home; the empty-before-securing gate covers every table (UAC #2276)' {
        $text = Get-Content -Raw $script:ScriptPath
        $homes = [regex]::Match($text, '(?ms)^\$SolutionHomes = @\{(.*?)^\}').Groups[1].Value
        # ebilling-r1 task 090 (UAC answer to 081 Q5): the rule set and the Procedure assignment, created unsecured by 080 like these four.
        foreach ($t in 'sprk_procedure', 'sprk_procedureversion', 'sprk_policy', 'sprk_policyversion', 'sprk_ruleset', 'sprk_procedureassignment') {
            $text | Should -Match "(?m)^\`$Tables = @\([^)]*'$t'[^)]*\)"
            $homes | Should -Match "(?m)^\s+$t\s+= @\('SpaarkeMaster'\)"
        }
        # UAC #2276: the empty-before-securing gate is universal, not a list of tables. Every unsecured column is counted before it is
        # secured, in (c) and in (c2).
        $text | Should -Not -Match 'AssertEmptyBeforeSecuring'
        ([regex]::Matches($text, '(?m)^\s*if \(\$(attrs\[\$t\]|a)\.IsSecured -ne \$Secured -and -not \(Test-ColumnEmptyBeforeSecuring \$t\)\) \{ (\$gatedOut\[\$t\] = \$true; )?continue \}\s*$')).Count | Should -Be 2
        ([regex]::Matches($text, 'Set-ColumnSecured \$t; Assert-ColumnStillEmptyAfterSecuring \$t')).Count | Should -Be 2
    }

    It 'ebilling-r1 013: lists the four eBilling tables in $Tables with SpaarkeMaster as their only solution home' {
        $text = Get-Content -Raw $script:ScriptPath
        $homes = [regex]::Match($text, '(?ms)^\$SolutionHomes = @\{(.*?)^\}').Groups[1].Value
        foreach ($t in 'sprk_invoicesubmission', 'sprk_invoiceapproval', 'sprk_externalidentifier', 'sprk_invoicecorrection') {
            $text | Should -Match "(?m)^\`$Tables = @\([^)]*'$t'[^)]*\)"
            $homes | Should -Match "(?m)^\s+$t\s+= @\('SpaarkeMaster'\)"
        }
    }

    It 'D-136: the narrow profile covers exactly sprk_budgetrevision, sprk_decisionrecord and sprk_signal' {
        $text = Get-Content -Raw $script:ScriptPath
        $text | Should -Match '(?m)^\$OntologyWriterTables = @\(''sprk_budgetrevision'', ''sprk_decisionrecord'', ''sprk_signal''\)\s*$'
        $text | Should -Match '(?m)^\$OntologyWriterProfileName = ''Spaarke Ontology Writer Field Writers''\s*$'
    }
}

# A whole run against a simulated Dataverse (az and Invoke-RestMethod shadowed; nothing leaves the process, every write is
# recorded and not performed). It proves the relationship-name form, the solution-home check, the D-136 profile and the F3
# refusal work in the script as shipped, not only in the extracted functions -- the run is what found that an empty list
# assigned through `$x = if (...)` unrolls to $null.
Describe 'a simulated run' {
    BeforeAll {
        $tablesLine = (Select-String -Path $script:ScriptPath -Pattern '^\$Tables = @\((.*)\)\s*$').Matches[0].Groups[1].Value
        $global:SimCpTables = @($tablesLine -split ',' | ForEach-Object { $_.Trim().Trim("'") })
        $global:SimCpScenario = 'ok'
        $global:SimCpWrites = [System.Collections.Generic.List[string]]::new()
        # The narrow profile as the simulated environment holds it: per table the (create, update) levels, its members and any
        # permission outside the three columns. A scenario changes these; 'ok' is the D-136 shape.
        $global:SimCpNarrowExists = $true
        $global:SimCpNarrow = @{}
        $global:SimCpNarrowUsers = @()
        $global:SimCpNarrowTeams = @()
        $global:SimCpNarrowExtra = @()
        function global:SimCp-Guid([string]$s) { ([guid]::new([System.Security.Cryptography.MD5]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($s)))).ToString() }
        function global:SimCp-Reset {
            $global:SimCpScenario = 'ok'
            $global:SimCpWrites.Clear()
            if ($global:SimCpPermissionPosts) { $global:SimCpPermissionPosts.Clear() }
            $global:SimCpNarrowExists = $true
            $global:SimCpNarrow = @{ sprk_budgetrevision = @{ c = 4; u = 4 }; sprk_decisionrecord = @{ c = 4; u = 4 }; sprk_signal = @{ c = 4; u = 4 } }
            $global:SimCpNarrowUsers = @((SimCp-Guid 'mi'))
            $global:SimCpNarrowTeams = @()
            $global:SimCpNarrowExtra = @()
        }
        # The tables whose column travels in SpaarkeMaster / OntologyPlatformSolution, as the script's $SolutionHomes says.
        $global:SimCpHomeTables = @('sprk_budgetrevision', 'sprk_decisionrecord', 'sprk_signal')
        # ebilling-r1 task 013: tables whose column travels in SpaarkeMaster only (the eBilling schema scripts create it with
        # the canonical relationship name).
        $global:SimCpMasterOnlyTables = @('sprk_invoicesubmission', 'sprk_invoiceapproval', 'sprk_externalidentifier', 'sprk_invoicecorrection',
            'sprk_procedure', 'sprk_procedureversion', 'sprk_policy', 'sprk_policyversion', 'sprk_ruleset', 'sprk_procedureassignment')
        # Task 081: the tables a field-permission POST was sent for (the write list records only "POST fieldpermissions"), and no
        # real waiting: Ensure-FieldPermission's retry sleeps are shadowed so a refused grant fails fast instead of after two minutes.
        $global:SimCpPermissionPosts = [System.Collections.Generic.List[string]]::new()
        function global:Start-Sleep { }
        function global:SimCp-Rel([string]$t) {
            # the maker-made lookups (D-135 Q27) carry the maker's relationship name
            if ($t -in $global:SimCpHomeTables) { "$($t)_CreatedByPerson_systemuser" } else { "sprk_systemuser_$($t)_createdbyperson" }
        }
        SimCp-Reset
        function global:az { 'fake-token' }
        function global:Invoke-RestMethod {
            param($Uri, $Headers, $Method, $Body)
            $path = [System.Uri]::UnescapeDataString($Uri) -replace '^https://[^/]+/api/data/v9.2/', ''
            $reader = SimCp-Guid 'reader'; $writer = SimCp-Guid 'writer'; $bff = SimCp-Guid 'bff'
            $narrow = SimCp-Guid 'narrow'; $mi = SimCp-Guid 'mi'; $narrowName = 'Spaarke Ontology Writer Field Writers'
            # Every write is recorded, never performed.
            if ($Method -and $Method -ne 'Get') {
                $global:SimCpWrites.Add("$Method $path")
                if ($path -eq 'fieldpermissions' -and "$Body" -match '"entityname":"(\w+)"') {
                    $permTable = $Matches[1]
                    $global:SimCpPermissionPosts.Add($permTable)
                    # As live: a field permission on a column that is not (yet) secured is refused 0x8004f508. In the task 081
                    # scenarios sprk_procedure's column starts unsecured, so a grant is accepted only after this run secured it.
                    $securedNow = @($global:SimCpWrites | Where-Object { $_ -like "PUT EntityDefinitions(LogicalName='$permTable')/Attributes*" }).Count -gt 0
                    if ($global:SimCpScenario -like 'proc-unsecured-*' -and $permTable -eq 'sprk_procedure' -and -not $securedNow) {
                        throw "0x8004f508: The attribute sprk_createdbyperson is NOT secured for entity fieldpermission"
                    }
                    # Same for the non-ebilling table the 'br-unsecured-valued' scenario leaves unsecured: the gate refuses to secure it,
                    # so the narrow profile's grant (c2) must not be attempted (the old script sent it and the run died here).
                    if ($global:SimCpScenario -eq 'br-unsecured-valued' -and $permTable -eq 'sprk_budgetrevision' -and -not $securedNow) {
                        throw "0x8004f508: The attribute sprk_createdbyperson is NOT secured for entity fieldpermission"
                    }
                }
                if ($path -eq 'fieldsecurityprofiles') { return @{ fieldsecurityprofileid = $narrow } }
                return $null
            }
            if ($path -like 'organizations*') { return @{ value = @(@{ name = 'sim' }) } }
            if ($path -match "^EntityDefinitions\(LogicalName='(\w+)'\)/Attributes\(LogicalName='sprk_createdbyperson'\)/Microsoft") {
                if ($global:SimCpScenario -eq 'no-br-column' -and $Matches[1] -eq 'sprk_budgetrevision') { throw '404 not found' }
                if ($global:SimCpScenario -eq 'no-dr-column' -and $Matches[1] -eq 'sprk_decisionrecord') { throw '404 not found' }
                if ($global:SimCpScenario -eq 'no-signal-column' -and $Matches[1] -eq 'sprk_signal') { throw '404 not found' }
                # Task 081: 'proc-unsecured-empty' / 'proc-unsecured-valued' leave sprk_procedure's column unsecured, as 080 created it.
                # (Set-ColumnSecured then reads the untyped attribute, answered below.)
                $secured = -not ($global:SimCpScenario -like 'proc-unsecured-*' -and $Matches[1] -eq 'sprk_procedure')
                # UAC #2276: a NON-ebilling table (the owner created sprk_budgetrevision's column by hand) left unsecured.
                if ($global:SimCpScenario -eq 'br-unsecured-valued' -and $Matches[1] -eq 'sprk_budgetrevision') { $secured = $false }
                return @{ LogicalName = 'sprk_createdbyperson'; Targets = @('systemuser'); IsSecured = $secured; MetadataId = (SimCp-Guid "attr-$($Matches[1])") }
            }
            if ($path -match "^RelationshipDefinitions\(SchemaName='([^']+)'\)/Microsoft.Dynamics.CRM.OneToMany") {
                $name = $Matches[1]
                foreach ($t in $global:SimCpTables) {
                    $gone = ($global:SimCpScenario -eq 'no-br-column' -and $t -eq 'sprk_budgetrevision') -or ($global:SimCpScenario -eq 'no-dr-column' -and $t -eq 'sprk_decisionrecord') -or ($global:SimCpScenario -eq 'no-signal-column' -and $t -eq 'sprk_signal')
                    if ($name -eq (SimCp-Rel $t) -and -not $gone) {
                        return @{ SchemaName = $name; ReferencedEntity = 'systemuser'; ReferencingEntity = $t; ReferencingAttribute = 'sprk_createdbyperson'
                            ReferencingEntityNavigationPropertyName = 'sprk_CreatedByPerson'; MetadataId = (SimCp-Guid "rel-$t")
                            CascadeConfiguration = @{ Assign = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade'; Merge = 'NoCascade'; Delete = 'RemoveLink' } }
                    }
                }
                throw '404 not found'
            }
            if ($path -match "^fieldsecurityprofiles\?.*name eq 'Spaarke BFF-Managed Field Readers'") { return @{ value = @(@{ fieldsecurityprofileid = $reader; name = 'r' }) } }
            if ($path -match "^fieldsecurityprofiles\?.*name eq 'Spaarke BFF-Managed Field Writers'") { return @{ value = @(@{ fieldsecurityprofileid = $writer; name = 'w' }) } }
            if ($path -match "^fieldsecurityprofiles\?.*name eq 'Spaarke Ontology Writer Field Writers'") {
                if ($global:SimCpNarrowExists) { return @{ value = @(@{ fieldsecurityprofileid = $narrow; name = $narrowName }) } }
                return @{ value = @() }
            }
            if ($path -like "systemusers?*applicationid eq $(SimCp-Guid 'mi-app')*") { return @{ value = @(@{ systemuserid = $mi; fullname = 'MI' }) } }
            if ($path -like 'systemusers?*applicationid eq *') { return @{ value = @(@{ systemuserid = $bff; fullname = 'BFF' }) } }
            if ($path -like 'systemusers?*applicationid ne null*') { return @{ value = @() } }
            if ($path -like 'teams?*') { return @{ value = @() } }
            if ($path -like "fieldsecurityprofiles($writer)/systemuserprofiles_association*") {
                $members = @(@{ systemuserid = $bff; fullname = 'BFF'; applicationid = 'x' })
                if ($global:SimCpScenario -eq 'mi-in-bff-writers') { $members += @{ systemuserid = $mi; fullname = 'MI'; applicationid = 'mi-app-id' } }
                return @{ value = $members }
            }
            if ($path -like "fieldsecurityprofiles($narrow)/systemuserprofiles_association*") {
                $members = foreach ($u in $global:SimCpNarrowUsers) {
                    if ($u -eq $mi) { @{ systemuserid = $u; fullname = 'MI'; applicationid = 'mi-app-id' } } else { @{ systemuserid = $u; fullname = 'Someone'; applicationid = $null } }
                }
                return @{ value = @($members) }
            }
            if ($path -like "fieldsecurityprofiles($narrow)/teamprofiles_association*") {
                return @{ value = @($global:SimCpNarrowTeams | ForEach-Object { @{ teamid = $_; name = 'Some team' } }) }
            }
            if ($path -like 'fieldsecurityprofiles(*)/teamprofiles_association*') { return @{ value = @() } }
            if ($path -like "fieldpermissions?*_fieldsecurityprofileid_value eq $narrow and entityname eq *") {
                $tbl = $path -replace ".*entityname eq '(\w+)'.*", '$1'
                $row = $global:SimCpNarrow[$tbl]
                if (-not $row) { return @{ value = @() } }
                return @{ value = @(@{ fieldpermissionid = 'n'; canread = 4; cancreate = $row.c; canupdate = $row.u }) }
            }
            if ($path -like "fieldpermissions?*_fieldsecurityprofileid_value eq $narrow") {
                $rows = @($global:SimCpNarrow.Keys | ForEach-Object { @{ entityname = $_; attributelogicalname = 'sprk_createdbyperson' } }) + @($global:SimCpNarrowExtra)
                return @{ value = $rows }
            }
            if ($path -like '*expand=fieldsecurityprofileid*entityname eq ''sprk_budgetrevision''*' -or $path -like '*expand=fieldsecurityprofileid*entityname eq ''sprk_decisionrecord''*' -or $path -like '*expand=fieldsecurityprofileid*entityname eq ''sprk_signal''*') {
                # who can write the column on the budget revision / decision record / signal: the narrow profile does, and the script must allow it there
                return @{ value = @(@{ fieldsecurityprofileid = @{ name = $narrowName } }) }
            }
            if ($global:SimCpScenario -like 'proc-unsecured-*' -and $path -like "fieldpermissions?*_fieldsecurityprofileid_value eq *entityname eq 'sprk_procedure'*") {
                # Task 081: the column was never secured, so neither BFF profile holds a permission on it yet.
                return @{ value = @() }
            }
            if ($path -like 'fieldpermissions?*_fieldsecurityprofileid_value eq *') {
                $w = $path -like "*eq $writer*"
                return @{ value = @(@{ fieldpermissionid = 'p'; canread = 4; cancreate = $(if ($w) { 4 } else { 0 }); canupdate = $(if ($w) { 4 } else { 0 }) }) }
            }
            if ($path -like 'fieldpermissions?*') { return @{ value = @() } }
            if ($path -match "^EntityDefinitions\(LogicalName='(\w+)'\)/Attributes\(LogicalName='sprk_createdbyperson'\)$") {
                # Set-ColumnSecured's read of the attribute before it PUTs it back with IsSecured (task 081 scenarios only reach it).
                return [pscustomobject]@{ LogicalName = 'sprk_createdbyperson'; IsSecured = $false }
            }
            if ($path -like 'sprk_budgetrevisions?*_sprk_createdbyperson_value ne null*') {
                # UAC #2276: the universal gate's count on a non-ebilling table. Two valued rows in 'br-unsecured-valued'.
                if ($global:SimCpScenario -eq 'br-unsecured-valued') { return @{ value = @(@{ sprk_budgetrevisionid = 'b1' }, @{ sprk_budgetrevisionid = 'b2' }) } }
                return @{ value = @() }
            }
            if ($path -like 'sprk_procedures?*_sprk_createdbyperson_value ne null*') {
                # Task 081: the zero-non-null read before securing. One valued row in the 'proc-unsecured-valued' scenario.
                if ($global:SimCpScenario -eq 'proc-unsecured-valued') { return @{ value = @(@{ sprk_procedureid = 'p1' }) } }
                # uac-r2 review: 'proc-unsecured-raced' is empty at the check, and a writer stamps one row before the secure lands, so
                # the count AFTER this run's PUT finds it.
                $securedByRun = @($global:SimCpWrites | Where-Object { $_ -like "PUT EntityDefinitions(LogicalName='sprk_procedure')/Attributes*" }).Count -gt 0
                if ($global:SimCpScenario -eq 'proc-unsecured-raced' -and $securedByRun) { return @{ value = @(@{ sprk_procedureid = 'p2' }) } }
                return @{ value = @() }
            }
            if ($path -match '^EntityDefinitions\(LogicalName=''(\w+)''\)\?\$select=MetadataId') { return @{ MetadataId = (SimCp-Guid "table-$($Matches[1])") } }
            if ($path -match '^EntityDefinitions\(LogicalName=''(\w+)''\)\?\$select=EntitySetName') { return @{ EntitySetName = "$($Matches[1])s" } }
            if ($path -like "solutions?*uniquename eq '*'") {
                $n = $path -replace ".*uniquename eq '([^']+)'.*", '$1'
                return @{ value = @(@{ solutionid = (SimCp-Guid "sol-$n"); uniquename = $n }) }
            }
            if ($path -like 'solutioncomponents?*') {
                $sid = $path -replace '.*_solutionid_value eq ([0-9a-fA-F-]+).*', '$1'
                $rows = @()
                $budgetRevisionTable = @{ objectid = (SimCp-Guid 'table-sprk_budgetrevision'); componenttype = 1; rootcomponentbehavior = 0 }
                $decisionRecordTable = @{ objectid = (SimCp-Guid 'table-sprk_decisionrecord'); componenttype = 1; rootcomponentbehavior = 0 }
                $signalTable = @{ objectid = (SimCp-Guid 'table-sprk_signal'); componenttype = 1; rootcomponentbehavior = 0 }
                if ($sid -eq (SimCp-Guid 'sol-SpaarkeCore')) {
                    # SpaarkeCore holds every other table's column and relationship, and NOT the budget revision's or the decision record's.
                    foreach ($t in $global:SimCpTables | Where-Object { $_ -notin $global:SimCpHomeTables -and $_ -notin $global:SimCpMasterOnlyTables }) {
                        $rows += @{ objectid = (SimCp-Guid "attr-$t"); componenttype = 2; rootcomponentbehavior = 0 }
                        $rows += @{ objectid = (SimCp-Guid "rel-$t"); componenttype = 10; rootcomponentbehavior = 0 }
                    }
                    $rows += @{ objectid = $reader; componenttype = 70; rootcomponentbehavior = 0 }
                    $rows += @{ objectid = $writer; componenttype = 70; rootcomponentbehavior = 0 }
                }
                elseif ($sid -eq (SimCp-Guid 'sol-SpaarkeMaster')) {
                    # SpaarkeMaster holds the budget revision, decision record and signal tables AND the D-136 narrow profile.
                    $rows += $budgetRevisionTable
                    $rows += $decisionRecordTable
                    $rows += $signalTable
                    foreach ($t in $global:SimCpMasterOnlyTables) { $rows += @{ objectid = (SimCp-Guid "table-$t"); componenttype = 1; rootcomponentbehavior = 0 } }
                    if ($global:SimCpScenario -ne 'narrow-not-in-master') { $rows += @{ objectid = $narrow; componenttype = 70; rootcomponentbehavior = 0 } }
                }
                elseif ($sid -eq (SimCp-Guid 'sol-OntologyPlatformSolution')) {
                    if ($global:SimCpScenario -ne 'not-in-ontology') { $rows += $budgetRevisionTable }
                    if ($global:SimCpScenario -ne 'dr-not-in-ontology') { $rows += $decisionRecordTable }
                    if ($global:SimCpScenario -ne 'signal-not-in-ontology') { $rows += $signalTable }
                }
                return @{ value = $rows }
            }
            throw "simulation: unhandled request $path"
        }
        function script:Invoke-SimulatedRun([hashtable]$Mode = @{ Verify = $true }) {
            $global:LASTEXITCODE = 0
            $global:SimCpWrites.Clear()
            $global:SimCpPermissionPosts.Clear()
            $text = & $script:ScriptPath -EnvironmentUrl 'https://sim.crm.dynamics.com' -BffApplicationIds (SimCp-Guid 'app') -OntologyWriterApplicationId (SimCp-Guid 'mi-app') @Mode *>&1 | Out-String
            [pscustomobject]@{ Text = $text; ExitCode = $global:LASTEXITCODE; Writes = @($global:SimCpWrites) }
        }
        function script:Invoke-SimulatedVerify { Invoke-SimulatedRun @{ Verify = $true } }
    }
    AfterAll {
        foreach ($f in 'az', 'Invoke-RestMethod', 'SimCp-Guid', 'SimCp-Rel', 'SimCp-Reset', 'Start-Sleep') { Remove-Item "function:global:$f" -ErrorAction SilentlyContinue }
        Remove-Variable SimCpTables, SimCpHomeTables, SimCpMasterOnlyTables, SimCpScenario, SimCpWrites, SimCpNarrowExists, SimCpNarrow, SimCpNarrowUsers, SimCpNarrowTeams, SimCpNarrowExtra, SimCpPermissionPosts -Scope Global -ErrorAction SilentlyContinue
    }

    It 'passes: the maker relationship name is accepted, and the column is found in SpaarkeMaster and OntologyPlatformSolution, not reported missing from SpaarkeCore' {
        SimCp-Reset
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'OK\s+sprk_budgetrevision_CreatedByPerson_systemuser \(navigation sprk_CreatedByPerson'
        $run.Text | Should -Match 'OK\s+sprk_budgetrevision\.sprk_createdbyperson in SpaarkeMaster'
        $run.Text | Should -Match 'OK\s+sprk_budgetrevision\.sprk_createdbyperson in OntologyPlatformSolution'
        $run.Text | Should -Not -Match 'created another way'
        $run.Text | Should -Not -Match 'MISSING\s+\S*budgetrevision\S* in SpaarkeCore'
        $run.Text | Should -Match 'VERIFY PASS'
        $run.ExitCode | Should -Be 0
    }

    It '142 passes: the decision record''s column is found in SpaarkeMaster and OntologyPlatformSolution, not reported missing from SpaarkeCore, and the narrow profile may write it' {
        SimCp-Reset
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'OK\s+sprk_decisionrecord\.sprk_createdbyperson \(lookup -> systemuser\)'
        $run.Text | Should -Match 'OK\s+sprk_decisionrecord_CreatedByPerson_systemuser \(navigation sprk_CreatedByPerson'
        $run.Text | Should -Match 'OK\s+sprk_decisionrecord\.sprk_createdbyperson in SpaarkeMaster'
        $run.Text | Should -Match 'OK\s+sprk_decisionrecord\.sprk_createdbyperson in OntologyPlatformSolution'
        $run.Text | Should -Not -Match 'MISSING\s+\S*decisionrecord\S* in SpaarkeCore'
        $run.Text | Should -Not -Match 'can also write sprk_decisionrecord\.sprk_createdbyperson'
        $run.Text | Should -Match 'VERIFY PASS'
        $run.ExitCode | Should -Be 0
    }

    It '142 still fails when OntologyPlatformSolution does not carry the decision record''s column' {
        SimCp-Reset; $global:SimCpScenario = 'dr-not-in-ontology'
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'MISSING\s+sprk_decisionrecord\.sprk_createdbyperson in OntologyPlatformSolution'
        $run.Text | Should -Not -Match 'MISSING\s+sprk_budgetrevision'
        $run.Text | Should -Match 'VERIFY FAIL'
        $run.ExitCode | Should -Be 1
    }

    It '143 passes: the signal''s column is found in SpaarkeMaster and OntologyPlatformSolution, not reported missing from SpaarkeCore, and the narrow profile may write it' {
        SimCp-Reset
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'OK\s+sprk_signal\.sprk_createdbyperson \(lookup -> systemuser\)'
        $run.Text | Should -Match 'OK\s+sprk_signal_CreatedByPerson_systemuser \(navigation sprk_CreatedByPerson'
        $run.Text | Should -Match 'OK\s+sprk_signal\.sprk_createdbyperson in SpaarkeMaster'
        $run.Text | Should -Match 'OK\s+sprk_signal\.sprk_createdbyperson in OntologyPlatformSolution'
        $run.Text | Should -Not -Match 'MISSING\s+\S*sprk_signal\S* in SpaarkeCore'
        $run.Text | Should -Not -Match 'can also write sprk_signal\.sprk_createdbyperson'
        $run.Text | Should -Match 'VERIFY PASS'
        $run.ExitCode | Should -Be 0
    }

    It '143 still fails when OntologyPlatformSolution does not carry the signal''s column' {
        SimCp-Reset; $global:SimCpScenario = 'signal-not-in-ontology'
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'MISSING\s+sprk_signal\.sprk_createdbyperson in OntologyPlatformSolution'
        $run.Text | Should -Not -Match 'MISSING\s+sprk_budgetrevision'
        $run.Text | Should -Not -Match 'MISSING\s+sprk_decisionrecord'
        $run.Text | Should -Match 'VERIFY FAIL'
        $run.ExitCode | Should -Be 1
    }

    It '143 -Verify FAILs when the signal''s column is missing, and writes nothing' {
        SimCp-Reset; $global:SimCpScenario = 'no-signal-column'
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'MISSING\s+sprk_signal\.sprk_createdbyperson'
        $run.Text | Should -Match 'VERIFY FAIL'
        $run.ExitCode | Should -Be 1
        $run.Writes.Count | Should -Be 0
    }

    It 'still fails when a solution that should carry the column does not (a real gap is not hidden)' {
        SimCp-Reset; $global:SimCpScenario = 'not-in-ontology'
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'MISSING\s+sprk_budgetrevision\.sprk_createdbyperson in OntologyPlatformSolution'
        $run.Text | Should -Match 'VERIFY FAIL'
        $run.ExitCode | Should -Be 1
    }

    # ── D-136: the narrow profile ────────────────────────────────────────────────────────────────────────────
    It 'D-136 passes: the narrow profile exists, its only member is the writer, it holds 4/4/4 on exactly the three tables, and the BFF writers rule is untouched' {
        SimCp-Reset
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'OK\s+profile ''Spaarke Ontology Writer Field Writers'''
        $run.Text | Should -Match 'OK\s+Spaarke Ontology Writer Field Writers has no member besides the ontology writer application user'
        foreach ($t in 'sprk_budgetrevision', 'sprk_decisionrecord', 'sprk_signal') {
            $run.Text | Should -Match "OK\s+Spaarke Ontology Writer Field Writers on $t\.sprk_createdbyperson"
        }
        $run.Text | Should -Match 'OK\s+Spaarke Ontology Writer Field Writers holds no permission outside'
        $run.Text | Should -Match 'OK\s+field security profile ''Spaarke Ontology Writer Field Writers'' in SpaarkeMaster'
        $run.Text | Should -Not -Match 'can also write sprk_budgetrevision\.sprk_createdbyperson'
        # the BFF writers profile has NO ontology writer in it, and its rule passes exactly as before
        $run.Text | Should -Match 'OK\s+Spaarke BFF-Managed Field Writers has no member besides the BFF application user\(s\)'
        $run.Text | Should -Match 'VERIFY PASS'
        $run.ExitCode | Should -Be 0
    }

    It 'D-136 FAILs when the narrow profile has a member other than the writer' {
        SimCp-Reset; $global:SimCpNarrowUsers = @((SimCp-Guid 'mi'), (SimCp-Guid 'intruder'))
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'FAIL\s+Spaarke Ontology Writer Field Writers member ''Someone'' .* is a HUMAN user'
        $run.Text | Should -Match 'VERIFY FAIL'
        $run.ExitCode | Should -Be 1
    }

    It 'D-136 FAILs when a team is a member of the narrow profile' {
        SimCp-Reset; $global:SimCpNarrowTeams = @((SimCp-Guid 'team'))
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'FAIL\s+Spaarke Ontology Writer Field Writers has team ''Some team'''
        $run.ExitCode | Should -Be 1
    }

    It 'D-136 FAILs when a row is not 4/4/4 (update=0)' {
        SimCp-Reset; $global:SimCpNarrow['sprk_signal'] = @{ c = 4; u = 0 }
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'FAIL\s+Spaarke Ontology Writer Field Writers on sprk_signal\.sprk_createdbyperson is read=4 create=4 update=0; expected read=4 create=4 update=4'
        $run.ExitCode | Should -Be 1
    }

    It 'D-136 reports a missing row' {
        SimCp-Reset; $global:SimCpNarrow.Remove('sprk_decisionrecord')
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'MISSING\s+Spaarke Ontology Writer Field Writers on sprk_decisionrecord\.sprk_createdbyperson'
        $run.ExitCode | Should -Be 1
    }

    It 'D-136 FAILs when the narrow profile holds a permission outside the three tables' {
        SimCp-Reset; $global:SimCpNarrowExtra = @(@{ entityname = 'sprk_matter'; attributelogicalname = 'sprk_createdbyperson' })
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'FAIL\s+Spaarke Ontology Writer Field Writers also grants sprk_matter\.sprk_createdbyperson'
        $run.ExitCode | Should -Be 1
    }

    It 'D-136 FAILs when the narrow profile has a permission on another column of an allowed table' {
        SimCp-Reset; $global:SimCpNarrowExtra = @(@{ entityname = 'sprk_signal'; attributelogicalname = 'sprk_issecure' })
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'FAIL\s+Spaarke Ontology Writer Field Writers also grants sprk_signal\.sprk_issecure'
        $run.ExitCode | Should -Be 1
    }

    It 'D-136 FAILs under -Verify when the narrow profile is missing, and writes nothing' {
        SimCp-Reset; $global:SimCpNarrowExists = $false
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'MISSING\s+profile ''Spaarke Ontology Writer Field Writers'''
        $run.Text | Should -Match 'VERIFY FAIL'
        $run.ExitCode | Should -Be 1
        $run.Writes.Count | Should -Be 0
    }

    It 'the BFF writers rule is unchanged: the ontology writer joining the BFF profile is still a FAIL' {
        SimCp-Reset; $global:SimCpScenario = 'mi-in-bff-writers'
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'FAIL\s+Spaarke BFF-Managed Field Writers member ''MI'' .* not in -BffApplicationIds .* only the BFF may write sprk_createdbyperson'
        $run.ExitCode | Should -Be 1
    }

    It 'requires -OntologyWriterApplicationId for -Verify and -Apply' {
        { & $script:ScriptPath -EnvironmentUrl 'https://sim.crm.dynamics.com' -BffApplicationIds 'a' -Verify } | Should -Throw '*-OntologyWriterApplicationId is required*'
        { & $script:ScriptPath -EnvironmentUrl 'https://sim.crm.dynamics.com' -BffApplicationIds 'a' -Apply } | Should -Throw '*-OntologyWriterApplicationId is required*'
    }

    It 'a full -Apply on a complete environment writes nothing but the publish and exits 0' {
        SimCp-Reset
        $run = Invoke-SimulatedRun @{ Apply = $true }
        $run.Writes | Should -Be @('POST PublishXml')
        $run.ExitCode | Should -Be 0
    }

    It 'D-136 -Apply creates the narrow profile, adds the writer, grants 4/4/4 on the three tables and adds the profile to SpaarkeMaster' {
        # a fresh environment: no profile, so no member, no rows, and SpaarkeMaster does not hold it yet
        SimCp-Reset; $global:SimCpNarrowExists = $false; $global:SimCpNarrowUsers = @(); $global:SimCpNarrow = @{}; $global:SimCpScenario = 'narrow-not-in-master'
        $run = Invoke-SimulatedRun @{ Apply = $true }
        $run.Writes | Should -Contain 'POST fieldsecurityprofiles'
        @($run.Writes | Where-Object { $_ -like 'POST fieldsecurityprofiles(*)/systemuserprofiles_association/$ref' }).Count | Should -Be 1
        @($run.Writes | Where-Object { $_ -eq 'POST fieldpermissions' }).Count | Should -Be 3
        $run.Writes | Should -Contain 'POST AddSolutionComponent'
        $run.Text | Should -Match 'DONE\s+created profile ''Spaarke Ontology Writer Field Writers'''
        $run.Text | Should -Match 'DONE\s+added field security profile ''Spaarke Ontology Writer Field Writers'' to SpaarkeMaster'
        $run.ExitCode | Should -Be 0
    }

    # ── F3: -Apply never creates a SolutionHomes table's column ──────────────────────────────────────────────
    It 'F3: -Apply refuses to create sprk_budgetrevision.sprk_createdbyperson (it travels in SpaarkeMaster), says so, and exits non-zero' {
        SimCp-Reset; $global:SimCpScenario = 'no-br-column'
        $run = Invoke-SimulatedRun @{ Apply = $true }
        @($run.Writes | Where-Object { $_ -like '*RelationshipDefinitions*' -or $_ -like '*Attributes*' }).Count | Should -Be 0
        $run.Text | Should -Match 'FAIL\s+sprk_budgetrevision\.sprk_createdbyperson is missing and this script never creates it'
        $run.Text | Should -Match 'import the owning solution \(SpaarkeMaster / OntologyPlatformSolution\) first'
        $run.Text | Should -Not -Match 'DONE\s+created sprk_budgetrevision'
        $run.ExitCode | Should -Be 1
    }

    It '142 F3: -Apply refuses to create sprk_decisionrecord.sprk_createdbyperson (it travels in SpaarkeMaster), says so, and exits non-zero' {
        SimCp-Reset; $global:SimCpScenario = 'no-dr-column'
        $run = Invoke-SimulatedRun @{ Apply = $true }
        @($run.Writes | Where-Object { $_ -like '*RelationshipDefinitions*' -or $_ -like '*Attributes*' }).Count | Should -Be 0
        $run.Text | Should -Match 'FAIL\s+sprk_decisionrecord\.sprk_createdbyperson is missing and this script never creates it'
        $run.Text | Should -Not -Match 'DONE\s+created sprk_decisionrecord'
        $run.ExitCode | Should -Be 1
    }

    It '143 F3: -Apply refuses to create sprk_signal.sprk_createdbyperson (it travels in SpaarkeMaster), says so, and exits non-zero' {
        SimCp-Reset; $global:SimCpScenario = 'no-signal-column'
        $run = Invoke-SimulatedRun @{ Apply = $true }
        @($run.Writes | Where-Object { $_ -like '*RelationshipDefinitions*' -or $_ -like '*Attributes*' }).Count | Should -Be 0
        $run.Text | Should -Match 'FAIL\s+sprk_signal\.sprk_createdbyperson is missing and this script never creates it'
        $run.Text | Should -Not -Match 'DONE\s+created sprk_signal'
        $run.ExitCode | Should -Be 1
    }

    # ── ebilling-r1 task 081: the Procedure tables' column is secured only when it holds no value ──────────────────────
    It '081: -Apply secures an unsecured, empty sprk_procedure.sprk_createdbyperson after reading that it holds no value' {
        SimCp-Reset; $global:SimCpScenario = 'proc-unsecured-empty'
        $run = Invoke-SimulatedRun @{ Apply = $true }
        $run.Text | Should -Match 'OK\s+sprk_procedure\.sprk_createdbyperson holds no value yet'
        $run.Text | Should -Match 'DONE\s+secured sprk_procedure\.sprk_createdbyperson'
        @($run.Writes | Where-Object { $_ -like "PUT EntityDefinitions(LogicalName='sprk_procedure')/Attributes*" }).Count | Should -Be 1
        # then both BFF profiles are granted on the newly secured column (the grant after the PUT is accepted)
        @($global:SimCpPermissionPosts | Where-Object { $_ -eq 'sprk_procedure' }).Count | Should -Be 2
        $run.Text | Should -Match 'DONE\s+granted Spaarke BFF-Managed Field Writers on sprk_procedure\.sprk_createdbyperson'
        # uac-r2 review: counted again after the secure, and still empty
        $run.Text | Should -Match 'OK\s+sprk_procedure\.sprk_createdbyperson still holds no value after securing'
        $run.Text | Should -Not -Match 'gained \d+ value'
    }

    It '081 uac must fire: -Apply counts again after securing, and FAILs (exit 1) when a value was written between the check and the secure' {
        SimCp-Reset; $global:SimCpScenario = 'proc-unsecured-raced'
        $run = Invoke-SimulatedRun @{ Apply = $true }
        $run.Text | Should -Match 'OK\s+sprk_procedure\.sprk_createdbyperson holds no value yet'
        $run.Text | Should -Match 'DONE\s+secured sprk_procedure\.sprk_createdbyperson'
        $run.Text | Should -Match 'FAIL\s+sprk_procedure\.sprk_createdbyperson gained 1 value\(s\) between the empty check and securing'
        $run.Text | Should -Not -Match 'still holds no value after securing'
        $run.Text | Should -Match 'APPLY INCOMPLETE'
        $run.ExitCode | Should -Be 1
    }

    It '081 uac: -Verify on an unsecured, EMPTY Procedure column reports it safe to secure but NOT secured, writes nothing, and exits 1' {
        SimCp-Reset; $global:SimCpScenario = 'proc-unsecured-empty'
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'OK\s+sprk_procedure\.sprk_createdbyperson holds no value yet'
        $run.Text | Should -Match 'MISSING\s+sprk_procedure\.sprk_createdbyperson is NOT field-secured'
        $run.Text | Should -Not -Match 'after securing'
        $run.Writes.Count | Should -Be 0
        $run.ExitCode | Should -Be 1
    }

    It '081 uac: -Verify on an unsecured Procedure column that HOLDS a value FAILs on the value, writes nothing, and exits 1' {
        SimCp-Reset; $global:SimCpScenario = 'proc-unsecured-valued'
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'FAIL\s+sprk_procedure\.sprk_createdbyperson already holds 1 value\(s\) while unsecured'
        $run.Text | Should -Not -Match 'holds no value yet'
        $run.Writes.Count | Should -Be 0
        $run.ExitCode | Should -Be 1
    }

    It '081 uac: the dry run on an unsecured, EMPTY Procedure column says it WOULD secure and grant, and writes nothing' {
        SimCp-Reset; $global:SimCpScenario = 'proc-unsecured-empty'
        $run = Invoke-SimulatedRun @{}
        $run.Text | Should -Match 'OK\s+sprk_procedure\.sprk_createdbyperson holds no value yet'
        $run.Text | Should -Match 'WOULD\s+secure sprk_procedure\.sprk_createdbyperson'
        $run.Text | Should -Match 'WOULD\s+grant Spaarke BFF-Managed Field Writers on sprk_procedure\.sprk_createdbyperson'
        $run.Text | Should -Not -Match 'after securing'
        $run.Writes.Count | Should -Be 0
    }

    It '081 uac: the dry run on an unsecured Procedure column that HOLDS a value FAILs on it, would not secure it, and writes nothing' {
        SimCp-Reset; $global:SimCpScenario = 'proc-unsecured-valued'
        $run = Invoke-SimulatedRun @{}
        $run.Text | Should -Match 'FAIL\s+sprk_procedure\.sprk_createdbyperson already holds 1 value\(s\) while unsecured'
        $run.Text | Should -Not -Match 'WOULD\s+secure sprk_procedure\.sprk_createdbyperson'
        $run.Text | Should -Not -Match 'WOULD\s+grant [^
]* on sprk_procedure\.sprk_createdbyperson'
        $run.Writes.Count | Should -Be 0
    }

    It '081 must fire: -Apply does NOT secure sprk_procedure.sprk_createdbyperson when it already holds a value, grants nothing on it, carries on, and exits non-zero' {
        SimCp-Reset; $global:SimCpScenario = 'proc-unsecured-valued'
        $run = Invoke-SimulatedRun @{ Apply = $true }
        $run.Text | Should -Match 'FAIL\s+sprk_procedure\.sprk_createdbyperson already holds 1 value\(s\) while unsecured; it is NOT secured'
        $run.Text | Should -Not -Match 'DONE\s+secured sprk_procedure\.sprk_createdbyperson'
        @($run.Writes | Where-Object { $_ -like "PUT EntityDefinitions(LogicalName='sprk_procedure')/Attributes*" }).Count | Should -Be 0
        # no grant on the unsecured column (live refuses it 0x8004f508 and the script would stall, then abort the run)
        @($global:SimCpPermissionPosts | Where-Object { $_ -eq 'sprk_procedure' }).Count | Should -Be 0
        $run.Text | Should -Not -Match 'not yet seen as secured'
        # the run goes on: the next tables are still checked and the publish step is reached
        $run.Text | Should -Match 'OK\s+sprk_procedureversion\.sprk_createdbyperson is field-secured'
        $run.Text | Should -Match 'Published '
        $run.ExitCode | Should -Be 1
    }

    It 'UAC #2276 must fire: -Apply does NOT secure an unsecured sprk_budgetrevision column (not an ebilling table) that holds values, and exits 1' {
        SimCp-Reset; $global:SimCpScenario = 'br-unsecured-valued'
        # the narrow profile holds no row on the budget revision (the column was never secured, so it cannot); the seeded 4/4 row would hide the grant
        $global:SimCpNarrow.Remove('sprk_budgetrevision')
        $run = Invoke-SimulatedRun @{ Apply = $true }
        $run.Text | Should -Match 'FAIL\s+sprk_budgetrevision\.sprk_createdbyperson already holds 2 value\(s\) while unsecured; it is NOT secured'
        $run.Text | Should -Not -Match 'DONE\s+secured sprk_budgetrevision\.sprk_createdbyperson'
        @($run.Writes | Where-Object { $_ -like "PUT EntityDefinitions(LogicalName='sprk_budgetrevision')/Attributes*" }).Count | Should -Be 0
        @($global:SimCpPermissionPosts | Where-Object { $_ -eq 'sprk_budgetrevision' }).Count | Should -Be 0
        # uac-r2 round 2: (c2) skips the table (c) refused; the grant it used to send was refused 0x8004f508 and killed the run before (d)/(e)
        $run.Text | Should -Not -Match 'not yet seen as secured'
        $run.Text | Should -Match 'Published '
        $run.Text | Should -Match 'APPLY INCOMPLETE'
        $run.ExitCode | Should -Be 1
    }

    It 'uac-r2 round 2 must fire: the dry run on that table FAILs, and prints no WOULD-grant (or any narrow-profile line) for the column the gate refused to secure' {
        SimCp-Reset; $global:SimCpScenario = 'br-unsecured-valued'
        $global:SimCpNarrow.Remove('sprk_budgetrevision')
        $run = Invoke-SimulatedRun @{}
        $run.Text | Should -Match 'FAIL\s+sprk_budgetrevision\.sprk_createdbyperson already holds 2 value\(s\) while unsecured'
        $run.Text | Should -Not -Match 'WOULD\s+grant [^
]* on sprk_budgetrevision\.sprk_createdbyperson'
        $run.Text | Should -Not -Match 'WOULD\s+secure sprk_budgetrevision\.sprk_createdbyperson'
        $run.Writes.Count | Should -Be 0
        @($global:SimCpPermissionPosts | Where-Object { $_ -eq 'sprk_budgetrevision' }).Count | Should -Be 0
        $run.ExitCode | Should -Be 1
    }

    It 'uac-r2 round 2 must not fire: the OTHER narrow-profile tables are still granted in the same run' {
        SimCp-Reset; $global:SimCpScenario = 'br-unsecured-valued'
        $global:SimCpNarrow.Remove('sprk_budgetrevision'); $global:SimCpNarrow.Remove('sprk_signal')
        $run = Invoke-SimulatedRun @{}
        $run.Text | Should -Match 'WOULD\s+grant Spaarke Ontology Writer Field Writers on sprk_signal\.sprk_createdbyperson'
        $run.Text | Should -Not -Match 'WOULD\s+grant Spaarke Ontology Writer Field Writers on sprk_budgetrevision\.sprk_createdbyperson'
    }

    It '081 must not fire: an already secured Procedure column is not re-counted (no row read), and the dry run only reports' {
        SimCp-Reset
        $run = Invoke-SimulatedRun @{}
        $run.Text | Should -Match 'OK\s+sprk_procedure\.sprk_createdbyperson is field-secured'
        $run.Text | Should -Not -Match 'sprk_procedure\.sprk_createdbyperson holds no value'
        $run.Writes.Count | Should -Be 0
    }
}
