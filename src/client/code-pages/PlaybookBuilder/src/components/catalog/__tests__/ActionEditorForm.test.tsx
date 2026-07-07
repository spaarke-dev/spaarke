/**
 * ActionEditorForm — authoring-time validation UX (task 053 / FR-P4-04).
 */

import { useState } from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ActionEditorForm, JPS_STARTER_TEMPLATE } from '../ActionEditorForm';
import { ActionKind, newActionRow } from '../../../types/catalog';
import type { ActionRow } from '../../../types/catalog';
import type { ValidationErrors } from '../../../services/catalogService';

function Harness({
  initial,
  errors = {},
  onRow,
}: {
  initial?: Partial<ActionRow>;
  errors?: ValidationErrors;
  onRow?: (row: ActionRow) => void;
}): JSX.Element {
  const [row, setRow] = useState<ActionRow>({ ...newActionRow(), ...initial });
  return (
    <FluentProvider theme={webLightTheme}>
      <ActionEditorForm
        row={row}
        errors={errors}
        onChange={next => {
          setRow(next);
          onRow?.(next);
        }}
      />
    </FluentProvider>
  );
}

describe('ActionEditorForm', () => {
  it('renders the full Action contract fields for a Prompted action', () => {
    render(<Harness />);

    expect(screen.getByLabelText('Action name')).toBeInTheDocument();
    expect(screen.getByLabelText('Action code')).toBeInTheDocument();
    expect(screen.getByLabelText('Action kind')).toBeInTheDocument();
    expect(screen.getByLabelText('Default model tier')).toBeInTheDocument();
    expect(screen.getByLabelText('Prompt (JPS JSON or flat text)')).toBeInTheDocument();
    expect(screen.getByLabelText('Input schema (sprk_inputschema)')).toBeInTheDocument();
    expect(screen.getByLabelText('Output schema (sprk_outputschemajson)')).toBeInTheDocument();
    // Prompted kind → no workflow class field.
    expect(screen.queryByLabelText('Workflow class')).not.toBeInTheDocument();
  });

  it('Coded kind swaps the prompt surface for the workflow class field', () => {
    render(<Harness initial={{ kind: ActionKind.Coded }} />);

    expect(screen.getByLabelText('Workflow class')).toBeInTheDocument();
    expect(screen.queryByLabelText('Prompt (JPS JSON or flat text)')).not.toBeInTheDocument();
  });

  it('shows the property-level-required error LIVE while typing the outage schema', async () => {
    render(<Harness />);

    fireEvent.change(screen.getByLabelText('Input schema (sprk_inputschema)'), {
      target: { value: '{"type":"object","properties":{"x":{"type":"string","required":true}}}' },
    });

    expect(
      await screen.findByText(/property-level "required": true is not JSON Schema/)
    ).toBeInTheDocument();
  });

  it('flags a JPS prompt that is not valid JSON', async () => {
    render(<Harness />);

    fireEvent.change(screen.getByLabelText('Prompt (JPS JSON or flat text)'), {
      target: { value: '{ "instruction": ' },
    });

    expect(await screen.findByText(/JPS.*not valid JSON/)).toBeInTheDocument();
  });

  it('inserts the JPS starter template into an empty prompt', async () => {
    const user = userEvent.setup();
    const onRow = jest.fn();
    render(<Harness onRow={onRow} />);

    await user.click(screen.getByRole('button', { name: 'Insert JPS starter template' }));

    const lastRow = onRow.mock.calls[onRow.mock.calls.length - 1][0] as ActionRow;
    expect(lastRow.systemPrompt).toBe(JPS_STARTER_TEMPLATE);
    expect(JSON.parse(JPS_STARTER_TEMPLATE)).toHaveProperty('instruction');
  });

  it('renders save-gate errors passed from the shell', () => {
    render(<Harness errors={{ actionCode: 'Action code is required (stable versioned code, e.g. SUM-CHAT@v1).' }} />);
    expect(screen.getByText(/Action code is required/)).toBeInTheDocument();
  });
});
