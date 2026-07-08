/**
 * ChipTransitionsEditor — structured authoring for `sprk_chiptransitions`
 * (D4 / Click path; shape pinned by PublicContracts/Binding.cs ChipTransition).
 *
 * A BA authors chips as rows (target Binding + labels + attachment
 * precondition + prefill args) instead of raw JSON; the component owns the
 * parse/serialize round-trip and never emits a shape the routing parser
 * degrades. Malformed EXISTING JSON is surfaced (not silently dropped) so a
 * bad legacy row is visible at authoring time.
 *
 * ADR-021: Fluent v9 tokens only.
 */

import { useEffect, useRef, useState } from 'react';
import {
  Button,
  Checkbox,
  Field,
  Input,
  Label,
  MessageBar,
  MessageBarBody,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Add16Regular, Delete16Regular } from '@fluentui/react-icons';
import type { BindingRow, ChipTransition } from '../../types/catalog';
import { validateChipTransitionsJson } from '../../services/schemaValidation';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  chipCard: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    padding: tokens.spacingVerticalS,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  chipHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  row: {
    display: 'flex',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  grow: {
    flexGrow: 1,
    minWidth: '200px',
  },
  prefill: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
  },
  hint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
});

/** Editor row: ChipTransition with prefill kept as raw JSON text while editing. */
interface ChipDraft {
  target_binding_id: string;
  chip_label: string;
  bulk_chip_label: string;
  requires_attachments: boolean;
  prefillSlotsJson: string;
}

/**
 * STRUCTURAL parse only (JSON array of objects). Deliberately more lenient
 * than `validateChipTransitionsJson`: an in-progress draft (blank target /
 * label) must stay editable — per-field errors below and the form save gate
 * enforce required-ness. Returns null only when structured editing is
 * impossible (malformed JSON / wrong shape).
 */
function parseChips(raw: string): ChipDraft[] | null {
  if (raw.trim() === '') return [];
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return null;
  }
  if (!Array.isArray(parsed)) return null;
  if (!parsed.every(entry => typeof entry === 'object' && entry !== null && !Array.isArray(entry))) return null;

  return (parsed as ChipTransition[]).map(chip => ({
    target_binding_id: typeof chip.target_binding_id === 'string' ? chip.target_binding_id : '',
    chip_label: typeof chip.chip_label === 'string' ? chip.chip_label : '',
    bulk_chip_label: typeof chip.bulk_chip_label === 'string' ? chip.bulk_chip_label : '',
    requires_attachments: chip.requires_attachments === true,
    prefillSlotsJson: chip.prefill_slots ? JSON.stringify(chip.prefill_slots, null, 2) : '',
  }));
}

function serializeChips(drafts: ChipDraft[]): string {
  if (drafts.length === 0) return '';
  const chips = drafts.map(draft => {
    const chip: ChipTransition = {
      target_binding_id: draft.target_binding_id,
      chip_label: draft.chip_label,
    };
    if (draft.bulk_chip_label.trim() !== '') chip.bulk_chip_label = draft.bulk_chip_label;
    if (draft.requires_attachments) chip.requires_attachments = true;
    if (draft.prefillSlotsJson.trim() !== '') {
      try {
        chip.prefill_slots = JSON.parse(draft.prefillSlotsJson) as Record<string, unknown>;
      } catch {
        // Leave prefill_slots out; the per-field error below tells the BA.
      }
    }
    return chip;
  });
  return JSON.stringify(chips, null, 2);
}

function prefillError(raw: string): string | undefined {
  if (raw.trim() === '') return undefined;
  try {
    const parsed: unknown = JSON.parse(raw);
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
      return 'Prefill slots must be a JSON object of capability args.';
    }
    return undefined;
  } catch {
    return 'Prefill slots must be valid JSON.';
  }
}

export interface ChipTransitionsEditorProps {
  /** Raw `sprk_chiptransitions` JSON (empty = no chips). */
  value: string;
  /** Existing Bindings offered as chip targets. */
  bindings: BindingRow[];
  onChange: (rawJson: string) => void;
}

export function ChipTransitionsEditor({ value, bindings, onChange }: ChipTransitionsEditorProps): JSX.Element {
  const styles = useStyles();

  // Drafts live in STATE, not derived per render: `serializeChips` omits a
  // prefill draft that isn't valid JSON yet, so deriving drafts from `value`
  // would reset the prefill input on every keystroke (code-review Critical #1,
  // 2026-07-07). State keeps the in-progress text; we re-derive only when the
  // parent changes `value` externally (row switch / load).
  const [drafts, setDrafts] = useState<ChipDraft[] | null>(() => parseChips(value));
  const lastEmittedRef = useRef(value);
  useEffect(() => {
    if (value !== lastEmittedRef.current) {
      lastEmittedRef.current = value;
      setDrafts(parseChips(value));
    }
  }, [value]);

  const commit = (next: ChipDraft[]): void => {
    setDrafts(next);
    const json = serializeChips(next);
    lastEmittedRef.current = json;
    onChange(json);
  };

  if (drafts === null) {
    return (
      <MessageBar intent="error">
        <MessageBarBody>
          Existing chip transitions JSON is invalid:{' '}
          {validateChipTransitionsJson(value) ?? 'must be a JSON array of chip objects'} — fix the JSON before
          structured editing is available.
        </MessageBarBody>
      </MessageBar>
    );
  }

  const update = (index: number, patch: Partial<ChipDraft>): void => {
    commit(drafts.map((d, i) => (i === index ? { ...d, ...patch } : d)));
  };

  const remove = (index: number): void => {
    commit(drafts.filter((_d, i) => i !== index));
  };

  const add = (): void => {
    commit([
      ...drafts,
      {
        target_binding_id: '',
        chip_label: '',
        bulk_chip_label: '',
        requires_attachments: false,
        prefillSlotsJson: '',
      },
    ]);
  };

  return (
    <div className={styles.root} data-testid="chip-transitions-editor">
      <Label weight="semibold">Next-step chips (chip transitions)</Label>
      <Text className={styles.hint}>
        Curated follow-up capabilities offered after this Binding&apos;s output renders (Click path). Each chip targets
        another Binding by id.
      </Text>

      {drafts.map((draft, index) => (
        <div key={index} className={styles.chipCard} data-testid={`chip-transition-${index}`}>
          <div className={styles.chipHeader}>
            <Text weight="semibold">Chip {index + 1}</Text>
            <Button
              appearance="subtle"
              size="small"
              icon={<Delete16Regular />}
              aria-label={`Remove chip ${index + 1}`}
              onClick={() => remove(index)}
            />
          </div>

          <div className={styles.row}>
            <Field
              className={styles.grow}
              label="Target Binding id"
              hint={bindings.length > 0 ? 'Paste or pick a Binding row id from the Bindings tab.' : undefined}
              validationState={draft.target_binding_id.trim() === '' ? 'error' : 'none'}
              validationMessage={
                draft.target_binding_id.trim() === '' ? 'Required — the Click path resolves it.' : undefined
              }
            >
              <Input
                value={draft.target_binding_id}
                onChange={(_ev, data) => update(index, { target_binding_id: data.value })}
                aria-label={`Chip ${index + 1} target binding id`}
                list={`chip-binding-targets-${index}`}
              />
            </Field>
            <datalist id={`chip-binding-targets-${index}`}>
              {bindings
                .filter(b => b.id)
                .map(b => (
                  <option key={b.id} value={b.id}>
                    {b.consumerType}
                  </option>
                ))}
            </datalist>
          </div>

          <div className={styles.row}>
            <Field
              className={styles.grow}
              label="Chip label"
              validationState={draft.chip_label.trim() === '' ? 'error' : 'none'}
              validationMessage={draft.chip_label.trim() === '' ? 'Required — rendered to the user.' : undefined}
            >
              <Input
                value={draft.chip_label}
                onChange={(_ev, data) => update(index, { chip_label: data.value })}
                aria-label={`Chip ${index + 1} label`}
                placeholder="Summarize this document"
              />
            </Field>
            <Field
              className={styles.grow}
              label="Bulk chip label (optional short verb)"
              hint='Used in composite labels like "Summarize all N files?"'
            >
              <Input
                value={draft.bulk_chip_label}
                onChange={(_ev, data) => update(index, { bulk_chip_label: data.value })}
                aria-label={`Chip ${index + 1} bulk label`}
                placeholder="Summarize"
              />
            </Field>
          </div>

          <Checkbox
            checked={draft.requires_attachments}
            onChange={(_ev, data) => update(index, { requires_attachments: data.checked === true })}
            label="Requires session attachments (chip disabled at zero attachments)"
          />

          <Field
            label="Prefill slots (optional JSON object)"
            hint="Pre-filled capability args forwarded verbatim as the chip's args."
            validationState={prefillError(draft.prefillSlotsJson) ? 'error' : 'none'}
            validationMessage={prefillError(draft.prefillSlotsJson)}
          >
            <Input
              className={styles.prefill}
              value={draft.prefillSlotsJson}
              onChange={(_ev, data) => update(index, { prefillSlotsJson: data.value })}
              aria-label={`Chip ${index + 1} prefill slots JSON`}
              placeholder='{"styleHint": "executive"}'
            />
          </Field>
        </div>
      ))}

      <div>
        <Button appearance="secondary" size="small" icon={<Add16Regular />} onClick={add}>
          Add chip
        </Button>
      </div>
    </div>
  );
}
