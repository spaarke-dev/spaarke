import * as React from 'react';
import { Button } from '@fluentui/react-components';
import { SprkModal, type SprkModalNav } from '../SprkModal';
import { PreviewGridBody, type PreviewModalMetadataItem } from './PreviewModal';

/**
 * BrowseModal — `PreviewModal` + the `SprkModal` `nav` prop (spec FR-09; design
 * §6.1/§6.4). The header "N of M" counter + prev/next are rendered by the
 * shell's OWN nav group — the SINGLE title/counter source (design §6.4).
 *
 * This preset does NOT nest `RecordNavigationModalShell`'s `Dialog`/header
 * envelope inside `SprkModal` — that would double the chrome (two counters,
 * two titles). Instead it exposes an optional `onBeforeNavigate` guard hook:
 * the internal navigate handler calls it first and only invokes the
 * consumer's `nav.onNavigate(dir)` when it resolves truthy. A consumer wires
 * `RecordNavigationModalShell`'s cross-frame dirty-check / discard-confirm
 * protocol through this seam (e.g. running the shell's `queryDirtyState` +
 * discard-confirm UI from `onBeforeNavigate`) without rendering the shell's
 * own nav chrome — P4 (task 060) is the consumer of this seam.
 */
export interface BrowseModalProps {
  /** Whether the modal is open. */
  open: boolean;
  /** Close callback — wired to the × and the footer's primary Close. */
  onClose: () => void;
  /** Header title (ellipsized; announced). */
  title: string;
  /** The `--sprk-ui-scale` factor for sizing, forwarded to the shell. */
  uiScale?: number;
  /** Metadata rows rendered in the meta column. */
  metadata?: PreviewModalMetadataItem[];
  /**
   * Header actions rendered left of the window controls (forwarded to the
   * shell's `headerActions` slot) — mirrors `PreviewModal.headerActions`.
   */
  headerActions?: React.ReactNode;
  /** Stage content — e.g. a `RichFilePreview` renderer. Defaults to a placeholder. */
  children?: React.ReactNode;
  /** Browse ("N of M") navigation — forwarded to `SprkModal`'s header nav group. */
  nav: SprkModalNav;
  /**
   * Optional guard invoked BEFORE `nav.onNavigate`. Return (or resolve to)
   * `false` to block navigation. This is the composition seam for a
   * cross-frame dirty-check / discard-confirm (e.g. delegating to
   * `RecordNavigationModalShell`'s protocol) without nesting that shell's nav
   * chrome inside `SprkModal`.
   */
  onBeforeNavigate?: (dir: 'prev' | 'next') => boolean | Promise<boolean>;
}

export const BrowseModal: React.FC<BrowseModalProps> = ({
  open,
  onClose,
  title,
  uiScale,
  metadata = [],
  headerActions,
  children,
  nav,
  onBeforeNavigate,
}) => {
  const handleNavigate = React.useCallback(
    (dir: 'prev' | 'next') => {
      void (async () => {
        const allowed = onBeforeNavigate ? await onBeforeNavigate(dir) : true;
        if (allowed) nav.onNavigate(dir);
      })();
    },
    [nav, onBeforeNavigate]
  );

  const effectiveNav: SprkModalNav = {
    index: nav.index,
    total: nav.total,
    onNavigate: handleNavigate,
  };

  return (
    <SprkModal
      open={open}
      onClose={onClose}
      title={title}
      size="lg"
      layout="landscape"
      padded={false}
      uiScale={uiScale}
      headerActions={headerActions}
      nav={effectiveNav}
      footer={
        <Button appearance="primary" onClick={onClose}>
          Close
        </Button>
      }
    >
      <PreviewGridBody metadata={metadata}>{children}</PreviewGridBody>
    </SprkModal>
  );
};

export default BrowseModal;
