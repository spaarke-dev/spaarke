/**
 * BindingConfigEditor + SchemaJsonEditor + the validator twin
 * (task 053 / FR-P4-04).
 *
 * Pins the record-form authoring guard: the EXACT G-P3 round-1 outage
 * payload (property-level "required": true) must render an inline error —
 * plus the per-column Binding contract validation (chip transitions,
 * on-event bindings, match conditions).
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { BindingConfigEditor } from '../components/BindingConfigEditor';
import { SchemaJsonEditor } from '../components/SchemaJsonEditor';
import {
  validateChipTransitionsJson,
  validateMatchConditionsJson,
  validateOnEventBindingsJson,
  validateSchemaForAuthoring,
} from '../utils/openAiSchemaValidator';

const renderWithProvider = (ui: React.ReactElement) =>
  render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

const OUTAGE_PAYLOAD =
  '{"type":"object","properties":{"due_date":{"type":"string","required":true}},"required":["due_date"]}';

// ─────────────────────────────────────────────────────────────────────────────
// Validator twin — rule matrix pins (server source of truth:
// OpenAiFunctionSchemaValidator.cs; sibling twin: PlaybookBuilder
// schemaValidation.ts)
// ─────────────────────────────────────────────────────────────────────────────

describe('openAiSchemaValidator twin — shared rule matrix', () => {
  it('rejects the exact G-P3 outage payload (property-level "required": true)', () => {
    const error = validateSchemaForAuthoring(OUTAGE_PAYLOAD);
    expect(error).not.toBeNull();
    expect(error).toContain('property-level "required": true is not JSON Schema');
  });

  it('accepts a valid function-parameters schema with maker keywords', () => {
    expect(
      validateSchemaForAuthoring(
        '{"type":"object","properties":{"due_date":{"type":"string","elicitation_prompt":"?"}},"required":["due_date"]}'
      )
    ).toBeNull();
  });

  it('rejects array schemas without items (OpenAI subset rule)', () => {
    expect(validateSchemaForAuthoring('{"type":"object","properties":{"files":{"type":"array"}}}')).toContain(
      "array schemas must declare 'items'"
    );
  });

  it('is authoring-strict: malformed JSON and non-object roots are errors', () => {
    expect(validateSchemaForAuthoring('{ nope')).toContain('not valid JSON');
    expect(validateSchemaForAuthoring('[1]')).toContain('root must be a JSON object');
    expect(validateSchemaForAuthoring('')).toBeNull();
  });

  it('validates chip transitions / on-event bindings / match conditions pinned shapes', () => {
    expect(validateChipTransitionsJson('[{"target_binding_id":"b1","chip_label":"Summarize"}]')).toBeNull();
    expect(validateChipTransitionsJson('[{"chip_label":"no target"}]')).toContain('target_binding_id');

    expect(validateOnEventBindingsJson('[{"event":"document_uploaded","order":1}]')).toBeNull();
    expect(validateOnEventBindingsJson('[{"event":"document_uploaded"}]')).toContain('.order');

    expect(validateMatchConditionsJson('{"entityType":["sprk_matter"]}')).toBeNull();
    expect(validateMatchConditionsJson('{"a":{"nested":true}}')).toContain('string or array of strings');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// BindingConfigEditor — per-column variants
// ─────────────────────────────────────────────────────────────────────────────

describe('BindingConfigEditor (sprk_playbookconsumer variant)', () => {
  it('renders the chip-transitions variant with validity badge', () => {
    renderWithProvider(
      <BindingConfigEditor
        boundAttributeName="sprk_chiptransitions"
        value='[{"target_binding_id":"b1","chip_label":"Summarize this document"}]'
        onChange={jest.fn()}
      />
    );

    expect(screen.getByText('Chip transitions')).toBeInTheDocument();
    expect(screen.getByTestId('binding-validity-badge')).toHaveTextContent('Valid');
  });

  it('flags an invalid chip-transitions shape inline', () => {
    renderWithProvider(
      <BindingConfigEditor
        boundAttributeName="sprk_chiptransitions"
        value='[{"chip_label":"missing target"}]'
        onChange={jest.fn()}
      />
    );

    expect(screen.getByTestId('binding-validity-badge')).toHaveTextContent('Invalid');
    // The specific validation error (the guidance text also mentions the
    // field name, so match the error copy exactly).
    expect(screen.getByText(/target_binding_id: required non-empty string/)).toBeInTheDocument();
  });

  it('renders the on-event-bindings variant and flags a missing order', () => {
    renderWithProvider(
      <BindingConfigEditor
        boundAttributeName="sprk_oneventbindings"
        value='[{"event":"document_uploaded"}]'
        onChange={jest.fn()}
      />
    );

    expect(screen.getByText('On-event bindings')).toBeInTheDocument();
    expect(screen.getByText(/\.order/)).toBeInTheDocument();
  });

  it('renders the match-conditions variant', () => {
    renderWithProvider(
      <BindingConfigEditor
        boundAttributeName="sprk_matchconditions"
        value='{"entityType":"sprk_matter"}'
        onChange={jest.fn()}
      />
    );

    expect(screen.getByText('Match conditions')).toBeInTheDocument();
    expect(screen.getByTestId('binding-validity-badge')).toHaveTextContent('Valid');
  });

  it('renders the tool-description variant with intent-surface guidance (no JSON validation)', () => {
    renderWithProvider(
      <BindingConfigEditor
        boundAttributeName="sprk_tooldescription"
        value="Summarize the files uploaded to this chat session."
        onChange={jest.fn()}
      />
    );

    expect(screen.getByText('Tool description')).toBeInTheDocument();
    expect(screen.getByText(/agent loop sees/i)).toBeInTheDocument();
    expect(screen.getByTestId('binding-validity-badge')).toHaveTextContent('Valid');
  });

  it('propagates edits through onChange', () => {
    const onChange = jest.fn();
    renderWithProvider(<BindingConfigEditor boundAttributeName="sprk_oneventbindings" value="" onChange={onChange} />);

    fireEvent.change(screen.getByLabelText('On-event bindings'), {
      target: { value: '[{"event":"document_uploaded","order":1}]' },
    });

    expect(onChange).toHaveBeenCalledWith('[{"event":"document_uploaded","order":1}]');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// SchemaJsonEditor — Action schema columns
// ─────────────────────────────────────────────────────────────────────────────

describe('SchemaJsonEditor (Action schema columns)', () => {
  it('shows the outage payload as an inline error on sprk_inputschema (never silently authorable)', () => {
    renderWithProvider(
      <SchemaJsonEditor boundAttributeName="sprk_inputschema" value={OUTAGE_PAYLOAD} onChange={jest.fn()} />
    );

    expect(screen.getByTestId('schema-validity-badge')).toHaveTextContent('Invalid');
    expect(screen.getByText(/property-level "required": true is not JSON Schema/)).toBeInTheDocument();
  });

  it('marks a corrected schema Valid', () => {
    renderWithProvider(
      <SchemaJsonEditor
        boundAttributeName="sprk_inputschema"
        value='{"type":"object","properties":{"due_date":{"type":"string"}},"required":["due_date"]}'
        onChange={jest.fn()}
      />
    );

    expect(screen.getByTestId('schema-validity-badge')).toHaveTextContent('Valid');
  });

  it('labels the output-schema variant distinctly', () => {
    renderWithProvider(<SchemaJsonEditor boundAttributeName="sprk_outputschemajson" value="" onChange={jest.fn()} />);

    expect(screen.getByText(/Output schema/)).toBeInTheDocument();
    expect(screen.getByText(/streaming emission order/i)).toBeInTheDocument();
  });
});
