/**
 * TagFilter.tsx
 *
 * Generic Fluent v9 multi-select chip filter (FR-SC-01).
 *
 * Renders a trigger button (label + count badge) that opens a Fluent v9
 * `Menu` populated with `MenuItemCheckbox` rows — one per option. When
 * `selected.length >= 1` an active-tag chip row appears below the trigger,
 * showing one dismissible `Tag` per selected value plus a trailing "Clear
 * all" subtle button. The component is fully controlled — it holds NO
 * internal selection state.
 *
 * NOT bound to any specific Dataverse choice field. The first consumer is
 * the Semantic Search PCF Tags filter (FR-DOC-05, `sprk_documenttype`),
 * but the component contract is intentionally generic so any string-based
 * choice field can reuse it.
 *
 * Standards:
 *   - ADR-012  Shared component library — context-agnostic, no solution imports
 *   - ADR-021  Fluent v9 design system — semantic tokens only, dark-mode safe
 *   - ADR-022  React 16/17-safe — no React 18+ exclusive APIs
 *
 * Patterns applied (per `/fluent-v9-component` skill):
 *   - `fluent-v9-component-authoring.md`  makeStyles at module scope, tokens-only colors
 *   - `fluent-v9-theming.md`              FluentProvider is owned by the consumer, not us
 *   - `fluent-v9-portal-gotcha.md`        Menu renders through a Portal; consumers
 *                                          must wrap their app in a FluentProvider with
 *                                          `applyStylesToPortals` (Fluent v9 default IS
 *                                          true) — we do NOT re-wrap here because that
 *                                          would create a nested theme and break the
 *                                          consumer's customer-tenant theme.
 *   - `fluent-v9-react-version-boundaries.md`  No React 18+ APIs (useId from Fluent v9,
 *                                               not from React 18).
 *
 * @see ../types/TagFilter.ts
 */

import * as React from 'react';
import {
  Button,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItemCheckbox,
  Tag,
  TagGroup,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import { ChevronDownRegular, DismissRegular } from '@fluentui/react-icons';

import type { TagFilterOption, TagFilterProps } from '../types/TagFilter';

// Re-export the public types so callers can import them directly from
// '@spaarke/ui-components' once the barrel is updated (deferred to task 012).
export type { TagFilterOption, TagFilterProps };

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Stable `name` for the MenuItemCheckbox group — internal implementation detail. */
const CHECKBOX_GROUP_NAME = 'tagFilter';

/** Default trigger label when caller does not supply one. */
const DEFAULT_LABEL = 'Tags';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * Root container — vertical stack of trigger button + (optional) chip row.
   * `display: inline-flex` keeps the filter compact in toolbar contexts.
   */
  root: {
    display: 'inline-flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    gap: tokens.spacingVerticalXS,
  },

  /**
   * Trigger button — neutral subtle by default; uses `colorBrandForeground1`
   * for the count badge to draw attention when filters are active. The
   * button reuses Fluent's own focus / hover styling — we add nothing custom
   * here so theme overrides and dark-mode parity work automatically.
   */
  trigger: {
    // Allow the trigger to size naturally. No explicit dimensions so it
    // adapts to toolbar height in any consumer surface.
  },

  /**
   * Count badge — small pill rendered inline inside the trigger label
   * (e.g., `Tags (2)`). Uses brand tokens so it's visually distinct in
   * both light and dark themes.
   */
  countBadge: {
    marginLeft: tokens.spacingHorizontalXS,
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },

  /**
   * Active-tag chip row — visible only when `selected.length >= 1`. Wraps
   * onto multiple lines for long selections. `gap` controls both row and
   * column spacing per Fluent v9 flexbox conventions.
   */
  chipRow: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    rowGap: tokens.spacingVerticalXS,
  },

  /**
   * "Clear all" button — subtle appearance so it doesn't compete with the
   * chips themselves. Uses Fluent v9 link-foreground for the affordance.
   */
  clearAll: {
    color: tokens.colorBrandForegroundLink,
  },

  /**
   * Menu popover surface — explicit min/max widths so option labels don't
   * truncate awkwardly. Token-based padding only.
   */
  menuList: {
    minWidth: '200px',
    maxWidth: '320px',
    ...shorthands.padding(tokens.spacingVerticalXS, 0),
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Reusable Fluent v9 multi-select chip filter. See module-level JSDoc for
 * design rationale and the type definitions in `../types/TagFilter.ts` for
 * the prop contract.
 */
export const TagFilter: React.FC<TagFilterProps> = ({
  options,
  selected,
  onChange,
  label,
  sortAlphabetical = false,
}) => {
  const styles = useStyles();

  const resolvedLabel = label ?? DEFAULT_LABEL;

  // ── Display options ──────────────────────────────────────────────────────
  // When `sortAlphabetical` is true, sort a SHALLOW COPY by label using a
  // locale-aware comparator — never mutate the caller's array. When false,
  // preserve caller-supplied order verbatim (FR-SC-01 acceptance criterion).
  const displayOptions = React.useMemo<TagFilterOption[]>(() => {
    if (!sortAlphabetical) {
      return options;
    }
    return [...options].sort((a, b) => a.label.localeCompare(b.label));
  }, [options, sortAlphabetical]);

  // ── Fast lookup for chip labels ──────────────────────────────────────────
  // Map value → label so the chip row can render the human-readable label
  // even when option order differs from selection order. Values present in
  // `selected` but missing from `options` fall back to the raw value.
  const labelByValue = React.useMemo<Record<string, string>>(() => {
    const map: Record<string, string> = {};
    for (const opt of options) {
      map[opt.value] = opt.label;
    }
    return map;
  }, [options]);

  // ── Fluent v9 Menu checked-state binding ─────────────────────────────────
  // Menu expects `checkedValues` keyed by the `name` of each checkbox group.
  // We always wire `tagFilter` → current selection.
  const checkedValues = React.useMemo(() => ({ [CHECKBOX_GROUP_NAME]: selected }), [selected]);

  // ── Handlers ─────────────────────────────────────────────────────────────
  const handleCheckedValueChange = React.useCallback(
    (_event: unknown, data: { name: string; checkedItems: string[] }) => {
      if (data.name === CHECKBOX_GROUP_NAME) {
        onChange(data.checkedItems);
      }
    },
    [onChange]
  );

  const handleRemoveTag = React.useCallback(
    (valueToRemove: string) => {
      onChange(selected.filter(v => v !== valueToRemove));
    },
    [onChange, selected]
  );

  const handleClearAll = React.useCallback(() => {
    onChange([]);
  }, [onChange]);

  /**
   * Defensive `stopPropagation` on the trigger button: TagFilter is intended
   * for command-bar contexts (no row-click handler in scope) but if a
   * future consumer mounts it inside a clickable row container, this
   * prevents the menu trigger from bubbling up and accidentally selecting
   * the row. The menu's own click handling is unaffected.
   */
  const handleTriggerClick = React.useCallback((e: React.MouseEvent): void => {
    e.stopPropagation();
  }, []);

  const selectedCount = selected.length;
  const showChipRow = selectedCount > 0;

  // ── Render ───────────────────────────────────────────────────────────────
  return (
    <div className={styles.root} data-testid="tag-filter">
      {/* Trigger + Menu — Fluent v9 Menu renders through a Portal; the
          consumer's <FluentProvider> at the surface root MUST have
          `applyStylesToPortals` (default IS true in Fluent v9) for the
          popover to inherit the customer-tenant theme. We deliberately
          DO NOT re-wrap the MenuPopover in a nested FluentProvider —
          that would shadow the customer theme. See
          `.claude/patterns/ui/fluent-v9-portal-gotcha.md`. */}
      <Menu checkedValues={checkedValues} onCheckedValueChange={handleCheckedValueChange}>
        <MenuTrigger disableButtonEnhancement>
          <Button
            className={styles.trigger}
            appearance="subtle"
            iconPosition="after"
            icon={<ChevronDownRegular aria-hidden="true" />}
            onClick={handleTriggerClick}
            aria-label={selectedCount > 0 ? `${resolvedLabel} (${selectedCount} selected)` : resolvedLabel}
            data-testid="tag-filter-trigger"
          >
            {resolvedLabel}
            {selectedCount > 0 ? (
              <span className={styles.countBadge} aria-hidden="true">
                ({selectedCount})
              </span>
            ) : null}
          </Button>
        </MenuTrigger>
        <MenuPopover>
          <MenuList className={styles.menuList}>
            {displayOptions.map(opt => (
              <MenuItemCheckbox
                key={opt.value}
                name={CHECKBOX_GROUP_NAME}
                value={opt.value}
                data-testid={`tag-filter-option-${opt.value}`}
              >
                {opt.label}
              </MenuItemCheckbox>
            ))}
          </MenuList>
        </MenuPopover>
      </Menu>

      {/* Active-tag chip row — rendered ONLY when there is at least one
          selection. Uses Fluent v9 TagGroup + dismissible Tag pattern; the
          dismiss icon is wired via `onClick` (matches the convention in
          SprkChatContextSelector.tsx for cross-component consistency). */}
      {showChipRow ? (
        <TagGroup
          className={styles.chipRow}
          role="list"
          aria-label={`Active ${resolvedLabel} filters`}
          data-testid="tag-filter-active-tags"
        >
          {selected.map(value => {
            const chipLabel = labelByValue[value] ?? value;
            return (
              <Tag
                key={value}
                shape="rounded"
                size="small"
                appearance="brand"
                dismissible
                dismissIcon={<DismissRegular aria-label={`Remove ${chipLabel}`} />}
                value={value}
                onClick={() => handleRemoveTag(value)}
                data-testid={`tag-filter-active-tag-${value}`}
              >
                {chipLabel}
              </Tag>
            );
          })}
          <Button
            className={styles.clearAll}
            appearance="subtle"
            size="small"
            onClick={handleClearAll}
            aria-label={`Clear all ${resolvedLabel} filters`}
            data-testid="tag-filter-clear-all"
          >
            Clear all
          </Button>
        </TagGroup>
      ) : null}
    </div>
  );
};
