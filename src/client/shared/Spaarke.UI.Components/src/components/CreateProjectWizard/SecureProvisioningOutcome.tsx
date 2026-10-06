/**
 * SecureProvisioningOutcome.tsx — the provisioning-failure state of a project-creating wizard, with the action that
 * finishes securing (unified-access-control-r2 task 133, C11).
 *
 * WHY THIS EXISTS. A wizard's success screen is a static `IWizardSuccessConfig` returned once from `onFinish`, so it
 * cannot re-render after a second call. Before task 133 the wizards called provisioning once and had no retry at
 * all; the server now tells the SAME caller they may call again for several states (an undone creator-share failure,
 * an interrupted owner move, a missing container — which the next call resumes). A message promising a retry with
 * nothing to click is the FR-31 defect class (a sentence that renders and is untrue), so the advice and the button
 * live together here, keyed on the classified result's `retryable`. Rendered by `CreateProjectWizard` and
 * `SummarizeFilesDialog` — the two hosts that provision a new secure project.
 *
 * In-dialog retry precedent: `CloseProjectDialog` (`handleRetry`).
 */
import * as React from 'react';
import { Button, MessageBar, MessageBarBody, Spinner, makeStyles, tokens } from '@fluentui/react-components';

import { provisionSecureProject, type IProvisionProjectResult } from './provisioningService';

export interface ISecureProvisioningOutcomeProps {
  /** The project provisioning was called for. A retry calls it for the SAME id. */
  projectId: string;
  /** Fallback container display name, as on the first call. */
  projectRef?: string;
  /** The result of the call that did not succeed. */
  initialResult: IProvisionProjectResult;
  /** MSAL-backed fetch for the BFF. */
  authenticatedFetch: typeof fetch;
  /** BFF API base URL. */
  bffBaseUrl: string;
}

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    marginTop: tokens.spacingVerticalM,
  },
  actions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
});

export const SecureProvisioningOutcome: React.FC<ISecureProvisioningOutcomeProps> = ({
  projectId,
  projectRef,
  initialResult,
  authenticatedFetch,
  bffBaseUrl,
}) => {
  const styles = useStyles();
  const [result, setResult] = React.useState<IProvisionProjectResult>(initialResult);
  const [busy, setBusy] = React.useState(false);

  const handleRetry = React.useCallback(async () => {
    setBusy(true);
    try {
      // provisionSecureProject never throws: every outcome comes back classified.
      setResult(await provisionSecureProject({ projectId, projectRef }, authenticatedFetch, bffBaseUrl));
    } finally {
      setBusy(false);
    }
  }, [projectId, projectRef, authenticatedFetch, bffBaseUrl]);

  if (result.success) {
    return (
      <div className={styles.root}>
        <MessageBar intent="success">
          <MessageBarBody>
            The project is now secured, with its own document container, and shared with you. Anyone else who needs it
            has to be added explicitly.
          </MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  return (
    <div className={styles.root}>
      <MessageBar intent="warning">
        <MessageBarBody>
          {result.errorMessage}
          {result.retryable ? ' You can try securing it again.' : null}
        </MessageBarBody>
      </MessageBar>
      {result.retryable && (
        <div className={styles.actions}>
          <Button appearance="primary" onClick={handleRetry} disabled={busy}>
            Try securing again
          </Button>
          {busy && <Spinner size="tiny" label="Securing…" />}
        </div>
      )}
    </div>
  );
};
