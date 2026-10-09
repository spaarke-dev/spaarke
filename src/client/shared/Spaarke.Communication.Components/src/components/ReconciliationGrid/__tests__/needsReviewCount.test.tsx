/**
 * needsReviewCount.test.tsx - task 054: the aggregate card's count equals the Needs Review tab's.
 *
 * A fake IDataverseClient holds a fixture of sprk_communication rows and a small FetchXML evaluator
 * (and/or filters; eq/ne/null). The "tab" path runs the config's own FetchXML exactly as the grid does
 * (rows listed); the "card" path goes through loadNeedsReviewCount / ReconciliationAggregateCard. Both
 * must agree on the same fixture, including rows a naive status count would get wrong (inactive rows,
 * outgoing mail, null status).
 */
import * as React from 'react';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { FluentProvider, webDarkTheme } from '@fluentui/react-components';
import type { IDataverseClient } from '@spaarke/ui-components';
import * as fs from 'fs';
import * as path from 'path';
import { NEEDS_REVIEW_CONFIG_ID } from '../ReconciliationGrid';
import { buildCountFetchXml, loadNeedsReviewCount } from '../needsReviewCount';
import { ReconciliationAggregateCard } from '../ReconciliationAggregateCard';

const configJson = fs.readFileSync(path.join(__dirname, '..', 'needs-review.gridconfiguration.json'), 'utf8');
const CONFIG_FETCHXML: string = JSON.parse(configJson).source.fetchXml;

type Row = Record<string, number | null>;
const RESOLVED = 100000000;
const INCOMING = 100000000;
const FIXTURE: Row[] = [
  { statecode: 0, sprk_communicationtype: INCOMING, sprk_associationstatus: 100000001 }, // pending: counts
  { statecode: 0, sprk_communicationtype: INCOMING, sprk_associationstatus: 100000003 }, // suggested: counts
  { statecode: 0, sprk_communicationtype: INCOMING, sprk_associationstatus: null }, // null: counts
  { statecode: 0, sprk_communicationtype: INCOMING, sprk_associationstatus: RESOLVED }, // resolved: no
  { statecode: 1, sprk_communicationtype: INCOMING, sprk_associationstatus: 100000001 }, // inactive: no
  { statecode: 0, sprk_communicationtype: 100000001, sprk_associationstatus: 100000001 }, // outgoing: no
];
const EXPECTED = 3;

function evalFilter(el: Element, row: Row): boolean {
  const results = Array.from(el.children).map(k => {
    if (k.tagName === 'filter') return evalFilter(k, row);
    const v = row[k.getAttribute('attribute') as string];
    const op = k.getAttribute('operator');
    const want = Number(k.getAttribute('value'));
    if (op === 'eq') return v === want;
    if (op === 'ne') return v !== null && v !== undefined && v !== want;
    if (op === 'null') return v === null || v === undefined;
    throw new Error(`unsupported operator ${op}`);
  });
  return el.getAttribute('type') === 'or' ? results.some(Boolean) : results.every(Boolean);
}

function matching(fetchXml: string): Row[] {
  const doc = new DOMParser().parseFromString(fetchXml, 'text/xml');
  const entity = doc.documentElement.getElementsByTagName('entity')[0];
  const filter = Array.from(entity.children).find(c => c.tagName === 'filter');
  return FIXTURE.filter(r => (filter ? evalFilter(filter, r) : true));
}

function makeClient(overrides: Partial<IDataverseClient> = {}): jest.Mocked<IDataverseClient> {
  return {
    retrieveSavedQuery: jest.fn(),
    retrieveSavedQueriesForEntity: jest.fn(),
    retrieveEntityMetadata: jest.fn(),
    retrieveRecord: jest.fn(async () => ({ sprk_configjson: configJson })),
    retrieveMultipleRecords: jest.fn(async (_e: string, xml: string) => {
      const rows = matching(xml);
      const aggregate = /aggregate="true"/.test(xml);
      return {
        entities: (aggregate ? [{ sprk_needsreviewcount: rows.length }] : rows) as never,
        moreRecords: false,
      };
    }),
    ...overrides,
  } as unknown as jest.Mocked<IDataverseClient>;
}

const filterOf = (s: string): string =>
  new XMLSerializer().serializeToString(
    new DOMParser().parseFromString(s, 'text/xml').getElementsByTagName('filter')[0]
  );

describe('buildCountFetchXml', () => {
  it('keeps the filter verbatim and drops attributes and ordering', () => {
    const xml = buildCountFetchXml(CONFIG_FETCHXML)!;
    expect(filterOf(xml)).toBe(filterOf(CONFIG_FETCHXML));
    expect(xml).toContain('aggregate="true"');
    expect(xml).not.toContain('<order');
    expect(xml.match(/<attribute /g)).toHaveLength(1);
  });

  it('returns null for unparseable input', () => {
    expect(buildCountFetchXml('<fetch><entity')).toBeNull();
    expect(buildCountFetchXml('<fetch/>')).toBeNull();
  });
});

describe('loadNeedsReviewCount', () => {
  it('equals the number of rows the tab lists, on the same fixture', async () => {
    const client = makeClient();
    const tabRows = (await client.retrieveMultipleRecords('sprk_communication', CONFIG_FETCHXML)).entities;
    expect(tabRows).toHaveLength(EXPECTED);
    expect(await loadNeedsReviewCount(client)).toBe(tabRows.length);
    expect(client.retrieveRecord).toHaveBeenCalledWith('sprk_gridconfiguration', NEEDS_REVIEW_CONFIG_ID, [
      'sprk_configjson',
    ]);
  });

  it('is null (not 0) when the config, its JSON or the query is unavailable', async () => {
    expect(await loadNeedsReviewCount(makeClient({ retrieveRecord: jest.fn(async () => ({})) }))).toBeNull();
    expect(
      await loadNeedsReviewCount(makeClient({ retrieveRecord: jest.fn(async () => ({ sprk_configjson: '{bad' })) }))
    ).toBeNull();
    expect(
      await loadNeedsReviewCount(makeClient({ retrieveMultipleRecords: jest.fn().mockRejectedValue(new Error('x')) }))
    ).toBeNull();
  });

  it('is a genuine zero when the queue is empty', async () => {
    const client = makeClient({
      retrieveMultipleRecords: jest.fn(async () => ({ entities: [{ sprk_needsreviewcount: 0 }], moreRecords: false })),
    });
    expect(await loadNeedsReviewCount(client)).toBe(0);
  });

  it('issues exactly one count query (no separate status count)', async () => {
    const client = makeClient();
    await loadNeedsReviewCount(client);
    expect(client.retrieveMultipleRecords).toHaveBeenCalledTimes(1);
  });
});

describe('ReconciliationAggregateCard', () => {
  it('renders the same count as the tab and links to it, in dark theme', async () => {
    const onOpen = jest.fn();
    render(
      <FluentProvider theme={webDarkTheme}>
        <ReconciliationAggregateCard dataverseClient={makeClient()} onOpen={onOpen} />
      </FluentProvider>
    );
    await waitFor(() =>
      expect(screen.getByTestId('aggregate-count')).toHaveTextContent(`${EXPECTED} emails await a match confirmation`)
    );
    fireEvent.click(screen.getByText(/Open Email Review/));
    expect(onOpen).toHaveBeenCalledTimes(1);
    expect(screen.getAllByTestId('aggregate-card')).toHaveLength(1);
  });

  it('reads Missing when there is no client', () => {
    render(
      <FluentProvider theme={webDarkTheme}>
        <ReconciliationAggregateCard dataverseClient={undefined} onOpen={jest.fn()} />
      </FluentProvider>
    );
    expect(screen.getByTestId('aggregate-count')).toHaveTextContent('Missing');
  });
});
