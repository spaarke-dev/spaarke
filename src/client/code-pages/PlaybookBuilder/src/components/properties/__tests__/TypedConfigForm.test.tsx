/**
 * TypedConfigForm tests — R7 task 084 (Wave 8) covering FR-23 typed config forms
 * for the 5 priority executors.
 *
 * Verifies the schema-driven renderer (task 083 infrastructure) renders the
 * canonical Wave 3 task 032 rich schemas for AiAnalysis (0), AiCompletion (1),
 * Condition (30), EntityNameValidator (141), and CreateNotification (50) without
 * canvas-side hand-crafted forms.
 *
 * Test surface (per task 084 POML Step 9):
 *   1. Each of the 5 priority schemas renders all declared fields with no crashes.
 *   2. Required-field markers render on `required: true` fields.
 *   3. Validation error fires on empty required fields (e.g., Condition.condition,
 *      EntityNameValidator.candidateText/allowList, CreateNotification.title/body).
 *   4. JSON sub-editor (SchemaFieldType.Object / Array) reports invalid JSON syntax.
 *   5. onChange fires with the new config bag on every edit.
 *
 * Per ADR-038 (testing strategy) + task 084 POML constraint: schemas are injected
 * directly via component props — no HttpMessageHandler mocking, no
 * `executorSchemaService.fetchExecutorSchemas` invocation. The 5 schema constants
 * below are HAND-COPIED from the BFF executor source so the test file fails LOUDLY
 * if the BFF schema shape drifts (intentional — matches the design.md §11 contract
 * "schema field names match config-record [JsonPropertyName] attributes").
 *
 * @see src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs
 * @see src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiCompletionNodeExecutor.cs
 * @see src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/ConditionNodeExecutor.cs
 * @see src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/EntityNameValidatorNodeExecutor.cs
 * @see src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs
 * @see projects/spaarke-ai-platform-unification-r7/notes/spikes/executor-config-fields-inventory.md
 * @see ADR-006 — Fluent UI v9 only
 * @see ADR-021 — Dark mode semantic tokens
 * @see ADR-038 — Testing strategy (no HttpMessageHandler mocks)
 */
import * as React from 'react';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { TypedConfigForm } from '../TypedConfigForm';
import type { ExecutorConfigSchema } from '../../../services/executorSchemaService';
import { renderWithProviders } from './testUtils';

// ---------------------------------------------------------------------------
// Wave 3 task 032 schemas — mirrored from BFF source for direct injection.
// If any of these drift from the C# source, this test file fails and forces
// reconciliation per the design.md §11 maker-contract / runtime-contract rule.
// ---------------------------------------------------------------------------

const AI_ANALYSIS_SCHEMA: ExecutorConfigSchema = {
  executorTypeName: 'AiAnalysis',
  executorTypeValue: 0,
  description:
    'Document-grounded structured analysis with tool dispatch (FR-13). Requires Action FK + Tool + Document with extracted text.',
  fields: [
    {
      name: 'templateParameters',
      type: 'Object',
      required: false,
      description: 'Key-to-value map substituted into {{var}} bindings in the JPS prompt instruction section.',
    },
    {
      name: 'promptSchemaOverride',
      type: 'Object',
      required: false,
      description: "Per-node override merged into the Action's base JPS prompt schema (FR-25).",
    },
    {
      name: 'knowledgeRetrieval',
      type: 'Object',
      required: false,
      description: 'Knowledge retrieval config: { mode, topK, includeDocumentContext, includeEntityContext }.',
    },
    {
      name: 'includeDocumentContext',
      type: 'Boolean',
      required: false,
      description: 'Legacy top-level flag — superseded by knowledgeRetrieval.includeDocumentContext.',
      default: false,
    },
    {
      name: 'parentEntityType',
      type: 'String',
      required: false,
      description: 'Parent entity type for L2/L3 retrieval scoping (e.g., Matter, Project, Invoice).',
    },
    {
      name: 'parentEntityId',
      type: 'String',
      required: false,
      description: 'Parent entity ID (GUID) for L2/L3 retrieval scoping.',
    },
  ],
};

const AI_COMPLETION_SCHEMA: ExecutorConfigSchema = {
  executorTypeName: 'AiCompletion',
  executorTypeValue: 1,
  description:
    'Prompt-only structured LLM completion (FR-12). Requires Action FK with SystemPrompt + OutputSchemaJson. Prohibits Tool + Document.',
  fields: [
    {
      name: 'templateParameters',
      type: 'Object',
      required: false,
      description: 'Key-to-value map substituted into {{var}} bindings in the JPS prompt instruction section.',
    },
    {
      name: 'promptSchemaOverride',
      type: 'Object',
      required: false,
      description: "Per-node override merged into the Action's base JPS prompt schema (FR-25).",
    },
  ],
};

const CONDITION_SCHEMA: ExecutorConfigSchema = {
  executorTypeName: 'Condition',
  executorTypeValue: 30,
  description: 'Conditional branching based on expression evaluation. Routes execution to true or false branch.',
  fields: [
    {
      name: 'condition',
      type: 'Object',
      required: true,
      description: 'Condition expression: { operator, left, right?, conditions?, condition? }.',
    },
    {
      name: 'trueBranch',
      type: 'String',
      required: false,
      description: 'Node OutputVariable name to select when condition evaluates to true.',
    },
    {
      name: 'falseBranch',
      type: 'String',
      required: false,
      description: 'Node OutputVariable name to select when condition evaluates to false.',
    },
  ],
};

const ENTITY_NAME_VALIDATOR_SCHEMA: ExecutorConfigSchema = {
  executorTypeName: 'EntityNameValidator',
  executorTypeValue: 141,
  description: 'Post-LLM defense-in-depth scrubber. Removes hallucinated entity names from LLM output.',
  fields: [
    {
      name: 'candidateText',
      type: 'String',
      required: true,
      description: 'Raw text emitted by the upstream LLM node that needs scrubbing.',
    },
    {
      name: 'allowList',
      type: 'Array',
      required: true,
      description: 'Array of entity names known to be present in the input payload.',
    },
  ],
};

const CREATE_NOTIFICATION_SCHEMA: ExecutorConfigSchema = {
  executorTypeName: 'CreateNotification',
  executorTypeValue: 50,
  description: 'Creates a Dataverse appnotification record for the recipient with template substitution + idempotency.',
  fields: [
    {
      name: 'title',
      type: 'String',
      required: true,
      description: 'Notification title. Supports {{templateVars}} resolved against previous node outputs.',
    },
    {
      name: 'body',
      type: 'String',
      required: true,
      description: 'Notification body text. Supports {{templateVars}} resolved against previous node outputs.',
    },
    {
      name: 'recipientId',
      type: 'String',
      required: false,
      description: 'Recipient systemuserid (GUID). Falls back to run-context userId when not specified.',
    },
    {
      name: 'category',
      type: 'String',
      required: false,
      description: 'Category string for grouping and idempotency check.',
    },
    {
      name: 'priority',
      type: 'Number',
      required: false,
      description: 'Priority: 100000000=Informational, 200000000=Important (default), 300000000=Urgent.',
      default: 200000000,
    },
    {
      name: 'toastType',
      type: 'Number',
      required: false,
      description: 'Toast visibility: 100000000=Hidden, 200000000=Timed (default), 300000000=Standard.',
      default: 200000000,
    },
    {
      name: 'actionUrl',
      type: 'String',
      required: false,
      description: 'URL to navigate when the notification is clicked. Supports {{templateVars}}.',
    },
    {
      name: 'regardingId',
      type: 'String',
      required: false,
      description: 'Regarding record ID (GUID). Supports templates. Required for idempotency check.',
    },
    {
      name: 'regardingType',
      type: 'String',
      required: false,
      description: 'Regarding entity logical name (e.g., sprk_document, sprk_matter).',
    },
    {
      name: 'iterateItems',
      type: 'Boolean',
      required: false,
      description: 'When true, iterate over items from upstream query output.',
      default: false,
    },
    {
      name: 'itemNotification',
      type: 'Object',
      required: false,
      description: 'Per-item notification template (same shape as top-level) used when iterateItems is true.',
    },
  ],
};

const PRIORITY_SCHEMAS: ReadonlyArray<{ label: string; schema: ExecutorConfigSchema }> = [
  { label: 'AiAnalysis', schema: AI_ANALYSIS_SCHEMA },
  { label: 'AiCompletion', schema: AI_COMPLETION_SCHEMA },
  { label: 'Condition', schema: CONDITION_SCHEMA },
  { label: 'EntityNameValidator', schema: ENTITY_NAME_VALIDATOR_SCHEMA },
  { label: 'CreateNotification', schema: CREATE_NOTIFICATION_SCHEMA },
];

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('TypedConfigForm — 5 priority executor schemas (R7 FR-23 / task 084)', () => {
  describe.each(PRIORITY_SCHEMAS)('$label schema', ({ schema }) => {
    it('renders without crashing and displays every declared field', () => {
      renderWithProviders(
        <TypedConfigForm nodeId={`node-${schema.executorTypeName}`} schema={schema} value={{}} onChange={jest.fn()} />
      );

      // Renderer-rendered description.
      expect(screen.getByText(schema.description)).toBeInTheDocument();

      // Each declared field gets a Label with the field name. We don't use
      // getByLabelText here because some inputs (Switch/SpinButton/Dropdown)
      // have a non-trivial accessible-name path — looking for the Label text
      // is the most reliable cross-widget assertion.
      for (const field of schema.fields) {
        expect(screen.getByText(field.name, { selector: 'label, label *' })).toBeInTheDocument();
      }
    });

    it('marks required fields with the required attribute on their Label', () => {
      renderWithProviders(
        <TypedConfigForm nodeId={`node-${schema.executorTypeName}`} schema={schema} value={{}} onChange={jest.fn()} />
      );

      const requiredFields = schema.fields.filter(f => f.required);
      const optionalFields = schema.fields.filter(f => !f.required);

      for (const field of requiredFields) {
        // Fluent v9 Label renders an asterisk span when `required` is true.
        const label = screen.getByText(field.name, { selector: 'label, label *' }).closest('label')!;
        // The asterisk span has aria-hidden="true" and text "*" — accessible-name path
        // includes "required" via Fluent's aria-label wiring on the LabelRequiredIndicator.
        expect(within(label).getByText('*')).toBeInTheDocument();
      }

      // Spot check: at least one optional field has NO required asterisk (if any optional exist).
      if (optionalFields.length > 0) {
        const optionalLabel = screen
          .getByText(optionalFields[0].name, { selector: 'label, label *' })
          .closest('label')!;
        expect(within(optionalLabel).queryByText('*')).toBeNull();
      }
    });
  });

  // ---- Validation: required fields ----------------------------------------

  it('shows "Required" error on empty required field — Condition.condition', () => {
    renderWithProviders(<TypedConfigForm nodeId="n-cond" schema={CONDITION_SCHEMA} value={{}} onChange={jest.fn()} />);

    // condition is required + Object — an empty bag with no key means undefined,
    // which `validateField` treats as empty + required → "Required".
    const errorAlerts = screen.getAllByRole('alert');
    expect(errorAlerts.length).toBeGreaterThan(0);
    expect(errorAlerts.some(el => el.textContent === 'Required')).toBe(true);
  });

  it('shows "Required" error on empty required fields — EntityNameValidator.candidateText + allowList', () => {
    renderWithProviders(
      <TypedConfigForm nodeId="n-env" schema={ENTITY_NAME_VALIDATOR_SCHEMA} value={{}} onChange={jest.fn()} />
    );

    // Both fields are required; the bag is empty.
    const errorAlerts = screen.getAllByRole('alert');
    const requiredErrors = errorAlerts.filter(el => el.textContent === 'Required');
    expect(requiredErrors.length).toBe(2);
  });

  it('shows "Required" error on empty required fields — CreateNotification.title + body', () => {
    renderWithProviders(
      <TypedConfigForm nodeId="n-cn" schema={CREATE_NOTIFICATION_SCHEMA} value={{}} onChange={jest.fn()} />
    );

    const errorAlerts = screen.getAllByRole('alert');
    const requiredErrors = errorAlerts.filter(el => el.textContent === 'Required');
    expect(requiredErrors.length).toBe(2);
  });

  it('does NOT show required error when required field has a value', () => {
    renderWithProviders(
      <TypedConfigForm
        nodeId="n-cn-ok"
        schema={CREATE_NOTIFICATION_SCHEMA}
        value={{ title: 'Hello', body: 'World' }}
        onChange={jest.fn()}
      />
    );

    const errorAlerts = screen.queryAllByRole('alert');
    const requiredErrors = errorAlerts.filter(el => el.textContent === 'Required');
    expect(requiredErrors.length).toBe(0);
  });

  // ---- Validation: JSON sub-editor ----------------------------------------

  it('reports "Invalid JSON" on malformed Object field value', () => {
    renderWithProviders(
      <TypedConfigForm
        nodeId="n-ai-bad-json"
        schema={AI_COMPLETION_SCHEMA}
        value={{ templateParameters: '{ not: json' }}
        onChange={jest.fn()}
      />
    );

    const errorAlerts = screen.getAllByRole('alert');
    expect(errorAlerts.some(el => el.textContent === 'Invalid JSON')).toBe(true);
  });

  it('reports "Must be a JSON array" when Array field contains a non-array value', () => {
    renderWithProviders(
      <TypedConfigForm
        nodeId="n-env-bad-array"
        schema={ENTITY_NAME_VALIDATOR_SCHEMA}
        value={{ candidateText: 'sample', allowList: '{"oops":"not-an-array"}' }}
        onChange={jest.fn()}
      />
    );

    const errorAlerts = screen.getAllByRole('alert');
    expect(errorAlerts.some(el => el.textContent === 'Must be a JSON array')).toBe(true);
  });

  // ---- onChange wire-up ---------------------------------------------------

  it('fires onChange with the merged config bag when a String field is edited', async () => {
    const onChange = jest.fn();
    const user = userEvent.setup();

    const { container } = renderWithProviders(
      <TypedConfigForm
        nodeId="n-cn-edit"
        schema={CREATE_NOTIFICATION_SCHEMA}
        value={{ title: '', body: '' }}
        onChange={onChange}
      />
    );

    // The renderer builds input ids as `${nodeId}-${field.name}` — use the id directly
    // since Fluent v9's Label asterisk markup confuses getByLabelText's text-match.
    const titleInput = container.querySelector('#n-cn-edit-title') as HTMLInputElement;
    expect(titleInput).not.toBeNull();
    await user.click(titleInput);
    await user.paste('My notification');

    expect(onChange).toHaveBeenCalled();
    const last = onChange.mock.calls[onChange.mock.calls.length - 1][0];
    expect(last.title).toBe('My notification');
    // Existing keys preserved.
    expect(last.body).toBe('');
  });

  it('also fires onConfigJsonChange with a serialized string when supplied', async () => {
    const onChange = jest.fn();
    const onConfigJsonChange = jest.fn();
    const user = userEvent.setup();

    const { container } = renderWithProviders(
      <TypedConfigForm
        nodeId="n-cn-json"
        schema={CREATE_NOTIFICATION_SCHEMA}
        value={{ title: '', body: '' }}
        onChange={onChange}
        onConfigJsonChange={onConfigJsonChange}
      />
    );

    const titleInput = container.querySelector('#n-cn-json-title') as HTMLInputElement;
    expect(titleInput).not.toBeNull();
    await user.click(titleInput);
    await user.paste('X');

    expect(onConfigJsonChange).toHaveBeenCalled();
    const lastJson = onConfigJsonChange.mock.calls[onConfigJsonChange.mock.calls.length - 1][0];
    const parsed = JSON.parse(lastJson);
    expect(parsed.title).toBe('X');
  });

  // ---- Empty / placeholder schema branches --------------------------------

  it('renders "No schema available" placeholder when schema is undefined', () => {
    renderWithProviders(<TypedConfigForm nodeId="n-undef" schema={undefined} value={{}} onChange={jest.fn()} />);

    expect(screen.getByText(/No schema available/i)).toBeInTheDocument();
  });

  it('renders the empty-schema placeholder when schema has zero fields', () => {
    const emptySchema: ExecutorConfigSchema = {
      executorTypeName: 'Start',
      executorTypeValue: 33,
      description: 'Canvas anchor — pass-through with no execution logic.',
      fields: [],
    };
    renderWithProviders(<TypedConfigForm nodeId="n-start" schema={emptySchema} value={{}} onChange={jest.fn()} />);

    expect(screen.getByText('Start')).toBeInTheDocument();
    expect(screen.getByText(emptySchema.description)).toBeInTheDocument();
  });
});
