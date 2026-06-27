/**
 * Filter chip subsystem types — the runtime model that bridges
 * {@link FilterChipsConfig} (declarative configjson) and the Phase A primitive
 * chip components (`TextFilterChip`, `OptionSetMultiFilterChip`, etc.).
 *
 * A {@link ChipDescriptor} is what `chipDiscovery.discoverChips()` produces
 * after walking the configjson + entity metadata + resolved columns. A
 * {@link ChipState} is the user's currently-applied filter values, keyed by
 * attribute logical name. `chipFetchXml.augmentFetchXmlWithChips()` consumes
 * both to inject `<condition>` elements into the savedquery's FetchXML before
 * it hits `IDataverseClient.retrieveMultipleRecords`.
 *
 * **Spec source**: projects/spaarke-datagrid-framework-r1/design.md §6.4
 * (filter chip composition layer)
 *
 * **Phase A note**: the five primitive chips already exist under
 * `../chips/` — this module is the composition layer that wires them to
 * FetchXML. We map the configjson's `FilterChipKind` (`optionset-multi`,
 * `lookup-multi`, `date-range`, etc.) to a flatter {@link ChipKind} that
 * pairs 1:1 with a chip primitive component.
 *
 * @see ChipDescriptor — discovered chip metadata (label, options, etc.)
 * @see ChipState     — runtime applied-filter map
 * @see chipDiscovery — converts FilterChipsConfig + metadata → ChipDescriptor[]
 * @see chipFetchXml  — converts ChipDescriptor[] + ChipState → augmented FetchXML
 */

/**
 * Flat chip kind discriminator — one-to-one with a Phase A primitive component.
 *
 * Mapping from the configjson {@link FilterChipKind}:
 *
 * | configjson kind     | runtime kind | primitive component        |
 * |---------------------|--------------|----------------------------|
 * | `'text'`            | `'text'`     | `TextFilterChip`           |
 * | `'optionset-multi'` | `'optionset'`| `OptionSetMultiFilterChip` |
 * | `'lookup-multi'`    | `'lookup'`   | `LookupMultiFilterChip`    |
 * | `'date-range'`      | `'daterange'`| `DateRangeFilterChip`      |
 * | `'bool'`            | `'bool'`     | `BoolFilterChip`           |
 */
export type ChipKind = 'text' | 'optionset' | 'lookup' | 'daterange' | 'bool';

/**
 * Discovered chip — everything {@link FilterChipBar} needs to render the
 * matching primitive component.
 *
 * Produced by {@link discoverChips}; consumed by {@link FilterChipBar} and
 * {@link augmentFetchXmlWithChips}.
 */
export interface ChipDescriptor {
  /** Discriminator — selects the primitive component to render. */
  kind: ChipKind;
  /** Attribute logical name the chip filters on (e.g., `'sprk_status'`). */
  attribute: string;
  /** Display label (e.g., `'Status'`). */
  label: string;
  /**
   * OPTIONAL — option-set options for `kind === 'optionset'`. Sourced from
   * `EntityMetadata.attributes[attribute].optionSet`. Absent for non-option-set
   * chips.
   */
  options?: { value: number; label: string }[];
  /**
   * OPTIONAL — for `kind === 'lookup'`, the target entity logical name
   * (e.g., `'systemuser'`). Required by `LookupMultiFilterChip` at render
   * time.
   */
  lookupEntity?: string;
  /**
   * OPTIONAL — for `kind === 'lookup'`, the lookup target's primary-name
   * attribute (e.g., `'fullname'`). Required by `LookupMultiFilterChip`.
   */
  lookupNameField?: string;
}

/**
 * Per-chip applied filter value. The discriminator (`kind`) matches the
 * matching {@link ChipDescriptor.kind}. An entry being `undefined` in
 * {@link ChipState} means "no filter on this attribute".
 *
 * Note: the runtime payload here differs from the chip components'
 * prop-level value shapes (which use `Set<number>`, `Set<string>`,
 * `UtcDateBounds`, etc.) — this type is the serialization-friendly form
 * that `FilterChipBar` translates to/from the primitive prop shapes.
 */
/** Operator for a text/lookup filter — drives the FetchXML condition shape. */
export type TextFilterOperator = 'equals' | 'contains' | 'begins';

export type ChipValue =
  | { kind: 'text'; value: string; op?: TextFilterOperator }
  | { kind: 'optionset'; values: (string | number)[] }
  | { kind: 'lookup'; recordIds: string[]; op?: TextFilterOperator }
  | { kind: 'daterange'; from?: string; to?: string }
  | { kind: 'bool'; value: boolean };

/**
 * Map of attribute logical name → applied {@link ChipValue}. Entries are
 * present only for chips that currently have a filter set; absent / undefined
 * entries mean "no filter".
 *
 * Designed so `Object.keys(state).length === 0` and "every value is undefined"
 * BOTH mean "no chips active" — callers SHOULD prefer the latter for clarity
 * but the {@link augmentFetchXmlWithChips} helper handles both.
 */
export type ChipState = Record<string, ChipValue | undefined>;
