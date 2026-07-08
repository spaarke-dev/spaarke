/**
 * ActionEditorForm — BA authoring form for ONE `sprk_analysisaction` row
 * (the execution unit, canonical §6.1; FR-P4-04).
 *
 * Fields: name, action code, description, kind (prompted | coded),
 * workflow class (coded only), JPS prompt, input schema, output schema,
 * default model tier. Schema fields validate live against the client twin of
 * `OpenAiFunctionSchemaValidator` — the G-P3 outage shape cannot be authored.
 *
 * ADR-021: Fluent v9 tokens only; verified in light + dark themes.
 */

import { Button, Dropdown, Field, Input, Option, Textarea, makeStyles, tokens } from '@fluentui/react-components';
import { ActionKind, AiModelTier } from '../../types/catalog';
import type { ActionRow } from '../../types/catalog';
import type { ValidationErrors } from '../../services/catalogService';
import { validateJpsPrompt, validateSchemaForAuthoring } from '../../services/schemaValidation';
import { JsonField } from './JsonField';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  row: {
    display: 'flex',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  grow: {
    flexGrow: 1,
    minWidth: '220px',
  },
  prompt: {
    width: '100%',
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
  },
});

const KIND_LABELS: Record<ActionKind, string> = {
  [ActionKind.Prompted]: 'Prompted (JPS prompt via ActionRunner)',
  [ActionKind.Coded]: 'Coded (registered ICodedWorkflow)',
};

const TIER_LABELS: Record<AiModelTier, string> = {
  [AiModelTier.Fast]: 'Fast (classification, validation)',
  [AiModelTier.Standard]: 'Standard (content generation)',
  [AiModelTier.Reasoning]: 'Reasoning (multi-step planning)',
};

const PLATFORM_DEFAULT_TIER = 'Platform default (ModelSelector decides)';

/** Starter JPS document offered when the prompt field is empty (SUM-CHAT@v1 shape). */
export const JPS_STARTER_TEMPLATE = JSON.stringify(
  {
    $schema: 'https://spaarke.com/schemas/prompt/v1',
    $version: 1,
    instruction: {
      role: 'You are ...',
      task: 'Read the input in the ## Document section and produce ...',
      constraints: ['Emit a JSON object matching the configured output schema EXACTLY.'],
    },
    input: {
      document: { required: true, maxLength: 100000, placeholder: '{{document.extractedText}}' },
    },
    output: {
      fields: [{ name: 'summary', type: 'string', description: '...' }],
      structuredOutput: true,
    },
  },
  null,
  2
);

export interface ActionEditorFormProps {
  row: ActionRow;
  /** Form-level errors from the save gate (validateActionRow). */
  errors: ValidationErrors;
  onChange: (row: ActionRow) => void;
}

export function ActionEditorForm({ row, errors, onChange }: ActionEditorFormProps): JSX.Element {
  const styles = useStyles();

  const patch = (partial: Partial<ActionRow>): void => onChange({ ...row, ...partial });

  const promptError = errors.systemPrompt ?? validateJpsPrompt(row.systemPrompt) ?? undefined;

  return (
    <div className={styles.root} data-testid="action-editor-form">
      <div className={styles.row}>
        <Field
          className={styles.grow}
          label="Name"
          required
          validationState={errors.name ? 'error' : 'none'}
          validationMessage={errors.name}
        >
          <Input
            value={row.name}
            onChange={(_ev, data) => patch({ name: data.value })}
            aria-label="Action name"
            placeholder="Summarize chat files"
          />
        </Field>
        <Field
          className={styles.grow}
          label="Action code"
          required
          hint="Stable versioned code, e.g. SUM-CHAT@v1."
          validationState={errors.actionCode ? 'error' : 'none'}
          validationMessage={errors.actionCode}
        >
          <Input
            value={row.actionCode}
            onChange={(_ev, data) => patch({ actionCode: data.value })}
            aria-label="Action code"
            placeholder="SUM-CHAT@v1"
          />
        </Field>
      </div>

      <Field label="Description">
        <Input
          value={row.description}
          onChange={(_ev, data) => patch({ description: data.value })}
          aria-label="Action description"
        />
      </Field>

      <div className={styles.row}>
        <Field className={styles.grow} label="Kind" required>
          <Dropdown
            value={KIND_LABELS[row.kind]}
            selectedOptions={[String(row.kind)]}
            onOptionSelect={(_ev, data) => {
              if (data.optionValue) patch({ kind: Number(data.optionValue) as ActionKind });
            }}
            aria-label="Action kind"
          >
            {Object.entries(KIND_LABELS).map(([value, label]) => (
              <Option key={value} value={value} text={label}>
                {label}
              </Option>
            ))}
          </Dropdown>
        </Field>

        <Field className={styles.grow} label="Default model tier" hint="Binding rows may override per-Binding.">
          <Dropdown
            value={row.modelTier === null ? PLATFORM_DEFAULT_TIER : TIER_LABELS[row.modelTier]}
            selectedOptions={[row.modelTier === null ? '' : String(row.modelTier)]}
            onOptionSelect={(_ev, data) =>
              patch({ modelTier: data.optionValue === '' ? null : (Number(data.optionValue) as AiModelTier) })
            }
            aria-label="Default model tier"
          >
            <Option value="" text={PLATFORM_DEFAULT_TIER}>
              {PLATFORM_DEFAULT_TIER}
            </Option>
            {Object.entries(TIER_LABELS).map(([value, label]) => (
              <Option key={value} value={value} text={label}>
                {label}
              </Option>
            ))}
          </Dropdown>
        </Field>
      </div>

      {row.kind === ActionKind.Coded && (
        <Field
          label="Workflow class"
          required
          hint="Registered ICodedWorkflow class reference (sprk_workflowclass)."
          validationState={errors.workflowClass ? 'error' : 'none'}
          validationMessage={errors.workflowClass}
        >
          <Input
            value={row.workflowClass}
            onChange={(_ev, data) => patch({ workflowClass: data.value })}
            aria-label="Workflow class"
            placeholder="Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingNarrator"
          />
        </Field>
      )}

      {row.kind === ActionKind.Prompted && (
        <>
          <Field
            label="Prompt (JPS JSON or flat text)"
            required
            hint="A body starting with '{' is a JPS document rendered by PromptSchemaRenderer; plain text passes through."
            validationState={promptError ? 'error' : 'none'}
            validationMessage={promptError}
          >
            <Textarea
              id="action-system-prompt"
              className={styles.prompt}
              value={row.systemPrompt}
              resize="vertical"
              rows={12}
              onChange={(_ev, data) => patch({ systemPrompt: data.value })}
              aria-label="Prompt (JPS JSON or flat text)"
            />
          </Field>
          {row.systemPrompt.trim() === '' && (
            <div>
              <Button appearance="secondary" size="small" onClick={() => patch({ systemPrompt: JPS_STARTER_TEMPLATE })}>
                Insert JPS starter template
              </Button>
            </div>
          )}
        </>
      )}

      <JsonField
        id="action-input-schema"
        label="Input schema (sprk_inputschema)"
        hint={`Typed-argument JSON Schema (OpenAI function-parameters subset). Required-ness goes ONLY in the object-level 'required' array — property-level "required": true is banned. Author-mirror-first: infra/dataverse/inputschemas/.`}
        value={row.inputSchema}
        placeholder='{"type":"object","properties":{"due_date":{"type":"string","description":"...","elicitation_prompt":"..."}},"required":["due_date"]}'
        validate={validateSchemaForAuthoring}
        externalError={errors.inputSchema}
        onChange={inputSchema => patch({ inputSchema })}
      />

      <JsonField
        id="action-output-schema"
        label="Output schema (sprk_outputschemajson)"
        hint="Structured-Outputs JSON Schema. Property declaration order = streaming emission order."
        value={row.outputSchema}
        placeholder='{"type":"object","properties":{"summary":{"type":"string"}},"required":["summary"],"additionalProperties":false}'
        validate={validateSchemaForAuthoring}
        externalError={errors.outputSchema}
        onChange={outputSchema => patch({ outputSchema })}
      />
    </div>
  );
}
