/**
 * MatterHeaderView - Composition of shared RecordHeader primitives for sprk_matter.
 *
 * Implements spec FR-12 — thin composition of `@spaarke/ui-components` primitives
 * with the 5-field header + sparkle popover wired via `useRecordHeaderToolbarActions`.
 * Entity is compile-time-fixed as `"sprk_matter"`.
 *
 * v1.0.7 (2026-07-03) changes from live QA (dirty-state pattern):
 *  - **Form-buffer save**: text + lookup edits now write to the FORM buffer
 *    via `Xrm.Page.getAttribute(<name>).setValue(<value>)` instead of straight
 *    to Dataverse via `Xrm.WebApi.updateRecord`. Rationale: users reported that
 *    every auto-save triggered a full PCF re-render (the retrieveRecord round
 *    trip toggled the shell's loading skeleton). Form-buffer stages changes
 *    without hitting Dataverse — the field goes dirty, the user clicks Save on
 *    the form to commit. Matches OOB Dataverse dirty-state UX. No refresh
 *    round trip, no flash.
 *  - **Pending changes local state**: field renderers read from
 *    `pendingChanges[fieldName] ?? values?.[fieldName]` so the display
 *    reflects the pending value until the form's Save reloads the record and
 *    the PCF re-mounts.
 *  - **Notepad modal shrunk**: `NOTEPAD_MODAL` in `toolbarLaunchDefaults.ts`
 *    is now 25% × 35% (was 70% × 80%). Compact editor.
 *
 * v1.0.6 changes (retained):
 *  - `this`-binding fix on `Xrm.Navigation.navigateTo` (call via `.call(...)`)
 *  - Sparkle popover: sparkle icon + copy button + updated empty state text
 *  - TextareaField: `maxLines={10}` default; auto-grow past that
 *
 * Field payload (verified via Dataverse MCP):
 *   sprk_matternumber (span 1, required), sprk_mattername (span 2),
 *   _sprk_mattertype_value → editable LookupField (span 1),
 *   _sprk_practicearea_value → editable LookupField (span 1),
 *   sprk_matterdescription (span 3), sprk_recordsummary (sparkle popover body).
 *
 * Theming: platform-library Fluent v9 auto-applies host theme (control-type="virtual").
 *
 * Standards: ADR-006 / ADR-012 / ADR-021 semantic tokens / ADR-022 React 16-17
 * safe; NFR-05 no `@spaarke/auth`; NFR-07 no BFF (host-context Xrm only).
 *
 * @see FR-12 in projects/record-header-and-notepad-r1/spec.md
 */

import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import {
  FieldGrid,
  RecordHeaderShell,
  TextField,
  TextareaField,
} from '@spaarke/ui-components/dist/components/RecordHeader';
import { LookupField } from '@spaarke/ui-components/dist/components/LookupField/LookupField';
import type { ILookupItem } from '@spaarke/ui-components/dist/types/LookupTypes';
import { useRecordFieldValues, useRecordHeaderToolbarActions } from '@spaarke/ui-components/dist/hooks';
import { getXrm } from '@spaarke/ui-components/dist/utils/xrmContext';
import { CONTROL_VERSION } from './version';

const ENTITY = 'sprk_matter';

const FIELDS = [
  'sprk_matternumber',
  'sprk_mattername',
  '_sprk_mattertype_value',
  '_sprk_practicearea_value',
  'sprk_matterdescription',
  'sprk_recordsummary',
];

// Lookup metadata. `refEntity` is the target entity's logical name (used for
// retrieveMultipleRecords AND as `entityType` in the form buffer's lookup value
// shape `[{ id, name, entityType }]`).
const LOOKUP_META = {
  sprk_mattertype: {
    refEntity: 'sprk_mattertype_ref',
    refIdField: 'sprk_mattertype_refid',
    refNameField: 'sprk_mattertypename',
  },
  sprk_practicearea: {
    refEntity: 'sprk_practicearea_ref',
    refIdField: 'sprk_practicearea_refid',
    refNameField: 'sprk_practiceareaname',
  },
} as const;

type LookupKey = keyof typeof LOOKUP_META;

const useStyles = makeStyles({
  root: { position: 'relative', width: '100%' },
  lookupCell: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
  },
  versionFooter: {
    position: 'absolute',
    right: tokens.spacingHorizontalXS,
    bottom: tokens.spacingVerticalXXS,
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
    pointerEvents: 'none',
    userSelect: 'none',
  },
});

export interface IMatterHeaderViewProps {
  /** Record GUID (no braces). Empty string = "no record selected". */
  recordId: string;
  /** Optional inline toolbar title. Falls back to "Matter" when omitted. */
  title?: string;
  /** When `true` (default), the version footer is rendered. */
  showVersion?: boolean;
}

/** Extract Xrm.Page (deprecated but functional API used by AssociationResolver + other PCFs). */
function getXrmPage(): unknown | null {
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const w = window as any;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrmObj = w.Xrm || (w.parent as any)?.Xrm;
    return xrmObj?.Page ?? null;
  } catch {
    return null;
  }
}

/** Project a lookup value from `useRecordFieldValues` into `ILookupItem`. */
function projectLookup(values: Record<string, unknown> | null | undefined, lookupField: string): ILookupItem | null {
  if (!values) return null;
  const key = `_${lookupField}_value`;
  const id = values[key];
  if (typeof id !== 'string' || id.length === 0) return null;
  const name = values[`${key}@OData.Community.Display.V1.FormattedValue`];
  return {
    id,
    name: typeof name === 'string' ? name : '',
  };
}

export const MatterHeaderView: React.FC<IMatterHeaderViewProps> = ({ recordId, title, showVersion = true }) => {
  const styles = useStyles();
  const { values, loading } = useRecordFieldValues(ENTITY, recordId, FIELDS);

  // v1.0.7 pending-changes buffer. Field renderers read from
  // `pendingChanges[key] ?? values?.[key]` so a staged edit is visible without
  // waiting for the retrieveRecord refetch (which we no longer trigger — the
  // form's own Save/Reload eventually reconciles Dataverse). Cleared implicitly
  // when the record changes (React re-mount at recordId change resets state).
  const [pendingText, setPendingText] = React.useState<Record<string, string>>({});
  const [pendingLookup, setPendingLookup] = React.useState<Record<string, ILookupItem | null>>({});

  React.useEffect(() => {
    setPendingText({});
    setPendingLookup({});
  }, [recordId]);

  // ── Text-field save (form buffer) ──────────────────────────────────────────
  const saveText = React.useCallback(async (fieldName: string, newValue: string): Promise<void> => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrmPage = getXrmPage() as any;
    if (!xrmPage?.getAttribute) {
      // eslint-disable-next-line no-console
      console.warn('[MatterHeader] Xrm.Page unavailable — text save skipped', fieldName);
      throw new Error('Form buffer unavailable');
    }
    const attr = xrmPage.getAttribute(fieldName);
    if (!attr) {
      // eslint-disable-next-line no-console
      console.warn('[MatterHeader] attribute not on form', fieldName);
      throw new Error(`Field '${fieldName}' not on form`);
    }
    attr.setValue(newValue);
    setPendingText(prev => ({ ...prev, [fieldName]: newValue }));
    // eslint-disable-next-line no-console
    console.info('[MatterHeader] staged text edit', { field: fieldName, dirty: !!attr.getIsDirty?.() });
  }, []);

  const saveMatterNumber = React.useCallback((v: string) => saveText('sprk_matternumber', v), [saveText]);
  const saveMatterName = React.useCallback((v: string) => saveText('sprk_mattername', v), [saveText]);
  const saveMatterDescription = React.useCallback((v: string) => saveText('sprk_matterdescription', v), [saveText]);

  // ── Lookup search + form-buffer save ───────────────────────────────────────
  const searchLookup = React.useCallback(async (key: LookupKey, query: string): Promise<ILookupItem[]> => {
    const meta = LOOKUP_META[key];
    const xrm = getXrm();
    if (!xrm?.WebApi?.retrieveMultipleRecords) return [];
    const trimmed = query ? query.trim() : '';
    const filterClause =
      trimmed.length > 0
        ? `&$filter=contains(${meta.refNameField},'${encodeURIComponent(trimmed.replace(/'/g, "''"))}')`
        : '';
    const odata =
      `?$select=${meta.refIdField},${meta.refNameField}` +
      filterClause +
      `&$orderby=${meta.refNameField} asc&$top=10`;
    const result = await xrm.WebApi.retrieveMultipleRecords(meta.refEntity, odata);
    return (result.entities as Array<Record<string, unknown>>).map(e => ({
      id: e[meta.refIdField] as string,
      name: e[meta.refNameField] as string,
    }));
  }, []);

  const saveLookup = React.useCallback((key: LookupKey, item: ILookupItem | null): void => {
    const meta = LOOKUP_META[key];
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrmPage = getXrmPage() as any;
    if (!xrmPage?.getAttribute) {
      // eslint-disable-next-line no-console
      console.warn('[MatterHeader] Xrm.Page unavailable — lookup save skipped', key);
      return;
    }
    const attr = xrmPage.getAttribute(key);
    if (!attr) {
      // eslint-disable-next-line no-console
      console.warn('[MatterHeader] lookup attribute not on form', key);
      return;
    }
    // Xrm lookup value shape: [{ id, name, entityType }]. `null` clears.
    const nextValue = item ? [{ id: item.id, name: item.name, entityType: meta.refEntity }] : null;
    attr.setValue(nextValue);
    setPendingLookup(prev => ({ ...prev, [key]: item }));
    // eslint-disable-next-line no-console
    console.info('[MatterHeader] staged lookup edit', { field: key, item, dirty: !!attr.getIsDirty?.() });
  }, []);

  const searchMatterType = React.useCallback((q: string) => searchLookup('sprk_mattertype', q), [searchLookup]);
  const searchPracticeArea = React.useCallback((q: string) => searchLookup('sprk_practicearea', q), [searchLookup]);
  const saveMatterTypeLookup = React.useCallback((item: ILookupItem | null) => saveLookup('sprk_mattertype', item), [
    saveLookup,
  ]);
  const savePracticeAreaLookup = React.useCallback(
    (item: ILookupItem | null) => saveLookup('sprk_practicearea', item),
    [saveLookup]
  );

  // Resolve display values — pending change overrides the Dataverse-loaded value.
  const displayMatterNumber = pendingText.sprk_matternumber ?? (values?.sprk_matternumber as string | undefined);
  const displayMatterName = pendingText.sprk_mattername ?? (values?.sprk_mattername as string | undefined);
  const displayMatterDescription =
    pendingText.sprk_matterdescription ?? (values?.sprk_matterdescription as string | undefined);
  const displayMatterType =
    'sprk_mattertype' in pendingLookup ? pendingLookup.sprk_mattertype : projectLookup(values, 'sprk_mattertype');
  const displayPracticeArea =
    'sprk_practicearea' in pendingLookup
      ? pendingLookup.sprk_practicearea
      : projectLookup(values, 'sprk_practicearea');

  const { toolbarProps } = useRecordHeaderToolbarActions({
    entity: ENTITY,
    recordId,
    title: title || 'Matter',
  });

  // v1.0.10 — sparkle AI summary now delegates to the SHARED `<AiSummaryPopover>`
  // component (same one VisualHost, SemanticSearchControl, DocumentCard,
  // RichFilePreview, InsightSummaryCard, and DocumentLibrary use — per
  // SHARED-UI-COMPONENTS-GUIDE.md §180). HeaderToolbar renders the sparkle
  // trigger + popover itself when `aiSummary.onFetchSummary` is provided —
  // consumers don't touch icons directly (icon imports break virtual-PCF
  // webpack resolution, see v1.0.4 + v1.0.10 build regressions).
  const fetchSparkleSummary = React.useCallback(
    async (): Promise<{ summary: string | null; tldr: string | null }> => ({
      summary: (values?.sprk_recordsummary ?? null) as string | null,
      tldr: null,
    }),
    [values?.sprk_recordsummary]
  );

  const toolbarPropsWithSparkle = {
    ...toolbarProps,
    aiSummary: { onFetchSummary: fetchSparkleSummary },
  };

  return (
    <div className={styles.root}>
      <RecordHeaderShell toolbar={toolbarPropsWithSparkle} loading={loading} borderless>
        <FieldGrid columns={3}>
          <TextField
            span={1}
            label="Matter Number"
            value={displayMatterNumber}
            required
            onSave={saveMatterNumber}
          />
          <TextField span={2} label="Matter Name" value={displayMatterName} onSave={saveMatterName} />
          <div className={styles.lookupCell} style={{ gridColumn: 'span 1' }}>
            <LookupField
              label="Matter Type"
              value={displayMatterType}
              onChange={saveMatterTypeLookup}
              onSearch={searchMatterType}
              minSearchLength={0}
              openOnFocus
            />
          </div>
          <div className={styles.lookupCell} style={{ gridColumn: 'span 1' }}>
            <LookupField
              label="Practice Area"
              value={displayPracticeArea}
              onChange={savePracticeAreaLookup}
              onSearch={searchPracticeArea}
              minSearchLength={0}
              openOnFocus
            />
          </div>
          <TextareaField
            span={3}
            label="Matter Description"
            value={displayMatterDescription}
            maxLines={10}
            onSave={saveMatterDescription}
          />
        </FieldGrid>
      </RecordHeaderShell>
      {showVersion ? (
        <span className={styles.versionFooter} aria-hidden="true" data-testid="matter-header-version">
          v{CONTROL_VERSION}
        </span>
      ) : null}
    </div>
  );
};
