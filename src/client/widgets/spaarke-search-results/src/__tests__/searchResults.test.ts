/**
 * @jest-environment node
 *
 * The mapping from task 022's tool results to the component's props (spaarkeai-mcp-bff-api-r2 task 035): documents vs records, the one-line
 * excerpt, link safety, the paging contract (`nextOffset`, never `hasMore`), the continuation call, and the notices. Pure; no React, no clock.
 */
import {
  ASK_FOR_MORE,
  MAY_BE_MORE,
  NARROW_ADVICE,
  NO_MORE_AFTER_THESE,
  PAGING_LIMIT,
  nextCallArguments,
  noticesFor,
  parseSearchResult,
  queryOf,
  safeLink,
  toResultItems,
  type SearchPage,
} from '../search/searchResults';

const page = (overrides: Partial<SearchPage> = {}): SearchPage => ({
  kind: 'documents',
  hits: [{ id: 'a', title: 'A', excerpt: '' }],
  limit: 20,
  offset: 0,
  nextOffset: null,
  truncated: false,
  partial: false,
  ...overrides,
});

describe('parseSearchResult', () => {
  it('reads a document page', () => {
    const parsed = parseSearchResult({
      items: [
        {
          id: 'd1',
          name: 'MSA',
          fileType: 'pdf',
          parentType: 'matter',
          parentName: 'Acme',
          snippet: 'text',
          openLink: 'https://x.example/a?b=1',
        },
      ],
      limit: 10,
      nextOffset: 10,
      truncated: true,
      warnings: ['PARTIAL_RESULTS'],
    });

    expect(parsed).toEqual({
      status: 'page',
      page: {
        kind: 'documents',
        hits: [{ id: 'd1', title: 'MSA', excerpt: 'pdf · in matter Acme — text', link: 'https://x.example/a?b=1' }],
        limit: 10,
        offset: 0,
        nextOffset: 10,
        truncated: true,
        partial: true,
      },
    });
  });

  it('tells records from documents by the record type, and writes the type and references into the line', () => {
    const parsed = parseSearchResult({
      items: [{ id: 'r1', name: 'Acme', type: 'invoice', referenceNumbers: ['INV-1'] }],
      limit: 20,
    });
    expect(parsed.status === 'page' && parsed.page.kind).toBe('records');
    expect(parsed.status === 'page' && parsed.page.hits[0].excerpt).toBe('Invoice · ref INV-1');
  });

  it('treats hasMore as nothing: only nextOffset continues a list', () => {
    const parsed = parseSearchResult({ items: [{ id: 'd1', name: 'A' }], limit: 1, hasMore: true, nextOffset: null });
    expect(parsed.status === 'page' && parsed.page.nextOffset).toBeNull();
  });

  it.each([[0], [-5], [1.5], ['20'], [null], [undefined]])('ignores a nextOffset of %p', value => {
    const parsed = parseSearchResult({ items: [{ id: 'd1', name: 'A' }], limit: 20, nextOffset: value });
    expect(parsed.status === 'page' && parsed.page.nextOffset).toBeNull();
  });

  it('reads the page offset, 0 when it is missing or not a whole number', () => {
    const offsetOf = (offset: unknown) => {
      const parsed = parseSearchResult({ items: [{ id: 'd1', name: 'A' }], limit: 20, offset });
      return parsed.status === 'page' ? parsed.page.offset : -1;
    };
    expect(offsetOf(40)).toBe(40);
    expect([offsetOf(undefined), offsetOf(-3), offsetOf(1.5), offsetOf('40')]).toEqual([0, 0, 0, 0]);
  });

  it('drops a row with no id, de-duplicates by id and falls back to a title', () => {
    const parsed = parseSearchResult({
      items: [{ name: 'no id' }, { id: 'd1' }, { id: 'd1', name: 'again' }, 'junk', null],
    });
    expect(parsed.status === 'page' && parsed.page.hits).toEqual([
      { id: 'd1', title: 'Untitled document', excerpt: '', link: undefined },
    ]);
  });

  it('turns a failure body into its fixed message', () => {
    expect(parseSearchResult({ reasonCode: 'QUERY_REQUIRED', message: 'Send a query.', status: 400 })).toEqual({
      status: 'error',
      message: 'Send a query.',
    });
  });

  it('uses the transport text when the call produced no body, and a fixed sentence when there is nothing', () => {
    expect(parseSearchResult(undefined, { isError: true, message: 'The host did not answer.' })).toEqual({
      status: 'error',
      message: 'The host did not answer.',
    });
    expect(parseSearchResult(undefined)).toEqual({
      status: 'error',
      message: 'The search results could not be loaded.',
    });
  });

  it('is an error, not a list, when the call failed even if the body has items', () => {
    expect(parseSearchResult({ items: [{ id: 'x', name: 'x' }] }, { isError: true, message: 'failed' }).status).toBe(
      'error'
    );
  });
});

describe('safeLink', () => {
  it.each([
    ['https://org.example/main.aspx?id=1', 'https://org.example/main.aspx?id=1'],
    ['http://org.example/a', 'http://org.example/a'],
  ])('keeps %s', (input, expected) => expect(safeLink(input)).toBe(expected));

  it.each([
    ['javascript:alert(1)'],
    ['data:text/html,x'],
    ['file:///c:/x'],
    ['//org.example/a'],
    ['/relative'],
    ['  '],
    [42],
    [null],
    [undefined],
  ])('refuses %p', value => expect(safeLink(value)).toBeUndefined());
});

describe('toResultItems and queryOf', () => {
  it('maps hits onto the component props with a score of 0 (never shown)', () => {
    expect(toResultItems([{ id: 'a', title: 'T', excerpt: 'E', link: 'https://x.example/' }])).toEqual([
      { id: 'a', title: 'T', excerpt: 'E', score: 0, url: 'https://x.example/' },
    ]);
  });

  it('takes the query from the call, or a neutral phrase', () => {
    expect(queryOf({ query: '  indemnity ' })).toBe('indemnity');
    expect(queryOf({ query: 5 })).toBe('your search');
    expect(queryOf(undefined)).toBe('your search');
  });
});

describe('nextCallArguments', () => {
  it('repeats a document search with only its own arguments and the new offset', () => {
    expect(
      nextCallArguments(
        'documents',
        { query: 'q', entityType: 'matter', entityId: 'id', limit: 5, recordTypes: ['x'], other: 1 },
        5
      )
    ).toEqual({
      tool: 'spaarke_search_documents',
      args: { query: 'q', offset: 5, limit: 5, entityType: 'matter', entityId: 'id' },
    });
  });

  it('repeats a record search with recordTypes', () => {
    expect(nextCallArguments('records', { query: 'q', recordTypes: ['matter'], entityType: 'matter' }, 20)).toEqual({
      tool: 'spaarke_find_records',
      args: { query: 'q', offset: 20, recordTypes: ['matter'] },
    });
  });

  it('is null when the host sent no query', () => {
    expect(nextCallArguments('documents', undefined, 20)).toBeNull();
    expect(nextCallArguments('documents', { limit: 20 }, 20)).toBeNull();
  });

  it('asks for the largest offset the tools accept, and for none above it (they would refuse it)', () => {
    expect(nextCallArguments('documents', { query: 'q' }, 1000)).toEqual({
      tool: 'spaarke_search_documents',
      args: { query: 'q', offset: 1000 },
    });
    expect(nextCallArguments('documents', { query: 'q' }, 1001)).toBeNull();
    expect(nextCallArguments('records', { query: 'q' }, 1003)).toBeNull();
  });
});

describe('noticesFor', () => {
  it('says the list was cut and the index may be incomplete', () => {
    expect(noticesFor(page({ truncated: true }), true, true).incomplete).toEqual([
      'Some results were left out to keep this list short.',
      'These results may be incomplete.',
    ]);
  });

  it('adds no ending when Load more is offered or nothing was found', () => {
    expect(noticesFor(page({ nextOffset: 20 }), false, true).ending).toBeNull();
    expect(noticesFor(page({ hits: [] }), false, false).ending).toBeNull();
  });

  it('advises narrowing for a full or cut page with no offset (a document search over every record)', () => {
    expect(noticesFor(page({ limit: 1 }), false, false).ending).toBe(NARROW_ADVICE);
    expect(noticesFor(page({ truncated: true }), false, false).ending).toBe(NARROW_ADVICE);
  });

  it('does not call a short page the end', () => {
    expect(noticesFor(page(), false, false).ending).toBe(MAY_BE_MORE);
  });

  it('points at the chat when an offset was advised but the widget cannot ask for it', () => {
    expect(noticesFor(page({ nextOffset: 20 }), false, false).ending).toBe(ASK_FOR_MORE);
  });

  it('says to narrow the search, not to ask the chat, when the advised offset is above the tools bound', () => {
    expect(noticesFor(page({ nextOffset: 1001 }), false, false).ending).toBe(PAGING_LIMIT);
    expect(noticesFor(page({ nextOffset: 1000 }), false, false).ending).toBe(ASK_FOR_MORE); // at the bound it is still a page the tools accept
  });

  it('ends a list the reader paged into when the newest page is empty, and never calls an empty first page an end', () => {
    expect(noticesFor(page({ hits: [], offset: 20 }), false, false, 2).ending).toBe(NO_MORE_AFTER_THESE);
    expect(noticesFor(page({ hits: [] }), false, false, 1).ending).toBeNull();
  });

  it('says a first page that opened past the start and found nothing is past the end', () => {
    expect(noticesFor(page({ hits: [], offset: 40 }), false, false, 1).ending).toBe(
      'No more matches after the first 40.'
    );
  });
});
