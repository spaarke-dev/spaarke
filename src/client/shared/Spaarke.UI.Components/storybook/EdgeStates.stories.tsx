/**
 * EdgeStates.stories — Storybook stories for the DataGrid edge / non-happy-path
 * states required by the Phase A acceptance gate (task 009, NFR-07).
 *
 *   1. Empty               — savedquery returns zero records; empty-state surface
 *   2. Loading              — initial config + saved-query + metadata fetch in flight
 *   3. Error                — savedquery / metadata fetch throws → red error banner
 *   4. LazyLoadInProgress   — page 1 loaded, page 2 fetch in flight (sentinel visible)
 *
 * The Storybook a11y addon runs axe-core across each story; states intentionally
 * triggering an `alert` role (Error) keep their landmark for screen-reader users.
 *
 * **Storybook config**: like other stories in this directory, this file lives
 * OUTSIDE `src/` so the library build (`tsc`) does not include it.
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
// Mock client builders — each edge state needs slightly different behavior, so
// instead of one factory we expose three specialized builders.
// ─────────────────────────────────────────────────────────────────────────────

const baseConfig: DataGridConfiguration = {
  _version: '1.0',
  source: { type: 'savedquery', savedQueryId: 'mock-savedquery-id' },
  display: {
    title: 'Edge State — mock',
    densityDefault: 'comfortable',
    emptyStateMessage: 'No events match the current filters.',
  },
  behavior: {
    selectionMode: 'multi',
    pageSize: 100,
    enableSorting: true,
    enableColumnResize: true,
    enableKeyboardNavigation: true,
  },
};

const baseMetadata: EntityMetadata = {
  primaryIdAttribute: 'sprk_eventid',
  primaryNameAttribute: 'sprk_eventname',
  attributes: {
    sprk_eventid: { attributeType: 'String', isPrimaryId: true },
    sprk_eventname: { attributeType: 'String', isPrimaryName: true },
    sprk_status: { attributeType: 'Picklist' },
    sprk_amount: { attributeType: 'Money' },
    sprk_duedate: { attributeType: 'DateTime', format: 'DateOnly' },
    sprk_owner: { attributeType: 'Lookup' },
  },
};

const baseLayoutXml =
  '<grid name="resultset" object="1" jump="sprk_eventname" select="1" preview="1" icon="1">' +
  '  <row name="result" id="sprk_eventid">' +
  '    <cell name="sprk_eventname" width="220" isfirstcell="true" />' +
  '    <cell name="sprk_status" width="140" />' +
  '    <cell name="sprk_owner" width="160" />' +
  '    <cell name="sprk_amount" width="120" />' +
  '    <cell name="sprk_duedate" width="140" />' +
  '  </row>' +
  '</grid>';

const baseFetchXml =
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

/** Empty result — config + saved-query + metadata resolve, but query returns []. */
function createEmptyClient(): IDataverseClient {
  return {
    retrieveSavedQuery: async (): Promise<SavedQueryResult> => ({
      entityName: 'sprk_event',
      fetchXml: baseFetchXml,
      layoutXml: baseLayoutXml,
      name: 'Empty Mock View',
    }),
    retrieveSavedQueriesForEntity: async (): Promise<SavedQuerySummary[]> => [
      { id: 'mock-savedquery-id', name: 'All events', isDefault: true, queryType: 0 },
    ],
    retrieveEntityMetadata: async (): Promise<EntityMetadata> => baseMetadata,
    retrieveMultipleRecords: async <T,>(): Promise<FetchMultipleResult<T>> => ({
      entities: [],
      moreRecords: false,
      pagingCookie: undefined,
    }),
    retrieveRecord: async <T,>(entityName: string): Promise<T> => {
      if (entityName === 'sprk_gridconfiguration') {
        return { sprk_configjson: JSON.stringify(baseConfig) } as unknown as T;
      }
      throw new Error('not found');
    },
  };
}

/** Loading state — config record fetch never resolves (Promise stays pending). */
function createLoadingClient(): IDataverseClient {
  const neverResolve: Promise<never> = new Promise(() => undefined);
  return {
    retrieveSavedQuery: () => neverResolve,
    retrieveSavedQueriesForEntity: () => neverResolve,
    retrieveEntityMetadata: () => neverResolve,
    retrieveMultipleRecords: () => neverResolve,
    retrieveRecord: () => neverResolve,
  };
}

/** Error state — saved-query lookup throws (network failure simulated). */
function createErrorClient(): IDataverseClient {
  return {
    retrieveSavedQuery: async (): Promise<SavedQueryResult> => {
      throw new Error('Simulated network failure: SavedQuery retrieval timed out (HTTP 504).');
    },
    retrieveSavedQueriesForEntity: async (): Promise<SavedQuerySummary[]> => {
      throw new Error('Simulated network failure: savedquery set retrieval failed.');
    },
    retrieveEntityMetadata: async (): Promise<EntityMetadata> => {
      throw new Error('Simulated network failure: metadata retrieval failed.');
    },
    retrieveMultipleRecords: async <T,>(): Promise<FetchMultipleResult<T>> => {
      throw new Error('Simulated network failure: FetchXML execution failed.');
    },
    retrieveRecord: async <T,>(entityName: string): Promise<T> => {
      if (entityName === 'sprk_gridconfiguration') {
        // Return valid config — the failure surfaces on the savedquery step.
        return { sprk_configjson: JSON.stringify(baseConfig) } as unknown as T;
      }
      throw new Error('Simulated network failure: record retrieval failed.');
    },
  };
}

/**
 * Lazy-load-in-progress — page 1 resolves immediately, page 2 hangs (Promise
 * pending). The IntersectionObserver fires `fetchNextPage` when the sentinel
 * scrolls into view; the user sees the page-1 records + the "Loading more…"
 * spinner.
 */
function createLazyLoadingClient(): IDataverseClient {
  const allRecords: Record<string, unknown>[] = Array.from({ length: 250 }, (_, i) => ({
    sprk_eventid: `evt-${String(i + 1).padStart(4, '0')}`,
    sprk_eventname: `Event ${i + 1}`,
    sprk_status: 100000000,
    sprk_amount: 1000 + i * 37.5,
    sprk_duedate: new Date(Date.now() + i * 86_400_000).toISOString(),
    sprk_owner: { name: `User ${1 + (i % 5)}` },
  }));

  return {
    retrieveSavedQuery: async (): Promise<SavedQueryResult> => ({
      entityName: 'sprk_event',
      fetchXml: baseFetchXml,
      layoutXml: baseLayoutXml,
      name: 'Lazy-load Mock View',
    }),
    retrieveSavedQueriesForEntity: async (): Promise<SavedQuerySummary[]> => [
      { id: 'mock-savedquery-id', name: 'All events', isDefault: true, queryType: 0 },
    ],
    retrieveEntityMetadata: async (): Promise<EntityMetadata> => baseMetadata,
    retrieveMultipleRecords: async <T,>(
      _entityName: string,
      pagedFetchXml: string,
    ): Promise<FetchMultipleResult<T>> => {
      const pageMatch = pagedFetchXml.match(/\bpage="(\d+)"/);
      const page = pageMatch ? Number.parseInt(pageMatch[1], 10) : 1;
      if (page === 1) {
        const slice = allRecords.slice(0, 100);
        return {
          entities: slice as unknown as T[],
          moreRecords: true,
          pagingCookie: 'mock-cookie-page-1',
        };
      }
      // Page 2+ — hang indefinitely to keep the "Loading more…" spinner visible.
      return new Promise(() => undefined);
    },
    retrieveRecord: async <T,>(entityName: string): Promise<T> => {
      if (entityName === 'sprk_gridconfiguration') {
        return { sprk_configjson: JSON.stringify({ ...baseConfig, behavior: { ...baseConfig.behavior, pageSize: 100 } }) } as unknown as T;
      }
      throw new Error('not found');
    },
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Storybook meta
// ─────────────────────────────────────────────────────────────────────────────

export default {
  title: 'DataGrid/Edge States',
  component: DataGrid,
  argTypes: {
    theme: {
      control: { type: 'radio' },
      options: ['light', 'dark'],
    },
  },
  parameters: {
    a11y: { manual: false },
    viewport: {
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
): React.ReactNode => (
  <FluentProvider
    applyStylesToPortals
    theme={theme === 'dark' ? webDarkTheme : webLightTheme}
    style={{ height: '600px', display: 'flex' }}
  >
    {content}
  </FluentProvider>
);

// ─────────────────────────────────────────────────────────────────────────────
// Story 1: Empty (zero records returned)
// ─────────────────────────────────────────────────────────────────────────────

export const Empty = (args: StoryArgs) => {
  const client = React.useMemo(() => createEmptyClient(), []);
  return withFluentProvider(
    args.theme,
    <DataGrid configId="story-edge-empty" dataverseClient={client} />,
  );
};
Empty.args = { theme: 'light' as const };
Empty.storyName = 'Empty (zero records — empty-state surface)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 2: Loading (initial fetch in progress)
// ─────────────────────────────────────────────────────────────────────────────

export const Loading = (args: StoryArgs) => {
  const client = React.useMemo(() => createLoadingClient(), []);
  return withFluentProvider(
    args.theme,
    <DataGrid configId="story-edge-loading" dataverseClient={client} />,
  );
};
Loading.args = { theme: 'light' as const };
Loading.storyName = 'Loading (config fetch in flight — Spinner)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 3: Error (network failure simulated — DataGrid surfaces a red banner)
// ─────────────────────────────────────────────────────────────────────────────

export const Error = (args: StoryArgs) => {
  const client = React.useMemo(() => createErrorClient(), []);
  return withFluentProvider(
    args.theme,
    <DataGrid configId="story-edge-error" dataverseClient={client} />,
  );
};
Error.args = { theme: 'light' as const };
Error.storyName = 'Error (savedquery throws — role="alert" banner)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 4: LazyLoadInProgress (page 1 loaded; page 2 fetch hanging)
// ─────────────────────────────────────────────────────────────────────────────

export const LazyLoadInProgress = (args: StoryArgs) => {
  const client = React.useMemo(() => createLazyLoadingClient(), []);
  return withFluentProvider(
    args.theme,
    <DataGrid configId="story-edge-lazyload-in-progress" dataverseClient={client} />,
  );
};
LazyLoadInProgress.args = { theme: 'light' as const };
LazyLoadInProgress.storyName = 'LazyLoadInProgress (page 1 loaded; page 2 hanging)';
