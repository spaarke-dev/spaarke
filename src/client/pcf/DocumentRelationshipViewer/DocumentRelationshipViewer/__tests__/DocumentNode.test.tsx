/**
 * DocumentNode — the PCF's react-flow-renderer node around the shared DocumentNodeCard (#3093).
 *
 * Renders the shared card under this PCF's React 16, and pins the PCF's choices: no preview link
 * (a node click here only selects), a details tooltip, and an open-record link that opens the
 * Dataverse record in a new tab without selecting the node.
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import type { NodeProps } from 'react-flow-renderer';
import type { DocumentNodeData } from '@spaarke/ui-components/dist/components/DocumentRelationshipGraph';
import { mockWindowOpen } from '../../jest.setup';

jest.mock('react-flow-renderer', () => ({
  __esModule: true,
  Handle: ({ type, position }: { type: string; position: string }) => (
    <span data-testid={`handle-${type}-${position}`} />
  ),
  Position: { Left: 'left', Right: 'right' },
}));

import { DocumentNode } from '../components/DocumentNode';

const nodeProps = (data: DocumentNodeData): NodeProps<DocumentNodeData> =>
  ({ id: 'n1', data, selected: false, type: 'document' }) as unknown as NodeProps<DocumentNodeData>;

const relatedData: DocumentNodeData = {
  documentId: 'doc-1',
  name: 'Contract.pdf',
  fileType: 'pdf',
  nodeType: 'related',
  similarity: 0.82,
  relationshipLabel: 'Semantic',
  relationshipTypes: [{ type: 'semantic', label: 'Semantic', similarity: 0.82 }],
  recordUrl: 'https://org.example/main.aspx?etn=sprk_document&id=doc-1',
};

const renderNode = (data: DocumentNodeData, onNodeClick = jest.fn()) => {
  render(
    <FluentProvider theme={webLightTheme}>
      <div onClick={onNodeClick}>
        <DocumentNode {...nodeProps(data)} />
      </div>
    </FluentProvider>
  );
  return onNodeClick;
};

describe('DocumentNode (PCF)', () => {
  beforeEach(() => {
    mockWindowOpen.mockClear();
  });

  it('renders the shared card with react-flow-renderer handles, target left and source right', () => {
    renderNode(relatedData);
    // The card title (the details tooltip also carries the name).
    expect(screen.getByText('Contract.pdf', { selector: '.fui-Body1Strong' })).toBeInTheDocument();
    expect(screen.getByTestId('handle-target-left')).toBeInTheDocument();
    expect(screen.getByTestId('handle-source-right')).toBeInTheDocument();
  });

  it('has no preview link, because a node click here only selects the node', () => {
    renderNode(relatedData);
    expect(screen.queryByLabelText('Preview document')).toBeNull();
  });

  it('opens the record in a new tab without selecting the node', () => {
    const onNodeClick = renderNode(relatedData);
    fireEvent.click(screen.getByLabelText('Open record'));
    expect(mockWindowOpen).toHaveBeenCalledWith(relatedData.recordUrl, '_blank', 'noopener,noreferrer');
    expect(onNodeClick).not.toHaveBeenCalled();
  });

  it('shows the details tooltip with the similarity', async () => {
    renderNode(relatedData);
    fireEvent.focus(screen.getByLabelText('Document details'));
    expect(await screen.findByText('82%')).toBeInTheDocument();
  });

  it('shows no open-record link for an orphan file (no record)', () => {
    renderNode({ ...relatedData, documentId: undefined, recordUrl: undefined, isOrphanFile: true, nodeType: 'orphan' });
    expect(screen.queryByLabelText('Open record')).toBeNull();
  });
});
