/**
 * CloseProjectDialog.tsx
 * Confirmation dialog for closing a Secure Project (internal users only).
 *
 * Displayed when an internal user (attorney, paralegal, admin) wants to close
 * a Secure Project — permanently revoking all external access.
 *
 * Closure consequences clearly communicated to user:
 *   - All external access records deactivated (sprk_externalrecordaccess)
 *   - All external members removed from the SPE document container
 *   - Redis participation cache invalidated for all affected contacts
 *
 * Three states:
 *   1. Confirmation — warning list + "Close Project" (danger) / "Cancel" buttons
 *   2. Closing      — Spinner with progress label
 *   3. Result       — Success summary or error MessageBar
 *
 * Dependencies are injected via props (no solution-specific imports):
 *   - authenticatedFetch: MSAL-backed fetch function
 *   - bffBaseUrl: BFF API base URL
 *
 * Constraints:
 *   - Fluent v9 only: Dialog, DialogSurface, DialogBody, DialogTitle,
 *     DialogContent, DialogActions, Button, Spinner, Text, MessageBar,
 *     MessageBarBody, makeStyles, tokens (ADR-021)
 *   - makeStyles with semantic tokens — ZERO hard-coded colours
 *   - Supports light, dark, and high-contrast modes (ADR-021)
 *   - Default export enables React.lazy() dynamic import
 */

import * as React from 'react';
import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  MessageBar,
  MessageBarBody,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  DismissRegular,
  LockClosedFilled,
  PersonDeleteRegular,
  StorageRegular,
  WarningFilled,
  CheckmarkCircleFilled,
  DismissCircleFilled,
} from '@fluentui/react-icons';
import { closeSecureProject, type ICloseProjectResponse } from './closureService';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICloseProjectDialogProps {
  /** Whether the dialog is open. */
  open: boolean;
  /** Dataverse project GUID. Required to call the closure endpoint. */
  projectId: string;
  /** Human-readable project name shown in the dialog title area. */
  projectName: string;
  /**
   * Optional SPE container ID. When provided, external container members
   * are also removed from SharePoint Embedded.
   */
  containerId?: string;
  /** Called when the dialog is dismissed (cancelled or completed). */
  onClose: () => void;
  /**
   * Optional callback invoked after a successful project closure.
   * Callers can use this to refresh data or navigate away.
   */
  onClosed?: (result: ICloseProjectResponse) => void;
  /** MSAL-backed authenticated fetch function for BFF API calls. */
  authenticatedFetch: typeof fetch;
  /** BFF API base URL. */
  bffBaseUrl: string;
}

// ---------------------------------------------------------------------------
// Internal state
// ---------------------------------------------------------------------------

type DialogPhase = 'confirm' | 'closing' | 'success' | 'error';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  dialogSurface: {
    maxWidth: '520px',
    width: '90vw',
  },

  // ── Title row ──────────────────────────────────────────────────────────────
  titleRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flex: '1 1 0',
  },
  titleIcon: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: '20px',
    flexShrink: 0,
  },
  titleText: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  titleSubtext: {
    color: tokens.colorNeutralForeground3,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    marginTop: tokens.spacingVerticalXXS,
  },

  // ── Content ────────────────────────────────────────────────────────────────
  contentArea: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    minHeight: '120px',
  },

  // ── Warning section ────────────────────────────────────────────────────────
  warningIntro: {
    color: tokens.colorNeutralForeground1,
  },
  consequenceList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    borderLeft: `3px solid ${tokens.colorPaletteRedBorder1}`,
  },
  consequenceItem: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
  },
  consequenceIcon: {
    color: tokens.colorPaletteRedForeground1,
    marginTop: '2px',
    flexShrink: 0,
  },
  consequenceText: {
    display: 'flex',
    flexDirection: 'column',
    gap: '2px',
  },
  consequenceItemTitle: {
    color: tokens.colorNeutralForeground1,
  },
  consequenceItemDesc: {
    color: tokens.colorNeutralForeground3,
  },

  // ── Spinner / progress ─────────────────────────────────────────────────────
  spinnerContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    paddingTop: tokens.spacingVerticalXL,
    paddingBottom: tokens.spacingVerticalXL,
    gap: tokens.spacingVerticalS,
  },

  // ── Success / error states ─────────────────────────────────────────────────
  resultContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    textAlign: 'center',
  },
  resultIconSuccess: {
    color: tokens.colorPaletteGreenForeground1,
    fontSize: '48px',
  },
  resultIconError: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: '48px',
  },
  resultTitle: {
    color: tokens.colorNeutralForeground1,
  },
  resultSubtitle: {
    color: tokens.colorNeutralForeground3,
  },

  // ── Success summary card ───────────────────────────────────────────────────
  summaryCard: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    width: '100%',
    textAlign: 'left',
  },
  summaryRow: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  summaryLabel: {
    color: tokens.colorNeutralForeground3,
  },
  summaryValue: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
});

// ---------------------------------------------------------------------------
// Consequence items shown in confirmation phase
// ---------------------------------------------------------------------------

interface IConsequenceItem {
  icon: React.ReactElement;
  title: string;
  description: string;
}

const CLOSURE_CONSEQUENCES: IConsequenceItem[] = [
  {
    icon: <PersonDeleteRegular fontSize={16} />,
    title: 'All external access revoked',
    description:
      'Every external user\u2019s participation record will be deactivated immediately. They will no longer be able to access this project.',
  },
  {
    icon: <StorageRegular fontSize={16} />,
    title: 'External members removed from document container',
    description:
      'All external users will be removed from the SharePoint Embedded container. They will lose access to project documents.',
  },
  {
    icon: <LockClosedFilled fontSize={16} />,
    title: 'Permanent — cannot be undone',
    description:
      'Project closure cannot be reversed. To re-enable external access, each user would need to be invited again.',
  },
];

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

const CloseProjectDialog: React.FC<ICloseProjectDialogProps> = ({
  open,
  projectId,
  projectName,
  containerId,
  onClose,
  onClosed,
  authenticatedFetch: authFetch,
  bffBaseUrl,
}) => {
  const styles = useStyles();

  const [phase, setPhase] = React.useState<DialogPhase>('confirm');
  const [errorMessage, setErrorMessage] = React.useState<string | undefined>(undefined);
  const [closureResult, setClosureResult] = React.useState<ICloseProjectResponse | undefined>(undefined);

  // Reset state when dialog opens
  React.useEffect(() => {
    if (open) {
      setPhase('confirm');
      setErrorMessage(undefined);
      setClosureResult(undefined);
    }
  }, [open]);

  // ── Handlers ─────────────────────────────────────────────────────────────

  const handleClose = React.useCallback(() => {
    // Only allow closing from confirm, success, or error states.
    // Prevent accidental dismissal during the API call.
    if (phase !== 'closing') {
      onClose();
    }
  }, [phase, onClose]);

  const handleConfirmClosure = React.useCallback(async () => {
    setPhase('closing');
    setErrorMessage(undefined);

    const result = await closeSecureProject(
      {
        projectId,
        containerId,
      },
      authFetch,
      bffBaseUrl,
    );

    if (result.success && result.data) {
      setClosureResult(result.data);
      setPhase('success');
      onClosed?.(result.data);
    } else {
      setErrorMessage(result.errorMessage ?? 'An unexpected error occurred during project closure.');
      setPhase('error');
    }
  }, [projectId, containerId, onClosed, authFetch, bffBaseUrl]);

  const handleRetry = React.useCallback(() => {
    setPhase('confirm');
    setErrorMessage(undefined);
  }, []);

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <Dialog
      open={open}
      onOpenChange={(_event, data) => {
        if (!data.open) {
          handleClose();
        }
      }}
    >
      <DialogSurface className={styles.dialogSurface}>
        <DialogBody>
          {/* ── Title ───────────────────────────────────────────────────── */}
          <DialogTitle
            action={
              phase !== 'closing' ? (
                <Button
                  appearance="subtle"
                  aria-label="Close dialog"
                  size="small"
                  icon={<DismissRegular aria-hidden="true" />}
                  onClick={handleClose}
                />
              ) : undefined
            }
          >
            <div className={styles.titleRow}>
              <WarningFilled
                className={styles.titleIcon}
                aria-hidden="true"
              />
              <div>
                <Text size={400} className={styles.titleText} as="span" block>
                  Close Secure Project
                </Text>
                <Text size={200} className={styles.titleSubtext} block title={projectName}>
                  {projectName}
                </Text>
              </div>
            </div>
          </DialogTitle>

          {/* ── Content ─────────────────────────────────────────────────── */}
          <DialogContent>
            <div className={styles.contentArea}>

              {/* ── Confirm phase ── */}
              {phase === 'confirm' && (
                <>
                  <Text size={300} className={styles.warningIntro}>
                    Closing this project will permanently revoke all external access. Please review the
                    consequences before proceeding.
                  </Text>

                  <div className={styles.consequenceList} role="list" aria-label="Closure consequences">
                    {CLOSURE_CONSEQUENCES.map((item) => (
                      <div key={item.title} className={styles.consequenceItem} role="listitem">
                        <span className={styles.consequenceIcon} aria-hidden="true">
                          {item.icon}
                        </span>
                        <div className={styles.consequenceText}>
                          <Text size={300} weight="semibold" className={styles.consequenceItemTitle}>
                            {item.title}
                          </Text>
                          <Text size={200} className={styles.consequenceItemDesc}>
                            {item.description}
                          </Text>
                        </div>
                      </div>
                    ))}
                  </div>

                  <MessageBar intent="warning">
                    <MessageBarBody>
                      <Text size={200}>
                        This action is{' '}
                        <Text size={200} weight="semibold">
                          irreversible
                        </Text>
                        . Confirm that you want to close this project and revoke all external access.
                      </Text>
                    </MessageBarBody>
                  </MessageBar>
                </>
              )}

              {/* ── Closing phase (spinner) ── */}
              {phase === 'closing' && (
                <div className={styles.spinnerContainer} aria-live="polite" aria-busy="true">
                  <Spinner size="large" />
                  <Text size={300} style={{ color: tokens.colorNeutralForeground3 }}>
                    Closing project and revoking all external access\u2026
                  </Text>
                  <Text size={200} style={{ color: tokens.colorNeutralForeground4 }}>
                    This may take a few seconds.
                  </Text>
                </div>
              )}

              {/* ── Success phase ── */}
              {phase === 'success' && closureResult && (
                <div className={styles.resultContainer}>
                  <CheckmarkCircleFilled
                    className={styles.resultIconSuccess}
                    aria-hidden="true"
                  />
                  <Text size={500} weight="semibold" className={styles.resultTitle}>
                    Project closed
                  </Text>
                  <Text size={300} className={styles.resultSubtitle}>
                    All external access has been revoked and the project has been closed.
                  </Text>

                  {/* Summary card */}
                  <div className={styles.summaryCard}>
                    <div className={styles.summaryRow}>
                      <Text size={200} className={styles.summaryLabel}>
                        Access records revoked
                      </Text>
                      <Text size={200} className={styles.summaryValue}>
                        {closureResult.accessRecordsRevoked}
                      </Text>
                    </div>
                    <div className={styles.summaryRow}>
                      <Text size={200} className={styles.summaryLabel}>
                        SPE container members removed
                      </Text>
                      <Text size={200} className={styles.summaryValue}>
                        {closureResult.speContainerMembersRemoved}
                      </Text>
                    </div>
                    <div className={styles.summaryRow}>
                      <Text size={200} className={styles.summaryLabel}>
                        Cache entries invalidated
                      </Text>
                      <Text size={200} className={styles.summaryValue}>
                        {closureResult.affectedContactIds.length}
                      </Text>
                    </div>
                  </div>
                </div>
              )}

              {/* ── Error phase ── */}
              {phase === 'error' && (
                <div className={styles.resultContainer}>
                  <DismissCircleFilled
                    className={styles.resultIconError}
                    aria-hidden="true"
                  />
                  <Text size={500} weight="semibold" className={styles.resultTitle}>
                    Closure failed
                  </Text>
                  {errorMessage && (
                    <MessageBar intent="error" style={{ textAlign: 'left', width: '100%' }}>
                      <MessageBarBody>
                        <Text size={200}>{errorMessage}</Text>
                      </MessageBarBody>
                    </MessageBar>
                  )}
                  <Text size={300} className={styles.resultSubtitle}>
                    The project was not closed. You can retry or contact your administrator if the
                    issue persists.
                  </Text>
                </div>
              )}
            </div>
          </DialogContent>

          {/* ── Actions ─────────────────────────────────────────────────── */}
          <DialogActions>
            {phase === 'confirm' && (
              <>
                <Button
                  appearance="primary"
                  style={{
                    backgroundColor: tokens.colorPaletteRedBackground3,
                    color: tokens.colorNeutralForegroundOnBrand,
                    borderColor: tokens.colorPaletteRedBorder2,
                  }}
                  onClick={handleConfirmClosure}
                  aria-label="Confirm and close this secure project"
                  icon={<LockClosedFilled aria-hidden="true" />}
                >
                  Close Project
                </Button>
                <Button
                  appearance="secondary"
                  onClick={handleClose}
                  aria-label="Cancel and keep the project open"
                >
                  Cancel
                </Button>
              </>
            )}

            {phase === 'closing' && (
              <Button appearance="secondary" disabled>
                Closing\u2026
              </Button>
            )}

            {phase === 'success' && (
              <Button
                appearance="primary"
                onClick={handleClose}
                aria-label="Close this dialog"
              >
                Done
              </Button>
            )}

            {phase === 'error' && (
              <>
                <Button
                  appearance="primary"
                  onClick={handleRetry}
                  aria-label="Try closing the project again"
                >
                  Try Again
                </Button>
                <Button
                  appearance="secondary"
                  onClick={handleClose}
                  aria-label="Cancel and dismiss this dialog"
                >
                  Cancel
                </Button>
              </>
            )}
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

CloseProjectDialog.displayName = 'CloseProjectDialog';

// Default export enables React.lazy() dynamic import for bundle-size optimization.
// Named export preserved for direct imports in tests.
export { CloseProjectDialog };
export default CloseProjectDialog;
