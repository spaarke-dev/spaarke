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
  launchCreateProjectWizard,
  launchAssignWorkWizard,
  launchSummarizeFilesWizard,
  launchFindSimilarWizard,
  launchPlaybookIntent,
} from '@spaarke/ui-components';
import { getBffBaseUrl } from '../../config/runtimeConfig';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface QuickStartModalProps {
  /** Whether the Quick Start modal is open. */
  open: boolean;
  /** Close request (dismiss, Escape, scrim click, or after a card launches a wizard). */
  onClose: () => void;
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
export const QuickStartModal: React.FC<QuickStartModalProps> = ({ open, onClose }) => {
  const styles = useStyles();

  const handleCardClick = React.useCallback(
    (cardId: GetStartedCardId): void => {
      const bffBaseUrl = getBffBaseUrl();

      switch (cardId) {
        case 'create-matter-wizard':
          // The one Get-Started card with a SURFACE_LAUNCH_REGISTRY entry —
          // launch through the shipped 012 hand-off envelope.
          void launchSurface({ consumerType: 'create-matter', bffBaseUrl });
          break;

        case 'create-project-wizard':
          launchCreateProjectWizard({ bffBaseUrl });
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
          launchPlaybookIntent({ bffBaseUrl, intent: 'email-compose' });
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
    [onClose],
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
