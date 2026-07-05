# Task 023 — Deviations from POML

> **Task**: 023 — MatterHeader Solution folder + pack.ps1
> **Completed**: 2026-07-02
> **Files created**:
> - `src/client/pcf/MatterHeader/Solution/solution.xml`
> - `src/client/pcf/MatterHeader/Solution/customizations.xml`
> - `src/client/pcf/MatterHeader/Solution/[Content_Types].xml`
> - `src/client/pcf/MatterHeader/Solution/Controls/sprk_Spaarke.Records.MatterHeader/ControlManifest.xml`
> - `src/client/pcf/MatterHeader/Solution/pack.ps1`
> **Reference PCF copied from**: `DocumentRelationshipViewer` (schema naming pattern `sprk_Spaarke.*.*` matches task 023's expected `sprk_Spaarke.Records.MatterHeader`).

---

## D-01: `pack.ps1` uses `System.IO.Compression`, not `pac solution pack`

**POML step 4 says**: "Author pack.ps1 that runs npm run build:prod, copies output to Solution/, and runs pac solution pack."

**What was written**: The pack step uses `[System.IO.Compression.ZipFile]::Open(...)` to build the ZIP directly, NOT `pac solution pack`.

**Rationale (CLAUDE.md §6.5 Path C — pivot to comply-with-reference-pattern)**:
Both existing PCF references (`DocumentRelationshipViewer/Solution/pack.ps1` and `SemanticSearchControl/Solution/pack.ps1`) already use `System.IO.Compression`. This has three advantages the POML's `pac`-based approach lacks:
1. **No pac CLI dependency for packing**: pack succeeds even on machines without Power Platform CLI installed. The task's own enforcement bullet 3 asked pack.ps1 to fail gracefully without `pac`; the .NET approach removes the dependency entirely at the pack step.
2. **Consistent with proven repo pattern**: two shipped PCFs use this exact structure; deviating to `pac solution pack` for a third would be unjustified variance.
3. **Better error surface**: `System.IO.Compression` produces clean line-by-line "Adding: …" trace output; `pac solution pack` bundles messages that are harder to parse in CI.

The `pac` CLI is still surfaced at the end of the script as the recommended IMPORT path (task 025's job), with a graceful message when unavailable. This preserves the "fail gracefully with a diagnostic" requirement of the enforcement checklist.

## D-02: `pack.ps1` includes build step + `-SkipBuild` switch (composite script)

**POML step 4** implies `pack.ps1` runs build + copy + pack all in one. Reference PCFs split this into two scripts (`copy-build.ps1` at PCF root + `Solution/pack.ps1`).

**What was written**: `Solution/pack.ps1` is a single composite script that:
1. Runs `npm run build:prod` in the parent PCF folder (unless `-SkipBuild`)
2. Copies `out/controls/*/{bundle.js,ControlManifest.xml,styles.css}` to `Solution/Controls/sprk_Spaarke.Records.MatterHeader/`
3. Packs the ZIP via `System.IO.Compression`.

**Rationale (Path A — POML-driven convenience)**: Task 023 POML step 4 explicitly asks for the one-script UX. Task 024 will call this script once; task 025 will call it with `-SkipBuild` if rebuilding is not needed. The composite form matches the POML wording while remaining functionally equivalent to the split reference form. The `-SkipBuild` switch preserves the ability to iterate on solution-XML content without re-invoking the ~30s webpack build.

## D-03: `ControlManifest.xml` in Controls/ is a scaffold — will be overwritten by `pcf-scripts build`

**POML step 3** says: "Update Controls/sprk_Spaarke.Records.MatterHeader/ControlManifest.xml to match the input manifest's control name and version."

**What was written**: The checked-in `Controls/sprk_Spaarke.Records.MatterHeader/ControlManifest.xml` mirrors the input manifest (namespace `Spaarke.Records`, constructor `MatterHeader`, version `1.0.0`) but references `bundle.js` (the built output) rather than `control/index.ts` (the source). This matches the pattern seen in `DocumentRelationshipViewer/Solution/Controls/.../ControlManifest.xml` (which references `bundle.js` and adds a `built-by` element that `pcf-scripts` injects at build time).

**Rationale**: pack.ps1 always overwrites this file from `out/controls/*/ControlManifest.xml` before packing. The checked-in version serves as (a) a placeholder so directory structure is committable, and (b) a fallback if pack.ps1 is invoked with `-SkipBuild` in a clean environment. When the real build runs, `pcf-scripts` re-emits the manifest with the current version + `built-by` marker.

---

## Verification performed

- ✅ All four XML files parse cleanly (`[xml]$x = Get-Content ...`; DocumentElement.Name check on each: `ImportExportXml`, `ImportExportXml`, `Types`, `manifest`)
- ✅ pack.ps1 parses cleanly (PowerShell AST parser, zero syntax errors)
- ✅ `solution.xml` UniqueName = `MatterHeaderPcf` (matches task 023 enforcement bullet 1)
- ✅ `solution.xml` Version = `1.0.0.0` (Dataverse 4-part) matches `version.ts` CONTROL_VERSION = `1.0.0` and ControlManifest.Input.xml version = `1.0.0` (3-part semver — same source of truth per PCF-DEPLOYMENT-GUIDE)
- ✅ `Controls/sprk_Spaarke.Records.MatterHeader/ControlManifest.xml` version = `1.0.0` matches version.ts
- ✅ `pack.ps1` line 21 sets `$ErrorActionPreference = 'Stop'`
- ✅ `pack.ps1` line 43 invokes `npm run build:prod` (NOT `npm run build`) — repo AP-1 compliance
- ✅ `pack.ps1` checks `Get-Command npm -ErrorAction SilentlyContinue` and errors gracefully if npm is missing
- ✅ `pack.ps1` checks `Get-Command pac -ErrorAction SilentlyContinue` at the end and prints a helpful diagnostic + install URL when absent (per task 023 instructions §4 "verify pack.ps1 runs a dry-check … fail gracefully with a diagnostic")
- ✅ No `@spaarke/auth` references anywhere in Solution/ (host-context — the input manifest already omits authoring service usage)

## What was NOT done (per POML §"What NOT to do")

- Did NOT run `npm run build:prod` (task 024 owns build + measure)
- Did NOT run `pack.ps1` end-to-end (task 024 owns build + measure)
- Did NOT deploy anything (task 025)
- Did NOT modify SemanticSearchControl or DocumentRelationshipViewer (reference only)
- Did NOT modify MatterHeader's `index.ts`, `MatterHeaderView.tsx`, or `version.ts` (tasks 021/022 shipped)
