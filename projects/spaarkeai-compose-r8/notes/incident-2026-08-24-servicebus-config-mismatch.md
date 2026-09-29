# Incident — dev BFF cannot start: Service Bus config/code mismatch

> **Date**: 2026-08-24 · **Status**: OPEN — `spaarke-bff-dev` is DOWN
> **Found by**: `spaarkeai-compose-r8` (task 051 deploy) · **Owner**: unassigned — needs cross-project input
> **Audience**: any project that deploys the BFF or configures a Spaarke environment
> **Shareable summary (same content, formatted)**: https://claude.ai/code/artifact/a0afa791-49bb-433c-bf9c-279f6db9ac24

---

## TL;DR

The BFF requires a Service Bus **connection string** at startup. `spaarke-bff-dev` is configured for
**managed identity** and has no connection string. The two cannot coexist, so the app aborts on startup.

The failing guard is **5+ months old and on `master`** — it is not new code. What surfaced it was a deploy
that forced the first cold start since 12:52 UTC.

**This is not scoped to dev.** Any environment configured for MI Service Bus cannot cold-start this build.

A second, unrelated defect ships a stale `net8` fragment in every BFF publish.

---

## Current state

| | |
|---|---|
| Service | `spaarke-bff-dev` (App Service, `rg-spaarke-dev`) |
| Subscription | `Spaarke Devlopment Environment` (note: **not** the CLI default, which is `Spaarke Model 1 Production`) |
| HTTP | 503 |
| Container | `exit code: 134` (SIGABRT), ~9s after start, 8 consecutive failures |
| Down since | 2026-08-24 18:38 UTC |
| Mitigation applied | **none** — no app settings modified, no rollback attempted |

---

## Defect 1 — Service Bus auth (BLOCKING)

**Location**: `src/server/api/Sprk.Bff.Api/Infrastructure/DI/JobProcessingModule.cs:54-62`

```csharp
var serviceBusConnectionString = configuration.GetConnectionString("ServiceBus");
if (string.IsNullOrWhiteSpace(serviceBusConnectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:ServiceBus is required. " + ...);
}
services.AddSingleton(sp => new ServiceBusClient(serviceBusConnectionString));
```

There is **no managed-identity path** in this code. A fully-qualified namespace is never consulted.

### Startup exception (repeating)

```
Unhandled exception. System.InvalidOperationException:
  ConnectionStrings:ServiceBus is required.
   at Sprk.Bff.Api.Infrastructure.DI.JobProcessingModule
        .AddJobProcessingModule(...) JobProcessingModule.cs:line 58
   at Program.<Main>$(String[] args) Program.cs:line 196
```

### What the environment actually has

```
ServiceBus__FullyQualifiedNamespace = spaarke-servicebus-dev.servicebus.windows.net
ServiceBus__QueueName               = sdap-jobs
ServiceBus__OfficeQueueName         = office-jobs
```

- `az webapp config connection-string list -g rg-spaarke-dev -n spaarke-bff-dev` → `[]`
- No `ConnectionStrings__ServiceBus` app setting
- No deployment slots
- Secrets in this environment use Key Vault references (`AzureOpenAI__ApiKey`, `Communication__WebhookClientState`, …), not inline values

### Age — this is NOT a recent regression

Commit `47aa560a2` (2026-03-13, "extract Program.cs into DI modules") only **relocated** the guard.
Verify:

```bash
git show 47aa560a2^:src/server/api/Sprk.Bff.Api/Program.cs | grep -n "ServiceBus is required"
# → 685:        "ConnectionStrings:ServiceBus is required. "
```

It already existed at `Program.cs:681-685` beforehand. Long-standing code on `master`.

### Blast radius

**Any environment configured for MI Service Bus cannot cold-start this build.** Dev is simply the
environment that got restarted. **Prod and demo need checking.**

### ADR tension

[ADR-028](../../../.claude/adr/ADR-028-spaarke-auth-architecture.md) makes managed identity the canonical
server-outbound path. For Service Bus the code does not implement it — so the environment that follows the
ADR is the environment that cannot boot.

---

## Defect 2 — stale `net8` in every publish (LATENT)

**Location**: `src/server/api/Sprk.Bff.Api/runtimeconfig.template.json` (checked in)

```json
{
  "configProperties": { "System.Runtime.TieredCompilation": true, "System.Runtime.TieredPGO": true },
  "runtimeOptions": {
    "tfm": "net8.0",
    "rollForward": "LatestMinor",
    ...
  }
}
```

The SDK merges this template **into** the generated `runtimeOptions`. Because the template wraps its
contents in its own `runtimeOptions` key, the published artifact gets a **nested** `runtimeOptions`
carrying `net8.0` inside an otherwise-correct net10 publish:

```json
"runtimeOptions": {
  "tfm": "net10.0",
  "frameworks": [ { "name": "Microsoft.NETCore.App", "version": "10.0.0" }, ... ],
  "runtimeOptions": {              // <-- nested; should not exist
    "tfm": "net8.0",
    "rollForward": "LatestMinor"
  }
}
```

- **Observed**: one `System.BadImageFormatException` at 18:38:50 on the first startup attempt. It did
  **not** recur after redeploy, so it is not what is keeping the service down — but the artifact is
  genuinely malformed.
- **Origin**: missed by `dotnet-10-upgrade-r1` — the `.csproj` was retargeted to `net10.0`, this file was not.
- **Scope**: every BFF publish, every environment, since the net10 retarget.
- Verify: `cat deploy/api-publish/Sprk.Bff.Api.runtimeconfig.json`

Local toolchain was correct: SDK `10.0.101`, `TargetFramework=net10.0`, `RuntimeIdentifier=linux-x64`,
`SelfContained=false`; App Service runtime `DOTNETCORE|10.0`.

---

## Timeline (UTC, 2026-08-24)

| Time | Event |
|---|---|
| 12:52:28 | `Site started.` — last successful start |
| 16:38 / 16:50 / 16:53 | `config/appsettings` updated ×3 |
| 18:10 / 18:13 / 18:18 / 18:23 | `config/appsettings` updated ×4 |
| 18:37:53 | Deploy begins (publishing profile fetched) |
| 18:38:50 | `BadImageFormatException` — once, first attempt only |
| 18:38:57 | First `exit code: 134` |
| 18:40:06 → | `ConnectionStrings:ServiceBus is required`, repeating |
| 18:53:15 | Azure blocks the site for consecutive cold-start failures |

The deploy itself succeeded: SHA-256 hash verification passed on all 4 critical assemblies.

### ⚠️ What this timeline does and does NOT prove

**Established:**
- The code and the environment configuration disagree.
- No Service Bus connection string exists in either config location, right now.
- The guard predates all of today's activity.
- The deploy forced the first cold start since 12:52.

**NOT established:**
- **What those seven app-settings changes contained.** Azure's activity log records that
  `config/appsettings` was *written*, not which keys changed. Property-level diffs require Change
  Analysis, which is not enabled on this resource.
- **Do not** read this document as claiming a specific change removed a specific key.

---

## Questions we need answered before fixing

1. **Is anyone migrating dev Service Bus to managed identity?**
   The MI settings are present and the connection string is absent — that pattern looks intentional. If it
   is, the code fix is work you already need, and restoring a connection string would undo it.

2. **Who made the seven app-settings changes between 16:38 and 18:23?**
   Not to assign blame — to learn the intended end state before anyone writes config on top of it.

3. **Does the BFF's managed identity hold `Azure Service Bus Data Sender` / `Data Receiver` on
   `spaarke-servicebus-dev`?**
   This decides whether the MI fix works. Without the role grant it compiles, deploys, and still fails at
   runtime — a second outage for the same reason.

4. **Are prod and demo configured for MI Service Bus?**
   If yes, this is not a dev issue — the next deploy to those environments hits the same wall.

---

## Options

| Option | What it does | Trade-off |
|---|---|---|
| **A — Fix the code** | `JobProcessingModule` uses `ServiceBus__FullyQualifiedNamespace` + `DefaultAzureCredential`, falling back to a connection string when present | ADR-028 conformant; fixes every environment. Requires the MI role grant (Q3) to work at runtime. |
| **B — Restore the connection string** | Set `ConnectionStrings__ServiceBus` on dev from `spaarke-servicebus-dev` | Restores dev in ~2 min. Reintroduces an inline secret against dev's Key-Vault convention, and works against the MI configuration already in place. |
| **C — A + the template** | Option A plus the `runtimeconfig.template.json` fix in one change | Any redeploy ships the template anyway; fixing it separately costs an extra deploy for no benefit. |

**Recommendation: C, gated on confirming the MI role assignment (Q3) first.** B is available as a bridge
if dev must be up before the code change lands — but taken knowingly, as a temporary reversal of the MI
direction, not as the fix.

---

## Process gaps worth closing

1. **No configuration pre-flight before deploy.** A branch not deployed in a long time can carry startup
   requirements the target environment no longer satisfies. Nothing checks this; the container aborting is
   the first signal. A pre-flight diffing required config keys against the target's settings would have
   caught this before any downtime. `.claude/skills/bff-deploy/SKILL.md` has no such step.

2. **Cross-project coordination covers files, not environments.** `/conflict-check` and
   [`projects/INDEX.md`](../../INDEX.md) track file overlap between active worktrees. Neither has any
   notion of shared dev-environment configuration, so two projects can reconfigure and redeploy the same
   app service without ever seeing each other.

3. **Fail-fast guards encode one auth model.** A hard `throw` on a missing connection string quietly makes
   the ADR-sanctioned auth path un-deployable. Guards like this should accept every sanctioned path, or
   say plainly which one is required.

---

## Reproduction / evidence commands

```bash
# 1. The guard
sed -n '54,62p' src/server/api/Sprk.Bff.Api/Infrastructure/DI/JobProcessingModule.cs

# 2. It predates the March refactor
git show 47aa560a2^:src/server/api/Sprk.Bff.Api/Program.cs | grep -n "ServiceBus is required"

# 3. Environment has MI settings, no connection string
az account set --subscription "Spaarke Devlopment Environment"
az webapp config appsettings list -g rg-spaarke-dev -n spaarke-bff-dev \
  --query "[?contains(name,'ServiceBus')].{n:name,v:value}" -o tsv
az webapp config connection-string list -g rg-spaarke-dev -n spaarke-bff-dev -o json   # → []

# 4. Startup failure log (needs a management token)
TOKEN=$(az account get-access-token --resource https://management.azure.com --query accessToken -o tsv)
curl -s -H "Authorization: Bearer $TOKEN" \
  "https://spaarke-bff-dev.scm.azurewebsites.net/api/vfs/LogFiles/StartupLogs/2026_08_24_ln0sdlwk003J0U_failure.log" \
  | grep -n "Unhandled exception"

# 5. Config-write history (shows THAT appsettings changed, not WHICH keys)
az monitor activity-log list -g rg-spaarke-dev --offset 12h \
  --query "[?contains(resourceId,'spaarke-bff-dev') && contains(operationName.localizedValue,'config')]" -o table

# 6. The malformed publish artifact
cat deploy/api-publish/Sprk.Bff.Api.runtimeconfig.json
```
