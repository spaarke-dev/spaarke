/**
 * configResolution — pure two-tier config resolution for the
 * `Spaarke.Records.RecordHeader` PCF.
 *
 * Implements design.md §5.3 / §6.3: resolves the effective header layout by
 * composing, in priority order:
 *   1. **Tier 1** — a valid `RecordHeaderConfiguration` v1.0 parsed from the
 *      control's `layoutJson` manifest property.
 *   2. **Tier 2** — defaults derived from form/entity metadata (primary name
 *      first at span 2, then up to four further non-system fields in form
 *      order), so the control renders usefully on a form with **no**
 *      `layoutJson` at all.
 *
 * **Spec source**: projects/record-header-and-notepad-r2/spec.md
 * **FR**: FR-02 (pure, fully resolved) · FR-03 (never throws) · FR-04 (tier-2
 * derived defaults) · FR-05 (span clamp) · NFR-10 (graceful degradation)
 *
 * This module is INTENTIONALLY pure — **no React, no I/O, no `Xrm`, no
 * network**. The single permitted side effect is `console.warn` on an
 * invalid/absent configuration (FR-03), emitted **at most once per call**. It
 * is the single point in the control that decides "what should the header look
 * like" and is therefore the easiest unit to exhaustively test.
 *
 * It deliberately mirrors the STRUCTURE and test approach of the proven
 * in-repo tiered resolver, `components/DataGrid/configResolution.ts`
 * (`resolveConfig` / `buildResolvedColumn` / `synthesizeColumnsFromMetadata` /
 * `rendererFromAttributeType`), without importing any of it — the two resolve
 * unrelated domain objects (grid columns/views vs. header field cells) and
 * DataGrid's helpers are module-private.
 *
 * **Why tier-SHAPED**: design.md §5.1 reversibility — a future config-record
 * tier (a `sprk_headerconfiguration` row, explicitly rejected for R2) can slot
 * in between tiers 1 and 2 without touching any renderer.
 *
 * ## The span clamp is load-bearing (FR-05)
 *
 * `FieldGrid` does **NOT** validate span: each field cell applies its own
 * `gridColumn: span N`, so a `span: 3` cell inside a `columns: 2` grid
 * silently creates an implicit third grid track and breaks the layout. This
 * resolver is the ONLY guard — `span = min(span, columns)` is applied to
 * every resolved field in BOTH tiers, after renderer-derived span defaulting.
 *
 * @see ../../types/RecordHeaderConfiguration — the v1.0 input schema + guard
 * @see ../DataGrid/configResolution — the canonical structural reference
 * @see FieldGrid — the consumer that does not validate span
 */

import { isValidRecordHeaderConfiguration } from '../../types/RecordHeaderConfiguration';
import type {
  RecordHeaderConfiguration,
  RecordHeaderFieldConfig,
  RecordHeaderFieldRenderer,
} from '../../types/RecordHeaderConfiguration';

// ─────────────────────────────────────────────────────────────────────────────
// Metadata input shape — the resolver's second parameter
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Dataverse requirement level for an attribute, in either of the two
 * vocabularies a caller may have on hand:
 *
 * - **Form-control vocabulary** (`Xrm.Page.getAttribute(n).getRequiredLevel()`):
 *   `'none' | 'required' | 'recommended'`
 * - **Metadata vocabulary** (`RequiredLevel.Value` from entity metadata):
 *   `'None' | 'SystemRequired' | 'ApplicationRequired' | 'Recommended'`
 *
 * Comparison is case-insensitive, and `required` / `systemrequired` /
 * `applicationrequired` are the only levels that render the `*` marker.
 * `recommended` renders a `+` in Dataverse, never a `*`, so it resolves to
 * `required: false` here. Widened to `string` so an unrecognised level from a
 * future platform version degrades to `false` instead of failing to compile.
 */
export type HeaderAttributeRequiredLevel =
  | 'none'
  | 'required'
  | 'recommended'
  | 'None'
  | 'SystemRequired'
  | 'ApplicationRequired'
  | 'Recommended'
  | string;

/**
 * One projected attribute, as the resolver needs it.
 *
 * Deliberately a **narrow structural projection**, not `EntityAttributeMetadata`
 * from `services/IDataverseClient` — keeping this module decoupled from
 * `IDataverseClient` is what lets the whole resolver be unit-tested with plain
 * object literals (no service, no mock, no `Xrm`). Task 033 adapts the live
 * form context + `retrieveEntityMetadata` result into this shape at the wiring
 * site; the field names are a superset-compatible subset of both sources.
 */
export interface HeaderAttributeMetadata {
  /**
   * The **form control's** label (`formContext.getControl(n).getLabel()`) —
   * design.md §5.4's primary, zero-network label source. Preferred over
   * {@link displayName}.
   */
  readonly label?: string;
  /**
   * User-localized metadata `DisplayName` (e.g. "Matter Number"). The fallback
   * when no form control exists for the attribute.
   */
  readonly displayName?: string;
  /**
   * Dataverse attribute type (`'String' | 'Memo' | 'Money' | 'DateTime' |
   * 'Lookup' | 'Picklist' | 'Status' | 'State' | 'Boolean' | 'Integer' |
   * 'Decimal' | 'Double' | 'BigInt' | …`). Drives renderer derivation when the
   * config does not override it.
   */
  readonly attributeType?: string;
  /** Sub-type discriminator — notably `'DateOnly'` vs `'DateAndTime'` for `DateTime`. */
  readonly format?: string;
  /** Requirement level backing the derived `*` marker. */
  readonly requiredLevel?: HeaderAttributeRequiredLevel;
}

/**
 * The resolver's metadata input — everything tier 2 needs to synthesize a
 * useful default layout, and everything tier 1's per-field merge needs to fill
 * the gaps a config leaves.
 *
 * ## Insertion order IS form order (caller contract)
 *
 * `attributes` is an **insertion-ordered** record: the resolver walks it with
 * `Object.keys()` and treats that order as **form order** when picking tier-2
 * default fields (FR-04). **Callers guarantee this ordering** — task 033 builds
 * the record by iterating the form's own control collection, so the first keys
 * inserted are the first controls on the form.
 *
 * (JS objects return integer-like keys first, in ascending numeric order,
 * before string keys in insertion order. Dataverse logical names are never
 * integer-like, so insertion order holds. This is the same assumption
 * `synthesizeColumnsFromMetadata` makes in DataGrid.)
 */
export interface HeaderFormMetadata {
  /** Entity logical name, e.g. `'sprk_project'`. Used for the title fallback. */
  readonly entityLogicalName: string;
  /** Entity display name, e.g. `'Project'`. The title fallback before humanization. */
  readonly entityDisplayName?: string;
  /**
   * Primary id attribute, e.g. `'sprk_projectid'`. **Excluded** from tier-2
   * derivation (it is a GUID, never useful in a header).
   */
  readonly primaryIdAttribute: string;
  /**
   * Primary name attribute — the first tier-2 field, at span 2.
   *
   * ⚠️ This is **not** derivable from a naming convention: `sprk_project`'s
   * primary name is `sprk_projectnumber`, NOT `sprk_projectname` (both columns
   * exist; live-verified 2026-08-24). It must come from metadata.
   */
  readonly primaryNameAttribute: string;
  /** Insertion-ordered map of logical name → projected attribute metadata. */
  readonly attributes: Record<string, HeaderAttributeMetadata>;
}

// ─────────────────────────────────────────────────────────────────────────────
// Resolved output — what the RecordHeader renderer actually consumes
// ─────────────────────────────────────────────────────────────────────────────

/**
 * A single, **fully resolved** header field cell (FR-02: "every output field
 * fully resolved — no optionals after merge").
 *
 * `name`, `label`, `span`, `renderer`, `readOnly` and `required` are all
 * concrete and non-optional — task 033 renders straight from these with no
 * further defaulting. `maxLines` is the one sanctioned optional: it is
 * renderer-specific to `textarea` and is passed through from config only when
 * the maker supplied it.
 */
export interface ResolvedHeaderField {
  /** Attribute logical name. Never empty. */
  readonly name: string;
  /** Display label. `config.label` → form-control label → metadata `displayName` → humanized logical name. */
  readonly label: string;
  /**
   * Grid column span, always `1..columns` — **clamped** (FR-05). Never
   * exceeds the resolved `columns`, in either tier.
   */
  readonly span: number;
  /** Renderer kind. `config.renderer` (when a valid union member) → derived from `attributeType`/`format`. */
  readonly renderer: RecordHeaderFieldRenderer;
  /** Whether inline editing is suppressed. `config.readOnly` → `false`. */
  readonly readOnly: boolean;
  /** Whether the `*` marker renders. `config.required` → derived from `requiredLevel` → `false`. */
  readonly required: boolean;
  /**
   * Max visible lines before a `textarea` scrolls. The ONLY optional on this
   * type — present iff the config supplied a positive number.
   */
  readonly maxLines?: number;
}

/**
 * The fully-resolved header configuration after two-tier merge — the analogue
 * of DataGrid's `ResolvedConfig`.
 *
 * `title`, `columns` and `fields` are concrete. `summaryField` is the one
 * `string | undefined` pass-through: the `RECORDSUMMARY_FIELD` default and the
 * metadata-existence gate that decides sparkle visibility (FR-17) are applied
 * by task 034 at the wiring site, **not here** — this module deliberately does
 * not import `toolbarLaunchDefaults`.
 */
export interface ResolvedHeaderConfig {
  /** Toolbar title. `config.title` → entity display name → humanized entity logical name. */
  readonly title: string;
  /** Effective grid column count. Always exactly `2` or `3` (FR-05's clamp ceiling). */
  readonly columns: 2 | 3;
  /**
   * Field backing the sparkle summary popover, passed through **as-is** from
   * config. `undefined` when the config omits it (or is absent entirely).
   * The key is always present on the object; its value may be `undefined`.
   */
  readonly summaryField: string | undefined;
  /** Ordered, fully-resolved field cells to render. Never empty unless the entity has no usable attributes. */
  readonly fields: ReadonlyArray<ResolvedHeaderField>;
}

// ─────────────────────────────────────────────────────────────────────────────
// Framework defaults
// ─────────────────────────────────────────────────────────────────────────────

/** Default grid column count when config omits it or supplies anything but `2` / `3` (design.md §5.2). */
const DEFAULT_COLUMNS: 2 | 3 = 3;

/** Span the primary name field gets in tier-2 derivation, before clamping (FR-04). */
const TIER2_PRIMARY_NAME_SPAN = 2;

/**
 * Maximum fields tier-2 derivation emits: the primary name plus up to FOUR
 * further non-system fields (FR-04).
 */
const TIER2_MAX_FIELDS = 5;

/**
 * The closed `RecordHeaderFieldRenderer` union as a runtime list, so a config
 * `renderer` value that is not a union member (a maker typo) falls back to
 * type-derivation instead of reaching the renderer layer as garbage.
 */
const HEADER_FIELD_RENDERERS: ReadonlyArray<string> = [
  'text',
  'textarea',
  'lookup',
  'optionset',
  'date',
  'datetime',
  'number',
  'currency',
  'boolean',
];

/** Prefix on every `console.warn` this module emits — greppable in a browser console. */
const WARN_PREFIX = '[RecordHeader] resolveHeaderConfig:';

// ─────────────────────────────────────────────────────────────────────────────
// Renderer derivation from attribute type — used when config does not override
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Derive a header renderer from a Dataverse attribute type + format.
 *
 * Mirrors the STRUCTURE of DataGrid's `rendererFromAttributeType` (a switch on
 * `attributeType`, with `format` disambiguating `DateTime`), but emits the
 * header renderer union (design.md §5.2) rather than the grid one.
 *
 * @param attributeType Dataverse attribute type. `undefined` ⇒ `'text'`.
 * @param format        Sub-type discriminator; only `'DateOnly'` is consulted.
 * @returns A member of the closed `RecordHeaderFieldRenderer` union. Never throws.
 */
function rendererFromAttributeType(
  attributeType: string | undefined,
  format: string | undefined
): RecordHeaderFieldRenderer {
  if (!attributeType) return 'text';
  switch (attributeType) {
    case 'Money':
      return 'currency';
    case 'DateTime':
      return format === 'DateOnly' ? 'date' : 'datetime';
    case 'Picklist':
    case 'Status':
    case 'State':
      return 'optionset';
    case 'Boolean':
      return 'boolean';
    case 'Lookup':
      return 'lookup';
    case 'Memo':
      return 'textarea';
    case 'Integer':
    case 'Decimal':
    case 'Double':
    case 'BigInt':
      return 'number';
    case 'String':
    default:
      return 'text';
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Local helpers (all module-private — mirrors of DataGrid internals that are
// NOT exported from that module and MUST NOT be imported from it)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * `'sprk_eventname'` → `'Event Name'`; `'name'` → `'Name'`.
 *
 * Best-effort label of last resort when neither a form-control label nor a
 * metadata `displayName` is available. Replicated verbatim from
 * `DataGrid/configResolution.ts` (module-private there — the same duplication
 * `DataGrid/filterChips/chipDiscovery.ts` already makes, and for the same
 * reason: importing DataGrid internals is not permitted).
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

/** Return `value` when it is a non-blank string, else `undefined`. */
function nonBlankString(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim().length > 0 ? value : undefined;
}

/**
 * Look an attribute up in the metadata map by **own property only**.
 *
 * `fields[].name` is maker-authored free text, so a plain `attributes[name]`
 * read would resolve inherited `Object.prototype` members — `{"name":
 * "toString"}` would hand back a function that is truthy but is not metadata.
 * Own-property semantics is also what "is this attribute present in metadata"
 * actually means.
 */
function lookupAttribute(
  attributes: Record<string, HeaderAttributeMetadata>,
  name: string
): HeaderAttributeMetadata | undefined {
  return Object.prototype.hasOwnProperty.call(attributes, name) ? attributes[name] : undefined;
}

/** Type guard for a config `renderer` value against the closed union. */
function isHeaderFieldRenderer(value: unknown): value is RecordHeaderFieldRenderer {
  return typeof value === 'string' && HEADER_FIELD_RENDERERS.indexOf(value) !== -1;
}

/**
 * Normalize the config `columns` value.
 *
 * `2` and `3` are honored; **anything else** — absent, `0`, `5`, `'3'`, `null`,
 * `NaN` — normalizes to `3`, with the rest of the config still applied
 * (design.md §5.2). Strict equality is deliberate: the string `'3'` is a maker
 * error, not a 3.
 */
function normalizeColumns(value: unknown): 2 | 3 {
  return value === 2 || value === 3 ? value : DEFAULT_COLUMNS;
}

/**
 * Derive the `*` marker from a requirement level, in either vocabulary.
 *
 * `recommended` is deliberately `false` — Dataverse renders `+` for
 * recommended, and only `*` for required.
 */
function requiredFromLevel(level: HeaderAttributeRequiredLevel | undefined): boolean {
  if (typeof level !== 'string') return false;
  switch (level.toLowerCase()) {
    case 'required':
    case 'systemrequired':
    case 'applicationrequired':
      return true;
    default:
      return false;
  }
}

/**
 * Resolve a field's span: default by renderer, then **clamp to `columns`**
 * (FR-05).
 *
 * - Unspecified (or non-numeric / `< 1` / non-finite) span defaults to
 *   `columns` for `textarea` and `1` for every other renderer (design.md §5.2).
 * - A fractional span floors (`2.7` ⇒ `2`).
 * - The clamp `min(span, columns)` is applied **unconditionally**, to the
 *   defaulted value as well as to an explicit one, in BOTH tiers. `FieldGrid`
 *   never validates span, so an unclamped `3` in a 2-column grid would add a
 *   phantom third grid track.
 */
function resolveSpan(configSpan: unknown, renderer: RecordHeaderFieldRenderer, columns: 2 | 3): number {
  const rendererDefault: number = renderer === 'textarea' ? columns : 1;
  const explicit =
    typeof configSpan === 'number' && Number.isFinite(configSpan) && configSpan >= 1
      ? Math.floor(configSpan)
      : undefined;
  const span = explicit ?? rendererDefault;
  // FR-05 — the only guard between config and an implicit extra grid track.
  return Math.min(span, columns);
}

/**
 * Whether a raw `fields[]` entry can actually be rendered.
 *
 * `isValidRecordHeaderConfiguration` is **deliberately shallow** — it accepts
 * `fields: [{}]` — so entry-level validation lands here (task 030 documents
 * this hand-off explicitly). An entry with no usable `name` cannot be bound to
 * a form attribute, so it is dropped rather than rendered as a nameless cell.
 */
function isUsableFieldConfig(entry: unknown): entry is RecordHeaderFieldConfig {
  if (entry === null || typeof entry !== 'object' || Array.isArray(entry)) return false;
  return nonBlankString((entry as { name?: unknown }).name) !== undefined;
}

/**
 * Normalize a caller-supplied metadata object so every downstream read is
 * total.
 *
 * The parameter is typed as required, but NFR-10 ("never throws, for any
 * input") is a runtime contract, and a caller that hands over a partially-built
 * metadata object during an early render must degrade, not crash.
 */
function safeMetadata(formMetadata: HeaderFormMetadata | null | undefined): HeaderFormMetadata {
  const source = (formMetadata ?? {}) as Partial<HeaderFormMetadata>;
  const attributes =
    source.attributes !== null && typeof source.attributes === 'object' && !Array.isArray(source.attributes)
      ? source.attributes
      : {};
  return {
    entityLogicalName: typeof source.entityLogicalName === 'string' ? source.entityLogicalName : '',
    entityDisplayName: nonBlankString(source.entityDisplayName),
    primaryIdAttribute: typeof source.primaryIdAttribute === 'string' ? source.primaryIdAttribute : '',
    primaryNameAttribute: typeof source.primaryNameAttribute === 'string' ? source.primaryNameAttribute : '',
    attributes,
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Per-field merge — the analogue of DataGrid's buildResolvedColumn
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Merge one field config against metadata into a fully-resolved field.
 *
 * Precedence for every property, mirroring `buildResolvedColumn`:
 * **config override ?? form-control/metadata value ?? derived fallback.**
 *
 * Used by BOTH tiers — tier 2 calls it with a synthetic `{ name }` (plus
 * `span: 2` for the primary name), so the clamp, the renderer derivation and
 * the label chain are identical in both paths by construction.
 */
function buildResolvedField(
  fieldConfig: RecordHeaderFieldConfig,
  formMetadata: HeaderFormMetadata,
  columns: 2 | 3
): ResolvedHeaderField {
  const name = fieldConfig.name;
  const attr = lookupAttribute(formMetadata.attributes, name);

  // renderer: config override (validated against the closed union) ?? derived
  const renderer: RecordHeaderFieldRenderer = isHeaderFieldRenderer(fieldConfig.renderer)
    ? fieldConfig.renderer
    : rendererFromAttributeType(attr?.attributeType, attr?.format);

  // label: config ?? form-control label ?? metadata displayName ?? humanized
  const label =
    nonBlankString(fieldConfig.label) ??
    nonBlankString(attr?.label) ??
    nonBlankString(attr?.displayName) ??
    humanizeLogicalName(name);

  // span: renderer-derived default when unspecified, then ALWAYS clamped (FR-05)
  const span = resolveSpan(fieldConfig.span, renderer, columns);

  // readOnly: config ?? false
  const readOnly = typeof fieldConfig.readOnly === 'boolean' ? fieldConfig.readOnly : false;

  // required: config ?? derived from requirement level ?? false
  const required =
    typeof fieldConfig.required === 'boolean' ? fieldConfig.required : requiredFromLevel(attr?.requiredLevel);

  // maxLines: the one sanctioned optional — passed through only when positive.
  const maxLines =
    typeof fieldConfig.maxLines === 'number' && Number.isFinite(fieldConfig.maxLines) && fieldConfig.maxLines > 0
      ? Math.floor(fieldConfig.maxLines)
      : undefined;

  return {
    name,
    label,
    span,
    renderer,
    readOnly,
    required,
    ...(maxLines !== undefined ? { maxLines } : {}),
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Tier 2 — metadata-derived defaults (FR-04)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Synthesize a useful default layout from metadata alone: the primary name
 * field first at span 2, then up to FOUR further non-system attributes in form
 * (insertion) order, for at most five fields total.
 *
 * The skip list is the **identical** set `synthesizeColumnsFromMetadata` uses
 * in `DataGrid/configResolution.ts` (lines 361-371) — the primary id attribute
 * plus the eight audit/state columns — verified against live source before
 * this file was written (FR-04 requires the SAME skip set). It is replicated
 * locally because DataGrid does not export it and this module MUST NOT import
 * DataGrid internals.
 *
 * This is what makes "drop it on a new form and it works" true, and it is why
 * the control can never render blank (NFR-10).
 */
function deriveDefaultFields(formMetadata: HeaderFormMetadata, columns: 2 | 3): ResolvedHeaderField[] {
  const result: ResolvedHeaderField[] = [];
  const seen = new Set<string>();

  // Primary name first, at span 2 (clamped like everything else).
  const primaryName = formMetadata.primaryNameAttribute;
  if (primaryName && lookupAttribute(formMetadata.attributes, primaryName) !== undefined) {
    result.push(buildResolvedField({ name: primaryName, span: TIER2_PRIMARY_NAME_SPAN }, formMetadata, columns));
    seen.add(primaryName);
  }

  // Skip the primary id + the audit/state columns. IDENTICAL to
  // DataGrid/configResolution.ts:361-371.
  const skipSet = new Set<string>([
    formMetadata.primaryIdAttribute,
    'createdon',
    'modifiedon',
    'createdby',
    'modifiedby',
    'ownerid',
    'statecode',
    'statuscode',
    'versionnumber',
  ]);

  for (const attrName of Object.keys(formMetadata.attributes)) {
    if (result.length >= TIER2_MAX_FIELDS) break;
    if (seen.has(attrName)) continue;
    if (skipSet.has(attrName)) continue;
    result.push(buildResolvedField({ name: attrName }, formMetadata, columns));
    seen.add(attrName);
  }

  return result;
}

// ─────────────────────────────────────────────────────────────────────────────
// Tier 1 — parse + guard the raw manifest string
// ─────────────────────────────────────────────────────────────────────────────

/** Outcome of parsing the raw `layoutJson` manifest value. */
interface ManifestParseOutcome {
  /** The valid v1.0 configuration, or `null` when tier 1 is unavailable. */
  readonly config: RecordHeaderConfiguration | null;
  /** Human-readable reason tier 1 was unavailable. Present iff `config === null`. */
  readonly reason?: string;
}

/**
 * Parse + guard the raw manifest string. **Never throws** — `JSON.parse` is
 * wrapped, and every non-string / blank / malformed / wrong-version input
 * returns a reason instead of an exception (FR-03).
 */
function parseHeaderManifest(manifestJson: string | null | undefined): ManifestParseOutcome {
  if (typeof manifestJson !== 'string' || manifestJson.trim().length === 0) {
    return { config: null, reason: "'layoutJson' is absent or empty — using metadata-derived defaults" };
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(manifestJson);
  } catch {
    return { config: null, reason: "'layoutJson' is not valid JSON — using metadata-derived defaults" };
  }

  if (!isValidRecordHeaderConfiguration(parsed)) {
    return {
      config: null,
      reason:
        "'layoutJson' failed the v1.0 discriminator check (_version must be '1.0' and fields must be an array) — using metadata-derived defaults",
    };
  }

  return { config: parsed };
}

// ─────────────────────────────────────────────────────────────────────────────
// The resolver
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Resolve the effective header configuration from the raw `layoutJson`
 * manifest value plus form/entity metadata.
 *
 * **Pure function** — no React, no I/O, no `Xrm`, no network. The single
 * permitted side effect is **at most one** `console.warn` per call (FR-03).
 *
 * **Never throws, for any input.** Malformed JSON, a wrong `_version`, an
 * absent property, an empty/garbage `fields` array, even a partially-built
 * metadata object all degrade to tier-2 derived defaults with one warning —
 * a bad maker paste must never blank a production form (NFR-10).
 *
 * **Every output field is fully resolved** — `name`, `label`, `span`,
 * `renderer`, `readOnly`, `required` are all concrete (FR-02). `span` is
 * always `min(span, columns)` (FR-05).
 *
 * Note that a valid config's `title`, `columns` and `summaryField` are still
 * honored even when its `fields` array yields nothing renderable — only the
 * field list falls through to tier 2 in that case.
 *
 * @param manifestJson The RAW `layoutJson` manifest value, exactly as the PCF
 *                     context hands it over (`string | null | undefined`). Not
 *                     pre-parsed — parsing is this function's job so the
 *                     failure path stays in one place.
 * @param formMetadata Projected form/entity metadata. `attributes` insertion
 *                     order is treated as form order (see
 *                     {@link HeaderFormMetadata}).
 * @returns A fully-resolved configuration. Never `null`, never throws.
 *
 * @example
 * ```ts
 * const resolved = resolveHeaderConfig(context.parameters.layoutJson.raw, {
 *   entityLogicalName: 'sprk_project',
 *   entityDisplayName: 'Project',
 *   primaryIdAttribute: 'sprk_projectid',
 *   primaryNameAttribute: 'sprk_projectnumber', // NOT sprk_projectname
 *   attributes: { sprk_projectnumber: { attributeType: 'String', label: 'Project Number' } },
 * });
 * // → { title: 'Project', columns: 3, summaryField: undefined, fields: [...] }
 * ```
 */
export function resolveHeaderConfig(
  manifestJson: string | null | undefined,
  formMetadata: HeaderFormMetadata
): ResolvedHeaderConfig {
  const metadata = safeMetadata(formMetadata);
  const { config, reason } = parseHeaderManifest(manifestJson);

  // At most ONE console.warn per call — accumulate the reason, emit at the end.
  let warning: string | undefined = reason;

  // Scalars resolve from the config whenever one parsed, independent of whether
  // its field list turned out to be usable.
  const columns = normalizeColumns(config?.columns);
  const title =
    nonBlankString(config?.title) ?? metadata.entityDisplayName ?? humanizeLogicalName(metadata.entityLogicalName);
  // summaryField passes through AS-IS. The RECORDSUMMARY_FIELD default and the
  // metadata-existence gate (FR-17) belong to task 034, not here.
  const summaryField: string | undefined =
    config !== null && typeof config.summaryField === 'string' ? config.summaryField : undefined;

  // Tier 1 field list, minus entries the shallow 030 guard let through.
  const usableFieldConfigs = config !== null ? config.fields.filter(isUsableFieldConfig) : [];

  let fields: ResolvedHeaderField[];
  if (config !== null && usableFieldConfigs.length > 0) {
    const dropped = config.fields.length - usableFieldConfigs.length;
    if (dropped > 0) {
      warning = `'layoutJson' had ${dropped} field entr${dropped === 1 ? 'y' : 'ies'} with no usable 'name' — ignoring ${dropped === 1 ? 'it' : 'them'}`;
    }
    fields = usableFieldConfigs.map(fieldConfig => buildResolvedField(fieldConfig, metadata, columns));
  } else {
    if (config !== null) {
      // Guard passed, but nothing renderable survived — NFR-10 says never blank.
      warning =
        config.fields.length === 0
          ? "'layoutJson' declares an empty 'fields' array — using metadata-derived defaults"
          : "'layoutJson' has no field entry with a usable 'name' — using metadata-derived defaults";
    }
    fields = deriveDefaultFields(metadata, columns);
  }

  if (warning !== undefined) {
    console.warn(`${WARN_PREFIX} ${warning} (entity '${metadata.entityLogicalName}').`);
  }

  return { title, columns, summaryField, fields };
}
