/**
 * The Decide lane in the widget (spaarkeai-mcp-bff-api-r1 task 031, FR-12, FR-05; EB-D-194, D-208).
 *
 * It reuses the Console's lane, not a copy: `WorklistLanePanel` (header, count, category cards, rows grouped by matter, Start review),
 * `buildLaneView` (the cards' counts and the visible list) and `createWorklistService` (the read), the last over the MCP fetch shim
 * (`decideLaneFetch.ts`). The panel is not in the Worklist barrel, so it is imported by deep path (ADR-012).
 *
 *  - Counts: the lane is read to the end before it is shown, so the heading count and the cards describe the whole lane, as the Console's
 *    do. "Done today" is the optional `doneToday` prop: the shell passes `closedToday.all` of `spaarke_show_decisions` (DEF-001, FR-13), the
 *    count the Console's closed-today set gives, and the All card then says "n of m done today". Absent (an older server, or a read that
 *    did not answer), the card carries no progress line, as the Console's does not.
 *  - A billing Work Item (its row carries `invoiceReview`) is decided only in Invoice Review (EB-D-194). Its row is replaced, through the
 *    panel's `renderItem` seam, by a "Finding" row with a link out (`billingLinkOut.ts`) that the host opens (`openLink`). It has no decide
 *    action, never raises `onOpenWizard`, and is left out of Start review and of the wizard's browse set.
 *  - A non-billing row raises `onOpenWizard` (the Decision wizard, task 032, consumes it). Start review walks the non-billing items of the
 *    view, whole lane across every page, with a "n of N" label and Previous / Next; each step raises `onOpenWizard` for that item.
 *  - Nothing here opens a network connection, reads a token, or calls `openLookup`, `openRecord` or Xrm. The lane's open or closed state is
 *    the shared hook's (`useLaneOpen`: localStorage inside try/catch, so it degrades safely).
 */
import * as React from 'react';
import {
  Badge,
  Button,
  Caption1,
  Link,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { WorklistLanePanel } from '@spaarke/ui-components/components/Worklist/WorklistLanePanel';
import { buildLaneView } from '@spaarke/ui-components/components/Worklist/worklistView';
import { composeIssueLine } from '@spaarke/ui-components/components/Worklist/composeIssueLine';
import { INVOICE_REVIEW_FAILED } from '@spaarke/ui-components/components/Worklist/openInvoiceReview';
import { hasInvoiceReview } from '@spaarke/ui-components/components/Worklist/types';
import type { LaneFilterKey } from '@spaarke/ui-components/components/Worklist/laneCountFilters';
import type { OpenItemEvent, WorklistItem } from '@spaarke/ui-components/components/Worklist/types';
import {
  WorklistRequestError,
  type WorklistData,
  type WorklistService,
} from '@spaarke/ui-components/components/Worklist/worklistService';
import { buildBillingLinkOut } from './billingLinkOut';

/** What the lane raises when a non-billing row is opened, or a review step is taken: the Decision wizard (task 032) consumes it. */
export interface OpenWizardEvent {
  /** The Work Item's Signal id. */
  signalId: string;
  /**
   * The row's core record id; `null` for a Not filed card. No record type: the paged read carries none (the widget's `CORE_RECORD_TYPE`
   * is an internal placeholder, not a Dataverse logical name), so the event claims none until the page carries the real one.
   */
  core: { recordId: string } | null;
  /** The non-billing Work Items of the view, in the order shown, whole lane across every page: the wizard's browse set. */
  visibleList: readonly string[];
  /** 1-based place of `signalId` in `visibleList`. */
  position: number;
  /** `visibleList.length`. */
  total: number;
}

export interface DecideLaneProps {
  /** The worklist service over the MCP shim (`createWorklistService({ fetch: createDecideLaneFetch(callTool), baseUrl: '' })`). */
  service: WorklistService;
  /** `consoleBaseUrl` of `spaarke_show_decisions`; without a usable one a billing row has no link. */
  consoleBaseUrl?: string | null;
  /** Asks the host to open a link in the user's browser. Resolves false when it did not open. */
  openLink: (url: string) => Promise<boolean>;
  /** A non-billing row was opened, or Start review stepped. Task 032 opens the wizard; absent, nothing opens. */
  onOpenWizard?: (event: OpenWizardEvent) => void;
  /** Items finished today, for the All card's progress line (`closedToday.all` of `spaarke_show_decisions`); absent shows none. */
  doneToday?: number;
  /** "Now" for ages; tests inject a fixed date. Defaults to the time the lane was read. */
  today?: Date;
  /**
   * Read the lane again, in place (task 032). A change of this number (the wizard recorded a decision, so the row has left the lane) makes
   * a new read WITHOUT the loading state: the rows stay where they are until the answer replaces them, the filter and the notice are kept,
   * and a review under way is trimmed to what is still in the lane. Absent or unchanged: nothing is read again.
   */
  reloadToken?: number;
  /** The lane as last read (`null` while it loads or after a failed read), so the wizard can open on the lane as the user saw it. */
  onData?: (data: WorklistData | null) => void;
  /**
   * Asked before a row is opened or a review step is taken (task 032): false when a decision is already open (one at a time, and the
   * open one is never replaced). The lane then says so and leaves the review where it was, so its "n of N" never names an item that
   * is not the one in front of the user. Absent: always allowed.
   */
  canOpenWizard?: () => boolean;
}

/** What the lane says when a row is opened, or a review step taken, while a decision is already open. */
export const WIZARD_OPEN_NOTICE = 'Finish or close the open decision before opening another.';

/** A notice and its number: the same words twice are two announcements (`key={id}` makes the second a new alert). */
interface Notice {
  text: string;
  id: number;
}

type LoadState =
  | { status: 'loading' }
  | { status: 'ready'; data: WorklistData }
  | { status: 'error'; message: string; httpStatus: number | null };

interface Review {
  ids: readonly string[];
  index: number;
}

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalM, minWidth: 0 },
  center: { display: 'flex', justifyContent: 'center', padding: tokens.spacingVerticalXXL },
  heading: { display: 'flex', flexDirection: 'column' },
  muted: { color: tokens.colorNeutralForeground3 },
  belowHeader: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalS },
  reviewBar: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    columnGap: tokens.spacingHorizontalM,
    rowGap: tokens.spacingVerticalXS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  spacer: { flexGrow: 1 },
  finding: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalM,
    width: '100%',
    minWidth: 0,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
  },
  /** The finding over the subject, as on the shared row (IssueLine): one column that takes the free width, so neither
   * line can squeeze the other to nothing; the badge, timing and link stay on the right. */
  text: { flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' },
  headline: { minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' },
  reason: {
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    color: tokens.colorNeutralForeground3,
  },
  timing: { flexShrink: 0, color: tokens.colorNeutralForeground3 },
});

const messageOf = (err: unknown): string =>
  err instanceof Error && err.message ? err.message : 'The decisions could not be read.';

export const DecideLane: React.FC<DecideLaneProps> = ({
  service,
  consoleBaseUrl,
  openLink,
  onOpenWizard,
  doneToday,
  today: todayProp,
  reloadToken,
  onData,
  canOpenWizard,
}) => {
  const styles = useStyles();
  const [load, setLoad] = React.useState<LoadState>({ status: 'loading' });
  const [attempt, setAttempt] = React.useState(0);
  const [filter, setFilter] = React.useState<LaneFilterKey>('all');
  const [review, setReview] = React.useState<Review | null>(null);
  const [notice, setNotice] = React.useState<Notice | null>(null);
  const noticeSeq = React.useRef(0);
  /** Shows a notice. Each call is a new alert, so the same words again are announced again (`setNotice(null)` then the same text batches into one render). */
  const showNotice = React.useCallback((text: string) => {
    noticeSeq.current += 1;
    setNotice({ text, id: noticeSeq.current });
  }, []);

  const latestOnData = React.useRef(onData);
  latestOnData.current = onData;

  // What the previous read was for: a changed `reloadToken` on the same service is an in-place re-read; anything else is a new read.
  const lastRead = React.useRef<{ service: WorklistService; reloadToken: number } | null>(null);
  // Counts the reads that are NEW (a new service, Try again): work started under an older count (a billing link still opening) must not
  // act on the lane of a newer one.
  const readEpoch = React.useRef(0);

  // The whole lane is read before it is shown, so the counts are the lane's. A newer read (Try again, a new service, a reload) ignores an
  // older answer.
  const token = reloadToken ?? 0; // absent and 0 are the same token: leaving it out is not a change
  React.useEffect(() => {
    let current = true;
    const previous = lastRead.current;
    const inPlace = previous !== null && previous.service === service && previous.reloadToken !== token;
    lastRead.current = { service, reloadToken: token };

    if (!inPlace) {
      readEpoch.current += 1;
      setLoad({ status: 'loading' });
      latestOnData.current?.(null);
      // A new service (or Try again) is a new read of the lane: a walk, a notice or a filter from the old one may name ids or cards that
      // are not in it.
      setReview(null);
      setNotice(null);
      setFilter('all');
    }
    service.read('lanes').then(
      data => {
        if (!current) return;
        setLoad({ status: 'ready', data });
        latestOnData.current?.(data);
        if (inPlace) {
          // A review under way keeps its place among the items that are still in the lane (the one just decided has left it).
          const present = new Set(data.Decide.flatMap(group => group.items.map(item => item.signalId)));
          setReview(walk => {
            if (!walk) return walk;
            const ids = walk.ids.filter(id => present.has(id));
            if (ids.length === 0) return null;
            const at = ids.indexOf(walk.ids[walk.index]);
            return { ids, index: at >= 0 ? at : Math.min(walk.index, ids.length - 1) };
          });
        }
      },
      (err: unknown) => {
        if (!current) return;
        // A lane that cannot be read again after a decision cannot be trusted to show what is left, so it is not left on screen.
        setLoad({
          status: 'error',
          message: messageOf(err),
          httpStatus: err instanceof WorklistRequestError ? err.httpStatus : null,
        });
        latestOnData.current?.(null);
      }
    );
    return () => {
      current = false;
    };
  }, [service, attempt, token]);

  const data = load.status === 'ready' ? load.data : null;
  const today = React.useMemo(() => todayProp ?? new Date(), [todayProp, data]); // eslint-disable-line react-hooks/exhaustive-deps
  const view = React.useMemo(() => buildLaneView(data?.Decide ?? [], 'Decide', filter), [data, filter]);
  const billingIds = React.useMemo(
    () => new Set(view.allItems.filter(hasInvoiceReview).map(item => item.signalId)),
    [view.allItems]
  );
  /** The non-billing items of the view, in the order shown: what Start review walks and what the wizard browses. */
  const reviewable = React.useMemo(
    () => view.visibleIds.filter(id => !billingIds.has(id)),
    [view.visibleIds, billingIds]
  );

  const raise = React.useCallback(
    (ids: readonly string[], index: number, core: OpenWizardEvent['core']) => {
      onOpenWizard?.({ signalId: ids[index], core, visibleList: ids, position: index + 1, total: ids.length });
    },
    [onOpenWizard]
  );

  const coreOf = React.useCallback(
    (signalId: string): OpenWizardEvent['core'] => {
      const group = view.groups.find(g => g.items.some(i => i.signalId === signalId));
      return group?.group.core ? { recordId: group.group.core.recordId } : null;
    },
    [view.groups]
  );

  const selectKey = (key: LaneFilterKey) => {
    setFilter(key);
    setReview(null); // the walked list was the old view's
    setNotice(null);
  };

  /** True when a decision may be opened now. When one is already open the lane says so and moves nothing (not the review either). */
  const mayOpenWizard = (): boolean => {
    if (!canOpenWizard || canOpenWizard()) return true;
    showNotice(WIZARD_OPEN_NOTICE);
    return false;
  };

  const openItem = (event: OpenItemEvent) => {
    // A billing row has no decide action; the panel's line is replaced for it, and this is the second guard.
    if (billingIds.has(event.itemId)) return;
    const index = reviewable.indexOf(event.itemId);
    if (index < 0) return;
    if (!mayOpenWizard()) return;
    setNotice(null);
    // While a review is under way the label follows the row that was opened, so "n of N" always names the item in front of the user.
    if (review) setReview({ ids: reviewable, index });
    raise(reviewable, index, event.core ? { recordId: event.core.recordId } : null);
  };

  const startReview = () => {
    if (!mayOpenWizard()) return;
    setNotice(null);
    if (reviewable.length === 0) {
      setReview(null);
      showNotice('Nothing in this view can be reviewed here. Invoice decisions are decided in Invoice Review.');
      return;
    }
    setReview({ ids: reviewable, index: 0 });
    raise(reviewable, 0, coreOf(reviewable[0]));
  };

  const stepReview = (delta: 1 | -1) => {
    if (!review) return;
    const index = Math.min(review.ids.length - 1, Math.max(0, review.index + delta));
    if (index === review.index) return;
    if (!mayOpenWizard()) return;
    setReview({ ids: review.ids, index });
    raise(review.ids, index, coreOf(review.ids[index]));
  };

  const openBillingLink = async (url: string) => {
    // The lane this link was clicked in: if a newer read has replaced it (a new service), a late failure is not about this lane.
    const epoch = readEpoch.current;
    setNotice(null);
    let opened = false;
    try {
      opened = await openLink(url);
    } catch {
      opened = false;
    }
    if (!opened && readEpoch.current === epoch) showNotice(INVOICE_REVIEW_FAILED);
  };

  const renderItem = (item: WorklistItem): React.ReactNode => {
    if (!hasInvoiceReview(item)) return undefined;
    const href = buildBillingLinkOut(item.invoiceReview, consoleBaseUrl);
    const line = composeIssueLine(item, today);
    const showSubject = line.headline !== line.finding;
    // Two findings on one invoice share a subject, so the link is named after the finding and the subject.
    const linkName = showSubject ? `${line.finding}, ${line.headline}` : line.finding;
    return (
      <div className={styles.finding} data-testid="billing-finding" data-signal-id={item.signalId}>
        {/* D-227 (owner: "all surfaces"): the finding leads, the subject sits under it, as on the shared row (IssueLine). */}
        <span className={styles.text} data-testid="billing-finding-lines">
          <Text className={styles.headline} size={300} title={line.finding} data-testid="billing-finding-text">
            {line.finding}
          </Text>
          {showSubject && (
            <Caption1 className={styles.reason} title={line.headline} data-testid="billing-finding-subject">
              {line.headline}
            </Caption1>
          )}
        </span>
        <Badge appearance="tint" color="informative" size="small" data-testid="billing-finding-label">
          Finding
        </Badge>
        <Text className={styles.timing} size={200}>
          {line.timing.text}
        </Text>
        {href ? (
          <Link
            href={href}
            aria-label={`Open in Invoice Review: ${linkName}`}
            data-testid="billing-finding-link"
            onClick={event => {
              // The widget never navigates itself: the host opens the page.
              event.preventDefault();
              void openBillingLink(href);
            }}
          >
            Open in Invoice Review
          </Link>
        ) : (
          <Caption1 className={styles.muted} data-testid="billing-finding-no-link">
            Invoice Review link unavailable
          </Caption1>
        )}
      </div>
    );
  };

  if (load.status === 'loading') {
    return (
      <div className={styles.center} data-testid="decide-lane-loading">
        <Spinner label="Loading the Decide lane" />
      </div>
    );
  }

  if (load.status === 'error') {
    // A 403 is the caller's access, not a fault: another try gives the same answer, so there is no button.
    const forbidden = load.httpStatus === 403;
    return (
      <div className={styles.root}>
        <div className={styles.heading}>
          <Text as="h2" size={500} weight="semibold">
            Decide
          </Text>
          <Caption1 className={styles.muted}>Items that need your decision</Caption1>
        </div>
        <MessageBar intent="error" data-testid="decide-lane-error">
          <MessageBarBody>
            {forbidden ? "Your decisions aren't available for your account. Contact your administrator." : load.message}
          </MessageBarBody>
          {!forbidden && (
            <MessageBarActions>
              <Button size="small" onClick={() => setAttempt(n => n + 1)}>
                Try again
              </Button>
            </MessageBarActions>
          )}
        </MessageBar>
      </div>
    );
  }

  // Directly under the lane header, so the walk's controls and the failure notice are in reach however long the list is (a long
  // lane has up to thousands of rows below). The notice is an alert (announced when it appears); the live region of the review bar is
  // the position label alone, so stepping announces "n of N" and the buttons are not re-read.
  const belowHeader =
    notice || review ? (
      <div className={styles.belowHeader}>
        {notice && (
          // `key`: the same words again are a new alert, so a repeat is announced again.
          <div role="alert" key={notice.id} data-testid="decide-lane-notice">
            <MessageBar intent="warning">
              <MessageBarBody>{notice.text}</MessageBarBody>
            </MessageBar>
          </div>
        )}
        {review && (
          <div className={styles.reviewBar} role="group" aria-label="Review" data-testid="decide-review-bar">
            <Text weight="semibold">Reviewing</Text>
            <Text
              role="status"
              aria-live="polite"
              data-testid="decide-review-position"
            >{`${review.index + 1} of ${review.ids.length}`}</Text>
            <span className={styles.spacer} />
            <Button
              size="small"
              disabled={review.index === 0}
              onClick={() => stepReview(-1)}
              data-testid="decide-review-previous"
            >
              Previous
            </Button>
            <Button
              size="small"
              appearance="primary"
              disabled={review.index >= review.ids.length - 1}
              onClick={() => stepReview(1)}
              data-testid="decide-review-next"
            >
              Next
            </Button>
            <Button size="small" appearance="subtle" onClick={() => setReview(null)} data-testid="decide-review-stop">
              Stop review
            </Button>
          </div>
        )}
      </div>
    ) : undefined;

  return (
    <div className={styles.root} data-testid="decide-lane">
      <WorklistLanePanel
        lane="Decide"
        view={view}
        doneToday={doneToday}
        today={today}
        onSelectKey={selectKey}
        onOpenItem={openItem}
        onStart={startReview}
        renderItem={renderItem}
        belowHeader={belowHeader}
      />
    </div>
  );
};

DecideLane.displayName = 'DecideLane';
