/**
 * useRecordHeaderFields — the shared record-header field machinery: form-buffer
 * staging, the pending-changes buffer, and lookup projection.
 *
 * FR-13 / FR-14 / FR-19 (record-header-and-notepad-r2). Hoisted verbatim from
 * `MatterHeader/control/MatterHeaderView.tsx` (lines 137-256 at the time of the
 * hoist) so every R2 consumer — the configurable `RecordHeader` control (task
 * 033) and the OOB lookup cell (task 023) — shares ONE implementation instead
 * of re-copying ~82 lines each.
 *
 * Task 033 (r2) EXTENDED the hoist with the generic `saveValue` / `displayValue`
 * pair. The original hoist carried only what `MatterHeaderView` rendered — text
 * and lookup — but the configuration-driven control renders all seven renderers,
 * and `DateField` / `NumberField` / `BooleanField` / `OptionSetField` hand back
 * `Date | null`, `number | null` and `boolean` from `onSave`. Staging those in
 * the PCF layer would have forked the form-buffer primitive, so the third
 * buffer lives here alongside its siblings and shares their throwing gate.
 *
 * ══════════════════════════════════════════════════════════════════════════
 * THE LOAD-BEARING RULE (R1 v1.0.7 — do not "optimize" this away)
 * ══════════════════════════════════════════════════════════════════════════
 * Edits stage into the FORM BUFFER via `getXrmPage().getAttribute(n).setValue(v)`.
 * They are NEVER written straight to Dataverse.
 *
 * R1 originally saved with `Xrm.WebApi.updateRecord` and then re-read the row.
 * That retrieveRecord round trip toggled `useRecordFieldValues`'s `loading`,
 * which toggled `RecordHeaderShell`'s skeleton, which made the ENTIRE header
 * flash on every single keystroke-commit. v1.0.7 replaced it with form-buffer
 * staging: `setValue` marks the attribute dirty, the pending buffer makes the
 * new value visible immediately, and the FORM'S OWN Save commits to Dataverse.
 * This matches OOB Dataverse dirty-state UX — no round trip, no flash.
 *
 * Therefore this hook, by construction:
 *  - MUST NOT call `Xrm.WebApi.updateRecord` (or any write API)
 *  - MUST NOT refetch / `refresh()` after staging — note `useRecordFieldValues`'s
 *    `refresh` is deliberately NOT re-exported from this hook's result, so a
 *    consumer cannot accidentally reintroduce the round trip
 *  - MUST NOT toggle any loading state on save — `loading` is a pure
 *    pass-through of the initial read
 *  - resets the pending buffers ONLY when `recordId` changes
 *
 * ══════════════════════════════════════════════════════════════════════════
 * FR-14 — the unified throwing path (the defect this hoist fixes)
 * ══════════════════════════════════════════════════════════════════════════
 * In R1, `saveText` threw `Field '<n>' not on form` but `saveLookup` only
 * `console.warn`ed and returned — a user's lookup edit was silently discarded
 * whenever the field was absent from the form. BOTH paths now throw, so a
 * `layoutJson` naming a field that is not on the form fails loudly on first
 * edit instead of dropping input. No silent no-op survives in either path.
 *
 * Design notes:
 *  - Host-context surface. `Xrm.Page` via the shared `getXrmPage()`
 *    (`utils/xrmContext.ts`, FR-20) — never a local window-walker (ADR-012).
 *    Note `getXrmPage()` walks `window` → `parent` (2 frames) while `getXrm()`
 *    walks `window` → `parent` → `top` (3). That asymmetry is R1's, preserved
 *    deliberately: a form-embedded RecordHeader PCF always has `Xrm` on frame
 *    1, and a Code Page host on frame 2. Only a host nested deeper than one
 *    iframe (a side pane) would read fine but fail to stage — no such consumer
 *    exists. Widen `getXrmPage()` if one ever does.
 *  - All read I/O stays inside `useRecordFieldValues` (`Xrm.WebApi`) — no raw
 *    `fetch`, no `@spaarke/auth`, no BFF (NFR-05 / NFR-07; ADR-028 boundary).
 *  - React 16/17 compatible: `React.useState` / `useEffect` / `useCallback`
 *    only, imported as `* as React`. No `use()`, `useSyncExternalStore`, or
 *    `createRoot` (ADR-022 / spec NFR-06).
 *  - Entity- and field-agnostic (ADR-012): no `sprk_`-prefixed constants. The
 *    lookup target arrives as the `entityType` argument, resolved by the caller
 *    from metadata (FR-15/FR-21) — R1's hard-coded `LOOKUP_META` is gone.
 *  - Deviation from R1, documented: R1's `console.warn`/`console.info` calls
 *    are replaced by the shared `createLogger` (the shared-lib convention, cf.
 *    `services/FieldMappingHandler.ts`). The warn-before-throw was redundant
 *    with the thrown message; the staged-edit trace is retained at debug level.
 *    Logging is not a form-buffer semantic — staging behavior is unchanged.
 *
 * @see FR-13 / FR-14 / FR-19 in projects/record-header-and-notepad-r2/spec.md
 * @see .claude/adr/ADR-012-shared-components.md
 * @see .claude/adr/ADR-022-pcf-platform-libraries.md
 */

import * as React from 'react';
import type { ILookupItem } from '../types/LookupTypes';
import { createLogger } from '../utils/logger';
import { getXrmPage, type XrmPageAttributeLike } from '../utils/xrmContext';
import { useRecordFieldValues } from './useRecordFieldValues';

const logger = createLogger('UI.Components');
const LOG_COMPONENT = 'useRecordHeaderFields';

/**
 * The OData annotation Dataverse appends to a lookup's `_<field>_value` key to
 * carry the target record's display name.
 */
const FORMATTED_VALUE_ANNOTATION = '@OData.Community.Display.V1.FormattedValue';

// ─────────────────────────────────────────────────────────────────────────────
// Pure helpers (exported for unit testing)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Project a lookup value out of a `useRecordFieldValues` payload into an
 * {@link ILookupItem}.
 *
 * Dataverse returns a lookup as the pair
 * `_<lookupField>_value` (the target GUID) plus
 * `_<lookupField>_value@OData.Community.Display.V1.FormattedValue` (its name).
 *
 * @param values      The `retrieveRecord` payload, or `null`/`undefined` while loading.
 * @param lookupField The lookup attribute's LOGICAL name — e.g. `"sprk_mattertype"`,
 *                    NOT the decorated `"_sprk_mattertype_value"` read key.
 * @returns `{ id, name }` — `name` falls back to `''` when the annotation is
 *          absent — or `null` when `values` is unavailable or the id is
 *          missing / not a string / empty.
 *
 * @example
 * ```ts
 * projectLookup({ _sprk_mattertype_value: 'guid-1',
 *   '_sprk_mattertype_value@OData.Community.Display.V1.FormattedValue': 'Litigation' },
 *   'sprk_mattertype');
 * // → { id: 'guid-1', name: 'Litigation' }
 * ```
 */
export function projectLookup(
  values: Record<string, unknown> | null | undefined,
  lookupField: string
): ILookupItem | null {
  if (!values) return null;
  const key = `_${lookupField}_value`;
  const id = values[key];
  if (typeof id !== 'string' || id.length === 0) return null;
  const name = values[`${key}${FORMATTED_VALUE_ANNOTATION}`];
  return {
    id,
    name: typeof name === 'string' ? name : '',
  };
}

/**
 * Resolve the form-buffer attribute for `fieldName`, or THROW.
 *
 * The single gate both save paths pass through — this is what makes FR-14's
 * "no silent no-op in either path" structural rather than a convention.
 *
 * @throws `Error("Form buffer unavailable")` when `Xrm.Page` is not reachable
 *         on any frame the shared `getXrmPage()` walks.
 * @throws ``Error(`Field '<fieldName>' not on form`)`` when the attribute is
 *         absent from the form (the `layoutJson` names a field the form does
 *         not place).
 */
function requireFormAttribute(fieldName: string): XrmPageAttributeLike {
  const xrmPage = getXrmPage();
  if (!xrmPage?.getAttribute) {
    throw new Error('Form buffer unavailable');
  }
  const attribute = xrmPage.getAttribute(fieldName);
  if (!attribute) {
    throw new Error(`Field '${fieldName}' not on form`);
  }
  return attribute;
}

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Input to {@link useRecordHeaderFields}.
 */
export interface IUseRecordHeaderFieldsOptions {
  /** Dataverse entity logical name (e.g. `"sprk_matter"`). */
  entity: string;
  /**
   * Record GUID (no braces). `null`/empty means "no record selected" — no read
   * is issued and the pending buffers stay empty.
   */
  recordId: string | null;
  /**
   * Field logical names to `$select`. Lookups are read with the decorated
   * `_<field>_value` key; pass that decorated form here, and the UNdecorated
   * logical name to {@link IUseRecordHeaderFieldsResult.saveLookup} /
   * {@link IUseRecordHeaderFieldsResult.displayLookup}.
   *
   * A fresh array literal each render is fine — `useRecordFieldValues` keys on
   * contents, not reference.
   */
  fields: string[];
}

/**
 * Result of {@link useRecordHeaderFields}.
 *
 * `refresh` from `useRecordFieldValues` is intentionally NOT surfaced: a
 * post-save refetch is the exact R1 v1.0.7 regression this hook exists to
 * prevent.
 */
export interface IUseRecordHeaderFieldsResult {
  /** The raw `retrieveRecord` payload keyed by field logical name; `null` until loaded. */
  values: Record<string, unknown> | null;
  /**
   * `true` only while the INITIAL read (or a `recordId`/`fields` change) is in
   * flight. Staging a save never sets this — that is the anti-flash guarantee.
   */
  loading: boolean;
  /** Read error surfaced verbatim by `useRecordFieldValues`; `null` otherwise. */
  error: Error | null;
  /**
   * Stage a text edit into the form buffer and record it in the pending buffer.
   *
   * `async` so it drops directly into `TextField` / `TextareaField`'s
   * `onSave?: (newValue: string) => Promise<void>` contract — those renderers
   * revert the cell on a rejected promise, which is how a throw here surfaces
   * to the user.
   *
   * @throws `Error("Form buffer unavailable")` / ``Error(`Field '<n>' not on form`)``
   */
  saveText: (fieldName: string, newValue: string) => Promise<void>;
  /**
   * Stage a lookup edit into the form buffer and record it in the pending buffer.
   *
   * Synchronous `void` to match `LookupField`'s `onChange: (item) => void`
   * contract (R1 parity).
   *
   * @param fieldName  Lookup attribute LOGICAL name (e.g. `"sprk_mattertype"`).
   * @param item       The selected record, or `null` to CLEAR the lookup.
   * @param entityType Target table's logical name, resolved by the caller from
   *                   metadata (FR-15). Written into the Xrm lookup value shape
   *                   `[{ id, name, entityType }]`. Unused when `item` is `null`.
   * @throws `Error("Form buffer unavailable")` / ``Error(`Field '<n>' not on form`)``
   */
  saveLookup: (fieldName: string, item: ILookupItem | null, entityType: string) => void;
  /**
   * Stage a NON-text, NON-lookup edit into the form buffer and record it in the
   * pending buffer — the generic path for the `date` / `datetime` / `number` /
   * `currency` / `boolean` / `optionset` renderers.
   *
   * Added by task 033 (r2). The hoist in task 022 covered only the two value
   * kinds `MatterHeaderView` actually rendered (text + lookup), but the
   * configuration-driven `RecordHeader` control renders ALL seven renderers,
   * and FR-06 / FR-07 / FR-08 / FR-09 each require an EDIT mode. Their `onSave`
   * callbacks hand back `Date | null`, `number | null` and `boolean`, none of
   * which `saveText` accepts. Implementing that staging in the PCF layer would
   * have forked the form-buffer primitive (a spec MUST NOT), so the behavior
   * lands here instead and every consumer gets it.
   *
   * The value is passed to `setValue` UNCHANGED. Dataverses form buffer already
   * accepts the native types its renderers produce — `Date` for DateTime
   * attributes, `number` for Integer/Decimal/Double/Money and for the numeric
   * option value of a Picklist, `boolean` for TwoOptions — and `null` clears.
   * Coercion belongs to the caller that knows the attribute type, not here.
   *
   * `async` so it drops directly into the renderers
   * `onSave?: (v) => Promise<void>` contract — they revert the cell and stay in
   * edit mode on a rejected promise, which is how a throw here reaches the user.
   *
   * @throws `Error("Form buffer unavailable")` / ``Error(`Field '<n>' not on form`)``
   *         — the SAME unified throwing path as `saveText` / `saveLookup` (FR-14).
   */
  saveValue: (fieldName: string, newValue: unknown) => Promise<void>;
  /**
   * Resolve a text field for display: `pendingText[name] ?? values?.[name]`.
   * A staged edit wins over the Dataverse-loaded value until the form's Save
   * reloads the record.
   */
  displayText: (fieldName: string) => string | null | undefined;
  /**
   * Resolve a NON-text, NON-lookup field for display (task 033, r2).
   *
   * Uses a `'name' in pendingValue` MEMBERSHIP check rather than `??` for the
   * same reason `displayLookup` does: a staged CLEAR stores `null`, and `??`
   * would treat that as "no pending value" and fall back to the still-loaded
   * Dataverse value — the cleared date would spring back.
   *
   * Returns `unknown`: the caller knows the attribute type and hands the value
   * to the matching renderer, each of which already accepts a widened input
   * (`NumberField` takes `number | string`, `DateField` takes `string | Date`,
   * `BooleanField` takes `boolean | ''`).
   */
  displayValue: (fieldName: string) => unknown;
  /**
   * Resolve a lookup for display. Uses a `'name' in pendingLookup` MEMBERSHIP
   * check rather than `??` so a staged CLEAR (pending `null`) displays as empty
   * instead of falling back to the still-loaded Dataverse value.
   */
  displayLookup: (fieldName: string) => ILookupItem | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Read a record's header fields and stage edits into the Dataverse FORM BUFFER.
 *
 * Composes {@link useRecordFieldValues} for the read; adds form-buffer staging,
 * the pending-changes buffer, and display resolution. See the file header for
 * the v1.0.7 no-flash rule that governs every save path here.
 *
 * @example
 * ```tsx
 * const h = useRecordHeaderFields({ entity, recordId, fields });
 *
 * <TextField label="Name" span={2}
 *   value={h.displayText('sprk_mattername')}
 *   onSave={v => h.saveText('sprk_mattername', v)} />
 *
 * // LookupField (task 023, FR-15/FR-15a): `targets` resolved from Dataverse
 * // metadata (task 020's `EntityAttributeMetadata.targets`), never
 * // hard-coded. The OOB `Xrm.Utility.lookupObjects` picker's result
 * // `{ id, name, entityType }` IS `saveLookup`'s `item` + `entityType`
 * // arguments — no translation layer between the picker and the form buffer.
 * <LookupField label="Matter Type" span={1}
 *   value={h.displayLookup('sprk_mattertype')}
 *   targets={targets}
 *   onSave={item => item && h.saveLookup('sprk_mattertype', item, item.entityType)} />
 * ```
 */
export function useRecordHeaderFields(options: IUseRecordHeaderFieldsOptions): IUseRecordHeaderFieldsResult {
  const { entity, recordId, fields } = options;

  // Read path — the ONLY Dataverse I/O in this hook. `refresh` is deliberately
  // not destructured: nothing here may trigger a post-save round trip.
  const { values, loading, error } = useRecordFieldValues(entity, recordId, fields);

  // v1.0.7 pending-changes buffers. Renderers read through `displayText` /
  // `displayLookup` so a staged edit is visible WITHOUT the refetch that used
  // to re-render the whole control.
  const [pendingText, setPendingText] = React.useState<Record<string, string>>({});
  const [pendingLookup, setPendingLookup] = React.useState<Record<string, ILookupItem | null>>({});
  // Task 033 (r2): the generic buffer backing `saveValue` / `displayValue` —
  // date, number, currency, boolean and optionset staged values.
  const [pendingValue, setPendingValue] = React.useState<Record<string, unknown>>({});

  // Reset on record change — and at NO other time. A reset on any other
  // dependency would discard the user's staged, uncommitted edits.
  React.useEffect(() => {
    setPendingText({});
    setPendingLookup({});
    setPendingValue({});
  }, [recordId]);

  // ── Text save (form buffer) ────────────────────────────────────────────────
  const saveText = React.useCallback(async (fieldName: string, newValue: string): Promise<void> => {
    const attribute = requireFormAttribute(fieldName);
    attribute.setValue(newValue);
    setPendingText(prev => ({ ...prev, [fieldName]: newValue }));
    logger.logDebug(LOG_COMPONENT, 'staged text edit', {
      field: fieldName,
      dirty: !!attribute.getIsDirty?.(),
    });
  }, []);

  // ── Lookup save (form buffer) ──────────────────────────────────────────────
  const saveLookup = React.useCallback((fieldName: string, item: ILookupItem | null, entityType: string): void => {
    const attribute = requireFormAttribute(fieldName);
    // Xrm lookup value shape: `[{ id, name, entityType }]`. `null` clears.
    const nextValue = item ? [{ id: item.id, name: item.name, entityType }] : null;
    attribute.setValue(nextValue);
    setPendingLookup(prev => ({ ...prev, [fieldName]: item }));
    logger.logDebug(LOG_COMPONENT, 'staged lookup edit', {
      field: fieldName,
      item,
      dirty: !!attribute.getIsDirty?.(),
    });
  }, []);

  // ── Generic value save (form buffer) — task 033 (r2) ───────────────────────
  // Same `requireFormAttribute` gate as the two paths above, so the FR-14
  // "no silent no-op in ANY path" guarantee extends to every renderer.
  const saveValue = React.useCallback(async (fieldName: string, newValue: unknown): Promise<void> => {
    const attribute = requireFormAttribute(fieldName);
    attribute.setValue(newValue);
    setPendingValue(prev => ({ ...prev, [fieldName]: newValue }));
    logger.logDebug(LOG_COMPONENT, 'staged value edit', {
      field: fieldName,
      dirty: !!attribute.getIsDirty?.(),
    });
  }, []);

  // ── Display resolution ─────────────────────────────────────────────────────
  const displayText = React.useCallback(
    (fieldName: string): string | null | undefined =>
      pendingText[fieldName] ?? (values?.[fieldName] as string | null | undefined),
    [pendingText, values]
  );

  const displayLookup = React.useCallback(
    (fieldName: string): ILookupItem | null =>
      // MEMBERSHIP check, not `??` — a staged CLEAR stores `null`, which `??`
      // would treat as "no pending value" and fall back to the loaded value.
      fieldName in pendingLookup ? pendingLookup[fieldName] : projectLookup(values, fieldName),
    [pendingLookup, values]
  );

  const displayValue = React.useCallback(
    // MEMBERSHIP check, not `??` — see `displayLookup` above for why.
    (fieldName: string): unknown => (fieldName in pendingValue ? pendingValue[fieldName] : values?.[fieldName]),
    [pendingValue, values]
  );

  return { values, loading, error, saveText, saveLookup, saveValue, displayText, displayLookup, displayValue };
}
