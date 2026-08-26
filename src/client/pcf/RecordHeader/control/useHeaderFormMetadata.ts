/**
 * useHeaderFormMetadata — adapts the LIVE form context + Dataverse entity
 * metadata into the `HeaderFormMetadata` shape `resolveHeaderConfig` consumes.
 *
 * Task 031 explicitly assigns this adaptation to task 033s wiring site:
 *
 *   > "Task 033 adapts the live form context + `retrieveEntityMetadata` result
 *      into this shape at the wiring site; the field names are a superset-
 *      compatible subset of both sources."
 *
 * It lives in its own module (not in `RecordHeaderView.tsx`) so the view stays
 * a pure composition of shared primitives — the view never touches `Xrm`.
 *
 * ══════════════════════════════════════════════════════════════════════════
 * INSERTION ORDER IS FORM ORDER (the 031 caller contract)
 * ══════════════════════════════════════════════════════════════════════════
 * `resolveHeaderConfig` walks `attributes` with `Object.keys()` and treats
 * that order as FORM ORDER when deriving tier-2 defaults (FR-04). So this
 * module inserts the form controls FIRST, in the order the form declares them,
 * and only then appends the metadata-only attributes (those on the entity but
 * not placed on the form).
 *
 * Appending the metadata-only attributes is deliberate, not sloppiness: a
 * `layoutJson` may legitimately name an attribute that exists on the entity but
 * is NOT on the form. Such a field must still resolve a label + renderer and
 * RENDER — and then fail LOUDLY (`Field '<n>' not on form`) on first edit, from
 * the shared hooks unified throwing path (FR-14). Dropping it at resolve time
 * would silently hide the maker error instead of surfacing it.
 *
 * Two zero-network reads come from the FORM rather than from metadata, because
 * the form is the authority for both:
 *  - `label` — `control.getLabel()`, which reflects the makers per-form label
 *    override (design.md 5.4 names this the primary label source).
 *  - `requiredLevel` — `attribute.getRequiredLevel()`, the level actually in
 *    force on this form (business rules can raise it above the metadata level).
 *
 * Boundary: `Xrm.Utility` / `Xrm.Page` only, through the shared
 * `XrmDataverseClient` + `getXrmPage()`. No BFF, no `@spaarke/auth`, no raw
 * fetch (NFR-05 / NFR-06).
 *
 * @see @spaarke/ui-components configResolution — `HeaderFormMetadata` contract
 * @see @spaarke/ui-components XrmDataverseClient — page-session metadata cache (task 020)
 */

import * as React from 'react';
import type {
  HeaderAttributeMetadata,
  HeaderFormMetadata,
} from '@spaarke/ui-components/dist/components/RecordHeader/configResolution';
import type { EntityMetadata } from '@spaarke/ui-components/dist/services/IDataverseClient';
import { XrmDataverseClient } from '@spaarke/ui-components/dist/services/XrmDataverseClient';
import { getXrmPage } from '@spaarke/ui-components/dist/utils/xrmContext';

// ─────────────────────────────────────────────────────────────────────────────
// Minimal structural typing for the form-context surfaces we read.
//
// Declared locally (the shared `XrmPageLike` covers only `getAttribute`) rather
// than taking a dependency on `@types/xrm`, matching the shared librarys own
// convention in `utils/xrmContext.ts`.
// ─────────────────────────────────────────────────────────────────────────────

interface IFormAttributeLike {
  getRequiredLevel?(): string;
  /**
   * `"date"` | `"datetime"` for a DateTime attribute (also `"text"`,
   * `"email"`, `"textarea"`, … for String). Documented to return a STRING —
   * which is exactly why it is preferred over the metadata `Format`. See
   * {@link normalizeFormFormat}.
   */
  getFormat?(): string;
}

interface IFormControlLike {
  getName?(): string;
  getLabel?(): string;
  getAttribute?(): IFormAttributeLike | null | undefined;
  /** Lookup controls only — the entity logical names the picker may search. */
  getEntityTypes?(): string[];
}

interface IFormControlCollectionLike {
  forEach?(callback: (control: IFormControlLike, index: number) => void): void;
  getAll?(): IFormControlLike[];
}

interface IFormPageLike {
  ui?: { controls?: IFormControlCollectionLike };
  getAttribute?(name: string): IFormAttributeLike | null | undefined;
}

/** One control as read off the live form, in form order. */
export interface IFormControlProjection {
  name: string;
  label?: string;
  requiredLevel?: string;
  /** Already normalized to the metadata vocabulary — see {@link normalizeFormFormat}. */
  format?: string;
  /** Lookup targets read from the form control (`getEntityTypes()`). */
  entityTypes?: string[];
}

/**
 * Translate `attribute.getFormat()` into the vocabulary the resolver speaks.
 *
 * ══════════════════════════════════════════════════════════════════════════
 * WHY THE FORM, AND NOT THE METADATA `Format`
 * ══════════════════════════════════════════════════════════════════════════
 * `resolveHeaderConfig` distinguishes a date cell from a datetime cell on
 * `format === 'DateOnly'` — the WEB API spelling. But the header's metadata
 * comes from `Xrm.Utility.getEntityMetadata`, the CLIENT API, which returns
 * `Format` as a NUMBER. `projectAttribute` accepts only a string, so `format`
 * arrived `undefined` and EVERY DateOnly column rendered as
 * `<Input type="datetime-local">` — the "Opened Date shows a time picker"
 * defect, and the third instance of the Client-API-shape trap (FAILURE-MODES
 * G-13, after `AttributeType` and `DisplayName`).
 *
 * The numeric enum is NOT decoded here on purpose. `Format` is scoped by
 * attribute type — `0` means `DateOnly` for a DateTime but `Email` for a
 * String — and this project has already shipped three broken builds on
 * confidently-guessed platform details. `attribute.getFormat()` is documented
 * to return a STRING (`"date"` / `"datetime"`), needs no enum table, and is
 * zero-network. The form is already the authority for `label` and
 * `requiredLevel` here for the same reason.
 *
 * Non-date formats pass through untouched: the resolver consults only
 * `'DateOnly'`, so `"text"` or `"email"` is inert rather than wrong.
 */
export function normalizeFormFormat(rawFormat: unknown): string | undefined {
  if (typeof rawFormat !== 'string' || rawFormat.length === 0) return undefined;
  switch (rawFormat.toLowerCase()) {
    case 'date':
      return 'DateOnly';
    case 'datetime':
      return 'DateAndTime';
    default:
      return rawFormat;
  }
}

/**
 * Fill gaps in the Client-API metadata payload from the live form.
 *
 * Returns a NEW object — `retrieveEntityMetadata` results are page-session
 * cached and shared across every header on the page, so mutating them in place
 * would leak one form's controls into another's metadata.
 *
 * Two gaps, both because the Client API's attribute payload is narrower than
 * the Web API's:
 *
 *  - **`format`** — see {@link normalizeFormFormat}.
 *  - **`targets`** — Microsoft's documented Client-API attribute metadata
 *    (AttributeType, DisplayName, LogicalName, OptionSet, SchemaName,
 *    IsPrimaryId/Name, IsValidFor*) does **not** include `Targets`. Without it
 *    `RecordHeaderLookupField` computes `editable = onSave && hasTargets` as
 *    FALSE, so the cell renders its value and clicking does nothing — the
 *    "Project Type lookup does not work" defect. `control.getEntityTypes()` is
 *    the documented Client-API source for exactly this and costs no round trip.
 *
 * Metadata WINS wherever it already supplied a value; the form only fills
 * blanks. A field that is not on the form contributes nothing, which is the
 * pre-existing behaviour.
 */
export function applyFormControlHints(
  entityMetadata: EntityMetadata,
  formControls: ReadonlyArray<IFormControlProjection>
): EntityMetadata {
  if (formControls.length === 0) return entityMetadata;

  let changed = false;
  const attributes: EntityMetadata['attributes'] = { ...entityMetadata.attributes };

  for (const control of formControls) {
    const existing = Object.prototype.hasOwnProperty.call(attributes, control.name)
      ? attributes[control.name]
      : undefined;
    if (!existing) continue;

    const needsFormat = !existing.format && !!control.format;
    const needsTargets =
      (!Array.isArray(existing.targets) || existing.targets.length === 0) &&
      Array.isArray(control.entityTypes) &&
      control.entityTypes.length > 0;

    if (!needsFormat && !needsTargets) continue;

    attributes[control.name] = {
      ...existing,
      ...(needsFormat ? { format: control.format } : {}),
      ...(needsTargets ? { targets: control.entityTypes } : {}),
    };
    changed = true;
  }

  return changed ? { ...entityMetadata, attributes } : entityMetadata;
}

/**
 * Walk the form control collection IN ORDER.
 *
 * Defensive throughout: this runs inside a `useEffect` on a host we do not
 * own, and a throw here would blank the header — exactly what NFR-10 forbids.
 * Every failure degrades to "no form controls", which simply means tier-2
 * derivation falls back to metadata order.
 */
export function readFormControlOrder(): IFormControlProjection[] {
  const page = getXrmPage() as unknown as IFormPageLike | null;
  const controls = page?.ui?.controls;
  if (!controls) return [];

  const collected: IFormControlLike[] = [];
  try {
    if (typeof controls.forEach === 'function') {
      controls.forEach(control => collected.push(control));
    } else if (typeof controls.getAll === 'function') {
      collected.push(...controls.getAll());
    }
  } catch {
    return [];
  }

  const projections: IFormControlProjection[] = [];
  const seen = new Set<string>();

  for (const control of collected) {
    let name: string | undefined;
    let label: string | undefined;
    let requiredLevel: string | undefined;
    let format: string | undefined;
    let entityTypes: string[] | undefined;

    try {
      name = typeof control?.getName === 'function' ? control.getName() : undefined;
    } catch {
      name = undefined;
    }
    // Controls without a backing attribute (sub-grids, web resources, spacers)
    // have no logical name we can bind a header cell to.
    if (typeof name !== 'string' || name.length === 0 || seen.has(name)) continue;

    try {
      label = typeof control?.getLabel === 'function' ? control.getLabel() : undefined;
    } catch {
      label = undefined;
    }
    try {
      const attribute =
        typeof control?.getAttribute === 'function' ? control.getAttribute() : page?.getAttribute?.(name);
      requiredLevel = typeof attribute?.getRequiredLevel === 'function' ? attribute.getRequiredLevel() : undefined;
      // `"date"` / `"datetime"` — the string the Client API metadata does NOT
      // give us. Normalized here so downstream only ever sees the Web-API
      // vocabulary the resolver compares against.
      format =
        typeof attribute?.getFormat === 'function' ? normalizeFormFormat(attribute.getFormat()) : undefined;
    } catch {
      requiredLevel = undefined;
      format = undefined;
    }

    try {
      // Lookup controls only; every other control type lacks the method, so
      // this stays `undefined` rather than throwing.
      const types = typeof control?.getEntityTypes === 'function' ? control.getEntityTypes() : undefined;
      entityTypes = Array.isArray(types) && types.length > 0 ? types : undefined;
    } catch {
      entityTypes = undefined;
    }

    seen.add(name);
    projections.push({ name, label, requiredLevel, format, entityTypes });
  }

  return projections;
}

/**
 * Build the attribute list to request from `retrieveEntityMetadata`.
 *
 * PURE — directly unit-testable, which matters because getting this list wrong
 * is what produced the v1.1.0 "every cell is an em-dash" defect.
 *
 * The union is (form controls in form order) ∪ (names the `layoutJson`
 * references). Form controls come first so a debug dump reads in form order;
 * ordering is otherwise irrelevant to the caller, which sorts for its cache
 * key. Duplicates and blanks are dropped.
 *
 * @param formControls   Controls as read off the live form, in form order.
 * @param configuredNames Names referenced by `layoutJson` (may include
 *                        attributes that are NOT on the form — that is
 *                        legitimate and is precisely why they must be
 *                        requested explicitly).
 */
export function buildRequestedAttributeNames(
  formControls: ReadonlyArray<IFormControlProjection>,
  configuredNames: ReadonlyArray<string>
): string[] {
  const seen = new Set<string>();
  const out: string[] = [];
  const push = (name: unknown): void => {
    if (typeof name !== 'string') return;
    const trimmed = name.trim();
    if (trimmed.length === 0 || seen.has(trimmed)) return;
    seen.add(trimmed);
    out.push(trimmed);
  };

  for (const control of formControls) push(control?.name);
  for (const name of configuredNames) push(name);

  return out;
}

/**
 * Merge form-order controls + entity metadata into the resolver input.
 *
 * PURE — no `Xrm`, no I/O — so the ordering contract is directly unit-testable.
 *
 * @param entityLogicalName Entity logical name (self-detected by the control class).
 * @param entityMetadata    Projected metadata from `retrieveEntityMetadata`.
 * @param formControls      Form controls IN FORM ORDER (see {@link readFormControlOrder}).
 */
export function buildHeaderFormMetadata(
  entityLogicalName: string,
  entityMetadata: EntityMetadata,
  formControls: ReadonlyArray<IFormControlProjection>
): HeaderFormMetadata {
  // Insertion order matters — see the file header. Form controls first.
  const attributes: Record<string, HeaderAttributeMetadata> = {};

  const project = (
    name: string,
    formControl: IFormControlProjection | undefined
  ): HeaderAttributeMetadata | undefined => {
    const meta = Object.prototype.hasOwnProperty.call(entityMetadata.attributes, name)
      ? entityMetadata.attributes[name]
      : undefined;
    // A control with neither metadata nor a form label carries no information.
    if (!meta && !formControl) return undefined;
    return {
      label: formControl?.label,
      displayName: meta?.displayName,
      attributeType: meta?.attributeType,
      format: meta?.format,
      requiredLevel: formControl?.requiredLevel,
    };
  };

  for (const control of formControls) {
    const projected = project(control.name, control);
    if (projected) attributes[control.name] = projected;
  }

  for (const name of Object.keys(entityMetadata.attributes)) {
    if (Object.prototype.hasOwnProperty.call(attributes, name)) continue;
    const projected = project(name, undefined);
    if (projected) attributes[name] = projected;
  }

  return {
    entityLogicalName,
    // `EntityMetadata` (the shared `IDataverseClient` projection) does not carry
    // the entity DISPLAY name, so this stays undefined and the resolver falls
    // back to humanizing the logical name. A maker who wants a nicer default
    // sets `title` in `layoutJson` or in the manifest `title` property, both of
    // which outrank this. Widening the shared projection for a fallback-of-a-
    // fallback is not worth the cross-consumer churn (CLAUDE.md 11).
    entityDisplayName: undefined,
    primaryIdAttribute: entityMetadata.primaryIdAttribute,
    primaryNameAttribute: entityMetadata.primaryNameAttribute,
    attributes,
  };
}

export interface IUseHeaderFormMetadataResult {
  /** Resolver input. `null` until entity metadata resolves (or on failure). */
  formMetadata: HeaderFormMetadata | null;
  /**
   * The raw projected entity metadata, kept alongside because the view needs
   * two fields `HeaderFormMetadata` deliberately does not carry: lookup
   * `targets` (FR-15a picker) and `optionSet` (FR-09 dropdown).
   */
  entityMetadata: EntityMetadata | null;
  loading: boolean;
  error: Error | null;
}

/**
 * Load + adapt the form/entity metadata for `entityLogicalName`.
 *
 * `retrieveEntityMetadata` is page-session cached by `XrmDataverseClient`
 * (task 020, FR-21 / NFR-01), so a second header on the same page — or a
 * re-mount on record navigation — costs no extra round trip.
 *
 * ══════════════════════════════════════════════════════════════════════════
 * WHY WE NAME THE ATTRIBUTES WE WANT
 * ══════════════════════════════════════════════════════════════════════════
 * `Xrm.Utility.getEntityMetadata`'s second argument is the documented way to
 * guarantee the `Attributes` collection comes back populated. Relying on the
 * one-argument form left the header with an EMPTY attribute map in UAT, so
 * every field derived the `text` renderer — and a lookup rendered as `text`
 * gets `$select`ed by its bare logical name, which 400s the whole read and
 * turns every cell into an em-dash.
 *
 * The union we request is exactly what the header can possibly bind:
 *  - every control on the form, in form order (tier-2 derivation, FR-04), and
 *  - every name the `layoutJson` refers to — which may legitimately include
 *    attributes NOT on the form, plus `summaryField`.
 *
 * When the union is empty (no form controls and no layout) we omit the
 * argument and let the platform decide, which is the old behaviour.
 *
 * @param entityLogicalName Entity logical name, self-detected by the control class.
 * @param configuredNames   Attribute names referenced by `layoutJson` — pass
 *                          `extractConfiguredAttributeNames(layoutJson)`.
 *
 * React 16/17-safe: `useState` / `useEffect` / `useMemo` only (ADR-022).
 */
export function useHeaderFormMetadata(
  entityLogicalName: string,
  configuredNames?: ReadonlyArray<string>
): IUseHeaderFormMetadataResult {
  const [entityMetadata, setEntityMetadata] = React.useState<EntityMetadata | null>(null);
  const [formControls, setFormControls] = React.useState<ReadonlyArray<IFormControlProjection> | null>(null);
  const [loading, setLoading] = React.useState<boolean>(false);
  const [error, setError] = React.useState<Error | null>(null);

  // Stable dep key: a fresh array literal with the same contents must not
  // re-trigger the fetch (mirrors `useRecordFieldValues`'s `fieldsKey`).
  const configuredKey = (configuredNames ?? []).join(',');

  React.useEffect(() => {
    if (!entityLogicalName) {
      setEntityMetadata(null);
      setFormControls(null);
      setLoading(false);
      setError(null);
      return;
    }

    let cancelled = false;
    setLoading(true);
    setError(null);

    // Zero-network, synchronous — read it before awaiting so the form order is
    // captured from the same render pass that triggered the fetch.
    const controls = readFormControlOrder();
    const requested = buildRequestedAttributeNames(controls, configuredKey === '' ? [] : configuredKey.split(','));

    const client = new XrmDataverseClient();
    const requestedNames = requested.length > 0 ? requested : undefined;

    client
      .retrieveEntityMetadata(entityLogicalName, requestedNames)
      .catch((err: unknown) => {
        // ── Named-request fallback (task 034) ────────────────────────────────
        // The requested list is a UNION of form controls, every name the
        // `layoutJson` mentions, and both summary-field candidates — so it can
        // legitimately contain a name the entity does not have (a maker typo in
        // `summaryField` is the expected case, and FR-17's negative path
        // requires the header to survive it).
        //
        // `Xrm.Utility.getEntityMetadata` is documented to FILTER on this
        // argument, so an unknown name should simply be absent from the result
        // — which is exactly the signal the existence gate reads. But if a host
        // ever rejects instead, the two-argument form would take the WHOLE
        // header down: no metadata → no resolved config → no fields, i.e. the
        // blank form NFR-10 forbids over a single mistyped character.
        //
        // Retrying unprojected costs one round trip in a path that is otherwise
        // broken anyway, and degrades to the platform's own default behaviour.
        // Same shape as the no-`$select` retry in `useRecordFieldValues`
        // (FAILURE-MODES G-12) — the read path already learned this lesson.
        if (!requestedNames) throw err;
        console.warn(
          `[RecordHeader] named-attribute metadata request failed for '${entityLogicalName}'; ` +
            'retrying without the attribute filter.',
          err
        );
        return client.retrieveEntityMetadata(entityLogicalName);
      })
      .then(
        metadata => {
          if (cancelled) return;
          setFormControls(controls);
          // Fill the Client-API payload's two gaps (DateTime `format`, lookup
          // `targets`) from the live form BEFORE anything downstream sees it,
          // so both the resolver and the view read one already-complete object.
          setEntityMetadata(applyFormControlHints(metadata, controls));
          setLoading(false);
        },
        (err: unknown) => {
          if (cancelled) return;
          const wrapped =
            err instanceof Error ? err : new Error(typeof err === 'string' ? err : 'retrieveEntityMetadata failed');
          setFormControls(controls);
          setEntityMetadata(null);
          setError(wrapped);
          setLoading(false);
        }
      );

    return () => {
      cancelled = true;
    };
  }, [entityLogicalName, configuredKey]);

  const formMetadata = React.useMemo<HeaderFormMetadata | null>(() => {
    if (!entityMetadata) return null;
    return buildHeaderFormMetadata(entityLogicalName, entityMetadata, formControls ?? []);
  }, [entityLogicalName, entityMetadata, formControls]);

  return { formMetadata, entityMetadata, loading, error };
}
