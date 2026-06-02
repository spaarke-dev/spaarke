/**
 * FluentV9NativeFeatures.stories — Storybook stories demonstrating that the
 * Spaarke `<DataGrid />` framework relies on Fluent v9 native DataGrid props for
 * every interaction primitive (selection, sort, resize, focus / keyboard nav,
 * density, sticky header).
 *
 * Phase A acceptance gate (task 009) per NFR-07. Each story below exercises the
 * underlying Fluent v9 `DataGrid` capability by tuning the `DataGridConfiguration`
 * passed through `sprk_configjson` — the wrapper translates the config knob into
 * the matching Fluent v9 prop:
 *
 *   | Story                  | Fluent v9 native prop                    |
 *   |------------------------|------------------------------------------|
 *   | SelectionSingle        | `selectionMode="single"`                  |
 *   | SelectionMulti         | `selectionMode="multiselect"`             |
 *   | SelectionSelectAll     | `selectionMode="multiselect"` + header   |
 *   |                        | `selectionCell.checkboxIndicator`         |
 *   | SortableColumns        | `sortable` + `createTableColumn.compare` |
 *   | ResizableColumns       | `resizableColumns` + `columnSizingOptions` |
 *   | KeyboardNavigation     | `focusMode="composite"`                   |
 *   | DensityExtraSmall      | (size mapping note — see story)           |
 *   | DensitySmall           | `size="small"` (densityDefault=compact)  |
 *   | DensityMedium          | `size="medium"` (densityDefault=comfort) |
 *   | StickyHeader           | Native sticky header (constrained height) |
 *
 * **NO hand-rolled `<input type="checkbox">`, sort arrows, drag handles, or
 * arrow-key `onKeyDown` switches in the framework code.** This is the visual
 * proof. The code-review gate (task 009 step 8) is the programmatic backstop.
 *
 * **NFR-03**: every story wraps the surface in
 *   `<FluentProvider applyStylesToPortals theme={…} />`
 * so popover-bearing surfaces (column header menus that may open via right-click
 * in future tasks) inherit the active theme.
 *
 * **Storybook config**: like the other stories in this directory, this file lives
 * OUTSIDE `src/` so the library build (`tsc`) does not include it. When Storybook
 * wiring is added in a later milestone, the standard
 * `stories: ['../storybook/**\/*.stories.@(ts|tsx)']` glob picks it up.
 *
 * @see projects/spaarke-datagrid-framework-r1/tasks/009-storybook-coverage-and-visual-diff-gate.poml
 */

import * as React from 'react';
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
} from '@fluentui/react-components';
import { DataGrid } from '../src/components/DataGrid';
import type {
  IDataverseClient,
  SavedQueryResult,
  SavedQuerySummary,
  EntityMetadata,
  FetchMultipleResult,
} from '../src/services/IDataverseClient';
import type { DataGridConfiguration } from '../src/types/DataGridConfiguration';

// ─────────────────────────────────────────────────────────────────────────────
// Mock IDataverseClient factory — STORYBOOK-ONLY (mirrors DataGrid.stories pattern).
// ─────────────────────────────────────────────────────────────────────────────

interface MockClientOptions {
  recordCount?: number;
  configJson?: DataGridConfiguration;
}

function createMockDataverseClient(options: MockClientOptions = {}): IDataverseClient {
  const recordCount = options.recordCount ?? 30;
  const configJson: DataGridConfiguration = options.configJson ?? {
    _version: '1.0',
    source: { type: 'savedquery', savedQueryId: 'mock-savedquery-id' },
    display: { title: 'Mock Events', densityDefault: 'comfortable' },
    behavior: {
      selectionMode: 'multi',
      pageSize: 100,
      enableSorting: true,
      enableColumnResize: true,
      enableKeyboardNavigation: true,
    },
  };

  const entityMetadata: EntityMetadata = {
    primaryIdAttribute: 'sprk_eventid',
    primaryNameAttribute: 'sprk_eventname',
    attributes: {
      sprk_eventid: { attributeType: 'String', isPrimaryId: true },
      sprk_eventname: { attributeType: 'String', isPrimaryName: true },
      sprk_status: {
        attributeType: 'Picklist',
        optionSet: [
          { value: 100000000, label: 'Open' },
          { value: 100000001, label: 'In progress' },
          { value: 100000002, label: 'Closed' },
        ],
      },
      sprk_amount: { attributeType: 'Money' },
      sprk_duedate: { attributeType: 'DateTime', format: 'DateOnly' },
      sprk_owner: { attributeType: 'Lookup' },
    },
  };

  const layoutXml =
    '<grid name="resultset" object="1" jump="sprk_eventname" select="1" preview="1" icon="1">' +
    '  <row name="result" id="sprk_eventid">' +
    '    <cell name="sprk_eventname" width="220" isfirstcell="true" />' +
    '    <cell name="sprk_status" width="140" />' +
    '    <cell name="sprk_owner" width="160" />' +
    '    <cell name="sprk_amount" width="120" />' +
    '    <cell name="sprk_duedate" width="140" />' +
    '  </row>' +
    '</grid>';

  const fetchXml =
    '<fetch>' +
    '  <entity name="sprk_event">' +
    '    <attribute name="sprk_eventid" />' +
    '    <attribute name="sprk_eventname" />' +
    '    <attribute name="sprk_status" />' +
    '    <attribute name="sprk_amount" />' +
    '    <attribute name="sprk_duedate" />' +
    '    <attribute name="sprk_owner" />' +
    '  </entity>' +
    '</fetch>';

  const allRecords: Record<string, unknown>[] = Array.from({ length: recordCount }, (_, i) => ({
    sprk_eventid: `evt-${String(i + 1).padStart(4, '0')}`,
    sprk_eventname: `Event ${i + 1}`,
    sprk_status:
      i % 3 === 0 ? 100000000 : i % 3 === 1 ? 100000001 : 100000002,
    sprk_amount: 1000 + i * 37.5,
    sprk_duedate: new Date(Date.now() + i * 86_400_000).toISOString(),
    sprk_owner: { name: `User ${1 + (i % 5)}` },
  }));

  return {
    retrieveSavedQuery: async (savedQueryId: string): Promise<SavedQueryResult> => ({
      entityName: 'sprk_event',
      fetchXml,
      layoutXml,
      name: `Mock Saved Query (${savedQueryId})`,
    }),
    retrieveSavedQueriesForEntity: async (entityName: string): Promise<SavedQuerySummary[]> => [
      { id: 'mock-savedquery-id', name: `All ${entityName}`, isDefault: true, queryType: 0 },
    ],
    retrieveEntityMetadata: async (_entityName: string): Promise<EntityMetadata> => entityMetadata,
    retrieveMultipleRecords: async <T = Record<string, unknown>,>(
      _entityName: string,
      pagedFetchXml: string,
    ): Promise<FetchMultipleResult<T>> => {
      const pageMatch = pagedFetchXml.match(/\bpage="(\d+)"/);
      const countMatch = pagedFetchXml.match(/\bcount="(\d+)"/);
      const page = pageMatch ? Number.parseInt(pageMatch[1], 10) : 1;
      const count = countMatch ? Number.parseInt(countMatch[1], 10) : 100;
      const start = (page - 1) * count;
      const slice = allRecords.slice(start, start + count);
      const moreRecords = start + slice.length < allRecords.length;
      return {
        entities: slice as unknown as T[],
        moreRecords,
        pagingCookie: moreRecords ? `mock-cookie-page-${page}` : undefined,
      };
    },
    retrieveRecord: async <T = Record<string, unknown>,>(
      entityName: string,
      id: string,
      _select?: string[],
    ): Promise<T> => {
      if (entityName === 'sprk_gridconfiguration') {
        if (id === 'nonexistent') throw new Error('Record not found');
        return { sprk_configjson: JSON.stringify(configJson) } as unknown as T;
      }
      const hit = allRecords.find((r) => r.sprk_eventid === id);
      if (!hit) throw new Error(`Record ${id} not found`);
      return hit as unknown as T;
    },
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Storybook meta
// ─────────────────────────────────────────────────────────────────────────────

export default {
  title: 'DataGrid/Fluent v9 Native Features',
  component: DataGrid,
  argTypes: {
    theme: {
      control: { type: 'radio' },
      options: ['light', 'dark'],
    },
  },
  parameters: {
    // a11y addon: run axe-core on every story rendered from this file. Stories that
    // intentionally exercise an empty/error/loading state may override this in their
    // own `parameters.a11y.disable` block.
    a11y: { manual: false },
    viewport: {
      // Zoom-level testing presets (NFR-01 acceptance gate — light + dark + 4 zoom levels).
      // These viewports approximate the *visual* zoom by scaling the rendering area; pair
      // with the host-browser zoom (Ctrl + / Ctrl -) during manual MDA parity review.
      viewports: {
        zoom75: { name: 'Zoom 75%', styles: { width: '1707px', height: '960px' } },
        zoom100: { name: 'Zoom 100%', styles: { width: '1280px', height: '720px' } },
        zoom125: { name: 'Zoom 125%', styles: { width: '1024px', height: '576px' } },
        zoom150: { name: 'Zoom 150%', styles: { width: '853px', height: '480px' } },
      },
      defaultViewport: 'zoom100',
    },
  },
};

interface StoryArgs {
  theme: 'light' | 'dark';
}

const withFluentProvider = (
  theme: 'light' | 'dark',
  content: React.ReactNode,
  height = '600px',
): React.ReactNode => (
  <FluentProvider
    applyStylesToPortals
    theme={theme === 'dark' ? webDarkTheme : webLightTheme}
    style={{ height, display: 'flex' }}
  >
    {content}
  </FluentProvider>
);

// ─────────────────────────────────────────────────────────────────────────────
// Selection — single
// ─────────────────────────────────────────────────────────────────────────────

export const SelectionSingle = (args: StoryArgs) => {
  const client = React.useMemo(
    () =>
      createMockDataverseClient({
        recordCount: 12,
        configJson: {
          _version: '1.0',
          source: { type: 'savedquery', savedQueryId: 'mock-savedquery-id' },
          display: { title: 'Selection — single (Fluent native)', densityDefault: 'comfortable' },
          behavior: { selectionMode: 'single', pageSize: 50, enableSorting: true, enableColumnResize: true },
        },
      }),
    [],
  );
  return withFluentProvider(
    args.theme,
    <DataGrid configId="story-selection-single" dataverseClient={client} />,
  );
};
SelectionSingle.args = { theme: 'light' as const };
SelectionSingle.storyName = 'Selection — single (selectionMode="single")';

// ─────────────────────────────────────────────────────────────────────────────
// Selection — multi (with native row checkboxes)
// ─────────────────────────────────────────────────────────────────────────────

export const SelectionMulti = (args: StoryArgs) => {
  const client = React.useMemo(
    () =>
      createMockDataverseClient({
        recordCount: 15,
        configJson: {
          _version: '1.0',
          source: { type: 'savedquery', savedQueryId: 'mock-savedquery-id' },
          display: { title: 'Selection — multi (Fluent native)', densityDefault: 'comfortable' },
          behavior: { selectionMode: 'multi', pageSize: 50, enableSorting: true, enableColumnResize: true },
        },
      }),
    [],
  );
  return withFluentProvider(
    args.theme,
    <DataGrid configId="story-selection-multi" dataverseClient={client} />,
  );
};
SelectionMulti.args = { theme: 'light' as const };
SelectionMulti.storyName = 'Selection — multi (selectionMode="multiselect")';

// ─────────────────────────────────────────────────────────────────────────────
// Selection — select-all (header checkbox toggles every row)
// ─────────────────────────────────────────────────────────────────────────────

export const SelectionSelectAll = (args: StoryArgs) => {
  const client = React.useMemo(
    () =>
      createMockDataverseClient({
        recordCount: 25,
        configJson: {
          _version: '1.0',
          source: { type: 'savedquery', savedQueryId: 'mock-savedquery-id' },
          display: { title: 'Selection — select all (header checkbox)', densityDefault: 'comfortable' },
          behavior: { selectionMode: 'multi', pageSize: 50, enableSorting: true, enableColumnResize: true },
        },
      }),
    [],
  );
  return withFluentProvider(
    args.theme,
    <DataGrid configId="story-selection-all" dataverseClient={client} />,
  );
};
SelectionSelectAll.args = { theme: 'light' as const };
SelectionSelectAll.storyName = 'Selection — select all (header checkboxIndicator)';

// ─────────────────────────────────────────────────────────────────────────────
// Sort — click header to sort ascending / descending (Fluent native)
// ─────────────────────────────────────────────────────────────────────────────

export const SortableColumns = (args: StoryArgs) => {
  const client = React.useMemo(
    () =>
      createMockDataverseClient({
        recordCount: 20,
        configJson: {
          _version: '1.0',
          source: { type: 'savedquery', savedQueryId: 'mock-savedquery-id' },
          display: { title: 'Sort (Fluent native — createTableColumn.compare)', densityDefault: 'comfortable' },
          behavior: { selectionMode: 'multi', pageSize: 50, enableSorting: true, enableColumnResize: true },
        },
      }),
    [],
  );
  return withFluentProvider(
    args.theme,
    <DataGrid configId="story-sort" dataverseClient={client} />,
  );
};
SortableColumns.args = { theme: 'light' as const };
SortableColumns.storyName = 'Sort (sortable + createTableColumn.compare)';

// ─────────────────────────────────────────────────────────────────────────────
// Column resize — drag column borders (Fluent native, no custom drag handles)
// ─────────────────────────────────────────────────────────────────────────────

export const ResizableColumns = (args: StoryArgs) => {
  const client = React.useMemo(
    () =>
      createMockDataverseClient({
        recordCount: 18,
        configJson: {
          _version: '1.0',
          source: { type: 'savedquery', savedQueryId: 'mock-savedquery-id' },
          display: { title: 'Resize (Fluent native — columnSizingOptions)', densityDefault: 'comfortable' },
          behavior: { selectionMode: 'multi', pageSize: 50, enableSorting: true, enableColumnResize: true },
        },
      }),
    [],
  );
  return withFluentProvider(
    args.theme,
    <DataGrid configId="story-resize" dataverseClient={client} />,
  );
};
ResizableColumns.args = { theme: 'light' as const };
ResizableColumns.storyName = 'Resize (resizableColumns + columnSizingOptions)';

// ─────────────────────────────────────────────────────────────────────────────
// Keyboard navigation — Tab/Shift+Tab + Arrow keys (Fluent native composite focus)
// ─────────────────────────────────────────────────────────────────────────────

export const KeyboardNavigation = (args: StoryArgs) => {
  const client = React.useMemo(
    () =>
      createMockDataverseClient({
        recordCount: 16,
        configJson: {
          _version: '1.0',
          source: { type: 'savedquery', savedQueryId: 'mock-savedquery-id' },
          display: { title: 'Keyboard navigation (Fluent focusMode="composite")', densityDefault: 'comfortable' },
          behavior: { selectionMode: 'multi', pageSize: 50, enableSorting: true, enableColumnResize: true },
        },
      }),
    [],
  );
  return withFluentProvider(
    args.theme,
    <DataGrid configId="story-keyboard-nav" dataverseClient={client} />,
  );
};
KeyboardNavigation.args = { theme: 'light' as const };
KeyboardNavigation.storyName = 'Keyboard nav (focusMode="composite")';

// ─────────────────────────────────────────────────────────────────────────────
// Density — extra-small (Fluent native size; wrapper currently maps two density
// values, framework reviewers can confirm the underlying primitive supports 3).
// ─────────────────────────────────────────────────────────────────────────────

export const DensityExtraSmall = (args: StoryArgs) => {
  // The wrapper maps densityDefault: 'compact' → size: 'small'. The story name
  // tracks Fluent v9's smallest size; when the wrapper grows a third density tier
  // (NFR-07 future), bump this story's config to set size="extra-small" directly.
  const client = React.useMemo(
    () =>
      createMockDataverseClient({
        recordCount: 22,
        configJson: {
          _version: '1.0',
          source: { type: 'savedquery', savedQueryId: 'mock-savedquery-id' },
          display: { title: 'Density — extra-small (Fluent native)', densityDefault: 'compact' },
          behavior: { selectionMode: 'multi', pageSize: 50, enableSorting: true, enableColumnResize: true },
        },
      }),
    [],
  );
  return withFluentProvider(
    args.theme,
    <DataGrid configId="story-density-xs" dataverseClient={client} />,
  );
};
DensityExtraSmall.args = { theme: 'light' as const };
DensityExtraSmall.storyName = 'Density — extra-small (size="small", wrapper note)';

// ─────────────────────────────────────────────────────────────────────────────
// Density — small (compact)
// ─────────────────────────────────────────────────────────────────────────────

export const DensitySmall = (args: StoryArgs) => {
  const client = React.useMemo(
    () =>
      createMockDataverseClient({
        recordCount: 22,
        configJson: {
          _version: '1.0',
          source: { type: 'savedquery', savedQueryId: 'mock-savedquery-id' },
          display: { title: 'Density — small (densityDefault=compact)', densityDefault: 'compact' },
          behavior: { selectionMode: 'multi', pageSize: 50, enableSorting: true, enableColumnResize: true },
        },
      }),
    [],
  );
  return withFluentProvider(
    args.theme,
    <DataGrid configId="story-density-small" dataverseClient={client} />,
  );
};
DensitySmall.args = { theme: 'light' as const };
DensitySmall.storyName = 'Density — small (size="small")';

// ─────────────────────────────────────────────────────────────────────────────
// Density — medium (default comfortable)
// ─────────────────────────────────────────────────────────────────────────────

export const DensityMedium = (args: StoryArgs) => {
  const client = React.useMemo(
    () =>
      createMockDataverseClient({
        recordCount: 22,
        configJson: {
          _version: '1.0',
          source: { type: 'savedquery', savedQueryId: 'mock-savedquery-id' },
          display: { title: 'Density — medium (densityDefault=comfortable)', densityDefault: 'comfortable' },
          behavior: { selectionMode: 'multi', pageSize: 50, enableSorting: true, enableColumnResize: true },
        },
      }),
    [],
  );
  return withFluentProvider(
    args.theme,
    <DataGrid configId="story-density-medium" dataverseClient={client} />,
  );
};
DensityMedium.args = { theme: 'light' as const };
DensityMedium.storyName = 'Density — medium (size="medium")';

// ─────────────────────────────────────────────────────────────────────────────
// Sticky header — header remains pinned during vertical scroll (Fluent native)
// ─────────────────────────────────────────────────────────────────────────────

export const StickyHeader = (args: StoryArgs) => {
  // Render 200 records in a constrained-height container — the user can scroll
  // the body while the column headers stay pinned (Fluent v9 default behavior
  // for `DataGridHeader` inside a scroll container).
  const client = React.useMemo(
    () =>
      createMockDataverseClient({
        recordCount: 200,
        configJson: {
          _version: '1.0',
          source: { type: 'savedquery', savedQueryId: 'mock-savedquery-id' },
          display: { title: 'Sticky header (Fluent native scroll behavior)', densityDefault: 'comfortable' },
          behavior: { selectionMode: 'multi', pageSize: 100, enableSorting: true, enableColumnResize: true },
        },
      }),
    [],
  );
  return withFluentProvider(
    args.theme,
    <DataGrid configId="story-sticky-header" dataverseClient={client} />,
    '400px', // constrain height so the user has something to scroll past
  );
};
StickyHeader.args = { theme: 'light' as const };
StickyHeader.storyName = 'Sticky header (constrained height + Fluent header)';
