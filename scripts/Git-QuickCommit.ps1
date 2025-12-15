[CmdletBinding()]
param(
	[Parameter(Mandatory = $true)]
	[string]$Message,

	[switch]$Push,

	[switch]$All,

	[string[]]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Exec([string]$Command, [string[]]$Arguments) {
	& $Command @Arguments
	if ($LASTEXITCODE -ne 0) {
		throw "Command failed ($LASTEXITCODE): $Command $($Arguments -join ' ')"
	}
}

Exec git @('status')

if ($All) {
	Exec git @('add', '-A')
}
elseif ($Path -and $Path.Count -gt 0) {
	$addArgs = @('add', '--') + $Path
	Exec git $addArgs
}
else {
	throw "Nothing staged. Use -All or -Path <files> to stage changes before committing."
}

Exec git @('commit', '-m', $Message)

if ($Push) {
	# Push current branch; set upstream if needed
	Exec git @('push', '-u', 'origin', 'HEAD')
}
