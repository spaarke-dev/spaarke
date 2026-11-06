# Deploy-Release.ps1 reference

Lookup tables for the `deploy-new-release` skill: every parameter of `scripts/Deploy-Release.ps1`, and the shape of the environment registry. Moved out of SKILL.md on 2026-11-03 (verbatim).

## Parameters for Deploy-Release.ps1

| Parameter | Type | Default | Notes |
|-----------|------|---------|-------|
| `-EnvironmentUrl` | string[] | (required) | Environment names or Dataverse URLs |
| `-CustomerId` | string | **(required)** | Phase 4 target via the `sprk_dataverseenvironment` registry; `^[a-z][a-z0-9]{2,7}$`. Dev = `dev`. See Step 5 |
| `-AdminEnvironmentUrl` | string | `$env:SPAARKE_ADMIN_DATAVERSE_URL`, else the registry's platform-admin entry, else the first non-template entry (dev) | Where the registry lives |
| `-Version` | string | auto-suggested | `v{major}.{minor}.{patch}` format |
| `-SkipPhase` | string[] | none | `Build`, `BffApi`, `OntologyConfig`, `Solutions`, `WebResources`, `Validation` |
| `-SkipBuild` | switch | $false | Shortcut for `-SkipPhase Build` |
| `-StopOnFailure` | bool | $true | Stop remaining environments on failure |
| `-ClientSecret` | string | (none) | **Ignored since T218f (2026-10-08)** — prints a warning. Phase 3 imports SpaarkeMaster with the operator's own az/pac sign-in; no service principal secret is used anywhere in the release. Kept so older invocations do not break. |
| `-WhatIf` | switch | $false | Preview without executing |

---

## Environment Registry Format

Environments are defined in `config/environments.json` (read it at runtime; this excerpt shows the shape and the dev values as of 2026-10-11):

```json
{
  "environments": {
    "dev": {
      "solutionPackageType": "none",
      "displayName": "Development",
      "dataverseUrl": "https://spaarkedev1.crm.dynamics.com",
      "bffApiUrl": "https://spaarke-bff-dev.azurewebsites.net",
      "appServiceName": "spaarke-bff-dev",
      "appServiceSlot": "staging",
      "resourceGroup": "rg-spaarke-dev",
      "keyVaultName": "spaarke-spekvcert",
      "tenantId": "a221a95e-..."
    }
  }
}
```
