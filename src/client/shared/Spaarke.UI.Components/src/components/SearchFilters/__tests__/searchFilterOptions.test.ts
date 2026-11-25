/**
 * searchFilterOptions (#3131): the option lists the SemanticSearch code page and the Console's
 * search criteria both offer. The values must be Dataverse's (option numbers, record ids), not
 * labels or invented ids, because the search page sends them to the search API as they are.
 */

import {
  loadSearchFilterOptions,
  fetchOptionsetOptions,
  fetchLookupOptions,
  clearSearchFilterOptionsCache,
  SEARCH_FILE_TYPE_OPTIONS,
} from '../searchFilterOptions';

function jsonResponse(body: unknown, status = 200): Response {
  return { ok: status >= 200 && status < 300, status, json: async () => body } as unknown as Response;
}

const fetchMock = jest.fn();

beforeEach(() => {
  clearSearchFilterOptionsCache();
  fetchMock.mockReset();
  (global as unknown as { fetch: jest.Mock }).fetch = fetchMock;
  jest.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  jest.restoreAllMocks();
});

it('reads Document Type from the choice metadata: option number as value, sorted by label', async () => {
  fetchMock.mockResolvedValue(
    jsonResponse({
      OptionSet: {
        Options: [
          { Value: 100000002, Label: { UserLocalizedLabel: { Label: 'NDA' } } },
          { Value: 100000001, Label: { UserLocalizedLabel: { Label: 'Contract' } } },
        ],
      },
    })
  );

  const options = await fetchOptionsetOptions('sprk_document', 'sprk_documenttype');

  expect(options).toEqual([
    { value: '100000001', label: 'Contract' },
    { value: '100000002', label: 'NDA' },
  ]);
  expect(fetchMock.mock.calls[0][0]).toContain(
    "EntityDefinitions(LogicalName='sprk_document')/Attributes(LogicalName='sprk_documenttype')"
  );
});

it('reads Matter Type from active sprk_mattertype_ref records: record id as value', async () => {
  fetchMock.mockResolvedValue(
    jsonResponse({ value: [{ sprk_mattertype_refid: 'mt-1', sprk_mattertypename: 'Litigation' }] })
  );

  const options = await fetchLookupOptions('sprk_mattertype_refs', 'sprk_mattertypename');

  expect(options).toEqual([{ value: 'mt-1', label: 'Litigation' }]);
  expect(fetchMock.mock.calls[0][0]).toContain('/api/data/v9.2/sprk_mattertype_refs?');
  expect(fetchMock.mock.calls[0][0]).toContain('statecode eq 0');
});

it('loads all three lists, file types from the fixed list', async () => {
  fetchMock.mockImplementation(async (url: string) =>
    url.includes('EntityDefinitions')
      ? jsonResponse({ OptionSet: { Options: [{ Value: 1, Label: { UserLocalizedLabel: { Label: 'Brief' } } }] } })
      : jsonResponse({ value: [{ sprk_mattertype_refid: 'mt-1', sprk_mattertypename: 'Litigation' }] })
  );

  const options = await loadSearchFilterOptions();

  expect(options.documentTypes).toEqual([{ value: '1', label: 'Brief' }]);
  expect(options.matterTypes).toEqual([{ value: 'mt-1', label: 'Litigation' }]);
  expect(options.fileTypes).toEqual(SEARCH_FILE_TYPE_OPTIONS);
});

it('caches a list until the cache is cleared', async () => {
  fetchMock.mockResolvedValue(jsonResponse({ value: [] }));

  await fetchLookupOptions('sprk_mattertype_refs', 'sprk_mattertypename');
  await fetchLookupOptions('sprk_mattertype_refs', 'sprk_mattertypename');
  expect(fetchMock).toHaveBeenCalledTimes(1);

  clearSearchFilterOptionsCache();
  await fetchLookupOptions('sprk_mattertype_refs', 'sprk_mattertypename');
  expect(fetchMock).toHaveBeenCalledTimes(2);
});

it('returns an empty list (not a throw) for a missing column, a failed read or a network error', async () => {
  fetchMock.mockResolvedValueOnce(jsonResponse({}, 404));
  expect(await fetchOptionsetOptions('sprk_document', 'sprk_missing')).toEqual([]);

  fetchMock.mockResolvedValueOnce(jsonResponse({}, 500));
  expect(await fetchLookupOptions('sprk_mattertype_refs', 'sprk_mattertypename')).toEqual([]);

  fetchMock.mockRejectedValueOnce(new Error('offline'));
  expect(await fetchOptionsetOptions('sprk_document', 'sprk_documenttype')).toEqual([]);
});
