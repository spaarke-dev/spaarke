# =============================================================================
# THROWAWAY SPIKE — task 010 (FR-P0-08), spaarke-ai-architecture-redesign-r1
# OBO exchange: BFF confidential client + user assertion -> Dataverse mcp.tools
# delegated scope -> call /api/mcp (MCP initialize + tools/list).
#
# NOT production code. Never lands in src/. Secrets are read from the
# gitignored config/secrets.local.json in the main repo — never inline them.
# =============================================================================
param(
    [string]$ScopeForm = "https://spaarkedev1.crm.dynamics.com/mcp.tools"
)

$ErrorActionPreference = "Stop"

# --- Config (mirrors DataverseUserClient.cs keys: AzureAd:TenantId/ClientId/ClientSecret) ---
$tenantId  = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
$bffClient = "1e40baad-e065-4aea-a8d4-4b7ab273458c"   # SDAP-BFF-SPE-API (confidential client)
$dvUrl     = "https://spaarkedev1.crm.dynamics.com"
$secrets   = Get-Content "C:\code_files\spaarke\config\secrets.local.json" | ConvertFrom-Json
$bffSecret = $secrets.secrets.'bff.secrets.api_client_secret'

# --- Step 1: user assertion — a token whose AUDIENCE is the BFF app (what a PCF/code page sends) ---
Write-Host "== Step 1: acquire user assertion (az CLI -> resource api://$bffClient)"
$assertion = az account get-access-token --resource "api://$bffClient" --query accessToken -o tsv
if (-not $assertion) { throw "Could not acquire user assertion" }
# decode payload claims for evidence (aud/scp/upn only)
$p = $assertion.Split('.')[1]; $p = $p.Replace('-','+').Replace('_','/'); switch ($p.Length % 4) { 2 {$p += '=='} 3 {$p += '='} }
$claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p)) | ConvertFrom-Json
Write-Host "   assertion aud=$($claims.aud) scp=$($claims.scp) upn=$($claims.upn) appid=$($claims.appid)"

# --- Step 2: OBO exchange for the delegated Dataverse scope ---
Write-Host "== Step 2: OBO exchange, scope = $ScopeForm"
$body = @{
    grant_type          = "urn:ietf:params:oauth:grant-type:jwt-bearer"
    client_id           = $bffClient
    client_secret       = $bffSecret
    assertion           = $assertion
    scope               = $ScopeForm
    requested_token_use = "on_behalf_of"
}
try {
    $resp = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/token" -Body $body
    Write-Host "   OBO SUCCESS. token_type=$($resp.token_type) expires_in=$($resp.expires_in) scope=$($resp.scope)"
    $mcpToken = $resp.access_token
    $p2 = $mcpToken.Split('.')[1]; $p2 = $p2.Replace('-','+').Replace('_','/'); switch ($p2.Length % 4) { 2 {$p2 += '=='} 3 {$p2 += '='} }
    $c2 = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p2)) | ConvertFrom-Json
    Write-Host "   dv-token aud=$($c2.aud) scp=$($c2.scp) upn=$($c2.upn)"
}
catch {
    $err = $_.ErrorDetails.Message
    Write-Host "   OBO FAILED:"
    Write-Host $err
    exit 1
}

# --- Step 3: MCP initialize + tools/list against /api/mcp (Streamable HTTP JSON-RPC) ---
Write-Host "== Step 3: POST $dvUrl/api/mcp — initialize"
$headers = @{
    Authorization = "Bearer $mcpToken"
    Accept        = "application/json, text/event-stream"
}
$initBody = @{
    jsonrpc = "2.0"; id = 1; method = "initialize"
    params  = @{
        protocolVersion = "2025-06-18"
        capabilities    = @{}
        clientInfo      = @{ name = "spaarke-obo-spike"; version = "0.1" }
    }
} | ConvertTo-Json -Depth 6
try {
    $initResp = Invoke-WebRequest -Method Post -Uri "$dvUrl/api/mcp" -Headers $headers -ContentType "application/json" -Body $initBody
    Write-Host "   initialize -> HTTP $($initResp.StatusCode)"
    $sessionId = $initResp.Headers['Mcp-Session-Id'] | Select-Object -First 1
    Write-Host "   Mcp-Session-Id: $sessionId"
    Write-Host ($initResp.Content | Out-String)
}
catch {
    Write-Host "   initialize FAILED: HTTP $($_.Exception.Response.StatusCode.value__)"
    Write-Host ($_.ErrorDetails.Message | Out-String)
    exit 2
}

# notifications/initialized then tools/list
if ($sessionId) { $headers['Mcp-Session-Id'] = $sessionId }
$notifBody = @{ jsonrpc = "2.0"; method = "notifications/initialized" } | ConvertTo-Json
try { Invoke-WebRequest -Method Post -Uri "$dvUrl/api/mcp" -Headers $headers -ContentType "application/json" -Body $notifBody | Out-Null } catch {}

Write-Host "== Step 4: tools/list"
$listBody = @{ jsonrpc = "2.0"; id = 2; method = "tools/list"; params = @{} } | ConvertTo-Json
try {
    $listResp = Invoke-WebRequest -Method Post -Uri "$dvUrl/api/mcp" -Headers $headers -ContentType "application/json" -Body $listBody
    Write-Host "   tools/list -> HTTP $($listResp.StatusCode)"
    Write-Host ($listResp.Content | Out-String)
}
catch {
    Write-Host "   tools/list FAILED: HTTP $($_.Exception.Response.StatusCode.value__)"
    Write-Host ($_.ErrorDetails.Message | Out-String)
    exit 3
}
