/**
 * RecordHeaderConfiguration — the v1.0 schema for the `Spaarke.Records.RecordHeader`
 * PCF's `layoutJson` manifest property.
 *
 * Discriminator: `_version: '1.0'`.
 *
 * A maker pastes this JSON directly into the manifest's `layoutJson` property (FR-01).
 * Because the JSON is maker-authored free text, a malformed paste must NEVER throw and
 * NEVER blank a production form (FR-03, NFR-10) — see {@link isValidRecordHeaderConfiguration}.
 *
 * **Two-tier resolution** (design.md §5.3): when `layoutJson` is absent, unparseable,
 * or fails the shallow guard below, the resolver (task 031, `resolveHeaderConfig`)
 * derives a default layout from entity metadata instead of rendering blank.
 *
 * **Spec source**: projects/record-header-and-notepad-r2/design.md §5.2 (FR-01, FR-03)
 *
 * @see isValidRecordHeaderConfiguration — the shallow, non-throwing runtime guard
 * @see DataGridConfiguration — the sibling v1.0 schema this mirrors in STRUCTURE only.
 *      The two describe unrelated surfaces (grid columns/views vs. header fields) and
 *      have independent discriminator contracts — do not conflate them.
 */

// ─────────────────────────────────────────────────────────────────────────────
// Per-field configuration
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Renderer kind for a single header field. When omitted, the renderer is derived
 * from the field's Dataverse attribute type (design.md §5.2 `fields[].renderer`).
 */
export type RecordHeaderFieldRenderer =
  | 'text'
  | 'textarea'
  | 'lookup'
  | 'optionset'
  | 'date'
  | 'datetime'
  | 'number'
  | 'currency'
  | 'boolean';

/**
 * One field cell in the header's `FieldGrid`.
 *
 * Closed set per spec FR-01 / design.md §5.2 — do NOT add keys beyond these eight.
 * In particular, there is intentionally no `lookup` escape hatch: lookup targets are
 * resolved from entity metadata at render time (design.md §5.4), not authored in
 * config (design decision, closed 2026-08-24).
 */
export interface RecordHeaderFieldConfig {
  /**
   * Logical name of the attribute. For lookups, the **lookup attribute** name
   * (e.g. `sprk_mattertype`), not the `_sprk_mattertype_value` OData alias.
   */
  readonly name: string;
  /**
   * Grid column span, `1`–`3`. Default: derived from renderer — `textarea` spans
   * the full `columns` width, everything else spans `1` (design.md §5.2).
   * **Not validated here**: the resolver (task 031) clamps an over-wide span to
   * the effective `columns` value; `FieldGrid` itself applies whatever span it
   * is given without checking.
   */
  readonly span?: number;
  /** Override the auto-derived display label. Default: the form control's label. */
  readonly label?: string;
  /** Override the renderer auto-derived from the attribute's Dataverse type. */
  readonly renderer?: RecordHeaderFieldRenderer;
  /** Suppress inline editing for this cell. Default `false`. */
  readonly readOnly?: boolean;
  /**
   * Renders the `*` required marker. Default: derived from the attribute's
   * requirement level. Per design.md §6.1, only the `text` renderer currently
   * renders this marker in R2 — a known, accepted UX gap, not this file's concern.
   */
  readonly required?: boolean;
  /** Max visible lines for a `textarea` renderer before it scrolls. */
  readonly maxLines?: number;
}

// ─────────────────────────────────────────────────────────────────────────────
// Top-level configuration
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Top-level v1.0 configuration consumed by the `Spaarke.Records.RecordHeader` PCF.
 *
 * Authored as the body of the control's `layoutJson` manifest property and parsed
 * at render time. Invalid configurations DO NOT throw — see
 * {@link isValidRecordHeaderConfiguration}.
 *
 * Closed set per spec FR-01 / design.md §5.2 — do NOT add keys beyond these five.
 */
export interface RecordHeaderConfiguration {
  readonly _version: '1.0';
  /** Toolbar title. Default: entity display name from metadata. */
  readonly title?: string;
  /**
   * Grid column count, `2` or `3`. Default `3`. **The default is applied by the
   * resolver (task 031, `resolveHeaderConfig`), not by this types file** — this
   * property stays optional so a config that omits it still type-checks.
   */
  readonly columns?: 2 | 3;
  /**
   * Field backing the sparkle summary popover. Sparkle shows whenever the named
   * attribute **exists in metadata**, even with zero populated records (popover
   * renders "No summary yet"). Omitted, or naming a non-existent attribute →
   * sparkle hidden.
   */
  readonly summaryField?: string;
  /** Ordered list of fields rendered in the header's `FieldGrid`. */
  readonly fields: ReadonlyArray<RecordHeaderFieldConfig>;
}

// ─────────────────────────────────────────────────────────────────────────────
// Runtime validation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Runtime guard for {@link RecordHeaderConfiguration} v1.0.
 *
 * **Does NOT throw** on invalid input — returns `false`. Callers (the resolver in
 * task 031) react by logging via `console.warn` and falling back to metadata-derived
 * defaults (design.md §5.3). This satisfies spec FR-03 / NFR-10: a malformed maker
 * paste into `layoutJson` must never throw and never blank a production form.
 *
 * The guard validates ONLY the discriminators — a non-null object, `_version ===
 * '1.0'`, and `Array.isArray(fields)` — intentionally shallow, mirroring
 * {@link isValidDataGridConfiguration}. Deep shape errors (e.g. a field entry
 * missing `name`) surface as undefined property reads downstream and degrade
 * gracefully rather than being rejected here.
 *
 * @param value - Anything (typically `JSON.parse(layoutJson)`).
 * @returns `true` if `value` matches the v1.0 discriminators, `false` otherwise.
 */
export function isValidRecordHeaderConfiguration(value: unknown): value is RecordHeaderConfiguration {
  if (value === null || typeof value !== 'object') {
    return false;
  }
  const obj = value as Record<string, unknown>;
  if (obj._version !== '1.0') {
    return false;
  }
  if (!Array.isArray(obj.fields)) {
    return false;
  }
  return true;
}
