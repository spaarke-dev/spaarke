/**
 * ParameterFields - the form of one action or Next step, built from the plan's `parameters` and nothing else (the UI never
 * switches on an action, a rule or a lane). One editor per `DecisionParameterKind`.
 *
 * - Text / TextArea / Choice / Money / Date: Fluent inputs. The shown value is what the user typed, else the starting value
 *   (a templated draft, D-53), which is marked "Prefilled from this item" until it is edited.
 * - Person / Lookup: when the host provides `openLookup`, a read-only field that opens the platform's side-pane lookup (single
 *   select, scoped by the host; D-182) and shows the pick. Without it, the shared `LookupField` over the host's `lookup` search.
 *   A required Lookup that has exactly one candidate is pre-selected, without opening anything (D-18: a matter with one
 *   budget); with several the user picks.
 *
 * Fluent v9 tokens only (ADR-021). React 16/17-safe.
 */

import * as React from 'react';
import {
  Button,
  Caption1,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  Select,
  Textarea,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { SearchRegular } from '@fluentui/react-icons';
import { LookupField } from '../../LookupField/LookupField';
import type { ILookupItem } from '../../../types/LookupTypes';
import { fieldLabel, fieldValue, parameterValid, withLookup, withValue } from '../decisionDraft';
import type { StepScope } from '../decisionDraft';
import { useDecisionItem } from '../decisionItemContext';
import type { DecisionAction, DecisionParameter } from '../decisionWizardTypes';

const useStyles = makeStyles({
  form: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalM },
  prefilled: { color: tokens.colorNeutralForeground3 },
  pickInput: { cursor: 'pointer' },
});

const INVALID: Partial<Record<DecisionParameter['kind'], string>> = {
  Money: 'Enter an amount of zero or more, as digits with a period for cents, for example 1250.50.',
  Date: 'Enter a date.',
  Choice: 'Choose one of the options.',
};

/** Shown on a step while a failed attempt pins the review: the route has seen this exact decision, so it cannot change. */
export const LockedNote: React.FC = () => (
  <MessageBar intent="info" data-testid="locked-note">
    <MessageBarBody>
      An earlier attempt to record this decision did not finish, so it cannot be changed now. Record the same decision
      again, or close without recording.
    </MessageBarBody>
  </MessageBar>
);

interface FieldProps {
  parameter: DecisionParameter;
  scope: StepScope;
}

const LookupParameter: React.FC<FieldProps> = ({ parameter, scope }) => {
  const { draft, update, lookup, openLookup, item, locked } = useDecisionItem();
  const styles = useStyles();
  const id = fieldValue(draft, scope, parameter.code);
  const name = fieldLabel(draft, scope, parameter.code);
  const value: ILookupItem | null = id ? { id, name: name ?? id } : null;
  const kind = parameter.kind === 'Person' ? 'Person' : 'Lookup';

  const search = React.useCallback(
    (query: string) =>
      lookup
        ? lookup({ kind, entity: parameter.lookupEntity, parameterCode: parameter.code, query })
        : Promise.resolve([]),
    [lookup, kind, parameter.lookupEntity, parameter.code]
  );

  // D-182: the side-pane lookup. A Person is a contact; a Lookup needs the catalog's table, and without one the inline field stays.
  const paneEntity = kind === 'Person' ? 'contact' : (parameter.lookupEntity ?? null);
  const usePane = openLookup != null && paneEntity != null;
  // Single flight: a second activation while the pane is open opens nothing.
  const picking = React.useRef(false);
  const [paneOpen, setPaneOpen] = React.useState(false);
  const mounted = React.useRef(true);
  React.useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);
  const openPane = React.useCallback(async () => {
    if (!openLookup || !paneEntity || picking.current) return;
    picking.current = true;
    setPaneOpen(true);
    try {
      const picked = await openLookup({ kind, entity: paneEntity, parameterCode: parameter.code });
      // Cancelling (null) changes nothing; the pick fills the field exactly as the inline pick does.
      if (picked?.id) update(d => withLookup(d, scope, parameter.code, { id: picked.id, name: picked.name }));
    } catch {
      // A pane that failed is treated as a cancel: the field is unchanged and can be tried again.
    } finally {
      picking.current = false;
      if (mounted.current) setPaneOpen(false);
    }
  }, [openLookup, paneEntity, kind, parameter.code, update, scope]);

  // D-18: one candidate is pre-selected as a starting value; several are left for the user to pick.
  const preselected = React.useRef(false);
  const hasValue = id !== '';
  React.useEffect(() => {
    // Only a REQUIRED lookup is pre-selected: an optional one (an outside firm) is never filled in for the user.
    if (kind !== 'Lookup' || !parameter.required || !lookup || hasValue || preselected.current) return undefined;
    preselected.current = true;
    let cancelled = false;
    void search('')
      .then(found => {
        if (!cancelled && found.length === 1) update(d => withLookup(d, scope, parameter.code, found[0], true));
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [kind, lookup, hasValue, search, update, scope, parameter.code, parameter.required]);

  if (locked) {
    return (
      <div data-testid={`field-${parameter.code}`}>
        <Field label={parameter.label} required={parameter.required}>
          <Input readOnly disabled value={id ? (name ?? 'Selected record') : ''} />
        </Field>
      </div>
    );
  }

  if (usePane) {
    return (
      <div data-testid={`field-${parameter.code}`}>
        <Field label={parameter.label} required={parameter.required} hint={parameter.hint ?? undefined}>
          <Input
            readOnly
            className={styles.pickInput}
            value={value?.name ?? ''}
            placeholder={`Select ${parameter.label.toLowerCase()}`}
            aria-busy={paneOpen}
            onClick={() => void openPane()}
            onKeyDown={e => {
              if (e.key === 'Enter' || e.key === ' ' || e.key === 'ArrowDown') {
                e.preventDefault();
                void openPane();
              }
            }}
            contentAfter={
              <Button
                appearance="transparent"
                size="small"
                icon={<SearchRegular />}
                aria-label={`Select ${parameter.label.toLowerCase()}`}
                onClick={() => void openPane()}
              />
            }
          />
        </Field>
      </div>
    );
  }

  if (!lookup) {
    return (
      <MessageBar intent="warning" data-testid={`field-${parameter.code}`}>
        <MessageBarBody>
          {parameter.label} cannot be searched from here{item.core?.name ? ` for ${item.core.name}` : ''}.
          {parameter.required ? ' It is required, so this step cannot be completed.' : ''}
        </MessageBarBody>
      </MessageBar>
    );
  }

  return (
    <div data-testid={`field-${parameter.code}`}>
      <LookupField
        label={parameter.label}
        required={parameter.required}
        value={value}
        onChange={picked => update(d => withLookup(d, scope, parameter.code, picked))}
        onSearch={search}
        openOnFocus
        placeholder={`Search ${parameter.label.toLowerCase()}`}
      />
      {parameter.hint ? <Caption1 className={styles.prefilled}>{parameter.hint}</Caption1> : null}
    </div>
  );
};

const ScalarParameter: React.FC<FieldProps> = ({ parameter, scope }) => {
  const { draft, update, locked } = useDecisionItem();
  const styles = useStyles();
  const value = fieldValue(draft, scope, parameter.code);
  const typed = draft.values[scope]?.[parameter.code] !== undefined;
  const prefilled = !typed && value !== '';
  const invalid = value.trim() !== '' && !parameterValid(parameter, value);
  const set = (next: string) => update(d => withValue(d, scope, parameter.code, next));
  const hint = prefilled ? 'Prefilled from this item. Edit it as needed.' : (parameter.hint ?? undefined);

  let control: React.ReactElement;
  switch (parameter.kind) {
    case 'TextArea':
      control = (
        <Textarea rows={6} resize="vertical" disabled={locked} value={value} onChange={(_e, data) => set(data.value)} />
      );
      break;
    case 'Choice':
      control = (
        <Select value={value} disabled={locked} onChange={(_e, data) => set(data.value)}>
          <option value="">Choose...</option>
          {(parameter.options ?? []).map(o => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </Select>
      );
      break;
    case 'Date':
      control = <Input type="date" disabled={locked} value={value} onChange={(_e, data) => set(data.value)} />;
      break;
    case 'Money':
      control = <Input inputMode="decimal" disabled={locked} value={value} onChange={(_e, data) => set(data.value)} />;
      break;
    default:
      control = <Input disabled={locked} value={value} onChange={(_e, data) => set(data.value)} />;
  }

  return (
    <Field
      label={parameter.label}
      required={parameter.required}
      hint={hint ? <span className={prefilled ? styles.prefilled : undefined}>{hint}</span> : undefined}
      validationState={invalid ? 'error' : 'none'}
      validationMessage={invalid ? (INVALID[parameter.kind] ?? 'Check this value.') : undefined}
      data-testid={`field-${parameter.code}`}
    >
      {control}
    </Field>
  );
};

/** The editable form of an action or Next step. */
export const ParameterFields: React.FC<{ action: DecisionAction; scope: StepScope }> = ({ action, scope }) => {
  const styles = useStyles();
  if (action.parameters.length === 0) return null;
  return (
    <div className={styles.form} data-testid="parameter-fields">
      {action.parameters.map(p =>
        p.kind === 'Person' || p.kind === 'Lookup' ? (
          <LookupParameter key={p.code} parameter={p} scope={scope} />
        ) : (
          <ScalarParameter key={p.code} parameter={p} scope={scope} />
        )
      )}
    </div>
  );
};
