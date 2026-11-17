# Pester 5.7.1. Run: Import-Module Pester -RequiredVersion 5.7.1; Invoke-Pester tests/scripts/Deploy-EbillingRolePrivileges.Tests.ps1
# ebilling-r1 iss028 (EB-D-249), correction rows: native write hardening. scripts/ebilling/Deploy-EbillingRolePrivileges.ps1 (D-90) mirrors
# a role's sprk_invoice privileges onto its working tables. sprk_invoicecorrection is not one of them. The script cannot be dot-sourced
# (it begins with `using module` and runs against the environment), so its $WorkingTables assignment is read from its own syntax tree.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:ScriptPath = Join-Path $script:RepoRoot 'scripts' 'ebilling' 'Deploy-EbillingRolePrivileges.ps1'

    # The string values of the top-level `$WorkingTables = ...` assignment in a script's syntax tree; throws when there is not exactly one.
    function Get-WorkingTables([System.Management.Automation.Language.ScriptBlockAst]$Ast) {
        $assignments = @($Ast.EndBlock.Statements | Where-Object {
                $_ -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                $_.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
                $_.Left.VariablePath.UserPath -eq 'WorkingTables'
            })
        if ($assignments.Count -ne 1) { throw "Expected one top-level `$WorkingTables assignment, found $($assignments.Count)." }
        return @($assignments[0].Right.FindAll({ param($n) $n -is [System.Management.Automation.Language.StringConstantExpressionAst] }, $true) |
                ForEach-Object { $_.Value })
    }

    function Get-ScriptAst([string]$Text) {
        $tokens = $null; $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$tokens, [ref]$errors)
        if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }
        return $ast
    }

    $script:Tables = Get-WorkingTables (Get-ScriptAst (Get-Content -Raw $script:ScriptPath))
}

Describe 'Deploy-EbillingRolePrivileges.ps1 working tables (correction rows: native write hardening)' {
    It 'does not mirror onto sprk_invoicecorrection' {
        $script:Tables | Should -Not -Contain 'sprk_invoicecorrection'
    }

    It 'still mirrors onto the other working tables' {
        foreach ($t in 'sprk_invoiceapproval', 'sprk_invoicesubmission', 'sprk_externalidentifier', 'sprk_billingexport') {
            $script:Tables | Should -Contain $t
        }
    }

    It 'must-fire: a script whose working tables name sprk_invoicecorrection is caught' {
        $seeded = "`$WorkingTables = 'sprk_invoiceapproval', 'sprk_invoicecorrection'`n"
        Get-WorkingTables (Get-ScriptAst $seeded) | Should -Contain 'sprk_invoicecorrection'
    }

    It 'fails closed when the assignment is missing' {
        { Get-WorkingTables (Get-ScriptAst "`$Other = 'x'`n") } | Should -Throw '*WorkingTables*'
    }
}
