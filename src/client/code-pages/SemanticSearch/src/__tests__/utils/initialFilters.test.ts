/**
 * buildInitialFilters (#3131): the filters the page starts with, seeded from the URL envelope.
 * The Console's search criteria arrive this way; before #3131 Document Type and Matter Type were
 * not read from the URL at all.
 */

import { buildInitialFilters, DEFAULT_FILTERS } from '../../utils/initialFilters';
import { parseUrlParams } from '../../utils/parseUrlParams';

describe('buildInitialFilters', () => {
  it('starts from the defaults with no envelope', () => {
    expect(buildInitialFilters(undefined)).toEqual(DEFAULT_FILTERS);
  });

  it('seeds every filter the envelope carries', () => {
    const data = encodeURIComponent(
      'query=x&domain=documents&documentTypes=100000001,100000002&fileTypes=pdf&matterTypes=mt-1' +
        '&dateFrom=2026-11-01&dateTo=2026-11-20&threshold=70&searchMode=keywordOnly'
    );

    const filters = buildInitialFilters(parseUrlParams(`?data=${encodeURIComponent(data)}`));

    expect(filters).toEqual({
      documentTypes: ['100000001', '100000002'],
      fileTypes: ['pdf'],
      matterTypes: ['mt-1'],
      dateRange: { from: '2026-11-01', to: '2026-11-20' },
      threshold: 70,
      searchMode: 'keywordOnly',
    });
  });

  it('keeps the defaults for anything the envelope leaves out, and maps "hybrid" to rrf', () => {
    const filters = buildInitialFilters({ dateFrom: '2026-11-01', searchMode: 'hybrid' } as Parameters<
      typeof buildInitialFilters
    >[0]);

    expect(filters.documentTypes).toEqual([]);
    expect(filters.matterTypes).toEqual([]);
    expect(filters.dateRange).toEqual({ from: '2026-11-01', to: null });
    expect(filters.threshold).toBe(DEFAULT_FILTERS.threshold);
    expect(filters.searchMode).toBe('rrf');
  });
});
