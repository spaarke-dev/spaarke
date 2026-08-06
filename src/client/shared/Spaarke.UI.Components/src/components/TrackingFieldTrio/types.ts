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

  // ---------------------------------------------------------------------
  // Governance toolbar (person + email icons — task 040, teams-app-r1).
  // Toolbar shell + callback wiring only; the modal (task 041) and the
  // email-members action (task 042) supply the actual dialog contents by
  // implementing these callbacks. All three props are optional so any
  // existing consumer that hasn't wired the toolbar is unaffected
  // (entity-agnostic, prop-injected — no baked-in entity/field values).
  // ---------------------------------------------------------------------

  /** Invoked when the person icon is clicked, to open the access-grant
   * modal (task 041). When omitted, the person icon is NOT rendered — this
   * keeps the toolbar opt-in per consumer. */
  onOpenGrantModal?: () => void;
  /** Invoked when the email icon is clicked, to open the email-members
   * action (task 042, via the canonical EmailComposer/SendEmailDialog per
   * ADR-045 — this component MUST NOT implement ad hoc send logic). When
   * omitted, the email icon is NOT rendered. */
  onOpenEmailMembers?: () => void;
  /** Gates the person icon's enabled state. Defaults to `true` (enabled)
   * when `onOpenGrantModal` is supplied and this prop is omitted. Pass
   * `false` when the current user lacks grant privilege — the icon then
   * renders genuinely disabled (native Fluent `disabled`, no attached
   * click handler), never merely dimmed with a live handler, so there is
   * no dead click. */
  canGrantAccess?: boolean;
}
