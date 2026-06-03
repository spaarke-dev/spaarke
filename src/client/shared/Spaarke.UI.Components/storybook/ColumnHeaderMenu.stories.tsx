/**
 * ColumnHeaderMenu / ColumnFilterHeader — Storybook stories.
 *
 * Four stories covering both surfaces in light + dark:
 *   1. ColumnHeaderMenu — Default (light)
 *   2. ColumnHeaderMenu — Dark (NFR-03 portal verification)
 *   3. ColumnFilterHeader — Default (light)
 *   4. ColumnFilterHeader — Dark (NFR-03 portal verification)
 *
 * Each story is wrapped in an OUTER `<FluentProvider applyStylesToPortals theme={...}>`
 * AND passes the same theme into the component's `theme` prop, which the components use
 * for their INNER popover-surface re-wrap. This is the load-bearing pattern that closes
 * the Fluent v9 portal-theming gotcha — without the inner re-wrap, popovers render with
 * light-mode styles when the host is in dark mode.
 *
 * **Storybook config**: this project has no `.storybook/` configuration wired yet; the
 * file lives OUTSIDE `src/` so the TypeScript library build (`tsc`) does not pick it up.
 * When Storybook is wired in a later task, `stories: ['../storybook/**\/*.stories.@(ts|tsx)']`
 * picks it up automatically.
 *
 * @see projects/spaarke-datagrid-framework-r1/tasks/004-column-header-menu-lift.poml
 * @see ../src/components/DataGrid/columnHeader/ColumnHeaderMenu.tsx
 * @see ../src/components/DataGrid/columnHeader/ColumnFilterHeader.tsx
 */

import * as React from 'react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { ColumnHeaderMenu } from '../src/components/DataGrid/columnHeader/ColumnHeaderMenu';
import { ColumnFilterHeader } from '../src/components/DataGrid/columnHeader/ColumnFilterHeader';

// ─────────────────────────────────────────────────────────────────────────────
// Storybook meta
// ─────────────────────────────────────────────────────────────────────────────

export default {
  title: 'DataGrid/Column Header',
  argTypes: {
    theme: {
      control: { type: 'radio' },
      options: ['light', 'dark'],
    },
  },
};

interface StoryArgs {
  theme: 'light' | 'dark';
}

const choiceOptions = [
  { value: 100000000, label: 'Open' },
  { value: 100000001, label: 'In progress' },
  { value: 100000002, label: 'Closed' },
];

/**
 * Wrap a single column header in a minimal `<table>` so the `<th>` renders correctly.
 */
const wrapInTable = (header: React.ReactNode): React.ReactNode => (
  <table
    style={{
      width: '480px',
      borderCollapse: 'separate',
      borderSpacing: 0,
    }}
  >
    <thead>
      <tr>{header}</tr>
    </thead>
    <tbody>
      <tr>
        <td
          style={{
            padding: '10px 12px',
            fontFamily: "'Segoe UI', 'Segoe UI Web', Arial, sans-serif",
            fontSize: '12px',
          }}
        >
          Sample row 1
        </td>
      </tr>
      <tr>
        <td
          style={{
            padding: '10px 12px',
            fontFamily: "'Segoe UI', 'Segoe UI Web', Arial, sans-serif",
            fontSize: '12px',
          }}
        >
          Sample row 2
        </td>
      </tr>
    </tbody>
  </table>
);

/**
 * Outer FluentProvider wrap. NFR-03 requires `applyStylesToPortals` on every
 * provider that hosts a popover-bearing primitive — the column header opens both
 * a Menu and a Popover, so both must inherit the theme.
 */
const withFluentProvider = (theme: 'light' | 'dark', content: React.ReactNode): React.ReactNode => {
  const themeObject = theme === 'dark' ? webDarkTheme : webLightTheme;
  return (
    <FluentProvider
      applyStylesToPortals
      theme={themeObject}
      style={{
        padding: '24px',
        backgroundColor: theme === 'dark' ? '#1f1f1f' : '#ffffff',
        minHeight: '320px',
      }}
    >
      {content}
    </FluentProvider>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Story 1: ColumnHeaderMenu — Light theme (default)
// ─────────────────────────────────────────────────────────────────────────────

export const HeaderMenuLight = (args: StoryArgs) => {
  const [filterValue, setFilterValue] = React.useState<string | (string | number)[] | null>(null);
  const [sortDirection, setSortDirection] = React.useState<'asc' | 'desc' | null>(null);

  return withFluentProvider(
    args.theme,
    wrapInTable(
      <ColumnHeaderMenu
        columnLogicalName="account_name"
        title="Account Name"
        filterType="text"
        filterValue={typeof filterValue === 'string' ? filterValue : ''}
        onFilterChange={v => setFilterValue(v)}
        hasActiveFilter={Boolean(filterValue)}
        sortDirection={sortDirection}
        onSortChange={d => setSortDirection(d)}
        theme={args.theme === 'dark' ? webDarkTheme : webLightTheme}
      />
    )
  );
};
HeaderMenuLight.args = { theme: 'light' as const };
HeaderMenuLight.storyName = 'ColumnHeaderMenu — Light (text filter)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 2: ColumnHeaderMenu — Dark theme (NFR-03 portal verification)
// ─────────────────────────────────────────────────────────────────────────────

export const HeaderMenuDark = (args: StoryArgs) => {
  const [selected, setSelected] = React.useState<(string | number)[]>([]);
  const [sortDirection, setSortDirection] = React.useState<'asc' | 'desc' | null>('asc');

  return withFluentProvider(
    args.theme,
    wrapInTable(
      <ColumnHeaderMenu
        columnLogicalName="account_status"
        title="Status"
        filterType="choice"
        options={choiceOptions}
        selectedValues={selected}
        onFilterChange={v => setSelected(Array.isArray(v) ? v : v === null ? [] : [v as string])}
        hasActiveFilter={selected.length > 0}
        sortDirection={sortDirection}
        onSortChange={d => setSortDirection(d)}
        theme={args.theme === 'dark' ? webDarkTheme : webLightTheme}
      />
    )
  );
};
HeaderMenuDark.args = { theme: 'dark' as const };
HeaderMenuDark.storyName = 'ColumnHeaderMenu — Dark (choice filter, NFR-03 verification)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 3: ColumnFilterHeader — Light theme (default)
// ─────────────────────────────────────────────────────────────────────────────

export const FilterHeaderLight = (args: StoryArgs) => {
  const [filterValue, setFilterValue] = React.useState<string | (string | number)[] | null>(null);

  return withFluentProvider(
    args.theme,
    wrapInTable(
      <ColumnFilterHeader
        columnLogicalName="account_owner"
        title="Owner"
        filterType="text"
        filterValue={typeof filterValue === 'string' ? filterValue : ''}
        onFilterChange={v => setFilterValue(v)}
        hasActiveFilter={Boolean(filterValue)}
        theme={args.theme === 'dark' ? webDarkTheme : webLightTheme}
      />
    )
  );
};
FilterHeaderLight.args = { theme: 'light' as const };
FilterHeaderLight.storyName = 'ColumnFilterHeader — Light (text filter)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 4: ColumnFilterHeader — Dark theme (NFR-03 portal verification)
// ─────────────────────────────────────────────────────────────────────────────

export const FilterHeaderDark = (args: StoryArgs) => {
  const [selected, setSelected] = React.useState<(string | number)[]>([]);

  return withFluentProvider(
    args.theme,
    wrapInTable(
      <ColumnFilterHeader
        columnLogicalName="account_status"
        title="Status"
        filterType="choice"
        options={choiceOptions}
        selectedValues={selected}
        onFilterChange={v => setSelected(Array.isArray(v) ? v : v === null ? [] : [v as string])}
        hasActiveFilter={selected.length > 0}
        theme={args.theme === 'dark' ? webDarkTheme : webLightTheme}
      />
    )
  );
};
FilterHeaderDark.args = { theme: 'dark' as const };
FilterHeaderDark.storyName = 'ColumnFilterHeader — Dark (choice filter, NFR-03 verification)';
