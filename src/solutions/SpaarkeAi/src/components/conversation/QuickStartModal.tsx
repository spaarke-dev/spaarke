/**
 * QuickStartModal.tsx — the Assistant "Quick Start" modal (task 041 / FR-F2).
 *
 * Hosts the existing `GetStartedCardsWidget` (the shared `@spaarke/ai-widgets`
 * 7-card wizard library, FR-18/FR-19) inside a Fluent v9 `<Dialog>` launched
 * from `AssistantToolMenu`'s "Quick Start" entry (task 040). Per CLAUDE.md §11
 * (component justification — default to reuse) this file is a THIN host:
 * no new card grid, no new launch mechanism.
 *
 * Card → launch mapping
 * ======================
 *   - `create-matter-wizard` is the ONE Get-Started card whose surface has a
 *     `SURFACE_LAUNCH_REGISTRY` entry (`'create-matter'`, task 012). It
 *     launches through the shipped hand-off envelope —
 *     `launchSurface({ consumerType: 'create-matter', bffBaseUrl })` — from
 *     `src/client/shared/Spaarke.UI.Components/src/services/surfaceHandoff/`.
 *     (The registry's other two entries, `create-task` / `create-todo`, have
 *     no corresponding Get-Started card — those consumer types are reached
 *     from the Assistant's own draft-in-chat `surface_launch` dispatch, a
 *     different entry point than this card grid.)
 *   - The remaining six cards reuse the EXACT SAME `@spaarke/ui-components`
 *     launchers (`launchCreateProjectWizard`, `launchAssignWorkWizard`,
 *     `launchSummarizeFilesWizard`, `launchFindSimilarWizard`,
 *     `launchPlaybookIntent`) that `ContextPaneController.handleGetStartedCardClick`
 *     already uses for this identical widget in the Context pane — mirrored
 *     verbatim so Quick Start behaves exactly like the shipped Get Started
 *     surface. Calling `launchSurface` for these would be a no-op (the
 *     registry has no entry for them and degrades to `launched:false`
 *     without navigating anywhere), so the working launchers are the correct
 *     reuse target for those six, not a parallel mechanism.
 *
 * Modal choice (per docs/standards/MODAL-DECISION-CRITERIA.md): Family 2 — a
 * plain Fluent v9 `<Dialog>`. This is a picker/launcher surface (select one of
 * 7 cards, then close), not a record browse, so Family 3 / `RecordNavigationModalShell`
 * chrome does not apply.
 *
 * @see GetStartedCardsWidget — the reused 7-card grid (@spaarke/ai-widgets)
 * @see surfaceHandoff/launchSurface — the 012 hand-off envelope
 * @see ContextPaneController.tsx — the sibling card-click handler this mirrors
 * @see AssistantToolMenu.tsx — the launcher ("Quick Start" menu entry)
 * @see ADR-021 — Fluent v9 semantic tokens only; dark mode adapts automatically
 */

import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { GetStartedCardsWidget } from '@spaarke/ai-widgets';
import type { GetStartedCardId } from '@spaarke/ai-widgets';
import {
  launchSurface,
  launchAssignWorkWizard,
  launchSummarizeFilesWizard,
  launchFindSimilarWizard,
  launchPlaybookIntent,
} from '@spaarke/ui-components';
import { getBffBaseUrl } from '../../config/runtimeConfig';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/**
 * The current chat session's uploaded-file context (UAT R4-12). Read at card-click time so a
 * wizard launched from Quick Start opens PRE-SEEDED with the files the user has attached — parity
 * with the draft-in-chat surface-launch path (`ConversationPane.handleSurfaceLaunch`). Carried BY
 * REFERENCE only (session id + session file ids + display names); the wizard fetches the binary via
 * `GET /api/ai/chat/sessions/{sessionId}/documents/{fileId}/content` (never inline binary — the
 * surface-handoff envelope invariant).
 */
export interface QuickStartFileContext {
  sessionId: string | null;
  fileIds: string[];
  fileNames: string[];
}

export interface QuickStartModalProps {
  /** Whether the Quick Start modal is open. */
  open: boolean;
  /** Close request (dismiss, Escape, scrim click, or after a card launches a wizard). */
  onClose: () => void;
  /**
   * UAT R4-12: returns the session's attached-file context at click time (or null when none).
   * Threaded into `launchSurface` for the envelope-based wizards (create-matter / create-project)
   * so they open with the files pre-attached. Omitted → wizards open with no seeded files (as before).
   */
  getFileContext?: () => QuickStartFileContext | null;
  /**
   * UAT R5-9: open the shared Email Compose modal for the "Send Email" card instead of the
   * playbook-library web resource. Omitted → falls back to the legacy launchPlaybookIntent route.
   */
  onSendEmail?: () => void;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  content: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: '480px',
    maxWidth: '720px',
    minHeight: '320px',
    maxHeight: '70vh',
    padding: 0,
  },
  title: {
    color: tokens.colorNeutralForeground1,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * `QuickStartModal` — Fluent v9 Dialog hosting `GetStartedCardsWidget`.
 * Launched from `AssistantToolMenu`'s "Quick Start" entry.
 */
export const QuickStartModal: React.FC<QuickStartModalProps> = ({ open, onClose, getFileContext, onSendEmail }) => {
  const styles = useStyles();

  const handleCardClick = React.useCallback(
    (cardId: GetStartedCardId): void => {
      const bffBaseUrl = getBffBaseUrl();

      // UAT R4-12: the session's attached files, carried by reference into the envelope-based
      // wizards so they open pre-seeded. Null → no files (as before). See handleSurfaceLaunch parity.
      const fileCtx = getFileContext?.() ?? null;
      const surfaceFileArgs =
        fileCtx && fileCtx.fileIds.length > 0
          ? {
              fileIds: fileCtx.fileIds,
              source: fileCtx.sessionId ? { sessionId: fileCtx.sessionId } : undefined,
              provenance: fileCtx.fileNames.length > 0 ? { sourceFiles: fileCtx.fileNames } : undefined,
            }
          : {};

      switch (cardId) {
        case 'create-matter-wizard':
          // The one Get-Started card with a SURFACE_LAUNCH_REGISTRY entry —
          // launch through the shipped 012 hand-off envelope, now carrying the
          // session's attached files (R4-12) so the wizard opens pre-attached.
          void launchSurface({ consumerType: 'create-matter', bffBaseUrl, ...surfaceFileArgs });
          break;

        case 'create-project-wizard':
          // R4-12: create-project also reads the handoff envelope (CreateProjectWizard main.tsx
          // wires initialFileRefs) and has a SURFACE_LAUNCH_REGISTRY entry — route it through the
          // SAME launchSurface envelope (not the file-less Path-B launcher) so files flow in too.
          void launchSurface({ consumerType: 'create-project', bffBaseUrl, ...surfaceFileArgs });
          break;

        case 'assign-work':
          launchAssignWorkWizard({ bffBaseUrl });
          break;

        case 'document-upload-wizard':
          // GetStartedCardsWidget labels this "Summarize Files".
          launchSummarizeFilesWizard({ bffBaseUrl });
          break;

        case 'find-similar-wizard':
          launchFindSimilarWizard({ bffBaseUrl });
          break;

        case 'email-compose':
          // R5-9: open the shared Email Compose modal when the host provides the handler;
          // otherwise fall back to the legacy playbook-library intent route.
          if (onSendEmail) {
            onSendEmail();
          } else {
            launchPlaybookIntent({ bffBaseUrl, intent: 'email-compose' });
          }
          break;

        case 'meeting-schedule':
          launchPlaybookIntent({ bffBaseUrl, intent: 'meeting-schedule' });
          break;

        default: {
          // Exhaustiveness check — TypeScript flags this if a new
          // GetStartedCardId is added without a matching case.
          const _exhaustive: never = cardId;
          void _exhaustive;
          break;
        }
      }

      // Every card click launches a wizard/dialog of its own — close Quick
      // Start so the two modals don't stack (MODAL-DECISION-CRITERIA anti-
      // pattern #5: don't nest modals from different chrome families).
      onClose();
    },
    [onClose, getFileContext, onSendEmail],
  );

  return (
    <Dialog
      open={open}
      onOpenChange={(_ev, data) => {
        if (!data.open) onClose();
      }}
      modalType="modal"
    >
      <DialogSurface data-testid="quick-start-modal">
        <DialogBody>
          <DialogTitle className={styles.title}>Quick Start</DialogTitle>
          <DialogContent className={styles.content}>
            <GetStartedCardsWidget onCardClick={handleCardClick} />
          </DialogContent>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

QuickStartModal.displayName = 'QuickStartModal';

export default QuickStartModal;
