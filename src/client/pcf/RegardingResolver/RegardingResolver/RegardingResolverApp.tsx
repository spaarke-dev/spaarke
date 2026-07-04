/**
 * RegardingResolverApp — v1.3.6 streamlined 2-row UI for the polymorphic regarding picker.
 *
 * # v1.3.6 (SRFR-043 Row 2 hyperlink WebAPI-based fix)
 *
 * Follow-up bug fix (owner post-v1.3.5 deploy): SRFR-042's URL-parse path
 * silently failed. Owner + MCP data query confirmed that Dataverse HAS the
 * `sprk_regardingrecordurl` populated correctly on host records, BUT the field
 * is NOT on the sprk_todo form (SRFR-036 added only `sprk_regardingrecordnumber`;
 * the URL field was never added, even hidden). Consequence: `Xrm.Page.getAttribute`
 * returns `null` for fields not present on the form → v1.3.5 Priority 2 was
 * never reachable → click fell through to inert Priority 3 → silent no-op
 * (same failure mode as v1.3.4).
 *
 * Fix (SRFR-043 v1.3.6): replace the Xrm.Page-based URL read with a
 * WebAPI-based host-record retrieve at click time. `context.webAPI` can read
 * ANY field on the host record regardless of form presence, so the URL is
 * available even though the field isn't on the form. Priority order:
 *   1. `selectedTarget` — fresh picker selection wins (unchanged, synchronous).
 *   2. `webApi.retrieveRecord(hostEntity, hostRecordId, '?$select=sprk_regardingrecordurl')`
 *      → parse `etn` + `id` from the returned URL. Async — click handler is
 *      wrapped to await this via `void resolveClickTarget(...).then(...)`.
 *
 * Removed from v1.3.5: (a) Xrm.Page `sprk_regardingrecordurl` attribute read
 * (never reachable in production), (b) `sprk_regardingrecordtype.entityType`
 * hint fallback (was inert per prior docstring warning — the lookup targets
 * `sprk_recordtype_ref`, not the parent entity).
 *
 * All defensive: WebAPI rejection (record deleted, no privilege, network error)
 * → warn + no-op. URL parse errors → warn + no-op. Never throws to the host
 * form. Shared library + manifest properties UNCHANGED.
 *
 * All prior SRFR-034/035/037/039 features preserved: refresh button,
 * auto-refresh, showVersionFooter, PolymorphicPicker consumption, Row 2 grid
 * with labels, Name cell, title semi-bold + reduced top padding.
 *
 * # v1.3.4 (SRFR-039 restore Name cell + top-aligned OOB-parity labels)
 *
 * Owner clarification post-v1.3.3: SRFR-034 §6 over-eagerly removed the Row 2
 * record-name cell. Owner wants the Name displayed INSIDE the PCF, next to
 * the record-number, in a proportional 1/3 : 2/3 grid layout (Number 1/3
 * left, Name 2/3 right). Additionally, both cells now carry OOB-parity
 * top-aligned labels: "Regarding Number" and "Regarding Name" (small font
 * 12px, weight 400, color #616161 — matches Dataverse OOB field-label
 * styling). Empty cells hide entirely (no em-dash placeholder).
 *
 * The `regardingRecordNameField` bound manifest property (preserved for
 * backward-compat since v1.3.1 SRFR-034 §6) is now RE-CONSUMED in Row 2's
 * right cell as plain-text (Fluent v9 `<Text>` — NOT a Link; hyperlink is
 * only on the number).
 *
 * All other SRFR-034/035/037 features preserved: refresh button,
 * auto-refresh, showVersionFooter, PolymorphicPicker consumption, title
 * font-weight 600 + reduced top padding.
 *
 * # v1.3.3 (SRFR-037 title-styling fix)
 *
 * Two OOB-parity corrections after owner DevTools inspection of the OOB
 * "TRACKING" section header on the sprk_todo form:
 *   1. Title font-weight was 400 (per SRFR-034 briefing) but OOB actually uses
 *      600 (semi-bold) — `.pa-hw { font-weight: 600 }`. Corrected to 600.
 *      All other title style values (Segoe UI stack, 14px, #242424,
 *      padding 4px 0px) already matched OOB and remain unchanged.
 *   2. Root container top padding reduced to 0 (was ~8px via
 *      `tokens.spacingHorizontalS`). The extra top gap made the RELATED
 *      RECORD title sit visibly below other OOB section headers on the same
 *      form. Right/bottom/left padding preserved at `spacingHorizontalS`.
 *
 * Refresh button (SRFR-034), auto-refresh after picker selection (SRFR-035),
 * `showVersionFooter` (SRFR-034), and PolymorphicPicker consumption
 * (SRFR-030/034) all preserved.
 *
 * # v1.3.2 (SRFR-035 owner post-UAT polish)
 *
 * After a successful picker selection in UPDATE mode, the form auto-refreshes
 * so any bound fields (e.g. `sprk_regardingrecordnumber`,
 * `sprk_regardingrecordname` displayed elsewhere on the form) update
 * immediately without the user clicking Save or Refresh. CREATE mode
 * (formType === 1) is DELIBERATELY skipped — the SRFR-032 presave bridge
 * (`__sprk_regarding_pending__` window seam) owns that path, and auto-refresh
 * would clobber the buffered attributes. The manual refresh button added in
 * SRFR-034 remains as an escape hatch. See `autoRefreshForm` helper below.
 *
 * # v1.3.1 layout (SRFR-034 owner polish pass)
 *
 *   ┌────────────────────────────────────────────────────────────────────────┐
 *   │  RELATED RECORD                                          [⟳] [🔎]      │  ← Row 1: OOB-styled title + refresh + lookup
 *   ├────────────────────┬───────────────────────────────────────────────────┤
 *   │  Regarding Number  │  Regarding Name                                    │  ← v1.3.4: top-aligned labels
 *   │  MTR-2025-0142     │  Smith v. Jones                                    │  ← Row 2: number link (1/3) + name text (2/3)
 *   └────────────────────┴───────────────────────────────────────────────────┘
 *
 * Row 1: OOB-styled uppercase title (Segoe UI 14px / #242424 / weight 600 /
 *        padding 4px 0px per SRFR-037 correction of SRFR-034 §4 — documented
 *        Path A exception to ADR-021 for OOB parity). Title text is derived from the `title`
 *        manifest input (default "RELATED RECORD") and rendered UPPERCASED
 *        regardless of maker input case (SRFR-034 §1). Right side of Row 1
 *        contains the refresh ToolbarButton (SRFR-034 §5 — save + refresh
 *        handler) LEFT of the shared `PolymorphicPicker` lookup icon. The
 *        picker's search icon is CSS-flipped horizontally (`scaleX(-1)`) to
 *        match OOB Dataverse lookup direction (SRFR-034 §3, consumer-side
 *        transform — shared lib unchanged per ADR-012). Clicking the lookup
 *        opens `Xrm.Utility.lookupObjects`; the picked record flows through
 *        `onSelect(entityType, recordId, recordName)` back into this
 *        component's `applyRegardingSelection` handler which delegates to
 *        `PolymorphicResolverService.applyResolverFields` per FR-A4-01.
 *
 * Row 2: v1.3.4 (SRFR-039) — 2-column CSS grid (`1fr 2fr`) with top-aligned
 *        OOB-parity labels above each value. LEFT (1/3 width): "Regarding
 *        Number" label + record-number Link (SRFR-031 modal-open handler
 *        preserved). RIGHT (2/3 width): "Regarding Name" label + record-name
 *        plain-text (Fluent v9 `<Text>` — NOT a Link; hyperlink is only on
 *        the number). Empty cells hide entirely (no em-dash placeholder).
 *        Field names for both cells are resolved from bound manifest
 *        properties (`regardingRecordNumberField` default
 *        `sprk_regardingrecordnumber`; `regardingRecordNameField` default
 *        `sprk_regardingrecordname`) so makers can rebind on a new host
 *        entity without code change (FR-A1-02).
 *
 * Footer: Version footer (v1.3.4 • Built {YYYY-MM-DD}) — conditionally rendered
 *         based on the new `showVersionFooter` input property (SRFR-034 §2,
 *         default true).
 *
 * # HOST-only usage (binding contract — R4-112 clarification 2026-06-24)
 *
 * Bind this PCF ONLY to entities that HOST the polymorphic regarding fields
 * (`sprk_regardingrecordtype` lookup + `sprk_regardingrecordid` / `name` / `url`
 * + `sprk_regardingrecordnumber` text fields + the 11 `sprk_Regarding<X>`
 * entity-specific lookups). Currently host entities: `sprk_todo`,
 * `sprk_communication` (FR-22).
 *
 * # Read-only mode (FR-A5-01 / NFR-04) — preserved by SRFR-033 + SRFR-034
 *
 * When read-only (either `context.parameters.readOnly.raw === true` OR
 * `context.mode.isControlDisabled === true`, resolved by RegardingResolverHost),
 * Row 1 hides BOTH the refresh ToolbarButton (SRFR-034 §5) AND the
 * PolymorphicPicker lookup trigger (gated on `!readOnly` internally); the
 * OOB-styled title text still renders. Row 2 continues to render BOTH the
 * number Link and the plain-text Name (v1.3.4 SRFR-039 restored the Name
 * cell). The number-hyperlink click handler stays active because opening the
 * related record in a modal is a VIEW action — safe under read-only per
 * FR-A5-01. handlePickerSelect has a defensive write-gate that refuses to
 * write if a race reaches it while readOnly is true.
 *
 * # CREATE-mode presave bridge (FR-A5-02)
 *
 * On CREATE forms (no host record id), the selection payload is published on
 * `window.__sprk_regarding_pending__` so the companion OnSave handler
 * (`sprk_todo_regarding_presave.js`) can stage the fields into the form's
 * pending-attribute buffer for the INSERT transaction. SRFR-032 owns the
 * `recordNumber` extension of that bridge payload; this task keeps the shape
 * consistent (recordId / recordName / recordUrl / entityType).
 */

import * as React from 'react';
import {
  Link,
  MessageBar,
  MessageBarBody,
  Text,
  Toolbar,
  ToolbarButton,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { ArrowClockwiseRegular } from '@fluentui/react-icons';
import {
  PolymorphicPicker as PolymorphicPickerRaw,
  buildRecordUrl,
  resolveRecordType,
  type IPolymorphicPickerWebApi,
  type ITodoRegardingTargetCatalogEntry,
  type PolymorphicPickerProps,
  type RecordTypeCatalogEntry,
} from '@spaarke/ui-components';

/**
 * The shared library's `.d.ts` bundle exposes `PolymorphicPicker` as
 * `React.FC<PolymorphicPickerProps>`, but that `React.FC` is emitted against
 * the shared lib's own `@types/react` (React 19-family), whose FC return type
 * (`ReactNode | Promise<ReactNode>`) is incompatible with React 16's JSX
 * element type. PCFs pin `@types/react` to 16.x per ADR-022. Cast to the React
 * 16 `React.ComponentType` shape at the seam so the JSX use-site typechecks
 * against the local React 16 types. Runtime is unaffected — the compiled JS
 * module is the same regardless of which type version emitted the `.d.ts`.
 */
const PolymorphicPicker = PolymorphicPickerRaw as unknown as React.ComponentType<PolymorphicPickerProps>;
import { IInputs } from './generated/ManifestTypes';
import {
  applyRegardingSelection,
  resolveAllowedCatalog,
  type IRegardingSelection,
  type IResolverWriteContext,
} from './handlers/ResolverWriteHandler';

// ---------------------------------------------------------------------------
// Build date embedded in the version footer per src/client/pcf/CLAUDE.md
// "Version Footer Requirement (MANDATORY)". Bump alongside the CONTROL_VERSION
// in index.ts and the manifest attributes on every release (SRFR-033).
// ---------------------------------------------------------------------------

const BUILD_DATE = '2026-07-04';

// ---------------------------------------------------------------------------
// Styles
//
// Note (v1.3.3): The `title` style intentionally uses hardcoded values
// (Segoe UI stack, 14px, weight 600 (semi-bold), #242424, padding 4px 0px)
// to match the Dataverse OOB section-header EXACTLY, as observed via owner
// DevTools inspection of the OOB "TRACKING" section header:
//   .pa-nn { padding: 4px 0px; }
//   .pa-nl { font-size: 14px; }
//   .pa-hw { font-weight: 600; }   ← corrected from 400 in SRFR-037
//   .pa-de { font-family: "Segoe UI", "Segoe UI Web (West European)", ... }
//   .pa-s  { color: rgb(36, 36, 36); }
// This is a documented Path A exception to ADR-021 (semantic tokens
// preferred): OOB parity is the visual target, not Fluent v9 theming.
// Owner-approved (SRFR-034 spec §4, corrected by SRFR-037, extended by
// SRFR-039 for Row 2 field labels).
//
// Note (v1.3.4 SRFR-039): The `fieldLabel` style uses hardcoded OOB field-
// label values (12px, weight 400, color #616161, Segoe UI stack). This is
// the Path A extension covering Row 2 top-aligned labels. `row2Grid` uses
// CSS grid `1fr 2fr` for the 1/3 : 2/3 Number/Name proportional layout.
//
// The `container` block uses directional padding (top: 0) rather than
// uniform `padding: tokens.spacingHorizontalS`; this removes the ~8px extra
// gap above the title so the RELATED RECORD title aligns with OOB section
// headers on the same form (SRFR-037).
//
// All other styles remain on Fluent v9 semantic tokens per ADR-021.
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    // SRFR-037: reduce top padding to 0 so the OOB-styled title aligns with
    // other section headers on the host form (OOB `.pa-nn` uses `padding: 4px 0px`
    // — the title Griffel block already carries that vertical padding, so the
    // root container needs zero additional top gap). Right/bottom/left padding
    // preserved at `tokens.spacingHorizontalS` (8px) for the surrounding layout.
    paddingTop: 0,
    paddingRight: tokens.spacingHorizontalS,
    paddingBottom: tokens.spacingHorizontalS,
    paddingLeft: tokens.spacingHorizontalS,
    height: '100%',
    boxSizing: 'border-box',
  },
  row1: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    minHeight: '32px',
  },
  // Row-1 title — Dataverse OOB section-header parity (SRFR-034 §4, corrected
  // by SRFR-037 to font-weight 600 per actual OOB DevTools inspection).
  title: {
    fontFamily:
      '"Segoe UI", "Segoe UI Web (West European)", -apple-system, BlinkMacSystemFont, Roboto, "Helvetica Neue", sans-serif',
    fontSize: '14px',
    fontWeight: 600,
    color: '#242424',
    padding: '4px 0px',
    letterSpacing: 0,
    textTransform: 'uppercase',
  },
  // Row-1 actions area — refresh (left) + PolymorphicPicker (right).
  row1Actions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },
  refreshToolbar: {
    paddingLeft: 0,
    paddingRight: 0,
    minHeight: 'auto',
  },
  // Flip the shared PolymorphicPicker's trigger icon horizontally so the
  // magnifier handle points toward the top-left, matching the OOB Dataverse
  // lookup icon direction (SRFR-034 §3). Consumer-side CSS transform —
  // shared component is unchanged (ADR-012).
  //
  // Also visually suppress the picker's internal title span: our own OOB-styled
  // title (rendered above) is the visible label; the picker's title is kept
  // in the DOM for accessibility (aria-label + tooltip content wiring) but
  // hidden with `display: none` on its span. The picker's title text is still
  // used for the tooltip / aria-label wiring inside the shared component.
  pickerContainer: {
    '& [data-testid="polymorphic-picker-title"]': {
      display: 'none',
    },
    '& [data-testid="polymorphic-picker-trigger"] svg': {
      transform: 'scaleX(-1)',
    },
  },
  // Row-2 — v1.3.4 (SRFR-039): two-column grid. Number cell = 1/3 width
  // (left), Name cell = 2/3 width (right). Empty cells hide entirely.
  row2: {
    display: 'grid',
    gridTemplateColumns: '1fr 2fr',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalXS}`,
    minHeight: '24px',
    alignItems: 'start',
  },
  // Per-cell wrapper: label above, value below (flex column).
  numberCell: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
  },
  nameCell: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
  },
  // Row-2 field label — OOB-parity styling (SRFR-039 Path A extension).
  // Small font (12px), regular weight (400), gray color (#616161), Segoe UI
  // stack. Left-aligned inherently via flex column. Documented exception
  // to ADR-021 semantic tokens for OOB parity.
  fieldLabel: {
    fontFamily:
      '"Segoe UI", "Segoe UI Web (West European)", -apple-system, BlinkMacSystemFont, Roboto, "Helvetica Neue", sans-serif',
    fontSize: '12px',
    fontWeight: 400,
    color: '#616161',
    lineHeight: '16px',
  },
  recordNumber: {
    fontWeight: tokens.fontWeightSemibold,
  },
  // Row-2 record-name value — plain text (NOT a Link). Segoe UI 14px, weight
  // 400, color #242424 — matches OOB body-text on the same form. Truncate
  // with ellipsis on overflow so long names don't blow out the cell.
  recordName: {
    fontFamily:
      '"Segoe UI", "Segoe UI Web (West European)", -apple-system, BlinkMacSystemFont, Roboto, "Helvetica Neue", sans-serif',
    fontSize: '14px',
    fontWeight: 400,
    color: '#242424',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  footer: {
    marginTop: 'auto',
    paddingTop: tokens.spacingVerticalS,
    display: 'flex',
    justifyContent: 'flex-end',
    alignItems: 'center',
  },
  versionText: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Walk through window / parent frames to locate Xrm. PCF runs in an iframe,
 * so the form host is exposed via window.parent or window.top.
 */
function getXrm():
  | {
      Utility?: {
        getGlobalContext?: () => unknown;
      };
      Page?: Xrm.Page;
      Navigation?: {
        navigateTo?: (
          pageInput: { pageType: 'entityrecord'; entityName: string; entityId: string },
          navigationOptions: {
            target: 1 | 2;
            width: { value: number; unit: '%' | 'px' };
            height: { value: number; unit: '%' | 'px' };
          }
        ) => Promise<unknown>;
      };
    }
  | undefined {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const w = window as any;
  return w.Xrm ?? w.parent?.Xrm ?? w.top?.Xrm;
}

/**
 * v1.3.2 — Auto-refresh helper (SRFR-035).
 *
 * Called after a successful picker selection so the form updates
 * transparently for the user. `applyResolverFields` already committed the
 * writes via `webApi.updateRecord`, so this helper's job is to (a) flush any
 * pending form-side bound-property writes with `data.entity.save()` and then
 * (b) pull server-side updated values with `data.refresh(true)`.
 *
 * CREATE mode gate (formType === 1): CREATE mode is DELIBERATELY skipped
 * because the SRFR-032 presave bridge (`__sprk_regarding_pending__` window
 * seam) owns that path. On CREATE the host record does not yet exist —
 * calling save() would fire the OnSave chain (including the presave webresource
 * that stages the resolver payload into the form buffer for INSERT), which we
 * do NOT want as a side effect of picker selection; refresh(true) would then
 * clobber the buffered attributes and break the INSERT transaction.
 *
 * Xrm-unavailable path: silent-continue with a warn. Test harness and canvas
 * app both lack Xrm; rejecting here would surface as unhandled promise
 * rejection breaking test infra.
 *
 * Any exception thrown by save/refresh is caught and warned — the manual
 * refresh button added in SRFR-034 remains as an escape hatch for the user.
 */
async function autoRefreshForm(formType: number): Promise<void> {
  // Skip auto-refresh on CREATE (formType === 1) — presave bridge handles that path.
  if (formType === 1) return;

  const xrm = getXrm();
  if (!xrm) {
    console.warn('[RegardingResolver] Auto-refresh skipped: Xrm unavailable (test harness or canvas app).');
    return;
  }

  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const data = (xrm.Page as any)?.data;
    if (!data) return;

    // Flush any pending form-side bound-property writes before refresh.
    const save = data.entity?.save;
    if (typeof save === 'function') {
      const saveResult = save.call(data.entity);
      if (saveResult && typeof (saveResult as Promise<unknown>).then === 'function') {
        await saveResult;
      }
    }

    // Refresh to pull updated field values from the server.
    const refresh = data.refresh;
    if (typeof refresh === 'function') {
      const refreshResult = refresh.call(data, true);
      if (refreshResult && typeof (refreshResult as Promise<unknown>).then === 'function') {
        await refreshResult;
      }
    }
  } catch (err) {
    // Silent — user still has the manual refresh button (SRFR-034) as escape hatch.
    console.warn('[RegardingResolver] Auto-refresh after selection failed:', err);
  }
}

/**
 * Resolve the current form type via `Xrm.Page.ui.getFormType()`.
 * Return values: 1 = CREATE, 2 = UPDATE, 3 = READONLY, 4 = DISABLED, 6 = BULKEDIT.
 * Defaults to UPDATE (2) when Xrm.Page.ui or getFormType is unavailable
 * (test harness), so the auto-refresh gate opens rather than closes — the
 * inner Xrm-unavailable check in autoRefreshForm covers the actual no-op.
 */
function getFormType(): number {
  const xrm = getXrm();
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const ui = (xrm?.Page as any)?.ui;
    const ft = ui?.getFormType?.();
    if (typeof ft === 'number') return ft;
  } catch {
    /* ignore */
  }
  return 2; // Default UPDATE
}

/**
 * v1.3.1 — Refresh handler wired to a new toolbar button (SRFR-034 §5).
 *
 * Behavior:
 *   1. Saves the current form (`Xrm.Page.data.entity.save()`) so any dirty
 *      buffered attributes commit before refreshing.
 *   2. Refreshes the form (`Xrm.Page.data.refresh(true)`) — the `true` arg
 *      re-fetches from the server (parity with the OOB "Refresh" ribbon
 *      command).
 *   3. If Xrm is unavailable (test harness / canvas app), falls back to
 *      `window.location.reload()` so the user still gets a refresh.
 *   4. Any failure is caught and warned — the form host must NEVER crash
 *      because a save error propagated (parity with the existing modal-open
 *      handler's defensive posture).
 */
async function handleRefreshInternal(): Promise<void> {
  const xrm = getXrm();
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const data = (xrm?.Page as any)?.data;
    const save = data?.entity?.save;
    if (typeof save === 'function') {
      // save() returns a promise in modern MDA; older forms return void.
      const saveResult = save.call(data.entity);
      if (saveResult && typeof (saveResult as Promise<unknown>).then === 'function') {
        await saveResult;
      }
    }
    const refresh = data?.refresh;
    if (typeof refresh === 'function') {
      const refreshResult = refresh.call(data, true);
      if (refreshResult && typeof (refreshResult as Promise<unknown>).then === 'function') {
        await refreshResult;
      }
      return;
    }
    // Fallback — no MDA refresh API available.
    if (typeof window !== 'undefined' && typeof window.location?.reload === 'function') {
      window.location.reload();
    }
  } catch (err) {
    // Defensive: never crash the host form.
    console.warn('[RegardingResolver] Refresh failed:', err);
  }
}

/**
 * v1.3.6 (SRFR-043) — Click-time click-target resolution helper.
 *
 * Returns the `entityName` + `entityId` the Row 2 record-number hyperlink should
 * open. Fixes the pre-loaded-record no-op bug (originally observed in v1.3.4;
 * v1.3.5's URL-parse-via-Xrm.Page path never fired in production because the
 * `sprk_regardingrecordurl` field is not on the sprk_todo form, so
 * `Xrm.Page.getAttribute` returned null — see file docstring for full history).
 *
 * Priority (fresh selection wins over pre-loaded state):
 *   1. `selectedTarget` — populated by the PolymorphicPicker onSelect callback.
 *      Synchronous, no WebAPI call.
 *   2. `webApi.retrieveRecord(hostEntity, hostRecordId, '?$select=sprk_regardingrecordurl')`
 *      → parse `etn` + `id` from the returned URL. Async. Works regardless of
 *      whether the URL field is on the form because `context.webAPI` reads
 *      DIRECTLY from the record row, not from form attributes.
 *
 * Returns `null` when the target cannot be resolved. Defensive throughout:
 * WebAPI rejection (record deleted, no privilege, network error) → return null.
 * Malformed URL → return null. Never throws to the host form.
 *
 * @param selectedTarget - Fresh picker selection state (populated by onSelect).
 * @param webApi - The PCF's context.webAPI (Xrm.WebApi-compatible).
 * @param hostEntity - Host entity logical name (from context.parameters.entity.raw).
 * @param hostRecordId - Host record GUID (from getHostRecordId()).
 */
async function resolveClickTarget(
  selectedTarget: IRegardingSelection | null,
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  webApi: any,
  hostEntity: string,
  hostRecordId: string | undefined
): Promise<{ entityName: string; entityId: string } | null> {
  // Priority 1 — fresh picker selection wins. Synchronous, no WebAPI call.
  if (selectedTarget?.entityType && selectedTarget?.recordId) {
    const cleanId = String(selectedTarget.recordId).replace(/[{}]/g, '');
    if (cleanId.length > 0) {
      return { entityName: selectedTarget.entityType, entityId: cleanId };
    }
  }

  // Priority 2 — WebAPI retrieve of sprk_regardingrecordurl on the host record.
  // Requires hostEntity + hostRecordId (pre-loaded UPDATE-mode form). CREATE
  // mode has no host record id so this path opens for UPDATE only.
  if (!hostEntity || !hostRecordId) return null;
  if (!webApi || typeof webApi.retrieveRecord !== 'function') return null;

  try {
    const result = await webApi.retrieveRecord(hostEntity, hostRecordId, '?$select=sprk_regardingrecordurl');
    const urlValue = result?.sprk_regardingrecordurl;
    if (typeof urlValue === 'string' && urlValue.length > 0) {
      try {
        // URL constructor handles absolute URLs; the stored value is
        // `https://<orgurl>/main.aspx?etn=<entity>&id=<guid>` per the shared
        // buildRecordUrl helper (used by applyResolverFields on write).
        const parsed = new URL(urlValue);
        const etn = parsed.searchParams.get('etn');
        const id = parsed.searchParams.get('id');
        if (etn && id) {
          const cleanId = id.replace(/[{}]/g, '');
          if (cleanId.length > 0) {
            return { entityName: etn, entityId: cleanId };
          }
        }
      } catch {
        // Malformed URL — return null. Warn is intentionally silent here to
        // avoid noisy logs for admin-edited records; the caller warns on null.
      }
    }
  } catch (err) {
    // Record deleted, no privilege, or network error. Warn (developer-visible)
    // but do not throw — the host form must never crash from a click.
    console.warn('[RegardingResolver] webApi.retrieveRecord(sprk_regardingrecordurl) rejected:', err);
  }

  return null;
}

/** Try to resolve the host record's GUID from `Xrm.Page`. */
function getHostRecordId(): string | undefined {
  const xrm = getXrm();
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const data = (xrm?.Page as any)?.data?.entity;
    const id = data?.getId?.();
    if (typeof id === 'string' && id.length > 0) {
      return id.replace(/[{}]/g, '');
    }
  } catch {
    /* ignore */
  }
  return undefined;
}

/**
 * Adapt the internal `TODO_REGARDING_CATALOG` shape (used by the write path
 * per ADR-024) to the shared `RecordTypeCatalogEntry` shape consumed by
 * `PolymorphicPicker`. The shared picker only needs a stable key, display
 * label, and logical name; the recordTypeRefId is derived from `entityType`
 * (a stable string) since the picker doesn't touch the actual
 * `sprk_recordtype_ref` GUID.
 */
function adaptCatalogForPicker(catalog: ReadonlyArray<ITodoRegardingTargetCatalogEntry>): RecordTypeCatalogEntry[] {
  return catalog.map(entry => ({
    recordTypeRefId: entry.entityType,
    displayName: entry.entityType,
    logicalName: entry.entityType,
    regardingField: entry.lookupAttribute,
    // regardingRecordNumberField intentionally omitted at the adapter layer —
    // the write path (applyResolverFields) resolves this from
    // sprk_recordtype_ref.sprk_regardingrecordnumberfield per FR-A4-01.
  }));
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export interface IRegardingResolverAppProps {
  context: ComponentFramework.Context<IInputs>;
  readOnly: boolean;
  onRecordTypeChanged: (value: ComponentFramework.LookupValue | null) => void;
  version: string;
}

export const RegardingResolverApp: React.FC<IRegardingResolverAppProps> = ({
  context,
  readOnly,
  onRecordTypeChanged,
  version,
}) => {
  const styles = useStyles();

  // Host entity (FR-22 lever — single config point, no code branching).
  const hostEntity = (context.parameters.entity?.raw ?? '').trim();

  // Row-1 title from manifest input property (FR-A1-01). Falls back to the
  // default "RELATED RECORD" if the maker omits the property or clears it.
  // v1.3.1 (SRFR-034 §1 + §4): Uppercase unconditionally in the app to match
  // Dataverse OOB section-header convention, regardless of maker input
  // case. Belt-and-suspenders with the manifest default "RELATED RECORD".
  const titleRaw = context.parameters.title?.raw ?? null;
  const titleInput = titleRaw && titleRaw.trim().length > 0 ? titleRaw.trim() : 'RELATED RECORD';
  const title = titleInput.toUpperCase();

  // v1.3.1 (SRFR-034 §2): version-footer visibility toggle. Defaults to true
  // when the maker omits the property or sets it to null; only an explicit
  // `false` hides the footer.
  const showVersionFooterRaw = context.parameters.showVersionFooter?.raw;
  const showVersionFooter = showVersionFooterRaw !== false;

  // Row-2 record-number — read from bound manifest property (FR-A1-02).
  // Value comes straight from the host record's column value as the framework
  // passes them. When the maker binds to a different column (default
  // `sprk_regardingrecordnumber`), the raw already reflects that column's
  // value; we render as-is.
  //
  // v1.3.4 (SRFR-039): The `regardingRecordNameField` bound property is now
  // RE-CONSUMED in Row 2's right cell as plain-text (Fluent v9 `<Text>` —
  // NOT a Link; hyperlink is only on the number). This restores the Name
  // display SRFR-034 §6 removed, per owner clarification.
  const boundRecordNumber = context.parameters.regardingRecordNumberField?.raw ?? null;
  const boundRecordName = context.parameters.regardingRecordNameField?.raw ?? null;

  // Allowed regarding targets (subset of TODO_REGARDING_CATALOG).
  const catalog = React.useMemo<readonly ITodoRegardingTargetCatalogEntry[]>(
    () => resolveAllowedCatalog(context.parameters.regardingTargets?.raw),
    [context.parameters.regardingTargets?.raw]
  );

  const pickerCatalog = React.useMemo<RecordTypeCatalogEntry[]>(() => adaptCatalogForPicker(catalog), [catalog]);

  // Local transient state — errors + write-in-flight indicator.
  const [error, setError] = React.useState<string | null>(null);
  const [isWriting, setIsWriting] = React.useState(false);
  const [selectedTarget, setSelectedTarget] = React.useState<IRegardingSelection | null>(null);

  // ---------------- Write context ----------------
  const writeCtx = React.useMemo<IResolverWriteContext>(
    () => ({
      // context.webAPI is IPolymorphicWebApi-compatible AND has updateRecord.
      webApi: context.webAPI as unknown as IResolverWriteContext['webApi'],
      hostEntity,
      hostRecordId: getHostRecordId(),
    }),
    [context.webAPI, hostEntity]
  );

  // ---------------- PolymorphicPicker onSelect ----------------

  const handlePickerSelect = React.useCallback(
    async (entityType: string, recordId: string, recordName: string): Promise<void> => {
      // FR-24 — Defensive write-gate: read-only mode hides the picker trigger,
      // but if a race reaches this handler, refuse to write.
      if (readOnly) {
        console.warn('[RegardingResolver] handlePickerSelect invoked in read-only mode — write skipped (FR-A5-01).');
        return;
      }
      if (!hostEntity) {
        setError("Host entity is not configured (manifest 'entity' input property is empty).");
        return;
      }

      const selection: IRegardingSelection = {
        entityType,
        recordId,
        recordName,
      };

      setError(null);
      setIsWriting(true);
      try {
        // Delegate to the shared write path — nav-prop discovery + 15-field
        // clear-and-set + applyResolverFields for the SET group. This is the
        // ONLY write logic (FR-A4-01 / ADR-024).
        const result = await applyRegardingSelection(writeCtx, selection);
        if (!result.success) {
          setError(result.error ?? 'Failed to apply regarding fields.');
          return;
        }

        setSelectedTarget(selection);

        // CREATE-mode bridge: populate the well-known window global so the
        // presave OnSave handler can stage the resolver payload into the form
        // buffer for the INSERT transaction. UPDATE-mode already persisted via
        // webApi.updateRecord inside applyRegardingSelection.
        //
        // SRFR-032 (FR-A5-04 client half): propagates the resolved
        // `recordNumber` from the shared applyResolverFields result so the
        // v1.2.0+ presave webresource can stage `sprk_regardingrecordnumber`
        // onto the form. Backward compat: presave < v1.2.0 ignores the extra
        // key (its TEXT_FIELDS array explicitly enumerates the 3 legacy
        // fields; unrecognized keys are simply not consumed).
        if (!writeCtx.hostRecordId) {
          (
            window as unknown as {
              __sprk_regarding_pending__?: Record<string, unknown>;
            }
          ).__sprk_regarding_pending__ = {
            hostEntity: writeCtx.hostEntity,
            entityType: selection.entityType,
            entitySet: result.catalogEntry?.entitySet,
            lookupAttribute: result.catalogEntry?.lookupAttribute,
            recordId: selection.recordId,
            recordName: selection.recordName,
            recordUrl: buildRecordUrl(selection.entityType, selection.recordId),
            recordNumber: result.recordNumber ?? null,
          };
        }

        // Notify the PCF class so the bound `sprk_regardingrecordtype` lookup
        // output tracks the picked entity's record-type-ref (per Bug-1 fix
        // 2026-06-24: the bound lookup targets `sprk_recordtype_ref`, NOT the
        // parent entity itself).
        try {
          const recordType = await resolveRecordType(writeCtx.webApi, selection.entityType);
          if (recordType) {
            onRecordTypeChanged({
              id: recordType.id,
              name: recordType.name,
              entityType: 'sprk_recordtype_ref',
            });
          } else {
            onRecordTypeChanged(null);
          }
        } catch (rtErr) {
          console.warn('[RegardingResolver] resolveRecordType for output notify failed:', rtErr);
          onRecordTypeChanged(null);
        }

        // v1.3.2 (SRFR-035) — After successful selection, auto-refresh the
        // form so bound fields (record-number etc.) display fresh values
        // without the user clicking Save/Refresh. CREATE mode (formType === 1)
        // is deliberately skipped inside autoRefreshForm (presave bridge owns
        // that path). Manual refresh button from SRFR-034 remains as escape
        // hatch. Errors are swallowed inside autoRefreshForm (warn only).
        const formType = getFormType();
        void autoRefreshForm(formType);
      } catch (err) {
        console.error('[RegardingResolver] handlePickerSelect error:', err);
        setError(err instanceof Error ? err.message : 'Selection failed.');
      } finally {
        setIsWriting(false);
      }
    },
    [readOnly, hostEntity, writeCtx, onRecordTypeChanged]
  );

  // Row-2 record-number click handler (SRFR-031 / FR-A2-01) — opens the related
  // record in a Dataverse MODAL via `Xrm.Navigation.navigateTo`.
  //
  // Design contract (verbatim per spec FR-A2-01):
  //   Xrm.Navigation.navigateTo(
  //     { pageType: 'entityrecord', entityName, entityId },
  //     { target: 2, width: { value: 80, unit: '%' }, height: { value: 80, unit: '%' } }
  //   )
  //
  // `target: 2` = modal (per docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md#L513
  // and MS Learn PageInput type). `target: 1` would REPLACE main content —
  // do NOT confuse the two.
  //
  // v1.3.6 (SRFR-043): entity name + id come from `resolveClickTarget()` which
  // is now ASYNC. For fresh picker selections the resolution is synchronous
  // (Priority 1 returns immediately). For pre-loaded records the helper calls
  // `context.webAPI.retrieveRecord` to fetch `sprk_regardingrecordurl` directly
  // from the row (works even though the URL field is not on the form; that was
  // the v1.3.5 failure mode). The click handler wraps the async work in a
  // fire-and-forget promise via `void .then(...)` so the React event handler
  // itself stays synchronous.
  //
  // Xrm-unavailable path (test harness / missing SDK) logs warn and no-ops
  // without throwing so the host form is never broken by a click on an inert
  // link.
  const handleRecordNumberClick = React.useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();

      // v1.3.6 SRFR-043 — click-time target resolution is now async because
      // pre-loaded records go through webApi.retrieveRecord. Fire-and-forget
      // via .then; errors are logged inside resolveClickTarget.
      void resolveClickTarget(selectedTarget, context.webAPI, hostEntity, getHostRecordId()).then(clickTarget => {
        const entityName = clickTarget?.entityName;
        const entityId = clickTarget?.entityId;

        // Empty-state guard — no picker selection AND no bound URL/id to derive
        // a target from. Silent no-op is intentional to keep the host form
        // safe under partially-populated states (e.g., mid-load, cleared, or
        // brand-new blank form).
        if (!entityName || !entityId) {
          console.warn(
            '[RegardingResolver] Cannot open modal — entityName or entityId not resolved from selection or bound fields.'
          );
          return;
        }

        const xrm = getXrm();
        if (typeof xrm?.Navigation?.navigateTo !== 'function') {
          // Xrm unavailable — test harness, canvas app, or missing SDK. Warn
          // (developer-visible), do not throw (host-safe).
          console.warn(
            '[RegardingResolver] Xrm.Navigation.navigateTo unavailable; cannot open regarding record modal.'
          );
          return;
        }

        // Promise handling: navigateTo returns a Promise per MS docs. Rejection
        // (record deleted, no privilege, user cancelled) must NOT surface as an
        // unhandled rejection or crash the host form.
        //
        // v1.3.7 (SRFR-044): call as `xrm.Navigation.navigateTo(...)` — NOT
        // via an extracted `const navigateTo = xrm.Navigation.navigateTo`
        // reference. The extracted reference loses `this`; the platform impl
        // accesses `this.Navigation._clientApiExecutor` internally and throws
        // `TypeError: Cannot read properties of undefined (reading '_clientApiExecutor')`
        // when unbound. Method-call form preserves `this`.
        try {
          const result = xrm.Navigation.navigateTo(
            { pageType: 'entityrecord', entityName, entityId },
            {
              target: 2,
              width: { value: 80, unit: '%' },
              height: { value: 80, unit: '%' },
            }
          );
          if (result && typeof (result as Promise<unknown>).catch === 'function') {
            (result as Promise<unknown>).catch((err: unknown) => {
              console.warn('[RegardingResolver] Xrm.Navigation.navigateTo rejected:', err);
            });
          }
        } catch (err) {
          // Defensive: synchronous throw from a stubbed / non-conformant Xrm.
          console.warn('[RegardingResolver] Xrm.Navigation.navigateTo threw:', err);
        }
      });
    },
    [selectedTarget, context.webAPI, hostEntity]
  );

  const hasRecordNumber = typeof boundRecordNumber === 'string' && boundRecordNumber.trim().length > 0;
  const hasRecordName = typeof boundRecordName === 'string' && boundRecordName.trim().length > 0;

  // v1.3.1 (SRFR-034 §5) — refresh handler wraps the module-level internal
  // helper as a stable useCallback so the onClick reference is stable across
  // re-renders.
  const handleRefreshClick = React.useCallback(() => {
    void handleRefreshInternal();
  }, []);

  // ---------------- Render ----------------
  //
  // v1.3.4 layout (SRFR-039):
  //   - Row 1 title uppercased + styled to match OOB section-header (SRFR-034 §1 + §4, weight 600 per SRFR-037).
  //   - Row 1 "actions" area holds refresh (LEFT) + PolymorphicPicker (RIGHT) (SRFR-034 §5).
  //   - PolymorphicPicker trigger icon flipped horizontally via CSS transform (SRFR-034 §3).
  //   - Row 2 = 1/3:2/3 CSS grid: LEFT record-number Link (SRFR-031 modal-open), RIGHT record-name plain-text Text.
  //   - Row 2 top-aligned OOB-parity labels above each cell ("Regarding Number" / "Regarding Name").
  //   - Empty cells hide entirely (no em-dash placeholder).
  //   - Version footer gated on `showVersionFooter` (SRFR-034 §2).

  return (
    <div className={styles.container} data-testid="regarding-resolver-root">
      {error && (
        <MessageBar intent="error" data-testid="regarding-resolver-error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {/* Row 1 — OOB-styled title (left) + refresh + PolymorphicPicker (right) */}
      <div className={styles.row1} data-testid="regarding-resolver-row-1">
        <Text className={styles.title} data-testid="regarding-resolver-title">
          {title}
        </Text>
        <div className={styles.row1Actions}>
          {!readOnly && (
            <Toolbar className={styles.refreshToolbar} size="small" aria-label="Refresh actions">
              <Tooltip content="Refresh form" relationship="label" withArrow>
                <ToolbarButton
                  icon={<ArrowClockwiseRegular />}
                  aria-label="Refresh form"
                  data-testid="regarding-resolver-refresh"
                  onClick={handleRefreshClick}
                />
              </Tooltip>
            </Toolbar>
          )}
          <div className={styles.pickerContainer}>
            <PolymorphicPicker
              catalog={pickerCatalog}
              webApi={context.webAPI as unknown as IPolymorphicPickerWebApi}
              // Pass the resolved title so the picker's aria/tooltip surface
              // remain accessible ("Select related record"). The visible
              // duplicate is suppressed via the pickerContainer style below
              // (which also targets the picker's own title span).
              title={title}
              onSelect={handlePickerSelect}
              readOnly={readOnly}
              disabled={isWriting}
              onError={setError}
            />
          </div>
        </div>
      </div>

      {/* Row 2 — v1.3.4 (SRFR-039): 1/3 : 2/3 grid layout with top-aligned
          OOB-parity labels. Left cell = number hyperlink (SRFR-031 modal-open
          preserved). Right cell = plain-text name (NOT a Link). Empty cells
          hide entirely. */}
      <div className={styles.row2} data-testid="regarding-resolver-row-2">
        {hasRecordNumber && (
          <div className={styles.numberCell} data-testid="regarding-resolver-number-cell">
            <Text className={styles.fieldLabel} data-testid="regarding-resolver-number-label">
              Regarding Number
            </Text>
            <Link
              className={styles.recordNumber}
              role="link"
              data-testid="regarding-resolver-record-number"
              onClick={handleRecordNumberClick}
            >
              {boundRecordNumber}
            </Link>
          </div>
        )}
        {hasRecordName && (
          <div className={styles.nameCell} data-testid="regarding-resolver-name-cell">
            <Text className={styles.fieldLabel} data-testid="regarding-resolver-name-label">
              Regarding Name
            </Text>
            <Text className={styles.recordName} data-testid="regarding-resolver-record-name">
              {boundRecordName}
            </Text>
          </div>
        )}
      </div>

      {showVersionFooter && (
        <div className={styles.footer} data-testid="regarding-resolver-footer">
          <Text className={styles.versionText} data-testid="regarding-resolver-version">
            v{version} • Built {BUILD_DATE}
          </Text>
        </div>
      )}
    </div>
  );
};
