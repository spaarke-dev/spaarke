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
 * Four phases, each rendered as `SprkModal` header/footer/body content:
 *   1. Confirm  — warning list + "Close Project" (danger) / "Cancel" buttons
 *   2. Closing  — Spinner with progress label
 *   3. Success  — Success summary
 *   4. Error    — Error MessageBar + "Try Again" / "Cancel" buttons
 *
 * Dependencies are injected via props (no solution-specific imports):
 *   - authenticatedFetch: MSAL-backed fetch function
 *   - bffBaseUrl: BFF API base URL
 *
 * Constraints:
 *   - Fluent v9 only: Button, MessageBar, MessageBarBody, Spinner, Text,
 *     makeStyles, tokens (ADR-021)
 *   - makeStyles with semantic tokens — ZERO hard-coded colours
 *   - Supports light, dark, and high-contrast modes (ADR-021)
 *   - Default export enables React.lazy() dynamic import
 *
 * P2 re-base (spaarke-modal-system, task 040 — supersedes the task-030 interim
 * `ModalWindowControls`/local-`isMaximized` wiring):
 *   - Envelope is now the shared `SprkModal` shell (`size="sm"`, `dismiss="alert"`,
 *     `maximizable={false}`) — the size design §6.2 names for this dialog
 *     ("simple form / single choice", 560px cap, close to the original
 *     520px/90vw). `SprkModal` is composed DIRECTLY (rather than the literal
 *     `<ConfirmModal>` wrapper) because the footer is PHASE-DEPENDENT (0–2
 *     buttons with different labels/handlers across confirm/closing/success/
 *     error), which `ConfirmModalProps`'s fixed Cancel+Confirm shape cannot
 *     express. Composing the footer via `SprkModal`'s public
 *     `footerStart`/`footer` slots per phase is legitimate (NOT forking) —
 *     `ConfirmModal` itself is only a thin config of the same shell, and both
 *     `SprkModal`/`ConfirmModal` are unmodified by this change. Cancel (when
 *     present) now renders in `footerStart` (left), matching the "Cancel is
 *     ALWAYS left-aligned" standard footer contract (design §6.5) — this
 *     swaps the confirm/error phases' prior left-to-right order (danger/retry
 *     action first, Cancel second) to Cancel-left/action-right, which is the
 *     INTENDED effect of standardizing onto the shared footer contract, not
 *     an incidental behavior change.
 *   - The destructive "Close Project" primary now gets its danger styling from
 *     the preset's exported `useDangerButtonClassName` (single source shared
 *     with `ConfirmModal`; P2 consolidation) instead of the inline
 *     `style={{ backgroundColor, color, borderColor }}` override — the exact
 *     anti-pattern called out in design §3.3 / §6.5 and removed here.
 *   - The custom title row (warning icon + two-line title/subtitle) is folded
 *     into a single string title (`Close Secure Project — {projectName}`) —
 *     `SprkModal`'s `title` is a plain string with no icon/subtitle slot, the
 *     standard "one header contract" every re-based dialog now shares (design
 *     §6.4). The warning intent is unaffected: it is still fully conveyed by
 *     the in-body warning `MessageBar` and the consequence list (unchanged).
 *   - Maximize/restore is retired (the `ConfirmModal` contract is
 *     non-maximizable) — the local `isMaximized` state + `dialogSurface`/
 *     `dialogSurfaceMaximized` styles from the task-030 interim wiring are
 *     removed as dead code. The shell's own `ModalWindowControls` (×) is
 *     always present per the header standard, routed to the same
 *     phase-guarded `handleClose` as before — clicking it during the
 *     "closing" phase remains a deliberate no-op (unchanged guard logic).
 */

import * as React from 'react';
import { Button, MessageBar, MessageBarBody, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import {
  LockClosedFilled,
  PersonDeleteRegular,
  StorageRegular,
  CheckmarkCircleFilled,
  DismissCircleFilled,
} from '@fluentui/react-icons';
import { SprkModal, useDangerButtonClassName } from '../SprkModal';
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
  /**
   * The `--sprk-ui-scale` factor forwarded to the `SprkModal` shell (design
   * §6.9). Optional, backward-compatible; hosts thread this via `useUiScale()`
   * (spaarke-modal-system task 040 low-cost passthrough).
   */
  uiScale?: number;
}

// ---------------------------------------------------------------------------
// Internal state
// ---------------------------------------------------------------------------

type DialogPhase = 'confirm' | 'closing' | 'success' | 'error';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // Destructive primary styling comes from the preset's exported
  // `useDangerButtonClassName` (P2 consolidation) — a single source shared with
  // `ConfirmModal`, replacing the verbatim copy of its recipe (and, before
  // that, the removed inline `style={{ backgroundColor, ... }}` anti-pattern
  // called out in design §3.3 / §6.5).

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
  // task-100 gate fix: former inline `style={{ color: tokens.* }}` on the
  // closing-phase labels moved into token classes (ADR-050 zero-inline-color).
  spinnerLabel: {
    color: tokens.colorNeutralForeground3,
  },
  spinnerSubLabel: {
    color: tokens.colorNeutralForeground4,
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
      'Every external user’s participation record will be deactivated immediately. They will no longer be able to access this project.',
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
  uiScale,
}) => {
  const styles = useStyles();
  const dangerClassName = useDangerButtonClassName();

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
      bffBaseUrl
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
    <SprkModal
      open={open}
      onClose={handleClose}
      title={`Close Secure Project — ${projectName}`}
      size="sm"
      dismiss="alert"
      maximizable={false}
      uiScale={uiScale}
      footerStart={
        phase === 'confirm' ? (
          <Button appearance="secondary" onClick={handleClose} aria-label="Cancel and keep the project open">
            Cancel
          </Button>
        ) : phase === 'error' ? (
          <Button appearance="secondary" onClick={handleClose} aria-label="Cancel and dismiss this dialog">
            Cancel
          </Button>
        ) : undefined
      }
      footer={
        phase === 'confirm' ? (
          <Button
            appearance="primary"
            className={dangerClassName}
            onClick={handleConfirmClosure}
            aria-label="Confirm and close this secure project"
            icon={<LockClosedFilled aria-hidden="true" />}
          >
            Close Project
          </Button>
        ) : phase === 'closing' ? (
          <Button appearance="secondary" disabled>
            Closing…
          </Button>
        ) : phase === 'success' ? (
          <Button appearance="primary" onClick={handleClose} aria-label="Close this dialog">
            Done
          </Button>
        ) : (
          <Button appearance="primary" onClick={handleRetry} aria-label="Try closing the project again">
            Try Again
          </Button>
        )
      }
    >
      <div className={styles.contentArea}>
        {/* ── Confirm phase ── */}
        {phase === 'confirm' && (
          <>
            <Text size={300} className={styles.warningIntro}>
              Closing this project will permanently revoke all external access. Please review the consequences before
              proceeding.
            </Text>

            <div className={styles.consequenceList} role="list" aria-label="Closure consequences">
              {CLOSURE_CONSEQUENCES.map(item => (
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
            <Text size={300} className={styles.spinnerLabel}>
              Closing project and revoking all external access…
            </Text>
            <Text size={200} className={styles.spinnerSubLabel}>
              This may take a few seconds.
            </Text>
          </div>
        )}

        {/* ── Success phase ── */}
        {phase === 'success' && closureResult && (
          <div className={styles.resultContainer}>
            <CheckmarkCircleFilled className={styles.resultIconSuccess} aria-hidden="true" />
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
            <DismissCircleFilled className={styles.resultIconError} aria-hidden="true" />
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
              The project was not closed. You can retry or contact your administrator if the issue persists.
            </Text>
          </div>
        )}
      </div>
    </SprkModal>
  );
};

CloseProjectDialog.displayName = 'CloseProjectDialog';

// Default export enables React.lazy() dynamic import for bundle-size optimization.
// Named export preserved for direct imports in tests.
export { CloseProjectDialog };
export default CloseProjectDialog;
