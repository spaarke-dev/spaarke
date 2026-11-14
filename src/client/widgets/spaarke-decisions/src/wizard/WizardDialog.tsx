/**
 * The decision wizard inside the widget (spaarkeai-mcp-bff-api-r1 task 032, FR-13; design section 9.4, O-5).
 *
 * It mounts the shared `DecisionWizardHost` (the same host the Console mounts once) and launches it for the item the lane raised, over a
 * `ResponseFetchFn` that turns the wizard's three routes into tool calls (`wizardFetch.ts`), an item loader (`wizardItems.ts`) and an inline
 * type-ahead (`lookupTypeAhead.ts`). Everything else is the shared wizard unchanged: the steps, the draft, the discard check, the Confirm
 * and the way a refusal or a failed commit is worded (`decisionDisplay`).
 *
 * Where it shows (O-5), in order:
 *  1. An in-frame dialog: the shared modal shell (`SprkModal` through `WizardShell`, ADR-050), not a host modal (`requestModal` is not
 *     supported in Copilot). The widget asks the host for fullscreen as it opens, because an inline frame is at most about 640 px tall and
 *     a dialog needs the room. Fullscreen granted: the dialog.
 *  2. Master-detail: the lane stays as the master and the wizard is shown beside it (below it in a narrow frame), embedded in the page
 *     with no modal envelope. Used when fullscreen was refused or is not offered (no room for a dialog), and when the dialog threw
 *     while rendering. (The design's "master-detail in fullscreen" is read as this layout: the list stays visible beside the wizard.)
 *  3. A link to the Console, opened through the host at once (`openLink`), and kept on screen in case the host does not open it: the
 *     same Work Item is in the user's Decide lane there. Used when master-detail cannot be shown either (the frame is too narrow, or it
 *     threw). The Console has no link to one item, so the link is to the Console page itself.
 * The wizard closing, after the user finished or discarded, returns the frame to inline.
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
  tokens,
} from '@fluentui/react-components';
import { DecisionWizardHost } from '@spaarke/ui-components/components/DecisionWizard/DecisionWizardHost';
import { launchDecisionWizard } from '@spaarke/ui-components/components/DecisionWizard/decisionWizardLauncher';
import type { WorklistData } from '@spaarke/ui-components/components/Worklist/worklistService';
import { buildConsoleLinkOut } from '../lane/billingLinkOut';
import type { OpenWizardEvent } from '../lane/DecideLane';
import { useMcpHost } from '../mcp/mcpHostContext';
import { createLookupTypeAhead } from './lookupTypeAhead';
import { createWizardBridge } from './wizardFetch';
import { createWizardItemLoader, findDecideGroup, ITEM_GONE } from './wizardItems';

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
  /** The master: the Decide lane. It stays mounted, and in place, whatever the wizard shows. */
  children: React.ReactNode;
}

/** A frame narrower than this cannot hold master-detail even stacked: the Console link is used. */
export const MIN_DETAIL_WIDTH = 480;

type Place = 'idle' | 'dialog' | 'detail' | 'link';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalM, minWidth: 0 },
  split: { display: 'flex', flexWrap: 'wrap', alignItems: 'flex-start', gap: tokens.spacingHorizontalL, minWidth: 0 },
  master: { flex: '1 1 320px', minWidth: 0 },
  detail: {
    flex: '2 1 420px',
    minWidth: 0,
    // A fixed height: the embedded shell fills its container, and a frame sized from its content must not size itself from the viewport.
    height: '640px',
    display: 'flex',
    flexDirection: 'column',
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    overflow: 'hidden',
  },
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

export function WizardDialog({ request, getLane, consoleBaseUrl, onRecorded, children }: WizardDialogProps) {
  const styles = useStyles();
  const { callTool, openLink, requestDisplayMode, hostContext } = useMcpHost();

  const bridge = React.useMemo(() => createWizardBridge(callTool), [callTool]);
  const typeAhead = React.useMemo(() => createLookupTypeAhead(bridge.fetch), [bridge]);
  React.useEffect(() => () => typeAhead.cancel(), [typeAhead]);

  const [place, setPlace] = React.useState<{ place: Place; launch: number }>({ place: 'idle', launch: 0 });
  const [notice, setNotice] = React.useState<string | null>(null);
  const [linkFailed, setLinkFailed] = React.useState(false);

  // What the current open is about, and whether a wizard is open at all (one at a time, as the host allows).
  const active = React.useRef<{ event: OpenWizardEvent; lane: WorklistData; fullscreen: boolean } | null>(null);
  const handled = React.useRef(0);
  const launchCount = React.useRef(0);
  const unmounted = React.useRef(false);
  React.useEffect(() => {
    unmounted.current = false;
    return () => {
      unmounted.current = true;
    };
  }, []);

  const latest = React.useRef({ getLane, onRecorded, requestDisplayMode, hostContext, typeAhead, bridge });
  latest.current = { getLane, onRecorded, requestDisplayMode, hostContext, typeAhead, bridge };

  const finish = React.useCallback(() => {
    const was = active.current;
    active.current = null;
    latest.current.typeAhead.cancel();
    setLinkFailed(false);
    setPlace(prev => ({ place: 'idle', launch: prev.launch }));
    // The wizard asked for the room; the lane goes back to where it was.
    if (was?.fullscreen) void latest.current.requestDisplayMode('inline');
  }, []);

  const moveTo = React.useCallback((next: Place) => {
    launchCount.current += 1;
    setPlace({ place: next, launch: launchCount.current });
  }, []);

  // A new request from the lane.
  React.useEffect(() => {
    if (!request || request.seq === handled.current) return;
    handled.current = request.seq;

    if (active.current) {
      // One wizard at a time: never replace one with unrecorded choices in it.
      setNotice('Finish or close the open decision before opening another.');
      return;
    }
    setNotice(null);
    const lane = latest.current.getLane();
    if (!lane || !findDecideGroup(lane, request.event.signalId)) {
      setNotice(ITEM_GONE);
      return;
    }
    const opening = { event: request.event, lane, fullscreen: false };
    active.current = opening;

    void (async () => {
      // Step 1: the dialog, and ask for the room. A host that lists its modes and leaves fullscreen out is not asked.
      const offered = latest.current.hostContext?.availableDisplayModes;
      const canAsk = !Array.isArray(offered) || offered.includes('fullscreen');
      const display = canAsk ? await latest.current.requestDisplayMode('fullscreen') : null;
      if (unmounted.current || active.current !== opening) return;
      opening.fullscreen = display === 'fullscreen';

      // Fullscreen granted: the dialog has the room. Refused (or not offered): an inline frame is too short for a dialog, so the wizard
      // is shown beside the lane instead; a frame too narrow even for that has only the link.
      if (opening.fullscreen) moveTo('dialog');
      else if (window.innerWidth >= MIN_DETAIL_WIDTH) moveTo('detail');
      else moveTo('link');
    })();
  }, [request, moveTo]);

  // Launch the wizard in the place chosen. The host is mounted for that place before this runs (children's effects run first).
  React.useEffect(() => {
    if (place.place !== 'dialog' && place.place !== 'detail') return;
    const open = active.current;
    if (!open) return;
    const mine = place.launch;
    const loader = createWizardItemLoader(latest.current.bridge, open.lane);
    void launchDecisionWizard({
      itemId: open.event.signalId,
      // The host reads the item's core record from what the loader returns; the lane's placeholder type is never passed on.
      core: null,
      visibleList: open.event.visibleList,
      loadItem: loader,
      lookup: latest.current.typeAhead,
      onRecorded: () => latest.current.onRecorded(),
    }).then(
      result => {
        // A launch the display chain has moved past (the dialog failed and master-detail took over) is not this wizard closing.
        if (unmounted.current || launchCount.current !== mine) return;
        if (result.opened) finish();
        else if (result.reason === 'busy') setNotice('Finish or close the open decision before opening another.');
        else moveTo(place.place === 'dialog' ? 'detail' : 'link');
      },
      () => {
        if (unmounted.current || launchCount.current !== mine) return;
        moveTo(place.place === 'dialog' ? 'detail' : 'link');
      }
    );
  }, [place, finish, moveTo]);

  // The place failed while rendering: the next step of the chain.
  const failed = React.useCallback(
    (from: Place) => {
      if (unmounted.current) return;
      moveTo(from === 'dialog' ? 'detail' : 'link');
    },
    [moveTo]
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

  const wizardProps = { authenticatedFetch: bridge.fetch, bffBaseUrl: '', collectBecause: true } as const;

  return (
    <div className={styles.root}>
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

      <div className={styles.split} data-testid="wizard-place" data-place={place.place}>
        <div className={styles.master}>{children}</div>

        {place.place === 'detail' && (
          <section className={styles.detail} aria-label="Decision" data-testid="wizard-detail">
            <WizardBoundary key={`detail-${place.launch}`} onError={() => failed('detail')}>
              <DecisionWizardHost {...wizardProps} embedded />
            </WizardBoundary>
          </section>
        )}
      </div>

      {/* The dialog renders in the shared shell's own layer, so it needs no place in the split. */}
      {place.place === 'dialog' && (
        <WizardBoundary key={`dialog-${place.launch}`} onError={() => failed('dialog')}>
          <DecisionWizardHost {...wizardProps} />
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
