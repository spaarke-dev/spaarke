/**
 * Task 119 (UAT round 12 O4/W2): openable Find rows must look and behave openable — every similar document,
 * parent record and matching record is a named, keyboard-operable button with an "Open in Spaarke" cue, and
 * without `onOpenRecord` none of them is a button (never a broken link).
 */
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { FindResultsList, type FindResultNode } from '../FindResultsList';
import type { RecordMatch, UseFindRecordMatchesResult } from '../../hooks/useFindRecordMatches';

class MockIntersectionObserver {
  observe = jest.fn();
  unobserve = jest.fn();
  disconnect = jest.fn();
}
beforeAll(() => {
  (globalThis as unknown as { IntersectionObserver: unknown }).IntersectionObserver = MockIntersectionObserver;
  (globalThis as unknown as { ResizeObserver: unknown }).ResizeObserver = MockIntersectionObserver;
});

const DOC_ID = 'aaaaaaaa-1111-2222-3333-444444444444';
const MATTER_ID = 'bbbbbbbb-1111-2222-3333-444444444444';
const INVOICE_ID = 'cccccccc-1111-2222-3333-444444444444';

const docNode: FindResultNode = {
  id: DOC_ID,
  type: 'related',
  data: { label: 'MSA Draft', documentType: 'Contract', similarity: 0.9 },
};

function record(recordId: string, recordType: string, recordName: string): RecordMatch {
  return { recordId, recordType, recordName, confidenceScore: 0.8 };
}

function records(list: RecordMatch[]): UseFindRecordMatchesResult {
  return {
    status: 'ready',
    records: list,
    seedSource: 'keywords',
    isLoadingMore: false,
    hasMore: false,
    error: null,
    loadMoreError: null,
    sentinelRef: jest.fn(),
  };
}

function renderList(onOpenRecord?: (entityType: string, recordId: string) => void) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <FindResultsList
        documents={{ kind: 'loaded', nodes: [docNode], partialResultsWarning: null }}
        announce={jest.fn()}
        records={records([
          record(MATTER_ID, 'sprk_matter', 'Acme v. Globex'),
          record(INVOICE_ID, 'sprk_invoice', 'INV-7'),
        ])}
        {...(onOpenRecord ? { onOpenRecord } : {})}
      />
    </FluentProvider>
  );
}

describe('Find rows open what they show (task 119)', () => {
  it('a similar document opens sprk_document/{id}', async () => {
    const onOpen = jest.fn();
    renderList(onOpen);
    await userEvent.click(screen.getByRole('button', { name: /MSA Draft/ }));
    expect(onOpen).toHaveBeenCalledWith('sprk_document', DOC_ID);
  });

  it('a matching record opens its own entity and id (matter, invoice)', async () => {
    const onOpen = jest.fn();
    renderList(onOpen);
    await userEvent.click(screen.getByRole('button', { name: /Acme v\. Globex/ }));
    expect(onOpen).toHaveBeenLastCalledWith('sprk_matter', MATTER_ID);
    await userEvent.click(screen.getByRole('button', { name: /INV-7/ }));
    expect(onOpen).toHaveBeenLastCalledWith('sprk_invoice', INVOICE_ID);
  });

  it('Enter and Space activate a focused row', async () => {
    const onOpen = jest.fn();
    renderList(onOpen);
    const row = screen.getByRole('button', { name: /Acme v\. Globex/ });
    row.focus();
    await userEvent.keyboard('{Enter}');
    await userEvent.keyboard(' ');
    expect(onOpen).toHaveBeenCalledTimes(2);
  });

  it('openable rows carry the "Open in Spaarke" cue', () => {
    renderList(jest.fn());
    expect(screen.getByRole('button', { name: /MSA Draft/ }).getAttribute('title')).toBe('Open in Spaarke');
    expect(screen.getAllByTestId('find-record-row')[0]?.getAttribute('title')).toBe('Open in Spaarke');
  });

  it('without onOpenRecord (no ORG_URL / capability) nothing is a button', () => {
    renderList();
    expect(screen.queryByRole('button', { name: /MSA Draft/ })).toBeNull();
    expect(screen.queryByRole('button', { name: /Acme v\. Globex/ })).toBeNull();
    expect(screen.getByText('MSA Draft')).toBeTruthy();
  });
});
