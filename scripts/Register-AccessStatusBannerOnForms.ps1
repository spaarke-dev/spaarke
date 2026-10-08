<#
.SYNOPSIS
    Registers the access-status banner library (sprk_/scripts/accessstatus_banner.js) and its OnLoad handler on the
    Project, Matter and Work Assignment MAIN forms. DRY RUN by default; -Verify checks the result at any time.

.DESCRIPTION
    Task 153 (unified-access-control-r2): the red, text-only form banner for a SECURE record and/or a No Access
    restriction needs, on each of the three root main forms:
      - the form library sprk_/scripts/accessstatus_banner.js, AFTER sprk_/scripts/bff_auth.js (it calls
        Spaarke.BffAuth when the form loads);
      - exactly one form OnLoad handler Spaarke.AccessStatus.onLoad from that library, enabled, passing the execution
        context. The library registers its own data OnLoad and OnPostSave handlers; none are added to the form XML.

    The change is ADDITIVE ONLY. The library is appended at the end of <formLibraries> (so after bff_auth.js, which
    must already be there); the handler is appended at the end of the form-level OnLoad <Handlers> (the <events>,
    <event name="onload"> and <Handlers> elements are created only when missing). No other library or handler is
    removed, changed or reordered, and control-level events are never touched. A parsed comparison proves it: the
    result, with exactly the added nodes taken out, must equal the original. New ids are derived from the form id (a
    stable hash), so a re-run writes identical XML, and a form that is already complete is left as it is ("nothing to
    do").

    Targets ONLY the forms named by -ProjectFormName, -MatterFormName and -WorkAssignmentFormName (one active main form
    each). Fail closed (ADR-003). Each of these REFUSES (exit 2), names the form, and writes nothing:
      FORM_NOT_FOUND          not exactly one active main form (type 2) of that table with that name;
      MANAGED_FORM            a form that needs a change is managed (change it in its owning solution);
      AUTH_LIBRARY_MISSING    the form does not register sprk_/scripts/bff_auth.js (deploy task 142's form libraries
                              first; this script does not add it);
      LIBRARY_ORDER           the banner library is registered BEFORE bff_auth.js (moving it is a reorder this
                              script does not make; fix it in the form designer);
      HANDLER_MISCONFIGURED   Spaarke.AccessStatus.onLoad is registered more than once, disabled, from another
                              library, or without the execution context (a maker's choice this script does not
                              override or remove);
      PREREQ_MISSING          (live) the web resource sprk_/scripts/accessstatus_banner.js or sprk_/scripts/bff_auth.js
                              is not in the environment;
      TRANSFORM_PARSE         the result is not well-formed, changed or removed an original node, or is incomplete;
      FORM_CHANGED            (-Apply) a form's XML changed between the scan and its PATCH.

    ORDER (live steps run by the main session): deploy the web resource (scripts/Deploy-WebResourceInline.ps1), then
    this script dry run -> -Apply -> -Verify.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Snapshot each target form's XML, PATCH the forms that need the change, publish the three tables, read back.
    Without a mode switch the script is a READ-ONLY dry run.

.PARAMETER Verify
    Read-only. Exit 0 only when, on each target form, bff_auth.js and the banner library are registered with the banner
    library AFTER bff_auth.js, exactly one enabled Spaarke.AccessStatus.onLoad handler from the banner library passes
    the execution context, and both web resources exist. Otherwise exit 1 naming each gap. A read fault is a FAILED
    check.

.PARAMETER RestoreFrom
    Path to a snapshot written by -Apply. Refuses (exit 2) if the snapshot's environment differs from -EnvironmentUrl
    or if a form changed after the apply. Otherwise puts each form's "before" XML back and publishes the tables.

.PARAMETER SelfTest
    Offline (no token, no network): runs the pure transform, verify and refusal checks over inline fixtures (shapes
    taken from the live spaarkedev1 forms on 2026-10-08); exits 1 on any mismatch.

.PARAMETER SnapshotDir
    -Apply only. Default: scripts/logs (gitignored).

.EXAMPLE
    pwsh -File scripts/Register-AccessStatusBannerOnForms.ps1             # dry run
    pwsh -File scripts/Register-AccessStatusBannerOnForms.ps1 -SelfTest   # offline fixtures
    pwsh -File scripts/Register-AccessStatusBannerOnForms.ps1 -Apply      # snapshot, PATCH, publish, read back
    pwsh -File scripts/Register-AccessStatusBannerOnForms.ps1 -Verify     # read-only (exit 0 / 1)
    pwsh -File scripts/Register-AccessStatusBannerOnForms.ps1 -RestoreFrom scripts/logs/access-status-banner-snapshot-20261008120000.json

.NOTES
    Project : unified-access-control-r2
    Task    : 153 (#1077) — access-status banner, form registration (coordinator request on PR #1450)
    Created : 2026-10-08
    Docs    : projects/unified-access-control-r2/notes/task-153-access-status-banner.md

    OPERATOR-RUN ONLY: -Apply and -RestoreFrom are the main session's manual gate. Requires Azure CLI (`az login`) for
    every mode except -SelfTest. PowerShell 7+.

    Exit codes: 0 = done / nothing to do / dry run complete / VERIFY PASS / SELF-TEST PASS;
                2 = refused (nothing was written); 1 = VERIFY FAIL, SELF-TEST FAIL, or an unexpected error.
#>

[CmdletBinding()]
param(
    [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [switch]$Apply,
    [switch]$Verify,
    [string]$RestoreFrom,
    [switch]$SelfTest,
    [string]$SnapshotDir = (Join-Path $PSScriptRoot 'logs'),
    [string]$ProjectFormName = 'Project main form',
    [string]$MatterFormName = 'Matter main form',
    [string]$WorkAssignmentFormName = 'Work Assignment main form'
)

$ErrorActionPreference = 'Stop'
$modeCount = @($Apply.IsPresent, $Verify.IsPresent, (-not [string]::IsNullOrEmpty($RestoreFrom)), $SelfTest.IsPresent) |
    Where-Object { $_ } | Measure-Object | Select-Object -ExpandProperty Count
if ($modeCount -gt 1) { throw '-Apply, -Verify, -RestoreFrom and -SelfTest are separate modes; pass at most one.' }

# ============================================================================
# Constants
# ============================================================================

$AuthLibrary = 'sprk_/scripts/bff_auth.js'
$BannerLibrary = 'sprk_/scripts/accessstatus_banner.js'
$OnLoadHandler = 'Spaarke.AccessStatus.onLoad'
$Targets = @(
    @{ Table = 'sprk_project'; Name = $ProjectFormName },
    @{ Table = 'sprk_matter'; Name = $MatterFormName },
    @{ Table = 'sprk_workassignment'; Name = $WorkAssignmentFormName }
)

# ============================================================================
# Pure functions (no network) — exercised by -SelfTest
# ============================================================================

class RefusedException : System.Exception {
    [string]$Code
    RefusedException([string]$code, [string]$message) : base($message) { $this.Code = $code }
}

function ConvertTo-XmlDoc([string]$Xml) {
    $doc = [System.Xml.XmlDocument]::new()
    $doc.PreserveWhitespace = $true
    $doc.XmlResolver = $null
    $doc.LoadXml($Xml)
    return $doc
}

function New-StableGuid([string]$Seed) {
    # A deterministic id per form and purpose: a re-run writes identical XML, so the transform is idempotent.
    $bytes = [System.Security.Cryptography.MD5]::HashData([Text.Encoding]::UTF8.GetBytes("uac-r2-153|$Seed"))
    return '{' + ([guid]::new($bytes)).ToString() + '}'
}

function Get-LibraryNames([System.Xml.XmlDocument]$Doc) {
    # The comma keeps a one-item result an array (PowerShell unrolls a returned array otherwise).
    return , @($Doc.SelectNodes('/form/formLibraries/Library') | ForEach-Object { $_.GetAttribute('name') })
}

function Get-BannerHandlers([System.Xml.XmlDocument]$Doc) {
    return , @($Doc.SelectNodes("/form/events/event[@name='onload']/Handlers/Handler") |
        Where-Object { $_.GetAttribute('functionName') -eq $OnLoadHandler })
}

function Test-HandlerCorrect($Handler) {
    return $Handler.GetAttribute('libraryName') -eq $BannerLibrary -and
    $Handler.GetAttribute('enabled') -eq 'true' -and
    $Handler.GetAttribute('passExecutionContext') -eq 'true'
}

# The gaps of one form's XML, as text (empty = complete). Used by -Verify and the dry run.
function Get-BannerFormGaps([string]$FormXml) {
    $gaps = [System.Collections.Generic.List[string]]::new()
    $doc = ConvertTo-XmlDoc $FormXml
    $libs = Get-LibraryNames $doc
    $auth = [array]::IndexOf([string[]]$libs, $AuthLibrary)
    $banner = [array]::IndexOf([string[]]$libs, $BannerLibrary)
    if ($auth -lt 0) { $gaps.Add("library $AuthLibrary is not registered") }
    if ($banner -lt 0) { $gaps.Add("library $BannerLibrary is not registered") }
    elseif ($auth -ge 0 -and $banner -lt $auth) { $gaps.Add("library $BannerLibrary is registered BEFORE $AuthLibrary") }
    if (@($libs | Where-Object { $_ -eq $BannerLibrary }).Count -gt 1) { $gaps.Add("library $BannerLibrary is registered more than once") }
    $handlers = Get-BannerHandlers $doc
    if ($handlers.Count -eq 0) { $gaps.Add("no form OnLoad handler $OnLoadHandler") }
    elseif ($handlers.Count -gt 1) { $gaps.Add("form OnLoad handler $OnLoadHandler is registered $($handlers.Count) times") }
    elseif (-not (Test-HandlerCorrect $handlers[0])) {
        $gaps.Add("form OnLoad handler $OnLoadHandler must come from $BannerLibrary, be enabled and pass the execution context")
    }
    return , $gaps
}

# Every node of the original in document order, with its attributes and text, for the parsed comparison.
function Get-NodeSignature([System.Xml.XmlNode]$Node) {
    $attrs = if ($Node.Attributes) { (@($Node.Attributes | ForEach-Object { "$($_.get_Name())=$($_.get_Value())" }) | Sort-Object) -join '|' } else { '' }
    $text = if ($Node.get_NodeType() -eq [System.Xml.XmlNodeType]::Text) { $Node.get_Value() } else { '' }
    return "$($Node.get_NodeType()):$($Node.get_Name())[$attrs]$text"
}

function Get-Signatures([System.Xml.XmlNode]$Root) {
    $list = [System.Collections.Generic.List[string]]::new()
    $walk = {
        param($n)
        $list.Add((Get-NodeSignature $n))
        foreach ($c in $n.ChildNodes) { & $walk $c }
    }
    & $walk $Root
    return , $list
}

# Returns @{ Xml; Changed; Added } for one form, or throws [RefusedException]. $FormKey seeds the new ids.
function Add-BannerToFormXml([string]$FormXml, [string]$FormKey) {
    $doc = ConvertTo-XmlDoc $FormXml
    $form = $doc.SelectSingleNode('/form')
    if (-not $form) { throw [RefusedException]::new('TRANSFORM_PARSE', 'the form XML has no <form> root') }

    $libs = Get-LibraryNames $doc
    $auth = [array]::IndexOf([string[]]$libs, $AuthLibrary)
    $banner = [array]::IndexOf([string[]]$libs, $BannerLibrary)
    if ($auth -lt 0) {
        throw [RefusedException]::new('AUTH_LIBRARY_MISSING', "the form does not register $AuthLibrary; deploy task 142's form libraries first")
    }
    if ($banner -ge 0 -and $banner -lt $auth) {
        throw [RefusedException]::new('LIBRARY_ORDER', "$BannerLibrary is registered before $AuthLibrary; move it after in the form designer")
    }
    if (@($libs | Where-Object { $_ -eq $BannerLibrary }).Count -gt 1) {
        throw [RefusedException]::new('LIBRARY_ORDER', "$BannerLibrary is registered more than once")
    }
    $handlers = Get-BannerHandlers $doc
    if ($handlers.Count -gt 1 -or ($handlers.Count -eq 1 -and -not (Test-HandlerCorrect $handlers[0]))) {
        throw [RefusedException]::new('HANDLER_MISCONFIGURED',
            "$OnLoadHandler is registered $($handlers.Count) time(s) and not exactly once from $BannerLibrary, enabled, passing the execution context")
    }

    $added = [System.Collections.Generic.List[System.Xml.XmlNode]]::new()
    if ($banner -lt 0) {
        $container = $doc.SelectSingleNode('/form/formLibraries')
        $lib = $doc.CreateElement('Library')
        $lib.SetAttribute('name', $BannerLibrary)
        $lib.SetAttribute('libraryUniqueId', (New-StableGuid "$FormKey|library"))
        $container.AppendChild($lib) | Out-Null
        $added.Add($lib)
    }
    if ($handlers.Count -eq 0) {
        $events = $doc.SelectSingleNode('/form/events')
        if (-not $events) {
            $events = $doc.CreateElement('events'); $form.AppendChild($events) | Out-Null; $added.Add($events)
        }
        $onload = $events.SelectSingleNode("event[@name='onload']")
        if (-not $onload) {
            $onload = $doc.CreateElement('event')
            $onload.SetAttribute('name', 'onload'); $onload.SetAttribute('application', 'false'); $onload.SetAttribute('active', 'false')
            $events.AppendChild($onload) | Out-Null
            if (-not $added.Contains($events)) { $added.Add($onload) }
        }
        $hs = $onload.SelectSingleNode('Handlers')
        if (-not $hs) {
            $hs = $doc.CreateElement('Handlers'); $onload.AppendChild($hs) | Out-Null
            if (-not ($added.Contains($events) -or $added.Contains($onload))) { $added.Add($hs) }
        }
        $h = $doc.CreateElement('Handler')
        foreach ($pair in @(
                @('functionName', $OnLoadHandler), @('libraryName', $BannerLibrary),
                @('handlerUniqueId', (New-StableGuid "$FormKey|onload")), @('enabled', 'true'),
                @('parameters', ''), @('passExecutionContext', 'true'))) {
            $h.SetAttribute($pair[0], $pair[1])
        }
        $hs.AppendChild($h) | Out-Null
        if (-not ($added.Contains($events) -or $added.Contains($onload) -or $added.Contains($hs))) { $added.Add($h) }
    }

    if ($added.Count -eq 0) { return @{ Xml = $FormXml; Changed = $false; Added = 0 } }
    $newXml = $doc.OuterXml

    # Proof: well-formed, complete, and identical to the original once the added nodes are taken out again.
    $check = ConvertTo-XmlDoc $newXml
    $remaining = Get-BannerFormGaps $newXml
    if ($remaining.Count -gt 0) { throw [RefusedException]::new('TRANSFORM_PARSE', "the result is incomplete: $($remaining -join '; ')") }
    foreach ($n in $added) {
        # LocalName, not Name: PowerShell's XML adapter answers .Name with a 'name' ATTRIBUTE when the element has one.
        $path = if ($n.LocalName -eq 'Library') { "/form/formLibraries/Library[@name='$BannerLibrary']" }
        elseif ($n.LocalName -eq 'Handler') { "/form/events/event[@name='onload']/Handlers/Handler[@functionName='$OnLoadHandler']" }
        elseif ($n.LocalName -eq 'Handlers') { "/form/events/event[@name='onload']/Handlers" }
        elseif ($n.LocalName -eq 'event') { "/form/events/event[@name='onload']" }
        else { '/form/events' }
        $node = $check.SelectSingleNode($path)
        if (-not $node) { throw [RefusedException]::new('TRANSFORM_PARSE', "added node $path is missing from the result") }
        $node.ParentNode.RemoveChild($node) | Out-Null
    }
    $before = Get-Signatures (ConvertTo-XmlDoc $FormXml).DocumentElement
    $after = Get-Signatures $check.DocumentElement
    if (($before -join "`n") -ne ($after -join "`n")) {
        throw [RefusedException]::new('TRANSFORM_PARSE', 'the result changed, removed or reordered an original node')
    }
    return @{ Xml = $newXml; Changed = $true; Added = $added.Count }
}

# ============================================================================
# -SelfTest (offline)
# ============================================================================

function Invoke-SelfTest {
    $failures = [System.Collections.Generic.List[string]]::new()
    function Assert-True([bool]$Condition, [string]$Name) {
        if ($Condition) { Write-Host "   pass  $Name" -ForegroundColor Green } else { $failures.Add($Name); Write-Host "   FAIL  $Name" -ForegroundColor Red }
    }
    function Get-Refusal([string]$Xml) {
        try { Add-BannerToFormXml $Xml 'selftest' | Out-Null; return $null } catch [RefusedException] { return $_.Exception.Code }
    }

    $tabs = '<tabs><tab name="general"><columns><column><sections><section name="s"><rows><row><cell id="{c1}">' +
    '<control id="sprk_name" datafieldname="sprk_name" /><events><event name="onchange"><Handlers><Handler functionName="X.onChange" libraryName="x.js" /></Handlers></event></events>' +
    '</cell></row></rows></section></sections></column></columns></tab></tabs>'

    # Shapes of the live spaarkedev1 forms (2026-10-08): Project / Work Assignment (events before formLibraries) and
    # Matter (formLibraries before events, several handlers, one with JSON parameters).
    $project = "<form>$tabs<events><event name=""onload"" application=""false"" active=""false""><Handlers><Handler functionName=""Spaarke.AssignedAccess.onLoad"" libraryName=""sprk_/scripts/assignedaccess_postsave.js"" handlerUniqueId=""{ae388ebe-2ad0-80bb-e3b4-d70afd8f3471}"" enabled=""true"" parameters="""" passExecutionContext=""true"" /></Handlers></event></events>" +
    "<formLibraries><Library name=""sprk_/scripts/bff_auth.js"" libraryUniqueId=""{6f44a23e-2c9c-171f-37b0-52bfa1beb77c}"" /><Library name=""sprk_/scripts/assignedaccess_postsave.js"" libraryUniqueId=""{71df880f-29ff-6bff-7f60-fe22c9f2210a}"" /></formLibraries></form>"
    $matter = "<form>$tabs<formLibraries><Library name=""sprk_matter_kpi_refresh.js"" libraryUniqueId=""{082ddb78-357e-4cdf-a854-1eb75b005444}"" /><Library name=""sprk_/scripts/bff_auth.js"" libraryUniqueId=""{1e82d24f-3e75-20b8-37e0-05fe29864648}"" /><Library name=""sprk_/scripts/assignedaccess_postsave.js"" libraryUniqueId=""{eb213817-b296-9b62-6137-113e5b8be344}"" /></formLibraries>" +
    "<events><event name=""onload"" application=""false"" active=""false""><Handlers><Handler functionName=""Spaarke.MatterKpi.onLoad"" libraryName=""sprk_matter_kpi_refresh.js"" handlerUniqueId=""{6171046f-3769-45a2-a3ba-de048d05cef9}"" enabled=""true"" parameters="""" passExecutionContext=""true"" />" +
    "<Handler functionName=""Spaarke.SubgridRollup.onLoad"" libraryName=""sprk_subgrid_parent_rollup.js"" handlerUniqueId=""{a0fac206-f596-4321-bfb2-8d762af147c6}"" enabled=""true"" parameters=""{&quot;subgridName&quot;:&quot;subgrid_invoice&quot;}"" passExecutionContext=""true"" />" +
    "<Handler functionName=""Spaarke.AssignedAccess.onLoad"" libraryName=""sprk_/scripts/assignedaccess_postsave.js"" handlerUniqueId=""{a7ee2da3-cc6e-8224-b5d5-715307f272ad}"" enabled=""true"" parameters="""" passExecutionContext=""true"" /></Handlers></event></events></form>"
    $noEvents = "<form>$tabs<formLibraries><Library name=""sprk_/scripts/bff_auth.js"" libraryUniqueId=""{11111111-1111-1111-1111-111111111111}"" /></formLibraries></form>"
    $emptyOnload = "<form>$tabs<events><event name=""onload"" application=""false"" active=""false"" /></events><formLibraries><Library name=""sprk_/scripts/bff_auth.js"" libraryUniqueId=""{11111111-1111-1111-1111-111111111111}"" /></formLibraries></form>"

    foreach ($case in @(@{ N = 'project shape'; X = $project }, @{ N = 'matter shape'; X = $matter },
            @{ N = 'no form events'; X = $noEvents }, @{ N = 'empty form onload'; X = $emptyOnload })) {
        $before = ConvertTo-XmlDoc $case.X
        Assert-True ((Get-BannerFormGaps $case.X).Count -gt 0) "$($case.N): the original reports gaps"
        $r = Add-BannerToFormXml $case.X 'form-a'
        $after = ConvertTo-XmlDoc $r.Xml
        Assert-True $r.Changed "$($case.N): changed"
        Assert-True ((Get-BannerFormGaps $r.Xml).Count -eq 0) "$($case.N): the result verifies clean"
        $libsBefore = Get-LibraryNames $before
        $libsAfter = Get-LibraryNames $after
        Assert-True (($libsAfter -join '|') -eq ((@($libsBefore) + $BannerLibrary) -join '|')) "$($case.N): libraries kept in order, banner appended last"
        $hBefore = @($before.SelectNodes("/form/events/event[@name='onload']/Handlers/Handler") | ForEach-Object { $_.OuterXml })
        $hAfter = @($after.SelectNodes("/form/events/event[@name='onload']/Handlers/Handler") | ForEach-Object { $_.OuterXml })
        Assert-True ((($hAfter | Select-Object -First $hBefore.Count) -join '') -eq ($hBefore -join '') -and $hAfter.Count -eq $hBefore.Count + 1) "$($case.N): existing handlers kept, identical and in order; one added last"
        Assert-True ($after.SelectSingleNode("//cell/events/event[@name='onchange']/Handlers/Handler[@functionName='X.onChange']") -ne $null -and
            $after.SelectNodes("//cell/events//Handler").Count -eq 1) "$($case.N): control-level events untouched"
        $again = Add-BannerToFormXml $r.Xml 'form-a'
        Assert-True (-not $again.Changed -and $again.Xml -eq $r.Xml) "$($case.N): a second run changes nothing"
        Assert-True ((Add-BannerToFormXml $case.X 'form-a').Xml -eq $r.Xml) "$($case.N): deterministic (identical XML on a re-run from the original)"
    }
    Assert-True ($matter -match 'subgrid_invoice') 'fixture sanity'
    $m = ConvertTo-XmlDoc (Add-BannerToFormXml $matter 'm').Xml
    Assert-True ($m.SelectSingleNode("//Handler[@functionName='Spaarke.SubgridRollup.onLoad']").GetAttribute('parameters') -eq '{"subgridName":"subgrid_invoice"}') 'matter: JSON handler parameters preserved'

    # Refusals.
    $noAuth = $project -replace '<Library name="sprk_/scripts/bff_auth.js"[^>]*/>', ''
    Assert-True ((Get-Refusal $noAuth) -eq 'AUTH_LIBRARY_MISSING') 'refuses a form without bff_auth.js'
    $bannerFirst = $project -replace '<formLibraries>', "<formLibraries><Library name=""$BannerLibrary"" libraryUniqueId=""{22222222-2222-2222-2222-222222222222}"" />"
    Assert-True ((Get-Refusal $bannerFirst) -eq 'LIBRARY_ORDER') 'refuses the banner library before bff_auth.js'
    Assert-True (@(Get-BannerFormGaps $bannerFirst | Where-Object { $_ -like '*BEFORE*' }).Count -eq 1) 'verify names the order gap'
    $done = (Add-BannerToFormXml $project 'p').Xml
    $dup = $done -replace '(<Handler functionName="Spaarke\.AccessStatus\.onLoad"[^>]*/>)', '$1$1'
    Assert-True ((Get-Refusal $dup) -eq 'HANDLER_MISCONFIGURED') 'refuses a duplicated banner handler (never removes it)'
    Assert-True (@(Get-BannerFormGaps $dup | Where-Object { $_ -like '*2 times*' }).Count -eq 1) 'verify names the duplicate'
    $disabled = $done -replace '(functionName="Spaarke\.AccessStatus\.onLoad"[^>]*?)enabled="true"', '$1enabled="false"'
    Assert-True ((Get-Refusal $disabled) -eq 'HANDLER_MISCONFIGURED') 'refuses a disabled banner handler'
    $noContext = $done -replace '(functionName="Spaarke\.AccessStatus\.onLoad"[^>]*?)passExecutionContext="true"', '$1passExecutionContext="false"'
    Assert-True ((Get-Refusal $noContext) -eq 'HANDLER_MISCONFIGURED') 'refuses a handler without the execution context'
    Assert-True ((Get-BannerFormGaps $noContext).Count -eq 1) 'verify names the misconfigured handler'
    $libOnly = $project -replace '</formLibraries>', "<Library name=""$BannerLibrary"" libraryUniqueId=""{33333333-3333-3333-3333-333333333333}"" /></formLibraries>"
    $r2 = Add-BannerToFormXml $libOnly 'p'
    Assert-True ($r2.Changed -and $r2.Added -eq 1 -and (Get-BannerFormGaps $r2.Xml).Count -eq 0) 'a form with the library but no handler gains only the handler'
    $authAfter = $project -replace '</formLibraries>', '<Library name="z_other.js" libraryUniqueId="{44444444-4444-4444-4444-444444444444}" /></formLibraries>'
    $libsZ = Get-LibraryNames (ConvertTo-XmlDoc (Add-BannerToFormXml $authAfter 'p').Xml)
    Assert-True ($libsZ[-1] -eq $BannerLibrary -and $libsZ[-2] -eq 'z_other.js') 'appended after every existing library (never inserted between others)'

    Write-Host ''
    if ($failures.Count -eq 0) { Write-Host 'SELF-TEST PASS' -ForegroundColor Green; exit 0 }
    Write-Host "SELF-TEST FAIL: $($failures.Count) check(s): $($failures -join '; ')" -ForegroundColor Red
    exit 1
}

if ($SelfTest) { Invoke-SelfTest }

# ============================================================================
# Live helpers
# ============================================================================

$BaseUrl = $EnvironmentUrl.TrimEnd('/')

function Get-DataverseToken {
    $t = az account get-access-token --resource $BaseUrl --query accessToken -o tsv 2>&1
    if ($LASTEXITCODE -ne 0 -or -not $t) { throw "No Dataverse token for $BaseUrl ($t). Run 'az login' first." }
    return "$t".Trim()
}

function Invoke-Dv {
    param([string]$Endpoint, [string]$Method = 'GET', [object]$Body = $null)
    $headers = @{
        Authorization      = "Bearer $Token"
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
        Accept             = 'application/json'
        'Content-Type'     = 'application/json; charset=utf-8'
    }
    $params = @{ Uri = "$BaseUrl/api/data/v9.2/$Endpoint"; Method = $Method; Headers = $headers }
    if ($null -ne $Body) { $params.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 10)) }
    try { return Invoke-RestMethod @params }
    catch {
        $detail = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $err = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($err.error.message) { $detail = $err.error.message }
        }
        throw "API error ($Method $Endpoint): $detail"
    }
}

function Stop-Refused([string]$Code, [string]$Reason) {
    Write-Host ''
    Write-Host "REFUSED ($Code): $Reason" -ForegroundColor Red
    Write-Host 'Nothing was changed.' -ForegroundColor Red
    exit 2
}

function Write-Step([string]$Text) { Write-Host ''; Write-Host "== $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text) { Write-Host "   ok    $Text" -ForegroundColor Green }
function Write-Plan([string]$Text) { Write-Host "   PLAN  $Text" -ForegroundColor Yellow }
function Write-Gap([string]$Text) { $script:Gaps.Add($Text); Write-Host "   GAP   $Text" -ForegroundColor Red }

function Get-TargetForm([string]$Table, [string]$Name) {
    $escaped = $Name.Replace("'", "''")
    $forms = @((Invoke-Dv "systemforms?`$filter=objecttypecode eq '$Table' and type eq 2 and formactivationstate eq 1 and name eq '$escaped'&`$select=formid,name,formxml,ismanaged,objecttypecode").value)
    if ($forms.Count -ne 1) { return $null }
    return $forms[0]
}

function Publish-Tables {
    $publish = '<importexportxml><entities>' + (($Targets | ForEach-Object { "<entity>$($_.Table)</entity>" }) -join '') + '</entities></importexportxml>'
    Invoke-Dv 'PublishXml' 'POST' @{ ParameterXml = $publish } | Out-Null
}

# ============================================================================
# Start
# ============================================================================

$Token = Get-DataverseToken
$Gaps = [System.Collections.Generic.List[string]]::new()
$org = (Invoke-Dv 'organizations?$select=name').value[0].name
$modeText = if ($Verify) { 'VERIFY (read-only)' } elseif ($Apply) { 'APPLY' } elseif ($RestoreFrom) { "RESTORE from $RestoreFrom" } else { 'DRY RUN (no writes)' }
Write-Host "Register-AccessStatusBannerOnForms (task 153)  env: $BaseUrl (org '$org')  mode: $modeText" -ForegroundColor White

if ($RestoreFrom) {
    if (-not (Test-Path -LiteralPath $RestoreFrom)) { Stop-Refused 'SNAPSHOT_MISSING' "snapshot '$RestoreFrom' does not exist." }
    $snap = Get-Content -LiteralPath $RestoreFrom -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $snap.environment -or "$($snap.environment)".TrimEnd('/') -ne $BaseUrl) {
        Stop-Refused 'SNAPSHOT_ENVIRONMENT' "snapshot '$RestoreFrom' was taken in '$($snap.environment)', not in $BaseUrl."
    }
    foreach ($f in @($snap.forms)) {
        $now = (Invoke-Dv "systemforms($($f.formid))?`$select=formxml").formxml
        if ($now -ne $f.after) { Stop-Refused 'FORM_CHANGED' "form '$($f.name)' changed after the apply; restore it by hand from the snapshot." }
    }
    foreach ($f in @($snap.forms)) { Invoke-Dv "systemforms($($f.formid))" 'PATCH' @{ formxml = $f.before } | Out-Null; Write-Ok "form '$($f.name)' restored" }
    Publish-Tables
    Write-Ok 'published'
    exit 0
}

Write-Step 'Prerequisites'
foreach ($lib in $AuthLibrary, $BannerLibrary) {
    $wr = @((Invoke-Dv "webresourceset?`$filter=name eq '$lib'&`$select=webresourceid").value)
    if ($wr.Count -ne 1) {
        if ($Verify -or -not $Apply) { Write-Gap "web resource $lib is not in the environment (deploy it first: scripts/Deploy-WebResourceInline.ps1)"; continue }
        Stop-Refused 'PREREQ_MISSING' "web resource $lib is not in the environment. Deploy it first (scripts/Deploy-WebResourceInline.ps1)."
    }
    Write-Ok "web resource $lib present"
}

$planned = [System.Collections.Generic.List[object]]::new()
foreach ($t in $Targets) {
    Write-Step "$($t.Table): '$($t.Name)'"
    $form = Get-TargetForm $t.Table $t.Name
    if (-not $form) {
        if ($Verify) { Write-Gap "$($t.Table): not exactly one active main form named '$($t.Name)'"; continue }
        Stop-Refused 'FORM_NOT_FOUND' "$($t.Table) has not exactly one active main form named '$($t.Name)'."
    }
    # $formGaps, not $gaps: PowerShell variable names are case-insensitive, and $Gaps is the run's gap list.
    $formGaps = Get-BannerFormGaps $form.formxml
    if ($formGaps.Count -eq 0) { Write-Ok "complete (form $($form.formid))"; continue }
    if ($Verify) { foreach ($g in $formGaps) { Write-Gap "$($t.Table) '$($t.Name)': $g" }; continue }
    foreach ($g in $formGaps) { Write-Plan $g }
    if ($form.ismanaged) { Stop-Refused 'MANAGED_FORM' "$($t.Table) '$($t.Name)' is managed; change it in its owning solution." }
    try { $result = Add-BannerToFormXml $form.formxml "$($form.formid)".ToLowerInvariant() }
    catch [RefusedException] { Stop-Refused $_.Exception.Code "$($t.Table) '$($t.Name)': $($_.Exception.Message)" }
    Write-Plan "add $($result.Added) node(s); every existing library and handler kept, in order"
    $planned.Add([pscustomobject]@{ Table = $t.Table; Name = $form.name; FormId = $form.formid; Before = $form.formxml; After = $result.Xml })
}

if ($Verify) {
    Write-Host ''
    if ($Gaps.Count -eq 0) { Write-Host 'VERIFY PASS: the banner library and its OnLoad handler are registered on every target form.' -ForegroundColor Green; exit 0 }
    Write-Host "VERIFY FAIL: $($Gaps.Count) gap(s)." -ForegroundColor Red
    exit 1
}
if ($planned.Count -eq 0) {
    if ($Gaps.Count -gt 0) { Write-Host "`nDRY RUN: no form change needed, but $($Gaps.Count) prerequisite gap(s) above." -ForegroundColor Yellow; exit 0 }
    Write-Host "`nNothing to change." -ForegroundColor Green; exit 0
}
if (-not $Apply) {
    $extra = if ($Gaps.Count -gt 0) { " $($Gaps.Count) prerequisite gap(s) must be closed before -Apply." } else { '' }
    Write-Host "`nDRY RUN: $($planned.Count) form(s) would change: $(($planned | ForEach-Object { $_.Table }) -join ', ').$extra Re-run with -Apply." -ForegroundColor Cyan
    exit 0
}

Write-Step 'Apply'
New-Item -ItemType Directory -Force -Path $SnapshotDir | Out-Null
$snapPath = Join-Path $SnapshotDir ('access-status-banner-snapshot-{0}.json' -f (Get-Date -Format 'yyyyMMddHHmmss'))
@{
    environment = $BaseUrl
    forms       = @($planned | ForEach-Object { @{ formid = $_.FormId; name = $_.Name; table = $_.Table; before = $_.Before; after = $_.After } })
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $snapPath -Encoding UTF8
Write-Ok "snapshot: $snapPath (restore with -RestoreFrom)"

foreach ($p in $planned) {
    $now = (Invoke-Dv "systemforms($($p.FormId))?`$select=formxml").formxml
    if ($now -ne $p.Before) { Stop-Refused 'FORM_CHANGED' "$($p.Table) '$($p.Name)' changed between the scan and its PATCH; re-run." }
}
foreach ($p in $planned) {
    Invoke-Dv "systemforms($($p.FormId))" 'PATCH' @{ formxml = $p.After } | Out-Null
    Write-Ok "$($p.Table) '$($p.Name)' written"
}
# The read-back must show what was written (the snapshot's "after" is what -RestoreFrom compares against).
foreach ($p in $planned) {
    $back = (Invoke-Dv "systemforms($($p.FormId))?`$select=formxml").formxml
    if ((Get-BannerFormGaps $back).Count -gt 0) { throw "$($p.Table) '$($p.Name)': the read-back is incomplete." }
    if ($back -ne $p.After) {
        # Dataverse may normalise the XML on save: keep the stored text as "after" so -RestoreFrom can match it.
        $p.After = $back
        @{ environment = $BaseUrl; forms = @($planned | ForEach-Object { @{ formid = $_.FormId; name = $_.Name; table = $_.Table; before = $_.Before; after = $_.After } }) } |
            ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $snapPath -Encoding UTF8
    }
}
Publish-Tables
Write-Ok 'published sprk_project, sprk_matter, sprk_workassignment'
Write-Host ''
Write-Host 'APPLIED. Now run this script with -Verify, then the PR #1450 live gates.' -ForegroundColor Green
exit 0
