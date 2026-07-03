/**
 * MatterHeader PCF - Single source of truth for control version.
 *
 * Per src/client/pcf/CLAUDE.md, version MUST be kept in sync across 4 locations:
 *   1. ControlManifest.Input.xml  (version="X.Y.Z")
 *   2. control/version.ts         (CONTROL_VERSION — this file)
 *   3. Solution manifest          (created by task 023)
 *   4. Solution Control Manifest  (created by task 023)
 *
 * See docs/guides/PCF-DEPLOYMENT-GUIDE.md for the update workflow.
 */
export const CONTROL_VERSION = '1.0.1';
