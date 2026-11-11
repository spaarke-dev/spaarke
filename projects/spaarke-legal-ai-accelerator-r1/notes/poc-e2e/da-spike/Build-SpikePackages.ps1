#Requires -Version 7
<#
.SYNOPSIS
  Builds the spike DA-1 declarative-agent app packages (zip) for upload as custom apps.
.DESCRIPTION
  Three packages, each a Teams app manifest v1.30 + declarative agent manifest v1.9 with agent skills
  (no knowledge capability, no actions, no secrets, no network calls):
    - Spaarke NDA Review DA         -> skill spaarke-nda-review
    - Spaarke Legal Hold Review DA  -> skill spaarke-legal-hold-review
    - Spaarke Legal Review DA       -> both skills (front door)
  Skill sources: ./skills/<name>/ (SKILL.md + playbook.md). Icons: src/solutions/CopilotAgent/appPackage.
  Spike only; not the product packaging path (that is scripts/copilot-agent/, which refuses capabilities).
.PARAMETER OutDir
  Folder for the zips.
#>
param([Parameter(Mandatory)][string]$OutDir)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = (Resolve-Path "$here/../../../../..").Path
$icons = Join-Path $repo 'src/solutions/CopilotAgent/appPackage'
$enc = New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName System.IO.Compression.FileSystem

$common = 'Results are drafts for attorney review, not legal advice. Do not edit the document unless the user asks for edits in a separate request.'
$agents = @(
  @{ file = 'spaarke-nda-review-da'; id = '817dfafa-76c2-4943-99f8-874ecf0724d8'; name = 'Spaarke NDA Review DA'
     short = 'Reviews a mutual NDA against Acme''s NDA playbook (24 topics).'
     skills = @('spaarke-nda-review')
     instructions = "You are Spaarke NDA Review DA, a reviewer for Acme Company's Legal department. For any request to review, assess or check the open document, use the spaarke-nda-review skill and follow it exactly, including its output format. Begin every review with the line 'Agent: Spaarke NDA Review DA (package) · skill: spaarke-nda-review'. If you cannot load the skill, say so plainly and do not review from memory. $common"
     starters = @(@{ title = 'Review this NDA'; text = 'Review this NDA.' },
                  @{ title = 'Playbook topics'; text = 'Which playbook and version are you using? List all its topics with their Topic IDs.' }) },
  @{ file = 'spaarke-legal-hold-review-da'; id = '70769785-1f82-4f1e-9dd8-7a080ec1d62c'; name = 'Spaarke Legal Hold Review DA'
     short = 'Reviews a draft legal hold notice against Acme''s playbook (13 topics).'
     skills = @('spaarke-legal-hold-review')
     instructions = "You are Spaarke Legal Hold Review DA, a reviewer for Acme Company's Legal department. For any request to review, assess or check the open document, use the spaarke-legal-hold-review skill and follow it exactly, including its output format. Begin every review with the line 'Agent: Spaarke Legal Hold Review DA (package) · skill: spaarke-legal-hold-review'. If you cannot load the skill, say so plainly and do not review from memory. $common"
     starters = @(@{ title = 'Review this notice'; text = 'Review this legal hold notice.' },
                  @{ title = 'Playbook topics'; text = 'Which playbook and version are you using? List all its topics.' }) },
  @{ file = 'spaarke-legal-review-da'; id = 'fd2f828e-88b2-4da1-b3c9-79c83089b815'; name = 'Spaarke Legal Review DA'
     short = 'Reviews an NDA or a legal hold notice against Acme''s playbooks.'
     skills = @('spaarke-nda-review', 'spaarke-legal-hold-review')
     instructions = "You are Spaarke Legal Review DA, a reviewer for Acme Company's Legal department. Work out the type of the open document from its content. For a mutual NDA, use the spaarke-nda-review skill. For a legal hold notice to employee custodians, use the spaarke-legal-hold-review skill. Follow the skill exactly, including its output format. Begin every review with the line 'Agent: Spaarke Legal Review DA (package) · skill: <the skill you used>'. If no skill fits, say what the document appears to be and that no Spaarke playbook covers it yet; do not review it. If you cannot load a skill, say so plainly and do not review from memory. $common"
     starters = @(@{ title = 'Review this document'; text = 'Review this document.' },
                  @{ title = 'Skills'; text = 'Which skills do you have?' }) }
)

New-Item -ItemType Directory -Force $OutDir | Out-Null
foreach ($a in $agents) {
  $stage = Join-Path ([IO.Path]::GetTempPath()) ("da1-" + [guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory $stage | Out-Null
  try {
    Copy-Item (Join-Path $icons 'color.png'), (Join-Path $icons 'outline.png') $stage
    foreach ($s in $a.skills) {
      $d = Join-Path $stage "skills/$s"; New-Item -ItemType Directory -Force $d | Out-Null
      Copy-Item (Join-Path $here "skills/$s/SKILL.md"), (Join-Path $here "skills/$s/playbook.md") $d
    }
    if ($a.instructions.Length -gt 8000) { throw "$($a.name): instructions over 8,000 characters" }
    $da = [ordered]@{
      '$schema' = 'https://developer.microsoft.com/json-schemas/copilot/declarative-agent/v1.9/schema.json'
      version = 'v1.9'
      name = $a.name
      description = $a.short
      instructions = $a.instructions
      conversation_starters = $a.starters
      agent_skills = @($a.skills | ForEach-Object { [ordered]@{ folder = "./skills/$_" } })
    }
    $app = [ordered]@{
      '$schema' = 'https://developer.microsoft.com/json-schemas/teams/v1.30/MicrosoftTeams.schema.json'
      manifestVersion = '1.30'
      version = '0.1.0'
      id = $a.id
      developer = [ordered]@{ name = 'Spaarke'; websiteUrl = 'https://spaarke.com'; privacyUrl = 'https://spaarke.com/privacy'; termsOfUseUrl = 'https://spaarke.com/terms' }
      name = [ordered]@{ short = $a.name; full = "$($a.name) (spike DA-1)" }
      description = [ordered]@{ short = $a.short; full = "$($a.short) Spike DA-1 test agent; results are drafts for attorney review." }
      icons = [ordered]@{ color = 'color.png'; outline = 'outline.png' }
      accentColor = '#4F6BED'
      copilotAgents = [ordered]@{ declarativeAgents = @([ordered]@{ id = ($a.file -replace '-', ''); file = 'declarativeAgent.json' }) }
    }
    if ($a.name.Length -gt 30 -or $a.short.Length -gt 80) { throw "$($a.name): name or short description too long" }
    [IO.File]::WriteAllText((Join-Path $stage 'declarativeAgent.json'), ($da | ConvertTo-Json -Depth 8), $enc)
    [IO.File]::WriteAllText((Join-Path $stage 'manifest.json'), ($app | ConvertTo-Json -Depth 8), $enc)
    $zip = Join-Path $OutDir "$($a.file).zip"
    if (Test-Path $zip) { Remove-Item $zip }
    # Zip entries must use forward slashes; CreateFromDirectory on .NET Core does.
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
    $z = [IO.Compression.ZipFile]::OpenRead($zip)
    "{0}: {1}" -f (Split-Path $zip -Leaf), (($z.Entries | ForEach-Object FullName) -join ', ')
    $z.Dispose()
  }
  finally { Remove-Item -Recurse -Force $stage }
}
