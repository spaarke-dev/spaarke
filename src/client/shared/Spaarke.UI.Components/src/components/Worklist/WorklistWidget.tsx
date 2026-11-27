/**
 * WorklistWidget - the Console's worklist: two lanes (Decide always above Do), each with its count, its count filters and its
 * start button, one MatterCard per core record, and the Closed today / Suppressed / Closed earlier disclosures below. A line,
 * a start button or a disclosure line opens the decision wizard in-app (D-26) on the list the user is looking at.
 *
 * Membership and order come from the Signal read route (task 038) and from nowhere else (D-15): this component reads no
 * `sprk_signal` row itself and never sorts or filters by a rule of its own. A count filter is a lens on the list already held
 * (task 052), is kept in component state only and is gone on the next mount (HANDOFF section 1.4 L123).
 *
 * Slots the host fills (this library has no Dataverse or Xrm dependency, ADR-012):
 *  - `aggregateRow` - the email reconciliation row (task 054's card). Top of the Do lane, outside both lanes' counts, hidden
 *    while a Do filter is on.
 *  - `narrative` - the Briefing narrative. Hidden while ANY filter is on; framed as written by the Assistant, not choosing or
 *    ordering the list (W-10). What it may say (never an item that is not on the list) is the narrative's own contract (task 062).
 *  - `lookup` - the wizard's search for Person / Lookup fields.
 *
 * The inquiry-outcome To Do (D-111, task 155): opening a Do row whose subject is a To Do first asks the BFF whether that To Do records
 * the outcome of an open inquiry. If so the disposition picker opens (the reply, the four outcomes) instead of the wizard, and a
 * recorded outcome reads the lanes again. Any other row opens the wizard at once, with no lookup. The widget still reads no Dataverse.
 *
 * Billing items (D-207, task 175): a Work Item with `invoiceReview` is decided only in the Invoice Review page, so activating one
 * calls the host's `openInvoiceReview` (a dialog on its invoice, focused on its line) INSTEAD of the wizard, and the lanes are read
 * again when that dialog closes. The wizard's browse set leaves billing items out, so "next" never reaches one.
 * Task 195: when the host also supplies `invoiceReviewClientUrl`, the page opens in-app on the shared modal shell instead
 * (`InvoiceReviewModal`: ADR-050's launch rule) with "N of M" browse over the billing items of that list, and `openInvoiceReview`
 * stays the fallback for a host that cannot supply the URL.
 *
 * Task: spaarke-ontology-platform-r1, task 059 (task 175 added the billing items). Fluent v9 tokens only (ADR-021). No React 18-only API (shared with PCF hosts).
 */

import * as React from 'react';
import {
  Button,
  Body1,
  Caption1,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  Spinner,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { SparkleRegular } from '@fluentui/react-icons';
import { launchDecisionWizard } from '../DecisionWizard/decisionWizardLauncher';
import type { DecisionWizardLaunchRequest, DecisionWizardLaunchResult } from '../DecisionWizard/decisionWizardLauncher';
import type {
  DecisionItemLoader,
  DecisionLookupSearch,
  DecisionOpenLookup,
  DecisionOpenRecord,
} from '../DecisionWizard/decisionWizardTypes';
import { getEffectiveDarkMode } from '../../utils/themeStorage';
import type { LaneFilterKey } from './laneCountFilters';
import { buildInvoiceBrowseSet } from './invoiceReviewBrowse';
import type { InvoiceBrowseSet } from './invoiceReviewBrowse';
import { InvoiceReviewModal } from './InvoiceReviewModal';
import { INVOICE_REVIEW_FAILED } from './openInvoiceReview';
import { hasInvoiceReview } from './types';
import type { InvoiceReviewTarget, OpenItemEvent, WorklistLane } from './types';
import { useWorklistData } from './useWorklistData';
import { useWorklistNarrative } from './useWorklistNarrative';
import { WorklistDisclosures } from './WorklistDisclosures';
import type { DisclosureSetName } from './WorklistDisclosures';
import { WorklistLanePanel } from './WorklistLanePanel';
import { InquiryDispositionPicker } from './InquiryDispositionPicker';
import {
  INQUIRY_LOOKUP_FAILED,
  OUTCOME_RECORDED_TODO_DONE,
  OUTCOME_RECORDED_TODO_DONE_NIGHTLY,
  OUTCOME_RECORDED_TODO_OPEN,
  inquiryTodoIdOf,
} from './inquiryDisposition';
import type {
  InquiryForTodo,
  WorklistGroupData,
  WorklistRouteItem,
  WorklistService,
  WorklistSetName,
} from './worklistService';
import { buildLaneView } from './worklistView';
import { buildWizardItem } from './worklistWizardItem';

export interface WorklistWidgetProps {
  /** Reads the Signal read route. `createWorklistService` builds one over the host's authenticated fetch. */
  service: WorklistService;
  /** The wizard's search for Person / Lookup fields (the item's own budgets, firms, people). */
  lookup?: DecisionLookupSearch;
  /** The wizard's side-pane lookup for Person / Lookup fields (D-182); absent keeps the inline type-ahead. */
  openLookup?: DecisionOpenLookup;
  /** The wizard's open-record-in-a-modal for the subject link (D-182); absent leaves the subject as text. */
  openRecord?: DecisionOpenRecord;
  /** Opens the decision wizard. Defaults to the in-app host's launcher; tests inject one. */
  launchWizard?: (request: DecisionWizardLaunchRequest) => Promise<DecisionWizardLaunchResult>;
  /**
   * Opens Invoice Review for a billing item (D-207); the promise settles when its dialog closes. The host builds it over
   * `Xrm.Navigation` (`openInvoiceReview`). Absent, or when it rejects, a billing item shows "Invoice Review could not be opened"
   * and never opens the wizard.
   */
  openInvoiceReview?: (target: InvoiceReviewTarget) => Promise<unknown>;
  /**
   * The org's client URL (`Xrm.Utility.getGlobalContext().getClientUrl()`, read by the HOST: this widget has no Xrm, ADR-012). When
   * supplied, activating a billing item opens Invoice Review IN-APP on the shared modal shell (`InvoiceReviewModal`, task 195, ADR-050),
   * with "N of M" previous / next browse over the billing items of the list it was activated from, and the lanes are read again when
   * it closes. Absent (or empty), the `openInvoiceReview` path above runs unchanged.
   */
  invoiceReviewClientUrl?: string;
  /** The email aggregate row (task 054's card), placed at the top of the Do lane. */
  aggregateRow?: React.ReactNode;
  /**
   * The Briefing narrative, shown above the lanes while no filter is on. When a host passes none, the widget asks `service.narrate`
   * for the Assistant's summary of the list's counts (task 062) and shows that instead. A host-supplied narrative wins.
   */
  narrative?: React.ReactNode;
  /** "Now" for ages and due states; tests inject a fixed date. Defaults to the time the list was last read. */
  today?: Date;
  className?: string;
}

const LANES: readonly WorklistLane[] = ['Decide', 'Do'];

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalL, minWidth: 0 },
  narrative: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusLarge,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
  },
  narrativeList: { margin: 0, paddingLeft: tokens.spacingHorizontalXL },
  narrativeTag: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground3,
  },
  center: { display: 'flex', justifyContent: 'center', padding: tokens.spacingVerticalXXL },
});

/** The picker being shown: the line that was opened, the inquiry its To Do records the outcome of, and the subject's name. */
interface PickerState {
  event: OpenItemEvent;
  /** The To Do the picker was opened from. */
  todoId: string;
  inquiry: InquiryForTodo;
  subjectName: string | null;
}

/** A notice above the lanes: a warning (something did not open) or plain information (something was recorded). */
interface Notice {
  text: string;
  intent: 'warning' | 'info';
}

const coreRef = (core: { recordType: string; recordId: string } | null) =>
  core ? { recordType: core.recordType, recordId: core.recordId } : null;

export const WorklistWidget: React.FC<WorklistWidgetProps> = ({
  service,
  lookup,
  openLookup,
  openRecord,
  launchWizard = launchDecisionWizard,
  openInvoiceReview,
  invoiceReviewClientUrl,
  aggregateRow,
  narrative,
  today: todayProp,
  className,
}) => {
  const styles = useStyles();
  const { sets, latest, ensure, retry, reload } = useWorklistData(service);

  // Component state only: nothing here is stored, so a reload opens with every filter off.
  const [filters, setFilters] = React.useState<Record<WorklistLane, LaneFilterKey>>({ Decide: 'all', Do: 'all' });
  const [notice, setNotice] = React.useState<Notice | null>(null);
  const [picker, setPicker] = React.useState<PickerState | null>(null);
  // The in-app Invoice Review modal (task 195): its browse set, taken when it opens and never changed while it is open, so a lane
  // reload under it cannot reshuffle "N of M".
  // `theme` is what THIS frame (the Console) resolved when it opened, handed to the page so a dark Console never gets a light page (the
  // page is two frames below the model-driven app, out of reach of the navbar check that only walks one parent frame).
  // It stays mounted after it closes (#2658: a Fluent Dialog unmounted or re-keyed while open leaves the page aria-hidden until
  // reload). It closes with `open: false`; `seq` keys each opening and changes only when opening from closed, so every opening mounts
  // a fresh modal on its own start index.
  const [reviewModal, setReviewModal] = React.useState<{
    browse: InvoiceBrowseSet;
    theme: 'dark' | 'light';
    open: boolean;
    seq: number;
  } | null>(null);
  // Every open takes the next sequence number; a lookup that returns after a later open is stale and is dropped, so a slow lookup can
  // never put a picker on top of the wizard (or the picker of another To Do) the person opened since. `pendingTodoId` is the To Do whose
  // lookup is the latest in flight (a second click on it is ignored). An object, not a `let`: set across an await, which control-flow
  // narrowing cannot follow. `mounted` stops state being set after the widget has gone.
  const opens = React.useRef({ seq: 0, pendingTodoId: null as string | null, mounted: true });
  React.useEffect(() => {
    const state = opens.current;
    state.mounted = true;
    return () => {
      state.mounted = false;
    };
  }, []);
  // To Dos whose outcome was recorded in this widget instance, with the notice each one was told. The server completes the To Do and
  // queues a re-evaluation that closes its Signal within seconds (#1991, DI-101); the reload right after the record can beat that job
  // (and when the queue refused it, the row stays until the nightly check), so opening it again would show a picker or wizard for finished work.
  const recordedTodos = React.useRef(new Map<string, Notice>());

  // "Today" moves forward each time the lanes are read again.
  const lanesData = sets.lanes.data;
  // `lanesData` is the trigger on purpose: a fresh read is when "now" is taken again.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const today = React.useMemo(() => todayProp ?? new Date(), [todayProp, lanesData]);

  const views = {
    Decide: buildLaneView(lanesData?.Decide ?? [], 'Decide', filters.Decide),
    Do: buildLaneView(lanesData?.Do ?? [], 'Do', filters.Do),
  };

  const anyFilter = LANES.some(l => views[l].filterKey !== 'all');
  // The Assistant's own narrative (task 062): counts of the list as read, never an item. Asked for only when the host gave none and
  // no filter is on (W-10: hidden under any filter), so a filter does not call the model. A hook, so it runs before the early returns.
  const assistantNarrative = useWorklistNarrative(service, lanesData, today, narrative === undefined && !anyFilter);

  // A filter whose cards emptied (the list changed under it) falls back to All, so it cannot come back unannounced.
  React.useEffect(() => {
    setFilters(prev => {
      const next = { ...prev };
      let changed = false;
      for (const lane of LANES) {
        if (views[lane].filterKey !== prev[lane]) {
          next[lane] = views[lane].filterKey;
          changed = true;
        }
      }
      return changed ? next : prev;
    });
  }, [views.Decide.filterKey, views.Do.filterKey]); // eslint-disable-line react-hooks/exhaustive-deps

  // One Signal in one set, with its group; every read of an item (the loader, the picker check) goes through here.
  const findItem = React.useCallback(
    (source: WorklistSetName, signalId: string): { item: WorklistRouteItem; group: WorklistGroupData } | null => {
      const data = latest.current[source].data;
      if (data) {
        for (const lane of LANES) {
          for (const group of data[lane]) {
            const item = group.items.find(i => i.signalId === signalId);
            if (item) return { item, group };
          }
        }
      }
      return null;
    },
    [latest]
  );

  // The loader is tied to the set the user launched from: a line in Closed today must open that set's (resolved, read-only) copy
  // even when a stale copy of the same Signal is still in the lanes (a refresh after a decision can leave one behind). The wizard
  // only asks for ids of the list it was given, so reading the source set alone is enough.
  const loaderFor = React.useCallback(
    (source: WorklistSetName): DecisionItemLoader =>
      async signalId => {
        const found = findItem(source, signalId);
        if (found) return buildWizardItem({ item: found.item, group: found.group, set: source, today: new Date() });
        throw new Error('This item is no longer on the list. Close the wizard and refresh the worklist.');
      },
    [findItem]
  );

  const launchFor = React.useCallback(
    async (event: OpenItemEvent, sourceSet: WorklistSetName) => {
      // An object, not a `let`: the flag is set inside a callback, which control-flow narrowing cannot see.
      const outcome = { recorded: false };
      try {
        const result = await launchWizard({
          ...event,
          loadItem: loaderFor(sourceSet),
          lookup,
          openLookup,
          openRecord,
          onRecorded: () => {
            outcome.recorded = true;
          },
        });
        if (!result.opened && result.reason === 'no-host') {
          if (opens.current.mounted) {
            setNotice({ text: 'The decision wizard is not available in this view.', intent: 'warning' });
          }
        }
      } catch {
        if (opens.current.mounted) setNotice({ text: 'The decision wizard could not be opened.', intent: 'warning' });
      }
      // What was recorded has left the lanes: read them again once the wizard is closed.
      if (outcome.recorded && opens.current.mounted) reload();
    },
    [launchWizard, loaderFor, lookup, openLookup, openRecord, reload]
  );

  // A billing item (D-207) is decided only in Invoice Review: open it as a dialog on the item's invoice and read the lanes again when
  // it closes. The promise settling is the only signal (no postMessage); a rejection reads again too, since the dialog may have done
  // work before it failed. Never reaches the wizard.
  const openReview = React.useCallback(
    async (review: InvoiceReviewTarget, browse?: InvoiceBrowseSet) => {
      const state = opens.current;
      state.seq += 1; // supersedes any inquiry lookup still in flight
      state.pendingTodoId = null;
      setNotice(null);
      // Task 195: with the org client URL the page opens in-app on the shared modal shell, with browse over `browse` (the billing
      // items of the list it was activated from). Without it, the navigateTo path below runs as before.
      if (invoiceReviewClientUrl) {
        // The shared resolver, run in this (the Console's) frame: stored preference, URL flag, then the navbar of the frame above.
        // An opening while the modal is already open keeps it as it is: re-keying an open modal is the #2658 leak, and the modal
        // covers the lanes, so this is not reachable from them.
        setReviewModal(prev =>
          prev?.open
            ? prev
            : {
                browse: browse ?? buildInvoiceBrowseSet([review], review),
                theme: getEffectiveDarkMode() ? 'dark' : 'light',
                open: true,
                seq: (prev?.seq ?? 0) + 1,
              }
        );
        return;
      }
      if (!openInvoiceReview) {
        setNotice({ text: INVOICE_REVIEW_FAILED, intent: 'warning' });
        return;
      }
      try {
        await openInvoiceReview({ invoiceId: review.invoiceId, lineId: review.lineId ?? null });
      } catch {
        if (state.mounted) setNotice({ text: INVOICE_REVIEW_FAILED, intent: 'warning' });
      }
      if (state.mounted) reload();
    },
    [openInvoiceReview, invoiceReviewClientUrl, reload]
  );

  // The in-app Invoice Review modal closed: read the lanes again, exactly as when the navigateTo promise settles.
  const closeReviewModal = React.useCallback(() => {
    setReviewModal(prev => (prev ? { ...prev, open: false } : prev));
    if (opens.current.mounted) reload();
  }, [reload]);

  // A Do row whose subject is a To Do may be an inquiry-outcome To Do (D-111): ask the BFF first. Only the lanes can hold one (a closed
  // copy has nothing left to record). 404 / not an inquiry opens the wizard as for any row; a failed lookup opens nothing, because
  // the To Do might be an inquiry's and the wizard cannot complete one (the server refuses mark-complete while the inquiry is open).
  const openWizard = React.useCallback(
    async (rawEvent: OpenItemEvent, sourceSet: WorklistSetName) => {
      const state = opens.current;
      const opened = findItem(sourceSet, rawEvent.itemId);
      if (opened?.item.invoiceReview && hasInvoiceReview(opened.item)) {
        // The browse set (task 195): the billing items of the list this was activated from, in the order shown.
        const browse = buildInvoiceBrowseSet(
          rawEvent.visibleList.map(id => {
            const other = findItem(sourceSet, id);
            return other && hasInvoiceReview(other.item) ? other.item.invoiceReview : null;
          }),
          opened.item.invoiceReview
        );
        await openReview(opened.item.invoiceReview, browse);
        return;
      }
      // The wizard never browses to a billing item (D-207): its set is the visible list without them.
      const event: OpenItemEvent = {
        ...rawEvent,
        visibleList: rawEvent.visibleList.filter(id => {
          const other = findItem(sourceSet, id);
          return !(other && hasInvoiceReview(other.item));
        }),
      };
      const found = sourceSet === 'lanes' ? findItem('lanes', event.itemId) : null;
      const todoId = found ? inquiryTodoIdOf(found.item) : null;
      // A second click on the To Do whose lookup is running changes nothing. A click on any other row is a new open (below).
      if (todoId && state.pendingTodoId === todoId) return;

      // This open supersedes any lookup still in flight: that lookup's answer will find a newer number and be dropped.
      state.seq += 1;
      const seq = state.seq;
      state.pendingTodoId = null;
      setNotice(null);

      if (found && todoId) {
        const recorded = recordedTodos.current.get(todoId);
        if (recorded) {
          setNotice(recorded);
          return;
        }
        state.pendingTodoId = todoId;
        let inquiry: InquiryForTodo | null;
        try {
          inquiry = await service.findInquiryForTodo(todoId);
        } catch {
          if (state.seq === seq && state.mounted) setNotice({ text: INQUIRY_LOOKUP_FAILED, intent: 'warning' });
          return;
        } finally {
          if (state.seq === seq) state.pendingTodoId = null;
        }
        // Superseded by a later open (or the widget is gone): no picker, no wizard.
        if (state.seq !== seq || !state.mounted) return;
        if (inquiry) {
          setPicker({ event, todoId, inquiry, subjectName: found.item.subjectName ?? null });
          return;
        }
      }
      await launchFor(event, sourceSet);
    },
    [findItem, launchFor, openReview, service]
  );

  const startLane = (lane: WorklistLane) => {
    const view = views[lane];
    if (view.groups.length === 0) return;
    // Start on the first item the wizard can decide: a billing item opens Invoice Review instead (D-207), so it is only the start
    // when nothing else is visible (for example, under the Invoice decisions card).
    const first = view.groups
      .flatMap(g => g.items.map(item => ({ group: g.group, item })))
      .find(e => !hasInvoiceReview(e.item)) ?? { group: view.groups[0].group, item: view.groups[0].items[0] };
    void openWizard(
      {
        itemId: first.item.signalId,
        core: coreRef(first.group.core),
        visibleList: [...view.visibleIds],
      },
      'lanes'
    );
  };

  const doneToday = (lane: WorklistLane): number | undefined => {
    const data = sets.closedToday.data;
    if (!data) return undefined;
    return data[lane].reduce((n, g) => n + g.items.length, 0);
  };

  if (!lanesData) {
    if (sets.lanes.status === 'error') {
      // A 403 is the caller's access, not a fault: another try gives the same answer, so there is no button and no route text.
      const forbidden = sets.lanes.httpStatus === 403;
      return (
        <div className={className}>
          <MessageBar intent="error" data-testid="worklist-error">
            <MessageBarBody>
              {forbidden
                ? "Your worklist isn't available for your account. Contact your administrator."
                : (sets.lanes.error ?? 'The worklist could not be read.')}
            </MessageBarBody>
            {!forbidden && (
              <MessageBarActions>
                <Button size="small" onClick={() => retry('lanes')}>
                  Try again
                </Button>
              </MessageBarActions>
            )}
          </MessageBar>
        </div>
      );
    }
    return (
      <div className={styles.center} data-testid="worklist-loading">
        <Spinner label="Loading the worklist" />
      </div>
    );
  }

  const shownNarrative =
    narrative ??
    (assistantNarrative && (
      <>
        <Body1 data-testid="worklist-narrative-summary">{assistantNarrative.summary}</Body1>
        {assistantNarrative.keyTakeaways.length > 0 && (
          <ul className={styles.narrativeList}>
            {assistantNarrative.keyTakeaways.map(t => (
              <li key={t}>
                <Body1>{t}</Body1>
              </li>
            ))}
          </ul>
        )}
      </>
    ));

  return (
    <div className={mergeClasses(styles.root, className)} data-testid="worklist-widget">
      {sets.lanes.status === 'error' && (
        <MessageBar intent="warning">
          <MessageBarBody>The worklist could not be refreshed. This is the last list that was read.</MessageBarBody>
          <MessageBarActions>
            <Button size="small" onClick={() => retry('lanes')}>
              Try again
            </Button>
          </MessageBarActions>
        </MessageBar>
      )}
      {notice && (
        <MessageBar intent={notice.intent} data-testid="worklist-notice">
          <MessageBarBody>{notice.text}</MessageBarBody>
        </MessageBar>
      )}

      {shownNarrative && !anyFilter && (
        <section className={styles.narrative} aria-label="Briefing" data-testid="worklist-narrative">
          {shownNarrative}
          <span className={styles.narrativeTag}>
            <SparkleRegular aria-hidden />
            <Caption1>Written by the Assistant from the lists below. It doesn&apos;t choose or order them.</Caption1>
          </span>
        </section>
      )}

      {LANES.map(lane => (
        <WorklistLanePanel
          key={lane}
          lane={lane}
          view={views[lane]}
          doneToday={doneToday(lane)}
          today={today}
          onSelectKey={key => setFilters(prev => ({ ...prev, [lane]: key }))}
          onOpenItem={event => void openWizard(event, 'lanes')}
          onStart={() => startLane(lane)}
          aggregateRow={lane === 'Do' ? aggregateRow : undefined}
        />
      ))}

      {picker && (
        <InquiryDispositionPicker
          inquiry={picker.inquiry}
          subjectName={picker.subjectName}
          service={service}
          onRecorded={result => {
            // The notice says only what the server did: "leaves shortly" when the re-evaluation was queued, else the nightly wait.
            const told: Notice = !result.todoCompleted
              ? { text: OUTCOME_RECORDED_TODO_OPEN, intent: 'warning' }
              : result.signalRefreshQueued
                ? { text: OUTCOME_RECORDED_TODO_DONE, intent: 'info' }
                : { text: OUTCOME_RECORDED_TODO_DONE_NIGHTLY, intent: 'info' };
            recordedTodos.current.set(picker.todoId, told);
            setPicker(null);
            setNotice(told);
            reload();
          }}
          onAlreadyResolved={() => {
            setPicker(null);
            reload();
          }}
          onCancel={() => setPicker(null)}
          onOpenWizard={() => {
            const { event } = picker;
            setPicker(null);
            void launchFor(event, 'lanes');
          }}
        />
      )}

      {reviewModal && invoiceReviewClientUrl && (
        <InvoiceReviewModal
          key={reviewModal.seq}
          open={reviewModal.open}
          onClose={closeReviewModal}
          clientUrl={invoiceReviewClientUrl}
          targets={reviewModal.browse.targets}
          startIndex={reviewModal.browse.startIndex}
          theme={reviewModal.theme}
        />
      )}

      <WorklistDisclosures
        sets={{
          closedToday: sets.closedToday,
          suppressed: sets.suppressed,
          closedEarlier: sets.closedEarlier,
        }}
        today={today}
        onOpenSet={(set: DisclosureSetName) => ensure(set)}
        onRetry={(set: DisclosureSetName) => retry(set)}
        onOpenItem={(event, set) => void openWizard(event, set)}
      />
    </div>
  );
};

WorklistWidget.displayName = 'WorklistWidget';
