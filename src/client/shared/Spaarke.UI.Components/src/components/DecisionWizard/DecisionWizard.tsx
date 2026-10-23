/**
 * DecisionWizard - the decision wizard: one WizardShell consumer for every Decide and Do decision, made and replayed in one
 * place (spaarke-ontology-platform-r1 task 058; D-17, D-23, D-26).
 *
 * This component is the BROWSE and RECORD layer around `DecisionItemWizard`:
 *  - **Browse** walks the list the wizard was opened from ("N of M" in the modal header), no wrap. Each item keeps its own
 *    draft while the wizard is open, so what was typed on one item is still there when the user comes back.
 *  - **Nothing is written by the wizard itself.** Record decision posts ONE review, with a client-generated review id, to the
 *    commit route (task 043) through `service.commit`, once: the request is single-flight, so a double click sends one.
 *  - **Closing** with unrecorded choices on ANY item opens a nested "Discard and close / Keep deciding" check. Escape and the
 *    backdrop never close (the shell's `dismiss: 'explicit'`).
 *  - **Recording flips the item to read-only in place**: the status bar takes focus, "Next open item" moves on.
 *
 * Item data (sentence, evidence, other open items, the record of a closed item) comes from the host's `loadItem`: the Signal
 * read route is task 038 and the record read is task 045, so neither exists to call yet. Steps come from the plan route
 * (task 036); the dismissal count from task 034.
 *
 * Context-agnostic (ADR-012): fetch and lookups are injected. Fluent v9 tokens only (ADR-021). React 16/17-safe.
 */

import * as React from 'react';
import { Button, MessageBar, MessageBarBody, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import { ConfirmModal } from '../SprkModal/presets/ConfirmModal';
import { WizardShell } from '../Wizard/WizardShell';
import type { SprkModalNav } from '../SprkModal/SprkModal.types';
import { DecisionItemWizard, hasRecordedDecision } from './DecisionItemWizard';
import { buildReview, emptyDraft, hasUnrecordedChoices, newReviewId } from './decisionDraft';
import type { ItemDraft } from './decisionDraft';
import type { DecisionLookup, DecisionOpenLookupForItem } from './decisionItemContext';
import {
  closesSignal,
  failureKeepsReview,
  isFailedOutcome,
  resolutionFromRecord,
  reviewKey,
} from './decisionRecording';
import { withTemplatedDefaults } from './templatedDrafts';
import { DecisionCommitError } from './decisionWizardTypes';
import type {
  DecisionCommitFailure,
  DecisionCommitResult,
  DecisionItemLoader,
  DecisionLookupSearch,
  DecisionOpenLookup,
  DecisionOpenRecord,
  DecisionPlan,
  DecisionResolution,
  DecisionWizardItem,
  DecisionWizardService,
  SuppressionStatus,
} from './decisionWizardTypes';

export interface DecisionWizardProps {
  /** The Signal ids the wizard was opened from, in the order shown (the worklist's visible list). */
  itemIds: readonly string[];
  /** The item to open on. */
  startItemId: string;
  service: DecisionWizardService;
  /** The host's reader for one item (the Signal read route, task 038, and the record read, task 045). */
  loadItem: DecisionItemLoader;
  /** Searches for Lookup / Person fields. Absent: those fields say they cannot be searched. */
  lookup?: DecisionLookupSearch;
  /**
   * The host's side-pane lookup (`Xrm.Utility.lookupObjects` through `INavigationService.openLookup`, D-182). Present: every
   * Person / Lookup field opens it (or, when it lists `supportedEntities`, every field on a listed table; the rest keep the
   * inline type-ahead). Absent: those fields keep the inline type-ahead over `lookup`.
   */
  openLookup?: DecisionOpenLookup;
  /** The host's open-record-in-a-modal (`INavigationService.openRecordModal`, D-182). Absent: the subject is plain text. */
  openRecord?: DecisionOpenRecord;
  /** Close the wizard. Called only after the discard check (or when nothing is unrecorded). */
  onClose: () => void;
  /** Raised after an item was recorded, so the host can refresh its worklist. */
  onRecorded?: (event: { signalId: string; alsoResolved: readonly string[]; result: DecisionCommitResult }) => void;
  /** The `--sprk-ui-scale` factor the host passes to `scaleTheme`. */
  uiScale?: number;
  /** "Now" for ages and due states; tests inject a fixed date. */
  today?: Date;
}

type Loaded =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | {
      status: 'ready';
      item: DecisionWizardItem;
      plan: DecisionPlan;
      suppression: SuppressionStatus | null;
      suppressionFailed: boolean;
    };

interface Recorded {
  result: DecisionCommitResult;
  resolution: DecisionResolution;
}

const useStyles = makeStyles({
  center: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    rowGap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalXXL,
  },
});

/** The plan of an item that closed without a person deciding it (superseded, cleared): there is nothing to replay. */
function emptyPlan(item: DecisionWizardItem): DecisionPlan {
  return {
    signalId: item.row.signalId,
    lane: item.row.lane,
    policyVersionId: '',
    actions: [],
    nextSteps: [],
    dismissalReasons: [],
  };
}

const errorMessage = (err: unknown, fallback: string): string =>
  err instanceof Error && err.message ? err.message : fallback;

export const DecisionWizard: React.FC<DecisionWizardProps> = ({
  itemIds,
  startItemId,
  service,
  loadItem,
  lookup,
  openLookup,
  openRecord,
  onClose,
  onRecorded,
  uiScale,
  today: todayProp,
}) => {
  const styles = useStyles();
  const today = React.useMemo(() => todayProp ?? new Date(), [todayProp]);

  const [index, setIndex] = React.useState(() => Math.max(0, itemIds.indexOf(startItemId)));
  const [loaded, setLoaded] = React.useState<Partial<Record<string, Loaded>>>({});
  const [drafts, setDrafts] = React.useState<Partial<Record<string, ItemDraft>>>({});
  const [recorded, setRecorded] = React.useState<Partial<Record<string, Recorded>>>({});
  const [failures, setFailures] = React.useState<Partial<Record<string, DecisionCommitFailure>>>({});
  const [pinned, setPinned] = React.useState<Partial<Record<string, DecisionCommitFailure>>>({});
  const [recording, setRecording] = React.useState<string | null>(null);
  const [alsoClosed, setAlsoClosed] = React.useState<readonly string[]>([]);
  const [confirmClose, setConfirmClose] = React.useState(false);

  const currentId = itemIds[index] ?? startItemId;

  // Everything an async path needs, read at call time.
  const live = React.useRef({ service, loadItem, loaded, drafts, failures, recorded, onRecorded });
  live.current = { service, loadItem, loaded, drafts, failures, recorded, onRecorded };
  const recordingRef = React.useRef<string | null>(null);
  // Per item: the review a failed attempt posted. Once a failure keeps the review id it is PINNED: the id never changes again,
  // the review cannot be edited, and `pinning` is the Failed outcome whose `resumable` decides `retry`.
  const attempts = React.useRef<
    Partial<
      Record<string, { key: string; pinned: boolean; pinning: DecisionCommitFailure | null; sent: ItemDraft | null }>
    >
  >({});
  const closedNow = React.useRef<(id: string) => boolean>(() => false);
  const mounted = React.useRef(true);
  React.useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  // ---- Loading one item ---------------------------------------------------------------------------
  const loadOne = React.useCallback(async (id: string) => {
    setLoaded(prev => ({ ...prev, [id]: { status: 'loading' } }));
    try {
      const item = await live.current.loadItem(id);
      const decided = hasRecordedDecision(item.resolution);
      const open = !item.resolution;
      const needPlan = open || decided;
      const [plan, suppression] = await Promise.all([
        needPlan
          ? live.current.service.getPlan(id).catch(err => {
              // A decided item replays from its record; without the plan it still shows the record, the status bar and the
              // fact snapshot, just no per-action steps. An open item needs the plan to be decided at all.
              if (open) throw err;
              return emptyPlan(item);
            })
          : Promise.resolve(emptyPlan(item)),
        open
          ? live.current.service.getSuppression(id).then(
              s => ({ s, failed: false }),
              () => ({ s: null, failed: true })
            )
          : Promise.resolve({ s: null, failed: false }),
      ]);
      if (!mounted.current) return;
      setLoaded(prev => ({
        ...prev,
        [id]: { status: 'ready', item, plan, suppression: suppression.s, suppressionFailed: suppression.failed },
      }));
      if (open) {
        setDrafts(prev =>
          prev[id] ? prev : { ...prev, [id]: withTemplatedDefaults(emptyDraft(newReviewId()), plan, item) }
        );
      }
    } catch (err) {
      if (!mounted.current) return;
      setLoaded(prev => ({
        ...prev,
        [id]: { status: 'error', message: errorMessage(err, 'This item could not be loaded.') },
      }));
    }
  }, []);

  React.useEffect(() => {
    if (!live.current.loaded[currentId]) void loadOne(currentId);
  }, [currentId, loadOne]);

  // ---- Drafts ------------------------------------------------------------------------------------------
  const updateDraft = React.useCallback((id: string, change: (d: ItemDraft) => ItemDraft) => {
    // A pinned review is read-only: any change would be a different decision under an id the route has already seen.
    if (attempts.current[id]?.pinned) return;
    setDrafts(prev => (prev[id] ? { ...prev, [id]: change(prev[id]) } : prev));
  }, []);

  // ---- Record: the ONE write, single-flight --------------------------------------------------------------
  const record = React.useCallback(
    async (id: string) => {
      if (recordingRef.current) return; // a second click while a request is out sends nothing
      const l = live.current.loaded[id];
      const draft = live.current.drafts[id];
      if (!l || l.status !== 'ready' || !draft || live.current.recorded[id]) return;
      recordingRef.current = id;
      setRecording(id);

      const previous = attempts.current[id];
      const isPinned = previous?.pinned === true;
      // `retry` resumes the Failed first attempt; it comes from the failure that pinned the review, not from the latest answer
      // (a refusal after it says nothing about whether the first attempt can be resumed).
      const retry = isPinned && previous.pinning?.resumable === true;
      // A pinned review is posted exactly as first sent (items that closed since were already dropped from it).
      let sendDraft = isPinned && previous.sent ? previous.sent : draft;
      if (!isPinned) {
        // Items that closed since they were ticked are not asked for again.
        const stillOpen = draft.alsoResolve.filter(x => !closedNow.current(x));
        if (stillOpen.length !== draft.alsoResolve.length) sendDraft = { ...draft, alsoResolve: stillOpen };
      }
      let review = buildReview(l.plan, sendDraft, { retry });
      // An edit after a plain refusal (nothing stored) is a different decision and gets its own review id. A pinned review
      // is never edited and never changes id: a new id would run the steps that already landed again.
      if (previous && !isPinned && previous.key !== reviewKey(review)) {
        sendDraft = { ...sendDraft, reviewId: newReviewId() };
        review = buildReview(l.plan, sendDraft, { retry: false });
        updateDraft(id, d => ({ ...d, reviewId: sendDraft.reviewId }));
      }

      try {
        const result = await live.current.service.commit(id, review);
        if (!mounted.current) return;
        const resolution = resolutionFromRecord(l.plan, l.item, sendDraft, review, result, new Date());
        delete attempts.current[id];
        setRecorded(prev => ({ ...prev, [id]: { result, resolution } }));
        setPinned(prev => {
          if (!(id in prev)) return prev;
          const next = { ...prev };
          delete next[id];
          return next;
        });
        setFailures(prev => {
          if (!(id in prev)) return prev;
          const next = { ...prev };
          delete next[id];
          return next;
        });
        // Only what the route says it closed (Acted or Dismissed) counts as closed here; an item it left open stays open.
        const also = (review.alsoResolve ?? []).filter(x => closesSignal(result, x));
        if (also.length > 0) {
          setAlsoClosed(prev => [...prev, ...also.filter(x => !prev.includes(x))]);
          // Their cached copies are open; a later visit must read them again.
          setLoaded(prev => {
            const next = { ...prev };
            for (const x of also) delete next[x];
            return next;
          });
        }
        live.current.onRecorded?.({ signalId: id, alsoResolved: also, result });
      } catch (err) {
        if (!mounted.current) return;
        const failure =
          err instanceof DecisionCommitError
            ? err.failure
            : ({
                httpStatus: 0,
                title: 'The decision may not have been recorded',
                detail: errorMessage(err, 'The request could not be completed.'),
                landed: [],
                possiblyLanded: [],
                steps: [],
                recordMayExist: false,
                sentButNotRecorded: false,
                resumable: false,
                replayed: false,
              } as DecisionCommitFailure);
        const keeps = failureKeepsReview(failure);
        const prior = attempts.current[id];
        const wasPinned = prior?.pinned === true;
        // The first failure that keeps the id pins the review. After that only a new Failed outcome replaces the failure that
        // decides `retry`; a refusal or an in-progress answer leaves it alone.
        const pinning = keeps && (!wasPinned || isFailedOutcome(failure)) ? failure : (prior?.pinning ?? null);
        const isNowPinned = keeps || wasPinned;
        attempts.current[id] = {
          key: wasPinned ? prior.key : reviewKey(review),
          pinned: isNowPinned,
          pinning,
          sent: wasPinned ? (prior.sent ?? sendDraft) : sendDraft,
        };
        setFailures(prev => ({ ...prev, [id]: failure }));
        setPinned(prev => {
          const shown = attempts.current[id]?.pinning;
          if (!isNowPinned || !shown) return prev;
          return prev[id] === shown ? prev : { ...prev, [id]: shown };
        });
      } finally {
        recordingRef.current = null;
        if (mounted.current) setRecording(null);
      }
    },
    [updateDraft]
  );

  // ---- What is closed, what is open --------------------------------------------------------------------
  const isClosed = React.useCallback(
    (id: string): boolean => {
      const r = recorded[id];
      // Recorded, but the route left the item open (an inquiry was sent: Acknowledged, linked to the record).
      if (r) return !r.resolution.leftOpen;
      if (alsoClosed.includes(id)) return true;
      const l = loaded[id];
      return l?.status === 'ready' && l.item.resolution != null;
    },
    [recorded, alsoClosed, loaded]
  );
  closedNow.current = isClosed;
  const closedIds = React.useMemo(() => new Set(itemIds.filter(isClosed)), [itemIds, isClosed]);

  const openItemsLeft = itemIds.filter(id => id !== currentId && !isClosed(id)).length;
  const nextOpenIndex = (() => {
    for (let i = index + 1; i < itemIds.length; i++) if (!isClosed(itemIds[i])) return i;
    for (let i = 0; i < index; i++) if (!isClosed(itemIds[i])) return i;
    return -1;
  })();

  // ---- Close with the discard check ------------------------------------------------------------------
  const hasUnrecorded = Object.entries(drafts).some(
    ([id, d]) => d !== undefined && !recorded[id] && !alsoClosed.includes(id) && hasUnrecordedChoices(d)
  );
  const requestClose = React.useCallback(() => {
    if (recordingRef.current) return;
    if (hasUnrecorded) setConfirmClose(true);
    else onClose();
  }, [hasUnrecorded, onClose]);

  // ---- Browse -----------------------------------------------------------------------------------------------
  const nav: SprkModalNav = {
    index,
    total: itemIds.length,
    onNavigate: dir => setIndex(i => Math.min(itemIds.length - 1, Math.max(0, i + (dir === 'next' ? 1 : -1)))),
    // Browsing keeps every draft; it only waits for a request in flight.
    onBeforeNavigate: () => recordingRef.current === null,
  };

  const state = loaded[currentId];
  // Stable per item and host search: a lookup field re-runs its search whenever this function changes identity.
  const readyItem = state?.status === 'ready' ? state.item : null;
  const itemLookup = React.useMemo<DecisionLookup | null>(
    () =>
      lookup && readyItem
        ? req =>
            lookup({
              ...req,
              core: readyItem.core
                ? { recordType: readyItem.core.recordType, recordId: readyItem.core.recordId }
                : null,
            })
        : null,
    [lookup, readyItem]
  );
  const itemOpenLookup = React.useMemo<DecisionOpenLookupForItem | null>(
    () =>
      openLookup && readyItem
        ? Object.assign(
            (req: Parameters<DecisionOpenLookupForItem>[0]) =>
              openLookup({
                ...req,
                core: readyItem.core
                  ? { recordType: readyItem.core.recordType, recordId: readyItem.core.recordId }
                  : null,
              }),
            { supportedEntities: openLookup.supportedEntities }
          )
        : null,
    [openLookup, readyItem]
  );
  const draft = drafts[currentId];
  const done = recorded[currentId];

  let body: React.ReactNode;
  if (state?.status === 'ready' && (draft || state.item.resolution)) {
    const resolution = done?.resolution ?? state.item.resolution ?? null;
    body = (
      <DecisionItemWizard
        // Recording remounts the shell so it opens, read-only, on its last step (the Confirm step): the steps it listed while
        // being decided can differ from the steps a closed item lists.
        key={done ? `${currentId}:recorded` : currentId}
        item={state.item}
        plan={state.plan}
        suppression={state.suppression}
        suppressionFailed={state.suppressionFailed}
        draft={draft ?? emptyDraft(currentId)}
        onDraftChange={change => updateDraft(currentId, change)}
        resolution={resolution}
        justRecorded={done != null}
        failure={failures[currentId] ?? null}
        pinnedFailure={pinned[currentId] ?? null}
        closedIds={closedIds}
        recordResult={done?.result ?? null}
        recording={recording === currentId}
        onRecord={() => record(currentId)}
        lookup={itemLookup}
        openLookup={itemOpenLookup}
        openRecord={openRecord ?? null}
        openItemsLeft={openItemsLeft}
        onNextOpenItem={nextOpenIndex >= 0 ? () => setIndex(nextOpenIndex) : undefined}
        nav={nav}
        onClose={requestClose}
        uiScale={uiScale}
        today={today}
      />
    );
  } else {
    const failed = state?.status === 'error';
    body = (
      <WizardShell
        key={`${currentId}:${failed ? 'error' : 'loading'}`}
        open
        title="Decision"
        steps={[
          {
            id: 'loading',
            label: failed ? 'Unavailable' : 'Loading',
            canAdvance: () => false,
            renderContent: () =>
              failed ? (
                <div className={styles.center} data-testid="item-error">
                  <MessageBar intent="error">
                    <MessageBarBody>{state.message}</MessageBarBody>
                  </MessageBar>
                  <Button appearance="primary" onClick={() => void loadOne(currentId)}>
                    Try again
                  </Button>
                </div>
              ) : (
                <div className={styles.center} data-testid="item-loading">
                  <Spinner size="small" />
                  <Text>Loading the decision...</Text>
                </div>
              ),
          },
        ]}
        onClose={requestClose}
        onFinish={async () => undefined}
        stayOpenOnFinish
        dismiss="explicit"
        uiScale={uiScale}
        nav={nav}
      />
    );
  }

  return (
    <>
      {body}
      {/* Mounted only while asked: a closed Fluent dialog left mounted marks the open wizard surface aria-hidden. */}
      {confirmClose && (
        <ConfirmModal
          open={confirmClose}
          onClose={() => setConfirmClose(false)}
          onConfirm={() => {
            setConfirmClose(false);
            onClose();
          }}
          title="Discard unrecorded choices?"
          message="Some choices have not been recorded. Closing now discards them, and nothing is written."
          confirmLabel="Discard and close"
          cancelLabel="Keep deciding"
          destructive
          uiScale={uiScale}
        />
      )}
    </>
  );
};

DecisionWizard.displayName = 'DecisionWizard';
