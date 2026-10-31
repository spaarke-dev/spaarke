# tests/scripts/Set-ProductConfigurationReadDepth.Tests.ps1
# ---------------------------------------------------------------------------
# scripts/Set-ProductConfigurationReadDepth.ps1 refuses a config whose Global list names platform service configuration
# (its one list, $PlatformServiceTables, which ProductConfigurationReadRoleGuardTests reads from the script). These tests
# prove, with no live call:
#   1. Get-PlatformServiceViolations fires on each platform table in any letter case and not on a product table;
#   2. the script refuses such a config in the dry run, with -Apply and with -Verify, before it connects;
#   3. must not fire: the real config passes the check (the run then stops at the next, offline check: no role XML).
# and, for $NoEndUserPrivilegeTables (the tables the two roles hold no privilege on; the guard reads it too):
#   4. Get-NoPrivilegeXmlViolations fires on any table privilege there, in any letter case, and not on another table;
#   5. the script refuses, in every mode and before it connects, while a role XML still holds one;
#   6. must not fire: the real role XML holds none.
#
# Pester 3.4:  Import-Module Pester -RequiredVersion 3.4.0 ; Invoke-Pester tests/scripts/Set-ProductConfigurationReadDepth.Tests.ps1
# ---------------------------------------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$script:ScriptPath = Join-Path $repoRoot 'scripts/Set-ProductConfigurationReadDepth.ps1'
$script:ConfigDir = Join-Path $repoRoot 'config'
$script:Work = Join-Path ([System.IO.Path]::GetTempPath()) ("prodcfgread-tests-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $script:Work | Out-Null

# Pull the function under test out of the script (it cannot be dot-sourced: it runs on load).
$errs = $null; $tokens = $null
$script:Ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errs)
foreach ($name in 'Get-PlatformServiceViolations', 'Get-NoPrivilegeXmlViolations') {
    $fn = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true) | Select-Object -First 1
    Invoke-Expression $fn.Extent.Text
}

# The script's own lists, read from their one assignment each (the same lines the ArchTest guard parses).
function Get-ScriptList([string]$Variable) {
    $assign = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and $n.Left.Extent.Text -eq $Variable }, $true)
    @(Invoke-Expression $assign[0].Right.Extent.Text)
}
$script:PlatformTables = Get-ScriptList '$PlatformServiceTables'
$script:NoPrivilegeTables = Get-ScriptList '$NoEndUserPrivilegeTables'
$script:RealRolesDir = Join-Path $repoRoot 'src/dataverse/solutions/SpaarkeMaster/Roles'

# A config directory: the given config plus the three exclusion sources the script reads beside it.
function New-ConfigDir([string]$Name, [scriptblock]$Mutate) {
    $dir = Join-Path $script:Work $Name
    New-Item -ItemType Directory -Path $dir | Out-Null
    foreach ($f in 'restricted-content-owner-role.json', 'customer-configuration-owner-role.json', 'secure-record-owner-role.json') {
        Copy-Item -LiteralPath (Join-Path $script:ConfigDir $f) -Destination $dir
    }
    $config = Get-Content -LiteralPath (Join-Path $script:ConfigDir 'product-configuration-read-role.json') -Raw | ConvertFrom-Json
    if ($Mutate) { & $Mutate $config }
    $path = Join-Path $dir 'product-configuration-read-role.json'
    $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $path -Encoding UTF8
    $path
}

# Runs the script in a child process; never reaches the network for any case below (the checks under test run first).
function Invoke-Script([string]$ConfigPath, [string[]]$ModeArgs, [string]$RolesDir) {
    if (-not $RolesDir) {
        $RolesDir = Join-Path $script:Work ('no-roles-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $RolesDir | Out-Null
    }
    $output = & pwsh -NoProfile -NonInteractive -File $script:ScriptPath -EnvironmentUrl 'https://example.invalid' `
        -ConfigPath $ConfigPath -RolesXmlDir $RolesDir @ModeArgs 2>&1 | Out-String
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

# A roles directory: copies of the real Core and Basic User XML, Core User with one privilege line added.
function New-RolesDir([string]$Name, [string]$AddedPrivilege) {
    $dir = Join-Path $script:Work $Name
    New-Item -ItemType Directory -Path $dir | Out-Null
    foreach ($r in 'Spaarke Core User', 'Spaarke Basic User') {
        $xml = Get-Content -LiteralPath (Join-Path $script:RealRolesDir "$r.xml") -Raw
        if ($r -eq 'Spaarke Core User') { $xml = $xml -replace '<RolePrivileges>', "<RolePrivileges>`n    <RolePrivilege name=`"$AddedPrivilege`" level=`"Global`" />" }
        Set-Content -LiteralPath (Join-Path $dir "$r.xml") -Value $xml -Encoding UTF8
    }
    $dir
}

function Get-RealPrivilegeName([string]$Role) {
    @(([xml](Get-Content -LiteralPath (Join-Path $script:RealRolesDir "$Role.xml") -Raw)).SelectNodes('//RolePrivilege') | ForEach-Object { $_.name })
}

Describe 'Set-ProductConfigurationReadDepth: platform service configuration' {

    It 'holds exactly the three platform tables' {
        (@($script:PlatformTables | Sort-Object) -join ',') | Should Be 'sprk_aimodeldeployment,sprk_dataverseenvironment,sprk_externalserviceconfig'
    }

    It 'Get-PlatformServiceViolations fires on each platform table, in any letter case' {
        foreach ($t in $script:PlatformTables) {
            $rows = @([pscustomobject]@{ logicalName = $t.ToUpperInvariant() }, [pscustomobject]@{ logicalName = 'sprk_aitooltype' })
            $found = @(Get-PlatformServiceViolations $rows $script:PlatformTables)
            $found.Count | Should Be 1
            $found[0] | Should Match ([regex]::Escape($t.ToUpperInvariant()))
        }
    }

    It 'Get-PlatformServiceViolations does not fire on product configuration (must not fire)' {
        $rows = @([pscustomobject]@{ logicalName = 'sprk_aitooltype' }, [pscustomobject]@{ logicalName = 'sprk_gridconfiguration' })
        @(Get-PlatformServiceViolations $rows $script:PlatformTables).Count | Should Be 0
    }

    foreach ($mode in @(@{ Name = 'dry run'; Args = @() }, @{ Name = '-Apply'; Args = @('-Apply') }, @{ Name = '-Verify'; Args = @('-Verify') })) {
        It "refuses a config that lists sprk_externalserviceconfig ($($mode.Name))" {
            $config = New-ConfigDir ('platform-' + $mode.Name.Trim('-').Replace(' ', '')) {
                param($c)
                $c.tables += [pscustomobject]@{
                    logicalName = 'sprk_externalserviceconfig'; privilegeName = 'prvReadsprk_ExternalServiceConfig'
                    reason = 'seeded by the test'; evidence = 'seeded by the test'
                }
            }
            $run = Invoke-Script $config $mode.Args
            $run.ExitCode | Should Not Be 0
            $run.Output | Should Match 'platform service configuration'
            $run.Output | Should Not Match 'Role XML not found'
        }
    }

    It 'passes the real config through the check (must not fire); the run stops at the next offline check' {
        $config = New-ConfigDir 'real' $null
        $run = Invoke-Script $config @('-Verify')
        $run.ExitCode | Should Not Be 0
        $run.Output | Should Not Match 'platform service configuration'
        $run.Output | Should Match 'Role XML not found'
    }
}

Describe 'Set-ProductConfigurationReadDepth: tables the two roles hold no privilege on' {

    It 'holds sprk_externalserviceconfig' {
        $script:NoPrivilegeTables | Should Be @('sprk_externalserviceconfig')
    }

    It 'Get-NoPrivilegeXmlViolations fires on any privilege on the table, in any letter case, and not on another table' {
        $names = @('prvReadsprk_ExternalServiceConfig', 'PRVAPPENDTOSPRK_EXTERNALSERVICECONFIG', 'prvReadsprk_ExternalServiceConfigArchive', 'prvReadsprk_GridConfiguration')
        $found = @(Get-NoPrivilegeXmlViolations $names $script:NoPrivilegeTables)
        $found.Count | Should Be 2
        ($found -join '|') | Should Match 'prvReadsprk_ExternalServiceConfig \(on'
        ($found -join '|') | Should Match 'PRVAPPENDTOSPRK_EXTERNALSERVICECONFIG'
    }

    It 'the real Core and Basic User XML hold none (must not fire)' {
        foreach ($r in 'Spaarke Core User', 'Spaarke Basic User') {
            $names = Get-RealPrivilegeName $r
            $names.Count | Should BeGreaterThan 0
            @(Get-NoPrivilegeXmlViolations $names $script:NoPrivilegeTables).Count | Should Be 0
        }
    }

    foreach ($mode in @(@{ Name = 'dry run'; Args = @() }, @{ Name = '-Apply'; Args = @('-Apply') }, @{ Name = '-Verify'; Args = @('-Verify') })) {
        It "refuses while a role XML still holds a privilege on the table ($($mode.Name))" {
            $config = New-ConfigDir ('noprivcfg-' + $mode.Name.Trim('-').Replace(' ', '')) $null
            $roles = New-RolesDir ('noprivroles-' + $mode.Name.Trim('-').Replace(' ', '')) 'prvReadsprk_ExternalServiceConfig'
            $run = Invoke-Script $config $mode.Args $roles
            $run.ExitCode | Should Not Be 0
            $run.Output | Should Match 'the role holds no privilege on that table'
            $run.Output | Should Not Match 'No Dataverse token'
        }
    }
}

Remove-Item -LiteralPath $script:Work -Recurse -Force -ErrorAction SilentlyContinue
