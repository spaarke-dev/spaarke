/**
 * BindingEditorForm + the two structured JSON-column editors
 * (task 053 / FR-P4-04). Covers the closed-catalog wiring surfaces the BA
 * authors: chip transitions (Click path) and on-event bindings (Event path).
 */

import { useState } from 'react';
import { render, screen, fireEvent, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { BindingEditorForm } from '../BindingEditorForm';
import { newBindingRow, SURFACE_TOKENS } from '../../../types/catalog';
import type { ActionRow, BindingRow } from '../../../types/catalog';

const ACTIONS: ActionRow[] = [
  {
    id: 'action-1',
    name: 'Summarize chat files',
    actionCode: 'SUM-CHAT@v1',
    description: '',
    kind: 100000000,
    workflowClass: '',
    systemPrompt: 'x',
    inputSchema: '',
    outputSchema: '',
    modelTier: null,
  },
];

function Harness({
  initial,
  onRow,
}: {
  initial?: Partial<BindingRow>;
  onRow?: (row: BindingRow) => void;
}): JSX.Element {
  const [row, setRow] = useState<BindingRow>({ ...newBindingRow(), ...initial });
  return (
    <FluentProvider theme={webLightTheme}>
      <BindingEditorForm
        row={row}
        actions={ACTIONS}
        bindings={[]}
        errors={{}}
        onChange={next => {
          setRow(next);
          onRow?.(next);
        }}
      />
    </FluentProvider>
  );
}

describe('BindingEditorForm — full sprk_playbookconsumer contract', () => {
  it('renders every contract field group', () => {
    render(<Harness />);

    expect(screen.getByLabelText('Binding name')).toBeInTheDocument();
    expect(screen.getByLabelText('Consumer type')).toBeInTheDocument();
    expect(screen.getByLabelText('Consumer code')).toBeInTheDocument();
    expect(screen.getByLabelText('Environment')).toBeInTheDocument();
    expect(screen.getByLabelText('Priority')).toBeInTheDocument();
    expect(screen.getByLabelText('Target Action')).toBeInTheDocument();
    expect(screen.getByLabelText('Use-case id')).toBeInTheDocument();
    expect(screen.getByLabelText('Tool description')).toBeInTheDocument();
    expect(screen.getByLabelText('Disposition')).toBeInTheDocument();
    expect(screen.getByLabelText('Risk')).toBeInTheDocument();
    expect(screen.getByLabelText('Capture mode')).toBeInTheDocument();
    expect(screen.getByLabelText('Model tier override')).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Surfaces' })).toBeInTheDocument();
    expect(screen.getByTestId('chip-transitions-editor')).toBeInTheDocument();
    expect(screen.getByTestId('on-event-bindings-editor')).toBeInTheDocument();
    expect(screen.getByLabelText('Match conditions (optional)')).toBeInTheDocument();
  });

  it('offers ALL §4.1 surface tokens and serializes toggles into the surfaces list', async () => {
    const user = userEvent.setup();
    const onRow = jest.fn();
    render(<Harness onRow={onRow} />);

    const group = screen.getByRole('group', { name: 'Surfaces' });
    for (const token of SURFACE_TOKENS) {
      expect(within(group).getByLabelText(token)).toBeInTheDocument();
    }

    await user.click(within(group).getByLabelText('assistant'));
    await user.click(within(group).getByLabelText('office'));

    const lastRow = onRow.mock.calls[onRow.mock.calls.length - 1][0] as BindingRow;
    expect(lastRow.surfaces).toEqual(['assistant', 'office']);
  });

  it('authors a chip transition through the structured editor (pinned JSON shape)', async () => {
    const user = userEvent.setup();
    const onRow = jest.fn();
    render(<Harness onRow={onRow} />);

    await user.click(screen.getByRole('button', { name: 'Add chip' }));
    fireEvent.change(screen.getByLabelText('Chip 1 target binding id'), { target: { value: 'binding-guid-1' } });
    fireEvent.change(screen.getByLabelText('Chip 1 label'), { target: { value: 'Summarize this document' } });
    fireEvent.change(screen.getByLabelText('Chip 1 bulk label'), { target: { value: 'Summarize' } });
    await user.click(screen.getByLabelText(/Requires session attachments/));
    fireEvent.change(screen.getByLabelText('Chip 1 prefill slots JSON'), {
      target: { value: '{"styleHint":"executive"}' },
    });

    const lastRow = onRow.mock.calls[onRow.mock.calls.length - 1][0] as BindingRow;
    expect(JSON.parse(lastRow.chipTransitionsJson)).toEqual([
      {
        target_binding_id: 'binding-guid-1',
        chip_label: 'Summarize this document',
        bulk_chip_label: 'Summarize',
        requires_attachments: true,
        prefill_slots: { styleHint: 'executive' },
      },
    ]);
  });

  it('authors an on-event membership and warns on unknown event tokens', async () => {
    const user = userEvent.setup();
    const onRow = jest.fn();
    render(<Harness onRow={onRow} />);

    await user.click(screen.getByRole('button', { name: 'Add event membership' }));
    expect(screen.getByLabelText('Event 1 token')).toHaveValue('document_uploaded');

    let lastRow = onRow.mock.calls[onRow.mock.calls.length - 1][0] as BindingRow;
    expect(JSON.parse(lastRow.onEventBindingsJson)).toEqual([{ event: 'document_uploaded', order: 1 }]);

    // Unknown token → warning (closed vocabulary; unknown tokens never fire), not a block.
    fireEvent.change(screen.getByLabelText('Event 1 token'), { target: { value: 'matter_created' } });
    expect(await screen.findByText(/not a known platform event token/)).toBeInTheDocument();

    lastRow = onRow.mock.calls[onRow.mock.calls.length - 1][0] as BindingRow;
    expect(JSON.parse(lastRow.onEventBindingsJson)).toEqual([{ event: 'matter_created', order: 1 }]);
  });

  it('surfaces structurally invalid EXISTING chip JSON instead of silently dropping it (routing-tolerance twin)', () => {
    render(<Harness initial={{ chipTransitionsJson: '{"chip_label": "not an array"}' }} />);
    expect(screen.getByText(/Existing chip transitions JSON is invalid/)).toBeInTheDocument();
  });

  it('prefill-slots survives INCREMENTAL typing of not-yet-valid JSON (code-review Critical #1 regression)', async () => {
    const user = userEvent.setup();
    const onRow = jest.fn();
    render(<Harness onRow={onRow} />);

    await user.click(screen.getByRole('button', { name: 'Add chip' }));
    fireEvent.change(screen.getByLabelText('Chip 1 target binding id'), { target: { value: 'b1' } });
    fireEvent.change(screen.getByLabelText('Chip 1 label'), { target: { value: 'Summarize' } });

    // Type the prefill JSON character-by-character — every intermediate state
    // is invalid JSON. The input must retain the typed text (drafts are state,
    // not re-derived from the serialized value which omits invalid prefill).
    const prefillInput = screen.getByLabelText('Chip 1 prefill slots JSON');
    const target = '{"a":1}';
    for (let i = 1; i <= target.length; i++) {
      fireEvent.change(prefillInput, { target: { value: target.slice(0, i) } });
    }
    expect(prefillInput).toHaveValue(target);

    const lastRow = onRow.mock.calls[onRow.mock.calls.length - 1][0] as BindingRow;
    expect(JSON.parse(lastRow.chipTransitionsJson)).toEqual([
      { target_binding_id: 'b1', chip_label: 'Summarize', prefill_slots: { a: 1 } },
    ]);
  });

  it('keeps an in-progress chip (blank required fields) editable with per-field errors', () => {
    render(<Harness initial={{ chipTransitionsJson: '[{"target_binding_id":"","chip_label":""}]' }} />);
    expect(screen.getByTestId('chip-transition-0')).toBeInTheDocument();
    expect(screen.getByText(/Required — the Click path resolves it/)).toBeInTheDocument();
    expect(screen.getByText(/Required — rendered to the user/)).toBeInTheDocument();
  });

  it('validates match conditions live (flat key → string | string[] only)', async () => {
    render(<Harness />);

    fireEvent.change(screen.getByLabelText('Match conditions (optional)'), {
      target: { value: '{"entityType": {"nested": true}}' },
    });

    expect(await screen.findByText(/values must be a string or array of strings/)).toBeInTheDocument();
  });
});
