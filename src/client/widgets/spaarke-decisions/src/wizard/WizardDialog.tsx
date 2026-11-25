/**
 * The decision wizard inside the widget (spaarkeai-mcp-bff-api-r1 task 032, FR-13; design section 9.4, O-5).
 *
 * It mounts the shared `DecisionWizardHost` (the same host the Console mounts once) and launches it for the item the lane raised, over a
 * `ResponseFetchFn` that turns the wizard's three routes into tool calls (`wizardFetch.ts`), an item loader (`wizardItems.ts`) and an inline
 * type-ahead (`lookupTypeAhead.ts`). Everything else is the shared wizard unchanged: the steps, the draft, the discard check, the Confirm
 * and the way a refusal or a failed commit is worded (`decisionDisplay`).
 *
 * Where it shows (O-5; owner decision 2026-11-24, `projects/spaarkeai-mcp-bff-api-r1/notes/decisions.md`), in order:
 *  1. An in-frame dialog, inline in the conversation over the lane, OPEN MAXIMIZED so it fills the widget frame (owner decision
 *     2026-11-24, after a live Copilot test found the wizard-size dialog cramped; the shell's restore control returns it to the wizard
 *     size): the shared modal shell (`SprkModal` through `WizardShell`, ADR-050),
 *     not a host modal (`requestModal` is not supported in Copilot). The widget does NOT ask the host for a display mode: Copilot's
 *     "fullscreen" is a side pane with the dialog over a dimmed lane, which the owner does not want. The dialog opens in whatever mode the
 *     frame is already in (a frame the host or the user put in fullscreen keeps it), and while it is open the widget root has a minimum
 *     height (`WIZARD_ROOM_HEIGHT`) so a short lane cannot leave an inline frame too small for it. The user browses Work Items with the
 *     wizard's own previous / next, and X, Cancel or Escape closes it back to the lane cards.
 *  2. Master-detail: the lane stays as the master and the wizard is shown beside it (below it in a narrow frame), embedded in the page
 *     with no modal envelope. Used only when the dialog threw while rendering or the launch reported it was not opened.
 *  3. A link to the Console, opened through the host at once (`openLink`), and kept on screen in case the host does not open it: the
 *     same Work Item is in the user's Decide lane there. Used when master-detail cannot be shown either (the frame is narrower than
 *     `MIN_DETAIL_WIDTH`, or it threw). The Console has no link to one item, so the link is to the Console page itself.
 * The wizard closing, after the user finished, discarded or pressed Escape, reads the lane again in place (a decision may have been
 * recorded, and an item may have left the lane) and puts the focus back on the row the user opened it from. The display mode is never
 * changed, so closing has none to restore.
 *
 * Nothing here records a decision: `spaarke_complete_decision` is reached only by the wizard's own Record decision button. Opening,
 * moving between steps and closing call no write tool.
 */
import * as React from 'react';
import {
  Button,
  Link,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { CONFIRM_GUARD_MS } from '@spaarke/ui-components/components/DecisionWizard/DecisionItemWizard';
import { DecisionWizardHost } from '@spaarke/ui-components/components/DecisionWizard/DecisionWizardHost';
import { launchDecisionWizard } from '@spaarke/ui-components/components/DecisionWizard/decisionWizardLauncher';
import type { WorklistData } from '@spaarke/ui-components/components/Worklist/worklistService';
import { buildConsoleLinkOut, buildRecordLinkOut } from '../lane/billingLinkOut';
import { WIZARD_OPEN_NOTICE, type OpenWizardEvent } from '../lane/DecideLane';
import { useMcpHost } from '../mcp/mcpHostContext';
import { createLookupTypeAhead } from './lookupTypeAhead';
import { createWizardBridge } from './wizardFetch';
import { createWizardItemLoader, findDecideGroup, ITEM_GONE_NOTICE } from './wizardItems';

/** A request to open the wizard: the lane's event, numbered so that the same item opened twice is two requests. */
export interface WizardRequest {
  event: OpenWizardEvent;
  seq: number;
}

export interface WizardDialogProps {
  /** The latest request from the lane; each new `seq` opens the wizard (or says one is already open). */
  request: WizardRequest | null;
  /** The lane as last read: the wizard opens on the lane as the user saw it. */
  getLane: () => WorklistData | null;
  /** `consoleBaseUrl` of `spaarke_show_decisions`, for the last fallback's link. */
  consoleBaseUrl?: string | null;
  /** A decision was recorded: the Work Item has left the lane, which the host reads again. */
  onRecorded: () => void;
  /**
   * The lane should be read again, in place: the wizard closed (whatever happened in it, a Work Item may have left the lane), or the Work
   * Item asked for is no longer in it. The host numbers the read (`reloadToken`).
   */
  onReloadLane?: () => void;
  /** How many times the lane has been read so far: a read that lands after the wizard closed may take the row the focus returns to. */
  laneReads?: number;
  /** True while a decision is open (from the moment a request is taken until the wizard closes), false after: the lane asks it before opening another. */
  onActiveChange?: (active: boolean) => void;
  /** The master: the Decide lane. It stays mounted, and in place, whatever the wizard shows. */
  children: React.ReactNode;
}

/** A frame narrower than this cannot hold master-detail even stacked: the Console link is used (when the dialog fails). */
export const MIN_DETAIL_WIDTH = 480;
/**
 * A frame narrower than this cannot hold the lane (320 px) and the decision (420 px) side by side, so they stack. The decision is then put
 * BEFORE the lane: below it, on a lane of dozens of rows, it would be out of sight, with nothing to say it had opened.
 */
export const SIDE_BY_SIDE_MIN_WIDTH = 790;

type Place = 'idle' | 'dialog' | 'detail' | 'link';

/**
 * The height the shared wizard dialog needs. An inline frame is sized from its content (at most about 640 px), so while the dialog is
 * open the widget root is at least this tall: a short lane (one or two cards) cannot leave the frame smaller than the dialog. The same
 * figure as the master-detail section's fixed height.
 */
const WIZARD_ROOM_HEIGHT = '640px';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalM, minWidth: 0 },
  rootDialog: { minHeight: WIZARD_ROOM_HEIGHT },
  split: { display: 'flex', flexWrap: 'wrap', alignItems: 'flex-start', gap: tokens.spacingHorizontalL, minWidth: 0 },
  splitStacked: { flexDirection: 'column', alignItems: 'stretch', flexWrap: 'nowrap' },
  master: { flex: '1 1 320px', minWidth: 0, outlineStyle: 'none' },
  masterStacked: { flex: '0 0 auto' },
  detail: {
    flex: '2 1 420px',
    minWidth: 0,
    // A fixed height: the embedded shell fills its container, and a frame sized from its content must not size itself from the viewport.
    height: WIZARD_ROOM_HEIGHT,
    display: 'flex',
    flexDirection: 'column',
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    overflow: 'hidden',
    // The section takes the focus when the decision opens (so a screen reader says "Decision" and the page scrolls to it), not a tab stop.
    outlineStyle: 'none',
  },
  detailStacked: { flex: '0 0 auto' },
});

/** Reports a render error to the display chain and renders nothing in the wizard's place. */
class WizardBoundary extends React.Component<{ onError: () => void; children: React.ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() {
    return { failed: true };
  }
  componentDidCatch() {
    this.props.onError();
  }
  render() {
    return this.state.failed ? null : this.props.children;
  }
}

export function WizardDialog({
  request,
  getLane,
  consoleBaseUrl,
  onRecorded,
  onReloadLane,
  laneReads = 0,
  onActiveChange,
  children,
}: WizardDialogProps) {
  const styles = useStyles();
  const { callTool, openLink } = useMcpHost();

  const bridge = React.useMemo(() => createWizardBridge(callTool), [callTool]);
  const typeAhead = React.useMemo(() => createLookupTypeAhead(bridge.fetch), [bridge]);
  React.useEffect(() => () => typeAhead.cancel(), [typeAhead]);

  const [place, setPlace] = React.useState<{ place: Place; launch: number }>({ place: 'idle', launch: 0 });
  const [notice, setNotice] = React.useState<string | null>(null);
  const [linkFailed, setLinkFailed] = React.useState(false);

  // What the current open is about, and whether a wizard is open at all (one at a time, as the host allows).
  const active = React.useRef<{ event: OpenWizardEvent; lane: WorklistData } | null>(null);
  const handled = React.useRef(0);
  const launchCount = React.useRef(0);
  const unmounted = React.useRef(false);
  React.useEffect(() => {
    unmounted.current = false;
    return () => {
      unmounted.current = true;
    };
  }, []);

  const latest = React.useRef({
    getLane,
    onRecorded,
    onReloadLane,
    laneReads,
    onActiveChange,
    openLink,
    consoleBaseUrl,
    typeAhead,
    bridge,
  });
  latest.current = {
    getLane,
    onRecorded,
    onReloadLane,
    laneReads,
    onActiveChange,
    openLink,
    consoleBaseUrl,
    typeAhead,
    bridge,
  };

  // The row the wizard was opened from, to give the focus back to when it closes (see the effect below).
  const returnFocus = React.useRef<{ signalId: string; readsAtClose: number } | null>(null);

  /** Takes or releases the one open decision, and tells the lane. */
  const setActive = React.useCallback((next: typeof active.current) => {
    active.current = next;
    latest.current.onActiveChange?.(next !== null);
  }, []);

  const finish = React.useCallback(() => {
    const was = active.current;
    setActive(null);
    latest.current.typeAhead.cancel();
    setLinkFailed(false);
    setPlace(prev => ({ place: 'idle', launch: prev.launch }));
    if (was) {
      // Whatever happened in the wizard (a decision recorded, one found gone, nothing), the lane is read again in place, so what it shows is
      // what is there; and the focus goes back to the row the user opened it from.
      returnFocus.current = { signalId: was.event.signalId, readsAtClose: latest.current.laneReads };
      latest.current.onReloadLane?.();
    }
  }, [setActive]);

  const moveTo = React.useCallback((next: Place) => {
    launchCount.current += 1;
    setPlace({ place: next, launch: launchCount.current });
  }, []);

  /** The next place after one failed: the dialog falls to master-detail (the link when the frame is too narrow for it), the rest to the link. */
  const nextAfter = React.useCallback(
    (from: Place): Place => (from === 'dialog' && window.innerWidth >= MIN_DETAIL_WIDTH ? 'detail' : 'link'),
    []
  );

  // A new request from the lane.
  React.useEffect(() => {
    if (!request || request.seq === handled.current) return;
    handled.current = request.seq;

    if (active.current) {
      // One wizard at a time: never replace one with unrecorded choices in it.
      setNotice(WIZARD_OPEN_NOTICE);
      return;
    }
    setNotice(null);
    const lane = latest.current.getLane();
    if (!lane || !findDecideGroup(lane, request.event.signalId)) {
      // The lane the user saw is out of date: read it again, so what they see is what is there.
      setNotice(ITEM_GONE_NOTICE);
      latest.current.onReloadLane?.();
      return;
    }
    setActive({ event: request.event, lane });
    // Step 1: the dialog, in the display mode the frame is already in. No display mode is requested (owner decision 2026-11-24).
    moveTo('dialog');
  }, [request, moveTo, setActive]);

  // Launch the wizard in the place chosen. The host is mounted for that place before this runs (children's effects run first).
  React.useEffect(() => {
    if (place.place !== 'dialog' && place.place !== 'detail') return;
    const open = active.current;
    if (!open) return;
    const mine = place.launch;
    // Links to the Console's own record forms, opened by the host (no Xrm in a Copilot frame). Without a usable Console address the wizard
    // is given none, so no evidence line shows a link that cannot work.
    const { openLink: hostOpenLink, consoleBaseUrl: base } = latest.current;
    const consolePage = buildConsoleLinkOut(base);
    const openRecord = consolePage
      ? async (entity: string, id: string) => {
          const url = buildRecordLinkOut(base, entity, id);
          if (!url) throw new Error('This record has no address to open.');
          if (!(await hostOpenLink(url))) throw new Error('The host did not open the record.');
        }
      : undefined;
    // The "Showing N of M" note's link: the Console, where the lane holds the whole item.
    const openAll = consolePage
      ? () => {
          void Promise.resolve(hostOpenLink(consolePage)).catch(err => {
            console.warn('[WizardDialog] opening the Console failed', err);
          });
        }
      : undefined;
    const loader = createWizardItemLoader(latest.current.bridge, open.lane, undefined, openAll);
    void launchDecisionWizard({
      itemId: open.event.signalId,
      // The host reads the item's core record from what the loader returns; the lane's placeholder type is never passed on.
      core: null,
      visibleList: open.event.visibleList,
      loadItem: loader,
      lookup: latest.current.typeAhead,
      ...(openRecord ? { openRecord } : {}),
      onRecorded: () => latest.current.onRecorded(),
    }).then(
      result => {
        // A launch the display chain has moved past (the dialog failed and master-detail took over) is not this wizard closing.
        if (unmounted.current || launchCount.current !== mine) return;
        if (result.opened) finish();
        else if (result.reason === 'busy') {
          // The shared host has a wizard open that this one did not open: nothing of ours is open, so release the place it was given.
          finish();
          setNotice(WIZARD_OPEN_NOTICE);
        } else moveTo(nextAfter(place.place));
      },
      () => {
        if (unmounted.current || launchCount.current !== mine) return;
        moveTo(nextAfter(place.place));
      }
    );
  }, [place, finish, moveTo, nextAfter]);

  // The place failed while rendering: the next step of the chain.
  const failed = React.useCallback(
    (from: Place) => {
      if (unmounted.current) return;
      moveTo(nextAfter(from));
    },
    [moveTo, nextAfter]
  );

  const consoleLink = buildConsoleLinkOut(consoleBaseUrl);
  const openConsole = React.useCallback(async () => {
    let opened = false;
    try {
      opened = consoleLink ? await openLink(consoleLink) : false;
    } catch {
      opened = false;
    }
    if (!unmounted.current) setLinkFailed(!opened);
  }, [consoleLink, openLink]);

  // The last step of the chain asks the host to open the Console at once; the banner stays so the user can open it again if the host did not.
  React.useEffect(() => {
    if (place.place === 'link' && consoleLink) void openConsole();
    // Once per arrival at the link (`place.launch` counts arrivals), not on every change of the callback's inputs.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [place.place, place.launch]);

  // `confirmGuardMs`: Confirm cannot record until it has been showing half a second (NFR-06), so a double click or a held Enter on Next at
  // the last action step moves to Confirm and records nothing: Next and Record decision are the same button.
  // ---- Where the decision sits beside the lane, and where the focus goes --------------------------------------
  // Side by side when the frame can hold both; stacked otherwise, and then the decision comes FIRST.
  const [stacked, setStacked] = React.useState(() => window.innerWidth < SIDE_BY_SIDE_MIN_WIDTH);
  React.useEffect(() => {
    const onResize = () => setStacked(window.innerWidth < SIDE_BY_SIDE_MIN_WIDTH);
    window.addEventListener('resize', onResize);
    return () => window.removeEventListener('resize', onResize);
  }, []);

  // Master-detail: the decision opens in a frame the user may be looking at the far end of a long lane in, so it takes the focus (a screen
  // reader says "Decision") and is scrolled into view.
  const detailRef = React.useRef<HTMLElement>(null);
  const masterRef = React.useRef<HTMLDivElement>(null);
  React.useEffect(() => {
    if (place.place !== 'detail') return;
    const section = detailRef.current;
    if (!section) return;
    section.focus({ preventScroll: true });
    section.scrollIntoView?.({ block: 'start' });
  }, [place]);

  // When the wizard has closed, the focus goes back to the row it was opened from, the way a dialog gives it back to its opener (the
  // in-frame dialog's own focus return finds nothing to return to once the lane has been read again). The lane is read again in place, so
  // the row is usually still there; when the read lands and the row has gone (it was decided), the focus goes to the lane.
  React.useEffect(() => {
    const pending = returnFocus.current;
    if (!pending || place.place !== 'idle') return;
    const current = document.activeElement;
    // Focus the user has already moved on to is left alone; focus that was lost (the wizard's controls were removed) is returned.
    if (!current || current === document.body || !current.isConnected) {
      const wanted = pending.signalId.toLowerCase();
      const row = Array.from(document.querySelectorAll<HTMLElement>('button[data-signal-id]')).find(
        el => el.dataset.signalId?.toLowerCase() === wanted
      );
      (row ?? masterRef.current)?.focus();
    }
    // The lane has been read since the wizard closed: what it shows is what there is, so there is nothing more to return to.
    if (laneReads !== pending.readsAtClose) returnFocus.current = null;
  }, [place, laneReads]);

  const wizardProps = {
    authenticatedFetch: bridge.fetch,
    bffBaseUrl: '',
    collectBecause: true,
    confirmGuardMs: CONFIRM_GUARD_MS,
    // An evidence line that names a record offers "Open in Spaarke" (when the launch has a way to open it: a usable Console address).
    evidenceLinks: true,
    // Escape closes the decision (through the same discard check as Close), in the dialog and beside the lane: the host's own way out.
    closeOnEscape: true,
  } as const;

  const master = (
    <div
      key="master"
      ref={masterRef}
      tabIndex={-1}
      className={mergeClasses(styles.master, stacked && styles.masterStacked)}
    >
      {children}
    </div>
  );
  const detail =
    place.place === 'detail' ? (
      <section
        key="detail"
        ref={detailRef}
        tabIndex={-1}
        className={mergeClasses(styles.detail, stacked && styles.detailStacked)}
        aria-label="Decision"
        data-testid="wizard-detail"
      >
        <WizardBoundary key={`detail-${place.launch}`} onError={() => failed('detail')}>
          <DecisionWizardHost {...wizardProps} embedded />
        </WizardBoundary>
      </section>
    ) : null;

  return (
    <div className={mergeClasses(styles.root, place.place === 'dialog' && styles.rootDialog)}>
      {notice && (
        <div role="alert" data-testid="wizard-notice">
          <MessageBar intent="warning">
            <MessageBarBody>{notice}</MessageBarBody>
            <MessageBarActions>
              <Button size="small" appearance="transparent" onClick={() => setNotice(null)}>
                Dismiss
              </Button>
            </MessageBarActions>
          </MessageBar>
        </div>
      )}

      <div
        className={mergeClasses(styles.split, stacked && styles.splitStacked)}
        data-testid="wizard-place"
        data-place={place.place}
      >
        {/* Keyed, in an array: the order differs when stacked, and the lane (the master) is never remounted by it. */}
        {stacked ? [detail, master] : [master, detail]}
      </div>

      {/* The dialog renders in the shared shell's own layer, so it needs no place in the split. */}
      {place.place === 'dialog' && (
        <WizardBoundary key={`dialog-${place.launch}`} onError={() => failed('dialog')}>
          <DecisionWizardHost {...wizardProps} defaultMaximized />
        </WizardBoundary>
      )}

      {place.place === 'link' && (
        <div role="alert" data-testid="wizard-link-fallback">
          <MessageBar intent="warning">
            <MessageBarBody>
              The decision cannot be shown in this frame, so it is opened in Spaarke.{' '}
              {consoleLink ? (
                <Link
                  href={consoleLink}
                  onClick={event => {
                    // The widget never navigates itself: the host opens the page.
                    event.preventDefault();
                    void openConsole();
                  }}
                >
                  Open Spaarke
                </Link>
              ) : (
                'Open Spaarke and decide it from your Decide lane.'
              )}
              {linkFailed ? ' The link could not be opened.' : ''}
            </MessageBarBody>
            <MessageBarActions>
              <Button size="small" onClick={finish} data-testid="wizard-link-close">
                Close
              </Button>
            </MessageBarActions>
          </MessageBar>
        </div>
      )}
    </div>
  );
}
