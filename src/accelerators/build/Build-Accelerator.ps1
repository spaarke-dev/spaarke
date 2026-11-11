<#
.SYNOPSIS
  Build, gate and package one Spaarke practice accelerator (design §5.5).

.DESCRIPTION
  Finds a real Python 3 (never the Windows Store stub), checks the build-time packages, and runs
  build_accelerator.py: topic catalogue -> generated references; playbooks validated and rendered
  for Word; skills assembled; lint gates; engine + gate tests; local end-to-end run; Cowork plugin
  zip, Claude-format plugin zip, per-skill zips; manifest.json with hashes.
  Exit code 0 only when every gate passes.

.EXAMPLE
  ./Build-Accelerator.ps1 -Practice nda
  ./Build-Accelerator.ps1 -Practice nda -SkipTests -Python C:\Python313\python.exe
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)] [string] $Practice,
  [string] $Python = $env:SPRK_PYTHON,
  [string] $Out,
  [switch] $SkipTests,
  [switch] $RegenCorpus
)
$ErrorActionPreference = 'Stop'

function Test-RealPython([string] $exe) {
  if (-not $exe -or -not (Test-Path $exe)) { return $false }
  if ($exe -match '\\WindowsApps\\') { return $false }   # Store stub: hangs or opens the Store
  $v = & $exe -c "import sys; print(sys.version_info >= (3, 9))" 2>$null
  return $v -eq 'True'
}

if (-not (Test-RealPython $Python)) {
  $candidates = @('C:\Python313\python.exe', 'C:\Python312\python.exe', 'C:\Python311\python.exe') +
    (Get-ChildItem "$env:LOCALAPPDATA\Programs\Python\Python3*\python.exe" -ErrorAction SilentlyContinue | ForEach-Object FullName)
  $py = Get-Command py -ErrorAction SilentlyContinue
  if ($py) { $candidates += (& py -3 -c "import sys; print(sys.executable)" 2>$null) }
  $Python = $candidates | Where-Object { Test-RealPython $_ } | Select-Object -First 1
}
if (-not $Python) { throw 'No real Python 3.9+ found. Install Python or pass -Python <path> (not the Windows Store stub).' }
Write-Host "Python: $Python"

$missing = & $Python -c "import importlib.util as u; print(','.join(m for m in ('yaml','docx','lxml') if not u.find_spec(m)))"
if ($missing) { throw "Missing build packages: $missing. Run: & '$Python' -m pip install pyyaml python-docx lxml" }

$argsList = @((Join-Path $PSScriptRoot 'build_accelerator.py'), '--practice', $Practice)
if ($Out) { $argsList += @('--out', $Out) }
if ($SkipTests) { $argsList += '--skip-tests' }
if ($RegenCorpus) { $argsList += '--regen-corpus' }
& $Python @argsList
exit $LASTEXITCODE
