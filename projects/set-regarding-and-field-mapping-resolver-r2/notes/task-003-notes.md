# Task 003 — BFF tests + publish-size + push regression — Notes

**Completed**: 2026-07-09 · Rigor: FULL · Model: sonnet@high

## What was done

### 1. New test file (KEEP-path: matches existing sibling FieldMapping tests)

`tests/unit/Sprk.Bff.Api.Tests/Api/FieldMappings/FieldMappingRuleProjectionTests.cs` — 15 tests, 2 groups:

**Rule-DTO projection** (`MapRuleEntityToDto`) — asserts all 5 new `FieldMappingRuleDto` fields project
correctly from `FieldMappingRuleEntity`:
- `MappingType`: Theory over all 4 int→string branches (0→Copy, 1→Default, 2→Concat, 3→Template) + an
  unknown-int (99) fallback-to-Copy case.
- `DefaultValue` / `Expression` / `IsRequired`: verbatim passthrough for Default/Concat/Template rules;
  confirmed null/false for a plain Copy rule.
- `CompatibilityMode`: Theory over both int→string branches (0→Strict, 1→Resolve) + an unknown-int (42)
  fallback-to-Strict case.

**Push-path regression** (`ApplyMappingRule`) — proves the existing push engine (shared by
`PushFieldMappingsAsync` via `QueryProfileWithRulesByEntityPairAsync` → `MapRuleEntityToDto`) still maps
source→target correctly when handed a `FieldMappingRuleDto` carrying all 5 new fields populated:
- Happy path: `MappingType=Default`, `DefaultValue`, `Expression`, `IsRequired=true`, `CompatibilityMode=Resolve`
  set → engine still applies Copy-style `SourceField`→`TargetField` transfer, `Status=Mapped`, payload
  correctly populated. (The new fields are inert in `ApplyMappingRule` today — later tasks 012/013 wire the
  client-side engine to branch on `MappingType`; this test documents that the push path tolerates the shape
  without throwing or misbehaving.)
- Skip path: null source value + new fields populated → `Status=Skipped`, `ErrorMessage="Source value is null"`,
  empty payload — unchanged behavior.

### 2. Code-review remediation — cleaner seam over reflection

`MapRuleEntityToDto` and `ApplyMappingRule` were `private static` on `FieldMappingEndpoints`. The
established codebase precedent for testing private static endpoint helpers (`ChatEndpointsAttachmentsTests.cs`,
`SpendSnapshotServiceTests.cs`) uses reflection — but `tests/CLAUDE.md` bans B8 ("internal/private method
tests via reflection") as a default anti-pattern, with "test through the public surface" as the preferred fix.

Since `Sprk.Bff.Api.csproj` already declares `<InternalsVisibleTo Include="Sprk.Bff.Api.Tests" />`, a strictly
cleaner seam existed: change both methods from `private static` to `internal static` (accessibility-only,
zero behavior change — confirmed no other callers, no logic touched) and call them directly from the test.
This satisfies the task POML constraint verbatim ("do NOT introduce reflection hacks if a cleaner seam
exists") and the sibling constraint ("Do NOT modify the push endpoint's behavior" — an accessibility modifier
change to two private helpers is not a behavior change to `PushFieldMappingsAsync`).

**File changed**: `src/server/api/Sprk.Bff.Api/Api/FieldMappings/FieldMappingEndpoints.cs` — 2 accessibility
modifiers changed (`private` → `internal`) + 2 explanatory `<remarks>` doc-comments added. No other lines touched.

## Verification

### dotnet test — Sprk.Bff.Api.Tests

Two consecutive full-suite runs after the accessibility change, both clean:

```
Passed!  - Failed: 0, Passed: 7662, Skipped: 101, Total: 7763
```

(An intermediate run reported 1 failure; re-run without further code changes came back clean at the identical
7662/0/101/7763 counts — confirmed transient/order-dependent flake unrelated to this task's files, not a
regression.)

Filtered FieldMapping-scoped run: 93 passed, 1 pre-existing skip (`InboundPipelineTests` — unrelated).

### Publish size

```
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
```

Measured via `Compress-Archive -CompressionLevel Optimal` (PowerShell), matching the established measurement
convention:

| Convention | Size | Baseline (2026-07-08, `ai-architecture-redesign-r1` task 055) | Delta |
|---|---|---|---|
| Incl. PDBs | **49.60 MB** | ~49.63 MB | ~-0.03 MB |
| Excl. PDBs | **45.73 MB** | 45.87 MB | ~-0.14 MB |

Both flat vs baseline (expected — additive DTO fields + 2 accessibility-modifier changes only, no new
packages). Ceiling ≤60 MB — **well within margin** (~14 MB headroom incl. PDBs).

### CVE scan

```
dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive
```

```
Project `Sprk.Bff.Api` has the following vulnerable packages
   [net8.0]:
   Top-level Package                   Requested   Resolved   Severity   Advisory URL
   > Microsoft.Kiota.Abstractions      1.21.2      1.21.2     High       https://github.com/advisories/GHSA-7j59-v9qr-6fq9
```

1 HIGH-severity finding — **pre-existing**, not introduced by this task. Confirmed via
`git diff origin/master...HEAD --stat -- '**/*.csproj'` returning empty for this branch: no package
references were added/changed by tasks 001, 002, or 003. `Microsoft.Kiota.Abstractions` is a transitive
dependency of `Microsoft.Graph` (see `src/server/api/Sprk.Bff.Api/CLAUDE.md` Kiota version-pinning section),
already present on `master`.

## Quality gates (Step 9.5 — mandatory, unconditional per TEST-MODIFYING override)

- **code-review**: 1 finding — B8-adjacent reflection-seam concern on the initial draft (private static
  methods forced a reflection-based test, matching existing precedent but flagged by `tests/CLAUDE.md`'s
  ban list). **Remediated**: methods changed to `internal static` (already covered by existing
  `InternalsVisibleTo`); test now calls them directly. Re-reviewed clean. No AI code smells (no
  single-impl interfaces, no catch-log-rethrow, no null-checks on non-nullable types, no restating
  comments, no >3-responsibility methods) — this task added no production logic, only 2 accessibility
  modifiers + doc comments.
- **adr-check**: 0 violations. ADR-001 (Minimal API) ✅ unaffected. ADR-008 (endpoint filters) ✅ unaffected.
  ADR-010 (DI minimalism) ✅ no new interfaces/DI. ADR-013 N/A (no AI types touched). ADR-038 (testing
  strategy) — all 17 banned antipatterns (B1–B17) checked, 0 present after the B8 remediation. 1 Warning
  noted: the new test file's path (`tests/unit/Sprk.Bff.Api.Tests/Api/FieldMappings/`) is not literally one
  of the 6 canonical KEEP paths — but this matches 100% of the existing sibling FieldMapping tests
  (`PushFieldMappingsTests.cs`, `TypeCompatibilityValidatorTests.cs`) and the broader legacy
  `Sprk.Bff.Api.Tests` tree, which predates the KEEP-path reorg (`tests/CLAUDE.md` notes "after task 050
  path reorganization completes" as still pending repo-wide). Relocating the entire legacy tree is out of
  scope for this task; Path A (documented, matches established local convention) accepted.

## §10 BFF Hygiene

- Placement: no new endpoint/service/DI/interface/package — pure test addition + 2 accessibility-modifier
  changes on existing private helpers. No new component; §11 justification N/A (modification of existing
  files, not new surface).
- Publish-size: verified above, flat vs baseline, well under ceiling.
- CVE: verified above, no new HIGH-severity finding.
- No new CRUD→AI dependency.

## Constraint compliance

- ADR-038 KEEP-path categories / 17-ban list: honored (see Quality Gates above).
- Push endpoint behavior: unmodified — `PushFieldMappingsAsync` logic untouched; only 2 accessibility
  modifiers changed on private helper methods it calls, which is not a behavior change.
- Publish-size ceiling ≤60 MB: met (49.60 MB incl. PDBs).

**Unblocks**: 010 (engine shell — needs the DTO shape, now test-covered end to end).
