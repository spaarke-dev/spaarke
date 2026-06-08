import React, { useMemo } from 'react';
import {
  Button,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { CheckmarkCircleRegular, TaskListAddRegular } from '@fluentui/react-icons';
import type { IHostAdapter } from '@shared/adapters';
import { useCreateTodoFromEmail, type SaveEmailToSpaarkeFn } from '../../hooks';
import type { CreateTodoFlowState } from '../../hooks';

/**
 * CreateTodoView — Outlook ribbon "Create To Do" entry point view.
 *
 * Per smart-todo-decoupling-r3 FR-27 (task 070):
 *
 *   When the user clicks the "Create To Do" ribbon button in the Outlook
 *   email-read context, this view renders inside the taskpane. It:
 *
 *     1. Reads the current email's `internetMessageId` + subject via the
 *        injected host adapter.
 *     2. Calls the BFF lookup to see if a `sprk_communication` already
 *        exists for this email (`useCreateTodoFromEmail` hook).
 *     3. If not saved → host invokes the existing Save flow (the
 *        `saveEmailToSpaarke` callback opens / awaits the existing SaveView).
 *     4. Once a `sprk_communicationid` is known, opens the CreateTodo
 *        wizard from the SmartTodo Code Page in a new browser window, with
 *        `initialRegarding` pre-filled per the launch-context contract
 *        (`notes/createtodo-launch-contract.md` §3).
 *
 * Why a popup window and not an inline wizard?
 *
 *   The CreateTodoWizard requires `IDataService` + `INavigationService` (Xrm
 *   Web API + Dataverse lookup dialogs) which are only available inside the
 *   Power Apps host. Mounting the wizard inside the Outlook taskpane would
 *   require either (a) shipping a Dataverse Xrm shim into the add-in (heavy)
 *   or (b) writing a new BFF-backed IDataService adapter (out of scope for
 *   task 070). The existing pattern for "open a Dataverse form from Outlook"
 *   is `window.open` (see `SaveView.tsx` Quick Create handler) — this view
 *   follows that pattern.
 *
 * Per NFR-01: Fluent UI v9 + Griffel `makeStyles` + semantic tokens only.
 * Per CLAUDE.md §16: no hardcoded org URLs; `codePageBaseUrl` is supplied via
 * env-var-derived prop.
 *
 * @see projects/smart-todo-decoupling-r3/notes/outlook-ribbon-create-todo.md
 */

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
  body: {
    color: tokens.colorNeutralForeground2,
  },
  actions: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  statusBar: {
    width: '100%',
  },
  inlineRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
});

/**
 * Props for the CreateTodoView.
 */
export interface CreateTodoViewProps {
  /**
   * Host adapter used to read the current email's metadata.
   *
   * MUST be an Outlook adapter (the view is mounted only when
   * `hostType === 'outlook'`). The adapter MUST expose
   * `getInternetMessageId()` — this is provided by `OutlookAdapter`
   * (see `shared/adapters/OutlookAdapter.ts`).
   */
  hostAdapter: IHostAdapter;
  /**
   * Caller-supplied save-flow callback. Invoked when the email isn't yet
   * saved to Spaarke. The host (App.tsx) is responsible for wiring this to
   * the existing SaveView and resolving with the new communicationId.
   *
   * When this prop is omitted (or returns null), the view surfaces a
   * "save first" error and lets the user dismiss + retry.
   */
  saveEmailToSpaarke: SaveEmailToSpaarkeFn;
  /**
   * Base URL of the SmartTodo Code Page (which hosts the wizard). Comes
   * from `SMARTTODO_CODEPAGE_URL` env var via the taskpane index entry.
   */
  codePageBaseUrl: string;
}

/**
 * Adapter for reading the current email's id + subject.
 *
 * Some IHostAdapter implementations expose `getInternetMessageId` (Outlook),
 * others don't (Word). We feature-detect and fall back to the generic
 * `getItemId` for hosts that conflate the two ids (acceptable for tests; in
 * production the Outlook adapter exposes both).
 */
function buildEmailReader(hostAdapter: IHostAdapter): {
  getInternetMessageId: () => Promise<string>;
  getSubject: () => Promise<string>;
} {
  // Duck-type for the Outlook-specific method without leaking a hard import.
  const maybeOutlook = hostAdapter as unknown as {
    getInternetMessageId?: () => Promise<string> | string;
  };
  return {
    getInternetMessageId: async () => {
      if (typeof maybeOutlook.getInternetMessageId === 'function') {
        const value = await Promise.resolve(maybeOutlook.getInternetMessageId());
        return value ?? '';
      }
      // Fallback: itemId. Sufficient for hosts where itemId IS the unique key.
      return hostAdapter.getItemId();
    },
    getSubject: () => hostAdapter.getSubject(),
  };
}

/**
 * Renders the per-state body of the view.
 */
function renderStateBody(state: CreateTodoFlowState, styles: ReturnType<typeof useStyles>): React.ReactElement {
  switch (state.kind) {
    case 'idle':
      return (
        <Text className={styles.body}>
          Create a Spaarke To Do linked to this email. If the email isn&apos;t yet saved
          to Spaarke, we&apos;ll save it first, then open the wizard.
        </Text>
      );
    case 'looking-up':
      return (
        <MessageBar className={styles.statusBar} intent="info" role="status">
          <MessageBarBody>
            <span className={styles.inlineRow}>
              <Spinner size="tiny" aria-hidden="true" /> Checking whether this email is saved to Spaarke&hellip;
            </span>
          </MessageBarBody>
        </MessageBar>
      );
    case 'saving':
      return (
        <MessageBar className={styles.statusBar} intent="info" role="status">
          <MessageBarBody>
            <MessageBarTitle>Saving email to Spaarke</MessageBarTitle>
            <span className={styles.inlineRow}>
              <Spinner size="tiny" aria-hidden="true" /> Once saved, we&apos;ll open the To Do wizard.
            </span>
          </MessageBarBody>
        </MessageBar>
      );
    case 'launching':
      return (
        <MessageBar className={styles.statusBar} intent="info" role="status">
          <MessageBarBody>
            <span className={styles.inlineRow}>
              <Spinner size="tiny" aria-hidden="true" /> Opening the To Do wizard&hellip;
            </span>
          </MessageBarBody>
        </MessageBar>
      );
    case 'opened':
      return (
        <MessageBar className={styles.statusBar} intent="success" role="status">
          <MessageBarBody>
            <MessageBarTitle>To Do wizard opened</MessageBarTitle>
            Complete the wizard in the new window to create your Spaarke To Do.
          </MessageBarBody>
        </MessageBar>
      );
    case 'error':
      return (
        <MessageBar className={styles.statusBar} intent="error" role="alert">
          <MessageBarBody>
            <MessageBarTitle>Couldn&apos;t create the To Do</MessageBarTitle>
            {state.message}
          </MessageBarBody>
        </MessageBar>
      );
    default: {
      // Exhaustiveness check — TS should catch any new `kind` added to
      // CreateTodoFlowState that isn't handled here. The `satisfies` lets
      // us assert exhaustion without binding an unused variable
      // (noUnusedLocals-friendly).
      ((_exhaustive: never): never => _exhaustive)(state);
      return <></>;
    }
  }
}

export const CreateTodoView: React.FC<CreateTodoViewProps> = ({
  hostAdapter,
  saveEmailToSpaarke,
  codePageBaseUrl,
}) => {
  const styles = useStyles();
  const emailReader = useMemo(() => buildEmailReader(hostAdapter), [hostAdapter]);

  const { state, isBusy, start, reset } = useCreateTodoFromEmail({
    emailReader,
    saveEmailToSpaarke,
    codePageBaseUrl,
  });

  return (
    <div className={styles.container} role="region" aria-label="Create To Do">
      <div className={styles.header}>
        <TaskListAddRegular aria-hidden="true" />
        <Text size={500} weight="semibold">
          Create a Spaarke To Do
        </Text>
      </div>

      {renderStateBody(state, styles)}

      <div className={styles.actions}>
        {state.kind === 'opened' ? (
          <Button
            appearance="primary"
            icon={<CheckmarkCircleRegular />}
            onClick={reset}
          >
            Done
          </Button>
        ) : state.kind === 'error' ? (
          <MessageBarActions>
            <Button appearance="primary" onClick={() => void start()}>
              Try again
            </Button>
            <Button appearance="subtle" onClick={reset}>
              Dismiss
            </Button>
          </MessageBarActions>
        ) : (
          <Button
            appearance="primary"
            icon={<TaskListAddRegular />}
            onClick={() => void start()}
            disabled={isBusy}
            size="large"
          >
            {isBusy ? 'Working…' : 'Create To Do'}
          </Button>
        )}
      </div>
    </div>
  );
};
