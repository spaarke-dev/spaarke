/**
 * DocumentNodeCard — the relationship-graph node body shared by the DocumentRelationshipViewer code
 * page and PCF (#3093). Covers the renderings, the host-supplied handles, and the two behaviours the
 * former copies disagreed on:
 *   - a compact (icon-only) node is named through its `title` (only the PCF copy did this);
 *   - the source node's preview icon lets the click reach the host's node click (the code page copy
 *     swallowed it, so that icon did nothing).
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DocumentNodeCard, getCompactNodeTitle } from '../DocumentNodeCard';
import type { DocumentNodeHandleSpec } from '../DocumentNodeCard';
import type { DocumentNodeData } from '../types';

const data = (overrides: Partial<DocumentNodeData> = {}): DocumentNodeData => ({
  name: 'Contract.pdf',
  fileType: 'pdf',
  nodeType: 'related',
  ...overrides,
});

const renderCard = (props: React.ComponentProps<typeof DocumentNodeCard>) =>
  render(
    <FluentProvider theme={webLightTheme}>
      <DocumentNodeCard {...props} />
    </FluentProvider>
  );

describe('DocumentNodeCard', () => {
  it('renders a related document with its relationship indicators', () => {
    renderCard({
      data: data({
        relationshipTypes: [
          { type: 'semantic', label: 'Semantic', similarity: 0.8 },
          { type: 'same_email', label: 'Attachment', similarity: 1 },
        ],
      }),
    });
    expect(screen.getByText('Contract.pdf')).toBeInTheDocument();
    expect(screen.getByTitle('Semantic')).toBeInTheDocument();
    expect(screen.getByText('Attachment')).toBeInTheDocument();
  });

  it('shows "File only" for an orphan file with no relationships', () => {
    renderCard({ data: data({ isOrphanFile: true, nodeType: 'orphan' }) });
    expect(screen.getByText('File only')).toBeInTheDocument();
  });

  it('renders the source node with its Source badge', () => {
    renderCard({ data: data({ isSource: true, nodeType: 'source' }) });
    expect(screen.getAllByText('Source').length).toBeGreaterThan(0);
  });

  it('renders a parent hub with its type label', () => {
    renderCard({ data: data({ name: 'Acme v Beta', nodeType: 'matter', fileType: 'matter' }) });
    expect(screen.getByText('Acme v Beta')).toBeInTheDocument();
    expect(screen.getByText('Matter')).toBeInTheDocument();
  });

  it('asks the host for a target handle on the left and a source handle on the right', () => {
    const specs: DocumentNodeHandleSpec[] = [];
    const renderHandle = (spec: DocumentNodeHandleSpec) => {
      specs.push(spec);
      return <span data-testid={`handle-${spec.type}-${spec.side}`} />;
    };
    renderCard({ data: data(), renderHandle });
    expect(screen.getByTestId('handle-target-left')).toBeInTheDocument();
    expect(screen.getByTestId('handle-source-right')).toBeInTheDocument();
    expect(specs.map(s => s.style.width)).toEqual(['6px', '6px']);
  });

  it('renders without handles when the host supplies none', () => {
    const { container } = renderCard({ data: data() });
    expect(container.querySelector('[data-testid^="handle-"]')).toBeNull();
  });

  describe('compact mode', () => {
    it('names the icon-only node through its title', () => {
      renderCard({
        data: data({ compactMode: true, relationshipLabel: 'Semantic', similarity: 0.82 }),
      });
      expect(screen.getByTestId('document-node-compact')).toHaveAttribute('title', 'Contract.pdf • Semantic (82%)');
    });

    it('marks orphan files and names hubs by type', () => {
      expect(getCompactNodeTitle(data({ isOrphanFile: true, nodeType: 'orphan' }))).toBe('Contract.pdf (File only)');
      expect(getCompactNodeTitle(data({ name: 'Acme', nodeType: 'matter' }))).toBe('Matter: Acme');
    });
  });

  describe('host-chosen footer affordances (the PCF turns preview off, details and open-record on)', () => {
    const withRecord = (overrides: Partial<DocumentNodeData> = {}) =>
      data({ recordUrl: 'https://org.example/main.aspx?id=1', ...overrides });

    it('hides the preview link when showPreviewLink is false', () => {
      renderCard({ data: withRecord(), showPreviewLink: false });
      expect(screen.queryByLabelText('Preview document')).toBeNull();
    });

    it.each([
      ['source', withRecord({ isSource: true, nodeType: 'source' })],
      ['related', withRecord()],
    ])('on the %s node, the open-record link calls the host and does not reach the node click', (_kind, nodeData) => {
      const onOpenRecord = jest.fn();
      const onNodeClick = jest.fn();
      render(
        <FluentProvider theme={webLightTheme}>
          <div onClick={onNodeClick}>
            <DocumentNodeCard data={nodeData} onOpenRecord={onOpenRecord} />
          </div>
        </FluentProvider>
      );
      fireEvent.click(screen.getByLabelText('Open record'));
      expect(onOpenRecord).toHaveBeenCalledWith(nodeData);
      expect(onNodeClick).not.toHaveBeenCalled();
    });

    it('shows no open-record link without a recordUrl or without a handler', () => {
      const { unmount } = renderCard({ data: data(), onOpenRecord: jest.fn() });
      expect(screen.queryByLabelText('Open record')).toBeNull();
      unmount();
      renderCard({ data: withRecord() });
      expect(screen.queryByLabelText('Open record')).toBeNull();
    });

    it('shows the details tooltip, with similarity and shared keywords, when showDetails is set', async () => {
      renderCard({
        data: withRecord({
          similarity: 0.82,
          relationshipLabel: 'Semantic',
          parentEntityName: 'Acme v Beta',
          size: 1536,
          sharedKeywords: ['indemnity', 'term', 'notice', 'venue'],
        }),
        showDetails: true,
      });
      fireEvent.focus(screen.getByLabelText('Document details'));
      expect(await screen.findByText('82%')).toBeInTheDocument();
      expect(screen.getByText('Acme v Beta')).toBeInTheDocument();
      expect(screen.getByText('1.5 KB')).toBeInTheDocument();
      expect(screen.getByText('indemnity, term, notice...')).toBeInTheDocument();
    });

    it('has no details icon by default', () => {
      renderCard({ data: withRecord() });
      expect(screen.queryByLabelText('Document details')).toBeNull();
    });
  });

  describe('preview icon', () => {
    it.each([
      ['source', data({ isSource: true, nodeType: 'source' })],
      ['related', data()],
    ])('on the %s node, a click reaches the host node click', (_kind, nodeData) => {
      const onNodeClick = jest.fn();
      render(
        <FluentProvider theme={webLightTheme}>
          <div onClick={onNodeClick}>
            <DocumentNodeCard data={nodeData} />
          </div>
        </FluentProvider>
      );
      fireEvent.click(screen.getByLabelText('Preview document'));
      expect(onNodeClick).toHaveBeenCalledTimes(1);
    });
  });
});
