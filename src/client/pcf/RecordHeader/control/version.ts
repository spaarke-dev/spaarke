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
 */
export const CONTROL_VERSION = '1.1.0';
