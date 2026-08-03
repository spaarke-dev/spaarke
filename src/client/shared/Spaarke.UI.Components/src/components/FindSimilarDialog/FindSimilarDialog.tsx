/**
 * FindSimilarDialog - Reusable iframe dialog for the DocumentRelationshipViewer.
 *
 * Renders a near-fullscreen dialog containing an iframe that loads the
 * DocumentRelationshipViewer Code Page web resource.
 *
 * Consumer builds the URL (since URL construction differs between PCF and
 * LegalWorkspace) and passes it in. This component just provides the dialog
 * shell with correct sizing and no scrollbars.
 *
 * Optional `embedded` prop hides the title bar chrome when the dialog is
 * rendered inside a Dataverse form (e.g., as part of a PCF control panel).
 *
 * Optional `authenticatedFetch` and `bffBaseUrl` are accepted for forward
 * compatibility with service-injected patterns but are not currently used
 * by the iframe shell itself.
 *
 * Zero hard service dependencies — fully prop-based.
 *
 * P4 re-base (spaarke-modal-system, task 061, FR-15 — supersedes the task-030
 * interim `ModalWindowControls`/local-`isMaximized` wiring):
 *   - The DEFAULT (non-embedded) path — the ONLY path any current consumer
 *     exercises (SemanticSearchControl, RelatedDocumentCount,
 *     DocumentUploadWizard's NextStepsStep, LegalWorkspace's DocumentCard —
 *     none pass `embedded`, confirmed by repo-wide grep) — now composes the
 *     shared `SprkModal` shell directly (`size="xl"`, the "near-full iframe /
 *     app host" size, design §6.2) instead of a hand-rolled `Dialog`/
 *     `DialogSurface` with a bespoke 85vw×85vh surface. `SprkModal` is
 *     composed DIRECTLY (not via a preset) because the header carries a
 *     PER-SURFACE action (the "Open in new tab" button) via the shell's
 *     `headerActions` slot — legitimate composition, not a fork
 *     (`CloseProjectDialog`/task 040 precedent). Maximize/restore and the ×
 *     are now owned entirely by the shell's internal `ModalWindowControls`;
 *     the local `isMaximized` state + `surfaceMaximized` style (task 030
 *     interim) are removed as superseded dead code. `padded={false}` — the
 *     iframe must fill the body edge to edge, matching the pre-existing
 *     zero-padding behavior.
 *   - The `embedded` path is INTENTIONALLY NOT routed through `SprkModal`:
 *     `SprkModal`'s header (title + `ModalWindowControls`) is unconditional —
 *     it has no "no header" mode (verified by reading `SprkModal.tsx`) — so
 *     wrapping this path in `SprkModal` would ALWAYS show a header, breaking
 *     the "hides the title bar chrome" contract `embedded` exists to
 *     provide. This mirrors the sibling reconciliation this same task made
 *     for the `FindSimilar/FindSimilarDialog.tsx` (WizardShell-hosted)
 *     copies, where a mandatory pre-existing envelope cannot be
 *     double-wrapped in a second shell. The embedded branch therefore keeps
 *     a minimal, header-less `Dialog`/`DialogSurface` composition, but its
 *     surface is now sized via the SAME canonical `xl` token
 *     (`getSurfaceStyle('xl')`, imported from `SprkModal/sizes`) rather than
 *     the old hand-rolled 85vw/85vh literal — satisfying "xl only, no
 *     per-copy sizes" for this branch too, without forking or extending
 *     `SprkModal` itself. `embedded` currently has zero live callers (see
 *     above), so this path's behavior is unchanged-but-unexercised in
 *     production. See `projects/spaarke-modal-system/notes/
 *     task-061-completion.md` for the full per-copy investigation.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 * @see ADR-012 for shared component library patterns
 * @see ADR-050 / docs/standards/MODAL-DESIGN-SYSTEM.md for the SprkModal shell
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Dialog,
  DialogSurface,
  DialogBody,
  Button,
  Tooltip,
} from '@fluentui/react-components';
import { ArrowExpandRegular } from '@fluentui/react-icons';

import { SprkModal, getSurfaceStyle } from '../SprkModal';
import type { IDataService } from '../../types/serviceInterfaces';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IFindSimilarDialogProps {
  /** Whether the dialog is open. */
  open: boolean;
  /** Called when the dialog requests to close (backdrop click, Escape). */
  onClose: () => void;
  /** The URL to load in the iframe. When null/undefined the iframe is not rendered. */
  url: string | null;
  /**
   * When true, hides the title bar chrome (expand / close buttons).
   * Useful when the dialog is rendered inside a Dataverse form where
   * the parent already provides navigation controls.
   * @default false
   */
  embedded?: boolean;
  /**
   * Optional authenticated fetch function for forward compatibility
   * with service-injection patterns. Not used by the iframe shell itself.
   */
  authenticatedFetch?: (url: string, init?: RequestInit) => Promise<Response>;
  /**
   * Optional BFF API base URL for forward compatibility with
   * service-injection patterns. Not used by the iframe shell itself.
   */
  bffBaseUrl?: string;
  /**
   * Optional data service for forward compatibility with service-injection
   * patterns. Not used by the iframe shell itself, but available for
   * consumers that may extend this component.
   */
  dataService?: IDataService;
  /**
   * The `--sprk-ui-scale` factor forwarded to the `SprkModal` shell (design
   * §6.9). Optional, backward-compatible; hosts thread this via `useUiScale()`
   * (task-100 gate fix — the one re-based dialog that missed the passthrough).
   */
  uiScale?: number;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // Embedded-only bare envelope (no `SprkModal` — see file docstring for
  // why). Mirrors `SprkModal`'s own `surface` class (rounded corners, flex
  // column, hidden overflow) so the embedded card still reads like the
  // standard shell even though it renders no header.
  embeddedSurface: {
    padding: 0,
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
    borderRadius: tokens.borderRadiusXLarge,
  },
  // Fills its parent's box — `SprkModal`'s own body div in the default path,
  // or this dialog's own `DialogBody` in the embedded path — so the
  // absolutely positioned iframe below fills edge to edge in either DOM
  // position. `flex: 1` applies when the parent is itself a flex item
  // (embedded path, direct child of the flex-column `DialogSurface`);
  // `width`/`height: 100%` applies when the parent is a plain block box with
  // a definite height (default path, child of `SprkModal`'s body div).
  frameWrap: {
    position: 'relative',
    flex: 1,
    minHeight: 0,
    width: '100%',
    height: '100%',
  },
  frame: {
    position: 'absolute',
    top: 0,
    left: 0,
    width: '100%',
    height: '100%',
    border: 'none',
    display: 'block',
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const FindSimilarDialog: React.FC<IFindSimilarDialogProps> = ({ open, onClose, url, embedded = false, uiScale }) => {
  const styles = useStyles();

  const handleExpand = React.useCallback(() => {
    if (url) {
      window.open(url, '_blank', 'noopener,noreferrer');
    }
  }, [url]);

  const frame = url ? <iframe src={url} title="Document Relationships" className={styles.frame} /> : null;

  // Embedded hosts (e.g. a PCF panel already inside a Dataverse form) supply
  // their own navigation chrome. `SprkModal`'s header (title + window
  // controls) is unconditional, so this path cannot route through it
  // without breaking the "hides the title bar chrome" contract — see file
  // docstring.
  if (embedded) {
    return (
      <Dialog
        open={open}
        onOpenChange={(_, data) => {
          if (!data.open) onClose();
        }}
      >
        <DialogSurface
          className={styles.embeddedSurface}
          style={getSurfaceStyle('xl')}
          aria-label="Similar Documents"
        >
          <DialogBody className={styles.frameWrap}>{frame}</DialogBody>
        </DialogSurface>
      </Dialog>
    );
  }

  return (
    <SprkModal
      open={open}
      onClose={onClose}
      title="Similar Documents"
      size="xl"
      padded={false}
      uiScale={uiScale}
      headerActions={
        <Tooltip content="Open in new tab" relationship="label">
          <Button
            appearance="subtle"
            icon={<ArrowExpandRegular />}
            size="small"
            onClick={handleExpand}
            aria-label="Open in new tab"
          />
        </Tooltip>
      }
    >
      <div className={styles.frameWrap}>{frame}</div>
    </SprkModal>
  );
};

export default FindSimilarDialog;
