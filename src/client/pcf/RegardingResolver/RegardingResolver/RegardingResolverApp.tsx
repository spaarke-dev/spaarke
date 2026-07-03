/**
 * RegardingResolverApp — v1.3.1 streamlined 2-row UI for the polymorphic regarding picker.
 *
 * # v1.3.1 layout (SRFR-034 owner polish pass)
 *
 *   ┌────────────────────────────────────────────────────────────────────────┐
 *   │  RELATED RECORD                                          [⟳] [🔎]      │  ← Row 1: OOB-styled title + refresh + lookup
 *   ├────────────────────────────────────────────────────────────────────────┤
 *   │  MTR-2025-0142                                                          │  ← Row 2: record-number link ONLY (name cell removed)
 *   └────────────────────────────────────────────────────────────────────────┘
 *
 * Row 1: OOB-styled uppercase title (Segoe UI 14px / #242424 / weight 400 /
 *        padding 4px 0px per SRFR-034 §4 — documented Path A exception to
 *        ADR-021 for OOB parity). Title text is derived from the `title`
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
 * Row 2: `sprk_regardingrecordnumber` hyperlink ONLY (single-column layout —
 *        SRFR-034 §6 removed the record-name cell and the em-dash empty-state
 *        placeholder). Owner uses the OOB record-name field placed elsewhere
 *        on the form. The record-name bound property
 *        (`regardingRecordNameField`) is preserved in the manifest for
 *        backward-compat with v1.3.0 form bindings but is NOT rendered.
 *        Field name for the record-number Link is resolved from the
 *        `regardingRecordNumberField` bound manifest property (default
 *        `sprk_regardingrecordnumber`) so makers can rebind on a new host
 *        entity without code change (FR-A1-02). Clicking the link opens the
 *        related record in a modal via `Xrm.Navigation.navigateTo` (SRFR-031).
 *
 * Footer: Version footer (v1.3.1 • Built {YYYY-MM-DD}) — conditionally rendered
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
 * OOB-styled title text still renders. Row 2 continues to render the
 * record-number hyperlink (name cell was removed in v1.3.1 per SRFR-034
 * §6). The hyperlink click handler stays active because opening the related
 * record in a modal is a VIEW action — safe under read-only per FR-A5-01.
 * handlePickerSelect has a defensive write-gate that refuses to write if a
 * race reaches it while readOnly is true.
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
const PolymorphicPicker =
  PolymorphicPickerRaw as unknown as React.ComponentType<PolymorphicPickerProps>;
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

const BUILD_DATE = '2026-07-03';

// ---------------------------------------------------------------------------
// Styles
//
// Note (v1.3.1): The `title` style intentionally uses hardcoded values
// (#242424, Segoe UI, 14px, weight 400, padding 4px 0px) to match the
// Dataverse OOB section-header exactly. This is a documented exception to
// ADR-021 (semantic tokens preferred): OOB parity is the visual target, not
// Fluent v9 theming. Owner-approved (SRFR-034 spec §4).
//
// All other styles remain on Fluent v9 semantic tokens per ADR-021.
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    padding: tokens.spacingHorizontalS,
    height: '100%',
    boxSizing: 'border-box',
  },
  row1: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    minHeight: '32px',
  },
  // Row-1 title — Dataverse OOB section-header parity (SRFR-034 §4).
  title: {
    fontFamily:
      '"Segoe UI", "Segoe UI Web (West European)", -apple-system, BlinkMacSystemFont, Roboto, "Helvetica Neue", sans-serif',
    fontSize: '14px',
    fontWeight: 400,
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
  // Row-2 — single-column (record-number Link only). SRFR-034 §6 removed
  // the record-name cell + em-dash placeholder.
  row2: {
    display: 'flex',
    alignItems: 'baseline',
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalXS}`,
    minHeight: '24px',
  },
  recordNumber: {
    fontWeight: tokens.fontWeightSemibold,
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
function adaptCatalogForPicker(
  catalog: ReadonlyArray<ITodoRegardingTargetCatalogEntry>
): RecordTypeCatalogEntry[] {
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
  const titleInput =
    titleRaw && titleRaw.trim().length > 0 ? titleRaw.trim() : 'RELATED RECORD';
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
  // v1.3.1 (SRFR-034 §6): the record-name cell was removed. The
  // `regardingRecordNameField` bound property remains in the manifest for
  // backward-compat with v1.3.0 form bindings; its value is not rendered.
  const boundRecordNumber = context.parameters.regardingRecordNumberField?.raw ?? null;

  // Allowed regarding targets (subset of TODO_REGARDING_CATALOG).
  const catalog = React.useMemo<readonly ITodoRegardingTargetCatalogEntry[]>(
    () => resolveAllowedCatalog(context.parameters.regardingTargets?.raw),
    [context.parameters.regardingTargets?.raw]
  );

  const pickerCatalog = React.useMemo<RecordTypeCatalogEntry[]>(
    () => adaptCatalogForPicker(catalog),
    [catalog]
  );

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
  // Entity name + id come from live control state (`selectedTarget`) —
  // populated by the PolymorphicPicker onSelect handler. On initial mount
  // before any picker selection, the state is null and this handler is a
  // silent no-op (there is no target to open). Xrm-unavailable path
  // (test harness / missing SDK) logs warn and no-ops without throwing so
  // the host form is never broken by a click on an inert link.
  const handleRecordNumberClick = React.useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();

      const entityName = selectedTarget?.entityType;
      const entityId = selectedTarget?.recordId;

      // Empty-state guard — no picker selection yet, or selection cleared.
      // Silent no-op is intentional (link may be rendered from a bound field
      // that reflects the persisted record but no in-memory target exists
      // until the picker fires; SRFR-033 will polish read-only navigation).
      if (!entityName || !entityId) {
        return;
      }

      const xrm = getXrm();
      const navigateTo = xrm?.Navigation?.navigateTo;
      if (typeof navigateTo !== 'function') {
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
      try {
        const result = navigateTo(
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
    },
    [selectedTarget]
  );

  const hasRecordNumber = typeof boundRecordNumber === 'string' && boundRecordNumber.trim().length > 0;

  // v1.3.1 (SRFR-034 §5) — refresh handler wraps the module-level internal
  // helper as a stable useCallback so the onClick reference is stable across
  // re-renders.
  const handleRefreshClick = React.useCallback(() => {
    void handleRefreshInternal();
  }, []);

  // ---------------- Render ----------------
  //
  // v1.3.1 layout changes (SRFR-034):
  //   - Row 1 title uppercased + styled to match OOB section-header (§1 + §4).
  //   - Row 1 "actions" area holds refresh (LEFT) + PolymorphicPicker (RIGHT) (§5).
  //   - PolymorphicPicker trigger icon flipped horizontally via CSS transform (§3).
  //   - Row 2 = single record-number Link cell (no name, no em-dash placeholder) (§6).
  //   - Version footer gated on `showVersionFooter` (§2).

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

      {/* Row 2 — record-number hyperlink only (v1.3.1 SRFR-034 §6) */}
      <div className={styles.row2} data-testid="regarding-resolver-row-2">
        {hasRecordNumber && (
          <Link
            className={styles.recordNumber}
            role="link"
            data-testid="regarding-resolver-record-number"
            onClick={handleRecordNumberClick}
          >
            {boundRecordNumber}
          </Link>
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
