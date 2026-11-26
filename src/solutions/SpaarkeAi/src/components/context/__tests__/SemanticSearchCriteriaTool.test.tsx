/**
 * SemanticSearchCriteriaTool (#3131): the Console's search criteria reach the search page.
 *
 * Before #3131 the tool sent `documentType` / `fileType` / `matterType` / `dateRange=<preset>`,
 * keys the SemanticSearch page never reads, so every filter chosen in the Console was dropped.
 * The contract test below runs the tool's `data` string through the page's own parser.
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { parseUrlParams } from '../../../../../../client/code-pages/SemanticSearch/src/utils/parseUrlParams';

const mockNavigateTo = jest.fn();
const mockDispatch = jest.fn();
const mockRefresh = jest.fn();
let mockOptionsError: string | null = null;

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    getXrm: () => ({ Navigation: { navigateTo: mockNavigateTo } }),
    useSearchFilterOptions: () => ({
      documentTypes: [
        { value: '100000001', label: 'Contract' },
        { value: '100000002', label: 'NDA' },
      ],
      fileTypes: [{ value: 'pdf', label: 'PDF' }],
      matterTypes: [{ value: 'mt-1', label: 'Litigation' }],
      isLoading: false,
      error: mockOptionsError,
      refresh: mockRefresh,
    }),
  };
});

jest.mock('@spaarke/ai-widgets', () => ({
  useDispatchPaneEvent: () => mockDispatch,
  SEARCH_CRITERIA_RESULT_WIDGET_TYPE: 'search-criteria-result',
}));

import {
  SemanticSearchCriteriaTool,
  buildSearchDataParams,
  normalizeCriteria,
  type PersistedCriteria,
} from '../SemanticSearchCriteriaTool';

const STORAGE_KEY = 'spaarke:context:semantic-search-criteria';
// A Tuesday; local time so presets resolve the same in every runner zone.
const NOW = new Date(2026, 10, 24, 15, 0, 0);

function criteria(patch: Partial<PersistedCriteria>): PersistedCriteria {
  return {
    query: 'indemnity',
    domain: 'documents',
    documentTypes: [],
    fileTypes: [],
    matterTypes: [],
    datePreset: 'off',
    dateRange: null,
    ...patch,
  };
}

/**
 * The page's view of the `data` handed to navigateTo: Dataverse puts it in the URL as
 * `?data=<encoded>`, and the page decodes the envelope once more before parsing it.
 */
function pageSees(navigateData: string) {
  return parseUrlParams(`?data=${encodeURIComponent(navigateData)}`);
}

/** The page's view of a `buildSearchDataParams` string, as the tool launches it (encoded once). */
function pageSeesParams(params: string) {
  return pageSees(encodeURIComponent(params));
}

beforeEach(() => {
  mockNavigateTo.mockReset().mockReturnValue(Promise.resolve());
  mockDispatch.mockReset();
  mockRefresh.mockReset();
  mockOptionsError = null;
  window.localStorage.clear();
});

describe('buildSearchDataParams — the page reads every criterion', () => {
  it('documents: all three filter lists and the dates reach the page parser', () => {
    const sees = pageSeesParams(
      buildSearchDataParams(
        criteria({
          documentTypes: ['100000001', '100000002'],
          fileTypes: ['pdf'],
          matterTypes: ['mt-1'],
          datePreset: 'custom',
          dateRange: { from: '2026-11-01', to: '2026-11-20' },
        })
      )
    );

    expect(sees.query).toBe('indemnity');
    expect(sees.domain).toBe('documents');
    expect(sees.documentTypes).toEqual(['100000001', '100000002']);
    expect(sees.fileTypes).toEqual(['pdf']);
    expect(sees.matterTypes).toEqual(['mt-1']);
    expect(sees.dateFrom).toBe('2026-11-01');
    expect(sees.dateTo).toBe('2026-11-20');
  });

  it('matters: only Matter Type is sent (the page rule), an open date side is left out', () => {
    const data = buildSearchDataParams(
      criteria({
        domain: 'matters',
        documentTypes: ['100000001'],
        fileTypes: ['pdf'],
        matterTypes: ['mt-1'],
        datePreset: 'custom',
        dateRange: { from: '2026-11-01', to: null },
      })
    );
    const sees = pageSeesParams(data);

    expect(sees.documentTypes).toBeUndefined();
    expect(sees.fileTypes).toBeUndefined();
    expect(sees.matterTypes).toEqual(['mt-1']);
    expect(sees.dateFrom).toBe('2026-11-01');
    expect(data).not.toContain('dateTo');
  });

  it('nothing selected: no filter keys at all', () => {
    expect(buildSearchDataParams(criteria({}))).toBe('query=indemnity&domain=documents');
  });
});

describe('normalizeCriteria — stored criteria', () => {
  it('re-resolves a stored preset against today, so "Last 7 days" stays relative', () => {
    const restored = normalizeCriteria(
      { query: '', domain: 'documents', datePreset: 'last7', dateRange: { from: '2020-01-01', to: '2020-01-08' } },
      NOW
    );

    expect(restored.datePreset).toBe('last7');
    expect(restored.dateRange).toEqual({ from: '2026-11-17', to: '2026-11-24' });
  });

  it('keeps a custom range as typed', () => {
    const restored = normalizeCriteria(
      { datePreset: 'custom', dateRange: { from: '2026-01-05', to: null } },
      NOW
    );

    expect(restored.dateRange).toEqual({ from: '2026-01-05', to: null });
  });

  it('reads the pre-#3131 shape: file type kept, placeholder document/matter types dropped, preset id resolved', () => {
    const restored = normalizeCriteria(
      {
        query: 'q',
        domain: 'matters',
        documentType: 'contract',
        fileType: 'pdf',
        matterType: 'litigation',
        dateRange: 'last30',
      },
      NOW
    );

    expect(restored.domain).toBe('matters');
    expect(restored.documentTypes).toEqual([]);
    expect(restored.fileTypes).toEqual(['pdf']);
    expect(restored.matterTypes).toEqual([]);
    expect(restored.datePreset).toBe('last30');
    expect(restored.dateRange).toEqual({ from: '2026-10-25', to: '2026-11-24' });
  });

  it('a legacy "custom" preset (it never had dates) and junk read as Off', () => {
    expect(normalizeCriteria({ dateRange: 'custom' }, NOW).datePreset).toBe('off');
    expect(normalizeCriteria({ dateRange: 'custom' }, NOW).dateRange).toBeNull();
    expect(normalizeCriteria('nonsense', NOW).datePreset).toBe('off');
    expect(normalizeCriteria({ documentTypes: [1, 2] }, NOW).documentTypes).toEqual([]);
  });
});

describe('SemanticSearchCriteriaTool — Search', () => {
  function renderTool(): void {
    render(
      <FluentProvider theme={webLightTheme}>
        <SemanticSearchCriteriaTool />
      </FluentProvider>
    );
  }

  it('opens the search page with the stored criteria in the keys the page reads', () => {
    window.localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify(
        criteria({
          documentTypes: ['100000002'],
          fileTypes: ['pdf'],
          datePreset: 'custom',
          dateRange: { from: '2026-11-01', to: '2026-11-02' },
        })
      )
    );
    renderTool();

    fireEvent.click(screen.getByTestId('semantic-search-criteria-submit'));

    expect(mockNavigateTo).toHaveBeenCalledTimes(1);
    const sees = pageSees(mockNavigateTo.mock.calls[0][0].data as string);
    expect(sees.documentTypes).toEqual(['100000002']);
    expect(sees.fileTypes).toEqual(['pdf']);
    expect(sees.dateFrom).toBe('2026-11-01');
    expect(sees.dateTo).toBe('2026-11-02');
  });

  it('shows the selection with the option labels in the shared dropdowns', () => {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(criteria({ documentTypes: ['100000002'] })));
    renderTool();

    expect(screen.getByRole('combobox', { name: /Document Type/ }).textContent).toContain('NDA');
  });

  it('"Also add to Workspace" sends the filter labels and dates to the widget', () => {
    window.localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify(
        criteria({
          documentTypes: ['100000001'],
          matterTypes: ['mt-1'],
          datePreset: 'custom',
          dateRange: { from: '2026-11-01', to: null },
        })
      )
    );
    renderTool();

    fireEvent.click(screen.getByRole('checkbox', { name: 'Also add to Workspace' }));
    fireEvent.click(screen.getByTestId('semantic-search-criteria-submit'));

    expect(mockDispatch).toHaveBeenCalledTimes(1);
    const widgetData = mockDispatch.mock.calls[0][1].widgetData;
    expect(widgetData.documentTypes).toEqual(['Contract']);
    expect(widgetData.fileTypes).toEqual([]);
    expect(widgetData.matterTypes).toEqual(['Litigation']);
    expect(widgetData.dateFrom).toBe('2026-11-01');
    expect(widgetData.dateTo).toBeNull();
  });

  it('a query with "&" and "+" reaches the page whole', () => {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(criteria({ query: 'M&A + indemnity' })));
    renderTool();

    fireEvent.click(screen.getByTestId('semantic-search-criteria-submit'));

    expect(pageSees(mockNavigateTo.mock.calls[0][0].data as string).query).toBe('M&A + indemnity');
  });

  it('says when the filter options could not be loaded, and Retry reloads them', () => {
    mockOptionsError = 'Failed to load filter options';
    renderTool();

    expect(screen.getByTestId('semantic-search-criteria-options-error').textContent).toContain(
      "Couldn't load the filter options"
    );
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(mockRefresh).toHaveBeenCalledTimes(1);
  });

  it('drops a stored custom date that is not a real date', () => {
    expect(
      normalizeCriteria({ datePreset: 'custom', dateRange: { from: '2026-13-45', to: '2026-02-30' } }, NOW).dateRange
    ).toBeNull();
  });

  describe('date range', () => {
    // Fake only Date: Fluent's dropdown needs real timers.
    const fakeDateOnly = (now: Date) =>
      jest.useFakeTimers({
        now,
        doNotFake: [
          'setTimeout',
          'clearTimeout',
          'setInterval',
          'clearInterval',
          'setImmediate',
          'clearImmediate',
          'queueMicrotask',
          'nextTick',
          'requestAnimationFrame',
          'cancelAnimationFrame',
          'requestIdleCallback',
          'cancelIdleCallback',
          'performance',
          'hrtime',
        ],
      });

    afterEach(() => {
      jest.useRealTimers();
    });

    it('picking a preset stores the preset id and searches with its range', () => {
      fakeDateOnly(NOW);
      renderTool();

      fireEvent.click(screen.getByRole('combobox', { name: 'Date range preset' }));
      fireEvent.click(screen.getByRole('option', { name: 'Last 7 days' }));
      fireEvent.click(screen.getByTestId('semantic-search-criteria-submit'));

      const stored = JSON.parse(window.localStorage.getItem(STORAGE_KEY) as string) as PersistedCriteria;
      expect(stored.datePreset).toBe('last7');
      const sees = pageSees(mockNavigateTo.mock.calls[0][0].data as string);
      expect(sees.dateFrom).toBe('2026-11-17');
      expect(sees.dateTo).toBe('2026-11-24');
    });

    // On 8 January "This year" and "Last 7 days" are the same range; the picked one is stored.
    it('stores the preset picked, not one inferred from the dates', () => {
      fakeDateOnly(new Date(2026, 0, 8, 9, 0, 0));
      renderTool();

      fireEvent.click(screen.getByRole('combobox', { name: 'Date range preset' }));
      fireEvent.click(screen.getByRole('option', { name: 'This year' }));

      const stored = JSON.parse(window.localStorage.getItem(STORAGE_KEY) as string) as PersistedCriteria;
      expect(stored.datePreset).toBe('thisYear');
    });

    it('re-resolves the preset when Search is pressed on a later day', () => {
      fakeDateOnly(NOW);
      renderTool();
      fireEvent.click(screen.getByRole('combobox', { name: 'Date range preset' }));
      fireEvent.click(screen.getByRole('option', { name: 'Last 7 days' }));

      jest.setSystemTime(new Date(2026, 10, 26, 8, 0, 0));
      fireEvent.click(screen.getByTestId('semantic-search-criteria-submit'));

      const sees = pageSees(mockNavigateTo.mock.calls[0][0].data as string);
      expect(sees.dateFrom).toBe('2026-11-19');
      expect(sees.dateTo).toBe('2026-11-26');
    });
  });
});
