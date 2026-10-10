# tests/scripts/CopilotAgentPackage.Tests.ps1
# ---------------------------------------------------------------------------
# customer-provisioning-orchestration-r1 T257: the per-customer Copilot agent package
# (scripts/copilot-agent/CopilotAgentPackage.psm1). Proves:
#   - the manifest id is RFC 9562 UUIDv5 of the customerId under a fixed namespace (stable across versions, distinct
#     per customer), and only a valid customerId gets one;
#   - the committed template source passes the template checks and carries the pinned schema versions (v1.30 / v1.8 /
#     v2.4) with no permissions, webApplicationInfo, bot or knowledge capability;
#   - the checks refuse each forbidden shape (negative cases);
#   - the render puts each value in its place (manifest id, servers[0].url, authorize/token URLs, scope, reference_id),
#     leaves no token, keeps the icons, and is deterministic (same inputs → same bytes);
#   - value validation refuses a non-tenant authority, a non-https or pathful URL and unsafe auth config ids;
#   - a registry row maps to the render inputs, and a row missing a column is refused by name.
# Pester 5+ syntax (Install-Module Pester -MinimumVersion 5.5). CI: .github/workflows/publish-copilot-agent-template.yml.
# ---------------------------------------------------------------------------

BeforeAll {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    Import-Module (Join-Path $repoRoot 'scripts/copilot-agent/CopilotAgentPackage.psm1') -Force
    $script:SourceFolder = Join-Path $repoRoot 'src/solutions/CopilotAgent'

    $script:TenantId = 'a221a95e-6abc-4434-aecc-e48338a1b2f2'
    $script:BffAppId = '11111111-2222-3333-4444-555555555555'
    $script:AuthConfigId = 'YTIyMWE5NWUtNmFiYy00NDM0LWFlY2MtZTQ4MzM4YTFiMmYyIyM3ZmFj'

    function script:Get-Values([string]$CustomerId = 'acme', [string]$Url = 'https://sprk-acme-prod-api.azurewebsites.net') {
        Get-CopilotAgentRenderValues -ManifestId (Get-CopilotAgentManifestId -CustomerId $CustomerId) -BffBaseUrl $Url `
            -BffAppId $script:BffAppId -AuthConfigId $script:AuthConfigId -SpaarkeTenantId $script:TenantId
    }

    function script:Get-SourceEntries { Read-CopilotAgentTemplateSource -SourceFolder $script:SourceFolder }

    function script:Set-JsonEntry([System.Collections.IDictionary]$Entries, [string]$Name, [scriptblock]$Mutate) {
        $obj = [System.Text.Encoding]::UTF8.GetString($Entries[$Name]) | ConvertFrom-Json -AsHashtable
        & $Mutate $obj
        $Entries[$Name] = [System.Text.Encoding]::UTF8.GetBytes(($obj | ConvertTo-Json -Depth 20))
    }

    function script:Get-Text([System.Collections.IDictionary]$Entries, [string]$Name) {
        [System.Text.Encoding]::UTF8.GetString($Entries[$Name])
    }
}

Describe 'Manifest id (UUIDv5 of the customerId)' {
    It 'implements RFC 9562 UUIDv5 (the published python.org / DNS vector)' {
        New-UuidV5 -Namespace '6ba7b810-9dad-11d1-80b4-00c04fd430c8' -Name 'python.org' | Should -Be '886313e1-3b8a-5372-9b90-0c9aee199e5d'
    }

    It 'uses the fixed namespace UUIDv5(NAMESPACE_URL, https://spaarke.com/copilot-agent)' {
        (Get-CopilotAgentPins).ManifestIdNamespace | Should -Be (New-UuidV5 -Namespace '6ba7b811-9dad-11d1-80b4-00c04fd430c8' -Name 'https://spaarke.com/copilot-agent')
    }

    It 'is stable for a customer and pinned (a change would turn every update into a new catalog app)' {
        Get-CopilotAgentManifestId -CustomerId 'acme' | Should -Be '6a9d34ad-2bef-50f7-99d0-b324ffc2ed51'
        Get-CopilotAgentManifestId -CustomerId 'dewey' | Should -Be 'e061ee8e-05e7-52a8-ab5a-ac4f5fafae37'
    }

    It 'differs between customers' {
        Get-CopilotAgentManifestId -CustomerId 'acme' | Should -Not -Be (Get-CopilotAgentManifestId -CustomerId 'acme2')
    }

    It 'refuses <CustomerId>, which breaks the customerId standard' -TestCases @(
        @{ CustomerId = 'Acme' }, @{ CustomerId = 'ab' }, @{ CustomerId = 'acme-1' }, @{ CustomerId = 'abcdefghi' }, @{ CustomerId = '1acme' }
    ) {
        { Get-CopilotAgentManifestId -CustomerId $CustomerId } | Should -Throw '*customerId standard*'
    }
}

Describe 'Template source (src/solutions/CopilotAgent)' {
    It 'passes every template check' {
        Test-CopilotAgentTemplateEntries -Entries (Get-SourceEntries) | Should -BeNullOrEmpty
    }

    It 'carries the pinned schema versions v1.30 / v1.8 / v2.4' {
        $e = Get-SourceEntries
        $pins = Get-CopilotAgentPins
        $pins.ManifestSchemaVersion | Should -Be '1.30'
        $pins.DeclarativeAgentVersion | Should -Be 'v1.8'
        $pins.PluginSchemaVersion | Should -Be 'v2.4'
        (Get-Text $e 'manifest.json' | ConvertFrom-Json).manifestVersion | Should -Be '1.30'
        (Get-Text $e 'declarativeAgent.json' | ConvertFrom-Json).version | Should -Be 'v1.8'
        (Get-Text $e 'spaarke-api-plugin.json' | ConvertFrom-Json).schema_version | Should -Be 'v2.4'
    }

    It 'holds no dev value (tenant, BFF app, host or reference id)' {
        $e = Get-SourceEntries
        foreach ($n in @('manifest.json', 'declarativeAgent.json', 'spaarke-api-plugin.json', 'spaarke-bff-openapi.yaml')) {
            $t = Get-Text $e $n
            $t | Should -Not -Match 'a221a95e|1e40baad|f257a0a9|spaarke-bff-dev|YTIyMWE5NWU'
        }
    }

    It 'builds a template zip named for its version with the six entries in order' {
        $t = New-CopilotAgentTemplate -SourceFolder $script:SourceFolder -OutputFolder (Join-Path $TestDrive 'tpl')
        $t.BlobName | Should -Be "copilot-agent-template-$($t.Version).zip"
        $t.Sha256 | Should -Match '^[0-9a-f]{64}$'
        @((Read-ZipEntries -Path $t.ZipPath).Keys) | Should -Be @((Get-CopilotAgentPins).EntryNames)
    }
}

Describe 'Template checks refuse forbidden shapes' {
    It 'refuses manifest member <Member>' -TestCases @(
        @{ Member = 'permissions' }, @{ Member = 'webApplicationInfo' }, @{ Member = 'bots' }, @{ Member = 'composeExtensions' }
    ) {
        $e = Get-SourceEntries
        Set-JsonEntry $e 'manifest.json' { param($o) $o[$Member] = @('x') }
        (Test-CopilotAgentTemplateEntries -Entries $e) -join "`n" | Should -Match ([regex]::Escape("manifest.json must not declare '$Member'"))
    }

    It 'refuses a devPreview manifest' {
        $e = Get-SourceEntries
        Set-JsonEntry $e 'manifest.json' { param($o) $o['manifestVersion'] = 'devPreview' }
        (Test-CopilotAgentTemplateEntries -Entries $e) -join "`n" | Should -Match "manifestVersion 'devPreview' is not 1.30"
    }

    It 'refuses a hard-coded manifest id' {
        $e = Get-SourceEntries
        Set-JsonEntry $e 'manifest.json' { param($o) $o['id'] = 'f257a0a9-1061-4f9b-8918-3ad056fe90db' }
        (Test-CopilotAgentTemplateEntries -Entries $e) -join "`n" | Should -Match 'manifest.json id must be the'
    }

    It 'refuses a knowledge capability on the declarative agent' {
        $e = Get-SourceEntries
        Set-JsonEntry $e 'declarativeAgent.json' { param($o) $o['capabilities'] = @(@{ name = 'OneDriveAndSharePoint' }) }
        (Test-CopilotAgentTemplateEntries -Entries $e) -join "`n" | Should -Match 'must declare no capabilities'
    }

    It 'refuses a hard-coded auth reference id' {
        $e = Get-SourceEntries
        Set-JsonEntry $e 'spaarke-api-plugin.json' { param($o) $o['runtimes'][0]['auth']['reference_id'] = 'abc' }
        (Test-CopilotAgentTemplateEntries -Entries $e) -join "`n" | Should -Match 'reference_id must be the'
    }

    It 'refuses a hard-coded scope or authority in the OpenAPI description' {
        $e = Get-SourceEntries
        $t = (Get-Text $e 'spaarke-bff-openapi.yaml').Replace('${{SPAARKE_BFF_SCOPE}}', 'api://1e40baad-e065-4aea-a8d4-4b7ab273458c/access_as_user')
        $e['spaarke-bff-openapi.yaml'] = [System.Text.Encoding]::UTF8.GetBytes($t)
        $p = (Test-CopilotAgentTemplateEntries -Entries $e) -join "`n"
        $p | Should -Match 'hard-codes an app id'
        $p | Should -Match 'SPAARKE_BFF_SCOPE.*is not used'
    }

    It 'refuses an unknown token' {
        $e = Get-SourceEntries
        Set-JsonEntry $e 'declarativeAgent.json' { param($o) $o['description'] = 'x ${{SPAARKE_SOMETHING}}' }
        (Test-CopilotAgentTemplateEntries -Entries $e) -join "`n" | Should -Match 'unknown token'
    }

    It 'refuses a wrongly sized icon' {
        $e = Get-SourceEntries
        $e['outline.png'] = $e['color.png']
        (Test-CopilotAgentTemplateEntries -Entries $e) -join "`n" | Should -Match 'outline.png is 192x192; expected 32x32'
    }
}

Describe 'Render values' {
    It 'builds the scope on user_impersonation (the scope H3 exposes and pre-authorizes)' {
        (Get-Values).SPAARKE_BFF_SCOPE | Should -Be "api://$($script:BffAppId)/user_impersonation"
    }

    It 'normalizes a trailing slash and upper case in the BFF URL' {
        (Get-Values -Url 'https://SPRK-acme-prod-api.azurewebsites.net/').SPAARKE_BFF_BASE_URL | Should -Be 'https://sprk-acme-prod-api.azurewebsites.net'
    }

    It 'refuses BFF URL <Url>' -TestCases @(
        @{ Url = 'http://sprk-acme-prod-api.azurewebsites.net' }, @{ Url = 'https://sprk-acme-prod-api.azurewebsites.net/api' },
        @{ Url = 'https://sprk-acme-prod-api.azurewebsites.net:443' }, @{ Url = 'https://localhost' }
    ) {
        { Get-Values -Url $Url } | Should -Throw '*BffBaseUrl*'
    }

    It 'refuses authority <Tenant> (tenant-specific only, ADR-028)' -TestCases @(
        @{ Tenant = 'common' }, @{ Tenant = 'organizations' }, @{ Tenant = '00000000-0000-0000-0000-000000000000' }
    ) {
        { Get-CopilotAgentRenderValues -ManifestId (Get-CopilotAgentManifestId acme) -BffBaseUrl 'https://a.b.net' -BffAppId $script:BffAppId -AuthConfigId $script:AuthConfigId -SpaarkeTenantId $Tenant } |
            Should -Throw '*SpaarkeTenantId*not a GUID*'
    }

    It 'refuses auth config id <Id>' -TestCases @(@{ Id = 'abc' }, @{ Id = 'has space 12345' }, @{ Id = 'quote"12345678' }) {
        { Get-CopilotAgentRenderValues -ManifestId (Get-CopilotAgentManifestId acme) -BffBaseUrl 'https://a.b.net' -BffAppId $script:BffAppId -AuthConfigId $Id -SpaarkeTenantId $script:TenantId } |
            Should -Throw '*AuthConfigId*'
    }
}

Describe 'Render' {
    BeforeAll {
        $script:Template = New-CopilotAgentTemplate -SourceFolder $script:SourceFolder -OutputFolder (Join-Path $TestDrive 'tpl')
        $script:Values = Get-Values
        $script:Rendered = Invoke-CopilotAgentRender -Template $script:Template.ZipPath -Values $script:Values -OutputPath (Join-Path $TestDrive 'a/pkg.zip')
        $script:Out = Read-ZipEntries -Path $script:Rendered.ZipPath
        $script:Oa = Get-Text $script:Out 'spaarke-bff-openapi.yaml'
    }

    It 'puts the customer manifest id in manifest.json' {
        (Get-Text $script:Out 'manifest.json' | ConvertFrom-Json).id | Should -Be '6a9d34ad-2bef-50f7-99d0-b324ffc2ed51'
        $script:Rendered.ManifestId | Should -Be '6a9d34ad-2bef-50f7-99d0-b324ffc2ed51'
    }

    It 'puts the BFF in servers[0].url' {
        $script:Oa | Should -Match '(?m)^  - url: https://sprk-acme-prod-api\.azurewebsites\.net\s*$'
    }

    It "puts Spaarke's tenant in the authorize and token URLs" {
        $script:Oa | Should -Match "(?m)^\s+authorizationUrl: https://login\.microsoftonline\.com/$($script:TenantId)/oauth2/v2\.0/authorize\s*$"
        $script:Oa | Should -Match "(?m)^\s+tokenUrl: https://login\.microsoftonline\.com/$($script:TenantId)/oauth2/v2\.0/token\s*$"
    }

    It 'puts the scope in the OAuth scopes and the security requirement' {
        $scope = [regex]::Escape("api://$($script:BffAppId)/user_impersonation")
        $script:Oa | Should -Match "(?m)^\s+$($scope): "
        $script:Oa | Should -Match "(?m)^\s+- $($scope)\s*$"
    }

    It 'puts the auth config id in the plugin auth.reference_id' {
        (Get-Text $script:Out 'spaarke-api-plugin.json' | ConvertFrom-Json).runtimes[0].auth.reference_id | Should -Be $script:AuthConfigId
    }

    It 'leaves no token and keeps the template version and icons' {
        foreach ($n in @('manifest.json', 'declarativeAgent.json', 'spaarke-api-plugin.json', 'spaarke-bff-openapi.yaml')) {
            Get-Text $script:Out $n | Should -Not -Match '\$\{\{'
        }
        $script:Rendered.Version | Should -Be $script:Template.Version
        $tpl = Read-ZipEntries -Path $script:Template.ZipPath
        [Convert]::ToBase64String($script:Out['color.png']) | Should -Be ([Convert]::ToBase64String($tpl['color.png']))
        [Convert]::ToBase64String($script:Out['outline.png']) | Should -Be ([Convert]::ToBase64String($tpl['outline.png']))
    }

    It 'is deterministic: the same inputs give the same bytes' {
        $again = Invoke-CopilotAgentRender -Template $script:Template.ZipPath -Values $script:Values -OutputPath (Join-Path $TestDrive 'b/pkg.zip')
        $again.Sha256 | Should -Be $script:Rendered.Sha256
    }

    It 'differs for another customer' {
        $other = Invoke-CopilotAgentRender -Template $script:Template.ZipPath -Values (Get-Values -CustomerId 'dewey' -Url 'https://sprk-dewey-prod-api.azurewebsites.net') -OutputPath (Join-Path $TestDrive 'c/pkg.zip')
        $other.Sha256 | Should -Not -Be $script:Rendered.Sha256
        $other.ManifestId | Should -Be 'e061ee8e-05e7-52a8-ab5a-ac4f5fafae37'
    }

    It 'replaces the version only when one is given, keeping every other manifest member' {
        $v = Invoke-CopilotAgentRender -Template $script:Template.ZipPath -Values $script:Values -OutputPath (Join-Path $TestDrive 'd/pkg.zip') -Version '9.9.9'
        $v.Version | Should -Be '9.9.9'
        $before = Get-Text $script:Out 'manifest.json' | ConvertFrom-Json -AsHashtable
        $after = Get-Text (Read-ZipEntries -Path $v.ZipPath) 'manifest.json' | ConvertFrom-Json -AsHashtable
        @($after.Keys) | Should -Be @($before.Keys)
        $after['$schema'] | Should -Be $before['$schema']
        $after['manifestVersion'] | Should -Be '1.30'
        $after['id'] | Should -Be $before['id']
    }

    It 'refuses a value map without every token' {
        $partial = [ordered]@{} + $script:Values
        $partial.Remove('SPAARKE_COPILOT_AUTH_CONFIG_ID')
        { Invoke-CopilotAgentRender -Template $script:Template.ZipPath -Values $partial -OutputPath (Join-Path $TestDrive 'e/pkg.zip') } |
            Should -Throw '*SPAARKE_COPILOT_AUTH_CONFIG_ID*'
    }
}

Describe 'Registry row mapping' {
    BeforeAll {
        $script:Row = [PSCustomObject]@{
            sprk_customerid          = 'acme'
            sprk_appservicename      = 'sprk-acme-prod-api'
            sprk_bffappid            = $script:BffAppId
            sprk_copilotauthconfigid = $script:AuthConfigId
            sprk_tenantid            = $script:TenantId
        }
    }

    It 'maps a complete row (BFF URL from sprk_appservicename)' {
        $i = ConvertFrom-CopilotAgentRegistryRow -Row $script:Row
        $i.CustomerId | Should -Be 'acme'
        $i.BffBaseUrl | Should -Be 'https://sprk-acme-prod-api.azurewebsites.net'
        $i.BffAppId | Should -Be $script:BffAppId
        $i.AuthConfigId | Should -Be $script:AuthConfigId
        $i.SpaarkeTenantId | Should -Be $script:TenantId
    }

    It 'refuses a row missing the auth config id and the BFF app id, naming both' {
        $row = $script:Row.PSObject.Copy()
        $row.sprk_copilotauthconfigid = $null
        $row.PSObject.Properties.Remove('sprk_bffappid')
        { ConvertFrom-CopilotAgentRegistryRow -Row $row } | Should -Throw '*acme is missing sprk_bffappid, sprk_copilotauthconfigid*'
    }

    It 'refuses an App Service name that is not one' {
        $row = $script:Row.PSObject.Copy()
        $row.sprk_appservicename = 'https://x.azurewebsites.net'
        { ConvertFrom-CopilotAgentRegistryRow -Row $row } | Should -Throw '*not an App Service name*'
    }
}

Describe 'Store manifest (copilot-agent-template-latest.json)' {
    It 'has exactly the keys the render script reads' {
        $m = New-CopilotAgentTemplateManifest -Version '1.1.0' -BlobName 'copilot-agent-template-1.1.0.zip' -Sha256 ('a' * 64) -BuildId '2026.10.09-1' -SourceSha 'abc' | ConvertFrom-Json
        @($m.PSObject.Properties.Name) | Should -Be @('copilotAgentTemplate')
        @($m.copilotAgentTemplate.PSObject.Properties.Name) | Should -Be @('version', 'blobName', 'sha256', 'buildId', 'sourceSha')
    }

    It 'refuses a SHA-256 that is not lowercase hex' {
        { New-CopilotAgentTemplateManifest -Version '1.1.0' -BlobName 'x.zip' -Sha256 'ABC' -BuildId 'b' -SourceSha 's' } | Should -Throw '*SHA-256*'
    }
}

Describe 'Render-CopilotAgentPackage.ps1 (operator entry point; explicit mode, no Azure)' {
    BeforeAll {
        $script:RenderScript = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path 'scripts/copilot-agent/Render-CopilotAgentPackage.ps1'
        $script:Tpl = New-CopilotAgentTemplate -SourceFolder $script:SourceFolder -OutputFolder (Join-Path $TestDrive 'rtpl')
        $script:ManifestPath = Join-Path $TestDrive 'latest.json'
        New-CopilotAgentTemplateManifest -Version $script:Tpl.Version -BlobName $script:Tpl.BlobName -Sha256 $script:Tpl.Sha256 -BuildId 'b' -SourceSha 's' |
            Set-Content -LiteralPath $script:ManifestPath -Encoding utf8NoBOM
        $script:RenderArgs = @{
            TemplatePath = $script:Tpl.ZipPath; CustomerId = 'acme'; BffBaseUrl = 'https://sprk-acme-prod-api.azurewebsites.net'
            BffAppId = $script:BffAppId; AuthConfigId = $script:AuthConfigId; SpaarkeTenantId = $script:TenantId
        }
    }

    BeforeEach { $RenderArgs = $script:RenderArgs }

    It 'renders spaarke-copilot-{customerId}-{version}.zip when the store manifest matches the template' {
        $r = & $script:RenderScript @RenderArgs -TemplateManifestPath $script:ManifestPath -OutputFolder (Join-Path $TestDrive 'r1')
        Split-Path -Leaf $r.Package | Should -Be "spaarke-copilot-acme-$($script:Tpl.Version).zip"
        $r.ManifestId | Should -Be '6a9d34ad-2bef-50f7-99d0-b324ffc2ed51'
        $r.Scope | Should -Be "api://$($script:BffAppId)/user_impersonation"
    }

    It 'refuses a template whose SHA-256 differs from the store manifest' {
        $bad = Join-Path $TestDrive 'bad-sha.json'
        New-CopilotAgentTemplateManifest -Version $script:Tpl.Version -BlobName $script:Tpl.BlobName -Sha256 ('0' * 64) -BuildId 'b' -SourceSha 's' |
            Set-Content -LiteralPath $bad -Encoding utf8NoBOM
        { & $script:RenderScript @RenderArgs -TemplateManifestPath $bad -OutputFolder (Join-Path $TestDrive 'r2') } | Should -Throw '*SHA-256*does not match*'
    }

    It 'refuses a template whose version differs from the store manifest' {
        $bad = Join-Path $TestDrive 'bad-version.json'
        New-CopilotAgentTemplateManifest -Version '0.0.1' -BlobName $script:Tpl.BlobName -Sha256 $script:Tpl.Sha256 -BuildId 'b' -SourceSha 's' |
            Set-Content -LiteralPath $bad -Encoding utf8NoBOM
        { & $script:RenderScript @RenderArgs -TemplateManifestPath $bad -OutputFolder (Join-Path $TestDrive 'r3') } | Should -Throw '*version*does not match*'
    }
}

Describe 'Release-loop registry filter' {
    It 'reads only active, Ready, Model 1 rows that have an auth config' {
        Get-CopilotAgentRegistryFilter | Should -Be 'sprk_isactive eq true and sprk_setupstatus eq 2 and sprk_tenancymodel eq 0 and sprk_copilotauthconfigid ne null'
    }
}
