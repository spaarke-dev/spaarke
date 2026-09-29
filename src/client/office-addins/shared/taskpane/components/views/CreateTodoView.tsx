import React, { useEffect, useMemo, useRef, useState } from 'react';
import {
  Button,
  Card,
  Dropdown,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Option,
  Spinner,
  Text,
  Textarea,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  CheckmarkRegular,
  DismissRegular,
  PersonSearchRegular,
  SearchRegular,
  TaskListAddRegular,
} from '@fluentui/react-icons';
import type { IHostAdapter } from '@shared/adapters';
import {
  TODO_PRIORITY_CHOICES,
  TODO_EFFORT_CHOICES,
  DEFAULT_PRIORITY_CHOICE,
  DEFAULT_EFFORT_CHOICE,
  priorityChoiceToScore,
  effortChoiceToScore,
} from '../../services/todoChoices';

/**
 * CreateTodoView — inline "Create To Do" tool in the Spaarke taskpane.
 *
 * UX decision (email-communication-intelligence-r2, owner 2026-09-02): the tool creates a
 * **first-class `sprk_todo`** (NOT a `sprk_event` — "we are not using the sprk-event type 'to do'
 * anymore"), mirroring the **To Do Details** step of the `CreateTodoWizard`
 * (`@spaarke/ui-components`). The form fields are Name, Description, Assigned To (a Contact lookup),
 * Due Date, Priority, and Effort. The To Do's regarding is **the record the email was filed to**
 * ("the To Do should be created Related to the record that the email has been Related to"), shown as
 * a read-only green record card mirroring the Save screen's selected card.
 *
 * On Save the pane does NOT close — the Save button turns into a gray "Saved" indicator (owner
 * 2026-09-02). Priority/Effort are resolved client-side to their 0-100 scores (see `todoChoices.ts`)
 * and POSTed to `/api/office/todo`.
 *
 * Fluent UI v9 + Griffel `makeStyles` + semantic tokens only (ADR-021).
 */

/** The record this email is filed to (from the Save flow) — the To Do's regarding. */
export interface SavedTodoContext {
  /**
   * `sprk_communicationid` of the saved email, when known. Optional — the To Do's regarding is the
   * RECORD (below), not the communication, so a real save need not surface a communication id. The
   * browser harness sets a `demo-…` value to route the create to a mocked success.
   */
  communicationId?: string;
  /** Confirmed record's friendly type — "Matter" / "Project" / "Invoice" (the To Do regarding). */
  regardingEntity: string;
  /** Confirmed record id (the To Do regarding). */
  regardingRecordId: string;
  /** Friendly label for the regarding record, shown in the pane + written to sprk_regardingrecordname. */
  regardingName?: string;
}

/** A contact returned by the Assigned-To lookup. */
export interface ContactOption {
  id: string;
  name: string;
  displayInfo?: string;
}

/** Human-authored fields for the create-To-Do call (the client resolves Priority/Effort to scores). */
export interface CreateTodoInput {
  name: string;
  description?: string;
  assignedToContactId?: string;
  /** ISO `yyyy-mm-dd` (`sprk_duedate` is Date-Only). */
  dueDate?: string;
  priorityScore: number;
  effortScore: number;
}

export interface CreateTodoResult {
  ok: boolean;
  error?: string;
}

export interface CreateTodoViewProps {
  /** Host adapter — used to prefill the title from the email subject. */
  hostAdapter: IHostAdapter;
  /**
   * The record this email is filed to (from the Save flow). When absent the form is disabled and the
   * user is prompted to file the email first.
   */
  savedContext?: SavedTodoContext;
  /** Creates the To Do (host wires this to `POST /api/office/todo`). */
  onCreateTodo: (input: CreateTodoInput) => Promise<CreateTodoResult>;
  /** Searches Contacts for the Assigned-To lookup (host wires this to the BFF entity search, type=Contact). */
  onSearchContacts: (query: string) => Promise<ContactOption[]>;
  /** Navigate to the Save tab (offered when the email isn't filed yet). */
  onGoToSave?: () => void;
}

type FlowStatus = 'idle' | 'creating' | 'created' | 'error';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    padding: tokens.spacingVerticalM,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  // Regarding record card — mirrors the Save screen's selected (green) card.
  regardingCard: {
    padding: tokens.spacingVerticalS,
    borderLeft: `3px solid ${tokens.colorStatusSuccessBorder2}`,
  },
  regardingRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  regardingBody: { display: 'flex', flexDirection: 'column', gap: '2px', flexGrow: 1, minWidth: 0 },
  regardingMeta: { color: tokens.colorNeutralForeground3 },
  greenCheck: {
    flexShrink: 0,
    backgroundColor: tokens.colorStatusSuccessBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
    ':hover': { backgroundColor: tokens.colorStatusSuccessBackground3, color: tokens.colorNeutralForegroundOnBrand },
  },
  regardingLabel: { color: tokens.colorNeutralForeground2, marginBottom: tokens.spacingVerticalXS },
  // Priority/Effort: side-by-side when there's room, stack when the pane is narrow.
  twoCol: { display: 'flex', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  col: { flex: '1 1 110px', minWidth: '110px' },
  // Fluent Dropdown defaults to a ~250px min-width — override so it shrinks in a narrow pane.
  dropdownFull: { minWidth: 'unset', width: '100%' },
  // Contact lookup.
  lookupResults: {
    display: 'flex',
    flexDirection: 'column',
    gap: '2px',
    marginTop: tokens.spacingVerticalXS,
    maxHeight: '160px',
    overflowY: 'auto',
  },
  lookupItem: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
    borderRadius: tokens.borderRadiusMedium,
    cursor: 'pointer',
    textAlign: 'left',
    border: 'none',
    backgroundColor: tokens.colorNeutralBackground1,
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
  },
  lookupMeta: { color: tokens.colorNeutralForeground3 },
  selectedContact: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
  },
  selectedContactName: { flexGrow: 1, minWidth: 0 },
  footer: {
    display: 'flex',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
    marginTop: tokens.spacingVerticalS,
    flexWrap: 'wrap',
  },
  savedBtn: {
    backgroundColor: tokens.colorNeutralBackground5,
    color: tokens.colorNeutralForeground3,
    ':hover': { backgroundColor: tokens.colorNeutralBackground5, color: tokens.colorNeutralForeground3 },
  },
});

export const CreateTodoView: React.FC<CreateTodoViewProps> = ({
  hostAdapter,
  savedContext,
  onCreateTodo,
  onSearchContacts,
  onGoToSave,
}) => {
  const styles = useStyles();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [dueDate, setDueDate] = useState('');
  const [priority, setPriority] = useState<string>(DEFAULT_PRIORITY_CHOICE);
  const [effort, setEffort] = useState<string>(DEFAULT_EFFORT_CHOICE);
  const [status, setStatus] = useState<FlowStatus>('idle');
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  // Assigned-To (Contact) lookup state.
  const [assignedTo, setAssignedTo] = useState<ContactOption | null>(null);
  const [contactQuery, setContactQuery] = useState('');
  const [contactResults, setContactResults] = useState<ContactOption[]>([]);
  const [contactSearching, setContactSearching] = useState(false);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const isFiled = savedContext !== undefined;
  const disabled = !isFiled || status === 'creating' || status === 'created';

  // Prefill the title from the email subject (best-effort).
  useEffect(() => {
    let active = true;
    void (async () => {
      try {
        const subject = await hostAdapter.getSubject();
        if (active && subject) {
          setName(subject);
        }
      } catch {
        /* subject is optional — leave the field empty */
      }
    })();
    return () => {
      active = false;
    };
  }, [hostAdapter]);

  // Debounced Contact search (type-ahead) — only while nothing is selected.
  useEffect(() => {
    if (debounceRef.current) {
      clearTimeout(debounceRef.current);
    }
    if (assignedTo || contactQuery.trim().length < 2) {
      setContactResults([]);
      return;
    }
    debounceRef.current = setTimeout(() => {
      void (async () => {
        setContactSearching(true);
        try {
          setContactResults(await onSearchContacts(contactQuery.trim()));
        } catch {
          setContactResults([]);
        } finally {
          setContactSearching(false);
        }
      })();
    }, 300);
    return () => {
      if (debounceRef.current) {
        clearTimeout(debounceRef.current);
      }
    };
  }, [contactQuery, assignedTo, onSearchContacts]);

  const canCreate = isFiled && name.trim().length > 0 && status === 'idle';

  const handleCreate = async (): Promise<void> => {
    setStatus('creating');
    setErrorMsg(null);
    try {
      const input: CreateTodoInput = {
        name: name.trim(),
        priorityScore: priorityChoiceToScore(priority),
        effortScore: effortChoiceToScore(effort),
      };
      if (description.trim()) {
        input.description = description.trim();
      }
      if (assignedTo) {
        input.assignedToContactId = assignedTo.id;
      }
      if (dueDate) {
        input.dueDate = dueDate;
      }
      const result = await onCreateTodo(input);
      if (result.ok) {
        setStatus('created');
      } else {
        setStatus('error');
        setErrorMsg(result.error ?? 'Could not create the To Do.');
      }
    } catch (err) {
      setStatus('error');
      setErrorMsg(err instanceof Error ? err.message : 'Could not create the To Do.');
    }
  };

  const regardingLabel = useMemo(
    () => savedContext?.regardingName ?? savedContext?.regardingEntity ?? '',
    [savedContext]
  );

  // Cancel = discard the current form input (clears fields, stays on the tab).
  const handleCancel = (): void => {
    setName('');
    setDescription('');
    setDueDate('');
    setPriority(DEFAULT_PRIORITY_CHOICE);
    setEffort(DEFAULT_EFFORT_CHOICE);
    setAssignedTo(null);
    setContactQuery('');
    setContactResults([]);
    setStatus('idle');
    setErrorMsg(null);
  };

  return (
    <div className={styles.container} role="region" aria-label="Create To Do">
      <div className={styles.header}>
        <TaskListAddRegular aria-hidden="true" />
        <Text size={500} weight="semibold">
          Create a To Do
        </Text>
      </div>

      {!isFiled ? (
        <>
          <MessageBar intent="warning" role="status">
            <MessageBarBody>
              <MessageBarTitle>File this email first</MessageBarTitle>A To Do is related to the record you file the
              email to. Save it on the <strong>Save</strong> tab, then come back here.
            </MessageBarBody>
          </MessageBar>
          {onGoToSave && (
            <div className={styles.footer}>
              <span />
              <Button appearance="primary" onClick={onGoToSave}>
                Go to Save
              </Button>
            </div>
          )}
        </>
      ) : (
        <>
          {/* Regarding — the record the email was filed to (green record card, read-only). */}
          <div>
            <Text size={200} weight="semibold" className={styles.regardingLabel} block>
              Related to
            </Text>
            <Card className={styles.regardingCard}>
              <div className={styles.regardingRow}>
                <div className={styles.regardingBody}>
                  <Text weight="semibold">{regardingLabel}</Text>
                  <Text size={200} className={styles.regardingMeta}>
                    {savedContext?.regardingEntity}
                  </Text>
                </div>
                <Button
                  className={styles.greenCheck}
                  appearance="primary"
                  icon={<CheckmarkRegular />}
                  aria-label="Related record"
                  disabled
                />
              </div>
            </Card>
          </div>

          <Field label="Name" required>
            <Input
              value={name}
              onChange={(_, d) => setName(d.value)}
              placeholder="What needs to be done?"
              disabled={disabled}
            />
          </Field>

          <Field label="Description">
            <Textarea
              value={description}
              onChange={(_, d) => setDescription(d.value)}
              placeholder="Add details (optional)"
              rows={3}
              resize="vertical"
              disabled={disabled}
            />
          </Field>

          <Field label="Assigned To" hint="Contact">
            {assignedTo ? (
              <div className={styles.selectedContact}>
                <PersonSearchRegular aria-hidden="true" />
                <Text className={styles.selectedContactName}>{assignedTo.name}</Text>
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<DismissRegular />}
                  aria-label="Clear assignee"
                  onClick={() => {
                    setAssignedTo(null);
                    setContactQuery('');
                  }}
                  disabled={disabled}
                />
              </div>
            ) : (
              <>
                <Input
                  value={contactQuery}
                  onChange={(_, d) => setContactQuery(d.value)}
                  placeholder="Search contacts…"
                  contentBefore={contactSearching ? <Spinner size="tiny" /> : <SearchRegular />}
                  disabled={disabled}
                  aria-label="Search contacts"
                />
                {contactResults.length > 0 && (
                  <div className={styles.lookupResults} role="listbox" aria-label="Contact results">
                    {contactResults.map(c => (
                      <button
                        key={c.id}
                        type="button"
                        className={styles.lookupItem}
                        role="option"
                        aria-selected="false"
                        onClick={() => {
                          setAssignedTo(c);
                          setContactResults([]);
                          setContactQuery('');
                        }}
                      >
                        <Text size={300}>{c.name}</Text>
                        {c.displayInfo && (
                          <Text size={200} className={styles.lookupMeta}>
                            {c.displayInfo}
                          </Text>
                        )}
                      </button>
                    ))}
                  </div>
                )}
              </>
            )}
          </Field>

          <Field label="Due Date" hint="Optional">
            <Input type="date" value={dueDate} onChange={(_, d) => setDueDate(d.value)} disabled={disabled} />
          </Field>

          <div className={styles.twoCol}>
            <Field label="Priority" className={styles.col}>
              <Dropdown
                className={styles.dropdownFull}
                value={priority}
                selectedOptions={[priority]}
                onOptionSelect={(_, d) => setPriority((d.optionValue as string) ?? DEFAULT_PRIORITY_CHOICE)}
                disabled={disabled}
              >
                {TODO_PRIORITY_CHOICES.map(choice => (
                  <Option key={choice} value={choice}>
                    {choice}
                  </Option>
                ))}
              </Dropdown>
            </Field>
            <Field label="Effort" className={styles.col}>
              <Dropdown
                className={styles.dropdownFull}
                value={effort}
                selectedOptions={[effort]}
                onOptionSelect={(_, d) => setEffort((d.optionValue as string) ?? DEFAULT_EFFORT_CHOICE)}
                disabled={disabled}
              >
                {TODO_EFFORT_CHOICES.map(choice => (
                  <Option key={choice} value={choice}>
                    {choice}
                  </Option>
                ))}
              </Dropdown>
            </Field>
          </div>

          {status === 'error' && errorMsg && (
            <MessageBar intent="error" role="alert">
              <MessageBarBody>{errorMsg}</MessageBarBody>
            </MessageBar>
          )}

          {/* Footer — Cancel (left), Save (right). On save the pane stays open; Save → gray "Saved". */}
          <div className={styles.footer}>
            <Button appearance="secondary" onClick={handleCancel} disabled={status === 'creating'}>
              Cancel
            </Button>
            {status === 'created' ? (
              <Button className={styles.savedBtn} disabled>
                Saved
              </Button>
            ) : (
              <Button appearance="primary" onClick={() => void handleCreate()} disabled={!canCreate}>
                {status === 'creating' ? 'Saving…' : 'Save'}
              </Button>
            )}
          </div>
        </>
      )}
    </div>
  );
};
