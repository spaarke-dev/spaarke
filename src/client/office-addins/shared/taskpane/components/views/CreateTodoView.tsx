import React, { useEffect, useMemo, useRef, useState } from 'react';
import {
  Button,
  Card,
  Dropdown,
  Field,
  Input,
  MessageBar,
  MessageBarActions,
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
  OpenRegular,
  PersonSearchRegular,
  SearchRegular,
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
import { MicrosoftToDoIcon } from '../icons/MicrosoftToDoIcon';
import { useAnnounce } from '../../hooks/useAnnounce';
import { openRecord } from '../../services/openRecordLauncher';

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
   * `sprk_communicationid` of the saved email, when known — ALSO (FR-14, task 035) written to
   * `sprk_regardingcommunication` as the Outlook counterpart of `documentId` below, when it is a real
   * (non-`demo-`) id. The browser harness sets a `demo-…` value to route the create to a mocked success;
   * that value is never sent to the server as a regarding.
   */
  communicationId?: string;
  /** Confirmed record's friendly type — "Matter" / "Project" / "Invoice" (the To Do regarding). */
  regardingEntity: string;
  /** Confirmed record id (the To Do regarding). */
  regardingRecordId: string;
  /**
   * Friendly label for the regarding record — written to `sprk_regardingrecordname`. For Matter/Project
   * this is the record's NUMBER (Dataverse primary-name quirk, not a display name — see
   * `notes/026-slot-scope-decision.md` §3), which is why `regardingDisplayName` below is preferred for
   * what the pane SHOWS; this field remains what the server writes as a fallback when no richer label
   * is available.
   */
  regardingName?: string;
  /**
   * The regarding record's DESCRIPTIVE display name (task 026's richer label,
   * `RelatedRecordIdentity.displayName`) — preferred over `regardingName` for what the pane shows and
   * what is sent as `regardingRecordName`. Absent (not just falsy) when not resolved via the document-
   * identity path; a pane-created Matter can also have a blank/empty value here until it has a name.
   */
  regardingDisplayName?: string;
  /**
   * The open Word document's `sprk_document` id (task 013's resolved identity) — written to
   * `sprk_regardingdocument` (FR-14, task 035). Independent of the record regarding: both may be set on
   * the same create call.
   */
  documentId?: string;
}

/** A contact returned by the Assigned-To lookup. */
export interface ContactOption {
  id: string;
  name: string;
  /**
   * The server's `displayInfo` for this contact — today, the job title, or (when the contact has none)
   * the literal entity-type string `"contact"` (`OfficeSearchService.MapSearchRow`'s own fallback — not
   * this task's scope to change, since the SAME field feeds the shared "Related to" record picker).
   * Rendered as the quieter, third line ONLY when it is not that literal (task 091 / UAT-2: "no literal
   * 'contact'").
   */
  displayInfo?: string;
  /**
   * The contact's email (task 091 / UAT-2) — additive, from the server's new `EntitySearchResult.Email`.
   * Absent when the contact has no email on file. Lets the picker tell apart two contacts sharing a name.
   */
  email?: string;
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
  /**
   * Task 091 (UAT-2): the created `sprk_todo` id (the server's `CreateTodoResponse.TodoId`), echoed back
   * so the confirmation can offer "Open in Spaarke". Absent on failure, and absent on the browser test
   * harness's demo-success path (no real row was written, so there is nothing to open).
   */
  todoId?: string;
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
  /**
   * Task 091 (UAT-2, NFR-10): whether this host can open a browser tab
   * (`hostAdapter.getCapabilities().canOpenBrowserWindow`, decided by `App` from the live adapter — never a
   * `hostType` check here, same pattern as `FindView.canOpenRecord` / `SaveView.canOpenRecord`). Gates the
   * created-To-Do confirmation's "Open in Spaarke" link; also requires `ORG_URL` to be configured.
   * Defaults to `false`.
   */
  canOpenRecord?: boolean;
}

type FlowStatus = 'idle' | 'creating' | 'created' | 'error';

/**
 * Task 091 (UAT-2): the contact's job title, or `undefined` when there isn't one — INCLUDING when the
 * server's `displayInfo` fallback is the literal Dataverse logical-name string `"contact"`
 * (`OfficeSearchService.MapSearchRow`'s fallback for a contact with no job title). That literal must
 * never render as if it were a job title ("no literal 'contact'"). Pure — independently testable.
 */
function realJobTitle(contact: ContactOption): string | undefined {
  return contact.displayInfo && contact.displayInfo !== 'contact' ? contact.displayInfo : undefined;
}

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
  // Task 091 (UAT-2): the job title, kept as a third, QUIETER line than the email line above it — same
  // color token, smaller text size (the <Text size={100}> below), rather than a new, unproven token.
  lookupMetaQuiet: { color: tokens.colorNeutralForeground3 },
  selectedContact: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
  },
  // Task 091 (UAT-2): the chip now stacks name + email (when present) instead of a single text node.
  selectedContactBody: { display: 'flex', flexDirection: 'column', gap: '2px', flexGrow: 1, minWidth: 0 },
  selectedContactName: { flexGrow: 1, minWidth: 0 },
  selectedContactMeta: { color: tokens.colorNeutralForeground3 },
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
  canOpenRecord = false,
}) => {
  const styles = useStyles();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [dueDate, setDueDate] = useState('');
  const [priority, setPriority] = useState<string>(DEFAULT_PRIORITY_CHOICE);
  const [effort, setEffort] = useState<string>(DEFAULT_EFFORT_CHOICE);
  const [status, setStatus] = useState<FlowStatus>('idle');
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  // Task 091 (UAT-2): a snapshot of what was just created — taken at the moment `onCreateTodo` succeeds,
  // so the confirmation names the To Do that was actually created even though the form's own fields stay
  // disabled (and therefore unchanged) for the rest of the 'created' state.
  const [createdTodo, setCreatedTodo] = useState<{ name: string; todoId?: string } | null>(null);
  const { announce, liveRegion } = useAnnounce();
  // NFR-10: same pattern as FindView.openRecordAvailable / SaveFlow.openRecordAvailable — decided from
  // the capability PLUS config, never rendered as a disabled/dead link.
  const openRecordAvailable = canOpenRecord && Boolean(process.env.ORG_URL);

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
        setCreatedTodo({ name: input.name, ...(result.todoId ? { todoId: result.todoId } : {}) });
        announce(`To Do created: ${input.name}`, 'polite');
      } else {
        const message = result.error ?? 'Could not create the To Do.';
        setStatus('error');
        setErrorMsg(message);
        announce(message, 'assertive');
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Could not create the To Do.';
      setStatus('error');
      setErrorMsg(message);
      announce(message, 'assertive');
    }
  };

  // Prefer the descriptive displayName (task 026) over the raw regardingName — which, for Matter/Project,
  // is actually the record NUMBER (a Dataverse primary-name quirk, not a display name; see
  // notes/026-slot-scope-decision.md §3) and can be blank for a pane-created Matter with no number yet.
  // Falls through to regardingEntity so the card is never blank.
  const regardingLabel = useMemo(
    () => savedContext?.regardingDisplayName || savedContext?.regardingName || savedContext?.regardingEntity || '',
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
    setCreatedTodo(null);
  };

  return (
    <div className={styles.container} role="region" aria-label="Create To Do">
      {liveRegion}
      <div className={styles.header}>
        {/* Task 091 (UAT-2): the blue Microsoft To Do check — the view heading keeps its own text
            ("Create a To Do"), only the icon changes, per the owner's exact scope. The SVG itself already
            carries aria-hidden="true" (decorative; the heading text is the accessible label). */}
        <MicrosoftToDoIcon active />
        <Text size={500} weight="semibold">
          Create a To Do
        </Text>
      </div>

      {/* Task 091 (UAT-2): an unmistakable confirmation after a successful create — a full MessageBar,
          not the gray "Saved" button alone (which stays, in the footer, matching the Save tab's own
          precedent for "the form stays visible after a save"). Mirrors SaveFlow's `renderSavedBar`. */}
      {status === 'created' && createdTodo && (
        <MessageBar intent="success" layout="multiline" role="status">
          <MessageBarBody>
            <MessageBarTitle>To Do created: {createdTodo.name}</MessageBarTitle>
          </MessageBarBody>
          <MessageBarActions>
            {/* NFR-10: rendered only when the host can open a browser tab AND ORG_URL is set AND a real
                todoId came back — never a disabled/dead link. */}
            {openRecordAvailable && createdTodo.todoId && (
              <Button
                appearance="outline"
                size="small"
                icon={<OpenRegular />}
                onClick={() =>
                  openRecord({ orgUrl: process.env.ORG_URL, entityType: 'sprk_todo', recordId: createdTodo.todoId! })
                }
              >
                Open in Spaarke
              </Button>
            )}
          </MessageBarActions>
        </MessageBar>
      )}

      {!isFiled ? (
        <>
          {/* Host-neutral (task 049 / NFR-10): Create To Do is a shared capability, so this prompt must
              read correctly for a saved Word document as well as a filed Outlook email — no host-type
              conditional, just wording that avoids naming either concept. */}
          <MessageBar intent="warning" role="status">
            <MessageBarBody>
              <MessageBarTitle>Save this to Spaarke first</MessageBarTitle>A To Do is related to the record this is
              saved to. Save it on the <strong>Save</strong> tab, then come back here.
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
                {/* Task 091 (UAT-2): the chip shows name AND email, so a Save of this To Do can't be
                    second-guessed as "assigned to the wrong Jane Cooper". */}
                <div className={styles.selectedContactBody}>
                  <Text className={styles.selectedContactName}>{assignedTo.name}</Text>
                  {assignedTo.email && (
                    <Text size={200} className={styles.selectedContactMeta}>
                      {assignedTo.email}
                    </Text>
                  )}
                </div>
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
                    {/* Task 091 (UAT-2): name, then email (when the contact has one — tells apart two
                        contacts sharing a name), then job title as a third, quieter line (when present
                        and not the server's literal "contact" fallback). A contact with neither shows
                        name only — never the literal "contact". */}
                    {contactResults.map(c => {
                      const jobTitle = realJobTitle(c);
                      return (
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
                          {c.email && (
                            <Text size={200} className={styles.lookupMeta}>
                              {c.email}
                            </Text>
                          )}
                          {jobTitle && (
                            <Text size={100} className={styles.lookupMetaQuiet}>
                              {jobTitle}
                            </Text>
                          )}
                        </button>
                      );
                    })}
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
