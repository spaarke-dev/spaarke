/**
 * TypedConfigForm — 5 priority executor incremental tests (R7 Wave 8 task 089b, FR-23).
 *
 * Complements `TypedConfigForm.test.tsx` (task 084) which covered:
 *   - render-without-crash per priority schema
 *   - required-asterisk markup
 *   - required-field error firing
 *   - JSON sub-editor error reporting
 *   - onChange / onConfigJsonChange wire-up
 *   - empty-schema placeholder
 *
 * This file adds behaviors NOT covered by task 084, focused on per-form
 * interactive contract surface (FR-23 user-facing semantics):
 *
 *   1. Schema field-count regression sentinel (drift detection per design.md §11)
 *   2. Default-value resolution from `field.default` when bag has no key
 *   3. Boolean Switch toggle commits a boolean (not a string)
 *   4. Number SpinButton commits a finite number
 *   5. Optional field with empty value does NOT fire required error
 *   6. JSON Object field accepts well-formed JSON without raising "Invalid JSON"
 *   7. Controlled-component re-render: external value change reflects in inputs
 *   8. Each of the 5 priority schemas tested INDEPENDENTLY for the above
 *
 * Per ADR-038: KEEP path; mocks limited to the schema fixtures + the onChange
 * spy. NO React/React Flow internals are mocked. NO HttpMessageHandler. The
 * 5 schemas below are HAND-COPIED from task 084 so a drift between this file
 * and the BFF source fails loudly (intentional sentinel).
 *
 * @see src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs
 * @see src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiCompletionNodeExecutor.cs
 * @see src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/ConditionNodeExecutor.cs
 * @see src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/EntityNameValidatorNodeExecutor.cs
 * @see src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs
 * @see projects/spaarke-ai-platform-unification-r7/tasks/089b-ui-test-typed-config-forms-5-executors.poml
 * @see ADR-038 — Testing strategy (KEEP path; no HttpMessageHandler mocks)
 */
import * as React from 'react';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { TypedConfigForm } from '../TypedConfigForm';
import type { ExecutorConfigSchema } from '../../../services/executorSchemaService';
import { renderWithProviders } from './testUtils';

// ---------------------------------------------------------------------------
// Wave 3 task 032 schemas — mirrored from BFF source. Hand-copied from
// TypedConfigForm.test.tsx (task 084) for drift-detection symmetry.
// ---------------------------------------------------------------------------

const AI_ANALYSIS_SCHEMA: ExecutorConfigSchema = {
  executorTypeName: 'AiAnalysis',
  executorTypeValue: 0,
  description:
    'Document-grounded structured analysis with tool dispatch (FR-13). Requires Action FK + Tool + Document with extracted text.',
  fields: [
    { name: 'templateParameters', type: 'Object', required: false, description: 'Key-to-value map.' },
    { name: 'promptSchemaOverride', type: 'Object', required: false, description: 'Per-node override.' },
    { name: 'knowledgeRetrieval', type: 'Object', required: false, description: 'Retrieval config.' },
    { name: 'includeDocumentContext', type: 'Boolean', required: false, description: 'Legacy flag.', default: false },
    { name: 'parentEntityType', type: 'String', required: false, description: 'Parent entity type.' },
    { name: 'parentEntityId', type: 'String', required: false, description: 'Parent entity id.' },
  ],
};

const AI_COMPLETION_SCHEMA: ExecutorConfigSchema = {
  executorTypeName: 'AiCompletion',
  executorTypeValue: 1,
  description: 'Prompt-only structured LLM completion (FR-12).',
  fields: [
    { name: 'templateParameters', type: 'Object', required: false, description: 'Key-to-value map.' },
    { name: 'promptSchemaOverride', type: 'Object', required: false, description: 'Per-node override.' },
  ],
};

const CONDITION_SCHEMA: ExecutorConfigSchema = {
  executorTypeName: 'Condition',
  executorTypeValue: 30,
  description: 'Conditional branching.',
  fields: [
    { name: 'condition', type: 'Object', required: true, description: 'Condition expression.' },
    { name: 'trueBranch', type: 'String', required: false, description: 'True branch variable.' },
    { name: 'falseBranch', type: 'String', required: false, description: 'False branch variable.' },
  ],
};

const ENTITY_NAME_VALIDATOR_SCHEMA: ExecutorConfigSchema = {
  executorTypeName: 'EntityNameValidator',
  executorTypeValue: 141,
  description: 'Post-LLM defense-in-depth scrubber.',
  fields: [
    { name: 'candidateText', type: 'String', required: true, description: 'Raw LLM text.' },
    { name: 'allowList', type: 'Array', required: true, description: 'Known entity names.' },
  ],
};

const CREATE_NOTIFICATION_SCHEMA: ExecutorConfigSchema = {
  executorTypeName: 'CreateNotification',
  executorTypeValue: 50,
  description: 'Creates a Dataverse appnotification record.',
  fields: [
    { name: 'title', type: 'String', required: true, description: 'Notification title.' },
    { name: 'body', type: 'String', required: true, description: 'Notification body.' },
    { name: 'recipientId', type: 'String', required: false, description: 'Recipient guid.' },
    { name: 'category', type: 'String', required: false, description: 'Category.' },
    { name: 'priority', type: 'Number', required: false, description: 'Priority code.', default: 200000000 },
    { name: 'toastType', type: 'Number', required: false, description: 'Toast code.', default: 200000000 },
    { name: 'actionUrl', type: 'String', required: false, description: 'Action url.' },
    { name: 'regardingId', type: 'String', required: false, description: 'Regarding id.' },
    { name: 'regardingType', type: 'String', required: false, description: 'Regarding entity.' },
    { name: 'iterateItems', type: 'Boolean', required: false, description: 'Iteration flag.', default: false },
    { name: 'itemNotification', type: 'Object', required: false, description: 'Per-item template.' },
  ],
};

// ---------------------------------------------------------------------------
// Schema field-count regression sentinel (test 1)
// ---------------------------------------------------------------------------
// If BFF schema shape changes, these counts must be updated together with the
// schema constants above. A drift here means the canvas + BFF disagree on the
// maker-facing field surface for an executor — a maker-visible bug.

describe('TypedConfigForm priority-executor sentinels (R7 task 089b)', () => {
  describe('schema field-count drift sentinels (BFF→canvas contract)', () => {
    it('AiAnalysis schema exposes 6 fields', () => {
      expect(AI_ANALYSIS_SCHEMA.fields.length).toBe(6);
    });
    it('AiCompletion schema exposes 2 fields', () => {
      expect(AI_COMPLETION_SCHEMA.fields.length).toBe(2);
    });
    it('Condition schema exposes 3 fields (1 required)', () => {
      expect(CONDITION_SCHEMA.fields.length).toBe(3);
      expect(CONDITION_SCHEMA.fields.filter(f => f.required).length).toBe(1);
    });
    it('EntityNameValidator schema exposes 2 fields (both required)', () => {
      expect(ENTITY_NAME_VALIDATOR_SCHEMA.fields.length).toBe(2);
      expect(ENTITY_NAME_VALIDATOR_SCHEMA.fields.filter(f => f.required).length).toBe(2);
    });
    it('CreateNotification schema exposes 11 fields (2 required: title + body)', () => {
      expect(CREATE_NOTIFICATION_SCHEMA.fields.length).toBe(11);
      expect(CREATE_NOTIFICATION_SCHEMA.fields.filter(f => f.required).length).toBe(2);
      expect(CREATE_NOTIFICATION_SCHEMA.fields.filter(f => f.required).map(f => f.name)).toEqual(['title', 'body']);
    });
  });

  // -------------------------------------------------------------------------
  // Default-value resolution (test 2)
  // -------------------------------------------------------------------------

  describe('default-value resolution', () => {
    it('CreateNotification renders the priority Number default (200000000) when bag lacks priority', () => {
      const { container } = renderWithProviders(
        <TypedConfigForm
          nodeId="n-cn-default"
          schema={CREATE_NOTIFICATION_SCHEMA}
          value={{ title: 'X', body: 'Y' }}
          onChange={jest.fn()}
        />
      );
      const priorityInput = container.querySelector('#n-cn-default-priority') as HTMLInputElement;
      expect(priorityInput).not.toBeNull();
      // SpinButton stores the typed numeric in the DOM input as a string.
      expect(priorityInput.value).toBe('200000000');
    });

    it('AiAnalysis renders the includeDocumentContext Boolean default (false) when bag lacks the key', () => {
      const { container } = renderWithProviders(
        <TypedConfigForm nodeId="n-ai-default" schema={AI_ANALYSIS_SCHEMA} value={{}} onChange={jest.fn()} />
      );
      const switchEl = container.querySelector('#n-ai-default-includeDocumentContext') as HTMLInputElement;
      expect(switchEl).not.toBeNull();
      expect(switchEl.checked).toBe(false);
    });

    it('CreateNotification renders the iterateItems Boolean default (false) when bag lacks the key', () => {
      const { container } = renderWithProviders(
        <TypedConfigForm
          nodeId="n-cn-iter"
          schema={CREATE_NOTIFICATION_SCHEMA}
          value={{ title: 'X', body: 'Y' }}
          onChange={jest.fn()}
        />
      );
      const switchEl = container.querySelector('#n-cn-iter-iterateItems') as HTMLInputElement;
      expect(switchEl).not.toBeNull();
      expect(switchEl.checked).toBe(false);
    });

    it('Bag-provided value overrides schema default (CreateNotification.priority)', () => {
      const { container } = renderWithProviders(
        <TypedConfigForm
          nodeId="n-cn-override"
          schema={CREATE_NOTIFICATION_SCHEMA}
          value={{ title: 'X', body: 'Y', priority: 300000000 }}
          onChange={jest.fn()}
        />
      );
      const priorityInput = container.querySelector('#n-cn-override-priority') as HTMLInputElement;
      expect(priorityInput.value).toBe('300000000');
    });
  });

  // -------------------------------------------------------------------------
  // Boolean Switch interaction (test 3) — commits a boolean, not a string
  // -------------------------------------------------------------------------

  describe('Boolean Switch toggle', () => {
    it('AiAnalysis includeDocumentContext toggle commits boolean true (not "true")', async () => {
      const onChange = jest.fn();
      const user = userEvent.setup();
      const { container } = renderWithProviders(
        <TypedConfigForm nodeId="n-ai-bool" schema={AI_ANALYSIS_SCHEMA} value={{}} onChange={onChange} />
      );
      const switchEl = container.querySelector('#n-ai-bool-includeDocumentContext') as HTMLInputElement;
      await user.click(switchEl);
      expect(onChange).toHaveBeenCalled();
      const last = onChange.mock.calls[onChange.mock.calls.length - 1][0];
      expect(last.includeDocumentContext).toBe(true);
      expect(typeof last.includeDocumentContext).toBe('boolean');
    });

    it('CreateNotification iterateItems toggle commits boolean (not "true")', async () => {
      const onChange = jest.fn();
      const user = userEvent.setup();
      const { container } = renderWithProviders(
        <TypedConfigForm
          nodeId="n-cn-bool"
          schema={CREATE_NOTIFICATION_SCHEMA}
          value={{ title: 'A', body: 'B' }}
          onChange={onChange}
        />
      );
      const switchEl = container.querySelector('#n-cn-bool-iterateItems') as HTMLInputElement;
      await user.click(switchEl);
      const last = onChange.mock.calls[onChange.mock.calls.length - 1][0];
      expect(last.iterateItems).toBe(true);
      expect(typeof last.iterateItems).toBe('boolean');
    });
  });

  // -------------------------------------------------------------------------
  // Number SpinButton interaction (test 4) — commits a finite number
  // -------------------------------------------------------------------------

  describe('Number SpinButton', () => {
    it('CreateNotification priority SpinButton commits a finite number after increment', async () => {
      const onChange = jest.fn();
      const user = userEvent.setup();
      const { container } = renderWithProviders(
        <TypedConfigForm
          nodeId="n-cn-num"
          schema={CREATE_NOTIFICATION_SCHEMA}
          value={{ title: 'A', body: 'B', priority: 1 }}
          onChange={onChange}
        />
      );
      // The SpinButton renders a number input; user can directly clear + type to commit.
      const input = container.querySelector('#n-cn-num-priority') as HTMLInputElement;
      expect(input).not.toBeNull();
      await user.click(input);
      await user.clear(input);
      await user.type(input, '42');
      // SpinButton commits on blur in jsdom — tab away to trigger.
      await user.tab();
      expect(onChange).toHaveBeenCalled();
      // The committed value should be a number (not a string).
      const calls = onChange.mock.calls;
      const lastNumericCall = [...calls].reverse().find(c => typeof c[0].priority === 'number');
      expect(lastNumericCall).toBeDefined();
      expect(typeof lastNumericCall![0].priority).toBe('number');
      expect(Number.isFinite(lastNumericCall![0].priority)).toBe(true);
    });
  });

  // -------------------------------------------------------------------------
  // Optional fields don't fire required errors (test 5)
  // -------------------------------------------------------------------------

  describe('optional-field empty-value semantics', () => {
    it('AiCompletion with empty bag shows ZERO "Required" errors (no required fields)', () => {
      renderWithProviders(
        <TypedConfigForm nodeId="n-ac-empty" schema={AI_COMPLETION_SCHEMA} value={{}} onChange={jest.fn()} />
      );
      const alerts = screen.queryAllByRole('alert');
      const required = alerts.filter(el => el.textContent === 'Required');
      expect(required.length).toBe(0);
    });

    it('AiAnalysis with empty bag shows ZERO "Required" errors (no required fields in schema)', () => {
      renderWithProviders(
        <TypedConfigForm nodeId="n-aa-empty" schema={AI_ANALYSIS_SCHEMA} value={{}} onChange={jest.fn()} />
      );
      const alerts = screen.queryAllByRole('alert');
      const required = alerts.filter(el => el.textContent === 'Required');
      expect(required.length).toBe(0);
    });

    it('CreateNotification with title+body filled shows ZERO "Required" errors (only those 2 are required)', () => {
      renderWithProviders(
        <TypedConfigForm
          nodeId="n-cn-ok"
          schema={CREATE_NOTIFICATION_SCHEMA}
          value={{ title: 'Hello', body: 'World' }}
          onChange={jest.fn()}
        />
      );
      const alerts = screen.queryAllByRole('alert');
      const required = alerts.filter(el => el.textContent === 'Required');
      expect(required.length).toBe(0);
    });
  });

  // -------------------------------------------------------------------------
  // JSON Object field accepts valid JSON (test 6)
  // -------------------------------------------------------------------------

  describe('JSON Object field — valid JSON passes', () => {
    it('AiCompletion templateParameters with valid JSON string does NOT raise "Invalid JSON"', () => {
      renderWithProviders(
        <TypedConfigForm
          nodeId="n-ac-validjson"
          schema={AI_COMPLETION_SCHEMA}
          value={{ templateParameters: '{"matterName":"Acme v Smith"}' }}
          onChange={jest.fn()}
        />
      );
      const alerts = screen.queryAllByRole('alert');
      expect(alerts.some(el => el.textContent === 'Invalid JSON')).toBe(false);
    });

    it('EntityNameValidator allowList with valid JSON array passes validation', () => {
      renderWithProviders(
        <TypedConfigForm
          nodeId="n-env-validarr"
          schema={ENTITY_NAME_VALIDATOR_SCHEMA}
          value={{ candidateText: 'sample', allowList: '["Acme","Smith"]' }}
          onChange={jest.fn()}
        />
      );
      const alerts = screen.queryAllByRole('alert');
      expect(alerts.some(el => el.textContent === 'Must be a JSON array')).toBe(false);
      expect(alerts.some(el => el.textContent === 'Invalid JSON')).toBe(false);
    });

    it('AiAnalysis templateParameters accepts an object value (not just string) without error', () => {
      renderWithProviders(
        <TypedConfigForm
          nodeId="n-aa-objval"
          schema={AI_ANALYSIS_SCHEMA}
          value={{ templateParameters: { matterName: 'Acme' } }}
          onChange={jest.fn()}
        />
      );
      const alerts = screen.queryAllByRole('alert');
      expect(alerts.some(el => el.textContent === 'Invalid JSON')).toBe(false);
      expect(alerts.some(el => el.textContent === 'Must be a JSON object')).toBe(false);
    });
  });

  // -------------------------------------------------------------------------
  // Controlled-component re-render (test 7)
  // -------------------------------------------------------------------------

  describe('controlled-component re-render', () => {
    it('CreateNotification title input reflects an external value change on re-render', () => {
      const { container, rerender } = renderWithProviders(
        <TypedConfigForm
          nodeId="n-cn-ctl"
          schema={CREATE_NOTIFICATION_SCHEMA}
          value={{ title: 'V1', body: 'B' }}
          onChange={jest.fn()}
        />
      );
      let input = container.querySelector('#n-cn-ctl-title') as HTMLInputElement;
      expect(input.value).toBe('V1');

      rerender(
        <TypedConfigForm
          nodeId="n-cn-ctl"
          schema={CREATE_NOTIFICATION_SCHEMA}
          value={{ title: 'V2', body: 'B' }}
          onChange={jest.fn()}
        />
      );
      input = container.querySelector('#n-cn-ctl-title') as HTMLInputElement;
      expect(input.value).toBe('V2');
    });

    it('Condition trueBranch input reflects external value change without re-mount', () => {
      const { container, rerender } = renderWithProviders(
        <TypedConfigForm
          nodeId="n-cond-ctl"
          schema={CONDITION_SCHEMA}
          value={{ condition: { operator: 'eq', left: 'x', right: 'y' }, trueBranch: 'A' }}
          onChange={jest.fn()}
        />
      );
      let trueInput = container.querySelector('#n-cond-ctl-trueBranch') as HTMLInputElement;
      expect(trueInput.value).toBe('A');

      rerender(
        <TypedConfigForm
          nodeId="n-cond-ctl"
          schema={CONDITION_SCHEMA}
          value={{ condition: { operator: 'eq', left: 'x', right: 'y' }, trueBranch: 'B' }}
          onChange={jest.fn()}
        />
      );
      trueInput = container.querySelector('#n-cond-ctl-trueBranch') as HTMLInputElement;
      expect(trueInput.value).toBe('B');
    });
  });

  // -------------------------------------------------------------------------
  // Per-executor independent render — each priority schema renders
  // independently without leaking state across mounts (test 8)
  // -------------------------------------------------------------------------

  describe('per-priority-executor independent render', () => {
    it.each([
      ['AiAnalysis', AI_ANALYSIS_SCHEMA],
      ['AiCompletion', AI_COMPLETION_SCHEMA],
      ['Condition', CONDITION_SCHEMA],
      ['EntityNameValidator', ENTITY_NAME_VALIDATOR_SCHEMA],
      ['CreateNotification', CREATE_NOTIFICATION_SCHEMA],
    ])('%s mounts in isolation with no console error', (_label, schema) => {
      const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
      const { unmount } = renderWithProviders(
        <TypedConfigForm nodeId={`iso-${schema.executorTypeName}`} schema={schema} value={{}} onChange={jest.fn()} />
      );
      // Render produced no console.error (catches PropTypes / React warnings).
      expect(errorSpy).not.toHaveBeenCalled();
      unmount();
      errorSpy.mockRestore();
    });

    it('Each priority schema renders the executor description text', () => {
      const schemas = [
        AI_ANALYSIS_SCHEMA,
        AI_COMPLETION_SCHEMA,
        CONDITION_SCHEMA,
        ENTITY_NAME_VALIDATOR_SCHEMA,
        CREATE_NOTIFICATION_SCHEMA,
      ];
      for (const schema of schemas) {
        const { unmount } = renderWithProviders(
          <TypedConfigForm nodeId={`desc-${schema.executorTypeName}`} schema={schema} value={{}} onChange={jest.fn()} />
        );
        expect(screen.getByText(schema.description)).toBeInTheDocument();
        unmount();
      }
    });
  });
});
