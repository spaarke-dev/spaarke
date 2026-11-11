<#
.SYNOPSIS
  Build, gate and zip the individual-base package of the Spaarke Legal AI Accelerator.

.DESCRIPTION
  Finds a real Python 3 (never the Windows Store stub), checks the build-time packages, runs the NDA build
  (its skills go into the package; skip with -SkipNdaBuild when it is current), then build_package.py:
  library playbooks rendered (four-column and readable), skills assembled, review method and Start here
  documents written, package gates (unique file names, path length, custom instruction length), and two
  zips under build/out/package/:
    Spaarke Legal Accelerator - Individual base v0.1.zip   (extract into OneDrive Documents)
    Spaarke test documents v0.1.zip                        (test files, decoy, Screens folder)
  Exit code 0 only when every gate passes.

.EXAMPLE
  ./Build-Package.ps1
  ./Build-Package.ps1 -SkipNdaBuild -Python C:\Python313\python.exe
#>
[CmdletBinding()]
param(
  [string] $Python = $env:SPRK_PYTHON,
  [string] $Out,
  [string] $Kit,
  [switch] $SkipNdaBuild
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

if (-not $SkipNdaBuild) {
  $nda = @{ Practice = 'nda'; Python = $Python }
  if ($Out) { $nda.Out = $Out }
  & (Join-Path $PSScriptRoot 'Build-Accelerator.ps1') @nda
  if ($LASTEXITCODE -ne 0) { throw 'The NDA build failed; the package needs its skills.' }
}

$argsList = @((Join-Path $PSScriptRoot 'build_package.py'))
if ($Out) { $argsList += @('--out', $Out) }
if ($Kit) { $argsList += @('--kit', $Kit) }
& $Python -B @argsList
exit $LASTEXITCODE
