/**
 * BranchPickerDialog tests — R3 task 094 (covers task 092 / AC-H2.2).
 *
 * BranchPickerDialog reads its state from the canvasStore (Zustand). These tests
 * seed the store with a Condition source node + pending connection, then verify:
 *   - the three radio options render (True / False / Both)
 *   - default selection = 'true'
 *   - Confirm → store.confirmBranchSelection(choice) (via store state assertions)
 *   - Cancel → store.cancelBranchSelection (clears pendingBranchConnection)
 *   - branch labels from conditionJson surface in option labels
 */
import * as React from 'react';
import { screen, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BranchPickerDialog } from '../BranchPickerDialog';
import { useCanvasStore } from '../../../stores/canvasStore';
import type { PlaybookNode } from '../../../types/canvas';
import { renderWithProviders } from './testUtils';

function makeConditionNode(conditionJson?: string): PlaybookNode {
  return {
    id: 'cond_1',
    type: 'condition',
    position: { x: 0, y: 0 },
    data: {
      label: 'Is High Priority?',
      type: 'condition',
      conditionJson,
    },
  };
}

function makeNonConditionNode(): PlaybookNode {
  return {
    id: 'plain_1',
    type: 'updateRecord',
    position: { x: 0, y: 0 },
    data: { label: 'Update Record', type: 'updateRecord' },
  };
}

function seedStore(node: PlaybookNode, pending: { source: string; target: string }) {
  act(() => {
    useCanvasStore.setState({
      nodes: [node, { ...node, id: 'target_1' }],
      edges: [],
      pendingBranchConnection: {
        source: pending.source,
        target: pending.target,
        sourceHandle: null,
        targetHandle: null,
      },
    });
  });
}

describe('BranchPickerDialog', () => {
  beforeEach(() => {
    act(() => {
      useCanvasStore.getState().reset();
    });
  });

  afterEach(() => {
    act(() => {
      useCanvasStore.getState().reset();
    });
  });

  it('does not render when pendingBranchConnection is null', () => {
    renderWithProviders(<BranchPickerDialog />);
    expect(screen.queryByText('Choose branch for this connection')).not.toBeInTheDocument();
  });

  it('renders title, three radios, Cancel + Connect buttons when pending connection exists', () => {
    seedStore(makeConditionNode(), { source: 'cond_1', target: 'target_1' });
    renderWithProviders(<BranchPickerDialog />);

    expect(screen.getByText('Choose branch for this connection')).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /True branch \(True\)/ })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /False branch \(False\)/ })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /Both branches/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Connect' })).toBeInTheDocument();
  });

  it('defaults to "true" radio selection on open', () => {
    seedStore(makeConditionNode(), { source: 'cond_1', target: 'target_1' });
    renderWithProviders(<BranchPickerDialog />);
    const trueRadio = screen.getByRole('radio', { name: /True branch/ }) as HTMLInputElement;
    expect(trueRadio.checked).toBe(true);
  });

  it('surfaces author-supplied trueBranch / falseBranch labels from conditionJson', () => {
    const node = makeConditionNode(JSON.stringify({ trueBranch: 'Approved', falseBranch: 'Rejected' }));
    seedStore(node, { source: 'cond_1', target: 'target_1' });
    renderWithProviders(<BranchPickerDialog />);

    expect(screen.getByRole('radio', { name: /True branch \(Approved\)/ })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /False branch \(Rejected\)/ })).toBeInTheDocument();
  });

  it('falls back to default labels when conditionJson is malformed', () => {
    const node = makeConditionNode('not-valid-json');
    seedStore(node, { source: 'cond_1', target: 'target_1' });
    renderWithProviders(<BranchPickerDialog />);

    expect(screen.getByRole('radio', { name: /True branch \(True\)/ })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /False branch \(False\)/ })).toBeInTheDocument();
  });

  it('falls back to default labels when source node is not a condition', () => {
    seedStore(makeNonConditionNode(), { source: 'plain_1', target: 'target_1' });
    renderWithProviders(<BranchPickerDialog />);

    expect(screen.getByRole('radio', { name: /True branch \(True\)/ })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /False branch \(False\)/ })).toBeInTheDocument();
  });

  it('confirming with default selection creates a single trueBranch edge', async () => {
    const user = userEvent.setup();
    seedStore(makeConditionNode(), { source: 'cond_1', target: 'target_1' });
    renderWithProviders(<BranchPickerDialog />);

    await user.click(screen.getByRole('button', { name: 'Connect' }));

    const state = useCanvasStore.getState();
    expect(state.pendingBranchConnection).toBeNull();
    expect(state.edges).toHaveLength(1);
    expect(state.edges[0].type).toBe('trueBranch');
    expect(state.edges[0].sourceHandle).toBe('true');
  });

  it('confirming with "both" creates two edges (trueBranch + falseBranch)', async () => {
    const user = userEvent.setup();
    seedStore(makeConditionNode(), { source: 'cond_1', target: 'target_1' });
    renderWithProviders(<BranchPickerDialog />);

    await user.click(screen.getByRole('radio', { name: /Both branches/ }));
    await user.click(screen.getByRole('button', { name: 'Connect' }));

    const state = useCanvasStore.getState();
    expect(state.pendingBranchConnection).toBeNull();
    expect(state.edges).toHaveLength(2);
    const types = state.edges.map(e => e.type).sort();
    expect(types).toEqual(['falseBranch', 'trueBranch']);
  });

  it('clicking Cancel discards the pending connection without creating an edge', async () => {
    const user = userEvent.setup();
    seedStore(makeConditionNode(), { source: 'cond_1', target: 'target_1' });
    renderWithProviders(<BranchPickerDialog />);

    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    const state = useCanvasStore.getState();
    expect(state.pendingBranchConnection).toBeNull();
    expect(state.edges).toHaveLength(0);
  });
});
