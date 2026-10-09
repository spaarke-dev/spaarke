/**
 * TrackingFieldTrio — injected-props contract (entity-agnostic, FR-14).
 *
 * Lifted from the `TrackingFieldTrio` PCF (`TrackingFieldTrioApp.tsx`) into
 * `@spaarke/ui-components` per task 023 (email-communication-solution-r5).
 * The shared core carries NO entity-specific option integers, labels, or
 * colors — every access-permission segment (value + label + color) and every
 * field display label is supplied by the caller. The PCF caller injects the
 * bound record's OptionSet metadata via `getAccessPermissionOptions()` (the
 * project / matter / work-assignment `sprk_accesspermission`; the
 * `sprk_communication` copy is retired — task 138, owner Q6: a communication
 * inherits its parent's permission).
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

import type { ITrackingAccessStatus } from './accessStatus';

/** The section of Manage Access a click asks the host to show (task 153). Omitted = the top (Current Access). */
export type GrantModalSection = 'noAccess';

export interface ITrackingFieldTrioProps {
  monitor: boolean;
  highPriority: boolean;
  accessPermission: number | null;
  /** Optional control header title (task 073 UAT #3). When supplied, the
   * component renders a header ROW (32px tall) with this title on the left
   * (14px semibold) and the governance icons (person/email) on the right —
   * the standard PCF header treatment. When OMITTED (the default), no header
   * is drawn and the governance icons fall back to their prior absolute
   * top-right placement, so existing consumers (e.g. the reading-pane
   * `EmailTrackingPanel`) are visually unchanged. */
  title?: string;
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

  /** The control is read-only — the host's form is disabled or read-only (task
   * 138; the PCF passes `context.mode.isControlDisabled`). Disables ALL THREE
   * controls: the Monitor and High Priority switches and the access-permission
   * pill, whose menu then cannot open — so no `on*Change` callback can fire and
   * no write that the form would refuse is offered. Default `false`. */
  disabled?: boolean;
  /** Disables the access-permission pill ONLY — e.g. the bound column is not
   * editable for this user (the PCF passes the attribute's
   * `security.editable === false`). Default `false`. */
  accessPermissionDisabled?: boolean;
  /** Whether the access-permission pill is shown at all (task 138). `false`
   * when the host has no access-permission column bound (the property is
   * optional in the PCF manifest) — the third column then stays empty, keeping
   * the grid aligned. Default `true`. */
  showAccessPermission?: boolean;
  /** Secure-record display for the pill (task 138; owner O1 FINAL, 2026-10-01).
   * When supplied — the host knows the record is SECURE — the CLOSED pill reads
   * `label` in red for every underlying value (both "secure" and "secure +
   * Restricted": "Secure – Restricted" would not fit the pill without changing
   * the trio's spacing). It changes the closed label ONLY: the open menu still
   * lists {@link accessPermissionOptions} unchanged (Standard / Limited /
   * Restricted), and selecting one calls {@link onAccessPermissionChange} with
   * that option's own value — the secure display never rewrites the stored
   * value. O1 FINAL specifies the closed label and the Manage Access bar, not a
   * different menu. Securing and unsecuring the record is NOT done here (task
   * 150's ribbon command). */
  secureAccessPermission?: {
    label: string;
  };
  /** Where the pill's effective value comes from (unified-access-control-r2 task 175, owner round 87), e.g. "Access
   * Permission: Restricted (inherited from Matter X)". When supplied, shown as the pill's tooltip and added to its
   * accessible description. Omit (the default) for no note. */
  accessPermissionNote?: string;

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
   * keeps the toolbar opt-in per consumer.
   *
   * `section` (task 153): the access-status indicator asks for `'noAccess'` when a No Access restriction applies, so
   * the host opens Manage Access at its No Access List; the person icon and a Secure-only indicator pass nothing (the
   * top, Current Access). The same callback serves both: there is no second open callback. */
  onOpenGrantModal?: (section?: GrantModalSection) => void;
  /** Invoked when the email icon is clicked, to open the email-members
   * action (task 042, via the canonical EmailComposer/SendEmailDialog per
   * ADR-045 — this component MUST NOT implement ad hoc send logic). When
   * omitted, the email icon is NOT rendered. */
  onOpenEmailMembers?: () => void;
  /** Gates the person icon's enabled state: `true` enables it, anything else
   * — `false`, or the prop omitted — disables it.
   *
   * 🔴 The default INVERTED in task 118 (unified-access-control-r2, FR-07 /
   * owner decision D-1 option C). It was `true`, so a host that had not wired
   * an access decision at all offered the affordance to everyone; it is now
   * `false`, so an unanswered access question is a denial. The host's job is
   * to pass the server's answer — see `TrackingFieldTrio`'s PCF `index.ts`
   * `evaluateGrantGate()`, which asks `GET /api/v1/external-access/
   * can-manage-access` (Write on THIS record, evaluated as the caller over
   * OBO) and passes `false` on every path that does not produce that answer.
   *
   * A disabled icon is genuinely disabled — native Fluent `disabled`, with no
   * click handler attached — never merely dimmed with a live handler, so
   * there is no dead click. */
  canGrantAccess?: boolean;

  /** The record's access status (task 153, owner round 83 item 11) — the two signals of task 064's per-record read,
   * parsed by the host with `parseAccessStatusResponse` (fail closed: anything it cannot trust is `unknown`). When
   * supplied, the header row shows an indicator: No Access in red (Secure only when `INDICATOR_SHOWS_SECURE` is true;
   * owner round 85 set it false, the pill already says Secure); "Access status unavailable" in a neutral colour when
   * either signal is `unknown`; nothing when no shown signal applies (a secure-only record included). It is clickable ONLY when
   * {@link onOpenGrantModal} is supplied AND {@link canGrantAccess} is `true` (the person icon's own gate); a click
   * opens Manage Access at the No Access List (No Access, or both) or at the top (Secure only). Otherwise it has no
   * click handler and its tooltip says the caller cannot manage access here. When OMITTED (the default, and while the
   * host's request is in flight) no indicator is drawn, so existing consumers such as the email reading pane are
   * unchanged. */
  accessStatus?: ITrackingAccessStatus;
}
