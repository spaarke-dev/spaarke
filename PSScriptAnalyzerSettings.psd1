@{
    # PSScriptAnalyzer Settings for Spaarke Repository
    # https://github.com/PowerShell/PSScriptAnalyzer
    #
    # Severity levels:
    #   Error    — Security issues, bugs that will cause runtime failures
    #   Warning  — Best practice violations, style issues
    #   Information — Minor suggestions

    # Rules to include (explicitly listed for clarity)
    IncludeRules = @(
        # Security rules (ERROR severity)
        'PSAvoidUsingPlainTextForPassword'
        'PSAvoidUsingConvertToSecureStringWithPlainText'
        'PSAvoidUsingUserNameAndPasswordParams'
        'PSAvoidUsingInvokeExpression'

        # Best practice rules (WARNING severity)
        'PSUseShouldProcessForStateChangingFunctions'
        'PSUseDeclaredVarsMoreThanAssignments'
        'PSAvoidGlobalVars'
        'PSAvoidDefaultValueSwitchParameter'
        'PSAvoidUsingCmdletAliases'
        'PSAvoidUsingPositionalParameters'
        'PSAvoidTrailingWhitespace'
        'PSMissingModuleManifestField'
        'PSReservedCmdletChar'
        'PSReservedParams'
        'PSShouldProcess'
        'PSUseApprovedVerbs'
        'PSUseCmdletCorrectly'
        'PSUseOutputTypeCorrectly'
        'PSUsePSCredentialType'

        # Code quality rules (WARNING severity)
        'PSAvoidUsingEmptyCatchBlock'
        'PSPossibleIncorrectComparisonWithNull'
        'PSPossibleIncorrectUsageOfAssignmentOperator'
        'PSPossibleIncorrectUsageOfRedirectionOperator'
        'PSUseProcessBlockForPipelineCommand'
    )

    # Rules to exclude (cause excessive false positives on deployment scripts)
    ExcludeRules = @(
        # Write-Host is intentional in deployment/interactive scripts
        'PSAvoidUsingWriteHost'

        # Many deployment scripts use positional parameters for brevity
        # Enforcing named parameters would require rewriting functional scripts
        # Re-enable after remediation (Task 033)
        # 'PSAvoidUsingPositionalParameters'
    )

    # Rule-specific configuration
    Rules = @{
        PSAvoidUsingCmdletAliases = @{
            # Allow common aliases that are widely understood
            AllowList = @('cd', 'cls', 'echo')
        }

        PSAvoidTrailingWhitespace = @{
            Enable = $true
        }
    }
}
