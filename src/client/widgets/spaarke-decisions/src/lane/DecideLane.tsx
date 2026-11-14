/**
 * The Decide lane in the widget (spaarkeai-mcp-bff-api-r1 task 031, FR-12, FR-05; EB-D-194, D-208).
 *
 * It reuses the Console's lane, not a copy: `WorklistLanePanel` (header, count, category cards, rows grouped by matter, Start review),
 * `buildLaneView` (the cards' counts and the visible list) and `createWorklistService` (the read), the last over the MCP fetch shim
 * (`decideLaneFetch.ts`). The panel is not in the Worklist barrel, so it is imported by deep path (ADR-012).
 *
 *  - Counts: the lane is read to the end before it is shown, so the heading count and the cards describe the whole lane, as the Console's
 *    do. "Done today" is not shown: no tool returns the items closed today (the Console reads that set from a route the widget has no tool
 *    for), so the All card carries no progress line until one does (`doneToday` is an optional prop for that day).
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
  /** The row's core record (matter, ...); `null` for a Not filed card. */
  core: OpenItemEvent['core'];
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
  /** Items finished today, for the All card's progress line; no tool provides it yet, so absent shows none. */
  doneToday?: number;
  /** "Now" for ages; tests inject a fixed date. Defaults to the time the lane was read. */
  today?: Date;
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
  headline: { flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' },
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
}) => {
  const styles = useStyles();
  const [load, setLoad] = React.useState<LoadState>({ status: 'loading' });
  const [attempt, setAttempt] = React.useState(0);
  const [filter, setFilter] = React.useState<LaneFilterKey>('all');
  const [review, setReview] = React.useState<Review | null>(null);
  const [notice, setNotice] = React.useState<string | null>(null);

  // The whole lane is read before it is shown, so the counts are the lane's. A newer read (Try again, a new service) ignores an older answer.
  React.useEffect(() => {
    let current = true;
    setLoad({ status: 'loading' });
    service.read('lanes').then(
      data => {
        if (current) setLoad({ status: 'ready', data });
      },
      (err: unknown) => {
        if (current) {
          setLoad({
            status: 'error',
            message: messageOf(err),
            httpStatus: err instanceof WorklistRequestError ? err.httpStatus : null,
          });
        }
      }
    );
    return () => {
      current = false;
    };
  }, [service, attempt]);

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
    (ids: readonly string[], index: number, core: OpenItemEvent['core']) => {
      onOpenWizard?.({ signalId: ids[index], core, visibleList: ids, position: index + 1, total: ids.length });
    },
    [onOpenWizard]
  );

  const coreOf = React.useCallback(
    (signalId: string): OpenItemEvent['core'] => {
      const group = view.groups.find(g => g.items.some(i => i.signalId === signalId));
      return group?.group.core
        ? { recordType: group.group.core.recordType, recordId: group.group.core.recordId }
        : null;
    },
    [view.groups]
  );

  const selectKey = (key: LaneFilterKey) => {
    setFilter(key);
    setReview(null); // the walked list was the old view's
    setNotice(null);
  };

  const openItem = (event: OpenItemEvent) => {
    // A billing row has no decide action; the panel's line is replaced for it, and this is the second guard.
    if (billingIds.has(event.itemId)) return;
    const index = reviewable.indexOf(event.itemId);
    if (index < 0) return;
    setNotice(null);
    // While a review is under way the label follows the row that was opened, so "n of N" always names the item in front of the user.
    if (review) setReview({ ids: reviewable, index });
    raise(reviewable, index, event.core);
  };

  const startReview = () => {
    setNotice(null);
    if (reviewable.length === 0) {
      setReview(null);
      setNotice('Nothing in this view can be reviewed here. Invoice decisions are decided in Invoice Review.');
      return;
    }
    setReview({ ids: reviewable, index: 0 });
    raise(reviewable, 0, coreOf(reviewable[0]));
  };

  const stepReview = (delta: 1 | -1) => {
    if (!review) return;
    const index = Math.min(review.ids.length - 1, Math.max(0, review.index + delta));
    if (index === review.index) return;
    setReview({ ids: review.ids, index });
    raise(review.ids, index, coreOf(review.ids[index]));
  };

  const openBillingLink = async (url: string) => {
    setNotice(null);
    let opened = false;
    try {
      opened = await openLink(url);
    } catch {
      opened = false;
    }
    if (!opened) setNotice(INVOICE_REVIEW_FAILED);
  };

  const renderItem = (item: WorklistItem): React.ReactNode => {
    if (!hasInvoiceReview(item)) return undefined;
    const href = buildBillingLinkOut(item.invoiceReview, consoleBaseUrl);
    const line = composeIssueLine(item, today);
    return (
      <div className={styles.finding} data-testid="billing-finding" data-signal-id={item.signalId}>
        <Text className={styles.headline} size={300} title={line.headline}>
          {line.headline}
        </Text>
        <Badge appearance="tint" color="informative" size="small" data-testid="billing-finding-label">
          Finding
        </Badge>
        <Text className={styles.timing} size={200}>
          {line.timing.text}
        </Text>
        {href ? (
          <Link
            href={href}
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

  return (
    <div className={styles.root} data-testid="decide-lane">
      {notice && (
        <MessageBar intent="warning" data-testid="decide-lane-notice">
          <MessageBarBody>{notice}</MessageBarBody>
        </MessageBar>
      )}

      <WorklistLanePanel
        lane="Decide"
        view={view}
        doneToday={doneToday}
        today={today}
        onSelectKey={selectKey}
        onOpenItem={openItem}
        onStart={startReview}
        renderItem={renderItem}
      />

      {review && (
        <div className={styles.reviewBar} role="status" aria-label="Review" data-testid="decide-review-bar">
          <Text weight="semibold">Reviewing</Text>
          <Text data-testid="decide-review-position">{`${review.index + 1} of ${review.ids.length}`}</Text>
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
  );
};

DecideLane.displayName = 'DecideLane';
