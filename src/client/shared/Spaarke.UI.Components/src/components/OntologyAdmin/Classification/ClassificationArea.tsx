/**
 * ClassificationArea - the Classification area of the Ontology admin tab (task 103; D-22, FR-59, D-49).
 *
 * The triage categories the classifier chooses from, one row each: guidance, On/Off, the rules that read it, its volume over the last
 * 30 days, and its measured recall against the floor (80 percent on at least 50 labelled items). An administrator can add a category
 * (saved Off), edit its name and guidance, and turn it On or Off. A category edit is live on the next enrichment, so every write
 * goes through the BFF as the caller; this component holds no role check (#42) and shows what the server answered.
 *
 * A rename is refused by the server while any policy version reads the category; the form shows the refusal naming the rule. A
 * category never measured shows "Not measured"; no classifier confidence is shown, because none exists (#5).
 *
 * Fluent v9 semantic tokens only (ADR-021); light and dark both render through the host `FluentProvider`.
 *
 * Task: spaarke-ontology-platform-r1, task 103.
 */

import * as React from 'react';
import {
  Button,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  Spinner,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Text,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { StatusBadge } from '../../StatusBadge';
import { CategoryForm } from './CategoryForm';
import type { CategoryFormValues } from './CategoryForm';
import type { ClassificationCategory, ClassificationList, ClassificationService } from './classificationService';
import { describeRecall, describeVolume, groupRuleReads } from './classificationView';

export interface ClassificationAreaProps {
  /** The BFF-backed service (`createClassificationService`). */
  service: ClassificationService;
}

type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; data: ClassificationList };

type FormState = { mode: 'create' } | { mode: 'edit'; id: string } | null;

/** What turning a category Off does, in the words the administrator needs: it stops NEW classification only (K1). */
function offEffect(name: string, ruleCodes: ReadonlyArray<string>): string {
  const base = `New emails will no longer be classified as ${name}.`;
  return ruleCodes.length === 0
    ? base
    : `${base} ${ruleCodes.join(', ')} still ${ruleCodes.length === 1 ? 'matches' : 'match'} emails already classified as ${name}.`;
}

interface Notice {
  intent: 'success' | 'warning' | 'error';
  text: string;
}

const GUIDANCE_PREVIEW_LENGTH = 140;

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalM, minWidth: 0 },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    columnGap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  muted: { color: tokens.colorNeutralForeground3 },
  cellStack: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalXXS },
  rules: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalXXS },
  badgeRow: { display: 'flex', alignItems: 'center', columnGap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  numeric: { textAlign: 'right' },
});

const messageOf = (err: unknown, fallback: string): string => (err instanceof Error && err.message ? err.message : fallback);

export const ClassificationArea: React.FC<ClassificationAreaProps> = ({ service }) => {
  const styles = useStyles();
  const [state, setState] = React.useState<LoadState>({ status: 'loading' });
  const [form, setForm] = React.useState<FormState>(null);
  const [notice, setNotice] = React.useState<Notice | null>(null);
  const [busyId, setBusyId] = React.useState<string | null>(null);
  /** The category the administrator asked to turn Off while a rule reads it: nothing is written until they confirm. */
  const [confirmOffId, setConfirmOffId] = React.useState<string | null>(null);
  const requests = React.useRef(0);
  const alive = React.useRef(true);

  /** Reads the list; an answer that is no longer the latest request is ignored. Keeps the last good list on screen while reloading. */
  const load = React.useCallback(async () => {
    requests.current += 1;
    const mine = requests.current;
    try {
      const data = await service.list();
      if (alive.current && requests.current === mine) setState({ status: 'ready', data });
    } catch (err) {
      if (alive.current && requests.current === mine) {
        setState({ status: 'error', message: messageOf(err, 'The triage categories could not be read.') });
      }
    }
  }, [service]);

  React.useEffect(() => {
    alive.current = true;
    void load();
    return () => {
      alive.current = false;
    };
  }, [load]);

  const toggle = React.useCallback(
    async (category: ClassificationCategory, enabled: boolean) => {
      setBusyId(category.id);
      setNotice(null);
      try {
        await service.setEnabled(category.id, enabled, category.version);
        if (!alive.current) return;
        const readers = groupRuleReads(category.readBy);
        setNotice(
          !enabled && readers.length > 0
            ? { intent: 'warning', text: `${category.name} is Off. ${offEffect(category.name, readers.map(r => r.policyCode))}` }
            : { intent: 'success', text: `${category.name} is ${enabled ? 'On' : 'Off'}.` }
        );
      } catch (err) {
        if (alive.current) setNotice({ intent: 'error', text: messageOf(err, 'The category could not be changed.') });
      } finally {
        if (alive.current) setBusyId(null);
      }
      await load();
    },
    [service, load]
  );

  /** On writes at once. Off asks first when a rule reads the category, because the rule keeps matching what was classified before. */
  const requestToggle = React.useCallback(
    (category: ClassificationCategory, enabled: boolean) => {
      if (!enabled && groupRuleReads(category.readBy).length > 0) {
        setNotice(null);
        setConfirmOffId(category.id);
        return;
      }
      setConfirmOffId(null);
      void toggle(category, enabled);
    },
    [toggle]
  );

  const submit = React.useCallback(
    async (values: CategoryFormValues, editing: ClassificationCategory | undefined) => {
      // A failure throws to the form, which shows it; success closes the form and reloads.
      if (editing) {
        await service.update(editing.id, { ...values, version: editing.version });
        setNotice({ intent: 'success', text: `${values.name} was saved.` });
      } else {
        await service.create(values);
        setNotice({ intent: 'success', text: `${values.name} was created Off. Turn it On when its guidance reads right.` });
      }
      if (alive.current) setForm(null);
      await load();
    },
    [service, load]
  );

  if (state.status === 'loading') {
    return <Spinner label="Loading triage categories" data-testid="classification-loading" />;
  }

  if (state.status === 'error') {
    return (
      <div className={styles.root}>
        <MessageBar intent="error" data-testid="classification-error">
          <MessageBarBody>{state.message}</MessageBarBody>
        </MessageBar>
        <div>
          <Button
            onClick={() => {
              setState({ status: 'loading' });
              void load();
            }}
            data-testid="classification-retry"
          >
            Try again
          </Button>
        </div>
      </div>
    );
  }

  const { data } = state;
  const editing = form?.mode === 'edit' ? data.categories.find(c => c.id === form.id) : undefined;
  const confirmOff = confirmOffId === null ? undefined : data.categories.find(c => c.id === confirmOffId && c.enabled);

  return (
    <div className={styles.root} data-testid="classification-area">
      <div className={styles.header}>
        <div className={styles.cellStack}>
          <Text weight="semibold" size={500}>
            Triage categories
          </Text>
          <Text className={styles.muted}>
            The categories the classifier chooses from. A change here applies to the next email classified. Recall floor:{' '}
            {data.recallFloor.percent}% on at least {data.recallFloor.minItems} labelled items.
          </Text>
        </div>
        {form === null && (
          <Button
            appearance="primary"
            onClick={() => {
              setNotice(null);
              setForm({ mode: 'create' });
            }}
            data-testid="classification-new"
          >
            New category
          </Button>
        )}
      </div>

      {notice && (
        <MessageBar intent={notice.intent} data-testid="classification-notice">
          <MessageBarBody>{notice.text}</MessageBarBody>
        </MessageBar>
      )}
      {confirmOff && (
        <MessageBar intent="warning" data-testid="classification-confirm-off">
          <MessageBarBody>
            Turn {confirmOff.name} Off?{' '}
            {offEffect(
              confirmOff.name,
              groupRuleReads(confirmOff.readBy).map(r => r.policyCode)
            )}
          </MessageBarBody>
          <MessageBarActions>
            <Button
              size="small"
              appearance="primary"
              onClick={() => {
                setConfirmOffId(null);
                void toggle(confirmOff, false);
              }}
              data-testid="classification-confirm-off-yes"
            >
              Turn Off
            </Button>
            <Button size="small" onClick={() => setConfirmOffId(null)} data-testid="classification-confirm-off-no">
              Keep On
            </Button>
          </MessageBarActions>
        </MessageBar>
      )}
      {!data.referencesAvailable && (
        <MessageBar intent="warning" data-testid="classification-references-unavailable">
          <MessageBarBody>
            Which rules read each category could not be checked, so an empty list below does not mean no rule reads it. A category
            cannot be renamed until it can be checked.
          </MessageBarBody>
        </MessageBar>
      )}
      {!data.volumeAvailable && (
        <MessageBar intent="warning" data-testid="classification-volume-unavailable">
          <MessageBarBody>The last {data.volumeWindowDays} days could not be counted; volumes show a dash.</MessageBarBody>
        </MessageBar>
      )}

      {form?.mode === 'create' && (
        <CategoryForm onSubmit={values => submit(values, undefined)} onCancel={() => setForm(null)} />
      )}
      {form?.mode === 'edit' && editing && (
        <CategoryForm
          key={`${editing.id}:${editing.version}`}
          category={editing}
          onSubmit={values => submit(values, editing)}
          onCancel={() => setForm(null)}
        />
      )}

      {data.categories.length === 0 ? (
        <Text className={styles.muted} data-testid="classification-empty">
          There are no triage categories yet.
        </Text>
      ) : (
        <Table aria-label="Triage categories" data-testid="classification-table">
          <TableHeader>
            <TableRow>
              <TableHeaderCell>Category</TableHeaderCell>
              <TableHeaderCell>On / Off</TableHeaderCell>
              <TableHeaderCell>Guidance</TableHeaderCell>
              <TableHeaderCell>Read by</TableHeaderCell>
              <TableHeaderCell className={styles.numeric}>Last {data.volumeWindowDays} days</TableHeaderCell>
              <TableHeaderCell>Recall</TableHeaderCell>
              <TableHeaderCell />
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.categories.map(category => (
              <CategoryRow
                key={category.id}
                category={category}
                floor={data.recallFloor}
                busy={busyId === category.id}
                formOpen={editing?.id === category.id}
                onToggle={enabled => requestToggle(category, enabled)}
                onEdit={() => {
                  setNotice(null);
                  setForm({ mode: 'edit', id: category.id });
                }}
              />
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  );
};

ClassificationArea.displayName = 'ClassificationArea';

interface CategoryRowProps {
  category: ClassificationCategory;
  floor: ClassificationList['recallFloor'];
  busy: boolean;
  /** This row's edit form is open: the switch is disabled so a reload cannot remount the form and lose what was typed (K4). */
  formOpen: boolean;
  onToggle: (enabled: boolean) => void;
  onEdit: () => void;
}

const CategoryRow: React.FC<CategoryRowProps> = ({ category, floor, busy, formOpen, onToggle, onEdit }) => {
  const styles = useStyles();
  const recall = describeRecall(category.recall, floor);
  const readers = groupRuleReads(category.readBy);
  const guidance = category.guidance ?? '';
  const preview = guidance.length > GUIDANCE_PREVIEW_LENGTH ? `${guidance.slice(0, GUIDANCE_PREVIEW_LENGTH)}...` : guidance;

  return (
    <TableRow data-testid={`classification-row-${category.id}`}>
      <TableCell>
        <Text weight="semibold">{category.name}</Text>
      </TableCell>
      <TableCell>
        <Switch
          checked={category.enabled}
          disabled={busy || formOpen}
          label={category.enabled ? 'On' : 'Off'}
          aria-label={`${category.name} is ${category.enabled ? 'On' : 'Off'}`}
          onChange={(_e, data) => onToggle(data.checked)}
          data-testid={`classification-toggle-${category.id}`}
        />
      </TableCell>
      <TableCell>
        {guidance === '' ? (
          <Text className={styles.muted}>No guidance</Text>
        ) : (
          <Tooltip content={guidance} relationship="description">
            <Text>{preview}</Text>
          </Tooltip>
        )}
      </TableCell>
      <TableCell>
        {readers.length === 0 ? (
          <Text className={styles.muted}>No rule</Text>
        ) : (
          <div className={styles.rules} data-testid={`classification-readers-${category.id}`}>
            {readers.map(r => (
              <Tooltip key={r.policyId} content={`${r.policyName ?? r.policyCode}: ${r.versions}`} relationship="description">
                <Text>
                  {r.policyCode}
                  {r.policyEnabled ? '' : ' (Off)'}
                </Text>
              </Tooltip>
            ))}
          </div>
        )}
      </TableCell>
      <TableCell className={styles.numeric}>
        <Text data-testid={`classification-volume-${category.id}`}>{describeVolume(category.volume30Days)}</Text>
      </TableCell>
      <TableCell>
        <div className={styles.cellStack} data-testid={`classification-recall-${category.id}`}>
          <div className={styles.badgeRow}>
            <StatusBadge label={recall.label} tone={recall.tone} />
          </div>
          {recall.detail !== null && <Text className={styles.muted}>{recall.detail}</Text>}
          {recall.staleNote !== null && (
            <Text className={styles.muted} data-testid={`classification-recall-stale-${category.id}`}>
              {recall.staleNote}
            </Text>
          )}
        </div>
      </TableCell>
      <TableCell>
        <Button size="small" onClick={onEdit} aria-label={`Edit ${category.name}`} data-testid={`classification-edit-${category.id}`}>
          Edit
        </Button>
      </TableCell>
    </TableRow>
  );
};
