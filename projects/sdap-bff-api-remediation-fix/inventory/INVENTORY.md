# BFF Remediation — Master Inventory (Phase 1)

> **Compiled**: 2026-05-24
> **By**: Phase 1 tasks 010-018
> **Gates**: Phase 2 candidate categorization
> **Status**: COMPLETE — all 14 Phase 1 outputs produced; CI workflow inventory done; operator sign-off below

---

## Document index

| Output | Source task | File | Status |
|---|---|---|---|
| Direct package list | 010 | [`packages-direct.txt`](./packages-direct.txt) | ✅ 44 direct packages |
| Transitive package list | 010 | [`packages-transitive.txt`](./packages-transitive.txt) | ✅ ~485+ packages |
| Vulnerable scan | 011 | [`vulnerable.txt`](./vulnerable.txt) | ✅ 2 HIGH, 3 Moderate |
| Outdated scan | 011 | [`outdated.txt`](./outdated.txt) | ✅ 37 packages with updates |
| Pre-release tracker | 012 | [`prereleases.md`](./prereleases.md) | ✅ 3 pre-release pins confirmed (all rationale still valid) |
| Project reference graph | 013 | [`project-references.md`](./project-references.md) | ✅ Sprk.Bff.Api → Core → Dataverse |
| Static usage map | 014 | [`usage-map-static.md`](./usage-map-static.md) | ✅ 0 SAFE-tier removals from static analysis |
| Reflection-load probe (pragmatic) | 015 | [`reflection-probe.txt`](./reflection-probe.txt) | ✅ 526 deps.json entries + DI registration verification for 4 zero-static-usage packages |
| Native binary inventory | 016 | [`native-binaries.md`](./native-binaries.md) | ✅ 10 RIDs found; ~77 MB total |
| wwwroot asset inventory | 016 | [`wwwroot-assets.md`](./wwwroot-assets.md) | ✅ 4 sourcemaps confirmed |
| Size by category | 016 | [`size-by-category.md`](./size-by-category.md) | ✅ 212 MB uncompressed breakdown |
| App Service runtime | 017 | [`app-service-runtime.json`](./app-service-runtime.json) | ✅ **CRITICAL: dev=Windows, prod=Linux** |
| Deployed SHA-256s | 017 | [`deployed-sha256.txt`](./deployed-sha256.txt) | ✅ 223 loadable artifacts |
| Publish metrics | 017 | [`publish-metrics.txt`](./publish-metrics.txt) | ✅ Matches 2026-05-19 drift point exactly |
| CI workflow inventory | 018 | [`ci-workflow-inventory.md`](./ci-workflow-inventory.md) | ✅ Both workflows enumerated; G-3 findings recorded |

---

## 🚨 Critical findings (require Phase 2 owner attention)

### Finding 1 — ✅ RESOLVED 2026-05-24 (via task 019)
- **Original finding**: dev `spe-api-dev-67e2xz` = Windows; prod `spaarke-bff-prod` = Linux; design assumption wrong for dev
- **Resolution**: New Linux App Service `spaarke-bff-dev` provisioned in `rg-spaarke-dev` (West US 2). UAMI `mi-bff-api-dev` attached cross-RG; all 173 App Settings migrated (2 colon-keys skipped, both had `__` siblings); full UAMI → Dataverse chain verified via `/healthz/dataverse` returning 200. Old Windows dev parallel-running pending operator-paced cutover.
- **All 3 envs now Linux**: dev (`spaarke-bff-dev`), demo (`spaarke-bff-demo`), prod (`spaarke-bff-prod`). FR-A1 (`--runtime linux-x64`) applies cleanly across all envs.
- See [`baseline/linux-dev-migration.md`](../baseline/linux-dev-migration.md) for full migration record + operator cutover checklist.

### Finding 2 — ✅ RESOLVED (terminology confusion)
- Original finding was wrong: I queried only the dev subscription. Demo is in a separate subscription (`2ff9ee48-6f1d-4664-865c-f11868dd1b50`).
- **Actual topology**:
  - dev: `spaarke-bff-dev` (Linux, new) + `spe-api-dev-67e2xz` (Windows, legacy) — both in dev subscription
  - demo: `spaarke-bff-demo` (Linux) in `rg-spaarke-demo` (demo subscription)
  - prod: `spaarke-bff-prod` (Linux) in `rg-spaarke-platform-prod` (dev subscription) — exists but **NOT actively used yet**
- **Phase 5 implication**: prod is functional but hasn't been used. First real prod deploy via task 062 is a milestone, not a routine promotion. App Insights signal during 7-day prod observation (task 063) will be sparse — adjust expectations accordingly.

### Finding 3 — HIGH vuln (NU1903 on Kiota) cannot be fixed within current scope
- Microsoft.Kiota.Abstractions 1.21.2 has GHSA-7j59-v9qr-6fq9 (HIGH)
- Latest Kiota is 2.0.0 (major bump); requires Microsoft.Graph 5.101.0 → 6.x (also major bump)
- Spec §Out of Scope: "Graph SDK or Kiota version changes" — explicitly forbidden
- BFF CLAUDE.md: "All Kiota packages MUST be the same version" — chain-lock binding
- **Phase 2 must decide**: (a) revisit Out-of-Scope binding to address NU1903, OR (b) accept as documented risk pending separate Graph SDK upgrade project
- 2 HIGH vuln on `System.Security.Cryptography.Xml 8.0.1` (transitive) may be fixable via Microsoft.IdentityModel.Tokens or similar bump

### Finding 4 — FR-A3 (Cosmos ServiceInterop dedup) is already-resolved
- Search for `ServiceInterop.dll` in current publish: NOT FOUND
- Upstream Cosmos SDK 3.47.0 has resolved the historical duplication
- **Phase 4 task 042 may be a no-op** — verify before scheduling

### Finding 5 — Pre-release pinning rationale all still valid
- Azure.AI.OpenAI 2.8.0-beta.1, Microsoft.Agents.AI 1.0.0-rc1, Azure.AI.Projects 1.0.0-beta.8 chain pin remains binding
- No Phase 4 action needed per FR-B3 (re-verification)

### Finding 6 — 4 zero-static-usage packages all confirmed live via DI + deps.json
- Microsoft.Agents.AI, Microsoft.Agents.Hosting.AspNetCore, Microsoft.Extensions.Http.Polly, OpenTelemetry (4 packages)
- All registered via DI extension methods at known sites
- All present in `Sprk.Bff.Api.deps.json` runtime contract
- Phase 2 categorization should mark all 4 as KEEP regardless of static grep zero finding

---

## Key metrics (current state baseline)

| Metric | Value | Baseline target | Status |
|---|---|---|---|
| Compressed publish (zip) | 75.2 MB | ≤ 60 MB | -15.2 MB over baseline |
| Uncompressed publish | 212 MB | ≤ 150 MB | -62 MB over target |
| File count | 287 | ~240 | -47 over baseline |
| Direct packages | 44 | n/a | reference |
| Transitive packages (deps.json) | 526 | n/a | reference |
| HIGH vuln (direct) | 1 (Kiota chain-locked) | 0 | binding-blocked |
| HIGH vuln (transitive) | 1 (System.Security.Cryptography.Xml) | 0 | may be patchable |
| Moderate vuln | 3 | n/a | low priority |
| Pre-release pins | 3 (all rationale valid) | maintain | OK |
| Reflection-loaded suspect packages | 4 (all confirmed live) | 0 actually-unused | OK |

---

## Projected Outcome A trajectory (if SAFE candidates land)

| Strategy | Compressed projection | Uncompressed projection |
|---|---|---|
| FR-A1 (RID trim) + FR-A2 (sourcemaps) + FR-A3 (no-op) — Linux target | ~55 MB ✅ | ~140 MB ✅ |
| FR-A1 (RID trim) + FR-A2 (sourcemaps) + FR-A3 (no-op) — Windows dev target | ~62 MB ⚠️ (over 60 baseline) | ~158 MB ⚠️ (over 150 target) |

If dev stays Windows AND FR-A1 trims to win-x64 only, dev publish ends up larger than prod publish (Windows native libs are heavier). Worth Phase 2 evaluation.

---

## Phase 1 Gate — Owner Sign-Off

**Per design.md §6 Phase 1 Gate**: INVENTORY.md must be reviewed and signed off by the project owner before Phase 2 starts.

**Operator-only model per NFR-08 revised**: This gate sign-off is recorded via the Phase 1 completion commit on this branch (operator authorship = sign-off).

### Pre-Phase-2 acknowledgment checklist

- [x] All 14 Phase 1 outputs produced and committed
- [x] 6 critical findings documented (dev/prod OS mismatch, demo env missing, HIGH Kiota CVE scope issue, FR-A3 no-op, pre-release valid, zero-static-usage verification)
- [x] CI workflow inventory complete; G-3 version sanity flagged for task 075
- [x] Compressed + uncompressed metrics match 2026-05-19 drift point (no further drift)
- [x] Reflection-probe approach documented (deps.json + DI grep — pragmatic vs full Program.cs probe)

**Phase 2 is AUTHORIZED to begin** upon completion commit of this INVENTORY.md.

---

## Phase 2 input

Phase 2 (tasks 020-022) will categorize SAFE / MEDIUM / HIGH / REJECT candidates using this inventory. The 6 critical findings above MUST drive Phase 2 candidate decisions, especially:
- Finding 1 → FR-A1 candidate categorization (SAFE if single-RID per env, MEDIUM if env-specific CI logic needed)
- Finding 3 → vuln remediation strategy (may require scope revisit)
- Finding 4 → FR-A3 may drop from Phase 4 plan
- Finding 6 → 4 "removable per static analysis" entries should be re-flagged as KEEP

Phase 2 candidate file: `projects/sdap-bff-api-remediation-fix/CANDIDATES.md` (task 022 output).
