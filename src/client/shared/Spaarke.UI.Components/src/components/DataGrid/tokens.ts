/**
 * dataGridTokens — MDA Power Apps grid UI parity tokens for the DataGrid framework.
 *
 * Single source of truth for the new `<DataGrid />` component, its primitives
 * (filter chips, command bar, column header menu), and any consumer authoring
 * custom cell renderers. NO raw hex anywhere — every color flows through Fluent v9
 * `tokens.*` so light + dark + Windows High Contrast resolve automatically per
 * the active `<FluentProvider>` theme.
 *
 * The values codify the parity table in design.md §11.5.2. They were lifted from
 * `src/client/shared/Spaarke.Events.Components/src/components/GridSection/GridSection.tsx`
 * lines 195–260 (the EventsPage grid that targets MDA visual parity today).
 *
 * **Spec source**: projects/spaarke-datagrid-framework-r1/design.md §11.5.2
 * **ADR**: ADR-021 (Fluent v9, tokens-only)
 *
 * **Px values** (`'10px 12px'`, `'8px'`, `'12px'`, `'12px 16px'`) are raw because
 * Fluent v9 does not ship semantic tokens for these exact MDA-parity dimensions.
 * The NO-RAW-HEX rule applies to colors, not to spacing literals.
 */
import { tokens } from '@fluentui/react-components';

export const dataGridTokens = {
  /** Outer surface that hosts the grid (border + radius + elevation). */
  container: {
    background: tokens.colorNeutralBackground1,
    border: tokens.colorNeutralStroke1,
    borderRadius: tokens.borderRadiusMedium,
    shadow: tokens.shadow4,
  },

  /**
   * Sticky header row above the body. Power Apps OOB grid headers are
   * regular-weight (NOT semibold) with the muted `Foreground2` color and a
   * background flush with the grid surface — visually lighter than our cells.
   * See `projects/spaarke-datagrid-framework-r1/notes/testing-screenshots/oob-view-modal.jpg`.
   */
  header: {
    background: tokens.colorNeutralBackground1,
    foreground: tokens.colorNeutralForeground2,
    fontWeight: tokens.fontWeightRegular,
    borderBottom: tokens.colorNeutralStroke2,
  },

  /** Body cell typography + padding + bottom border. */
  cell: {
    fontFamily: "'Segoe UI', 'Segoe UI Web', Arial, sans-serif",
    /** MDA cells = 12px. `fontSizeBase200` resolves to 12px in Fluent v9. */
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightRegular,
    padding: '10px 12px',
    borderBottom: tokens.colorNeutralStroke2,
  },

  /** Row interaction states (hover / selected). */
  row: {
    hoverBackground: tokens.colorNeutralBackground1Hover,
    selectedBackground: tokens.colorNeutralBackground1Selected,
  },

  /** Primary-name "open record" link styling. */
  primaryNameLink: {
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
    /** No underline at rest. */
    textDecoration: 'none' as const,
    /** Underline on hover. */
    hoverTextDecoration: 'underline' as const,
  },

  /**
   * Command bar — sits flush inside the outer DataGrid card. The outer card
   * already provides the floating elevation (per
   * `.claude/patterns/ui/fluent-v9-host-visual-fit.md`), so the command bar
   * itself is borderless / shadowless / radiusless and inherits the page
   * background. This matches Power Apps OOB which has no nested chrome.
   */
  commandBar: {
    background: 'transparent',
    border: 'transparent',
    borderRadius: '0',
    shadow: 'none',
  },

  /** Filter chip strip layout. */
  filterChip: {
    /** Gap between adjacent chips. */
    gap: '8px',
    fontSize: tokens.fontSizeBase200,
  },

  /** Outer page layout consumed by Custom Page hosts wrapping the framework. */
  page: {
    padding: '12px 16px',
    /** Vertical gap between command bar and grid. */
    sectionGap: '12px',
  },
} as const;

/**
 * Convenience type for consumers who need to reference the token shape (e.g.,
 * Storybook stories asserting parity, test snapshots).
 */
export type DataGridTokens = typeof dataGridTokens;
