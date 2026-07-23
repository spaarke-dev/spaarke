/**
 * BindingEditorForm — BA authoring form for ONE `sprk_playbookconsumer` row
 * (the invocation unit, canonical §6.2; FR-P4-04).
 *
 * Covers the FULL Binding contract (PublicContracts/Binding.cs): identity
 * (consumer type / code / environment / priority / enabled), target Action,
 * ucid, tool description, disposition, risk, capture mode, surfaces, model
 * tier override, chip transitions, on-event bindings, match conditions.
 *
 * ADR-021: Fluent v9 tokens only; verified in light + dark themes.
 */

import {
  Checkbox,
  Dropdown,
  Field,
  Input,
  Label,
  Option,
  SpinButton,
  Switch,
  Text,
  Textarea,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { AiModelTier, BindingCaptureMode, BindingDisposition, BindingRisk, SURFACE_TOKENS } from '../../types/catalog';
import type { ActionRow, BindingRow } from '../../types/catalog';
import type { ValidationErrors } from '../../services/catalogService';
import { validateMatchConditionsJson } from '../../services/schemaValidation';
import { ChipTransitionsEditor } from './ChipTransitionsEditor';
import { OnEventBindingsEditor } from './OnEventBindingsEditor';
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
    alignItems: 'flex-end',
  },
  grow: {
    flexGrow: 1,
    minWidth: '200px',
  },
  priority: {
    width: '140px',
  },
  surfaces: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  surfaceGrid: {
    display: 'flex',
    flexWrap: 'wrap',
    columnGap: tokens.spacingHorizontalL,
  },
  hint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  toolDescription: {
    width: '100%',
  },
});

const DISPOSITION_LABELS: Record<BindingDisposition, string> = {
  [BindingDisposition.Informational]: 'Informational (Assistant pane, default)',
  [BindingDisposition.WorkProduct]: 'Work product (persisted envelope)',
  [BindingDisposition.Overlay]: 'Overlay (widget annotations)',
  [BindingDisposition.Email]: 'Email (Communication service, DRAFT-only)',
  [BindingDisposition.Record]: 'Record (Dataverse write)',
  [BindingDisposition.Notification]: 'Notification',
  [BindingDisposition.Compose]: 'Compose',
};

const RISK_LABELS: Record<BindingRisk, string> = {
  [BindingRisk.None]: 'None (tool-level side-effect gating still applies)',
  [BindingRisk.ConfirmWhenUncertain]: 'Confirm when uncertain',
  [BindingRisk.AlwaysConfirm]: 'Always confirm',
};

const CAPTURE_LABELS: Record<BindingCaptureMode, string> = {
  [BindingCaptureMode.LoopElicitation]: 'Loop elicitation (clarifying turns, default)',
  [BindingCaptureMode.Modal]: 'Modal (escape-hatch dialog)',
};

const TIER_LABELS: Record<AiModelTier, string> = {
  [AiModelTier.Fast]: 'Fast',
  [AiModelTier.Standard]: 'Standard',
  [AiModelTier.Reasoning]: 'Reasoning',
};

const USE_ACTION_DEFAULT = "Use the Action's default tier";

function enumDropdown<T extends number>(
  label: string,
  ariaLabel: string,
  value: T,
  labels: Record<T, string>,
  onSelect: (value: T) => void,
  className?: string
): JSX.Element {
  return (
    <Field className={className} label={label}>
      <Dropdown
        value={labels[value]}
        selectedOptions={[String(value)]}
        onOptionSelect={(_ev, data) => {
          if (data.optionValue) onSelect(Number(data.optionValue) as T);
        }}
        aria-label={ariaLabel}
      >
        {(Object.entries(labels) as [string, string][]).map(([optionValue, optionLabel]) => (
          <Option key={optionValue} value={optionValue} text={optionLabel}>
            {optionLabel}
          </Option>
        ))}
      </Dropdown>
    </Field>
  );
}

export interface BindingEditorFormProps {
  row: BindingRow;
  /** Existing Actions offered as the Binding target. */
  actions: ActionRow[];
  /** Existing Bindings offered as chip-transition targets. */
  bindings: BindingRow[];
  /** Form-level errors from the save gate (validateBindingRow). */
  errors: ValidationErrors;
  onChange: (row: BindingRow) => void;
}

export function BindingEditorForm({ row, actions, bindings, errors, onChange }: BindingEditorFormProps): JSX.Element {
  const styles = useStyles();

  const patch = (partial: Partial<BindingRow>): void => onChange({ ...row, ...partial });

  const selectedAction = actions.find(a => a.id === row.actionId);

  const toggleSurface = (token: string, checked: boolean): void => {
    const next = checked ? [...row.surfaces, token] : row.surfaces.filter(s => s !== token);
    patch({ surfaces: next });
  };

  return (
    <div className={styles.root} data-testid="binding-editor-form">
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
            aria-label="Binding name"
            placeholder="Chat summarize (default)"
          />
        </Field>
        <Field
          className={styles.grow}
          label="Consumer type"
          required
          hint="Stable consumer-type code, e.g. chat-summarize."
          validationState={errors.consumerType ? 'error' : 'none'}
          validationMessage={errors.consumerType}
        >
          <Input
            value={row.consumerType}
            onChange={(_ev, data) => patch({ consumerType: data.value })}
            aria-label="Consumer type"
            placeholder="chat-summarize"
          />
        </Field>
      </div>

      <div className={styles.row}>
        <Field className={styles.grow} label="Consumer code" hint="Sub-discriminator; empty = default.">
          <Input
            value={row.consumerCode}
            onChange={(_ev, data) => patch({ consumerCode: data.value })}
            aria-label="Consumer code"
          />
        </Field>
        <Field className={styles.grow} label="Environment" hint="Empty or * = all environments.">
          <Input
            value={row.environment}
            onChange={(_ev, data) => patch({ environment: data.value })}
            aria-label="Environment"
          />
        </Field>
        <Field
          className={styles.priority}
          label="Priority"
          hint="Lower wins."
          validationState={errors.priority ? 'error' : 'none'}
          validationMessage={errors.priority}
        >
          <SpinButton
            value={row.priority}
            onChange={(_ev, data) => {
              const next = data.value ?? (data.displayValue ? parseInt(data.displayValue, 10) : NaN);
              if (Number.isInteger(next)) patch({ priority: next as number });
            }}
            aria-label="Priority"
          />
        </Field>
        <Switch
          checked={row.enabled}
          onChange={(_ev, data) => patch({ enabled: data.checked })}
          label={row.enabled ? 'Enabled' : 'Disabled'}
        />
      </div>

      <div className={styles.row}>
        <Field
          className={styles.grow}
          label="Target Action"
          required
          hint="The execution unit this Binding routes to."
          validationState={errors.actionId ? 'error' : 'none'}
          validationMessage={errors.actionId}
        >
          <Dropdown
            value={selectedAction ? `${selectedAction.name} (${selectedAction.actionCode})` : ''}
            selectedOptions={row.actionId ? [row.actionId] : []}
            onOptionSelect={(_ev, data) => patch({ actionId: data.optionValue ?? null })}
            aria-label="Target Action"
            placeholder="Select an Action"
          >
            {actions
              .filter(a => a.id)
              .map(a => (
                <Option key={a.id} value={a.id as string} text={`${a.name} (${a.actionCode})`}>
                  {a.name} ({a.actionCode})
                </Option>
              ))}
          </Dropdown>
        </Field>
        <Field className={styles.grow} label="Use-case id (UCID)" hint="§3 vocabulary, e.g. UC-A-1.">
          <Input
            value={row.ucid}
            onChange={(_ev, data) => patch({ ucid: data.value })}
            aria-label="Use-case id"
            placeholder="UC-A-1"
          />
        </Field>
      </div>

      <Field
        label="Tool description"
        required
        hint="The intent surface the agent loop sees when this Binding projects as a capability tool — describe WHEN to use it, in plain language."
        validationState={errors.toolDescription ? 'error' : 'none'}
        validationMessage={errors.toolDescription}
      >
        <Textarea
          className={styles.toolDescription}
          value={row.toolDescription}
          resize="vertical"
          rows={3}
          onChange={(_ev, data) => patch({ toolDescription: data.value })}
          aria-label="Tool description"
          placeholder="Summarize the files uploaded to this chat session into TL;DR bullets and a narrative summary."
        />
      </Field>

      <div className={styles.row}>
        {enumDropdown(
          'Disposition',
          'Disposition',
          row.disposition,
          DISPOSITION_LABELS,
          disposition => patch({ disposition }),
          styles.grow
        )}
        {enumDropdown('Risk', 'Risk', row.risk, RISK_LABELS, risk => patch({ risk }), styles.grow)}
      </div>

      <div className={styles.row}>
        {enumDropdown(
          'Capture mode',
          'Capture mode',
          row.captureMode,
          CAPTURE_LABELS,
          captureMode => patch({ captureMode }),
          styles.grow
        )}
        <Field className={styles.grow} label="Model tier override" hint="Overrides the Action's default tier.">
          <Dropdown
            value={row.modelTierOverride === null ? USE_ACTION_DEFAULT : TIER_LABELS[row.modelTierOverride]}
            selectedOptions={[row.modelTierOverride === null ? '' : String(row.modelTierOverride)]}
            onOptionSelect={(_ev, data) =>
              patch({
                modelTierOverride: data.optionValue === '' ? null : (Number(data.optionValue) as AiModelTier),
              })
            }
            aria-label="Model tier override"
          >
            <Option value="" text={USE_ACTION_DEFAULT}>
              {USE_ACTION_DEFAULT}
            </Option>
            {Object.entries(TIER_LABELS).map(([value, label]) => (
              <Option key={value} value={value} text={label}>
                {label}
              </Option>
            ))}
          </Dropdown>
        </Field>
      </div>

      <div className={styles.surfaces}>
        <Label weight="semibold">Surfaces</Label>
        <Text className={styles.hint}>Where this capability is offered. None checked = ALL surfaces.</Text>
        <div className={styles.surfaceGrid} role="group" aria-label="Surfaces">
          {SURFACE_TOKENS.map(token => (
            <Checkbox
              key={token}
              label={token}
              checked={row.surfaces.includes(token)}
              onChange={(_ev, data) => toggleSurface(token, data.checked === true)}
            />
          ))}
        </div>
      </div>

      <ChipTransitionsEditor
        value={row.chipTransitionsJson}
        bindings={bindings.filter(b => b.id !== row.id)}
        onChange={chipTransitionsJson => patch({ chipTransitionsJson })}
      />

      <OnEventBindingsEditor
        value={row.onEventBindingsJson}
        onChange={onEventBindingsJson => patch({ onEventBindingsJson })}
      />

      <JsonField
        id="binding-match-conditions"
        label="Match conditions (optional)"
        hint="Flat JSON predicate (key → string | string[]). Empty always matches."
        value={row.matchConditionsJson}
        placeholder='{"entityType": "sprk_matter"}'
        rows={4}
        validate={validateMatchConditionsJson}
        externalError={errors.matchConditionsJson}
        onChange={matchConditionsJson => patch({ matchConditionsJson })}
      />
    </div>
  );
}
