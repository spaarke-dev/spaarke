/**
 * RecordHeader PCF - single source of truth for the control version.
 *
 * Version MUST be kept in sync across 5 locations (ADR-020;
 * `src/client/pcf/CLAUDE.md` says 4 — that count is stale, `pcf-deploy` adds
 * pack.ps1, which is what actually names the emitted ZIP):
 *   1. control/ControlManifest.Input.xml                (version="X.Y.Z")
 *   2. control/version.ts                               (CONTROL_VERSION — this file)
 *   3. Solution/solution.xml                            (<Version>X.Y.Z.0</Version>)
 *   4. Solution/Controls/sprk_Spaarke.Records.RecordHeader/ControlManifest.xml
 *      (build artifact — do NOT hand-edit; it is copied from out/ after build)
 *   5. Solution/pack.ps1                                ($version = "X.Y.Z.0")
 *
 * See docs/guides/PCF-DEPLOYMENT-GUIDE.md and .claude/skills/pcf-deploy/SKILL.md
 * for the update workflow.
 *
 * 1.1.0 (2026-08-25) — initial release of the configuration-driven control
 * (record-header-and-notepad-r2 task 033). Starts at 1.1.0 rather than 1.0.0
 * because this is a NEW control identity carrying R1s already-shipped
 * MatterHeader feature set (spec assumption, confirmed here) plus the R2
 * renderer/config work. `MatterHeaderPcf` v1.0.21 stays live and is retired
 * separately at task 081.
 *
 * 1.1.1 (2026-08-26) — first-UAT defect fixes.
 *   DEF-1 metadata never reached the resolver, so every field derived the
 *   `text` renderer, every label humanized its logical name, and a lookup got
 *   `$select`ed by its bare name (HTTP 400 -> every cell an em-dash). Two
 *   independent causes, both fixed in `@spaarke/ui-components`:
 *     (a) the attribute label/type rescue call used
 *         `Xrm.WebApi.retrieveMultipleRecords('EntityDefinition', ...)`, which
 *         cannot work - `Xrm.WebApi` does not serve metadata entities - so it
 *         threw on every call and its catch swallowed the throw;
 *     (b) `projectAttribute` parsed only Web-API shapes, but the CLIENT API
 *         returns a numeric `AttributeType` and a plain-string `DisplayName`.
 *   Plus two defences: the metadata fetch now NAMES the attributes it needs,
 *   and a failed `$select` read degrades to an unprojected read instead of
 *   blanking the header (the RS-1 failure mode, third occurrence).
 *   DEF-2 `layoutJson` moved to `of-type="Multiple"` - the classic form
 *   designer caps SingleLine.Text at 100 characters, below any real layout.
 */
export const CONTROL_VERSION = '1.1.2';
