/**
 * UnknownNode — render tests (R7 Wave 8 task 089, FR-27).
 *
 * Verifies the warning-state shell renders with the right label, icon, and
 * accessibility attributes. Does NOT test the canvas-store coercion or the
 * NodePropertiesDialog tab-disable behavior — those are integration concerns
 * covered by the broader playbook validation layer + canvas hydration path.
 */

import React from 'react';
import { render, screen } from '@testing-library/react';
import { ReactFlowProvider } from '@xyflow/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { UnknownNode } from '../UnknownNode';
import type { PlaybookNodeData } from '../../../types/canvas';

function renderUnknownNode(data: Partial<PlaybookNodeData>, selected = false) {
  const fullData = {
    type: 'unknown' as PlaybookNodeData['type'],
    label: 'Test Unknown Node',
    ...data,
  } as PlaybookNodeData;

  return render(
    <FluentProvider theme={webLightTheme}>
      <ReactFlowProvider>
        <UnknownNode
          id="test-node-id"
          type="unknown"
          data={fullData}
          selected={selected}
          dragging={false}
          zIndex={1}
          isConnectable={true}
          positionAbsoluteX={0}
          positionAbsoluteY={0}
          deletable={true}
          selectable={true}
          draggable={true}
        />
      </ReactFlowProvider>
    </FluentProvider>
  );
}

describe('UnknownNode (R7 Wave 8 task 089 / FR-27)', () => {
  it('renders with executor type number when executorType is set to an unknown value', () => {
    renderUnknownNode({ executorType: 999 });
    expect(screen.getByText(/Unknown Executor Type 999/)).toBeInTheDocument();
  });

  it('renders with "unset" suffix when executorType is undefined', () => {
    renderUnknownNode({ executorType: undefined });
    expect(screen.getByText(/Unknown Executor Type unset/)).toBeInTheDocument();
  });

  it('renders the user-supplied label in the header', () => {
    renderUnknownNode({ executorType: 999, label: 'My Custom Label' });
    expect(screen.getByText('My Custom Label')).toBeInTheDocument();
  });

  it('falls back to "Unknown Node" when label is empty', () => {
    renderUnknownNode({ executorType: 999, label: '' });
    expect(screen.getByText('Unknown Node')).toBeInTheDocument();
  });

  it('exposes an aria-label that includes the executor type for screen readers', () => {
    renderUnknownNode({ executorType: 42 });
    expect(screen.getByRole('group', { name: /Unknown executor type 42/ })).toBeInTheDocument();
  });

  it('renders the CTA prompting the maker to pick an Executor Type', () => {
    renderUnknownNode({ executorType: 999 });
    expect(screen.getByText(/Open the node and pick an Executor Type/)).toBeInTheDocument();
  });
});
