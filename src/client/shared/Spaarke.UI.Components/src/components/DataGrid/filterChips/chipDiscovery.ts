/**
 * chipDiscovery — pure transform from {@link FilterChipsConfig} +
 * {@link EntityMetadata} + {@link ResolvedColumn}[] into a flat list of
 * {@link ChipDescriptor}s ready for {@link FilterChipBar} to render.
 *
 * Implements the four `mode` branches from the configjson schema:
 *
 * - `'explicit'`   — use `filterChipsConfig.explicit[]` verbatim, hydrating
 *                    metadata-derived bits (options, displayName, etc.).
 * - `'allowlist'`  — for each name in `allowlist[]`, derive a chip from the
 *                    attribute's metadata type.
 * - `'denylist'`   — every chip-eligible visible column EXCEPT the listed
 *                    names.
 * - `'auto'`       — every chip-eligible visible column.
 *
 * "Chip-eligible" is decided by {@link deriveChipKindFromMetadata}: Picklist /
 * Status / State → optionset, Boolean → bool, DateTime → daterange,
 * String / Memo → text, Lookup → lookup. Other types (Money, Decimal,
 * Integer) return `undefined` and are quietly skipped.
 *
 * **Pure function — no React, no I/O, no Dataverse**. Easy to unit-test.
 *
 * **Spec source**: projects/spaarke-datagrid-framework-r1/design.md §6.4
 * **FR**: FR-DG-06 (metadata-driven chip auto-derivation), FR-DG-07
 *
 * @see types.ts        — {@link ChipDescriptor} shape
 * @see FilterChipBar   — consumer of the returned descriptors
 * @see chipFetchXml    — converts descriptors + state → FetchXML
 */

import type { ExplicitFilterChip, FilterChipKind, FilterChipsConfig } from '../../../types/DataGridConfiguration';
import type { EntityAttributeMetadata, EntityMetadata } from '../../../services/IDataverseClient';
import type { ResolvedColumn } from '../configResolution';

import type { ChipDescriptor, ChipKind } from './types';

// ─────────────────────────────────────────────────────────────────────────────
// Public API
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Discover the chips that should appear in the filter strip.
 *
 * @param filterChipsConfig The `filterChips` slice of the resolved configjson.
 *                          Pass `{ mode: 'auto' }` to derive every eligible
 *                          column.
 * @param columns           The resolved column list (visibility-aware).
 * @param metadata          Projected entity metadata.
 * @returns                 Ordered list of chip descriptors. Empty when no
 *                          attributes qualify; never throws.
 */
export function discoverChips(
  filterChipsConfig: FilterChipsConfig,
  columns: readonly ResolvedColumn[],
  metadata: EntityMetadata
): ChipDescriptor[] {
  switch (filterChipsConfig.mode) {
    case 'explicit':
      return discoverExplicit(filterChipsConfig.explicit ?? [], metadata);
    case 'allowlist':
      return discoverFromAttributeNames(filterChipsConfig.allowlist ?? [], metadata, columns);
    case 'denylist':
      return discoverDenylist(filterChipsConfig.denylist ?? [], metadata, columns);
    case 'auto':
    default:
      return discoverAuto(metadata, columns);
  }
}

/**
 * Map a Dataverse attribute's metadata type to one of the five flat
 * {@link ChipKind} discriminators — or `undefined` when the attribute is not
 * filter-chip-eligible (e.g., Money, Decimal, Integer with no formatter).
 *
 * The framework relies on this being the single decision point for "what
 * primitive renders for THIS attribute" so the four discovery branches all
 * produce consistent shapes.
 */
export function deriveChipKindFromMetadata(attr: EntityAttributeMetadata | undefined): ChipKind | undefined {
  if (!attr) return undefined;
  switch (attr.attributeType) {
    case 'Picklist':
    case 'Status':
    case 'State':
      return 'optionset';
    case 'Boolean':
      return 'bool';
    case 'DateTime':
      return 'daterange';
    case 'String':
      return 'text';
    case 'Lookup':
      return 'lookup';
    default:
      // 'Memo' arrives via the open-ended `string` branch of MetadataAttributeType.
      if (attr.attributeType === 'Memo') return 'text';
      return undefined;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Mode branches
// ─────────────────────────────────────────────────────────────────────────────

function discoverExplicit(explicit: readonly ExplicitFilterChip[], metadata: EntityMetadata): ChipDescriptor[] {
  const result: ChipDescriptor[] = [];
  for (const ex of explicit) {
    const kind = mapConfigKind(ex.kind);
    if (!kind) continue;
    const attrMeta = metadata.attributes[ex.field];
    // For lookup chips we need a target entity. If `valueSource` is missing
    // AND metadata doesn't carry the target either, skip the chip — there's
    // no way to render it without knowing the lookup target.
    // TODO(R2): support metadata-derived lookup target (Xrm metadata exposes
    //   `targets` on lookup attributes — not currently projected onto
    //   `EntityAttributeMetadata`).
    if (kind === 'lookup') {
      const lookup = resolveLookupTarget(ex);
      if (!lookup) continue;
      result.push({
        kind,
        attribute: ex.field,
        label: resolveLabel(ex.label, attrMeta, ex.field),
        lookupEntity: lookup.entity,
        lookupNameField: lookup.nameField,
      });
      continue;
    }
    result.push({
      kind,
      attribute: ex.field,
      label: resolveLabel(ex.label, attrMeta, ex.field),
      options: kind === 'optionset' ? attrMeta?.optionSet?.map(toOption) : undefined,
    });
  }
  return result;
}

function discoverFromAttributeNames(
  names: readonly string[],
  metadata: EntityMetadata,
  columns: readonly ResolvedColumn[]
): ChipDescriptor[] {
  // allowlist semantics: names take precedence over column visibility — if
  // an admin explicitly named an attribute we honour it whether or not it
  // appears in `columns`. Columns are still consulted for label fallback.
  const labelOverrides = new Map<string, string>();
  for (const col of columns) {
    if (!col.hidden) labelOverrides.set(col.name, col.label);
  }
  const result: ChipDescriptor[] = [];
  for (const name of names) {
    const desc = buildAutoDescriptor(name, metadata, labelOverrides.get(name));
    if (desc) result.push(desc);
  }
  return result;
}

function discoverDenylist(
  denylist: readonly string[],
  metadata: EntityMetadata,
  columns: readonly ResolvedColumn[]
): ChipDescriptor[] {
  const banned = new Set(denylist);
  const result: ChipDescriptor[] = [];
  for (const col of columns) {
    if (col.hidden) continue;
    if (banned.has(col.name)) continue;
    const desc = buildAutoDescriptor(col.name, metadata, col.label);
    if (desc) result.push(desc);
  }
  return result;
}

function discoverAuto(metadata: EntityMetadata, columns: readonly ResolvedColumn[]): ChipDescriptor[] {
  const result: ChipDescriptor[] = [];
  for (const col of columns) {
    if (col.hidden) continue;
    const desc = buildAutoDescriptor(col.name, metadata, col.label);
    if (desc) result.push(desc);
  }
  return result;
}

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Build a descriptor for an attribute by reading its metadata type. Returns
 * `undefined` when the attribute is missing or not filter-eligible.
 */
function buildAutoDescriptor(
  attributeName: string,
  metadata: EntityMetadata,
  labelOverride: string | undefined
): ChipDescriptor | undefined {
  const attrMeta = metadata.attributes[attributeName];
  const kind = deriveChipKindFromMetadata(attrMeta);
  if (!kind) return undefined;
  if (kind === 'lookup') {
    // No metadata-derived lookup target available in the projected
    // EntityAttributeMetadata shape (see TODO above). Skip silently.
    return undefined;
  }
  return {
    kind,
    attribute: attributeName,
    label: labelOverride ?? resolveLabel(undefined, attrMeta, attributeName),
    options: kind === 'optionset' ? attrMeta?.optionSet?.map(toOption) : undefined,
  };
}

function mapConfigKind(kind: FilterChipKind): ChipKind | undefined {
  switch (kind) {
    case 'text':
      return 'text';
    case 'optionset-multi':
      return 'optionset';
    case 'lookup-multi':
      return 'lookup';
    case 'date-range':
      return 'daterange';
    case 'bool':
      return 'bool';
    default:
      return undefined;
  }
}

function resolveLookupTarget(ex: ExplicitFilterChip): { entity: string; nameField: string } | undefined {
  if (!ex.valueSource) return undefined;
  if (ex.valueSource.type === 'systemusers') {
    return { entity: 'systemuser', nameField: 'fullname' };
  }
  return { entity: ex.valueSource.entity, nameField: ex.valueSource.nameField };
}

function resolveLabel(
  explicitLabel: string | undefined,
  attrMeta: EntityAttributeMetadata | undefined,
  attributeName: string
): string {
  if (explicitLabel && explicitLabel.length > 0) return explicitLabel;
  if (attrMeta?.displayName && attrMeta.displayName.length > 0) return attrMeta.displayName;
  return humanizeLogicalName(attributeName);
}

function toOption(o: { value: number; label: string }): { value: number; label: string } {
  return { value: o.value, label: o.label };
}

/**
 * Small humanizer for logical names when metadata `displayName` is unavailable.
 * Duplicated from `configResolution.ts#humanizeLogicalName` to keep this
 * module self-contained (no cross-import beyond types).
 */
function humanizeLogicalName(logicalName: string): string {
  const stripped = logicalName.replace(/^[a-z]+_/, '');
  return stripped
    .replace(/([A-Z])/g, ' $1')
    .replace(/_/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .split(' ')
    .map(w => (w.length > 0 ? w[0].toUpperCase() + w.slice(1) : w))
    .join(' ');
}
