/**
 * OnEventBindingsEditor — structured authoring for `sprk_oneventbindings`
 * (Event path memberships, canonical §7.1; shape pinned by
 * PublicContracts/Binding.cs OnEventBinding: `[{"event","order"}]`).
 *
 * The event vocabulary is CLOSED server-side (IEventRulesService constants);
 * the editor suggests known tokens and warns — without blocking — on unknown
 * ones, since the server vocabulary can grow ahead of this list.
 *
 * ADR-021: Fluent v9 tokens only.
 */

import { useMemo } from 'react';
import {
  Button,
  Field,
  Input,
  Label,
  MessageBar,
  MessageBarBody,
  SpinButton,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Add16Regular, Delete16Regular } from '@fluentui/react-icons';
import { KNOWN_EVENT_TOKENS } from '../../types/catalog';
import type { OnEventBinding } from '../../types/catalog';
import { validateOnEventBindingsJson } from '../../services/schemaValidation';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  row: {
    display: 'flex',
    alignItems: 'flex-end',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  eventField: {
    flexGrow: 1,
    minWidth: '220px',
  },
  orderField: {
    width: '120px',
  },
  hint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
});

/**
 * STRUCTURAL parse only (JSON array of objects) — an in-progress draft (blank
 * event token) must stay editable; per-field errors and the form save gate
 * enforce the pinned shape. Null only when structured editing is impossible.
 */
function parseEntries(raw: string): OnEventBinding[] | null {
  if (raw.trim() === '') return [];
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return null;
  }
  if (!Array.isArray(parsed)) return null;
  if (!parsed.every(entry => typeof entry === 'object' && entry !== null && !Array.isArray(entry))) return null;

  return (parsed as Partial<OnEventBinding>[]).map((entry, index) => ({
    event: typeof entry.event === 'string' ? entry.event : '',
    order: typeof entry.order === 'number' && Number.isInteger(entry.order) ? entry.order : index + 1,
  }));
}

function serializeEntries(entries: OnEventBinding[]): string {
  return entries.length === 0 ? '' : JSON.stringify(entries);
}

export interface OnEventBindingsEditorProps {
  /** Raw `sprk_oneventbindings` JSON (empty = not on any event). */
  value: string;
  onChange: (rawJson: string) => void;
}

export function OnEventBindingsEditor({ value, onChange }: OnEventBindingsEditorProps): JSX.Element {
  const styles = useStyles();
  const entries = useMemo(() => parseEntries(value), [value]);

  if (entries === null) {
    return (
      <MessageBar intent="error">
        <MessageBarBody>
          Existing on-event bindings JSON is invalid:{' '}
          {validateOnEventBindingsJson(value) ?? 'must be a JSON array of event objects'} — fix the JSON before
          structured editing is available.
        </MessageBarBody>
      </MessageBar>
    );
  }

  const update = (index: number, patch: Partial<OnEventBinding>): void => {
    onChange(serializeEntries(entries.map((e, i) => (i === index ? { ...e, ...patch } : e))));
  };

  const remove = (index: number): void => {
    onChange(serializeEntries(entries.filter((_e, i) => i !== index)));
  };

  const add = (): void => {
    onChange(serializeEntries([...entries, { event: KNOWN_EVENT_TOKENS[0], order: entries.length + 1 }]));
  };

  const knownTokens = KNOWN_EVENT_TOKENS as readonly string[];

  return (
    <div className={styles.root} data-testid="on-event-bindings-editor">
      <Label weight="semibold">Event memberships (on-event bindings)</Label>
      <Text className={styles.hint}>
        Runs this capability automatically on a platform event (Event path). Order: lower runs first within the
        event&apos;s composite.
      </Text>

      {entries.map((entry, index) => {
        const unknownToken = entry.event.trim() !== '' && !knownTokens.includes(entry.event);
        return (
          <div key={index} className={styles.row} data-testid={`on-event-binding-${index}`}>
            <Field
              className={styles.eventField}
              label="Event token"
              validationState={unknownToken ? 'warning' : 'none'}
              validationMessage={
                unknownToken
                  ? `'${entry.event}' is not a known platform event token — unknown tokens never fire. Known: ${knownTokens.join(', ')}.`
                  : undefined
              }
            >
              <Input
                value={entry.event}
                onChange={(_ev, data) => update(index, { event: data.value })}
                aria-label={`Event ${index + 1} token`}
                list={`known-event-tokens-${index}`}
              />
            </Field>
            <datalist id={`known-event-tokens-${index}`}>
              {knownTokens.map(token => (
                <option key={token} value={token} />
              ))}
            </datalist>

            <Field className={styles.orderField} label="Order">
              <SpinButton
                value={entry.order}
                min={1}
                onChange={(_ev, data) => {
                  const next = data.value ?? (data.displayValue ? parseInt(data.displayValue, 10) : NaN);
                  if (Number.isInteger(next)) update(index, { order: next as number });
                }}
                aria-label={`Event ${index + 1} order`}
              />
            </Field>

            <Button
              appearance="subtle"
              size="small"
              icon={<Delete16Regular />}
              aria-label={`Remove event membership ${index + 1}`}
              onClick={() => remove(index)}
            />
          </div>
        );
      })}

      <div>
        <Button appearance="secondary" size="small" icon={<Add16Regular />} onClick={add}>
          Add event membership
        </Button>
      </div>
    </div>
  );
}
