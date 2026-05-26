# ADR-029: BFF Publish Hygiene (Concise)

> **Status**: Accepted
> **Domain**: BFF API Deployment / Packaging
> **Last Updated**: 2026-05-26
> **Source project**: `sdap-bff-api-remediation-fix` Phase 4 (Outcome A + Outcome B), 2026-05-25

---

## Decision

The `Sprk.Bff.Api` publish output MUST be **framework-dependent linux-x64**, MUST exclude `wwwroot/**/*.js.map`, MUST patch known HIGH-severity transitive CVEs via explicit `<PackageReference>` overrides, and MUST stay within a documented size baseline ceiling. These four rules together cut the publish package from 75.2 MB compressed / 212.5 MB uncompressed (2026-05-19 drift point) to 45.65 MB / 139 MB and eliminate the entire multi-RID `runtimes/` tree (10 RIDs → 0). The decision is grounded in `Sprk.Bff.Api.csproj` properties and item groups; no PowerShell / CI flag flips its behavior at publish time.

---

## Constraints

### ✅ MUST

- **MUST** publish framework-dependent for Linux App Service via these two `Sprk.Bff.Api.csproj` properties:
  ```xml
  <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
  <SelfContained>false</SelfContained>
  ```
  `<SelfContained>false</SelfContained>` is explicit alongside the RID so a future bare `dotnet publish` cannot accidentally produce a self-contained build.
- **MUST** exclude wwwroot sourcemaps from publish output via:
  ```xml
  <ItemGroup>
    <Content Update="wwwroot\**\*.js.map" CopyToPublishDirectory="Never" />
  </ItemGroup>
  ```
  Sourcemaps remain in the source tree for local debugging.
- **MUST** patch known HIGH-severity transitive CVEs via explicit `<PackageReference>` overrides when the parent identity stack cannot be major-bumped in the current scope. Pattern (current canonical example):
  ```xml
  <PackageReference Include="System.Security.Cryptography.Xml" Version="8.0.3" />
  ```
  Patches GHSA-37gx-xxp4-5rgx + GHSA-w3x6-4m5h-cxqf (both HIGH); same-major bump; surgical scope.
- **MUST** stay within the documented publish-size baseline: compressed zip ≤ 50 MB (current measured 45.65 MB; ceiling = baseline + 10%) and uncompressed ≤ 150 MB. Phase 5 measured 45.65 MB compressed / 139 MB uncompressed / 279 files / 268 `deps.json` entries.
- **MUST** verify HIGH-severity vulnerability scan after any package change:
  ```
  dotnet list package --vulnerable --include-transitive
  ```
  Output MUST contain zero HIGH entries from in-scope packages (Kiota HIGH is accepted risk pending separate Graph SDK 6.x upgrade project).

### ❌ MUST NOT

- **MUST NOT** set `<PublishTrimmed>true</PublishTrimmed>` — reflection-hostile to Graph SDK, Identity.Web, EF Core, DI, and serializers; silent runtime breakage risk.
- **MUST NOT** set `<PublishAot>true</PublishAot>` — same reflection-hostility, more aggressive than trimming.
- **MUST NOT** ship multiple RIDs in `publish/runtimes/`. Linux App Service is the binding deploy target post Phase 4 task 019 dev-env migration; multi-RID adds dead native binaries.
- **MUST NOT** publish into a directory inside the project source tree (causes recursive artifact nesting on subsequent publishes). Publish to `deploy/api-publish/`.
- **MUST NOT** approve a PR that removes the `<RuntimeIdentifier>` / `<SelfContained>` / sourcemap-exclusion / transitive-override item groups without an explicit ADR amendment and CI override label.

---

## Rationale

The BFF publish package grew from ~60 MB to 75.2 MB compressed between 2026-03 and 2026-05 with no single offending PR — many small additions across multiple parallel projects each accepted a small increment. The `sdap-bff-api-remediation-fix` Phase 1 inventory found 10 platform RIDs (~77 MB uncompressed) in `runtimes/` even though the App Service is unambiguously Linux, 4 sourcemaps shipping to production, and 2 HIGH CVEs reachable transitively. The four MUST rules in this ADR are the minimum set that prevents that drift recurring.

Framework-dependent Linux-x64 publish is the maximum-leverage rule: it eliminates 10 RIDs at once. Sourcemap exclusion is a smaller win (~1 MB) but addresses a production hygiene + IP-exposure concern. The transitive override pattern is documented because it is genuinely the right tool when major version bumps of the parent identity stack are out of scope — and it is a surgical alternative that does not preclude a later full IdentityModel bump if other work requires it.

The publish-size baseline ceiling is documented here for CI to enforce (FR-C5; the `Deploy-BffApi.ps1` hard-fail size guard is the implementation; CI workflow guard is a follow-up). This ADR records the canonical baseline (45.65 MB compressed at Phase 5 close) and the +10% ceiling that the guard MUST honor.

---

## Verification

How to confirm any of these rules is currently honored:

| Rule | Verification command (run from `src/server/api/Sprk.Bff.Api/`) | Pass criterion |
|---|---|---|
| Linux-x64 only | `find publish/runtimes -type d` | Returns empty (no `runtimes/` directory) |
| No sourcemaps in publish | `find publish/wwwroot -name "*.js.map" \| wc -l` | Returns `0` |
| No HIGH transitive CVE for SCC.Xml | `dotnet list package --vulnerable --include-transitive \| grep "System.Security.Cryptography.Xml"` | Returns no output |
| Compressed publish size | `scripts/Deploy-BffApi.ps1` package-size line | ≤ 50 MB (45.65 MB current; ceiling 50 MB) |
| Uncompressed publish size | `du -sm deploy/api-publish/` | ≤ 150 MB (139 MB current) |
| csproj has all four rules | `grep -E "RuntimeIdentifier\|SelfContained\|wwwroot.*js\.map\|System\.Security\.Cryptography\.Xml" Sprk.Bff.Api.csproj` | Returns ≥ 4 matching lines |

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-001](ADR-001-minimal-api.md) | Single BFF runtime — this ADR ratifies that the runtime publishes for a single OS (Linux App Service) |
| [ADR-007](ADR-007-spefilestore.md) | Storage facade decouples Graph SDK surface — relevant because trimming would break Graph SDK reflection, this ADR keeps trimming OFF |
| [ADR-013](ADR-013-ai-architecture.md) | AI lives in BFF (refined 2026-05-20); publish hygiene applies to the unified AI + CRUD BFF — this ADR codifies that the unified package stays bounded |
| [ADR-028](ADR-028-spaarke-auth-architecture.md) | Identity.Web + IdentityModel + Kiota chain is publish-size and CVE relevant — transitive override pattern in this ADR applies surgically without disturbing the auth chain |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-029-bff-publish-hygiene.md](../../docs/adr/ADR-029-bff-publish-hygiene.md)

For detailed context including:
- Pre-Phase-4 state and 2026-05-19 drift inflection point
- Alternatives considered (trim/AOT/full Graph-SDK bump/full IdentityModel bump/multi-RID publish) with rejection rationale
- Phase 4 measured deltas (size, deps.json, smoke probes, reflection-load delta)
- Negative consequences and mitigations (csproj-OS coupling, deps.json reflection-probe baseline shift)
- Quarterly review cadence guidance

**Source evidence**: [`projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md`](../../projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md) Phase 4 Outcome A + Outcome B

**Operational rules**: [`.claude/constraints/azure-deployment.md`](../constraints/azure-deployment.md) Publish & Packaging

**Strategy reference**: [`docs/architecture/INFRASTRUCTURE-PACKAGING-STRATEGY.md`](../../docs/architecture/INFRASTRUCTURE-PACKAGING-STRATEGY.md) §5

---

**Lines**: ~140
**Pattern files**: csproj snippets are inlined above; no separate pattern file
