/**
 * DeliverOutputForm - Configuration form for Deliver Output nodes.
 *
 * Allows users to configure how a playbook delivers its output:
 * - Delivery type (markdown, html, text, json)
 * - Handlebars template with variable support ({{nodeName.output.fieldName}})
 * - Include/exclude metadata and source citations
 * - Max output length
 *
 * Updates typed PlaybookNodeData fields directly so buildConfigJson()
 * in playbookNodeSync produces the correct sprk_configjson for the
 * server-side DeliverOutputNodeExecutor.
 *
 * Server config shape (DeliveryNodeConfig):
 *   { deliveryType, template, outputFormat: { includeMetadata, includeSourceCitations, maxLength } }
 *
 * @see ADR-021 - Fluent UI v9 design system (dark mode required)
 */

import { useCallback, memo } from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Label,
  Input,
  Textarea,
  Dropdown,
  Option,
  Switch,
  SpinButton,
  shorthands,
} from '@fluentui/react-components';
import type {
  InputOnChangeData,
  OptionOnSelectData,
  SelectionEvents,
  SwitchOnChangeData,
  SpinButtonOnChangeData,
  SpinButtonChangeEvent,
} from '@fluentui/react-components';
import type { PlaybookNodeData } from '../../types/playbook';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface DeliverOutputFormProps {
  nodeId: string;
  data: PlaybookNodeData;
  onUpdate: (field: string, value: unknown) => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  field: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  fieldHint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
  },
  templateArea: {
    minHeight: '120px',
  },
  switchRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    ...shorthands.padding(tokens.spacingVerticalXS, '0'),
  },
});

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Delivery types matching server-side DeliveryNodeConfig.DeliveryType */
const DELIVERY_TYPES = [
  { value: 'markdown', label: 'Markdown' },
  { value: 'html', label: 'HTML' },
  { value: 'text', label: 'Plain Text' },
  { value: 'json', label: 'JSON' },
] as const;

type DeliveryType = (typeof DELIVERY_TYPES)[number]['value'];

/**
 * R6 Pillar 5 / DEF-003 — Destination wire values matching the C# NodeDestination
 * enum (Models/Ai/NodeRoutingConfig.cs NodeDestinationJsonConverter). Five wire
 * values: chat (default, NodeRoutingConfig.Parse(null) → Chat), workspace (requires
 * widgetType), both (chat-routing-redesign-r1 — render in chat AND open a workspace
 * tab), form-prefill (used by pre-fill flows; NFR-07 surface), side-effect
 * (fire-and-forget side-effect playbooks; no widget render).
 */
const DESTINATIONS = [
  { value: '', label: 'Chat (default)' },
  { value: 'chat', label: 'Chat (explicit)' },
  { value: 'workspace', label: 'Workspace' },
  { value: 'both', label: 'Both — chat + workspace' },
  { value: 'form-prefill', label: 'Form pre-fill' },
  { value: 'side-effect', label: 'Side-effect (no render)' },
] as const;

type Destination = '' | 'chat' | 'workspace' | 'both' | 'form-prefill' | 'side-effect';

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const DeliverOutputForm = memo(function DeliverOutputForm({ nodeId, data, onUpdate }: DeliverOutputFormProps) {
  const styles = useStyles();

  // Read from typed PlaybookNodeData fields
  const deliveryType = (data.deliveryType as DeliveryType) || 'markdown';
  const template = data.template ?? '';
  const includeMetadata = data.includeMetadata ?? false;
  const includeSourceCitations = data.includeSourceCitations ?? false;
  const maxOutputLength = data.maxOutputLength ?? 0;
  const destination: Destination = (data.destination as Destination) ?? '';
  const widgetType = data.widgetType ?? '';

  // Find display labels for current values
  const deliveryTypeLabel = DELIVERY_TYPES.find(dt => dt.value === deliveryType)?.label ?? 'Markdown';
  const destinationLabel = DESTINATIONS.find(d => d.value === destination)?.label ?? 'Chat (default)';

  // Conditional gate for widgetType — surfaced for workspace + both; hidden for
  // chat / form-prefill / side-effect to match the server-side
  // NodeRoutingConfig.Validate rule (widgetType ignored when destination !=
  // Workspace, though "both" carries a workspace render too).
  const widgetTypeRelevant = destination === 'workspace' || destination === 'both';
  const widgetTypeError = widgetTypeRelevant && widgetType.trim().length === 0;

  // -- Handlers --

  const handleDeliveryTypeChange = useCallback(
    (_event: SelectionEvents, item: OptionOnSelectData) => {
      if (item.optionValue) {
        onUpdate('deliveryType', item.optionValue);
      }
    },
    [onUpdate]
  );

  const handleTemplateChange = useCallback(
    (e: React.ChangeEvent<HTMLTextAreaElement>) => {
      onUpdate('template', e.target.value);
    },
    [onUpdate]
  );

  const handleMetadataToggle = useCallback(
    (_e: React.ChangeEvent<HTMLInputElement>, item: SwitchOnChangeData) => {
      onUpdate('includeMetadata', item.checked);
    },
    [onUpdate]
  );

  const handleCitationsToggle = useCallback(
    (_e: React.ChangeEvent<HTMLInputElement>, item: SwitchOnChangeData) => {
      onUpdate('includeSourceCitations', item.checked);
    },
    [onUpdate]
  );

  const handleMaxLengthChange = useCallback(
    (_e: SpinButtonChangeEvent, item: SpinButtonOnChangeData) => {
      onUpdate('maxOutputLength', item.value ?? 0);
    },
    [onUpdate]
  );

  const handleDestinationChange = useCallback(
    (_event: SelectionEvents, item: OptionOnSelectData) => {
      // Empty string sentinel = "Chat (default)" — persists as undefined so
      // NodeRoutingConfig.Parse defaults to Chat (FR-27 backward compatibility).
      const next = item.optionValue ?? '';
      onUpdate('destination', next.length === 0 ? undefined : next);
      // When switching away from workspace/both, clear widgetType so it isn't
      // resurfaced if the user re-enters workspace.
      if (next !== 'workspace' && next !== 'both' && widgetType.length > 0) {
        onUpdate('widgetType', undefined);
      }
    },
    [onUpdate, widgetType]
  );

  const handleWidgetTypeChange = useCallback(
    (_e: React.ChangeEvent<HTMLInputElement>, item: InputOnChangeData) => {
      const next = item.value ?? '';
      onUpdate('widgetType', next.length === 0 ? undefined : next);
    },
    [onUpdate]
  );

  // -- Render --

  return (
    <div className={styles.form}>
      {/* Delivery Type */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-deliveryType`} size="small" required>
          Delivery Format
        </Label>
        <Dropdown
          id={`${nodeId}-deliveryType`}
          size="small"
          value={deliveryTypeLabel}
          selectedOptions={[deliveryType]}
          onOptionSelect={handleDeliveryTypeChange}
        >
          {DELIVERY_TYPES.map(dt => (
            <Option key={dt.value} value={dt.value}>
              {dt.label}
            </Option>
          ))}
        </Dropdown>
        <Text className={styles.fieldHint}>Format used when rendering the output content</Text>
      </div>

      {/* Handlebars Template */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-template`} size="small">
          Output Template
        </Label>
        <Textarea
          id={`${nodeId}-template`}
          size="small"
          className={styles.templateArea}
          value={template}
          onChange={handleTemplateChange}
          placeholder={
            'Handlebars template with node output variables:\n\n' +
            '## Summary\n' +
            '{{summarize.text}}\n\n' +
            '## Details\n' +
            '{{extract_entities.text}}\n\n' +
            'Leave empty for auto-assembly of all previous outputs.'
          }
          resize="vertical"
        />
        <Text className={styles.fieldHint}>
          {'Use {{outputVariable.text}} or {{outputVariable.output.field}} syntax. ' +
            'Leave empty to auto-assemble all previous node outputs.'}
        </Text>
      </div>

      {/* Include Metadata */}
      <div className={styles.switchRow}>
        <Label htmlFor={`${nodeId}-includeMetadata`} size="small">
          Include Metadata
        </Label>
        <Switch id={`${nodeId}-includeMetadata`} checked={includeMetadata} onChange={handleMetadataToggle} />
      </div>
      <Text className={styles.fieldHint}>Append execution metadata (timestamps, run ID, confidence) to the output</Text>

      {/* Include Source Citations */}
      <div className={styles.switchRow}>
        <Label htmlFor={`${nodeId}-includeCitations`} size="small">
          Include Source Citations
        </Label>
        <Switch id={`${nodeId}-includeCitations`} checked={includeSourceCitations} onChange={handleCitationsToggle} />
      </div>
      <Text className={styles.fieldHint}>Append source citation references to the output</Text>

      {/* Max Output Length */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-maxLength`} size="small">
          Max Output Length
        </Label>
        <SpinButton
          id={`${nodeId}-maxLength`}
          size="small"
          min={0}
          max={100000}
          step={1000}
          value={maxOutputLength}
          onChange={handleMaxLengthChange}
        />
        <Text className={styles.fieldHint}>
          Maximum characters in output (0 = unlimited). Content beyond this limit is truncated.
        </Text>
      </div>

      {/* R6 Pillar 5 / DEF-003 — Routing destination + widget type */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-destination`} size="small">
          Routing Destination
        </Label>
        <Dropdown
          id={`${nodeId}-destination`}
          size="small"
          value={destinationLabel}
          selectedOptions={[destination]}
          onOptionSelect={handleDestinationChange}
        >
          {DESTINATIONS.map(d => (
            <Option key={d.value || 'default'} value={d.value}>
              {d.label}
            </Option>
          ))}
        </Dropdown>
        <Text className={styles.fieldHint}>
          Where the playbook output renders. Chat is the default; Workspace opens a tab; Both renders inline AND opens a
          tab; Form pre-fill targets the existing pre-fill flows; Side-effect runs the playbook with no UI render.
        </Text>
      </div>

      {widgetTypeRelevant ? (
        <div className={styles.field}>
          <Label htmlFor={`${nodeId}-widgetType`} size="small" required>
            Widget Type
          </Label>
          <Input
            id={`${nodeId}-widgetType`}
            size="small"
            value={widgetType}
            onChange={handleWidgetTypeChange}
            placeholder="e.g. structured-output-stream, Summary, DocumentViewer"
          />
          <Text
            className={styles.fieldHint}
            style={widgetTypeError ? { color: tokens.colorPaletteRedForeground1 } : undefined}
          >
            {widgetTypeError
              ? 'Widget type is required when destination is Workspace or Both.'
              : 'Workspace widget registry key. Must match a registered widget type ' +
                '(WorkspaceWidgetRegistry on the SpaarkeAi client). Unknown values fall back to default.'}
          </Text>
        </div>
      ) : null}
    </div>
  );
});
