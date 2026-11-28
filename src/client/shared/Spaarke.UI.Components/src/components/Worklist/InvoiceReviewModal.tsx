/**
 * InvoiceReviewModal - the eBilling Invoice Review page (`sprk_invoicereviewpage`) in-app on the shared modal shell, with the standard
 * "N of M" previous / next browse in the header (task 195, ADR-050 launch rule).
 *
 * The Console's Worklist used to open the page with `Xrm.Navigation.navigateTo` (target 2): that dialog's title bar, expand and close are
 * platform chrome (light-only, not themeable) and it has no record browse. A Spaarke React surface opens its modals in-app on the
 * `SprkModal` family instead, so this composes `SprkModal` DIRECTLY (the `FindSimilarViewerDialog` iframe-in-`SprkModal` composition):
 * size `xl`, unpadded so the iframe fills the body edge to edge, and the shell's own `nav` prop for the browse group. It is not
 * `BrowseModal`: that preset is fixed at `lg` with a metadata grid and a Close footer, none of which a full review page can use.
 *
 * Browse. `targets` is the browse set, one invoice per entry, captured by the caller when the modal opens (a lane reload while it is
 * open does not reshuffle it). Previous / next mount a FRESH iframe on the neighbouring invoice (`key` = its URL: a new frame's first load adds no browser history entry, where changing one frame's `src` would add one per move and make Back disagree with the header). At either end the matching button is
 * disabled, and a one-invoice set shows "1 of 1" with both disabled (the shell derives both from `index` / `total`).
 *
 * Unsaved work. The page may hold unsaved work (a note being typed, an open decision). Before the frame moves, `nav.onBeforeNavigate`
 * runs the cross-frame dirty-check protocol (`queryDirtyState`, `RecordNavigationModalShell`'s own round trip) against the iframe:
 * only an answer from the iframe's window, from this window's origin, carrying the request's correlationId counts; no answer in 1000 ms
 * is clean. A dirty frame raises the shared `ConfirmModal`: Cancel keeps the record, confirm moves. If the frame is on another origin
 * the page cannot answer (the answer's origin would not match) and the move is treated as clean, as the protocol says.
 *
 * The component never touches Xrm (ADR-012): the HOST supplies `clientUrl`. Closing is the shell's: the host reads its lists again in
 * `onClose`. Dismiss is `explicit` (the ×, not the backdrop or Escape) because the frame may hold unsaved work.
 *
 * Fluent v9 tokens only (ADR-021); no React 18-only API (ADR-022).
 */

import * as React from 'react';
import { makeStyles } from '@fluentui/react-components';
import { SprkModal } from '../SprkModal';
import { ConfirmModal } from '../SprkModal/presets/ConfirmModal';
import { queryDirtyState } from '../RecordNavigationModalShell/queryDirtyState';
import { buildInvoiceReviewUrl } from './openInvoiceReview';
import type { InvoiceReviewTarget } from './types';

export interface InvoiceReviewModalProps {
  /** Whether the modal is open. */
  open: boolean;
  /** Called when the modal is closed (the ×). The host reads its lists again here. */
  onClose: () => void;
  /** The org's client URL, e.g. `https://org.crm.dynamics.com`. Supplied by the host (ADR-012). */
  clientUrl: string;
  /** The browse set: one target per invoice, in display order. Captured by the caller when the modal opens. */
  targets: readonly InvoiceReviewTarget[];
  /** Index in `targets` the modal opens on. Default 0. */
  startIndex?: number;
  /** The `--sprk-ui-scale` factor forwarded to the shell. */
  uiScale?: number;
  /**
   * The theme the host resolved (`dark` / `light`), passed to the page as `theme=` beside `data`. The page is two frames below the
   * model-driven app, so its own navbar check (one parent frame) cannot see the app theme; a stored explicit preference still wins there.
   */
  theme?: 'dark' | 'light';
}

/** What the person is asked when the frame holds unsaved work. */
const DISCARD_TITLE = "Discard what you haven't saved?";
const DISCARD_MESSAGE =
  "This invoice has changes you haven't saved. If you move to another invoice, those changes will be lost.";

const useStyles = makeStyles({
  // Fills the shell body, so the absolutely positioned iframe fills it edge to edge (FindSimilarViewerDialog's composition).
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

/** The origin of the frame's URL, to pin the outbound request to; `*` when the URL cannot be parsed. */
function originOf(url: string): string {
  try {
    return new URL(url, window.location.href).origin;
  } catch {
    return '*';
  }
}

export const InvoiceReviewModal: React.FC<InvoiceReviewModalProps> = ({
  open,
  onClose,
  clientUrl,
  targets,
  startIndex = 0,
  uiScale,
  theme,
}) => {
  const styles = useStyles();
  const frameRef = React.useRef<HTMLIFrameElement>(null);
  const total = targets.length;
  const [index, setIndex] = React.useState(() => Math.min(Math.max(startIndex, 0), Math.max(total - 1, 0)));
  const current = targets[index] as InvoiceReviewTarget | undefined;
  const url = current ? buildInvoiceReviewUrl(clientUrl, current, theme) : null;

  // The pending "discard what you haven't saved?" answer: set while the confirm is up, settled by its buttons (or by unmount).
  const [confirming, setConfirming] = React.useState(false);
  const pendingAnswer = React.useRef<((allowed: boolean) => void) | null>(null);
  const settle = React.useCallback((allowed: boolean) => {
    const answer = pendingAnswer.current;
    pendingAnswer.current = null;
    setConfirming(false);
    answer?.(allowed);
  }, []);
  React.useEffect(
    () => () => {
      // Closed with the confirm up: nothing moves.
      pendingAnswer.current?.(false);
      pendingAnswer.current = null;
    },
    []
  );
  // The host keeps this mounted after it closes (#2658), so closing must settle a pending ask too, and take the confirm down with it.
  React.useEffect(() => {
    if (!open && pendingAnswer.current) settle(false);
  }, [open, settle]);

  const urlRef = React.useRef(url);
  urlRef.current = url;
  const beforeNavigate = React.useCallback(async (): Promise<boolean> => {
    const dirty = await queryDirtyState({
      targetWindow: frameRef.current?.contentWindow,
      targetOrigin: originOf(urlRef.current ?? clientUrl),
      isOriginTrusted: origin => origin === window.location.origin,
      requireSourceMatch: true,
    });
    if (!dirty) return true;
    return new Promise<boolean>(resolve => {
      pendingAnswer.current?.(false); // a second ask supersedes the first
      pendingAnswer.current = resolve;
      setConfirming(true);
    });
  }, [clientUrl]);

  // Functional update: the shell calls this from the render the click happened in, after the (up to 1 s) dirty check.
  const onNavigate = React.useCallback(
    (dir: 'prev' | 'next') => {
      setIndex(i => Math.min(Math.max(i + (dir === 'next' ? 1 : -1), 0), Math.max(total - 1, 0)));
    },
    [total]
  );

  return (
    <>
      <SprkModal
        open={open}
        onClose={onClose}
        title="Invoice Review"
        size="xl"
        padded={false}
        dismiss="explicit"
        uiScale={uiScale}
        nav={{ index, total, onNavigate, onBeforeNavigate: beforeNavigate }}
      >
        <div className={styles.frameWrap}>
          {/* `key={url}`: each invoice gets a FRESH frame. Changing one frame's `src` adds a browser history entry per move, so Back
              would put the frame on the previous invoice while the header kept its count (and skip the dirty check). A new frame's first
              load adds no entry. */}
          {url && <iframe key={url} ref={frameRef} src={url} title="Invoice Review" className={styles.frame} />}
        </div>
      </SprkModal>
      <ConfirmModal
        open={confirming}
        onClose={() => settle(false)}
        onConfirm={() => settle(true)}
        title={DISCARD_TITLE}
        message={DISCARD_MESSAGE}
        confirmLabel="Discard and continue"
        uiScale={uiScale}
      />
    </>
  );
};

InvoiceReviewModal.displayName = 'InvoiceReviewModal';
