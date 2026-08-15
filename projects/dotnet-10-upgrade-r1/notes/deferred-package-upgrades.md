# Deferred Package Major Upgrades (post-.NET 10)

> **Date**: 2026-08-14 · Inventory captured at the net10 cutover. These were **intentionally deferred** by `dotnet-10-upgrade-r1` (spec: "zero behavior change, minimal churn") and are queued for **`code-quality-and-assurance-r3`**, which re-plans on the net10 baseline. Each is an API-breaking major needing its own testing — NOT to be batch-applied.
> **Baseline is healthy**: net10 LTS, SDK 10.0.101, `dotnet list --vulnerable` = **zero** across the graph. There is **no security pressure** driving any of these.
> Source data: `dotnet list Spaarke.sln package --outdated` (2026-08-14). Tier-1 same-major patches were already applied at cutover (see `tier1-bump-test-run.txt`).

---

## A. Deferred majors — evaluate individually in r3

| Package | Current | Target | Why deferred / risk |
|---|---|---|---|
| `Azure.Search.Documents` | 11.6.0 | 12.0.0 | v11→v12 major; index/query API surface changes. Spec "5 deferred majors" #1. |
| `Microsoft.PowerBI.Api` | 4.21.1 | 5.1.0 | v4→v5 major. Spec #3. |
| `Azure.AI.Projects` | 1.0.0-beta.8 | 2.0.1 | beta→GA v2 — significant client redesign. Spec #4. |
| `Microsoft.Agents.AI` (+ `Agents.Hosting.AspNetCore`) | 1.0.0-rc1 / 1.0.1 | 1.17.0 / 1.7.129 | rc→GA, large jump; agent-framework surface. Spec #5. |
| `Microsoft.Extensions.Http.Polly` | 8.0.8 | → **migrate to `Microsoft.Extensions.Http.Resilience`** | `Http.Polly` is **deprecated**; the forward path is the Resilience package (different API). Spec follow-on. |
| `JsonSchema.Net` | 7.3.4 | 9.4.0 | two majors (v7→v9); validation API changes. |
| `Microsoft.Extensions.AI` (+ `.AI.OpenAI`) | 10.3.0 | 10.9.0 | Same major, but **coupled** — bumping drags `OpenAI` 2.8→≥2.12; the net10 project pinned 10.3.0 deliberately to avoid that chain. Treat as a coordinated bump (M.E.AI + OpenAI + Agents together). |

> **AppInsights 3.x** (spec deferred-major #2) is **N/A** — FR-06 removed the classic App Insights SDK entirely; OTel→Azure Monitor is the sole telemetry path.

## B. Test-tooling majors — do as ONE batched pass with a full test run

| Package | Current | Target |
|---|---|---|
| `Microsoft.NET.Test.Sdk` | 17.11.1 | 18.9.0 (Microsoft Testing Platform) |
| `xunit.runner.visualstudio` | 2.8.2 | 3.1.5 |
| `coverlet.collector` | 6.0.2 | 10.0.1 |
| `NSubstitute` | 5.3.0 | 6.2.0 (codebase standard is Moq — audit usage/migrate) |
| `WireMock.Net` | 1.5.45 | 2.14.0 |

## C. 🚫 HELD — do NOT upgrade without explicit review

| Package | Current | "Latest" | Reason held |
|---|---|---|---|
| `FluentAssertions` | 6.12.0 | 8.10.0 | **v7+ switched to a paid commercial license (Xceed).** Staying on 6.12.0 is deliberate & correct. Requires legal/licensing sign-off before any bump. |
| `QuestPDF` | 2025.12.1 | 2026.7.3 | Revenue-gated license (free < $1M, else Professional). Confirm license compliance before bumping. |

## D. Tooling (non-NuGet)

- **Bicep CLI**: upgraded 0.41.2 → **0.46.1** at cutover (`platform.json` regenerated). ✅ done.
- **Azure Functions Core Tools**: local `4.2.2`. If `func publish` is ever used for the net10 insights Functions app, use a current version but **avoid v4.7.0** (net10 Flex-publish regression, core-tools#4794). The insights Functions deploys via Bicep/ARM, so this is likely moot.
- **.NET SDK**: 10.0.101 (latest patch; `global.json` rolls forward). ✅
- **Frontend** (React/Fluent v9/TypeScript/Vite/PCF): a **separate** modernization track, unrelated to the .NET cutover. Node 22 LTS is current. Own `npm outdated` sweep per solution.

---

**Recommendation**: leave A/B for `code-quality-and-assurance-r3` to spec + test individually; keep C held pending licensing review. The net10 baseline is stable, current-LTS, and zero-CVE — none of these is urgent.
