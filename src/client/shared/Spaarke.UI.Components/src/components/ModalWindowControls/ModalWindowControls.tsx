/**
 * ModalWindowControls — the standard Spaarke modal window-controls cluster:
 * a maximize/restore toggle and a close (×), right-aligned in a dialog header
 * (owner UAT 2026-07-31 item 4 — "we are standardizing to use the expand/collapse
 * buttons and 'x' on all modals; these should be in the shared UI components").
 *
 * Context-agnostic (ADR-012): the host owns surface sizing (`isMaximized` +
 * `onToggleMaximize`) and lifecycle (`onClose`); this component only renders the
 * two affordances and calls back. Either handler may be omitted — a modal that
 * cannot be maximized passes only `onClose`, and vice-versa; when both are absent
 * the cluster renders nothing. Fluent v9 semantic tokens only (ADR-021).
 *
 * Glyph (owner UAT 2026-08-03, supersedes the 2026-07-31 pick): the
 * maximize/restore toggle uses `ExpandUpRight`/`ContractDownLeft` — the glyph
 * the MDA `navigateTo` dialog header actually renders (box with the arrow
 * escaping top-right; empirically verified against the live OOB dialog — the
 * earlier `FullScreenMaximize` four-corners pick did NOT match it). One expand
 * vocabulary across both modal families, even though the scopes differ:
 * OOB expands to the whole app surface, ours fills the Spaarke container (an
 * iframe cannot escape its boundary).
 */
import * as React from 'react';
import { Button, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { ExpandUpRight20Regular, ContractDownLeft20Regular, Dismiss20Regular } from '@fluentui/react-icons';

export interface IModalWindowControlsProps {
  /** Current maximized state — flips the toggle icon + label between maximize and restore. */
  isMaximized?: boolean;
  /** Toggle maximize/restore. Omit → no maximize button (a non-resizable modal). */
  onToggleMaximize?: () => void;
  /** Close the modal. Omit → no close button. */
  onClose?: () => void;
  /** Optional extra class on the cluster wrapper. */
  className?: string;
}

const useStyles = makeStyles({
  cluster: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
    flexShrink: 0,
  },
});

/**
 * The standard maximize/restore + close cluster for Spaarke modals. Render it in
 * the upper-right of a dialog header (e.g. via `justify-content: space-between`
 * with the title on the left).
 */
export const ModalWindowControls: React.FC<IModalWindowControlsProps> = ({
  isMaximized,
  onToggleMaximize,
  onClose,
  className,
}) => {
  const styles = useStyles();
  if (!onToggleMaximize && !onClose) return null;
  return (
    <div className={mergeClasses(styles.cluster, className)}>
      {onToggleMaximize && (
        <Button
          appearance="subtle"
          size="small"
          icon={isMaximized ? <ContractDownLeft20Regular /> : <ExpandUpRight20Regular />}
          aria-label={isMaximized ? 'Restore dialog size' : 'Maximize dialog'}
          onClick={() => onToggleMaximize()}
        />
      )}
      {onClose && (
        <Button
          appearance="subtle"
          size="small"
          icon={<Dismiss20Regular />}
          aria-label="Close"
          onClick={() => onClose()}
        />
      )}
    </div>
  );
};

export default ModalWindowControls;
