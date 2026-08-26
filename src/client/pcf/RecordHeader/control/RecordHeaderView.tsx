/**
 * RecordHeaderView — the CONFIGURATION-DRIVEN composition of shared RecordHeader
 * primitives. One view, every entity.
 *
 * This is R2s central deliverable (FR-01 / FR-12). Where R1s `MatterHeaderView`
 * hard-coded `sprk_matter`, its five fields and its two lookup targets, this
 * view compiles in NO entity name, NO field name and NO lookup target: the
 * layout arrives as `layoutJson` (or is derived from entity metadata), and the
 * entity + record are self-detected by the control class.
 *
 * ══════════════════════════════════════════════════════════════════════════
 * THIS FILE CONTAINS NO MACHINERY — BY CONSTRUCTION
 * ══════════════════════════════════════════════════════════════════════════
 * Every capability comes from a shared primitive, and re-implementing any of
 * them here is a spec MUST NOT (they would then have to be fixed 6+ times):
 *
 *   config resolution   → `resolveHeaderConfig`            (task 031)
 *   field staging       → `useRecordHeaderFields`          (task 022)
 *   editable lookups    → `RecordHeaderLookupField`        (task 023, OOB picker)
 *   toolbar             → `useRecordHeaderToolbarActions`  (task 024)
 *   card + skeleton     → `RecordHeaderShell columns`      (task 032)
 *   renderers           → the `fields/` barrel             (task 015)
 *   form/entity metadata→ `useHeaderFormMetadata`          (this PCF, per 031s
 *                                                           documented hand-off)
 *
 * There is deliberately no `Xrm.Page` attribute access and no OData search
 * builder anywhere below. The only thing this file decides is WHICH renderer a
 * resolved field maps to and HOW that renderers value/callback is shaped —
 * i.e. presentation wiring, not behavior.
 *
 * ── Import discipline (NFR-08, load-bearing) ──────────────────────────────
 * Every `@spaarke/ui-components` import uses a deep `dist/*` path. The shared
 * library has NO `exports` map; the top-level barrel drags
 * `EntityCreationService` → `mammoth` (~1.6 MB vs ~40 KB). Do NOT "clean these
 * up" to barrel imports — DEF-06 is explicitly out of R2 scope.
 * There are likewise NO direct `@fluentui/react-icons` imports in this PCF
 * layer (pcf-build-scaffold gotcha 1 — they break the virtual-PCF webpack
 * build via griffel/src). Icons live inside the shared components.
 *
 * Standards: ADR-006 / ADR-012 / ADR-021 semantic tokens (zero hex) / ADR-022
 * React 16-17 safe; NFR-05 no `@spaarke/auth`; NFR-06 no BFF (host-context
 * `Xrm` only).
 *
 * NOTE: the sparkle / `summaryField` wiring is task 034, layered on this view.
 *
 * @see FR-01 / FR-12 in projects/record-header-and-notepad-r2/spec.md
 */

import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import {
  BooleanField,
  DateField,
  FieldGrid,
  NumberField,
  OptionSetField,
  RecordHeaderLookupField,
  RecordHeaderShell,
  TextField,
  TextareaField,
  extractConfiguredAttributeNames,
  resolveHeaderConfig,
} from '@spaarke/ui-components/dist/components/RecordHeader';
import type {
  ILookupFieldValue,
  ResolvedHeaderConfig,
  ResolvedHeaderField,
} from '@spaarke/ui-components/dist/components/RecordHeader';
import type { NumberFieldKind } from '@spaarke/ui-components/dist/components/RecordHeader';
import type { EntityMetadata } from '@spaarke/ui-components/dist/services/IDataverseClient';
// Per-hook deep paths, NOT the `dist/hooks` barrel: that barrel re-exports
// `useForceSimulation`, which pulls in `d3-force`. Webpack tree-shakes it out of
// the production bundle (the shared lib declares `sideEffects: false`), but
// there is no reason to put it in the graph at all — and importing the barrel
// makes the module unloadable under ts-jest, which has no such tree-shaking.
import { useRecordHeaderFields } from '@spaarke/ui-components/dist/hooks/useRecordHeaderFields';
import type { IUseRecordHeaderFieldsResult } from '@spaarke/ui-components/dist/hooks/useRecordHeaderFields';
import { useRecordHeaderToolbarActions } from '@spaarke/ui-components/dist/hooks/useRecordHeaderToolbarActions';
import { CONTROL_VERSION } from './version';
import { useHeaderFormMetadata } from './useHeaderFormMetadata';

/** The OData annotation carrying a formatted (display) value on a read payload. */
const FORMATTED_VALUE_ANNOTATION = '@OData.Community.Display.V1.FormattedValue';

const useStyles = makeStyles({
  root: { position: 'relative', width: '100%' },
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

export interface IRecordHeaderViewProps {
  /** Entity logical name, self-detected by the control class (FR-12). */
  entityName: string;
  /** Record GUID (no braces). Empty string = "no record selected". */
  recordId: string;
  /** Manifest title override. Outranks the layoutJson / metadata title. */
  title?: string;
  /** When `true` (default), the version footer is rendered. */
  showVersion?: boolean;
  /** RAW `layoutJson` manifest value — parsed by `resolveHeaderConfig`, not here. */
  layoutJson: string | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Pure presentation helpers (exported for unit testing)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * `FieldGrid` cells accept only a `1 | 2 | 3` span. `resolveHeaderConfig`
 * already clamps to the resolved `columns` (FR-05), so this narrows the type
 * without changing the value — it is a compile-time bridge, not a second clamp.
 */
export function toCellSpan(span: number): 1 | 2 | 3 {
  if (span >= 3) return 3;
  if (span === 2) return 2;
  return 1;
}

/**
 * Build the `$select` list for a resolved layout.
 *
 * Lookups MUST be read through their decorated `_<name>_value` key (that is the
 * key `useRecordHeaderFields` documents, and the only one Dataverse returns);
 * every other renderer reads the plain logical name. Duplicates are collapsed
 * so a layout that names the same attribute twice does not produce an invalid
 * repeated `$select` entry.
 */
export function buildSelectFields(fields: ReadonlyArray<ResolvedHeaderField>): string[] {
  const seen = new Set<string>();
  const result: string[] = [];
  for (const field of fields) {
    const key = field.renderer === 'lookup' ? `_${field.name}_value` : field.name;
    if (seen.has(key)) continue;
    seen.add(key);
    result.push(key);
  }
  return result;
}

/**
 * Map a resolved renderer + the attributes Dataverse type onto
 * `NumberField`s formatting kind.
 *
 * The `currency` renderer is unambiguous. A plain `number` renderer defers to
 * the attribute type so Integer does not render two decimal places.
 */
export function toNumberKind(renderer: string, attributeType: string | undefined): NumberFieldKind {
  if (renderer === 'currency' || attributeType === 'Money') return 'money';
  switch (attributeType) {
    case 'Integer':
    case 'BigInt':
      return 'integer';
    case 'Double':
      return 'double';
    case 'Decimal':
    default:
      return 'decimal';
  }
}

/**
 * Pull the currency SYMBOL out of a formatted money value (`"$12,500.00"` →
 * `"$"`), which is what `NumberField.currencySymbol` wants — it deliberately
 * takes symbol text, never an ISO code.
 *
 * Dataverse formats money per the records transaction currency, so the
 * annotation is the correct per-record source. Returns `undefined` when there
 * is no annotation or no leading symbol, and `NumberField` then renders the
 * bare number rather than guessing a currency.
 */
export function extractCurrencySymbol(formatted: unknown): string | undefined {
  if (typeof formatted !== 'string') return undefined;
  const match = /^[^\d\s+-]+/.exec(formatted.trim());
  return match ? match[0] : undefined;
}

// ─────────────────────────────────────────────────────────────────────────────
// View
// ─────────────────────────────────────────────────────────────────────────────

export const RecordHeaderView: React.FC<IRecordHeaderViewProps> = ({
  entityName,
  recordId,
  title,
  showVersion = true,
  layoutJson,
}) => {
  const styles = useStyles();

  // ── 1. Metadata (page-session cached; zero-network form walk) ──────────────
  // The layout's attribute names are read from the RAW json BEFORE the metadata
  // round trip, so the fetch can name every attribute the header might bind —
  // including any the layout references that are not placed on the form. See
  // `useHeaderFormMetadata` for why naming them is load-bearing.
  const configuredNames = React.useMemo(() => extractConfiguredAttributeNames(layoutJson), [layoutJson]);
  const {
    formMetadata,
    entityMetadata,
    loading: metadataLoading,
  } = useHeaderFormMetadata(entityName, configuredNames);

  // ── 2. Config resolution (pure; at most one console.warn per resolve) ──────
  // Memoized so a malformed `layoutJson` warns ONCE per config change rather
  // than on every render. Not called at all until metadata lands, so the
  // absent-layoutJson warning cannot fire against an empty attribute map.
  const resolved: ResolvedHeaderConfig | null = React.useMemo(
    () => (formMetadata ? resolveHeaderConfig(layoutJson, formMetadata) : null),
    [layoutJson, formMetadata]
  );

  // ── 3. Record read + form-buffer staging ───────────────────────────────────
  const selectFields = React.useMemo(
    () => (resolved ? buildSelectFields(resolved.fields) : []),
    [resolved]
  );
  // `recordId` is withheld until the field list is known — an empty `$select`
  // would otherwise pull the ENTIRE record for one throwaway render.
  const fieldsApi = useRecordHeaderFields({
    entity: entityName,
    recordId: resolved && recordId ? recordId : null,
    fields: selectFields,
  });

  // ── 4. Toolbar ─────────────────────────────────────────────────────────────
  // Manifest `title` outranks the resolved (layoutJson → metadata) title.
  const toolbarTitle = (title && title.trim().length > 0 ? title : resolved?.title) ?? '';
  const { toolbarProps } = useRecordHeaderToolbarActions({
    entity: entityName,
    recordId,
    title: toolbarTitle,
  });

  const columns = resolved?.columns ?? 3;

  return (
    <div className={styles.root}>
      <RecordHeaderShell
        toolbar={toolbarProps}
        loading={metadataLoading || fieldsApi.loading}
        columns={columns}
        borderless
      >
        <FieldGrid columns={columns}>
          {(resolved?.fields ?? []).map(field => (
            <HeaderFieldCell
              key={field.name}
              field={field}
              fieldsApi={fieldsApi}
              entityMetadata={entityMetadata}
            />
          ))}
        </FieldGrid>
      </RecordHeaderShell>
      {showVersion ? (
        <span className={styles.versionFooter} aria-hidden="true" data-testid="record-header-version">
          v{CONTROL_VERSION}
        </span>
      ) : null}
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Per-field renderer dispatch
// ─────────────────────────────────────────────────────────────────────────────

interface IHeaderFieldCellProps {
  field: ResolvedHeaderField;
  fieldsApi: IUseRecordHeaderFieldsResult;
  entityMetadata: EntityMetadata | null;
}

/**
 * Render ONE resolved field with the renderer its config/metadata selected.
 *
 * `readOnly` is expressed the way the FR-10 renderer contract defines it:
 * `editable === (typeof onSave === 'function')`, so a read-only cell simply
 * omits the callback rather than passing `disabled`.
 *
 * Extracted as a component (rather than a `switch` inlined in a `.map()`) so
 * each cells `useCallback`s are stable per field — an inline map would
 * re-create every callback on every parent render and defeat the renderers
 * own draft/commit memoization.
 */
const HeaderFieldCell: React.FC<IHeaderFieldCellProps> = ({ field, fieldsApi, entityMetadata }) => {
  const { name, label, required, readOnly, renderer } = field;
  const span = toCellSpan(field.span);
  const attribute = entityMetadata?.attributes?.[name];

  const { saveText, saveValue, saveLookup, displayText, displayValue, displayLookup, values } = fieldsApi;

  const handleSaveText = React.useCallback((v: string) => saveText(name, v), [saveText, name]);
  const handleSaveValue = React.useCallback((v: unknown) => saveValue(name, v), [saveValue, name]);
  const handleSaveLookup = React.useCallback(
    (item: ILookupFieldValue | null): void => {
      // The 023 picker never calls back with `null` (cancel is a no-op); the
      // null arm exists only for a future explicit-clear affordance.
      if (!item) return;
      // The pickers `{ id, name, entityType }` IS the form-buffer payload —
      // no translation layer, per the 022/023 contract.
      saveLookup(name, { id: item.id, name: item.name }, item.entityType);
    },
    [saveLookup, name]
  );

  switch (renderer) {
    case 'textarea':
      return (
        <TextareaField
          label={label}
          span={span}
          required={required}
          value={displayText(name)}
          maxLines={field.maxLines}
          onSave={readOnly ? undefined : handleSaveText}
        />
      );

    case 'lookup': {
      // FR-15 — targets come from METADATA, never hard-coded. `targets[0]` is
      // what the OOB picker uses (023); see the task notes on polymorphic
      // lookups.
      const targets = attribute?.targets;
      const current = displayLookup(name);
      const value: ILookupFieldValue | null = current
        ? { id: current.id, name: current.name, entityType: targets?.[0] ?? '' }
        : null;
      return (
        // NOTE: no `required` here — deliberately. Unlike its six siblings the
        // 023 lookup renderer does not accept a `required` prop, and that is BY
        // DESIGN: `rendererContract.test.tsx` explicitly holds this renderer out
        // of the FR-10 contract suite (its value shape and commit model differ —
        // the OOB picker has no draft state). Since the `*` marker is
        // TextField-only per D-10, the prop is inert on every sibling anyway, so
        // omitting it is visually identical. Not a gap to patch.
        <RecordHeaderLookupField
          label={label}
          span={span}
          value={value}
          targets={targets}
          onSave={readOnly ? undefined : handleSaveLookup}
        />
      );
    }

    case 'optionset': {
      const options = attribute?.optionSet?.map(o => ({ value: o.value, label: o.label }));
      const raw = displayValue(name);
      // The staged value and the loaded value are BOTH the raw numeric option,
      // so one lookup covers both. The formatted annotation is the fallback for
      // an attribute whose option set metadata did not project.
      const resolvedLabel =
        (typeof raw === 'number' ? options?.find(o => o.value === raw)?.label : undefined) ??
        (raw === null || raw === undefined
          ? undefined
          : (values?.[`${name}${FORMATTED_VALUE_ANNOTATION}`] as string | undefined));
      return (
        <OptionSetField
          label={label}
          span={span}
          required={required}
          value={resolvedLabel}
          options={options}
          onSave={readOnly ? undefined : handleSaveValue}
        />
      );
    }

    case 'date':
    case 'datetime':
      return (
        <DateField
          label={label}
          span={span}
          required={required}
          format={renderer === 'date' ? 'date' : 'datetime'}
          value={displayValue(name) as string | Date | null | undefined}
          onSave={readOnly ? undefined : handleSaveValue}
        />
      );

    case 'number':
    case 'currency':
      return (
        <NumberField
          label={label}
          span={span}
          required={required}
          kind={toNumberKind(renderer, attribute?.attributeType)}
          currencySymbol={extractCurrencySymbol(values?.[`${name}${FORMATTED_VALUE_ANNOTATION}`])}
          value={displayValue(name) as number | string | null | undefined}
          onSave={readOnly ? undefined : handleSaveValue}
        />
      );

    case 'boolean': {
      // TwoOptions labels ship in the same `optionSet` projection as a picklist:
      // value 1 is the true option, value 0 the false one. Falling through to
      // `BooleanField`s Yes/No defaults when they are absent.
      const options = attribute?.optionSet;
      return (
        <BooleanField
          label={label}
          span={span}
          required={required}
          trueLabel={options?.find(o => o.value === 1)?.label}
          falseLabel={options?.find(o => o.value === 0)?.label}
          value={displayValue(name) as boolean | null | undefined}
          onSave={readOnly ? undefined : handleSaveValue}
        />
      );
    }

    case 'text':
    default:
      return (
        <TextField
          label={label}
          span={span}
          required={required}
          value={displayText(name)}
          onSave={readOnly ? undefined : handleSaveText}
        />
      );
  }
};
