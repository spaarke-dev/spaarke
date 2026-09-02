import React, { useEffect, useState } from 'react';
import {
  Button,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { CheckmarkCircleRegular, DocumentRegular, TaskListAddRegular } from '@fluentui/react-icons';
import type { IHostAdapter } from '@shared/adapters';

/**
 * CreateTodoView — inline "Create To Do" tool in the Spaarke taskpane.
 *
 * UX decision (email-communication-intelligence-r2, owner 2026-09-01): the To Do is
 * created **inline, right in the pane** — a small form (title / due date / regarding)
 * that POSTs to `/api/communications/{communicationId}/create-task`. It does NOT open
 * the full SmartTodo kanban/app (the earlier popup-launcher behavior is retired).
 *
 * A To Do is a `sprk_event` (type=task) whose regarding is the record the email was
 * filed to (NFR-10). So the view needs a {@link SavedTodoContext}: absent → the email
 * isn't filed yet, so we prompt the user to file it on the Save tab first.
 *
 * Fluent UI v9 + Griffel `makeStyles` + semantic tokens only (ADR-021).
 */

/** The record this email is filed to (from the Save flow) — the To Do's regarding + its communication. */
export interface SavedTodoContext {
  /** `sprk_communicationid` of the saved email (the create-task route param). */
  communicationId: string;
  /** Confirmed record logical name (the task regarding, NFR-10). */
  regardingEntity: string;
  /** Confirmed record id (the task regarding, NFR-10). */
  regardingRecordId: string;
  /** Friendly label for the regarding record, shown in the pane. */
  regardingName?: string;
}

/** Human-authored fields for the inline create-task call. */
export interface CreateTaskInput {
  subject: string;
  /** ISO `yyyy-mm-dd` (the `sprk_event` date columns are Date-Only). */
  dueDate?: string;
  description?: string;
}

export interface CreateTaskResult {
  ok: boolean;
  error?: string;
}

export interface CreateTodoViewProps {
  /** Host adapter — used to prefill the title from the email subject. */
  hostAdapter: IHostAdapter;
  /**
   * The record this email is filed to (from the Save flow). When absent the form is
   * disabled and the user is prompted to file the email first.
   */
  savedContext?: SavedTodoContext;
  /** Creates the To Do (host wires this to `POST /communications/{id}/create-task`). */
  onCreateTask: (input: CreateTaskInput) => Promise<CreateTaskResult>;
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
  regarding: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground2,
  },
  actions: {
    display: 'flex',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalS,
  },
  successBody: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    minHeight: '240px',
    textAlign: 'center',
    color: tokens.colorNeutralForeground2,
  },
  successIcon: {
    fontSize: '40px',
    color: tokens.colorStatusSuccessForeground1,
  },
});

export const CreateTodoView: React.FC<CreateTodoViewProps> = ({
  hostAdapter,
  savedContext,
  onCreateTask,
  onGoToSave,
}) => {
  const styles = useStyles();
  const [title, setTitle] = useState('');
  const [dueDate, setDueDate] = useState('');
  const [status, setStatus] = useState<FlowStatus>('idle');
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  // Prefill the title from the email subject (best-effort).
  useEffect(() => {
    let active = true;
    void (async () => {
      try {
        const subject = await hostAdapter.getSubject();
        if (active && subject) {
          setTitle(subject);
        }
      } catch {
        /* subject is optional — leave the field empty */
      }
    })();
    return () => {
      active = false;
    };
  }, [hostAdapter]);

  const isFiled = savedContext !== undefined;
  const canCreate = isFiled && title.trim().length > 0 && status !== 'creating';

  const handleCreate = async (): Promise<void> => {
    setStatus('creating');
    setErrorMsg(null);
    try {
      const input: CreateTaskInput = { subject: title.trim() };
      if (dueDate) {
        input.dueDate = dueDate;
      }
      const result = await onCreateTask(input);
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

  const reset = (): void => {
    setStatus('idle');
    setErrorMsg(null);
  };

  // Success screen — create-another / done.
  if (status === 'created') {
    return (
      <div className={styles.container} role="region" aria-label="Create To Do">
        <div className={styles.successBody}>
          <CheckmarkCircleRegular className={styles.successIcon} aria-hidden="true" />
          <Text size={500} weight="semibold">
            To Do created
          </Text>
          <Text>
            Linked to <strong>{savedContext?.regardingName ?? savedContext?.regardingEntity}</strong>.
          </Text>
          <Button appearance="primary" icon={<TaskListAddRegular />} onClick={reset}>
            Create another
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.container} role="region" aria-label="Create To Do">
      <div className={styles.header}>
        <TaskListAddRegular aria-hidden="true" />
        <Text size={500} weight="semibold">
          Create a To Do
        </Text>
      </div>

      {isFiled ? (
        <Text className={styles.regarding} size={200}>
          <DocumentRegular aria-hidden="true" /> Linked to&nbsp;
          <strong>{savedContext?.regardingName ?? savedContext?.regardingEntity}</strong>
        </Text>
      ) : (
        <MessageBar intent="warning" role="status">
          <MessageBarBody>
            <MessageBarTitle>File this email first</MessageBarTitle>A To Do is linked to the record you file the email
            to. Save it on the <strong>Save</strong> tab, then come back here.
          </MessageBarBody>
        </MessageBar>
      )}

      <Field label="Title" required>
        <Input
          value={title}
          onChange={(_, d) => setTitle(d.value)}
          placeholder="What needs to be done?"
          disabled={!isFiled || status === 'creating'}
        />
      </Field>

      <Field label="Due date" hint="Optional">
        <Input
          type="date"
          value={dueDate}
          onChange={(_, d) => setDueDate(d.value)}
          disabled={!isFiled || status === 'creating'}
        />
      </Field>

      {status === 'error' && errorMsg && (
        <MessageBar intent="error" role="alert">
          <MessageBarBody>{errorMsg}</MessageBarBody>
        </MessageBar>
      )}

      <div className={styles.actions}>
        {!isFiled && onGoToSave ? (
          <Button appearance="primary" onClick={onGoToSave}>
            Go to Save
          </Button>
        ) : (
          <Button
            appearance="primary"
            icon={status === 'creating' ? <Spinner size="tiny" /> : <TaskListAddRegular />}
            onClick={() => void handleCreate()}
            disabled={!canCreate}
          >
            {status === 'creating' ? 'Creating…' : 'Create To Do'}
          </Button>
        )}
      </div>
    </div>
  );
};
