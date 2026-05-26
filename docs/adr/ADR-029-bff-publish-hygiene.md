# ADR-029: BFF Publish Hygiene

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2026-05-25 (Phase 4 close) |
| Updated | 2026-05-26 (ratified post-Phase-5) |
| Authors | Spaarke Engineering |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-029 Concise](../../.claude/adr/ADR-029-bff-publish-hygiene.md) — ~140 lines, decision + MUST/MUST NOT + verification
- [Azure Deployment Constraints](../../.claude/constraints/azure-deployment.md) — Publish & Packaging MUST rules
- [Infrastructure Packaging Strategy §5](../architecture/INFRASTRUCTURE-PACKAGING-STRATEGY.md) — strategic context

**When to load this full ADR**: PR review for csproj publish-property changes, Graph SDK / IdentityModel upgrade design, debating trim/AOT, multi-OS App Service decisions, CVE remediation strategy.

---

## Context

The `Sprk.Bff.Api` publish package grew from a ~60 MB compressed baseline (early 2026) to **75.19 MB compressed / 212 MB uncompressed** by the 2026-05-19 measurement that triggered the `sdap-bff-api-remediation-fix` project. Diagnostic findings (Phase 1 inventory, tasks 010–018):

1. **10 native runtime IDs (RIDs) in `publish/runtimes/`** (~77 MB uncompressed) — `win-x64`, `win-x86`, `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64`, `linux-musl-x64`, plus more. The App Service target is unambiguously Linux post Phase 4 task 019 (dev-env migration completed 2026-05-25); all non-Linux RIDs are dead weight. Multi-RID publish is the .NET SDK default when no `<RuntimeIdentifier>` is set, so the bloat had crept in silently across multiple parallel projects.
2. **4 JavaScript sourcemaps in `wwwroot/playbook-builder/assets/`** shipping to production — `flow-vendor-BHHmI87s.js.map`, `fluent-vendor-CmJVTK5h.js.map`, `index-BWeOj5bW.js.map`, `react-vendor-BWFb42Va.js.map`. Sourcemaps reveal full pre-minified source code, which is both a packaging-hygiene issue (~1 MB) and an IP exposure concern.
3. **2 HIGH-severity CVEs reachable transitively** via `Microsoft.IdentityModel.Tokens` → `System.Security.Cryptography.Xml 8.0.1`:
   - GHSA-37gx-xxp4-5rgx — XML signature bypass (HIGH)
   - GHSA-w3x6-4m5h-cxqf — XML schema validation (HIGH)
4. **No published-size CI guard**. `Deploy-BffApi.ps1` had a warn-only size check; CI had no size, RID, or sourcemap gate. Drift was undetectable until manual investigation.
5. **A meta-pattern of process failure**: many small projects (R1, R2, R3, Insights Engine, others) each added BFF features without holistic consideration of overall publish quality. No single PR was wrong, but the cumulative effect was material drift. This pattern is documented in `.claude/FAILURE-MODES.md` and addressed structurally by [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) (per root CLAUDE.md §10).

The remediation project executed five outcomes in parallel; this ADR codifies the publish-hygiene rules from Outcomes A (size) and B (security) so the same drift cannot recur silently.

## Decision

Four binding publish hygiene rules apply to `Sprk.Bff.Api`:

### 1. Framework-Dependent linux-x64 RID (FR-A1)

`Sprk.Bff.Api.csproj` MUST declare:

```xml
<RuntimeIdentifier>linux-x64</RuntimeIdentifier>
<SelfContained>false</SelfContained>
```

`<SelfContained>false</SelfContained>` is explicit alongside the RID so a future bare `dotnet publish` (without `--no-self-contained`) cannot accidentally produce a self-contained build that re-bloats the package with the entire .NET runtime. Framework-dependent is canonical per ADR-001 (single BFF runtime, .NET supplied by App Service Linux host).

This eliminates the entire `publish/runtimes/` directory tree on dev/demo/prod, because Linux App Service binds to OS-installed native libraries (OpenSSL, ICU, etc.) rather than requiring SDK-shipped natives.

### 2. Sourcemap Exclusion (FR-A2)

`Sprk.Bff.Api.csproj` MUST contain an `<ItemGroup>` excluding wwwroot sourcemaps from publish:

```xml
<ItemGroup>
  <Content Update="wwwroot\**\*.js.map" CopyToPublishDirectory="Never" />
</ItemGroup>
```

`CopyToPublishDirectory="Never"` (not `"PreserveNewest"`, not `"Always"`) is the key — it keeps the files in the source tree for local development tooling but never copies them to publish output. The 4 currently-existing sourcemaps in `wwwroot/playbook-builder/assets/` remain in source unchanged.

### 3. Transitive Vulnerability Override Pattern (FR-B1)

When a transitive package has a known HIGH-severity CVE and major-bumping the parent identity stack is out of scope (large blast radius, calendar weeks, breaking API changes), the canonical remediation is an explicit `<PackageReference>` override in `Sprk.Bff.Api.csproj`. Current canonical example:

```xml
<!-- FR-B1 (task 044): patch System.Security.Cryptography.Xml transitive 8.0.1 -> 8.0.3 to fix
     GHSA-37gx-xxp4-5rgx + GHSA-w3x6-4m5h-cxqf (both HIGH). Pulled in transitively via
     Microsoft.IdentityModel.Tokens (JWT validation stack); same-major bump per Phase 2 MEDIUM-1. -->
<PackageReference Include="System.Security.Cryptography.Xml" Version="8.0.3" />
```

Constraints on the pattern:
- The override version MUST be the **same major** as the transitive resolution would otherwise produce (8.x → 8.x in the canonical case). Cross-major overrides invite ABI surprises.
- The override MUST carry an inline `<!-- comment -->` citing the specific GHSA / CVE IDs it patches and the upstream parent chain.
- The pattern is meant for **surgical, time-bound** application. If the same parent chain accrues multiple overrides, that is a signal to plan the proper major bump as a separate project. Currently 2 overrides in scope (`System.Text.RegularExpressions 4.3.1` predating this ADR, and `System.Security.Cryptography.Xml 8.0.3` from task 044).

### 4. Publish-Size Baseline Ratchet (FR-A4 / FR-A5 / FR-C5)

The Phase 5 measured publish baseline is the documented ceiling for future CI guards:

| Metric | Measured (Phase 5 close, 2026-05-25) | Documented ceiling |
|---|---:|---:|
| Compressed publish (`.zip`) | **45.65 MB** | 50 MB (baseline + 10%) |
| Uncompressed publish (`du -sm deploy/api-publish/`) | **139 MB** | 150 MB |
| File count | **279** | informational only |
| `Sprk.Bff.Api.deps.json` entries | **268** | informational; baseline for reflection-load probe |
| Runtime native binaries (`publish/runtimes/`) | **0** (entire tree eliminated) | 0 (binding) |
| HIGH-severity transitive CVEs in scope | **0** (Kiota HIGH explicitly accepted-risk pending Graph SDK 6.x upgrade) | 0 (excluding documented accepted-risk packages) |

The `Deploy-BffApi.ps1` hard-fail size guard implementation (project task 070) reads the 50 MB ceiling. A CI workflow guard (FR-C5) is a planned follow-up; this ADR documents the baseline for that future guard to consume.

## Consequences

**Positive:**
- **-35% uncompressed publish size** (212.5 MB → 139 MB) and **-37% compressed** (72.9 MB → 45.65 MB) — direct deploy-time and storage benefits.
- **-49% `deps.json` entries** (526 → 268) — runtime asset resolver has less to scan on cold start; reduces startup-time variance.
- **Entire `runtimes/` tree eliminated** — no more multi-platform native binaries shipped to a Linux host that cannot execute them.
- **2 HIGH CVEs resolved** without disturbing the Microsoft.IdentityModel.* 8.15.0 → 8.18.0 family upgrade (which has a wider blast radius and is reverse-decision-able later if needed for other reasons).
- **Clearer deployment story** — one OS, one RID, one explicit framework-dependent property. Future deploy debugging starts from a known publish shape.
- **Documented drift recovery pattern** — the rules + verification commands in this ADR are the template for catching the next drift event before it accumulates to material levels.

**Negative:**
- **Hidden tie between `Sprk.Bff.Api.csproj` and target App Service OS**. If a future environment needs Windows App Service or a different Linux flavor (musl, ARM64), the csproj `<RuntimeIdentifier>` MUST change. Mitigation: Phase 4 task 019 standardized dev → Linux App Service (matching demo and prod); all known environments are now `linux-x64`. If a Windows env becomes necessary, the multi-RID publish alternative is documented under Alternatives Considered below.
- **`deps.json` shape changed** (526 → 268 entries, -49%). This affects the reflection-load probe baseline at `projects/sdap-bff-api-remediation-fix/baseline/reflection-probe-baseline.txt`. The shift is documented in the post-Phase-4 entry of that baseline file; future reflection-load probes should compare against the post-Phase-4 baseline, not the pre-Phase-4 526-entry baseline.
- **Transitive override pattern can be abused**. Each override is a small piece of technical debt that delays the inevitable proper major bump of the parent chain. Mitigation: ADR-mandated inline comment with GHSA citation; explicit policy that >2 overrides on the same chain triggers a major-bump project.

**Neutral:**
- Sourcemap exclusion has no debugging-experience impact because source-tree sourcemaps remain. PCF and Code Pages have their own bundled sourcemaps not affected by this rule.
- `dotnet publish --runtime linux-x64` continues to work without explicit `--runtime` flag because the csproj `<RuntimeIdentifier>` property is the source of truth. Existing `Deploy-BffApi.ps1` and CI invocations did not need modification.

## Alternatives Considered

| Alternative | Rejection reason |
|---|---|
| **`<PublishTrimmed>true</PublishTrimmed>`** | Reflection-hostile to Microsoft.Graph SDK, Microsoft.Identity.Web, EF Core, the .NET DI container, and `System.Text.Json` polymorphic serializers. The BFF heavily uses all five. Trimming would produce silent runtime failures (NullReferenceException from a "trimmed" type, missing constructors on DI-resolved types, missing JsonConverter targets). Test coverage cannot reliably catch trim-induced regressions because trimming decisions are made at publish time, not test time. Permanently excluded for the BFF. |
| **`<PublishAot>true</PublishAot>`** | Same reflection-hostility as trimming, plus more aggressive (`PublishAot` implies `PublishTrimmed`). Same rejection rationale. Additionally, several BFF packages (`Microsoft.Agents.AI 1.0.0-rc1`, `Azure.AI.OpenAI 2.8.0-beta.1`, `Azure.AI.Projects 1.0.0-beta.8`) are pre-release and have not certified AOT compatibility. Permanently excluded. |
| **Microsoft.Graph 5.x → 6.x + Microsoft.Kiota 1.x → 2.x major bump** | Would also patch Kiota's NU1903 HIGH CVE (GHSA-7j59-v9qr-6fq9) and shrink the Graph SDK transitive footprint. Out of scope per project spec (chain-locked Kiota dependency per `Sprk.Bff.Api/CLAUDE.md` Package Management section). Estimated calendar: ~3 weeks for safe migration + bake. Tracked as a separate follow-up project; the Kiota HIGH is accepted-risk in the interim per Phase 0 Decision C.1 of the remediation project. |
| **Full Microsoft.IdentityModel.* family bump 8.15.0 → 8.18.0** | Would patch `System.Security.Cryptography.Xml` via a different (cleaner) path — by pulling in the newer IdentityModel chain that already references 8.0.3. Rejected for THIS project because the blast radius is 8 packages × all consumers of JWT validation, refresh tokens, OIDC discovery, key rollover, etc. Surgical transitive override is reverse-decision-able: if a future project needs the full IdentityModel bump for other reasons, the `<PackageReference Include="System.Security.Cryptography.Xml" Version="8.0.3" />` line is removed in the same PR. Smaller blast radius wins for the current scope. |
| **Multi-RID publish (`linux-x64` + `win-x64`)** | Not used because dev, demo, and prod are all Linux App Service post Phase 4 task 019 (dev-env migration completed 2026-05-25; documented at `projects/sdap-bff-api-remediation-fix/baseline/linux-dev-migration.md`). If a Windows env becomes necessary, the csproj would change to omit `<RuntimeIdentifier>` and rely on `dotnet publish --runtime <rid>` per-environment in CI. Documented here for reverse-decision visibility. |

## Operationalization

### What enforces this ADR

| Mechanism | Scope |
|---|---|
| `Sprk.Bff.Api.csproj` property + item-group declarations | Compile-time / publish-time enforcement of rules 1, 2, 3 |
| `Deploy-BffApi.ps1` hard-fail size guard | Pre-deploy enforcement of rule 4 (50 MB ceiling) |
| `.claude/constraints/azure-deployment.md` Publish & Packaging | Documented MUST rules for PR review |
| `.claude/constraints/bff-extensions.md` | Binding pre-merge checklist for ALL BFF additions (per root CLAUDE.md §10) — references this ADR's size ceiling |
| `.claude/skills/bff-deploy/SKILL.md` Publish Hygiene section | Operational guidance for any BFF deploy |
| Planned CI workflow guard (FR-C1, FR-C2, FR-C3, FR-C5) | Per-PR enforcement of rules 1, 2, 3, 4 in `.github/workflows/sdap-ci.yml` (project task 071) |
| Quarterly publish-hygiene review | Walk the verification table in the concise ADR; record results in `projects/sdap-bff-api-remediation-fix/LESSONS-LEARNED.md` |

### Verification commands

How to confirm any rule is currently honored (run from `src/server/api/Sprk.Bff.Api/` after `dotnet publish`):

```bash
# Rule 1: linux-x64 only, no runtimes/ tree
find publish/runtimes -type d
# Expected: empty (no runtimes/ directory exists)

# Rule 2: no sourcemaps in publish
find publish/wwwroot -name "*.js.map" | wc -l
# Expected: 0

# Rule 3: no HIGH transitive CVE for System.Security.Cryptography.Xml
dotnet list package --vulnerable --include-transitive | grep "System.Security.Cryptography.Xml"
# Expected: no output

# Rule 4: package size within ceiling
# Run Deploy-BffApi.ps1; check "Package size" line
# Expected: <= 50 MB compressed

# Verify csproj has all four rules declared
grep -E "RuntimeIdentifier|SelfContained|wwwroot.*js\.map|System\.Security\.Cryptography\.Xml" Sprk.Bff.Api.csproj
# Expected: >= 4 matching lines
```

### Review cadence

- **Quarterly review** (set: 2026-08-25, then 2026-11-25, then 2027-02-25, ...): walk the verification table; record any drift in `projects/sdap-bff-api-remediation-fix/LESSONS-LEARNED.md`.
- **Per-PR** when a PR touches `Sprk.Bff.Api.csproj`, adds a package, or modifies `wwwroot/`: PR reviewer MUST run the verification commands locally and paste results in the PR.
- **On any `dotnet list package --outdated` weekly Dependabot triage**: cross-check against the four rules; if a Dependabot bump would remove the `System.Security.Cryptography.Xml` override path, ensure the replacement chain still patches the CVE pair.

## References

| Source | Purpose |
|---|---|
| `projects/sdap-bff-api-remediation-fix/spec.md` | FR-A1 / FR-A2 / FR-A3 / FR-B1 / FR-A4 / FR-A5 / FR-C5 — original requirements |
| `projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md` Phase 4 Outcome A + Outcome B | Full evidence (size deltas, smoke probes, deploy logs, reflection-load delta) |
| `projects/sdap-bff-api-remediation-fix/baseline/` | Phase 3 baseline + Phase 4 post-deploy artifacts |
| `.claude/constraints/azure-deployment.md` Publish & Packaging | Codified MUST rules for routine PR review |
| `docs/architecture/INFRASTRUCTURE-PACKAGING-STRATEGY.md` §5 | Strategic context for BFF binary packaging |
| `.claude/constraints/bff-extensions.md` | Pre-merge checklist for any BFF addition (binding per root CLAUDE.md §10) |
| `.claude/FAILURE-MODES.md` | "Many-projects-each-adding-without-considering-overall-quality" process-failure pattern that this ADR addresses structurally |

### Related ADRs

| ADR | Relationship |
|---|---|
| [ADR-001](ADR-001-minimal-api-and-workers.md) | Single BFF runtime — this ADR ratifies that the runtime publishes for a single OS (Linux App Service) |
| [ADR-007](ADR-007-spe-storage-seam-minimalism.md) | Storage facade decouples Graph SDK surface — relevant because trimming would break Graph SDK reflection; this ADR keeps trimming OFF |
| [ADR-013](ADR-013-ai-architecture.md) | AI lives in BFF (refined 2026-05-20); this ADR codifies that the unified AI + CRUD BFF package stays bounded |
| [ADR-028](ADR-028-spaarke-auth-architecture.md) | Identity.Web + IdentityModel + Kiota chain is publish-size and CVE relevant — transitive override pattern in this ADR applies surgically without disturbing the auth chain |
| ADR-010 (DI minimalism) | Tangential: DI registration count is a different drift signal; this ADR addresses publish-output drift specifically |

---

## AI-Directed Coding Guidance

- When asked to add a NuGet package to `Sprk.Bff.Api`, FIRST check the four rules in this ADR against the addition. State the placement decision per [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md).
- When asked to "fix a HIGH CVE", check if the override pattern applies before recommending a major version bump. Cite the GHSA/CVE in an inline csproj comment.
- When asked to enable `PublishTrimmed` or `PublishAot` "for performance", refuse and cite this ADR. Direct the user to the Alternatives Considered section.
- When asked to ship multi-RID, refuse and cite this ADR. Direct the user to the Alternatives Considered multi-RID row.
- When investigating a publish-size regression, walk the verification commands first; the rule that fails verification is the one to debug.

---

*Document Owner: Spaarke Engineering · Originating project: `sdap-bff-api-remediation-fix` (Phase 4)*
