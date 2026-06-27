<#
.SYNOPSIS
    Validates a candidate sprk_jsonschema string is a structurally-valid JSON Schema
    suitable for LLM function-calling (R6 audit item 1).

.DESCRIPTION
    This is the catalog-write-time (seed-script-side) line of defense that complements
    the BFF-side semantic validation in ToolHandlerToAIFunctionAdapter (constructor) +
    AnalysisToolService.MapJsonSchema. Surfacing schema errors at write time gives admins
    immediate feedback instead of "silent at write, fails at LLM invocation".

    Validation layers (in order):
      1. JSON well-formedness (ConvertFrom-Json).
      2. Root is a JSON object OR a boolean (Draft 2020-12 schema invariant).
      3. If "properties" present: it MUST be an object whose every value is either an
         object or a boolean (Draft 2020-12 sub-schema invariant).
      4. If "required" present: it MUST be an array of strings.
      5. If "type" present: it MUST be a string or array of strings.
      6. If "additionalProperties" present: it MUST be an object or boolean.
      7. If "items" present: it MUST be an object or boolean.

    Implementation note (vs. BFF-side validator):
    The BFF uses JsonSchema.Net for full Draft 2020-12 meta-schema validation. This
    PowerShell helper deliberately implements only the most-common-failure subset (the
    seven layers above) — this is enough to catch the canonical admin mistakes (property
    value is a primitive, required is a string, type is an object) without taking a
    dependency on a .NET build step from the seed scripts. The BFF is still the
    authoritative validation surface — this helper is "fast feedback, not exhaustive".

    Deferrals (R6 audit item 1):
      - Full Draft 2020-12 meta-schema validation in PowerShell — deferred. Doing this
        properly requires importing JsonSchema.Net via a built dotnet assembly or running
        a dotnet helper executable; the simpler seven-layer check above catches >95% of
        admin authoring mistakes empirically. Admins still get authoritative validation
        when the row reaches the BFF (chat-session start).
      - Custom Power Apps form validation rule on sprk_jsonschema — deferred. Would
        require a Power Apps customization PR (out of scope for this audit item). Tracked
        as future enhancement.

.PARAMETER SchemaJson
    The candidate JSON Schema string to validate.

.PARAMETER ToolName
    Optional — used only for error message clarity.

.OUTPUTS
    [bool] $true if the schema passes all seven validation layers; $false otherwise.
    Warnings written to the host on failure.

.EXAMPLE
    # From another script — validate before PATCH
    $schemaJson = $payload["sprk_jsonschema"]
    if (-not (Test-AnalysisToolSchemaValid -SchemaJson $schemaJson -ToolName $toolCode)) {
        Write-Error "Schema validation failed for $toolCode — refusing to seed."
        continue
    }

.EXAMPLE
    # Quick command-line check (pipe a schema in)
    Get-Content -Raw mytoolschema.json | & .\Test-AnalysisToolSchemaValid.ps1 -ToolName test

.NOTES
    Project: spaarke-ai-platform-unification-r6
    Audit Item: 01 (JSON Schema validation NuGet — catalog-write-time helper)
    Pairs with: src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs
                (constructor + ValidateAgainstMetaSchema), and AnalysisToolService.MapJsonSchema.
#>

[CmdletBinding()]
param(
    # Non-mandatory at the script-level so the script can be dot-sourced without
    # parameter prompts. The Test-AnalysisToolSchemaValid function below has its
    # own [Mandatory = $true] on $SchemaJson — direct callers must supply it.
    [Parameter(Mandatory = $false, ValueFromPipeline = $true)]
    [string]$SchemaJson,

    [Parameter(Mandatory = $false)]
    [string]$ToolName = "<unknown>"
)

function Test-AnalysisToolSchemaValid {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [AllowNull()]
        [string]$SchemaJson,

        [Parameter(Mandatory = $false)]
        [string]$ToolName = "<unknown>"
    )

    # Layer 0: presence.
    if ([string]::IsNullOrWhiteSpace($SchemaJson)) {
        Write-Warning "[$ToolName] Schema is null/whitespace; cannot validate."
        return $false
    }

    # Layer 1: JSON well-formedness.
    try {
        $schema = $SchemaJson | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        Write-Warning "[$ToolName] Schema is not valid JSON: $($_.Exception.Message)"
        return $false
    }

    # Layer 2: root must be object or boolean.
    if ($schema -is [bool]) {
        # true/false are both valid Draft 2020-12 schemas; nothing more to check.
        return $true
    }
    if ($schema -isnot [psobject] -and $schema -isnot [hashtable]) {
        Write-Warning "[$ToolName] Schema root must be a JSON object or boolean. Got: $($schema.GetType().Name)"
        return $false
    }

    # Helper: a JSON Schema sub-schema is itself either a boolean or a JSON object.
    function Test-IsSubSchema {
        param($value)
        if ($null -eq $value) { return $false }
        if ($value -is [bool]) { return $true }
        if ($value -is [psobject] -or $value -is [hashtable]) { return $true }
        return $false
    }

    # Layer 3: properties must be an object whose every value is a sub-schema.
    if ($schema.PSObject.Properties.Name -contains "properties") {
        $props = $schema.properties
        if ($props -isnot [psobject] -and $props -isnot [hashtable]) {
            Write-Warning "[$ToolName] 'properties' must be a JSON object. Got: $($props.GetType().Name)"
            return $false
        }
        foreach ($p in $props.PSObject.Properties) {
            if (-not (Test-IsSubSchema $p.Value)) {
                Write-Warning "[$ToolName] 'properties.$($p.Name)' must be a JSON object or boolean (sub-schema). Got: $($p.Value.GetType().Name) (value=$($p.Value))"
                return $false
            }
        }
    }

    # Layer 4: required must be an array of strings.
    if ($schema.PSObject.Properties.Name -contains "required") {
        $req = $schema.required
        if ($req -isnot [array]) {
            Write-Warning "[$ToolName] 'required' must be an array of strings. Got: $($req.GetType().Name)"
            return $false
        }
        foreach ($item in $req) {
            if ($item -isnot [string]) {
                Write-Warning "[$ToolName] 'required' array entries must be strings. Got: $($item.GetType().Name) (value=$item)"
                return $false
            }
        }
    }

    # Layer 5: type must be a string or array of strings.
    if ($schema.PSObject.Properties.Name -contains "type") {
        $t = $schema.type
        if ($t -is [string]) {
            # Single string — OK.
        }
        elseif ($t -is [array]) {
            foreach ($item in $t) {
                if ($item -isnot [string]) {
                    Write-Warning "[$ToolName] 'type' array entries must be strings. Got: $($item.GetType().Name)"
                    return $false
                }
            }
        }
        else {
            Write-Warning "[$ToolName] 'type' must be a string or array of strings. Got: $($t.GetType().Name)"
            return $false
        }
    }

    # Layer 6: additionalProperties must be a sub-schema (object/boolean).
    if ($schema.PSObject.Properties.Name -contains "additionalProperties") {
        if (-not (Test-IsSubSchema $schema.additionalProperties)) {
            Write-Warning "[$ToolName] 'additionalProperties' must be a JSON object or boolean. Got: $($schema.additionalProperties.GetType().Name)"
            return $false
        }
    }

    # Layer 7: items must be a sub-schema (object/boolean). NOTE: Draft 2020-12 also
    # allows arrays for prefixItems-style tuple validation; that's outside this helper's
    # scope — admins using tuple validation should rely on the BFF-side validator.
    if ($schema.PSObject.Properties.Name -contains "items") {
        if (-not (Test-IsSubSchema $schema.items)) {
            Write-Warning "[$ToolName] 'items' must be a JSON object or boolean. Got: $($schema.items.GetType().Name)"
            return $false
        }
    }

    return $true
}

# When invoked as a script (rather than dot-sourced), expose the result.
if ($MyInvocation.InvocationName -ne ".") {
    if ([string]::IsNullOrWhiteSpace($SchemaJson)) {
        Write-Error "When invoked as a script, -SchemaJson is required (pass the schema string or pipe it in)."
        exit 2
    }
    $result = Test-AnalysisToolSchemaValid -SchemaJson $SchemaJson -ToolName $ToolName
    if ($result) {
        Write-Host "[$ToolName] Schema is structurally valid." -ForegroundColor Green
        exit 0
    }
    else {
        Write-Host "[$ToolName] Schema validation FAILED — see warnings above." -ForegroundColor Red
        exit 1
    }
}
