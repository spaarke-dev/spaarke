/**
 * TrackingFieldTrio — injected-props contract (entity-agnostic, FR-14).
 *
 * Lifted from the `TrackingFieldTrio` PCF (`TrackingFieldTrioApp.tsx`) into
 * `@spaarke/ui-components` per task 023 (email-communication-solution-r5).
 * The shared core carries NO `sprk_communication`-specific option integers,
 * labels, or colors — every access-permission segment (value + label +
 * color) and every field display label is supplied by the caller. The PCF
 * caller injects its `sprk_communication` OptionSet metadata via
 * `getAccessPermissionOptions()`; a future consumer (e.g. the Phase-3
 * reading-pane tracking view, task 035) injects its own.
 */

/** A single access-permission segment: the option's raw value, display label,
 * and an optional per-option color (e.g., a hex string sourced from Dataverse
 * OptionSet metadata). When `color` is omitted, the component falls back to a
 * position-based (NOT value-keyed) default palette — see
 * `DEFAULT_SEGMENT_FALLBACK_COLORS` in `TrackingFieldTrio.tsx`. */
export interface IAccessPermissionOption {
  value: number;
  label: string;
  /** Hex color from the Dataverse OptionSet metadata (e.g., "#00B050"). */
  color?: string;
}

export interface ITrackingFieldTrioProps {
  monitor: boolean;
  highPriority: boolean;
  accessPermission: number | null;
  /** Show field labels above each control. Default true. */
  showTitle?: boolean;
  /** Show a version footer in the bottom-right corner. Default false (hidden). */
  showVersion?: boolean;
  /** Text rendered in the version footer when `showVersion` is true. Caller
   * supplies its own version string — the shared core does not hardcode a
   * PCF-specific version (entity/surface-agnostic, FR-14). */
  versionText?: string;
  /** Injected access-permission segments — value + label + optional color.
   * Determines BOTH which segments render AND their order (entity-agnostic:
   * the shared core no longer hardcodes `sprk_communication`'s Standard/
   * Limited/Restricted values). Caller MUST supply at least one segment. */
  accessPermissionOptions: IAccessPermissionOption[];
  /** Field display names — sourced from the caller's own field metadata. */
  monitorLabel: string;
  highPriorityLabel: string;
  accessPermissionLabel: string;
  onMonitorChange: (value: boolean) => void;
  onHighPriorityChange: (value: boolean) => void;
  onAccessPermissionChange: (value: number) => void;
}
