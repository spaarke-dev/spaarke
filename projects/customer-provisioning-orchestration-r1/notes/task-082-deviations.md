# Task 082 — Delete DemoProvisioning legacy config: Deviations & ESCALATION

> **Task**: `082-delete-demo-provisioning-legacy-config.poml`
> **Author**: Wave-4-Batch-4E subagent (Sonnet 4.7)
> **Date**: 2026-08-18
> **Baseline commit at start**: `e3919a0ef` (HEAD)
> **Wave**: 4 Batch 4E — parallel with main-session task 075 (skill authoring)
> **Rigor**: FULL (deploy, azure-deployment, bff-api, testing tags)
> **Outcome**: 🛑 **BLOCKED — ESCALATED per POML escalation trigger. No Azure config was mutated.**

---

## TL;DR

Pre-verification GATE surfaced an **active production code binding to the "orphan" config keys** that tasks 080 + 081 missed. Deleting the keys as spec'd would crash BFF startup. Per the POML `<escalation><trigger>` and root CLAUDE.md §6, I STOPPED and escalated instead of proceeding silently. Snapshot was still taken (rollback ready if the deletion is later attempted). Publish-size / /health / TASK-INDEX flip were NOT executed.

---

## Pre-check evidence

### 1. Prerequisite commits confirmed present

```
5fdd1d9ea — Task 080 (DemoExpirationService migration)
0b8ca53ba — Task 081 (RegistrationEndpoints.SubmitDemoRequest admin-notification migration)
```

Both merged into `work/customer-provisioning-orchestration-r1`.

### 2. Grep for config-key literals — CLEAN

```
grep -rn "DemoProvisioning__Environments\|DemoProvisioning__DefaultEnvironment" src/
→ 0 matches
```

No code binds to the raw `DemoProvisioning__Environments*` / `DemoProvisioning__DefaultEnvironment` **appsetting names**.

### 3. Grep for `[Obsolete]` property accessors — HIT (BLOCKER)

```
grep -rn "\.Environments\b\|\.DefaultEnvironment\b" src/
```

**Live production binding at `src/server/api/Sprk.Bff.Api/Services/Registration/RegistrationDataverseService.cs:60-75`**:

```csharp
// Admin Dataverse URL: prefer DATAVERSE_URL config, fall back to legacy Environments config
var dataverseUrl = configuration["DATAVERSE_URL"];
#pragma warning disable CS0618 // Obsolete — legacy fallback until DemoExpirationService migration
if (string.IsNullOrEmpty(dataverseUrl) && _options.Environments.Length > 0)
{
    var defaultEnv = _options.Environments.FirstOrDefault(e => e.Name == _options.DefaultEnvironment)
        ?? _options.Environments.First();
    dataverseUrl = defaultEnv.DataverseUrl;
}
#pragma warning restore CS0618

if (string.IsNullOrEmpty(dataverseUrl))
{
    throw new InvalidOperationException(
        "RegistrationDataverseService requires DATAVERSE_URL configuration (or legacy DemoProvisioning:Environments).");
}
```

The class is a **DI singleton** (`RegistrationModule.cs:32`) consumed by `DemoExpirationService` (BackgroundService), `DemoProvisioningService`, and `RegistrationEndpoints` (SubmitDemoRequest / ApproveRequest / ExpireDemoRequest). Because `DemoExpirationService` is an `IHostedService` that depends on this singleton, the constructor executes at `IHost.StartAsync()` — a throw here is a boot crash.

### 4. Azure App Service current state — CONFIRMS the fallback is LIVE

Snapshot saved at [`phase-e-config-snapshot.json`](phase-e-config-snapshot.json) (rollback safety per POML step 3).

Query: `az webapp config appsettings list --resource-group rg-spaarke-dev --name spaarke-bff-dev --query "[?starts_with(name, 'DemoProvisioning__') || name == 'DATAVERSE_URL']" -o table`

Results (redacted for GUIDs):

```
DemoProvisioning__AccountDomain                             demo.spaarke.com
DemoProvisioning__AdminNotificationEmails__0                ralph.schroeder@spaarke.com
DemoProvisioning__DefaultEnvironment                        Dev
DemoProvisioning__DemoUsersGroupId                          [GUID]
DemoProvisioning__Environments__0__AppId                    [GUID]
DemoProvisioning__Environments__0__BusinessUnitName         Spaarke Demo
DemoProvisioning__Environments__0__DataverseUrl             https://spaarkedev1.crm.dynamics.com
DemoProvisioning__Environments__0__DefaultDemoDurationDays  14
DemoProvisioning__Environments__0__Name                     Dev
DemoProvisioning__Environments__0__SpeContainerId           [CONTAINER-ID]
DemoProvisioning__Environments__0__TeamName                 Spaarke Demo
DemoProvisioning__Environments__1__BusinessUnitName         Spaarke Demo
DemoProvisioning__Environments__1__DataverseUrl             https://spaarke-demo.crm.dynamics.com
DemoProvisioning__Environments__1__DefaultDemoDurationDays  14
DemoProvisioning__Environments__1__Name                     Demo 1
DemoProvisioning__Environments__1__SpeContainerId           [CONTAINER-ID]
DemoProvisioning__Environments__1__TeamName                 Spaarke Demo
DemoProvisioning__Licenses__FabricFreeSkuId                 [GUID]
DemoProvisioning__Licenses__PowerAppsPlan2TrialSkuId        [GUID]
DemoProvisioning__Licenses__PowerAutomateFreeSkuId          [GUID]
```

**Critical**: `DATAVERSE_URL` is **NOT present** on `spaarke-bff-dev`. `RegistrationDataverseService` is therefore actively taking the fallback path today. Its `_apiUrl` today resolves to `https://spaarkedev1.crm.dynamics.com/api/data/v9.2` via `Environments[0]` (name=`Dev`, matching `DefaultEnvironment=Dev`).

If the DemoProvisioning keys are deleted with `DATAVERSE_URL` still unset, the singleton throws at boot → 500.30 at `/health` → task rollback required.

---

## Why the escalation trigger fires (POML `<escalation><trigger>`)

> "if pre-verification grep shows any remaining consumer that tasks 080/081 missed, STOP and escalate per CLAUDE.md §6 rather than deleting silently. Rollback via saved snapshot is the safe path."

Tasks 080/081 self-scoped as follows (per their commit messages):

- **080** — `DemoExpirationService` only.
- **081** — `RegistrationEndpoints.SubmitDemoRequest.SendAdminNotificationAsync` only. Its own commit body says: *"Together they unblock task 082 (removing the [Obsolete] properties + Azure App Service settings)."*

Neither task touched `RegistrationDataverseService.cs`. The class comment on line 15 even says: *"Uses S2S (client secret) auth targeting the Demo Dataverse URL from DemoProvisioningOptions."* The `#pragma warning disable CS0618` comment on line 62 acknowledges the reliance: *"legacy fallback until DemoExpirationService migration"* — but the migration that actually removes it never landed.

This is a legitimate scope-planning gap, not a phantom finding.

---

## What I did NOT do (safety)

Per the escalation trigger and CLAUDE.md §6:

- ❌ No `az webapp config appsettings delete` executed.
- ❌ No `az webapp config appsettings set DATAVERSE_URL=...` executed (that would be a scope expansion — not what task 082 authorized).
- ❌ No BFF redeploy.
- ❌ No source-code modification (would expand scope beyond 082 into an unplanned "081.5" refactor).
- ❌ No `[Obsolete]` class removal.
- ❌ TASK-INDEX 082 row NOT flipped to ✅ — remains ⏸.
- ❌ No `notes/phase-e-publish-size-*.md` written (would misrepresent state).

## What I DID do

- ✅ Pre-verification grep executed + evidence captured (this doc).
- ✅ Snapshot of current Azure config saved to [`phase-e-config-snapshot.json`](phase-e-config-snapshot.json) — rollback-ready IF the deletion is later attempted after remediation.
- ✅ TASK-INDEX 082 row left at ⏸ (blocked).
- ✅ Escalation via SendMessage to main-session with recommended paths.

---

## Recommended remediation paths (for main-session judgment)

Presented in priority order. All three are viable; choice is a scope-management decision, not a technical one.

### Path C (comply — RECOMMENDED): Author task 081.5 to complete the migration

Scope: refactor `RegistrationDataverseService` constructor off the `Environments`/`DefaultEnvironment` fallback onto `DataverseEnvironmentService` (mirror the pattern 081 used for `RegistrationEndpoints`). Then task 082 becomes safe to execute as spec'd.

- **Pro**: aligns with the ADR-013/§10 spirit (one source of truth for Dataverse env config; the `DataverseEnvironmentService` r3 built for this purpose). Also aligns with FR-33's `[Obsolete]` retirement contract.
- **Con**: `RegistrationDataverseService` constructs the singleton eagerly and picks a single `_apiUrl` at boot; `DataverseEnvironmentService.GetActiveEnvironmentsAsync` is async. Either the ctor becomes async-lazy (backing field + double-check), or the URL resolution moves to a factory. Non-trivial mechanical change but well-scoped (~1 task).
- **Effort**: 1 FULL-rigor task, sonnet @ high or xhigh (matches 080 shape).

### Path A (documented exception): Add `DATAVERSE_URL` app-setting, then delete DemoProvisioning keys

Scope: `az webapp config appsettings set --setting-names DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com` FIRST, then execute the DemoProvisioning deletion.

- **Pro**: single Azure-side change; no source-code churn; preserves current runtime behavior (URL is the same value the fallback resolves to today).
- **Con**: `RegistrationDataverseService` still bears dead `Environments`/`DefaultEnvironment` code. The `[Obsolete]` class can't be fully retired. Kicks the class-cleanup can down the road without solving the FR-33 retirement contract.
- **Effort**: within the current task's scope but requires re-authorizing task 082 with the added `set` step; needs explicit owner sign-off since it expands scope.

### Path B (ADR amendment): Defer FR-33 retirement of `[Obsolete] Environments`

Scope: mark FR-33 as "partial — RegistrationDataverseService fallback retained pending later refactor." Delete only the OTHER unused property (`DefaultEnvironment`?) if any exist, or defer the entire deletion.

- **Pro**: minimal work now.
- **Con**: contradicts the r1 project's stated Phase E completion criteria. Not recommended.

---

## Rollback plan (if the deletion is later attempted and /health fails)

Restore all keys from the snapshot via:

```powershell
$snap = Get-Content 'projects/customer-provisioning-orchestration-r1/notes/phase-e-config-snapshot.json' | ConvertFrom-Json
$settings = $snap | ForEach-Object { "$($_.name)=$($_.value)" }
az webapp config appsettings set --resource-group rg-spaarke-dev --name spaarke-bff-dev --settings @settings
```

Then restart:

```
az webapp restart --resource-group rg-spaarke-dev --name spaarke-bff-dev
```

Verify:

```
curl https://spaarke-bff-dev.azurewebsites.net/healthz
# Expect: 200
```

---

## Files created

| File | Purpose |
|---|---|
| `projects/customer-provisioning-orchestration-r1/notes/task-082-deviations.md` | This doc (escalation record + rollback plan) |
| `projects/customer-provisioning-orchestration-r1/notes/phase-e-config-snapshot.json` | Pre-delete snapshot of DemoProvisioning App Service settings (rollback safety) |

## Files NOT created (deferred until unblock)

| File | Reason |
|---|---|
| `projects/customer-provisioning-orchestration-r1/notes/phase-e-publish-size-2026-08-18.md` | No deletion + no publish + no /health delta = nothing to report |

## Coordination

- Main-session working on task 075 (`.claude/skills/provision-environment/SKILL.md`) — no overlap with this escalation.
- No other Wave-4 subagent overlaps this file surface.
- Cross-worktree: `code-quality-and-assurance-r3` decomposition PRs *could* land in this file territory next; heads-up would be worthwhile if Path C is taken.

## Suggested TASK-INDEX action (main-session decision)

Leave row 082 at ⏸. Add a new row `081.5` (or renumber under Phase E) for the `RegistrationDataverseService` migration if Path C is chosen. Update after path selection.
