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
}

interface IFormControlLike {
  getName?(): string;
  getLabel?(): string;
  getAttribute?(): IFormAttributeLike | null | undefined;
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
    } catch {
      requiredLevel = undefined;
    }

    seen.add(name);
    projections.push({ name, label, requiredLevel });
  }

  return projections;
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
 * React 16/17-safe: `useState` / `useEffect` / `useMemo` only (ADR-022).
 */
export function useHeaderFormMetadata(entityLogicalName: string): IUseHeaderFormMetadataResult {
  const [entityMetadata, setEntityMetadata] = React.useState<EntityMetadata | null>(null);
  const [formControls, setFormControls] = React.useState<ReadonlyArray<IFormControlProjection> | null>(null);
  const [loading, setLoading] = React.useState<boolean>(false);
  const [error, setError] = React.useState<Error | null>(null);

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

    const client = new XrmDataverseClient();
    client.retrieveEntityMetadata(entityLogicalName).then(
      metadata => {
        if (cancelled) return;
        setFormControls(controls);
        setEntityMetadata(metadata);
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
  }, [entityLogicalName]);

  const formMetadata = React.useMemo<HeaderFormMetadata | null>(() => {
    if (!entityMetadata) return null;
    return buildHeaderFormMetadata(entityLogicalName, entityMetadata, formControls ?? []);
  }, [entityLogicalName, entityMetadata, formControls]);

  return { formMetadata, entityMetadata, loading, error };
}
