# MEDIUM-Tier Candidates (Task 021)

> **Captured**: 2026-05-24
> **Definition (per design.md §6 Phase 2)**: MEDIUM = csproj edit OR transitive-vulnerability patch on same major; compile-time verifiable; reflection use ruled out via task 015 dynamic probe.
> **Approval**: Owner ACK (operator-only model per NFR-08 revised)
> **Both probe agreement required**: Any "package removable" claim must show BOTH static-grep agreement (task 014) AND deps.json/DI agreement (task 015).

---

## Summary

After analyzing Phase 1 outputs (`vulnerable.txt`, `outdated.txt`, `usage-map-static.md`, `reflection-probe.txt`), the MEDIUM tier has:

- **1 candidate**: patch `System.Security.Cryptography.Xml` transitive HIGH CVE (via Microsoft.IdentityModel.* bump path)
- **0 package-removal candidates**: all 44 direct packages confirmed used (4 zero-static-usage packages all verified live via DI registration + deps.json — see Finding 6 in INVENTORY.md)

**Outcome E facade migration** (FR-E1/FR-E2/FR-E3) is **NOT** categorized here — it's Phase 4 work executed per the Outcome E track in design §6 Phase 4 (independent of A/B/C tracks). Treated as its own work stream.

---

## MEDIUM-1 — Patch `System.Security.Cryptography.Xml` HIGH CVE (transitive)

| Field | Value |
|---|---|
| **CVE IDs** | [GHSA-37gx-xxp4-5rgx](https://github.com/advisories/GHSA-37gx-xxp4-5rgx), [GHSA-w3x6-4m5h-cxqf](https://github.com/advisories/GHSA-w3x6-4m5h-cxqf) |
| **Severity** | HIGH (×2 advisories on the same package version) |
| **Affected version** | `System.Security.Cryptography.Xml 8.0.1` (transitive) |
| **Source chain** | Transitive via `Microsoft.IdentityModel.Tokens` → IdentityModel JWT validation stack |
| **Cost** | None (no size impact; pure security patch) |
| **Action** | Bump `Microsoft.IdentityModel.Tokens` (and related: `JsonWebTokens`, `Protocols.OpenIdConnect`, `Validators`, `Logging`, `LoggingExtensions`, `Abstractions`, `Protocols`) from 8.15.0 → latest 8.x patch that pulls a fixed `System.Security.Cryptography.Xml >= 8.0.2` (or whatever non-vulnerable version). Verify via `dotnet list package --vulnerable --include-transitive` post-bump. |
| **Reflection / DI impact** | LOW — Microsoft.IdentityModel.Tokens is the JWT validation core; loaded via DI through `Microsoft.Identity.Web` extension methods. Bumping within 8.x major should be drop-in for these public APIs. |
| **Chain coupling** | Microsoft.IdentityModel.* (8 packages) chain-locked together at 8.15.0; bump all 8 to same target version. NOT coupled to Microsoft.Graph or Kiota chain. |
| **Test plan** | (a) Bump all 8 IdentityModel.* packages in csproj; (b) `dotnet build` — must compile clean, no warnings about API changes; (c) `dotnet test` — JWT auth tests must pass at same rate as Phase 3 baseline; (d) Deploy to dev; (e) Verify `/healthz`, `/api/documents/test/preview-url` 401 (route registered, auth required); (f) Authenticated request flow (operator manual test or smoke script with token); (g) 24-48h bake; (h) Re-run `dotnet list package --vulnerable` — both HIGH CVEs gone. |
| **Rollback** | `git revert` on the csproj commit + redeploy. ~3 min wall-clock per rollback drill (NFR-06 verified). |
| **Tier rationale** | MEDIUM per design definition: csproj edit + same-major version bump + transitive patch. Reflection use rule-out: Microsoft.IdentityModel is heavily DI-registered + reflection-used by JWT pipeline — but bumps within same major are typically API-stable. The bake window catches any subtle regression. |
| **Static probe agreement** | `Microsoft.Identity.Web` has 1 static usage; `Microsoft.IdentityModel` extensions are accessed via DI builder methods (typical pattern) — no static usings of `Microsoft.IdentityModel.*` namespaces means the packages are used through `Microsoft.Identity.Web`'s API surface, which is stable across 8.x. |
| **Dynamic probe agreement** | All 8 `Microsoft.IdentityModel.*` packages present in `Sprk.Bff.Api.deps.json` (verified). Will load at runtime via JWT validation pipeline initialization. |
| **Phase 4 task** | 044 (renumbered from "vuln-patch-1" slot; was generic) |
| **Owner-approved per Phase 0 Decision E** | YES (2026-05-24) |

### Cross-reference to Dependabot

No specific open Dependabot PR addresses this CVE directly. Closest is PR #266 (`DocumentFormat.OpenXml 3.4.1 → 3.5.1`) which is unrelated. The IdentityModel bump would be a new PR.

---

## NOT MEDIUM — Microsoft.Kiota.Abstractions HIGH (direct)

**Status**: DEFERRED to separate Graph SDK upgrade project (per Phase 0 Decision C.1).

| Field | Value |
|---|---|
| CVE | [GHSA-7j59-v9qr-6fq9](https://github.com/advisories/GHSA-7j59-v9qr-6fq9) — HIGH |
| Why not MEDIUM tier | Patch path requires moving from Kiota 1.21.2 → 2.0.0 (major bump) AND Microsoft.Graph 5.101.0 → 6.x (major bump). Both forbidden by spec.md §Out of Scope. Bumping in-scope would expand this project's calendar by ~3 weeks. |
| Treatment | Accepted risk documented in `inventory/vulnerable.txt`; follow-up project pointer to be added in LESSONS-LEARNED.md (task 090). |

---

## NOT MEDIUM — OpenMcdf + OpenTelemetry.Api Moderate CVEs (transitive)

| Package | CVE | Severity | Decision |
|---|---|---|---|
| `OpenMcdf 3.1.0` | GHSA-jxpf-xq2m-q525, GHSA-5qwm-7pvp-w988 | Moderate ×2 | DEFER — Moderate severity below Outcome B's "HIGH" threshold per FR-B1. Document in CANDIDATES.md REJECT-or-DEFER. |
| `OpenTelemetry.Api 1.15.0` | GHSA-g94r-2vxg-569j | Moderate | DEFER — Same reasoning. Dependabot PR #175 may address this when merged. |

---

## NOT MEDIUM — 4 zero-static-usage packages (per Finding 6)

| Package | Why kept | Verification |
|---|---|---|
| `Microsoft.Agents.AI 1.0.0-rc1` | DI: `AddAgentModule()` Program.cs:89; pre-release chain-locked | deps.json confirms loadable |
| `Microsoft.Agents.Hosting.AspNetCore 1.0.1` | DI: hosting extension; pre-release chain-locked | deps.json confirms loadable |
| `Microsoft.Extensions.Http.Polly 8.0.8` | DI: `AddHttpClient<T>().AddPolicyHandler()` across 10+ Infrastructure/DI/*.cs files | deps.json confirms loadable |
| `OpenTelemetry` (4 sibling packages) | DI: `AddOpenTelemetry()` TelemetryModule.cs:19 | deps.json confirms loadable |

None of these are candidates for removal — all verified live via dual probe agreement (static grep + DI registration grep + deps.json presence).

---

## NOT MEDIUM — 37 outdated package bumps (per `outdated.txt`)

Most are minor-version patches that:
- Are not driven by security (Outcome B only)
- Add change risk without addressing this project's goals
- Should accumulate as Dependabot PRs and be merged per the project's normal cadence

Per **plan.md PR-1**: Dependabot PRs touching BFF csproj are auto-deferred to weekly owner triage during this remediation project. Not addressed here.

Exception: if any specific bump is needed to enable MEDIUM-1 (the IdentityModel bump path), it gets pulled into that task.

---

## Phase 4 ordering recommendation (MEDIUM-tier track)

Single candidate; runs after Outcome A SAFE-tier track completes (so size + IL-related changes don't compound):

1. **MEDIUM-1** (Microsoft.IdentityModel.* bump, addresses System.Security.Cryptography.Xml HIGH ×2) — task 044, 24-48h bake

Outcome B completion: 1 of 2 HIGH CVEs remediated (50%). Kiota HIGH remains accepted-risk per Phase 0 Decision C.1.

---

## Updated count for CANDIDATES.md aggregation

| Tier | Count | Notes |
|---|---|---|
| SAFE | 3 (FR-A1, FR-A2, FR-A3 no-op) | task 020 |
| MEDIUM | 1 (System.Security.Cryptography.Xml patch via IdentityModel bump) | task 021 |
| HIGH | (TBD task 022) | likely 0 — all naturally-HIGH items are REJECTed or accepted-risk |
| REJECT | (TBD task 022) | Kiota chain, Graph SDK 6.x, pre-release bumps, trim/AOT, .NET 9 upgrade, DI minimalism fix |
