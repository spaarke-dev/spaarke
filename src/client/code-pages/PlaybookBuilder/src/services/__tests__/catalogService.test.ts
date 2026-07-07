/**
 * catalogService — validation-gated save paths + row↔column mapping
 * (task 053 / FR-P4-04). The save gate is the fail-early twin of the H1
 * projection guard: an invalid row must NEVER reach Dataverse.
 */

import {
  CatalogValidationError,
  saveAction,
  saveBinding,
  validateActionRow,
  validateBindingRow,
} from '../catalogService';
import * as dataverseClient from '../dataverseClient';
import {
  ActionKind,
  BindingDisposition,
  BindingRisk,
  newActionRow,
  newBindingRow,
} from '../../types/catalog';
import type { ActionRow, BindingRow } from '../../types/catalog';

jest.mock('../dataverseClient', () => ({
  createRecord: jest.fn(),
  updateRecord: jest.fn(),
  retrieveMultipleRecords: jest.fn(),
}));

const createRecordMock = dataverseClient.createRecord as jest.Mock;
const updateRecordMock = dataverseClient.updateRecord as jest.Mock;

function validAction(): ActionRow {
  return {
    ...newActionRow(),
    name: 'Summarize chat files',
    actionCode: 'SUM-CHAT@v1',
    systemPrompt: 'Summarize the document below.',
    inputSchema: JSON.stringify({
      type: 'object',
      properties: { fileIds: { type: 'array', items: { type: 'string' } } },
    }),
    outputSchema: JSON.stringify({
      type: 'object',
      properties: { summary: { type: 'string' } },
      required: ['summary'],
      additionalProperties: false,
    }),
  };
}

function validBinding(): BindingRow {
  return {
    ...newBindingRow(),
    name: 'Chat summarize (default)',
    consumerType: 'chat-summarize',
    actionId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
    ucid: 'UC-A-1',
    toolDescription: 'Summarize the files uploaded to this chat session.',
  };
}

beforeEach(() => {
  jest.clearAllMocks();
  createRecordMock.mockResolvedValue('new-row-id');
  updateRecordMock.mockResolvedValue(undefined);
});

describe('validateActionRow', () => {
  it('valid row → no errors', () => {
    expect(validateActionRow(validAction())).toEqual({});
  });

  it('requires name, actionCode, and a prompt for Prompted kind', () => {
    const errors = validateActionRow(newActionRow());
    expect(errors.name).toBeDefined();
    expect(errors.actionCode).toBeDefined();
    expect(errors.systemPrompt).toBeDefined();
  });

  it('Coded kind requires a workflow class and no prompt', () => {
    const row: ActionRow = {
      ...validAction(),
      kind: ActionKind.Coded,
      workflowClass: '',
      systemPrompt: '',
    };
    const errors = validateActionRow(row);
    expect(errors.workflowClass).toContain('ICodedWorkflow');
    expect(errors.systemPrompt).toBeUndefined();
  });

  it('flags the G-P3 outage input schema (property-level required)', () => {
    const row = validAction();
    row.inputSchema = JSON.stringify({
      type: 'object',
      properties: { due_date: { type: 'string', required: true } },
    });
    const errors = validateActionRow(row);
    expect(errors.inputSchema).toContain('property-level "required": true');
  });

  it('flags an invalid output schema (array without items)', () => {
    const row = validAction();
    row.outputSchema = JSON.stringify({
      type: 'object',
      properties: { tldr: { type: 'array' } },
    });
    const errors = validateActionRow(row);
    expect(errors.outputSchema).toContain("array schemas must declare 'items'");
  });
});

describe('validateBindingRow', () => {
  it('valid row → no errors', () => {
    expect(validateBindingRow(validBinding())).toEqual({});
  });

  it('requires name, consumerType, target Action, and tool description', () => {
    const errors = validateBindingRow(newBindingRow());
    expect(errors.name).toBeDefined();
    expect(errors.consumerType).toBeDefined();
    expect(errors.actionId).toBeDefined();
    expect(errors.toolDescription).toBeDefined();
  });

  it('flags malformed chip transitions / on-event bindings JSON', () => {
    const row = validBinding();
    row.chipTransitionsJson = '[{"chip_label":"missing target"}]';
    row.onEventBindingsJson = '[{"event":"document_uploaded"}]';
    const errors = validateBindingRow(row);
    expect(errors.chipTransitionsJson).toContain('target_binding_id');
    expect(errors.onEventBindingsJson).toContain('.order');
  });
});

describe('saveAction — the fail-early gate', () => {
  it('REFUSES to write the outage schema (CatalogValidationError; zero Dataverse calls)', async () => {
    const row = validAction();
    row.inputSchema = JSON.stringify({
      type: 'object',
      properties: { due_date: { type: 'string', required: true } },
    });

    await expect(saveAction(row)).rejects.toBeInstanceOf(CatalogValidationError);
    expect(createRecordMock).not.toHaveBeenCalled();
    expect(updateRecordMock).not.toHaveBeenCalled();
  });

  it('creates a new row with the sprk_analysisaction column mapping', async () => {
    const id = await saveAction(validAction());

    expect(id).toBe('new-row-id');
    expect(createRecordMock).toHaveBeenCalledWith(
      'sprk_analysisactions',
      expect.objectContaining({
        sprk_name: 'Summarize chat files',
        sprk_actioncode: 'SUM-CHAT@v1',
        sprk_kind: ActionKind.Prompted,
        sprk_systemprompt: 'Summarize the document below.',
      })
    );
  });

  it('updates when the row carries an id', async () => {
    const row = { ...validAction(), id: 'existing-id' };
    const id = await saveAction(row);

    expect(id).toBe('existing-id');
    expect(updateRecordMock).toHaveBeenCalledWith('sprk_analysisactions', 'existing-id', expect.any(Object));
    expect(createRecordMock).not.toHaveBeenCalled();
  });
});

describe('saveBinding — full contract column mapping', () => {
  it('creates a row with option-set values, surfaces CSV, and the sprk_action bind', async () => {
    const row = validBinding();
    row.surfaces = ['assistant', 'record-form'];
    row.disposition = BindingDisposition.WorkProduct;
    row.risk = BindingRisk.AlwaysConfirm;
    row.chipTransitionsJson = JSON.stringify([
      { target_binding_id: 'target-1', chip_label: 'Summarize this document' },
    ]);
    row.onEventBindingsJson = '[{"event":"document_uploaded","order":1}]';

    await saveBinding(row);

    expect(createRecordMock).toHaveBeenCalledWith(
      'sprk_playbookconsumers',
      expect.objectContaining({
        sprk_consumertype: 'chat-summarize',
        sprk_ucid: 'UC-A-1',
        sprk_disposition: BindingDisposition.WorkProduct,
        sprk_risk: BindingRisk.AlwaysConfirm,
        sprk_surfaces: 'assistant,record-form',
        sprk_chiptransitions: row.chipTransitionsJson,
        sprk_oneventbindings: row.onEventBindingsJson,
        'sprk_action@odata.bind': '/sprk_analysisactions(aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee)',
      })
    );
  });

  it('REFUSES invalid rows without touching Dataverse', async () => {
    const row = validBinding();
    row.toolDescription = '';

    await expect(saveBinding(row)).rejects.toBeInstanceOf(CatalogValidationError);
    expect(createRecordMock).not.toHaveBeenCalled();
  });

  it('empty surfaces → null column (ALL surfaces per column dictionary)', async () => {
    await saveBinding(validBinding());
    const columns = createRecordMock.mock.calls[0][1] as Record<string, unknown>;
    expect(columns.sprk_surfaces).toBeNull();
  });
});
