<#
.SYNOPSIS
  /provision-environment Step 0d — records the Dataverse MCP ping result and applies the disconnect policy.

.DESCRIPTION
  The ping itself is an agent tool call (mcp__dataverse__describe on sprk_dataverseenvironment); pass its outcome.
    -Alive                  → mcpFallbackActive = false.
    not alive, interactive  → the skill asked the operator "Continue anyway? (yes/no)"; pass -OperatorAnswer.
                              'yes' → fallback active; anything else → exit 1 ("Aborted at Step 0d MCP prompt.").
    not alive, batch        → BAT-04 intake.mcpDisconnectPolicy (default failFast):
                              failFast            → runs/pre-dispatch-mcp-disconnect.json + exit 1
                              proceedWithFallback → warning + fallback active (recorded for Step 7b)
                              other value         → exit 1
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$SessionFile,
  [switch]$Alive,
  [string]$OperatorAnswer
)

Import-Module (Join-Path $PSScriptRoot 'ProvisionEnvironment.psm1') -Force
$s = Read-ProvisionSession -SessionFile $SessionFile

if ($Alive) {
  Update-ProvisionSession -SessionFile $SessionFile -Values @{ mcpFallbackActive = $false } | Out-Null
  Write-Host '  [PASS] Dataverse MCP connected' -ForegroundColor Green
  exit 0
}

if ($s.mode -eq 'batch') {
  $policy = (Get-BatchPolicies -Intake (Get-ProvisionIntake -Session $s)).mcpDisconnectPolicy
  switch ($policy) {
    'failFast' {
      $diagPath = Write-ProvisionDiagnostic -Path 'runs/pre-dispatch-mcp-disconnect.json' -Data @{
        check    = 'dataverse-mcp'
        runId    = 'pre-dispatch'
        detected = (Get-Date -Format 'o')
        reason   = 'Dataverse MCP ping returned no result; batch policy mcpDisconnectPolicy=failFast'
        remedy   = 'Reconnect Dataverse MCP (see .claude/skills/provision-environment/references/fallback-matrix.md F1) OR rerun with mcpDisconnectPolicy=proceedWithFallback'
      }
      Stop-Provision "[skill] Batch HARD STOP (BAT-04, mcpDisconnectPolicy=failFast): Dataverse MCP not reachable. Diagnostic: $diagPath"
    }
    'proceedWithFallback' {
      Write-Warning '[skill] Batch mcpDisconnectPolicy=proceedWithFallback: Dataverse MCP not reachable. Registry ops (Step 1a probe, Step 1f placeholder-create, Step 6a completion PATCH) will use the raw Web API fallback per Fallback F1. This choice is captured in Step 7b lessons-learned.'
      Update-ProvisionSession -SessionFile $SessionFile -Values @{ mcpFallbackActive = $true } | Out-Null
      exit 0
    }
    default {
      Stop-Provision "[skill] Batch HARD STOP: unknown mcpDisconnectPolicy '$policy'. Valid: failFast | proceedWithFallback."
    }
  }
}

# INTERACTIVE — the skill showed the Step 0d prompt and passes the operator's answer.
if ($OperatorAnswer -ne 'yes') { Stop-Provision 'Aborted at Step 0d MCP prompt.' }
Update-ProvisionSession -SessionFile $SessionFile -Values @{ mcpFallbackActive = $true } | Out-Null
Write-Warning 'Dataverse MCP not connected — registry operations will use the Fallback F1 path.'
exit 0
