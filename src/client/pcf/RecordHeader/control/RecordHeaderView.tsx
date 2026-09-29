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
 *   editable lookups    → `LookupField` + `useLookupTargetSearch`
 *                                                          (inline, 2026-08-27)
 *   read-only lookups   → `RecordHeaderLookupField`        (task 023)
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
 * Task 034 layered the sparkle / `summaryField` wiring on top. It follows the
 * same rule: the popover itself is `HeaderToolbar` + the shared
 * `AiSummaryPopover`; this file only decides WHETHER the affordance exists
 * (metadata existence, FR-17) and WHICH column feeds it (FR-22).
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
  useLookupTargetSearch,
} from '@spaarke/ui-components/dist/components/RecordHeader';
import type {
  ILookupFieldValue,
  ResolvedHeaderConfig,
  ResolvedHeaderField,
} from '@spaarke/ui-components/dist/components/RecordHeader';
// The INLINE search-as-you-type lookup — a DIFFERENT component from the
// `RecordHeaderLookupField` imported above, and the project CLAUDE.md warns
// they are easy to confuse. This one reproduces the OOB inline dropdown; that
// one is the display/navigate renderer, kept for the read-only path below.
import { LookupField } from '@spaarke/ui-components/dist/components/LookupField/LookupField';
import type { ILookupItem } from '@spaarke/ui-components/dist/types/LookupTypes';
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
// FR-22a — the summary field name and its empty-state copy are IMPORTED, never
// re-declared here. R1 task 001 already corrected this constant once, and the
// v1.0.20 sparkle regression was precisely a summary-field mismatch between two
// copies of the literal. A grep guard in the test suite enforces the rule.
import {
  RECORDSUMMARY_FIELD,
  RECORD_SUMMARY_EMPTY_TEXT,
} from '@spaarke/ui-components/dist/hooks/toolbarLaunchDefaults';
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
 *
 * ── `summaryField` is CONDITIONAL, and that is the whole point (FR-23) ──────
 * The sparkle's backing column is appended ONLY when the caller has already
 * confirmed it exists in entity metadata. A `$select` is all-or-nothing: one
 * name Dataverse does not recognise fails the ENTIRE retrieve with HTTP 400 and
 * blanks every cell in the header. That is not hypothetical — it is RS-1, which
 * took the shipped Matter header down on every record, and it is the third
 * occurrence of the same failure class in this codebase (FAILURE-MODES G-12).
 * Passing `null`/`undefined` here is the "attribute does not exist" branch.
 *
 * @param fields       Resolved layout fields, in render order.
 * @param summaryField Effective summary attribute, or `null` when it is absent
 *                     from metadata and MUST NOT be selected.
 */
export function buildSelectFields(fields: ReadonlyArray<ResolvedHeaderField>, summaryField?: string | null): string[] {
  const seen = new Set<string>();
  const result: string[] = [];
  const push = (key: string): void => {
    if (seen.has(key)) return;
    seen.add(key);
    result.push(key);
  };
  for (const field of fields) {
    push(field.renderer === 'lookup' ? `_${field.name}_value` : field.name);
  }
  // Appended last so the layout's own field order is untouched; de-duplicated
  // by the shared `seen` set, so a layout that also RENDERS its summary column
  // does not produce a repeated `$select` entry.
  if (typeof summaryField === 'string' && summaryField.length > 0) push(summaryField);
  return result;
}

/**
 * Widen the metadata request to cover BOTH summary-field candidates.
 *
 * `extractConfiguredAttributeNames` already contributes a summaryField that
 * `layoutJson` names explicitly. It cannot contribute the DEFAULT, because the
 * default is applied downstream at this wiring site (task 031 deliberately
 * passes `summaryField` through as `undefined` when unconfigured).
 *
 * That ordering would otherwise deadlock: the effective field comes from the
 * resolved config, the resolved config comes from metadata, and metadata is
 * fetched by NAME. Requesting both candidates up front breaks the cycle — the
 * existence check downstream then simply reads whichever one won.
 *
 * Getting this wrong is silent and total: `sprk_recordsummary` sits on none of
 * the six rollout entities' FORMS, so without this the metadata payload would
 * never contain it, the existence gate would fail on every entity, and the
 * sparkle would be invisible everywhere with no error to explain why.
 */
export function buildMetadataAttributeNames(configuredNames: ReadonlyArray<string>): string[] {
  const names = [...configuredNames];
  if (!names.includes(RECORDSUMMARY_FIELD)) names.push(RECORDSUMMARY_FIELD);
  return names;
}

/**
 * Does `summaryField` name an attribute the entity actually has?
 *
 * FR-17's visibility rule keys on EXISTENCE, never on population: the sparkle
 * shows for an existing-but-empty column (rendering "No summary yet"), and
 * hides only when the attribute is genuinely absent. A separate project
 * populates these columns, so at R2 ship time "exists but empty" is the normal
 * case and must not read as a broken affordance.
 */
export function summaryFieldExists(entityMetadata: EntityMetadata | null, summaryField: string): boolean {
  const attributes = entityMetadata?.attributes;
  if (!attributes || summaryField.length === 0) return false;
  return Object.prototype.hasOwnProperty.call(attributes, summaryField);
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
  const configuredNames = React.useMemo(
    () => buildMetadataAttributeNames(extractConfiguredAttributeNames(layoutJson)),
    [layoutJson]
  );
  const { formMetadata, entityMetadata, loading: metadataLoading } = useHeaderFormMetadata(entityName, configuredNames);

  // ── 2. Config resolution (pure; at most one console.warn per resolve) ──────
  // Memoized so a malformed `layoutJson` warns ONCE per config change rather
  // than on every render. Not called at all until metadata lands, so the
  // absent-layoutJson warning cannot fire against an empty attribute map.
  const resolved: ResolvedHeaderConfig | null = React.useMemo(
    () => (formMetadata ? resolveHeaderConfig(layoutJson, formMetadata) : null),
    [layoutJson, formMetadata]
  );

  // ── 3. Sparkle summary field — existence-gated (FR-17 / FR-22) ─────────────
  // The DEFAULT is applied here, not in the resolver: task 031 passes
  // `summaryField` through as `undefined` when `layoutJson` omits it, precisely
  // so the wiring site owns this decision. A configured field outranks it.
  const effectiveSummaryField = resolved?.summaryField ?? RECORDSUMMARY_FIELD;
  const hasSummaryField = summaryFieldExists(entityMetadata, effectiveSummaryField);

  // ── 4. Record read + form-buffer staging ───────────────────────────────────
  const selectFields = React.useMemo(
    () => (resolved ? buildSelectFields(resolved.fields, hasSummaryField ? effectiveSummaryField : null) : []),
    [resolved, hasSummaryField, effectiveSummaryField]
  );
  // `recordId` is withheld until the field list is known — an empty `$select`
  // would otherwise pull the ENTIRE record for one throwaway render.
  const fieldsApi = useRecordHeaderFields({
    entity: entityName,
    recordId: resolved && recordId ? recordId : null,
    fields: selectFields,
  });

  // ── 5. Toolbar ─────────────────────────────────────────────────────────────
  // Manifest `title` outranks the resolved (layoutJson → metadata) title.
  const toolbarTitle = (title && title.trim().length > 0 ? title : resolved?.title) ?? '';
  const { toolbarProps } = useRecordHeaderToolbarActions({
    entity: entityName,
    recordId,
    title: toolbarTitle,
  });

  // The sparkle is composed by the CONSUMER and merged into `toolbarProps` —
  // `useRecordHeaderToolbarActions` stopped owning it at v1.0.10 and supplies
  // only the launcher slots. `HeaderToolbar` builds the trigger + popover from
  // this prop, so no icon is imported at the PCF layer (a direct
  // `@fluentui/react-icons` import breaks the virtual-PCF webpack resolution).
  // Hoisted to a local so the callback's dep array stays a plain identifier.
  const summaryValue = (fieldsApi.values?.[effectiveSummaryField] ?? null) as string | null;

  // Unconditional hook — only the SPREAD below is conditional, so the hook
  // order is identical on every render whichever branch the gate takes.
  const fetchSummary = React.useCallback(
    async (): Promise<{ summary: string | null; tldr: string | null }> => ({
      summary: summaryValue,
      tldr: null,
    }),
    [summaryValue]
  );

  // FR-17: OMIT the prop entirely when the attribute is absent — that is what
  // makes `HeaderToolbar` render no sparkle at all. Passing a fetch that
  // resolves to `null` would instead show a sparkle whose popover is
  // permanently empty, which is the dead affordance the spec rules out.
  const toolbarPropsWithSparkle = hasSummaryField
    ? {
        ...toolbarProps,
        aiSummary: { onFetchSummary: fetchSummary, emptyText: RECORD_SUMMARY_EMPTY_TEXT },
      }
    : toolbarProps;

  const columns = resolved?.columns ?? 3;

  // ── Per-cell decision diagnostic (UAT round 4) ────────────────────────────
  // The form/metadata diagnostic in `useHeaderFormMetadata` reports what the
  // control READ. This reports what it DECIDED per field, which is the other
  // half UAT kept needing: a lookup that renders a value but will not open its
  // picker, or a DateOnly column that renders a time picker, are both invisible
  // from the outside. `renderer` + `targets` + `editable` explain either in one
  // line. Fires once per resolve (memoized upstream), not per render.
  React.useEffect(() => {
    if (!resolved || !entityMetadata) return;
    try {
      console.info(
        '[RecordHeader] field decisions',
        resolved.fields.map(f => {
          const attr = entityMetadata.attributes?.[f.name];
          return {
            field: f.name,
            renderer: f.renderer,
            attributeType: attr?.attributeType,
            // For a date cell this is the whole story: anything but 'DateOnly'
            // renders `datetime-local`.
            format: attr?.format,
            targets: attr?.targets,
            readOnly: f.readOnly,
            // A lookup needs BOTH halves to become editable.
            editable: !f.readOnly && (f.renderer !== 'lookup' || !!attr?.targets?.length),
            // Which lookup SURFACE this cell chose. 'display' on a field the
            // maker expects to edit means one of the two halves above is
            // missing — read `readOnly` and `targets` on the same line.
            picker: f.renderer !== 'lookup' ? undefined : !f.readOnly && attr?.targets?.length ? 'inline' : 'display',
          };
        })
      );
    } catch {
      /* diagnostics must never affect rendering */
    }
  }, [resolved, entityMetadata]);

  return (
    <div className={styles.root}>
      <RecordHeaderShell
        toolbar={toolbarPropsWithSparkle}
        loading={metadataLoading || fieldsApi.loading}
        columns={columns}
        borderless
      >
        <FieldGrid columns={columns}>
          {(resolved?.fields ?? []).map(field => (
            <HeaderFieldCell key={field.name} field={field} fieldsApi={fieldsApi} entityMetadata={entityMetadata} />
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

  // ── Inline-lookup wiring ───────────────────────────────────────────────────
  // `targets[0]` is the TARGET TABLE, resolved from metadata or the form
  // control — never a constant (FR-15). Undefined for the six non-lookup
  // renderers, which is exactly why the hook below is safe to call for every
  // cell: with no target it does no work and issues no request.
  const lookupTarget = renderer === 'lookup' ? attribute?.targets?.[0] : undefined;

  // Unlike `handleSaveLookup`, this DOES pass `null` through. The inline
  // dropdown reports a genuine clear (the chip's dismiss button) and a
  // type-over of a committed value the same way, and both should stage —
  // `saveLookup(…, null, …)` calls `setValue(null)`, which is how R1's Matter
  // header has always behaved.
  const handlePickLookup = React.useCallback(
    (item: ILookupItem | null): void => {
      saveLookup(name, item, lookupTarget ?? '');
    },
    [saveLookup, name, lookupTarget]
  );

  // Hooks must run unconditionally — this sits ABOVE the renderer switch by
  // necessity, not by preference.
  const { search: searchLookup, openAdvanced } = useLookupTargetSearch(lookupTarget, label, handlePickLookup);

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
      // FR-15 — targets come from METADATA, never hard-coded. See the task
      // notes on polymorphic lookups for why only `targets[0]` is used.
      const targets = attribute?.targets;
      const current = displayLookup(name);

      // ── Editable → the INLINE dropdown (2026-08-27) ────────────────────────
      // Reverses FR-15a's original "OOB picker (modal)" decision, which shipped
      // in v1.1.8 and opened the platform SIDE PANE. OOB renders lookups as an
      // inline type-ahead, and a header that departs from that on every lookup
      // cell of every entity reads as broken rather than different (UAT round
      // 5). Hosting the platform's own inline control is not possible —
      // `ComponentFramework.Factory` exposes no way to instantiate it — so the
      // shape is reproduced with supported primitives and **Advanced**
      // escalates to the real OOB dialog. That is the "proprietary browse + OOB
      // escalation" pattern in MODAL-DECISION-CRITERIA.md.
      //
      // Requires BOTH halves, same as the picker did: a save path (the layout
      // did not mark the field read-only) AND a resolved target. Without a
      // target there is nothing to search, so the cell falls through to the
      // display renderer below, whose console.warn names which half is missing.
      if (!readOnly && lookupTarget) {
        return (
          <LookupField
            span={span}
            label={label}
            required={required}
            value={current}
            onChange={handlePickLookup}
            onSearch={searchLookup}
            // OOB form-field look: no border box, gray fill, brand underline on
            // focus. Not a custom style — `filled-darker` IS
            // `colorNeutralBackground3`, the same gray the sibling read cells
            // use, and Fluent draws the focus underline for every appearance.
            appearance="filled-darker"
            // OOB's own wording, verbatim — "Look for Project Type", not the
            // component's generic "Search project type...". Sentence case is
            // wrong here: the label is a proper field name.
            placeholder={`Look for ${label}`}
            // Opt-in footer. Supplied here because a form-hosted PCF always has
            // `Xrm.Utility.lookupObjects`; the wizard consumers in Code Pages
            // deliberately omit it. There is NO "+ New" beside it — owner
            // decision, guarded by a test in the shared lib.
            onAdvanced={openAdvanced}
            // Browse-without-typing: an empty term returns the target's first
            // N rows, matching the OOB dropdown.
            minSearchLength={0}
            openOnFocus
          />
        );
      }

      const value: ILookupFieldValue | null = current
        ? { id: current.id, name: current.name, entityType: targets?.[0] ?? '' }
        : null;
      return (
        // ── Read-only, or editable-but-targetless ─────────────────────────────
        // Renders the value and navigates to the related record on click.
        //
        // `onSave` is still passed on the targetless branch even though the
        // component cannot become editable without targets. That is deliberate
        // and diagnostic: it makes the component's own warning report
        // `hasOnSave: true, hasTargets: false`, which names the actual missing
        // half. Suppressing it would report the field as merely read-only and
        // send the next investigation to the wrong place — the exact confusion
        // that made UAT round 5 read as "a locked field linked to the OOB one".
        //
        // NOTE: no `required` here — deliberately. Unlike its six siblings the
        // 023 lookup renderer does not accept a `required` prop, and that is BY
        // DESIGN: `rendererContract.test.tsx` explicitly holds this renderer out
        // of the FR-10 contract suite (its value shape and commit model differ).
        // Since the `*` marker is TextField-only per D-10, the prop is inert on
        // every sibling anyway, so omitting it is visually identical.
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
