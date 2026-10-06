/**
 * Unit tests for FindResultsList (spaarkeai-word-add-in-r1 task 034, extended task 077, restructured
 * task 092 / UAT-3+UAT-8 — 2026-10-03 round-3 UAT, `notes/042-uat-round3-2026-10-03.md` §1).
 *
 * Covers:
 * - Two INDEPENDENT sections ("Similar Documents" / "Matching records"), each with its own
 *   loading, empty and error state — neither section's state affects the other's render (UAT-3/8).
 * - Task 102: each section has its OWN scroll container, with no `maxHeight` cap anywhere.
 * - Opening a document row / a parent (hub) row / a matching-record row via `onOpenRecord`, gated by
 *   its mere presence (NFR-10) — omitted means every row is plain, non-interactive text.
 * - A hub row with no real record id (the `thread-` shape) never renders as a button, even when
 *   `onOpenRecord` is provided.
 * - Matching records show a confidence percentage and up to 3 match reasons; `<em>` becomes bold;
 *   any other markup (script/img) is dropped, never executed (security constraint).
 * - No pager of any kind (ADR-051), preserved from the pre-092 suite.
 */

import React from 'react';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import * as fs from 'fs';
import * as path from 'path';
import { FindResultsList, renderMatchReason, type DocumentsResultState, type FindResultNode } from '../FindResultsList';
import type { UseFindRecordMatchesResult, RecordMatch } from '../../hooks/useFindRecordMatches';

// jsdom does not implement IntersectionObserver.
class IntersectionObserverMock {
  observe(): void {
    /* no-op */
  }
  unobserve(): void {
    /* no-op */
  }
  disconnect(): void {
    /* no-op */
  }
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(globalThis as any).IntersectionObserver = (globalThis as any).IntersectionObserver ?? IntersectionObserverMock;

const renderWithProvider = (ui: React.ReactElement) =>
  render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

function resultNode(id: string, label: string, similarity: number, parentEntityName?: string): FindResultNode {
  return {
    id,
    type: 'related',
    data: { label, documentType: 'Contract', similarity, parentEntityName: parentEntityName ?? null },
  };
}

function hubNode(id: string, type: 'matter' | 'project' | 'invoice' | 'email', label: string): FindResultNode {
  return { id, type, data: { label } };
}

function sourceNode(id: string, label: string): FindResultNode {
  return { id, type: 'source', data: { label } };
}

function loaded(nodes: FindResultNode[], partialResultsWarning: string | null = null): DocumentsResultState {
  return { kind: 'loaded', nodes, partialResultsWarning };
}

function recordMatch(overrides: Partial<RecordMatch> = {}): RecordMatch {
  return {
    recordId: 'm-1',
    recordType: 'sprk_matter',
    recordName: 'Acme v. Globex',
    confidenceScore: 0.87,
    ...overrides,
  };
}

function recordsResult(overrides: Partial<UseFindRecordMatchesResult> = {}): UseFindRecordMatchesResult {
  return {
    status: 'ready',
    records: [
      recordMatch(),
      recordMatch({ recordId: 'p-1', recordType: 'sprk_project', recordName: 'Globex Integration' }),
    ],
    seedSource: 'keywords',
    isLoadingMore: false,
    hasMore: true,
    error: null,
    loadMoreError: null,
    sentinelRef: jest.fn(),
    ...overrides,
  };
}

describe('FindResultsList', () => {
  describe('two sections, each with its own scroll container (task 102, UAT round 6 item 4)', () => {
    it('renders "Similar Documents" and "Matching records" as separate sections with separate scrollers', () => {
      const announce = jest.fn();
      renderWithProvider(
        <FindResultsList
          documents={loaded([resultNode('doc-1', 'MSA Draft', 0.9)])}
          announce={announce}
          records={recordsResult()}
        />
      );

      const docsSection = screen.getByTestId('find-documents-section');
      const recordsSection = screen.getByTestId('find-records-section');
      expect(within(docsSection).getByText('Similar Documents')).toBeTruthy();
      expect(within(recordsSection).getByText('Matching records')).toBeTruthy();
      expect(screen.queryByText('Most similar documents')).toBeNull();

      const docsScroll = screen.getByTestId('find-documents-scroll');
      const recordsScroll = screen.getByTestId('find-records-scroll');
      expect(docsScroll).not.toBe(recordsScroll);
      expect(docsSection.contains(docsScroll)).toBe(true);
      expect(recordsSection.contains(recordsScroll)).toBe(true);
      expect(docsScroll.contains(recordsScroll)).toBe(false);
      expect(within(docsScroll).getByText('MSA Draft')).toBeTruthy();
      expect(within(recordsScroll).getByText('Acme v. Globex')).toBeTruthy();
      // The divider sits between the two sections.
      expect(screen.getByRole('separator')).toBeTruthy();
    });

    it('the pinned "this document\'s record" row lives with Similar Documents', () => {
      renderWithProvider(
        <FindResultsList
          documents={loaded([resultNode('doc-1', 'MSA Draft', 0.9), hubNode('matter-m1', 'matter', 'Smith v Smith')])}
          announce={jest.fn()}
          records={recordsResult()}
        />
      );
      expect(within(screen.getByTestId('find-documents-scroll')).getByTestId('find-results-hub-section')).toBeTruthy();
    });

    it('without records, only the documents pane renders (no divider)', () => {
      renderWithProvider(<FindResultsList documents={loaded([resultNode('doc-1', 'A', 0.9)])} announce={jest.fn()} />);
      expect(screen.queryByRole('separator')).toBeNull();
      expect(screen.getByTestId('find-documents-scroll')).toBeTruthy();
    });

    it('never has a 360px maxHeight anywhere in the component source (regression guard)', () => {
      // jsdom does not resolve real CSS for Griffel's atomic classes, so the layout invariant this
      // pins is checked at the source level: the pre-092 fixed-height cap must never come back. Doc
      // comments are stripped first — this FILE's own header prose mentions "360px" historically
      // (explaining what was removed), which must not false-positive this guard.
      const source = fs.readFileSync(path.join(__dirname, '../FindResultsList.tsx'), 'utf8');
      const code = source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/.*$/gm, '');
      expect(code).not.toMatch(/360px/);
      expect(code).not.toMatch(/maxHeight/i);
    });
  });

  describe('Documents section — independent loading/error/empty state (UAT-3/8)', () => {
    it('loading: shows its own spinner, and the Matching records section is UNAFFECTED', () => {
      const announce = jest.fn();
      renderWithProvider(
        <FindResultsList documents={{ kind: 'loading' }} announce={announce} records={recordsResult()} />
      );

      expect(screen.getByText('Finding similar documents…')).toBeTruthy();
      // Records already rendered, proving records never wait on documents.
      expect(screen.getByText('Acme v. Globex')).toBeTruthy();
    });

    it('error: shows its own message, and the Matching records section is UNAFFECTED', () => {
      const announce = jest.fn();
      renderWithProvider(
        <FindResultsList
          documents={{ kind: 'error', message: 'Network down' }}
          announce={announce}
          records={recordsResult()}
        />
      );

      expect(screen.getByText(/Couldn.t load similar documents/)).toBeTruthy();
      expect(screen.getByText('Network down')).toBeTruthy();
      expect(screen.getByText('Acme v. Globex')).toBeTruthy();
    });

    it('loaded + empty: renders the exact copy "No similar documents found"', () => {
      const announce = jest.fn();
      renderWithProvider(<FindResultsList documents={loaded([])} announce={announce} />);

      expect(screen.getByText('No similar documents found')).toBeTruthy();
      expect(announce).toHaveBeenCalledWith('No similar documents found.', 'polite');
    });

    it('the source node alone (no result rows, no hubs) still renders the empty state', () => {
      const announce = jest.fn();
      renderWithProvider(
        <FindResultsList documents={loaded([sourceNode('src-1', 'Source Doc')])} announce={announce} />
      );

      expect(screen.getByText('No similar documents found')).toBeTruthy();
    });

    it('a hub node with zero similarity matches still shows the empty state, with the hub section retained', () => {
      const announce = jest.fn();
      renderWithProvider(
        <FindResultsList
          documents={loaded([hubNode('matter-matter1', 'matter', 'Smith v Smith')])}
          announce={announce}
        />
      );

      expect(screen.getByText('No similar documents found')).toBeTruthy();
      expect(screen.getByTestId('find-results-hub-section')).toBeTruthy();
      expect(screen.getByText(/Matter: Smith v Smith/)).toBeTruthy();
    });
  });

  describe('Matching records section — independent loading/error/empty state (UAT-3/8)', () => {
    it('loading: shows its own spinner, and the Documents section is UNAFFECTED', () => {
      const announce = jest.fn();
      renderWithProvider(
        <FindResultsList
          documents={loaded([resultNode('doc-1', 'MSA Draft', 0.9)])}
          announce={announce}
          records={recordsResult({ status: 'loading', records: [], hasMore: false })}
        />
      );

      expect(screen.getByText('Finding matching records…')).toBeTruthy();
      expect(screen.getByText('MSA Draft')).toBeTruthy();
    });

    it('error: shows its own message, and the Documents section is UNAFFECTED', () => {
      const announce = jest.fn();
      renderWithProvider(
        <FindResultsList
          documents={loaded([resultNode('doc-1', 'MSA Draft', 0.9)])}
          announce={announce}
          records={recordsResult({ status: 'error', records: [], hasMore: false, error: 'Boom' })}
        />
      );

      expect(screen.getByText(/Couldn.t load matching records\. Boom/)).toBeTruthy();
      expect(screen.getByText('MSA Draft')).toBeTruthy();
    });

    it('ready + empty: renders the exact copy "No matching records found"', () => {
      const announce = jest.fn();
      renderWithProvider(
        <FindResultsList
          documents={loaded([])}
          announce={announce}
          records={recordsResult({ records: [], hasMore: false })}
        />
      );

      expect(screen.getByText('No matching records found')).toBeTruthy();
    });

    it('no seed: does not claim there are no matching records (none were searched)', () => {
      const announce = jest.fn();
      renderWithProvider(
        <FindResultsList
          documents={loaded([])}
          announce={announce}
          records={recordsResult({ status: 'no-seed', records: [], seedSource: null, hasMore: false })}
        />
      );

      expect(screen.getByText('No similar documents found')).toBeTruthy();
      expect(screen.queryByText(/matching records found/i)).toBeNull();
      expect(screen.getByText(/none is available for it yet\./)).toBeTruthy();
    });

    it('omitting records entirely renders no Matching records section at all', () => {
      const announce = jest.fn();
      renderWithProvider(
        <FindResultsList documents={loaded([resultNode('doc-1', 'MSA Draft', 0.9)])} announce={announce} />
      );

      expect(screen.queryByText('Matching records')).toBeNull();
      expect(screen.queryByTestId('find-records-section')).toBeNull();
    });
  });

  describe('no pager of any kind (ADR-051)', () => {
    it('renders no numbered pages, prev/next, chevron, or "Load more" control — even with many results', () => {
      const announce = jest.fn();
      const nodes = Array.from({ length: 45 }, (_, i) => resultNode(`doc-${i}`, `Document ${i}`, 0.5 + i / 100));

      renderWithProvider(<FindResultsList documents={loaded(nodes)} announce={announce} />);

      expect(screen.queryByText(/load more/i)).toBeNull();
      expect(screen.queryByText(/next page/i)).toBeNull();
      expect(screen.queryByText(/previous page/i)).toBeNull();
      expect(screen.queryByText(/^page \d+/i)).toBeNull();

      const pagerNamePattern = /load more|next|previous|prev|page \d+|»|›|‹|«/i;
      const buttons = screen.queryAllByRole('button');
      for (const button of buttons) {
        expect(button.textContent ?? '').not.toMatch(pagerNamePattern);
      }

      expect(screen.queryByRole('navigation')).toBeNull();
    });
  });

  describe('hub nodes are never presented as similarity matches', () => {
    it('a hub node renders in its own, distinctly labeled section, separate from ranked rows', () => {
      const announce = jest.fn();
      const nodes = [resultNode('doc-1', 'MSA Draft', 0.91), hubNode('matter-matter1', 'matter', 'Smith v Smith')];

      renderWithProvider(<FindResultsList documents={loaded(nodes)} announce={announce} />);

      const hubSection = screen.getByTestId('find-results-hub-section');
      expect(hubSection.textContent).toContain('Matter: Smith v Smith');
      expect(hubSection.textContent).toMatch(/not a similarity match/i);

      const scrollArea = screen.getByTestId('find-documents-scroll');
      expect(within(scrollArea).getByRole('list', { name: 'Similar Documents' }).textContent).not.toContain(
        'Smith v Smith'
      );
    });
  });

  describe('row labels (task 102 label check — server placeholders are not shown as data)', () => {
    it('a hub whose name is the server placeholder (label === type) reads "Matter (name unavailable)", not "Matter: Matter"', () => {
      renderWithProvider(
        <FindResultsList documents={loaded([hubNode('matter-m1', 'matter', 'Matter')])} announce={jest.fn()} />
      );
      const hub = screen.getByTestId('find-results-hub-section');
      expect(hub.textContent).toContain('Matter (name unavailable)');
      expect(hub.textContent).not.toContain('Matter: Matter');
    });

    it('a document with the server fallback type "Unknown" omits it from the meta line', () => {
      const node: FindResultNode = {
        id: 'd1',
        type: 'related',
        data: { label: 'SEC FORM 4_2.pdf', documentType: 'Unknown', similarity: 1 },
      };
      renderWithProvider(<FindResultsList documents={loaded([node])} announce={jest.fn()} />);
      expect(screen.getByText('100% match')).toBeTruthy();
      expect(screen.queryByText(/Unknown/)).toBeNull();
    });
  });

  describe('opening a row (task 092, UAT-3 — gated by onOpenRecord/NFR-10)', () => {
    it('document row: calls onOpenRecord with sprk_document + the cleaned id, and is keyboard-operable', async () => {
      const announce = jest.fn();
      const onOpenRecord = jest.fn();
      renderWithProvider(
        <FindResultsList
          documents={loaded([resultNode('{AAAAAAAA-1111-2222-3333-444444444444}', 'MSA Draft', 0.9)])}
          announce={announce}
          onOpenRecord={onOpenRecord}
        />
      );

      const row = screen.getByRole('button', { name: /MSA Draft/ });
      await userEvent.click(row);
      expect(onOpenRecord).toHaveBeenCalledWith('sprk_document', 'aaaaaaaa-1111-2222-3333-444444444444');

      onOpenRecord.mockClear();
      row.focus();
      await userEvent.keyboard('{Enter}');
      expect(onOpenRecord).toHaveBeenCalledTimes(1);
    });

    it('document row: when onOpenRecord is omitted, renders as non-interactive text (no button role)', () => {
      const announce = jest.fn();
      renderWithProvider(
        <FindResultsList documents={loaded([resultNode('doc-1', 'MSA Draft', 0.9)])} announce={announce} />
      );

      expect(screen.queryByRole('button', { name: /MSA Draft/ })).toBeNull();
      expect(screen.getByText('MSA Draft')).toBeTruthy();
    });

    it('a valid hub row (matter) opens via onOpenRecord with sprk_matter + the real id', async () => {
      const announce = jest.fn();
      const onOpenRecord = jest.fn();
      const nodes = [
        resultNode('doc-1', 'MSA Draft', 0.9),
        hubNode('matter-bbbbbbbb-1111-2222-3333-444444444444', 'matter', 'Smith v Smith'),
      ];

      renderWithProvider(<FindResultsList documents={loaded(nodes)} announce={announce} onOpenRecord={onOpenRecord} />);

      const hubRow = screen.getByRole('button', { name: /Matter: Smith v Smith/ });
      await userEvent.click(hubRow);
      expect(onOpenRecord).toHaveBeenCalledWith('sprk_matter', 'bbbbbbbb-1111-2222-3333-444444444444');
    });

    it('an email hub row opens as sprk_document (the parent email document, not a separate entity)', async () => {
      const announce = jest.fn();
      const onOpenRecord = jest.fn();
      const nodes = [hubNode('email-cccccccc-1111-2222-3333-444444444444', 'email', 'Re: Contract')];

      renderWithProvider(<FindResultsList documents={loaded(nodes)} announce={announce} onOpenRecord={onOpenRecord} />);

      const hubRow = screen.getByRole('button', { name: /Email: Re: Contract/ });
      await userEvent.click(hubRow);
      expect(onOpenRecord).toHaveBeenCalledWith('sprk_document', 'cccccccc-1111-2222-3333-444444444444');
    });

    it('a thread-shaped email hub (no real record id) never renders as a button, even with onOpenRecord provided', () => {
      const announce = jest.fn();
      const onOpenRecord = jest.fn();
      // VisualizationService.CreateParentHubNode's SameThread branch: a truncated conversation index,
      // not a GUID — this must degrade to plain text rather than build a broken deep link.
      const nodes = [hubNode('thread-01020304050607080910111213141516171819', 'email', 'Email Thread')];

      renderWithProvider(<FindResultsList documents={loaded(nodes)} announce={announce} onOpenRecord={onOpenRecord} />);

      expect(screen.queryByRole('button', { name: /Email: Email Thread/ })).toBeNull();
      expect(screen.getByText(/Email: Email Thread/)).toBeTruthy();
    });

    it('matching record row: calls onOpenRecord with its own recordType + recordId', async () => {
      const announce = jest.fn();
      const onOpenRecord = jest.fn();
      renderWithProvider(
        <FindResultsList
          documents={loaded([])}
          announce={announce}
          onOpenRecord={onOpenRecord}
          records={recordsResult({
            records: [recordMatch({ recordId: 'm-9', recordType: 'sprk_matter' })],
            hasMore: false,
          })}
        />
      );

      const row = screen.getByRole('button', { name: /Acme v\. Globex/ });
      await userEvent.click(row);
      expect(onOpenRecord).toHaveBeenCalledWith('sprk_matter', 'm-9');
    });

    it('matching record row: when onOpenRecord is omitted, renders as non-interactive text', () => {
      const announce = jest.fn();
      renderWithProvider(
        <FindResultsList documents={loaded([])} announce={announce} records={recordsResult({ hasMore: false })} />
      );

      expect(screen.queryByRole('button', { name: /Acme v\. Globex/ })).toBeNull();
      expect(screen.getByText('Acme v. Globex')).toBeTruthy();
    });
  });

  describe('matching records show confidence + why they matched (task 092, UAT-3)', () => {
    it('shows a rounded confidence percentage and up to 3 match reasons', () => {
      const announce = jest.fn();
      renderWithProvider(
        <FindResultsList
          documents={loaded([])}
          announce={announce}
          records={recordsResult({
            records: [
              recordMatch({
                confidenceScore: 0.876,
                matchReasons: [
                  'Name match: Acme',
                  'Description match: Globex dispute',
                  'Keyword: indemnity',
                  'Keyword: Delaware',
                ],
              }),
            ],
            hasMore: false,
          })}
        />
      );

      expect(screen.getByText(/88% match/)).toBeTruthy();
      expect(screen.getByText('Name match: Acme')).toBeTruthy();
      expect(screen.getByText('Description match: Globex dispute')).toBeTruthy();
      expect(screen.getByText('Keyword: indemnity')).toBeTruthy();
      // Only the first 3 reasons are shown — the server allows up to 5.
      expect(screen.queryByText('Keyword: Delaware')).toBeNull();
    });

    it('renders <em>…</em> highlighting as bold text', () => {
      const announce = jest.fn();
      renderWithProvider(
        <FindResultsList
          documents={loaded([])}
          announce={announce}
          records={recordsResult({
            records: [recordMatch({ matchReasons: ['Name match: <em>Acme</em> Corp'] })],
            hasMore: false,
          })}
        />
      );

      const bold = screen.getByText('Acme');
      expect(bold.tagName).toBe('STRONG');
      expect(screen.getByText(/Name match:/)).toBeTruthy();
      expect(screen.getByText(/Corp/)).toBeTruthy();
    });

    it('SECURITY: drops a <script> tag and an <img onerror> tag — never executes, never dangerouslySetInnerHTML', () => {
      const announce = jest.fn();
      const { container } = renderWithProvider(
        <FindResultsList
          documents={loaded([])}
          announce={announce}
          records={recordsResult({
            records: [
              recordMatch({
                matchReasons: [
                  'Name match: <script>window.__pwned = true;</script>Acme',
                  '<img src="x" onerror="window.__pwned2 = true">Description match',
                ],
              }),
            ],
            hasMore: false,
          })}
        />
      );

      // No script/img ELEMENT was ever created — the payload never became markup.
      expect(container.querySelector('script')).toBeNull();
      expect(container.querySelector('img')).toBeNull();
      // The inner text survives as plain, inert text (never executed — these are just characters).
      expect(screen.getByText(/Name match:.*Acme/)).toBeTruthy();
      expect(screen.getByText(/Description match/)).toBeTruthy();
      expect((globalThis as Record<string, unknown>).__pwned).toBeUndefined();
      expect((globalThis as Record<string, unknown>).__pwned2).toBeUndefined();
    });
  });

  describe('renderMatchReason (pure)', () => {
    it('plain text with no markup passes through unchanged', () => {
      expect(renderMatchReason('Name match: Acme')).toEqual(['Name match: Acme']);
    });

    it('converts <em>…</em> to a bold React node', () => {
      const nodes = renderMatchReason('Name match: <em>Acme</em>');
      expect(nodes).toHaveLength(2);
      expect(nodes[0]).toBe('Name match: ');
      expect(React.isValidElement(nodes[1])).toBe(true);
    });

    it('drops any other tag, keeping surrounding text as plain strings', () => {
      const nodes = renderMatchReason('<script>alert(1)</script>Name match');
      expect(nodes.join('')).toBe('alert(1)Name match');
      expect(nodes.every(n => typeof n === 'string')).toBe(true);
    });
  });

  describe('Documents section — lazy loading still fetches/reveals on scroll (per section)', () => {
    it('documents reveal more rows via the sentinel; the records sentinel is independent', () => {
      const announce = jest.fn();
      const docs = Array.from({ length: 25 }, (_, i) => resultNode(`d-${i}`, `Doc ${i}`, 0.9));
      renderWithProvider(<FindResultsList documents={loaded(docs)} announce={announce} records={recordsResult()} />);

      // Each sentinel sits at the end of its OWN scroller, so each load is triggered by its own scroll.
      expect(within(screen.getByTestId('find-documents-scroll')).getByTestId('find-results-sentinel')).toBeTruthy();
      expect(within(screen.getByTestId('find-records-scroll')).getByTestId('find-records-sentinel')).toBeTruthy();
      expect(within(screen.getByTestId('find-documents-scroll')).queryByTestId('find-records-sentinel')).toBeNull();
      expect(within(screen.getByTestId('find-records-scroll')).queryByTestId('find-results-sentinel')).toBeNull();
    });
  });
});
