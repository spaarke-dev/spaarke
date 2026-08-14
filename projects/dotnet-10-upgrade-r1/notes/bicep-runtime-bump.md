# App Service + Functions Runtime Bump to net10 (task 041, FR-14)

> **Date**: 2026-08-13 · **Task**: 041 · **Gate**: 032 complete.

---

## App Service runtime strings — `DOTNETCORE|8.0` → `DOTNETCORE|10.0` (pipe form)

All 5 source locations edited + `platform.json` regenerated:

| File | Location | Change |
|---|---|---|
| `infrastructure/bicep/modules/app-service.bicep` | `param runtimeStack` default (L14) | `DOTNETCORE|10.0` |
| `infrastructure/bicep/modules/app-service-slot.bicep` | `linuxFxVersion` (L39) | `DOTNETCORE|10.0` |
| `infrastructure/bicep/modules/deployment-slot.bicep` | `param runtimeStack` default (L22) | `DOTNETCORE|10.0` |
| `infrastructure/byok/main.bicep` | `linuxFxVersion` (L369) | `DOTNETCORE|10.0` |
| `infrastructure/bicep/platform.json` | compiled output (param default + slot linuxFxVersion) | `DOTNETCORE|10.0` (regenerated via `az bicep build`) |

`bicep build` clean on all touched files (one pre-existing unrelated linter warning: `outputs-should-not-contain-secrets` on `ai-search.bicep` — not from this task).

## Functions runtime — `dotnet-isolated 8.0` → `10.0`

- `infra/insights/modules/function-app.bicep` L176-178: `runtime: { name: 'dotnet-isolated', version: '10.0' }`.
- **Escalation valve (Flex Consumption net10 support) did NOT fire** — `researcher` verdict: **SUPPORTED**. Functions 4.x isolated supports .NET 10 (alongside 9.0/8.0); Flex Consumption is the recommended plan for .NET 10 (Linux Consumption does NOT support it). Version string is the bare `'10.0'` (same format as `'8.0'`). Sources: MicrosoftDocs `functions-dotnet-supported-versions.md`; Learn `flex-consumption-how-to` (updated 2026-08-04); Azure Functions .NET 10 GA update.
- **⚠️ Runbook caveat for task 042 / deploy (NOT a Bicep/ARM gate)**: Azure Functions Core Tools **v4.7.0** (~Feb 2026) has a regression where `func publish` of .NET 10 isolated to Flex Consumption fails / ignores `--dotnet-version 10.0` ([core-tools#4794](https://github.com/Azure/azure-functions-core-tools/issues/4794)); v4.6.0 worked. Bicep/ARM provisioning (how this insights Functions app is deployed) is unaffected. If any deploy path uses `func publish`, pin Core Tools ≠ 4.7.0. Also optionally confirm region availability at deploy time: `az functionapp list-flexconsumption-runtimes --location <region>`.

## ⚠️ DECISION FLAGGED — `platform.json` recompile carried pre-existing drift

`platform.json` was last compiled **2026-03-13**, but its source modules were edited through **2026-07-16** (4 months of `apiVersion` bumps — openai/content-safety `2024-10-01`, cosmos `2024-07-18`, etc. — never recompiled). So the committed `platform.json` was **already stale**. A genuine `az bicep build` (which the POML step 2 prescribes: "recompile … rather than hand-editing the ARM output alone") therefore produces a **~611-insertion / 19-deletion diff**, not just the 2 runtime-string lines.

- **Chosen (this commit)**: full recompile — the honest compiled output of current source; constraint-compliant; fixes 4 months of latent staleness. Downside: bundles unrelated infra `apiVersion` drift into the .NET 10 commit.
- **Alternative (available on request)**: surgical 2-line hand-edit of the old `platform.json` — minimal diff, but violates the prescriptive "don't hand-edit" constraint and leaves `platform.json` stale vs its source.

The drift is **pre-existing** (not introduced by .NET 10) and IaC-source-only — nothing auto-deploys it (spaarke-dev is the only live env; platform/prod IaC is applied at operator-driven deploy). Surfaced to owner for a keep-vs-surgical call; default kept the recompile.

## Negative criterion

No live production runtime is flipped by this task — IaC source only. The live runtime flip is the operator-driven slot swap (tasks 050/051; prod 061 deferred).
