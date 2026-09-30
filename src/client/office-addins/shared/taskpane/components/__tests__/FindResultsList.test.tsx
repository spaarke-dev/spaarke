/**
 * Unit tests for FindResultsList (spaarkeai-word-add-in-r1 task 034, Path 2).
 *
 * Covers:
 * - The empty state (no result rows) renders explicit copy and is announced.
 * - No pager control of any kind renders (ADR-051) — asserted over the rendered output, not by
 *   inspection.
 * - Hub nodes (matter/project/invoice/email) render in a distinctly labeled section, never mixed into
 *   the ranked similarity list.
 * - The ranked-list framing ("Most similar documents"), never implying further server pages exist.
 * - The `onOpenResult` seam is wired but this component never navigates anywhere itself.
 */

import React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { FindResultsList, type FindResultNode } from '../FindResultsList';

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

describe('FindResultsList', () => {
  describe('empty state', () => {
    it('renders explicit empty copy and announces it — no indefinite spinner, no implied withholding', () => {
      const announce = jest.fn();
      renderWithProvider(<FindResultsList nodes={[]} announce={announce} />);

      expect(screen.getByText('No similar documents found')).toBeTruthy();
      expect(
        screen.getByText(/Spaarke didn.t find any documents you can see that are similar to this one\./)
      ).toBeTruthy();
      expect(announce).toHaveBeenCalledWith('No similar documents found.', 'polite');
    });

    it('the source node alone (no result rows, no hubs) still renders the empty state', () => {
      const announce = jest.fn();
      renderWithProvider(<FindResultsList nodes={[sourceNode('src-1', 'Source Doc')]} announce={announce} />);

      expect(screen.getByText('No similar documents found')).toBeTruthy();
    });

    it('a hub node with zero similarity matches still shows the empty state, with the hub section retained', () => {
      const announce = jest.fn();
      renderWithProvider(
        <FindResultsList nodes={[hubNode('matter-1', 'matter', 'Smith v Smith')]} announce={announce} />
      );

      expect(screen.getByText('No similar documents found')).toBeTruthy();
      expect(screen.getByTestId('find-results-hub-section')).toBeTruthy();
      expect(screen.getByText(/Matter: Smith v Smith/)).toBeTruthy();
    });
  });

  describe('ranked-list framing (never a pageable dataset)', () => {
    it('headlines the list as ranked "top matches", not a count of a larger set', () => {
      const announce = jest.fn();
      renderWithProvider(<FindResultsList nodes={[resultNode('doc-1', 'MSA Draft', 0.92)]} announce={announce} />);

      expect(screen.getByText('Most similar documents')).toBeTruthy();
    });
  });

  describe('no pager of any kind (ADR-051)', () => {
    it('renders no numbered pages, prev/next, chevron, or "Load more" control — even with many results', () => {
      const announce = jest.fn();
      const nodes = Array.from({ length: 45 }, (_, i) => resultNode(`doc-${i}`, `Document ${i}`, 0.5 + i / 100));

      renderWithProvider(<FindResultsList nodes={nodes} announce={announce} />);

      // Text-based pager affordances.
      expect(screen.queryByText(/load more/i)).toBeNull();
      expect(screen.queryByText(/next page/i)).toBeNull();
      expect(screen.queryByText(/previous page/i)).toBeNull();
      expect(screen.queryByText(/^page \d+/i)).toBeNull();

      // Every rendered button's accessible name must be a document row, never a pager control.
      const pagerNamePattern = /load more|next|previous|prev|page \d+|»|›|‹|«/i;
      const buttons = screen.queryAllByRole('button');
      for (const button of buttons) {
        expect(button.textContent ?? '').not.toMatch(pagerNamePattern);
      }

      // No <nav> / pagination landmark of any kind.
      expect(screen.queryByRole('navigation')).toBeNull();
    });
  });

  describe('hub nodes are never presented as similarity matches', () => {
    it('a hub node renders in its own, distinctly labeled section, separate from ranked rows', () => {
      const announce = jest.fn();
      const nodes = [resultNode('doc-1', 'MSA Draft', 0.91), hubNode('matter-1', 'matter', 'Smith v Smith')];

      renderWithProvider(<FindResultsList nodes={nodes} announce={announce} />);

      const hubSection = screen.getByTestId('find-results-hub-section');
      expect(hubSection.textContent).toContain('Matter: Smith v Smith');
      expect(hubSection.textContent).toMatch(/not a similarity match/i);

      // The hub row's own label text is NOT inside the ranked scroll list.
      const scrollArea = screen.getByTestId('find-results-scroll-area');
      expect(scrollArea.textContent).not.toContain('Smith v Smith');
      expect(scrollArea.textContent).toContain('MSA Draft');
    });

    it('multiple hub types (matter + project) each get their own labeled line', () => {
      const announce = jest.fn();
      const nodes = [
        resultNode('doc-1', 'MSA Draft', 0.91),
        hubNode('matter-1', 'matter', 'Smith v Smith'),
        hubNode('project-1', 'project', 'Q3 Rollout'),
      ];

      renderWithProvider(<FindResultsList nodes={nodes} announce={announce} />);

      const hubSection = screen.getByTestId('find-results-hub-section');
      expect(hubSection.textContent).toContain('Matter: Smith v Smith');
      expect(hubSection.textContent).toContain('Project: Q3 Rollout');
    });

    it('the source node itself never renders as a result row or a hub', () => {
      const announce = jest.fn();
      const nodes = [sourceNode('src-1', 'The Open Document'), resultNode('doc-1', 'MSA Draft', 0.91)];

      renderWithProvider(<FindResultsList nodes={nodes} announce={announce} />);

      expect(screen.queryByText('The Open Document')).toBeNull();
      expect(screen.queryByTestId('find-results-hub-section')).toBeNull();
    });
  });

  describe('rows render as ranked matches with similarity context', () => {
    it('shows the document label and a similarity percentage for each row', () => {
      const announce = jest.fn();
      renderWithProvider(<FindResultsList nodes={[resultNode('doc-1', 'MSA Draft', 0.876)]} announce={announce} />);

      expect(screen.getByText('MSA Draft')).toBeTruthy();
      expect(screen.getByText(/88% match/)).toBeTruthy();
    });
  });

  describe('onOpenResult seam (task 034 does not implement opening a result)', () => {
    it('when provided, rows are interactive buttons that call it with the node', async () => {
      const announce = jest.fn();
      const onOpenResult = jest.fn();
      const node = resultNode('doc-1', 'MSA Draft', 0.9);

      renderWithProvider(<FindResultsList nodes={[node]} announce={announce} onOpenResult={onOpenResult} />);

      const row = screen.getByRole('button', { name: /MSA Draft/ });
      row.click();
      expect(onOpenResult).toHaveBeenCalledWith(node);
    });

    it('when omitted, rows render as non-interactive text (no button role)', () => {
      const announce = jest.fn();
      renderWithProvider(<FindResultsList nodes={[resultNode('doc-1', 'MSA Draft', 0.9)]} announce={announce} />);

      expect(screen.queryByRole('button', { name: /MSA Draft/ })).toBeNull();
      expect(screen.getByText('MSA Draft')).toBeTruthy();
    });
  });
});
