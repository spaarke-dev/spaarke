/**
 * TagFilter.ts
 *
 * Public types for the shared `TagFilter` Fluent v9 multi-select chip filter
 * component (FR-SC-01).
 *
 * The component is intentionally generic — it is not bound to any specific
 * Dataverse choice field (e.g., `sprk_documenttype`). Its first consumer is
 * the Semantic Search PCF Tags filter (FR-DOC-05), but the contract here is
 * field-type-agnostic so future consumers can reuse the same component for
 * any string-based choice field.
 *
 * Standards:
 *   - ADR-012  Shared component library — types co-located with component
 *   - ADR-021  Fluent v9 design system
 *   - ADR-022  React 16/17-safe (consumed by PCFs)
 *
 * @see ../components/TagFilter.tsx
 */

/**
 * Single selectable option in a `TagFilter`.
 *
 * `value` is the stable identifier passed back via `onChange` (e.g., the
 * option-set numeric code stringified, or a tag GUID). `label` is the
 * human-readable text shown in both the menu checkbox row and the active
 * chip below the trigger.
 */
export interface TagFilterOption {
  /** Stable identifier — what `onChange` returns in `selected[]`. */
  value: string;
  /** Human-readable text shown in the menu + chip. */
  label: string;
}

/**
 * Props for the shared `TagFilter` component.
 *
 * The component is fully controlled — it holds NO internal selection state.
 * Callers own `selected` and apply updates through `onChange`. This keeps the
 * component reusable across surfaces (PCF + Code Pages) that own their own
 * filter state.
 */
export interface TagFilterProps {
  /** All selectable options. Render order is preserved unless `sortAlphabetical` is true. */
  options: TagFilterOption[];

  /**
   * Currently selected values. The component is controlled — selection state
   * must be owned by the parent. Values not present in `options` are ignored
   * for display purposes but are still emitted unchanged when other values
   * change (so the parent retains them).
   */
  selected: string[];

  /**
   * Callback invoked with the next selection on every user interaction:
   * menu checkbox toggle, chip dismiss, or "Clear all". Always receives the
   * full next selection (not a delta).
   */
  onChange: (selected: string[]) => void;

  /**
   * Optional trigger button label. Defaults to `"Tags"`. Also used to derive
   * the trigger's `aria-label` (e.g., `"Tags (2 selected)"`).
   */
  label?: string;

  /**
   * When true, options are sorted alphabetically by `label` in the menu.
   * Defaults to `false` — caller-supplied option order is preserved per
   * FR-SC-01 acceptance criterion. Pass `true` to opt into alphabetic
   * sorting (e.g., for large unordered tag sets).
   */
  sortAlphabetical?: boolean;
}
