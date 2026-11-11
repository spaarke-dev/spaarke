<#
.SYNOPSIS
  Split legal-for-cowork-skills.json (from export-skills.js) into one .md per skill.
.EXAMPLE
  ./split-skills.ps1 -Json "$env:USERPROFILE\Downloads\legal-for-cowork-skills.json"
#>
param(
  [Parameter(Mandatory)] [string] $Json,
  [string] $OutDir = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'
$data = Get-Content -LiteralPath $Json -Raw -Encoding UTF8 | ConvertFrom-Json
$utf8 = [System.Text.UTF8Encoding]::new($false)
$nl = "`n"
$index = @("# Legal for Cowork - skills", '',
  "Plugin **$($data.plugin)** by $($data.publisher), version $($data.version), extracted $($data.extracted).", '',
  '| Skill | Description |', '|---|---|')

foreach ($s in $data.skills) {
  $body = @(
    "# $($s.name)", '',
    "*Skill from **$($data.plugin)** ($($data.publisher)), plugin version $($data.version), extracted $($data.extracted).*", '',
    '## Description', '', $s.description.Trim(), '',
    '## Instructions', '', $s.instructions.Trim(), ''
  ) -join $nl
  [System.IO.File]::WriteAllText((Join-Path $OutDir "$($s.name).md"), $body, $utf8)
  $index += "| [$($s.name)]($($s.name).md) | $(($s.description -replace '\s+', ' ' -replace '\|', '\|').Trim()) |"
}
[System.IO.File]::WriteAllText((Join-Path $OutDir 'README.md'), (($index + '') -join $nl), $utf8)
"Wrote $($data.skills.Count) skill files + README.md to $OutDir"
