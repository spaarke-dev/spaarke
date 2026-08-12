/**
 * CreateRecordChooserModal.tsx
 *
 * A SHARED "create a record" chooser (email-communication-intelligence-r2 task
 * 064, E1b/E1c) — the FULL Quick Start create menu, mountable by hosts that
 * cannot import the SpaarkeAi-solution-only `QuickStartModal` (the reconciliation
 * AI-widget + the standalone `CommunicationReconciliation` code page).
 *
 * It reuses the SHARED `GetStartedCardsWidget` (the same 7-card menu QuickStartModal
 * renders) inside the shared `SprkModal`, and the SHARED `launchSurface` hand-off —
 * so the operator sees every create wizard option, and a committed record is
 * reported back to the caller. No new picker UI, no QuickStartModal import
 * (§11 — reuse the shared cards widget, do not duplicate).
 *
 * Contract: `onResult` fires EXACTLY ONCE per open — with the created record ref
 * (a record-card whose wizard committed), or `null` (wizard cancelled, dismissed,
 * or a non-record card). The caller resolves its `onLaunchCreateRecord` promise on
 * `onResult` and closes the modal. `onClose` is the visual close signal (dismiss OR
 * a card was picked) and never resolves — keeping the two concerns separate avoids
 * the double-resolve race when a card click both closes the chooser and later
 * yields an outcome.
 *
 * ADR-021 (Fluent v9 tokens via the shared shell), ADR-050 (`SprkModal`).
 */
import * as React from 'react';
import { SprkModal } from '../SprkModal';
import { launchSurface, type LaunchSurfaceOutcome } from '../../services/surfaceHandoff/launchSurface';
import { GetStartedCardsWidget, type GetStartedCardId } from './GetStartedCardsWidget';

/** A record the chooser created via a Quick Start card's wizard. */
export interface ChooserCreatedRecordRef {
  /** The created Dataverse record id (bare GUID). */
  id: string;
  /** The created record's entity logical name (e.g. `sprk_matter`). */
  entityType: string;
}

/** Session-held SPE file args pre-seeded into the launched wizard (E1c `.eml`). */
export interface ChooserFileArgs {
  readonly fileIds: string[];
  readonly source?: { readonly sessionId?: string };
  readonly provenance?: { readonly sourceFiles?: string[] };
}

/**
 * The record-creation cards whose committed record is a valid regarding target,
 * mapped to their surface-launch `consumerType` + entity logical name. Only these
 * yield a `ChooserCreatedRecordRef`; the file-only / non-record cards resolve `null`.
 * (Mirrors QuickStartModal's `RECORD_CARD_ENTITY` — kept local to avoid coupling the
 * SpaarkeAi hot-path component to this shared widget.)
 */
const CREATE_CARD: Partial<Record<GetStartedCardId, { consumerType: string; entityType: string }>> = {
  'create-matter-wizard': { consumerType: 'create-matter', entityType: 'sprk_matter' },
  'create-project-wizard': { consumerType: 'create-project', entityType: 'sprk_project' },
  'assign-work': { consumerType: 'create-work-assignment', entityType: 'sprk_workassignment' },
};

export interface CreateRecordChooserModalProps {
  /** Whether the chooser is open. */
  open: boolean;
  /** Spaarke BFF base URL (forwarded to `launchSurface`; ADR-028 — base only). */
  bffBaseUrl: string;
  /** Optional pre-seed file args (the reconciled email's `.eml` session file; E1c). */
  fileArgs?: ChooserFileArgs;
  /**
   * Fires EXACTLY ONCE per open with the created record ref, or `null` (cancelled /
   * dismissed / non-record card). The caller resolves its launcher promise here.
   */
  onResult: (ref: ChooserCreatedRecordRef | null) => void;
  /** Visual close signal (dismiss OR a card was picked). Does NOT resolve. */
  onClose: () => void;
  /** `--sprk-ui-scale` forwarded to the shell. */
  uiScale?: number;
}

/**
 * Shared full-menu create-record chooser. See file header for the `onResult` /
 * `onClose` contract.
 */
export const CreateRecordChooserModal: React.FC<CreateRecordChooserModalProps> = ({
  open,
  bffBaseUrl,
  fileArgs,
  onResult,
  onClose,
  uiScale,
}) => {
  // Guards `onResult` to exactly-once across the pick→launch→outcome sequence.
  const settledRef = React.useRef(false);
  React.useEffect(() => {
    if (open) settledRef.current = false;
  }, [open]);

  const settle = React.useCallback(
    (ref: ChooserCreatedRecordRef | null) => {
      if (settledRef.current) return;
      settledRef.current = true;
      onResult(ref);
    },
    [onResult]
  );

  const handleCardClick = React.useCallback(
    (cardId: GetStartedCardId): void => {
      const map = CREATE_CARD[cardId];
      // Close the chooser immediately (the wizard opens in its own surface).
      onClose();
      if (!map) {
        // A non-record card (summarize / find-similar / email / meeting) — the
        // reconciliation "New record" flow only creates a regarding record.
        settle(null);
        return;
      }
      const args = fileArgs && fileArgs.fileIds.length > 0 ? fileArgs : {};
      void launchSurface({ consumerType: map.consumerType, bffBaseUrl, ...args })
        .then((outcome: LaunchSurfaceOutcome) => {
          settle(
            outcome.launched && outcome.result?.committed && outcome.result.recordId
              ? { id: outcome.result.recordId, entityType: map.entityType }
              : null
          );
        })
        .catch(() => settle(null));
    },
    [bffBaseUrl, fileArgs, onClose, settle]
  );

  // Dismiss (Cancel / ×) without picking a card → resolve null.
  const handleDismiss = React.useCallback(() => {
    onClose();
    settle(null);
  }, [onClose, settle]);

  return (
    <SprkModal
      open={open}
      onClose={handleDismiss}
      title="Create a record"
      size="md"
      dismiss="explicit"
      uiScale={uiScale}
    >
      <div data-testid="create-record-chooser">
        <GetStartedCardsWidget onCardClick={handleCardClick} />
      </div>
    </SprkModal>
  );
};

CreateRecordChooserModal.displayName = 'CreateRecordChooserModal';

export default CreateRecordChooserModal;
