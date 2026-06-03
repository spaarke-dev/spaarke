/**
 * DataGrid.stories — Storybook stories for the Spaarke DataGrid framework core.
 *
 * Four stories per task 003 POML:
 *   1. Default (sprk_event configId) — happy path with mock dataverseClient
 *   2. No configId (metadata fallback) — non-existent configId still renders
 *   3. Lazy load (1000 records) — IntersectionObserver paging
 *   4. Selection across pages — Set<string> preserved across page boundaries
 *
 * All stories wrapped in `<FluentProvider applyStylesToPortals theme={...} />`.
 * Dark variants exposed via the `theme` argType.
 *
 * **Storybook config**: this project has no `.storybook/` configuration yet; this
 * file lives OUTSIDE `src/` so the TypeScript library build (`tsc`) does not pick
 * it up. When Storybook is wired in a later task, the standard `main.ts`
 * `stories: ['../storybook/**\/*.stories.@(ts|tsx)']` pattern picks it up.
 *
 * @see projects/spaarke-datagrid-framework-r1/tasks/003-datagrid-core.poml
 */

import * as React from 'react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
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
// Mock IDataverseClient factory — STORYBOOK-ONLY (not exported from the library).
// ─────────────────────────────────────────────────────────────────────────────

interface MockDataverseClientOptions {
  /** Total record count to generate. Default 50. */
  recordCount?: number;
  /** Whether `sprk_gridconfiguration` lookup returns a valid record. Default true. */
  hasConfigRecord?: boolean;
  /** Custom configjson body (only used when `hasConfigRecord = true`). */
  configJson?: DataGridConfiguration;
}

function createMockDataverseClient(options: MockDataverseClientOptions = {}): IDataverseClient {
  const recordCount = options.recordCount ?? 50;
  const hasConfigRecord = options.hasConfigRecord ?? true;
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

  // Pre-generate the full record corpus so paging is deterministic across re-renders.
  const allRecords: Record<string, unknown>[] = Array.from({ length: recordCount }, (_, i) => ({
    sprk_eventid: `evt-${String(i + 1).padStart(4, '0')}`,
    sprk_eventname: `Event ${i + 1}`,
    sprk_status: (i % 3) * 100000000 + (i % 3 === 0 ? 100000000 : i % 3 === 1 ? 100000001 : 100000002),
    sprk_amount: 1000 + i * 37.5,
    sprk_duedate: new Date(Date.now() + i * 86_400_000).toISOString(),
    sprk_owner: { name: `User ${1 + (i % 5)}` },
  }));

  return {
    retrieveSavedQuery: async (savedQueryId: string): Promise<SavedQueryResult> => {
      return {
        entityName: 'sprk_event',
        fetchXml,
        layoutXml,
        name: `Mock Saved Query (${savedQueryId})`,
      };
    },
    retrieveSavedQueriesForEntity: async (entityName: string): Promise<SavedQuerySummary[]> => {
      return [{ id: 'mock-savedquery-id', name: `All ${entityName}`, isDefault: true, queryType: 0 }];
    },
    retrieveEntityMetadata: async (_entityName: string): Promise<EntityMetadata> => {
      return entityMetadata;
    },
    retrieveMultipleRecords: async <T = Record<string, unknown>,>(
      _entityName: string,
      pagedFetchXml: string
    ): Promise<FetchMultipleResult<T>> => {
      // Parse `page` + `count` attributes that useLazyLoad injects.
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
      _select?: string[]
    ): Promise<T> => {
      if (entityName === 'sprk_gridconfiguration') {
        if (!hasConfigRecord || id === 'nonexistent') {
          throw new Error('Record not found');
        }
        return { sprk_configjson: JSON.stringify(configJson) } as unknown as T;
      }
      const hit = allRecords.find(r => r.sprk_eventid === id);
      if (!hit) throw new Error(`Record ${id} not found`);
      return hit as unknown as T;
    },
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Storybook meta
// ─────────────────────────────────────────────────────────────────────────────

// CSF default export — Storybook will pick this up once `.storybook` is configured.
export default {
  title: 'DataGrid/Core',
  component: DataGrid,
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

const withFluentProvider = (theme: 'light' | 'dark', content: React.ReactNode): React.ReactNode => {
  // `applyStylesToPortals` is MANDATORY per NFR-03 — popover-bearing surfaces
  // (column header menus, command bar overflow) render in portals and must
  // inherit the active theme.
  return (
    <FluentProvider
      applyStylesToPortals
      theme={theme === 'dark' ? webDarkTheme : webLightTheme}
      style={{ height: '600px', display: 'flex' }}
    >
      {content}
    </FluentProvider>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Story 1: Default (sprk_event configId)
// ─────────────────────────────────────────────────────────────────────────────

export const DefaultSprkEvent = (args: StoryArgs) => {
  const client = React.useMemo(() => createMockDataverseClient({ recordCount: 25 }), []);
  return withFluentProvider(
    args.theme,
    <DataGrid configId="sprk_event_default" dataverseClient={client} onRecordOpen={id => alert(`Opened: ${id}`)} />
  );
};
DefaultSprkEvent.args = { theme: 'light' as const };
DefaultSprkEvent.storyName = 'Default (sprk_event configId)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 2: No configId (metadata fallback)
// ─────────────────────────────────────────────────────────────────────────────

export const NoConfigIdMetadataFallback = (args: StoryArgs) => {
  // hasConfigRecord = false ⇒ retrieveRecord throws ⇒ DataGrid uses metadata fallback.
  // Mock still returns metadata + a stub savedquery if any code path requests one,
  // but since configRecord is null AND source resolution fails, no fetchXml is
  // available — the grid surfaces a friendly error, demonstrating graceful
  // fallthrough per FR-DG-04.
  const client = React.useMemo(() => createMockDataverseClient({ hasConfigRecord: false }), []);
  return withFluentProvider(
    args.theme,
    <DataGrid configId="nonexistent" dataverseClient={client} onRecordOpen={id => alert(`Opened: ${id}`)} />
  );
};
NoConfigIdMetadataFallback.args = { theme: 'light' as const };
NoConfigIdMetadataFallback.storyName = 'No configId (metadata fallback)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 3: Lazy load (1000 records)
// ─────────────────────────────────────────────────────────────────────────────

export const LazyLoad1000Records = (args: StoryArgs) => {
  const client = React.useMemo(
    () =>
      createMockDataverseClient({
        recordCount: 1000,
        configJson: {
          _version: '1.0',
          source: { type: 'savedquery', savedQueryId: 'mock-savedquery-id' },
          display: { title: 'Lazy-load (1000 rows)', densityDefault: 'comfortable' },
          // 100 rows per page → 10 pages total.
          behavior: { pageSize: 100 },
        },
      }),
    []
  );
  return withFluentProvider(
    args.theme,
    <DataGrid configId="sprk_event_lazy" dataverseClient={client} onRecordOpen={id => alert(`Opened: ${id}`)} />
  );
};
LazyLoad1000Records.args = { theme: 'light' as const };
LazyLoad1000Records.storyName = 'Lazy load (1000 records)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 4: Selection across pages
// ─────────────────────────────────────────────────────────────────────────────

export const SelectionAcrossPages = (args: StoryArgs) => {
  // 500 records, page size 50 → 10 pages; user selects on page 1, scrolls, returns.
  const client = React.useMemo(
    () =>
      createMockDataverseClient({
        recordCount: 500,
        configJson: {
          _version: '1.0',
          source: { type: 'savedquery', savedQueryId: 'mock-savedquery-id' },
          display: { title: 'Selection across pages', densityDefault: 'comfortable' },
          behavior: { pageSize: 50 },
        },
      }),
    []
  );
  return withFluentProvider(
    args.theme,
    <DataGrid configId="sprk_event_selection" dataverseClient={client} onRecordOpen={id => alert(`Opened: ${id}`)} />
  );
};
SelectionAcrossPages.args = { theme: 'light' as const };
SelectionAcrossPages.storyName = 'Selection across pages';
