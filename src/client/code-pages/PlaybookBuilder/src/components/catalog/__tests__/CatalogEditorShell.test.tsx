/**
 * CatalogEditorShell — the FR-P4-04 acceptance flow as jest-DOM evidence:
 * "a BA can author an Action + Binding end-to-end in the UI, producing valid
 * catalog rows". The Dataverse Web API layer is mocked at the wire
 * (dataverseClient); ALL validation and mapping logic runs for real, so the
 * saves asserted here are the exact column payloads spaarkedev1 would receive.
 * The G-M maker gate (task 090) re-runs this flow in the browser.
 */

import { render, screen, waitFor, fireEvent, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { CatalogEditorShell } from '../CatalogEditorShell';
import * as dataverseClient from '../../../services/dataverseClient';

jest.mock('../../../services/dataverseClient', () => ({
  createRecord: jest.fn(),
  updateRecord: jest.fn(),
  retrieveMultipleRecords: jest.fn(),
}));

const createRecordMock = dataverseClient.createRecord as jest.Mock;
const updateRecordMock = dataverseClient.updateRecord as jest.Mock;
const retrieveMultipleRecordsMock = dataverseClient.retrieveMultipleRecords as jest.Mock;

/** Stateful in-memory Dataverse fake: created rows show up in subsequent lists. */
let actionRows: Record<string, unknown>[];
let bindingRows: Record<string, unknown>[];

beforeEach(() => {
  jest.clearAllMocks();
  actionRows = [];
  bindingRows = [];

  retrieveMultipleRecordsMock.mockImplementation((entitySet: string) =>
    Promise.resolve({
      entities: entitySet === 'sprk_analysisactions' ? [...actionRows] : [...bindingRows],
    })
  );

  createRecordMock.mockImplementation((entitySet: string, data: Record<string, unknown>) => {
    if (entitySet === 'sprk_analysisactions') {
      const id = `action-id-${actionRows.length + 1}`;
      actionRows.push({ ...data, sprk_analysisactionid: id });
      return Promise.resolve(id);
    }
    const id = `binding-id-${bindingRows.length + 1}`;
    const bind = data['sprk_action@odata.bind'] as string | undefined;
    const actionId = bind ? /\(([^)]+)\)/.exec(bind)?.[1] : undefined;
    bindingRows.push({ ...data, sprk_playbookconsumerid: id, _sprk_action_value: actionId });
    return Promise.resolve(id);
  });

  updateRecordMock.mockResolvedValue(undefined);
});

function renderShell(): void {
  render(
    <FluentProvider theme={webLightTheme}>
      <CatalogEditorShell />
    </FluentProvider>
  );
}

const VALID_INPUT_SCHEMA = JSON.stringify({
  type: 'object',
  properties: {
    due_date: { type: 'string', description: 'Due date.', elicitation_prompt: "What's the due date?" },
  },
  required: ['due_date'],
});

const OUTAGE_INPUT_SCHEMA = JSON.stringify({
  type: 'object',
  properties: { due_date: { type: 'string', required: true } },
});

const VALID_OUTPUT_SCHEMA = JSON.stringify({
  type: 'object',
  properties: { summary: { type: 'string' } },
  required: ['summary'],
  additionalProperties: false,
});

describe('CatalogEditorShell — BA authors an Action + Binding end-to-end (FR-P4-04)', () => {
  it('authors a complete Action then its Binding; both rows reach Dataverse with valid contract columns', async () => {
    const user = userEvent.setup();
    renderShell();

    // ── Cold load: catalogs empty ──
    await waitFor(() => expect(screen.getByTestId('new-row-button')).toBeInTheDocument());

    // ── Author the Action ──
    await user.click(screen.getByTestId('new-row-button'));
    expect(screen.getByTestId('action-editor-form')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Action name'), { target: { value: 'Create follow-up task' } });
    fireEvent.change(screen.getByLabelText('Action code'), { target: { value: 'CREATE-TASK@v2' } });
    fireEvent.change(screen.getByLabelText('Prompt (JPS JSON or flat text)'), {
      target: { value: 'Create a follow-up task from the conversation.' },
    });

    // Authoring-time guard: the EXACT G-P3 outage schema is rejected inline…
    fireEvent.change(screen.getByLabelText('Input schema (sprk_inputschema)'), {
      target: { value: OUTAGE_INPUT_SCHEMA },
    });
    expect(await screen.findByText(/property-level "required": true is not JSON Schema/)).toBeInTheDocument();

    // …and the save gate refuses it.
    await user.click(screen.getByTestId('save-row-button'));
    expect(await screen.findByTestId('validation-error-bar')).toBeInTheDocument();
    expect(createRecordMock).not.toHaveBeenCalled();

    // Fix the schema → save succeeds.
    fireEvent.change(screen.getByLabelText('Input schema (sprk_inputschema)'), {
      target: { value: VALID_INPUT_SCHEMA },
    });
    fireEvent.change(screen.getByLabelText('Output schema (sprk_outputschemajson)'), {
      target: { value: VALID_OUTPUT_SCHEMA },
    });
    await user.click(screen.getByTestId('save-row-button'));

    await waitFor(() => expect(screen.getByTestId('save-success-bar')).toBeInTheDocument());
    expect(screen.getByTestId('save-success-bar')).toHaveTextContent('Action CREATE-TASK@v2 saved.');
    // NFR-06 eval-suite reminder surfaces on every catalog save.
    expect(screen.getByTestId('save-success-bar')).toHaveTextContent(/eval case/i);

    expect(createRecordMock).toHaveBeenCalledWith(
      'sprk_analysisactions',
      expect.objectContaining({
        sprk_name: 'Create follow-up task',
        sprk_actioncode: 'CREATE-TASK@v2',
        sprk_inputschema: VALID_INPUT_SCHEMA,
        sprk_outputschemajson: VALID_OUTPUT_SCHEMA,
      })
    );

    // The saved Action appears in the list (reload round-trip).
    await waitFor(() => expect(screen.getByTestId('action-list-item-CREATE-TASK@v2')).toBeInTheDocument());

    // ── Author the Binding ──
    await user.click(screen.getByTestId('tab-bindings'));
    await user.click(screen.getByTestId('new-row-button'));
    expect(screen.getByTestId('binding-editor-form')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Binding name'), { target: { value: 'Create task (default)' } });
    fireEvent.change(screen.getByLabelText('Consumer type'), { target: { value: 'chat-create-task' } });
    fireEvent.change(screen.getByLabelText('Use-case id'), { target: { value: 'UC-A-4' } });
    fireEvent.change(screen.getByLabelText('Tool description'), {
      target: { value: 'Create a follow-up task grounded in the session files.' },
    });

    // Target Action: pick the row authored above.
    await user.click(screen.getByLabelText('Target Action'));
    await user.click(await screen.findByText('Create follow-up task (CREATE-TASK@v2)'));

    // Surfaces: closed §4.1 vocabulary as checkboxes.
    const surfaces = screen.getByRole('group', { name: 'Surfaces' });
    await user.click(within(surfaces).getByLabelText('assistant'));

    // Chip transitions: structured editor writes the pinned JSON shape.
    await user.click(screen.getByRole('button', { name: 'Add chip' }));
    fireEvent.change(screen.getByLabelText('Chip 1 target binding id'), { target: { value: 'target-binding-guid' } });
    fireEvent.change(screen.getByLabelText('Chip 1 label'), { target: { value: 'Summarize this document' } });

    // On-event bindings: structured editor with the known closed vocabulary.
    await user.click(screen.getByRole('button', { name: 'Add event membership' }));
    expect(screen.getByLabelText('Event 1 token')).toHaveValue('document_uploaded');

    await user.click(screen.getByTestId('save-row-button'));
    await waitFor(() => expect(screen.getByTestId('save-success-bar')).toBeInTheDocument());
    expect(screen.getByTestId('save-success-bar')).toHaveTextContent('Binding chat-create-task saved.');

    expect(createRecordMock).toHaveBeenCalledWith(
      'sprk_playbookconsumers',
      expect.objectContaining({
        sprk_name: 'Create task (default)',
        sprk_consumertype: 'chat-create-task',
        sprk_ucid: 'UC-A-4',
        sprk_tooldescription: 'Create a follow-up task grounded in the session files.',
        sprk_surfaces: 'assistant',
        sprk_oneventbindings: '[{"event":"document_uploaded","order":1}]',
        'sprk_action@odata.bind': '/sprk_analysisactions(action-id-1)',
      })
    );

    // Chip JSON round-trips through the structured editor with the pinned shape.
    const bindingColumns = createRecordMock.mock.calls.find(c => c[0] === 'sprk_playbookconsumers')?.[1] as Record<
      string,
      unknown
    >;
    expect(JSON.parse(bindingColumns.sprk_chiptransitions as string)).toEqual([
      { target_binding_id: 'target-binding-guid', chip_label: 'Summarize this document' },
    ]);

    // Both rows now exist in the (fake) Dataverse.
    expect(actionRows).toHaveLength(1);
    expect(bindingRows).toHaveLength(1);
  }, 30000);

  it('a Binding without a target Action or tool description is refused (closed-catalog contract)', async () => {
    const user = userEvent.setup();
    renderShell();

    await waitFor(() => expect(screen.getByTestId('new-row-button')).toBeInTheDocument());
    await user.click(screen.getByTestId('tab-bindings'));
    await user.click(screen.getByTestId('new-row-button'));

    fireEvent.change(screen.getByLabelText('Binding name'), { target: { value: 'Incomplete' } });
    fireEvent.change(screen.getByLabelText('Consumer type'), { target: { value: 'incomplete' } });

    await user.click(screen.getByTestId('save-row-button'));

    expect(await screen.findByTestId('validation-error-bar')).toBeInTheDocument();
    expect(screen.getByText(/Select the target Action/)).toBeInTheDocument();
    expect(screen.getByText(/intent surface the agent loop matches/)).toBeInTheDocument();
    expect(createRecordMock).not.toHaveBeenCalled();
  }, 30000);

  it('no canvas/graph authoring surface is reachable anywhere on the page (NFR-08)', async () => {
    renderShell();
    await waitFor(() => expect(screen.getByTestId('catalog-editor-shell')).toBeInTheDocument());

    // The de-scoped canvas vocabulary must not exist in the rendered DOM.
    expect(document.body.innerHTML).not.toMatch(/react-flow|reactflow|Node Palette|node-palette|canvas/i);
    expect(screen.queryByText(/Node Types/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Run playbook/)).not.toBeInTheDocument();
  });
});
