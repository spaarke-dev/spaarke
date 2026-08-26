# Resource Name Availability Pre-Check Pattern

> **Last Reviewed**: 2026-08-25
> **Reviewed By**: customer-provisioning-orchestration-r1 task 203a per punch list row A07
> **Status**: Content filled (task 203a). Was skeleton from task 202.

## When

Load this pattern when:
- Provisioning a resource with a GLOBAL Azure namespace (Service Bus namespace, Storage account, Cognitive Services subdomain, Front Door, Azure Container Registry).
- Extending `bicepparam` files with resource names that must be globally unique.
- Debugging a deploy that fails at 16+ minutes with `NameAvailabilityError` or `ConflictingResourceName`.
- Adding a new global-namespace resource type to the platform.

## Read These Files (canonical source)

1. `projects/customer-provisioning-orchestration-r1/notes/lessons-learned-model1-prod-standup-2026-08-22.md` § F10 — original discovery. `sprksharedprod-sb` failed at 16m35s because Service Bus reserves `-sb` as a suffix.
2. `docs/guides/PROVISIONING-PREREQUISITES.md` PRQ-C-03 (global resource-name availability check) — the codified prereq.
3. `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §2.5 — existing naming collision check runbook.
4. `scripts/provisioning-prereqs/prereqs.yaml` — machine-parseable prereq registry (Step 0.5 of `/provision-environment` reads this).

## Constraints

- Global namespace check MUST run at Step 2 preflight, BEFORE `az deployment sub create`. Failing at Step 2 → operator adjusts `bicepparam` before ANY resource is created; failing at deploy time → 16+ minutes wasted plus a partial-deploy rollback.
- `az deployment sub what-if` does NOT catch global-namespace collisions — the What-If evaluator asks "would the deploy succeed given current state?" but doesn't call the resource-type `checkNameAvailability` API.
- Reserved suffixes MUST be documented (Service Bus reserves `-sb`; others surface as they're discovered). Maintain the reserved-suffix registry in `scripts/provisioning-prereqs/reserved-suffixes.yaml`.
- Handler-side H2a MUST emit a rejection code if a global-name collision surfaces at `az deployment sub create` despite the preflight — this indicates the reserved-suffix registry drifted from Azure's actual rules and needs updating.

## Key Rules (walk this for every global-namespace resource)

1. **Identify the resource type's `checkNameAvailability` API**. Common ones:
   - **Service Bus**: `POST https://management.azure.com/subscriptions/{sub}/providers/Microsoft.ServiceBus/checkNameAvailability?api-version=2022-10-01-preview` — body: `{"name": "{proposedName}"}`.
   - **Storage**: `POST https://management.azure.com/subscriptions/{sub}/providers/Microsoft.Storage/checkNameAvailability?api-version=2023-01-01` — body: `{"name": "{proposedName}", "type": "Microsoft.Storage/storageAccounts"}`.
   - **Cognitive Services**: `az cognitiveservices account list --query "[?name=='{proposedName}']"` — empty result means available (poor-man's check).
   - **Front Door**: `POST https://management.azure.com/providers/Microsoft.Cdn/checkFrontDoorNameAvailability?api-version=2020-09-01` — body: `{"name": "{proposedName}", "type": "Microsoft.Cdn/Profiles/AfdEndpoints"}`.
   - **Azure Container Registry**: `POST /subscriptions/{sub}/providers/Microsoft.ContainerRegistry/checkNameAvailability?api-version=2023-01-01-preview` — body: `{"name": "{proposedName}", "type": "Microsoft.ContainerRegistry/registries"}`.
2. **Interpret the response**: `{"nameAvailable": false, "reason": "AlreadyExists|Invalid", "message": "..."}` → HARD STOP with the reason as the operator remediation.
3. **Reserved-suffix registry**: maintain `scripts/provisioning-prereqs/reserved-suffixes.yaml` with entries like `- resource: Microsoft.ServiceBus/namespaces\n  suffix: "-sb"\n  discovered: 2026-08-22\n  source: F10`. Preflight consults this before hitting the availability API (fast-fail on known reserved suffixes).
4. **Handler-side rejection** (H2a): if `what-if` passes but `create` fails on name-collision, emit `h2a-global-name-collision` rejection code with the resource name + type. Log to `handler-log.md` + link to this pattern.
5. **Rename recovery**: if a live customer already has a global-name resource that turned out to be problematic, DO NOT rename in place — Azure global-namespace names are practically immutable. Provision the new-named resource, migrate data (if applicable), decommission the old.

## Anti-patterns this catches

- ❌ Trusting `az deployment sub what-if` alone → does NOT check global namespace; wastes 16+ minutes at real deploy time when the collision surfaces.
- ❌ Assuming a name is available because it's specific ("`sprksharedprod-sb`" is specific but hits a reserved suffix) → reserved-suffix registry catches this.
- ❌ Retrying the failed deploy after a name collision without changing the name → same failure, same 16 minutes.
- ❌ Hardcoding names in Bicep without exposing them as parameters → operators can't fix a collision without a Bicep edit + rebuild cycle.
- ❌ Skipping this pre-check "because we've done it before" → Azure reserved suffixes change; global namespace fill rate grows; assumption goes stale.

## Recovery recipes

- **Preflight surfaces `nameAvailable: false` with reason `AlreadyExists`**: the name is taken globally. Change the `bicepparam` value; re-run preflight; re-run deploy.
- **Preflight surfaces `nameAvailable: false` with reason `Invalid`**: the name violates the resource-type's naming rules (length, char set, suffix). Adjust to match rules; consult the resource type's docs.
- **Real deploy fails on name collision despite passing preflight**: race condition (someone else took the name between preflight and deploy) OR reserved-suffix registry is stale. Update the registry; re-run.
- **Rename needed on a live resource**: don't. Provision new resource with a different name; migrate; decommission old. Global-namespace names are effectively immutable.

## Worked example — Service Bus + Storage preflight checks

PowerShell recipe for Step 2.5 preflight (called by `/provision-environment` skill):

```powershell
function Test-GlobalResourceNameAvailability {
  param(
    [string]$SubscriptionId,
    [string]$ResourceType,   # 'Microsoft.ServiceBus/namespaces' | 'Microsoft.Storage/storageAccounts' | ...
    [string]$ProposedName,
    [string]$ReservedSuffixesPath = "scripts/provisioning-prereqs/reserved-suffixes.yaml"
  )

  # 1. Fast-fail on reserved suffix
  $reserved = Get-Content $ReservedSuffixesPath | ConvertFrom-Yaml
  $ruleForType = $reserved | Where-Object { $_.resource -eq $ResourceType }
  foreach ($r in $ruleForType) {
    if ($ProposedName -match "$($r.suffix)$") {
      return @{ available = $false; reason = "ReservedSuffix"; message = "'$($r.suffix)' is reserved for $ResourceType (discovered $($r.discovered) via $($r.source))" }
    }
  }

  # 2. Hit the resource-type checkNameAvailability API
  $apiMap = @{
    'Microsoft.ServiceBus/namespaces' = @{
      url = "https://management.azure.com/subscriptions/$SubscriptionId/providers/Microsoft.ServiceBus/checkNameAvailability?api-version=2022-10-01-preview"
      body = @{ name = $ProposedName } | ConvertTo-Json -Compress
    }
    'Microsoft.Storage/storageAccounts' = @{
      url = "https://management.azure.com/subscriptions/$SubscriptionId/providers/Microsoft.Storage/checkNameAvailability?api-version=2023-01-01"
      body = @{ name = $ProposedName; type = 'Microsoft.Storage/storageAccounts' } | ConvertTo-Json -Compress
    }
    # ... more resource types
  }

  $api = $apiMap[$ResourceType]
  if (-not $api) { throw "No checkNameAvailability API registered for $ResourceType — add to apiMap." }

  $response = az rest --method post --url $api.url --body $api.body | ConvertFrom-Json
  return @{
    available = $response.nameAvailable
    reason    = $response.reason
    message   = $response.message
  }
}

# Usage in preflight
$check = Test-GlobalResourceNameAvailability -SubscriptionId $sub -ResourceType 'Microsoft.ServiceBus/namespaces' -ProposedName 'sprksharedprod-sb'
if (-not $check.available) {
  Write-Error "❌ Name '$($check.name)' unavailable: $($check.reason) — $($check.message)"
  # HARD STOP; operator adjusts bicepparam
  exit 1
}
```

Reserved-suffix registry (`scripts/provisioning-prereqs/reserved-suffixes.yaml` — task 203-followup authors):

```yaml
- resource: Microsoft.ServiceBus/namespaces
  suffix: "-sb"
  discovered: 2026-08-22
  source: F10
  note: "Service Bus reserves this suffix for its own use; naming rules reject it silently at PUT time."
```

## Cross-refs

- Related prereq: `docs/guides/PROVISIONING-PREREQUISITES.md` PRQ-C-03
- Related script (planned): `scripts/provisioning-prereqs/reserved-suffixes.yaml` (task 203-followup)
- Related handler: `H2a` (Bicep infra deploy) — emits `h2a-global-name-collision` rejection code
- Related design doc: `projects/customer-provisioning-orchestration-r1/design.md` § H2a
