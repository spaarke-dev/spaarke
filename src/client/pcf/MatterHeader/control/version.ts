/**
 * MatterHeader PCF - Single source of truth for control version.
 *
 * Version MUST be kept in sync across 5 locations (src/client/pcf/CLAUDE.md says
 * 4 — that count is stale; `pcf-deploy` adds pack.ps1, which is what actually
 * names the emitted ZIP):
 *   1. control/ControlManifest.Input.xml                (version="X.Y.Z")
 *   2. control/version.ts                               (CONTROL_VERSION — this file)
 *   3. Solution/solution.xml                            (<Version>X.Y.Z.0</Version>)
 *   4. Solution/Controls/sprk_Spaarke.Records.MatterHeader/ControlManifest.xml
 *      (build artifact — do NOT hand-edit; it is copied from out/ after build)
 *   5. Solution/pack.ps1                                ($version = "X.Y.Z.0")
 *
 * See docs/guides/PCF-DEPLOYMENT-GUIDE.md and .claude/skills/pcf-deploy/SKILL.md
 * for the update workflow.
 *
 * 1.0.21 (2026-08-25) — RS-1 hotfix: the $select named a Matter-specific summary
 * column deleted in the 2026-08-25 standardization, so Dataverse 400'd and the
 * whole header failed to load. Now reads the shared RECORDSUMMARY_FIELD.
 */
export const CONTROL_VERSION = '1.0.21';
