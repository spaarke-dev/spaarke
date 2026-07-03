# Current Task

**Project**: set-regarding-and-field-mapping-resolver-r1
**Wave**: Wave 8 remaining (SRFR-084 UAT); Wave 9 wrap-up (SRFR-090)
**Task**: none active
**Status**: idle
**Started**: —
**Rigor**: —

## Quick Recovery

| Field | Value |
|-------|-------|
| **Task** | — (SRFR-034 complete, v1.3.1 deployed) |
| **Step** | — |
| **Status** | idle |
| **Next Action** | Wave 8 remaining: **SRFR-084 (UAT Matter→Event end-to-end STANDARD)**. Or Wave 9 SRFR-090 (project wrap-up). |

## Session Notes / Key Learnings (SRFR-034)

- **Dataverse PCF XSD `noAposStringType`** — the extracted `Solution/Controls/.../ControlManifest.xml` `description-key` attribute cannot contain apostrophes (single-quote character). Import fails with "The 'description-key' attribute is invalid — noAposStringType Pattern constraint failed". Fix: replace `entity's` → `entity`, `'RELATED RECORD'` → `RELATED RECORD` in `description-key` values. The source `ControlManifest.Input.xml` used in pcf-scripts build is not shipped (only the extracted Solution manifest is), but syncing both keeps them consistent.
- **Extracted Solution ControlManifest.xml lags source manifest** — the `Solution/Controls/.../ControlManifest.xml` had NOT been re-synced when v1.3 added `regardingRecordNumberField`, `regardingRecordNameField`, `title`. This shipped in v1.3.0 without those properties in the manifest surface (worked because the runtime bundle handles them regardless, but maker property panels didn't see them). SRFR-034 sync'd all v1.3 properties into the extracted manifest alongside the version bump.
- **Path A ADR-021 exception for OOB parity** — Fluent v9 semantic tokens do NOT map to Dataverse OOB section-header conventions. When owner-requested visual target is OOB parity, hardcoded values (font family + size + color + weight + padding) are correct. Document inline + in POML constraints + acknowledge in PR description.
- **Icon flip via consumer CSS** — `[data-testid="polymorphic-picker-trigger"] svg { transform: scaleX(-1); }` targets the shared PolymorphicPicker's SVG child without modifying the shared lib. ADR-012 preserved. Coupling to testid selector is a documented trade-off; alternative is a semantic class name on the shared component.
- **7-anchor version discipline (SRFR-033 legacy) works** — 1.3.0 → 1.3.1 across manifest × 2, index.ts, package.json, solution.xml, pack.ps1, UI footer BUILD_DATE. Zero straggling references confirmed by grep.

## Applicable ADRs (session-level)

- **ADR-021** — Semantic tokens (documented Path A exception for OOB title parity, owner-approved).
- **ADR-022** — PCF platform libraries (preserved; virtual pattern intact; bundle 1.57 MiB parity).
- **ADR-012** — Shared component library (preserved; PolymorphicPicker consumed unchanged; icon flip is consumer-side CSS transform).

## Files Modified This Session

- MODIFIED: `src/client/pcf/RegardingResolver/RegardingResolver/ControlManifest.Input.xml` (title default + showVersionFooter + version + apostrophe fix)
- MODIFIED: `src/client/pcf/RegardingResolver/RegardingResolver/index.ts` (CONTROL_VERSION)
- MODIFIED: `src/client/pcf/RegardingResolver/RegardingResolver/RegardingResolverApp.tsx` (main refactor: refresh button, styles, uppercased title, row-2 simplify, footer gate, docstring update)
- MODIFIED: `src/client/pcf/RegardingResolver/package.json` (version)
- MODIFIED: `src/client/pcf/RegardingResolver/Solution/solution.xml` (Version)
- MODIFIED: `src/client/pcf/RegardingResolver/Solution/Controls/sprk_Spaarke.Controls.RegardingResolver/ControlManifest.xml` (version + sync missing v1.3 props + apostrophe fix)
- MODIFIED: `src/client/pcf/RegardingResolver/Solution/pack.ps1` ($version)
- MODIFIED: `src/client/pcf/RegardingResolver/__tests__/RegardingResolverApp.test.tsx` (bulk 1.3.0→1.3.1 + 9 new tests)
- BUILT: `src/client/pcf/RegardingResolver/out/controls/RegardingResolver/bundle.js` (1.57 MiB)
- BUILT: `src/client/pcf/RegardingResolver/Solution/bin/RegardingResolverSolution_v1.3.1.zip`
- DEPLOYED: RegardingResolverSolution v1.3.1 to spaarkedev1
- CREATED: `projects/set-regarding-and-field-mapping-resolver-r1/tasks/034-regarding-resolver-ui-polish-v1.3.1.poml` (status: completed)
- CREATED: `projects/set-regarding-and-field-mapping-resolver-r1/notes/task-034-regarding-resolver-ui-polish.log`
- UPDATED: `projects/set-regarding-and-field-mapping-resolver-r1/tasks/TASK-INDEX.md` (SRFR-034 added, total 28 → 29)
