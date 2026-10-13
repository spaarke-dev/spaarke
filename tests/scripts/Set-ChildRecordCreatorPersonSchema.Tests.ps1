# Pester 5. Run: Invoke-Pester tests/scripts/Set-ChildRecordCreatorPersonSchema.Tests.ps1
# Ontology task 147 (D-135 Q27, #1777): the child creator-person schema script accepts the maker's relationship-name form
# (`{table}_CreatedByPerson_systemuser`) as the SAME relationship it creates under its own name
# (`sprk_systemuser_{table}_createdbyperson`), instead of reporting "created another way".
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
        $text | Should -Match '(?m)^\$SolutionHomes = @\{ sprk_budgetrevision = @\(''SpaarkeMaster'', ''OntologyPlatformSolution''\) \}'
    }
}

# A whole -Verify run against a simulated Dataverse (az and Invoke-RestMethod shadowed; nothing leaves the process). It
# proves the relationship-name form and the solution-home check work in the script as shipped, not only in the extracted
# functions -- the run is what found that an empty list assigned through `$x = if (...)` unrolls to $null.
Describe 'a simulated -Verify run' {
    BeforeAll {
        $tablesLine = (Select-String -Path $script:ScriptPath -Pattern '^\$Tables = @\((.*)\)\s*$').Matches[0].Groups[1].Value
        $global:SimCpTables = @($tablesLine -split ',' | ForEach-Object { $_.Trim().Trim("'") })
        $global:SimCpScenario = 'ok'
        function global:SimCp-Guid([string]$s) { ([guid]::new([System.Security.Cryptography.MD5]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($s)))).ToString() }
        function global:SimCp-Rel([string]$t) {
            if ($t -eq 'sprk_budgetrevision') { 'sprk_budgetrevision_CreatedByPerson_systemuser' } else { "sprk_systemuser_$($t)_createdbyperson" }
        }
        function global:az { 'fake-token' }
        function global:Invoke-RestMethod {
            param($Uri, $Headers, $Method, $Body)
            $path = [System.Uri]::UnescapeDataString($Uri) -replace '^https://[^/]+/api/data/v9.2/', ''
            $reader = SimCp-Guid 'reader'; $writer = SimCp-Guid 'writer'; $bff = SimCp-Guid 'bff'
            if ($path -like 'organizations*') { return @{ value = @(@{ name = 'sim' }) } }
            if ($path -match "^EntityDefinitions\(LogicalName='(\w+)'\)/Attributes\(LogicalName='sprk_createdbyperson'\)/Microsoft") {
                return @{ LogicalName = 'sprk_createdbyperson'; Targets = @('systemuser'); IsSecured = $true; MetadataId = (SimCp-Guid "attr-$($Matches[1])") }
            }
            if ($path -match "^RelationshipDefinitions\(SchemaName='([^']+)'\)/Microsoft.Dynamics.CRM.OneToMany") {
                $name = $Matches[1]
                foreach ($t in $global:SimCpTables) {
                    if ($name -eq (SimCp-Rel $t)) {
                        return @{ SchemaName = $name; ReferencedEntity = 'systemuser'; ReferencingEntity = $t; ReferencingAttribute = 'sprk_createdbyperson'
                            ReferencingEntityNavigationPropertyName = 'sprk_CreatedByPerson'; MetadataId = (SimCp-Guid "rel-$t")
                            CascadeConfiguration = @{ Assign = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade'; Merge = 'NoCascade'; Delete = 'RemoveLink' } }
                    }
                }
                throw '404 not found'
            }
            if ($path -match "^fieldsecurityprofiles\?.*name eq 'Spaarke BFF-Managed Field Readers'") { return @{ value = @(@{ fieldsecurityprofileid = $reader; name = 'r' }) } }
            if ($path -match "^fieldsecurityprofiles\?.*name eq 'Spaarke BFF-Managed Field Writers'") { return @{ value = @(@{ fieldsecurityprofileid = $writer; name = 'w' }) } }
            if ($path -like 'systemusers?*applicationid eq *') { return @{ value = @(@{ systemuserid = $bff; fullname = 'BFF' }) } }
            if ($path -like 'systemusers?*applicationid ne null*') { return @{ value = @() } }
            if ($path -like 'teams?*') { return @{ value = @() } }
            if ($path -like "fieldsecurityprofiles($writer)/systemuserprofiles_association*") { return @{ value = @(@{ systemuserid = $bff; fullname = 'BFF'; applicationid = 'x' }) } }
            if ($path -like 'fieldsecurityprofiles(*)/teamprofiles_association*') { return @{ value = @() } }
            if ($path -like 'fieldpermissions?*_fieldsecurityprofileid_value eq *') {
                $w = $path -like "*eq $writer*"
                return @{ value = @(@{ fieldpermissionid = 'p'; canread = 4; cancreate = $(if ($w) { 4 } else { 0 }); canupdate = $(if ($w) { 4 } else { 0 }) }) }
            }
            if ($path -like 'fieldpermissions?*') { return @{ value = @() } }
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
                if ($sid -eq (SimCp-Guid 'sol-SpaarkeCore')) {
                    # SpaarkeCore holds every other table's column and relationship, and NOT the budget revision's.
                    foreach ($t in $global:SimCpTables | Where-Object { $_ -ne 'sprk_budgetrevision' }) {
                        $rows += @{ objectid = (SimCp-Guid "attr-$t"); componenttype = 2; rootcomponentbehavior = 0 }
                        $rows += @{ objectid = (SimCp-Guid "rel-$t"); componenttype = 10; rootcomponentbehavior = 0 }
                    }
                    $rows += @{ objectid = $reader; componenttype = 70; rootcomponentbehavior = 0 }
                    $rows += @{ objectid = $writer; componenttype = 70; rootcomponentbehavior = 0 }
                }
                elseif ($sid -eq (SimCp-Guid 'sol-SpaarkeMaster')) { $rows += $budgetRevisionTable }
                elseif ($sid -eq (SimCp-Guid 'sol-OntologyPlatformSolution') -and $global:SimCpScenario -ne 'not-in-ontology') { $rows += $budgetRevisionTable }
                return @{ value = $rows }
            }
            throw "simulation: unhandled request $path"
        }
        function script:Invoke-SimulatedVerify {
            $global:LASTEXITCODE = 0
            $text = & $script:ScriptPath -EnvironmentUrl 'https://sim.crm.dynamics.com' -BffApplicationIds (SimCp-Guid 'app') -Verify *>&1 | Out-String
            [pscustomobject]@{ Text = $text; ExitCode = $global:LASTEXITCODE }
        }
    }
    AfterAll {
        foreach ($f in 'az', 'Invoke-RestMethod', 'SimCp-Guid', 'SimCp-Rel') { Remove-Item "function:global:$f" -ErrorAction SilentlyContinue }
        Remove-Variable SimCpTables, SimCpScenario -Scope Global -ErrorAction SilentlyContinue
    }

    It 'passes: the maker relationship name is accepted, and the column is found in SpaarkeMaster and OntologyPlatformSolution, not reported missing from SpaarkeCore' {
        $global:SimCpScenario = 'ok'
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'OK\s+sprk_budgetrevision_CreatedByPerson_systemuser \(navigation sprk_CreatedByPerson'
        $run.Text | Should -Match 'OK\s+sprk_budgetrevision\.sprk_createdbyperson in SpaarkeMaster'
        $run.Text | Should -Match 'OK\s+sprk_budgetrevision\.sprk_createdbyperson in OntologyPlatformSolution'
        $run.Text | Should -Not -Match 'created another way'
        $run.Text | Should -Not -Match 'MISSING\s+\S*budgetrevision\S* in SpaarkeCore'
        $run.Text | Should -Match 'VERIFY PASS'
        $run.ExitCode | Should -Be 0
    }

    It 'still fails when a solution that should carry the column does not (a real gap is not hidden)' {
        $global:SimCpScenario = 'not-in-ontology'
        $run = Invoke-SimulatedVerify
        $run.Text | Should -Match 'MISSING\s+sprk_budgetrevision\.sprk_createdbyperson in OntologyPlatformSolution'
        $run.Text | Should -Match 'VERIFY FAIL'
        $run.ExitCode | Should -Be 1
    }
}
