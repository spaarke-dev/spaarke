<#
.SYNOPSIS
    Classifies every BFF unit-test method against the ADR-038 §7 build-vs-maintain bans.

.DESCRIPTION
    Task CICD-082 (spec FR-B10). Emits one CSV row per test method:
    KEEP-maintain / DELETE-scaffolding / AMBIGUOUS, tagged with the ban that fired.

    WHY A CLASSIFIER AND NOT A READ-THROUGH: the target is 592 files / 7,327 test
    methods / 211k LOC. Hand-reading that is neither feasible nor repeatable, and
    the ADR deliberately expresses its bans as *detectable patterns* so they can be
    applied uniformly. The POML's step 8 (spot-check 10 random DELETE tags) is the
    accuracy control on this approach, and it is a control that only makes sense
    against a mechanical pass.

    DELIBERATELY CONSERVATIVE. This script only emits DELETE for bans it can detect
    with high confidence from source. Everything it is unsure about becomes
    AMBIGUOUS, never DELETE. Under-calling costs a human review pass; over-calling
    deletes a regression test, and ADR-038's own rule is "doubt = KEEP".

    Bans detected here (8 of 17):
      B1  Mock<HttpMessageHandler>                     high confidence
      B3  DI-registration test                         high
      B4  constructor null-check test                  high
      B8  private/internal access via reflection       high
      B10 coverage-filler (no meaningful assertion)    medium-high
      B13 test name lacking scenario+expected          high (name-only)
      B15 setup-to-assertion ratio > 10:1              medium
      B16 getter/setter/auto-property round-trip       medium

    NOT detected -- these need human judgment and land as AMBIGUOUS if nothing
    else fires: B2, B5, B6 (mirror tests), B7, B9, B11, B12, B14, B17.

.PARAMETER TestRoot
    Root of the test project to classify.

.PARAMETER OutCsv
    Destination CSV.

.NOTES
    Read-only over the test tree. Writes only the CSV.
#>

[CmdletBinding()]
param(
    [string] $TestRoot = 'tests/unit/Sprk.Bff.Api.Tests',
    [string] $OutCsv   = 'projects/ci-cd-unit-test-remediation-r1/notes/test-inventory-broader.csv'
)

$ErrorActionPreference = 'Stop'

# ADR-038 §2 protected KEEP paths. A test under these is NEVER auto-DELETE:
# spec FR-B06 requires a same-PR replacement plan, which is a human decision.
$ProtectedPathFragments = @(
    'tests/integration/auth/',
    'tests/integration/regression/',
    'tests/integration/data-mutation/',
    'tests/integration/tenant/',
    'tests/integration/contract/',
    'tests/Spaarke.ArchTests/'      # Amendment A1 -- structural fitness functions
)

# Strip C# comments so ban detection sees code only. Added by spot-check round 3.
# Order matters: block comments first, then line comments. String literals containing
# "//" are rare in this suite and a stray strip only ever LOSES a signal (under-call),
# which is the safe direction per ADR-038's doubt = KEEP rule.
function Remove-CsComments {
    param([string] $Text)
    $noBlock = [regex]::Replace($Text, '/\*[\s\S]*?\*/', ' ')
    return [regex]::Replace($noBlock, '(?m)//.*$', '')
}

Write-Host "Classifying tests under $TestRoot against ADR-038 §7 ..." -ForegroundColor Cyan

$files = Get-ChildItem -Path $TestRoot -Filter *.cs -Recurse -File
Write-Host ("  files: {0}" -f $files.Count)

$rows = [System.Collections.Generic.List[object]]::new()

foreach ($file in $files) {
    $lines    = [System.IO.File]::ReadAllLines($file.FullName)
    $relPath  = (Resolve-Path -Relative $file.FullName) -replace '\\', '/' -replace '^\./', ''
    $fileText = $lines -join "`n"
    $fileLen  = $lines.Count

    $isProtected = $false
    foreach ($frag in $ProtectedPathFragments) {
        if ($relPath -like "*$frag*") { $isProtected = $true; break }
    }

    # Comment-stripped view of the file. Ban detection MUST run against code, never
    # against prose. Spot-check round 3 caught the reason: AiCompletionNodeExecutorTests
    # carries the header comment "ADR-038 compliance: NO Mock<HttpMessageHandler>", and
    # both the file-level flag below and the per-method body match fired on it. A file
    # that DOCUMENTS its compliance was classified as violating it.
    $fileTextCode = Remove-CsComments $fileText

    # File-level signals (apply to every method in the file).
    $fileHasHttpMessageHandlerMock = $fileTextCode -match 'Mock<\s*HttpMessageHandler\s*>'
    $fileHasReflection             = $fileTextCode -match 'BindingFlags\.(NonPublic|Instance\s*\|\s*BindingFlags\.NonPublic)'

    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -notmatch '^\s*\[\s*(Fact|Theory)\b') { continue }

        # Walk forward to the method signature (skip further attributes / InlineData).
        $sigIdx = -1
        for ($j = $i + 1; $j -lt [math]::Min($i + 40, $lines.Count); $j++) {
            if ($lines[$j] -match '^\s*(public|private|internal|protected)\s+.*\b(\w+)\s*\(') { $sigIdx = $j; break }
        }
        if ($sigIdx -lt 0) { continue }

        if ($lines[$sigIdx] -notmatch '\b(?<name>\w+)\s*\(') { continue }
        $methodName = $Matches['name']

        # Capture the body by brace balance, starting at the first '{' at/after the signature.
        $depth = 0; $started = $false; $bodyLines = @(); $endIdx = $sigIdx
        for ($k = $sigIdx; $k -lt [math]::Min($sigIdx + 400, $lines.Count); $k++) {
            $line = $lines[$k]
            $opens  = ([regex]::Matches($line, '\{')).Count
            $closes = ([regex]::Matches($line, '\}')).Count
            if (-not $started -and $opens -gt 0) { $started = $true }
            if ($started) {
                $bodyLines += $line
                $depth += $opens - $closes
                if ($depth -le 0) { $endIdx = $k; break }
            }
        }
        $body       = $bodyLines -join "`n"
        $methodLen  = $bodyLines.Count

        # Ban detection runs against $bodyCode (comments removed); assertion/setup
        # accounting keeps using $body. See Remove-CsComments for why (round 3).
        $bodyCode   = Remove-CsComments $body

        # --- assertion / setup accounting -------------------------------------
        $assertCount = ([regex]::Matches($body, '\bAssert\.|\.Should\(\)|\bVerify\(|\bVerifyAll\(')).Count
        $mockCount   = ([regex]::Matches($body, '\bMock<|\bnew Mock')).Count
        $codeLines   = @($bodyLines | Where-Object { $_ -match '\S' -and $_ -notmatch '^\s*//' -and $_ -notmatch '^\s*[{}]\s*$' }).Count
        $setupLines  = [math]::Max(0, $codeLines - $assertCount)

        # "Meaningful assertion" must be generous, because a FALSE POSITIVE HERE
        # DELETES A REAL REGRESSION TEST. The first version of this list omitted
        # Assert.Null/Empty/Single and flagged 2,276 methods as coverage-fillers --
        # including LoadSessionAsync_BothMiss_ReturnsNull, whose entire contract IS
        # "returns null". Spot-check (POML step 8) caught it. Anything that pins a
        # specific expected outcome counts, including a null/empty outcome.
        $meaningfulXunit = '\bAssert\.(Equal|NotEqual|True|False|Null|NotNull|Empty|NotEmpty|Single|Contains|DoesNotContain|Matches|Throws|ThrowsAsync|Same|NotSame|Collection|Multiple|All|InRange|NotInRange|IsType|IsNotType|IsAssignableFrom|Fail|StartsWith|EndsWith|Subset|Superset|Equivalent)\b'
        $meaningfulFluent = '\.Should\(\)\s*\.\s*(Be|BeEquivalentTo|BeNull|NotBeNull|BeEmpty|NotBeEmpty|BeTrue|BeFalse|Contain|NotContain|Match|Throw|ThrowAsync|NotThrow|HaveCount|Equal|BeOfType|BeAssignableTo|BeGreaterThan|BeLessThan|BeInRange|StartWith|EndWith|ContainSingle|OnlyContain|BeSameAs)\b'
        $meaningfulMoq   = '\.Verify\w*\(\s*\w+\s*=>'   # Verify with an expression pins an interaction

        $trivialOnly = ($assertCount -gt 0) -and
                       (-not ($body -match $meaningfulXunit)) -and
                       (-not ($body -match $meaningfulFluent)) -and
                       (-not ($body -match $meaningfulMoq))

        # --- ban detection (first match wins; ordered by confidence) ----------
        $bucket = $null; $rationale = $null

        # B1 — require the actual Mock<HttpMessageHandler> construction in this method's
        # code, not a bare mention of the type name. Round 3: the old `-match
        # 'HttpMessageHandler'` fired on a comment asserting ADR-038 compliance.
        if ($fileHasHttpMessageHandlerMock -and $bodyCode -match 'Mock<\s*HttpMessageHandler\s*>') {
            $bucket = 'B1-http-message-handler-mock'
            $rationale = 'Mocks HttpMessageHandler — couples the test to wire format (ADR-038 B1)'
        }
        # B3 — but NOT the ADR-032 Null-Object kill-switch contract. Those tests assert
        # WHICH concrete implementation resolves under a given feature-flag state; app
        # start does not prove that, and root CLAUDE.md §10 / bff-extensions.md §F.1 make
        # it a binding sub-mechanism (the RB-T028-03..06 defect class). Round 3 caught the
        # 8 TodoSyncModule FlagOn/FlagOff tests and the 2 CacheModule Redis on/off tests
        # heading for deletion — they are the regression cover for a real production bug.
        # B3 requires an actual DI container, and an assertion that goes no further than
        # "it resolved". Two round-3/4 over-calls fixed here:
        #   (a) bare `\bGetService\b` collided with DOMAIN methods of the same name.
        #       ExportServiceRegistry.GetService(ExportFormat.Docx) is a strategy selector
        #       built with `new`, nothing to do with IServiceProvider. Now the body must
        #       actually build a container.
        #   (b) asserting WHICH concrete type resolves is a different contract from
        #       asserting THAT it resolves, and app start proves only the latter. This
        #       covers the ADR-032 kill-switch pairs (root CLAUDE.md §10 / bff-extensions
        #       §F.1, the RB-T028-03..06 defect class) and also phase-pinning stubs like
        #       BeOfType<StubInsightGraph>. Both land in review, never in DELETE.
        elseif ($bodyCode -match '\bBuildServiceProvider\b' -and $assertCount -le 3 -and $bodyCode -match 'NotNull|IsType|BeOfType|BeAssignableTo') {
            $pinsConcreteImpl = $bodyCode -match '(BeOfType|BeAssignableTo|IsType|IsAssignableFrom)\s*<'
            if ($pinsConcreteImpl) {
                $bucket = 'AMBIGUOUS-adr032-killswitch'
                $rationale = 'Resolves a service BUT pins WHICH impl — ADR-032 kill-switch / phase contract, not B3 wiring'
            } else {
                $bucket = 'B3-di-registration'
                $rationale = 'Asserts a service resolves from the container — app start already proves wiring (B3)'
            }
        }
        # B4 — the ACT must be the construction itself. Two round-3 over-calls fixed here:
        #   (a) `-match` is case-INSENSITIVE in PowerShell, so `[^)]*null` matched the
        #       `Null...` in every ADR-032 Null-Object type name (e.g.
        #       `new NullMembershipEventPublisher(Mock.Of<ILogger<NullMembershipEventPublisher>>())`).
        #       Now `-cmatch`, so only the C# keyword `null` counts.
        #   (b) building a DTO with a null field and then calling a METHOD matched too
        #       (`new EffortScoreInput(null!, ...)` then `_sut.CalculateEffortScore(input)`).
        #       Requiring `=> new X(` keeps only tests whose subject is construction.
        elseif ($bodyCode -match 'ArgumentNullException' -and $bodyCode -cmatch '=>\s*new\s+\w+\s*\([^)]*\bnull\b') {
            $bucket = 'B4-ctor-null-check'
            $rationale = 'Constructor null-guard test — ArgumentNullException.ThrowIfNull covers this (B4)'
        }
        # B8 — require BindingFlags in THIS method. `GetProperty(` alone also matches
        # JsonElement.GetProperty("field"), which is JSON navigation, not reflection over
        # a private member. Round 3: that misread DailyBriefingResponseShapeTests, a golden
        # fixture locking the widget-consumed JSON contract, as an implementation-shape test.
        elseif ($fileHasReflection -and $bodyCode -match 'BindingFlags\.') {
            $bucket = 'B8-private-via-reflection'
            $rationale = 'Reaches a private/internal member by reflection — locks implementation shape (B8)'
        }
        # Absence-of-throw can BE the contract. Round 4 surfaced this while checking the
        # ADR-032 rescues: FlagOff_NullTodoGraphSyncHandler_IsQuietNoOp has no assertion
        # because "completes without throwing" is exactly the ADR-032 P2 quiet semantics
        # it exists to pin. Same shape as
        # LogInteractionAsync_CosmosThrows_DoesNotThrowToCallerAndLogsError (audit logging
        # must not break its caller) — a resilience contract, not coverage filler. This is
        # round 1's failure mode (LoadSessionAsync_BothMiss_ReturnsNull, whose contract was
        # a null outcome) recurring in a different bucket: a NEGATIVE expected outcome
        # reads as "no expectation" to a counter. Route to review, never to DELETE.
        elseif (($assertCount -eq 0 -or $trivialOnly) -and
                $methodName -match '(?i)(NoOp|No_Op|DoesNotThrow|DoNotThrow|Quiet|Tolerat|Ignor|Succeed|Complet|Safe|Never)') {
            $bucket = 'AMBIGUOUS-b10-absence-contract'
            $rationale = 'No/trivial assertion BUT the name states absence-of-throw as the contract — review, do not auto-delete'
        }
        elseif ($assertCount -eq 0) {
            $bucket = 'B10-coverage-filler'
            $rationale = 'No assertion — executes code without asserting behavior (B10)'
        }
        elseif ($trivialOnly) {
            $bucket = 'B10-coverage-filler'
            $rationale = 'Only trivial assertions (NotNull / does-not-throw) — coverage without behavior (B10)'
        }
        elseif ($methodName.Split('_').Count -lt 3) {
            $bucket = 'B13-name-missing-scenario'
            $rationale = "Name '$methodName' lacks {Method}_{Scenario}_{Expected} — reader cannot defend it (B13)"
        }
        elseif ($assertCount -gt 0 -and ($setupLines / [math]::Max(1, $assertCount)) -gt 10) {
            $bucket = 'B15-setup-heavy'
            $rationale = ("Setup:assertion ratio {0}:1 exceeds 10:1 — setup IS the test logic (B15)" -f [math]::Round($setupLines / [math]::Max(1, $assertCount)))
        }
        # B16 must anchor on WORD boundaries. The first version used a bare
        # `(get|set)_?` alternation, which matched the tail of ordinary words --
        # NoClo[set]_, StartOff[set]_, FireAndFor[get]_ -- and flagged three
        # behavioral tests as auto-property round-trips. Caught by spot-check
        # round 2. A property test also has to actually touch a property, so
        # require corroboration in the body rather than trusting the name alone.
        elseif ($methodName -match '(?i)(^|_)(Getter|Setter|Property|RoundTrip)(_|$)' -and
                $body -match '(?i)\b(get|set)\s*;|\.\w+\s*=\s*\w+\s*;[\s\S]*Assert') {
            $bucket = 'B16-property-roundtrip'
            $rationale = 'Auto-property round-trip — the language guarantees this (B16)'
        }
        elseif ($mockCount -ge 3 -and $assertCount -le 1) {
            $bucket = 'B7-all-mocks-trivial'
            $rationale = "All-mocks ($mockCount) with <=1 assertion — locks interaction shape (B7)"
        }

        # Not every ban justifies deletion. Three have a NON-DELETE first remedy in
        # ADR-038's own "Acceptable replacement" column, so auto-DELETE would skip
        # the cheaper fix and destroy a possibly-good test:
        #   B13 -> "Rename per convention or delete"   (rename comes first)
        #   B15 -> "Integration test with amortized setup"  (a smell, not proof)
        #   B7  -> "Integration test or delete"        (needs a human read)
        # AMBIGUOUS by construction. The spot-check confirmed the risk:
        # EvaluateAsync_MonthSnapshot_DoesNotTriggerBudgetRules is setup-heavy AND
        # a legitimate behavioral test.
        # 'AMBIGUOUS-adr032-killswitch' is review-only for a different reason than the
        # other three: it is not a ban at all. It marks a DI-resolution test that pins
        # which impl a feature flag selects, i.e. ADR-032 contract cover that B3 would
        # otherwise eat. Listed here so it can never reach DELETE.
        $reviewOnlyBuckets = @('B13-name-missing-scenario', 'B15-setup-heavy', 'B7-all-mocks-trivial',
                               'AMBIGUOUS-adr032-killswitch', 'AMBIGUOUS-b10-absence-contract')

        if ($bucket) {
            $classification =
                if ($isProtected)                        { 'AMBIGUOUS' }
                elseif ($reviewOnlyBuckets -contains $bucket) { 'AMBIGUOUS' }
                else                                     { 'DELETE-scaffolding' }
            if ($isProtected) {
                $rationale = "PROTECTED KEEP PATH — ban $bucket fired but FR-B06 requires a same-PR replacement plan; human decision. ($rationale)"
                $bucket    = "PROTECTED::$bucket"
            }
        }
        else {
            $classification = 'KEEP-maintain'
            $bucket         = ''
            $rationale      = 'No ADR-038 §7 ban detected'
        }

        $rows.Add([pscustomobject]@{
            file_path         = $relPath
            test_method_name  = $methodName
            classification    = $classification
            antipattern_bucket= $bucket
            rationale         = $rationale
            file_lines        = $fileLen
            method_lines      = $methodLen
        })

        $i = $endIdx
    }
}

$outDir = Split-Path -Parent $OutCsv
if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
$rows | Export-Csv -Path $OutCsv -NoTypeInformation -Encoding UTF8

$del  = @($rows | Where-Object classification -eq 'DELETE-scaffolding')
$keep = @($rows | Where-Object classification -eq 'KEEP-maintain')
$amb  = @($rows | Where-Object classification -eq 'AMBIGUOUS')

Write-Host ''
Write-Host ("  methods classified : {0}" -f $rows.Count) -ForegroundColor Cyan
Write-Host ("  DELETE-scaffolding : {0}" -f $del.Count)  -ForegroundColor Yellow
Write-Host ("  KEEP-maintain      : {0}" -f $keep.Count) -ForegroundColor Green
Write-Host ("  AMBIGUOUS          : {0}" -f $amb.Count)
Write-Host ''
Write-Host '  By bucket:'
$del | Group-Object antipattern_bucket | Sort-Object Count -Descending |
    ForEach-Object { Write-Host ("    {0,-34} {1,6}" -f $_.Name, $_.Count) }
Write-Host ''
Write-Host ("  CSV: {0}" -f $OutCsv) -ForegroundColor Cyan
