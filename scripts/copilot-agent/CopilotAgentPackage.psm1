# -----------------------------------------------------------------------------
# CopilotAgentPackage.psm1 — the per-customer Microsoft Copilot agent package: template build and render (T257)
#
# customer-provisioning-orchestration-r1 T257 (design: projects/customer-provisioning-orchestration-r1/notes/
# t257-copilot-agent-design.md §3, owner-accepted 2026-10-09). Each customer's IT installs Spaarke's Copilot agent in
# the customer's HOME tenant. The agent signs the user in to Spaarke's tenant (OAuth 2.0 + PKCE through the shared,
# secret-free "Spaarke Copilot Agent" client) and calls only that customer's BFF.
#
# ONE template, rendered per customer:
#   - CI builds the template from src/solutions/CopilotAgent (New-CopilotAgentTemplate) and publishes it to the
#     provisioning-artifacts store (.github/workflows/publish-copilot-agent-template.yml) — never hand-built.
#   - The operator renders a customer's package from that template (Invoke-CopilotAgentRender, through
#     scripts/copilot-agent/Render-CopilotAgentPackage.ps1). Five tokens are replaced:
#       ${{SPAARKE_AGENT_ID}}               manifest id = UUIDv5(Spaarke Copilot namespace, customerId) — stable across versions
#       ${{SPAARKE_BFF_BASE_URL}}           OpenAPI servers[0].url = the customer's BFF
#       ${{SPAARKE_TENANT_ID}}              OpenAPI authorize/token URLs = Spaarke's tenant (Model 1), never common/organizations
#       ${{SPAARKE_BFF_SCOPE}}              api://{customerBffAppId}/user_impersonation (the scope H3 exposes and pre-authorizes)
#       ${{SPAARKE_COPILOT_AUTH_CONFIG_ID}} plugin auth.reference_id = the customer's auth config (registry sprk_copilotauthconfigid)
#
# Pins (re-checked 2026-10-09, design note §5.3): app manifest v1.30, declarative agent v1.8, API plugin v2.4. The
# template carries no permissions, webApplicationInfo, bot or knowledge capability (each would force a consent prompt
# on update or need a Copilot licence / metering).
#
# Every function is pure except the file-writing ones, which write only the path they are given. Nothing here calls
# Azure, Graph, Dataverse or the Teams developer portal.
# Tests: tests/scripts/CopilotAgentPackage.Tests.ps1 (Pester 5+).
# -----------------------------------------------------------------------------

#Requires -Version 7.3
# 7.3+: null-conditional / null-coalescing operators and ordered ConvertFrom-Json -AsHashtable (the -Version rewrite).

Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression

# The schema versions the template must carry. Bump them together with the source files.
$script:ManifestSchemaVersion = '1.30'
$script:DeclarativeAgentVersion = 'v1.8'
$script:PluginSchemaVersion = 'v2.4'

# UUIDv5(NAMESPACE_URL, 'https://spaarke.com/copilot-agent'). Fixed forever: changing it changes every customer's
# manifest id, which makes the next upload a NEW app in the customer's catalog instead of an update.
$script:ManifestIdNamespace = 'ebe1b961-0c13-50ff-be8e-0556a85be73a'

# The customerId standard (T237; docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md).
$script:CustomerIdPattern = '^[a-z][a-z0-9]{2,7}$'

# Package entries, in the order they are written. Key = name in the package; value = path under the source folder.
$script:TemplateEntries = [ordered]@{
    'manifest.json'            = 'appPackage/manifest.json'
    'color.png'                = 'appPackage/color.png'
    'outline.png'              = 'appPackage/outline.png'
    'declarativeAgent.json'    = 'declarativeAgent.json'
    'spaarke-api-plugin.json'  = 'spaarke-api-plugin.json'
    'spaarke-bff-openapi.yaml' = 'spaarke-bff-openapi.yaml'
}

$script:TokenNames = @(
    'SPAARKE_AGENT_ID',
    'SPAARKE_BFF_BASE_URL',
    'SPAARKE_TENANT_ID',
    'SPAARKE_BFF_SCOPE',
    'SPAARKE_COPILOT_AUTH_CONFIG_ID'
)

# Manifest members that must never be in the template (design §3; each one forces a per-user consent prompt on update,
# or brings back the bot/AADSTS700016 trap seen with the add-in on 2026-10-08).
$script:ForbiddenManifestMembers = @('permissions', 'webApplicationInfo', 'bots', 'composeExtensions', 'staticTabs',
    'configurableTabs', 'connectors', 'authorization')

# Fixed entry timestamp: the same inputs give a byte-identical package (render determinism).
$script:EntryTimestamp = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)

function Get-CopilotAgentPins {
    <# .SYNOPSIS The pinned schema versions, the manifest-id namespace and the token names. #>
    [PSCustomObject]@{
        ManifestSchemaVersion   = $script:ManifestSchemaVersion
        DeclarativeAgentVersion = $script:DeclarativeAgentVersion
        PluginSchemaVersion     = $script:PluginSchemaVersion
        ManifestIdNamespace     = $script:ManifestIdNamespace
        TokenNames              = $script:TokenNames
        EntryNames              = @($script:TemplateEntries.Keys)
    }
}

function ConvertTo-GuidBytesBigEndian([Guid]$Guid) {
    # RFC 9562 byte order (network order), not .NET's little-endian ToByteArray layout.
    $hex = $Guid.ToString('N')
    [byte[]]$bytes = for ($i = 0; $i -lt 32; $i += 2) { [Convert]::ToByte($hex.Substring($i, 2), 16) }
    return , $bytes
}

function New-UuidV5 {
    <#
    .SYNOPSIS RFC 9562 name-based UUID (version 5, SHA-1) — lowercase 'D' format.
    #>
    param(
        [Parameter(Mandatory)] [Guid]$Namespace,
        [Parameter(Mandatory)] [string]$Name
    )
    $ns = ConvertTo-GuidBytesBigEndian $Namespace
    $nameBytes = [System.Text.Encoding]::UTF8.GetBytes($Name)
    $sha1 = [System.Security.Cryptography.SHA1]::Create()
    try { $hash = $sha1.ComputeHash([byte[]]($ns + $nameBytes)) } finally { $sha1.Dispose() }
    $b = $hash[0..15]
    $b[6] = ($b[6] -band 0x0F) -bor 0x50
    $b[8] = ($b[8] -band 0x3F) -bor 0x80
    $h = -join ($b | ForEach-Object { $_.ToString('x2') })
    return '{0}-{1}-{2}-{3}-{4}' -f $h.Substring(0, 8), $h.Substring(8, 4), $h.Substring(12, 4), $h.Substring(16, 4), $h.Substring(20, 12)
}

function Get-CopilotAgentManifestId {
    <#
    .SYNOPSIS The customer's app manifest id: UUIDv5 of the customerId. Stable across template versions, so an upload
              of a new version updates the customer's existing catalog app instead of adding a second one.
    #>
    param([Parameter(Mandatory)] [string]$CustomerId)
    if ($CustomerId -cnotmatch $script:CustomerIdPattern) {
        throw "CustomerId '$CustomerId' does not match the customerId standard $($script:CustomerIdPattern)."
    }
    return New-UuidV5 -Namespace $script:ManifestIdNamespace -Name $CustomerId
}

function ConvertTo-CanonicalGuid([string]$Value, [string]$Name) {
    $g = [Guid]::Empty
    if (-not [Guid]::TryParse(($Value ?? '').Trim(), [ref]$g) -or $g -eq [Guid]::Empty) {
        throw "$Name '$Value' is not a GUID."
    }
    return $g.ToString('D')
}

function Get-CopilotAgentRenderValues {
    <#
    .SYNOPSIS Validates the per-customer values and returns the token → value map the render applies.
    .PARAMETER ManifestId     The app manifest id (Get-CopilotAgentManifestId for a customer).
    .PARAMETER BffBaseUrl     The customer's BFF base URL: https, host only (no path, port, query or trailing slash).
    .PARAMETER BffAppId       The customer's BFF app registration (client) id.
    .PARAMETER AuthConfigId   The customer's auth config (registration) id from the Teams developer portal / atk.
    .PARAMETER SpaarkeTenantId Spaarke's Entra tenant id (Model 1): the authorize/token authority. A GUID, never common.
    .PARAMETER BffScopeName   The delegated scope on the BFF app. Customer BFF apps expose only user_impersonation (H3).
    #>
    param(
        [Parameter(Mandatory)] [string]$ManifestId,
        [Parameter(Mandatory)] [string]$BffBaseUrl,
        [Parameter(Mandatory)] [string]$BffAppId,
        [Parameter(Mandatory)] [string]$AuthConfigId,
        [Parameter(Mandatory)] [string]$SpaarkeTenantId,
        [string]$BffScopeName = 'user_impersonation'
    )
    $manifestGuid = ConvertTo-CanonicalGuid $ManifestId 'ManifestId'
    $bffAppGuid = ConvertTo-CanonicalGuid $BffAppId 'BffAppId'
    $tenantGuid = ConvertTo-CanonicalGuid $SpaarkeTenantId 'SpaarkeTenantId'

    $url = ($BffBaseUrl ?? '').Trim().TrimEnd('/').ToLowerInvariant()
    if ($url -notmatch '^https://[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$') {
        throw "BffBaseUrl '$BffBaseUrl' must be https://<host> with no path, port, query or trailing segment."
    }
    $auth = ($AuthConfigId ?? '').Trim()
    if ($auth -notmatch '^[A-Za-z0-9+/=_.-]{8,512}$') {
        throw "AuthConfigId '$AuthConfigId' is not an auth config id (8-512 characters of A-Z a-z 0-9 + / = _ . -)."
    }
    if ($BffScopeName -cnotmatch '^[A-Za-z][A-Za-z0-9_.-]{0,63}$') {
        throw "BffScopeName '$BffScopeName' is not a scope name."
    }

    return [ordered]@{
        SPAARKE_AGENT_ID               = $manifestGuid
        SPAARKE_BFF_BASE_URL           = $url
        SPAARKE_TENANT_ID              = $tenantGuid
        SPAARKE_BFF_SCOPE              = "api://$bffAppGuid/$BffScopeName"
        SPAARKE_COPILOT_AUTH_CONFIG_ID = $auth
    }
}

function Get-CopilotAgentRegistryFilter {
    <#
    .SYNOPSIS The OData $filter the release loop (-AllActive) reads the registry with: active rows, Setup Status = Ready
              (sprk_setupstatus 2), Model 1 (sprk_tenancymodel 0 — the authority is Spaarke's tenant), and an auth config
              recorded by the post-Ready Copilot gate.
    #>
    return 'sprk_isactive eq true and sprk_setupstatus eq 2 and sprk_tenancymodel eq 0 and sprk_copilotauthconfigid ne null'
}

function ConvertFrom-CopilotAgentRegistryRow {
    <#
    .SYNOPSIS Maps a sprk_dataverseenvironment row (Web API JSON) to the render parameters. Throws naming every
              column the row is missing.
    .DESCRIPTION The BFF base URL is https://{sprk_appservicename}.azurewebsites.net — the production slot H9 deploys
                 and records (InterStepState.BffApiUrl). Spaarke's tenant is the row's sprk_tenantid (Model 1).
    #>
    param([Parameter(Mandatory)] [object]$Row)
    $columns = [ordered]@{
        CustomerId      = 'sprk_customerid'
        AppServiceName  = 'sprk_appservicename'
        BffAppId        = 'sprk_bffappid'
        AuthConfigId    = 'sprk_copilotauthconfigid'
        SpaarkeTenantId = 'sprk_tenantid'
    }
    $values = @{}
    $missing = @()
    foreach ($k in $columns.Keys) {
        $prop = $Row.PSObject.Properties[$columns[$k]]
        $v = if ($prop) { [string]$prop.Value } else { '' }
        if ([string]::IsNullOrWhiteSpace($v)) { $missing += $columns[$k] } else { $values[$k] = $v.Trim() }
    }
    if ($missing.Count -gt 0) {
        $who = if ($values.ContainsKey('CustomerId')) { $values.CustomerId } else { '(no sprk_customerid)' }
        throw "Registry row $who is missing $($missing -join ', ')."
    }
    if ($values.AppServiceName -notmatch '^[a-z0-9][a-z0-9-]{0,58}[a-z0-9]$') {
        throw "Registry row $($values.CustomerId): sprk_appservicename '$($values.AppServiceName)' is not an App Service name."
    }
    return [PSCustomObject]@{
        CustomerId      = $values.CustomerId
        BffBaseUrl      = "https://$($values.AppServiceName).azurewebsites.net"
        BffAppId        = $values.BffAppId
        AuthConfigId    = $values.AuthConfigId
        SpaarkeTenantId = $values.SpaarkeTenantId
    }
}

function Get-PngSize([byte[]]$Bytes) {
    $sig = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
    if ($Bytes.Length -lt 24 -or (Compare-Object $sig $Bytes[0..7] -SyncWindow 0)) { return $null }
    $w = ([int]$Bytes[16] -shl 24) -bor ([int]$Bytes[17] -shl 16) -bor ([int]$Bytes[18] -shl 8) -bor [int]$Bytes[19]
    $h = ([int]$Bytes[20] -shl 24) -bor ([int]$Bytes[21] -shl 16) -bor ([int]$Bytes[22] -shl 8) -bor [int]$Bytes[23]
    return @($w, $h)
}

function Get-TokenNamesIn([string]$Text) {
    return @([regex]::Matches($Text, '\$\{\{([A-Za-z0-9_]+)\}\}') | ForEach-Object { $_.Groups[1].Value })
}

function Test-CopilotAgentTemplateEntries {
    <#
    .SYNOPSIS Checks template entries (name → bytes) and returns the list of problems (empty = valid).
    #>
    param([Parameter(Mandatory)] [System.Collections.IDictionary]$Entries)
    $problems = [System.Collections.Generic.List[string]]::new()
    foreach ($name in $script:TemplateEntries.Keys) {
        if (-not $Entries.Contains($name)) { $problems.Add("missing entry $name") }
    }
    if ($problems.Count -gt 0) { return , $problems.ToArray() }

    $text = @{}
    foreach ($name in @('manifest.json', 'declarativeAgent.json', 'spaarke-api-plugin.json', 'spaarke-bff-openapi.yaml')) {
        $text[$name] = [System.Text.Encoding]::UTF8.GetString($Entries[$name])
    }

    foreach ($icon in @(@('color.png', 192), @('outline.png', 32))) {
        $size = Get-PngSize $Entries[$icon[0]]
        if (-not $size) { $problems.Add("$($icon[0]) is not a PNG") }
        elseif ($size[0] -ne $icon[1] -or $size[1] -ne $icon[1]) { $problems.Add("$($icon[0]) is $($size[0])x$($size[1]); expected $($icon[1])x$($icon[1])") }
    }

    try { $m = $text['manifest.json'] | ConvertFrom-Json -AsHashtable } catch { $problems.Add("manifest.json is not JSON: $($_.Exception.Message)"); $m = $null }
    if ($m) {
        if ($m['manifestVersion'] -ne $script:ManifestSchemaVersion) { $problems.Add("manifest.json manifestVersion '$($m['manifestVersion'])' is not $($script:ManifestSchemaVersion)") }
        if ($m['$schema'] -notlike "*/teams/v$($script:ManifestSchemaVersion)/MicrosoftTeams.schema.json") { $problems.Add("manifest.json `$schema is not the v$($script:ManifestSchemaVersion) schema") }
        if ($m['version'] -notmatch '^\d+\.\d+\.\d+$') { $problems.Add("manifest.json version '$($m['version'])' is not x.y.z") }
        if ($m['id'] -ne '${{SPAARKE_AGENT_ID}}') { $problems.Add('manifest.json id must be the ${{SPAARKE_AGENT_ID}} token') }
        foreach ($f in $script:ForbiddenManifestMembers) { if ($m.ContainsKey($f)) { $problems.Add("manifest.json must not declare '$f'") } }
        $das = @($m['copilotAgents']?['declarativeAgents'] | Where-Object { $_ })
        if ($das.Count -ne 1 -or $das[0]['file'] -ne 'declarativeAgent.json') { $problems.Add('manifest.json must declare exactly one declarative agent, file declarativeAgent.json') }
        if ($m['icons']?['color'] -ne 'color.png' -or $m['icons']?['outline'] -ne 'outline.png') { $problems.Add('manifest.json icons must be color.png and outline.png') }
    }

    try { $da = $text['declarativeAgent.json'] | ConvertFrom-Json -AsHashtable } catch { $problems.Add("declarativeAgent.json is not JSON: $($_.Exception.Message)"); $da = $null }
    if ($da) {
        if ($da['version'] -ne $script:DeclarativeAgentVersion) { $problems.Add("declarativeAgent.json version '$($da['version'])' is not $($script:DeclarativeAgentVersion)") }
        if ($da['$schema'] -notlike "*/declarative-agent/$($script:DeclarativeAgentVersion)/schema.json") { $problems.Add("declarativeAgent.json `$schema is not the $($script:DeclarativeAgentVersion) schema") }
        if ($da.ContainsKey('capabilities') -and @($da['capabilities']).Count -gt 0) {
            $problems.Add('declarativeAgent.json must declare no capabilities (knowledge needs a Copilot licence or metering; design §1)')
        }
        if (($da['instructions'] ?? '').Length -gt 8000) { $problems.Add('declarativeAgent.json instructions exceed 8,000 characters') }
        if (@($da['conversation_starters']).Count -gt 12) { $problems.Add('declarativeAgent.json has more than 12 conversation starters') }
        $acts = @($da['actions'] | Where-Object { $_ })
        if ($acts.Count -ne 1 -or $acts[0]['file'] -ne 'spaarke-api-plugin.json') { $problems.Add('declarativeAgent.json must have exactly one action, file spaarke-api-plugin.json') }
    }

    try { $pl = $text['spaarke-api-plugin.json'] | ConvertFrom-Json -AsHashtable } catch { $problems.Add("spaarke-api-plugin.json is not JSON: $($_.Exception.Message)"); $pl = $null }
    if ($pl) {
        if ($pl['schema_version'] -ne $script:PluginSchemaVersion) { $problems.Add("spaarke-api-plugin.json schema_version '$($pl['schema_version'])' is not $($script:PluginSchemaVersion)") }
        if ($pl['$schema'] -notlike "*/plugin/$($script:PluginSchemaVersion)/schema.json") { $problems.Add("spaarke-api-plugin.json `$schema is not the $($script:PluginSchemaVersion) schema") }
        $rts = @($pl['runtimes'] | Where-Object { $_ })
        if ($rts.Count -ne 1) { $problems.Add('spaarke-api-plugin.json must have exactly one runtime') }
        else {
            if ($rts[0]['auth']?['type'] -ne 'OAuthPluginVault') { $problems.Add('spaarke-api-plugin.json runtime auth.type must be OAuthPluginVault') }
            if ($rts[0]['auth']?['reference_id'] -ne '${{SPAARKE_COPILOT_AUTH_CONFIG_ID}}') { $problems.Add('spaarke-api-plugin.json auth.reference_id must be the ${{SPAARKE_COPILOT_AUTH_CONFIG_ID}} token') }
            if ($rts[0]['spec']?['url'] -ne 'spaarke-bff-openapi.yaml') { $problems.Add('spaarke-api-plugin.json runtime spec.url must be spaarke-bff-openapi.yaml') }
        }
    }

    $oa = $text['spaarke-bff-openapi.yaml']
    if ($oa -notmatch '(?m)^  - url: \$\{\{SPAARKE_BFF_BASE_URL\}\}\s*$') { $problems.Add('spaarke-bff-openapi.yaml servers[0].url must be the ${{SPAARKE_BFF_BASE_URL}} token') }
    foreach ($ep in @(@('authorizationUrl', 'authorize'), @('tokenUrl', 'token'))) {
        if ($oa -notmatch "(?m)^\s+$($ep[0]): https://login\.microsoftonline\.com/\`$\{\{SPAARKE_TENANT_ID\}\}/oauth2/v2\.0/$($ep[1])\s*$") {
            $problems.Add("spaarke-bff-openapi.yaml $($ep[0]) must be https://login.microsoftonline.com/`${{SPAARKE_TENANT_ID}}/oauth2/v2.0/$($ep[1])")
        }
    }
    if ($oa -match 'api://[0-9a-fA-F-]{36}' -or $oa -match 'login\.microsoftonline\.com/(?!\$\{\{)') {
        $problems.Add('spaarke-bff-openapi.yaml hard-codes an app id or an authority; use the tokens')
    }

    $all = @()
    foreach ($t in $text.Values) { $all += Get-TokenNamesIn $t }
    foreach ($t in ($all | Sort-Object -Unique)) { if ($t -notin $script:TokenNames) { $problems.Add("unknown token `${{$t}}") } }
    foreach ($t in $script:TokenNames) { if ($t -notin $all) { $problems.Add("token `${{$t}} is not used by the template") } }

    return , $problems.ToArray()
}

function Read-CopilotAgentTemplateSource {
    <# .SYNOPSIS Reads the template entries (name → bytes) from the source folder (src/solutions/CopilotAgent). #>
    param([Parameter(Mandatory)] [string]$SourceFolder)
    $entries = [ordered]@{}
    foreach ($name in $script:TemplateEntries.Keys) {
        $path = Join-Path $SourceFolder $script:TemplateEntries[$name]
        if (Test-Path -LiteralPath $path -PathType Leaf) { $entries[$name] = [System.IO.File]::ReadAllBytes($path) }
    }
    return $entries
}

function Read-ZipEntries {
    param([Parameter(Mandatory)] [string]$Path)
    $entries = [ordered]@{}
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Path).Path)
    try {
        foreach ($e in $zip.Entries) {
            $ms = [System.IO.MemoryStream]::new()
            $s = $e.Open(); try { $s.CopyTo($ms) } finally { $s.Dispose() }
            $entries[$e.FullName] = $ms.ToArray()
        }
    } finally { $zip.Dispose() }
    return $entries
}

function Write-DeterministicZip {
    <# .SYNOPSIS Writes the entries in the given order with a fixed timestamp: the same input gives the same bytes. #>
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary]$Entries,
        [Parameter(Mandatory)] [string]$Path
    )
    # Resolve against PowerShell's location, not the process's current directory (they differ after Set-Location).
    $full = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    $dir = Split-Path -Parent $full
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force -WhatIf:$false | Out-Null }
    $fs = [System.IO.File]::Open($full, [System.IO.FileMode]::Create, [System.IO.FileAccess]::ReadWrite)
    try {
        $zip = [System.IO.Compression.ZipArchive]::new($fs, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($name in $Entries.Keys) {
                $e = $zip.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
                $e.LastWriteTime = $script:EntryTimestamp
                $s = $e.Open(); try { $s.Write($Entries[$name], 0, $Entries[$name].Length) } finally { $s.Dispose() }
            }
        } finally { $zip.Dispose() }
    } finally { $fs.Dispose() }
    return $full
}

function Get-Sha256Hex([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-CopilotAgentTemplateVersion {
    <# .SYNOPSIS The template version (manifest.json "version") of a template zip or entry map. #>
    param([Parameter(Mandatory)] [object]$Template)
    $entries = if ($Template -is [System.Collections.IDictionary]) { $Template } else { Read-ZipEntries -Path $Template }
    if (-not $entries.Contains('manifest.json')) { throw 'The template has no manifest.json.' }
    $v = ([System.Text.Encoding]::UTF8.GetString($entries['manifest.json']) | ConvertFrom-Json).version
    if ($v -notmatch '^\d+\.\d+\.\d+$') { throw "Template manifest version '$v' is not x.y.z." }
    return $v
}

function New-CopilotAgentTemplate {
    <#
    .SYNOPSIS Builds the template package from the committed source. Refuses a source that fails any check.
    .OUTPUTS [PSCustomObject] Version, ZipPath, BlobName, Sha256.
    #>
    param(
        [Parameter(Mandatory)] [string]$SourceFolder,
        [Parameter(Mandatory)] [string]$OutputFolder
    )
    $entries = Read-CopilotAgentTemplateSource -SourceFolder $SourceFolder
    $problems = Test-CopilotAgentTemplateEntries -Entries $entries
    if ($problems.Count -gt 0) { throw "The Copilot agent template source is not valid:`n - $($problems -join "`n - ")" }
    $version = Get-CopilotAgentTemplateVersion -Template $entries
    $blobName = "copilot-agent-template-$version.zip"
    $zip = Write-DeterministicZip -Entries $entries -Path (Join-Path $OutputFolder $blobName)
    return [PSCustomObject]@{ Version = $version; ZipPath = $zip; BlobName = $blobName; Sha256 = (Get-Sha256Hex $zip) }
}

function New-CopilotAgentTemplateManifest {
    <#
    .SYNOPSIS The store manifest (copilot-agent-template-latest.json) — its only producer.
    .DESCRIPTION Shape: {"copilotAgentTemplate":{"version","blobName","sha256","buildId","sourceSha"}}.
    #>
    param(
        [Parameter(Mandatory)] [string]$Version,
        [Parameter(Mandatory)] [string]$BlobName,
        [Parameter(Mandatory)] [string]$Sha256,
        [Parameter(Mandatory)] [string]$BuildId,
        [Parameter(Mandatory)] [string]$SourceSha
    )
    if ($Sha256 -notmatch '^[0-9a-f]{64}$') { throw "Sha256 '$Sha256' is not a lowercase SHA-256." }
    return ([ordered]@{
            copilotAgentTemplate = [ordered]@{
                version   = $Version
                blobName  = $BlobName
                sha256    = $Sha256
                buildId   = $BuildId
                sourceSha = $SourceSha
            }
        } | ConvertTo-Json -Depth 4)
}

function Invoke-CopilotAgentRender {
    <#
    .SYNOPSIS Renders one package from the template: replaces the five tokens, checks the result, writes the zip.
    .PARAMETER Template   A template zip path, or an entry map (name → bytes).
    .PARAMETER Values     The map from Get-CopilotAgentRenderValues.
    .PARAMETER OutputPath The package to write.
    .PARAMETER Version    Optional x.y.z that replaces the template version (Spaarke's own environments only, where an
                          existing catalog app needs a higher number). Customer packages keep the template version.
    .OUTPUTS [PSCustomObject] ZipPath, Sha256, ManifestId, Version.
    #>
    param(
        [Parameter(Mandatory)] [object]$Template,
        [Parameter(Mandatory)] [System.Collections.IDictionary]$Values,
        [Parameter(Mandatory)] [string]$OutputPath,
        [string]$Version
    )
    $entries = if ($Template -is [System.Collections.IDictionary]) { $Template } else { Read-ZipEntries -Path $Template }
    $problems = Test-CopilotAgentTemplateEntries -Entries $entries
    if ($problems.Count -gt 0) { throw "The template is not valid:`n - $($problems -join "`n - ")" }
    foreach ($t in $script:TokenNames) {
        if (-not $Values.Contains($t) -or [string]::IsNullOrWhiteSpace([string]$Values[$t])) { throw "No value for `${{$t}} (use Get-CopilotAgentRenderValues)." }
        if ([string]$Values[$t] -match '["\\\s]|\$\{\{') { throw "The value for `${{$t}} contains a quote, backslash, whitespace or a token." }
    }
    if ($Version -and $Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$Version' is not x.y.z." }

    $out = [ordered]@{}
    foreach ($name in $script:TemplateEntries.Keys) {
        if ($name -like '*.png') { $out[$name] = $entries[$name]; continue }
        $s = [System.Text.Encoding]::UTF8.GetString($entries[$name])
        foreach ($t in $script:TokenNames) { $s = $s.Replace("`${{$t}}", [string]$Values[$t]) }
        $left = @(Get-TokenNamesIn $s)
        if ($left.Count -gt 0) { throw "$name still holds tokens after rendering: $($left -join ', ')." }
        if ($name -eq 'manifest.json' -and $Version) {
            $obj = $s | ConvertFrom-Json -AsHashtable
            $obj['version'] = $Version
            $s = ($obj | ConvertTo-Json -Depth 20) + "`n"
        }
        $out[$name] = [System.Text.UTF8Encoding]::new($false).GetBytes($s)
    }

    # Value placement, read back from what will be zipped.
    $manifest = [System.Text.Encoding]::UTF8.GetString($out['manifest.json']) | ConvertFrom-Json
    $plugin = [System.Text.Encoding]::UTF8.GetString($out['spaarke-api-plugin.json']) | ConvertFrom-Json
    $null = [System.Text.Encoding]::UTF8.GetString($out['declarativeAgent.json']) | ConvertFrom-Json
    $oa = [System.Text.Encoding]::UTF8.GetString($out['spaarke-bff-openapi.yaml'])
    if ($manifest.id -ne $Values.SPAARKE_AGENT_ID) { throw 'Rendered manifest id does not match.' }
    if ($plugin.runtimes[0].auth.reference_id -ne $Values.SPAARKE_COPILOT_AUTH_CONFIG_ID) { throw 'Rendered auth.reference_id does not match.' }
    if ($oa -notmatch "(?m)^  - url: $([regex]::Escape($Values.SPAARKE_BFF_BASE_URL))\s*$") { throw 'Rendered servers[0].url does not match.' }

    $zip = Write-DeterministicZip -Entries $out -Path $OutputPath
    return [PSCustomObject]@{ ZipPath = $zip; Sha256 = (Get-Sha256Hex $zip); ManifestId = $manifest.id; Version = $manifest.version }
}

Export-ModuleMember -Function Get-CopilotAgentPins, New-UuidV5, Get-CopilotAgentManifestId, Get-CopilotAgentRenderValues,
    Get-CopilotAgentRegistryFilter, ConvertFrom-CopilotAgentRegistryRow, Test-CopilotAgentTemplateEntries, Read-CopilotAgentTemplateSource,
    Read-ZipEntries, Write-DeterministicZip, Get-CopilotAgentTemplateVersion, New-CopilotAgentTemplate,
    New-CopilotAgentTemplateManifest, Invoke-CopilotAgentRender
