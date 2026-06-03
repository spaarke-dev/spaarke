/**
 * `<OptionSetMultiFilterChip />` — metadata-driven multi-select filter chip for
 * Picklist / Status / State Dataverse attributes.
 *
 * Options auto-derive from `entityMetadata.attributes[columnLogicalName].optionSet`
 * (see {@link OptionSetOption}). For Status / State attributes the option's `color`
 * (a hex string from Dataverse metadata) renders as a Fluent v9 `Badge` swatch
 * beside the label. The chip emits a `Set<number>` of selected option values
 * (NOT labels) so the consumer can build FetchXML `<filter type="or">` clauses
 * without re-resolving display strings.
 *
 * Multi-select uses the Fluent v9 `<Menu>` + `<MenuItemCheckbox>` controlled
 * pattern — one of the few native Fluent v9 controls that supports multi-check.
 * `checkedValues` is derived from the inbound `value: Set<number>` mapped to
 * `string[]` (Fluent's required shape); `onCheckedValueChange` converts back.
 *
 * **Portal theming (NFR-03)**: the `MenuPopover` renders through a React portal
 * and escapes the consumer's root `FluentProvider`. Per ADR-021 + the project's
 * portal-gotcha pattern, the popover surface is re-wrapped in a `FluentProvider`
 * with `applyStylesToPortals={true}` using the inbound `theme` prop (consumer
 * passes their root theme so the portal inherits dark mode / customer theme).
 *
 * **Color is DATA, not styling intent**: `option.color` is a hex string sourced
 * from Dataverse metadata — it is data the chip MUST render verbatim, NOT a
 * design-time color choice. This is the documented EXEMPTION from the
 * NO-RAW-HEX rule (which applies to design-time `tokens.ts`). The exemption
 * is recorded both here and on {@link OptionSetOption.color}.
 *
 * **Spec source**: projects/spaarke-datagrid-framework-r1/design.md §11.5
 * **FR**: FR-DG-07 (chip primitive), FR-DG-06 (metadata-driven derivation)
 * **ADR**: ADR-021 (Fluent v9, dark mode), ADR-022 (React-16-safe)
 * **NFR**: NFR-03 (applyStylesToPortals on portal-bearing surfaces)
 *
 * @see TagFilter.tsx — sibling multi-select chip pattern (different surface)
 * @see EntityMetadata, OptionSetOption in services/IDataverseClient.ts
 */

import * as React from 'react';
import {
  Badge,
  Button,
  FluentProvider,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItemCheckbox,
  Text,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
  webLightTheme,
  type Theme,
  type MenuCheckedValueChangeData,
  type MenuCheckedValueChangeEvent,
} from '@fluentui/react-components';
import { ChevronDownRegular } from '@fluentui/react-icons';

import { dataGridTokens } from '../tokens';
import type { EntityMetadata, OptionSetOption } from '../../../services/IDataverseClient';

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Stable `name` for the `MenuItemCheckbox` group — internal detail. */
const CHECKBOX_GROUP_NAME = 'optionset';

// ─────────────────────────────────────────────────────────────────────────────
// Public types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Props for {@link OptionSetMultiFilterChip}.
 */
export interface OptionSetMultiFilterChipProps {
  /**
   * Logical name of the column this chip filters (e.g. `sprk_status`,
   * `statuscode`, `statecode`). The chip looks up the attribute in
   * `entityMetadata.attributes[columnLogicalName]` to derive its options.
   */
  columnLogicalName: string;

  /**
   * Projected entity metadata containing the option-set definition. Typically
   * obtained from {@link IDataverseClient.retrieveEntityMetadata}. The chip
   * renders nothing (returns null) if the attribute is missing or has no
   * `optionSet` — graceful no-op for misconfigured columns.
   */
  entityMetadata: EntityMetadata;

  /** Currently-selected option values (numeric `OptionSetValue`). */
  value: Set<number>;

  /**
   * Fired when the user changes the selection. The emitted `Set` is a NEW
   * instance — the chip never mutates the inbound `value`.
   */
  onChange: (next: Set<number>) => void;

  /**
   * Display label shown on the trigger button when no options are selected
   * (e.g. "Status", "Priority"). Defaults to `columnLogicalName`.
   */
  label?: string;

  /**
   * Theme to apply inside the portal-rendered `MenuPopover` so dark mode +
   * customer-tenant themes survive the Portal hop (NFR-03). The consumer
   * should pass the same theme used at their root `FluentProvider`.
   * Defaults to `webLightTheme`.
   */
  theme?: Theme;

  /** Optional className appended to the root container. */
  className?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles (module scope per fluent-v9-component-authoring.md)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  /** Root inline container — keeps the chip compact in filter strips. */
  root: {
    display: 'inline-flex',
    alignItems: 'center',
    fontSize: dataGridTokens.filterChip.fontSize,
  },

  /** Trigger button — neutral subtle to match command-bar / filter-strip aesthetics. */
  trigger: {
    // Let Fluent's `Button` size to content; no explicit dimensions so the
    // chip adapts to host toolbar density.
  },

  /**
   * MenuItemCheckbox row content layout — the Fluent default already lays out
   * the check-icon slot. We render `<Badge>` + label as the row's content,
   * keeping them inline with a small gap.
   */
  menuRow: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },

  /**
   * Status-color swatch badge. Width fixed to a small circular dot so the
   * `option.color` field reads as a swatch (Dataverse model UI does the same).
   * `backgroundColor` is set inline via `style={{...}}` because the value is
   * DATA (per `OptionSetOption.color` contract).
   */
  swatch: {
    minWidth: '12px',
    height: '12px',
    ...shorthands.padding('0'),
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
  },

  /** Label text inside the menu row — uses the body token for consistency. */
  rowLabel: {
    color: tokens.colorNeutralForeground1,
  },

  /**
   * "+N more" suffix on the trigger when more than one option is selected.
   * Slightly de-emphasized so the first label reads as primary.
   */
  moreSuffix: {
    color: tokens.colorNeutralForeground2,
    marginLeft: tokens.spacingHorizontalXXS,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Convert `Set<number>` of selected values to the `Record<name, string[]>`
 * shape Fluent v9's `<Menu>` requires for `checkedValues`.
 */
function toCheckedValues(value: Set<number>): Record<string, string[]> {
  const arr: string[] = [];
  // `for…of` keeps Set iteration React-16-safe (no Array.from polyfill assumptions).
  value.forEach(v => {
    arr.push(String(v));
  });
  return { [CHECKBOX_GROUP_NAME]: arr };
}

/**
 * Convert Fluent's `string[]` `checkedItems` back into a fresh `Set<number>`.
 * Invalid (non-numeric) entries are skipped defensively — should not happen
 * given we control the option values, but guards against future custom items.
 */
function fromCheckedItems(checkedItems: string[]): Set<number> {
  const next = new Set<number>();
  for (const s of checkedItems) {
    const n = Number.parseInt(s, 10);
    if (!Number.isNaN(n)) next.add(n);
  }
  return next;
}

/**
 * Resolve the option-set definition for a column. Returns `[]` when the
 * attribute is missing or has no `optionSet`, so the chip can render nothing.
 */
function getOptions(entityMetadata: EntityMetadata, columnLogicalName: string): OptionSetOption[] {
  const attr = entityMetadata.attributes?.[columnLogicalName];
  return attr?.optionSet ?? [];
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * @see OptionSetMultiFilterChipProps
 */
export const OptionSetMultiFilterChip: React.FC<OptionSetMultiFilterChipProps> = ({
  columnLogicalName,
  entityMetadata,
  value,
  onChange,
  label,
  theme,
  className,
}) => {
  const styles = useStyles();

  // Resolve options once per metadata + column change; cheap but cached for
  // re-renders triggered by selection changes.
  const options = React.useMemo(
    () => getOptions(entityMetadata, columnLogicalName),
    [entityMetadata, columnLogicalName]
  );

  // Map value-Set → Fluent `Record<name, string[]>` on every render. The Set
  // is small (option counts are typically <20) so allocation cost is trivial.
  const checkedValues = React.useMemo(() => toCheckedValues(value), [value]);

  const handleCheckedValueChange = React.useCallback(
    (_event: MenuCheckedValueChangeEvent, data: MenuCheckedValueChangeData) => {
      if (data.name !== CHECKBOX_GROUP_NAME) return;
      onChange(fromCheckedItems(data.checkedItems));
    },
    [onChange]
  );

  // Trigger label: "Status" with no selection, "Active" with one, "Active +2"
  // with multiple. The label-of-first-selected pattern matches MDA filter chips.
  const triggerContent = React.useMemo(() => {
    const resolvedLabel = label ?? columnLogicalName;
    if (value.size === 0) {
      return <Text>{resolvedLabel}</Text>;
    }
    // Find labels for selected values in option order (stable, matches menu order).
    const selectedLabels: string[] = [];
    for (const opt of options) {
      if (value.has(opt.value)) {
        selectedLabels.push(opt.label);
        if (selectedLabels.length === value.size) break;
      }
    }
    // Fallback: if no options matched (e.g. stale value with options not loaded),
    // fall back to a count summary.
    if (selectedLabels.length === 0) {
      return <Text>{`${resolvedLabel} (${value.size})`}</Text>;
    }
    const first = selectedLabels[0];
    const extra = value.size - 1;
    if (extra <= 0) {
      return <Text>{first}</Text>;
    }
    return (
      <>
        <Text>{first}</Text>
        <span className={styles.moreSuffix}>{`+${extra} more`}</span>
      </>
    );
  }, [value, options, label, columnLogicalName, styles.moreSuffix]);

  // If no options are available for this column, render nothing — silent no-op
  // for misconfigured columns (better than a dead chip with empty menu).
  if (options.length === 0) {
    return null;
  }

  // Theme passed by the consumer (root FluentProvider's theme). Defaults to
  // webLightTheme — safe in test harnesses that don't pass a theme.
  const portalTheme: Theme = theme ?? webLightTheme;

  return (
    <div className={mergeClasses(styles.root, className)} data-testid="optionset-multi-filter-chip">
      <Menu checkedValues={checkedValues} onCheckedValueChange={handleCheckedValueChange}>
        <MenuTrigger disableButtonEnhancement>
          <Button
            className={styles.trigger}
            appearance="subtle"
            iconPosition="after"
            icon={<ChevronDownRegular aria-hidden="true" />}
            aria-label={
              value.size > 0 ? `${label ?? columnLogicalName} (${value.size} selected)` : (label ?? columnLogicalName)
            }
            data-testid="optionset-multi-filter-chip-trigger"
          >
            {triggerContent}
          </Button>
        </MenuTrigger>
        <MenuPopover>
          {/*
            Portal-theming fix (NFR-03 + ADR-021 dark-mode requirement).
            MenuPopover renders through a React Portal and escapes the
            consumer's root FluentProvider subtree — without this re-wrap,
            dark mode bleeds to light inside the popover. `applyStylesToPortals`
            is set explicitly even though Fluent's default is true, because
            inner providers may shadow that default.
          */}
          <FluentProvider applyStylesToPortals={true} theme={portalTheme}>
            <MenuList>
              {options.map(opt => (
                <MenuItemCheckbox
                  key={opt.value}
                  name={CHECKBOX_GROUP_NAME}
                  value={String(opt.value)}
                  data-testid={`optionset-multi-filter-chip-option-${opt.value}`}
                >
                  <span className={styles.menuRow}>
                    {/*
                      `opt.color` is DATA from Dataverse metadata (hex string),
                      NOT a design-time styling choice — see OptionSetOption
                      contract. Inline `style.backgroundColor` is the documented
                      exemption from the NO-RAW-HEX rule. When `color` is absent
                      (e.g. plain Picklist), the swatch renders as a neutral
                      outline-only dot via the `swatch` class border.
                    */}
                    <Badge
                      appearance="filled"
                      shape="circular"
                      size="extra-small"
                      className={styles.swatch}
                      style={opt.color ? { backgroundColor: opt.color } : undefined}
                      aria-hidden="true"
                    />
                    <span className={styles.rowLabel}>{opt.label}</span>
                  </span>
                </MenuItemCheckbox>
              ))}
            </MenuList>
          </FluentProvider>
        </MenuPopover>
      </Menu>
    </div>
  );
};

export default OptionSetMultiFilterChip;
